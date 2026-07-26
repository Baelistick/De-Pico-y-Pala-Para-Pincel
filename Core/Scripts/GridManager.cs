using Godot;
using System;
using System.Collections.Generic;

public partial class GridManager : TileMap
{
    // --- SUPERPOSICIÓN DE COORDENADAS ---
    private Node2D _coordinateOverlay;
    public enum TileType { Canvas = 0, Dirt = 1, Stone = 2 }
    public enum ActionType { Paintbrush, Shovel, Pickaxe }

    // --- BARRERA DE AUTENTICACIÓN (Lógica Dura) ---
    private CanvasLayer _authLayer;
    private Control _authScreen;
    private LineEdit _nickInput;
    private LineEdit _passInput;
    private OptionButton _countrySelector;
    private bool _isPlayerAuthenticated = false;
    
    // Controles de Estado
    private bool _isLoginMode = false;
    private Label _authTitleLabel;
    private Button _submitAuthBtn;
    private Button _toggleModeBtn;

    // --- TELEMETRÍA DE RED (Tiltify API - OAuth2 Automático) ---
    private HttpRequest _tiltifyAuthRequest;
    private HttpRequest _tiltifyDataRequest;
    private Timer _apiTimer;

    // Pega aquí tus credenciales del panel de la App
    private string _clientId = "0d76aa539e8b96d1b008a8521151cb9b44e3feb493acdad4963271346ab0e623"; 
    private string _clientSecret = "0b05b2cce7ab2edd45ba8c1fb87f5246509a93ca32b2d60fdbef5c32c2fea078"; 
    
    // Pega aquí el ID que extrajiste de la URL del Dashboard
    private string _tiltifyCampaignId = "096442d7-4e70-4025-b5ee-ea29ea323b28"; 
    
    private string _tiltifyToken = "";

    [Export] public Vector2I GridSize = new Vector2I(50, 50);
    
    private const int TILESET_SOURCE_ID = 0;
    
    // Bypass Espacial
    private readonly Vector2I ATLAS_CANVAS = new Vector2I(0, 0); 
    private readonly Vector2I ATLAS_STONE = new Vector2I(4, 0);  
    private readonly Vector2I ATLAS_DIRT = new Vector2I(7, 0);   

    public ActionType CurrentTool = ActionType.Paintbrush;
    public Color CurrentPaintColor = new Color(1, 0, 0); // Rojo por defecto
    
    private ReferenceRect _cursorRect;
    private Label _toolIcon;
    
    // Diccionario para almacenar la memoria de colores del lienzo
    private Dictionary<Vector2I, Color> _paintedPixels = new Dictionary<Vector2I, Color>();
    private Node2D _paintOverlay; // Capa superior para renderizar el pincel

    // Variables de HUD y Progreso (Lógica Dura)
    private ProgressBar _cleanProgressBar;
    private ProgressBar _donationProgressBar;
    private int _totalTiles;

    // Variables de la Cámara (Lógica Dura)
    private Camera2D _devCamera;
    private float _currentZoom = 1.0f;
    private float _minZoom = 0.1f; // Límite de alejamiento (se autocalcula luego)
    private float _maxZoom = 4.0f; // Límite de acercamiento máximo

    // Variables de Eventos del Entorno (Lógica Dura)
    private Random _random = new Random();
    private Timer _spawnTimer;
    
    public override void _Ready()
    {
        InitializeLocalGrid(); 
        InitializeProceduralUI();

        // 1. CAPA DE PINTURA: Se coloca sobre el TileMap (ZIndex = 5)
        _paintOverlay = new Node2D();
        _paintOverlay.ZIndex = 5; 
        _paintOverlay.Draw += DrawPaintOverlay; // Conectamos el evento de dibujo
        AddChild(_paintOverlay);

        // 2. RELOJ DEL ECOSISTEMA (Generación provisional de escombros)
        _spawnTimer = new Timer();
        _spawnTimer.WaitTime = 10.0f; // Intervalo provisional de 10 segundos
        _spawnTimer.Autostart = true;
        _spawnTimer.Timeout += SpawnRandomDebris; // Conecta el reloj a la función
        AddChild(_spawnTimer);

        // 4. CLIENTES HTTP (Lógica Dura de Autenticación)
        _tiltifyAuthRequest = new HttpRequest();
        AddChild(_tiltifyAuthRequest);
        _tiltifyAuthRequest.RequestCompleted += OnTiltifyTokenReceived;

        _tiltifyDataRequest = new HttpRequest();
        AddChild(_tiltifyDataRequest);
        _tiltifyDataRequest.RequestCompleted += OnTiltifyDataReceived; // La función que ya tenías

        _apiTimer = new Timer();
        _apiTimer.WaitTime = 60.0f; 
        _apiTimer.Autostart = true;
        _apiTimer.Timeout += RequestTiltifyData; 
        AddChild(_apiTimer);
        
        // Fase 1: Pedir las llaves de seguridad al arrancar el motor
        RequestTiltifyToken();

        // 3. CÁMARA TÁCTICA AUTOMÁTICA (Con Margen Aislante)
        _devCamera = new Camera2D();
        Vector2 mapPixelSize = new Vector2(GridSize.X * TileSet.TileSize.X, GridSize.Y * TileSet.TileSize.Y);
        _devCamera.Position = mapPixelSize / 2f; 
        
        Vector2 viewportSize = GetViewportRect().Size;
        float baseZoomFactor = Mathf.Min((viewportSize.X - 150) / mapPixelSize.X, (viewportSize.Y - 250) / mapPixelSize.Y);
        
        // Asignamos el factor base como el zoom actual y el límite mínimo permitido
        _minZoom = baseZoomFactor; 
        _currentZoom = _minZoom;

        // Inicializar Capa de Coordenadas Tácticas
        _coordinateOverlay = new Node2D();
        _coordinateOverlay.ZIndex = 5; // Se dibuja por encima de las baldosas
        AddChild(_coordinateOverlay);
        _coordinateOverlay.Draw += DrawCoordinatesOverlay;
        
        _devCamera.Zoom = new Vector2(_currentZoom, _currentZoom);
        AddChild(_devCamera);
        _devCamera.MakeCurrent();

        InitializeAuthUI();

        // [DESBLOQUEO TÁCTICO] 
        // Descomenta esto SOLO cuando hayas limpiado tu base de datos (TRUNCATE TABLE pixels)
        /*
        SupabaseManager.Instance.FetchAllPixels((serverData) => 
        {
            foreach (var item in serverData)
            {
                var dict = item.AsGodotDictionary();
                int x = (int)dict["x"];
                int y = (int)dict["y"];
                int type = (int)dict["tile_type"];
                string hexColor = dict["hex_color"].AsString();
                
                Vector2I pos = new Vector2I(x, y);
                SetTile(pos, (TileType)type);

                if (type == 0 && !string.IsNullOrEmpty(hexColor))
                {
                    _paintedPixels[pos] = new Color(hexColor);
                }
            }
            _paintOverlay.QueueRedraw();
        });
        */

    }

    private void DrawCoordinatesOverlay()
    {
        // Extraemos la fuente por defecto del motor
        Font defaultFont = ThemeDB.FallbackFont;
        int fontSize = 8; // Tamaño microscópico relativo al zoom
        Color textColor = new Color(1.0f, 1.0f, 1.0f, 0.4f); // Blanco al 40% de opacidad

        for (int x = 0; x < GridSize.X; x++)
        {
            string colName = GetColumnName(x);
            for (int y = 0; y < GridSize.Y; y++)
            {
                // Nombre de la casilla (Ej: "A1", "C14")
                // Le sumamos 1 a 'y' para que empiece en 1 y no en 0
                string coordText = $"{colName}{y + 1}"; 

                // Buscamos el centro exacto de la baldosa en el espacio 2D
                Vector2 tileCenter = MapToLocal(new Vector2I(x, y));

                // Calculamos el tamaño del texto para poder centrarlo matemáticamente
                Vector2 stringSize = defaultFont.GetStringSize(coordText, HorizontalAlignment.Left, -1, fontSize);
                
                // Aplicamos un offset para que el texto quede en el medio de la casilla
                Vector2 drawPos = tileCenter + new Vector2(-stringSize.X / 2, stringSize.Y / 3);
                
                // Estampamos el texto en el lienzo
                _coordinateOverlay.DrawString(defaultFont, drawPos, coordText, HorizontalAlignment.Left, -1, fontSize, textColor);
            }
        }
    }

    // 1. GENERACIÓN PROCEDIMENTAL DEL HUD (Lógica Dura)
    private void InitializeProceduralUI()
    {
        // El Cursor (¡Ahora ignora los clics del ratón!)
        _cursorRect = new ReferenceRect { 
            BorderColor = new Color(0.2f, 0.8f, 1.0f), 
            BorderWidth = 3.0f, 
            EditorOnly = false, 
            Size = (Vector2)TileSet.TileSize, 
            ZIndex = 10,
            MouseFilter = Control.MouseFilterEnum.Ignore // BLINDAJE ANTICOLISIONES
        };
        
        _toolIcon = new Label { 
            Text = "🖌", 
            HorizontalAlignment = HorizontalAlignment.Center, 
            VerticalAlignment = VerticalAlignment.Center, 
            Size = (Vector2)TileSet.TileSize,
            MouseFilter = Control.MouseFilterEnum.Ignore // BLINDAJE ANTICOLISIONES
        };
        
        _cursorRect.AddChild(_toolIcon);
        AddChild(_cursorRect);

        

        // El Canvas de la UI
        CanvasLayer hudLayer = new CanvasLayer();
        HBoxContainer toolBar = new HBoxContainer();
        toolBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        toolBar.Position = new Vector2(0, -60);
        toolBar.Alignment = BoxContainer.AlignmentMode.Center;
        toolBar.AddThemeConstantOverride("separation", 20);

        // Botones de Herramientas (Estos SÍ deben interceptar clics)
        Button btnBrush = new Button { Text = "🖌 Pincel" };
        Button btnShovel = new Button { Text = "⚒ Pala" };
        Button btnPickaxe = new Button { Text = "⛏ Pico" };
        
        btnBrush.Pressed += () => UpdateTool(ActionType.Paintbrush, "🖌", new Color(1, 1, 1));
        btnShovel.Pressed += () => UpdateTool(ActionType.Shovel, "⚒", new Color(1, 1, 1));
        btnPickaxe.Pressed += () => UpdateTool(ActionType.Pickaxe, "⛏", new Color(1, 1, 1));

        // Selector de Color
        ColorPickerButton colorPicker = new ColorPickerButton();
        colorPicker.CustomMinimumSize = new Vector2(50, 40);
        colorPicker.Color = CurrentPaintColor;
        colorPicker.ColorChanged += (Color newColor) => CurrentPaintColor = newColor;

        // [NUEVO] Botones de Control de Lente
        Button btnZoomIn = new Button { Text = "🔍+" };
        Button btnZoomOut = new Button { Text = "🔍-" };
        
        btnZoomIn.Pressed += () => AdjustZoom(1.2f); // Aumenta el zoom un 20%
        btnZoomOut.Pressed += () => AdjustZoom(0.8f); // Reduce el zoom un 20%

        toolBar.AddChild(btnBrush);
        toolBar.AddChild(btnShovel);
        toolBar.AddChild(btnPickaxe);
        toolBar.AddChild(colorPicker);

        toolBar.AddChild(btnZoomIn);
        toolBar.AddChild(btnZoomOut);
        
        hudLayer.AddChild(toolBar);

        // --- NUEVO: PANELES DE PROGRESO SUPERIOR ---
        _totalTiles = GridSize.X * GridSize.Y;

        VBoxContainer topBarsContainer = new VBoxContainer();
        topBarsContainer.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        topBarsContainer.Position = new Vector2(0, 20); // Margen superior
        topBarsContainer.Alignment = BoxContainer.AlignmentMode.Center;
        topBarsContainer.AddThemeConstantOverride("separation", 15);

        // Estilos Vectoriales
        StyleBoxFlat bgStyle = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        StyleBoxFlat blueFill = new StyleBoxFlat { BgColor = new Color(0.1f, 0.4f, 0.8f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        StyleBoxFlat greenFill = new StyleBoxFlat { BgColor = new Color(0.2f, 0.7f, 0.3f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };

        // 1. Barra de Limpieza del Mapa
        _cleanProgressBar = new ProgressBar { CustomMinimumSize = new Vector2(600, 30), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, ShowPercentage = false, MaxValue = _totalTiles };
        _cleanProgressBar.AddThemeStyleboxOverride("background", bgStyle);
        _cleanProgressBar.AddThemeStyleboxOverride("fill", blueFill);
        
        Label cleanLabel = new Label { Name = "CustomLabel", Text = "Limpieza del Mapa: 0%", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        cleanLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cleanProgressBar.AddChild(cleanLabel);

        // 2. Barra de Donativos (Tiltify Mockup)
        _donationProgressBar = new ProgressBar { CustomMinimumSize = new Vector2(600, 30), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, ShowPercentage = false };
        _donationProgressBar.AddThemeStyleboxOverride("background", bgStyle);
        _donationProgressBar.AddThemeStyleboxOverride("fill", greenFill);
        
        _donationProgressBar.MaxValue = 50000; // Meta: $50,000
        _donationProgressBar.Value = 1750;     // Recaudado: $1,750
        
        Label donationLabel = new Label { Name = "CustomLabel", Text = $"Earthquake Relief: ${_donationProgressBar.Value:N0} / ${_donationProgressBar.MaxValue:N0}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        donationLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _donationProgressBar.AddChild(donationLabel);

        topBarsContainer.AddChild(_cleanProgressBar);
        topBarsContainer.AddChild(_donationProgressBar);
        hudLayer.AddChild(topBarsContainer);

        AddChild(hudLayer);
    }

    private void UpdateTool(ActionType tool, string icon, Color color)
    {
        CurrentTool = tool;
        _toolIcon.Text = icon;
        _toolIcon.Modulate = color;
    }

    private void InitializeLocalGrid()
    {
        Clear();
        _paintedPixels.Clear();
        FastNoiseLite noise = new FastNoiseLite { Seed = 1999, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.15f };

        for (int x = 0; x < GridSize.X; x++)
        {
            for (int y = 0; y < GridSize.Y; y++)
            {
                float noiseValue = noise.GetNoise2D(x, y);
                TileType baseTile = TileType.Canvas; 
                if (noiseValue > 0.1f) baseTile = TileType.Stone; 
                else if (noiseValue > -0.2f) baseTile = TileType.Dirt; 
                SetTile(new Vector2I(x, y), baseTile);
            }
        }

        UpdateCleanlinessScore();
    }

    // 5. GENERACIÓN ALEATORIA DE ESCUADRONES DE ROCA Y TIERRA
    // 5. GENERACIÓN ALEATORIA Y SISTEMA DE DEFENSAS (Lógica Dura)
    private void SpawnRandomDebris()
    {
        // Evaluación Matemática de los Anillos Perimetrales
        bool isLayer1Sealed = CheckLayerSealed(0); // Anillo Exterior (Bloquea Roca)
        bool isLayer2Sealed = CheckLayerSealed(1); // Anillo Interior (Bloquea Tierra)

        if (isLayer1Sealed && isLayer2Sealed)
        {
            GD.Print("[VICTORIA] Capas 1 y 2 selladas. El ecosistema ha sido dominado por completo.");
            return; // Bloquea todo el spawn
        }

        int debrisToSpawn = 5; 
        int spawned = 0;
        int maxAttempts = 50;  
        int attempts = 0;

        // Establecer permisos de generación
        bool canSpawnStone = !isLayer1Sealed;
        bool canSpawnDirt = !isLayer2Sealed;

        while (spawned < debrisToSpawn && attempts < maxAttempts)
        {
            attempts++;
            
            int rx = _random.Next(0, GridSize.X);
            int ry = _random.Next(0, GridSize.Y);
            Vector2I randomPos = new Vector2I(rx, ry);

            // Regla Estricta: Solo espacios blancos vírgenes
            if (GetTileType(randomPos) == TileType.Canvas && !_paintedPixels.ContainsKey(randomPos))
            {
                TileType newDebris = TileType.Canvas;

                // Selector de invasión basado en las defensas activas
                if (canSpawnStone && canSpawnDirt) {
                    newDebris = _random.NextDouble() > 0.5 ? TileType.Stone : TileType.Dirt;
                } else if (canSpawnStone && !canSpawnDirt) {
                    newDebris = TileType.Stone;
                } else if (!canSpawnStone && canSpawnDirt) {
                    newDebris = TileType.Dirt;
                } else {
                    break; // Falla de seguridad (no debería ocurrir por el return superior)
                }
                
                UpdateTileLocal(randomPos, newDebris, null);
                spawned++;
            }
        }

        if (spawned > 0)
        {
            string report = $"[ECOSISTEMA] Han germinado {spawned} nuevos escombros.";
            if (isLayer1Sealed) report += " (Roca neutralizada por Capa 1).";
            if (isLayer2Sealed) report += " (Tierra neutralizada por Capa 2).";
            GD.Print(report);
        }
    }

    // Algoritmo de Escaneo Perimetral Vectorial
    // Algoritmo de Escaneo Perimetral Vectorial con Radar de Brechas
    private bool CheckLayerSealed(int layer)
    {
        int minX = layer;
        int minY = layer;
        int maxX = GridSize.X - 1 - layer;
        int maxY = GridSize.Y - 1 - layer;

        if (maxX <= minX || maxY <= minY) return false;

        // Escáner de Eje X (Líneas horizontales superior e inferior)
        for (int x = minX; x <= maxX; x++)
        {
            if (!IsPixelSealed(new Vector2I(x, minY))) { LogBreach(layer, new Vector2I(x, minY), "Superior"); return false; }
            if (!IsPixelSealed(new Vector2I(x, maxY))) { LogBreach(layer, new Vector2I(x, maxY), "Inferior"); return false; }
        }

        // Escáner de Eje Y (Líneas verticales izquierda y derecha)
        for (int y = minY + 1; y < maxY; y++)
        {
            if (!IsPixelSealed(new Vector2I(minX, y))) { LogBreach(layer, new Vector2I(minX, y), "Izquierdo"); return false; }
            if (!IsPixelSealed(new Vector2I(maxX, y))) { LogBreach(layer, new Vector2I(maxX, y), "Derecho"); return false; }
        }

        return true; 
    }

    // LÓGICA DURA: Solo verifica si la baldosa ha sido pintada (existe en el diccionario)
    private bool IsPixelSealed(Vector2I pos)
    {
        return _paintedPixels.ContainsKey(pos);
    }

    // Telemetría simplificada
    private void LogBreach(int layer, Vector2I pos, string sector)
    {
        GD.Print($"[ALERTA] Brecha Capa {layer} | Sector {sector} | Coordenada: {pos} | Estado: VACÍO (Requiere pintura)");
    }

    public override void _Process(double delta)
    {
        Vector2 mousePos = GetLocalMousePosition();
        Vector2I mapPos = LocalToMap(mousePos);
        
        if (IsWithinBounds(mapPos))
        {
            _cursorRect.Visible = true;
            _cursorRect.Position = MapToLocal(mapPos) - ((Vector2)TileSet.TileSize / 2f);
        }
        else _cursorRect.Visible = false;
    }

    // 6. MOTOR DE ESCALADO VISUAL
    private void AdjustZoom(float factor)
    {
        _currentZoom *= factor;

        // Abrazadera matemática (Clamp) para respetar los límites
        if (_currentZoom < _minZoom) _currentZoom = _minZoom;
        if (_currentZoom > _maxZoom) _currentZoom = _maxZoom;

        _devCamera.Zoom = new Vector2(_currentZoom, _currentZoom);
    }

    // 2. RENDERIZADO VECTORIAL SOBRE LA CAPA DE CRISTAL
    private void DrawPaintOverlay()
    {
        Vector2 tileSize = (Vector2)TileSet.TileSize;
        
        // Dibuja los colores guardados por encima del lienzo blanco
        foreach(var pixel in _paintedPixels)
        {
            _paintOverlay.DrawRect(new Rect2(pixel.Key * tileSize, tileSize), pixel.Value);
        }

        // Dibuja la cuadrícula aquí también para que quede encima de todo
        Color gridColor = new Color(0.8f, 0.8f, 0.8f, 0.3f); 
        for (int x = 0; x <= GridSize.X; x++) _paintOverlay.DrawLine(new Vector2(x * tileSize.X, 0), new Vector2(x * tileSize.X, GridSize.Y * tileSize.Y), gridColor);
        for (int y = 0; y <= GridSize.Y; y++) _paintOverlay.DrawLine(new Vector2(0, y * tileSize.Y), new Vector2(GridSize.X * tileSize.X, y * tileSize.Y), gridColor);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // [CORTAFUEGOS] Si no ha ingresado, se ignora cualquier acción física
        if (!_isPlayerAuthenticated) return;
        // MÁQUINA DE ESTADOS Y MACROS DE TECLADO
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == Key.Key1) 
            { 
                CurrentTool = ActionType.Paintbrush; 
                _toolIcon.Text = "🖌"; 
                _toolIcon.Modulate = new Color(1, 1, 1); 
            }
            if (keyEvent.Keycode == Key.Key2) 
            { 
                CurrentTool = ActionType.Shovel; 
                _toolIcon.Text = "⚒"; 
                _toolIcon.Modulate = new Color(1, 1, 1); 
            }
            if (keyEvent.Keycode == Key.Key3) 
            { 
                CurrentTool = ActionType.Pickaxe; 
                _toolIcon.Text = "⛏"; 
                _toolIcon.Modulate = new Color(1, 1, 1); 
            }
            
            // [MACRO DE DESARROLLADOR] - Tecla 4: Sellar el Anillo Exterior (Capa 0)
            if (keyEvent.Keycode == Key.Key4)
            {
                AutoSealLayer(0);
                return;
            }
            // [MACRO DE DESARROLLADOR] - Tecla 5: Sellar el Anillo Interior (Capa 1)
            if (keyEvent.Keycode == Key.Key5)
            {
                AutoSealLayer(1);
                return;
            }
            return;
        }

        // [NUEVO] LÓGICA DE TRAZO CONTINUO (Arrastrar el ratón)
        if (@event is InputEventMouseMotion motionEvent && (motionEvent.ButtonMask & MouseButtonMask.Left) != 0)
        {
            ProcessMapInteraction(GetLocalMousePosition());
        }
        // Lógica clásica de clic simple
        else if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            ProcessMapInteraction(GetLocalMousePosition());
        }
    }

    // Función unificada para procesar la interacción física
    private void ProcessMapInteraction(Vector2 localPosition)
    {
        Vector2I mapPosition = LocalToMap(localPosition);

        if (IsWithinBounds(mapPosition))
        {
            if (ExecuteAction(mapPosition))
            {
                // PlayerManager.Instance.ConsumeAction(); // [DESACTIVADO EN PRUEBAS]
            }
        }
    }

    // Herramienta de fuerza bruta para testear defensas al instante
    private void AutoSealLayer(int layer)
    {
        if (CurrentTool != ActionType.Paintbrush)
        {
            GD.Print("[DEV] Debes tener el Pincel seleccionado para usar la macro de auto-sellado.");
            return;
        }

        string hexColor = "#" + CurrentPaintColor.ToHtml(false);
        int min = layer;
        int maxX = GridSize.X - 1 - layer;
        int maxY = GridSize.Y - 1 - layer;

        // Limpia escombros y pinta el marco superior e inferior
        for (int x = min; x <= maxX; x++) 
        { 
            UpdateTileLocal(new Vector2I(x, min), TileType.Canvas, hexColor); 
            UpdateTileLocal(new Vector2I(x, maxY), TileType.Canvas, hexColor); 
        }
        // Limpia escombros y pinta el marco izquierdo y derecho
        for (int y = min; y <= maxY; y++) 
        { 
            UpdateTileLocal(new Vector2I(min, y), TileType.Canvas, hexColor); 
            UpdateTileLocal(new Vector2I(maxX, y), TileType.Canvas, hexColor); 
        }
        
        GD.Print($"[DEV] Capa {layer} sellada artificialmente con éxito.");
    }

    private bool ExecuteAction(Vector2I pos)
    {
        TileType currentTile = GetTileType(pos);

        switch (CurrentTool)
        {
            case ActionType.Pickaxe:
                if (currentTile == TileType.Stone) 
                { 
                    // Transforma la Piedra en Lienzo (Blanco) y limpia cualquier color
                    UpdateTileLocal(pos, TileType.Canvas, null); 
                    GD.Print($"[ÉXITO] Piedra destruida en {pos}. Ahora es Lienzo Blanco.");
                    return true; 
                }
                GD.Print($"[FALLO] El Pico solo rompe Piedra. Bloque actual: {currentTile}");
                break;

            case ActionType.Shovel:
                if (currentTile == TileType.Dirt) 
                { 
                    // Transforma la Tierra en Lienzo (Blanco) y limpia cualquier color
                    UpdateTileLocal(pos, TileType.Canvas, null); 
                    GD.Print($"[ÉXITO] Tierra removida en {pos}. Ahora es Lienzo Blanco.");
                    return true; 
                }
                GD.Print($"[FALLO] La Pala solo remueve Tierra. Bloque actual: {currentTile}");
                break;

            case ActionType.Paintbrush:
                if (currentTile == TileType.Canvas) 
                { 
                    // Solo si es Lienzo Blanco, extrae el color del UI y lo aplica
                    string hexColor = "#" + CurrentPaintColor.ToHtml(false);
                    UpdateTileLocal(pos, TileType.Canvas, hexColor); 
                    GD.Print($"[ÉXITO] Lienzo pintado con color {hexColor} en {pos}.");
                    return true; 
                }
                GD.Print($"[FALLO] El Pincel solo pinta sobre Lienzo Blanco. Bloque actual: {currentTile}. ¡Límpialo primero!");
                break;
        }
        
        return false;
    }

    private void UpdateTileLocal(Vector2I pos, TileType type, string hexColor)
    {
        SetTile(pos, type);
        
        if (hexColor != null) _paintedPixels[pos] = new Color(hexColor);
        else _paintedPixels.Remove(pos);
        
        // ORDENAMOS REDIBUJAR LA CAPA DE CRISTAL
        _paintOverlay.QueueRedraw(); 

        SupabaseManager.Instance.SavePixel(pos.X, pos.Y, (int)type, hexColor);

        UpdateCleanlinessScore();
    }

    private void SetTile(Vector2I pos, TileType type)
    {
        Vector2I atlasCoords = type switch
        {
            TileType.Canvas => ATLAS_CANVAS,
            TileType.Dirt => ATLAS_DIRT,
            TileType.Stone => ATLAS_STONE,
            _ => ATLAS_CANVAS
        };
        SetCell(0, pos, TILESET_SOURCE_ID, atlasCoords);
    }

    private TileType GetTileType(Vector2I pos)
    {
        Vector2I atlasCoords = GetCellAtlasCoords(0, pos);
        if (atlasCoords == ATLAS_STONE) return TileType.Stone;
        if (atlasCoords == ATLAS_DIRT) return TileType.Dirt;
        return TileType.Canvas;
    }

    private bool IsWithinBounds(Vector2I pos)
    {
        return pos.X >= 0 && pos.X < GridSize.X && pos.Y >= 0 && pos.Y < GridSize.Y;
    }

    // 7. MOTOR DE MÉTRICAS Y PROGRESO
    private void UpdateCleanlinessScore()
    {
        if (_cleanProgressBar == null) return;

        int cleanCount = 0;
        for (int x = 0; x < GridSize.X; x++)
        {
            for (int y = 0; y < GridSize.Y; y++)
            {
                if (GetTileType(new Vector2I(x, y)) == TileType.Canvas)
                {
                    cleanCount++;
                }
            }
        }
        
        _cleanProgressBar.Value = cleanCount;
        // Opcional: Actualizar el texto para que muestre el % de limpieza
        _cleanProgressBar.GetNode<Label>("CustomLabel").Text = $"Limpieza del Mapa: {((float)cleanCount / _totalTiles * 100):0.0}%";
    }

    // Envía la petición a los servidores de Tiltify
    // Fase 1: Negociación de Credenciales con Tiltify
    private void RequestTiltifyToken()
    {
        string url = "https://v5api.tiltify.com/oauth/token";
        string[] headers = new string[] { "Content-Type: application/json" };
        
        // Empaquetamos tus credenciales en JSON
        var authData = new Godot.Collections.Dictionary
        {
            { "client_id", _clientId },
            { "client_secret", _clientSecret },
            { "grant_type", "client_credentials" }
        };
        
        string jsonBody = Json.Stringify(authData);
        _tiltifyAuthRequest.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

    // Recepción y validación de la Llave Maestra
    private void OnTiltifyTokenReceived(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode == 200)
        {
            string jsonString = System.Text.Encoding.UTF8.GetString(body);
            Json json = new Json();
            if (json.Parse(jsonString) == Error.Ok)
            {
                var data = (Godot.Collections.Dictionary)json.Data;
                if (data.ContainsKey("access_token"))
                {
                    _tiltifyToken = (string)data["access_token"];
                    GD.Print("[RED] Enlace seguro establecido. Token de acceso generado.");
                    
                    // Fase 2: Ahora que somos un cliente autorizado, pedimos los datos
                    RequestTiltifyData();
                }
            }
        }
        else
        {
            GD.PrintErr($"[ERROR OAUTH] Tiltify rechazó el Client ID/Secret. Código: {responseCode}");
        }
    }

    // Fase 2: Búsqueda de datos de la Campaña
    private void RequestTiltifyData()
    {
        // Bloqueo de seguridad: No pedir datos si la Fase 1 no ha terminado
        if (string.IsNullOrEmpty(_tiltifyToken)) return;

        string url = $"https://v5api.tiltify.com/api/public/campaigns/{_tiltifyCampaignId}";
        string[] headers = new string[] {
            $"Authorization: Bearer {_tiltifyToken}",
            "Content-Type: application/json"
        };

        _tiltifyDataRequest.Request(url, headers, HttpClient.Method.Get);
    }

    // Procesa la respuesta de Tiltify
    private void OnTiltifyDataReceived(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode == 200) // 200 significa "Éxito Absoluto"
        {
            string jsonString = System.Text.Encoding.UTF8.GetString(body);
            
            // Usamos el parseador nativo de Godot para desarmar el JSON
            Json json = new Json();
            Error error = json.Parse(jsonString);

            if (error == Error.Ok)
            {
                // Navegamos por la estructura de Lógica Dura del JSON de Tiltify
                var data = (Godot.Collections.Dictionary)json.Data;
                var campaignData = (Godot.Collections.Dictionary)data["data"];
                
                // Extraemos las variables (Tiltify suele enviarlas como strings con formato de dinero)
                var amountRaisedObj = (Godot.Collections.Dictionary)campaignData["amount_raised"];
                var goalObj = (Godot.Collections.Dictionary)campaignData["goal"];

                float amountRaised = amountRaisedObj["value"].AsSingle();
                float goal = goalObj["value"].AsSingle();

                // Actualizamos nuestra interfaz vectorial
                if (_donationProgressBar != null)
                {
                    _donationProgressBar.MaxValue = goal;
                    _donationProgressBar.Value = amountRaised;
                    
                    Label donationLabel = _donationProgressBar.GetNode<Label>("CustomLabel");
                    if (donationLabel != null)
                    {
                        donationLabel.Text = $"Earthquake Relief: ${amountRaised:N0} / ${goal:N0}";
                    }
                }
                GD.Print($"[RED] Sincronización Tiltify exitosa: ${amountRaised}");
            }
        }
        else
        {
            GD.PrintErr($"[ERROR DE RED] Tiltify rechazó la conexión. Código: {responseCode}");
        }
    }

    // 8. BARRERA DE INGRESO Y REGISTRO
    private void InitializeAuthUI()
    {
        _authLayer = new CanvasLayer();
        _authLayer.Layer = 100; 

        _authScreen = new Control();
        _authScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // Fondo oscuro
        ColorRect isolationBg = new ColorRect();
        isolationBg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        isolationBg.Color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        _authScreen.AddChild(isolationBg);

        // NUEVO: Envoltorio para centrado absoluto
        CenterContainer centerWrapper = new CenterContainer();
        centerWrapper.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _authScreen.AddChild(centerWrapper);

        // Estructura central del formulario
        VBoxContainer formContainer = new VBoxContainer();
        formContainer.CustomMinimumSize = new Vector2(400, 300);
        formContainer.Alignment = BoxContainer.AlignmentMode.Center;
        formContainer.AddThemeConstantOverride("separation", 20);
        centerWrapper.AddChild(formContainer);

        // Título Dinámico
        _authTitleLabel = new Label { Text = "SISTEMA DE ENLACE: NUEVO REGISTRO", HorizontalAlignment = HorizontalAlignment.Center };
        _authTitleLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 0.2f)); 
        formContainer.AddChild(_authTitleLabel);

        _nickInput = new LineEdit { PlaceholderText = "INGRESE SU NICK (Ej. Baelistick)", Alignment = HorizontalAlignment.Center };
        formContainer.AddChild(_nickInput);

        _passInput = new LineEdit { PlaceholderText = "PASSWORD TEMPORAL", Alignment = HorizontalAlignment.Center, Secret = true }; 
        formContainer.AddChild(_passInput);

        _countrySelector = new OptionButton();
        _countrySelector.Alignment = HorizontalAlignment.Center;
        _countrySelector.AddItem("🇻🇪 Venezuela", 0);
        _countrySelector.AddItem("🇲🇽 México", 1);
        _countrySelector.AddItem("🇦🇷 Argentina", 2);
        _countrySelector.AddItem("🇪🇸 España", 3);
        _countrySelector.AddItem("🇨🇱 Chile", 4);
        _countrySelector.AddItem("🇺🇳 Otra / Global", 5);
        formContainer.AddChild(_countrySelector);

        // Botón Principal
        _submitAuthBtn = new Button { Text = "[ REGISTRAR NUEVO ENLACE ]", CustomMinimumSize = new Vector2(0, 50) };
        _submitAuthBtn.Pressed += ProcessLoginAttempt;
        formContainer.AddChild(_submitAuthBtn);

        // NUEVO: Botón para cambiar entre Login y Registro
        _toggleModeBtn = new Button { Text = "¿Ya tienes un enlace? Iniciar Sesión", Flat = true };
        _toggleModeBtn.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f)); // Gris sutil
        _toggleModeBtn.Pressed += ToggleAuthMode;
        formContainer.AddChild(_toggleModeBtn);

        _authLayer.AddChild(_authScreen);
        AddChild(_authLayer);
    }

    private void ToggleAuthMode()
    {
        _isLoginMode = !_isLoginMode; // Invertimos el estado

        if (_isLoginMode)
        {
            _authTitleLabel.Text = "SISTEMA DE ENLACE: RECONEXIÓN";
            _countrySelector.Hide(); // Escondemos la bandera
            _submitAuthBtn.Text = "[ INICIAR SESIÓN ]";
            _toggleModeBtn.Text = "¿Nuevo operativo? Crear Enlace";
        }
        else
        {
            _authTitleLabel.Text = "SISTEMA DE ENLACE: NUEVO REGISTRO";
            _countrySelector.Show(); // Mostramos la bandera
            _submitAuthBtn.Text = "[ REGISTRAR NUEVO ENLACE ]";
            _toggleModeBtn.Text = "¿Ya tienes un enlace? Iniciar Sesión";
        }
    }

    private void ProcessLoginAttempt()
    {
        string nick = _nickInput.Text.Trim();
        string pass = _passInput.Text.Trim();

        if (string.IsNullOrEmpty(nick) || string.IsNullOrEmpty(pass))
        {
            GD.PrintErr("[SISTEMA] Acceso denegado: Se requiere Nick y Password.");
            return;
        }

        // 1. Ciframos la contraseña localmente
        string safePasswordHash = HashPassword(pass);

        // 2. Apagamos el botón momentáneamente para evitar que el usuario haga spam de clics
        _submitAuthBtn.Disabled = true;
        _toggleModeBtn.Disabled = true;
        _submitAuthBtn.Text = "ESTABLECIENDO ENLACE...";

        if (_isLoginMode)
        {
            GD.Print($"[RED] Solicitando reconexión a base de datos. Nick: {nick}");
            // Enviamos los datos y la orden de qué hacer al recibir la respuesta
            SupabaseManager.Instance.LoginUser(nick, safePasswordHash, OnAuthenticationResult);
        }
        else
        {
            string country = _countrySelector.GetItemText(_countrySelector.Selected);
            GD.Print($"[RED] Solicitando creación de usuario. Nick: {nick} | País: {country}");
            SupabaseManager.Instance.RegisterNewUser(nick, safePasswordHash, country, OnAuthenticationResult);
        }
    }

    // 3. El Recepcionista de Respuestas
    private void OnAuthenticationResult(bool success)
    {
        if (success)
        {
            // 1. EL DESBLOQUEO REAL
            _isPlayerAuthenticated = true;
            _authLayer.QueueFree(); 
            GD.Print("[SISTEMA] Enlace autorizado. Despliegue de herramientas tácticas habilitado.");
            
            // 2. PRUEBA TÁCTICA DE CONEXIONES
            string activeNickname = _nickInput.Text.Trim(); 
            
            SupabaseManager.Instance.GetConnections(activeNickname, (connectionsData) => 
            {
                if (connectionsData != null && connectionsData.Count > 0)
                {
                    GD.Print($"[RED] Escáner completado: Tienes {connectionsData.Count} conexiones directas.");
                    foreach (Godot.Collections.Dictionary recluta in connectionsData)
                    {
                        string n = (string)recluta["nickname"];
                        string c = (string)recluta["country"];
                        GD.Print($"  -> Recluta: {n} | Base: {c}");
                    }
                }
                else
                {
                    GD.Print("[RED] Escáner completado: Aún no tienes conexiones en tu red.");
                }
            });
        }
        else
        {
            // 3. RECHAZO: El candado no se abre, reactivamos los botones para otro intento
            _submitAuthBtn.Disabled = false;
            _toggleModeBtn.Disabled = false;
            
            if (_isLoginMode)
                _submitAuthBtn.Text = "[ ERROR: CREDENCIALES INVÁLIDAS. REINTENTAR ]";
            else
                _submitAuthBtn.Text = "[ ERROR: NICK EN USO. ELEGIR OTRO ]";
                
            _submitAuthBtn.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f)); // Se vuelve rojo
        }
    }

    // 9. PROTOCOLO DE SEGURIDAD (Cifrado SHA-256)
    private string HashPassword(string rawPassword)
    {
        using (System.Security.Cryptography.SHA256 sha256Hash = System.Security.Cryptography.SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawPassword));
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }

    // =======================================================
    // SISTEMA DE COORDENADAS TÁCTICAS
    // =======================================================

    // Convierte el índice X (0, 1, 2) en formato alfabético (A, B, C... AA, AB)
    private string GetColumnName(int index)
    {
        int dividend = index + 1;
        string columnName = "";
        int modulo;

        while (dividend > 0)
        {
            modulo = (dividend - 1) % 26;
            columnName = System.Convert.ToChar(65 + modulo).ToString() + columnName;
            dividend = (int)((dividend - modulo) / 26);
        }
        return columnName;
    }
}