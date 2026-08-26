// VeilMiningSystem.cs
// Digging THE VEIL ITSELF (Curse & Shardroot canon §2.3) — Astroneer-style
// terrain mining, 2D: the miner walks to a crusted vertex of the VeilField
// grid, picks at it, and every swing pulls veilstone out of the sheet AND
// drains saturation around the pick — the continuous crust mesh visibly
// recedes exactly where the villager is digging. The Veil is an infinite
// source; the cost of mining it is time spent standing on cursed ground.
//
// HARDNESS: crust is easier to break the FARTHER it is from the nearest
// well — the frontier is soft and fast, the well's core is dense and slow.
// Digging near a well pays more in time and danger.
//
// When the vertex under the pick breaks through (saturation drops below
// crust), the miner auto-advances to the closest remaining crusted vertex
// nearby, so a digger keeps eating the front edge-inward. Each cell dug
// through credits its veilstone STRAIGHT to the faction bank — like every
// mining flow, nothing is hauled. Same interrupt rules as the entity-mining
// systems. Deterministic: field reads/writes only, no RNG.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.MathUtil;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Work
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VeilstoneMiningSystem))]
    public partial struct VeilMiningSystem : ISystem
    {
        private const float GatherInterval = 1.5f;  // base seconds per swing
        private const int VeilstonePerGather = 1;
        // 4.0 (2026-07-12, second pass): 2.5 was EXACTLY astride the reachable
        // band once the crust became an impassable wall — the dig vertex is a
        // crusted CELL CENTER the worker can never stand on, and the closest
        // walkable stand point is 2.0-2.6 m away at a face, 2.8-3.5 m at a
        // corner. Half the approaches missed by centimeters, so workers ground
        // the wall and panic-circled. 4.0 covers every frontier-cell geometry
        // (face, corner, crowded) while staying tighter than the original 5;
        // combined with frontier-only vertex selection (VeilMiningUtil) the
        // diggers still cluster visibly ON the front.
        private const float GatherRange = 4.0f;
        private const float AutoFindRadius = 12f;   // next vertex after a break-through

        // Hardness: swing time is scaled by distance to the nearest well.
        // At/inside HardRadius every swing takes 2.4x as long; beyond
        // SoftRadius the frontier crumbles at 0.7x (faster than base).
        private const float HardRadius = 14f;
        private const float SoftRadius = 70f;
        private const float HardMult = 2.4f;
        private const float SoftMult = 0.7f;

        // Each swing drains this much saturation from every cell within
        // DrainRadius of the vertex — tuned to out-pace frontier growth
        // (GrowPerTick=4/s) so a lone digger genuinely pushes the sheet back.
        private const int DrainSatPerGather = 10;
        private const float DrainRadius = 5f;

        private EntityQuery _wellQuery;
        private EntityQuery _fieldQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VeilField>();

            _wellQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _fieldQuery = state.GetEntityQuery(ComponentType.ReadWrite<VeilField>());
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var field = _fieldQuery.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return;

            // Well positions for the hardness curve (few entities).
            using var wellXfs = _wellQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (minerState, cmd, transform, faction, entity) in SystemAPI
                .Query<RefRW<MinerState>, RefRW<GatherVeilCommand>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<MinerTag>()
                .WithEntityAccess())
            {
                ref var miner = ref minerState.ValueRW;
                var pos = transform.ValueRO.Position;
                var fac = faction.ValueRO.Value;

                // Same interrupt contract as the entity-mining systems: a
                // player move or a construction/repair draft wins instantly.
                if (em.HasComponent<UserMoveOrder>(entity)
                    || em.HasComponent<BuildCommand>(entity)
                    || em.HasComponent<BuildOrder>(entity)
                    || em.HasComponent<RepairOrder>(entity))
                {
                    miner.State = MinerWorkState.Idle;
                    ecb.RemoveComponent<GatherVeilCommand>(entity);
                    continue;
                }

                switch (miner.State)
                {
                    case MinerWorkState.MovingToDeposit:
                        ProcessMoving(ref miner, ref cmd.ValueRW, em, ref ecb,
                            entity, pos, dt, in field);
                        break;

                    case MinerWorkState.Gathering:
                        ProcessDigging(ref miner, ref cmd.ValueRW, em, ref ecb,
                            entity, fac, dt, ref field, wellXfs);
                        break;

                    default:
                        // Order present but state drifted (e.g. fresh assign
                        // raced an interrupt) — re-kick toward the site.
                        miner.State = MinerWorkState.MovingToDeposit;
                        SetDestination(em, ref ecb, entity, cmd.ValueRO.Target);
                        break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────

        private void ProcessMoving(ref MinerState miner, ref GatherVeilCommand cmd,
            EntityManager em, ref EntityCommandBuffer ecb, Entity entity,
            float3 pos, float dt, in VeilField field)
        {
            // Site crust already gone (drained by another digger / a verb)?
            if (field.SaturationAt(cmd.Target) < VeilField.CrustThreshold
                && !TryAdvance(ref cmd, ref miner, em, ref ecb, entity, in field))
            {
                GoIdle(ref miner, ref ecb, entity);
                return;
            }

            if (DistXZ(pos, cmd.Target) <= GatherRange)
            {
                miner.State = MinerWorkState.Gathering;
                miner.GatherTimer = 0f;
                // Plant and face the dig site.
                TheWaningBorder.Core.TargetGeometry.StopAndFace(em, entity, cmd.Target, dt);
                return;
            }

            // Stuck recovery: destination got cleared en route — re-issue it.
            if (em.HasComponent<DesiredDestination>(entity)
                && em.GetComponentData<DesiredDestination>(entity).Has == 0)
            {
                SetDestination(em, ref ecb, entity, cmd.Target);
            }
        }

        private void ProcessDigging(ref MinerState miner, ref GatherVeilCommand cmd,
            EntityManager em, ref EntityCommandBuffer ecb, Entity entity,
            Faction fac, float dt, ref VeilField field,
            NativeArray<LocalTransform> wells)
        {
            // Hold the facing for the whole dig — a turn spans several frames.
            TheWaningBorder.Core.TargetGeometry.Face(em, entity, cmd.Target, dt);

            miner.GatherTimer += dt;

            // Hardness by distance to the nearest well: the frontier is
            // soft, the core is dense.
            float wellDist = float.MaxValue;
            for (int i = 0; i < wells.Length; i++)
                wellDist = math.min(wellDist, DistXZ(cmd.Target, wells[i].Position));
            float hardness = wells.Length == 0 ? SoftMult
                : math.lerp(HardMult, SoftMult,
                    math.saturate((wellDist - HardRadius) / (SoftRadius - HardRadius)));

            float effectiveInterval = GatherInterval * hardness;
            if (miner.GatherSpeedMultiplier > 0f)
                effectiveInterval /= miner.GatherSpeedMultiplier;

            if (miner.GatherTimer < effectiveInterval) return;
            miner.GatherTimer = 0f;

            // Target already cleared (another digger / a verb) before this swing?
            if (field.SaturationAt(cmd.Target) < VeilField.CrustThreshold)
            {
                if (TryAdvance(ref cmd, ref miner, em, ref ecb, entity, in field)) return;
                GoIdle(ref miner, ref ecb, entity);
                return;
            }

            // The swing drains saturation — the crust recedes under the pick.
            DrainSaturation(ref field, cmd.Target, DrainSatPerGather);

            // ONE veilstone per cell dug THROUGH: pay out only when this swing
            // breaks the vertex below crust. No per-swing trickle — mining is
            // excavation through the veilstone layer, so it takes many swings to
            // earn each unit. Otherwise keep digging.
            if (field.SaturationAt(cmd.Target) >= VeilField.CrustThreshold)
                return;

            // Veilstone is credited DIRECTLY on the swing that breaks a cell —
            // straight to the faction bank, like every other mining flow.
            CreditVeilstone(em, fac, VeilstonePerGather);

            // Broke through — advance to the next crusted vertex (eat inward).
            if (!TryAdvance(ref cmd, ref miner, em, ref ecb, entity, in field))
                GoIdle(ref miner, ref ecb, entity);
        }

        // ─────────────────────────────────────────────────────────────

        /// <summary>Advance the dig site to the closest crusted vertex near
        /// the broken one (eat the front edge-inward). False = no crust left
        /// within AutoFindRadius.</summary>
        private static bool TryAdvance(ref GatherVeilCommand cmd, ref MinerState miner,
            EntityManager em, ref EntityCommandBuffer ecb, Entity entity, in VeilField field)
        {
            if (!VeilMiningUtil.TryFindCrustVertex(in field, cmd.Target,
                    AutoFindRadius, out float3 next))
                return false;

            cmd.Target = next;
            miner.LastDepositPos = next;
            miner.State = MinerWorkState.MovingToDeposit;
            SetDestination(em, ref ecb, entity, next);
            return true;
        }

        /// <summary>The local crust is gone: idle and drop the order.</summary>
        private static void GoIdle(ref MinerState miner, ref EntityCommandBuffer ecb, Entity entity)
        {
            miner.State = MinerWorkState.Idle;
            ecb.RemoveComponent<GatherVeilCommand>(entity);
        }

        /// <summary>Credit veilstone straight into the faction bank.</summary>
        private static void CreditVeilstone(EntityManager em, Faction fac, int amount)
        {
            if (FactionEconomy.TryGetBank(em, fac, out var bank))
            {
                var resources = em.GetComponentData<FactionResources>(bank);
                resources.Veilstone += amount;
                resources.Clamp();
                em.SetComponentData(bank, resources);
            }
        }

        private static void DrainSaturation(ref VeilField field, float3 center, int amount)
        {
            int r = (int)math.ceil(DrainRadius / field.CellSize);
            field.TryWorldToCell(center, out int cx, out int cz);
            float r2 = DrainRadius * DrainRadius;
            for (int z = math.max(0, cz - r); z <= math.min(field.Height - 1, cz + r); z++)
                for (int x = math.max(0, cx - r); x <= math.min(field.Width - 1, cx + r); x++)
                {
                    float dx = (x - cx) * field.CellSize;
                    float dz = (z - cz) * field.CellSize;
                    if (dx * dx + dz * dz > r2) continue;
                    int idx = field.Index(x, z);
                    field.Saturation[idx] = (byte)math.max(0, field.Saturation[idx] - amount);
                }
        }

        private static void SetDestination(EntityManager em, ref EntityCommandBuffer ecb,
            Entity entity, float3 dest)
        {
            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Position = dest, Has = 1 });
            else
                ecb.AddComponent(entity, new DesiredDestination { Position = dest, Has = 1 });
        }
    }
}
