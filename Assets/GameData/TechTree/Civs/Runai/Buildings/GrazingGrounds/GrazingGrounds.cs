// GrazingGrounds.cs
// The Runai mounted trainer (docs/Design/Age_1_Runai.md §4.4): Raider at L1,
// Horse Archer at L2, Camel Lancer at L3. Levels GrazingGrounds_Lvl1..3.

using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class GrazingGrounds
    {
        public const string BuildingId = "Runai_GrazingGrounds";
        public const int PresentationID = 591;   // GrazingGrounds.asset (borrows the Trade Hub's model)

        public static Entity Create(EntityManager em, float3 p, Faction f) => Make(new EmCreator(em), p, f);
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => Make(new EcbCreator(ecb), p, f);

        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiBuildingBody.Base(c, BuildingId, PresentationID, p, f, trainer: true);
            c.AddComponent<GrazingGroundsTag>(e);
            c.AddComponent<BuildingUpgradeable>(e);
            return e;
        }
    }
}
