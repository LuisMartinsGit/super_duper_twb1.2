// FarmRing.cs
// The Farm's crop ring (docs/Design/Age_1_Alanthor.md § The Farm, 2026-11-11):
// a CIRCLE centred on the Farm, of radius (the footprint's half width + the
// Farm SO's cropRingMeters), less the Farm's own footprint. Its FREE share —
// the part not covered by a building, impassable terrain or another Farm's
// ring — scales the Farm's pay. Roads are not obstacles.
//
// This file is the ONE definition of the ring's geometry: the simulation's
// free-share sampler, the placement preview and the crop visuals all read it,
// so they can never disagree about where a Farm's field is.
//
// THE FREE / BLOCKED ANSWER lives beside it, in FarmRing.Field.cs — the one
// obstacle test the simulation pays with and the UI and visuals show:
//   FarmRing.IsBlocked(em, farm, p)          one point of a Farm's ring
//                                            (presentation; gathers once a frame)
//   FarmRing.Field  .Gather(em, centre, self, ghost) then .IsBlocked(p) /
//                   .CountFreePermille()     the same, for many points
//   FarmRing.FreePermille(em, farm)          the share now, from the world
//   FarmRing.FreePermilleAt(em, centre, self, ghost)
//                                            a would-be position / building
//   FarmRing.CachedFreePermille(em, farm)    FarmField, what FarmSystem pays
//   FarmRing.SampleCount / SampleOffset(i)   the fixed 1 m lattice the share
//                                            is counted on (offsets from centre)

using Unity.Mathematics;
using TheWaningBorder.Data;

namespace TheWaningBorder.Entities
{
    public static partial class FarmRing
    {
        /// <summary>Half the Farm footprint's width, metres (square footprint).</summary>
        public static float FootprintHalfMeters
            => math.max(BuildingSizeConfig.GetSize(Farm.BuildingId).x, BuildingSizeConfig.GetSize(Farm.BuildingId).y) * 0.5f;

        /// <summary>The ring's outer radius, metres from the Farm's centre.</summary>
        public static float OuterRadius
            => FootprintHalfMeters + TechCatalog.Building(Farm.BuildingId).cropRingMeters;

        /// <summary>True when <paramref name="p"/> (world XZ) lies in the crop
        /// ring of a Farm centred at <paramref name="centre"/>: inside the
        /// outer circle and outside the Farm's own footprint square.</summary>
        public static bool InRing(float2 centre, float2 p)
        {
            float2 d = p - centre;
            float r = OuterRadius;
            if (math.lengthsq(d) > r * r) return false;
            float h = FootprintHalfMeters;
            return math.abs(d.x) >= h || math.abs(d.y) >= h;
        }
    }
}
