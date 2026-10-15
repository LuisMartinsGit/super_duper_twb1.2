// Collector.cs -- Sect of Prosperity unit (limit 20): fast melee finisher (Sects.md 4, Run Down).
// Body from the SO through SectUnitSpine; this file adds only the trait.
// Trait tuning lives here as named constants until the sect units get a
// config asset (docs/Design/Sects.md 4).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class Collector
    {
        public const int PresentationID = 371;
        public const string UnitId = "Sect_Collector";
        /// <summary>Run Down: bonus damage against slowed or half-dead enemies.</summary>
        public const float RunDownBonus = 0.5f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var e = SectUnitSpine.Create(creator, UnitId, PresentationID, position, faction);
            creator.AddComponent(e, new RunDownTrait { Bonus = RunDownBonus });
            return e;
        }
    }
}
