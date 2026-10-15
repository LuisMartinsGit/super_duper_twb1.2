using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Feraldis
{
    /// <summary>
    /// Every number of the Feraldis combat rules (docs/Design/Age_1_Feraldis.md
    /// §6-8): the Berserker's two techs, the Blood ladder, the trainer specials,
    /// the siege rigs and Harkon Fiend's abilities. The asset is
    /// FeraldisCombat.asset beside <see cref="FeraldisCombat"/>. No field
    /// initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Feraldis/FeraldisCombat",
                     fileName = "FeraldisCombat")]
    public sealed class FeraldisCombatConfig : ScriptableObject, IComponentConfig
    {
        // ── Berserker (§7.2) ────────────────────────────────────────────
        /// <summary>HP fraction at or below which Berserk triggers. Seeded 0.25.</summary>
        public float berserkHpFraction;
        /// <summary>Attack multiplier while berserk. Seeded 1.5.</summary>
        public float berserkDamageMultiplier;
        /// <summary>Speed multiplier while berserk. Seeded 1.2.</summary>
        public float berserkSpeedMultiplier;
        /// <summary>Seconds of invulnerability Altered Self-Perception grants at 1 HP. Seeded 10.</summary>
        public float aspInvulnerableSeconds;
        /// <summary>HP fraction a Berserker must be healed above by the end of the window to live.</summary>
        public float aspSurviveHpFraction;

        // ── Blood ladder (§7.6) ─────────────────────────────────────────
        /// <summary>Extra frenzy damage per Bloodrites tier (0.05 = +5 % on top of the base frenzy), index = tier-1.</summary>
        public float[] bloodritesDamageBonus;
        /// <summary>Extra seconds frenzy lingers after leaving blood per tier, index = tier-1.</summary>
        public float[] bloodritesLingerSeconds;

        // ── War Hall specials ───────────────────────────────────────────
        /// <summary>Hamstring: speed reduction a melee hit applies, and for how long.</summary>
        public float hamstringSlow;
        public float hamstringSeconds;
        /// <summary>Warpath Sprint: out-of-combat speed multiplier for Feraldis infantry, after this many seconds out of combat.</summary>
        public float warpathSprintSpeed;
        public float warpathSprintOutOfCombatSeconds;

        // ── Hall of Axes specials ───────────────────────────────────────
        /// <summary>Barbed Heads: multiplier on every Feraldis bleed's duration.</summary>
        public float barbedHeadsBleedMultiplier;
        /// <summary>Pitch Pots: multiplier on the Firethrower's burn duration and radius.</summary>
        public float pitchPotsBurnMultiplier;

        // ── Rig Yard ────────────────────────────────────────────────────
        /// <summary>War Chariot trample: damage to each enemy infantry it drives through, radius, per-target re-hit interval, minimum speed.</summary>
        public int trampleDamage;
        public float trampleRadius;
        public float trampleInterval;
        public float trampleMinSpeed;
        /// <summary>Spiked Wheels: trample damage multiplier.</summary>
        public float spikedWheelsMultiplier;

        // ── Engine Works ────────────────────────────────────────────────
        /// <summary>Blood Boilers: speed multiplier for Feraldis siege on blood.</summary>
        public float bloodBoilersSpeedMultiplier;
        /// <summary>Ram Rig: blast radius and damage (siege damage, hits everything).</summary>
        public float ramBlastRadius;
        public int ramBlastDamage;
        /// <summary>Ram Rig: surface distance to its ordered structure at which it detonates.</summary>
        public float ramTriggerDistance;
        /// <summary>Harpoon Rig wire: seconds it stands, its half-width, damage a second and the bleed it inflicts.</summary>
        public float wireSeconds;
        public float wireHalfWidth;
        public float wireDamagePerSecond;
        public float wireBleedDps;
        public float wireBleedSeconds;
        /// <summary>Barbed Coils: multiplier on the wire's lifetime.</summary>
        public float barbedCoilsMultiplier;
        /// <summary>Corpse Engine: blood each impact leaves (0..1) and the ring radius.</summary>
        public float corpseBlood;
        public float corpseBloodRing;
        /// <summary>War Rig: infantry it carries.</summary>
        public int warRigCapacity;
        /// <summary>Overcharge: damage multiplier, seconds, share of max HP it costs, cooldown.</summary>
        public float overchargeDamageMultiplier;
        public float overchargeSeconds;
        public float overchargeHpCost;
        public float overchargeCooldown;

        // ── Harkon Fiend (§8) ───────────────────────────────────────────
        /// <summary>Raze: share of a capital-class building's MAX HP it deals. Seeded 0.30.</summary>
        public float razeCapitalShare;
        /// <summary>Raze: metres from the aimed point within which the target building is found.</summary>
        public float razePickRadius;
        /// <summary>Leap: slow applied on landing (fraction), radius and seconds.</summary>
        public float leapSlow;
        public float leapSlowRadius;
        public float leapSlowSeconds;
        /// <summary>Leap: furthest the hero may jump.</summary>
        public float leapMaxDistance;
    }
}
