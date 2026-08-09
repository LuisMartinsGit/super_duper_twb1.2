// GatherVeilCommand.cs
// Mining THE VEIL ITSELF (Curse & Shardroot canon §2.3): there are no
// discrete veilstone deposits on cursed maps — the continuous crust sheet
// is the resource. This command targets a POSITION (the closest crusted
// lattice vertex of the VeilField grid), Astroneer-style: the miner walks
// there, picks at the crust, and the field drains under their pick so the
// sheet visibly recedes where they dig. Consumed by VeilMiningSystem;
// stays on the miner for the whole dig loop (like BuildOrder/RepairOrder)
// and is removed on interrupt or when the local crust is gone.
// Location: Assets/Scripts/Core/Commands/CommandTypes/

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Order a miner to dig veilstone out of the Veil sheet at (near) a
    /// world position. Position-targeted — there is no resource entity.
    /// </summary>
    public struct GatherVeilCommand : IComponentData
    {
        /// <summary>Current dig site — a crusted VeilField vertex. The
        /// mining system re-snaps it to nearby crust when the vertex under
        /// the pick breaks through.</summary>
        public float3 Target;
    }

    /// <summary>Helper for executing veil-dig commands.</summary>
    public static class GatherVeilCommandHelper
    {
        /// <summary>
        /// Send a miner to dig the Veil at <paramref name="site"/>. Clears
        /// conflicting commands and primes the veilstone-miner state
        /// machine (GatheringResource=1, no entity deposit).
        /// </summary>
        public static void Execute(EntityManager em, Entity miner, float3 site)
        {
            if (!em.Exists(miner) || !em.HasComponent<MinerTag>(miner)) return;
            if (!em.HasComponent<MinerState>(miner)) return;

            CommandHelper.ClearAllCommands(em, miner);
            // An explicit dig order overrides a stale player move flag —
            // otherwise VeilMiningSystem's interrupt check idles the miner
            // on the very next tick.
            if (em.HasComponent<UserMoveOrder>(miner))
                em.RemoveComponent<UserMoveOrder>(miner);

            var ms = em.GetComponentData<MinerState>(miner);
            ms.GatheringResource = 1;            // committed to veilstone
            ms.AssignedDeposit = Entity.Null;    // the FIELD is the deposit
            ms.State = MinerWorkState.MovingToDeposit;
            ms.GatherTimer = 0f;
            ms.LastDepositPos = site;
            em.SetComponentData(miner, ms);

            if (em.HasComponent<GatherVeilCommand>(miner))
                em.SetComponentData(miner, new GatherVeilCommand { Target = site });
            else
                em.AddComponentData(miner, new GatherVeilCommand { Target = site });

            if (em.HasComponent<DesiredDestination>(miner))
                em.SetComponentData(miner, new DesiredDestination { Position = site, Has = 1 });
            else
                em.AddComponentData(miner, new DesiredDestination { Position = site, Has = 1 });
        }
    }

    /// <summary>
    /// Shared crust-vertex lookups over the VeilField grid — used by input
    /// (snap a click to the closest diggable vertex), the mining system
    /// (advance to the next vertex when one breaks through), and the AI
    /// (find diggable crust near home). Deterministic scans, no RNG.
    /// </summary>
    public static class VeilMiningUtil
    {
        /// <summary>
        /// Closest crusted vertex (cell center) to <paramref name="from"/>
        /// within <paramref name="maxRadius"/>. Returns false if none.
        /// </summary>
        public static bool TryFindCrustVertex(in VeilField field, float3 from,
            float maxRadius, out float3 vertex)
        {
            return TryFindCrustVertexNear(in field, from, from, maxRadius, out vertex);
        }

        /// <summary>
        /// Closest crusted vertex to <paramref name="from"/> among cells
        /// within <paramref name="anchorRadius"/> of <paramref name="anchor"/>
        /// (the AI's home-tether: dig the crust nearest the worker, but only
        /// crust that is near home).
        /// </summary>
        public static bool TryFindCrustVertexNear(in VeilField field, float3 from,
            float3 anchor, float anchorRadius, out float3 vertex)
        {
            vertex = default;
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return false;

            int r = (int)math.ceil(anchorRadius / field.CellSize);
            field.TryWorldToCell(anchor, out int cx, out int cz);
            cx = math.clamp(cx, 0, field.Width - 1);
            cz = math.clamp(cz, 0, field.Height - 1);

            float anchorR2 = anchorRadius * anchorRadius;
            float bestD2 = float.MaxValue;
            bool found = false;

            for (int z = math.max(0, cz - r); z <= math.min(field.Height - 1, cz + r); z++)
            {
                float wz = field.Origin.y + (z + 0.5f) * field.CellSize;
                for (int x = math.max(0, cx - r); x <= math.min(field.Width - 1, cx + r); x++)
                {
                    if (field.Saturation[field.Index(x, z)] < VeilField.CrustThreshold)
                        continue;

                    // FRONTIER ONLY (2026-07-12): the crust is an impassable
                    // wall now — an INTERIOR cell (no open 4-neighbour) has
                    // no walkable stand point anywhere near it, so handing it
                    // out sent diggers after vertices they could never reach
                    // (grind-the-wall panic circles). Only face cells of the
                    // front are diggable; breaking one exposes the cell
                    // behind it, so the dig still eats inward face by face.
                    if (!HasOpenNeighbor(in field, x, z)) continue;

                    float wx = field.Origin.x + (x + 0.5f) * field.CellSize;
                    float ax = wx - anchor.x, az = wz - anchor.z;
                    if (ax * ax + az * az > anchorR2) continue;

                    float dx = wx - from.x, dz = wz - from.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        vertex = new float3(wx, 0f, wz);
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>True when at least one 4-neighbour of (x, z) is open
        /// ground (below crust). Grid edges count as open so the map-border
        /// face of the crust stays diggable.</summary>
        private static bool HasOpenNeighbor(in VeilField field, int x, int z)
        {
            if (x <= 0 || field.Saturation[field.Index(x - 1, z)] < VeilField.CrustThreshold)
                return true;
            if (x >= field.Width - 1 || field.Saturation[field.Index(x + 1, z)] < VeilField.CrustThreshold)
                return true;
            if (z <= 0 || field.Saturation[field.Index(x, z - 1)] < VeilField.CrustThreshold)
                return true;
            if (z >= field.Height - 1 || field.Saturation[field.Index(x, z + 1)] < VeilField.CrustThreshold)
                return true;
            return false;
        }
    }
}
