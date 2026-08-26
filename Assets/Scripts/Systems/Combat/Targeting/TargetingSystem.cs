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
    /// - Auto-acquire targets for idle units within line of sight
    /// - Initialize combat-related components (GuardPoint, AttackCooldown)
    /// - Handle return-to-guard behavior when no enemies present
    /// - Clean up stale attack commands
    /// 
    /// Respects UserMoveOrder tag to prevent interrupting player movement commands.
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
        private const float MaxGuardDistance = 10f;

        /// <summary>
        /// How far a unit may CHASE an auto-acquired target from its guard
        /// point before breaking off and going home.
        ///
        /// Deliberately much looser than <see cref="MaxGuardDistance"/> (which
        /// only governs standing still): a defender must be able to fight a
        /// real skirmish at the edge of its base without being yanked out of
        /// it mid-swing. It must NOT be able to follow a fleeing scout across
        /// the map into the enemy's garrison, which is exactly what happened
        /// with no pursuit limit at all.
        /// </summary>
        private const float MaxPursuitDistance = 30f;

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

        // Fix #207: spatial-hash cell size for the enemy scan.
        // Cell=20 means a unit with LOS<=20 only visits a 3x3 neighborhood
        // (9 cells); LOS<=40 (aggressive-stance boost) visits 5x5 (25 cells).
        // Keeps per-unit inner-loop work bounded regardless of total enemy count.
        private const float TargetingCellSize = 20f;

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var em = state.EntityManager;

            // =============================================================================
            // PHASE 0: Initialize required components for combat
            // =============================================================================
            InitializeCombatComponents(ref state, ref ecb);

            // =============================================================================
            // PHASE 1: Handle user attack commands
            // =============================================================================
            ProcessAttackCommands(ref state, ref ecb);

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
                .Build();

            using var allEnemies = enemyQuery.ToEntityArray(Allocator.Temp);
            using var allEnemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var allEnemyFactions = enemyQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var allEnemyHealth = enemyQuery.ToComponentDataArray<Health>(Allocator.Temp);

            // Fix #207: build a spatial hash so per-unit enemy scans visit
            // only nearby cells instead of every enemy in the world. Shared
            // between AutoAcquireTargets and ProcessReturnToGuard.
            using var spatialMap = new NativeParallelMultiHashMap<int2, int>(
                math.max(16, allEnemies.Length * 2), Allocator.Temp);
            for (int i = 0; i < allEnemies.Length; i++)
            {
                var pos = allEnemyTransforms[i].Position;
                var cell = new int2(
                    (int)math.floor(pos.x / TargetingCellSize),
                    (int)math.floor(pos.z / TargetingCellSize));
                spatialMap.Add(cell, i);
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

            // M2 (AI plan): tactical target priority per candidate. Within a
            // bounded distance band (see AutoAcquireTargets), units prefer
            // high-value classes — healers, siege, casters — over whatever is
            // merely nearest. Buildings and workers stay lowest.
            // Not `using var` — indexer writes on a using-variable are CS1654;
            // disposed manually right after the auto-acquire pass.
            var allEnemyPriority = new NativeArray<byte>(allEnemies.Length, Allocator.Temp);
            for (int i = 0; i < allEnemies.Length; i++)
            {
                byte prio = 1;
                if (em.HasComponent<UnitTag>(allEnemies[i]))
                {
                    var cls = em.GetComponentData<UnitTag>(allEnemies[i]).Class;
                    prio = cls switch
                    {
                        UnitClass.Support => 5,
                        UnitClass.Magic   => 4,
                        UnitClass.Siege   => 4,
                        UnitClass.Ranged  => 3,
                        UnitClass.Melee   => 2,
                        _                 => 1,
                    };
                }
                allEnemyPriority[i] = prio;
            }

            // =============================================================================
            // PHASE 2: Auto-acquire targets for idle units
            // =============================================================================
            AutoAcquireTargets(ref state, ref ecb, allEnemies, allEnemyTransforms, allEnemyFactions, allEnemyHealth, allEnemyPriority, spatialMap, ref attackerCount);
            allEnemyPriority.Dispose();

            // =============================================================================
            // PHASE 3: Return to guard point (handled after combat systems process)
            // =============================================================================
            ProcessReturnToGuard(ref state, ref ecb, allEnemies, allEnemyTransforms, allEnemyFactions, allEnemyHealth, spatialMap);

            attackerCount.Dispose();

            // =============================================================================
            // PHASE 4: Clean up stale AttackCommand components
            // =============================================================================
            CleanupStaleCommands(ref state, ref ecb);

            // =============================================================================
            // PHASE 5: Clear LastAttackerEntity to prevent stale references
            // =============================================================================
            CleanupLastAttacker(ref state, ref ecb);
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
}