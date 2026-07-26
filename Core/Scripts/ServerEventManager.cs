using Godot;
using System;
using System.Collections.Generic;
using Godot.Collections;

// Nivel de acoplamiento: Alto con GridManager y Supabase. Solo debe ejecutarse en el cliente Admin.
public partial class ServerEventManager : Node
{
    [Export] public bool IsAdminClient = true; // Apagar en la versión pública
    [Export] public float EventIntervalSeconds = 5.0f; // 1 Hora por defecto. (Cámbialo a 10f para probar)

    private Timer _eventTimer;
    private GridManager _grid;

    public override void _Ready()
    {
        if (!IsAdminClient) return;

        _grid = GetNode<GridManager>("../TileMap"); // Ajusta la ruta si es necesario

        _eventTimer = new Timer();
        _eventTimer.WaitTime = EventIntervalSeconds;
        _eventTimer.Autostart = true;
        _eventTimer.Timeout += OnServerEventTriggered;
        AddChild(_eventTimer);
        
        GD.Print("NEXUS-JAM: Módulo de Eventos de Servidor [ACTIVO].");
    }

    private void OnServerEventTriggered()
    {
        GD.Print("NEXUS-JAM: Iniciando evaluación de bordes...");
        SupabaseManager.Instance.FetchAllPixels((data) => EvaluateAndDropDebris(data));
    }

    private void EvaluateAndDropDebris(Godot.Collections.Array serverData)
    {
        int[,] gridState = new int[_grid.GridSize.X, _grid.GridSize.Y];
        
        // 1. Reconstruir estado actual en memoria local
        foreach (var item in serverData)
        {
            var dict = item.AsGodotDictionary();
            gridState[(int)dict["x"], (int)dict["y"]] = (int)dict["tile_type"];
        }

        // 2. Lógica Dura de Bordes
        bool layer1Sealed = CheckBorderLayer(gridState, 0); // Borde exterior (1 capa)
        bool layer2Sealed = CheckBorderLayer(gridState, 1); // Borde interior (2 capas)

        GD.Print($"Análisis de defensas - Capa 1 Sellada: {layer1Sealed} | Capa 2 Sellada: {layer2Sealed}");

        List<Vector2I> corruptedPixels = new List<Vector2I>();
        Random rand = new Random();

        // 3. Castigo del servidor si fallan las defensas
        for (int i = 0; i < 20; i++) // 20 bloques caen al azar por evento
        {
            int rx = rand.Next(2, _grid.GridSize.X - 2);
            int ry = rand.Next(2, _grid.GridSize.Y - 2);

            if (!layer1Sealed) 
            {
                // Cae Piedra (Tipo 2) si no hay ni 1 línea
                corruptedPixels.Add(new Vector2I(rx, ry));
                gridState[rx, ry] = 2; 
            }
            else if (!layer2Sealed)
            {
                // Cae Tierra (Tipo 1) si hay 1 línea pero no 2
                corruptedPixels.Add(new Vector2I(rx, ry));
                gridState[rx, ry] = 1;
            }
        }

        // 4. Enviar castigo masivo a Supabase
        foreach (Vector2I pos in corruptedPixels)
        {
            int tileType = gridState[pos.X, pos.Y];
            // Reutilizamos el método del Sprint 2 para inyectar la penalización
            SupabaseManager.Instance.SavePixel(pos.X, pos.Y, tileType, null);
            _grid.SetCell(0, pos, 0, new Vector2I(tileType, 0)); // Actualización visual admin
        }
    }

    // Algoritmo de escaneo de perímetro
    private bool CheckBorderLayer(int[,] grid, int depth)
    {
        int maxX = _grid.GridSize.X - 1 - depth;
        int maxY = _grid.GridSize.Y - 1 - depth;
        int minX = depth;
        int minY = depth;

        for (int x = minX; x <= maxX; x++)
        {
            // Verificamos que no sea tipo 0 (Lienzo blanco). Si es distinto a 0, está pintado/defendido.
            if (grid[x, minY] == 0 || grid[x, maxY] == 0) return false; 
        }
        for (int y = minY; y <= maxY; y++)
        {
            if (grid[minX, y] == 0 || grid[maxX, y] == 0) return false;
        }

        return true;
    }
}