// SpellDamage.cs
// The one door every SPELL's direct damage goes through: sect active powers
// (Smite / Sentence, Unmake, Spite, Writ of Attainder, Nowhere to Hide), the
// ability cards (Shardbound Fury) and the legacy sect-unit AoE. Canon:
// docs/Design/Spells.md, "Damage routing".
//
// Before this, each spell subtracted its number from Health directly, so a
// spell ignored everything the combat model knows about the victim: its
// armor, Immovable III's Invulnerable, Liquid Courage's 90% reduction, and
// the Second Wind / Life Cling floors. One spell even hit its caster's ALLIES'
// buildings (the smite's building pass tested `fac == faction`, not
// hostility). Now, in order:
//
//   * friendly fire      -> none unless the caller opts in; Alliances.AreHostile
//                           is the only hostility test (docs/Design/Teams.md)
//   * Invulnerable       -> no damage
//   * the Wall Rule      -> only Siege damages a wall piece (Combat_Pacing.md)
//   * armor              -> the victim's flat armor for the spell's DamageType,
//                           plus Fortified and SpellBuff armor (flat and %);
//                           True damage skips armor entirely. Same formula as
//                           a weapon hit: max(1, damage - armor)
//   * ScaleIncoming      -> SpellBuff.DamageTakenMultiplier (Liquid Courage)
//   * Commit             -> LifeCling / Second Wind floors and hostile kill
//                           credit, via DamageOverTime.Commit
//
// Death is never handled here: the victim's Health is written and DeathSystem
// destroys it on its own pass (the unit death contract). Deterministic: no
// clocks, no randomness, integer results, plain EntityManager writes that are
// safe inside a SystemAPI iteration because none of them is structural.

using Unity.Entities;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Systems.Combat
{
    public static class SpellDamage
    {
        /// <summary>
        /// Deal <paramref name="amount"/> spell damage of
        /// <paramref name="type"/> to <paramref name="victim"/> on behalf of
        /// <paramref name="source"/>. Returns the HP actually removed (0 when
        /// the victim is an ally, immune, a wall, or already dead).
        /// </summary>
        /// <param name="friendlyFire">True only for a spell the design says
        /// hurts everyone in its area; the default spares the caster's side
        /// and its team allies.</param>
        public static int Apply(EntityManager em, Entity victim, int amount, DamageType type,
            Faction source, bool friendlyFire = false)
        {
            if (amount <= 0 || victim == Entity.Null || !em.Exists(victim)) return 0;
            if (!em.HasComponent<Health>(victim)) return 0;
            var hp = em.GetComponentData<Health>(victim);
            if (hp.Value <= 0) return 0;

            if (!friendlyFire && em.HasComponent<FactionTag>(victim)
                && !Alliances.AreHostile(source, em.GetComponentData<FactionTag>(victim).Value))
                return 0;
            if (TransientState.Active<Invulnerable>(em, victim)) return 0;
            if (CombatDamageHelper.WallRuleBlocks(em, victim, type)) return 0;

            int dmg = Mitigate(em, victim, amount, type);
            dmg = AbilityDamageHooks.ScaleIncoming(em, victim, dmg);

            int applied = DamageOverTime.Commit(em, victim, ref hp, dmg, true, source);
            if (applied > 0) em.SetComponentData(victim, hp);
            return applied;
        }

        /// <summary>
        /// Armor step alone: what <paramref name="amount"/> of
        /// <paramref name="type"/> comes to after the victim's armor, with the
        /// weapon formula's floor of 1. True damage is returned unchanged.
        /// Exposed so a tooltip can show what a spell will really do.
        /// </summary>
        public static int Mitigate(EntityManager em, Entity victim, int amount, DamageType type)
        {
            if (amount <= 0) return 0;
            if (type == DamageType.True) return amount;

            int baseDefense = CombatDamageHelper.BaseDefense(em, victim, type);
            int defense = baseDefense;
            if (TransientState.Active<Fortified>(em, victim))
                defense += (int)em.GetComponentData<Fortified>(victim).ArmorBonus;
            defense += CombatDamageHelper.GetSpellBuffArmorBonus(em, victim, baseDefense);

            ArmorType armorType = em.HasComponent<ArmorTypeData>(victim)
                ? em.GetComponentData<ArmorTypeData>(victim).Value
                : em.HasComponent<BuildingTag>(victim) ? ArmorType.Structure : ArmorType.InfantryLight;

            // Neutral height and border modifiers: a spell falls from above,
            // it is not a blow struck up or down a slope.
            return CombatModifiers.CalculateFinalDamage(amount, type, armorType, defense, 1f, 1f);
        }
    }
}
