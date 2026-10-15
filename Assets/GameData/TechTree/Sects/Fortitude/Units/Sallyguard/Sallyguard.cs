// Sallyguard.cs -- Sect of Fortitude unit (limit 20): anti-siege striker (Sects.md 4, Stored Strike).
// Body from the SO through SectUnitSpine; this file adds only the trait.
// Trait tuning lives here as named constants until the sect units get a
// config asset (docs/Design/Sects.md 4).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class Sallyguard
    {
        public const int PresentationID = 372;
        public const string UnitId = "Sect_Sallyguard";
        /// <summary>Stored Strike: seconds out of combat to a full charge, the
        /// multiplier of the charged blow, and the delay before charging starts.</summary>
        public const float StoredStrikeSeconds = 8f;
        public const float StoredStrikeMultiplier = 2f;
        public const float StoredStrikeDelay = 2f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var e = SectUnitSpine.Create(creator, UnitId, PresentationID, position, faction);
            creator.AddComponent(e, new StoredStrike
            {
                Charge = StoredStrikeSeconds,   // trained ready
                ChargeSeconds = StoredStrikeSeconds,
                Multiplier = StoredStrikeMultiplier,
                OutOfCombatDelay = StoredStrikeDelay,
            });
            return e;
        }
    }
}
