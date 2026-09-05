/*
 * BAELISTICK LABS | MENTE-0 ARCHITECTURE
 * Project: De Pico y Pala Para Pincel
 * Module: ServerEventManager
 * Description: Admin-exclusive environmental event controller. Handles the mathematical 
 *              evaluation of perimeter defenses and coordinates global debris spawning.
 * Coupling Level: High. Tightly coupled with GridManager and SupabaseManager.
*/

using Godot;
using System;
using System.Collections.Generic;

public partial class ServerEventManager : Node
{
    [Export] public bool IsAdminClient = true; 
    [Export] public float EventIntervalSeconds = 5.0f; 

    private Timer _eventTimer;
    private GridManager _grid;

    public override void _Ready()
    {
        if (!IsAdminClient) return;

        _grid = GetNode<GridManager>("../TileMap"); 

        _eventTimer = new Timer();
        _eventTimer.WaitTime = EventIntervalSeconds;
        _eventTimer.Autostart = true;
        _eventTimer.Timeout += OnServerEventTriggered;
        AddChild(_eventTimer);
        
        GD.Print("[SERVER EVENT MANAGER] Module Initialized.");
    }

    /// <summary>
    /// Triggered periodically by the event timer.
    /// Acts as an identity firewall: Only the verified master developer account 
    /// can authorize environmental changes to the global database.
    /// </summary>
    private void OnServerEventTriggered()
    {
        string activeNick = _grid.PublicPlayerNick;

        // Strict Authority Validation (Hardcoded to Baelistick for security)
        if (activeNick == "Baelistick") 
        {
            GD.Print("[SERVER EVENT MANAGER] Authority confirmed. Initiating perimeter scan...");
            SupabaseManager.Instance.FetchAllPixels("", (data) => EvaluateAndDropDebris(data));
        }
        else
        {
            GD.Print("[SERVER EVENT MANAGER] Access Denied. Client lacks environmental override authority.");
        }
    }

    /// <summary>
    /// Reconstructs the grid state in local memory to evaluate defense integrity.
    /// Spawns debris payloads based on which perimeter layers remain unsealed.
    /// </summary>
    private void EvaluateAndDropDebris(Godot.Collections.Array serverData)
    {
        int[,] gridState = new int[_grid.GridSize.X, _grid.GridSize.Y];
        
        // 1. Reconstruct current state in local memory
        foreach (var item in serverData)
        {
            var dict = item.AsGodotDictionary();
            gridState[(int)dict["x"], (int)dict["y"]] = (int)dict["tile_type"];
        }

        // 2. Perimeter Defense Logic
        bool layer1Sealed = CheckBorderLayer(gridState, 0); // Outer Ring (Blocks Stone)
        bool layer2Sealed = CheckBorderLayer(gridState, 1); // Inner Ring (Blocks Dirt)

        List<Vector2I> corruptedPixels = new List<Vector2I>();
        Random rand = new Random();

        // 3. Server-side penalty execution if defenses are breached
        for (int i = 0; i < 20; i++) // Standard debris payload
        {
            int rx = rand.Next(2, _grid.GridSize.X - 2);
            int ry = rand.Next(2, _grid.GridSize.Y - 2);

            if (!layer1Sealed) 
            {
                // Unsealed Outer Ring: Spawn Stone (Type 2)
                corruptedPixels.Add(new Vector2I(rx, ry));
                gridState[rx, ry] = 2; 
            }
            else if (!layer2Sealed)
            {
                // Unsealed Inner Ring: Spawn Dirt (Type 1)
                corruptedPixels.Add(new Vector2I(rx, ry));
                gridState[rx, ry] = 1;
            }
        }

        // 4. Batch push payload to remote database
        foreach (Vector2I pos in corruptedPixels)
        {
            int tileType = gridState[pos.X, pos.Y];
            SupabaseManager.Instance.SavePixel(pos.X, pos.Y, tileType, null);
            _grid.SetCell(0, pos, 0, new Vector2I(tileType, 0)); // Local visual update for the Admin
        }
    }

    /// <summary>
    /// Mathematical perimeter scanner. Validates if a specific structural ring
    /// contains any vulnerabilities (empty canvas blocks).
    /// </summary>
    private bool CheckBorderLayer(int[,] grid, int depth)
    {
        int maxX = _grid.GridSize.X - 1 - depth;
        int maxY = _grid.GridSize.Y - 1 - depth;
        int minX = depth;
        int minY = depth;

        // X-Axis Scanner (Top and Bottom edges)
        for (int x = minX; x <= maxX; x++)
        {
            if (grid[x, minY] == 0 || grid[x, maxY] == 0) return false; 
        }
        
        // Y-Axis Scanner (Left and Right edges)
        for (int y = minY; y <= maxY; y++)
        {
            if (grid[minX, y] == 0 || grid[maxX, y] == 0) return false;
        }

        return true;
    }
}