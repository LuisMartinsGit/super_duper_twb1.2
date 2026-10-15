// Arrowyard.cs
// The Runai ranged trainer (docs/Design/Age_1_Runai.md §4): Skirmisher at L1,
// Slinger at L2, Sun Archer at L3. Levels Arrowyard_Lvl1..3.

using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class Arrowyard
    {
        public const string BuildingId = "Runai_Arrowyard";
        public const int PresentationID = 593;   // Arrowyard.asset

        public static Entity Create(EntityManager em, float3 p, Faction f) => Make(new EmCreator(em), p, f);
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => Make(new EcbCreator(ecb), p, f);

        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiBuildingBody.Base(c, BuildingId, PresentationID, p, f, trainer: true);
            c.AddComponent<ArrowyardTag>(e);
            c.AddComponent<BuildingUpgradeable>(e);
            return e;
        }
    }
}
