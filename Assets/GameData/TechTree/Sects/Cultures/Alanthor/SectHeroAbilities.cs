// SectHeroAbilities.cs
// The Alanthor sect heroes' abilities (docs/Design/Sects.md §4, 2026-10-15).
// Active ones are AbilityCard effects run by AbilityEffectExecutor; the four
// L1 auras (Catalogued, Cauterize, Called In and — in StoredStrikeChargeSystem —
// Drillmaster) and the timed state of all of them run in SectHeroAuraSystem.
//
//   Archivist of Hours  L1 Catalogued   L3 Stamp of Record   L6 Recall
//   Surgeon             L1 Cauterize    L3 Bloodletting      L6 Lancet
//   Warden              L1 Drillmaster  L3 Sally Forth       L6 Challenge
//   Guildmaster         L1 Called In    L3 Stake             L6 Liquidate
//
// Ability casts arrive through the lockstep command path, so every handler
// here runs on every peer with the same inputs.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    public static class SectHeroAbilities
    {
        /// <summary>How close the aim point must be to a unit or building to pick it.</summary>
        public const float PickRadius = 3f;
        /// <summary>Lancet bleed: the single-target band spread over its duration.</summary>
        public const float LancetTotalDamage = 120f;
        /// <summary>Bloodletting: the HP fraction allies drop to.</summary>
        public const float BloodlettingFloor = 0.35f;
        /// <summary>Sally Forth: allied move-speed multiplier.</summary>
        public const float SallySpeed = 1.25f;
        /// <summary>Stake: how close to its extractor the Guildmaster must stay.</summary>
        public const float StakeRange = 4f;

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Buildings;

        // ── helpers ─────────────────────────────────────────────────────────

        private static Faction FactionOf(EntityManager em, Entity e)
            => em.HasComponent<FactionTag>(e) ? em.GetComponentData<FactionTag>(e).Value : Faction.Border;

        private static float3 PosOf(EntityManager em, Entity e)
            => em.HasComponent<LocalTransform>(e) ? em.GetComponentData<LocalTransform>(e).Position : float3.zero;

        private static float3 AimOf(EntityManager em, Entity caster)
            => em.HasComponent<AbilityAimPoint>(caster)
                ? em.GetComponentData<AbilityAimPoint>(caster).Position : PosOf(em, caster);

        /// <summary>Live units within <paramref name="radius"/> of a point that
        /// are allied (or hostile) to <paramref name="faction"/>.</summary>
        public static NativeList<Entity> UnitsNear(EntityManager em, float3 at, float radius,
            Faction faction, bool hostile)
        {
            var list = new NativeList<Entity>(16, Allocator.Temp);
            float r2 = radius * radius;
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                bool match = hostile ? Alliances.AreHostile(faction, facs[i].Value)
                                     : Alliances.AreAllied(faction, facs[i].Value);
                if (!match) continue;
                if (math.distancesq(trs[i].Position.xz, at.xz) > r2) continue;
                list.Add(ents[i]);
            }
            return list;
        }

        private static Entity NearestUnit(EntityManager em, float3 at, float radius, Faction faction, bool hostile)
        {
            using var near = UnitsNear(em, at, radius, faction, hostile);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < near.Length; i++)
            {
                float d = math.distancesq(PosOf(em, near[i]).xz, at.xz);
                if (d < bestD) { bestD = d; best = near[i]; }
            }
            return best;
        }

        private static Entity NearestOwnBuilding(EntityManager em, float3 at, float radius, Faction faction)
        {
            Entity best = Entity.Null;
            float bestD = radius * radius;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction || hps[i].Value <= 0) continue;
                float d = math.distancesq(trs[i].Position.xz, at.xz);
                if (d < bestD) { bestD = d; best = ents[i]; }
            }
            return best;
        }

        // ── Archivist of Hours ──────────────────────────────────────────────

        /// <summary>L3: every ability of the aimed enemy goes on its full cooldown.</summary>
        public static void StampOfRecord(EntityManager em, Entity caster, AbilityCard card)
        {
            var target = NearestUnit(em, AimOf(em, caster), PickRadius, FactionOf(em, caster), hostile: true);
            if (target == Entity.Null) return;
            if (em.HasComponent<UnitAbilities>(target) && em.HasComponent<AbilityCooldowns>(target))
            {
                var slots = em.GetComponentData<UnitAbilities>(target);
                var cd = em.GetComponentData<AbilityCooldowns>(target);
                var c0 = AbilityCatalog.Get(slots.Get(0)); if (c0 != null) cd.C0 = math.max(cd.C0, c0.EffectiveCooldown);
                var c1 = AbilityCatalog.Get(slots.Get(1)); if (c1 != null) cd.C1 = math.max(cd.C1, c1.EffectiveCooldown);
                var c2 = AbilityCatalog.Get(slots.Get(2)); if (c2 != null) cd.C2 = math.max(cd.C2, c2.EffectiveCooldown);
                var c3 = AbilityCatalog.Get(slots.Get(3)); if (c3 != null) cd.C3 = math.max(cd.C3, c3.EffectiveCooldown);
                em.SetComponentData(target, cd);
            }
            if (em.HasComponent<UnitAbility>(target))
            {
                var ua = em.GetComponentData<UnitAbility>(target);
                ua.CooldownRemaining = math.max(ua.CooldownRemaining, ua.CooldownDuration);
                em.SetComponentData(target, ua);
            }
        }

        /// <summary>L6: the Archivist and allies within Radius step back to
        /// where the Archivist stood 5 s ago (each keeping its offset).
        /// Position only — HP is unchanged.</summary>
        public static void Recall(EntityManager em, Entity caster, AbilityCard card)
        {
            if (!em.HasComponent<ArchivistTrail>(caster)) return;
            var trail = em.GetComponentData<ArchivistTrail>(caster);
            if (trail.Count == 0) return;
            float3 past = Oldest(trail);
            float3 now = PosOf(em, caster);
            float3 delta = past - now;
            delta.y = 0f;
            using var allies = UnitsNear(em, now, card.Radius, FactionOf(em, caster), hostile: false);
            for (int i = 0; i < allies.Length; i++)
            {
                var lt = em.GetComponentData<LocalTransform>(allies[i]);
                lt.Position += delta;
                em.SetComponentData(allies[i], lt);
                TransientState.Clear<TheWaningBorder.Core.Commands.Types.MoveCommand>(em, allies[i]);
                TransientState.Clear<TheWaningBorder.Core.Commands.Types.AttackMoveCommand>(em, allies[i]);
            }
            trail.Count = 0;
            em.SetComponentData(caster, trail);
        }

        private static float3 Oldest(in ArchivistTrail t)
        {
            int idx = (t.Head - t.Count + 10) % 10;
            return idx switch
            {
                0 => t.P0, 1 => t.P1, 2 => t.P2, 3 => t.P3, 4 => t.P4,
                5 => t.P5, 6 => t.P6, 7 => t.P7, 8 => t.P8, _ => t.P9,
            };
        }

        /// <summary>Record the Archivist's position (SectHeroAuraSystem, every 0.5 s).</summary>
        public static void PushTrail(ref ArchivistTrail t, float3 p)
        {
            switch (t.Head)
            {
                case 0: t.P0 = p; break; case 1: t.P1 = p; break; case 2: t.P2 = p; break;
                case 3: t.P3 = p; break; case 4: t.P4 = p; break; case 5: t.P5 = p; break;
                case 6: t.P6 = p; break; case 7: t.P7 = p; break; case 8: t.P8 = p; break;
                default: t.P9 = p; break;
            }
            t.Head = (byte)((t.Head + 1) % 10);
            if (t.Count < 10) t.Count++;
        }

        // ── Surgeon ─────────────────────────────────────────────────────────

        /// <summary>L3: allies within Radius above 35% HP drop to 35% (never
        /// killed) and attack <paramref name="attackPct"/>% faster.</summary>
        public static void Bloodletting(EntityManager em, Entity caster, AbilityCard card, float attackPct, float dur)
        {
            using var allies = UnitsNear(em, PosOf(em, caster), card.Radius, FactionOf(em, caster), hostile: false);
            for (int i = 0; i < allies.Length; i++)
            {
                var hp = em.GetComponentData<Health>(allies[i]);
                int floor = math.max(1, (int)math.round(hp.Max * BloodlettingFloor));
                if (hp.Value > floor) { hp.Value = floor; em.SetComponentData(allies[i], hp); }
                var h = TransientState.Active<SectHaste>(em, allies[i]) ? em.GetComponentData<SectHaste>(allies[i]) : default;
                h.Multiplier = math.max(h.Multiplier, 1f + attackPct / 100f);
                h.TimeRemaining = math.max(h.TimeRemaining, dur);
                TransientState.Set(em, allies[i], h);
            }
        }

        /// <summary>L6: the aimed enemy bleeds for the duration and cannot be healed.</summary>
        public static void Lancet(EntityManager em, Entity caster, AbilityCard card, float dur)
        {
            var target = NearestUnit(em, AimOf(em, caster), PickRadius, FactionOf(em, caster), hostile: true);
            if (target == Entity.Null) return;
            float seconds = math.max(1f, dur);
            TransientState.Set(em, target, new Lanced
            {
                LastHp = em.GetComponentData<Health>(target).Value,
                DamagePerSecond = LancetTotalDamage / seconds,
                TimeRemaining = seconds,
                Source = FactionOf(em, caster),
            });
        }

        // ── Warden ──────────────────────────────────────────────────────────

        /// <summary>L3: allies within Radius move faster; Sallyguards among
        /// them are fully charged at once.</summary>
        public static void SallyForth(EntityManager em, Entity caster, AbilityCard card, float dur)
        {
            using var allies = UnitsNear(em, PosOf(em, caster), card.Radius, FactionOf(em, caster), hostile: false);
            for (int i = 0; i < allies.Length; i++)
            {
                var b = TransientState.Active<SpellBuff>(em, allies[i]) ? em.GetComponentData<SpellBuff>(allies[i]) : default;
                b.SpeedMultiplier = math.max(b.SpeedMultiplier, SallySpeed);
                b.TimeRemaining = math.max(b.TimeRemaining, dur);
                if (b.DamageMultiplier <= 0f) b.DamageMultiplier = 1f;
                TransientState.Set(em, allies[i], b);
                if (em.HasComponent<StoredStrike>(allies[i]))
                {
                    var ss = em.GetComponentData<StoredStrike>(allies[i]);
                    ss.Charge = ss.ChargeSeconds;
                    em.SetComponentData(allies[i], ss);
                }
            }
        }

        /// <summary>L6: enemies within Radius, siege included, must attack the
        /// Warden for the duration. A taunt only — no invulnerability.</summary>
        public static void Challenge(EntityManager em, Entity caster, AbilityCard card, float dur)
        {
            using var enemies = UnitsNear(em, PosOf(em, caster), card.Radius, FactionOf(em, caster), hostile: true);
            for (int i = 0; i < enemies.Length; i++)
                TransientState.Set(em, enemies[i], new Taunted { Taunter = caster, TimeRemaining = dur });
        }

        // ── Guildmaster ─────────────────────────────────────────────────────

        /// <summary>L3: channel on the extractor under the Guildmaster; it pays
        /// double while the hero stays and takes no damage.</summary>
        public static void Stake(EntityManager em, Entity caster, float dur)
        {
            if (!em.HasComponent<StakeChannel>(caster)) return;
            var node = NearestOwnBuilding(em, PosOf(em, caster), StakeRange, FactionOf(em, caster));
            if (node == Entity.Null) return;
            em.SetComponentData(caster, new StakeChannel
            {
                Node = node,
                TimeRemaining = dur,
                HealthAtStart = em.GetComponentData<Health>(caster).Value,
            });
        }

        /// <summary>L6: demolish the aimed own building at once for
        /// <paramref name="pct"/>% of everything put into it. The enemy gets no
        /// raze bounty or kill credit. The capital cannot be liquidated.</summary>
        public static void Liquidate(EntityManager em, Entity caster, AbilityCard card, float pct)
        {
            var faction = FactionOf(em, caster);
            var building = NearestOwnBuilding(em, AimOf(em, caster), PickRadius + 2f, faction);
            if (building == Entity.Null) return;
            if (em.HasComponent<HallTag>(building) || em.HasComponent<FortressTag>(building)) return;
            var worth = TheWaningBorder.Feraldis.FeraldisRaidIncomeSystem.BuildingWorth(em, building, faction);
            var refund = TheWaningBorder.Feraldis.FeraldisRaidIncomeSystem.Scale(worth, pct / 100f);
            if (!refund.IsZero) FactionEconomy.Add(em, faction, refund, IncomeSource.Other);
            TheWaningBorder.Core.Commands.CommandRouter.DeleteDirect(em, building);
        }
    }
}
