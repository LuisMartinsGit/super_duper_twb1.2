// FeraldisCombat.cs
// The Feraldis combat rules every shared combat path asks about
// (docs/Design/Age_1_Feraldis.md §6-7, 2026-10-12): frenzy strength by the
// Blood ladder, the Berserker's Berserk, the trainer specials (Hamstring,
// No Retreat, Warpath Sprint, Barbed Heads, Pitch Pots, Blood Boilers) and
// Overcharge. Every query returns the neutral value for non-Feraldis units,
// so the rest of the game pays a component check or two.

using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>A Berserker at or below the Berserk HP threshold (with the tech).</summary>
public struct BerserkBuff : IComponentData { }

/// <summary>Altered Self-Perception's window: invulnerable, then dies unless healed.</summary>
public struct AlteredSelfState : IComponentData { public float Remaining; }

/// <summary>Altered Self-Perception already used this life.</summary>
public struct AlteredSelfSpent : IComponentData { }

/// <summary>A War Rig's Overcharge window and cooldown.</summary>
public struct OverchargeState : IComponentData { public float Remaining; public float Cooldown; }

/// <summary>Fires while moving (Corpse Engine / Harpoon Rig with Fire on the Move; the War Rig always).</summary>
public struct FiresOnMove : IComponentData { }

/// <summary>Seconds a Feraldis infantry unit has gone without fighting (Warpath Sprint).</summary>
public struct SprintClock : IComponentData { public float OutOfCombat; }

namespace TheWaningBorder.Feraldis
{
    public static class FeraldisCombat
    {
        private static FeraldisCombatConfig _cfg;
        public static FeraldisCombatConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<FeraldisCombatConfig>());

        public static bool Researched(EntityManager em, Entity e, string tech)
        {
            var r = FactionResearchState.Instance;
            return r != null && em.HasComponent<FactionTag>(e)
                   && r.HasResearched(em.GetComponentData<FactionTag>(e).Value, tech);
        }

        public static bool Researched(Faction f, string tech)
        {
            var r = FactionResearchState.Instance;
            return r != null && r.HasResearched(f, tech);
        }

        // ── the Blood ladder ──────────────────────────────────────────────

        /// <summary>The Bloodrites line a unit's frenzy reads: infantry (War Hall), ranged (Hall of Axes) or cavalry (Rig Yard).</summary>
        public static string BloodritesLine(EntityManager em, Entity e)
        {
            if (em.HasComponent<UnitTagsData>(e))
            {
                var tags = em.GetComponentData<UnitTagsData>(e).Mask;
                if ((tags & UnitTagParse.Tag("cavalry")) != 0) return "Feraldis_BloodritesCavalry";
                if ((tags & UnitTagParse.Tag("ranged")) != 0) return "Feraldis_BloodritesRanged";
            }
            return "Feraldis_BloodritesInfantry";
        }

        /// <summary>Highest Bloodrites tier (0-3) this unit's line has.</summary>
        public static int BloodritesTier(EntityManager em, Entity e)
        {
            string line = BloodritesLine(em, e);
            for (int t = 3; t >= 1; t--)
                if (Researched(em, e, line + t)) return t;
            return 0;
        }

        /// <summary>Attack multiplier from Feraldis sources: frenzy (ladder-scaled), Berserk, Overcharge.</summary>
        public static float DamageMult(EntityManager em, Entity attacker, float baseFrenzyMult)
        {
            float m = 1f;
            if (em.HasComponent<BloodFrenzy>(attacker))
            {
                float bonus = 0f;
                int tier = BloodritesTier(em, attacker);
                var b = Cfg.bloodritesDamageBonus;
                if (tier > 0 && b != null && b.Length > 0) bonus = b[math.min(tier, b.Length) - 1];
                m *= baseFrenzyMult + bonus;
            }
            if (em.HasComponent<BerserkBuff>(attacker)) m *= Cfg.berserkDamageMultiplier;
            if (em.HasComponent<OverchargeState>(attacker)
                && em.GetComponentData<OverchargeState>(attacker).Remaining > 0f)
                m *= Cfg.overchargeDamageMultiplier;
            return m;
        }

        /// <summary>Extra seconds frenzy lingers for this unit's Bloodrites tier.</summary>
        public static float FrenzyLingerBonus(EntityManager em, Entity e)
        {
            int tier = BloodritesTier(em, e);
            var l = Cfg.bloodritesLingerSeconds;
            return tier > 0 && l != null && l.Length > 0 ? l[math.min(tier, l.Length) - 1] : 0f;
        }

        // ── speed ─────────────────────────────────────────────────────────

        /// <summary>No Retreat: a frenzied Feraldis unit ignores slows.</summary>
        public static bool IgnoresSlows(EntityManager em, Entity e)
            => em.HasComponent<BloodFrenzy>(e) && em.HasComponent<FeraldisUnitTag>(e)
               && Researched(em, e, "Feraldis_NoRetreat");

        /// <summary>Speed multiplier from Feraldis sources: Berserk, Warpath Sprint, Blood Boilers.</summary>
        public static float SpeedMult(EntityManager em, Entity e)
        {
            if (!em.HasComponent<FeraldisUnitTag>(e)) return 1f;
            float m = 1f;
            var c = Cfg;
            if (em.HasComponent<BerserkBuff>(e)) m *= c.berserkSpeedMultiplier;
            if (em.HasComponent<SprintClock>(e)
                && em.GetComponentData<SprintClock>(e).OutOfCombat >= c.warpathSprintOutOfCombatSeconds
                && Researched(em, e, "Feraldis_WarpathSprint"))
                m *= c.warpathSprintSpeed;
            if (em.HasComponent<BloodFrenzy>(e) && em.HasComponent<SiegeTag>(e)
                && Researched(em, e, "Feraldis_BloodBoilers"))
                m *= c.bloodBoilersSpeedMultiplier;
            return m;
        }

        // ── on-hit riders ────────────────────────────────────────────────

        /// <summary>Hamstring: a Feraldis melee hit slows its (unit) target.</summary>
        public static void OnMeleeHit(EntityManager em, EntityCommandBuffer ecb, Entity attacker, Entity target)
        {
            if (!em.HasComponent<FeraldisUnitTag>(attacker)) return;
            if (target == Entity.Null || !em.Exists(target) || !em.HasComponent<UnitTag>(target)) return;
            if (!Researched(em, attacker, "Feraldis_Hamstring")) return;
            var c = Cfg;
            var slow = new SpellDebuff { SpeedReduction = c.hamstringSlow, TimeRemaining = c.hamstringSeconds };
            if (em.HasComponent<SpellDebuff>(target))
            {
                var cur = em.GetComponentData<SpellDebuff>(target);
                cur.SpeedReduction = math.max(cur.SpeedReduction, slow.SpeedReduction);
                cur.TimeRemaining = math.max(cur.TimeRemaining, slow.TimeRemaining);
                ecb.SetComponent(target, cur);
                ecb.SetComponentEnabled<SpellDebuff>(target, true);
            }
            else ecb.AddComponent(target, slow);
        }

        /// <summary>Barbed Heads: every bleed a Feraldis faction inflicts lasts longer.</summary>
        public static float BleedDurationMult(Faction source)
            => Researched(source, "Feraldis_BarbedHeads") ? Cfg.barbedHeadsBleedMultiplier : 1f;

        /// <summary>Pitch Pots: Feraldis fire burns longer and wider.</summary>
        public static float BurnMult(Faction source)
            => Researched(source, "Feraldis_PitchPots") ? Cfg.pitchPotsBurnMultiplier : 1f;
    }
}
