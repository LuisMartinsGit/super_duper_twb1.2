// BuildingLevelDefSO.cs
// One LEVEL of a cultured building, as its own asset (2026-10-02).
// Part of: Data/TechTree/Definitions/
//
// A cultured building is the SAME entity as its Age 0 form, renamed and
// levelled (CLAUDE.md, TechTree bullet). The Age 0 form is the building's
// BuildingDefSO in Age0/; each level its owner's culture gives it is one of
// these, filed in that culture's folder under the CULTURED name:
//
//   Age0/Buildings/Hut/Hut.asset                         the Hut (level 0)
//   Civs/Alanthor/Buildings/House/House_Lvl1..3.asset     House - Lvl 1..3
//
// Every number the level changes lives here — name, the price and time of
// reaching it, the stat multipliers over the Age 0 base, population, the
// building's own attack and its look. BuildingUpgradeConfig reads these first
// and keeps its code tables only for the cultures not yet migrated (Runai,
// Feraldis); a missing Alanthor level is a data bug, logged loudly.
// docs/Design/Age_1_Alanthor.md § Building levels

using UnityEngine;

namespace TheWaningBorder.Data
{
    [CreateAssetMenu(fileName = "Building_Lvl", menuName = "Waning Border/Building Level Def", order = 2)]
    public class BuildingLevelDefSO : ScriptableObject
    {
        // ── Which building, which culture, which level ──
        /// <summary>The ladder's building id — the Age 0 id the entity keeps
        /// ("Hut", "Fortress", "Barracks"), not the cultured name.</summary>
        public string buildingId;
        /// <summary>"Alanthor" / "Runai" / "Feraldis".</summary>
        public string culture;
        /// <summary>1..3.</summary>
        public int level;

        /// <summary>The name the player reads; the HUD adds " - Lvl N".</summary>
        public string displayName;

        // ── Reaching it ──
        /// <summary>Price of the upgrade INTO this level (level 1 is granted
        /// free at age-up).</summary>
        public CostBlock upgradeCost;
        /// <summary>Seconds the upgrade takes.</summary>
        public float upgradeSeconds;

        // ── Stats, over the Age 0 base (absolute per level, never cumulative) ──
        public float hpMultiplier;
        public float trainTimeMultiplier;
        /// <summary>Scales the base attack cooldown (lower = faster).</summary>
        public float attackCooldownMultiplier;
        /// <summary>Targets per volley; 0 = leave the building's own.</summary>
        public int maxTargets;
        /// <summary>Population this level provides; 0 = not a provider.</summary>
        public int populationProvided;
        /// <summary>What a resource slot pays per minute with this level of
        /// the building on it — replaces the base building's rung for this
        /// culture (Alanthor's Guild 70/100/200, its Mines 140/200/400). Read
        /// only for the extractors; 0 on every other ladder.</summary>
        public float slotIncomePerMinute;
        /// <summary>Scales the building's interestPerMinute at this level (the
        /// Vault of Almierra). Read only for the Vault; 0 elsewhere.</summary>
        public float interestMultiplier;
        /// <summary>Scales the Trading Outpost's trade at this level — what it
        /// spends AND what it earns per minute, over the
        /// TradingOutpostSystem.asset rates. Read only for the Outpost; 0
        /// elsewhere (0 reads as 1).</summary>
        public float tradeRateMultiplier;

        /// <summary>An attack AUTHORED for this level, replacing the scaled one
        /// (the Watch Tower ladder; the Garrison's level-3 arrows).
        /// enabled = false: the multipliers above are the whole story.</summary>
        public BuildingAttack attack;
        /// <summary>Line of sight at this level; 0 = unchanged.</summary>
        public float lineOfSight;

        // ── Look ──
        /// <summary>The model this level shows; null = keep the current one.</summary>
        public GameObject prefab;
    }
}
