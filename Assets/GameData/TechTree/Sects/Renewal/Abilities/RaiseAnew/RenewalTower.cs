// RenewalTower.cs
// Raise Anew I: a modest, PERMANENT watch post. Stats: RenewalTower.asset.
// Recipe: RaiseAnewStructures (shared by all three levels).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class RenewalTower
    {
        public const string Id = "Renewal_Tower";
        public const int PresentationID = 414;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => RaiseAnewStructures.Create<RenewalTowerTag>(em, Id, PresentationID, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => RaiseAnewStructures.Create<RenewalTowerTag>(ecb, Id, PresentationID, position, faction);
    }
}
