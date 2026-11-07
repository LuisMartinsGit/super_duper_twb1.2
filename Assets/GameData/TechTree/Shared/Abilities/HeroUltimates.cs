// HeroUltimates.cs
// The level-10 abilities: each must be able to end the game (docs/Design/Heroes.md
// §3). Cast by the player or the AI's own rule, on every peer, with no
// line-of-sight limit. Reach, duration and strength are on each card's SO.
//
//   Call to the Banners  (Lexor)        PledgeArmy.SummonBanners
//   Foreclosure          (Ledger)       a share of every hostile stockpile
//   Stop the Hours       (Archivist)    every hostile unit frozen, heroes silenced
//   The Great Rewind     (Surgeon)      army healed, the recently fallen return
//   The Realm Holds      (Warden)       hostiles on your ground hurt, buildings safe
//   Buy Out              (Guildmaster)  enemy buildings in the circle fall, you are paid
//
// Blood Oath (Harkon) and Exodus (Wayfinder) stay with their casters' code.
//
// They share this file rather than one folder per owner because they are one
// rule across eight owners, and every one of them is a dozen lines.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Abilities
{
    public static class HeroUltimates
    {
        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Buildings;

        /// <summary>Player factions a bank can exist for.</summary>
        private const int PlayerSlots = 8;
        /// <summary>The Great Rewind's reach: the whole map.</summary>
        private const float WholeMap = 100000f;

        private static Faction FactionOf(EntityManager em, Entity e) => em.GetComponentData<FactionTag>(e).Value;

        private static void Announce(string text, Entity caster, EntityManager em)
        {
            SimSignals.Announce(text);
            if (em.HasComponent<LocalTransform>(caster))
                SimSignals.Ping(em.GetComponentData<LocalTransform>(caster).Position, SimPingKind.Combat, 20f, big: true);
        }

        /// <summary>Foreclosure: value% of every hostile faction's stockpile is yours.</summary>
        public static void Foreclosure(EntityManager em, Entity caster, float percent)
        {
            var me = FactionOf(em, caster);
            float share = math.saturate(percent / 100f);
            for (int i = 0; i < PlayerSlots; i++)
            {
                var f = (Faction)i;
                if (f == me || !Alliances.AreHostile(me, f)) continue;
                if (!FactionEconomy.TryGetResources(em, f, out var bank)) continue;
                var take = Cost.Of((int)(bank.Supplies * share), (int)(bank.Iron * share),
                    (int)(bank.Veilstone * share), (int)(bank.Veilsteel * share));
                if (take.IsZero || !FactionEconomy.Spend(em, f, take)) continue;
                FactionEconomy.Add(em, me, take, IncomeSource.Loot);
            }
            Announce(string.Format(Loc.T("{0} FORECLOSES — the realm's debts are called in!"), me), caster, em);
        }

        /// <summary>Stop the Hours: every hostile unit is frozen for value seconds
        /// and every hostile hero's abilities go on cooldown. Freezing reuses
        /// Launched with no height and no landing damage: the one hard stun, already
        /// excluded from movement, targeting and both attack systems. Ascended
        /// heroes cannot be disabled (Heroes.md §4).</summary>
        public static void StopTheHours(EntityManager em, Entity caster, AbilityCard card, float seconds)
        {
            var me = FactionOf(em, caster);
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (hps[i].Value <= 0 || !Alliances.AreHostile(me, facs[i].Value)) continue;
                if (em.HasComponent<TheWaningBorder.Entities.AscendedHero>(e)) continue;
                if (em.HasComponent<HeroLevel>(e))
                    TheWaningBorder.Systems.Sect.SectHeroAbilities.StampOfRecord(em, caster, card, e);
                // Kept at their height, or they would fall off the deck or the sky.
                if (em.HasComponent<TheWaningBorder.Entities.Launched>(e)
                    || em.HasComponent<EmplacedEngineTag>(e) || em.HasComponent<FlyingTag>(e)
                    || CombatDamageHelper.OnWallTop(em, e)
                    || TransientState.Active<DeathAnimationState>(em, e)) continue;
                em.AddComponentData(e, new TheWaningBorder.Entities.Launched
                {
                    Duration = math.max(0.1f, seconds), From = xfs[i].Position, To = xfs[i].Position,
                    Source = me, DamageKind = DamageType.True,
                });
                if (em.HasComponent<DesiredDestination>(e))
                {
                    var dd = em.GetComponentData<DesiredDestination>(e);
                    dd.Has = 0;
                    em.SetComponentData(e, dd);
                }
                if (em.HasComponent<Target>(e)) em.SetComponentData(e, new Target { Value = Entity.Null });
            }
            Announce(string.Format(Loc.T("{0} STOPS THE HOURS — the enemy stands frozen!"), me), caster, em);
        }

        /// <summary>The Great Rewind: every unit of yours at full HP; up to
        /// <paramref name="most"/> units fallen in the card's duration return whole.</summary>
        public static void GreatRewind(EntityManager em, Entity caster, float window, float most)
        {
            var me = FactionOf(em, caster);
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != me || hps[i].Value <= 0) continue;
                var h = hps[i]; h.Value = h.Max;
                em.SetComponentData(ents[i], h);
            }
            TheWaningBorder.Systems.Sect.SectActivePowerHelper.ReviveFallen(em, me, float3.zero, WholeMap, 1f, window, most);
            Announce(string.Format(Loc.T("{0} REWINDS — the fallen rise and the wounded are whole!"), me), caster, em);
        }

        /// <summary>The Realm Holds: hostile units standing on your or an ally's
        /// ground lose value% of max HP; your buildings cannot be hurt for the duration.</summary>
        public static void RealmHolds(EntityManager em, Entity caster, float percent, float duration)
        {
            var me = FactionOf(em, caster);
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0 || !Alliances.AreHostile(me, facs[i].Value)) continue;
                int owner = TheWaningBorder.World.Regions.TerritoryOwnership.OwnerAt(xfs[i].Position.x, xfs[i].Position.z);
                if (owner < 0 || owner >= PlayerSlots || !Alliances.AreAllied(me, (Faction)owner)) continue;
                int hit = (int)(hps[i].Max * math.saturate(percent / 100f));
                if (hit > 0) SpellDamage.Apply(em, ents[i], hit, DamageType.True, me);
            }
            var b = QC_Buildings.Get(em, QT_Buildings);
            using var bents = b.ToEntityArray(Allocator.Temp);
            using var bfacs = b.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < bents.Length; i++)
                if (bfacs[i].Value == me)
                    TransientState.Set(em, bents[i], new Invulnerable { TimeRemaining = duration });
            Announce(string.Format(Loc.T("{0}'s REALM HOLDS — the ground itself turns on the invader!"), me), caster, em);
        }

        /// <summary>Buy Out: every hostile building within the card's radius of
        /// the aim falls and pays you value% of its worth. A capital loses value%
        /// of its max HP instead.</summary>
        public static void BuyOut(EntityManager em, Entity caster, AbilityCard card, float percent)
        {
            if (!em.HasComponent<AbilityAimPoint>(caster)) return;
            var me = FactionOf(em, caster);
            float3 aim = em.GetComponentData<AbilityAimPoint>(caster).Position;
            float pct = math.saturate(percent / 100f);
            float r2 = card.Radius * card.Radius;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (hps[i].Value <= 0 || !Alliances.AreHostile(me, facs[i].Value)) continue;
                if (math.distancesq(xfs[i].Position.xz, aim.xz) > r2) continue;
                if (em.HasComponent<HallTag>(e) || em.HasComponent<FortressTag>(e))
                {
                    SpellDamage.Apply(em, e, (int)(hps[i].Max * pct), DamageType.True, me);
                    continue;
                }
                var worth = TheWaningBorder.Feraldis.FeraldisRaidIncomeSystem.BuildingWorth(em, e, facs[i].Value);
                var pay = TheWaningBorder.Feraldis.FeraldisRaidIncomeSystem.Scale(worth, pct);
                if (!pay.IsZero) FactionEconomy.Add(em, me, pay, IncomeSource.Loot);
                SpellDamage.Apply(em, e, hps[i].Value, DamageType.True, me);
            }
            SimSignals.Ping(aim, SimPingKind.Combat, card.Radius, big: true);
            SimSignals.Announce(string.Format(Loc.T("{0} BUYS OUT a district — its buildings fall!"), me));
        }
    }
}
