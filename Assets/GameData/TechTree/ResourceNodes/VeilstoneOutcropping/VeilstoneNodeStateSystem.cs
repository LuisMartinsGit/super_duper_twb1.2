// VeilstoneNodeStateSystem.cs
// Keeps every veilstone outcrop's Inactive / Cursed / Depleted state
// (docs/Design/Veilstone_Economy.md §2).
//
//   * a live curse node standing on the outcrop  -> Cursed
//   * else its NodeReserve is spent               -> Depleted
//   * else                                        -> Inactive
//
// THE CURSE REPLENISHES. The moment an outcrop turns Cursed its reserve is
// refilled to full, so a node Feraldis mined out and abandoned comes back as a
// fresh one once the curse has held it and somebody pacifies it again.
//
// The curse node is a separate building entity at the outcrop's position (the
// curse never marks the resource node itself — CurseTerritorySystem.Living
// builds a SmallNode on top), so "stands on" is positional.
//
// Lockstep state: the refill writes NodeReserve, which the income tick pays
// from. It runs on the SimCadence clock and walks both lists in query order,
// so every peer flips the same outcrops on the same tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VeilstoneNodeStateSystem : SystemBase
    {
        /// <summary>Seconds between state passes. State only gates placement
        /// and income, both of which tolerate a second of lag.</summary>
        private const float Interval = 1f;

        /// <summary>How close a curse node must be to count as ON the outcrop.
        /// The curse node snaps to a cell centre and the outcrop to a 2x2 cell
        /// corner, so the two sit about half a cell (1.4 m) apart.</summary>
        private const float CurseNodeOnRange = 3f;

        private SimCadence.Periodic _acc;

        static readonly ComponentType[] QT_Outcrops =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static readonly ComponentType[] QT_MissingState =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.Exclude<VeilstoneNodeState>(),
        };
        static readonly ComponentType[] QT_CurseNodes =
        {
            ComponentType.ReadOnly<SmallNodeTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<BuildingCollapseState>(),
        };
        static CachedEntityQuery QC_Outcrops, QC_MissingState, QC_CurseNodes;

        protected override void OnCreate()
        {
            RequireForUpdate<VeilstoneOutcroppingTag>();
        }

        protected override void OnUpdate()
        {
            if (!_acc.Due(SystemAPI.Time.DeltaTime, Interval)) return;
            var em = EntityManager;

            // Structural change first, outside any scan.
            var missing = QC_MissingState.Get(em, QT_MissingState);
            if (!missing.IsEmptyIgnoreFilter)
                em.AddComponent<VeilstoneNodeState>(missing);

            var curseQ = QC_CurseNodes.Get(em, QT_CurseNodes);
            using var curseXfs = curseQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var curseFacs = curseQ.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var q = QC_Outcrops.Get(em, QT_Outcrops);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float r2 = CurseNodeOnRange * CurseNodeOnRange;

            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                bool cursed = false;
                for (int c = 0; c < curseXfs.Length && !cursed; c++)
                {
                    if (curseFacs[c].Value != Faction.Border) continue;
                    float dx = curseXfs[c].Position.x - p.x, dz = curseXfs[c].Position.z - p.z;
                    cursed = dx * dx + dz * dz <= r2;
                }

                var e = ents[i];
                var prev = em.GetComponentData<VeilstoneNodeState>(e).Kind;
                VeilstoneNodeKind next;
                if (cursed)
                {
                    next = VeilstoneNodeKind.Cursed;
                    if (prev != VeilstoneNodeKind.Cursed && em.HasComponent<NodeReserve>(e))
                    {
                        var res = em.GetComponentData<NodeReserve>(e);
                        res.Remaining = res.Initial;
                        em.SetComponentData(e, res);
                    }
                }
                else if (em.HasComponent<NodeReserve>(e)
                         && em.GetComponentData<NodeReserve>(e).Remaining <= 0f)
                    next = VeilstoneNodeKind.Depleted;
                else
                    next = VeilstoneNodeKind.Inactive;

                if (next != prev)
                    em.SetComponentData(e, new VeilstoneNodeState { Kind = next });
            }
        }

        /// <summary>
        /// The outcrop nearest (<paramref name="x"/>, <paramref name="z"/>)
        /// within <paramref name="range"/>, and its state. Ties broken on the
        /// outcrop's coordinates so every lockstep peer names the same one.
        /// </summary>
        public static bool TryGetOutcropAt(EntityManager em, float x, float z, float range,
            out Entity outcrop, out VeilstoneNodeKind kind)
        {
            outcrop = Entity.Null;
            kind = VeilstoneNodeKind.Inactive;
            var q = QC_Outcrops.Get(em, QT_Outcrops);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float best = range * range;
            float bx = 0f, bz = 0f;
            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                float dx = p.x - x, dz = p.z - z;
                float d2 = dx * dx + dz * dz;
                if (d2 > best) continue;
                if (outcrop != Entity.Null && d2 == best
                    && (p.x > bx || (p.x == bx && p.z >= bz))) continue;
                best = d2; bx = p.x; bz = p.z;
                outcrop = ents[i];
            }
            if (outcrop == Entity.Null) return false;
            kind = KindOf(em, outcrop);
            return true;
        }

        /// <summary>The state of an outcrop, Inactive when it has none yet
        /// (the first pass has not run).</summary>
        public static VeilstoneNodeKind KindOf(EntityManager em, Entity outcrop)
            => em.HasComponent<VeilstoneNodeState>(outcrop)
                ? em.GetComponentData<VeilstoneNodeState>(outcrop).Kind
                : VeilstoneNodeKind.Inactive;
    }
}
