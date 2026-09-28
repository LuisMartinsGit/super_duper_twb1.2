// SpellLadder.cs
// The balance ladder every spell is measured against -- docs/Design/Spells.md
// section 8. The sect power tables (SectLeverEffects.*.cs) build their
// cooldowns and damage from these bands instead of inventing a number per
// power, so two powers with the same reach, level and role cannot drift apart.
//
// The ability cards are SOs and carry literal numbers; Spells.md section 9 is
// where they are checked against the same bands.
//
// Reference frame: a line unit has about 130 HP and deals about 10 per hit,
// so 10 damage is one line-unit hit and 130 is one line unit.

namespace TheWaningBorder.Economy
{
    public static class SpellLadder
    {
        // ── Cooldown bands (real seconds; the authored number is the one charged)

        /// <summary>Hero active (Liquid Courage).</summary>
        public const float HeroActiveCooldown = 45f;
        /// <summary>Unit tactical active (War Horn, Full Gallop, Volleys, Celestar, Ranging Shot).</summary>
        public const float UnitTacticalCooldown = 60f;
        /// <summary>Sect buff / debuff / heal / ward / utility.</summary>
        public const float SectTacticalCooldown = 60f;
        /// <summary>Sect economy (Harvest the Veil, Call to Arms, Cleanse).</summary>
        public const float SectEconomyCooldown = 60f;
        /// <summary>Sect power that deals damage.</summary>
        public const float SectDamageCooldown = 75f;
        /// <summary>Building active (the Reliquary's three).</summary>
        public const float BuildingActiveCooldown = 90f;
        /// <summary>Hero ultimate (Honour thy Pledge, Shardbound Fury).</summary>
        public const float HeroUltimateCooldown = 120f;
        /// <summary>Sect wildcard (Sew Disorder, Immovable III, Raise Anew I).</summary>
        public const float SectWildcardCooldown = 120f;
        /// <summary>Map-wide power (Blood Rain, Nowhere to Hide).</summary>
        public const float MapWideCooldown = 150f;
        /// <summary>Deployable structure (Deploy Field Hospital).</summary>
        public const float DeployableCooldown = 300f;

        // ── Damage bands: per victim at level I, by reach ─────────────────

        public const float SingleDamage  = 120f;
        public const float SmallDamage   = 60f;
        public const float MediumDamage  = 40f;
        public const float LargeDamage   = 30f;
        public const float MapWideDamage = 20f;

        /// <summary>Per-victim damage band at level I for a reach.</summary>
        public static float DamageBand(SectRadius reach) => reach switch
        {
            SectRadius.Small  => SmallDamage,
            SectRadius.Medium => MediumDamage,
            SectRadius.Large  => LargeDamage,
            _                 => SingleDamage,
        };

        /// <summary>Per-victim damage at a reach and level: band x 1 / 1.5 / 2.</summary>
        public static float Damage(SectRadius reach, int level)
            => DamageBand(reach) * SectLeverEffects.LevelScalar((byte)level);

        /// <summary>Per-victim damage of a map-wide power at a level.</summary>
        public static float MapWide(int level)
            => MapWideDamage * SectLeverEffects.LevelScalar((byte)level);

        // ── Derived rules ─────────────────────────────────────────────────

        /// <summary>Heal band as a fraction of max HP at level I: the damage
        /// band over a 130 HP line unit, rounded to 5 %.</summary>
        public static float HealBand(SectRadius reach) => reach switch
        {
            SectRadius.Small  => 0.45f,
            SectRadius.Medium => 0.30f,
            SectRadius.Large  => 0.25f,
            _                 => 0.90f,
        };

        /// <summary>Heal fraction at a reach and level.</summary>
        public static float Heal(SectRadius reach, int level)
            => HealBand(reach) * SectLeverEffects.LevelScalar((byte)level);

        /// <summary>A damage zone deals its band over this many seconds.</summary>
        public const float ZoneSeconds = 10f;

        /// <summary>DPS of a damage zone (burning ground) at a reach and level.</summary>
        public static float ZoneDps(SectRadius reach, int level)
            => Damage(reach, level) / ZoneSeconds;

        /// <summary>Conditional damage (lands only on units meeting a condition)
        /// may reach this multiple of the band.</summary>
        public const float ConditionalCapMultiplier = 2f;

        /// <summary>The per-victim cap of a conditional power at a reach and level.</summary>
        public static float ConditionalCap(SectRadius reach, int level)
            => Damage(reach, level) * ConditionalCapMultiplier;

        // ── Uptime caps (duration / cooldown) ─────────────────────────────

        /// <summary>At least 50 % damage reduction, invulnerable, cannot die,
        /// +100 % building HP.</summary>
        public const float StrongDefenseUptimeCap = 0.25f;
        /// <summary>Every other combat buff or debuff.</summary>
        public const float BuffUptimeCap = 0.35f;

        // ── Sect power wind-ups (telegraph seconds) ───────────────────────

        /// <summary>A power that deals damage.</summary>
        public const float DamageWindupSeconds = 3.0f;
        /// <summary>A hostile power that deals no damage.</summary>
        public const float HostileWindupSeconds = 1.5f;
        /// <summary>A friendly power.</summary>
        public const float FriendlyWindupSeconds = 1.0f;
    }
}
