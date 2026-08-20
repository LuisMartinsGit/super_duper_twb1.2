// File: Assets/GameData/TechTree/Buildings/Sects/Reliquary/Reliquary.cs
// Sect of Antiquity's building. A vaulted archive.
//
// Structure comes from SectBuilding — every sect building is the same shape
// (capped at 5, trains its sect's unit, sells its sect's research) and differs
// only in stats, tag and mesh. See docs/Design/Sects.md section 1.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Reliquary
    {
        public const int PresentationID = 561;

        /// <summary>Building id — the key BuildingFactory, BuildingCosts and
        /// BuildingSizeConfig all agree on.</summary>
        public const string BuildingId = "Sect_Reliquary";

        private const float DefaultHp = 900f;
        private const float DefaultLos = 16f;

        /// <summary>
        /// Every standing Reliquary shortens the faction's sect-power cooldowns
    /// a little, so spreading them out is the Antiquity tempo play.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => SectBuilding.Create<ReliquaryTag>(em, BuildingId, PresentationID,
                                             DefaultHp, DefaultLos, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => SectBuilding.Create<ReliquaryTag>(ecb, BuildingId, PresentationID,
                                             DefaultHp, DefaultLos, position, faction);
    }
}
