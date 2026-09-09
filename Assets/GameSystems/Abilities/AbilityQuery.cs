// AbilityQuery.cs
// Read-only queries over a unit's data-driven abilities — used by the unit
// action panel (to draw ability buttons + cooldowns) and CommandRouter (to gate
// firing).

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class AbilityQuery
    {
        /// <summary>
        /// Is this card available on this unit yet? Only hero level gates an
        /// ability today (docs/Design/Heroes.md §2), and a card that declares
        /// no level is available to everyone — which is every card authored
        /// before the hero-level system, so nothing already shipped changes.
        ///
        /// A unit with no HeroLevel at all is treated as level 1: non-heroes
        /// can still be granted ordinary abilities (cavalry get the Royal
        /// Stable horns), and those must keep working.
        /// </summary>
        public static bool IsUnlocked(EntityManager em, Entity unit, AbilityCard card)
        {
            if (card == null) return false;
            if (card.UnlocksAtLevel <= 1) return true;
            if (!em.HasComponent<HeroLevel>(unit)) return false;
            return em.GetComponentData<HeroLevel>(unit).Value >= card.UnlocksAtLevel;
        }

        /// <summary>
        /// The unit's usable Active slots, in slot order, written into
        /// <paramref name="into"/> (length 4). Returns how many were written.
        /// Locked abilities are skipped entirely — see IsUnlocked.
        /// </summary>
        public static int ActiveSlots(EntityManager em, Entity unit, int[] into)
        {
            int n = 0;
            if (into == null || !em.Exists(unit) || !em.HasComponent<UnitAbilities>(unit)) return 0;
            var slots = em.GetComponentData<UnitAbilities>(unit);
            for (int s = 0; s < 4 && n < into.Length; s++)
            {
                var card = AbilityCatalog.Get(slots.Get(s));
                if (card == null || card.Activation != AbilityActivation.Active) continue;
                if (!IsUnlocked(em, unit, card)) continue;
                into[n++] = s;
            }
            return n;
        }

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
                if (!IsUnlocked(em, unit, card)) continue;
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
                if (card == null || card.Activation != AbilityActivation.Active) continue;
                if (!IsUnlocked(em, unit, card)) continue;
                slot = s;
                return card;
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
