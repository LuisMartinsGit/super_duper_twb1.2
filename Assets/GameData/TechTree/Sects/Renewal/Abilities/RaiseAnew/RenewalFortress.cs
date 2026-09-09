// RenewalFortress.cs
// Raise Anew III: a genuine keep that anchors a position, PERMANENT and an
// investment-grade structure. Stats: RenewalFortress.asset.
// Recipe: RaiseAnewStructures.

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class RenewalFortress
    {
        public const string Id = "Renewal_Fortress";
        public const int PresentationID = 416;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => RaiseAnewStructures.Create<RenewalFortressTag>(em, Id, PresentationID, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => RaiseAnewStructures.Create<RenewalFortressTag>(ecb, Id, PresentationID, position, faction);
    }
}
