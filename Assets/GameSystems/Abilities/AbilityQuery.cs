// AbilityQuery.cs
// Read-only queries over a unit's data-driven abilities — used by the unit
// action panel (to draw ability buttons + cooldowns) and CommandRouter (to gate
// firing).

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class AbilityQuery
    {
        /// <summary>True if the unit has at least one Active ability off cooldown.</summary>
        public static bool HasReadyActiveAbility(EntityManager em, Entity unit)
        {
            if (!em.Exists(unit) || !em.HasComponent<UnitAbilities>(unit)) return false;
            var slots = em.GetComponentData<UnitAbilities>(unit);
            var cds = em.HasComponent<AbilityCooldowns>(unit) ? em.GetComponentData<AbilityCooldowns>(unit) : default;
            for (int s = 0; s < 4; s++)
            {
                var card = AbilityCatalog.Get(slots.Get(s));
                if (card == null || card.Activation != AbilityActivation.Active) continue;
                if (Cd(cds, s) <= 0f) return true;
            }
            return false;
        }

        /// <summary>The unit's first Active ability card (regardless of cooldown), for
        /// button display. slot = its UnitAbilities slot, -1 if none.</summary>
        public static AbilityCard FirstActiveCard(EntityManager em, Entity unit, out int slot)
        {
            slot = -1;
            if (!em.Exists(unit) || !em.HasComponent<UnitAbilities>(unit)) return null;
            var slots = em.GetComponentData<UnitAbilities>(unit);
            for (int s = 0; s < 4; s++)
            {
                var card = AbilityCatalog.Get(slots.Get(s));
                if (card != null && card.Activation == AbilityActivation.Active) { slot = s; return card; }
            }
            return null;
        }

        /// <summary>Cooldown remaining (seconds) on a given ability slot.</summary>
        public static float CooldownRemaining(EntityManager em, Entity unit, int slot)
        {
            if (slot < 0 || !em.Exists(unit) || !em.HasComponent<AbilityCooldowns>(unit)) return 0f;
            return Cd(em.GetComponentData<AbilityCooldowns>(unit), slot);
        }

        private static float Cd(AbilityCooldowns c, int slot)
            => slot == 0 ? c.C0 : slot == 1 ? c.C1 : slot == 2 ? c.C2 : c.C3;
    }
}
