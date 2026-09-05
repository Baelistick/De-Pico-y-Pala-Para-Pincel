/*
 * BAELISTICK LABS | MENTE-0 ARCHITECTURE
 * Project: De Pico y Pala Para Pincel
 * Module: SupabaseManager
 * Description: Singleton node acting as the primary HTTP REST client for the Supabase backend.
 *              Handles authentication, Delta Sync, telemetry, network graphing, and RPC execution.
 * Coupling Level: Global. Utilized heavily by GridManager and PlayerManager.
*/

using Godot;
using System.Text;

public partial class SupabaseManager : Node
{
    // Backend Credentials
    private string _supabaseUrl = ""; 
    private string _supabaseKey = "";

    public static SupabaseManager Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        LoadSecrets();
    }

    private void LoadSecrets()
    {
        ConfigFile config = new ConfigFile();
        // Leemos el archivo que está ignorado por GitHub
        if (config.Load("res://secrets.cfg") == Error.Ok)
        {
            _supabaseUrl = (string)config.GetValue("Supabase", "URL", "");
            _supabaseKey = (string)config.GetValue("Supabase", "Key", "");
            GD.Print("[SISTEMA] Credenciales de Supabase cargadas de forma segura.");
        }
        else
        {
            GD.PrintErr("[CRÍTICO] Falta el archivo secrets.cfg. El ecosistema no podrá conectarse.");
        }
    }

    // =======================================================
    // AUTHENTICATION & REGISTRATION
    // =======================================================

    /// <summary>
    /// Registers a new user and links their profile to a recruiter if provided.
    /// </summary>
    public void RegisterNewUser(string nickname, string passwordHash, string country, string recruiter, System.Action<bool> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 201) 
            {
                GD.Print("[SUPABASE] User successfully registered and linked.");
                onCompleted?.Invoke(true); 
            }
            else
            {
                string errorResponse = (body != null && body.Length > 0) 
                    ? Encoding.UTF8.GetString(body) 
                    : "Network failure (0). Server unreachable.";
                
                GD.PrintErr($"[SUPABASE ERROR] Code: {responseCode} | Details: {errorResponse}");
                onCompleted?.Invoke(false); 
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/usuarios";
        string[] headers = new string[] {
            $"apikey: {_supabaseKey}", 
            $"Authorization: Bearer {_supabaseKey}",
            "Content-Type: application/json",
            "Prefer: return=minimal"
        };

        var userData = new Godot.Collections.Dictionary
        {
            { "nickname", nickname },
            { "password_hash", passwordHash },
            { "country", country },
            { "invited_by", string.IsNullOrEmpty(recruiter) ? "" : recruiter } 
        };

        string jsonBody = Json.Stringify(userData);
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    /// <summary>
    /// Validates user credentials against the database records.
    /// </summary>
    public void LoginUser(string nickname, string passwordHash, System.Action<bool> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200) 
            {
                Json json = new Json();
                if (json.Parse(Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    var array = json.Data.AsGodotArray();
                    
                    if (array.Count > 0)
                    {
                        GD.Print("[SUPABASE] Credentials verified. Access granted.");
                        onCompleted?.Invoke(true);
                    }
                    else
                    {
                        GD.PrintErr("[SUPABASE] Auth Error: Invalid nickname or password.");
                        onCompleted?.Invoke(false);
                    }
                }
            }
            else
            {
                GD.PrintErr($"[SUPABASE ERROR] Read Code: {responseCode}");
                onCompleted?.Invoke(false);
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/usuarios?nickname=eq.{nickname}&password_hash=eq.{passwordHash}&select=id";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    // =======================================================
    // ECOSYSTEM SYNC (DELTA SYNC)
    // =======================================================

    /// <summary>
    /// Upserts a pixel alteration to the remote database. Handles canvas overrides and structural damage.
    /// </summary>
    public void SavePixel(int x, int y, int tileType, string hexColor = null, string ownerNick = null)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);
        
        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode != 200 && responseCode != 201 && responseCode != 204)
                GD.PrintErr($"[SUPABASE ERROR] {responseCode} - {Encoding.UTF8.GetString(body)}");
            
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/pixels";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json",
            "Prefer: resolution=merge-duplicates"
        };

        string colorJson = hexColor != null ? $"\"{hexColor}\"" : "null";
        string ownerJson = ownerNick != null ? $"\"{ownerNick}\"" : "null";
        
        string jsonBody = $"{{\"x\": {x}, \"y\": {y}, \"tile_type\": {tileType}, \"hex_color\": {colorJson}, \"owner_nick\": {ownerJson}}}";

        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    /// <summary>
    /// Retrieves map updates. If lastSync is provided, executes a Delta Sync filtering by updated_at.
    /// </summary>
    public void FetchAllPixels(string lastSync, System.Action<Godot.Collections.Array> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);
        
        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200)
            {
                Json json = new Json();
                Error err = json.Parse(Encoding.UTF8.GetString(body));
                if (err == Error.Ok)
                {
                    Godot.Collections.Array data = json.Data.AsGodotArray();
                    onCompleted?.Invoke(data);
                }
            }
            else
            {
                GD.PrintErr($"[NETWORK ERROR] Delta Sync Failure: {responseCode}");
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/pixels?select=*";
        
        if (!string.IsNullOrEmpty(lastSync)) 
        {
            string safeDate = System.Uri.EscapeDataString(lastSync);
            url += $"&updated_at=gte.{safeDate}";
        }

        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    // =======================================================
    // PLAYER ENDPOINTS & TELEMETRY
    // =======================================================

    public void PostPlayer(string jsonBody, System.Action<bool> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            onCompleted?.Invoke(responseCode == 201 || responseCode == 200);
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/players";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json",
            "Prefer: return=minimal"
        };

        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    public void GetPlayer(string playerId, System.Action<Godot.Collections.Dictionary> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200)
            {
                Json json = new Json();
                if (json.Parse(Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    var array = json.Data.AsGodotArray();
                    if (array.Count > 0)
                        onCompleted?.Invoke(array[0].AsGodotDictionary());
                }
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/players?id=eq.{playerId}&select=*";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    public void UpdatePlayerState(string playerId, string jsonBody)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode != 204 && responseCode != 200)
                GD.PrintErr($"[SUPABASE ERROR] State update failed: {responseCode}");
            
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/players?id=eq.{playerId}";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json",
            "Prefer: return=minimal"
        };

        request.Request(url, headers, HttpClient.Method.Patch, jsonBody);
    }

    // =======================================================
    // REMOTE PROCEDURE CALLS (RPC)
    // =======================================================

    public void IncrementUserStat(string nickname, string statType)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode != 200 && responseCode != 204)
                GD.PrintErr($"[SUPABASE ERROR] Telemetry increment failure ({statType}): {responseCode}");
            
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/rpc/incrementar_estadistica";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json"
        };

        string jsonBody = $"{{\"nick_jugador\": \"{nickname}\", \"tipo_bloque\": \"{statType}\"}}";
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    public void ConsumirEnergia(string nickname)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);
        request.RequestCompleted += (r, c, h, b) => request.CallDeferred(Node.MethodName.QueueFree);

        string url = $"{_supabaseUrl}/rest/v1/rpc/gastar_energia";
        string[] headers = { $"apikey: {_supabaseKey}", $"Authorization: Bearer {_supabaseKey}", "Content-Type: application/json" };
        string jsonBody = $"{{\"nick_jugador\": \"{nickname}\"}}";
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    public void RecargarEnergia(string nickname, int cantidad)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);
        request.RequestCompleted += (r, c, h, b) => request.CallDeferred(Node.MethodName.QueueFree);

        string url = $"{_supabaseUrl}/rest/v1/rpc/recargar_energia";
        string[] headers = { $"apikey: {_supabaseKey}", $"Authorization: Bearer {_supabaseKey}", "Content-Type: application/json" };
        string jsonBody = $"{{\"nick_jugador\": \"{nickname}\", \"cantidad\": {cantidad}}}";
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    public void ActivarBonoNodo(string reclutador)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);
        request.RequestCompleted += (r, c, h, b) => request.CallDeferred(Node.MethodName.QueueFree);

        string url = $"{_supabaseUrl}/rest/v1/rpc/bono_nodo";
        string[] headers = { $"apikey: {_supabaseKey}", $"Authorization: Bearer {_supabaseKey}", "Content-Type: application/json" };
        string jsonBody = $"{{\"nick_reclutador\": \"{reclutador}\"}}";
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    /// <summary>
    /// Pings the database to register the player as online and returns the current total active users.
    /// </summary>
    public void PingAndGetOnlineCount(string nickname, System.Action<int> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200)
            {
                string bodyText = Encoding.UTF8.GetString(body);
                if (int.TryParse(bodyText, out int count))
                {
                    onCompleted?.Invoke(count);
                }
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/rpc/ping_y_contar";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json"
        };

        string jsonBody = $"{{\"nick_jugador\": \"{nickname}\"}}";
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    // =======================================================
    // CONNECTIONS & NETWORK GRAPHING
    // =======================================================

    public void GetConnections(string myNickname, System.Action<Godot.Collections.Array> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200) 
            {
                Json json = new Json();
                if (json.Parse(Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    onCompleted?.Invoke(json.Data.AsGodotArray());
                }
            }
            else
            {
                GD.PrintErr($"[SUPABASE] Connection scan error: {responseCode}");
                onCompleted?.Invoke(null);
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/usuarios?invited_by=eq.{myNickname}&select=nickname,country,blocks_cleared,tierra,piedra,pintura";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    public void GetAllUsersForNetwork(System.Action<Godot.Collections.Array> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200)
            {
                Json json = new Json();
                if (json.Parse(Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    onCompleted?.Invoke(json.Data.AsGodotArray());
                }
            }
            else
            {
                GD.PrintErr($"[SUPABASE] Global network scan error: {responseCode}");
                onCompleted?.Invoke(null);
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/usuarios?select=nickname,country,blocks_cleared,tierra,piedra,pintura,invited_by";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    public void GetPlayerStats(string nickname, System.Action<Godot.Collections.Dictionary> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200)
            {
                Json json = new Json();
                if (json.Parse(Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    var array = json.Data.AsGodotArray();
                    if (array.Count > 0)
                    {
                        onCompleted?.Invoke(array[0].AsGodotDictionary());
                    }
                    else onCompleted?.Invoke(null);
                }
            }
            else
            {
                GD.PrintErr($"[SUPABASE] Stat retrieval error for {nickname}: {responseCode}");
                onCompleted?.Invoke(null);
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        string url = $"{_supabaseUrl}/rest/v1/usuarios?nickname=eq.{nickname}&select=country,blocks_cleared,tierra,piedra,pintura,action_points";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }
}