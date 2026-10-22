// AbilityCooldownUtil.cs
// Give a cast its cooldown back. The lifecycle charges a card's cooldown the
// moment the cast completes, BEFORE the effects run (AbilityLifecycleSystem),
// so an effect that refuses to land — a Field Hospital past the team's cap
// or too close to another — would otherwise burn the cooldown for nothing.

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class AbilityCooldownUtil
    {
        /// <summary>Zero the cooldown of <paramref name="card"/>'s slot on
        /// <paramref name="caster"/>, when it carries the card.</summary>
        public static void Refund(EntityManager em, Entity caster, AbilityCard card)
        {
            if (card == null || caster == Entity.Null || !em.Exists(caster)) return;
            if (!em.HasComponent<UnitAbilities>(caster) || !em.HasComponent<AbilityCooldowns>(caster)) return;
            int idx = AbilityCatalog.IndexOf(card.Name);
            if (idx < 0) return;
            var ua = em.GetComponentData<UnitAbilities>(caster);
            int slot = -1;
            for (int s = 0; s < 4; s++) if (ua.Get(s) == idx) { slot = s; break; }
            if (slot < 0) return;
            var c = em.GetComponentData<AbilityCooldowns>(caster);
            switch (slot) { case 0: c.C0 = 0f; break; case 1: c.C1 = 0f; break; case 2: c.C2 = 0f; break; default: c.C3 = 0f; break; }
            em.SetComponentData(caster, c);
        }
    }
}
