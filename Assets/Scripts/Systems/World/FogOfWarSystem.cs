using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.Rendering;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Visibility
{
    /// <summary>
    /// ECS system that updates fog of war visibility each frame.
    /// 
    /// Works with FogOfWarManager to:
    /// 1. Clear current visibility each frame
    /// 2. Stamp visibility circles for all units with LineOfSight
    /// 3. Mark revealed cells as permanently explored
    /// 4. Update the human player's fog texture
    /// 
    /// Visibility states:
    /// - Hidden: Never seen (dark fog)
    /// - Revealed: Previously seen but not currently visible (lighter fog, buildings show as ghosts)
    /// - Visible: Currently within line of sight (clear, full visibility)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FogOfWarSystem : SystemBase
    {
        /// <summary>Reveal cadence (2026-08-31 live-profile pass). The full
        /// pass — an 8-faction grid clear plus a stamp disc per sighted
        /// entity — ran EVERY FRAME, and at the fine 1 m fog grid that
        /// measured 5-14 ms per frame, the single largest steady drag in
        /// the live Perf.log (911 spikes, 6.4 s total in five minutes).
        /// Nothing that reads visibility needs frame-rate freshness: intel
        /// scans, the minimap and the overlay all work on second-scale
        /// cadences, and between ticks the grids simply keep the last
        /// tick's truth. 4 Hz cuts the cost by an order of magnitude with
        /// no visible change at the fog edge.</summary>
        private const float RevealInterval = 0.25f;
        private float _nextReveal;

        private EntityQuery _staticQuery;
        private EntityQuery _mobileQuery;

        protected override void OnCreate()
        {
            // Exclude BorderTag: veilstone entities are enemy to all players
            // and should NOT reveal fog. Split by UnitTag (2026-09-25): the
            // non-units (buildings, walls, towers) are stamped once into a
            // cached static layer and only re-stamped when that set changes.
            _staticQuery = GetEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<BorderTag>(),
                ComponentType.Exclude<UnitTag>());
            _mobileQuery = GetEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.Exclude<BorderTag>());
        }

        protected override void OnUpdate()
        {
            var mgr = FogOfWarManager.Instance;
            if (mgr == null) return;

            if (UnityEngine.Time.unscaledTime < _nextReveal) return;
            _nextReveal = UnityEngine.Time.unscaledTime + RevealInterval;

            double t0 = UnityEngine.Time.realtimeSinceStartupAsDouble;

            // task-063 phase 1: sect FogVisionBonus removed with the
            // FactionSectState bridge. Phase 2 reintroduces vision-related sect
            // levers (e.g. Witness — All-Seeing).

            var statics = BuildCommands(_staticQuery, out ulong staticHash);
            var mobiles = BuildCommands(_mobileQuery, out _);

            // Clear + static layer + unit stamps, one Burst worker per faction.
            mgr.StampPass(statics, statics.Length, staticHash, mobiles, mobiles.Length);

            statics.Dispose();
            mobiles.Dispose();

            // Finalize frame - rebuilds the overlay texture (throttled inside)
            mgr.EndFrameAndBuild();

            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("FogStamp",
                (UnityEngine.Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
        }

        /// <summary>Snapshot a query into stamp commands, plus an
        /// ORDER-INDEPENDENT content hash (sum and xor of per-entity mixes),
        /// so chunk reordering never looks like a change.</summary>
        private static NativeArray<FogOfWarManager.StampCommand> BuildCommands(EntityQuery q, out ulong hash)
        {
            var lineOfSights = q.ToComponentDataArray<LineOfSight>(Allocator.Temp);
            var transforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var commands = new NativeArray<FogOfWarManager.StampCommand>(
                lineOfSights.Length, Allocator.Temp);
            ulong sum = 0, xor = 0;
            for (int i = 0; i < lineOfSights.Length; i++)
            {
                float3 p = transforms[i].Position;
                float r = Mathf.Max(0.01f, lineOfSights[i].Radius);
                var f = factions[i].Value;
                commands[i] = new FogOfWarManager.StampCommand
                {
                    Position = (Vector3)p,
                    Radius = r,
                    Faction = f,
                };
                ulong h = Mix(((ulong)math.asuint(p.x) << 32) | math.asuint(p.z));
                h = Mix(h ^ (((ulong)math.asuint(r) << 8) | (uint)(int)f));
                sum += h;
                xor ^= Mix(h + 0x9E3779B97F4A7C15UL);
            }
            hash = sum ^ (xor * 0xBF58476D1CE4E5B9UL);

            lineOfSights.Dispose();
            transforms.Dispose();
            factions.Dispose();
            return commands;
        }

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // ==================== Static Query Methods ====================

        /// <summary>
        /// Check if a position is currently visible to a faction.
        /// Returns true if the position is within any of the faction's units' line of sight.
        /// </summary>
        public static bool IsVisibleToFaction(Faction faction, float3 position)
        {
            if (FogOfWarManager.Instance == null) return true; // Fallback: everything visible
            return FogOfWarManager.Instance.IsVisible(faction, new Vector3(position.x, 0, position.z));
        }

        /// <summary>
        /// Check if a position has been revealed (explored) by a faction.
        /// Returns true if the position was ever within the faction's line of sight.
        /// </summary>
        public static bool IsRevealedToFaction(Faction faction, float3 position)
        {
            if (FogOfWarManager.Instance == null) return true; // Fallback: everything revealed
            return FogOfWarManager.Instance.IsRevealed(faction, new Vector3(position.x, 0, position.z));
        }
    }

    /// <summary>
    /// Syncs GameObject visibility with fog of war state.
    /// 
    /// Visibility rules:
    /// - Player-owned entities: Always visible
    /// - Enemy units: Only visible when in current line of sight
    /// - Enemy buildings: Visible when in LoS, ghost when only revealed
    /// 
    /// Works with EntityViewManager to show/hide presentation GameObjects.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FogOfWarSystem))]
    public partial class FogVisibilitySyncSystem : SystemBase
    {
        // Show/hide runs at 10 Hz — SetActive flips don't need frame-exact
        // timing, and the full sweep over every presented entity is too
        // heavy to pay per frame.
        private const float SyncInterval = 0.1f;
        private double _nextSync;
        private EntityQuery _presentedQuery;
        private EntityQuery _playerLosQuery;

        protected override void OnCreate()
        {
            _presentedQuery = GetEntityQuery(
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<LocalTransform>());
            _playerLosQuery = GetEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LineOfSight>());
        }

        protected override void OnUpdate()
        {
            // Sim clock, not unscaledTime: this system writes no sim state
            // (SetActive only), but it sits INSIDE the tick-stepped group — a
            // wall-clock throttle here means the group's execution pattern
            // differs per peer, and any future sim write added to this sweep
            // would desync silently. Keep the tick wall-clock-free on principle.
            double now = SystemAPI.Time.ElapsedTime;
            if (now < _nextSync) return;
            _nextSync = now + SyncInterval;

            var mgr = FogOfWarManager.Instance;
            var entityViewManager = EntityViewManager.Instance;
            if (entityViewManager == null) return;

            var em = EntityManager;

            // When fog of war is disabled — or there is no view faction (an
            // observer with nothing selected) — make all entities visible.
            // In observer matches the fog manager still exists so the AIs'
            // intel stays fog-honest (per-faction grids); the moment the
            // observer selects someone's asset, ViewFaction resolves and the
            // normal path below culls to that player's vision.
            if (mgr == null || GameSettings.ViewFaction == null)
            {
                var allEntities = _presentedQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < allEntities.Length; i++)
                {
                    if (entityViewManager.TryGetView(allEntities[i], out var go) && go != null)
                        go.SetActive(true);
                }
                allEntities.Dispose();
                return;
            }

            var humanFaction = mgr.HumanFaction;

            // Player sight sources for the direct-distance fallback, bucketed
            // into a coarse grid (2026-09-25). The fallback used to walk
            // EVERY player source for EVERY non-visible enemy unit - O(E x P)
            // at 10 Hz, the late-game cost of this system. With cells no
            // smaller than the largest sight radius, every source that can
            // see a point sits in the 3x3 cells around it, so the answer
            // (any source in range?) is unchanged.
            var pAllTransforms = _playerLosQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var pAllFactions = _playerLosQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var pAllLOS = _playerLosQuery.ToComponentDataArray<LineOfSight>(Allocator.Temp);

            int playerCount = 0;
            float maxLos = StealthProximityReveal;
            for (int pi = 0; pi < pAllFactions.Length; pi++)
            {
                if (pAllFactions[pi].Value != humanFaction) continue;
                playerCount++;
                if (pAllLOS[pi].Radius > maxLos) maxLos = pAllLOS[pi].Radius;
            }

            var playerPositions = new NativeArray<float3>(playerCount, Allocator.Temp);
            var playerLOSRadii = new NativeArray<float>(playerCount, Allocator.Temp);
            var buckets = new NativeParallelMultiHashMap<int, int>(math.max(1, playerCount), Allocator.Temp);
            float invCell = 1f / math.max(1f, maxLos);
            int idx = 0;
            for (int pi = 0; pi < pAllFactions.Length; pi++)
            {
                if (pAllFactions[pi].Value != humanFaction) continue;
                var p = pAllTransforms[pi].Position;
                playerPositions[idx] = p;
                playerLOSRadii[idx] = pAllLOS[pi].Radius;
                buckets.Add(BucketKey((int)math.floor(p.x * invCell), (int)math.floor(p.z * invCell)), idx);
                idx++;
            }
            pAllTransforms.Dispose();
            pAllFactions.Dispose();
            pAllLOS.Dispose();

            // Chunk walk: the tag tests are per ARCHETYPE, not per entity -
            // three HasComponent + a GetComponentData per entity used to run
            // across every tree and rock on the map ten times a second.
            var entityHandle = GetEntityTypeHandle();
            var ltHandle = GetComponentTypeHandle<LocalTransform>(true);
            var factionHandle = GetComponentTypeHandle<FactionTag>(true);
            var buildingHandle = GetComponentTypeHandle<BuildingTag>(true);
            var unitHandle = GetComponentTypeHandle<UnitTag>(true);
            var stealthHandle = GetComponentTypeHandle<StealthTag>(true);

            // Main-thread chunk reads: finish any job still writing them.
            _presentedQuery.CompleteDependency();
            var chunks = _presentedQuery.ToArchetypeChunkArray(Allocator.Temp);
            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var entities = chunk.GetNativeArray(entityHandle);
                var transforms = chunk.GetNativeArray(ref ltHandle);
                bool hasFaction = chunk.Has(ref factionHandle);
                var factions = hasFaction ? chunk.GetNativeArray(ref factionHandle) : default;
                bool isBuilding = chunk.Has(ref buildingHandle);
                bool isUnit = chunk.Has(ref unitHandle);
                bool isStealth = chunk.Has(ref stealthHandle);
                bool mobile = isUnit && !isBuilding;

                for (int i = 0; i < entities.Length; i++)
                {
                    if (!entityViewManager.TryGetView(entities[i], out var gameObject) || gameObject == null)
                        continue;

                    // Player-owned entities - always visible
                    if (hasFaction && factions[i].Value == humanFaction)
                    {
                        if (!gameObject.activeSelf) gameObject.SetActive(true);
                        continue;
                    }

                    var position = transforms[i].Position;
                    bool isVisible = mgr.IsVisible(humanFaction, (Vector3)position);

                    // Enemy/neutral units - show when visible through fog OR
                    // when any player unit is close enough to see them directly.
                    // The direct distance check catches fog grid resolution issues.
                    if (mobile)
                    {
                        if (!isVisible)
                            isVisible = AnySourceWithin(position, buckets, invCell,
                                playerPositions, playerLOSRadii, true, 0f);

                        // Stealth: an enemy unit with StealthTag stays hidden inside
                        // our vision area unless one of our units is within proximity
                        // (mirrors TargetingSystem's 3u reveal - keeps "I can shoot it"
                        // and "I can see it" consistent).
                        if (isVisible && isStealth
                            && !AnySourceWithin(position, buckets, invCell,
                                playerPositions, playerLOSRadii, false,
                                StealthProximityReveal * StealthProximityReveal))
                            isVisible = false;

                        if (gameObject.activeSelf != isVisible) gameObject.SetActive(isVisible);
                        continue;
                    }

                    // Enemy/neutral static entities (buildings, deposits, border
                    // structures - anything without UnitTag). Three-state visibility:
                    //   currently visible              -> show normally
                    //   previously revealed, not visible -> show as ghost (last-seen)
                    //   never revealed                 -> hide entirely
                    // Previously the ghost branch required isBuilding, which made
                    // iron / veilstone deposits and any non-BuildingTag static entity
                    // vanish for good once they left vision - fixed here.
                    bool show = isVisible || mgr.IsRevealed(humanFaction, (Vector3)position);
                    if (gameObject.activeSelf != show) gameObject.SetActive(show);
                }
            }

            chunks.Dispose();
            buckets.Dispose();
            playerPositions.Dispose();
            playerLOSRadii.Dispose();
        }

        private const float StealthProximityReveal = 3f;

        private static int BucketKey(int x, int z) => (x * 73856093) ^ (z * 19349663);

        /// <summary>True if any bucketed source lies within its own sight
        /// radius (useRadii) or within the fixed squared range of the
        /// position. Order-free: an any() test.</summary>
        private static bool AnySourceWithin(float3 position, NativeParallelMultiHashMap<int, int> buckets,
            float invCell, NativeArray<float3> pos, NativeArray<float> radii, bool useRadii, float fixedSq)
        {
            int cx = (int)math.floor(position.x * invCell);
            int cz = (int)math.floor(position.z * invCell);
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                if (!buckets.TryGetFirstValue(BucketKey(cx + ox, cz + oz), out int pi, out var it))
                    continue;
                do
                {
                    float dx = position.x - pos[pi].x;
                    float dz = position.z - pos[pi].z;
                    float distSq = dx * dx + dz * dz;
                    float limit = useRadii ? radii[pi] * radii[pi] : fixedSq;
                    if (distSq <= limit) return true;
                } while (buckets.TryGetNextValue(out pi, ref it));
            }
            return false;
        }
    }
}