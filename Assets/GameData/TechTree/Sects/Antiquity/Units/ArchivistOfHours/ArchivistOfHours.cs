// ArchivistOfHours.cs -- Sect of Antiquity hero: the Archivist of Hours (docs/Design/Sects.md 4, Religion.md 4).
// Unique; recruited at the sect building for RP + resources. Body from the SO
// through SectUnitSpine, hero kit (levels 1-10, abilities at L1 / L3 / L6)
// through SectUnitSpine.AddHeroKit. The abilities are the SO's list.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class ArchivistOfHours
    {
        public const int PresentationID = 450;
        public const string UnitId = "Sect_ArchivistOfHours";

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var e = SectUnitSpine.Create(creator, UnitId, PresentationID, position, faction);
            SectUnitSpine.AddHeroKit(creator, e, UnitId, TheWaningBorder.Abilities.UniqueUnitKind.ArchivistOfHours, SectConfig.Antiquity);
            creator.AddComponent(e, default(ArchivistTrail));   // Recall (L6)
            return e;
        }
    }
}
