// Handles ranged attacks for buildings (Hall, Fiendstone Keep, etc.)
// Buildings auto-target and fire at enemies within range.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct BuildingCombatSystem : ISystem
    {
        // 25 -> 32.5 (2026-09-03): arrow flight sped up 30 percent.
        private const float ArrowSpeed = 32.5f;
        private const float LaserSpeed = 55f;

        // Border emplacements only fire on worker-class units inside this
        // range (guard-the-well rule, Curse & Shardroot canon §2.1) — the
        // crystal fields ring the wells at 24–32 m, comfortably outside.
        private const float BorderWorkerGraceRange = 9f;

        // ── Target grid (2026-10-15 perf pass) ────────────────────────────
        // Loop resolution, not tunables: the cell edge, the cap on cells per
        // axis (a stray far-off entity grows the cell instead of the table),
        // and the owner slots — 8 players + Border, plus one "other" slot
        // whose hostility is decided per candidate.
        private const float GridCellSize = 16f;
        private const float GridMaxCellsPerAxis = 256f;
        private const int FactionSlots = 9;
        private const int GridSlots = FactionSlots + 1;

        // factionFlags bits (see FactionFlagsFor).
        private const byte FlagKnown = 1;
        private const byte FlagSlits = 2;
        private const byte FlagAdditionalTowers = 4;
        private const byte FlagBallista = 8;
        private const byte FlagTrebuchet = 16;

        // Every BuildingSize / every Radius in the world, for the footprint
        // upper bound (MaxFootprintPad).
        private EntityQuery _sizeQuery;
        private EntityQuery _radiusQuery;

        // Per-candidate reads, through lookups (2026-10-15 perf pass): an
        // EntityManager.HasComponent walks the archetype's ~100 types; a
        // lookup caches the archetype and answers a repeat in O(1). Pure
        // reads — every answer is the one the EntityManager gave.
        private ComponentLookup<BuildingSize> _sizeLk;
        private ComponentLookup<Radius> _radiusLk;
        private ComponentLookup<WallTag> _wallLk;
        private ComponentLookup<WorkerTag> _workerLk;
        private ComponentLookup<CanBuild> _canBuildLk;
        private ComponentLookup<RitualState> _ritualLk;
        private ComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity> _netLk;

        // Projectiles are created whole from these archetypes: one
        // structural step at playback instead of create + three or more
        // adds. Same component set, same creation order.
        private EntityArchetype _arrowArch;
        private EntityArchetype _laserArch;
        private EntityArchetype _boltArch;
        private EntityArchetype _shellArch;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingRangedAttack>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _sizeLk = state.GetComponentLookup<BuildingSize>(true);
            _radiusLk = state.GetComponentLookup<Radius>(true);
            _wallLk = state.GetComponentLookup<WallTag>(true);
            _workerLk = state.GetComponentLookup<WorkerTag>(true);
            _canBuildLk = state.GetComponentLookup<CanBuild>(true);
            _ritualLk = state.GetComponentLookup<RitualState>(true);
            _netLk = state.GetComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(true);

            _sizeQuery = state.GetEntityQuery(ComponentType.ReadOnly<BuildingSize>());
            _radiusQuery = state.GetEntityQuery(ComponentType.ReadOnly<Radius>());

            var em = state.EntityManager;
            _arrowArch = em.CreateArchetype(typeof(LocalTransform), typeof(ArrowProjectile), typeof(Projectile));
            _laserArch = em.CreateArchetype(typeof(LocalTransform), typeof(ArrowProjectile), typeof(Projectile),
                typeof(LaserProjectileTag));
            _boltArch = em.CreateArchetype(typeof(LocalTransform), typeof(ArrowProjectile), typeof(Projectile),
                typeof(BallistaBoltTag));
            _shellArch = em.CreateArchetype(typeof(LocalTransform), typeof(ArrowProjectile), typeof(Projectile),
                typeof(AOEProjectile), typeof(HighArcProjectile), typeof(TrebuchetStoneTag));
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            float time = (float)SimCadence.MatchTimeOr(SystemAPI.Time.ElapsedTime);
            var em = state.EntityManager;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Snapshot all potential targets (anything with Health + FactionTag
            // + Transform) — MINUS the three things nothing may auto-acquire.
            //
            // THE SAME EXCLUSIONS TargetingSystem APPLIES (2026-09-08). This
            // query had none at all, so every Hall, tower and Keep in the game
            // was shooting things units are forbidden to shoot:
            //
            //   * NodeNoAutoAcquire — a verb WELL. Reported from a live match:
            //     Halls were shooting wells down with arrows, which deletes the
            //     Purify / Pacify / Corrupt objective and the Shardroot with it.
            //     A well is cracked open by a Feraldis Corruptor's ritual and
            //     by nothing else (CorruptionRitualSystem removes the tag);
            //     until then it is not a target for anyone.
            //   * NodeUntargetable — a rubble / rebuilding / cleansed node.
            //     An immune husk, so firing at it was pure wasted volleys.
            //   * SectVeiled — Stoneveil (Fortitude) makes a unit untargetable.
            //     Towers ignored the veil the player had just paid for.
            var targetQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, Health>()
                .WithNone<NodeUntargetable, NodeNoAutoAcquire>()
                .WithNone<SectVeiled>()
                .Build();

            // PASS 1 — tick cooldowns, collect the towers ready to fire, in
            // query order (2026-09-25). The four-array snapshot of every
            // target in the world used to be taken every tick even when every
            // tower was mid-cooldown, which late game is nearly every tick.
            // Burst IJobEntity Run() on the main thread (perf pass
            // 2026-10-16): the same query, the same chunk order, as the
            // managed foreach it replaces.
            var ready = new NativeList<Entity>(16, Allocator.TempJob);
            new TickTowerCooldownJob { Dt = dt, Ready = ready }.Run();

            if (ready.Length == 0)
            {
                ready.Dispose();
                return;
            }

            // A ready tower that finds nothing waits this long (sim time, so
            // identical on every peer) instead of re-scanning every tick.
            var cfg = BuildingCombatSystemConfig.I;
            float noTargetRetry = cfg != null ? cfg.noTargetRetryDelay : 0f;

            var tgtEntities = targetQuery.ToEntityArray(Allocator.TempJob);
            var tgtTransforms = targetQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var tgtFactions = targetQuery.ToComponentDataArray<FactionTag>(Allocator.TempJob);
            var tgtHealth = targetQuery.ToComponentDataArray<Health>(Allocator.TempJob);
            int nTargets = tgtEntities.Length;

            // An upper bound on every footprint's reach, for the cell sweep
            // and the first reject: the largest BuildingSize diagonal and the
            // largest Radius anywhere in the world. A superset of the targets,
            // so the bound can only be generous, never short.
            float maxPad = MaxFootprintPad(ref state);

            // ── Uniform grid of the targets (2026-10-15) ─────────────────
            // Every ready tower used to walk all ~2,300 targets. Now each
            // visits only the cells inside its reach. Bucketed by cell AND
            // owner slot (0..8 = the players + Border, 9 = anything else), so
            // a tower skips allied buckets outright. The visit order does not
            // matter for the result: the pick is a total order (distance, then
            // NetworkId, then position bits), so the same targets win on every
            // peer whatever order the buckets hold. Built in Burst.
            var bounds = new NativeReference<float4>(Allocator.TempJob);
            var boundsJob = new GridBoundsJob { Transforms = tgtTransforms, Bounds = bounds };
            boundsJob.Run();
            float4 bb = bounds.Value;
            bounds.Dispose();
            float minX = bb.x, minZ = bb.y, maxX = bb.z, maxZ = bb.w;
            if (nTargets == 0) { minX = minZ = maxX = maxZ = 0f; }
            // Grow the cell when the spread is huge (a stray entity far off
            // the map), so the table stays bounded.
            float cellSize = math.max(GridCellSize,
                math.max(maxX - minX, maxZ - minZ) / GridMaxCellsPerAxis);
            int cellsX = math.max(1, (int)math.floor((maxX - minX) / cellSize) + 1);
            int cellsZ = math.max(1, (int)math.floor((maxZ - minZ) / cellSize) + 1);
            int bucketCount = cellsX * cellsZ * GridSlots;
            var bucketStart = new NativeArray<int>(bucketCount + 1, Allocator.TempJob);
            var bucketItems = new NativeArray<int>(math.max(1, nTargets), Allocator.TempJob);
            var fillJob = new GridFillJob
            {
                Transforms = tgtTransforms,
                Factions = tgtFactions,
                MinX = minX,
                MinZ = minZ,
                CellSize = cellSize,
                CellsX = cellsX,
                CellsZ = cellsZ,
                BucketStart = bucketStart,
                BucketItems = bucketItems,
            };
            fillJob.Run();

            // Hostility table, once per update instead of a team lookup per
            // candidate. Slot FactionSlots ("other") is decided per candidate.
            var hostile = new NativeArray<byte>(FactionSlots * FactionSlots, Allocator.TempJob);
            for (int a = 0; a < FactionSlots; a++)
                for (int b = 0; b < FactionSlots; b++)
                    hostile[a * FactionSlots + b] = (byte)(Alliances.AreHostile((Faction)a, (Faction)b) ? 1 : 0);

            // Per-faction answers that are the same for every tower of that
            // faction, worked out on first use: Arrow Slits (a standing
            // Stonehold) and the three Fiendstone Keep techs. Nothing in this
            // loop can change them. Bit 0 = known, then one bit per answer.
            var factionFlags = new NativeArray<byte>(256, Allocator.Temp);

            // PASS 2a — each ready tower's parameters, in the old order
            // (managed: sect / research reads, the forced-target rule and its
            // one structural change).
            var towers = new NativeArray<TowerScan>(ready.Length, Allocator.TempJob);
            var fire = new NativeArray<TowerFire>(ready.Length, Allocator.TempJob);
            int totalSlots = 0;
            for (int ri = 0; ri < ready.Length; ri++)
            {
                var entity = ready[ri];
                var attackData = em.GetComponentData<BuildingRangedAttack>(entity);
                var myPos = em.GetComponentData<LocalTransform>(entity).Position;
                var myFaction = em.GetComponentData<FactionTag>(entity).Value;
                float range = attackData.Range;
                // Arrow Slits (the Stonehold, Sects.md §4): walls and towers
                // of a faction holding one reach further and hit harder.
                bool slits = TheWaningBorder.Systems.Sect.AlanthorSectEconomy.IsSlitShooter(em, entity)
                    && (FactionFlagsFor(em, ref factionFlags, myFaction) & FlagSlits) != 0;
                if (slits) range *= TheWaningBorder.Systems.Sect.AlanthorSectEconomy.ArrowSlitsRange;
                // Cheap squared reject before the exact distance: the bound is
                // padded, so every candidate the exact test would accept
                // still reaches it and the accepted set is unchanged.
                float rejectSq = (range * 1.001f + 0.01f) * (range * 1.001f + 0.01f);
                int maxTargets = math.max(1, attackData.MaxTargets);

                // Fiendstone Keep tech ladder (Age 0): AdditionalTowers adds
                // two auto-fire targets; the emplacement techs add extra
                // per-volley shots (fired below, after the normal volley).
                bool isKeep = em.HasComponent<FiendstoneKeepTag>(entity);
                var research = isKeep ? TheWaningBorder.Economy.FactionResearchState.Instance : null;
                byte keepTechs = isKeep && research != null ? FactionFlagsFor(em, ref factionFlags, myFaction) : (byte)0;
                if ((keepTechs & FlagAdditionalTowers) != 0)
                    maxTargets += 2;

                // The Wall Rule (docs/Design/Combat_Pacing.md): a building
                // whose own fire is not siege never takes a wall piece as a
                // target - ProjectileSystem would zero the hit anyway, so a
                // wall in the list only wastes a volley slot. The Keep's
                // Ballista / Trebuchet emplacement shots ARE siege, so a Keep
                // that has them remembers the nearest wall as a fallback for
                // those shots alone.
                DamageType dmgType = MainDamageType(em, entity);
                bool mainIsSiege = dmgType == DamageType.Siege;
                bool hasSiegeShot = em.HasComponent<BuildingSiegeShot>(entity);
                bool keepSiege = hasSiegeShot
                    || (keepTechs & (FlagBallista | FlagTrebuchet)) != 0;

                // Directed fire (docs/Design/Combat_Pacing.md § Directed
                // building fire): a legal forced target in range takes the
                // FIRST slot; out of range the order is simply kept. An
                // illegal one (dead, allied, veiled, a wall for non-siege
                // fire) ends the order.
                Entity forced = Entity.Null;
                var forcedCandidate = default(TargetCandidate);
                if (em.HasComponent<BuildingForcedTarget>(entity))
                {
                    var ft = em.GetComponentData<BuildingForcedTarget>(entity).Target;
                    if (!IsLegalForcedTarget(em, entity, ft))
                    {
                        // Direct, not through the ECB: the tower list is a
                        // copy, so the structural change is safe here, and a
                        // deferred removal could land on a building the same
                        // playback destroys. Nothing the sweep reads depends
                        // on it; the lookups are refreshed after this pass.
                        em.RemoveComponent<BuildingForcedTarget>(entity);
                    }
                    else
                    {
                        // Gate on the SURFACE distance; the pivot stays the
                        // aim point and its distance the projectile's flight.
                        var fpos = em.GetComponentData<LocalTransform>(ft).Position;
                        float fdist = math.distance(myPos, fpos);
                        if (TargetGeometry.SurfaceDistXZ(em, myPos, ft) <= range)
                        {
                            forced = ft;
                            forcedCandidate = new TargetCandidate { Entity = ft, Position = fpos, Distance = fdist };
                        }
                    }
                }
                int autoSlots = forced != Entity.Null ? maxTargets - 1 : maxTargets;

                towers[ri] = new TowerScan
                {
                    Entity = entity,
                    MyPos = myPos,
                    MyFaction = myFaction,
                    Range = range,
                    RejectSq = rejectSq,
                    AutoSlots = autoSlots,
                    MainIsSiege = (byte)(mainIsSiege ? 1 : 0),
                    KeepSiege = (byte)(keepSiege ? 1 : 0),
                    Forced = forced,
                    SlotOffset = totalSlots,
                };
                fire[ri] = new TowerFire
                {
                    Attack = attackData,
                    Slits = (byte)(slits ? 1 : 0),
                    KeepResearch = (byte)(isKeep && research != null ? 1 : 0),
                    KeepTechs = keepTechs,
                    DmgType = dmgType,
                    HasSiegeShot = (byte)(hasSiegeShot ? 1 : 0),
                    ForcedCandidate = forcedCandidate,
                };
                totalSlots += math.max(0, autoSlots);
            }

            // PASS 2b — every ready tower's candidate sweep, in parallel
            // (Burst). Each tower writes only its own record and slot range.
            // Refreshed here: a forced-target removal above invalidates them.
            RefreshLookups(ref state);
            var slots = new NativeArray<TargetCandidate>(math.max(1, totalSlots), Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var scanJob = new TowerScanJob
            {
                TgtEntities = tgtEntities,
                TgtTransforms = tgtTransforms,
                TgtFactions = tgtFactions,
                TgtHealth = tgtHealth,
                BucketStart = bucketStart,
                BucketItems = bucketItems,
                Hostile = hostile,
                MinX = minX,
                MinZ = minZ,
                CellSize = cellSize,
                CellsX = cellsX,
                CellsZ = cellsZ,
                MaxPad = maxPad,
                SizeLk = _sizeLk,
                RadiusLk = _radiusLk,
                WallLk = _wallLk,
                WorkerLk = _workerLk,
                CanBuildLk = _canBuildLk,
                RitualLk = _ritualLk,
                NetLk = _netLk,
                Towers = towers,
                Slots = slots,
            };
            var scanHandle = scanJob.Schedule(ready.Length, 1, state.Dependency);
            scanHandle.Complete();

            // PASS 2c — fire, in the old order (managed: per-target debuff
            // reads, projectile spawns through the ECB, timers).
            var targets = new NativeList<TargetCandidate>(8, Allocator.Temp);
            for (int ri = 0; ri < ready.Length; ri++)
            {
                var entity = ready[ri];
                var tw = towers[ri];
                var fd = fire[ri];
                var attackData = fd.Attack;
                var myPos = tw.MyPos;
                var myFaction = tw.MyFaction;
                bool slits = fd.Slits != 0;
                bool hasSiegeShot = fd.HasSiegeShot != 0;
                DamageType dmgType = fd.DmgType;
                bool haveWall = tw.HaveWall != 0;
                var nearestWall = tw.NearestWall;

                targets.Clear();
                // The forced target leads the list.
                if (tw.Forced != Entity.Null) targets.Add(fd.ForcedCandidate);
                for (int j = 0; j < tw.Found; j++) targets.Add(slots[tw.SlotOffset + j]);

                // Fire at each target
                if (targets.Length > 0 || haveWall)
                {
                    // Veilstone buildings fire lasers instead of arrows
                    bool isBorder = em.HasComponent<BorderTag>(entity);

                    // Veilstone buff/debuff modifiers (same pattern as MeleeCombatSystem)
                    float attackerBorderMod = 1.0f;
                    if (em.HasComponent<BorderBuff>(entity))
                    {
                        var buff = em.GetComponentData<BorderBuff>(entity);
                        attackerBorderMod *= 1f + buff.AttBonus;
                    }

                    // Spawn height: use entity's Radius + 0.5f (taller buildings shoot higher)
                    float spawnYOffset = em.HasComponent<Radius>(entity)
                        ? em.GetComponentData<Radius>(entity).Value + 0.5f
                        : 1.5f;

                    for (int t = 0; t < targets.Length; t++)
                    {
                        // Apply veilstone debuff on target
                        float borderMod = attackerBorderMod;
                        if (em.HasComponent<BorderDebuff>(targets[t].Entity))
                        {
                            var debuff = em.GetComponentData<BorderDebuff>(targets[t].Entity);
                            borderMod *= 1f + debuff.AttPenalty;
                        }

                        int modifiedDamage = math.max(1, (int)(attackData.Damage * borderMod
                            * (slits ? TheWaningBorder.Systems.Sect.AlanthorSectEconomy.ArrowSlitsDamage : 1f)));
                        CreateProjectile(ref ecb, in this, myPos, targets[t].Position,
                            targets[t].Distance, entity, myFaction,
                            modifiedDamage, time, targets[t].Entity, isBorder, dmgType, spawnYOffset);
                    }

                    // Keep emplacements: extra per-volley shots at the nearest
                    // target. Ballista = single-target siege bolt; Trebuchet =
                    // arcing siege shell with splash. With nothing else in
                    // range they take the nearest wall piece: siege is the
                    // one thing allowed to (the Wall Rule).
                    // A levelled building's ballista bolt (BuildingSiegeShot —
                    // the Watch Tower's L3): one siege bolt per volley at the
                    // nearest target, on top of its arrows.
                    if (hasSiegeShot)
                    {
                        var nearest = targets.Length > 0 ? targets[0] : nearestWall;
                        CreateProjectile(ref ecb, in this, myPos, nearest.Position,
                            nearest.Distance, entity, myFaction,
                            em.GetComponentData<BuildingSiegeShot>(entity).Damage, time,
                            nearest.Entity, isLaser: false, DamageType.Siege, spawnYOffset,
                            ballistaBolt: true);
                    }

                    if (fd.KeepResearch != 0)
                    {
                        var nearest = targets.Length > 0 ? targets[0] : nearestWall;
                        if ((fd.KeepTechs & FlagBallista) != 0)
                        {
                            CreateProjectile(ref ecb, in this, myPos, nearest.Position,
                                nearest.Distance, entity, myFaction,
                                18, time, nearest.Entity, isLaser: false,
                                DamageType.Siege, spawnYOffset, ballistaBolt: true);
                        }
                        if ((fd.KeepTechs & FlagTrebuchet) != 0)
                        {
                            CreateTrebuchetShot(ref ecb, _shellArch, myPos, nearest.Position,
                                entity, myFaction, 36, time, nearest.Entity, spawnYOffset);
                        }
                    }

                    attackData.Timer = attackData.Cooldown;
                    em.SetComponentData(entity, attackData);
                }
                else if (noTargetRetry > 0f)
                {
                    attackData.Timer = noTargetRetry;
                    em.SetComponentData(entity, attackData);
                }
            }

            targets.Dispose();
            slots.Dispose();
            towers.Dispose();
            fire.Dispose();
            tgtEntities.Dispose();
            tgtTransforms.Dispose();
            tgtFactions.Dispose();
            tgtHealth.Dispose();
            bucketStart.Dispose();
            bucketItems.Dispose();
            hostile.Dispose();
            factionFlags.Dispose();
            ready.Dispose();
        }

        /// <summary>One ready tower's sweep inputs and results.</summary>
        private struct TowerScan
        {
            public Entity Entity;
            public float3 MyPos;
            public Faction MyFaction;
            public float Range;
            public float RejectSq;
            public int AutoSlots;
            public byte MainIsSiege;
            public byte KeepSiege;
            public Entity Forced;
            /// <summary>Start of this tower's AutoSlots-long range in the slot buffer.</summary>
            public int SlotOffset;
            // Results.
            public int Found;
            public byte HaveWall;
            public TargetCandidate NearestWall;
        }

        /// <summary>One ready tower's fire inputs, read in PASS 2a.</summary>
        private struct TowerFire
        {
            public BuildingRangedAttack Attack;
            public byte Slits;
            public byte KeepResearch;
            public byte KeepTechs;
            public byte HasSiegeShot;
            public DamageType DmgType;
            public TargetCandidate ForcedCandidate;
        }

        /// <summary>The targets' XZ bounds (min x, min z, max x, max z).</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private struct GridBoundsJob : IJob
        {
            [ReadOnly] public NativeArray<LocalTransform> Transforms;
            public NativeReference<float4> Bounds;

            public void Execute()
            {
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                for (int i = 0; i < Transforms.Length; i++)
                {
                    var p = Transforms[i].Position;
                    minX = math.min(minX, p.x); maxX = math.max(maxX, p.x);
                    minZ = math.min(minZ, p.z); maxZ = math.max(maxZ, p.z);
                }
                Bounds.Value = new float4(minX, minZ, maxX, maxZ);
            }
        }

        /// <summary>Counting-sort the targets into (cell, owner slot) buckets —
        /// the old inline build, unchanged.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private struct GridFillJob : IJob
        {
            [ReadOnly] public NativeArray<LocalTransform> Transforms;
            [ReadOnly] public NativeArray<FactionTag> Factions;
            public float MinX, MinZ, CellSize;
            public int CellsX, CellsZ;
            public NativeArray<int> BucketStart;
            public NativeArray<int> BucketItems;

            public void Execute()
            {
                int n = Transforms.Length;
                int bucketCount = BucketStart.Length - 1;
                var tgtBucket = new NativeArray<int>(math.max(1, n), Allocator.Temp);
                for (int i = 0; i < n; i++)
                {
                    var p = Transforms[i].Position;
                    int cx = math.clamp((int)math.floor((p.x - MinX) / CellSize), 0, CellsX - 1);
                    int cz = math.clamp((int)math.floor((p.z - MinZ) / CellSize), 0, CellsZ - 1);
                    int f = (int)Factions[i].Value;
                    int slot = f < FactionSlots ? f : FactionSlots;
                    int b = (cz * CellsX + cx) * GridSlots + slot;
                    tgtBucket[i] = b;
                    BucketStart[b + 1]++;
                }
                for (int b = 0; b < bucketCount; b++) BucketStart[b + 1] += BucketStart[b];
                var fill = new NativeArray<int>(math.max(1, bucketCount), Allocator.Temp);
                for (int i = 0; i < n; i++)
                {
                    int b = tgtBucket[i];
                    BucketItems[BucketStart[b] + fill[b]++] = i;
                }
                fill.Dispose();
                tgtBucket.Dispose();
            }
        }

        /// <summary>
        /// One ready tower's candidate sweep — the old PASS 2 inner loop with
        /// its tests in the same order: keep the AutoSlots closest (a TOTAL
        /// order, so the visit order never matters), plus the nearest wall for
        /// a Keep's siege shots. Footprints are resolved per candidate (the old
        /// loop cached them across towers; the value is the same pure function
        /// of the same lookups).
        /// </summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private struct TowerScanJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> TgtEntities;
            [ReadOnly] public NativeArray<LocalTransform> TgtTransforms;
            [ReadOnly] public NativeArray<FactionTag> TgtFactions;
            [ReadOnly] public NativeArray<Health> TgtHealth;
            [ReadOnly] public NativeArray<int> BucketStart;
            [ReadOnly] public NativeArray<int> BucketItems;
            [ReadOnly] public NativeArray<byte> Hostile;
            public float MinX, MinZ, CellSize, MaxPad;
            public int CellsX, CellsZ;
            [ReadOnly] public ComponentLookup<BuildingSize> SizeLk;
            [ReadOnly] public ComponentLookup<Radius> RadiusLk;
            [ReadOnly] public ComponentLookup<WallTag> WallLk;
            [ReadOnly] public ComponentLookup<WorkerTag> WorkerLk;
            [ReadOnly] public ComponentLookup<CanBuild> CanBuildLk;
            [ReadOnly] public ComponentLookup<RitualState> RitualLk;
            [ReadOnly] public ComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity> NetLk;
            public NativeArray<TowerScan> Towers;
            [NativeDisableParallelForRestriction] public NativeArray<TargetCandidate> Slots;

            public void Execute(int ti)
            {
                var tw = Towers[ti];
                tw.Found = 0;
                tw.HaveWall = 0;
                tw.NearestWall = default;

                int autoSlots = tw.AutoSlots;
                bool keepSiege = tw.KeepSiege != 0;
                // Every slot belongs to the forced target and there is no
                // siege fallback to find: nothing to auto-pick.
                if (!(autoSlots > 0 || keepSiege)) { Towers[ti] = tw; return; }

                var entity = tw.Entity;
                var myPos = tw.MyPos;
                var myFaction = tw.MyFaction;
                float range = tw.Range;
                bool mainIsSiege = tw.MainIsSiege != 0;
                Entity forced = tw.Forced;
                int baseSlot = tw.SlotOffset;
                int count = 0;
                bool haveWall = false;
                var nearestWall = default(TargetCandidate);

                float reach = math.sqrt(tw.RejectSq);
                float sweep = reach + MaxPad;
                float sweepSq = sweep * sweep;
                // The cell range gets a metre of slack so float rounding
                // at a cell edge can never drop a target the sweep keeps.
                float cellSweep = sweep + 1f;
                int cx0 = math.clamp((int)math.floor((myPos.x - cellSweep - MinX) / CellSize), 0, CellsX - 1);
                int cx1 = math.clamp((int)math.floor((myPos.x + cellSweep - MinX) / CellSize), 0, CellsX - 1);
                int cz0 = math.clamp((int)math.floor((myPos.z - cellSweep - MinZ) / CellSize), 0, CellsZ - 1);
                int cz1 = math.clamp((int)math.floor((myPos.z + cellSweep - MinZ) / CellSize), 0, CellsZ - 1);
                int myF = (int)myFaction;
                bool tableFaction = myF < FactionSlots;

                for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                for (int slot = 0; slot < GridSlots; slot++)
                {
                    // Towers hold fire on allies. docs/Design/Teams.md
                    bool perCandidate = !tableFaction || slot == FactionSlots;
                    if (!perCandidate && Hostile[myF * FactionSlots + slot] == 0) continue;

                    int bucket = (cz * CellsX + cx) * GridSlots + slot;
                    int bEnd = BucketStart[bucket + 1];
                    for (int bi = BucketStart[bucket]; bi < bEnd; bi++)
                    {
                        int i = BucketItems[bi];
                        Entity cand = TgtEntities[i];
                        if (cand == forced) continue;
                        if (perCandidate && !Alliances.AreHostileBurst(myFaction, TgtFactions[i].Value)) continue;
                        if (TgtHealth[i].Value <= 0) continue;

                        // First reject on the pivot against the LARGEST
                        // footprint anywhere — no component reads yet.
                        float3 tp = TgtTransforms[i].Position;
                        float rx = tp.x - myPos.x, rz = tp.z - myPos.z;
                        float pivotSq = rx * rx + rz * rz;
                        if (pivotSq > sweepSq) continue;

                        var ext = TargetGeometry.Extent(cand, tp, in SizeLk, in RadiusLk);
                        float pad = ext.IsBox
                            ? math.sqrt(ext.HalfW * ext.HalfW + ext.HalfH * ext.HalfH)
                            : ext.Radius;

                        // The original padded reject, unchanged.
                        float padded = reach + pad;
                        if (pivotSq > padded * padded) continue;

                        // Range gate on the SURFACE distance; `dist` (pivot) is
                        // still what the aim, the ordering and the flight use.
                        if (ext.SurfaceDistXZ(myPos) > range) continue;
                        float dist = math.distance(myPos, tp);

                        if (!mainIsSiege && WallLk.HasComponent(cand))
                        {
                            if (keepSiege)
                            {
                                var wc = MakeCandidate(cand, tp, dist, in NetLk);
                                if (!haveWall || Closer(in wc, in nearestWall))
                                {
                                    nearestWall = wc;
                                    haveWall = true;
                                }
                            }
                            continue;
                        }
                        if (autoSlots <= 0) continue;

                        // Curse & Shardroot canon §2.1: BORDER emplacements
                        // guard the well; workers only when they press close.
                        if (myFaction == Faction.Border && dist > BorderWorkerGraceRange
                            && (WorkerLk.HasComponent(cand) || CanBuildLk.HasComponent(cand)))
                            continue;

                        // A ritualist channelling on THIS structure is not its
                        // target (the 2026-08-07 well-turret fix).
                        if (RitualLk.TryGetComponent(cand, out var ritual)
                            && ritual.TargetNode == entity)
                            continue;

                        var candidate = MakeCandidate(cand, tp, dist, in NetLk);

                        if (count < autoSlots)
                        {
                            Slots[baseSlot + count] = candidate;
                            count++;
                        }
                        else if (Closer(in candidate, Slots[baseSlot + count - 1]))
                        {
                            Slots[baseSlot + count - 1] = candidate;
                        }
                        else continue;

                        // Bubble the new entry into position
                        for (int j = count - 1; j > 0; j--)
                        {
                            if (!Closer(Slots[baseSlot + j], Slots[baseSlot + j - 1])) break;
                            var tmp = Slots[baseSlot + j];
                            Slots[baseSlot + j] = Slots[baseSlot + j - 1];
                            Slots[baseSlot + j - 1] = tmp;
                        }
                    }
                }

                tw.Found = count;
                tw.HaveWall = (byte)(haveWall ? 1 : 0);
                tw.NearestWall = nearestWall;
                Towers[ti] = tw;
            }
        }

        /// <summary>
        /// A building's own fire type: Magic for the curse's emplacements,
        /// Ranged for arrow buildings, unless the building carries an explicit
        /// DamageTypeData.
        /// </summary>
        public static DamageType MainDamageType(EntityManager em, Entity building)
        {
            if (em.HasComponent<DamageTypeData>(building))
                return em.GetComponentData<DamageTypeData>(building).Value;
            return em.HasComponent<BorderTag>(building) ? DamageType.Magic : DamageType.Ranged;
        }

        /// <summary>
        /// May <paramref name="building"/>'s fire be directed at
        /// <paramref name="target"/>? The same exclusions the auto-target
        /// snapshot applies, plus hostility and the Wall Rule. One test for
        /// the command's issuer, its executor on every peer, and the combat
        /// system that keeps or drops the order each volley.
        /// docs/Design/Combat_Pacing.md § Directed building fire
        /// </summary>
        public static bool IsLegalForcedTarget(EntityManager em, Entity building, Entity target)
        {
            if (building == Entity.Null || target == Entity.Null) return false;
            if (!em.Exists(building) || !em.Exists(target)) return false;
            if (!em.HasComponent<BuildingRangedAttack>(building) || !em.HasComponent<FactionTag>(building)) return false;
            if (!em.HasComponent<Health>(target) || !em.HasComponent<FactionTag>(target)
                || !em.HasComponent<LocalTransform>(target)) return false;
            if (em.GetComponentData<Health>(target).Value <= 0) return false;
            if (!Alliances.AreHostile(em.GetComponentData<FactionTag>(building).Value,
                                      em.GetComponentData<FactionTag>(target).Value)) return false;
            if (TransientState.Active<SectVeiled>(em, target) || em.HasComponent<NodeUntargetable>(target)
                || em.HasComponent<NodeNoAutoAcquire>(target)) return false;
            if (CombatDamageHelper.WallRuleBlocks(em, target, MainDamageType(em, building))) return false;
            return true;
        }

        private static void CreateProjectile(ref EntityCommandBuffer ecb, in BuildingCombatSystem sys,
            float3 start, float3 targetPos, float distance,
            Entity shooter, Faction faction, int damage, float time, Entity target,
            bool isLaser = false, DamageType dmgType = DamageType.Ranged, float spawnYOffset = 1.5f,
            bool ballistaBolt = false)
        {
            float speed = isLaser ? LaserSpeed : ArrowSpeed;
            var direction = math.normalize(targetPos - start);

            // Add upward arc for arrows only — lasers fly straight
            if (!isLaser)
            {
                float minPitch = math.radians(10f);
                float currentPitch = math.asin(direction.y);
                if (currentPitch < minPitch)
                {
                    float3 horizontalDir = math.normalize(new float3(direction.x, 0, direction.z));
                    direction = horizontalDir * math.cos(minPitch) + new float3(0, math.sin(minPitch), 0);
                    direction = math.normalize(direction);
                }
            }

            var velocity = direction * speed;
            var flightTime = distance / speed;

            // Laser and bolt never combine on one shot today; if they ever
            // do, the laser archetype plus a bolt tag is the same set.
            var arch = isLaser ? sys._laserArch : ballistaBolt ? sys._boltArch : sys._arrowArch;
            var projectile = ecb.CreateEntity(arch);

            ecb.SetComponent(projectile, new LocalTransform
            {
                Position = start + new float3(0, spawnYOffset, 0),
                Rotation = quaternion.LookRotation(velocity, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.SetComponent(projectile, new ArrowProjectile
            {
                Velocity = velocity,
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = false
            });

            ecb.SetComponent(projectile, new Projectile
            {
                Start = start,
                End = targetPos,
                StartTime = time,
                FlightTime = flightTime,
                Damage = damage,
                Target = target,
                Faction = faction,
                DmgType = dmgType
            });

            // Veilstone buildings fire lasers (LaserProjectileTag, for the
            // visual system) and bolts carry BallistaBoltTag — both already in
            // the archetype; only the never-seen laser bolt needs the add.
            if (isLaser && ballistaBolt)
                ecb.AddComponent<BallistaBoltTag>(projectile);
        }

        /// <summary>
        /// Trebuchet Emplacement shot: a slow, high parabolic siege shell with
        /// splash damage on impact (mirrors the Godsplinter bombard shape —
        /// HighArcProjectile + AOEProjectile, no LaserProjectileTag so
        /// ProjectileSystem runs the Bezier arc path).
        /// </summary>
        private static void CreateTrebuchetShot(ref EntityCommandBuffer ecb, EntityArchetype shellArch,
            float3 start, float3 targetPos, Entity shooter, Faction faction,
            int damage, float time, Entity target, float spawnYOffset)
        {
            float3 spawnPos = start + new float3(0, spawnYOffset, 0);
            var direction = math.normalizesafe(targetPos - spawnPos, new float3(0, 0, 1));

            var shell = ecb.CreateEntity(shellArch);
            ecb.SetComponent(shell, new LocalTransform
            {
                Position = spawnPos,
                Rotation = quaternion.LookRotation(direction, new float3(0, 1, 0)),
                Scale = 1f
            });
            ecb.SetComponent(shell, new ArrowProjectile
            {
                Velocity = direction,
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = true
            });
            ecb.SetComponent(shell, new Projectile
            {
                Start = spawnPos,
                End = targetPos,
                StartTime = time,
                FlightTime = 1.2f,
                Damage = damage,
                Target = target,
                Faction = faction,
                DmgType = DamageType.Siege
            });
            ecb.SetComponent(shell, new AOEProjectile { Radius = 4f });
            ecb.SetComponent(shell, new HighArcProjectile { ArcFraction = 0.25f });
            // Rendered as the Synty siege rock, like every trebuchet stone
            // (TrebuchetStoneTag, in the archetype).
        }

        private struct TargetCandidate
        {
            public Entity Entity;
            public float3 Position;
            public float Distance;
            /// <summary>Peer-stable tiebreak: NetworkId (long.MaxValue when
            /// unnetworked), then the position bits — the same key
            /// TargetingSystem orders its spatial hash by.</summary>
            public long NetId;
            public ulong PosKey;
        }

        private void RefreshLookups(ref SystemState state)
        {
            _sizeLk.Update(ref state);
            _radiusLk.Update(ref state);
            _wallLk.Update(ref state);
            _workerLk.Update(ref state);
            _canBuildLk.Update(ref state);
            _ritualLk.Update(ref state);
            _netLk.Update(ref state);
        }

        private static TargetCandidate MakeCandidate(Entity e, float3 pos, float dist,
            in ComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity> netLookup)
        {
            return new TargetCandidate
            {
                Entity = e,
                Position = pos,
                Distance = dist,
                NetId = netLookup.TryGetComponent(e, out var net) ? net.NetworkId : long.MaxValue,
                PosKey = ((ulong)math.asuint(pos.x) << 32) | math.asuint(pos.z),
            };
        }

        /// <summary>The pick order: nearer first, ties broken by NetworkId
        /// then position bits, so the result never depends on visit order.</summary>
        private static bool Closer(in TargetCandidate a, in TargetCandidate b)
        {
            if (a.Distance != b.Distance) return a.Distance < b.Distance;
            if (a.NetId != b.NetId) return a.NetId < b.NetId;
            return a.PosKey < b.PosKey;
        }

        /// <summary>
        /// The largest footprint reach from a pivot anywhere in the world: the
        /// biggest BuildingSize half-diagonal (the same expression the per-target
        /// pad uses, so the bound is never below it) and the biggest Radius.
        /// </summary>
        private float MaxFootprintPad(ref SystemState state)
        {
            // Burst, in place over the chunks (perf pass 2026-10-16) — no
            // per-tick copies of every BuildingSize and Radius in the world.
            // The same max over the same values (max is order-free).
            var pad = new NativeReference<float>(0f, Allocator.TempJob);
            new MaxSizePadJob { MaxPad = pad }.Run(_sizeQuery);
            new MaxRadiusPadJob { MaxPad = pad }.Run(_radiusQuery);
            float maxPad = pad.Value;
            pad.Dispose();
            return maxPad;
        }

        /// <summary>
        /// The per-faction answers every tower of <paramref name="faction"/>
        /// shares (Arrow Slits, the Keep techs), computed on first ask this
        /// update and remembered. Pure reads of state this system never writes.
        /// </summary>
        private static byte FactionFlagsFor(EntityManager em, ref NativeArray<byte> flags, Faction faction)
        {
            int f = (int)faction;
            byte v = flags[f];
            if ((v & FlagKnown) != 0) return v;

            v = FlagKnown;
            if (TheWaningBorder.Systems.Sect.AlanthorSectEconomy.HoldsArrowSlits(em, faction)) v |= FlagSlits;
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null)
            {
                if (research.HasResearched(faction, "AdditionalTowers")) v |= FlagAdditionalTowers;
                if (research.HasResearched(faction, "BallistaEmplacement")) v |= FlagBallista;
                if (research.HasResearched(faction, "TrebuchetEmplacement")) v |= FlagTrebuchet;
            }
            flags[f] = v;
            return v;
        }
    }

    /// <summary>BuildingCombatSystem PASS 1: tick every eligible tower's
    /// cooldown; collect the ready ones, in query order.</summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    [WithAll(typeof(BuildingTag), typeof(LocalTransform), typeof(FactionTag))]
    // BuildingCollapseState is the buildings' DeathAnimationState: a tower
    // that has already fallen kept firing for the whole collapse.
    [WithNone(typeof(UnderConstruction), typeof(BuildingUpgrading), typeof(NodeDormant),
        typeof(BuildingCollapseState))]
    internal partial struct TickTowerCooldownJob : IJobEntity
    {
        public float Dt;
        public NativeList<Entity> Ready;

        private void Execute(Entity entity, ref BuildingRangedAttack attack)
        {
            if (attack.Timer > 0f)
            {
                attack.Timer -= Dt;
                return;
            }
            Ready.Add(entity);
        }
    }

    /// <summary>Largest BuildingSize half-diagonal (MaxFootprintPad).</summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct MaxSizePadJob : IJobEntity
    {
        public NativeReference<float> MaxPad;

        private void Execute(in BuildingSize size)
        {
            float hw = size.Width * 0.5f, hh = size.Height * 0.5f;
            MaxPad.Value = math.max(MaxPad.Value, math.sqrt(hw * hw + hh * hh));
        }
    }

    /// <summary>Largest Radius (MaxFootprintPad).</summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct MaxRadiusPadJob : IJobEntity
    {
        public NativeReference<float> MaxPad;

        private void Execute(in Radius radius)
        {
            MaxPad.Value = math.max(MaxPad.Value, radius.Value);
        }
    }
}
