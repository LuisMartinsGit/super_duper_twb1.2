// AbilityAssignment.cs
// Helpers to attach data-driven abilities to a unit entity: sets the
// UnitAbilities slots (catalog indices) and ensures AbilityCooldowns exists.
// Used by unit factories (build the component up front) and by research grants
// (add an ability to an existing unit — Scouting Celestarii -> Use Celestar).

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class AbilityAssignment
    {
        /// <summary>Build a UnitAbilities from ability names (missing names ignored).</summary>
        public static UnitAbilities Build(params string[] abilityNames)
        {
            var ua = UnitAbilities.From();
            int slot = 0;
            if (abilityNames != null)
            {
                for (int i = 0; i < abilityNames.Length && slot < 4; i++)
                {
                    int idx = AbilityCatalog.IndexOf(abilityNames[i]);
                    if (idx < 0) continue;
                    switch (slot) { case 0: ua.S0 = idx; break; case 1: ua.S1 = idx; break; case 2: ua.S2 = idx; break; default: ua.S3 = idx; break; }
                    slot++;
                }
            }
            return ua;
        }

        /// <summary>
        /// The Royal Stable horn abilities a freshly trained cavalry unit should
        /// spawn with, given what the faction has researched. Mirrors the
        /// Scouting Celestarii spawn gate in Scout.Create so new cavalry matches
        /// units that were granted the ability retroactively by TechEffectSystem.
        /// Order matters: War Horn is listed first so the first-ready-active cast
        /// router prefers the charge buff over the sprint.
        /// </summary>
        public static UnitAbilities BuildCavalryAbilities(Faction faction, string[] authored)
        {
            var names = new System.Collections.Generic.List<string>();
            if (authored != null && authored.Length > 0) names.AddRange(authored);

            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null)
            {
                if (research.HasResearched(faction, "WarHorn") && !names.Contains("War Horn"))
                    names.Add("War Horn");
                if (research.HasResearched(faction, "FullGallop") && !names.Contains("Full Gallop"))
                    names.Add("Full Gallop");
            }
            return Build(names.ToArray());
        }

        /// <summary>Add one ability (by catalog index) to an existing unit's first
        /// free slot. No-op if already present or no slot free.</summary>
        public static void AddAbility(EntityManager em, Entity e, int abilityIndex)
        {
            if (abilityIndex < 0 || !em.Exists(e)) return;
            var ua = em.HasComponent<UnitAbilities>(e) ? em.GetComponentData<UnitAbilities>(e) : UnitAbilities.From();

            if (ua.S0 == abilityIndex || ua.S1 == abilityIndex || ua.S2 == abilityIndex || ua.S3 == abilityIndex) return;

            if (ua.S0 < 0) ua.S0 = abilityIndex;
            else if (ua.S1 < 0) ua.S1 = abilityIndex;
            else if (ua.S2 < 0) ua.S2 = abilityIndex;
            else if (ua.S3 < 0) ua.S3 = abilityIndex;
            else return; // no free slot

            if (em.HasComponent<UnitAbilities>(e)) em.SetComponentData(e, ua);
            else em.AddComponentData(e, ua);
            if (!em.HasComponent<AbilityCooldowns>(e)) em.AddComponentData(e, default(AbilityCooldowns));
        }
    }
}
