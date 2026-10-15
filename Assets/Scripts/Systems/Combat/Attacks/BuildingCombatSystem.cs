// Handles ranged attacks for buildings (Hall, Fiendstone Keep, etc.)
// Buildings auto-target and fire at enemies within range.

using Unity.Collections;
using Unity.Entities;
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
            var ready = new NativeList<Entity>(16, Allocator.Temp);
            foreach (var (attack, entity) in SystemAPI
                .Query<RefRW<BuildingRangedAttack>>()
                .WithAll<BuildingTag, LocalTransform, FactionTag>()
                // BuildingCollapseState is the buildings' DeathAnimationState:
                // a tower that has already fallen kept firing for the whole
                // collapse.
                .WithNone<UnderConstruction, BuildingUpgrading, NodeDormant>()
                .WithNone<BuildingCollapseState>()
                .WithEntityAccess())
            {
                // Tick cooldown
                if (attack.ValueRO.Timer > 0f)
                {
                    attack.ValueRW.Timer -= dt;
                    continue;
                }
                ready.Add(entity);
            }

            if (ready.Length == 0)
            {
                ready.Dispose();
                return;
            }

            // A ready tower that finds nothing waits this long (sim time, so
            // identical on every peer) instead of re-scanning every tick.
            var cfg = BuildingCombatSystemConfig.I;
            float noTargetRetry = cfg != null ? cfg.noTargetRetryDelay : 0f;

            var tgtEntities = targetQuery.ToEntityArray(Allocator.Temp);
            var tgtTransforms = targetQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var tgtFactions = targetQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var tgtHealth = targetQuery.ToComponentDataArray<Health>(Allocator.Temp);
            int nTargets = tgtEntities.Length;

            // The per-candidate lookups (see the fields) are refreshed here —
            // and again after the one structural change the tower loop makes
            // (dropping an illegal forced target), which invalidates them.
            RefreshLookups(ref state);

            // Footprints, resolved LAZILY — only for a candidate that survives
            // the cheap pivot reject, then remembered for the next tower. Range
            // is measured to the target's SURFACE (TargetGeometry), so a 7x7
            // Hall is in range when its wall is, not only when its pivot is.
            // `tgtPad` is the footprint's farthest reach from the pivot.
            var tgtExtents = new NativeArray<TargetExtent>(nTargets, Allocator.Temp);
            var tgtPad = new NativeArray<float>(nTargets, Allocator.Temp);
            var tgtExtentDone = new NativeArray<byte>(nTargets, Allocator.Temp);

            // An upper bound on every footprint's reach, for the cell sweep
            // and the first reject: the largest BuildingSize diagonal and the
            // largest Radius anywhere in the world. A superset of the targets,
            // so the bound can only be generous, never short.
            float maxPad = MaxFootprintPad(ref state);

            // ── Uniform grid of the targets (2026-10-15) ─────────────────
            // Every ready tower used to walk all ~2,300 targets. Now each
            // visits only the cells inside its reach. Bucketed by cell AND
            // owner slot (0..8 = the players + Border, 9 = anything else), so
            // a tower skips allied buckets outright. The visit order no
            // longer matters for the result: the pick below is a total order
            // (distance, then NetworkId, then position bits), so the same
            // targets win on every peer whatever order the buckets hold.
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < nTargets; i++)
            {
                var p = tgtTransforms[i].Position;
                minX = math.min(minX, p.x); maxX = math.max(maxX, p.x);
                minZ = math.min(minZ, p.z); maxZ = math.max(maxZ, p.z);
            }
            if (nTargets == 0) { minX = minZ = maxX = maxZ = 0f; }
            // Grow the cell when the spread is huge (a stray entity far off
            // the map), so the table stays bounded.
            float cellSize = math.max(GridCellSize,
                math.max(maxX - minX, maxZ - minZ) / GridMaxCellsPerAxis);
            int cellsX = math.max(1, (int)math.floor((maxX - minX) / cellSize) + 1);
            int cellsZ = math.max(1, (int)math.floor((maxZ - minZ) / cellSize) + 1);
            int bucketCount = cellsX * cellsZ * GridSlots;
            var bucketStart = new NativeArray<int>(bucketCount + 1, Allocator.Temp);
            var bucketItems = new NativeArray<int>(math.max(1, nTargets), Allocator.Temp);
            var tgtBucket = new NativeArray<int>(math.max(1, nTargets), Allocator.Temp);
            for (int i = 0; i < nTargets; i++)
            {
                var p = tgtTransforms[i].Position;
                int cx = math.clamp((int)math.floor((p.x - minX) / cellSize), 0, cellsX - 1);
                int cz = math.clamp((int)math.floor((p.z - minZ) / cellSize), 0, cellsZ - 1);
                int f = (int)tgtFactions[i].Value;
                int slot = f < FactionSlots ? f : FactionSlots;
                int b = (cz * cellsX + cx) * GridSlots + slot;
                tgtBucket[i] = b;
                bucketStart[b + 1]++;
            }
            for (int b = 0; b < bucketCount; b++) bucketStart[b + 1] += bucketStart[b];
            {
                var fill = new NativeArray<int>(bucketCount, Allocator.Temp);
                for (int i = 0; i < nTargets; i++)
                {
                    int b = tgtBucket[i];
                    bucketItems[bucketStart[b] + fill[b]++] = i;
                }
                fill.Dispose();
            }

            // Hostility table, once per update instead of a team lookup per
            // candidate. Slot FactionSlots ("other") is decided per candidate.
            var hostile = new NativeArray<byte>(FactionSlots * FactionSlots, Allocator.Temp);
            for (int a = 0; a < FactionSlots; a++)
                for (int b = 0; b < FactionSlots; b++)
                    hostile[a * FactionSlots + b] = (byte)(Alliances.AreHostile((Faction)a, (Faction)b) ? 1 : 0);

            // Per-faction answers that are the same for every tower of that
            // faction, worked out on first use: Arrow Slits (a standing
            // Stonehold) and the three Fiendstone Keep techs. Nothing in this
            // loop can change them. Bit 0 = known, then one bit per answer.
            var factionFlags = new NativeArray<byte>(256, Allocator.Temp);

            // PASS 2 — the ready towers, same order as the old single loop.
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
                bool haveWall = false;
                var nearestWall = default(TargetCandidate);

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
                        // Direct, not through the ECB: the loop walks a copied
                        // list, so the structural change is safe here, and a
                        // deferred removal could land on a building the same
                        // playback destroys.
                        em.RemoveComponent<BuildingForcedTarget>(entity);
                        RefreshLookups(ref state);
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

                // Find closest enemies within range
                var targets = new NativeList<TargetCandidate>(maxTargets, Allocator.Temp);

                // Every slot belongs to the forced target and there is no
                // siege fallback to find: nothing to auto-pick (a one-target
                // tower under a direct order).
                if (autoSlots > 0 || keepSiege)
                {
                    float reach = math.sqrt(rejectSq);
                    float sweep = reach + maxPad;
                    float sweepSq = sweep * sweep;
                    // The cell range gets a metre of slack so float rounding
                    // at a cell edge can never drop a target the sweep keeps.
                    float cellSweep = sweep + 1f;
                    int cx0 = math.clamp((int)math.floor((myPos.x - cellSweep - minX) / cellSize), 0, cellsX - 1);
                    int cx1 = math.clamp((int)math.floor((myPos.x + cellSweep - minX) / cellSize), 0, cellsX - 1);
                    int cz0 = math.clamp((int)math.floor((myPos.z - cellSweep - minZ) / cellSize), 0, cellsZ - 1);
                    int cz1 = math.clamp((int)math.floor((myPos.z + cellSweep - minZ) / cellSize), 0, cellsZ - 1);
                    int myF = (int)myFaction;
                    bool tableFaction = myF < FactionSlots;

                    for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    for (int slot = 0; slot < GridSlots; slot++)
                    {
                        // Towers hold fire on allies. docs/Design/Teams.md
                        // Whole buckets are skipped when the table can say so.
                        bool perCandidate = !tableFaction || slot == FactionSlots;
                        if (!perCandidate && hostile[myF * FactionSlots + slot] == 0) continue;

                        int bucket = (cz * cellsX + cx) * GridSlots + slot;
                        int bEnd = bucketStart[bucket + 1];
                        for (int bi = bucketStart[bucket]; bi < bEnd; bi++)
                        {
                            int i = bucketItems[bi];
                            Entity cand = tgtEntities[i];
                            if (cand == forced) continue;
                            if (perCandidate && !Alliances.AreHostile(myFaction, tgtFactions[i].Value)) continue;
                            if (tgtHealth[i].Value <= 0) continue;

                            // First reject on the pivot against the LARGEST
                            // footprint anywhere — no component reads yet.
                            float3 tp = tgtTransforms[i].Position;
                            float rx = tp.x - myPos.x, rz = tp.z - myPos.z;
                            float pivotSq = rx * rx + rz * rz;
                            if (pivotSq > sweepSq) continue;

                            // This target's own footprint, resolved once per
                            // update and shared by every tower after this one.
                            if (tgtExtentDone[i] == 0)
                            {
                                var ext = TargetGeometry.Extent(cand, tp, in _sizeLk, in _radiusLk);
                                tgtExtents[i] = ext;
                                tgtPad[i] = ext.IsBox
                                    ? math.sqrt(ext.HalfW * ext.HalfW + ext.HalfH * ext.HalfH)
                                    : ext.Radius;
                                tgtExtentDone[i] = 1;
                            }

                            // The original padded reject, unchanged, so the
                            // accepted set is bit-for-bit the old one.
                            float padded = reach + tgtPad[i];
                            if (pivotSq > padded * padded) continue;

                            // Range gate on the SURFACE distance; `dist` (pivot) is
                            // still what the aim, the ordering and the flight use.
                            if (tgtExtents[i].SurfaceDistXZ(myPos) > range) continue;
                            float dist = math.distance(myPos, tp);

                            if (!mainIsSiege && _wallLk.HasComponent(cand))
                            {
                                if (keepSiege)
                                {
                                    var wc = MakeCandidate(cand, tp, dist, in _netLk);
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
                            // (well turrets, Turret sub-nodes) GUARD the well — they
                            // don't hunt harvesters. Worker-class units (workers)
                            // are only fired on when they press right up
                            // to the structure; military targets are engaged
                            // normally. This is what makes sneak-mining the crystal
                            // fields survivable.
                            if (myFaction == Faction.Border && dist > BorderWorkerGraceRange
                                && (_workerLk.HasComponent(cand) || _canBuildLk.HasComponent(cand)))
                                continue;

                            // A RITUALIST CHANNELLING ON THIS STRUCTURE IS NOT ITS
                            // TARGET. The well turret is 25 damage per 1.2 s — 20.8
                            // DPS — over an 18 m reach, and every verb is channelled
                            // from 6 m, so the caster is never outside it. That kills
                            // a 90 HP Scholar in 4.3 s and a 280 HP Iconoclast in
                            // 13.4 s against channels of 35-45 s. It is not a hard
                            // fight, it is an impossible one: the well simply shoots
                            // whoever comes to claim it. The 2026-08-07 diagnostics
                            // caught it exactly — "channel BROKEN at 13.3 s:
                            // Corruptor dead (hp 0)", one attack cycle off the
                            // predicted 13.44 s.
                            //
                            // Scoped to THIS node deliberately, not blanket immunity:
                            // the well still fires on the escort and on the army
                            // assaulting it, other buildings still fire on the
                            // ritualist, and killing the caster remains the
                            // counterplay — it just has to be done by a player rather
                            // than by the objective defending itself for free.
                            if (_ritualLk.TryGetComponent(cand, out var ritual)
                                && ritual.TargetNode == entity)
                                continue;

                            // Keep the autoSlots closest. "Closest" is a TOTAL
                            // order — distance, then NetworkId, then position
                            // bits (2026-10-15). The old strict `<` on distance
                            // alone kept whichever of two equidistant targets
                            // the walk met first, and the walk was chunk order,
                            // which differs between lockstep peers: two
                            // grid-snapped buildings at the same distance could
                            // draw a tower's fire differently per peer.
                            var candidate = MakeCandidate(cand, tp, dist, in _netLk);

                            if (targets.Length < autoSlots)
                            {
                                targets.Add(candidate);
                            }
                            else if (Closer(in candidate, targets[targets.Length - 1]))
                            {
                                targets[targets.Length - 1] = candidate;
                            }
                            else continue;

                            // Bubble the new entry into position
                            for (int j = targets.Length - 1; j > 0; j--)
                            {
                                if (!Closer(targets[j], targets[j - 1])) break;
                                var tmp = targets[j];
                                targets[j] = targets[j - 1];
                                targets[j - 1] = tmp;
                            }
                        }
                    }
                }

                // The forced target leads the list.
                if (forced != Entity.Null)
                {
                    targets.Add(default);
                    for (int j = targets.Length - 1; j > 0; j--) targets[j] = targets[j - 1];
                    targets[0] = forcedCandidate;
                }

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

                    if (isKeep && research != null)
                    {
                        var nearest = targets.Length > 0 ? targets[0] : nearestWall;
                        if ((keepTechs & FlagBallista) != 0)
                        {
                            CreateProjectile(ref ecb, in this, myPos, nearest.Position,
                                nearest.Distance, entity, myFaction,
                                18, time, nearest.Entity, isLaser: false,
                                DamageType.Siege, spawnYOffset, ballistaBolt: true);
                        }
                        if ((keepTechs & FlagTrebuchet) != 0)
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

                targets.Dispose();
            }

            tgtEntities.Dispose();
            tgtTransforms.Dispose();
            tgtFactions.Dispose();
            tgtHealth.Dispose();
            tgtExtents.Dispose();
            tgtPad.Dispose();
            tgtExtentDone.Dispose();
            bucketStart.Dispose();
            bucketItems.Dispose();
            tgtBucket.Dispose();
            hostile.Dispose();
            factionFlags.Dispose();
            ready.Dispose();
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
            float maxPad = 0f;
            using (var sizes = _sizeQuery.ToComponentDataArray<BuildingSize>(Allocator.Temp))
            {
                for (int i = 0; i < sizes.Length; i++)
                {
                    float hw = sizes[i].Width * 0.5f, hh = sizes[i].Height * 0.5f;
                    maxPad = math.max(maxPad, math.sqrt(hw * hw + hh * hh));
                }
            }
            using (var radii = _radiusQuery.ToComponentDataArray<Radius>(Allocator.Temp))
            {
                for (int i = 0; i < radii.Length; i++)
                    maxPad = math.max(maxPad, radii[i].Value);
            }
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
}
