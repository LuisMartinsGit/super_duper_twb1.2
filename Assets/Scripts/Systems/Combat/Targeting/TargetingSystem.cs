using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles target acquisition and combat command processing.
    ///
    /// Responsibilities:
    /// - Process user AttackCommand components
    /// - Auto-acquire targets for idle units BY STANCE (docs/Design/Stances.md)
    /// - Leash every AUTO-acquired engagement and walk the unit home when it
    ///   breaks (player / AI attack orders are never leashed)
    /// - Initialize combat-related components (GuardPoint, AttackCooldown)
    /// - Handle return-to-guard behavior when no enemies present
    /// - Clean up stale attack commands
    ///
    /// Respects UserMoveOrder tag to prevent interrupting player movement commands.
    ///
    /// Determinism: the enemy spatial hash is filled in network-id order, the
    /// acquisition stagger phase comes from the network id, and the clock is
    /// this system's own tick count re-zeroed at every match epoch — nothing
    /// here reads wall-clock time or chunk order.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Navigation.UnitIntegratorSystem))]
    public partial struct TargetingSystem : ISystem
    {
        // Leash: an idle unit only chases a target this far from its guard
        // point before being sent back. Lowered 20→10 so idle units HOLD their
        // ground and wait to be massed into an army instead of wandering off to
        // hunt enemies one by one. Global (player + AI).
        //
        // Since the stances (docs/Design/Stances.md) this is the "after the
        // fight, go home" distance: an idle unit left farther than this from
        // its post walks back instead of looking for the next target. The
        // CHASE leash is per stance (StanceSettings) and applies while the
        // target is still alive.
        private const float MaxGuardDistance = 10f;

        /// <summary>
        /// How close a FORMATION attack-move must get to its destination
        /// before its members may start auto-acquiring targets. Beyond it
        /// they hold rank and march; inside it the assault opens normally
        /// (2026-09-03 directive: "armies must stay in formation until they
        /// are 20 units away from their target"). Retaliation while under
        /// fire is exempt — see the gate in the acquire pass.
        /// </summary>
        private const float FormationHoldRadius = 20f;

        // How far off its guard point an idle unit must be before the leash
        // walks it home.
        //
        // Derived from StuckRedirectSystem.ArrivalSkip, not hand-picked: that
        // system declares a unit ARRIVED anywhere inside ArrivalSkip once it
        // provably cannot get closer (a neighbour is parked on the exact
        // point). A leash that fires inside that same band means the two
        // systems disagree about what "arrived" means, and the unit is walked
        // back in, shoved out, re-declared arrived, walked back in... forever.
        // The old flat 2 m sat well inside the band — and below the 2.0 m
        // formation slot pitch — so ordinary crowd jostle was enough to trip
        // it. Must stay strictly greater than ArrivalSkip.
        private const float GuardReturnThreshold =
            TheWaningBorder.Systems.Navigation.StuckRedirectSystem.ArrivalSkip + 1f;
        /// <summary>Max height difference melee can strike across (a bridge
        /// deck is ~3-5m above the underpass — unreachable). Shared meaning
        /// with MeleeCombatSystem's gate.</summary>
        public const float MeleeMaxHeightDelta = 2f;

        /// <summary>
        /// Whether a height gap stops a melee strike. The gate exists for
        /// SEPARATE SURFACES — a bridge deck over its underpass, a rampart over
        /// the ground. A building target is exempt, because its pivot height
        /// is the terrain at its centre, not where its body is: on a slope the
        /// edge a unit is standing against can sit metres above or below that
        /// pivot, and the gate dropped the building for good. A building never
        /// stands on a bridge deck, so the exemption opens no deck/underpass
        /// hole; an attacker on a RAMPART still keeps the gate, so a wall's
        /// garrison cannot hack at buildings on the ground below it.
        /// </summary>
        public static bool MeleeHeightBlocks(float attackerY, float targetY,
            bool targetIsBuilding, bool attackerOnRampart)
        {
            if (targetIsBuilding && !attackerOnRampart) return false;
            return math.abs(attackerY - targetY) > MeleeMaxHeightDelta;
        }

        /// <summary>True when the unit stands on the wall-top nav layer.</summary>
        public static bool IsOnRampart(EntityManager em, Entity unit)
            => em.HasComponent<NavLayerIndex>(unit)
               && em.GetComponentData<NavLayerIndex>(unit).Layer == NavLayerIndex.LayerRampart;

        // Fix #207: spatial-hash cell size for the enemy scan.
        // Cell=20 means a unit with LOS<=20 only visits a 3x3 neighborhood
        // (9 cells). Keeps per-unit inner-loop work bounded regardless of
        // total enemy count.
        private const float TargetingCellSize = 20f;

        /// <summary>
        /// Acquisition runs for a given unit on one tick in this many (phase
        /// from its network id, so it is the same on every peer). An idle unit
        /// used to rescan every tick; a quarter of that is 0.13 s of reaction
        /// at 30 Hz, well under a swing. Loop resolution, not a tunable — and a
        /// power of two, because the phase test masks with it.
        /// </summary>
        internal const int AcquireStagger = 4;

        /// <summary>Faction slots in the hostility table: 8 players + Border.</summary>
        private const int FactionSlots = 9;

        // ── Per-enemy snapshot flags (cached with the enemy set) ─────────
        private const byte FlagBuilding = 1;
        private const byte FlagWall = 2;
        private const byte FlagWorker = 4;   // WorkerTag or CanBuild (Border's guard rule)
        private const byte FlagHero = 8;     // HeroLevel (TargetPreference "Hero")

        // ── Cross-frame cache of the enemy set's peer-stable order ────────
        // Rebuilding the order needs a NetworkedEntity lookup per enemy plus
        // an O(N log N) sort, every frame, while the SET of enemies changes
        // only when something spawns, dies or changes archetype. The cache is
        // keyed on the exact entity array (same entities, same chunk order):
        // any difference rebuilds it. Only kept while every enemy is
        // networked — the position tiebreak for an unnetworked one changes as
        // it moves.
        private NativeList<Entity> _cacheEnemies;
        private NativeList<int> _cacheOrder;
        private NativeList<byte> _cacheFlags;
        private NativeList<byte> _cachePrio;
        /// <summary>Per-enemy UnitTagsData mask (TargetPreference rules).</summary>
        private NativeList<uint> _cacheTags;
        private byte _cacheValid;

        // ── Own clock (seconds since the match epoch began) ──────────────
        // UnitEngagement's HitAt / NextAcquireAt are stamped against it.
        // SystemAPI.Time.ElapsedTime differs per peer (pre-match
        // accumulation); this sum of fixed lockstep steps does not.
        private int _epoch;
        private uint _tick;
        private float _clock;

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            // Published by UnitStanceSystem from UnitStanceSystem.asset. A
            // missing config is logged loudly there; there is no code-side
            // default (docs/Design/Stances.md §8).
            state.RequireForUpdate<StanceSettings>();

            _cacheEnemies = new NativeList<Entity>(256, Allocator.Persistent);
            _cacheOrder = new NativeList<int>(256, Allocator.Persistent);
            _cacheFlags = new NativeList<byte>(256, Allocator.Persistent);
            _cachePrio = new NativeList<byte>(256, Allocator.Persistent);
            _cacheTags = new NativeList<uint>(256, Allocator.Persistent);
            _cacheValid = 0;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_cacheEnemies.IsCreated) _cacheEnemies.Dispose();
            if (_cacheOrder.IsCreated) _cacheOrder.Dispose();
            if (_cacheFlags.IsCreated) _cacheFlags.Dispose();
            if (_cachePrio.IsCreated) _cachePrio.Dispose();
            if (_cacheTags.IsCreated) _cacheTags.Dispose();
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var em = state.EntityManager;
            var settings = SystemAPI.GetSingleton<StanceSettings>();

            // Re-phase at every match: the world runs before the match starts.
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                _tick = 0;
                _clock = 0f;
                _cacheValid = 0;
            }
            _tick++;
            _clock += SystemAPI.Time.DeltaTime;

            // =============================================================================
            // PHASE 0: Initialize required components for combat
            // =============================================================================
            InitializeCombatComponents(ref state, ref ecb);

            // Hit detection for the under-fire window and retaliation — a
            // health DROP since last tick, whatever dealt it.
            TrackHits(ref state);

            // =============================================================================
            // PHASE 1: Handle user attack commands
            // =============================================================================
            ProcessAttackCommands(ref state, ref ecb);

            // PHASE 1b: leash auto-acquired engagements (docs/Design/Stances.md §3).
            EnforceLeash(ref state, ref ecb, in settings);

            // Build enemy arrays ONCE for both auto-acquire and return-to-guard phases
            // Exclude NodeUntargetable — veilstone nodes are immune to targeting
            // unless ACTIVE (NodeTargetabilitySystem toggles the tag: Active =
            // destroyable, rubble/rebuilding/cleansed = immune husk).
            // Verb wells (BorderMainNodeTag) are NEVER auto-acquired by anyone
            // (2026-08-04): breaking a well is a deliberate Feraldis order
            // (CommandRouter gates it by culture), not something an army does
            // by standing near the objective.
            var enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, Health>()
                // NodeNoAutoAcquire replaces a blanket BorderMainNodeTag
                // exclusion: NodeTargetabilitySystem stamps it on every well
                // EXCEPT one that a Feraldis Corruptor has cracked open, so
                // wells stay un-auto-attackable as before but a corrupted
                // well can be swarmed by an army attack-moving onto it.
                .WithNone<NodeUntargetable, NodeNoAutoAcquire>()
                // Stoneveil (Fortitude): a veiled unit cannot be targeted at
                // all. Excluded from the enemy set rather than filtered later,
                // so it also drops out of the spatial hash and the
                // return-to-guard scan built from it.
                .WithNone<SectVeiled>()
                // A wall SEGMENT is the composite that owns the instances;
                // the instances are what gets hit, never the segment itself.
                .WithNone<WallSegmentTag>()
                .Build();

            using var allEnemies = enemyQuery.ToEntityArray(Allocator.Temp);
            using var allEnemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var allEnemyFactions = enemyQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var allEnemyHealth = enemyQuery.ToComponentDataArray<Health>(Allocator.Temp);

            RefreshEnemyCache(em, allEnemies, allEnemyTransforms);

            // Hostility table, once per tick instead of a team lookup per
            // candidate. Burst-safe variant (SharedStatic team table).
            var hostile = new NativeArray<byte>(FactionSlots * FactionSlots, Allocator.Temp);
            for (int a = 0; a < FactionSlots; a++)
                for (int b = 0; b < FactionSlots; b++)
                    hostile[a * FactionSlots + b] = (byte)(Alliances.AreHostileBurst((Faction)a, (Faction)b) ? 1 : 0);

            // Fix #207 spatial hash, now keyed PER FACTION as well as per cell:
            // a unit's neighbourhood is mostly its own army, and bucketing by
            // owner lets the scan skip every friendly entry outright instead
            // of rejecting them one at a time.
            using var spatialMap = new NativeParallelMultiHashMap<int3, int>(
                math.max(16, allEnemies.Length * 2), Allocator.Temp);
            var factionPresent = new NativeArray<byte>(FactionSlots, Allocator.Temp);

            // PEER-STABLE INSERT ORDER (2026-09-05, MP harness catch #14 —
            // the same disease and cure as the nav spatial hash, catch #4).
            // ToEntityArray arrives in CHUNK-WALK order, which legitimately
            // differs between lockstep peers (host-only AI structural changes
            // reshuffle the host's chunks), and a multimap's per-cell chain
            // order IS the insert order — the order the acquire scan visits
            // candidates in. Against a grid-snapped base two buildings sit at
            // EXACTLY equal surface distance, the strict '<' keeps the
            // first-seen one, and twelve besiegers picked different targets
            // per peer (tick 43790 fork, the first finisher-driven deep base
            // assault). Insert by NetworkId — the one identity every peer
            // agrees on — with position bits as the tiebreak for anything
            // unnetworked. (Order held in _cacheOrder; see RefreshEnemyCache.)
            for (int o = 0; o < _cacheOrder.Length; o++)
            {
                int i = _cacheOrder[o];
                int f = (int)allEnemyFactions[i].Value;
                if (f < 0 || f >= FactionSlots) continue;
                var pos = allEnemyTransforms[i].Position;
                var key = new int3(
                    (int)math.floor(pos.x / TargetingCellSize),
                    (int)math.floor(pos.z / TargetingCellSize),
                    f);
                spatialMap.Add(key, i);
                factionPresent[f] = 1;
            }

            // Per-target attacker count — spreads attackers across multiple
            // enemies so rank 2 of a melee column picks a different enemy than
            // rank 1 instead of queuing up behind it. Snapshot built from
            // existing Target components, then incremented in-place as we
            // assign new targets during this OnUpdate so the same enemy can't
            // be re-picked once it hits MaxAttackersPerEnemy.
            var attackerCount = new NativeHashMap<Entity, int>(
                math.max(16, allEnemies.Length), Allocator.Temp);
            var attackerSnapshotQuery = SystemAPI.QueryBuilder()
                .WithAll<Target, UnitTag>()
                .Build();
            using (var attackerTgts = attackerSnapshotQuery.ToComponentDataArray<Target>(Allocator.Temp))
            {
                for (int i = 0; i < attackerTgts.Length; i++)
                {
                    var t = attackerTgts[i].Value;
                    if (t == Entity.Null) continue;
                    if (attackerCount.TryGetValue(t, out int c)) attackerCount[t] = c + 1;
                    else attackerCount.Add(t, 1);
                }
            }

            var scan = new EnemyScan
            {
                Entities = allEnemies,
                Transforms = allEnemyTransforms,
                Factions = allEnemyFactions,
                Health = allEnemyHealth,
                Flags = _cacheFlags.AsArray(),
                Priority = _cachePrio.AsArray(),
                Tags = _cacheTags.AsArray(),
                Map = spatialMap,
                Hostile = hostile,
                FactionPresent = factionPresent,
            };

            // =============================================================================
            // PHASE 2: Auto-acquire targets for idle units
            // =============================================================================
            AutoAcquireTargets(ref state, ref ecb, in scan, in settings, ref attackerCount);

            // =============================================================================
            // PHASE 3: Return to guard point (handled after combat systems process)
            // =============================================================================
            ProcessReturnToGuard(ref state, ref ecb, in scan, in settings, ref attackerCount);

            attackerCount.Dispose();
            hostile.Dispose();
            factionPresent.Dispose();

            // =============================================================================
            // PHASE 4: Clean up stale AttackCommand components
            // =============================================================================
            CleanupStaleCommands(ref state, ref ecb);

            // =============================================================================
            // PHASE 5: Clear LastAttackerEntity to prevent stale references
            // =============================================================================
            CleanupLastAttacker(ref state, ref ecb);
        }

        /// <summary>
        /// Rebuild the peer-stable insert order, the per-enemy flags and the
        /// priority classes — unless the enemy set is exactly last tick's.
        /// </summary>
        private void RefreshEnemyCache(EntityManager em, NativeArray<Entity> allEnemies,
            NativeArray<LocalTransform> allEnemyTransforms)
        {
            if (_cacheValid != 0 && _cacheEnemies.Length == allEnemies.Length)
            {
                bool same = true;
                for (int i = 0; i < allEnemies.Length; i++)
                    if (_cacheEnemies[i] != allEnemies[i]) { same = false; break; }
                if (same) return;
            }

            int n = allEnemies.Length;
            _cacheEnemies.Clear();
            _cacheEnemies.AddRange(allEnemies);
            _cacheOrder.ResizeUninitialized(n);
            _cacheFlags.ResizeUninitialized(n);
            _cachePrio.ResizeUninitialized(n);
            _cacheTags.ResizeUninitialized(n);

            bool allNetworked = true;
            var order = new NativeArray<TargetMapKey>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++)
            {
                var e = allEnemies[i];
                long netId = long.MaxValue;
                if (em.HasComponent<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(e))
                    netId = em.GetComponentData<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(e).NetworkId;
                else
                    allNetworked = false;
                var pos = allEnemyTransforms[i].Position;
                order[i] = new TargetMapKey
                {
                    NetId = netId,
                    PosKey = ((ulong)math.asuint(pos.x) << 32) | math.asuint(pos.z),
                    Index = i,
                };

                byte flags = 0;
                if (em.HasComponent<BuildingTag>(e)) flags |= FlagBuilding;
                if (em.HasComponent<WallTag>(e)) flags |= FlagWall;
                if (em.HasComponent<WorkerTag>(e) || em.HasComponent<CanBuild>(e)) flags |= FlagWorker;
                if (em.HasComponent<HeroLevel>(e)) flags |= FlagHero;
                _cacheFlags[i] = flags;
                _cacheTags[i] = em.HasComponent<UnitTagsData>(e) ? em.GetComponentData<UnitTagsData>(e).Mask : 0u;

                // M2 (AI plan): tactical target priority per candidate. Within a
                // bounded distance band (see FindAutoTarget), units prefer
                // high-value classes — healers, siege, casters — over whatever is
                // merely nearest. Buildings and workers stay lowest.
                byte prio = 1;
                if (em.HasComponent<UnitTag>(e))
                {
                    prio = em.GetComponentData<UnitTag>(e).Class switch
                    {
                        UnitClass.Support => 5,
                        UnitClass.Magic   => 4,
                        UnitClass.Siege   => 4,
                        UnitClass.Ranged  => 3,
                        UnitClass.Melee   => 2,
                        _                 => 1,
                    };
                }
                _cachePrio[i] = prio;
            }
            order.Sort(new TargetMapKey.Order());
            for (int o = 0; o < n; o++) _cacheOrder[o] = order[o].Index;
            order.Dispose();

            _cacheValid = (byte)(allNetworked ? 1 : 0);
        }

        /// <summary>Everything the candidate scan reads, bundled so the two
        /// callers (acquire, return-to-guard) share one scan.</summary>
        private struct EnemyScan
        {
            public NativeArray<Entity> Entities;
            public NativeArray<LocalTransform> Transforms;
            public NativeArray<FactionTag> Factions;
            public NativeArray<Health> Health;
            public NativeArray<byte> Flags;
            public NativeArray<byte> Priority;
            public NativeArray<uint> Tags;
            public NativeParallelMultiHashMap<int3, int> Map;
            public NativeArray<byte> Hostile;
            public NativeArray<byte> FactionPresent;
        }

        // Cap how many MELEE attackers can target the same enemy at once.
        // Once the cap is hit, overflow melee attackers pick a different
        // nearby enemy and walk around the front-line clump to reach it.
        // Falls back to absolute-nearest if no under-cap enemy sits within
        // SpreadDistRatio × nearest, so units don't trek across the map to
        // attack a distant under-cap target when a saturated one is right
        // in front of them.
        // Does NOT apply to ranged/siege units — they fire from afar and
        // don't physically clump, so concentrating fire is fine.
        private const int MaxAttackersPerEnemy = 8;
        private const float SpreadDistRatio = 1.5f;

        // M2 (AI plan): a higher-priority candidate only wins over the nearest
        // one when it is within this ratio of the nearest distance — keeps the
        // value tie-break bounded so units never trek across the map for it.
        private const float ValuePickDistRatio = 1.25f;

        /// <summary>Lower bound for the distance a proximity RATIO is taken
        /// against. Candidate distances are surface distances, which legitimately
        /// hit 0 when a unit is touching a building — and every `x <= nearest *
        /// ratio` test degenerates to `x <= 0` there. See the use sites.</summary>
        private const float NearDistFloor = 1.5f;
    }
    /// <summary>Sort key for the targeting spatial map's peer-stable insert
    /// order (catch #14) — see the build site in OnUpdate.</summary>
    internal struct TargetMapKey
    {
        public long NetId;
        public ulong PosKey;
        public int Index;

        public struct Order : System.Collections.Generic.IComparer<TargetMapKey>
        {
            public int Compare(TargetMapKey a, TargetMapKey b)
            {
                if (a.NetId != b.NetId) return a.NetId < b.NetId ? -1 : 1;
                if (a.PosKey != b.PosKey) return a.PosKey < b.PosKey ? -1 : 1;
                return 0;
            }
        }
    }

}
