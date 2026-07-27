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

    // NUEVO REGISTRO (Con Callback de respuesta y Sistema de Reclutamiento)
    public void RegisterNewUser(string nickname, string passwordHash, string country, string recruiter, System.Action<bool> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 201) // Éxito de creación
            {
                GD.Print("[SUPABASE] Usuario registrado y enlazado con éxito en la base de datos.");
                onCompleted?.Invoke(true); 
            }
            else
            {
                // [BLINDAJE TÁCTICO] Solo decodifica si el body no es nulo y tiene datos
                string errorResponse = (body != null && body.Length > 0) 
                    ? System.Text.Encoding.UTF8.GetString(body) 
                    : "Falla de red (0). Servidor inalcanzable o bloqueado.";
                
                GD.PrintErr($"[SUPABASE ERROR] Código: {responseCode} | Detalles: {errorResponse}");
                onCompleted?.Invoke(false); // Ahora sí reactivará el botón correctamente
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        // Apuntamos a tu tabla exacta 'usuarios'
        string url = $"{_supabaseUrl}/rest/v1/usuarios";
        string[] headers = new string[] {
            $"apikey: {_supabaseKey}", // Usando tu variable exacta
            $"Authorization: Bearer {_supabaseKey}",
            "Content-Type: application/json",
            "Prefer: return=minimal"
        };

        var userData = new Godot.Collections.Dictionary
        {
            { "nickname", nickname },
            { "password_hash", passwordHash },
            { "country", country },
            // Inyectamos el reclutador en la columna correcta que usa tu GetConnections
            { "invited_by", string.IsNullOrEmpty(recruiter) ? "" : recruiter } 
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
            request.CallDeferred(Node.MethodName.QueueFree);
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
            
            request.CallDeferred(Node.MethodName.QueueFree);
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
            request.CallDeferred(Node.MethodName.QueueFree);
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

    public void IncrementUserStat(string nickname, string statType)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode != 200 && responseCode != 204)
                GD.PrintErr($"[SUPABASE ERROR] Falla de telemetría al incrementar {statType}: {responseCode}");
            
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        // /rpc/ invoca Remote Procedure Calls en Supabase
        string url = $"{_supabaseUrl}/rest/v1/rpc/incrementar_estadistica";
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Content-Type: application/json"
        };

        // Construimos el JSON con los parámetros exactos que pide nuestra función SQL
        string jsonBody = $"{{\"nick_jugador\": \"{nickname}\", \"tipo_bloque\": \"{statType}\"}}";
        request.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    public void UpdatePlayerState(string playerId, string jsonBody)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode != 204 && responseCode != 200)
                GD.PrintErr($"Error actualizando jugador: {responseCode}");
            
            request.CallDeferred(Node.MethodName.QueueFree);
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
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        // Filtramos buscando filas donde 'invited_by' sea igual a tu nickname.
        // Solo traemos los datos que nos importan para ahorrar ancho de banda.
        string url = $"{_supabaseUrl}/rest/v1/usuarios?invited_by=eq.{myNickname}&select=nickname,country,blocks_cleared,tierra,piedra,pintura";
        
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
    }

    // =======================================================
    // ESCÁNER DE ESTADÍSTICAS PERSONALES
    // =======================================================
    public void GetPlayerStats(string nickname, System.Action<Godot.Collections.Dictionary> onCompleted)
    {
        HttpRequest request = new HttpRequest();
        AddChild(request);

        request.RequestCompleted += (result, responseCode, headers, body) => 
        {
            if (responseCode == 200)
            {
                Json json = new Json();
                if (json.Parse(System.Text.Encoding.UTF8.GetString(body)) == Error.Ok)
                {
                    var array = json.Data.AsGodotArray();
                    if (array.Count > 0)
                    {
                        // Devolvemos el primer (y único) perfil encontrado
                        onCompleted?.Invoke(array[0].AsGodotDictionary());
                    }
                    else onCompleted?.Invoke(null);
                }
            }
            else
            {
                GD.PrintErr($"[SUPABASE] Error obteniendo estadísticas de {nickname}: {responseCode}");
                onCompleted?.Invoke(null);
            }
            request.CallDeferred(Node.MethodName.QueueFree);
        };

        // Filtramos la tabla buscando coincidencia exacta con el Nickname
        string url = $"{_supabaseUrl}/rest/v1/usuarios?nickname=eq.{nickname}&select=country,blocks_cleared,tierra,piedra,pintura,action_points";
        
        string[] headers = {
            "apikey: " + _supabaseKey,
            "Authorization: Bearer " + _supabaseKey,
            "Accept: application/json"
        };

        request.Request(url, headers, HttpClient.Method.Get);
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

    
}