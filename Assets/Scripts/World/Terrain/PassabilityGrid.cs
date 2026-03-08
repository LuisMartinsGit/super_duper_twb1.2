// PassabilityGrid.cs
// Managed MonoBehaviour singleton providing a flat passability grid
// generated from terrain slope and water data.
// Used by flow-field pathfinding and building placement validation.
// Location: Assets/Scripts/World/Terrain/PassabilityGrid.cs

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.World.Terrain
{
    /// <summary>
    /// Grid-based passability map generated from terrain.
    /// Cell values: 0 = passable, 1 = terrain-blocked (slope/water), 2 = building-blocked, 3 = obstacle-blocked (trees/rocks).
    /// Runs after ProceduralTerrain (-100) to ensure terrain exists.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PassabilityGrid : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════════

        public static PassabilityGrid Instance { get; private set; }

        // ═══════════════════════════════════════════════════════════════════════
        // CONSTANTS (must match MovementSystem)
        // ═══════════════════════════════════════════════════════════════════════

        private const float MaxWalkableSlope = 0.55f;
        private const float SlopeCheckStep = 1.5f;
        private const float WaterHeight = 20f;

        // ═══════════════════════════════════════════════════════════════════════
        // CELL VALUES
        // ═══════════════════════════════════════════════════════════════════════

        public const byte Passable = 0;
        public const byte TerrainBlocked = 1;
        public const byte BuildingBlocked = 2;
        public const byte ObstacleBlocked = 3;

        // ═══════════════════════════════════════════════════════════════════════
        // GRID DATA
        // ═══════════════════════════════════════════════════════════════════════

        private NativeArray<byte> _cells;
        private int _width;
        private int _height;
        private float _cellSize;
        private float3 _origin; // world position of cell (0,0) corner

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC ACCESSORS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Grid width in cells.</summary>
        public int Width => _width;

        /// <summary>Grid height in cells.</summary>
        public int Height => _height;

        /// <summary>World units per cell.</summary>
        public float CellSize => _cellSize;

        /// <summary>World position of cell (0,0) corner.</summary>
        public float3 Origin => _origin;

        /// <summary>Raw cell data (read-only access for jobs).</summary>
        public NativeArray<byte> Cells => _cells;

        // ═══════════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════════

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            var pt = ProceduralTerrain.Instance;
            if (pt == null)
            {
                Debug.LogError("[PassabilityGrid] ProceduralTerrain.Instance is null. Cannot generate grid.");
                return;
            }

            // Read configurable cell size (default 4 world units)
            _cellSize = GameSettings.PathfindingCellSize;

            // Derive grid bounds from ProceduralTerrain world extents
            float worldWidth = pt.worldMax.x - pt.worldMin.x;
            float worldHeight = pt.worldMax.y - pt.worldMin.y;

            _origin = new float3(pt.worldMin.x, 0f, pt.worldMin.y);
            _width = Mathf.CeilToInt(worldWidth / _cellSize);
            _height = Mathf.CeilToInt(worldHeight / _cellSize);

            int totalCells = _width * _height;
            _cells = new NativeArray<byte>(totalCells, Allocator.Persistent);

            GenerateFromTerrain();

            Debug.Log($"[PassabilityGrid] Generated {_width}x{_height} grid ({totalCells} cells, cellSize={_cellSize})");
        }

        void OnDestroy()
        {
            if (_cells.IsCreated)
                _cells.Dispose();

            if (Instance == this)
                Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // GRID GENERATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sample terrain at each cell center, marking cells as terrain-blocked
        /// if the slope exceeds MaxWalkableSlope or the height is below water level.
        /// Uses the same 4-point slope formula as MovementSystem.
        /// </summary>
        private void GenerateFromTerrain()
        {
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    float3 worldPos = CellToWorld(new int2(x, y));
                    float wx = worldPos.x;
                    float wz = worldPos.z;

                    // Sample center height
                    float hCenter = TerrainUtility.GetHeight(wx, wz);

                    // Water check: below water level is impassable
                    if (hCenter <= WaterHeight)
                    {
                        _cells[y * _width + x] = TerrainBlocked;
                        continue;
                    }

                    // 4-point slope check (matches MovementSystem exactly)
                    float hL = TerrainUtility.GetHeight(wx - SlopeCheckStep, wz);
                    float hR = TerrainUtility.GetHeight(wx + SlopeCheckStep, wz);
                    float hD = TerrainUtility.GetHeight(wx, wz - SlopeCheckStep);
                    float hU = TerrainUtility.GetHeight(wx, wz + SlopeCheckStep);

                    float dX = (hR - hL) / (SlopeCheckStep * 2f);
                    float dZ = (hU - hD) / (SlopeCheckStep * 2f);
                    float slope = math.sqrt(dX * dX + dZ * dZ);

                    _cells[y * _width + x] = slope > MaxWalkableSlope ? TerrainBlocked : Passable;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // COORDINATE CONVERSION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Convert a world position to cell coordinates.
        /// Returns the cell that contains the given world position.
        /// </summary>
        public int2 WorldToCell(float3 worldPos)
        {
            int cx = (int)math.floor((worldPos.x - _origin.x) / _cellSize);
            int cy = (int)math.floor((worldPos.z - _origin.z) / _cellSize);
            return new int2(cx, cy);
        }

        /// <summary>
        /// Convert cell coordinates to the world position at the cell center.
        /// </summary>
        public float3 CellToWorld(int2 cell)
        {
            float wx = _origin.x + (cell.x + 0.5f) * _cellSize;
            float wz = _origin.z + (cell.y + 0.5f) * _cellSize;
            float wy = TerrainUtility.GetHeight(wx, wz);
            return new float3(wx, wy, wz);
        }

        /// <summary>
        /// Snap a world position to the nearest grid cell center.
        /// Buildings should use this so their centers align with grid cells
        /// and are correctly marked as obstacles in the passability grid.
        /// </summary>
        public float3 SnapToGrid(float3 worldPos)
        {
            int2 cell = WorldToCell(worldPos);
            cell = math.clamp(cell, int2.zero, new int2(_width - 1, _height - 1));
            return CellToWorld(cell);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PASSABILITY QUERIES
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if a cell is passable (value == 0).
        /// Out-of-bounds cells are treated as impassable.
        /// </summary>
        public bool IsPassable(int2 cell)
        {
            if (cell.x < 0 || cell.x >= _width || cell.y < 0 || cell.y >= _height)
                return false;

            return _cells[cell.y * _width + cell.x] == Passable;
        }

        /// <summary>
        /// Check if the cell at a world position is passable.
        /// </summary>
        public bool IsPassable(float3 worldPos)
        {
            return IsPassable(WorldToCell(worldPos));
        }

        /// <summary>
        /// Get the raw cell value at the given cell coordinates.
        /// Returns TerrainBlocked for out-of-bounds cells.
        /// </summary>
        public byte GetCell(int2 cell)
        {
            if (cell.x < 0 || cell.x >= _width || cell.y < 0 || cell.y >= _height)
                return TerrainBlocked;

            return _cells[cell.y * _width + cell.x];
        }

        // ═══════════════════════════════════════════════════════════════════════
        // BUILDING BLOCKING
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mark all cells within the given radius of a world position as building-blocked.
        /// Only overwrites cells that are currently passable (terrain-blocked stays).
        /// </summary>
        public void BlockBuilding(float3 center, float radius)
        {
            if (!_cells.IsCreated) return;

            IterateCellsInRadius(center, radius, (int index, byte current) =>
            {
                if (current == Passable)
                    _cells[index] = BuildingBlocked;
            });
        }

        /// <summary>
        /// Unblock all cells within the given radius of a world position.
        /// Only clears cells that are building-blocked (terrain-blocked stays).
        /// </summary>
        public void UnblockBuilding(float3 center, float radius)
        {
            if (!_cells.IsCreated) return;

            IterateCellsInRadius(center, radius, (int index, byte current) =>
            {
                if (current == BuildingBlocked)
                    _cells[index] = Passable;
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // OBSTACLE BLOCKING (trees, rocks)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mark all cells within the given radius of a world position as obstacle-blocked.
        /// Only overwrites cells that are currently passable (terrain-blocked and building-blocked stay).
        /// </summary>
        public void BlockObstacle(float3 center, float radius)
        {
            if (!_cells.IsCreated) return;

            IterateCellsInRadius(center, radius, (int index, byte current) =>
            {
                if (current == Passable)
                    _cells[index] = ObstacleBlocked;
            });
        }

        /// <summary>
        /// Unblock all cells within the given radius of a world position.
        /// Only clears cells that are obstacle-blocked (terrain-blocked and building-blocked stay).
        /// </summary>
        public void UnblockObstacle(float3 center, float radius)
        {
            if (!_cells.IsCreated) return;

            IterateCellsInRadius(center, radius, (int index, byte current) =>
            {
                if (current == ObstacleBlocked)
                    _cells[index] = Passable;
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ITERATION HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Iterate all cells within a circular radius around a world position.
        /// Calls the action with (flatIndex, currentCellValue) for each cell in range.
        /// </summary>
        private void IterateCellsInRadius(float3 center, float radius, System.Action<int, byte> action)
        {
            int2 minCell = WorldToCell(new float3(center.x - radius, 0f, center.z - radius));
            int2 maxCell = WorldToCell(new float3(center.x + radius, 0f, center.z + radius));

            // Clamp to grid bounds
            minCell = math.max(minCell, int2.zero);
            maxCell = math.min(maxCell, new int2(_width - 1, _height - 1));

            float radiusSq = radius * radius;

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    float3 cellWorld = CellToWorld(new int2(x, y));
                    float dx = cellWorld.x - center.x;
                    float dz = cellWorld.z - center.z;

                    if (dx * dx + dz * dz <= radiusSq)
                    {
                        int index = y * _width + x;
                        action(index, _cells[index]);
                    }
                }
            }
        }
    }
}
