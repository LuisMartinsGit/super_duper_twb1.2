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
        // Matches ProceduralMapGen's waterPlaneY (2.5m). Earlier value (20m)
        // pre-dated the procedural generator and was leftover from a legacy
        // terrain where land sat ~30m up — on the current generator it
        // flagged every PlainY=8m cell as below-water, leaving only
        // mountains as "passable" terrain.
        private const float WaterHeight = 4.0f;
        // Any terrain whose elevation exceeds this (world units) is treated as
        // mountain — impassable regardless of local slope. With mountain peaks
        // capped at ~25m above PlainY (8m), threshold 24m gates the upper
        // half of mountain bulk without touching hills (which peak ~20m).
        private const float MountainHeight = 24f;
        // Cells whose mountain-region mask exceeds this are unconditionally
        // impassable — the heightmap composer uses the same mask to place
        // the soft dome, so this gates the entire massif by data instead of
        // by guessing from height/slope.
        private const float MountainMaskThreshold = 0.35f;

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
        // Per-cell reachable mask. 1 = reachable from every player's start
        // (BFS intersection). Built once by ComputePlayerReachability after
        // halls spawn; used by resource bootstraps to guarantee deposits sit
        // in a region that pathing can reach from any player.
        private NativeArray<byte> _reachable;
        private bool _reachabilityComputed;
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
            // Defer to a coroutine so we run AFTER ProceduralTerrain has
            // finished its async heightmap generation. The old code sampled
            // an empty heightmap (all zeros) when Start fired immediately
            // after the Awake stampede, and every cell came out as "below
            // water → terrain-blocked".
            StartCoroutine(WaitForTerrainAndBuild());
        }

        private System.Collections.IEnumerator WaitForTerrainAndBuild()
        {
            while (ProceduralTerrain.Instance == null || !ProceduralTerrain.IsGenerationComplete)
                yield return null;

            var pt = ProceduralTerrain.Instance;

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
        }

        void OnDestroy()
        {
            if (_cells.IsCreated)
                _cells.Dispose();

            if (_reachable.IsCreated)
                _reachable.Dispose();

            if (Instance == this)
                Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PLAYER REACHABILITY
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>True once <see cref="ComputePlayerReachability"/> has run.</summary>
        public bool IsReachabilityReady => _reachabilityComputed;

        /// <summary>
        /// Flood-fill from each player position and store the intersection
        /// (cells reachable by EVERY player) in <see cref="_reachable"/>.
        /// Must be called after halls spawn so the player positions are real.
        /// Building footprints are treated as passable for the BFS so a hall's
        /// own cell doesn't dead-end the flood.
        /// </summary>
        public void ComputePlayerReachability(float3[] playerPositions)
        {
            if (!_cells.IsCreated || playerPositions == null || playerPositions.Length == 0)
                return;

            int total = _width * _height;
            if (!_reachable.IsCreated)
                _reachable = new NativeArray<byte>(total, Allocator.Persistent);

            // Start with everything "reachable"; AND-in each player's BFS result.
            for (int i = 0; i < total; i++) _reachable[i] = 1;

            var perPlayer = new NativeArray<byte>(total, Allocator.Temp);
            var queue = new System.Collections.Generic.Queue<int2>(256);

            for (int p = 0; p < playerPositions.Length; p++)
            {
                for (int i = 0; i < total; i++) perPlayer[i] = 0;

                int2 start = NearestReachableCell(WorldToCell(playerPositions[p]));
                if (start.x < 0)
                {
                    // Player has no neighbouring passable cell — treat them as
                    // reaching nowhere; the intersection collapses to zero.
                    for (int i = 0; i < total; i++) _reachable[i] = 0;
                    break;
                }

                queue.Clear();
                queue.Enqueue(start);
                perPlayer[start.y * _width + start.x] = 1;

                while (queue.Count > 0)
                {
                    int2 c = queue.Dequeue();
                    TryEnqueueNeighbour(c.x + 1, c.y, perPlayer, queue);
                    TryEnqueueNeighbour(c.x - 1, c.y, perPlayer, queue);
                    TryEnqueueNeighbour(c.x, c.y + 1, perPlayer, queue);
                    TryEnqueueNeighbour(c.x, c.y - 1, perPlayer, queue);
                }

                for (int i = 0; i < total; i++)
                    _reachable[i] = (byte)(_reachable[i] & perPlayer[i]);
            }

            perPlayer.Dispose();
            _reachabilityComputed = true;
        }

        // Spiral-search outward from a starting cell for the first one that
        // pathing considers walkable. Returns int2(-1, -1) if nothing within
        // a reasonable radius is reachable.
        private int2 NearestReachableCell(int2 from)
        {
            if (IsBfsPassable(from)) return from;
            int maxR = math.max(_width, _height);
            for (int r = 1; r <= maxR; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int y = from.y + dy;
                    if (y < 0 || y >= _height) continue;
                    int absDy = math.abs(dy);
                    int absDx = r - absDy;
                    if (absDx < 0) continue;
                    int xLeft  = from.x - absDx;
                    int xRight = from.x + absDx;
                    if (xLeft  >= 0 && xLeft  < _width && IsBfsPassable(new int2(xLeft, y)))
                        return new int2(xLeft, y);
                    if (xRight != xLeft && xRight >= 0 && xRight < _width && IsBfsPassable(new int2(xRight, y)))
                        return new int2(xRight, y);
                }
            }
            return new int2(-1, -1);
        }

        private bool IsBfsPassable(int2 cell)
        {
            if (cell.x < 0 || cell.x >= _width || cell.y < 0 || cell.y >= _height) return false;
            byte v = _cells[cell.y * _width + cell.x];
            // Treat BuildingBlocked as passable for the player-reachability
            // flood so a hall's own footprint doesn't trap the search.
            return v == Passable || v == BuildingBlocked;
        }

        private void TryEnqueueNeighbour(int x, int y, NativeArray<byte> visited,
                                         System.Collections.Generic.Queue<int2> queue)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;
            int idx = y * _width + x;
            if (visited[idx] != 0) return;
            byte v = _cells[idx];
            if (v != Passable && v != BuildingBlocked) return;
            visited[idx] = 1;
            queue.Enqueue(new int2(x, y));
        }

        /// <summary>
        /// True if the cell at <paramref name="worldPos"/> is in the connected
        /// region every player can reach. Falls back to plain passability if
        /// reachability hasn't been computed yet (so callers don't need to
        /// branch on bootstrap order).
        /// </summary>
        public bool IsReachableByAllPlayers(float3 worldPos)
        {
            if (!_reachabilityComputed || !_reachable.IsCreated)
                return IsPassable(worldPos);
            int2 cell = WorldToCell(worldPos);
            if (cell.x < 0 || cell.x >= _width || cell.y < 0 || cell.y >= _height) return false;
            return _reachable[cell.y * _width + cell.x] != 0;
        }

        /// <summary>
        /// Radius-aware variant: the centre cell and four cardinal samples on
        /// the agent's boundary must all be in the common connected region.
        /// </summary>
        public bool IsReachableByAllPlayersForRadius(float3 worldPos, float radius)
        {
            if (!IsReachableByAllPlayers(worldPos)) return false;
            if (radius <= 0f) return true;
            return IsReachableByAllPlayers(worldPos + new float3(radius, 0f, 0f))
                && IsReachableByAllPlayers(worldPos + new float3(-radius, 0f, 0f))
                && IsReachableByAllPlayers(worldPos + new float3(0f, 0f, radius))
                && IsReachableByAllPlayers(worldPos + new float3(0f, 0f, -radius));
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
            // Cache the mountain-mask seed once per bake. ProceduralMapGen
            // stores the FINAL seed it used (which may differ from the
            // initial seed after retry) so we sample the exact same mask
            // the heightmap composer used.
            var mapSet = TheWaningBorder.World.Maps.ProceduralMapGen.Current;
            bool mapActive = mapSet != null;
            int mapSeed = mapActive ? mapSet.seed : 0;

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

                    // Mountain mask: cells inside the procedural mountain
                    // region are impassable by data, not by slope. The check
                    // routes through ProceduralHeightmap.IsMountainBlocked so
                    // it ALSO respects the composer's region-distance gate:
                    // a flat cell inside a PlayerStart whose FBM mask happens
                    // to be > 0.35 must NOT be blocked, because the composer
                    // doesn't raise a mountain there either. The earlier
                    // mask-only check was rejecting building placement on
                    // randomly-coincident FBM peaks inside player spawns.
                    if (mapActive && TheWaningBorder.World.Maps.ProceduralHeightmap
                            .IsMountainBlocked(mapSeed, mapSet, wx, wz, MountainMaskThreshold))
                    {
                        _cells[y * _width + x] = TerrainBlocked;
                        continue;
                    }

                    // Height fallback: if no region set exists (flat test
                    // map / legacy noise generator), keep the old elevation
                    // gate so towers and cliffs still block pathing.
                    if (hCenter >= MountainHeight)
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

        /// <summary>
        /// Snap a world position for a rectangular building.
        /// For odd-width dimensions, center snaps to cell center.
        /// For even-width dimensions, center snaps to cell edge (between two cells).
        /// This ensures the building footprint always covers exactly Width*Height cells.
        /// </summary>
        public float3 SnapToGridRect(float3 worldPos, int2 buildingSize)
        {
            float snappedX = SnapAxisImpl(worldPos.x, _origin.x, buildingSize.x);
            float snappedZ = SnapAxisImpl(worldPos.z, _origin.z, buildingSize.y);

            // Clamp so footprint stays in grid bounds
            float halfW = buildingSize.x * _cellSize / 2f;
            float halfH = buildingSize.y * _cellSize / 2f;
            snappedX = Mathf.Clamp(snappedX, _origin.x + halfW, _origin.x + _width * _cellSize - halfW);
            snappedZ = Mathf.Clamp(snappedZ, _origin.z + halfH, _origin.z + _height * _cellSize - halfH);

            float snappedY = TerrainUtility.GetHeight(snappedX, snappedZ);
            return new float3(snappedX, snappedY, snappedZ);
        }

        private float SnapAxisImpl(float worldCoord, float axisOrigin, int gridDimension)
        {
            float relative = (worldCoord - axisOrigin) / _cellSize;
            if (gridDimension % 2 == 1)
            {
                // Odd dimension: center on a cell center
                int cell = (int)math.floor(relative);
                return axisOrigin + (cell + 0.5f) * _cellSize;
            }
            else
            {
                // Even dimension: center on a cell edge
                int cell = Mathf.RoundToInt(relative);
                return axisOrigin + cell * _cellSize;
            }
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
        /// Geometric radius-aware passability — true only if every cell whose
        /// rectangle is within <paramref name="radius"/> world units of
        /// <paramref name="worldPos"/> is passable (Minkowski sum of obstacles
        /// with the agent disk).
        ///
        /// The centre cell is treated leniently: BuildingBlocked is OK at the
        /// centre so units can leave their own building's footprint. Surrounding
        /// cells must be Passable.
        ///
        /// Iterates the AABB enclosing the agent disk and computes the
        /// point-to-rectangle distance for each candidate cell — this catches
        /// the case where the agent's centre is at one cell's centre but its
        /// body still penetrates an adjacent blocked cell (e.g. 0.5m unit at
        /// world (2.0, 0.0) with a building at cell (1, 0) — body extends
        /// to x=1.5, z=-0.5, overlapping the building corner).
        /// (Nav-clearance fix, geometric pass.)
        /// </summary>
        public bool IsPassableForRadius(float3 worldPos, float radius)
        {
            if (radius <= 0f) return IsPassable(worldPos);
            int2 centreCell = WorldToCell(worldPos);
            int2 minCell = WorldToCell(new float3(worldPos.x - radius, 0f, worldPos.z - radius));
            int2 maxCell = WorldToCell(new float3(worldPos.x + radius, 0f, worldPos.z + radius));
            float r2 = radius * radius;
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    // Distance from worldPos to the cell rectangle.
                    float cellMinX = _origin.x + x * _cellSize;
                    float cellMaxX = cellMinX + _cellSize;
                    float cellMinZ = _origin.z + y * _cellSize;
                    float cellMaxZ = cellMinZ + _cellSize;
                    float dx = math.max(0f, math.max(cellMinX - worldPos.x, worldPos.x - cellMaxX));
                    float dz = math.max(0f, math.max(cellMinZ - worldPos.z, worldPos.z - cellMaxZ));
                    if (dx * dx + dz * dz >= r2) continue; // body just touches or doesn't reach

                    byte v;
                    if (x < 0 || x >= _width || y < 0 || y >= _height)
                        v = TerrainBlocked;
                    else
                        v = _cells[y * _width + x];

                    bool isCentre = (x == centreCell.x && y == centreCell.y);
                    if (isCentre)
                    {
                        if (v != Passable && v != BuildingBlocked) return false;
                    }
                    else
                    {
                        if (v != Passable) return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Sampled radius-aware line-of-sight between two world positions. Used
        /// by AStarPathfinder.StringPull to verify a "shortcut" between two
        /// waypoints is actually traversable by an agent of the given radius.
        ///
        /// Bresenham-on-cells is not enough: the centres of cells the line
        /// passes through can all be Passable while the actual world-space
        /// segment clips a building corner geometrically. Here we sample at
        /// half-cell intervals and run the geometric IsPassableForRadius at
        /// each sample.
        /// </summary>
        public bool HasClearLineOfSight(float3 a, float3 b, float radius)
        {
            float dist = math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
            // Sample at half-cell intervals (capped at 64 samples for sanity).
            int samples = (int)math.clamp(math.ceil(dist / (_cellSize * 0.5f)), 2f, 64f);
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float3 p = math.lerp(a, b, t);
                if (!IsPassableForRadius(p, radius)) return false;
            }
            return true;
        }

        /// <summary>
        /// Cell-space radius-aware check for use inside pathfind inner loops
        /// (no float3 conversion). <paramref name="cellRadius"/> is the half-
        /// width of the inflation box; pass 1 for a 3x3 sweep around the cell.
        /// </summary>
        public bool IsCellPassableForRadius(int2 cell, int cellRadius)
        {
            if (cellRadius <= 0) return IsPassable(cell);
            for (int dy = -cellRadius; dy <= cellRadius; dy++)
            {
                for (int dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    int x = cell.x + dx;
                    int y = cell.y + dy;
                    if (x < 0 || x >= _width || y < 0 || y >= _height) return false;
                    if (_cells[y * _width + x] != Passable) return false;
                }
            }
            return true;
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
        // RECTANGULAR BUILDING BLOCKING
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mark all cells covered by a rectangular building footprint as building-blocked.
        /// center is the building's snapped world position; size is in grid cells.
        /// </summary>
        public void BlockBuildingRect(float3 center, int2 size)
        {
            if (!_cells.IsCreated) return;
            IterateCellsInRect(center, size, (int index, byte current) =>
            {
                if (current == Passable)
                    _cells[index] = BuildingBlocked;
            });
        }

        /// <summary>
        /// Unblock all cells covered by a rectangular building footprint.
        /// Only clears cells that are building-blocked.
        /// </summary>
        public void UnblockBuildingRect(float3 center, int2 size)
        {
            if (!_cells.IsCreated) return;
            IterateCellsInRect(center, size, (int index, byte current) =>
            {
                if (current == BuildingBlocked)
                    _cells[index] = Passable;
            });
        }

        /// <summary>
        /// Check if all cells under a building footprint are passable.
        /// Used during placement validation.
        /// </summary>
        public bool IsFootprintPassable(float3 center, int2 size)
        {
            if (!_cells.IsCreated) return true;

            float halfW = size.x * _cellSize / 2f;
            float halfH = size.y * _cellSize / 2f;

            int2 minCell = WorldToCell(new float3(center.x - halfW + 0.01f, 0f, center.z - halfH + 0.01f));
            int2 maxCell = WorldToCell(new float3(center.x + halfW - 0.01f, 0f, center.z + halfH - 0.01f));

            minCell = math.max(minCell, int2.zero);
            maxCell = math.min(maxCell, new int2(_width - 1, _height - 1));

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    if (_cells[y * _width + x] != Passable)
                        return false;
                }
            }
            return true;
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
        /// Iterate all cells in a rectangle defined by center position and cell count.
        /// </summary>
        private void IterateCellsInRect(float3 center, int2 size, System.Action<int, byte> action)
        {
            float halfW = size.x * _cellSize / 2f;
            float halfH = size.y * _cellSize / 2f;

            int2 minCell = WorldToCell(new float3(center.x - halfW + 0.01f, 0f, center.z - halfH + 0.01f));
            int2 maxCell = WorldToCell(new float3(center.x + halfW - 0.01f, 0f, center.z + halfH - 0.01f));

            minCell = math.max(minCell, int2.zero);
            maxCell = math.min(maxCell, new int2(_width - 1, _height - 1));

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    int index = y * _width + x;
                    action(index, _cells[index]);
                }
            }
        }

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
