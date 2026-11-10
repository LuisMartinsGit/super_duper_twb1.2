// Collector.cs -- Sect of Prosperity unit (limit 20), shown as the BOUNTY HUNTER
// (renamed 2026-11-10, Sects.md § Sect of Prosperity, decision 47; the id stays
// Sect_Collector). Body from the SO through SectUnitSpine; this file adds only
// the trait. Trait tuning lives here as named constants until the sect units
// get a config asset (docs/Design/Sects.md 4).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class Collector
    {
        public const int PresentationID = 371;
        public const string UnitId = "Sect_Collector";
        /// <summary>Bounty: seconds between the hunter's casts.</summary>
        public const float BountyIntervalSeconds = 20f;
        /// <summary>Bounty: how far from the hunter its target may stand.</summary>
        public const float BountyRange = 10f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var e = SectUnitSpine.Create(creator, UnitId, PresentationID, position, faction);
            creator.AddComponent(e, new BountyHunterTrait
            {
                Interval = BountyIntervalSeconds,
                Range = BountyRange,
                Timer = BountyIntervalSeconds,
            });
            return e;
        }
    }
}
