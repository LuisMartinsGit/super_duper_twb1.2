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

        /// <summary><see cref="IsOnRampart(EntityManager, Entity)"/> through
        /// the combat loops' shared lookups.</summary>
        public static bool IsOnRampart(in CombatLookups lk, Entity unit)
            => lk.NavLayer.TryGetComponent(unit, out var nav) && nav.Layer == NavLayerIndex.LayerRampart;

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
        /// <summary>FRIENDLY FIRE (Sects.md, 2026-10-18): one more bucket in the
        /// enemy index, after the nine factions, holding every frenzied unit
        /// (SectDisordered). Its row and column of the hostility table are all
        /// hostile, so every scanner — the frenzied unit's own side included —
        /// sees it as an enemy, and a frenzied scanner (scanning as this row)
        /// sees everyone — units and buildings, its own side's included
        /// (2026-10-19). The unit's FactionTag never changes.</summary>
        private const int RogueSlot = FactionSlots;
        private const int GridSlots = FactionSlots + 1;

        // ── Per-enemy snapshot flags (cached with the enemy set) ─────────
        private const byte FlagBuilding = 1;
        private const byte FlagWall = 2;
        private const byte FlagWorker = 4;   // WorkerTag or CanBuild (Border's guard rule)
        private const byte FlagHero = 8;     // HeroLevel (TargetPreference "Hero")
        private const byte FlagCivilian = 16; // Runai caravan / escort / trader-warrior (Veiled Pilgrims)
        private const byte FlagFlying = 32;   // FlyingTag: only ranged units and ballistas may pick it

        /// <summary>A FLYING target (docs/Design/Age_1_Runai.md §6.4) can be hit
        /// only by ranged units, ballistas and shooting buildings — never melee,
        /// never a lobbing siege engine.</summary>
        public static bool CanHitFlying(EntityManager em, Entity attacker)
        {
            if (em.HasComponent<BuildingTag>(attacker)) return true;
            if (!em.HasComponent<ArcherTag>(attacker)) return false;
            return !em.HasComponent<SiegeTag>(attacker) || em.HasComponent<BallistaShooterTag>(attacker);
        }

        /// <summary><see cref="CanHitFlying(EntityManager, Entity)"/> through
        /// the combat loops' shared lookups.</summary>
        public static bool CanHitFlying(in CombatLookups lk, Entity attacker)
        {
            if (lk.Building.HasComponent(attacker)) return true;
            if (!lk.Archer.HasComponent(attacker)) return false;
            return !lk.Siege.HasComponent(attacker) || lk.BallistaShooter.HasComponent(attacker);
        }

        // ── Cross-frame cache of the enemy set's peer-stable order ────────
        // Rebuilding the order needs a NetworkedEntity lookup per enemy plus
        // an O(N log N) sort, every frame, while the SET of enemies changes
        // only when something spawns, dies or changes archetype. The cache is
        // keyed on the exact entity array (same entities, same chunk order):
        // any difference rebuilds it. Unnetworked enemies (whose position
        // tiebreak changes as they move) sort to the tail and only that tail
        // is re-sorted per tick — see _cacheNetworkedCount.
        private NativeList<Entity> _cacheEnemies;
        private NativeList<int> _cacheOrder;
        private NativeList<byte> _cacheFlags;
        private NativeList<byte> _cachePrio;
        /// <summary>Per-enemy UnitTagsData mask (TargetPreference rules).</summary>
        private NativeList<uint> _cacheTags;
        private byte _cacheValid;
        /// <summary>How many entries at the FRONT of _cacheOrder are
        /// networked. They sort by NetworkId, which never changes, so that
        /// prefix stays valid while the set does; only the unnetworked tail
        /// (NetworkId = max, ordered by position) has to be re-sorted as its
        /// members move. See RefreshEnemyCache.</summary>
        private int _cacheNetworkedCount;

        // ── Flat (cell, faction) enemy grid (perf pass 2026-10-16) ──────
        // Replaces the per-tick NativeParallelMultiHashMap: one contiguous
        // EnemyCand record per enemy, counting-sorted into its bucket, in
        // persistent lists. See BuildEnemyGrid for the visit-order contract.
        private NativeList<EnemyCand> _gridCands;
        private NativeList<int> _gridStart;
        private NativeList<int> _gridCursor;
        private NativeList<int> _gridBucketOf;
        private NativeHashMap<int3, int> _gridSparse;

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

            _gridCands = new NativeList<EnemyCand>(256, Allocator.Persistent);
            _gridStart = new NativeList<int>(1024, Allocator.Persistent);
            _gridCursor = new NativeList<int>(1024, Allocator.Persistent);
            _gridBucketOf = new NativeList<int>(256, Allocator.Persistent);
            _gridSparse = new NativeHashMap<int3, int>(256, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_cacheEnemies.IsCreated) _cacheEnemies.Dispose();
            if (_cacheOrder.IsCreated) _cacheOrder.Dispose();
            if (_cacheFlags.IsCreated) _cacheFlags.Dispose();
            if (_cachePrio.IsCreated) _cachePrio.Dispose();
            if (_cacheTags.IsCreated) _cacheTags.Dispose();
            if (_gridCands.IsCreated) _gridCands.Dispose();
            if (_gridStart.IsCreated) _gridStart.Dispose();
            if (_gridCursor.IsCreated) _gridCursor.Dispose();
            if (_gridBucketOf.IsCreated) _gridBucketOf.Dispose();
            if (_gridSparse.IsCreated) _gridSparse.Dispose();
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

            using var allEnemies = enemyQuery.ToEntityArray(Allocator.TempJob);
            using var allEnemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using var allEnemyFactions = enemyQuery.ToComponentDataArray<FactionTag>(Allocator.TempJob);
            using var allEnemyHealth = enemyQuery.ToComponentDataArray<Health>(Allocator.TempJob);

            // Lookups for the per-enemy reads (2026-10-15 perf pass): an
            // EntityManager.HasComponent walks the archetype's ~100 types per
            // call; a lookup caches the archetype, and consecutive enemies
            // share one (chunk order), so most reads are O(1). Pure reads —
            // every answer is the one the EntityManager gave.
            var lk = new EnemyLookups
            {
                Net = SystemAPI.GetComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(true),
                Building = SystemAPI.GetComponentLookup<BuildingTag>(true),
                Wall = SystemAPI.GetComponentLookup<WallTag>(true),
                Worker = SystemAPI.GetComponentLookup<WorkerTag>(true),
                CanBuild = SystemAPI.GetComponentLookup<CanBuild>(true),
                Hero = SystemAPI.GetComponentLookup<HeroLevel>(true),
                Caravan = SystemAPI.GetComponentLookup<CaravanTag>(true),
                TraderWarrior = SystemAPI.GetComponentLookup<RunaiTraderWarrior>(true),
                Escort = SystemAPI.GetComponentLookup<RunaiEscort>(true),
                Flying = SystemAPI.GetComponentLookup<FlyingTag>(true),
                UnitTags = SystemAPI.GetComponentLookup<UnitTagsData>(true),
                Unit = SystemAPI.GetComponentLookup<UnitTag>(true),
            };
            RefreshEnemyCache(allEnemies, allEnemyTransforms, in lk);

            // Per-enemy footprint, read ONCE per tick (N lookups) instead of
            // once per scanner x candidate through the EntityManager. Read
            // every tick, not cached with the flags: a value can change
            // without the enemy set changing. Pad is the footprint's farthest
            // reach from the pivot — the box's half-diagonal, the circle's
            // radius — for the cheap reject in FindAutoTarget. Folded into
            // the grid build below (one EnemyCand per enemy).
            var sizeLookup = SystemAPI.GetComponentLookup<BuildingSize>(true);
            var radiusLookup = SystemAPI.GetComponentLookup<Radius>(true);

            // Hostility table, once per tick instead of a team lookup per
            // candidate. Burst-safe variant (SharedStatic team table).
            var hostile = new NativeArray<byte>(GridSlots * GridSlots, Allocator.TempJob);
            for (int a = 0; a < GridSlots; a++)
                for (int b = 0; b < GridSlots; b++)
                    hostile[a * GridSlots + b] = (byte)(a == RogueSlot || b == RogueSlot
                        || Alliances.AreHostileBurst((Faction)a, (Faction)b) ? 1 : 0);

            // Fix #207 spatial index, keyed PER FACTION as well as per cell:
            // a unit's neighbourhood is mostly its own army, and bucketing by
            // owner lets the scan skip every friendly entry outright instead
            // of rejecting them one at a time.
            //
            // PEER-STABLE INSERT ORDER (2026-09-05, MP harness catch #14 —
            // the same disease and cure as the nav spatial hash, catch #4).
            // ToEntityArray arrives in CHUNK-WALK order, which legitimately
            // differs between lockstep peers (host-only AI structural changes
            // reshuffle the host's chunks), and a bucket's visit order IS the
            // order the acquire scan sees candidates in. Against a grid-snapped
            // base two buildings sit at EXACTLY equal surface distance, the
            // strict '<' keeps the first-seen one, and twelve besiegers picked
            // different targets per peer (tick 43790 fork). Entries go in by
            // NetworkId — the one identity every peer agrees on — with position
            // bits as the tiebreak for anything unnetworked. (Order held in
            // _cacheOrder; see RefreshEnemyCache.)
            //
            // FLAT GRID (perf pass 2026-10-16). This used to be a per-tick
            // NativeParallelMultiHashMap. Its per-key visit order was the
            // REVERSE of insertion (Add pushes onto the bucket chain's head);
            // BuildEnemyGrid lays every bucket out in exactly that order, so
            // every scan visits the same candidates in the same order as
            // before and every pick is unchanged — it just walks one
            // contiguous record per candidate instead of a hash chain.
            var factionPresent = new NativeArray<byte>(GridSlots, Allocator.TempJob);
            var disorderedLookup = SystemAPI.GetComponentLookup<SectDisordered>(true);
            var rogue = new NativeArray<byte>(allEnemies.Length, Allocator.TempJob);
            for (int i = 0; i < allEnemies.Length; i++)
                rogue[i] = (byte)(disorderedLookup.HasComponent(allEnemies[i])
                                  && disorderedLookup.IsComponentEnabled(allEnemies[i]) ? 1 : 0);
            var grid = BuildEnemyGrid(allEnemies, allEnemyTransforms, allEnemyFactions, allEnemyHealth,
                in sizeLookup, in radiusLookup, factionPresent, rogue);
            rogue.Dispose();

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
                Grid = grid,
                Hostile = hostile,
                FactionPresent = factionPresent,
                Stealth = SystemAPI.GetComponentLookup<StealthTag>(true),
                StealthRevealed = SystemAPI.GetComponentLookup<StealthRevealed>(true),
                Targets = SystemAPI.GetComponentLookup<Target>(true),
            };

            // =============================================================================
            // PHASE 2: Auto-acquire targets for idle units
            // PHASE 3: Return to guard point (handled after combat systems process)
            // Both planned in parallel Burst jobs, applied in order (Jobs.cs).
            // =============================================================================
            AutoAcquireAndReturnToGuard(ref state, ref ecb, in scan, in settings, ref attackerCount);

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
        ///
        /// An UNNETWORKED enemy used to invalidate the whole cache every tick
        /// (its position tiebreak changes as it moves), so one stray entity
        /// without a NetworkedEntity cost a full rebuild — ~13 component reads
        /// per enemy plus an N log N sort — for the rest of the match. Its
        /// order key is (NetworkId = max, position), so every unnetworked
        /// enemy sorts AFTER every networked one; the networked prefix never
        /// moves while the set holds, and only the unnetworked tail is
        /// re-sorted on its current positions (2026-10-15). The resulting
        /// order is the full sort's order.
        /// </summary>
        private void RefreshEnemyCache(NativeArray<Entity> allEnemies,
            NativeArray<LocalTransform> allEnemyTransforms, in EnemyLookups lk)
        {
            if (_cacheValid != 0 && _cacheEnemies.Length == allEnemies.Length)
            {
                bool same = true;
                for (int i = 0; i < allEnemies.Length; i++)
                    if (_cacheEnemies[i] != allEnemies[i]) { same = false; break; }
                if (same)
                {
                    int tail = allEnemies.Length - _cacheNetworkedCount;
                    if (tail > 1)
                    {
                        var tailKeys = new NativeArray<TargetMapKey>(tail, Allocator.Temp,
                            NativeArrayOptions.UninitializedMemory);
                        for (int t = 0; t < tail; t++)
                        {
                            int idx = _cacheOrder[_cacheNetworkedCount + t];
                            var pos = allEnemyTransforms[idx].Position;
                            tailKeys[t] = new TargetMapKey
                            {
                                NetId = long.MaxValue,
                                PosKey = ((ulong)math.asuint(pos.x) << 32) | math.asuint(pos.z),
                                Index = idx,
                            };
                        }
                        tailKeys.Sort(new TargetMapKey.Order());
                        for (int t = 0; t < tail; t++) _cacheOrder[_cacheNetworkedCount + t] = tailKeys[t].Index;
                        tailKeys.Dispose();
                    }
                    return;
                }
            }

            int n = allEnemies.Length;
            _cacheEnemies.Clear();
            _cacheEnemies.AddRange(allEnemies);
            _cacheOrder.ResizeUninitialized(n);
            _cacheFlags.ResizeUninitialized(n);
            _cachePrio.ResizeUninitialized(n);
            _cacheTags.ResizeUninitialized(n);

            int networked = 0;
            var order = new NativeArray<TargetMapKey>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++)
            {
                var e = allEnemies[i];
                long netId = long.MaxValue;
                if (lk.Net.TryGetComponent(e, out var net))
                {
                    netId = net.NetworkId;
                    networked++;
                }
                var pos = allEnemyTransforms[i].Position;
                order[i] = new TargetMapKey
                {
                    NetId = netId,
                    PosKey = ((ulong)math.asuint(pos.x) << 32) | math.asuint(pos.z),
                    Index = i,
                };

                byte flags = 0;
                if (lk.Building.HasComponent(e)) flags |= FlagBuilding;
                if (lk.Wall.HasComponent(e)) flags |= FlagWall;
                if (lk.Worker.HasComponent(e) || lk.CanBuild.HasComponent(e)) flags |= FlagWorker;
                if (lk.Hero.HasComponent(e)) flags |= FlagHero;
                bool caravan = lk.Caravan.HasComponent(e);
                if (caravan || lk.TraderWarrior.HasComponent(e)
                    || lk.Escort.HasComponent(e)) flags |= FlagCivilian;
                if (lk.Flying.HasComponent(e)) flags |= FlagFlying;
                _cacheFlags[i] = flags;
                _cacheTags[i] = lk.UnitTags.TryGetComponent(e, out var tags) ? tags.Mask : 0u;

                // M2 (AI plan): tactical target priority per candidate. Within a
                // bounded distance band (see FindAutoTarget), units prefer
                // high-value classes — healers, siege, casters — over whatever is
                // merely nearest. Buildings and workers stay lowest.
                byte prio = 1;
                if (lk.Unit.TryGetComponent(e, out var unit))
                {
                    prio = unit.Class switch
                    {
                        UnitClass.Support => 5,
                        UnitClass.Magic   => 4,
                        UnitClass.Siege   => 4,
                        UnitClass.Ranged  => 3,
                        UnitClass.Melee   => 2,
                        _                 => 1,
                    };
                    // A Runai caravan wagon is worth breaking off for (§9).
                    if (caravan) prio = 4;
                }
                _cachePrio[i] = prio;
            }
            order.Sort(new TargetMapKey.Order());
            for (int o = 0; o < n; o++) _cacheOrder[o] = order[o].Index;
            order.Dispose();

            // Networked keys (NetworkId is an int) all sort before the
            // unnetworked long.MaxValue ones, so the first `networked`
            // entries are exactly the networked enemies.
            _cacheNetworkedCount = networked;
            _cacheValid = 1;
        }

        /// <summary>Everything the candidate scan reads, bundled so the two
        /// callers (acquire, return-to-guard) share one scan.</summary>
        private struct EnemyScan
        {
            /// <summary>The (cell, faction) buckets and their candidates.</summary>
            public EnemyGrid Grid;
            [ReadOnly] public NativeArray<byte> Hostile;
            [ReadOnly] public NativeArray<byte> FactionPresent;

            // Live per-candidate reads for the candidates that survive the
            // range test.
            [ReadOnly] public ComponentLookup<StealthTag> Stealth;
            [ReadOnly] public ComponentLookup<StealthRevealed> StealthRevealed;
            [ReadOnly] public ComponentLookup<Target> Targets;

            /// <summary>Stealthed and not exposed — the two
            /// TransientState.Active tests, through lookups.</summary>
            public bool IsHidden(Entity e)
                => Stealth.HasComponent(e) && Stealth.IsComponentEnabled(e)
                   && !(StealthRevealed.HasComponent(e) && StealthRevealed.IsComponentEnabled(e));
        }

        /// <summary>One enemy as the scan reads it — every per-candidate
        /// value the old parallel arrays held, side by side, so a bucket walk
        /// is one contiguous read. Values are this tick's snapshot.</summary>
        private struct EnemyCand
        {
            public float3 Pos;
            public float Pad;
            public float HalfW;
            public float HalfH;
            public float Radius;
            public int Health;
            public Entity E;
            public uint Tags;
            public byte Flags;
            public byte Prio;
            public byte IsBox;

            /// <summary>TargetGeometry.SurfaceDistXZ for this enemy: the same
            /// arithmetic on the same values as the old per-tick arrays, so the
            /// same float.</summary>
            public float SurfaceDist(float3 fromPos)
            {
                if (IsBox != 0)
                {
                    float dx = math.max(math.abs(fromPos.x - Pos.x) - HalfW, 0f);
                    float dz = math.max(math.abs(fromPos.z - Pos.z) - HalfH, 0f);
                    return math.sqrt(dx * dx + dz * dz);
                }
                return math.max(DistXZ(fromPos, Pos) - Radius, 0f);
            }
        }

        /// <summary>
        /// The enemy index: candidates counting-sorted into (cell, faction)
        /// buckets. Dense table over the occupied cell rectangle; a sparse
        /// key → bucket map only when that rectangle would be huge (a stray
        /// entity far off the map). Either way a bucket holds its enemies in
        /// the order the old multimap visited them (see BuildEnemyGrid).
        /// </summary>
        private struct EnemyGrid
        {
            [ReadOnly] public NativeArray<EnemyCand> Cands;
            /// <summary>Bucket b's candidates are Cands[Start[b] .. Start[b+1]).</summary>
            [ReadOnly] public NativeArray<int> Start;
            [ReadOnly] public NativeHashMap<int3, int> Sparse;
            public byte Dense;
            public int MinCx, MinCz, CellsX, CellsZ;

            /// <summary>The candidate range of one (cellX, cellZ, faction)
            /// key; false when the key holds nobody.</summary>
            public bool Range(int3 key, out int k0, out int k1)
            {
                k0 = 0; k1 = 0;
                int b;
                if (Dense != 0)
                {
                    long cx = (long)key.x - MinCx, cz = (long)key.y - MinCz;
                    if (cx < 0 || cx >= CellsX || cz < 0 || cz >= CellsZ
                        || key.z < 0 || key.z >= GridSlots) return false;
                    b = ((int)cz * CellsX + (int)cx) * GridSlots + key.z;
                }
                else if (!Sparse.TryGetValue(key, out b)) return false;
                k0 = Start[b];
                k1 = Start[b + 1];
                return k1 > k0;
            }

            /// <summary>How many enemies one key holds (the old CountValuesForKey).</summary>
            public int Count(int3 key) => Range(key, out int k0, out int k1) ? k1 - k0 : 0;
        }

        /// <summary>Above this many (cell x faction) buckets the index goes
        /// sparse. Loop resolution, not a tunable.</summary>
        private const long MaxDenseBuckets = 1L << 20;

        /// <summary>
        /// Build the enemy index for this tick into the persistent lists.
        ///
        /// VISIT-ORDER CONTRACT. The old NativeParallelMultiHashMap received
        /// the enemies in _cacheOrder order and its per-key walk returned them
        /// newest-first (Add links each entry at the head of its bucket chain;
        /// the map never grew, so nothing re-linked). Every bucket here is laid
        /// out in exactly that order — descending _cacheOrder position — so
        /// each scan meets the same candidates in the same sequence and every
        /// strict-'&lt;' tie resolves as it did. Enemies with an out-of-table
        /// faction are left out, as before.
        /// </summary>
        private EnemyGrid BuildEnemyGrid(NativeArray<Entity> ents, NativeArray<LocalTransform> xf,
            NativeArray<FactionTag> fac, NativeArray<Health> hp,
            in ComponentLookup<BuildingSize> sizeLk, in ComponentLookup<Radius> radLk,
            NativeArray<byte> factionPresent, NativeArray<byte> rogue)
        {
            int n = _cacheOrder.Length;
            _gridBucketOf.ResizeUninitialized(math.max(1, n));

            // Pass 1: cell bounds of the inserted enemies.
            int minCx = int.MaxValue, minCz = int.MaxValue, maxCx = int.MinValue, maxCz = int.MinValue;
            int inserted = 0;
            for (int o = 0; o < n; o++)
            {
                int i = _cacheOrder[o];
                int f = (int)fac[i].Value;
                if (f < 0 || f >= FactionSlots) continue;
                if (rogue[i] != 0) f = RogueSlot;
                var pos = xf[i].Position;
                int cx = (int)math.floor(pos.x / TargetingCellSize);
                int cz = (int)math.floor(pos.z / TargetingCellSize);
                minCx = math.min(minCx, cx); maxCx = math.max(maxCx, cx);
                minCz = math.min(minCz, cz); maxCz = math.max(maxCz, cz);
                factionPresent[f] = 1;
                inserted++;
            }

            long cellsX = inserted > 0 ? (long)maxCx - minCx + 1 : 0;
            long cellsZ = inserted > 0 ? (long)maxCz - minCz + 1 : 0;
            long denseBuckets = cellsX * cellsZ * GridSlots;
            bool dense = denseBuckets <= MaxDenseBuckets;

            // Pass 2: each inserted enemy's bucket.
            int bucketCount = 0;
            if (dense) bucketCount = (int)denseBuckets;
            else _gridSparse.Clear();
            for (int o = 0; o < n; o++)
            {
                int i = _cacheOrder[o];
                int f = (int)fac[i].Value;
                if (f < 0 || f >= FactionSlots) { _gridBucketOf[o] = -1; continue; }
                if (rogue[i] != 0) f = RogueSlot;
                var pos = xf[i].Position;
                int cx = (int)math.floor(pos.x / TargetingCellSize);
                int cz = (int)math.floor(pos.z / TargetingCellSize);
                int b;
                if (dense)
                {
                    b = ((cz - minCz) * (int)cellsX + (cx - minCx)) * GridSlots + f;
                }
                else
                {
                    var key = new int3(cx, cz, f);
                    if (!_gridSparse.TryGetValue(key, out b))
                    {
                        b = bucketCount++;
                        _gridSparse.Add(key, b);
                    }
                }
                _gridBucketOf[o] = b;
            }

            // Counting sort: sizes, prefix sums, then fill each bucket from
            // its END while walking the order forwards — the bucket ends up
            // newest-first, the multimap's order.
            _gridStart.ResizeUninitialized(bucketCount + 1);
            for (int b = 0; b <= bucketCount; b++) _gridStart[b] = 0;
            for (int o = 0; o < n; o++)
            {
                int b = _gridBucketOf[o];
                if (b >= 0) _gridStart[b + 1] = _gridStart[b + 1] + 1;
            }
            for (int b = 0; b < bucketCount; b++) _gridStart[b + 1] = _gridStart[b + 1] + _gridStart[b];
            _gridCursor.ResizeUninitialized(math.max(1, bucketCount));
            for (int b = 0; b < bucketCount; b++) _gridCursor[b] = _gridStart[b + 1];

            _gridCands.ResizeUninitialized(math.max(1, inserted));
            for (int o = 0; o < n; o++)
            {
                int b = _gridBucketOf[o];
                if (b < 0) continue;
                int at = _gridCursor[b] - 1;
                _gridCursor[b] = at;

                int i = _cacheOrder[o];
                var e = ents[i];
                var c = new EnemyCand
                {
                    Pos = xf[i].Position,
                    Health = hp[i].Value,
                    E = e,
                    Tags = _cacheTags[i],
                    Flags = _cacheFlags[i],
                    Prio = _cachePrio[i],
                };
                // The footprint, exactly as the old per-tick arrays held it.
                if (sizeLk.TryGetComponent(e, out var bs))
                {
                    float hw = bs.Width * 0.5f, hh = bs.Height * 0.5f;
                    c.IsBox = 1;
                    c.HalfW = hw;
                    c.HalfH = hh;
                    c.Radius = 0f;
                    c.Pad = math.sqrt(hw * hw + hh * hh);
                }
                else
                {
                    float r = radLk.TryGetComponent(e, out var rad) ? rad.Value : 0f;
                    c.IsBox = 0;
                    c.HalfW = 0f;
                    c.HalfH = 0f;
                    c.Radius = r;
                    c.Pad = r;
                }
                _gridCands[at] = c;
            }

            return new EnemyGrid
            {
                Cands = _gridCands.AsArray(),
                Start = _gridStart.AsArray(),
                Sparse = _gridSparse,
                Dense = (byte)(dense ? 1 : 0),
                MinCx = minCx,
                MinCz = minCz,
                CellsX = (int)cellsX,
                CellsZ = (int)cellsZ,
            };
        }

        /// <summary>The flag lookups RefreshEnemyCache reads, built in OnUpdate.</summary>
        private struct EnemyLookups
        {
            [ReadOnly] public ComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity> Net;
            [ReadOnly] public ComponentLookup<BuildingTag> Building;
            [ReadOnly] public ComponentLookup<WallTag> Wall;
            [ReadOnly] public ComponentLookup<WorkerTag> Worker;
            [ReadOnly] public ComponentLookup<CanBuild> CanBuild;
            [ReadOnly] public ComponentLookup<HeroLevel> Hero;
            [ReadOnly] public ComponentLookup<CaravanTag> Caravan;
            [ReadOnly] public ComponentLookup<RunaiTraderWarrior> TraderWarrior;
            [ReadOnly] public ComponentLookup<RunaiEscort> Escort;
            [ReadOnly] public ComponentLookup<FlyingTag> Flying;
            [ReadOnly] public ComponentLookup<UnitTagsData> UnitTags;
            [ReadOnly] public ComponentLookup<UnitTag> Unit;
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
