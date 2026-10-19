using Unity.Burst;
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
        private EntityQuery _eagleQuery;
        private EntityQuery _shardrootQuery;
        private EntityQuery _embeddedQuery;
        private readonly System.Collections.Generic.List<float3> _shardrootSpots =
            new System.Collections.Generic.List<float3>(2);

        protected override void OnCreate()
        {
            // Exclude BorderTag: veilstone entities are enemy to all players
            // and should NOT reveal fog. Split by UnitTag (2026-09-25): the
            // non-units (buildings, walls, towers) are stamped once into a
            // cached static layer and only re-stamped when that set changes.
            // The Feraldis eagle has sight but no UnitTag (it is deliberately
            // not a unit), so it used to land in the STATIC set - and since it
            // moves every tick, it changed the static hash on every pass and
            // forced a full re-stamp of every building's sight into all eight
            // faction layers, 4x a second, for as long as one eagle flew
            // (2026-10-16). It is stamped with the units now. Which set a
            // stamp goes in never changes the grids (max is commutative, see
            // FogOfWarManager.StampPass), so this is result-identical.
            _staticQuery = GetEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<BorderTag>(),
                ComponentType.Exclude<UnitTag>(),
                ComponentType.Exclude<EagleCompanion>());
            _eagleQuery = GetEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<EagleCompanion>(),
                ComponentType.Exclude<BorderTag>(),
                ComponentType.Exclude<UnitTag>());
            _mobileQuery = GetEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.Exclude<BorderTag>());
            // The Shardroot's holder INCLUDING a disabled one: a unit
            // garrisoned in a wall or watch tower is Disabled, and the
            // artifact must stay seen while it is inside.
            _shardrootQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ShardrootTag>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(this);
            _embeddedQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ShardrootEmbedded, LocalTransform>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            var mgr = FogOfWarManager.Instance;
            if (mgr == null) return;

            double t0 = UnityEngine.Time.realtimeSinceStartupAsDouble;

            // LIVE SIGHT, EVERY TICK (2026-10-16): the moving sight sources
            // (units, eagles, the Shardroot) go to the overlay's GPU layer on
            // every tick, so the displayed fog edge keeps up with the unit.
            // The grid below — what gameplay reads — is still stamped at the
            // 4 Hz cadence, from this same list.
            var mobiles = BuildCommands(_mobileQuery, unit: true, out _);
            if (!_eagleQuery.IsEmptyIgnoreFilter)
            {
                var eagles = BuildCommands(_eagleQuery, unit: true, out _);
                var grown = new NativeArray<FogOfWarManager.StampCommand>(
                    mobiles.Length + eagles.Length, Allocator.TempJob);
                NativeArray<FogOfWarManager.StampCommand>.Copy(mobiles, 0, grown, 0, mobiles.Length);
                NativeArray<FogOfWarManager.StampCommand>.Copy(eagles, 0, grown, mobiles.Length, eagles.Length);
                mobiles.Dispose();
                eagles.Dispose();
                mobiles = grown;
            }
            AppendShardrootReveal(ref mobiles);
            mgr.SetLiveSight(mobiles, mobiles.Length);
            int unitCount = mobiles.Length;

            if (UnityEngine.Time.unscaledTime < _nextReveal)
            {
                mobiles.Dispose();
                return;
            }
            _nextReveal = UnityEngine.Time.unscaledTime + RevealInterval;

            // Cartography: held territories are seen whole (before the
            // static-layer reuse test - a changed set forces its rebuild).
            PushTerritorySight(mgr);

            // task-063 phase 1: sect FogVisionBonus removed with the
            // FactionSectState bridge. Phase 2 reintroduces vision-related sect
            // levers (e.g. Witness — All-Seeing).

            // STATIC SET UNCHANGED (2026-10-16 perf): the static commands
            // (and their content hash) were rebuilt from three full column
            // copies on every pass, only for StampPass to find the hash equal
            // and keep its cached layer. When no static chunk has gained or
            // lost entities and none had its LineOfSight, LocalTransform or
            // FactionTag written since the last pass, the static set is the
            // one the layer was built from - so the last hash and count are
            // passed with no commands, and StampPass reuses the layer exactly
            // as it would have. Any doubt (a change, a fresh manager, an
            // invalidated layer) builds the commands as before.
            double tStatic0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
            NativeArray<FogOfWarManager.StampCommand> statics;
            ulong staticHash;
            int staticCount;
            bool staticReused = _haveStatic && !StaticSetChanged() && mgr.StaticLayerMatches(_lastStaticHash, _lastStaticCount);
            if (staticReused)
            {
                statics = new NativeArray<FogOfWarManager.StampCommand>(0, Allocator.TempJob);
                staticHash = _lastStaticHash;
                staticCount = _lastStaticCount;
            }
            else
            {
                statics = BuildCommands(_staticQuery, unit: false, out staticHash);
                staticCount = statics.Length;
            }
            _lastStaticHash = staticHash;
            _lastStaticCount = staticCount;
            _haveStatic = true;
            _staticSince = EntityManager.GlobalSystemVersion;

            double tStamp0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
            bool rebuilt = !mgr.StaticLayerMatches(staticHash, staticCount);
            // Clear + static layer + unit stamps, one Burst worker per faction.
            mgr.StampPass(statics, staticCount, staticHash, mobiles, mobiles.Length);
            double tPush0 = UnityEngine.Time.realtimeSinceStartupAsDouble;

            statics.Dispose();
            mobiles.Dispose();

            // Finalize frame - rebuilds the overlay texture (throttled inside)
            mgr.EndFrameAndBuild();

            double tEnd = UnityEngine.Time.realtimeSinceStartupAsDouble;
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("FogStamp",
                (tEnd - t0) * 1000.0,
                $"live {(tStatic0 - t0) * 1000.0:0.0} | statics {(tStamp0 - tStatic0) * 1000.0:0.0}"
                + $" ({(staticReused ? "reused" : staticCount + " cmds")})"
                + $" | stamp {(tPush0 - tStamp0) * 1000.0:0.0}{(rebuilt ? " +rebuild" : "")}"
                + $" | push {(tEnd - tPush0) * 1000.0:0.0} | units {unitCount}");
        }

        /// <summary>
        /// THE SHARDROOT IS ALWAYS SEEN (2026-10-11): every player gets a
        /// shardrootRevealRadius circle of sight (BorderSettings.asset) on
        /// the artifact, wherever it is — on the ground, carried, enshrined,
        /// embedded in its host well, or inside a wall / watch tower with its
        /// garrisoned carrier (the circle then stands on the building). Added
        /// to the per-pass unit stamps, so it moves with its holder.
        /// </summary>
        private void AppendShardrootReveal(ref NativeArray<FogOfWarManager.StampCommand> mobiles)
        {
            var settings = TheWaningBorder.Data.Border.BorderSettings.Get();
            float radius = settings != null ? settings.shardrootRevealRadius : 0f;
            if (radius <= 0f) return;

            var em = EntityManager;
            _shardrootSpots.Clear();
            if (!_shardrootQuery.IsEmptyIgnoreFilter)
            {
                using var ents = _shardrootQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                    if (TryShardrootSpot(em, ents[i], out var p)) _shardrootSpots.Add(p);
            }
            if (!_embeddedQuery.IsEmptyIgnoreFilter)
            {
                using var xfs = _embeddedQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < xfs.Length; i++) _shardrootSpots.Add(xfs[i].Position);
            }
            if (_shardrootSpots.Count == 0) return;

            const int players = 8;   // Faction.Blue (0) .. Faction.White (7)
            var grown = new NativeArray<FogOfWarManager.StampCommand>(
                mobiles.Length + _shardrootSpots.Count * players, Allocator.TempJob);
            NativeArray<FogOfWarManager.StampCommand>.Copy(mobiles, grown, mobiles.Length);
            int at = mobiles.Length;
            for (int s = 0; s < _shardrootSpots.Count; s++)
                for (int f = 0; f < players; f++)
                    grown[at++] = new FogOfWarManager.StampCommand
                    {
                        Position = (Vector3)_shardrootSpots[s],
                        Radius = radius,
                        Faction = (Faction)f,
                    };
            mobiles.Dispose();
            mobiles = grown;
        }

        private bool[] _territorySight = System.Array.Empty<bool>();

        /// <summary>
        /// CARTOGRAPHY (docs/Design/Sects.md §4, 2026-10-19): a faction that
        /// has researched it sees every territory it currently HOLDS, whole
        /// and exactly to its border. Hands the manager the
        /// [faction * count + territory] table each reveal pass; the manager
        /// stamps those territories' cells into the static layer. A lost
        /// territory simply stops being stamped and falls back to explored.
        /// Team-mates share it through the ordinary shared-sight merge.
        /// </summary>
        private void PushTerritorySight(FogOfWarManager mgr)
        {
            const int players = 8;   // Faction.Blue (0) .. Faction.White (7)
            int count = TheWaningBorder.World.Regions.RegionMap.Ready
                        && TheWaningBorder.World.Regions.TerritoryOwnership.Ready
                ? TheWaningBorder.World.Regions.RegionMap.Count : 0;
            if (_territorySight.Length != players * count)
                _territorySight = new bool[players * count];

            ulong hash = 0xCBF29CE484222325UL;
            for (int f = 0; f < players; f++)
            {
                bool charts = count > 0 && SectResearchEffects.RevealsHeldTerritory((Faction)f);
                for (int t = 0; t < count; t++)
                {
                    bool on = charts && TheWaningBorder.World.Regions.TerritoryOwnership.IsOwnedBy(t, (Faction)f);
                    _territorySight[f * count + t] = on;
                    if (on) hash = (hash ^ (ulong)(f * count + t + 1)) * 0x100000001B3UL;
                }
            }
            mgr.SetTerritorySight(_territorySight, count, hash);
        }

        /// <summary>Where the artifact physically stands: its holder, or the
        /// wall / tower module a garrisoned holder is inside.</summary>
        private static bool TryShardrootSpot(EntityManager em, Entity e, out float3 pos)
        {
            pos = default;
            if (!em.Exists(e)) return false;
            if (em.HasComponent<WallGarrisonedIn>(e))
            {
                var module = em.GetComponentData<WallGarrisonedIn>(e).Module;
                if (em.Exists(module) && em.HasComponent<LocalTransform>(module))
                {
                    pos = em.GetComponentData<LocalTransform>(module).Position;
                    return true;
                }
            }
            if (!em.HasComponent<LocalTransform>(e)) return false;
            pos = em.GetComponentData<LocalTransform>(e).Position;
            return true;
        }

        private bool _haveStatic;
        private ulong _lastStaticHash;
        private int _lastStaticCount;
        private uint _staticSince;

        /// <summary>Has the static query's content possibly changed since
        /// the last pass? Order (entities in or out of a chunk) or a write to
        /// any column the commands are built from. Conservative: a "yes" only
        /// costs the old full rebuild.</summary>
        private bool StaticSetChanged()
        {
            var losH = GetComponentTypeHandle<LineOfSight>(true);
            var ltH = GetComponentTypeHandle<LocalTransform>(true);
            var facH = GetComponentTypeHandle<FactionTag>(true);
            using var chunks = _staticQuery.ToArchetypeChunkArray(Allocator.Temp);
            if (chunks.Length != _lastStaticChunks) { _lastStaticChunks = chunks.Length; return true; }
            uint since = _staticSince - 1;   // one back: a write stamped with the version we read is still new
            for (int c = 0; c < chunks.Length; c++)
            {
                var ch = chunks[c];
                if (ch.DidOrderChange(since) || ch.DidChange(ref losH, since)
                    || ch.DidChange(ref ltH, since) || ch.DidChange(ref facH, since))
                    return true;
            }
            return false;
        }
        private int _lastStaticChunks = -1;

        /// <summary>Snapshot a query into stamp commands, plus an
        /// ORDER-INDEPENDENT content hash (sum and xor of per-entity mixes),
        /// so chunk reordering never looks like a change. Burst (2026-10-16):
        /// the same per-entity maths in the same chunk order — integer mixes
        /// and one compare, identical bits.</summary>
        private NativeArray<FogOfWarManager.StampCommand> BuildCommands(
            EntityQuery q, bool unit, out ulong hash)
        {
            q.CompleteDependency();
            var list = new NativeList<FogOfWarManager.StampCommand>(
                q.CalculateEntityCount(), Allocator.TempJob);   // TempJob: written by a job (Temp is refused even for .Run)
            var acc = new NativeArray<ulong>(2, Allocator.TempJob);
            if (unit)
                new MobileCommandsJob { Commands = list, Acc = acc }.Run(q);
            else
                new StaticCommandsJob { Commands = list, Acc = acc }.Run(q);
            hash = acc[0] ^ (acc[1] * 0xBF58476D1CE4E5B9UL);
            acc.Dispose();
            var commands = new NativeArray<FogOfWarManager.StampCommand>(list.AsArray(), Allocator.TempJob);
            list.Dispose();
            return commands;
        }

        internal static void Append(ref NativeList<FogOfWarManager.StampCommand> commands,
            ref NativeArray<ulong> acc, in LineOfSight los, in LocalTransform xf, in FactionTag fac,
            Entity source)
        {
            float3 p = xf.Position;
            float r = 0.01f > los.Radius ? 0.01f : los.Radius;   // Mathf.Max(0.01f, r), exactly
            var f = fac.Value;
            commands.Add(new FogOfWarManager.StampCommand
            {
                Position = new Vector3(p.x, p.y, p.z),
                Radius = r,
                Faction = f,
                Source = source,   // the live layer smooths per entity; not hashed
            });
            ulong h = Mix(((ulong)math.asuint(p.x) << 32) | math.asuint(p.z));
            h = Mix(h ^ (((ulong)math.asuint(r) << 8) | (uint)(int)f));
            acc[0] += h;
            acc[1] ^= Mix(h + 0x9E3779B97F4A7C15UL);
        }

        [BurstCompile]
        [WithAll(typeof(UnitTag))]
        [WithNone(typeof(BorderTag))]
        private partial struct MobileCommandsJob : IJobEntity
        {
            public NativeList<FogOfWarManager.StampCommand> Commands;
            public NativeArray<ulong> Acc;
            void Execute(Entity e, in LineOfSight los, in LocalTransform xf, in FactionTag fac)
                => Append(ref Commands, ref Acc, in los, in xf, in fac, e);
        }

        [BurstCompile]
        [WithNone(typeof(BorderTag), typeof(UnitTag))]
        private partial struct StaticCommandsJob : IJobEntity
        {
            public NativeList<FogOfWarManager.StampCommand> Commands;
            public NativeArray<ulong> Acc;
            void Execute(Entity e, in LineOfSight los, in LocalTransform xf, in FactionTag fac)
                => Append(ref Commands, ref Acc, in los, in xf, in fac, e);
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

        // STATIC ROUND-ROBIN (2026-10-15 perf). Units (mobile chunks) are
        // re-evaluated every pass as before. Everything else - buildings,
        // nodes, walls, deposits: most of the views - only changes visibility
        // when the fog moves over it, so its chunks are re-evaluated one
        // StaticSlices-th per pass (every view within StaticSlices x 0.1 s),
        // EXCEPT a chunk that changed since the last pass (an entity spawned
        // or moved into it, a transform written, or a view landed - the
        // PresentationViewSpawned bit flip bumps that column's version),
        // which is done at once so a new view is never shown a frame it
        // should not be. A change of viewing faction re-sweeps everything.
        // Presentation only: nothing here writes simulation state.
        private const int StaticSlices = 3;
        private int _staticCursor;

        // APPLIED-STATE CACHE (2026-10-16 perf). The pass used to touch the
        // managed side for EVERY unit on EVERY pass - a view-dictionary
        // lookup, a Unity null check and an activeSelf read - although a
        // unit's visibility changes only when it crosses the fog edge. The
        // show/hide decided for each entity is now remembered (by Entity
        // index + version), and an entity whose decision is unchanged is not
        // touched. Everything is still re-verified against the real
        // GameObject on a rolling slice (MobileSlices x 0.1 s for units,
        // StaticSlices x 0.1 s for the rest), on any chunk that gained
        // entities or had a view land, and on every full pass - so a view
        // that something else re-activated (a prefab swap builds its new
        // object active) is corrected within the same bound the static
        // round-robin already had. Presentation only.
        private const int MobileSlices = 4;
        private int _mobileCursor;
        private int[] _appliedVersion = System.Array.Empty<int>();   // Entity.Version, 0 = unknown
        private byte[] _appliedShow = System.Array.Empty<byte>();

        private void EnsureCache(int index)
        {
            if (index < _appliedVersion.Length) return;
            int n = System.Math.Max(index + 1, System.Math.Max(1024, _appliedVersion.Length * 2));
            System.Array.Resize(ref _appliedVersion, n);
            System.Array.Resize(ref _appliedShow, n);
        }
        private uint _lastPassVersion;
        private bool _havePass;
        private Faction _lastHuman;

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
            // A match clock that restarted (a second match in this process)
            // must not leave the throttle parked minutes in the future.
            if (_nextSync - now > SyncInterval * 2) _nextSync = now;
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
                _havePass = false;   // the next culled pass starts with a full sweep
                return;
            }

            var humanFaction = mgr.HumanFaction;
            bool fullPass = !_havePass || humanFaction != _lastHuman;
            uint sinceVersion = _lastPassVersion;
            // Stamp BEFORE the walk, one version back, so a change stamped
            // with this same version (a MonoBehaviour flip before the next
            // system runs) still reads as new next pass.
            _lastPassVersion = EntityManager.GlobalSystemVersion - 1;
            _havePass = true;
            _lastHuman = humanFaction;
            _staticCursor = (_staticCursor + 1) % StaticSlices;
            _mobileCursor = (_mobileCursor + 1) % MobileSlices;

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
            var planHandle = GetComponentTypeHandle<PlannedBuilding>(true);
            var unitHandle = GetComponentTypeHandle<UnitTag>(true);
            var stealthHandle = GetComponentTypeHandle<StealthTag>(true);
            var revealedHandle = GetComponentTypeHandle<CurseRevealed>(true);
            var viewTagHandle = GetComponentTypeHandle<PresentationViewSpawned>(true);

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
                bool isPlan = chunk.Has(ref planHandle);
                bool isUnit = chunk.Has(ref unitHandle);
                bool isStealth = chunk.Has(ref stealthHandle);
                // An army the curse seized is seen by every player (§ 6.10).
                bool revealedToAll = chunk.Has(ref revealedHandle);
                bool mobile = isUnit && !isBuilding;

                // Verify = consult the real GameObject for every row of the
                // chunk, as the old pass did for all of them.
                bool landed = chunk.Has(ref viewTagHandle) && chunk.DidChange(ref viewTagHandle, sinceVersion);
                bool verify = fullPass || landed || chunk.DidOrderChange(sinceVersion)
                              || (mobile ? c % MobileSlices == _mobileCursor
                                         : c % StaticSlices == _staticCursor);

                // A static chunk outside this pass's slice that has not
                // changed keeps the visibility it was given (see header).
                if (!mobile && !verify && !chunk.DidChange(ref ltHandle, sinceVersion))
                    continue;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];

                    // What this entity should show as - decided from ECS and
                    // fog data alone, before touching anything managed.
                    bool show;
                    if (hasFaction && factions[i].Value == humanFaction)
                    {
                        // Player-owned entities - always visible
                        show = true;
                    }
                    else if (revealedToAll)
                    {
                        show = true;
                    }
                    else if (isPlan)
                    {
                        // Another player's PLAN is never seen, fog or not
                        // (docs/Design/Planned_Buildings.md).
                        show = false;
                    }
                    else
                    {
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
                            // StealthTag is enableable and pre-added on every unit
                            // (TransientState.cs): the chunk test only says the
                            // archetype CAN be stealthed; the bit says it is.
                            if (isVisible && isStealth && chunk.IsComponentEnabled(ref stealthHandle, i)
                                && !AnySourceWithin(position, buckets, invCell,
                                    playerPositions, playerLOSRadii, false,
                                    StealthProximityReveal * StealthProximityReveal))
                                isVisible = false;

                            show = isVisible;
                        }
                        else
                        {
                            // Enemy/neutral static entities (buildings, deposits, border
                            // structures - anything without UnitTag). Three-state visibility:
                            //   currently visible              -> show normally
                            //   previously revealed, not visible -> show as ghost (last-seen)
                            //   never revealed                 -> hide entirely
                            // Previously the ghost branch required isBuilding, which made
                            // iron / veilstone deposits and any non-BuildingTag static entity
                            // vanish for good once they left vision - fixed here.
                            show = isVisible || mgr.IsRevealed(humanFaction, (Vector3)position);
                        }
                    }

                    // Unchanged since it was last applied: nothing to do.
                    int slot = entity.Index;
                    EnsureCache(slot);
                    if (!verify && _appliedVersion[slot] == entity.Version
                        && (_appliedShow[slot] != 0) == show)
                        continue;

                    if (!entityViewManager.TryGetView(entity, out var gameObject) || gameObject == null)
                    {
                        _appliedVersion[slot] = 0;   // no view yet: its landing re-verifies the chunk
                        continue;
                    }
                    if (gameObject.activeSelf != show) gameObject.SetActive(show);
                    _appliedVersion[slot] = entity.Version;
                    _appliedShow[slot] = (byte)(show ? 1 : 0);
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