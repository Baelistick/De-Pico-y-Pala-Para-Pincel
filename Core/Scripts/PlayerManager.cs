using Godot;
using System;
using System.Text;

// Nivel de acoplamiento: Mínimo (Autoload). Gestiona la identidad del jugador y persistencia local.
public partial class PlayerManager : Node
{
    public static PlayerManager Instance { get; private set; }

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

    private void InitializeSession()
    {
        ConfigFile config = new ConfigFile();
        Error err = config.Load(SAVE_PATH);

        if (err == Error.Ok && config.HasSectionKey("Player", "Id"))
        {
            // Cargar sesión existente
            PlayerId = (string)config.GetValue("Player", "Id");
            ReferralCode = (string)config.GetValue("Player", "ReferralCode");
            FetchPlayerData();
        }
        else
        {
            // Nuevo jugador: Crear credencial anónima
            PlayerId = Guid.NewGuid().ToString();
            ReferralCode = GenerateRandomCode(6);

            // Guardar localmente
            config.SetValue("Player", "Id", PlayerId);
            config.SetValue("Player", "ReferralCode", ReferralCode);
            config.Save(SAVE_PATH);

            RegisterPlayerInDatabase();
        }
    }

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

    private void RegisterPlayerInDatabase()
    {
        // Verificar si el jugador llegó mediante un enlace de referidos en la URL (WebGL)
        string referredBy = GetReferralFromURL();

        string jsonBody = referredBy != null 
            ? $"{{\"id\": \"{PlayerId}\", \"referral_code\": \"{ReferralCode}\", \"referred_by\": \"{referredBy}\"}}"
            : $"{{\"id\": \"{PlayerId}\", \"referral_code\": \"{ReferralCode}\"}}";

        SupabaseManager.Instance.PostPlayer(jsonBody, (success) => 
        {
            if (success) GD.Print($"Jugador registrado exitosamente: {PlayerId}");
        });
    }

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

    private string GetReferralFromURL()
    {
        // Lógica para capturar el parámetro '?ref=XXXXXX' si el ejecutable es WebGL/HTML5
        if (OS.HasFeature("web"))
        {
            // En Godot 4.x WebGL se captura mediante JavaScriptBridge
            var window = JavaScriptBridge.GetInterface("window");
            var location = (GodotObject)window.Get("location");
            string search = (string)location.Get("search");

            if (!string.IsNullOrEmpty(search) && search.Contains("ref="))
            {
                int index = search.IndexOf("ref=") + 4;
                string code = search.Substring(index);
                return code.Split('&')[0]; // Extrae el código limpio
            }
        }
        return null;
    }

	// Evalúa la regla estricta de tiempo o bonificaciones
    public bool CanPerformAction()
    {
        if (BonusActions > 0) return true; // Prioriza gastar bonos
        
        TimeSpan timeSinceLastAction = DateTime.UtcNow - LastActionAt;
        return timeSinceLastAction.TotalMinutes >= 10;
    }

    // Ejecuta el cobro de la acción y actualiza la BD
    public void ConsumeAction()
    {
        if (BonusActions > 0)
        {
            BonusActions--;
        }
        
        LastActionAt = DateTime.UtcNow;
        
        // Formatea la fecha al estándar ISO 8601 que usa PostgreSQL/Supabase
        string timestamp = LastActionAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string jsonBody = $"{{\"last_action_at\": \"{timestamp}\", \"bonus_actions\": {BonusActions}}}";
        
        SupabaseManager.Instance.UpdatePlayerState(PlayerId, jsonBody);
    }
}