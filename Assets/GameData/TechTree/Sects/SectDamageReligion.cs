// SectDamageReligion.cs
// Sect units earn religion points in proportion to weapon damage dealt
// (docs/Design/Monuments_And_Petriarchy.md §4). AbilityDamageHooks reports every
// melee and projectile hit here; the damage is kept per faction on the bank, so
// it is the same on every peer.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    public static class SectDamageReligion
    {
        private static HashSet<FixedString64Bytes> _earners;

        private static HashSet<FixedString64Bytes> Earners()
        {
            if (_earners != null) return _earners;
            var set = new HashSet<FixedString64Bytes>();
            foreach (var sect in SectConfig.AllSectIds)
            {
                string unit = SectConfig.UnitIdFor(sect);
                if (!string.IsNullOrEmpty(unit)) set.Add(new FixedString64Bytes(unit));
                string hero = SectConfig.HeroIdFor(sect);
                if (!string.IsNullOrEmpty(hero)) set.Add(new FixedString64Bytes(hero));
            }
            return _earners = set;
        }

        public static void Note(EntityManager em, Entity attacker, int damage)
        {
            if (damage <= 0 || !em.Exists(attacker)) return;
            if (!em.HasComponent<UnitTypeId>(attacker) || !em.HasComponent<FactionTag>(attacker)) return;
            if (!Earners().Contains(em.GetComponentData<UnitTypeId>(attacker).Value)) return;

            var faction = em.GetComponentData<FactionTag>(attacker).Value;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return;

            int per = FactionReligionPointsHelper.Cfg.sectDamagePerRp;
            if (per <= 0) return;
            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            rp.SectDamage += damage;
            while (rp.SectDamage >= per)
            {
                rp.SectDamage -= per;
                rp.Balance++;
            }
            em.SetComponentData(bank, rp);
        }
    }
}
