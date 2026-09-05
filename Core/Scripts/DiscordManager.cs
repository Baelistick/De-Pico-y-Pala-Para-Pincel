/*
 * BAELISTICK LABS | MENTE-0 ARCHITECTURE
 * Project: De Pico y Pala Para Pincel
 * Module: DiscordManager
 * Description: Singleton node handling Discord Rich Presence (RPC) integration.
 * Dependencies: Requires 'DiscordRichPresence' NuGet package. 
*/

using Godot;
using DiscordRPC;
using DiscordRPC.Message;

public partial class DiscordManager : Node
{
    private DiscordRpcClient _client;
    
    // Global access point
    public static DiscordManager Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;

        // Execution Firewall: Restrict RPC initialization to desktop environments to prevent mobile crashes
        string osName = OS.GetName();
        if (osName == "Windows" || osName == "macOS" || osName == "Linux")
        {
            InitializeDiscord();
        }
    }

    private void InitializeDiscord()
    {
        // Initializes the Discord RPC client using the Developer Application ID
        _client = new DiscordRpcClient("1532444206575124510");

        _client.OnReady += (sender, e) =>
        {
            GD.Print($"[DISCORD] Presence connected as {e.User.Username}");
        };

        _client.Initialize();
        
        // Default state upon application launch
        UpdatePresence("Ingresando al ecosistema", "Preparando herramientas...");
    }

    /// <summary>
    /// Updates the current Rich Presence data on the user's Discord profile.
    /// </summary>
    /// <param name="details">Primary state description (e.g., current action or location).</param>
    /// <param name="state">Secondary state data (e.g., numerical stats or energy).</param>
    public void UpdatePresence(string details, string state)
    {
        if (_client == null || !_client.IsInitialized) return;

        _client.SetPresence(new RichPresence()
        {
            Details = details,
            State = state,
            Assets = new Assets()
            {
                LargeImageKey = "222.png",
                LargeImageText = "De Pico y Pala Para Pincel"
            }
        });
    }

    public override void _Process(double delta)
    {
        // Network Heartbeat: Forces the library to flush the message queue per frame
        if (_client != null && _client.IsInitialized)
        {
            _client.Invoke(); 
        }
    }

    public override void _ExitTree()
    {
        // Strict memory management: Dispose of the client connection on application exit
        if (_client != null)
        {
            _client.Dispose();
        }
    }
}