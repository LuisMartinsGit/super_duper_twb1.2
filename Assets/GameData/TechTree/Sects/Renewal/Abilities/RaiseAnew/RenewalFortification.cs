// RenewalFortification.cs
// Raise Anew II: a walled strongpoint, PERMANENT, notably tougher than the
// Tower. Stats: RenewalFortification.asset. Recipe: RaiseAnewStructures.

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class RenewalFortification
    {
        public const string Id = "Renewal_Fortification";
        public const int PresentationID = 415;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => RaiseAnewStructures.Create<RenewalFortificationTag>(em, Id, PresentationID, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => RaiseAnewStructures.Create<RenewalFortificationTag>(ecb, Id, PresentationID, position, faction);
    }
}
