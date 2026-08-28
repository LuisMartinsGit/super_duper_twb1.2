// RegionStartReveal.cs
// Every player begins with their OWN starting region explored.
//
// This is a direct consequence of the Age 0 rule, not a fog tweak:
// docs/Design/Regions.md §2 grants you the region your start sits in from tick
// 0 and confines your building to it. Ground you are said to HOLD should not be
// ground you have never seen — you would be told to build inside a shape you
// cannot make out.
//
// It also fixes an arithmetic problem the 704 m map exposed. Initial reveal is
// a Hall's line of sight, which is a fixed radius, so the fraction of the map
// you can see at t=0 falls with map AREA:
//
//     Twin Spans      352 m, 6 starts  ->  ~14 %  visible at match start
//     Sundered Reach  704 m, 3 starts  ->  ~1.7 % visible at match start
//
// Same vision, four times the ground, half the players. Revealing the home
// region scales with the map instead of against it, and it does so without
// touching unit line-of-sight, which is balance and belongs to the SOs.
//
// Explored, NOT visible: you know your own ground, you still need units on it
// to see what is happening there.
//
// The home position is passed IN, by whoever spawned the bases. It used to be
// read straight off the scene's PlayerStartMarkers, one reveal per marker,
// keyed by the marker's authored Faction field -- and nothing ever writes the
// real assignment back into that field. The lobby hands out starts randomly
// (and lets the player pick one on the preview), and a map with more markers
// than players leaves the extras authored to factions that are not in the
// match at all, so on most matches the two disagreed: the player got their
// REAL home revealed by their Hall's line of sight plus a second, unrelated
// region revealed on the far side of the map. Two territories explored instead
// of one, and never the same two twice.

using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.World.FogOfWar;

namespace TheWaningBorder.World.Regions
{
    public static class RegionStartReveal
    {
        /// <summary>
        /// Reveal each faction's home region to that faction. No-op when the
        /// map has no regions, when fog is off, or when the fog manager has not
        /// been created yet — all three are normal states, not errors.
        /// </summary>
        /// <param name="homes">Where each ACTIVE faction's base was actually
        /// spawned, as resolved by the spawner. Not the authored markers: only
        /// the spawner knows which start each lobby slot ended up on.</param>
        public static void RevealHomeRegions(IReadOnlyDictionary<Faction, Vector3> homes)
        {
            if (!GameSettings.FogOfWarEnabled) return;
            if (!RegionMap.Ready) return;
            if (homes == null || homes.Count == 0) return;

            var mgr = Object.FindFirstObjectByType<FogOfWarManager>();
            if (mgr == null) return;

            int revealed = 0;
            foreach (var pair in homes)
            {
                var p = pair.Value;
                // NearestRegion, not RegionAt: a start could sit close enough
                // to a lake edge that the exact spawn cell reads unclaimable,
                // and we want the region it belongs to regardless.
                int home = RegionMap.NearestRegion(p.x, p.z);
                if (home == RegionMap.None) continue;

                // Captured per faction so the predicate stays allocation-free
                // inside the grid walk.
                int homeRegion = home;
                Faction faction = pair.Key;
                // NearestRegion so the mountains and water INSIDE your own
                // ground are revealed too -- they belong to no region, but
                // leaving unexplored holes in your home would look broken.
                mgr.RevealWhere(faction, xz => RegionMap.NearestRegion(xz.x, xz.y) == homeRegion);
                revealed++;
            }

            RevealNatureRing(mgr);

            TWBLog.Log($"[RegionStartReveal] Revealed {revealed} home region(s) + the Nature ring.");
        }

        /// <summary>Depth from the map edge searched for ring ground.</summary>
        private const float RingSearchDepth = 70f;

        /// <summary>
        /// The Nature ring starts EXPLORED for everybody, and is never lit.
        ///
        /// Regions.md §1: the ring is scenery. It exists so the map ends in
        /// something that reads as terrain rather than a ruler cut -- which it
        /// cannot do if it is hidden behind fog nobody can ever lift, because
        /// nothing can walk there to lift it. Marking it explored shows the
        /// terrain; it is never marked VISIBLE, so it stays in the dimmed
        /// remembered state for the whole match, which is exactly the intent.
        ///
        /// Only ring ground is revealed, NOT every unclaimable cell. Mid-map
        /// mountains are also unclaimable, but where they sit is real scouting
        /// information -- they decide where armies can walk. The ring decides
        /// nothing, so it costs nothing to show.
        /// </summary>
        private static void RevealNatureRing(FogOfWarManager mgr)
        {
            Vector2 min = mgr.WorldMin, max = mgr.WorldMax;

            bool IsRing(Vector2 xz)
            {
                float edge = Mathf.Min(Mathf.Min(xz.x - min.x, max.x - xz.x),
                                       Mathf.Min(xz.y - min.y, max.y - xz.y));
                if (edge > RingSearchDepth) return false;
                return !RegionMap.IsClaimable(xz.x, xz.y);
            }

            for (int f = 0; f < 8; f++)
                mgr.RevealWhere((Faction)f, IsRing);
        }
    }
}
