using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Dual-target ranged combat system for Veilstinger veilstone units.
    ///
    /// Veilstingers fire lasers at up to two targets simultaneously:
    /// - Primary target is provided by TargetingSystem (Target component)
    /// - Secondary target is the nearest other enemy within range
    ///
    /// Behavior:
    /// - Too close (below MinRange): retreat away from target
    /// - In range (MinRange..MaxRange): aim, then fire dual lasers
    /// - Too far (above MaxRange): chase target (unless HoldPositionTag)
    ///
    /// Uses Projectile + ArrowProjectile entities for laser visuals,
    /// processed by the existing ProjectileSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct VeilstingerCombatSystem : ISystem
    {
        // Veilstinger fires SINGLE alternating shots from left/right guns
        // instead of dual lasers per cycle. Cooldown is 0.375 s — twice the
        // earlier 0.75 s cadence so the gun-alternation pulse reads as
        // continuous fire rather than discrete shots.
        private const float FireCooldown = 0.375f;

        // Bezier arc parameters. The arrow path in ProjectileSystem uses
        // proj.FlightTime as the Bezier traversal time; ~0.7s reads as a
        // clearly-arched lob at typical Veilstinger range (8–24 m).
        private const float ArcFlightTime = 0.7f;

        // Gun offset constants relative to the unit's center
        // These approximate the leftgun/rightgun child positions on the Veilstinger prefab
        private const float GunSideOffset = 0.5f;   // Left/right distance from center
        private const float GunForwardOffset = 0.3f; // Slightly in front of center
        private const float DefaultSpawnYOffset = 1.5f; // Fallback if no Radius component

        // Cached query — created once in OnCreate, reused every frame
        private EntityQuery _targetQuery;
        private EntityQuery _stingerQuery;

        // PatrolDefense-scenario targeting range. We pick up nearest enemy
        // within this radius for Veilstingers carrying PatrolTag+HoldPositionTag
        // so scenario patrol Veilstingers acquire targets without depending on
        // TargetingSystem (whose DesiredDestination-aware skip conditions
        // interact poorly with externally-driven patrol movement) and without
        // depending on cross-frame Target writes from a MonoBehaviour driver
        // (those were being zeroed out before this system's next read).
        private const float ScenarioPatrolEngageRange = 30f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _targetQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, FactionTag, Health>()
                .WithAny<UnitTag, BuildingTag>()
                .Build(ref state);
            // The main loop's own query, for the no-Veilstinger early-out.
            _stingerQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, Target, VeilstingerState, Damage, FactionTag, Health>()
                .WithAll<BorderUnitTag>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            float dt = SystemAPI.Time.DeltaTime;
            float time = (float)SimCadence.MatchTimeOr(SystemAPI.Time.ElapsedTime);
            var em = state.EntityManager;

            // PERF (2026-10-16): the candidate snapshot (every unit AND
            // building, four columns) was copied on every tick even with no
            // Veilstinger alive, and each shot then scanned all of it for the
            // secondary target. The snapshot is now taken on first need, and
            // the secondary search walks a spatial grid of the candidates.
            // The grid only narrows WHICH rows are tested: the winner is the
            // smallest distance with ties to the lowest snapshot row - exactly
            // what the old in-order strict-less scan picked. Result-identical.
            if (_stingerQuery.IsEmpty) return;
            var targets = new TargetSnapshot();

            foreach (var (transform, target, veilState, damage, faction, health, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<VeilstingerState>, RefRO<Damage>, RefRO<FactionTag>, RefRO<Health>>()
                .WithAll<BorderUnitTag>()
                .WithEntityAccess())
            {
                // Skip dead veilstingers — they may be destroyed before ECB playback
                if (health.ValueRO.Value <= 0) continue;

                ref var tgt = ref target.ValueRW;
                ref var vs = ref veilState.ValueRW;

                // Update cooldown timer
                if (vs.CooldownTimer > 0)
                {
                    vs.CooldownTimer -= dt;
                }

                // PatrolDefense scenario: if this Veilstinger carries both
                // PatrolTag and HoldPositionTag, do the targeting *here* —
                // inline, immediately before we use it — so nothing can clear
                // the write between us setting it and us reading it. Empirical
                // observation: external Target writes from MonoBehaviour or a
                // sibling ISystem were being zeroed before this system's read.
                bool isScenarioPatrol =
                    em.HasComponent<PatrolTag>(entity) && TransientState.Active<HoldPositionTag>(em, entity);
                if (isScenarioPatrol)
                {
                    targets.Ensure(_targetQuery);
                    var tgtEntities = targets.Entities;
                    var tgtTransforms = targets.Transforms;
                    var tgtFactions = targets.Factions;
                    var tgtHealth = targets.Health;
                    var selfPos = transform.ValueRO.Position;
                    var selfFaction = faction.ValueRO.Value;
                    float bestDistSq = ScenarioPatrolEngageRange * ScenarioPatrolEngageRange;
                    Entity bestEnemy = Entity.Null;

                    for (int i = 0; i < tgtEntities.Length; i++)
                    {
                        // docs/Design/Teams.md — Border is allied only with
                        // itself, so this stays a self-only skip in practice.
                        if (!Alliances.AreHostile(selfFaction, tgtFactions[i].Value)) continue;
                        if (tgtHealth[i].Value <= 0) continue;
                        var ep = tgtTransforms[i].Position;
                        float dx = ep.x - selfPos.x;
                        float dz = ep.z - selfPos.z;
                        float dsq = dx * dx + dz * dz;
                        if (dsq < bestDistSq)
                        {
                            bestDistSq = dsq;
                            bestEnemy = tgtEntities[i];
                        }
                    }

                    tgt.Value = bestEnemy;
                }

                // Validate primary target exists and is alive
                if (tgt.Value == Entity.Null || !em.Exists(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    vs.Target1 = Entity.Null;
                    vs.Target2 = Entity.Null;
                    vs.AimTimer = 0;
                    continue;
                }

                var targetHealth = em.GetComponentData<Health>(tgt.Value);
                if (targetHealth.Value <= 0)
                {
                    tgt.Value = Entity.Null;
                    vs.Target1 = Entity.Null;
                    vs.Target2 = Entity.Null;
                    vs.AimTimer = 0;
                    continue;
                }

                vs.Target1 = tgt.Value;
                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                var dist = DistXZ(myPos, targetPos);

                float minRange = vs.MinRange;
                float maxRange = vs.MaxRange;

                // High-ground rule (HeightAdvantage): effective range scales
                // per shot with the height difference to the primary target —
                // same rule the player's ranged units get in RangedCombatSystem.
                maxRange *= HeightAdvantage.Multiplier(myPos.y, targetPos.y);

                // =============================================================================
                // BEHAVIOR: Too close - RETREAT
                // =============================================================================
                if (dist < minRange)
                {
                    // Don't zero AimTimer on a brief range excursion — the
                    // turret has already tracked the target, it just can't
                    // shoot at point blank. Aim stays primed so when the
                    // target steps back into range fire is instant.
                    vs.IsFiring = 0;

                    var retreatDir = math.normalize(myPos - targetPos);
                    var retreatTarget = myPos + retreatDir * (minRange - dist + 3f);

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = retreatTarget,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = retreatTarget,
                            Has = 1
                        });
                    }
                }
                // =============================================================================
                // BEHAVIOR: In range - AIM AND FIRE dual lasers
                // =============================================================================
                else if (dist <= maxRange)
                {
                    // Attack while moving: the Veilstinger does NOT zero its
                    // own DesiredDestination on engagement. Any external
                    // movement command (patrol, manual order, formation)
                    // continues to drive the unit; only the chase branch
                    // below writes a destination, and even that respects
                    // HoldPositionTag.

                    // Accumulate aim time
                    vs.AimTimer += dt;

                    // Fire when aim is ready and cooldown is complete
                    if (vs.AimTimer >= vs.AimTimeRequired && vs.CooldownTimer <= 0)
                    {
                        vs.IsFiring = 1;
                        int dmg = damage.ValueRO.Value;
                        Faction myFaction = faction.ValueRO.Value;

                        // Veilstone buff on attacker (bonus damage)
                        if (em.HasComponent<BorderBuff>(entity))
                        {
                            var buff = em.GetComponentData<BorderBuff>(entity);
                            dmg = (int)math.round(dmg * (1f + buff.AttBonus));
                            dmg = math.max(1, dmg);
                        }

                        // Get shooter's damage type (default Magic for Veilstinger)
                        DamageType dmgType = DamageType.Magic;
                        if (em.HasComponent<DamageTypeData>(entity))
                            dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                        // Spawn height: use entity's Radius + 0.5f (taller units shoot higher)
                        float gunHeight = em.HasComponent<Radius>(entity)
                            ? em.GetComponentData<Radius>(entity).Value + 0.5f
                            : DefaultSpawnYOffset;

                        // Compute gun world positions based on facing direction
                        var facingDir = math.normalizesafe(
                            new float3(targetPos.x - myPos.x, 0, targetPos.z - myPos.z),
                            new float3(0, 0, 1));
                        var rightDir = math.cross(new float3(0, 1, 0), facingDir);

                        float3 leftGunPos = myPos
                            + facingDir * GunForwardOffset
                            - rightDir * GunSideOffset
                            + new float3(0, gunHeight, 0);

                        float3 rightGunPos = myPos
                            + facingDir * GunForwardOffset
                            + rightDir * GunSideOffset
                            + new float3(0, gunHeight, 0);

                        // Find the secondary target — nearest enemy in range
                        // that isn't the primary. Used for the right gun.
                        Entity secondTarget = Entity.Null;
                        float3 secondPos = float3.zero;
                        float bestDist = float.MaxValue;
                        targets.Ensure(_targetQuery);
                        targets.EnsureGrid();
                        {
                            var tgtEntities = targets.Entities;
                            var tgtTransforms = targets.Transforms;
                            var tgtFactions = targets.Factions;
                            var tgtHealth = targets.Health;
                            int bestRow = int.MaxValue;
                            float inv = TargetSnapshot.InvCell;
                            int x0 = (int)math.floor((myPos.x - maxRange) * inv) - 1;   // +-1 cell: float slack
                            int x1 = (int)math.floor((myPos.x + maxRange) * inv) + 1;
                            int z0 = (int)math.floor((myPos.z - maxRange) * inv) - 1;
                            int z1 = (int)math.floor((myPos.z + maxRange) * inv) + 1;
                            // An absurd range (not a real SO value) would walk
                            // millions of empty cells: scan the rows instead.
                            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 4096)
                            {
                                for (int i = 0; i < tgtEntities.Length; i++)
                                {
                                    if (!Alliances.AreHostile(myFaction, tgtFactions[i].Value)) continue;
                                    if (tgtHealth[i].Value <= 0) continue;
                                    if (tgtEntities[i] == tgt.Value) continue;
                                    float d = DistXZ(myPos, tgtTransforms[i].Position);
                                    if (d > maxRange) continue;
                                    if (d < bestDist)
                                    {
                                        bestDist = d;
                                        secondTarget = tgtEntities[i];
                                        secondPos = tgtTransforms[i].Position;
                                    }
                                }
                                z1 = z0 - 1;   // skip the grid walk below
                            }
                            for (int cz = z0; cz <= z1; cz++)
                            for (int cx = x0; cx <= x1; cx++)
                            {
                                if (!targets.Grid.TryGetFirstValue(TargetSnapshot.Key(cx, cz), out int i, out var it))
                                    continue;
                                do
                                {
                                    // docs/Design/Teams.md
                                    if (!Alliances.AreHostile(myFaction, tgtFactions[i].Value)) continue;
                                    if (tgtHealth[i].Value <= 0) continue;
                                    if (tgtEntities[i] == tgt.Value) continue;
                                    float d = DistXZ(myPos, tgtTransforms[i].Position);
                                    if (d > maxRange) continue;
                                    if (d < bestDist || (d == bestDist && i < bestRow))
                                    {
                                        bestDist = d;
                                        bestRow = i;
                                        secondTarget = tgtEntities[i];
                                        secondPos = tgtTransforms[i].Position;
                                    }
                                } while (targets.Grid.TryGetNextValue(out i, ref it));
                            }
                        }

                        // Left gun → primary target. Right gun → secondary
                        // if any, else also primary (so a single-target shot
                        // doubles up). The two arced missiles spawn in the
                        // same fire cycle. High-ground rule: each shot's
                        // damage is scaled by the height difference to ITS
                        // target (the two guns can hit different elevations).
                        // From a wall-walk the wall bonus replaces it
                        // (docs/Design/Combat_Pacing.md § Fighting from a wall-walk).
                        int primaryDmg = math.max(1, (int)math.round(dmg * CombatDamageHelper.AttackerHeightOrWallMult(em, entity, HeightAdvantage.Multiplier(myPos.y, targetPos.y))));
                        CreateArcedShotFromGun(ref ecb, leftGunPos, targetPos,
                            dist, entity, myFaction, primaryDmg, time, tgt.Value, dmgType);

                        if (secondTarget != Entity.Null)
                        {
                            vs.Target2 = secondTarget;
                            int secondDmg = math.max(1, (int)math.round(dmg * CombatDamageHelper.AttackerHeightOrWallMult(em, entity, HeightAdvantage.Multiplier(myPos.y, secondPos.y))));
                            CreateArcedShotFromGun(ref ecb, rightGunPos, secondPos,
                                bestDist, entity, myFaction, secondDmg, time, secondTarget, dmgType);
                        }
                        else
                        {
                            vs.Target2 = Entity.Null;
                            CreateArcedShotFromGun(ref ecb, rightGunPos, targetPos,
                                dist, entity, myFaction, primaryDmg, time, tgt.Value, dmgType);
                        }

                        // Reset cooldown and aim. Per-unit cadence comes from the
                        // unit's SO (attackCooldown); 0 = legacy constant.
                        vs.CooldownTimer = vs.FireCooldown > 0f ? vs.FireCooldown : FireCooldown;
                        vs.AimTimer = 0;
                        vs.IsFiring = 0;
                    }
                }
                // =============================================================================
                // BEHAVIOR: Too far - CHASE (unless holding position)
                // =============================================================================
                // Earlier this was a bare `{` block with no `else if (dist > maxRange)`
                // guard, so it ran after EVERY successful in-range shot — immediately
                // overwriting DesiredDestination to walk into the target, exiting range
                // (RETREAT branch fired next frame), and oscillating. HoldPosition was
                // also clobbered (Target was zeroed every shot). (task-058 F-1)
                else if (dist > maxRange)
                {
                    // Hold position units do NOT chase — but they DO keep
                    // tracking the target so the moment it walks back into
                    // range, fire is instant. Previously this branch zeroed
                    // both Target and AimTimer every frame the target was
                    // out of range, which combined with an inbound target
                    // meant the Veilstinger never finished an aim cycle.
                    if (TransientState.Active<HoldPositionTag>(em, entity))
                    {
                        vs.IsFiring = 0;
                        continue;
                    }

                    vs.AimTimer = 0;
                    vs.IsFiring = 0;

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                }
            }

            targets.Dispose();
        }

        /// <summary>The candidate-target columns, copied on first need, plus
        /// a lazily built XZ grid of row indices for range searches.</summary>
        private struct TargetSnapshot
        {
            public const float CellSize = 16f;
            public const float InvCell = 1f / CellSize;

            public bool Loaded;
            public NativeArray<Entity> Entities;
            public NativeArray<LocalTransform> Transforms;
            public NativeArray<FactionTag> Factions;
            public NativeArray<Health> Health;
            public bool HasGrid;
            public NativeParallelMultiHashMap<int, int> Grid;

            public static int Key(int x, int z) => (x * 73856093) ^ (z * 19349663);

            public void Ensure(EntityQuery q)
            {
                if (Loaded) return;
                Entities = q.ToEntityArray(Allocator.Temp);
                Transforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                Factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                Health = q.ToComponentDataArray<Health>(Allocator.Temp);
                Loaded = true;
            }

            public void EnsureGrid()
            {
                if (HasGrid) return;
                Grid = new NativeParallelMultiHashMap<int, int>(math.max(1, Transforms.Length), Allocator.Temp);
                for (int i = 0; i < Transforms.Length; i++)
                {
                    var p = Transforms[i].Position;
                    Grid.Add(Key((int)math.floor(p.x * InvCell), (int)math.floor(p.z * InvCell)), i);
                }
                HasGrid = true;
            }

            public void Dispose()
            {
                if (Loaded)
                {
                    Entities.Dispose(); Transforms.Dispose(); Factions.Dispose(); Health.Dispose();
                }
                if (HasGrid) Grid.Dispose();
            }
        }

        /// <summary>
        /// Create an arced Veilstinger missile from a specific gun position.
        /// No <see cref="LaserProjectileTag"/> — projectile follows the arrow
        /// Bezier path in <c>ProjectileSystem</c> (auto-hit, arches naturally).
        /// The <see cref="VeilstingerProjectileTag"/> drives both the visual
        /// (small arcane missile) and the impact-explosion VFX.
        /// </summary>
        private static void CreateArcedShotFromGun(ref EntityCommandBuffer ecb, float3 gunPos, float3 targetPos,
            float distance, Entity shooter, Faction faction, int damage, float time, Entity targetEntity,
            DamageType dmgType = DamageType.Magic)
        {
            // Initial velocity along the line of sight — ProjectileSystem
            // re-computes the actual Bezier velocity each frame, but we set
            // an initial direction so the rotation at spawn faces the arc's
            // launch axis.
            float3 dir = targetPos - gunPos;
            var direction = math.normalizesafe(dir, new float3(0, 0, 1));

            var missile = ecb.CreateEntity();

            ecb.AddComponent(missile, new LocalTransform
            {
                Position = gunPos,
                Rotation = quaternion.LookRotation(direction, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.AddComponent(missile, new ArrowProjectile
            {
                Velocity = direction, // overwritten each frame by Bezier tangent
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = true
            });

            ecb.AddComponent(missile, new Projectile
            {
                Start = gunPos,
                End = targetPos,
                StartTime = time,
                FlightTime = ArcFlightTime,
                Damage = damage,
                Target = targetEntity,
                Faction = faction,
                DmgType = dmgType
            });

            // No LaserProjectileTag → ProjectileSystem routes through the
            // arrow/Bezier path which auto-hits on arrival and ends in
            // ApplyDamage (the impact VFX is then spawned by the visual
            // system when the entity is destroyed).
            ecb.AddComponent<VeilstingerProjectileTag>(missile);
        }

    }
}
