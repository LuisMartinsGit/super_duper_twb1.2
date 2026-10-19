// WallTopOrders.cs
// Putting foot units ON a stone wall-walk (the rampart nav layer), spread
// along the corridor — the order behind a right-click on your wall AND the
// AI's wall manning (docs/Design/Age_1_Alanthor.md § The stone wall,
// docs/Design/Game_AI.md § 6s). Lifted out of SelectionOrders on 2026-10-20
// so the AI calls the same slot layout and the same CommandRouter layered
// move the player does, instead of a copy. No selection, no ownership rules:
// the caller decides who may go up.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Issuing
{
    public static class WallTopOrders
    {
        // Detect the wall's "along" axis at a deck cell by sampling which
        // direction (X vs Z) has more contiguous wall-deck cells. Used to
        // spread garrisoning units along the wall (the debug-wall cubes have
        // identity rotation, so we can't read orientation off the transform).
        public static float3 WallAlongAxis(float3 wallTopPoint)
        {
            var cell = TheWaningBorder.Systems.Navigation.NavGridQuery
                .WorldToCellInt2(wallTopPoint);
            if (cell.x == int.MinValue) return new float3(1f, 0f, 0f);
            int xRun = 0, zRun = 0;
            for (int k = 1; k <= 4; k++)
            {
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x + k, cell.y), 1)) xRun++;
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x - k, cell.y), 1)) xRun++;
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x, cell.y + k), 1)) zRun++;
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(new int2(cell.x, cell.y - k), 1)) zRun++;
            }
            return xRun >= zRun ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f);
        }

        /// <summary>
        /// Order <paramref name="units"/> onto the wall-walk at
        /// <paramref name="wallTopPoint"/> (a deck point), spread ALONG the
        /// corridor around it, one deck cell per man
        /// (docs/Design/Age_1_Alanthor.md § The stone wall).
        /// LayeredMoveSystem routes each to the nearest friendly access
        /// (tower/gate) or a breach ramp, LERPs it up, then it walks the deck
        /// to its slot. The caller has already filtered who may go up; this
        /// only lays the slots and issues one layered move per unit through
        /// CommandRouter (so it replicates for every source). Shared by the
        /// player's right-click (SelectionOrders) and the AI's wall manning
        /// (SimpleAISystem.WallDefence). Returns how many were ordered.
        /// </summary>
        public static int IssueWallTopMove(EntityManager em, List<Entity> units,
            float3 wallTopPoint, float spacing, CommandSource source)
        {
            if (units == null) return 0;
            bool isOverpassDeck = TheWaningBorder.World.Terrain.BridgeSurface
                .TryGetDeckHeight(wallTopPoint.x, wallTopPoint.z, out _);

            int n = units.Count;
            if (n == 0) return 0;

            // Overpass decks keep the old grid-axis spread (a bridge is a
            // straight road); a wall-walk gets a cell per man, laid along
            // the corridor itself — through hubs, round bends — centred on
            // the click (2026-10-20).
            List<float3> slots = isOverpassDeck ? null : DeckSlots(wallTopPoint, n, spacing);
            if (slots != null && slots.Count == n)
            {
                // Pair men and slots in the same order along the walk, so
                // the file does not cross itself on the way up.
                float3 axis = SlotAxis(slots);
                slots.Sort((a, b) => math.dot(a, axis).CompareTo(math.dot(b, axis)));
                var order = new List<Entity>(units);
                order.Sort((a, b) =>
                {
                    float pa = em.HasComponent<LocalTransform>(a) ? math.dot(em.GetComponentData<LocalTransform>(a).Position, axis) : 0f;
                    float pb = em.HasComponent<LocalTransform>(b) ? math.dot(em.GetComponentData<LocalTransform>(b).Position, axis) : 0f;
                    int c = pa.CompareTo(pb);
                    return c != 0 ? c : a.Index.CompareTo(b.Index);
                });
                for (int i = 0; i < n; i++)
                    CommandRouter.IssueLayeredMove(em, order[i], slots[i],
                        NavLayerIndex.LayerRampart, source);
                return n;
            }

            float3 along = WallAlongAxis(wallTopPoint);
            for (int i = 0; i < n; i++)
            {
                float off = (i - (n - 1) * 0.5f) * spacing;
                float3 dest = wallTopPoint + along * off;
                // The spread is laid on a grid axis, and a wall can run at any
                // bearing: a slot that falls off the walk takes the clicked
                // point instead, or the unit would be routed to thin air.
                var slotCell = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(dest);
                if (slotCell.x == int.MinValue
                    || !TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(slotCell, NavLayerIndex.LayerRampart))
                    dest = wallTopPoint;
                CommandRouter.IssueLayeredMove(em, units[i], dest,
                    NavLayerIndex.LayerRampart, source);
            }
            return n;
        }

        /// <summary>Deck cells a wall-top search may visit — loop resolution,
        /// not a tunable: enough walk for a large squad either side of the click.</summary>
        private const int DeckSlotSearchBudget = 4000;

        static readonly int2[] DeckSlotSteps =
            { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) };

        /// <summary>
        /// <paramref name="n"/> standing spots on the wall-walk, one deck cell
        /// each, spread ALONG the corridor from <paramref name="at"/>
        /// (docs/Design/Age_1_Alanthor.md § The stone wall, 2026-10-20). A
        /// breadth-first walk over the rampart cells joined to the clicked one
        /// — so the spread follows the walk through hubs and round bends and
        /// never jumps to a wall that merely lies close — takes cells nearest
        /// first, in both directions at once, so the squad is centred on the
        /// click. Cells in the MIDDLE of the walk come first (every
        /// four-neighbour is deck too), kept a spacing apart, so the men form
        /// a staggered file down the centre instead of a blob; only when the
        /// walk runs out do they take its edges, and only then may two stand
        /// on neighbouring cells. Never two on one cell. Null when the click
        /// is not on a deck cell.
        /// </summary>
        public static List<float3> DeckSlots(float3 at, int n, float spacing)
        {
            const byte deck = NavLayerIndex.LayerRampart;
            var c0 = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(at);
            if (c0.x == int.MinValue
                || !TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(c0, deck)) return null;
            float cs = math.distance(
                TheWaningBorder.Systems.Navigation.NavGridQuery.CellCenter(c0).xz,
                TheWaningBorder.Systems.Navigation.NavGridQuery.CellCenter(c0 + new int2(1, 0)).xz);
            if (cs < 0.01f) cs = 1f;
            // A man per cell, no closer than ~1.2 cells while the centre of
            // the walk lasts, and not wider than the formation spacing asks.
            float minGap = math.max(cs * 1.2f, spacing * 0.75f);

            var visited = new HashSet<int2> { c0 };
            var bfs = new List<int2> { c0 };
            for (int head = 0; head < bfs.Count && bfs.Count < DeckSlotSearchBudget; head++)
            {
                var c = bfs[head];
                for (int k = 0; k < DeckSlotSteps.Length; k++)
                {
                    var nb = c + DeckSlotSteps[k];
                    if (visited.Contains(nb)) continue;
                    if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(nb, deck)) continue;
                    visited.Add(nb);
                    bfs.Add(nb);
                }
            }

            var chosen = new List<float3>(n);
            var taken = new HashSet<int2>();
            // Pass 0: middle of the walk, spaced. Pass 1: anywhere, spaced.
            // Pass 2: any free cell.
            for (int pass = 0; pass < 3 && chosen.Count < n; pass++)
            {
                for (int i = 0; i < bfs.Count && chosen.Count < n; i++)
                {
                    var c = bfs[i];
                    if (taken.Contains(c)) continue;
                    if (pass == 0)
                    {
                        bool interior = true;
                        for (int k = 0; k < DeckSlotSteps.Length && interior; k++)
                            interior = visited.Contains(c + DeckSlotSteps[k]);
                        if (!interior) continue;
                    }
                    float3 p = TheWaningBorder.Systems.Navigation.NavGridQuery.CellCenter(c);
                    if (pass < 2)
                    {
                        bool clear = true;
                        for (int j = 0; j < chosen.Count && clear; j++)
                            clear = math.distancesq(chosen[j].xz, p.xz) >= minGap * minGap;
                        if (!clear) continue;
                    }
                    taken.Add(c);
                    chosen.Add(new float3(p.x,
                        TheWaningBorder.Systems.Navigation.LayerTransitionSystem.DeckYAt(p.x, p.z), p.z));
                }
            }
            return chosen;
        }

        /// <summary>The direction the slots spread along: their principal
        /// horizontal axis (a wall-walk is a strip, so this is its run).</summary>
        public static float3 SlotAxis(List<float3> slots)
        {
            float2 mean = float2.zero;
            for (int i = 0; i < slots.Count; i++) mean += slots[i].xz;
            mean /= math.max(1, slots.Count);
            float sxx = 0f, szz = 0f, sxz = 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                float2 d = slots[i].xz - mean;
                sxx += d.x * d.x; szz += d.y * d.y; sxz += d.x * d.y;
            }
            float angle = 0.5f * math.atan2(2f * sxz, sxx - szz);
            return new float3(math.cos(angle), 0f, math.sin(angle));
        }

        /// <summary>The walkable rampart-layer cell nearest <paramref name="near"/>
        /// within <paramref name="radius"/> metres, as a point on the deck.</summary>
        public static bool TryNearestDeckPoint(float3 near, float radius, out float3 point)
        {
            point = default;
            var c0 = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(near);
            if (c0.x == int.MinValue) return false;
            float cs = math.distance(
                TheWaningBorder.Systems.Navigation.NavGridQuery.CellCenter(c0).xz,
                TheWaningBorder.Systems.Navigation.NavGridQuery.CellCenter(c0 + new int2(1, 0)).xz);
            if (cs < 0.01f) cs = 1f;
            int n = (int)math.ceil(radius / cs);
            float best = float.MaxValue;
            bool found = false;
            for (int dz = -n; dz <= n; dz++)
            for (int dx = -n; dx <= n; dx++)
            {
                var c = new int2(c0.x + dx, c0.y + dz);
                if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(c, NavLayerIndex.LayerRampart))
                    continue;
                float3 cc = TheWaningBorder.Systems.Navigation.NavGridQuery.CellCenter(c);
                float d = math.distancesq(cc.xz, near.xz);
                if (d >= best || d > radius * radius) continue;
                best = d;
                point = new float3(cc.x,
                    TheWaningBorder.Systems.Navigation.LayerTransitionSystem.DeckYAt(cc.x, cc.z), cc.z);
                found = true;
            }
            return found;
        }
    }
}
