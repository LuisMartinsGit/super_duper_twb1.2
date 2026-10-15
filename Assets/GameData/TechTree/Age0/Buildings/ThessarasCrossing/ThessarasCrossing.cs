// ThessarasCrossing.cs
// Thessara's Crossing — the Runai landmark (docs/Design/Age_1_Runai.md §2).
// Building it in Age 0 IS the age-up into Runai (LandmarkAgeUp), exactly as
// the Vault ages into Alanthor and the Keep into Feraldis. After age-up it is
// the grand caravan market (§3.3) and the home of the trade research (§7.2).
// One per faction. Filed in Age0/ because it is BUILT in Age 0; its research
// is post-age-up Runai content and lives in Civs/Runai/Buildings/ThessarasCrossing/.

using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class ThessarasCrossing
    {
        public const string BuildingId = "ThessarasCrossing";
        public const int PresentationID = 590;   // ThessarasCrossing.asset (borrows the Bazaar's model)

        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));

        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiBuildingBody.Base(c, BuildingId, PresentationID, p, f, trainer: true, isBase: true);
            c.AddComponent<ThessarasCrossingTag>(e);
            c.AddComponent<ChoiceBuildingTag>(e);
            return e;
        }
    }
}
