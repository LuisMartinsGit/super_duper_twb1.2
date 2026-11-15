// BOUNTY — the Sect of Prosperity's price on a fight (docs/Design/Sects.md
// § Sect of Prosperity; decision 47, 2026-11-10, reworked by decision 59,
// 2026-11-15). Developer: "instantly wiping out an army is not fun", and
// "units that die pay YOU half their cost, not the owner".
//
// Every hostile UNIT the effect reaches takes a share of its MAXIMUM health
// as true damage and is MARKED for MarkSeconds: if it dies while marked —
// to the strike itself or to anything after it — the CASTER is paid
// PayShare of its cost (AlanthorSectPowerSystem's death scan pays the mark).
// Nothing dies outright any more. Three things use it, all through Strike /
// StrikeInRadius here:
//   * the sect's A power (an area that grows with the chapel level),
//   * the Bounty Hunter's auto-cast (one target, weaker than the hunter),
//   * the Guildmaster's L1 Bounty (an area that grows with the hero level).
//
// Heroes are immune (they are paid for with levels, not resources), and so
// is anything a ward or invulnerability already protects (SpellDamage). The
// curse's units are struck like anyone's; they have no cost, so they pay
// nothing.
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    public static class ProsperityBounty
    {
        /// <summary>The share of maximum health the sect power and the
        /// Guildmaster's Bounty deal.</summary>
        public const float StrikeShare = 0.40f;
        /// <summary>The share of maximum health the Bounty Hunter's single
        /// strike deals.</summary>
        public const float HunterShare = 0.60f;
        /// <summary>Seconds a struck unit stays marked.</summary>
        public const float MarkSeconds = 10f;
        /// <summary>The share of a marked unit's cost its death pays the caster.</summary>
        public const float PayShare = 0.5f;

        /// <summary>The sect power's circle by chapel level, metres.</summary>
        public static float PowerRadius(int level) => level <= 1 ? 8f : level == 2 ? 12f : 15f;

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        /// <summary>May Bounty strike <paramref name="victim"/> for <paramref name="caster"/>?</summary>
        public static bool CanTake(EntityManager em, Faction caster, Entity victim)
        {
            if (victim == Entity.Null || !em.Exists(victim)) return false;
            if (!em.HasComponent<UnitTag>(victim) || !em.HasComponent<Health>(victim)) return false;
            if (em.HasComponent<HeroLevel>(victim)) return false;
            if (em.GetComponentData<Health>(victim).Value <= 0) return false;
            return em.HasComponent<FactionTag>(victim)
                && Alliances.AreHostile(caster, em.GetComponentData<FactionTag>(victim).Value);
        }

        /// <summary>Mark one unit for the caster, then deal <paramref name="share"/>
        /// of its maximum health as true damage. True when any damage landed.</summary>
        public static bool Strike(EntityManager em, Faction caster, Entity victim, float share)
        {
            if (!CanTake(em, caster, victim)) return false;
            var hp = em.GetComponentData<Health>(victim);
            int amount = math.max(1, (int)math.round(math.max(hp.Max, hp.Value) * share));
            // The mark goes on first, so a strike that kills already pays.
            TransientState.Set(em, victim, new BountyMarked { ByFaction = caster, TimeRemaining = MarkSeconds });
            return SpellDamage.Apply(em, victim, amount, DamageType.True, caster) > 0;
        }

        /// <summary>What a marked unit's death pays its caster.</summary>
        public static Cost Payout(UnitDef def)
        {
            if (def == null || def.cost == null) return default;
            return Cost.Of(
                supplies: (int)math.round(def.cost.Supplies * PayShare),
                iron: (int)math.round(def.cost.Iron * PayShare),
                veilstone: (int)math.round(def.cost.Veilstone * PayShare));
        }

        /// <summary>Strike every hostile unit within <paramref name="radius"/> of
        /// <paramref name="center"/>. Returns how many were struck.
        /// Victims are collected first, then struck, so the walk never sees a
        /// half-dead set (lockstep: entity order is the same on every peer).</summary>
        public static int StrikeInRadius(EntityManager em, Faction caster, float3 center, float radius, float share)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var victims = new NativeList<Entity>(16, Allocator.Temp);
            float r2 = radius * radius;
            for (int i = 0; i < ents.Length; i++)
                if (math.distancesq(xfs[i].Position.xz, center.xz) <= r2 && CanTake(em, caster, ents[i]))
                    victims.Add(ents[i]);
            int struck = 0;
            for (int i = 0; i < victims.Length; i++)
                if (Strike(em, caster, victims[i], share)) struck++;
            victims.Dispose();
            return struck;
        }
    }
}
