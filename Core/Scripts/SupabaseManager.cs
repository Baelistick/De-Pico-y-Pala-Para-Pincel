using Godot;
using System.Text;

// Nivel de acoplamiento: Global (Singleton). Gestiona exclusivamente las peticiones HTTP REST.
public partial class SupabaseManager : Node
{
    // Credenciales de Base de Datos
    private readonly string _supabaseUrl = "https://nhcdszvzavlzlxojpmtp.supabase.co"; 
    private readonly string _supabaseKey = "sb_publishable_WPWustCQG1VnTRGMQNFGvw_7wAob0r1";

    // [CORRECCIÓN 1] Declaración estructural del nodo de autenticación
    private HttpRequest _authRequest;

    public static SupabaseManager Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
    }

    // El paquete de envío para un NUEVO REGISTRO
    // =======================================================
    // BARRERA DE AUTENTICACIÓN DINÁMICA
    // =======================================================

    // NUEVO REGISTRO (Con Callback de respuesta)
    public void RegisterNewUser(string nickname, string passwordHash, string country, System.Action<bool> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 201) // Éxito de creación
            {
                GD.Print("[SUPABASE] Usuario registrado y enlazado con éxito en la base de datos.");
                onCompleted?.Invoke(true); // Avisamos a GridManager que todo salió bien
            }
            else
            {
                string errorResponse = System.Text.Encoding.UTF8.GetString(body);
                GD.PrintErr($"[SUPABASE ERROR] Código: {responseCode} | Detalles: {errorResponse}");
                onCompleted?.Invoke(false); // Avisamos que hubo un error (ej. Nick repetido)
            }
            request.QueueFree();
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
            { "country", country }
        };

        string jsonBody = Json.Stringify(userData);
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    // INICIO DE SESIÓN (Verificación de Credenciales)
    public void LoginUser(string nickname, string passwordHash, System.Action<bool> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200) // Petición de lectura exitosa
            {
                Json json = new Json();
                if (json.Parse(System.Text.Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    var array = json.Data.AsGodotArray();
                    
                    // Si el array tiene más de 0 elementos, encontramos una coincidencia en la BD
                    if (array.Count > 0)
                    {
                        GD.Print("[SUPABASE] Credenciales verificadas. Acceso concedido.");
                        onCompleted?.Invoke(true);
                    }
                    else
                    {
                        GD.PrintErr("[SUPABASE] Error de Autenticación: Nickname o Password incorrectos.");
                        onCompleted?.Invoke(false);
                    }
                }
            }
            else
            {
                GD.PrintErr($"[SUPABASE ERROR] Código de lectura: {responseCode}");
                onCompleted?.Invoke(false);
            }
            request.QueueFree();
        };

        // Construimos una petición GET que busque exactamente esa fila
        string url = $"{_supabaseUrl}/rest/v1/usuarios?nickname=eq.{nickname}&password_hash=eq.{passwordHash}&select=id";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    // Método optimizado (Upsert) para guardar o sobreescribir un pixel
    public void SavePixel(int x, int y, int tileType, string hexColor = null)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);
        
        // Lambda para limpiar el nodo automáticamente al terminar la petición
        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode != 200 && responseCode != 201 && responseCode != 204)
                GD.PrintErr($"Error Supabase: {responseCode} - {Encoding.UTF8.GetString(body)}");
            
            request.QueueFree();
        };

        string url = $"{_supabaseUrl}/rest/v1/pixels";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json",
            "Prefer: resolution=merge-duplicates" // Vital: Si el pixel existe, lo actualiza (Upsert)
        };

        string colorJson = hexColor != null ? $"\"{hexColor}\"" : "null";
        string jsonBody = $"{{\"x\": {x}, \"y\": {y}, \"tile_type\": {tileType}, \"hex_color\": {colorJson}}}";

        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    public void FetchAllPixels(System.Action<Godot.Collections.Array> onCompleted)
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
                GD.PrintErr($"Error de Lectura Supabase: {responseCode}");
            }
            request.QueueFree();
        };

        string url = $"{_supabaseUrl}/rest/v1/pixels?select=*";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    public void PostPlayer(string jsonBody, System.Action<bool> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            onCompleted?.Invoke(responseCode == 201 || responseCode == 200);
            request.QueueFree();
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
            request.QueueFree();
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
                GD.PrintErr($"Error actualizando jugador: {responseCode}");
            
            request.QueueFree();
        };

        string url = $"{_supabaseUrl}/rest/v1/players?id=eq.{playerId}";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json",
            "Prefer: return=minimal"
        };

        // HttpClient.Method.Patch se usa para modificar registros existentes
        request.Request(url, headers, HttpClient.Method.Patch, jsonBody);
    }

    // =======================================================
    // RED DE CONEXIONES Y REFERIDOS
    // =======================================================

    public void GetConnections(string myNickname, System.Action<Godot.Collections.Array> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200) // 200 OK - Lectura exitosa
            {
                Json json = new Json();
                if (json.Parse(System.Text.Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    // Devolvemos el array JSON crudo con todos los referidos
                    onCompleted?.Invoke(json.Data.AsGodotArray());
                }
            }
            else
            {
                GD.PrintErr($"[SUPABASE] Error escaneando la red de conexiones: {responseCode}");
                onCompleted?.Invoke(null);
            }
            request.QueueFree();
        };

        // Filtramos buscando filas donde 'invited_by' sea igual a tu nickname.
        // Solo traemos los datos que nos importan para ahorrar ancho de banda.
        string url = $"{_supabaseUrl}/rest/v1/usuarios?invited_by=eq.{myNickname}&select=nickname,country,blocks_cleared";
        
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    
}