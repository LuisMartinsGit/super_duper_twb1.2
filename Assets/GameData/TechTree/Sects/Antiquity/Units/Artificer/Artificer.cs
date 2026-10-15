// Artificer.cs -- Sect of Antiquity unit (limit 20): long-range anti-armour glow-lance (Sects.md 4, Stasis Bolt).
// Body from the SO through SectUnitSpine; this file adds only the trait.
// Trait tuning lives here as named constants until the sect units get a
// config asset (docs/Design/Sects.md 4).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class Artificer
    {
        public const int PresentationID = 387;
        public const string UnitId = "Sect_Artificer";
        /// <summary>Stasis Bolt: the target's move and attack slow and duration.</summary>
        public const float StasisMoveSlow = 0.30f;
        public const float StasisAttackSpeed = 0.75f;
        public const float StasisSeconds = 3f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var e = SectUnitSpine.Create(creator, UnitId, PresentationID, position, faction);
            creator.AddComponent(e, new StasisBoltTrait
            {
                MoveSlow = StasisMoveSlow, AttackSlow = StasisAttackSpeed, Seconds = StasisSeconds,
            });
            return e;
        }
    }
}
