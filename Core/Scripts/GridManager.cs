/*
 * BAELISTICK LABS | MENTE-0 ARCHITECTURE
 * Project: De Pico y Pala Para Pincel
 * Module: GridManager
 * Description: Core gameplay controller. Handles TileMap interactions, procedural UI generation,
 *              multi-touch/mouse input, local rendering, and coordinates with SupabaseManager.
 * Coupling Level: High. Acts as the primary orchestrator for the client application.
*/

using Godot;
using System;
using System.Collections.Generic;

public partial class GridManager : TileMap
{
    public string PublicPlayerNick => _activePlayerNick;
    private Timer _networkSyncTimer;

    // Synchronization memory (UTC ISO-8601 format)
    private string _lastSyncTimestamp = "";

    // --- MULTI-TOUCH SYSTEM (Pinch-to-Zoom) ---
    private System.Collections.Generic.Dictionary<int, Vector2> _activeTouches = new System.Collections.Generic.Dictionary<int, Vector2>();
    private float _lastPinchDistance = 0f;

    // Local ownership cache for energy-free repainting
    private System.Collections.Generic.HashSet<Vector2I> _myPixels = new System.Collections.Generic.HashSet<Vector2I>();

    // --- ENERGY & ECONOMY SYSTEM ---
    private int _currentActionPoints = 50; 
    private Label _actionPointsLabel;
    private Timer _rechargeTimer;
    private float _rechargeTimeMinutes = 10.0f; 

    // --- ISO FLAG TRANSLATOR ---
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

    // --- TACTICAL OVERLAYS ---
    private Node2D _coordinateOverlay;
    public enum TileType { Canvas = 0, Dirt = 1, Stone = 2 }
    public enum ActionType { Paintbrush, Shovel, Pickaxe, Eyedropper }

    // --- NETWORK GRAPH UI ---
    private CanvasLayer _networkLayer;
    private Godot.GraphEdit _networkGraph;
    private Button _btnOpenNetwork;

    // --- GLOBAL LEADERBOARD UI ---
    private CanvasLayer _leaderboardLayer;
    private HBoxContainer _leaderboardColumnsContainer;

    // --- AUTHENTICATION UI ---
    private CanvasLayer _authLayer;
    private Control _authScreen;
    private LineEdit _nickInput;
    private LineEdit _passInput;
    private OptionButton _countrySelector;
    private LineEdit _recruiterInput;
    private bool _isPlayerAuthenticated = false;

    // --- SESSION MEMORY ---
    private string _activePlayerNick = "";
    private bool _isLoginMode = false;
    private Label _authTitleLabel;
    private Button _submitAuthBtn;
    private Button _toggleModeBtn;
    private Label _onlineCountLabel;

    // --- TILTIFY API TELEMETRY (OAuth2) ---
    private HttpRequest _tiltifyAuthRequest;
    private HttpRequest _tiltifyDataRequest;
    private Timer _apiTimer;

    // API Credentials
    private string _clientId = "0d76aa539e8b96d1b008a8521151cb9b44e3feb493acdad4963271346ab0e623"; 
    private string _clientSecret = "0b05b2cce7ab2edd45ba8c1fb87f5246509a93ca32b2d60fdbef5c32c2fea078"; 
    private string _tiltifyCampaignId = "096442d7-4e70-4025-b5ee-ea29ea323b28"; 
    private string _tiltifyToken = "";

    [Export] public Vector2I GridSize = new Vector2I(100, 100);
    private const int TILESET_SOURCE_ID = 0;
    private ColorPickerButton _colorPickerBtn;

    // --- NOTIFICATION MEMORY ---
    private bool _wasRing1Sealed = false;
    private bool _wasRing2Sealed = false;
    private float _lastKnownDonationAmount = -1f;
    
    // Atlas Coordinates Bypass
    private readonly Vector2I ATLAS_CANVAS = new Vector2I(0, 0); 
    private readonly Vector2I ATLAS_STONE = new Vector2I(4, 0);  
    private readonly Vector2I ATLAS_DIRT = new Vector2I(7, 0);   

    public ActionType CurrentTool = ActionType.Paintbrush;
    public Color CurrentPaintColor = new Color(1, 0, 0); 
    
    private ReferenceRect _cursorRect;
    private TextureRect _toolIcon; 
    
    // Paint rendering layer
    private Dictionary<Vector2I, Color> _paintedPixels = new Dictionary<Vector2I, Color>();
    private Node2D _paintOverlay; 

    // HUD & Progress variables
    private ProgressBar _cleanProgressBar;
    private ProgressBar _donationProgressBar;
    private int _totalTiles;
    private int _currentCleanCount = 0; 

    // --- I18N SYSTEM ---
    private bool _isEnglish = false;
    private Button _langToggleButton;

    // Camera Configuration
    private Camera2D _devCamera;
    private float _currentZoom = 1.0f;
    private float _minZoom = 0.1f; 
    private float _maxZoom = 4.0f; 

    // --- NAVIGATION LOGIC ---
    private bool _isDragging = false;
    private Vector2 _lastMousePosition;

    // Environmental Event Configuration
    private Random _random = new Random();
    private Timer _spawnTimer;
    
    public override void _Ready()
    {
        InitializeLocalGrid(); 
        InitializeProceduralUI();

        // Energy Recharge Engine
        _rechargeTimer = new Timer();
        _rechargeTimer.WaitTime = _rechargeTimeMinutes * 60.0f; 
        _rechargeTimer.Autostart = true;
        _rechargeTimer.Timeout += OnRechargeTick;
        AddChild(_rechargeTimer);

        // Paint Rendering Layer (ZIndex = 5 ensures it renders above the TileMap)
        _paintOverlay = new Node2D();
        _paintOverlay.ZIndex = 5; 
        _paintOverlay.Draw += DrawPaintOverlay; 
        AddChild(_paintOverlay);

        // Environmental clock for procedural debris spawning
        _spawnTimer = new Timer();
        _spawnTimer.WaitTime = 10.0f; 
        _spawnTimer.Autostart = true;
        _spawnTimer.Timeout += SpawnRandomDebris; 
        AddChild(_spawnTimer);

        // HTTP Clients for API Auth
        _tiltifyAuthRequest = new HttpRequest();
        AddChild(_tiltifyAuthRequest);
        _tiltifyAuthRequest.RequestCompleted += OnTiltifyTokenReceived;

        _tiltifyDataRequest = new HttpRequest();
        AddChild(_tiltifyDataRequest);
        _tiltifyDataRequest.RequestCompleted += OnTiltifyDataReceived;

        _apiTimer = new Timer();
        _apiTimer.WaitTime = 60.0f; 
        _apiTimer.Autostart = true;
        _apiTimer.Timeout += RequestTiltifyData; 
        AddChild(_apiTimer);
        
        // Initial token request
        RequestTiltifyToken();

        // Automated Tactical Camera Setup
        _devCamera = new Camera2D();
        Vector2 mapPixelSize = new Vector2(GridSize.X * TileSet.TileSize.X, GridSize.Y * TileSet.TileSize.Y);
        _devCamera.Position = mapPixelSize / 2f; 
        
        Vector2 viewportSize = GetViewportRect().Size;
        float baseZoomFactor = Mathf.Min((viewportSize.X - 150) / mapPixelSize.X, (viewportSize.Y - 250) / mapPixelSize.Y);
        
        _minZoom = baseZoomFactor; 
        _currentZoom = _minZoom;

        // Tactical Coordinates Overlay Layer
        _coordinateOverlay = new Node2D();
        _coordinateOverlay.ZIndex = 5; 
        _coordinateOverlay.Scale = new Vector2(0.2f, 0.2f); 
        _coordinateOverlay.Visible = false; // Disabled by default for performance optimization
        AddChild(_coordinateOverlay);
        _coordinateOverlay.Draw += DrawCoordinatesOverlay;
        
        _devCamera.Zoom = new Vector2(_currentZoom, _currentZoom);
        AddChild(_devCamera);
        _devCamera.MakeCurrent();

        // Initialize user flow: Show charity welcome card first
        InitializeWelcomeUI();

        // Initial Full Ecosystem Sync
        SupabaseManager.Instance.FetchAllPixels("", (serverData) =>
        {
            _lastSyncTimestamp = Time.GetDatetimeStringFromSystem(true, true) + "Z";

            foreach (var item in serverData)
            {
                var dict = item.AsGodotDictionary();
                int x = (int)dict["x"];
                int y = (int)dict["y"];
                int type = (int)dict["tile_type"];
                string hexColor = dict["hex_color"].AsString();

                Vector2I pos = new Vector2I(x, y);
                string owner = dict.ContainsKey("owner_nick") ? dict["owner_nick"].AsString() : "";

                // Identity Firewall: Verify ownership for energy exemption
                if (!string.IsNullOrEmpty(owner) && owner == _activePlayerNick)
                {
                    _myPixels.Add(pos);
                }
                else
                {
                    _myPixels.Remove(pos);
                }
    
                SetTile(pos, (TileType)type);

                if (type == 0 && !string.IsNullOrEmpty(hexColor))
                {
                    _paintedPixels[pos] = new Color(hexColor);
                }
            }
            _paintOverlay.QueueRedraw();
            
            // Recalibrate UI to reflect downloaded database state
            CalculateInitialCleanliness();
        });

        // Network Synchronization Clock (Delta Sync Radar)
        _networkSyncTimer = new Timer();
        _networkSyncTimer.WaitTime = 5.0f; 
        _networkSyncTimer.Autostart = true;
        _networkSyncTimer.Timeout += SyncMapData;
        AddChild(_networkSyncTimer);
    }

    private void InitializeWelcomeUI()
    {
        CanvasLayer welcomeLayer = new CanvasLayer { Layer = 150 }; 

        ColorRect bgDark = new ColorRect { Color = new Color(0.02f, 0.02f, 0.02f, 0.95f) };
        bgDark.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        welcomeLayer.AddChild(bgDark);

        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bgDark.AddChild(center);

        VBoxContainer cardBox = new VBoxContainer { CustomMinimumSize = new Vector2(700, 450), Alignment = BoxContainer.AlignmentMode.Center };
        cardBox.AddThemeConstantOverride("separation", 20);
        center.AddChild(cardBox);

        Button btnLangToggle = new Button { 
            Text = _isEnglish ? "🌐 Cambiar a Español (ES)" : "🌐 Switch to English (EN)", 
            CustomMinimumSize = new Vector2(240, 35), 
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter 
        };
        btnLangToggle.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1.0f));

        Label title = new Label { Text = GetText("WELCOME_TITLE"), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f));

        Label body1 = new Label { 
            Text = GetText("WELCOME_BODY1"), 
            HorizontalAlignment = HorizontalAlignment.Center, 
            AutowrapMode = TextServer.AutowrapMode.Word 
        };
        body1.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));

        Label body2 = new Label { 
            Text = GetText("WELCOME_CONTROLS"), 
            HorizontalAlignment = HorizontalAlignment.Center 
        };
        body2.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 1.0f));

        Label body3 = new Label { 
            Text = GetText("WELCOME_BODY3"), 
            HorizontalAlignment = HorizontalAlignment.Center, 
            AutowrapMode = TextServer.AutowrapMode.Word 
        };
        body3.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));

        Button btnContinue = new Button { Text = GetText("WELCOME_BTN"), CustomMinimumSize = new Vector2(300, 50), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        btnContinue.AddThemeColorOverride("font_color", new Color(0.2f, 0.9f, 0.4f));

        btnLangToggle.Pressed += () => 
        {
            _isEnglish = !_isEnglish;
            btnLangToggle.Text = _isEnglish ? "🌐 Cambiar a Español (ES)" : "🌐 Switch to English (EN)";
            title.Text = GetText("WELCOME_TITLE");
            body1.Text = GetText("WELCOME_BODY1");
            body2.Text = GetText("WELCOME_CONTROLS");
            body3.Text = GetText("WELCOME_BODY3");
            btnContinue.Text = GetText("WELCOME_BTN");
        };

        btnContinue.Pressed += () => 
        {
            welcomeLayer.QueueFree(); 
            InitializeAuthUI();       
        };

        cardBox.AddChild(btnLangToggle);
        cardBox.AddChild(title);
        cardBox.AddChild(body1);
        cardBox.AddChild(new HSeparator());
        cardBox.AddChild(body2);
        cardBox.AddChild(new HSeparator());
        cardBox.AddChild(body3);
        cardBox.AddChild(btnContinue);

        AddChild(welcomeLayer);
    }

    private void OnRechargeTick()
    {
        _currentActionPoints += 10;
        UpdateEnergyUI();
        SupabaseManager.Instance.RecargarEnergia(_activePlayerNick, 10);
        GD.Print($"[SYSTEM] Energy recharged. Current Points: {_currentActionPoints}");
    }

    private void SyncMapData()
    {
        if (!_isPlayerAuthenticated) return;

        // Population Radar Ping
        SupabaseManager.Instance.PingAndGetOnlineCount(_activePlayerNick, (count) => 
        {
            if (_onlineCountLabel != null)
                _onlineCountLabel.Text = $"{count} Online";
        });

        // Delta Sync: Retrieve only modified pixels based on timestamp
        SupabaseManager.Instance.FetchAllPixels(_lastSyncTimestamp, (serverData) =>
        {
            _lastSyncTimestamp = Time.GetDatetimeStringFromSystem(true, true) + "Z";

            if (serverData == null || serverData.Count == 0) return; 

            bool mapHasChanged = false;

            foreach (var item in serverData)
            {
                var dict = item.AsGodotDictionary();
                int x = (int)dict["x"];
                int y = (int)dict["y"];
                int type = (int)dict["tile_type"];
                string hexColor = dict["hex_color"].AsString();
    
                Vector2I pos = new Vector2I(x, y);
                string owner = dict.ContainsKey("owner_nick") ? dict["owner_nick"].AsString() : "";

                if (owner == _activePlayerNick)
                    _myPixels.Add(pos);
                else
                    _myPixels.Remove(pos);
    
                SetTile(pos, (TileType)type);
                
                if (IsWithinBounds(pos))
                {
                    TileType oldType = GetTileType(pos);
                    TileType newType = (TileType)type;
                    bool localMapChanged = false;

                    // Evaluate structural changes (Dirt/Stone breaking)
                    if (oldType != newType)
                    {
                        SetTile(pos, newType);
                        localMapChanged = true;

                        // Deduce tool used for remote visual feedback
                        if (newType == TileType.Canvas)
                        {
                            if (oldType == TileType.Stone) 
                                SpawnImpactEffects(pos, ActionType.Pickaxe);
                            else if (oldType == TileType.Dirt) 
                                SpawnImpactEffects(pos, ActionType.Shovel);
                        }
                    }

                    // Evaluate paint additions
                    if (newType == TileType.Canvas && !string.IsNullOrEmpty(hexColor))
                    {
                        string cleanHex = hexColor.Trim('#'); 
                        bool isNewPaint = !_paintedPixels.ContainsKey(pos);
                        bool isDifferentColor = isNewPaint || _paintedPixels[pos].ToHtml(false) != cleanHex;

                        if (isDifferentColor)
                        {
                            _paintedPixels[pos] = new Color(hexColor);
                            localMapChanged = true;
                            SpawnImpactEffects(pos, ActionType.Paintbrush, hexColor);
                        }
                    }
                    // Remote paint deletion
                    else if (newType == TileType.Canvas && string.IsNullOrEmpty(hexColor) && _paintedPixels.ContainsKey(pos))
                    {
                         _paintedPixels.Remove(pos);
                         localMapChanged = true;
                    }

                    if (localMapChanged) mapHasChanged = true;
                }
            }
            
            // Queue redraw only if data changes occurred to optimize rendering
            if (mapHasChanged) 
            {
                _paintOverlay.QueueRedraw();
                CalculateInitialCleanliness(); 
            }
        });

        // Fetch Player Stats & Detect Recruitment Bonus
        SupabaseManager.Instance.GetPlayerStats(_activePlayerNick, (myStats) => 
        {
            if (myStats != null && myStats.ContainsKey("action_points"))
            {
                int serverEnergy = myStats["action_points"].AsInt32();

                // Anti-bounce validation: Apply only massive energy spikes (+50 bonus)
                if (serverEnergy >= _currentActionPoints + 40) 
                {
                    ShowFloatingMessage("NEW RECRUIT JOINED! +50 Energy", new Color(0.2f, 0.8f, 1.0f));
                    _currentActionPoints = serverEnergy;
                    UpdateEnergyUI();

                    if (_networkLayer != null && IsInstanceValid(_networkLayer)) RefreshNetworkGraph();
                    if (_leaderboardLayer != null && IsInstanceValid(_leaderboardLayer)) RefreshLeaderboardData();
                }
            }
        });
    }

    private void DrawCoordinatesOverlay()
    {
        Font defaultFont = ThemeDB.FallbackFont;
        int fontSize = 25; 
        float densityMultiplier = 5.0f; 
        
        Color textColor = new Color(0.0f, 0.0f, 0.0f, 0.95f);
        Color outlineColor = new Color(1.0f, 1.0f, 1.0f, 0.8f); 
        int outlineSize = 10; 

        for (int x = 0; x < GridSize.X; x++)
        {
            string colName = GetColumnName(x);
            for (int y = 0; y < GridSize.Y; y++)
            {
                string coordText = $"{colName}{y + 1}"; 
                Vector2 tileCenter = MapToLocal(new Vector2I(x, y));

                Vector2 stringSize = defaultFont.GetStringSize(coordText, HorizontalAlignment.Left, -1, fontSize);
                Vector2 drawPos = (tileCenter * densityMultiplier) + new Vector2(-stringSize.X / 2, stringSize.Y / 3);
                
                _coordinateOverlay.DrawStringOutline(defaultFont, drawPos, coordText, HorizontalAlignment.Left, -1, fontSize, outlineSize, outlineColor);
                _coordinateOverlay.DrawString(defaultFont, drawPos, coordText, HorizontalAlignment.Left, -1, fontSize, textColor);
            }
        }
    }

    // --- PROCEDURAL UI GENERATION ---
    private void InitializeProceduralUI()
    {
        // Cursor setup (Mouse collision disabled)
        _cursorRect = new ReferenceRect { 
            BorderColor = new Color(0.2f, 0.8f, 1.0f), 
            BorderWidth = 3.0f, 
            EditorOnly = false, 
            Size = (Vector2)TileSet.TileSize, 
            ZIndex = 10,
            MouseFilter = Control.MouseFilterEnum.Ignore 
        };
        
        _toolIcon = new TextureRect { 
            Texture = GD.Load<Texture2D>("res://Resource/Icons/Pencil.png"), 
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = (Vector2)TileSet.TileSize,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        
        _cursorRect.AddChild(_toolIcon);
        AddChild(_cursorRect);

        CanvasLayer hudLayer = new CanvasLayer();
        HBoxContainer toolBar = new HBoxContainer();
        toolBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        toolBar.Position = new Vector2(0, -60);
        toolBar.Alignment = BoxContainer.AlignmentMode.Center;
        toolBar.AddThemeConstantOverride("separation", 20);

        // 1. Tool Logic Initialization
        Button btnBrush = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Pencil.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(65, 60) };
        Button btnShovel = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Shovel.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(65, 60) };
        Button btnPickaxe = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Pickaxe.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(65, 60) };
        Button btnEyedropper = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Eyedropper.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(65, 60) };

        // 2. System Buttons
        Button btnObjectives = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Quests.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(60, 45) };
        Button btnDownloadMap = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Save.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(60, 45) };
        
        // 3. Event Assignment
        btnBrush.Pressed += () => UpdateTool(ActionType.Paintbrush, "res://Resource/Icons/Pencil.png", new Color(1, 1, 1));
        btnShovel.Pressed += () => UpdateTool(ActionType.Shovel, "res://Resource/Icons/Shovel.png", new Color(1, 1, 1));
        btnPickaxe.Pressed += () => UpdateTool(ActionType.Pickaxe, "res://Resource/Icons/Pickaxe.png", new Color(1, 1, 1));
        btnEyedropper.Pressed += () => UpdateTool(ActionType.Eyedropper, "res://Resource/Icons/Eyedropper.png", new Color(1, 1, 1));
        btnObjectives.Pressed += OpenObjectivesPanel;
        btnDownloadMap.Pressed += ExportHighResMap;

        // 4. Color Picker Binding
        _colorPickerBtn = new ColorPickerButton();
        _colorPickerBtn.CustomMinimumSize = new Vector2(65, 60);
        _colorPickerBtn.Color = CurrentPaintColor;
        _colorPickerBtn.ColorChanged += (Color newColor) => CurrentPaintColor = newColor;

        // 5. Camera & Coordinate Controls
        Button btnZoomIn = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Zoom+.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(50, 45) };
        Button btnZoomOut = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Zoom-.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(50, 45) };
        btnZoomIn.Pressed += () => AdjustZoom(1.2f); 
        btnZoomOut.Pressed += () => AdjustZoom(0.8f);

        Button btnToggleCords = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Pin.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(50, 45) };
        btnToggleCords.Pressed += () => 
        {
            _coordinateOverlay.Visible = !_coordinateOverlay.Visible;
            btnToggleCords.Modulate = _coordinateOverlay.Visible ? new Color(1, 1, 1) : new Color(0.4f, 0.4f, 0.4f);
        };

        // 6. Global Menus (Leaderboard & Network)
        Button btnLeaderboard = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Trofy.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(60, 45) };
        btnLeaderboard.Pressed += OpenLeaderboardPanel;

        _btnOpenNetwork = new Button { Icon = GD.Load<Texture2D>("res://Resource/Icons/Group.png"), ExpandIcon = true, CustomMinimumSize = new Vector2(60, 45) };
        _btnOpenNetwork.Pressed += OpenReinforcementsNetwork;

        // 7. Energy System UI
        HBoxContainer energyBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        energyBox.AddThemeConstantOverride("separation", 8);

        TextureRect energyIcon = new TextureRect { 
            Texture = GD.Load<Texture2D>("res://Resource/Icons/Energy.png"), 
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, 
            CustomMinimumSize = new Vector2(30, 30),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };

        _actionPointsLabel = new Label { Text = _currentActionPoints.ToString() };
        _actionPointsLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); 
        _actionPointsLabel.AddThemeFontSizeOverride("font_size", 20);

        energyBox.AddChild(energyIcon);
        energyBox.AddChild(_actionPointsLabel);

        // 8. Toolbar Assembly
        toolBar.AddChild(btnBrush);
        toolBar.AddChild(btnShovel);
        toolBar.AddChild(btnPickaxe);
        toolBar.AddChild(btnEyedropper); 
        toolBar.AddChild(energyBox); 
        toolBar.AddChild(_colorPickerBtn);
        toolBar.AddChild(btnZoomIn);
        toolBar.AddChild(btnZoomOut);
        toolBar.AddChild(btnToggleCords);
        toolBar.AddChild(btnDownloadMap);
        toolBar.AddChild(btnLeaderboard); 
        toolBar.AddChild(_btnOpenNetwork); 
        toolBar.AddChild(btnObjectives); 
        
        hudLayer.AddChild(toolBar);

        // --- UPPER HUD PROGRESS BARS ---
        _totalTiles = GridSize.X * GridSize.Y;

        VBoxContainer topBarsContainer = new VBoxContainer();
        topBarsContainer.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        topBarsContainer.Position = new Vector2(0, 20); 
        topBarsContainer.Alignment = BoxContainer.AlignmentMode.Center;
        topBarsContainer.AddThemeConstantOverride("separation", 15);

        StyleBoxFlat bgStyle = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        StyleBoxFlat blueFill = new StyleBoxFlat { BgColor = new Color(0.1f, 0.4f, 0.8f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        StyleBoxFlat greenFill = new StyleBoxFlat { BgColor = new Color(0.2f, 0.7f, 0.3f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };

        // Map Cleanliness Bar
        _cleanProgressBar = new ProgressBar { CustomMinimumSize = new Vector2(600, 30), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, ShowPercentage = false, MaxValue = _totalTiles };
        _cleanProgressBar.AddThemeStyleboxOverride("background", bgStyle);
        _cleanProgressBar.AddThemeStyleboxOverride("fill", blueFill);
        
        Label cleanLabel = new Label { Name = "CustomLabel", Text = "Map Cleanliness: 0%", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        cleanLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cleanProgressBar.AddChild(cleanLabel);

        // Donation Sync Bar (Tiltify)
        _donationProgressBar = new ProgressBar { CustomMinimumSize = new Vector2(600, 30), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, ShowPercentage = false };
        _donationProgressBar.AddThemeStyleboxOverride("background", bgStyle);
        _donationProgressBar.AddThemeStyleboxOverride("fill", greenFill);
        
        _donationProgressBar.MaxValue = 50000; 
        _donationProgressBar.Value = 1750;     
        
        Label donationLabel = new Label { Name = "CustomLabel", Text = $"Earthquake Relief: ${_donationProgressBar.Value:N0} / ${_donationProgressBar.MaxValue:N0}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        donationLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _donationProgressBar.AddChild(donationLabel);

        topBarsContainer.AddChild(_cleanProgressBar);

        HBoxContainer donationContainer = new HBoxContainer();
        donationContainer.Alignment = BoxContainer.AlignmentMode.Center;
        donationContainer.AddThemeConstantOverride("separation", 15);
        donationContainer.AddChild(_donationProgressBar); 

        Button btnDonate = new Button { Text = GetText("DONATE_BTN"), CustomMinimumSize = new Vector2(180, 30) };
        btnDonate.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f)); 
        btnDonate.Pressed += () => OS.ShellOpen("https://tiltify.com/@baelistick/global-game-jam-venezuela-earthquake-relief-fundraiser?origin=dashboard");
        
        donationContainer.AddChild(btnDonate); 
        topBarsContainer.AddChild(donationContainer); 

        // Online Population Header
        HBoxContainer onlineBox = new HBoxContainer();
        onlineBox.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        onlineBox.Position = new Vector2(-160, 25); 
        onlineBox.Alignment = BoxContainer.AlignmentMode.End;
        onlineBox.AddThemeConstantOverride("separation", 10);

        TextureRect onlineIcon = new TextureRect { 
            Texture = GD.Load<Texture2D>("res://Resource/Icons/Group.png"), 
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, 
            CustomMinimumSize = new Vector2(30, 30),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        onlineIcon.Modulate = new Color(0.2f, 0.9f, 0.2f); 

        _onlineCountLabel = new Label { Text = "0 Online" };
        _onlineCountLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.9f, 0.2f));
        _onlineCountLabel.AddThemeFontSizeOverride("font_size", 20);

        onlineBox.AddChild(onlineIcon);
        onlineBox.AddChild(_onlineCountLabel);
        hudLayer.AddChild(onlineBox);

        AddChild(hudLayer);
    }

    private void UpdateEnergyUI()
    {
        if (_actionPointsLabel != null)
        {
            _actionPointsLabel.Text = _currentActionPoints.ToString();
            
            if (_currentActionPoints > 0)
                _actionPointsLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); 
            else
                _actionPointsLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f)); 
        }

        // Discord RPC Sync
        DiscordManager.Instance?.UpdatePresence(
            "Limpiando el ecosistema", 
            $"Energía Restante: {_currentActionPoints}"
        );
    }

    private void UpdateTool(ActionType tool, string iconPath, Color color)
    {
        CurrentTool = tool;
        _toolIcon.Texture = GD.Load<Texture2D>(iconPath);
        _toolIcon.Modulate = color;
    }

    /// <summary>
    /// Generates the deterministic noise map using the agreed-upon seed.
    /// Acts as the environmental baseline before user changes are downloaded.
    /// </summary>
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

        CalculateInitialCleanliness();
    }

    // --- ENVIRONMENTAL EVENT SYSTEM ---
    private void SpawnRandomDebris()
    {
        if (!_isPlayerAuthenticated) return;

        bool isLayer1Sealed = CheckLayerSealed(0); 
        bool isLayer2Sealed = CheckLayerSealed(1); 

        // Ring validation & UI triggers
        if (isLayer1Sealed && !_wasRing1Sealed)
        {
            _wasRing1Sealed = true;
            ShowFloatingMessage(GetText("MSG_RING1"), new Color(0.2f, 0.9f, 0.2f));
        }
        if (isLayer2Sealed && !_wasRing2Sealed)
        {
            _wasRing2Sealed = true;
            ShowFloatingMessage(GetText("MSG_RING2"), new Color(0.0f, 0.8f, 0.6f));
        }

        if (isLayer1Sealed && isLayer2Sealed)
        {
            GD.Print("[SYSTEM] All defense rings sealed. Spawning halted.");
            return; 
        }

        int debrisToSpawn = 45; 
        int spawned = 0;
        int maxAttempts = 200;  
        int attempts = 0;

        bool canSpawnStone = !isLayer1Sealed;
        bool canSpawnDirt = !isLayer2Sealed;

        while (spawned < debrisToSpawn && attempts < maxAttempts)
        {
            attempts++;
            
            int rx = _random.Next(0, GridSize.X);
            int ry = _random.Next(0, GridSize.Y);
            Vector2I randomPos = new Vector2I(rx, ry);

            // Strict spawn constraint: only target unpainted canvas
            if (GetTileType(randomPos) == TileType.Canvas && !_paintedPixels.ContainsKey(randomPos))
            {
                TileType newDebris = TileType.Canvas;

                if (canSpawnStone && canSpawnDirt) {
                    newDebris = _random.NextDouble() > 0.5 ? TileType.Stone : TileType.Dirt;
                } else if (canSpawnStone && !canSpawnDirt) {
                    newDebris = TileType.Stone;
                } else if (!canSpawnStone && canSpawnDirt) {
                    newDebris = TileType.Dirt;
                } else {
                    break; 
                }
                
                UpdateTileLocal(randomPos, newDebris, null);
                spawned++;
            }
        }
    }

    private bool CheckLayerSealed(int layer)
    {
        int minX = layer;
        int minY = layer;
        int maxX = GridSize.X - 1 - layer;
        int maxY = GridSize.Y - 1 - layer;

        if (maxX <= minX || maxY <= minY) return false;

        for (int x = minX; x <= maxX; x++)
        {
            if (!IsPixelSealed(new Vector2I(x, minY))) { LogBreach(layer, new Vector2I(x, minY), "Top"); return false; }
            if (!IsPixelSealed(new Vector2I(x, maxY))) { LogBreach(layer, new Vector2I(x, maxY), "Bottom"); return false; }
        }

        for (int y = minY + 1; y < maxY; y++)
        {
            if (!IsPixelSealed(new Vector2I(minX, y))) { LogBreach(layer, new Vector2I(minX, y), "Left"); return false; }
            if (!IsPixelSealed(new Vector2I(maxX, y))) { LogBreach(layer, new Vector2I(maxX, y), "Right"); return false; }
        }

        return true; 
    }

    private bool IsPixelSealed(Vector2I pos)
    {
        return _paintedPixels.ContainsKey(pos);
    }

    private void LogBreach(int layer, Vector2I pos, string sector)
    {
        GD.Print($"[ALERT] Layer {layer} Breach | Sector {sector} | Coord: {pos} | Status: UNPAINTED");
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

    private void AdjustZoom(float factor)
    {
        _currentZoom *= factor;

        if (_currentZoom < _minZoom) _currentZoom = _minZoom;
        if (_currentZoom > _maxZoom) _currentZoom = _maxZoom;

        _devCamera.Zoom = new Vector2(_currentZoom, _currentZoom);
    }

    private void DrawPaintOverlay()
    {
        Vector2 tileSize = (Vector2)TileSet.TileSize;
        
        foreach(var pixel in _paintedPixels)
        {
            _paintOverlay.DrawRect(new Rect2(pixel.Key * tileSize, tileSize), pixel.Value);
        }

        // Draws the structural grid above the map layer
        Color gridColor = new Color(0.8f, 0.8f, 0.8f, 0.3f); 
        for (int x = 0; x <= GridSize.X; x++) _paintOverlay.DrawLine(new Vector2(x * tileSize.X, 0), new Vector2(x * tileSize.X, GridSize.Y * tileSize.Y), gridColor);
        for (int y = 0; y <= GridSize.Y; y++) _paintOverlay.DrawLine(new Vector2(0, y * tileSize.Y), new Vector2(GridSize.X * tileSize.X, y * tileSize.Y), gridColor);
    }

    // --- USER INPUT LOGIC ---
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isPlayerAuthenticated) return;

        // Keyboard hotkeys
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == Key.Key1) UpdateTool(ActionType.Paintbrush, "res://Resource/Icons/Pencil.png", new Color(1, 1, 1));
            if (keyEvent.Keycode == Key.Key2) UpdateTool(ActionType.Shovel, "res://Resource/Icons/Shovel.png", new Color(1, 1, 1));
            if (keyEvent.Keycode == Key.Key3) UpdateTool(ActionType.Pickaxe, "res://Resource/Icons/Pickaxe.png", new Color(1, 1, 1));
            return;
        }

        // Mouse Button Interactions
        if (@event is InputEventMouseButton mouseBtnEvent)
        {
            // Drag Navigation triggers
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

            // Left Click Grid Interaction
            if (mouseBtnEvent.Pressed && mouseBtnEvent.ButtonIndex == MouseButton.Left)
            {
                if (_currentActionPoints > 0)
                {
                    ProcessMapInteraction(GetLocalMousePosition());
                }
                else
                {
                    GD.PrintErr("[SYSTEM] Energy depleted. Waiting for recharge.");
                    UpdateEnergyUI(); 
                }
            }
        }

        // Mouse Motion Panning
        if (@event is InputEventMouseMotion mouseMotionEvent)
        {
            if (_isDragging)
            {
                Vector2 delta = mouseMotionEvent.Position - _lastMousePosition;
                _devCamera.Position -= delta * (1.0f / _devCamera.Zoom.X);
                _lastMousePosition = mouseMotionEvent.Position;
            }
        }

        // Multi-touch Pinch-to-Zoom logic
        if (@event is InputEventScreenTouch touchEvent)
        {
            if (touchEvent.Pressed)
            {
                _activeTouches[touchEvent.Index] = touchEvent.Position;
            }
            else
            {
                _activeTouches.Remove(touchEvent.Index);
                if (_activeTouches.Count < 2) _lastPinchDistance = 0f; 
            }
        }

        if (@event is InputEventScreenDrag dragEventMobile)
        {
            _activeTouches[dragEventMobile.Index] = dragEventMobile.Position;

            if (_activeTouches.Count == 2)
            {
                var enumerator = _activeTouches.GetEnumerator();
                enumerator.MoveNext();
                Vector2 pos1 = enumerator.Current.Value;
                enumerator.MoveNext();
                Vector2 pos2 = enumerator.Current.Value;

                float currentDistance = pos1.DistanceTo(pos2);

                if (_lastPinchDistance > 0)
                {
                    float pinchFactor = currentDistance / _lastPinchDistance;
                    AdjustZoom(pinchFactor);
                }

                _lastPinchDistance = currentDistance;
                return; 
            }
            else if (_activeTouches.Count == 1)
            {
                if (_devCamera != null)
                {
                    _devCamera.Position -= dragEventMobile.Relative * (1.0f / _devCamera.Zoom.X);
                }
                return;
            }
        }
    }

    private void ProcessMapInteraction(Vector2 localPosition)
    {
        Vector2I mapPosition = LocalToMap(localPosition);

        if (IsWithinBounds(mapPosition))
        {
            // Execute Action. If valid, deduct action point
            if (ExecuteAction(mapPosition))
            {
                _currentActionPoints--;
                UpdateEnergyUI();
                SupabaseManager.Instance.ConsumirEnergia(_activePlayerNick); 
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
                    UpdateTileLocal(pos, TileType.Canvas, null);
                    SpawnImpactEffects(pos, ActionType.Pickaxe); 
                    SupabaseManager.Instance.IncrementUserStat(_activePlayerNick, "piedra");
                    return true; 
                }
                break;

            case ActionType.Shovel:
                if (currentTile == TileType.Dirt) 
                { 
                    UpdateTileLocal(pos, TileType.Canvas, null);
                    SpawnImpactEffects(pos, ActionType.Shovel); 
                    SupabaseManager.Instance.IncrementUserStat(_activePlayerNick, "tierra");
                    return true; 
                }
                break;

            case ActionType.Paintbrush:
                if (currentTile == TileType.Canvas) 
                { 
                    string hexColor = "#" + CurrentPaintColor.ToHtml(false);
                    
                    bool isMyTile = _myPixels.Contains(pos);
                    
                    UpdateTileLocal(pos, TileType.Canvas, hexColor, _activePlayerNick); 
                    SpawnImpactEffects(pos, ActionType.Paintbrush, hexColor); 
                    
                    if (isMyTile)
                    {
                        return false; // Energy exemption for overriding owned tiles
                    }
                    else
                    {
                        SupabaseManager.Instance.IncrementUserStat(_activePlayerNick, "pintura");
                        return true; 
                    }
                }
                break;

            case ActionType.Eyedropper:
                if (_paintedPixels.ContainsKey(pos))
                {
                    CurrentPaintColor = _paintedPixels[pos];
                    _colorPickerBtn.Color = CurrentPaintColor; 
                    
                    UpdateTool(ActionType.Paintbrush, "res://Resource/Icons/Pencil.png", new Color(1, 1, 1)); 
                }
                else if (currentTile == TileType.Canvas)
                {
                    CurrentPaintColor = new Color(1, 1, 1); 
                    _colorPickerBtn.Color = CurrentPaintColor;
                    
                    UpdateTool(ActionType.Paintbrush, "res://Resource/Icons/Pencil.png", new Color(1, 1, 1));
                }
                return false; 
        }
        
        return false;
    }

    private void UpdateTileLocal(Vector2I pos, TileType type, string hexColor, string ownerNick = null)
    {
        TileType oldType = GetTileType(pos); 

        SetTile(pos, type);
        
        if (hexColor != null) 
        {
            _paintedPixels[pos] = new Color(hexColor);
            if (ownerNick == _activePlayerNick) _myPixels.Add(pos);
        }
        else 
        {
            _paintedPixels.Remove(pos);
            _myPixels.Remove(pos); 
        }
        
        _paintOverlay.QueueRedraw(); 
        SupabaseManager.Instance.SavePixel(pos.X, pos.Y, (int)type, hexColor, ownerNick);

        // Performance Optimization: Arithmetic tally rather than full grid scans
        if (oldType != TileType.Canvas && type == TileType.Canvas) 
            _currentCleanCount++; 
        else if (oldType == TileType.Canvas && type != TileType.Canvas) 
            _currentCleanCount--;

        RefreshCleanlinessUI(); 
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

    // --- METRICS & GOALS ENGINE ---
    private void CalculateInitialCleanliness()
    {
        _currentCleanCount = 0;
        for (int x = 0; x < GridSize.X; x++)
        {
            for (int y = 0; y < GridSize.Y; y++)
            {
                if (GetTileType(new Vector2I(x, y)) == TileType.Canvas)
                {
                    _currentCleanCount++;
                }
            }
        }
        RefreshCleanlinessUI();
    }

    private void RefreshCleanlinessUI()
    {
        if (_cleanProgressBar == null) return;
        
        _cleanProgressBar.Value = _currentCleanCount;
        float percentage = ((float)_currentCleanCount / _totalTiles) * 100.0f;
        string prefix = GetText("CLEANLINESS_PREFIX");
        _cleanProgressBar.GetNode<Label>("CustomLabel").Text = $"{prefix}{percentage:0.0}%";
    }

    // Phase 1: Tiltify Credentials Negotiation
    private void RequestTiltifyToken()
    {
        string url = "https://v5api.tiltify.com/oauth/token";
        string[] headers = new string[] { "Content-Type: application/json" };
        
        var authData = new Godot.Collections.Dictionary
        {
            { "client_id", _clientId },
            { "client_secret", _clientSecret },
            { "grant_type", "client_credentials" }
        };
        
        string jsonBody = Json.Stringify(authData);
        _tiltifyAuthRequest.Request(url, headers, HttpClient.Method.Post, jsonBody);
    }

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
                    RequestTiltifyData(); // Fire Phase 2
                }
            }
        }
        else
        {
            GD.PrintErr($"[OAUTH ERROR] Tiltify denied credentials. Code: {responseCode}");
        }
    }

    // Phase 2: Tiltify Campaign Data Search
    private void RequestTiltifyData()
    {
        if (string.IsNullOrEmpty(_tiltifyToken)) return;

        string url = $"https://v5api.tiltify.com/api/public/campaigns/{_tiltifyCampaignId}";
        string[] headers = new string[] {
            $"Authorization: Bearer {_tiltifyToken}",
            "Content-Type: application/json"
        };

        _tiltifyDataRequest.Request(url, headers, HttpClient.Method.Get);
    }

    private void OnTiltifyDataReceived(long result, long responseCode, string[] headers, byte[] body)
    {
        if (responseCode == 200) 
        {
            string jsonString = System.Text.Encoding.UTF8.GetString(body);
            Json json = new Json();
            Error error = json.Parse(jsonString);

            if (error == Error.Ok)
            {
                var data = (Godot.Collections.Dictionary)json.Data;
                var campaignData = (Godot.Collections.Dictionary)data["data"];
                
                var amountRaisedObj = (Godot.Collections.Dictionary)campaignData["amount_raised"];
                var goalObj = (Godot.Collections.Dictionary)campaignData["goal"];

                float amountRaised = amountRaisedObj["value"].AsSingle();
                float goal = goalObj["value"].AsSingle();

                if (_lastKnownDonationAmount != -1f && amountRaised > _lastKnownDonationAmount)
                {
                    float difference = amountRaised - _lastKnownDonationAmount;
                    ShowFloatingMessage($"DONATION RECEIVED! (+ ${difference:N0}) Thank you! 🇻🇪", new Color(0.2f, 0.9f, 0.4f));
                }
                _lastKnownDonationAmount = amountRaised; 

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
            }
        }
    }

    // --- AUTHENTICATION REGISTRATION LAYER ---
    private void InitializeAuthUI()
    {
        _authLayer = new CanvasLayer();
        _authLayer.Layer = 100; 

        _authScreen = new Control();
        _authScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        ColorRect isolationBg = new ColorRect();
        isolationBg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        isolationBg.Color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        _authScreen.AddChild(isolationBg);

        CenterContainer centerWrapper = new CenterContainer();
        centerWrapper.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _authScreen.AddChild(centerWrapper);

        VBoxContainer formContainer = new VBoxContainer();
        formContainer.CustomMinimumSize = new Vector2(400, 300);
        formContainer.Alignment = BoxContainer.AlignmentMode.Center;
        formContainer.AddThemeConstantOverride("separation", 20);
        centerWrapper.AddChild(formContainer);

        _authTitleLabel = new Label { Text = GetText("AUTH_REG_TITLE"), HorizontalAlignment = HorizontalAlignment.Center };
        _authTitleLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 0.2f)); 
        formContainer.AddChild(_authTitleLabel);

        _nickInput = new LineEdit { PlaceholderText = GetText("NICK_PLACEHOLDER"), Alignment = HorizontalAlignment.Center };
        formContainer.AddChild(_nickInput);

        _passInput = new LineEdit { PlaceholderText = GetText("PASS_PLACEHOLDER"), Alignment = HorizontalAlignment.Center, Secret = true }; 
        formContainer.AddChild(_passInput);

        _countrySelector = new OptionButton();
        _countrySelector.Alignment = HorizontalAlignment.Center;

        Font emojiFont = GD.Load<Font>("res://Resource/Fonts/NotoColorEmoji.ttf");
        _countrySelector.AddThemeFontOverride("font", emojiFont);

        // International ISO standards
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
        _countrySelector.AddItem("🇩🇪 Alemania", 23);
        _countrySelector.AddItem("🇨🇳 China", 24);
        _countrySelector.AddItem("🇫🇷 Francia", 25);
        _countrySelector.AddItem("🇮🇹 Italia", 26);
        _countrySelector.AddItem("🇯🇵 Japón", 27);
        _countrySelector.AddItem("🇬🇧 Reino Unido", 28);
        _countrySelector.AddItem("🇷🇺 Rusia", 29);
        _countrySelector.AddItem("🇺🇳 Otra / Global", 30);

        formContainer.AddChild(_countrySelector);

        _recruiterInput = new LineEdit { PlaceholderText = GetText("RECRUITER_PLACEHOLDER"), Alignment = HorizontalAlignment.Center };
        formContainer.AddChild(_recruiterInput);

        _submitAuthBtn = new Button { Text = GetText("BTN_REGISTER"), CustomMinimumSize = new Vector2(0, 50) };
        _submitAuthBtn.Pressed += ProcessLoginAttempt;
        formContainer.AddChild(_submitAuthBtn);

        _toggleModeBtn = new Button { Text = GetText("TOGGLE_TO_LOGIN"), Flat = true };
        _toggleModeBtn.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f)); 
        _toggleModeBtn.Pressed += ToggleAuthMode;
        formContainer.AddChild(_toggleModeBtn);

        _authLayer.AddChild(_authScreen);
        AddChild(_authLayer);

        LoadCredentials();
    }

    private void ToggleAuthMode()
    {
        _isLoginMode = !_isLoginMode; 

        if (_isLoginMode)
        {
            _authTitleLabel.Text = GetText("AUTH_LOG_TITLE");
            _countrySelector.Hide(); 
            _recruiterInput.Hide();  
            _submitAuthBtn.Text = GetText("BTN_LOGIN");
            _toggleModeBtn.Text = GetText("TOGGLE_TO_REG");
        }
        else
        {
            _authTitleLabel.Text = GetText("AUTH_REG_TITLE");
            _countrySelector.Show(); 
            _recruiterInput.Show();  
            _submitAuthBtn.Text = GetText("BTN_REGISTER");
            _toggleModeBtn.Text = GetText("TOGGLE_TO_LOGIN");
        }
    }

    private void ProcessLoginAttempt()
    {
        string nick = _nickInput.Text.Trim();
        string pass = _passInput.Text.Trim();

        if (string.IsNullOrEmpty(nick) || string.IsNullOrEmpty(pass)) return;

        _tempPassword = pass; 
        string safePasswordHash = HashPassword(pass);

        _submitAuthBtn.Disabled = true;
        _toggleModeBtn.Disabled = true;
        _submitAuthBtn.Text = "AUTHENTICATING...";

        if (_isLoginMode)
        {
            SupabaseManager.Instance.LoginUser(nick, safePasswordHash, OnAuthenticationResult);
        }
        else
        {
            string country = _countrySelector.GetItemText(_countrySelector.Selected);
            string recruiter = _recruiterInput.Text.Trim(); 

            SupabaseManager.Instance.RegisterNewUser(nick, safePasswordHash, country, recruiter, (success) => 
            {
                if (success && !string.IsNullOrEmpty(recruiter))
                {
                    SupabaseManager.Instance.ActivarBonoNodo(recruiter);
                    _currentActionPoints += 50; 
                }
                OnAuthenticationResult(success);
            });
        }
    }

    private void OnAuthenticationResult(bool success)
    {
        if (success)
        {
            string activeNickname = _nickInput.Text.Trim(); 
            _activePlayerNick = activeNickname; 

            SaveCredentials(_activePlayerNick, _tempPassword);
            
            _isPlayerAuthenticated = true;
            _authLayer.QueueFree(); 
            
            SupabaseManager.Instance.GetConnections(_activePlayerNick, (connectionsData) => 
            {
                if (connectionsData != null && connectionsData.Count > 0)
                {
                    GD.Print($"[NETWORK] Scan Complete: {connectionsData.Count} direct connections detected.");
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
            _submitAuthBtn.Disabled = false;
            _toggleModeBtn.Disabled = false;
            
            if (_isLoginMode)
                _submitAuthBtn.Text = "[ ERROR: INVALID CREDENTIALS ]";
            else
                _submitAuthBtn.Text = "[ ERROR: NICKNAME UNAVAILABLE ]";
                
            _submitAuthBtn.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f)); 
        }
    }

    // Security Protocol (SHA-256)
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

    private const string CREDENTIALS_PATH = "user://operativo.cfg";
    private string _tempPassword = ""; 

    private void SaveCredentials(string nick, string pass)
    {
        ConfigFile config = new ConfigFile();
        config.SetValue("Auth", "Nick", nick);
        config.SetValue("Auth", "Pass", pass);
        config.Save(CREDENTIALS_PATH);
    }

    private void LoadCredentials()
    {
        ConfigFile config = new ConfigFile();
        if (config.Load(CREDENTIALS_PATH) == Error.Ok)
        {
            _nickInput.Text = (string)config.GetValue("Auth", "Nick", "");
            _passInput.Text = (string)config.GetValue("Auth", "Pass", "");
        }
    }

    // --- TACTICAL COORDINATES CONVERTER ---
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

    // --- NETWORK GRAPH UI ENGINE ---
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

        Button btnClose = new Button { Text = "[ X ]", CustomMinimumSize = new Vector2(150, 40) };
        btnClose.SetAnchorsPreset(Control.LayoutPreset.TopRight); 
        btnClose.Position = new Vector2(-170, 20); 
        btnClose.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
        btnClose.Pressed += () => _networkLayer.QueueFree();
        _networkLayer.AddChild(btnClose);

        AddChild(_networkLayer);
        RefreshNetworkGraph();
    }

    private void RefreshNetworkGraph()
    {
        if (_networkGraph == null || !IsInstanceValid(_networkGraph)) return;

        foreach (Node child in _networkGraph.GetChildren()) { if (child is GraphNode) child.QueueFree(); }
        _networkGraph.ClearConnections();

        SupabaseManager.Instance.GetAllUsersForNetwork((allUsers) => 
        {
            if (!IsInstanceValid(_networkGraph) || allUsers == null) return;
            
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
            string mySafeName = _activePlayerNick.Replace(" ", ""); 

            if (!string.IsNullOrEmpty(myParentNick) && userMap.ContainsKey(myParentNick))
            {
                string safeParentName = myParentNick.Replace(" ", "");
                DrawNodeFromData(userMap[myParentNick], safeParentName, myParentNick, GetText("RANK_BOSS"), new Vector2(100, currentY));
                DrawNodeFromData(myData, mySafeName, _activePlayerNick, GetText("RANK_YOU"), new Vector2(500, currentY));
                _networkGraph.ConnectNode(safeParentName, 0, mySafeName, 0);
            }
            else
            {
                DrawNodeFromData(myData, mySafeName, _activePlayerNick, GetText("RANK_ROOT"), new Vector2(100, currentY));
            }

            DrawFractalChildren(userMap, _activePlayerNick, mySafeName, currentY + 200);
        });
    }

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
                
                GraphNode parentNode = _networkGraph.GetNodeOrNull<GraphNode>(safeTargetName);
                float parentX = parentNode != null ? parentNode.PositionOffset.X : 100;
                
                Vector2 childPos = new Vector2(parentX + 400, currentY);
                DrawNodeFromData(childData, safeChildName, childNick, GetText("RANK_RECRUIT"), childPos);
                
                _networkGraph.ConnectNode(safeTargetName, 0, safeChildName, 0);
                
                int nextY = DrawFractalChildren(userMap, childNick, safeChildName, currentY);
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

    private GraphNode CreatePlayerNode(string idName, string nick, string country, string rank, int blocks, int tierra, int piedra, int pintura, Vector2 position)
    {
        GraphNode node = new GraphNode();
        node.Name = idName;
        node.Title = $"[{rank}] {nick}";
        node.PositionOffset = position; 
        node.SetSlot(0, true, 0, new Color(0.2f, 0.8f, 0.2f), true, 0, new Color(0.2f, 0.8f, 0.2f));

        VBoxContainer box = new VBoxContainer();
        box.Name = "DataContainer"; 
        node.AddChild(box);

        string isoCode = "un"; 
        string cleanCountry = "Otra / Global";

        foreach (var pair in _countryToIso)
        {
            if (country.Contains(pair.Key))
            {
                isoCode = pair.Value; 
                cleanCountry = pair.Key; 
                break;
            }
        }

        HBoxContainer headerBox = new HBoxContainer();
        headerBox.Alignment = BoxContainer.AlignmentMode.Begin; 
        headerBox.AddThemeConstantOverride("separation", 8);

        TextureRect flagIcon = new TextureRect();
        flagIcon.CustomMinimumSize = new Vector2(24, 24); 
        flagIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        flagIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        
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

        HSeparator separator = new HSeparator();
        separator.AddThemeConstantOverride("separation", 10);
        box.AddChild(separator);

        Label statsLabel = new Label();
        statsLabel.Name = "StatsLabel";
        statsLabel.Text = string.Format(GetText("STATS_BLOCKS_FORMAT"), blocks, tierra, piedra, pintura);
        statsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        box.AddChild(statsLabel);

        return node;
    }

    // --- OBJECTIVES & METRICS UI ---
    private void OpenObjectivesPanel()
    {
        CanvasLayer objLayer = new CanvasLayer { Layer = 95 }; 

        ColorRect bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.05f, 0.98f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        objLayer.AddChild(bg);

        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.AddChild(center);

        VBoxContainer box = new VBoxContainer { CustomMinimumSize = new Vector2(600, 500), Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 25);
        center.AddChild(box);

        Label title = new Label { Text = _isEnglish ? "GLOBAL OBJECTIVES SYSTEM" : "SISTEMA DE OBJETIVOS GLOBALES", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f)); 
        box.AddChild(title);

        int totalTiles = GridSize.X * GridSize.Y;
        int ring0Max = GetRingTotal(0);
        int ring0Cur = GetRingSealedCount(0);
        int ring1Max = GetRingTotal(1);
        int ring1Cur = GetRingSealedCount(1);
        int currentDirt = 0;
        int currentStone = 0;
        
        for (int x = 0; x < GridSize.X; x++) {
            for (int y = 0; y < GridSize.Y; y++) {
                TileType t = GetTileType(new Vector2I(x, y));
                if (t == TileType.Dirt) currentDirt++;
                else if (t == TileType.Stone) currentStone++;
            }
        }

        box.AddChild(CreateObjectiveBar(GetText("OBJ_1"), ring0Cur, ring0Max, new Color(0.2f, 0.8f, 1.0f)));
        box.AddChild(CreateObjectiveBar(GetText("OBJ_2"), ring1Cur, ring1Max, new Color(0.2f, 0.8f, 1.0f)));
        box.AddChild(CreateObjectiveBar(GetText("OBJ_3"), totalTiles - currentDirt, totalTiles, new Color(0.6f, 0.4f, 0.2f)));
        box.AddChild(CreateObjectiveBar(GetText("OBJ_4"), totalTiles - currentStone, totalTiles, new Color(0.5f, 0.5f, 0.5f)));
        box.AddChild(CreateObjectiveBar(GetText("OBJ_5"), _paintedPixels.Count, totalTiles, new Color(0.8f, 0.2f, 0.6f)));

        Button btnClose = new Button { Text = _isEnglish ? "[ RETURN TO TERRAIN ]" : "[ VOLVER AL TERRENO ]", CustomMinimumSize = new Vector2(250, 40), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
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
        string statsTemplate = GetText("STATS_FORMAT");
        Label lblStats = new Label { Text = string.Format(statsTemplate, missing, current), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        
        bar.AddChild(lblStats);
        container.AddChild(bar);
        
        return container;
    }

    private int GetRingTotal(int layer)
    {
        int w = GridSize.X - (layer * 2);
        int h = GridSize.Y - (layer * 2);
        if (w <= 0 || h <= 0) return 0;
        return (w * 2) + (h * 2) - 4; 
    }

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

    // --- VISUAL FEEDBACK (JUICE) ---
    private void SpawnImpactEffects(Vector2I mapPos, ActionType action, string hexColor = null)
    {
        Vector2 worldPos = MapToLocal(mapPos);

        CpuParticles2D vfx = new CpuParticles2D();
        vfx.Position = worldPos;
        vfx.Emitting = true;
        vfx.OneShot = true;
        vfx.Explosiveness = 0.85f; 
        vfx.Lifetime = 0.6f;
        vfx.EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere;
        vfx.EmissionSphereRadius = 8f;
        vfx.Spread = 180f; 
        vfx.Gravity = new Vector2(0, 150f); 
        vfx.InitialVelocityMin = 30f;
        vfx.InitialVelocityMax = 80f;
        vfx.ScaleAmountMin = 2f;
        vfx.ScaleAmountMax = 4f;

        if (action == ActionType.Pickaxe)
        {
            vfx.Color = new Color(0.6f, 0.6f, 0.6f); 
            vfx.ScaleAmountMax = 6f; 
        }
        else if (action == ActionType.Shovel)
        {
            vfx.Color = new Color(0.4f, 0.25f, 0.1f); 
            vfx.Amount = 16; 
        }
        else if (action == ActionType.Paintbrush && hexColor != null)
        {
            vfx.Color = new Color(hexColor); 
            vfx.Gravity = new Vector2(0, 50f); 
            vfx.InitialVelocityMax = 60f;
        }

        AddChild(vfx);

        GetTree().CreateTimer(1.0f).Timeout += () => 
        {
            if (IsInstanceValid(vfx)) vfx.QueueFree();
        };
    }

    private void ShowFloatingMessage(string text, Color color)
    {
        CanvasLayer toastLayer = new CanvasLayer { Layer = 110 }; 

        Label msgLabel = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        msgLabel.AddThemeColorOverride("font_color", color);
        
        msgLabel.AddThemeFontSizeOverride("font_size", 28); 
        msgLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1)); 
        msgLabel.AddThemeConstantOverride("outline_size", 8); 
        
        msgLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        msgLabel.Position = new Vector2(-400, 150); 
        msgLabel.CustomMinimumSize = new Vector2(800, 50);

        CpuParticles2D uiSparks = new CpuParticles2D();
        uiSparks.Position = new Vector2(400, 25); 
        uiSparks.Emitting = true;
        uiSparks.Amount = 40;
        uiSparks.Lifetime = 1.2f;
        uiSparks.OneShot = true;
        uiSparks.Explosiveness = 0.7f; 
        uiSparks.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
        uiSparks.EmissionRectExtents = new Vector2(350, 15); 
        uiSparks.Gravity = new Vector2(0, 60f); 
        uiSparks.InitialVelocityMin = 30f;
        uiSparks.InitialVelocityMax = 70f;
        uiSparks.ScaleAmountMin = 2f;
        uiSparks.ScaleAmountMax = 6f;
        uiSparks.Color = color; 

        msgLabel.AddChild(uiSparks);
        toastLayer.AddChild(msgLabel);
        AddChild(toastLayer);

        Tween tween = GetTree().CreateTween();
        tween.TweenProperty(msgLabel, "position", msgLabel.Position + new Vector2(0, -90), 3.0f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(msgLabel, "modulate", new Color(1, 1, 1, 0), 3.0f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        
        tween.TweenCallback(Callable.From(() => toastLayer.QueueFree()));
    }

    private void OpenLeaderboardPanel()
    {
        if (_leaderboardLayer != null && IsInstanceValid(_leaderboardLayer)) return;

        _leaderboardLayer = new CanvasLayer { Layer = 92 }; 

        ColorRect bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f, 0.98f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _leaderboardLayer.AddChild(bg);

        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _leaderboardLayer.AddChild(center);

        VBoxContainer mainBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        mainBox.AddThemeConstantOverride("separation", 30);
        center.AddChild(mainBox);

        Label title = new Label { Text = _isEnglish ? "GLOBAL LEADERBOARD SYSTEM" : "SISTEMA DE CLASIFICACIÓN GLOBAL", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f));
        mainBox.AddChild(title);

        _leaderboardColumnsContainer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _leaderboardColumnsContainer.AddThemeConstantOverride("separation", 60);
        mainBox.AddChild(_leaderboardColumnsContainer);

        Button btnClose = new Button { Text = _isEnglish ? "[ RETURN TO ECOSYSTEM ]" : "[ VOLVER AL ECOSISTEMA ]", CustomMinimumSize = new Vector2(250, 40), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        btnClose.AddThemeColorOverride("font_color", new Color(0.9f, 0.2f, 0.2f));
        btnClose.Pressed += () => _leaderboardLayer.QueueFree();
        mainBox.AddChild(btnClose);

        AddChild(_leaderboardLayer);
        RefreshLeaderboardData();
    }

    private void RefreshLeaderboardData()
    {
        if (_leaderboardLayer == null || !IsInstanceValid(_leaderboardLayer)) return;

        SupabaseManager.Instance.GetAllUsersForNetwork((allUsers) => 
        {
            if (_leaderboardLayer == null || !IsInstanceValid(_leaderboardLayer) || allUsers == null) return;

            foreach (Node child in _leaderboardColumnsContainer.GetChildren()) child.QueueFree();

            System.Collections.Generic.List<Godot.Collections.Dictionary> usersList = new System.Collections.Generic.List<Godot.Collections.Dictionary>();
            foreach (var u in allUsers) usersList.Add(u.AsGodotDictionary());

            usersList.Sort((a, b) => b["piedra"].AsInt32().CompareTo(a["piedra"].AsInt32()));
            CreateLeaderboardColumn(GetText("TOP_STONE"), usersList, "piedra", new Color(0.6f, 0.6f, 0.6f));

            usersList.Sort((a, b) => b["tierra"].AsInt32().CompareTo(a["tierra"].AsInt32()));
            CreateLeaderboardColumn(GetText("TOP_DIRT"), usersList, "tierra", new Color(0.7f, 0.5f, 0.3f));

            usersList.Sort((a, b) => b["pintura"].AsInt32().CompareTo(a["pintura"].AsInt32()));
            CreateLeaderboardColumn(GetText("TOP_PAINT"), usersList, "pintura", new Color(0.8f, 0.2f, 0.6f));
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

        int count = Mathf.Min(10, sortedUsers.Count);
        int validRanks = 0;

        for (int i = 0; i < count; i++)
        {
            var user = sortedUsers[i];
            string nick = user["nickname"].AsString();
            int score = user.ContainsKey(statKey) ? user[statKey].AsInt32() : 0;
            
            if (score <= 0) continue; 
            validRanks++;

            Label row = new Label { Text = $"#{validRanks} | {nick} : {score} ptos" };
            
            if (nick == _activePlayerNick) 
                row.AddThemeColorOverride("font_color", new Color(0.2f, 0.9f, 0.2f)); 
            else
                row.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));

            col.AddChild(row);
        }

        if (validRanks == 0)
        {
            Label empty = new Label { Text = "No data...", HorizontalAlignment = HorizontalAlignment.Center };
            empty.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.4f));
            col.AddChild(empty);
        }

        _leaderboardColumnsContainer.AddChild(col);
    }

    // --- I18N DICTIONARY ---
    private readonly Dictionary<string, Dictionary<string, string>> _localizedText = new Dictionary<string, Dictionary<string, string>>()
    {
        { "WELCOME_TITLE", new Dictionary<string, string>{ { "es", "¡Bienvenido a Pico, Pala y Pincel!" }, { "en", "Welcome to Pick, Shovel and Paint!" } } },
        { "WELCOME_BODY1", new Dictionary<string, string>{ { "es", "Este es un juego Benéfico en Apoyo a los Afectados el 24 de Julio de 2026 en Venezuela. Compartir el juego con tus amigos ya es un enorme apoyo a esta noble causa. ¡Gracias por llegar hasta aquí de todo corazón! ❤" }, { "en", "This is a charity game supporting those affected on July 24, 2026 in Venezuela. Sharing the game with your friends is already a huge support for this noble cause. Thank you for making it this far from the bottom of our hearts! ❤" } } },
        { "WELCOME_CONTROLS", new Dictionary<string, string>{ { "es", "⛏ Con Pico (1) Quitas la Piedra\n⚒ Con Pala (2) Remueves Tierra\n🖌 Con Pincel (3) Pintas Casilla" }, { "en", "⛏ With Pickaxe (1) Clear Stone\n⚒ With Shovel (2) Remove Dirt\n🖌 With Paintbrush (3) Paint Tile" } } },
        { "WELCOME_BODY3", new Dictionary<string, string>{ { "es", "Coordina a tus amigos para hacer un dibujo, mensaje o ayudar a remover los escombros es una gran ayuda. Al terminar todos los objetivos el lienzo completo será publicado para descargar y compartir, demostrándole al mundo lo unidos que podemos estar cuando es necesario estar Juntos en los momentos más complicados." }, { "en", "Coordinating with your friends to make a drawing, a message, or helping clear the debris is a huge help. Upon completing all objectives, the complete canvas will be published for download and sharing, showing the world how united we can be when it matters most in difficult times." } } },
        { "WELCOME_BTN", new Dictionary<string, string>{ { "es", "ENTENDIDO - CONTINUAR" }, { "en", "UNDERSTOOD - CONTINUE" } } },
        { "BTN_DOWNLOAD_MAP", new Dictionary<string, string>{ { "es", "💾" }, { "en", "💾" } } },
        { "AUTH_REG_TITLE", new Dictionary<string, string>{ { "es", "REGISTRARSE" }, { "en", "REGISTER" } } },
        { "AUTH_LOG_TITLE", new Dictionary<string, string>{ { "es", "INICIAR SESION" }, { "en", "LOG IN" } } },
        { "NICK_PLACEHOLDER", new Dictionary<string, string>{ { "es", "INGRESE UN NICK" }, { "en", "ENTER A NICKNAME" } } },
        { "PASS_PLACEHOLDER", new Dictionary<string, string>{ { "es", "CONTRASEÑA" }, { "en", "PASSWORD" } } },
        { "RECRUITER_PLACEHOLDER", new Dictionary<string, string>{ { "es", "¿QUIÉN TE INVITÓ? (Opcional)" }, { "en", "WHO INVITED YOU? (Optional)" } } },
        { "BTN_REGISTER", new Dictionary<string, string>{ { "es", "CONFIRMAR REGISTRO" }, { "en", "CONFIRM REGISTRATION" } } },
        { "BTN_LOGIN", new Dictionary<string, string>{ { "es", "INICIAR" }, { "en", "LOG IN" } } },
        { "TOGGLE_TO_LOGIN", new Dictionary<string, string>{ { "es", "¿Ya tienes un usuario? Iniciar Sesión" }, { "en", "Already have an account? Log In" } } },
        { "TOGGLE_TO_REG", new Dictionary<string, string>{ { "es", "¿Eres Nuevo? Crear Cuenta" }, { "en", "Are you new? Create Account" } } },
        { "HUD_BRUSH", new Dictionary<string, string>{ { "es", "🖌" }, { "en", "🖌" } } },
        { "HUD_SHOVEL", new Dictionary<string, string>{ { "es", "⚒" }, { "en", "⚒" } } },
        { "HUD_PICKAXE", new Dictionary<string, string>{ { "es", "⛏" }, { "en", "⛏" } } },
        { "HUD_OBJECTIVES", new Dictionary<string, string>{ { "es", "📋" }, { "en", "📋" } } },
        { "HUD_REINFORCEMENTS", new Dictionary<string, string>{ { "es", "🎖" }, { "en", "🎖" } } },
        { "LEADERBOARD_TITLE", new Dictionary<string, string>{ { "es", "SISTEMA DE CLASIFICACIÓN GLOBAL" }, { "en", "GLOBAL LEADERBOARD SYSTEM" } } },
        { "OBJECTIVES_TITLE", new Dictionary<string, string>{ { "es", "SISTEMA DE OBJETIVOS GLOBALES" }, { "en", "GLOBAL OBJECTIVES SYSTEM" } } },
        { "BTN_RETURN_TERRAIN", new Dictionary<string, string>{ { "es", "[ VOLVER AL TERRENO ]" }, { "en", "[ RETURN TO TERRAIN ]" } } },
        { "BTN_RETURN_ECOSYSTEM", new Dictionary<string, string>{ { "es", "[ VOLVER AL ECOSISTEMA ]" }, { "en", "[ RETURN TO ECOSYSTEM ]" } } },
        { "TOP_STONE", new Dictionary<string, string>{ { "es", "⛏ MÁXIMA PIEDRA DESTRUIDA" }, { "en", "⛏ MAX STONE DESTROYED" } } },
        { "TOP_DIRT", new Dictionary<string, string>{ { "es", "⚒ MÁXIMA TIERRA REMOVIDA" }, { "en", "⚒ MAX DIRT REMOVED" } } },
        { "TOP_PAINT", new Dictionary<string, string>{ { "es", "🖌 MÁXIMO LIENZO PINTADO" }, { "en", "🖌 MAX CANVAS PAINTED" } } },
        { "OBJ_1", new Dictionary<string, string>{ { "es", "1. SELLAR ANILLO EXTERIOR (Inhibe aparición de Piedra)" }, { "en", "1. SEAL OUTER RING (Inhibits Stone spawn)" } } },
        { "OBJ_2", new Dictionary<string, string>{ { "es", "2. SELLAR ANILLO INTERIOR (Inhibe aparición de Tierra)" }, { "en", "2. SEAL INNER RING (Inhibits Dirt spawn)" } } },
        { "OBJ_3", new Dictionary<string, string>{ { "es", "3. LIMPIAR TODA LA TIERRA" }, { "en", "3. CLEAR ALL DIRT" } } },
        { "OBJ_4", new Dictionary<string, string>{ { "es", "4. LIMPIAR TODA LA PIEDRA" }, { "en", "4. CLEAR ALL STONE" } } },
        { "OBJ_5", new Dictionary<string, string>{ { "es", "5. PINTAR EL ECOSISTEMA (Cubrir todo el Lienzo)" }, { "en", "5. PAINT THE ECOSYSTEM (Cover entire Canvas)" } } },
        { "STATS_FORMAT", new Dictionary<string, string>{ { "es", "Faltantes: {0} / Completados: {1}" }, { "en", "Remaining: {0} / Completed: {1}" } } },
        { "CLEANLINESS_PREFIX", new Dictionary<string, string>{ { "es", "Limpieza del Mapa: " }, { "en", "Map Cleanliness: " } } },
        { "DONATE_BTN", new Dictionary<string, string>{ { "es", "❤️ DONATE" }, { "en", "❤️ DONATE" } } },
        { "EARTHQUAKE_RELIEF", new Dictionary<string, string>{ { "es", "Earthquake Relief: " }, { "en", "Earthquake Relief: " } } },
        { "MSG_RING1", new Dictionary<string, string>{ { "es", "¡ANILLO 1 SELLADO! Invasión de Piedra Bloqueada." }, { "en", "RING 1 SEALED! Stone Invasion Blocked." } } },
        { "MSG_RING2", new Dictionary<string, string>{ { "es", "¡ANILLO 2 SELLADO! Invasión de Tierra Bloqueada." }, { "en", "RING 2 SEALED! Dirt Invasion Blocked." } } },
        { "RANK_BOSS", new Dictionary<string, string>{ { "es", "Jefe de Nodo" }, { "en", "Node Boss" } } },
        { "RANK_YOU", new Dictionary<string, string>{ { "es", "Tú" }, { "en", "You" } } },
        { "RANK_ROOT", new Dictionary<string, string>{ { "es", "Tú (Nodo Raíz)" }, { "en", "You (Root Node)" } } },
        { "RANK_RECRUIT", new Dictionary<string, string>{ { "es", "Recluta" }, { "en", "Recruit" } } },
        { "STATS_BLOCKS_FORMAT", new Dictionary<string, string>{ { "es", "Bloques Totales: {0}\n[ Tierra: {1} | Piedra: {2} | Pintura: {3} ]" }, { "en", "Total Blocks: {0}\n[ Dirt: {1} | Stone: {2} | Paint: {3} ]" } } }
    };

    private string GetText(string key)
    {
        string lang = _isEnglish ? "en" : "es";
        if (_localizedText.ContainsKey(key) && _localizedText[key].ContainsKey(lang))
        {
            return _localizedText[key][lang];
        }
        return key; 
    }

    // --- HIGH-RESOLUTION MAP EXPORT ENGINE ---
    private void ExportHighResMap()
    {
        Vector2I tileSize = TileSet.TileSize;
        int width = GridSize.X * tileSize.X;
        int height = GridSize.Y * tileSize.Y;

        Image mapImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);

        Color canvasColor = new Color(1f, 1f, 1f, 1f); 
        Color dirtColor = new Color(0.4f, 0.25f, 0.1f, 1f); 
        Color stoneColor = new Color(0.6f, 0.6f, 0.6f, 1f); 

        for (int x = 0; x < GridSize.X; x++)
        {
            for (int y = 0; y < GridSize.Y; y++)
            {
                Vector2I gridPos = new Vector2I(x, y);
                Rect2I rect = new Rect2I(x * tileSize.X, y * tileSize.Y, tileSize.X, tileSize.Y);

                TileType type = GetTileType(gridPos);
                Color pixelColor = canvasColor;
                
                if (type == TileType.Dirt) pixelColor = dirtColor;
                else if (type == TileType.Stone) pixelColor = stoneColor;

                // Superimposed paint overrides the base block color
                if (_paintedPixels.ContainsKey(gridPos))
                {
                    pixelColor = _paintedPixels[gridPos];
                    pixelColor.A = 1.0f; 
                }

                mapImage.FillRect(rect, pixelColor);
            }
        }

        string timeStamp = Time.GetDatetimeStringFromSystem().Replace(":", "-");
        string fileName = $"PicoPalaPincel_Map_{timeStamp}.png";
        
        string filePath = $"user://{fileName}"; 
        mapImage.SavePng(filePath);

        string realPath = ProjectSettings.GlobalizePath(filePath);
        
        string msg = _isEnglish ? $"Map saved successfully to:\n{realPath}" : $"Mapa guardado exitosamente en:\n{realPath}";
        ShowFloatingMessage(msg, new Color(0.2f, 0.9f, 0.2f));
        GD.Print($"[SYSTEM] High resolution map exported to: {realPath}");
    }
}