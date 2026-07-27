using Godot;
using System;
using System.Collections.Generic;

public partial class GridManager : TileMap
{
    private Timer _networkSyncTimer;
    // --- SISTEMA DE ENERGÍA Y RECARGA ---
    private int _currentActionPoints = 50; 
    private Label _actionPointsLabel;
    private Timer _rechargeTimer;
    private float _rechargeTimeMinutes = 10.0f; // [VARIABLE MODIFICABLE] Tiempo de recarga

    // --- TRADUCTOR DE BANDERAS ISO ---
    private readonly Dictionary<string, string> _countryToIso = new Dictionary<string, string>
    {
        {"Argentina", "ar"}, {"Bolivia", "bo"}, {"Brasil", "br"}, {"Canadá", "ca"},
        {"Chile", "cl"}, {"Colombia", "co"}, {"Costa Rica", "cr"}, {"Cuba", "cu"},
        {"Ecuador", "ec"}, {"El Salvador", "sv"}, {"España", "es"}, {"Estados Unidos", "us"},
        {"Guatemala", "gt"}, {"Honduras", "hn"}, {"México", "mx"}, {"Nicaragua", "ni"},
        {"Panamá", "pa"}, {"Paraguay", "py"}, {"Perú", "pe"}, {"Puerto Rico", "pr"},
        {"República Dominicana", "do"}, {"Uruguay", "uy"}, {"Venezuela", "ve"},
        {"Alemania", "de"}, {"China", "cn"}, {"Francia", "fr"}, {"Italia", "it"},
        {"Japón", "jp"}, {"Reino Unido", "gb"}, {"Rusia", "ru"}
    };

    // --- SUPERPOSICIÓN DE COORDENADAS ---
    private Node2D _coordinateOverlay;
    public enum TileType { Canvas = 0, Dirt = 1, Stone = 2 }
    public enum ActionType { Paintbrush, Shovel, Pickaxe }

    // --- RED DE REFUERZOS (GraphEdit UI) ---
    private CanvasLayer _networkLayer;
    private Godot.GraphEdit _networkGraph;
    private Button _btnOpenNetwork;

    // --- SISTEMA DE PUNTUACIONES (TOP GLOBAL) ---
    private CanvasLayer _leaderboardLayer;
    private HBoxContainer _leaderboardColumnsContainer;

    // --- BARRERA DE AUTENTICACIÓN (Lógica Dura) ---
    private CanvasLayer _authLayer;
    private Control _authScreen;
    private LineEdit _nickInput;
    private LineEdit _passInput;
    private OptionButton _countrySelector;

    private LineEdit _recruiterInput; // <--- NUEVA VARIABLE INYECTADA
    private bool _isPlayerAuthenticated = false;

    // --- MEMORIA DE JUGADOR ACTIVO ---
    private string _activePlayerNick = "";
    
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

    // --- MEMORIA DE NOTIFICACIONES ---
    private bool _wasRing1Sealed = false;
    private bool _wasRing2Sealed = false;
    private float _lastKnownDonationAmount = -1f;
    
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

    // --- NUEVO: SISTEMA DE NAVEGACIÓN ---
    private bool _isDragging = false;
    private Vector2 _lastMousePosition;

    // Variables de Eventos del Entorno (Lógica Dura)
    private Random _random = new Random();
    private Timer _spawnTimer;
    
    public override void _Ready()
    {
        InitializeLocalGrid(); 
        InitializeProceduralUI();

        // Inicializar Motor de Recarga de Energía
        _rechargeTimer = new Timer();
        _rechargeTimer.WaitTime = _rechargeTimeMinutes * 60.0f; // Convertimos minutos a segundos
        _rechargeTimer.Autostart = true;
        _rechargeTimer.Timeout += OnRechargeTick;
        AddChild(_rechargeTimer);

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
        // --- INYECCIÓN DE ALTA DENSIDAD ---
        _coordinateOverlay.Scale = new Vector2(0.2f, 0.2f); // La encogemos a una quinta parte
        AddChild(_coordinateOverlay);
        _coordinateOverlay.Draw += DrawCoordinatesOverlay;
        
        _devCamera.Zoom = new Vector2(_currentZoom, _currentZoom);
        AddChild(_devCamera);
        _devCamera.MakeCurrent();

        // Intercepción del flujo: Primero mostramos la tarjeta benéfica
        InitializeWelcomeUI();

        // [DESBLOQUEO TÁCTICO] 
        // Descomenta esto SOLO cuando hayas limpiado tu base de datos (TRUNCATE TABLE pixels)
        
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

        // RELOJ DE SINCRONIZACIÓN DE RED (RADAR)
        _networkSyncTimer = new Timer();
        _networkSyncTimer.WaitTime = 5.0f; // Escanea el mapa cada 5 segundos
        _networkSyncTimer.Autostart = true;
        _networkSyncTimer.Timeout += SyncMapData;
        AddChild(_networkSyncTimer);
        
    }

    private void InitializeWelcomeUI()
    {
        CanvasLayer welcomeLayer = new CanvasLayer { Layer = 150 }; // Capa absoluta suprema

        // Fondo oscuro para aislar la atención del jugador
        ColorRect bgDark = new ColorRect { Color = new Color(0.02f, 0.02f, 0.02f, 0.95f) };
        bgDark.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        welcomeLayer.AddChild(bgDark);

        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bgDark.AddChild(center);

        // Contenedor principal de la tarjeta
        VBoxContainer cardBox = new VBoxContainer { CustomMinimumSize = new Vector2(700, 400), Alignment = BoxContainer.AlignmentMode.Center };
        cardBox.AddThemeConstantOverride("separation", 25);
        center.AddChild(cardBox);

        // TÍTULO
        Label title = new Label { Text = "¡Bienvenido a Pico, Pala y Pincel!", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); // Amarillo brillante
        cardBox.AddChild(title);

        // TEXTO BENÉFICO
        Label body1 = new Label { 
            Text = "Este es un juego Benéfico en Apoyo a los Afectados el 24 de Julio de 2026 en Venezuela. Compartir el juego con tus amigos ya es un enorme apoyo a esta noble causa. ¡Gracias por llegar hasta aquí de todo corazón! ❤", 
            HorizontalAlignment = HorizontalAlignment.Center, 
            AutowrapMode = TextServer.AutowrapMode.Word 
        };
        body1.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        cardBox.AddChild(body1);

        // SEPARADOR TÁCTICO
        HSeparator sep1 = new HSeparator();
        cardBox.AddChild(sep1);

        // CONTROLES
        Label body2 = new Label { 
            Text = "⛏ Con Pico (1) Quitas la Piedra\n⚒ Con Pala (2) Remueves Tierra\n🖌 Con Pincel (3) Pintas Casilla", 
            HorizontalAlignment = HorizontalAlignment.Center 
        };
        body2.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 1.0f)); // Azul táctico
        cardBox.AddChild(body2);

        // SEPARADOR TÁCTICO
        HSeparator sep2 = new HSeparator();
        cardBox.AddChild(sep2);

        // OBJETIVO FINAL
        Label body3 = new Label { 
            Text = "Coordina a tus amigos para hacer un dibujo, mensaje o ayudar a remover los escombros es una gran ayuda. Al terminar todos los objetivos el lienzo completo será publicado para descargar y compartir, demostrándole al mundo lo unidos que podemos estar cuando es necesario estar Juntos en los momentos más complicados.", 
            HorizontalAlignment = HorizontalAlignment.Center, 
            AutowrapMode = TextServer.AutowrapMode.Word 
        };
        body3.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        cardBox.AddChild(body3);

        // BOTÓN DE CONTINUAR (El detonador de la Auth UI)
        Button btnContinue = new Button { Text = "ENTENDIDO - CONTINUAR", CustomMinimumSize = new Vector2(300, 50), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        btnContinue.AddThemeColorOverride("font_color", new Color(0.2f, 0.9f, 0.4f)); // Verde para avanzar
        
        btnContinue.Pressed += () => 
        {
            welcomeLayer.QueueFree(); // Destruye la tarjeta
            InitializeAuthUI();       // Invoca la pantalla de registro
        };
        
        cardBox.AddChild(btnContinue);

        AddChild(welcomeLayer);
    }

    private void OnRechargeTick()
    {
        _currentActionPoints += 10;
        UpdateEnergyUI();
        SupabaseManager.Instance.RecargarEnergia(_activePlayerNick, 10);
        GD.Print($"[ECOSISTEMA] Recarga completada. Puntos actuales: {_currentActionPoints}");
    }

    private void SyncMapData()
    {
        // El radar no funciona si el operativo no ha ingresado al ecosistema
        if (!_isPlayerAuthenticated) return;

        SupabaseManager.Instance.FetchAllPixels((serverData) => 
        {
            if (serverData == null) return; // Cortafuegos por si la red falla

            foreach (var item in serverData)
            {
                var dict = item.AsGodotDictionary();
                int x = (int)dict["x"];
                int y = (int)dict["y"];
                int type = (int)dict["tile_type"];
                string hexColor = dict["hex_color"].AsString();
                
                Vector2I pos = new Vector2I(x, y);
                
                // Actualiza solo si hubo cambios reales en el terreno
                if (GetTileType(pos) != (TileType)type)
                {
                    SetTile(pos, (TileType)type);
                }

                if (type == 0 && !string.IsNullOrEmpty(hexColor))
                {
                    _paintedPixels[pos] = new Color(hexColor);
                }
            }
            // Fuerza la actualización visual de la pintura
            _paintOverlay.QueueRedraw();
        });

        SupabaseManager.Instance.GetPlayerStats(_activePlayerNick, (myStats) => 
        {
            if (myStats != null && myStats.ContainsKey("action_points"))
            {
                int serverEnergy = myStats["action_points"].AsInt32();

                // [CORTAFUEGOS ANTI-REBOTE]
                // Solo aceptamos la energía del servidor si es masivamente mayor a la local (El bono de +50).
                // Esto evita que recuperes 1 punto mágicamente por el ligero retraso de internet al hacer clic.
                if (serverEnergy >= _currentActionPoints + 40) 
                {
                    GD.Print("[RED] ¡NUEVO RECLUTA DETECTADO! Bono de energía recibido en tiempo real.");
                    // --- NUEVO: DISPARADOR DE RECLUTA ---
                    ShowFloatingMessage("¡NUEVO RECLUTA SE HA UNIDO! +50 Energía", new Color(0.2f, 0.8f, 1.0f));
                    _currentActionPoints = serverEnergy;
                    UpdateEnergyUI();

                    // --- INYECCIÓN EN TIEMPO REAL ---
                    // Si el panel de red está abierto en este instante, ordénale redibujar toda la malla
                    if (_networkLayer != null && IsInstanceValid(_networkLayer)) RefreshNetworkGraph();

                    // --- INYECCIÓN EN TIEMPO REAL: PUNTUACIONES ---
                    // Si el panel de Puntuaciones está abierto, descarga y reordena los nuevos datos
                    if (_leaderboardLayer != null && IsInstanceValid(_leaderboardLayer)) RefreshLeaderboardData();
                }
            }
        });
    }

    private void DrawCoordinatesOverlay()
    {
        Font defaultFont = ThemeDB.FallbackFont;
        
        // 1. Aumentamos la resolución nativa de la fuente x5
        int fontSize = 25; 
        float densityMultiplier = 5.0f; // Compensador matemático
        
        Color textColor = new Color(0.0f, 0.0f, 0.0f, 0.95f);
        Color outlineColor = new Color(1.0f, 1.0f, 1.0f, 0.8f); 
        
        // 2. Aumentamos también el grosor del borde en la misma proporción x5
        int outlineSize = 10; 

        for (int x = 0; x < GridSize.X; x++)
        {
            string colName = GetColumnName(x);
            for (int y = 0; y < GridSize.Y; y++)
            {
                string coordText = $"{colName}{y + 1}"; 
                Vector2 tileCenter = MapToLocal(new Vector2I(x, y));

                Vector2 stringSize = defaultFont.GetStringSize(coordText, HorizontalAlignment.Left, -1, fontSize);
                
                // 3. Multiplicamos la posición espacial por la densidad para que encaje perfecto
                Vector2 drawPos = (tileCenter * densityMultiplier) + new Vector2(-stringSize.X / 2, stringSize.Y / 3);
                
                _coordinateOverlay.DrawStringOutline(defaultFont, drawPos, coordText, HorizontalAlignment.Left, -1, fontSize, outlineSize, outlineColor);
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

        // --- INYECCIÓN DEL BOTÓN DE OBJETIVOS ---
        Button btnObjectives = new Button { Text = "📋 OBJETIVOS", CustomMinimumSize = new Vector2(150, 40) };
        btnObjectives.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); // Amarillo Neón
        btnObjectives.Pressed += OpenObjectivesPanel;
        
        btnBrush.Pressed += () => UpdateTool(ActionType.Paintbrush, "🖌", new Color(1, 1, 1));
        btnShovel.Pressed += () => UpdateTool(ActionType.Shovel, "⚒", new Color(1, 1, 1));
        btnPickaxe.Pressed += () => UpdateTool(ActionType.Pickaxe, "⛏", new Color(1, 1, 1));

        // Selector de Color
        ColorPickerButton colorPicker = new ColorPickerButton();
        colorPicker.CustomMinimumSize = new Vector2(50, 40);
        colorPicker.Color = CurrentPaintColor;
        colorPicker.ColorChanged += (Color newColor) => CurrentPaintColor = newColor;

        // Botones de Control de Lente
        Button btnZoomIn = new Button { Text = "🔍+" };
        Button btnZoomOut = new Button { Text = "🔍-" };
        
        btnZoomIn.Pressed += () => AdjustZoom(1.2f); // Aumenta el zoom un 20%
        btnZoomOut.Pressed += () => AdjustZoom(0.8f); // Reduce el zoom un 20%

        // --- INYECCIÓN: INTERRUPTOR DE COORDENADAS ---
        Button btnToggleCords = new Button { Text = "#️⃣ +/-", CustomMinimumSize = new Vector2(70, 40) };
        btnToggleCords.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f)); // Color por defecto

        // --- INYECCIÓN: BOTÓN DE PUNTUACIONES ---
        Button btnLeaderboard = new Button { Text = "🏆 TOP GLOBAL", CustomMinimumSize = new Vector2(150, 40) };
        btnLeaderboard.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); // Amarillo Neón
        btnLeaderboard.Pressed += OpenLeaderboardPanel;
        
        // Conectamos el clic para invertir la visibilidad (Si es true, la hace false. Si es false, la hace true)
        btnToggleCords.Pressed += () => 
        {
            _coordinateOverlay.Visible = !_coordinateOverlay.Visible;
            
            // Retroalimentación visual: El botón se oscurece si las apagas, y se ilumina si las enciendes
            if (_coordinateOverlay.Visible)
                btnToggleCords.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
            else
                btnToggleCords.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.4f)); 
        };

        // --- NUEVO: Botón de Red de Conexiones ---
        _btnOpenNetwork = new Button { Text = "[ MIS REFUERZOS ]", CustomMinimumSize = new Vector2(200, 40) };
        _btnOpenNetwork.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 0.2f)); // Verde neón
        _btnOpenNetwork.Pressed += OpenReinforcementsNetwork;


        // Indicador de Energía
        _actionPointsLabel = new Label { Text = $"⚡ ENERGÍA: {_currentActionPoints} " };
        _actionPointsLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); // Amarillo Neón
        _actionPointsLabel.VerticalAlignment = VerticalAlignment.Center;

        // Añadimos todo al contenedor horizontal (toolBar)
        toolBar.AddChild(btnBrush);
        toolBar.AddChild(btnShovel);
        toolBar.AddChild(btnPickaxe);
        toolBar.AddChild(_actionPointsLabel); // INYECCIÓN AQUÍ
        toolBar.AddChild(colorPicker);
        toolBar.AddChild(btnZoomIn);
        toolBar.AddChild(btnZoomOut);
        toolBar.AddChild(btnToggleCords); // <--- INYECTADO AQUÍ
        toolBar.AddChild(btnLeaderboard); // <--- AÑADIR AQUÍ
        toolBar.AddChild(_btnOpenNetwork); // Inyectado de forma segura al final de la barra
        toolBar.AddChild(btnObjectives); // <--- NUEVO BOTÓN AQUÍ
        
        hudLayer.AddChild(toolBar);

        // --- PANELES DE PROGRESO SUPERIOR ---
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

        // --- INYECCIÓN DEL BOTÓN DE DONACIÓN ---
        // Creamos un contenedor horizontal para que la barra y el botón estén lado a lado
        HBoxContainer donationContainer = new HBoxContainer();
        donationContainer.Alignment = BoxContainer.AlignmentMode.Center;
        donationContainer.AddThemeConstantOverride("separation", 15);
        
        donationContainer.AddChild(_donationProgressBar); // Metemos la barra existente

        // Creamos el botón con un rojo sutil / coral para llamar la atención
        Button btnDonate = new Button { Text = "❤️ DONAR", CustomMinimumSize = new Vector2(180, 30) };
        btnDonate.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f)); 
        
        // El comando OS.ShellOpen le ordena al sistema operativo abrir el navegador web por defecto
        btnDonate.Pressed += () => OS.ShellOpen("https://tiltify.com/@baelistick/global-game-jam-venezuela-earthquake-relief-fundraiser?origin=dashboard"); // <-- REEMPLAZA CON EL LINK PÚBLICO DE TU CAMPAÑA
        
        donationContainer.AddChild(btnDonate); // Metemos el botón al lado de la barra
        
        topBarsContainer.AddChild(donationContainer); // Finalmente, añadimos el bloque completo al HUD
        hudLayer.AddChild(topBarsContainer);

        AddChild(hudLayer);
    }

    private void UpdateEnergyUI()
    {
        if (_actionPointsLabel != null)
        {
            _actionPointsLabel.Text = $"⚡ ENERGÍA: {_currentActionPoints} ";
            if (_currentActionPoints > 0)
                _actionPointsLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); // Amarillo Normal
            else
                _actionPointsLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f)); // Rojo Alerta
        }
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

    // 5. GENERACIÓN ALEATORIA Y SISTEMA DE DEFENSAS (Lógica Dura)
    private void SpawnRandomDebris()
    {
        // [CORTAFUEGOS INYECTADO] No generar si no hay un operativo conectado
        if (!_isPlayerAuthenticated) return;

        // Evaluación Matemática de los Anillos Perimetrales
        bool isLayer1Sealed = CheckLayerSealed(0); // Anillo Exterior (Bloquea Roca)
        bool isLayer2Sealed = CheckLayerSealed(1); // Anillo Interior (Bloquea Tierra)

        // --- NUEVO: DISPARADOR DE ANILLOS (VERDE NEÓN) ---
        if (isLayer1Sealed && !_wasRing1Sealed)
        {
            _wasRing1Sealed = true;
            ShowFloatingMessage("¡ANILLO 1 SELLADO! Invasión de Piedra Bloqueada.", new Color(0.2f, 0.9f, 0.2f));
        }
        if (isLayer2Sealed && !_wasRing2Sealed)
        {
            _wasRing2Sealed = true;
            // Para el anillo 2, podemos usar un Verde Cian para diferenciarlo un poco, o dejarlo igual
            ShowFloatingMessage("¡ANILLO 2 SELLADO! Invasión de Tierra Bloqueada.", new Color(0.0f, 0.8f, 0.6f));
        }

        if (isLayer1Sealed && isLayer2Sealed)
        {
            GD.Print("[VICTORIA] Capas 1 y 2 selladas. El ecosistema ha sido dominado por completo.");
            return; // Bloquea todo el spawn
        }

        int debrisToSpawn = 45; 
        int spawned = 0;
        int maxAttempts = 200;  
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

        // MÁQUINA DE ESTADOS Y ATAJOS DE TECLADO
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
            return;
        }

        // --- DETECCIÓN DE BOTONES DEL RATÓN ---
        if (@event is InputEventMouseButton mouseBtnEvent)
        {
            // 1. SISTEMA DE NAVEGACIÓN (Arrastre con Clic Derecho o Rueda)
            if (mouseBtnEvent.ButtonIndex == MouseButton.Right || mouseBtnEvent.ButtonIndex == MouseButton.Middle)
            {
                if (mouseBtnEvent.Pressed)
                {
                    _isDragging = true;
                    _lastMousePosition = mouseBtnEvent.Position;
                }
                else
                {
                    _isDragging = false;
                }
            }

            // 2. LÓGICA CLÁSICA DE INTERACCIÓN (Clic Izquierdo)
            if (mouseBtnEvent.Pressed && mouseBtnEvent.ButtonIndex == MouseButton.Left)
            {
                // BARRERA DE ENERGÍA
                if (_currentActionPoints > 0)
                {
                    ProcessMapInteraction(GetLocalMousePosition());
                }
                else
                {
                    GD.PrintErr("[SISTEMA] Energía agotada. Espere la recarga o llame a sus refuerzos.");
                    UpdateEnergyUI(); // Fuerza el color rojo
                }
            }
        }

        // --- SISTEMA DE PANEADO EN TIEMPO REAL ---
        if (@event is InputEventMouseMotion mouseMotionEvent)
        {
            if (_isDragging)
            {
                // Calculamos cuánto se movió el ratón en la pantalla
                Vector2 delta = mouseMotionEvent.Position - _lastMousePosition;
                
                // Aplicamos el movimiento a la cámara de forma inversa y compensada por el zoom
                _devCamera.Position -= delta * (1.0f / _devCamera.Zoom.X);
                
                // Refrescamos la memoria de la posición para el siguiente fotograma
                _lastMousePosition = mouseMotionEvent.Position;
            }
        }

        // --- INYECCIÓN: LÓGICA DURA DE NAVEGACIÓN TÁCTIL ---
        if (@event is InputEventScreenDrag dragEvent)
        {
            // Si la cámara es válida, invertimos el vector de arrastre para simular "agarrar" el terreno
            if (_devCamera != null)
            {
                // Multiplicamos por la inversa del zoom para que el arrastre se sienta proporcional
                // sin importar si estás muy cerca o muy lejos del mapa
                _devCamera.Position -= dragEvent.Relative * (1.0f / _devCamera.Zoom.X);
            }
            return; // Cortamos la ejecución para no procesar otras entradas
        }
    }

    // Función unificada para procesar la interacción física
    private void ProcessMapInteraction(Vector2 localPosition)
    {
        Vector2I mapPosition = LocalToMap(localPosition);

        if (IsWithinBounds(mapPosition))
        {
            // LÓGICA DURA: Ejecutamos el golpe. Si devuelve TRUE (es decir, sí rompimos o pintamos algo), cobramos el punto de energía.
            if (ExecuteAction(mapPosition))
            {
                _currentActionPoints--;
                UpdateEnergyUI();
                SupabaseManager.Instance.ConsumirEnergia(_activePlayerNick); // <--- INYECCIÓN
                _rechargeTimer.Start(); 
            }
        }
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
                    SpawnImpactEffects(pos, ActionType.Pickaxe); // <--- INYECCIÓN VFX
                    GD.Print($"[ÉXITO] Piedra destruida en {pos}. Ahora es Lienzo Blanco.");
                    
                    // DISPARADOR DE TELEMETRÍA (Lógica Dura)
                    SupabaseManager.Instance.IncrementUserStat(_activePlayerNick, "piedra");
                    return true; 
                }
                GD.Print($"[FALLO] El Pico solo rompe Piedra. Bloque actual: {currentTile}");
                break;

            case ActionType.Shovel:
                if (currentTile == TileType.Dirt) 
                { 
                    // Transforma la Tierra en Lienzo (Blanco) y limpia cualquier color
                    UpdateTileLocal(pos, TileType.Canvas, null);
                    SpawnImpactEffects(pos, ActionType.Shovel); // <--- INYECCIÓN VFX
                    GD.Print($"[ÉXITO] Tierra removida en {pos}. Ahora es Lienzo Blanco.");
                    
                    // DISPARADOR DE TELEMETRÍA (Lógica Dura)
                    SupabaseManager.Instance.IncrementUserStat(_activePlayerNick, "tierra");
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
                    SpawnImpactEffects(pos, ActionType.Paintbrush, hexColor); // <--- INYECCIÓN VFX
                    GD.Print($"[ÉXITO] Lienzo pintado con color {hexColor} en {pos}.");
                    
                    // DISPARADOR DE TELEMETRÍA (Lógica Dura)
                    SupabaseManager.Instance.IncrementUserStat(_activePlayerNick, "pintura");
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

                // --- NUEVO: DISPARADOR DE DONACIÓN ---
                if (_lastKnownDonationAmount != -1f && amountRaised > _lastKnownDonationAmount)
                {
                    float difference = amountRaised - _lastKnownDonationAmount;
                    ShowFloatingMessage($"¡DONATIVO RECIBIDO! (+ ${difference:N0}) ¡El mundo esta con Venezuela 🇻🇪!", new Color(0.2f, 0.9f, 0.4f));
                }
                _lastKnownDonationAmount = amountRaised; // Guardamos el nuevo valor en la memoria

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
        _authTitleLabel = new Label { Text = "REGISTRARSE", HorizontalAlignment = HorizontalAlignment.Center };
        _authTitleLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 0.2f)); 
        formContainer.AddChild(_authTitleLabel);

        _nickInput = new LineEdit { PlaceholderText = "INGRESE UN NICK", Alignment = HorizontalAlignment.Center };
        formContainer.AddChild(_nickInput);

        _passInput = new LineEdit { PlaceholderText = "CONTRASEÑA", Alignment = HorizontalAlignment.Center, Secret = true }; 
        formContainer.AddChild(_passInput);

        _countrySelector = new OptionButton();
        _countrySelector.Alignment = HorizontalAlignment.Center;

        // Cargamos la fuente de emojis desde los archivos del proyecto
        Font emojiFont = GD.Load<Font>("res://Resource/Fonts/NotoColorEmoji.ttf");
        _countrySelector.AddThemeFontOverride("font", emojiFont);

        // --- BASE DE DATOS REGIONAL Y GLOBAL ---
        _countrySelector.AddItem("🇦🇷 Argentina", 0);
        _countrySelector.AddItem("🇧🇴 Bolivia", 1);
        _countrySelector.AddItem("🇧🇷 Brasil", 2);
        _countrySelector.AddItem("🇨🇦 Canadá", 3);
        _countrySelector.AddItem("🇨🇱 Chile", 4);
        _countrySelector.AddItem("🇨🇴 Colombia", 5);
        _countrySelector.AddItem("🇨🇷 Costa Rica", 6);
        _countrySelector.AddItem("🇨🇺 Cuba", 7);
        _countrySelector.AddItem("🇪🇨 Ecuador", 8);
        _countrySelector.AddItem("🇸🇻 El Salvador", 9);
        _countrySelector.AddItem("🇪🇸 España", 10);
        _countrySelector.AddItem("🇺🇸 Estados Unidos", 11);
        _countrySelector.AddItem("🇬🇹 Guatemala", 12);
        _countrySelector.AddItem("🇭🇳 Honduras", 13);
        _countrySelector.AddItem("🇲🇽 México", 14);
        _countrySelector.AddItem("🇳🇮 Nicaragua", 15);
        _countrySelector.AddItem("🇵🇦 Panamá", 16);
        _countrySelector.AddItem("🇵🇾 Paraguay", 17);
        _countrySelector.AddItem("🇵🇪 Perú", 18);
        _countrySelector.AddItem("🇵🇷 Puerto Rico", 19);
        _countrySelector.AddItem("🇩🇴 República Dominicana", 20);
        _countrySelector.AddItem("🇺🇾 Uruguay", 21);
        _countrySelector.AddItem("🇻🇪 Venezuela", 22);
        
        // Módulo Intercontinental
        _countrySelector.AddItem("🇩🇪 Alemania", 23);
        _countrySelector.AddItem("🇨🇳 China", 24);
        _countrySelector.AddItem("🇫🇷 Francia", 25);
        _countrySelector.AddItem("🇮🇹 Italia", 26);
        _countrySelector.AddItem("🇯🇵 Japón", 27);
        _countrySelector.AddItem("🇬🇧 Reino Unido", 28);
        _countrySelector.AddItem("🇷🇺 Rusia", 29);
        
        // Fallback del Ecosistema
        _countrySelector.AddItem("🇺🇳 Otra / Global", 30);

        formContainer.AddChild(_countrySelector);

        // --- NUEVA INYECCIÓN: CAMPO DEL RECLUTADOR ---
        _recruiterInput = new LineEdit { PlaceholderText = "¿QUIÉN TE INVITÓ? (Opcional)", Alignment = HorizontalAlignment.Center };
        formContainer.AddChild(_recruiterInput);

        // Botón Principal
        _submitAuthBtn = new Button { Text = "CONFIRMAR REGISTRO", CustomMinimumSize = new Vector2(0, 50) };
        _submitAuthBtn.Pressed += ProcessLoginAttempt;
        formContainer.AddChild(_submitAuthBtn);

        // NUEVO: Botón para cambiar entre Login y Registro
        _toggleModeBtn = new Button { Text = "¿Ya tienes un usuario? Iniciar Sesión", Flat = true };
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
            _authTitleLabel.Text = "INICIAR SESION";
            _countrySelector.Hide(); // Escondemos la bandera
            _recruiterInput.Hide();  // <--- OCULTAMOS EL CAMPO DE INVITACIÓN
            _submitAuthBtn.Text = "INICIAR";
            _toggleModeBtn.Text = "¿Eres Nuevo? Crear Cuenta";
        }
        else
        {
            _authTitleLabel.Text = "REGISTRARSE";
            _countrySelector.Show(); // Mostramos la bandera
            _recruiterInput.Show();  // <--- MOSTRAMOS EL CAMPO DE INVITACIÓN
            _submitAuthBtn.Text = "CONFIRMAR REGISTRO";
            _toggleModeBtn.Text = "¿Ya tienes un usuario? Iniciar Sesión";
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

        string safePasswordHash = HashPassword(pass);

        _submitAuthBtn.Disabled = true;
        _toggleModeBtn.Disabled = true;
        _submitAuthBtn.Text = "ESTABLECIENDO ENLACE...";

        if (_isLoginMode)
        {
            GD.Print($"[RED] Solicitando reconexión a base de datos. Nick: {nick}");
            SupabaseManager.Instance.LoginUser(nick, safePasswordHash, OnAuthenticationResult);
        }
        else
        {
            string country = _countrySelector.GetItemText(_countrySelector.Selected);
            string recruiter = _recruiterInput.Text.Trim(); // <--- CAPTURA DEL TEXTO

            // INYECCIÓN DE LAMBDA PARA EVALUAR EL BONO
            SupabaseManager.Instance.RegisterNewUser(nick, safePasswordHash, country, recruiter, (success) => 
            {
                if (success && !string.IsNullOrEmpty(recruiter))
                {
                    SupabaseManager.Instance.ActivarBonoNodo(recruiter);
                    _currentActionPoints += 50; // Reflejo visual inmediato
                }
                OnAuthenticationResult(success);
            });

            GD.Print($"[RED] Registro. Nick: {nick} | País: {country} | Reclutador: {(string.IsNullOrEmpty(recruiter) ? "Ninguno" : recruiter)}");
        }
    }

    // 3. El Recepcionista de Respuestas
    private void OnAuthenticationResult(bool success)
    {
        if (success)
        {
            // 1. EL DESBLOQUEO REAL
            string activeNickname = _nickInput.Text.Trim(); 
            _activePlayerNick = activeNickname; // GUARDAMOS EL DATO EN LA MEMORIA SEGURA
            
            _isPlayerAuthenticated = true;
            _authLayer.QueueFree(); // Ahora sí, podemos destruir el menú con seguridad
            GD.Print("[SISTEMA] Enlace autorizado. Despliegue de herramientas tácticas habilitado.");
            
            // 2. PRUEBA TÁCTICA DE CONEXIONES
            SupabaseManager.Instance.GetConnections(_activePlayerNick, (connectionsData) => 
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
            SupabaseManager.Instance.GetPlayerStats(_activePlayerNick, (myStats) => 
            {
                if (myStats != null && myStats.ContainsKey("action_points"))
                {
                    _currentActionPoints = myStats["action_points"].AsInt32();
                    UpdateEnergyUI();
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

    // =======================================================
    // RENDERIZADO VISUAL DE LA RED DE CONEXIONES
    // =======================================================

    private void OpenReinforcementsNetwork()
    {
        if (_networkLayer != null && IsInstanceValid(_networkLayer)) return;

        _networkLayer = new CanvasLayer { Layer = 90 }; 

        ColorRect bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f, 0.98f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _networkLayer.AddChild(bg);

        _networkGraph = new Godot.GraphEdit { RightDisconnects = false, ConnectionLinesCurvature = 0.5f };
        _networkGraph.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _networkLayer.AddChild(_networkGraph);

        Button btnClose = new Button { Text = "[ X ] CERRAR RED", CustomMinimumSize = new Vector2(150, 40) };
        btnClose.SetAnchorsPreset(Control.LayoutPreset.TopRight); 
        btnClose.Position = new Vector2(-170, 20); 
        btnClose.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
        btnClose.Pressed += () => _networkLayer.QueueFree();
        _networkLayer.AddChild(btnClose);

        AddChild(_networkLayer);

        // Disparamos la generación de la red
        RefreshNetworkGraph();
    }

    private void RefreshNetworkGraph()
    {
        if (_networkGraph == null || !IsInstanceValid(_networkGraph)) return;

        // Limpiamos la pantalla por si es una actualización en tiempo real
        foreach (Node child in _networkGraph.GetChildren()) { if (child is GraphNode) child.QueueFree(); }
        _networkGraph.ClearConnections();

        SupabaseManager.Instance.GetAllUsersForNetwork((allUsers) => 
        {
            if (!IsInstanceValid(_networkGraph) || allUsers == null) return;
            
            // 1. Convertimos la lista en un Diccionario Rápido para buscar por Nickname
            Dictionary<string, Godot.Collections.Dictionary> userMap = new Dictionary<string, Godot.Collections.Dictionary>();
            foreach (var u in allUsers)
            {
                var dict = u.AsGodotDictionary();
                userMap[dict["nickname"].AsString()] = dict;
            }

            if (!userMap.ContainsKey(_activePlayerNick)) return;
            
            var myData = userMap[_activePlayerNick];
            string myParentNick = myData.ContainsKey("invited_by") ? myData["invited_by"].AsString() : "";
            int currentY = 50;

            string mySafeName = _activePlayerNick.Replace(" ", ""); // Evita errores visuales en las líneas del Graph

            // 2. ¿Tenemos un Reclutador (Padre)? Si es así, lo dibujamos arriba.
            if (!string.IsNullOrEmpty(myParentNick) && userMap.ContainsKey(myParentNick))
            {
                string safeParentName = myParentNick.Replace(" ", "");
                DrawNodeFromData(userMap[myParentNick], safeParentName, myParentNick, "Jefe de Nodo", new Vector2(100, currentY));
                
                DrawNodeFromData(myData, mySafeName, _activePlayerNick, "Tú", new Vector2(500, currentY));
                _networkGraph.ConnectNode(safeParentName, 0, mySafeName, 0);
            }
            else
            {
                // Si no fuimos invitados por nadie, somos la raíz pura
                DrawNodeFromData(myData, mySafeName, _activePlayerNick, "Tú (Nodo Raíz)", new Vector2(100, currentY));
            }

            // 3. Disparamos la recursividad fractal para dibujar a nuestros Hijos, Nietos, etc.
            DrawFractalChildren(userMap, _activePlayerNick, mySafeName, currentY + 200);
        });
    }

    // El Algoritmo Fractal (Lógica Dura)
    private int DrawFractalChildren(Dictionary<string, Godot.Collections.Dictionary> userMap, string targetNick, string safeTargetName, int startY)
    {
        int currentY = startY;
        
        foreach (var kvp in userMap)
        {
            string childNick = kvp.Key;
            var childData = kvp.Value;
            string recruiter = childData.ContainsKey("invited_by") ? childData["invited_by"].AsString() : "";

            if (recruiter == targetNick)
            {
                string safeChildName = childNick.Replace(" ", "");
                
                // Buscamos la posición X de nuestro padre para dibujarnos a su derecha
                GraphNode parentNode = _networkGraph.GetNodeOrNull<GraphNode>(safeTargetName);
                float parentX = parentNode != null ? parentNode.PositionOffset.X : 100;
                
                Vector2 childPos = new Vector2(parentX + 400, currentY);
                DrawNodeFromData(childData, safeChildName, childNick, "Recluta", childPos);
                
                _networkGraph.ConnectNode(safeTargetName, 0, safeChildName, 0);
                
                // ¡AUTO-LLAMADA! El algoritmo entra dentro del hijo para buscarle sus propios reclutas
                int nextY = DrawFractalChildren(userMap, childNick, safeChildName, currentY);
                
                // Desplazamos el eje Y para que el siguiente hermano no se dibuje encima
                currentY = nextY > currentY ? nextY : currentY + 160;
            }
        }
        return currentY;
    }

    private void DrawNodeFromData(Godot.Collections.Dictionary data, string safeName, string realNick, string rank, Vector2 pos)
    {
        string c = data.ContainsKey("country") ? data["country"].AsString() : "🇺🇳";
        int blocks = data.ContainsKey("blocks_cleared") ? data["blocks_cleared"].AsInt32() : 0;
        int t = data.ContainsKey("tierra") ? data["tierra"].AsInt32() : 0;
        int p = data.ContainsKey("piedra") ? data["piedra"].AsInt32() : 0;
        int pt = data.ContainsKey("pintura") ? data["pintura"].AsInt32() : 0;

        GraphNode node = CreatePlayerNode(safeName, realNick, c, rank, blocks, t, p, pt, pos);
        _networkGraph.AddChild(node);
    }

    // Crea las "cajas" individuales que se conectan con los hilos
    // Crea las "cajas" individuales que se conectan con los hilos
    // Crea las "cajas" individuales que se conectan con los hilos
    private GraphNode CreatePlayerNode(string idName, string nick, string country, string rank, int blocks, int tierra, int piedra, int pintura, Vector2 position)
    {
        GraphNode node = new GraphNode();
        node.Name = idName;

        // 1. Título minimalista (Sin emojis que rompan Windows)
        node.Title = $"[{rank}] {nick}";
        node.PositionOffset = position; 
        node.SetSlot(0, true, 0, new Color(0.2f, 0.8f, 0.2f), true, 0, new Color(0.2f, 0.8f, 0.2f));

        VBoxContainer box = new VBoxContainer();
        box.Name = "DataContainer"; 
        node.AddChild(box);

        // --- 2. MOTOR DE RENDERIZADO DE BANDERAS SVG ---
        string isoCode = "un"; // Archivo por defecto (Naciones Unidas / Global)
        string cleanCountry = "Otra / Global";

        // Escaneamos la variable 'country' que viene de Supabase para hallar coincidencias
        foreach (var pair in _countryToIso)
        {
            if (country.Contains(pair.Key))
            {
                isoCode = pair.Value; // Asignamos el código (ej. "ve")
                cleanCountry = pair.Key; // Asignamos el nombre limpio ("Venezuela")
                break;
            }
        }

        // Construimos el contenedor horizontal para la bandera y el texto
        HBoxContainer headerBox = new HBoxContainer();
        headerBox.Alignment = BoxContainer.AlignmentMode.Begin; // <-- LÓGICA DURA CORREGIDA
        headerBox.AddThemeConstantOverride("separation", 8);

        TextureRect flagIcon = new TextureRect();
        flagIcon.CustomMinimumSize = new Vector2(24, 24); // El tamaño 1x1 asegurado
        flagIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        flagIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        
        // Cargamos la imagen SVG desde los archivos. El 'ResourceLoader' evita que el juego crashee si falta un SVG.
        string flagPath = $"res://UI/Flags/{isoCode}.svg";
        if (ResourceLoader.Exists(flagPath))
        {
            flagIcon.Texture = GD.Load<Texture2D>(flagPath);
        }

        Label countryLabel = new Label();
        countryLabel.Text = cleanCountry;
        countryLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));

        headerBox.AddChild(flagIcon);
        headerBox.AddChild(countryLabel);
        box.AddChild(headerBox);

        // Línea separadora tecnológica
        HSeparator separator = new HSeparator();
        separator.AddThemeConstantOverride("separation", 10);
        box.AddChild(separator);

        // 3. Estadísticas Base
        Label statsLabel = new Label();
        statsLabel.Name = "StatsLabel";
        statsLabel.Text = $"Bloques Totales: {blocks}\n[ Tierra: {tierra} | Piedra: {piedra} | Pintura: {pintura} ]";
        statsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        box.AddChild(statsLabel);

        return node;
    }

    // =======================================================
    // MOTOR DE OBJETIVOS Y MÉTRICAS (Lógica Dura)
    // =======================================================

    private void OpenObjectivesPanel()
    {
        CanvasLayer objLayer = new CanvasLayer { Layer = 95 }; // Por encima de casi todo

        ColorRect bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.05f, 0.98f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        objLayer.AddChild(bg);

        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.AddChild(center);

        VBoxContainer box = new VBoxContainer { CustomMinimumSize = new Vector2(600, 500), Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 25);
        center.AddChild(box);

        Label title = new Label { Text = "SISTEMA DE OBJETIVOS GLOBALES", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); 
        box.AddChild(title);

        // --- CÁLCULO EN TIEMPO REAL ---
        int totalTiles = GridSize.X * GridSize.Y;
        
        int ring0Max = GetRingTotal(0);
        int ring0Cur = GetRingSealedCount(0);
        
        int ring1Max = GetRingTotal(1);
        int ring1Cur = GetRingSealedCount(1);
        
        int currentDirt = 0;
        int currentStone = 0;
        
        // Escaneo profundo del terreno
        for (int x = 0; x < GridSize.X; x++) {
            for (int y = 0; y < GridSize.Y; y++) {
                TileType t = GetTileType(new Vector2I(x, y));
                if (t == TileType.Dirt) currentDirt++;
                else if (t == TileType.Stone) currentStone++;
            }
        }

        // --- INYECCIÓN DE LAS 5 BARRAS ---
        box.AddChild(CreateObjectiveBar("1. SELLAR ANILLO EXTERIOR (Inhibe aparición de Piedra)", ring0Cur, ring0Max, new Color(0.2f, 0.8f, 1.0f)));
        box.AddChild(CreateObjectiveBar("2. SELLAR ANILLO INTERIOR (Inhibe aparición de Tierra)", ring1Cur, ring1Max, new Color(0.2f, 0.8f, 1.0f)));
        box.AddChild(CreateObjectiveBar("3. LIMPIAR TODA LA TIERRA", totalTiles - currentDirt, totalTiles, new Color(0.6f, 0.4f, 0.2f)));
        box.AddChild(CreateObjectiveBar("4. LIMPIAR TODA LA PIEDRA", totalTiles - currentStone, totalTiles, new Color(0.5f, 0.5f, 0.5f)));
        box.AddChild(CreateObjectiveBar("5. PINTAR EL ECOSISTEMA (Cubrir todo el Lienzo)", _paintedPixels.Count, totalTiles, new Color(0.8f, 0.2f, 0.6f)));

        Button btnClose = new Button { Text = "[ VOLVER AL TERRENO ]", CustomMinimumSize = new Vector2(250, 40), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        btnClose.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
        btnClose.Pressed += () => objLayer.QueueFree();
        box.AddChild(btnClose);

        AddChild(objLayer);
    }

    private VBoxContainer CreateObjectiveBar(string title, int current, int max, Color fillColor)
    {
        VBoxContainer container = new VBoxContainer();
        
        Label lblTitle = new Label { Text = title };
        container.AddChild(lblTitle);

        ProgressBar bar = new ProgressBar { MaxValue = max, Value = current, CustomMinimumSize = new Vector2(650, 30), ShowPercentage = false };
        StyleBoxFlat bgStyle = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f), CornerRadiusTopLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusTopRight = 4 };
        StyleBoxFlat fillStyle = new StyleBoxFlat { BgColor = fillColor, CornerRadiusTopLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusTopRight = 4 };
        
        bar.AddThemeStyleboxOverride("background", bgStyle);
        bar.AddThemeStyleboxOverride("fill", fillStyle);
        
        int missing = max - current;
        Label lblStats = new Label { Text = $"Faltantes: {missing} / Completados: {current}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lblStats.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        
        bar.AddChild(lblStats);
        container.AddChild(bar);
        
        return container;
    }

    // Fórmula matemática para saber cuántos bloques conforman el perímetro exacto de una capa
    private int GetRingTotal(int layer)
    {
        int w = GridSize.X - (layer * 2);
        int h = GridSize.Y - (layer * 2);
        if (w <= 0 || h <= 0) return 0;
        return (w * 2) + (h * 2) - 4; // Restamos 4 para no contar las esquinas dos veces
    }

    // Algoritmo vectorial para contar cuántos píxeles de un anillo perimetral ya están pintados
    private int GetRingSealedCount(int layer)
    {
        int min = layer;
        int maxX = GridSize.X - 1 - layer;
        int maxY = GridSize.Y - 1 - layer;
        int count = 0;

        for (int x = min; x <= maxX; x++)
        {
            if (IsPixelSealed(new Vector2I(x, min))) count++;
            if (IsPixelSealed(new Vector2I(x, maxY))) count++;
        }
        for (int y = min + 1; y < maxY; y++)
        {
            if (IsPixelSealed(new Vector2I(min, y))) count++;
            if (IsPixelSealed(new Vector2I(maxX, y))) count++;
        }
        return count;
    }

    // =======================================================
    // MOTOR DE JUICE (FEEDBACK AUDIOVISUAL)
    // =======================================================
    private void SpawnImpactEffects(Vector2I mapPos, ActionType action, string hexColor = null)
    {
        // 1. Convertir la coordenada vectorial del mapa a espacio global 2D en pantalla
        Vector2 worldPos = MapToLocal(mapPos);

        // 2. Ensamblar Sistema de Partículas Procedimental (VFX)
        // [CORRECCIÓN APLICADA: CpuParticles2D con la capitalización exacta de Godot 4]
        CpuParticles2D vfx = new CpuParticles2D();
        vfx.Position = worldPos;
        vfx.Emitting = true;
        vfx.OneShot = true;
        vfx.Explosiveness = 0.85f; // Estallido rápido y agresivo
        vfx.Lifetime = 0.6f;
        vfx.EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere;
        vfx.EmissionSphereRadius = 8f;
        vfx.Spread = 180f; // Dispersión en todas direcciones
        vfx.Gravity = new Vector2(0, 150f); // Gravedad hacia abajo
        vfx.InitialVelocityMin = 30f;
        vfx.InitialVelocityMax = 80f;
        vfx.ScaleAmountMin = 2f;
        vfx.ScaleAmountMax = 4f;

        // 3. Perfilado Estético (Material de los escombros)
        if (action == ActionType.Pickaxe)
        {
            vfx.Color = new Color(0.6f, 0.6f, 0.6f); // Gris concreto
            vfx.ScaleAmountMax = 6f; // Rocas más pesadas
        }
        else if (action == ActionType.Shovel)
        {
            vfx.Color = new Color(0.4f, 0.25f, 0.1f); // Marrón tierra
            vfx.Amount = 16; // Más partículas para simular polvo
        }
        else if (action == ActionType.Paintbrush && hexColor != null)
        {
            vfx.Color = new Color(hexColor); // Salpicadura de pintura del color táctico elegido
            vfx.Gravity = new Vector2(0, 50f); // La pintura es densa, cae más lento
            vfx.InitialVelocityMax = 60f;
        }

        // Inyectar en el lienzo principal
        AddChild(vfx);

        // 4. Temporizador de Autodestrucción del Nodo (Gestión de Memoria)
        GetTree().CreateTimer(1.0f).Timeout += () => 
        {
            if (IsInstanceValid(vfx)) vfx.QueueFree();
        };

        // --- SISTEMA DE AUDIO ---
    }

    private void ShowFloatingMessage(string text, Color color)
    {
        CanvasLayer toastLayer = new CanvasLayer { Layer = 110 }; 

        Label msgLabel = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        msgLabel.AddThemeColorOverride("font_color", color);
        
        // --- BLINDAJE DE VISIBILIDAD ---
        msgLabel.AddThemeFontSizeOverride("font_size", 28); // Texto más grande
        msgLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1)); // Borde negro sólido
        msgLabel.AddThemeConstantOverride("outline_size", 8); // Grosor del borde
        
        // Posicionamiento
        msgLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        msgLabel.Position = new Vector2(-400, 150); 
        msgLabel.CustomMinimumSize = new Vector2(800, 50);

        // --- INYECCIÓN DE JUICE: PARTÍCULAS UI ---
        CpuParticles2D uiSparks = new CpuParticles2D();
        uiSparks.Position = new Vector2(400, 25); // Lo centramos dentro de la caja del texto (800/2, 50/2)
        uiSparks.Emitting = true;
        uiSparks.Amount = 40;
        uiSparks.Lifetime = 1.2f;
        uiSparks.OneShot = true;
        uiSparks.Explosiveness = 0.7f; // Estallido rápido al aparecer
        uiSparks.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
        uiSparks.EmissionRectExtents = new Vector2(350, 15); // Dispersión a lo largo de todo el texto
        uiSparks.Gravity = new Vector2(0, 60f); // Caen suavemente
        uiSparks.InitialVelocityMin = 30f;
        uiSparks.InitialVelocityMax = 70f;
        uiSparks.ScaleAmountMin = 2f;
        uiSparks.ScaleAmountMax = 6f;
        uiSparks.Color = color; // Las partículas heredan automáticamente el color (Verde) del texto

        // Anidamos las partículas al texto para que suban con él
        msgLabel.AddChild(uiSparks);
        toastLayer.AddChild(msgLabel);
        AddChild(toastLayer);

        // Animación (Sube y se desvanece)
        Tween tween = GetTree().CreateTween();
        tween.TweenProperty(msgLabel, "position", msgLabel.Position + new Vector2(0, -90), 3.0f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(msgLabel, "modulate", new Color(1, 1, 1, 0), 3.0f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        
        // Destrucción limpia
        tween.TweenCallback(Callable.From(() => toastLayer.QueueFree()));
    }

    private void OpenLeaderboardPanel()
    {
        if (_leaderboardLayer != null && IsInstanceValid(_leaderboardLayer)) return;

        _leaderboardLayer = new CanvasLayer { Layer = 92 }; 

        ColorRect bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f, 0.98f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _leaderboardLayer.AddChild(bg);

        // Contenedor Central Absoluto
        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _leaderboardLayer.AddChild(center);

        VBoxContainer mainBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        mainBox.AddThemeConstantOverride("separation", 30);
        center.AddChild(mainBox);

        Label title = new Label { Text = "SISTEMA DE CLASIFICACIÓN GLOBAL", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f));
        mainBox.AddChild(title);

        // Contenedor Horizontal para las 3 columnas
        _leaderboardColumnsContainer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _leaderboardColumnsContainer.AddThemeConstantOverride("separation", 60);
        mainBox.AddChild(_leaderboardColumnsContainer);

        Button btnClose = new Button { Text = "[ VOLVER AL ECOSISTEMA ]", CustomMinimumSize = new Vector2(250, 40), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        btnClose.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
        btnClose.Pressed += () => _leaderboardLayer.QueueFree();
        mainBox.AddChild(btnClose);

        AddChild(_leaderboardLayer);

        // Ejecutar la primera carga de datos
        RefreshLeaderboardData();
    }

    private void RefreshLeaderboardData()
    {
        if (_leaderboardLayer == null || !IsInstanceValid(_leaderboardLayer)) return;

        SupabaseManager.Instance.GetAllUsersForNetwork((allUsers) => 
        {
            if (_leaderboardLayer == null || !IsInstanceValid(_leaderboardLayer) || allUsers == null) return;

            // Limpiamos las columnas viejas para inyectar los datos actualizados
            foreach (Node child in _leaderboardColumnsContainer.GetChildren()) child.QueueFree();

            // Convertimos a una Lista de C# para usar el motor de ordenamiento avanzado
            System.Collections.Generic.List<Godot.Collections.Dictionary> usersList = new System.Collections.Generic.List<Godot.Collections.Dictionary>();
            foreach (var u in allUsers) usersList.Add(u.AsGodotDictionary());

            // 1. TOP PIEDRA
            usersList.Sort((a, b) => b["piedra"].AsInt32().CompareTo(a["piedra"].AsInt32()));
            CreateLeaderboardColumn("⛏ MÁXIMA PIEDRA DESTRUIDA", usersList, "piedra", new Color(0.6f, 0.6f, 0.6f));

            // 2. TOP TIERRA
            usersList.Sort((a, b) => b["tierra"].AsInt32().CompareTo(a["tierra"].AsInt32()));
            CreateLeaderboardColumn("⚒ MÁXIMA TIERRA REMOVIDA", usersList, "tierra", new Color(0.7f, 0.5f, 0.3f));

            // 3. TOP PINTURA
            usersList.Sort((a, b) => b["pintura"].AsInt32().CompareTo(a["pintura"].AsInt32()));
            CreateLeaderboardColumn("🖌 MÁXIMO LIENZO PINTADO", usersList, "pintura", new Color(0.8f, 0.2f, 0.6f));
        });
    }

    private void CreateLeaderboardColumn(string title, System.Collections.Generic.List<Godot.Collections.Dictionary> sortedUsers, string statKey, Color titleColor)
    {
        VBoxContainer col = new VBoxContainer { CustomMinimumSize = new Vector2(250, 300) };
        col.AddThemeConstantOverride("separation", 12);
        
        Label titleLabel = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        titleLabel.AddThemeColorOverride("font_color", titleColor);
        col.AddChild(titleLabel);

        HSeparator sep = new HSeparator();
        col.AddChild(sep);

        // Extraemos solo el Top 10
        int count = Mathf.Min(10, sortedUsers.Count);
        int validRanks = 0;

        for (int i = 0; i < count; i++)
        {
            var user = sortedUsers[i];
            string nick = user["nickname"].AsString();
            int score = user.ContainsKey(statKey) ? user[statKey].AsInt32() : 0;
            
            // Si el jugador tiene 0, no lo mostramos en el Top
            if (score <= 0) continue; 
            validRanks++;

            Label row = new Label { Text = $"#{validRanks} | {nick} : {score} ptos" };
            
            // Si eres tú, tu nombre brilla en la tabla
            if (nick == _activePlayerNick) 
                row.AddThemeColorOverride("font_color", new Color(0.2f, 0.9f, 0.2f)); // Verde Neón
            else
                row.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));

            col.AddChild(row);
        }

        if (validRanks == 0)
        {
            Label empty = new Label { Text = "Sin datos aún...", HorizontalAlignment = HorizontalAlignment.Center };
            empty.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.4f));
            col.AddChild(empty);
        }

        _leaderboardColumnsContainer.AddChild(col);
    }
}