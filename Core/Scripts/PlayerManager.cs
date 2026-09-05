/*
 * BAELISTICK LABS | MENTE-0 ARCHITECTURE
 * Project: De Pico y Pala Para Pincel
 * Module: PlayerManager
 * Description: Singleton (Autoload) managing player identity, local persistence, 
 *              referral tracking, and action cooldown state validation.
 * Coupling Level: Low. Interacts exclusively with local storage (session.cfg) 
 *                 and SupabaseManager for remote syncing.
*/

using Godot;
using System;
using System.Text;

public partial class PlayerManager : Node
{
    // Global access point
    public static PlayerManager Instance { get; private set; }

    // Core Identity & State Properties
    public string PlayerId { get; private set; }
    public string ReferralCode { get; private set; }
    public int BonusActions { get; private set; } = 0;
    public DateTime LastActionAt { get; private set; } = DateTime.MinValue;

    private const string SAVE_PATH = "user://session.cfg";

    public override void _Ready()
    {
        Instance = this;
        InitializeSession();
    }

    /// <summary>
    /// Initializes the player session. Loads existing credentials from local storage
    /// or generates a new anonymous profile if no save file exists.
    /// </summary>
    private void InitializeSession()
    {
        ConfigFile config = new ConfigFile();
        Error err = config.Load(SAVE_PATH);

        if (err == Error.Ok && config.HasSectionKey("Player", "Id"))
        {
            // Restore existing session
            PlayerId = (string)config.GetValue("Player", "Id");
            ReferralCode = (string)config.GetValue("Player", "ReferralCode");
            FetchPlayerData();
        }
        else
        {
            // Generate new anonymous credentials
            PlayerId = Guid.NewGuid().ToString();
            ReferralCode = GenerateRandomCode(6);

            // Persist locally
            config.SetValue("Player", "Id", PlayerId);
            config.SetValue("Player", "ReferralCode", ReferralCode);
            config.Save(SAVE_PATH);

            RegisterPlayerInDatabase();
        }
    }

    /// <summary>
    /// Generates an alphanumeric verification code of the specified length.
    /// </summary>
    private string GenerateRandomCode(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        char[] stringChars = new char[length];
        Random random = new Random();
        
        for (int i = 0; i < stringChars.Length; i++)
        {
            stringChars[i] = chars[random.Next(chars.Length)];
        }
        return new string(stringChars);
    }

    /// <summary>
    /// Registers a new player in the backend. Evaluates URL parameters for referral tracking.
    /// </summary>
    private void RegisterPlayerInDatabase()
    {
        string referredBy = GetReferralFromURL();

        string jsonBody = referredBy != null 
            ? $"{{\"id\": \"{PlayerId}\", \"referral_code\": \"{ReferralCode}\", \"referred_by\": \"{referredBy}\"}}"
            : $"{{\"id\": \"{PlayerId}\", \"referral_code\": \"{ReferralCode}\"}}";

        SupabaseManager.Instance.PostPlayer(jsonBody, (success) => 
        {
            if (success) GD.Print($"[AUTH] Player registered successfully: {PlayerId}");
        });
    }

    /// <summary>
    /// Synchronizes local state with backend database values (bonuses and cooldowns).
    /// </summary>
    private void FetchPlayerData()
    {
        SupabaseManager.Instance.GetPlayer(PlayerId, (data) => 
        {
            if (data != null)
            {
                BonusActions = (int)data["bonus_actions"];
                string lastActionStr = (string)data["last_action_at"];
                
                if (DateTime.TryParse(lastActionStr, out DateTime parsedDate))
                {
                    LastActionAt = parsedDate;
                }
            }
        });
    }

    /// <summary>
    /// Extracts referral codes from the URL query string if the application is running via WebGL.
    /// </summary>
    private string GetReferralFromURL()
    {
        if (OS.HasFeature("web"))
        {
            // Godot 4.x JS Bridge for DOM location access
            var window = JavaScriptBridge.GetInterface("window");
            var location = (GodotObject)window.Get("location");
            string search = (string)location.Get("search");

            if (!string.IsNullOrEmpty(search) && search.Contains("ref="))
            {
                int index = search.IndexOf("ref=") + 4;
                string code = search.Substring(index);
                return code.Split('&')[0]; // Sanitize and return exact code
            }
        }
        return null;
    }

    /// <summary>
    /// Validates if the player meets the requirements to execute a grid action.
    /// Prioritizes bonus points over time-based cooldowns.
    /// </summary>
    public bool CanPerformAction()
    {
        if (BonusActions > 0) return true; 
        
        TimeSpan timeSinceLastAction = DateTime.UtcNow - LastActionAt;
        return timeSinceLastAction.TotalMinutes >= 10;
    }

    /// <summary>
    /// Deducts action cost, updates local timestamps, and syncs the new state to Supabase.
    /// </summary>
    public void ConsumeAction()
    {
        if (BonusActions > 0)
        {
            BonusActions--;
        }
        
        LastActionAt = DateTime.UtcNow;
        
        // Format to ISO 8601 standard for strict PostgreSQL compatibility
        string timestamp = LastActionAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string jsonBody = $"{{\"last_action_at\": \"{timestamp}\", \"bonus_actions\": {BonusActions}}}";
        
        SupabaseManager.Instance.UpdatePlayerState(PlayerId, jsonBody);
    }
}