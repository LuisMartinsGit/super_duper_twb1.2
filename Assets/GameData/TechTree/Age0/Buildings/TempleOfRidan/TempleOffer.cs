// TempleOffer.cs — what the Temple of Ridan offers, by age (2026-11-18,
// docs/Design/Age_0.md § Temple of Ridan). Developer: "most actions should be
// invisible" — in Age 0 the Temple trains the Litharch and nothing else; from
// Age 1 it adds the research that arms the Litharch (Warrior Priests). The
// lists live on TempleOffer.asset (TempleOfferConfig).
//
// ONE rule, read by everyone, so the player and the AI stay equal: the panel
// (EntityExtractors), the AI research sweep, the research command
// (CommandRouter.ResearchCommandDirect) and the Petriarchy purchase check
// (ReligionPurchases.CanBuy) all ask here.

using Unity.Entities;

namespace TheWaningBorder.Entities
{
    public static class TempleOffer
    {
        static TempleOfferConfig Cfg => TempleOfferConfig.I;

        /// <summary>Still in Age 0 (no completed age-up).</summary>
        public static bool InAge0(EntityManager em, Faction faction)
            => CultureConfig.GetCompletedCulture(em, faction) == Cultures.None;

        /// <summary>May this faction research <paramref name="techId"/> at its
        /// Temple now? Only techs the Temple hosts are asked about.</summary>
        public static bool OffersResearch(EntityManager em, Faction faction, string techId)
        {
            var cfg = Cfg;
            if (cfg == null || string.IsNullOrEmpty(techId)) return false;
            var list = InAge0(em, faction) ? cfg.age0Research : cfg.research;
            if (list == null) return false;
            for (int i = 0; i < list.Length; i++)
                if (list[i] == techId) return true;
            return false;
        }

        /// <summary>Do adopted sects' units train at the Temple now?</summary>
        public static bool OffersSectUnits(EntityManager em, Faction faction)
        {
            var cfg = Cfg;
            return cfg != null && cfg.sectUnits && !InAge0(em, faction);
        }

        /// <summary>Are the Petriarchy's Support and Army sold now?</summary>
        public static bool OffersPetriarchy(EntityManager em, Faction faction)
        {
            var cfg = Cfg;
            return cfg != null && cfg.petriarchy && !InAge0(em, faction);
        }
    }
}
