// ScarGuard.cs -- Sect of Renewal unit (limit 20): heavy melee shock (Sects.md 4, Borrowed Breath).
// Body from the SO through SectUnitSpine; this file adds only the trait.
// Trait tuning lives here as named constants until the sect units get a
// config asset (docs/Design/Sects.md 4).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class ScarGuard
    {
        public const int PresentationID = 370;
        public const string UnitId = "Sect_ScarGuard";
        /// <summary>Borrowed Breath: damage bonus at the edge of death.</summary>
        public const float BorrowedBreathMax = 0.6f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var e = SectUnitSpine.Create(creator, UnitId, PresentationID, position, faction);
            creator.AddComponent(e, new BorrowedBreathTrait { MaxBonus = BorrowedBreathMax });
            return e;
        }
    }
}
