// File: Assets/Scripts/Systems/Work/VeilstoneMiningSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Handles miners gathering veilstone from veilstone nodes (creature outcroppings).
    ///
    /// Miners assigned to veilstone (GatheringResource == 1):
    /// - Move to veilstone node
    /// - Gather 1 veilstone every 1.5 seconds, credited straight to the
    ///   faction bank (no carrying, no dropoff trip)
    /// - When the node depletes, auto-find another outcropping nearby or go idle
    ///
    /// State machine: MovingToDeposit -> Gathering -> (loop or Idle)
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MiningSystem))]
    public partial struct VeilstoneMiningSystem : ISystem
    {
        private const float GatherInterval = 1.5f;    // Mine 1 veilstone every 1.5 seconds
        private const int VeilstonePerGather = 1;        // 1 veilstone per gather action
        // Reach and the stand point both live in MiningReach — one definition
        // shared with GatheringSystem and MiningSystem.
        private const float AutoFindRadius = 10f;       // Radius to auto-find next outcropping around a depleted node

        // Cached query — created once in OnCreate, reused every frame
        private EntityQuery _outcroppingQuery;

        // §2.5b rev.3 mining corruption: registry singleton (present only
        // when the curse stack is active) + the seeded per-depletion dice.
        private EntityQuery _pocketRegistryQuery;
        private EntityQuery _veilFieldQuery;
        private EntityQuery _hallQuery; // hearth check for corruption immunity
        private Unity.Mathematics.Random _corruptRng;
        private int _rngEpoch;

        /// <summary>The corruption RNG's current state, for the lockstep
        /// checksum. See BloodCurseSpawnSystem.RngState for why a seeded
        /// stream still needs hashing.</summary>
        public uint RngState => _corruptRng.state;
        private double _now; // sim time, refreshed each OnUpdate

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MinerTag>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _outcroppingQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<VeilstoneOutcroppingState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
            _pocketRegistryQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<BlightPocket>());
            _veilFieldQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<VeilField>());
            _hallQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HallTag, LocalTransform>()
                .WithNone<UnderConstruction>()
                .Build(ref state);
            _corruptRng = new Unity.Mathematics.Random(
                (uint)(GameSettings.SpawnSeed ^ 0xC0221) | 1u);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Re-seed per match -- see BloodCurseSpawnSystem for why. Systems
            // outlive a match, so a stream left mid-position by the previous
            // one differs per peer and forks the next.
            if (_rngEpoch != SimCadence.Epoch)
            {
                _rngEpoch = SimCadence.Epoch;
                _corruptRng = new Unity.Mathematics.Random(
                    (uint)(GameSettings.SpawnSeed ^ 0xC0221) | 1u);
            }

            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            _now = SystemAPI.Time.ElapsedTime; // for UnreachableMark expiry checks

            // Read fresh each tick and pass down — never cache the struct.
            // SpatialHashRebuildSystem reallocates the map when the unit count
            // outgrows it, so a stale copy is a use-after-dispose.
            SystemAPI.TryGetSingleton<NavSpatialHash>(out var hash);

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // §2.5b rev.3: depletions that rolled corruption this frame —
            // drained AFTER the foreach (spawning is a structural change).
            var corrupted = new NativeList<float3>(Allocator.Temp);

            foreach (var (minerState, transform, faction, entity) in SystemAPI
                .Query<RefRW<MinerState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<MinerTag>()
                .WithEntityAccess())
            {
                ref var miner = ref minerState.ValueRW;

                // Only handle veilstone miners
                if (miner.GatheringResource != 1) continue;

                // Diggers working the Veil sheet itself (position-targeted,
                // no deposit entity) belong to VeilMiningSystem.
                if (em.HasComponent<GatherVeilCommand>(entity)) continue;

                var pos = transform.ValueRO.Position;
                var fac = faction.ValueRO.Value;

                // --- UserMoveOrder / construction interrupt ---
                // Player issued a move command, OR the worker was drafted to
                // build/repair (BuildCommand doesn't carry UserMoveOrder):
                // stop mining, go idle. Without the build/repair branch this
                // state machine keeps driving DesiredDestination toward the
                // outcropping every tick while BuildingConstructionSystem
                // drives it toward the site — a tug-of-war that made workers
                // walk AWAY from their shown destination. GatheringResource
                // stays 1 — interrupts don't switch the miner's resource type.
                if (em.HasComponent<UserMoveOrder>(entity)
                    || em.HasComponent<TheWaningBorder.Core.Commands.Types.BuildCommand>(entity)
                    || em.HasComponent<BuildOrder>(entity)
                    || em.HasComponent<RepairOrder>(entity))
                {
                    if (miner.State != MinerWorkState.Idle)
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
                    }
                    continue;
                }

                switch (miner.State)
                {
                    case MinerWorkState.MovingToDeposit:
                        ProcessMovingToVeilstoneOutcropping(ref miner, em, ref ecb, entity, pos, dt, in hash);
                        break;

                    case MinerWorkState.Gathering:
                        ProcessGatheringVeilstone(ref miner, em, ref ecb, entity, fac, dt, in hash, ref corrupted);
                        break;

                    case MinerWorkState.Idle:
                        // Idle veilstone miners do nothing on their own. AI miners
                        // are reassigned by SimpleAISystem.AssignIdleMiners; the
                        // player issues a fresh GatherCommand via right-click.
                        // Veilstone flow stays independent — never revert to iron.
                        break;
                }
            }

            // §2.5b rev.3: transform corrupted depletions into curse nodes —
            // the resistant SmallNode anchors haze over the whole patch,
            // invalidating it until killed/starved (BlightPocketSystem owns
            // the collapse + residue payout). Structural spawns, so done
            // strictly after the query iteration.
            // TELEGRAPHED (2026-08-04): the corruption no longer strikes
            // instantly — a purple ping + announcement fire now, and
            // BlightPocketSystem raises the curse node
            // CorruptionTelegraphSeconds later. Reaction window for
            // everyone; the roll itself is already decided.
            if (corrupted.Length > 0)
            {
                using var registries = _pocketRegistryQuery.ToEntityArray(Allocator.Temp);
                if (registries.Length > 0)
                {
                    var pending = em.GetBuffer<PendingCorruption>(registries[0]);
                    for (int i = 0; i < corrupted.Length; i++)
                    {
                        pending.Add(new PendingCorruption
                        {
                            Pos = corrupted[i],
                            At = _now + TheWaningBorder.Core.Config.VeilCrustConstants
                                .CorruptionTelegraphSeconds,
                        });
                        SimSignals.Ping(corrupted[i],
                            SimPingKind.Curse, 15f);
                        SimSignals.Notify(
                            string.Format(
                                Loc.T("A veilstone node is corrupting — the curse rises in {0}s!"),
                                (int)TheWaningBorder.Core.Config.VeilCrustConstants.CorruptionTelegraphSeconds));
                        TWBLog.Log($"[VeilstoneMining] node at {corrupted[i]} corrupting (telegraphed).");
                    }
                }
            }
            corrupted.Dispose();
        }

        /// <summary>Hearth ring of any completed Hall, or any player influence
        /// at/over the threshold — the same suppression rule the veil CA and
        /// BlightPocketSystem use, sampled point-wise. Suppressed ground is
        /// immune to mining corruption.</summary>
        /// <summary>
        /// True when the node just mined out was the LAST live bud of its
        /// patch — i.e. no other undepleted veilstone outcropping remains
        /// within PatchCohesionRadius. This is the §2.7 corruption trigger
        /// (canon rewrite 2026-08-07): the patch running dry is what wakes the
        /// blight pocket, not a dice roll on an arbitrary node.
        ///
        /// Detected by proximity rather than a stamped patch id on purpose.
        /// Patches are a SPAWN-TIME concept — markers and the procedural
        /// fallback both scatter N nodes around a centre and then forget the
        /// grouping — so there is no runtime patch identity to read, and
        /// adding one would mean touching every spawn path plus the netcode.
        /// Proximity also matches what the player actually sees: the ground
        /// around this node is empty, so this was the last one. It handles
        /// hand-authored maps, the procedural fallback and precipitation
        /// nodes identically, with no bookkeeping to keep in sync.
        ///
        /// The depleted node is excluded explicitly. It is also already
        /// flagged Depleted=1 by this point, so the state test alone would
        /// cover it — the entity check is belt-and-braces against that write
        /// order changing.
        ///
        /// Consequence worth knowing: a genuinely ISOLATED outcropping (a lone
        /// precipitation node, say) is trivially "the last of its patch" and
        /// always corrupts. That is the intended reading — mining a lone bud
        /// out in the wild wakes something — but it does mean precipitation
        /// nodes are self-limiting: farm the residue and you re-seed the
        /// pocket that produced it.
        /// </summary>
        private bool IsLastBudOfPatch(EntityManager em, Entity depleted, float3 pos)
        {
            float r2 = TheWaningBorder.Core.Config.VeilCrustConstants.PatchCohesionRadius
                     * TheWaningBorder.Core.Config.VeilCrustConstants.PatchCohesionRadius;

            using var ents = _outcroppingQuery.ToEntityArray(Allocator.Temp);
            using var states = _outcroppingQuery.ToComponentDataArray<VeilstoneOutcroppingState>(Allocator.Temp);
            using var xfs = _outcroppingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (ents[i] == depleted) continue;
                if (states[i].Depleted != 0) continue;
                if (states[i].RemainingVeilstone <= 0) continue;

                float dx = xfs[i].Position.x - pos.x;
                float dz = xfs[i].Position.z - pos.z;
                if (dx * dx + dz * dz <= r2) return false;   // patch still has buds
            }
            return true;
        }

        private bool IsSuppressedGround(float3 pos)
        {
            using var halls = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float r2 = TheWaningBorder.Core.Config.VeilCrustConstants.HallHearthRadius
                     * TheWaningBorder.Core.Config.VeilCrustConstants.HallHearthRadius;
            for (int i = 0; i < halls.Length; i++)
            {
                float dx = halls[i].Position.x - pos.x;
                float dz = halls[i].Position.z - pos.z;
                if (dx * dx + dz * dz <= r2) return true;
            }

            if (TheWaningBorder.Influence.PlayerInfluenceMap.Ready)
                for (int f = 0; f < TheWaningBorder.Influence.PlayerInfluenceMap.PlayerChannels; f++)
                    if (TheWaningBorder.Influence.PlayerInfluenceMap.ChannelStrengthWorld(f, pos.x, pos.z)
                        >= TheWaningBorder.Core.Config.VeilCrustConstants.InfluenceThreshold)
                        return true;
            return false;
        }

        private void ProcessMovingToVeilstoneOutcropping(ref MinerState miner, EntityManager em,
            ref EntityCommandBuffer ecb, Entity entity, float3 pos, float dt, in NavSpatialHash hash)
        {
            // Check if outcropping still exists or is depleted — auto-find next nearby
            bool needNewTarget = false;
            float3 searchCenter = pos;
            if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
            {
                // VeilstoneOutcropping was destroyed (likely depleted by another miner) — fall
                // back to the last known position captured in LastDepositPos.
                needNewTarget = true;
                if (!miner.LastDepositPos.Equals(float3.zero))
                    searchCenter = miner.LastDepositPos;
            }
            else if (em.HasComponent<VeilstoneOutcroppingState>(miner.AssignedDeposit))
            {
                var outcroppingState = em.GetComponentData<VeilstoneOutcroppingState>(miner.AssignedDeposit);
                if (outcroppingState.Depleted == 1)
                {
                    needNewTarget = true;
                    searchCenter = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
                    miner.LastDepositPos = searchCenter;
                }
            }

            if (needNewTarget)
            {
                // No outcropping in range — go idle as a veilstone miner. We keep
                // GatheringResource=1 so the miner stays committed to veilstone.
                if (!TryAssignNearestVeilstoneOutcropping(ref miner, em, ref ecb, entity, in hash,searchCenter))
                {
                    miner.State = MinerWorkState.Idle;
                    miner.AssignedDeposit = Entity.Null;
                }
                return;
            }

            var outcroppingPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
            // Reach measured to the outcropping's SURFACE, not its pivot.
            float dist = TargetGeometry.SurfaceDistXZ(em, pos, miner.AssignedDeposit);

            if (dist <= MiningReach.GatherRange)
            {
                // Reached outcropping - start gathering
                miner.State = MinerWorkState.Gathering;
                miner.GatherTimer = 0f;

                // Plant and turn to face the node.
                TargetGeometry.StopAndFace(em, entity, outcroppingPos, dt);
                return;
            }

            // Tier-3 stuck recovery: if our destination got cleared while we
            // were trying to reach the outcropping, drop to Idle so the next tick
            // assigns a fresh target. Stays a veilstone miner — flows are
            // independent. Same caveats as MiningSystem — only fire when the
            // component EXISTS but its Has==0 (otherwise we race the
            // structural-change deferral and ping-pong forever).
            if (em.HasComponent<DesiredDestination>(entity)
                && em.GetComponentData<DesiredDestination>(entity).Has == 0)
            {
                // Usually another worker took the spot we aimed at: we pressed
                // into its separation ring, walked on the spot, and the crowd
                // rules called us arrived. Step AROUND it — re-pick a slot on
                // another side of the node before dropping to Idle. Free slots
                // only, so "nowhere left" terminates instead of looping.
                if (MiningReach.TryGetFreeMiningStand(em, miner.AssignedDeposit, entity, pos,
                        in hash, out float3 retry))
                {
                    em.SetComponentData(entity, new DesiredDestination { Position = retry, Has = 1 });
                    return;
                }

                miner.State = MinerWorkState.Idle;
                miner.AssignedDeposit = Entity.Null;
            }
        }

        private void ProcessGatheringVeilstone(ref MinerState miner, EntityManager em,
            ref EntityCommandBuffer ecb, Entity entity, Faction fac, float dt,
            in NavSpatialHash hash, ref NativeList<float3> corrupted)
        {
            // Hold the facing for the whole channel, not just on arrival — a turn
            // spans several frames at DefaultTurnSpeed.
            if (miner.AssignedDeposit != Entity.Null && em.Exists(miner.AssignedDeposit)
                && em.HasComponent<LocalTransform>(miner.AssignedDeposit))
            {
                TargetGeometry.Face(em, entity,
                    em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position, dt);
            }

            miner.GatherTimer += dt;

            // Effective gather interval: faster when GatherSpeedMultiplier > 1
            float effectiveInterval = miner.GatherSpeedMultiplier > 0f
                ? GatherInterval / miner.GatherSpeedMultiplier
                : GatherInterval;

            if (miner.GatherTimer >= effectiveInterval)
            {
                miner.GatherTimer = 0f;

                // Check veilstone node still exists and has veilstone. If it was
                // destroyed by another miner exhausting it, the current miner
                // is still standing where the outcropping was (within GatherRange)
                // — use that as the search center for the next outcropping.
                if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit)
                    || !em.HasComponent<VeilstoneOutcroppingState>(miner.AssignedDeposit))
                {
                    var minerPos = em.GetComponentData<LocalTransform>(entity).Position;
                    miner.LastDepositPos = minerPos;
                    miner.AssignedDeposit = Entity.Null;

                    if (!TryAssignNearestVeilstoneOutcropping(ref miner, em, ref ecb, entity, in hash,miner.LastDepositPos))
                    {
                        miner.State = MinerWorkState.Idle;
                    }
                    return;
                }

                var outcroppingState = em.GetComponentData<VeilstoneOutcroppingState>(miner.AssignedDeposit);

                // Extract veilstone from the node and credit the bank directly —
                // the resource goes straight to the player's inventory.
                // Warden's Ledger (Reclamation): veilstone yields +25%. The
                // bonus is minted on CREDIT, not drawn from the node - the
                // research makes the same rock go further, it does not deplete
                // the map faster.
                int toGather = math.min(VeilstonePerGather, outcroppingState.RemainingVeilstone);
                outcroppingState.RemainingVeilstone -= toGather;
                int toCredit = (int)(toGather * SectResearchEffects.VeilstoneYieldMultiplier(fac));

                if (toGather > 0 && FactionEconomy.TryGetBank(em, fac, out var bank))
                {
                    var resources = em.GetComponentData<FactionResources>(bank);
                    resources.Veilstone += toCredit;
                    resources.Clamp();
                    em.SetComponentData(bank, resources);
                }

                bool justDepleted = false;
                if (outcroppingState.RemainingVeilstone <= 0)
                {
                    outcroppingState.RemainingVeilstone = 0;
                    outcroppingState.Depleted = 1;
                    justDepleted = true;
                }

                em.SetComponentData(miner.AssignedDeposit, outcroppingState);

                if (justDepleted)
                {
                    // Capture the outcropping's position before destruction so
                    // auto-find can search around where the depleted node was.
                    miner.LastDepositPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;

                    // MINING CORRUPTION — canon §2.7, rewritten 2026-08-07.
                    //
                    // WAS: a 15 % roll on every depletion. That is a slot
                    // machine — it fires on a node you had no reason to treat
                    // as special, six nodes before the patch is even spent, and
                    // the player has no way to see it coming or plan around it.
                    // Random punishment for doing the thing the game asks you
                    // to do all match.
                    //
                    // NOW: 100 %, but only on the LAST bud of the patch. Same
                    // expected number of pockets over a patch (one), completely
                    // different feel — the patch itself is the telegraph. You
                    // can see it thinning, you know the last node wakes
                    // something, and you decide when to take that last bite:
                    // now while your army is home, or later on your terms, or
                    // never. It converts a dice roll into a scheduling decision,
                    // and it is what makes the mid game a curse players CHOOSE
                    // to create (and then farm — the pocket's crust pays
                    // veilstone back as it recedes).
                    //
                    // Suppressed ground (hearth ring / player influence) stays
                    // immune: the universal "curse can never expand into your
                    // influence" rule. With HallHearthRadius at 34 m the
                    // starter patch (spawned 22-30 m out) sits inside it, so a
                    // home patch never wakes a pocket. That is deliberate — the
                    // opening should not punish you, and it puts the guaranteed
                    // pocket out on the CONTESTED patches you have to leave
                    // home for.
                    if (!_pocketRegistryQuery.IsEmptyIgnoreFilter
                        && !IsSuppressedGround(miner.LastDepositPos)
                        && IsLastBudOfPatch(em, miner.AssignedDeposit, miner.LastDepositPos))
                        corrupted.Add(miner.LastDepositPos);

                    // Destroy depleted outcropping via ECB (structural changes not allowed during iteration)
                    if (em.HasComponent<PresentationId>(miner.AssignedDeposit))
                        ecb.RemoveComponent<PresentationId>(miner.AssignedDeposit);
                    ecb.DestroyEntity(miner.AssignedDeposit);
                    miner.AssignedDeposit = Entity.Null;

                    // Look for another outcropping within AutoFindRadius of the
                    // depleted node.
                    if (!TryAssignNearestVeilstoneOutcropping(ref miner, em, ref ecb, entity, in hash,miner.LastDepositPos))
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
                    }
                }
                // else: keep gathering (stay in Gathering state)
            }
        }

        /// <summary>
        /// Look for the nearest non-depleted outcropping within AutoFindRadius of
        /// <paramref name="searchCenter"/> (typically the depleted node's
        /// position). If found, assign the miner to it and set the move
        /// destination. Returns true if a new outcropping was found.
        /// </summary>
        private bool TryAssignNearestVeilstoneOutcropping(ref MinerState miner, EntityManager em,
            ref EntityCommandBuffer ecb, Entity entity, in NavSpatialHash hash, float3 searchCenter)
        {
            using var outcroppings = _outcroppingQuery.ToEntityArray(Allocator.Temp);
            using var outcroppingStates = _outcroppingQuery.ToComponentDataArray<VeilstoneOutcroppingState>(Allocator.Temp);
            using var outcroppingTransforms = _outcroppingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // §2.5b loop damping: depletion auto-find never walks a worker
            // onto cursed ground — a hazed node (e.g. after a corruption
            // event) is only minable by explicit player order.
            bool hasVeilField = false;
            VeilField veilField = default;
            if (!_veilFieldQuery.IsEmptyIgnoreFilter)
            {
                veilField = _veilFieldQuery.GetSingleton<VeilField>();
                hasVeilField = veilField.Initialised == 1 && veilField.Saturation.IsCreated;
            }

            Entity bestVeilstoneOutcropping = Entity.Null;
            float bestDist = float.MaxValue;
            float3 bestPos = float3.zero;

            for (int i = 0; i < outcroppings.Length; i++)
            {
                if (outcroppingStates[i].Depleted == 1) continue;
                if (outcroppingStates[i].RemainingVeilstone <= 0) continue;
                if (hasVeilField && veilField.SaturationAt(outcroppingTransforms[i].Position)
                    >= VeilField.CrustThreshold) continue; // hazed — never auto-assign
                if (em.HasComponent<UnreachableMark>(outcroppings[i])
                    && em.GetComponentData<UnreachableMark>(outcroppings[i]).Until > _now)
                    continue; // provably blocked recently — don't orbit it again
                // Walled in by its neighbours (patch interior) — nowhere to
                // stand, so mining it is impossible however close we get.
                if (!MiningReach.IsMinable(em, outcroppings[i], searchCenter)) continue;

                float dist = DistXZ(searchCenter, outcroppingTransforms[i].Position);
                if (dist <= AutoFindRadius && dist < bestDist)
                {
                    bestDist = dist;
                    bestVeilstoneOutcropping = outcroppings[i];
                    bestPos = outcroppingTransforms[i].Position;
                }
            }

            if (bestVeilstoneOutcropping == Entity.Null) return false;

            // Assign miner to new outcropping
            miner.AssignedDeposit = bestVeilstoneOutcropping;
            miner.GatheringResource = 1;
            miner.State = MinerWorkState.MovingToDeposit;

            // Beside the node, never inside its impassable cell.
            MiningReach.TryGetMiningStand(em, bestVeilstoneOutcropping, entity, searchCenter, in hash, out float3 stand);
            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Position = stand, Has = 1 });
            else
                ecb.AddComponent(entity, new DesiredDestination { Position = stand, Has = 1 });

            return true;
        }
    }
}
