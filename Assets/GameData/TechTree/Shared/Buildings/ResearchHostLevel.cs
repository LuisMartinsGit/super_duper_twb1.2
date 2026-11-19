// ResearchHostLevel.cs — the level a research host's techs are gated against
// (TechDefSO.minBuildingLevel). For every building it is the building's own
// level (an unstamped building counts as L1). The TEMPLE has no levels
// (decision 21): its research level is the highest level among the faction's
// adopted sects (decision 77, 2026-11-18, developer: "Litharch tiers unlock
// with the max level of sects adopted") — 0 until a sect is adopted. The UI,
// the AI and the research command all read it here, so they agree.

using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class ResearchHostLevel
    {
        public static int Of(EntityManager em, Entity building)
        {
            if (!em.Exists(building)) return 0;
            if (em.HasComponent<TempleOfRidanTag>(building))
            {
                if (!em.HasComponent<FactionTag>(building)) return 0;
                return SectQuery.MaxAdoptedLevel(em, em.GetComponentData<FactionTag>(building).Value);
            }
            int level = 1;
            if (em.HasComponent<BuildingUpgradeState>(building))
            {
                int lv = em.GetComponentData<BuildingUpgradeState>(building).Level;
                if (lv > level) level = lv;
            }
            return level;
        }
    }
}
