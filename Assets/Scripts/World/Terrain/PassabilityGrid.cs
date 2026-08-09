// PassabilityGrid.cs
// Managed MonoBehaviour singleton providing a flat passability grid
// generated from terrain slope and water data.
// Used by flow-field pathfinding and building placement validation.
// Location: Assets/Scripts/World/Terrain/PassabilityGrid.cs
//
// M8-followup (task-112 M7 carry-over):
//   The task-112 navigation rewrite (M1..M7) introduced
//   NavGridQuery as the replacement for the simple
//   Block/Unblock/IsCellPassable/GetCellWorldCenter API surface
//   PassabilityGrid exposed. The migration was completed in M4 for
//   the *pathing* callers (MovementSystem / NavMesh stack deleted)
//   but PassabilityGrid stays alive because the following features
//   are NOT yet implemented in the new nav stack:
//     * ComputePlayerReachability + IsReachableByAllPlayers (BFS
//       intersection across player starts).
//     * IsPassableForRadius / IsCellPassableForRadius (Minkowski-sum
//       geometric checks that catch agents whose footprint clips a
//       building corner).
//     * HasClearLineOfSight (half-cell-sampled LOS for the
//       StringPull short-cut check).
//     * Multi-class cell values (Passable / TerrainBlocked /
//       BuildingBlocked / ObstacleBlocked) used by the
//       reachability BFS to walk through buildings without
//       trapping inside them.
//
//   The ~13 surviving call sites are tagged with `M8-followup:`
//   comments where they call into PassabilityGrid. Deleting this
//   file requires either migrating each caller to a richer
//   NavGridQuery API surface (porting the BFS + geometric checks
//   into the cost-field stack) or proving those callers no longer
//   need the features. Until then the file stays.
//
//   The pathing-critical features ARE migrated: every nav job
//   reads NavCostField (not _cells), the portal graph drives every
//   path query, and BlockBuildingRect/UnblockBuildingRect are
//   no-ops on the pathing layer because BuildingCostStampSystem
//   now stamps building footprints directly into NavCostField.
//   Callers that flip these (territorial enclosure scans,
//   minimap rendering) are reading the legacy slope/water mask
//   that NavCostField doesn't carry.

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

        // Incline budget: units may climb terrain up to a 45° incline
        // (gradient = tan(45°) = 1.0); only genuinely cliff-steep ground is
        // impassable — everything gentler is walkable, and hand-painted
        // NoWalk zones carry the deliberate blocking. (Directive 2026-07-05,
        // raised from 30°, itself raised from the original ~14°.)
        // This is the SINGLE source of truth for the budget — the cost-field
        // terrain bake (TerrainCostBakeSystem) reads this mask, and
        // UnitIntegratorSystem's per-step backstop references this same
        // constant so the two can't drift.
        public const float MaxWalkableSlope = 1.0f;
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
            // Wait for SOME terrain source to be ready — either ProceduralTerrain
            // (procedural map) or an active Unity Terrain placed in the scene
            // (hand-authored map; ProceduralTerrain.MarkExternalTerrainReady sets
            // IsGenerationComplete = true in that path so the gate flips).
            while (!ProceduralTerrain.IsGenerationComplete)
                yield return null;

            // Read configurable cell size (default 4 world units)
            _cellSize = GameSettings.PathfindingCellSize;

            var pt = ProceduralTerrain.Instance;
            if (pt != null)
            {
                // Procedural map — bounds come from the generator.
                float worldWidth = pt.worldMax.x - pt.worldMin.x;
                float worldHeight = pt.worldMax.y - pt.worldMin.y;

                _origin = new float3(pt.worldMin.x, 0f, pt.worldMin.y);
                _width = Mathf.CeilToInt(worldWidth / _cellSize);
                _height = Mathf.CeilToInt(worldHeight / _cellSize);
            }
            else
            {
                // Hand-authored map — derive bounds from the active Unity
                // Terrain (e.g. MapMagic output). Falls back to a generous
                // ±GameSettings.MapHalfSize box if no terrain is active.
                var ut = UnityEngine.Terrain.activeTerrain;
                Vector3 origin;
                Vector3 size;
                if (ut != null && ut.terrainData != null)
                {
                    origin = ut.transform.position;
                    size = ut.terrainData.size;
                }
                else
                {
                    int half = Mathf.Max(64, GameSettings.MapHalfSize);
                    origin = new Vector3(-half, 0f, -half);
                    size = new Vector3(half * 2f, 0f, half * 2f);
                    Debug.LogWarning("[PassabilityGrid] no ProceduralTerrain AND no active Unity Terrain — " +
                                     $"falling back to ±{half} box. Movement will work but the grid won't " +
                                     "match any visible terrain.");
                }

                _origin = new float3(origin.x, 0f, origin.z);
                _width = Mathf.CeilToInt(size.x / _cellSize);
                _height = Mathf.CeilToInt(size.z / _cellSize);

                TWBLog.Log($"[PassabilityGrid] non-procedural map — bounds from Unity Terrain: " +
                          $"origin=({_origin.x:F0},{_origin.z:F0}) size=({size.x:F0}×{size.z:F0}) " +
                          $"cells={_width}×{_height}");
            }

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

        // ─── Hand-painted "NoWalk" terrain layer ────────────────────────────
        // Map authors can paint impassable areas with Unity's standard Paint
        // Texture brush: add a TerrainLayer whose asset name contains
        // "NoWalk" (case-insensitive) and paint it where units must not go.
        // Cells whose painted weight >= NoWalkThreshold are terrain-blocked
        // regardless of slope. Asset data is identical on every client, so
        // the mask is lockstep-safe. Maps without such a layer are untouched.
        private float[,] _noWalkMask;   // [ay, ax] weight of the NoWalk layer
        private int _maskW, _maskH;
        private Vector3 _maskOrigin;
        private Vector3 _maskSize;
        private const float NoWalkThreshold = 0.5f;

        // Paint-only mode (PaintOnlyPassability component in the map scene):
        // the NoWalk paint is the ONLY terrain rule — slope and water checks
        // are skipped, every unpainted cell is passable.
        private bool _paintOnly;

        /// <summary>
        /// Extract the "NoWalk" layer's alphamap slice from the active Unity
        /// Terrain (null when the map has no such layer painted).
        /// </summary>
        private void LoadNoWalkMask()
        {
            _noWalkMask = null;

            var ut = UnityEngine.Terrain.activeTerrain;
            if (ut == null || ut.terrainData == null) return;
            var td = ut.terrainData;

            var layers = td.terrainLayers;
            int layerIdx = -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null &&
                    layers[i].name.ToLowerInvariant().Contains("nowalk"))
                {
                    layerIdx = i;
                    break;
                }
            }
            if (layerIdx < 0)
            {
                // Deliberately loud (once per map load): a missing NoWalk layer
                // is the #1 reason painted no-go zones "don't work" — the zones
                // were painted with a different (rock-looking) layer, or the
                // layer was never added to this terrain's palette.
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < layers.Length; i++)
                    names.Append(i > 0 ? ", " : "").Append(layers[i] != null ? layers[i].name : "<null>");
                Debug.Log($"[PassabilityGrid] no 'NoWalk' terrain layer on '{ut.name}' — hand-painted " +
                          $"blocking inactive. Layers present: [{names}]");
                return;
            }

            int w = td.alphamapWidth;
            int h = td.alphamapHeight;
            float[,,] maps = td.GetAlphamaps(0, 0, w, h);

            var mask = new float[h, w];
            int painted = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float weight = maps[y, x, layerIdx];
                    mask[y, x] = weight;
                    if (weight >= NoWalkThreshold) painted++;
                }
            }

            _noWalkMask = mask;
            _maskW = w;
            _maskH = h;
            _maskOrigin = ut.transform.position;
            _maskSize = td.size;

            Debug.Log($"[PassabilityGrid] NoWalk layer '{layers[layerIdx].name}' found — " +
                      $"{painted}/{w * h} alphamap texels painted above {NoWalkThreshold:0.##}.");
            if (painted == 0)
                Debug.LogWarning("[PassabilityGrid] the NoWalk layer exists but NOTHING is painted " +
                                 $"at weight >= {NoWalkThreshold:0.##} — no cells will block. Paint with " +
                                 "full brush opacity, or check the zones weren't painted with a different layer.");
        }

        /// <summary>True when the NoWalk layer is painted at this world position.</summary>
        private bool IsNoWalkPainted(float wx, float wz)
        {
            if (_noWalkMask == null) return false;

            float u = (wx - _maskOrigin.x) / _maskSize.x;
            float v = (wz - _maskOrigin.z) / _maskSize.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

            // Alphamap layout is [z, x] — v maps to the first index.
            int ax = Mathf.Clamp((int)(u * _maskW), 0, _maskW - 1);
            int ay = Mathf.Clamp((int)(v * _maskH), 0, _maskH - 1);
            return _noWalkMask[ay, ax] >= NoWalkThreshold;
        }

        /// <summary>
        /// Sample terrain at each cell center, marking cells as terrain-blocked
        /// if the slope exceeds MaxWalkableSlope, the height is below water
        /// level, or the cell is hand-painted with the NoWalk terrain layer.
        /// Uses the same 4-point slope formula as MovementSystem.
        /// </summary>
        private void GenerateFromTerrain()
        {
            LoadNoWalkMask();

            _paintOnly = PaintOnlyPassability.Active;
            if (_paintOnly && _noWalkMask == null)
            {
                Debug.LogWarning("[PassabilityGrid] PaintOnlyPassability is in the scene but the " +
                                 "terrain has no 'NoWalk' terrain layer — falling back to the " +
                                 "normal slope/water rules.");
                _paintOnly = false;
            }
            if (_paintOnly)
                Debug.Log("[PassabilityGrid] paint-only passability — NoWalk paint is the sole " +
                          "terrain rule (slope/water checks skipped).");

            // Use the ACTUAL water plane's sea level when one is present
            // (it's authored per map / set by the procedural generator), so
            // the impassable-water mask matches the visible water surface on
            // any map instead of a single hardcoded guess. The legacy 4m
            // fallback only applies to PROCEDURAL maps (whose generator
            // guarantees land sits above it); on hand-authored maps with no
            // WaterPlane there is NO water rule — a sculpted valley below
            // an arbitrary height guess is land, not sea (it used to bake
            // phantom water across low ground and the inclines out of it).
            float waterLevel;
            if (WaterPlane.Instance != null)
                waterLevel = WaterPlane.Instance.waterLevel;
            else if (ProceduralTerrain.Instance != null)
                waterLevel = WaterHeight;
            else
                waterLevel = float.MinValue;

            int waterBlocked = FillCellsFromTerrain(waterLevel);

            // Sanity check: a water line that swallows (nearly) the whole map
            // does not describe this terrain. A flat hand-authored terrain at
            // height 0 sits below the legacy 4 m fallback, which used to bake
            // EVERY cell impassable — downstream, the nav cost field became
            // all-wall, steering's dead-end fallback reversed units away from
            // their destinations, and the only "clear" probe directions were
            // off-grid, so units wandered off the map and never stopped. No
            // playable map is >80% water: drop the water rule and regenerate
            // from slope alone.
            int total = _width * _height;
            if (waterBlocked > (total * 8) / 10)
            {
                Debug.LogWarning(
                    $"[PassabilityGrid] water level {waterLevel:0.##} blocks {waterBlocked}/{total} cells — " +
                    "the terrain sits below the assumed water line, so the water rule is IGNORED for " +
                    "this map. If the map has real water, add a WaterPlane whose waterLevel matches " +
                    "the terrain's actual sea level.");
                FillCellsFromTerrain(float.MinValue);
            }
        }

        /// <summary>
        /// Sample terrain at every cell centre and write the passability
        /// mask. Returns how many cells the water rule blocked so the caller
        /// can sanity-check the water level against the terrain.
        /// </summary>
        private int FillCellsFromTerrain(float waterLevel)
        {
            // Deck-only / mount masks travel with the cells: recomputed on
            // every fill (including the water-fallback second pass).
            if (_bridgeDeckOnly == null || _bridgeDeckOnly.Length != _width * _height)
                _bridgeDeckOnly = new bool[_width * _height];
            if (_bridgeMount == null || _bridgeMount.Length != _width * _height)
                _bridgeMount = new bool[_width * _height];

            int waterBlocked = 0;
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int idx = y * _width + x;
                    _bridgeDeckOnly[idx] = false;
                    _bridgeMount[idx] = false;

                    float3 worldPos = CellToWorld(new int2(x, y));
                    float wx = worldPos.x;
                    float wz = worldPos.z;

                    // ── 1. Is the GROUND itself walkable, by the normal rules? ──
                    bool groundBlocked;
                    bool waterCause = false;

                    if (_paintOnly)
                    {
                        // Paint-only mode: the NoWalk paint decides everything.
                        groundBlocked = IsNoWalkPainted(wx, wz);
                    }
                    else
                    {
                        float hCenter = TerrainUtility.GetHeight(wx, wz);

                        if (hCenter <= waterLevel)
                        {
                            groundBlocked = true;
                            waterCause = true;
                        }
                        else if (IsNoWalkPainted(wx, wz))
                        {
                            // Hand-painted NoWalk layer: blocked regardless of slope.
                            groundBlocked = true;
                        }
                        else
                        {
                            // Mountain / cliff blocking is handled entirely by
                            // the slope check. 4-point slope sample.
                            float hL = TerrainUtility.GetHeight(wx - SlopeCheckStep, wz);
                            float hR = TerrainUtility.GetHeight(wx + SlopeCheckStep, wz);
                            float hD = TerrainUtility.GetHeight(wx, wz - SlopeCheckStep);
                            float hU = TerrainUtility.GetHeight(wx, wz + SlopeCheckStep);

                            float dX = (hR - hL) / (SlopeCheckStep * 2f);
                            float dZ = (hU - hD) / (SlopeCheckStep * 2f);
                            float slope = math.sqrt(dX * dX + dZ * dZ);
                            groundBlocked = slope > MaxWalkableSlope;
                        }
                    }

                    // ── 2. Bridges add the DECK as a second surface ────────────
                    // Walkable ground stays plain-walkable (units may pass
                    // UNDER the arch). Blocked ground under a deck becomes
                    // passable-for-planning but DECK-ONLY: the cost bake
                    // charges it a premium and the movement integrator only
                    // admits units that are actually AT deck height — so the
                    // bridge never launders cliff/NoWalk ground into a
                    // walkable shortcut.
                    if (!groundBlocked)
                    {
                        _cells[idx] = Passable;

                        // MOUNT cell: walkable ground anywhere under the
                        // bridge footprint — the legal flow-field
                        // entrance/exit of a deck-only strip. A height cutoff
                        // here proved fragile (near the ramp top the deck
                        // rises past any fixed gap and the corridor broke,
                        // making the crossing unplannable), so footprint
                        // presence is the rule. Cells BESIDE the bridge (no
                        // deck overhead) still never connect to the strip —
                        // the cliff-walk exploit stays dead — and ground
                        // units mis-planned onto the strip from a deep
                        // underpass are refused by the integrator's
                        // deck-height admission and cancel via the stuck
                        // escalation.
                        if (BridgeSurface.HasAny
                            && BridgeSurface.TryGetDeckHeight(wx, wz, out float deckY))
                        {
                            float groundY = TerrainUtility.GetHeight(wx, wz);
                            if (deckY > groundY)
                                _bridgeMount[idx] = true;
                        }
                    }
                    else if (BridgeSurface.HasAny
                             && BridgeSurface.OverlapsCell(wx, wz, _cellSize * 0.5f))
                    {
                        _cells[idx] = Passable;
                        _bridgeDeckOnly[idx] = true;
                    }
                    else
                    {
                        _cells[idx] = TerrainBlocked;
                        if (waterCause) waterBlocked++;
                    }
                }
            }
            return waterBlocked;
        }

        // ── Bridge deck-only mask ───────────────────────────────────────────
        // True for cells that are passable ONLY because a bridge deck spans
        // them (the ground beneath fails the normal rules). Consumers:
        // TerrainCostBakeSystem (cost premium) and UnitIntegratorSystem
        // (deck-height admission check).
        private bool[] _bridgeDeckOnly;

        /// <summary>True when the cell is walkable only via a bridge deck.</summary>
        public bool IsBridgeDeckOnly(int2 cell)
        {
            if (_bridgeDeckOnly == null) return false;
            if (cell.x < 0 || cell.x >= _width || cell.y < 0 || cell.y >= _height) return false;
            return _bridgeDeckOnly[cell.y * _width + cell.x];
        }

        private bool[] _bridgeMount;

        /// <summary>True when the cell is a deck touchdown (ramp toe) — the
        /// legal flow-field entrance of a deck-only strip.</summary>
        public bool IsBridgeMount(int2 cell)
        {
            if (_bridgeMount == null) return false;
            if (cell.x < 0 || cell.x >= _width || cell.y < 0 || cell.y >= _height) return false;
            return _bridgeMount[cell.y * _width + cell.x];
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
