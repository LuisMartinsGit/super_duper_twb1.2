// Surgeon.cs -- Sect of Renewal hero: the Surgeon (docs/Design/Sects.md 4, Religion.md 4).
// Unique; recruited at the sect building for RP + resources. Body from the SO
// through SectUnitSpine, hero kit (levels 1-10, abilities at L1 / L3 / L6)
// through SectUnitSpine.AddHeroKit. The abilities are the SO's list.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Surgeon
    {
        public const int PresentationID = 451;
        public const string UnitId = "Sect_Surgeon";

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var e = SectUnitSpine.Create(creator, UnitId, PresentationID, position, faction);
            SectUnitSpine.AddHeroKit(creator, e, UnitId, TheWaningBorder.Abilities.UniqueUnitKind.Surgeon, SectConfig.Renewal);

            return e;
        }
    }
}
