// BOUNTY — the Sect of Prosperity's buy-out of a fight (docs/Design/Sects.md
// § Sect of Prosperity, Unification decision 47, 2026-11-10). Every hostile
// UNIT the effect reaches dies at once, and its OWNER is paid back what it
// cost: the victim loses time and presence on the field, never resources.
// Three things use it, all through Kill / KillInRadius here:
//   * the sect's A power (an area that grows with the chapel level),
//   * the Bounty Hunter's auto-cast (one target, weaker than the hunter),
//   * the Guildmaster's L1 Bounty (an area that grows with the hero level).
//
// Heroes are immune (they are paid for with levels, not resources), and so
// is anything a ward or invulnerability already protects (SpellDamage). The
// curse has no bank: its units die and nobody is paid.
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    public static class ProsperityBounty
    {
        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        /// <summary>May Bounty take <paramref name="victim"/> for <paramref name="caster"/>?</summary>
        public static bool CanTake(EntityManager em, Faction caster, Entity victim)
        {
            if (victim == Entity.Null || !em.Exists(victim)) return false;
            if (!em.HasComponent<UnitTag>(victim) || !em.HasComponent<Health>(victim)) return false;
            if (em.HasComponent<HeroLevel>(victim)) return false;
            if (em.GetComponentData<Health>(victim).Value <= 0) return false;
            return em.HasComponent<FactionTag>(victim)
                && Alliances.AreHostile(caster, em.GetComponentData<FactionTag>(victim).Value);
        }

        /// <summary>Kill one unit and pay its owner its cost. True when it died.</summary>
        public static bool Kill(EntityManager em, Faction caster, Entity victim)
        {
            if (!CanTake(em, caster, victim)) return false;
            var hp = em.GetComponentData<Health>(victim);
            var owner = em.GetComponentData<FactionTag>(victim).Value;
            int applied = SpellDamage.Apply(em, victim, hp.Value, DamageType.True, caster);
            if (applied < hp.Value) return false;   // a ward or a shield held it
            if (owner != Faction.Border && em.HasComponent<UnitTypeId>(victim)
                && TechCatalog.TryGetUnit(em.GetComponentData<UnitTypeId>(victim).Value.ToString(), out var def)
                && def != null && def.cost != null)
            {
                var refund = Cost.Of(supplies: def.cost.Supplies, iron: def.cost.Iron, veilstone: def.cost.Veilstone);
                if (!refund.IsZero) FactionEconomy.Add(em, owner, refund, IncomeSource.Refund);
            }
            return true;
        }

        /// <summary>Every hostile unit within <paramref name="radius"/> of
        /// <paramref name="center"/> dies and its owner is paid. Returns how many died.
        /// Victims are collected first, then killed, so the walk never sees a
        /// half-dead set (lockstep: entity order is the same on every peer).</summary>
        public static int KillInRadius(EntityManager em, Faction caster, float3 center, float radius)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var victims = new NativeList<Entity>(16, Allocator.Temp);
            float r2 = radius * radius;
            for (int i = 0; i < ents.Length; i++)
                if (math.distancesq(xfs[i].Position.xz, center.xz) <= r2 && CanTake(em, caster, ents[i]))
                    victims.Add(ents[i]);
            int killed = 0;
            for (int i = 0; i < victims.Length; i++)
                if (Kill(em, caster, victims[i])) killed++;
            victims.Dispose();
            return killed;
        }
    }
}
