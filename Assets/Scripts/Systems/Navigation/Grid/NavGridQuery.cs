// NavGridQuery.cs
// task-112 M3 -- static helper that lets non-DOTS callers (chiefly
// MoveCommandHelper / AttackMoveCommandHelper) interact with the cost
// field WITHOUT depending on NavMeshManager. Replaces the M3 caller-side
// migration of NavMeshManager.SnapToNavMesh for the move/attack-move
// commands per the architecture's M3 section 3.5.
//
// The helper is intentionally minimal in M3:
//   * SnapToWalkable(world, out snapped, out ok) -- floors the world
//     position to a cell, walks an expanding ring until a non-impassable
//     cell is found, returns the centre of that cell snapped back to
//     world space. Bounded by SnapMaxCellRadius so callers don't spin
//     when the map has no walkable cells at all.
//   * IsBuildable(world, sizeXZ) -- for the building-placement preview
//     path (M4 may take this as a replacement for NavMeshStaticObstacle
//     queries; M3 ships the helper but doesn't migrate anything to it
//     yet).
//   * WorldToCellInt2(world) -- shared cell math used by every caller-
//     side helper. Mirrors the math in WholeMapFlowSystem's goal
//     resolver and SampleFlowAndWriteDesiredDirJob's per-tick sampler
//     so the three agree on cell boundaries.
//
// Determinism notes:
//   * All math is integer cell coordinates derived from the singleton
//     Origin/CellSize. No machine-dependent floats.
//   * Expansion ring is row-major within each radius shell -- a stable
//     iteration order so two callers on different machines pick the
//     same cell when several are equidistant.
//   * The helper reads the cost field via the world's singleton entity
//     query. Callable from managed code (no ECS scheduling here).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Managed-side helper around the <see cref="NavCostField"/> singleton.
    /// All operations no-op gracefully (returning the input or
    /// <c>ok = false</c>) when the singleton hasn't been bootstrapped yet
    /// (race-with-first-frame case).
    /// </summary>
    public static class NavGridQuery
    {
        /// <summary>How far (in cells) <see cref="SnapToWalkable"/> walks
        /// outward looking for a passable cell before giving up. 30 cells
        /// at <c>CellSize = 1</c> matches the legacy
        /// <c>MoveTargetSnapRadius</c> the M1/M2 commands used.</summary>
        public const int SnapMaxCellRadius = 30;

        // ── Singleton cache ─────────────────────────────────────────────
        // The hot per-frame callers (UnitIntegratorSystem.IsCellPassable /
        // WorldToCellInt2, called several times per unit per frame) used to
        // do em.CreateEntityQuery(...) on EVERY call — a managed allocation
        // that produced hundreds of EntityQuery objects per frame and drove
        // periodic GC hitches. The NavGridSingleton values and the
        // NavCostField array handles are stable FOR THE LIFETIME OF A MATCH,
        // so we resolve them once and reuse — but they ARE reallocated when a
        // new match rebuilds the grid, and the cache must be dropped first via
        // InvalidateCache(). See the remarks on that method.
        private static EntityWorld _cachedWorld;
        private static NavGridSingleton _cachedGrid;
        private static NavCostField _cachedCost;
        private static bool _cacheValid;

        /// <summary>
        /// Drop the cached singletons. MUST be called before the nav grid's
        /// arrays are disposed and rebuilt (NavGridBootstrapSystem).
        ///
        /// The comment above used to promise the cost arrays were allocated
        /// once and never reallocated, and that was true while the bootstrap
        /// used a one-shot latch. It stopped being true when the grid learned
        /// to rebuild itself after the end-of-match entity wipe, and nothing
        /// here noticed: <c>_cachedCost</c> is a COPY of the component struct,
        /// so its buffer pointer stays non-null after the real allocation is
        /// freed. <c>Cost.IsCreated</c> therefore still reported true, the
        /// cache was considered valid, and every read threw
        /// "The NativeArray has been deallocated" — every frame, from the
        /// second match onward.
        /// </summary>
        public static void InvalidateCache()
        {
            _cacheValid = false;
            _cachedWorld = null;
            _cachedGrid = default;
            _cachedCost = default;
        }

        /// <summary>
        /// Resolve + cache the grid / cost-field singletons for the current
        /// default world. Re-resolves only when the world changes or the
        /// cached cost array is no longer valid. Returns false when the nav
        /// singletons aren't bootstrapped yet.
        /// </summary>
        private static bool EnsureCache()
        {
            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) { _cacheValid = false; return false; }

            if (_cacheValid && ReferenceEquals(w, _cachedWorld) && _cachedCost.Cost.IsCreated)
                return true;

            var em = w.EntityManager;

            var gridQuery = em.CreateEntityQuery(typeof(NavGridSingleton));
            if (gridQuery.IsEmptyIgnoreFilter) { gridQuery.Dispose(); _cacheValid = false; return false; }
            _cachedGrid = gridQuery.GetSingleton<NavGridSingleton>();
            gridQuery.Dispose();

            var costQuery = em.CreateEntityQuery(typeof(NavCostField));
            if (costQuery.IsEmptyIgnoreFilter) { costQuery.Dispose(); _cacheValid = false; return false; }
            _cachedCost = costQuery.GetSingleton<NavCostField>();
            costQuery.Dispose();

            if (!_cachedCost.Cost.IsCreated) { _cacheValid = false; return false; }

            _cachedWorld = w;
            _cacheValid = true;
            return true;
        }

        /// <summary>
        /// Snap a world position onto the nearest walkable cell on the cost
        /// field. Sets <paramref name="ok"/> to false (and leaves
        /// <paramref name="snapped"/> equal to <paramref name="world"/>) when
        /// the cost field hasn't been bootstrapped, when the input is well
        /// outside the grid, or when no walkable cell exists within
        /// <see cref="SnapMaxCellRadius"/>.
        ///
        /// Snap target is always the cell centre (a (.5, .5) offset on the
        /// grid). Caller's <paramref name="world"/>.y is preserved on the
        /// output -- the cost field is ground-relative, height stays the
        /// caller's responsibility.
        /// </summary>
        public static void SnapToWalkable(float3 world, out float3 snapped, out bool ok)
        {
            snapped = world;
            ok = false;

            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return;
            var em = w.EntityManager;

            // Grid + cost singletons -- both required for the snap.
            var gridQuery = em.CreateEntityQuery(typeof(NavGridSingleton));
            if (gridQuery.IsEmptyIgnoreFilter)
            {
                gridQuery.Dispose();
                return;
            }
            var grid = gridQuery.GetSingleton<NavGridSingleton>();
            gridQuery.Dispose();

            var costQuery = em.CreateEntityQuery(typeof(NavCostField));
            if (costQuery.IsEmptyIgnoreFilter)
            {
                costQuery.Dispose();
                return;
            }
            var cost = costQuery.GetSingleton<NavCostField>();
            costQuery.Dispose();

            if (!cost.Cost.IsCreated) return;

            // World -> cell. Centre of cell (cx, cz) is at Origin + (cx+0.5, _, cz+0.5) * CellSize.
            int cx = (int)math.floor((world.x - grid.Origin.x) / grid.CellSize);
            int cz = (int)math.floor((world.z - grid.Origin.z) / grid.CellSize);

            // If the start cell is already inside grid bounds and walkable,
            // snap to its centre and return. This is the common case (click
            // on flat terrain).
            if (TryCellCentre(grid, cost, cx, cz, world.y, out snapped))
            {
                ok = true;
                return;
            }

            // Walk an expanding ring around (cx, cz). Within each ring,
            // visit cells row-major (z ascending, x ascending) so two
            // callers on different machines pick the same winner when
            // several cells are equidistant. Ring r covers the cells
            // whose Chebyshev distance from the centre is exactly r.
            for (int r = 1; r <= SnapMaxCellRadius; r++)
            {
                int z0 = cz - r;
                int z1 = cz + r;
                int x0 = cx - r;
                int x1 = cx + r;

                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        // Skip cells inside the previous ring (only the
                        // ring boundary should be visited at radius r).
                        if (x > x0 && x < x1 && z > z0 && z < z1) continue;
                        if (TryCellCentre(grid, cost, x, z, world.y, out snapped))
                        {
                            ok = true;
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Tests whether a Width x Height building footprint centred on the
        /// world position would land entirely on non-impassable cells.
        /// Returns false when any covered cell is impassable / out of
        /// bounds / when the singletons are missing.
        /// </summary>
        public static bool IsBuildable(float3 world, int2 sizeXZ)
        {
            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return false;
            var em = w.EntityManager;

            var gridQuery = em.CreateEntityQuery(typeof(NavGridSingleton));
            if (gridQuery.IsEmptyIgnoreFilter) { gridQuery.Dispose(); return false; }
            var grid = gridQuery.GetSingleton<NavGridSingleton>();
            gridQuery.Dispose();

            var costQuery = em.CreateEntityQuery(typeof(NavCostField));
            if (costQuery.IsEmptyIgnoreFilter) { costQuery.Dispose(); return false; }
            var cost = costQuery.GetSingleton<NavCostField>();
            costQuery.Dispose();

            if (!cost.Cost.IsCreated) return false;

            int cx = (int)math.floor((world.x - grid.Origin.x) / grid.CellSize);
            int cz = (int)math.floor((world.z - grid.Origin.z) / grid.CellSize);
            int halfW = sizeXZ.x / 2;
            int halfH = sizeXZ.y / 2;
            int x0 = cx - halfW;
            int z0 = cz - halfH;
            int x1 = cx + halfW;
            int z1 = cz + halfH;

            if (x0 < 0 || z0 < 0 || x1 >= grid.Width || z1 >= grid.Height) return false;

            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (cost.Cost[z * grid.Width + x] == NavCostField.CostImpassable)
                        return false;
            return true;
        }

        /// <summary>
        /// World-space to integer cell coordinates. Returns int.MinValue
        /// cells when the grid singleton is unavailable so callers can
        /// detect the race.
        /// </summary>
        public static int2 WorldToCellInt2(float3 world)
        {
            if (!EnsureCache())
                return new int2(int.MinValue, int.MinValue);

            int cx = (int)math.floor((world.x - _cachedGrid.Origin.x) / _cachedGrid.CellSize);
            int cz = (int)math.floor((world.z - _cachedGrid.Origin.z) / _cachedGrid.CellSize);
            return new int2(cx, cz);
        }

        // ── M4 API surface (architecture section 4.5 -- 13 PassabilityGrid sites) ────

        /// <summary>
        /// task-112 M4 -- snap a world position onto the nearest passable
        /// cell on the cost field. Alias for <see cref="SnapToWalkable"/>
        /// renamed to match the architecture's M4 section 4.5 API list.
        /// Returns true when <paramref name="snapped"/> holds a valid
        /// cell-centre world position; false when the singletons aren't
        /// up yet or no passable cell exists within
        /// <see cref="SnapMaxCellRadius"/>.
        /// </summary>
        public static bool SnapToPassable(float3 world, out float3 snapped, out bool ok)
        {
            SnapToWalkable(world, out snapped, out ok);
            return ok;
        }

        /// <summary>
        /// task-112 M4 -- tests whether the cell at <paramref name="cell"/>
        /// (integer cell coordinates) is currently passable on layer 0
        /// (i.e. cost byte != <see cref="NavCostField.CostImpassable"/>).
        /// Returns false for out-of-bounds cells or when the cost field
        /// singleton hasn't been bootstrapped yet.
        /// </summary>
        /// <summary>
        /// Can a unit actually stand at this WORLD position? Asks both
        /// authorities UnitIntegratorSystem enforces — the nav cost field (its
        /// primary gate) and the legacy PassabilityGrid (its terrain backstop)
        /// — so a destination chosen through this can never be somewhere
        /// movement will refuse to take the unit.
        ///
        /// Every "walk up to a thing and act on it" system needs this: aiming
        /// at a point inside an impassable cell is unreachable by construction,
        /// and it also defeats FlowFollowSystem's line-of-sight shortcut, so
        /// the approach stops being a straight line as well.
        /// </summary>
        public static bool IsWorldStandable(float3 world)
        {
            int2 navCell = WorldToCellInt2(world);
            if (navCell.x != int.MinValue && !IsCellPassable(navCell)) return false;

            // PassabilityGrid contributes ONLY its terrain mask here, exactly as
            // UnitIntegratorSystem's backstop does. Its building/obstacle bits
            // are legacy — the cost field is the authority for those, and it is
            // the one that carries a building's EDGE CLEARANCE. Rejecting
            // BuildingBlocked here would refuse the very lane between two
            // buildings that clearance exists to open.
            var pg = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
            if (pg != null)
            {
                var cell = pg.WorldToCell(world);
                if (cell.x >= 0 && cell.x < pg.Width && cell.y >= 0 && cell.y < pg.Height
                    && pg.GetCell(cell) == TheWaningBorder.World.Terrain.PassabilityGrid.TerrainBlocked)
                    return false;
            }
            return true;
        }

        public static bool IsCellPassable(int2 cell)
        {
            if (!EnsureCache()) return false;
            if (cell.x < 0 || cell.x >= _cachedGrid.Width || cell.y < 0 || cell.y >= _cachedGrid.Height) return false;
            return _cachedCost.Cost[cell.y * _cachedGrid.Width + cell.x] != NavCostField.CostImpassable;
        }

        /// <summary>
        /// Layer-aware passability (cached). Layer 0 = Ground, layer 1 =
        /// Rampart. A cell is passable when its cost byte on that layer isn't
        /// <see cref="NavCostField.CostImpassable"/>. Used by the integrator
        /// so a unit standing on the rampart (NavLayerIndex.Layer == 1) walks
        /// only on wall-top cells (which are walkable on layer 1 but
        /// impassable on layer 0) instead of being blocked by the wall's
        /// ground footprint.
        /// </summary>
        public static bool IsCellPassable(int2 cell, byte layer)
        {
            if (!EnsureCache()) return false;
            if (layer >= _cachedCost.LayerCount) return false;
            if (cell.x < 0 || cell.x >= _cachedGrid.Width || cell.y < 0 || cell.y >= _cachedGrid.Height) return false;
            int layerArea = _cachedCost.Width * _cachedCost.Height;
            int idx = layer * layerArea + cell.y * _cachedCost.Width + cell.x;
            return _cachedCost.Cost[idx] != NavCostField.CostImpassable;
        }

        /// <summary>
        /// Layer-aware passability for a unit of a KNOWN faction.
        ///
        /// Same as <see cref="IsCellPassable(int2, byte)"/> except it also
        /// honours gate ownership. A gate cell stamps
        /// <see cref="NavCostField.CostConditional"/> (254) on the ground
        /// layer, and 254 != 255, so the plain overload reports it passable
        /// to EVERYONE — which let enemy armies walk straight through a
        /// closed Alanthor gate as if the wall had a doorway in it. The owner
        /// faction is encoded in the low 3 bits of the cell's flag byte
        /// exactly for this check (see NavCostField.FlagOwnerMask), so a
        /// conditional cell is passable only to its owner.
        ///
        /// <paramref name="faction"/> is the Faction enum index. Only the
        /// eight player slots (Blue=0 .. White=7) fit the 3-bit owner field,
        /// so anything above that — Faction.Border (8), or the 0xFF "no
        /// FactionTag" sentinel — can never own a gate and is refused. That
        /// matters: 8 &amp; 0x07 == 0, so without the range check every curse
        /// creature would have walked through Blue's gates. Non-gate cells
        /// behave identically to the plain overload.
        /// </summary>
        public static bool IsCellPassableFor(int2 cell, byte layer, byte faction)
        {
            if (!EnsureCache()) return false;
            if (layer >= _cachedCost.LayerCount) return false;
            if (cell.x < 0 || cell.x >= _cachedGrid.Width
                || cell.y < 0 || cell.y >= _cachedGrid.Height) return false;

            int layerArea = _cachedCost.Width * _cachedCost.Height;
            int idx = layer * layerArea + cell.y * _cachedCost.Width + cell.x;

            byte c = _cachedCost.Cost[idx];
            if (c == NavCostField.CostImpassable) return false;
            if (c != NavCostField.CostConditional) return true;

            // Conditional cell: only the owning faction may cross.
            if (faction > NavCostField.FlagOwnerMask) return false; // not a player slot
            if (!_cachedCost.Flags.IsCreated) return false;
            byte flags = _cachedCost.Flags[idx];
            return (flags & NavCostField.FlagOwnerMask) == faction;
        }

        /// <summary>
        /// task-112 M4 -- world position at the centre of the given cell.
        /// Y is set to 0 (caller is responsible for terrain height). Used
        /// by ex-PassabilityGrid callers that want to translate a snapped
        /// cell back into a world-space waypoint.
        /// Returns float3.zero when the grid singleton is unavailable.
        /// </summary>
        public static float3 GetCellWorldCenter(int2 cell)
        {
            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return float3.zero;
            var em = w.EntityManager;
            var gridQuery = em.CreateEntityQuery(typeof(NavGridSingleton));
            if (gridQuery.IsEmptyIgnoreFilter) { gridQuery.Dispose(); return float3.zero; }
            var grid = gridQuery.GetSingleton<NavGridSingleton>();
            gridQuery.Dispose();
            return new float3(
                grid.Origin.x + (cell.x + 0.5f) * grid.CellSize,
                0f,
                grid.Origin.z + (cell.y + 0.5f) * grid.CellSize);
        }

        /// <summary>
        /// task-112 M4 -- buildable variant for the architecture section
        /// 4.5 surface. Snaps a candidate footprint placement; returns
        /// <c>true</c> + the snapped centre on success. Wraps
        /// <see cref="IsBuildable"/> with cell-centre snapping so the
        /// returned position aligns to the grid.
        /// </summary>
        public static bool IsBuildable(float3 world, int2 size, out float3 snapped)
        {
            snapped = world;
            if (!IsBuildable(world, size)) return false;
            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return false;
            var em = w.EntityManager;
            var gridQuery = em.CreateEntityQuery(typeof(NavGridSingleton));
            if (gridQuery.IsEmptyIgnoreFilter) { gridQuery.Dispose(); return true; }
            var grid = gridQuery.GetSingleton<NavGridSingleton>();
            gridQuery.Dispose();

            int cx = (int)math.floor((world.x - grid.Origin.x) / grid.CellSize);
            int cz = (int)math.floor((world.z - grid.Origin.z) / grid.CellSize);
            snapped = new float3(
                grid.Origin.x + (cx + 0.5f) * grid.CellSize,
                world.y,
                grid.Origin.z + (cz + 0.5f) * grid.CellSize);
            return true;
        }

        // ── M5 API surface (Rampart layer + per-layer queries) ─────────

        /// <summary>
        /// task-112 M5 -- tests whether the cell at <paramref name="cell"/>
        /// (integer cell coordinates) is passable on the requested layer.
        /// Layer 0 = Ground, layer 1 = Rampart. Returns false for
        /// out-of-bounds cells, out-of-range layers, or when the cost
        /// field singleton hasn't been bootstrapped yet.
        /// </summary>
        public static bool IsCellPassableForLayer(int2 cell, byte layer)
        {
            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return false;
            var em = w.EntityManager;

            var gridQuery = em.CreateEntityQuery(typeof(NavGridSingleton));
            if (gridQuery.IsEmptyIgnoreFilter) { gridQuery.Dispose(); return false; }
            var grid = gridQuery.GetSingleton<NavGridSingleton>();
            gridQuery.Dispose();

            var costQuery = em.CreateEntityQuery(typeof(NavCostField));
            if (costQuery.IsEmptyIgnoreFilter) { costQuery.Dispose(); return false; }
            var cost = costQuery.GetSingleton<NavCostField>();
            costQuery.Dispose();

            if (!cost.Cost.IsCreated) return false;
            if (layer >= cost.LayerCount) return false;
            if (cell.x < 0 || cell.x >= grid.Width || cell.y < 0 || cell.y >= grid.Height) return false;

            int idx = layer * (cost.Width * cost.Height) + cell.y * cost.Width + cell.x;
            byte costByte = cost.Cost[idx];
            // Treat both impassable (255) and conditional/gate (254) as
            // not-passable at the query layer; gate eligibility is
            // resolved by LayerTransitionSystem.
            return costByte != NavCostField.CostImpassable
                && costByte != NavCostField.CostConditional;
        }

        /// <summary>
        /// task-112 M5 -- world position -> integer cell on the requested
        /// layer. Returns the same (x, z) cell regardless of layer (the
        /// grid is per-cell in the XZ plane, layer chosen by the caller);
        /// the layer argument is accepted for API symmetry with
        /// <see cref="IsCellPassableForLayer"/>. Returns int.MinValue
        /// cells when the grid singleton is unavailable.
        /// </summary>
        public static int2 WorldToCellAtLayer(float3 world, byte layer)
        {
            // Layer 0 / 1 share XZ grid; this helper exists so callers
            // that walk layers explicitly can write IsCellPassableForLayer(
            // WorldToCellAtLayer(p, layer), layer) without the implicit
            // assumption that the layer choice changes the XZ math.
            // (Future layered grids could re-project; for M5 we don't.)
            _ = layer;
            return WorldToCellInt2(world);
        }

        // ── internal ────────────────────────────────────────────────────

        // Tests (cx, cz) against grid bounds + cost-field passability;
        // writes the cell-centre world position into `out` on success.
        // Preserves the caller's y on the output so height stays a
        // terrain-utility concern.
        private static bool TryCellCentre(
            in NavGridSingleton grid,
            in NavCostField cost,
            int cx,
            int cz,
            float worldY,
            out float3 snapped)
        {
            if (cx < 0 || cx >= grid.Width || cz < 0 || cz >= grid.Height)
            {
                snapped = float3.zero;
                return false;
            }
            int idx = cz * grid.Width + cx;
            if (cost.Cost[idx] == NavCostField.CostImpassable)
            {
                snapped = float3.zero;
                return false;
            }
            snapped = new float3(
                grid.Origin.x + (cx + 0.5f) * grid.CellSize,
                worldY,
                grid.Origin.z + (cz + 0.5f) * grid.CellSize);
            return true;
        }
    }
}
