// DropoffQuery.cs
// Shared "nearest faction dropoff" scan for the carry-and-deposit economy systems.
// Location: Assets/Scripts/Systems/Work/DropoffQuery.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.MathUtil;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Finds the nearest Hall or GathererHut belonging to a faction. Shared by
    /// MiningSystem, CrystalMiningSystem, and ForgeSupplySystem, which all walk a
    /// loaded miner back to the closest dropoff building.
    ///
    /// Iterates the caller-supplied queries rather than building its own, so each
    /// system keeps full control of its query definition (e.g. the
    /// UnderConstruction exclusion). Only the distance scan is centralized here.
    /// Burst-compatible: no managed allocations or query creation, so it is safe
    /// to call from the Burst-compiled mining systems as well as the managed forge
    /// system.
    /// </summary>
    public static class DropoffQuery
    {
        public static Entity FindNearest(float3 pos, Faction fac, EntityQuery hallQuery, EntityQuery hutQuery)
        {
            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;
            Scan(ref nearest, ref nearestDist, pos, fac, hallQuery);
            Scan(ref nearest, ref nearestDist, pos, fac, hutQuery);
            return nearest;
        }

        private static void Scan(ref Entity nearest, ref float nearestDist, float3 pos, Faction fac, EntityQuery query)
        {
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != fac) continue;
                float dist = DistXZ(pos, transforms[i].Position);
                if (dist < nearestDist)
                {
                    nearest = entities[i];
                    nearestDist = dist;
                }
            }
        }
    }
}
