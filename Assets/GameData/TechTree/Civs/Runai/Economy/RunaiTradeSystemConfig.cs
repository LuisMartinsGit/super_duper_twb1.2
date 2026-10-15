using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Runai
{
    /// <summary>
    /// Every number of the Runai stock-and-haul economy
    /// (docs/Design/Age_1_Runai.md §3-4): caravans, trader-warriors, markets,
    /// the distance premium and the Waystations. The asset is
    /// RunaiTradeSystem.asset beside <see cref="RunaiTradeSystem"/>. No field
    /// initialisers: the values live in the asset. All placeholders.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Runai/RunaiTradeSystem",
                     fileName = "RunaiTradeSystem")]
    public sealed class RunaiTradeSystemConfig : ScriptableObject, IComponentConfig
    {
        // ── Cadence ─────────────────────────────────────────────────────
        /// <summary>Seconds between caravan / warrior updates.</summary>
        public float tickSeconds;

        // ── Caravans (§3.2) ─────────────────────────────────────────────
        /// <summary>Resource units one caravan carries.</summary>
        public float caravanCapacity;
        /// <summary>Pack Animals: extra capacity, percent.</summary>
        public float packAnimalsCapacityPct;
        /// <summary>Seconds a caravan waits at its depot for a part load before leaving with it.</summary>
        public float loadSeconds;
        /// <summary>A depot holds at most this many caravan loads; income past it is lost.</summary>
        public float stockCapLoads;
        /// <summary>Seconds before a dead caravan is replaced.</summary>
        public float caravanRespawnSeconds;
        /// <summary>The most caravans (and trader-warriors) one depot keeps.</summary>
        public int maxCaravansPerDepot;
        /// <summary>Swift Wheels: caravan speed, percent.</summary>
        public float swiftWheelsSpeedPct;
        /// <summary>How close (beyond the building's radius) counts as arrived.</summary>
        public float arriveSlack;

        // ── The sale (§3.2-3.3) ─────────────────────────────────────────
        /// <summary>Distance premium per metre of depot-to-market distance (0.004 = +0.4 % / m).</summary>
        public float premiumPerMetre;
        /// <summary>The premium never exceeds this (1.0 = +100 %).</summary>
        public float premiumCap;
        /// <summary>Long-Haul Tariffs: premium per metre and cap with the tech.</summary>
        public float longHaulPremiumPerMetre;
        public float longHaulPremiumCap;
        /// <summary>Thessara's Crossing pays this multiple of the sale.</summary>
        public float crossingMarketMultiplier;
        /// <summary>A Waystation (after Market Rights) pays this multiple of the sale.</summary>
        public float waystationMarketMultiplier;

        // ── Waystations (§4.3) ──────────────────────────────────────────
        /// <summary>Caravans within this distance of a Waystation are sped up.</summary>
        public float waystationRadius;
        /// <summary>The Waystation speed bonus, percent (Road March: every Runai unit).</summary>
        public float waystationSpeedPct;
        /// <summary>A new Waystation must stand within this distance of a depot-to-market line.</summary>
        public float routeCorridorWidth;

        // ── Trader-warriors (§3.4) ──────────────────────────────────────
        /// <summary>Seconds before a dead trader-warrior is replaced.</summary>
        public float warriorRespawnSeconds;
        /// <summary>How far beside its caravan a trader-warrior rides.</summary>
        public float escortOffset;
        /// <summary>A trader-warrior drops a fight further than this from its caravan.</summary>
        public float escortLeash;

        // ── The Caravan Master (§6.1) ───────────────────────────────────
        public float caravanMasterRadius;
        /// <summary>Caravans near him: speed and capacity, percent.</summary>
        public float caravanMasterSpeedPct;
        public float caravanMasterCapacityPct;

        // ── Caravan of Swords (§8) ──────────────────────────────────────
        /// <summary>Attack an armed caravan deals.</summary>
        public float caravanOfSwordsDamage;
        /// <summary>Incoming-damage multiplier while armed (0.5 = half).</summary>
        public float caravanOfSwordsDamageTaken;

        // ── The road tax (§3.2) ─────────────────────────────────────────
        /// <summary>Veilstone a caravan pays when it passes one of its Waystations.</summary>
        public float taxVeilstone;
        /// <summary>Seconds a wagon must travel before it can pay another tax (the spacing rule).</summary>
        public float taxCooldownSeconds;

        // ── Trade paths (§7.2) ──────────────────────────────────────────
        /// <summary>Swift Wheels II: further caravan speed, percent.</summary>
        public float swiftWheels2SpeedPct;
        /// <summary>Wagon Archers: a caravan's retaliating shot.</summary>
        public float wagonArcherDamage;
        public float wagonArcherRange;
        /// <summary>Hunker Down: below this HP fraction the caravan stops for hunkerSeconds with +hunkerArmorPct armour.</summary>
        public float hunkerHpFraction;
        public float hunkerSeconds;
        public float hunkerArmorPct;

        // ── The Call (§8.1) ─────────────────────────────────────────────
        /// <summary>Seconds the called escorts stay a controllable army.</summary>
        public float callSeconds;
    }
}
