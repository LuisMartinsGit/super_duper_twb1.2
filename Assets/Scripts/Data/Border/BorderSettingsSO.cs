// BorderSettingsSO.cs
// Authoring asset for the Border ARMY logic (per-node defend/attack
// armies). Mirrors the UnitDefSO / TechTreeCatalog pattern: one .asset under
// Assets/Resources/BorderSettings.asset, read at runtime through the static
// BorderSettings loader (live-editable in Play mode, like TechCatalog).
//
// What lives here (tweakable in the Inspector):
//   * the 9 ARMY TIERS — unit composition + train cost + upgrade cost,
//   * per-node ECONOMY — base income, income per green-veilstone node, start bank,
//   * AI cadence + defend/attack tuning.
//
// What does NOT live here: per-unit / per-building STATS (hp, damage, speed,
// …). Those already have SO assets in the TechTreeCatalog (Unit_Crystalling,
// Unit_Veilstinger, Unit_Godsplinter, and the veilstone-node buildings) and stay
// the single source of truth for stats — edit them there.
//
// Generate the asset via  Waning Border ▸ Border ▸ Generate Border Settings.
//
// Location: Assets/Scripts/Data/Border/BorderSettingsSO.cs

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data.Border
{
    [CreateAssetMenu(fileName = "BorderSettings", menuName = "Waning Border/Border Settings", order = 10)]
    public class BorderSettingsSO : ScriptableObject
    {
        /// <summary>One trainable army preset (Tiny … Impossible).</summary>
        [Serializable]
        public class ArmyTier
        {
            public string name = "Tier";
            [Min(0)] public int crystallings;
            [Min(0)] public int veilstingers;
            [Min(0)] public int godsplinters;
            [Tooltip("Veilstone to field this army from empty.")]
            [Min(0)] public int trainCost;
            [Tooltip("Veilstone to switch a slot UP to this tier. Must exceed trainCost.")]
            [Min(0)] public int upgradeCost;

            public int TotalUnits => crystallings + veilstingers + godsplinters;
        }

        /// <summary>
        /// One row of the wave schedule: from <see cref="fromMinute"/> onward,
        /// attack waves field <see cref="tier"/> and wait
        /// <see cref="breatherSeconds"/> between waves.
        /// </summary>
        [Serializable]
        public class WaveEntry
        {
            [Tooltip("Game time (minutes) from which this entry governs the waves.")]
            [Min(0f)] public float fromMinute;
            [Tooltip("Army tier fielded during this window (index into tiers, 0 = smallest).")]
            [Min(0)] public int tier;
            [Tooltip("Seconds a node waits after its attack army dies before fielding the next wave.")]
            [Min(0f)] public float breatherSeconds = 120f;
        }

        [Header("Army tiers (index 0 = smallest)")]
        public List<ArmyTier> tiers = new List<ArmyTier>();

        [Header("Wave schedule")]
        [Tooltip("Author the wave ladder over game time. When off (or the list is empty) "
            + "nodes field the biggest tier they can afford — the old lumpy behaviour.")]
        public bool useWaveSchedule = true;
        [Tooltip("The entry with the largest fromMinute <= elapsed governs tier + breather. "
            + "Cost still gates: a node fields AT MOST the scheduled tier, less if its bank "
            + "can't afford it yet.")]
        public List<WaveEntry> waves = BuildDefaultWaves();
        [Tooltip("Seconds before any node fields its FIRST attack wave — the early-game grace period.")]
        [Min(0f)] public float firstWaveDelaySeconds = 150f;

        [Header("AI cadence (seconds)")]
        [Min(0.5f)] public float decisionInterval = 5f;
        [Min(0.5f)] public float replenishInterval = 4f;

        [Header("Train times (seconds per unit)")]
        [Min(0.1f)] public float crystallingTrainTime = 8f;
        [Min(0.1f)] public float veilstingerTrainTime = 15f;
        [Min(0.1f)] public float godsplinterTrainTime = 30f;

        /// <summary>
        /// Seconds to train one unit of the given type (1=C,2=V,3=G).
        /// The unit's own SO (GameData/TechTree/Units/Border, trainingTime) is
        /// authoritative; the fields on this asset are the fallback when the
        /// SO has no value.
        /// </summary>
        public float TrainTime(byte unitType)
        {
            string id = unitType == 2 ? "Veilstinger" : unitType == 3 ? "Godsplinter" : "Crystalling";
            if (TechCatalog.TryGetUnit(id, out var def) && def.trainingTime > 0f)
                return def.trainingTime;

            return unitType switch
            {
                2 => veilstingerTrainTime,
                3 => godsplinterTrainTime,
                _ => crystallingTrainTime,
            };
        }

        [Header("Per-node economy (each node is its own faction)")]
        [Tooltip("Veilstone/sec a living main node earns from its own territory.")]
        public float baseIncomePerSecond = 6f;
        [Tooltip("Extra veilstone/sec per green-veilstone (Resource) sub-node the node owns.")]
        public float incomePerResourceNode = 4f;
        [Tooltip("Veilstone a brand-new node starts its private bank with.")]
        [Min(0)] public int startingCrystal = 250;

        [Header("Defend slot")]
        [Tooltip("Defenders hold within this distance of their node.")]
        public float defendHoldRadius = 18f;
        [Tooltip("Free replenishment needs at least one green-veilstone (Resource) sub-node.")]
        public bool replenishNeedsResourceNode = true;

        [Header("Attack slot")]
        [Tooltip("Wait until the attack army is fully mustered before it marches.")]
        public bool requireFullMusterBeforeAttack = true;
        [Tooltip("How close (m) a recalled attack army must get to its node to disband.")]
        public float recallArriveRadius = 16f;

        [Header("Escalation + targeting (AI plan M5)")]
        [Tooltip("Per-node income multiplier per escalation phase: income *= 1 + phase * this.")]
        public float phaseIncomeBonus = 0.5f;
        [Tooltip("Training speed bonus per phase: trainTime /= 1 + phase * this.")]
        public float phaseTrainSpeedBonus = 0.25f;
        [Tooltip("Minute the continuous escalation ramp starts (phase stays 0 before this).")]
        [Min(0f)] public float escalationStartMinute = 5f;
        [Tooltip("Minute the ramp reaches maxEscalation. Between start and full the phase "
            + "interpolates linearly — no more discrete 0 -> 1 -> 2 power jumps.")]
        [Min(0f)] public float escalationFullMinute = 20f;
        [Tooltip("Phase value at/after escalationFullMinute (2 matches the old discrete cap).")]
        [Min(0f)] public float maxEscalation = 2f;
        [Tooltip("Fallback breather when the wave schedule is off/empty: seconds a node waits after its attack army dies before fielding the next one (muster/training time comes on top).")]
        [Min(0f)] public float waveBreatherSeconds = 120f;

        // ── lookups ─────────────────────────────────────────────────────────
        public int TierCount => tiers != null ? tiers.Count : 0;

        public ArmyTier Tier(int i)
            => (tiers != null && i >= 0 && i < tiers.Count) ? tiers[i] : null;

        /// <summary>
        /// Continuous escalation phase at <paramref name="elapsedSeconds"/>:
        /// 0 until escalationStartMinute, then a linear ramp reaching
        /// <see cref="maxEscalation"/> at escalationFullMinute. Replaces the
        /// old discrete 0/1/2 steps at 5/15 min.
        /// </summary>
        public float EscalationPhase(double elapsedSeconds)
        {
            float start = escalationStartMinute * 60f;
            float full = escalationFullMinute * 60f;
            float cap = Mathf.Max(0f, maxEscalation);
            if (elapsedSeconds <= start) return 0f;
            if (full <= start) return cap;
            float f = Mathf.Clamp01((float)((elapsedSeconds - start) / (full - start)));
            return f * cap;
        }

        /// <summary>
        /// Resolve the wave-schedule row governing <paramref name="elapsedSeconds"/>:
        /// the entry with the largest fromMinute that has already passed (before the
        /// first row, the earliest row governs). False when the schedule is disabled
        /// or empty — callers fall back to biggest-affordable + waveBreatherSeconds.
        /// </summary>
        public bool TryGetWave(double elapsedSeconds, out int tier, out float breatherSeconds)
        {
            tier = 0;
            breatherSeconds = Mathf.Max(0f, waveBreatherSeconds);
            if (!useWaveSchedule || waves == null || waves.Count == 0 || TierCount == 0)
                return false;

            WaveEntry current = null;
            WaveEntry earliest = null;
            float bestFrom = float.MinValue;
            float earliestFrom = float.MaxValue;
            for (int i = 0; i < waves.Count; i++)
            {
                var w = waves[i];
                if (w == null) continue;
                float from = w.fromMinute * 60f;
                if (from <= elapsedSeconds && from >= bestFrom) { bestFrom = from; current = w; }
                if (from < earliestFrom) { earliestFrom = from; earliest = w; }
            }
            var row = current ?? earliest;
            if (row == null) return false;

            tier = Mathf.Clamp(row.tier, 0, TierCount - 1);
            breatherSeconds = Mathf.Max(0f, row.breatherSeconds);
            return true;
        }

        /// <summary>
        /// Reset every field to the shipped defaults (the 9 tiers from the design
        /// table, costs = sum of unit costs, upgrade = 1.5× train). Used by the
        /// generator and as the runtime fallback when no asset is present.
        /// </summary>
        public void ResetToDefaults()
        {
            decisionInterval = 5f;
            replenishInterval = 4f;
            crystallingTrainTime = 8f;
            veilstingerTrainTime = 15f;
            godsplinterTrainTime = 30f;
            baseIncomePerSecond = 6f;
            incomePerResourceNode = 4f;
            startingCrystal = 250;
            defendHoldRadius = 18f;
            replenishNeedsResourceNode = true;
            requireFullMusterBeforeAttack = true;
            recallArriveRadius = 16f;
            phaseIncomeBonus = 0.5f;
            phaseTrainSpeedBonus = 0.25f;
            escalationStartMinute = 5f;
            escalationFullMinute = 20f;
            maxEscalation = 2f;
            waveBreatherSeconds = 120f;
            useWaveSchedule = true;
            firstWaveDelaySeconds = 150f;
            tiers = BuildDefaultTiers();
            waves = BuildDefaultWaves();
        }

        /// <summary>
        /// The shipped wave ladder — one tier step roughly every 4-6 minutes with
        /// breathers growing alongside army size, so pressure rises smoothly
        /// instead of jumping to whatever the node's bank happens to afford.
        /// </summary>
        public static List<WaveEntry> BuildDefaultWaves()
        {
            return new List<WaveEntry>
            {
                new WaveEntry { fromMinute = 0f,  tier = 0, breatherSeconds = 120f }, // Tiny
                new WaveEntry { fromMinute = 4f,  tier = 1, breatherSeconds = 120f }, // Small
                new WaveEntry { fromMinute = 8f,  tier = 2, breatherSeconds = 130f }, // Medium-small
                new WaveEntry { fromMinute = 12f, tier = 3, breatherSeconds = 140f }, // Medium
                new WaveEntry { fromMinute = 16f, tier = 4, breatherSeconds = 150f }, // Medium-large
                new WaveEntry { fromMinute = 21f, tier = 5, breatherSeconds = 160f }, // Large
                new WaveEntry { fromMinute = 26f, tier = 6, breatherSeconds = 170f }, // Very large
                new WaveEntry { fromMinute = 32f, tier = 7, breatherSeconds = 180f }, // Colossal
                new WaveEntry { fromMinute = 40f, tier = 8, breatherSeconds = 180f }, // Impossible
            };
        }

        // Per-unit veilstone costs used to derive each tier's train cost. Mirrors
        // BorderConstants.AI*Cost; kept here so the SO is self-contained.
        public const int CrystallingCost = 50;
        public const int VeilstingerCost = 150;
        public const int GodsplinterCost = 500;

        /// <summary>The shipped 9-tier table (Tiny … Impossible).</summary>
        public static List<ArmyTier> BuildDefaultTiers()
        {
            var rows = new (string name, int c, int v, int g)[]
            {
                ("Tiny",          5,  0,  0),
                ("Small",         5,  2,  0),
                ("Medium-small",  8,  5,  0),
                ("Medium",       12,  8,  1),
                ("Medium-large", 25, 15,  5),
                ("Large",        35, 25,  7),
                ("Very large",   50, 35, 10),
                ("Colossal",     70, 55, 15),
                ("Impossible",  100, 70, 20),
            };

            var list = new List<ArmyTier>(rows.Length);
            foreach (var r in rows)
            {
                int train = r.c * CrystallingCost + r.v * VeilstingerCost + r.g * GodsplinterCost;
                list.Add(new ArmyTier
                {
                    name = r.name,
                    crystallings = r.c,
                    veilstingers = r.v,
                    godsplinters = r.g,
                    trainCost = train,
                    // Strictly pricier than just fielding it (design rule).
                    upgradeCost = Mathf.CeilToInt(train * 1.5f),
                });
            }
            return list;
        }
    }
}
