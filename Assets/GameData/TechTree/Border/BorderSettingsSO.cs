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
            [Min(0)] public int trainCost;
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
            [Min(0f)] public float fromMinute;
            [Min(0)] public int tier;
            [Min(0f)] public float breatherSeconds = 120f;
        }

        public List<ArmyTier> tiers = new List<ArmyTier>();

        public bool useWaveSchedule = true;
        public List<WaveEntry> waves = BuildDefaultWaves();
        [Min(0f)] public float firstWaveDelaySeconds = 150f;

        /// <summary>
        /// Seconds a faction must reach no further into the curse before its
        /// wrath falls one tier (design §2.10). This is the "back off" valve:
        /// stop taking wells and the waves thin out.
        ///
        /// Zero disables cooling entirely, which makes provocation a one-way
        /// ratchet — the first mistake becomes permanent. Only set it to zero
        /// deliberately.
        /// </summary>
        [Min(0f)] public float wrathCoolSeconds = 180f;

        // ── The Living Curse (Curse_And_Shardroot.md §2.11, 2026-09-13) ──
        // Supersedes the Waking (§2.8) and the Wrath army rules (§2.10 1-3).
        // When `livingCurse` is on: wells pump from tick 0, every curse
        // territory keeps a garrison, intruders are chased, and the curse
        // expands on a timer by MERGING units into a neighbour's veilstone
        // or veilsteel node. Off = the previous provocation-driven model.

        /// <summary>Master switch for §2.11. Off restores §2.8/§2.10 exactly.</summary>
        public bool livingCurse = true;

        /// <summary>BASE size of a territory's garrison army (2.13 rule 3):
        /// the n-th spawn brings the garrison up to garrisonCap x
        /// armyGrowth^n. Survivors count toward the size.</summary>
        [Min(0)] public int garrisonCap = 8;

        /// <summary>Seconds between whole-army spawns in one territory
        /// (2.13 rule 1). Between spawns nothing regrows: defeat the army
        /// and the territory is vulnerable until this timer fires.</summary>
        [Min(10f)] public float armySpawnSeconds = 120f;

        /// <summary>Compounding growth of the garrison army per spawn
        /// (2.13 rule 3): 1.12 doubles the army roughly every six spawns.</summary>
        [Min(1f)] public float armyGrowth = 1.12f;

        /// <summary>Seconds between harassment-army dispatches (2.13 rule 4;
        /// was "expansion attempts" in 2.11 rule 4 -- same timer).</summary>
        [Min(10f)] public float expansionSeconds = 150f;

        /// <summary>Chance, per spawn (garrison or harassment party), that
        /// one unit of that spawn carries the Shardroot (2.13 rule 5).
        /// Rolled only while the artifact is neither out nor claimed.</summary>
        [Range(0f, 1f)] public float shardrootChance = 0.04f;

        /// <summary>THE MAW BACKSTOP (Curse_And_Shardroot.md §3): sim seconds
        /// the Shardroot's host well must spend Wild (unverbed) before it
        /// reaches "Maw maturity" and the artifact shows itself embedded in
        /// it -- a visible gem in the well, a minimap beacon and a ping for
        /// every player. It is still claimed only by verbing that well. The
        /// Well->Fissure->Maw ladder is superseded by the Veil (§2.3), so
        /// maturity is time-alive-and-feeding. 1500 s = 25 min, the middle of
        /// §2.3's "a neglected map is overrun in ~20-30 minutes". 0 = off.</summary>
        [Min(0f)] public float shardrootMawSeconds = 720f;

        /// <summary>Seconds of uninterrupted merging for a node to turn.
        /// Progress PAUSES while the merge party is out defending the node
        /// and RESETS if the whole party dies.</summary>
        [Min(5f)] public float mergeSeconds = 90f;

        /// <summary>Units sent to merge with a node. Drawn from the tier
        /// the match time has reached, so late merges are better guarded.</summary>
        [Min(1)] public int mergePartySize = 6;

        /// <summary>A hostile inside this radius of a merging node summons
        /// the party out to defend it (§2.11 rule 4).</summary>
        [Min(1f)] public float mergeDefendRadius = 30f;

        /// <summary>Seconds a defender may spend outside its home territory
        /// with nothing to fight before it walks home (§2.11 rule 3).</summary>
        [Min(1f)] public float leashSeconds = 25f;

        /// <summary>A garrison unit engages only hostiles within this many
        /// metres of the curse node it guards (Territory_Claims.md §6.7).</summary>
        [Min(1f)] public float guardRadius = 30f;

        /// <summary>A garrison unit fighting farther than this from its node
        /// drops the fight and walks back (Territory_Claims.md §6.7).</summary>
        [Min(1f)] public float guardLeashRadius = 45f;

        // ── Territory_Claims.md §6.8 (2026-10-04): attack waves + the cap ──

        /// <summary>Match second at which the first attack wave forms.</summary>
        [Min(0f)] public float firstWaveSeconds = 480f;

        /// <summary>Seconds between attack waves (§6.8). The timer runs on
        /// whether or not a wave could be sent.</summary>
        [Min(10f)] public float waveIntervalSeconds = 240f;

        /// <summary>Fraction of the curse's garrison units DRAFTED into each
        /// attack wave (§6.8, 2026-10-04: "all spawn as garrison, then waves
        /// are 30% of that"). Nothing is spawned for a wave.</summary>
        [Range(0f, 1f)] public float waveDraftFraction = 0.30f;

        /// <summary>A wave slot that can draft fewer units than this is
        /// skipped (the timer runs on).</summary>
        [Min(1)] public int waveMinSize = 6;

        /// <summary>No draft (claim / fill / hunt party or attack wave) takes
        /// a node's garrison below this many units (§6.8): a node is never
        /// stripped bare.</summary>
        [Min(0)] public int garrisonMinPerNode = 2;

        /// <summary>Seconds a wave fights at its target before it walks
        /// home and rejoins the garrison of the nearest curse node.</summary>
        [Min(10f)] public float waveDurationSeconds = 150f;

        /// <summary>A wave reduced below this fraction of the size it set
        /// out with breaks off and walks home.</summary>
        [Range(0f, 1f)] public float waveRetreatFraction = 0.25f;

        // ── §6.8 (2026-10-05): fair targeting — the strong carry the curse ──

        /// <summary>A player's wave STRENGTH blends its army power share with
        /// its territory share: 0 = army only, 1 = territories only (§6.8).</summary>
        [Range(0f, 1f)] public float waveShareTerritoryWeight = 0.5f;

        /// <summary>Floor on any living player's wave share before the shares
        /// are renormalised, so the weakest is never forgotten (§6.8).</summary>
        [Range(0f, 1f)] public float waveShareFloor = 0.1f;

        /// <summary>A player is not targeted while a wave is out against them,
        /// nor for this many seconds after it turned home (§6.8). Every living
        /// player cooling down: the slot is skipped.</summary>
        [Min(0f)] public float wavePlayerCooldownSeconds = 300f;

        /// <summary>A wave is drafted only until its own combat power reaches
        /// this multiple of the target's army power (x the target's
        /// difficulty multiplier), never below waveMinSize units and never
        /// above waveDraftFraction of the garrisons (§6.8).</summary>
        [Min(0f)] public float waveSizeVsPower = 0.6f;

        /// <summary>Wave-size multiplier against a player by that player's AI
        /// difficulty, indexed Easy, Normal, Hard, Expert (LobbyAIDifficulty).
        /// Human players count as Normal (§6.8).</summary>
        public float[] waveSizeByDifficulty = { 0.5f, 1f, 1f, 1.25f };

        /// <summary>Before this match second a claim party never targets a
        /// territory a player holds — the opening claims only unclaimed
        /// ground (§6.5, 2026-10-05). The Shardroot hunt ignores it.</summary>
        [Min(0f)] public float claimGraceSeconds = 480f;

        /// <summary>The wave-size multiplier for a player of the given lobby
        /// difficulty index (0 Easy .. 3 Expert); 1 when the table is short.</summary>
        public float WaveSizeForDifficulty(int difficulty)
            => waveSizeByDifficulty != null && difficulty >= 0 && difficulty < waveSizeByDifficulty.Length
                ? Mathf.Max(0f, waveSizeByDifficulty[difficulty]) : 1f;

        /// <summary>CEILING on live curse units (every BorderUnitTag unit,
        /// whatever raised it). The cap in force is curseUnitsBase +
        /// curseUnitsPerTerritory x the territories the curse holds, never
        /// above this (§6.8, 2026-10-07, CurseUnitCap). Every spawn path
        /// raises at most the headroom left under the cap; in the living
        /// curse the only spawner is the garrison, so it bounds the garrisons.</summary>
        [Min(0)] public int maxCurseUnits = 1000;

        /// <summary>Curse units allowed whatever ground it holds — the part
        /// of the cap that does not scale with territories (§6.8).</summary>
        [Min(0)] public int curseUnitsBase = 40;

        /// <summary>Curse units the cap grows by for every territory the curse
        /// holds (§6.8, 2026-10-07: "proportional to territories held").</summary>
        [Min(0)] public int curseUnitsPerTerritory = 40;

        // ── §6.5 / §6.8 (2026-10-07): conquest and holding ──

        /// <summary>Claim and fill parties the curse may have out at once
        /// (§6.5). Each expansion check sends at most one more.</summary>
        [Min(1)] public int maxConcurrentClaims = 2;

        /// <summary>CONQUEST WAVES (§6.8, 2026-10-07): an attack wave may march
        /// on a LOCKED territory of its target that borders curse ground,
        /// strike the structures that lock it, and — once it is unlocked —
        /// stay as a claim party that takes it, instead of walking home.</summary>
        public bool conquestWaves = true;

        // ── §6.6 (2026-10-07): the Shardroot — hunt and guard ──

        /// <summary>Seconds between Shardroot hunt dispatches while a player
        /// holds it (§6.6). The hunt has its own clock: it no longer takes the
        /// expansion slot, so the curse keeps claiming while it hunts.</summary>
        [Min(5f)] public float shardrootHuntSeconds = 45f;

        /// <summary>Units drafted into each Shardroot hunt party (x the
        /// holder bonus, shardrootCurseBonus).</summary>
        [Min(1)] public int shardrootHuntPartySize = 10;

        /// <summary>Hunt parties that may press the holder at once.</summary>
        [Min(0)] public int shardrootMaxHunts = 2;

        /// <summary>Units the curse keeps around the Shardroot while it lies
        /// on the ground (§6.6), topped up every check.</summary>
        [Min(0)] public int shardrootGuardPartySize = 12;

        /// <summary>The Shardroot guard engages hostiles within this many
        /// metres of the artifact.</summary>
        [Min(1f)] public float shardrootGuardRadius = 25f;

        /// <summary>A guard unit fighting farther than this from the artifact
        /// drops the fight and walks back.</summary>
        [Min(1f)] public float shardrootGuardLeashRadius = 40f;

        /// <summary>A garrison goes for the Shardroot holder, ahead of any
        /// other intruder, when the holder is within this many metres of the
        /// node it guards (§6.6).</summary>
        [Min(0f)] public float shardrootHolderAggroRadius = 40f;

        /// <summary>While a player holds the Shardroot the attack-wave
        /// interval is multiplied by this (§6.6): below 1 = waves come faster.</summary>
        [Range(0.1f, 1f)] public float shardrootHolderWaveIntervalMult = 0.6f;

        // ── Territory_Claims.md §6 (2026-09-29): the curse as a claimant ──

        /// <summary>Curse nodes raised at match start on random resource
        /// nodes (§6.4). 0 = one per player.</summary>
        [Min(0)] public int initialNodes = 0;

        /// <summary>Seconds a curse with no node left waits before it raises
        /// a fresh one (§6.5). The curse can be driven back, never out.</summary>
        [Min(0f)] public float reseedSeconds = 180f;

        /// <summary>Radius (m) of cursed ground around each curse node:
        /// speed debuff, damage over time and the cursed look (§6.3).</summary>
        [Min(0f)] public float nodeAuraRadius = 20f;

        /// <summary>The first curse spawn after this many seconds carries the
        /// Shardroot if it is not out yet (§6.6 backstop).</summary>
        [Min(0f)] public float shardrootGuaranteeSeconds = 720f;

        /// <summary>While a player holds the Shardroot the curse's garrison
        /// size and spawn rate rise by this fraction (§6.6): 0.5 = size x1.5,
        /// interval / 1.5.</summary>
        [Min(0f)] public float shardrootCurseBonus = 0.5f;

        /// <summary>Veilsteel a FERALDIS faction is paid for landing the last
        /// hit on a curse node (docs/Design/Veilstone_Economy.md §3.2).
        /// Veilsteel is never mined; this is how Feraldis get theirs.</summary>
        [Min(0)] public int feraldisNodeVeilsteel = 40;

        /// <summary>Which army tier the garrisons and merge parties draw from
        /// at a given match minute: tier index = minute / this. Clamped to
        /// the ladder. Replaces wrath as the composition dial.</summary>
        [Min(1f)] public float minutesPerTier = 8f;

        [Min(0.5f)] public float decisionInterval = 5f;
        [Min(0.5f)] public float replenishInterval = 4f;

        [Min(0.1f)] public float crystallingTrainTime = 8f;
        [Min(0.1f)] public float veilstingerTrainTime = 15f;
        [Min(0.1f)] public float godsplinterTrainTime = 30f;

        // Crystalling packs (2026-09-07). Each other Crystalling within the
        // radius adds crystallingPackBonusPerMember to the unit's damage,
        // up to crystallingPackMaxBonus. Radius 0 or bonus 0 turns it off.
        // At the defaults a pack of 11 hits for double.
        [Min(0f)] public float crystallingPackRadius = 7f;
        [Min(0f)] public float crystallingPackBonusPerMember = 0.10f;
        [Min(0f)] public float crystallingPackMaxBonus = 1.0f;

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

        public float baseIncomePerSecond = 6f;
        public float incomePerResourceNode = 4f;
        [Min(0)] public int startingCrystal = 250;

        public float defendHoldRadius = 18f;
        public bool replenishNeedsResourceNode = true;

        public bool requireFullMusterBeforeAttack = true;
        public float recallArriveRadius = 16f;

        public float phaseIncomeBonus = 0.5f;
        public float phaseTrainSpeedBonus = 0.25f;
        [Min(0f)] public float escalationStartMinute = 5f;
        [Min(0f)] public float escalationFullMinute = 20f;
        [Min(0f)] public float maxEscalation = 2f;
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
        /// <summary>
        /// RETIRED as the escalation driver (2026-09-08, design §2.10) and
        /// currently called by nothing. The wave tier comes from the provoking
        /// faction's CurseWrath now, not from elapsed match time; pacing comes
        /// from <see cref="BreatherForTier"/>.
        ///
        /// Kept because the authored <see cref="waves"/> rows are still the
        /// data BreatherForTier reads, and because a mode that genuinely wants
        /// a timed curse (a scenario, a horde mode) would want exactly this.
        /// Do NOT wire it back into CurseTerritorySystem: doing so restores
        /// the curse that fights everyone on a clock nobody can influence.
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
        /// The breather that belongs to a given TIER, rather than to a point
        /// on the match clock.
        ///
        /// Waves used to take both their tier and their pacing from elapsed
        /// time via <see cref="TryGetWave"/>. Now that wrath picks the tier
        /// (§2.10), reading the breather off the clock would smuggle the old
        /// escalation back in through the side door — an unprovoked curse
        /// would still speed up merely because the match got long. Matching on
        /// the tier keeps both halves of a schedule row describing the same
        /// intensity.
        ///
        /// Falls back to the row with the nearest lower tier, then to
        /// <see cref="waveBreatherSeconds"/>.
        /// </summary>
        public float BreatherForTier(int tier)
        {
            float fallback = Mathf.Max(0f, waveBreatherSeconds);
            if (!useWaveSchedule || waves == null || waves.Count == 0) return fallback;

            WaveEntry best = null;
            for (int i = 0; i < waves.Count; i++)
            {
                var w = waves[i];
                if (w == null || w.tier > tier) continue;
                if (best == null || w.tier > best.tier) best = w;
            }
            return best != null ? Mathf.Max(0f, best.breatherSeconds) : fallback;
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
            crystallingPackRadius = 7f;
            crystallingPackBonusPerMember = 0.10f;
            crystallingPackMaxBonus = 1.0f;
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
            wrathCoolSeconds = 180f;
            livingCurse = true;
            garrisonCap = 8;
            armySpawnSeconds = 120f;
            armyGrowth = 1.12f;
            expansionSeconds = 150f;
            shardrootChance = 0.04f;
            shardrootMawSeconds = 720f;
            mergeSeconds = 90f;
            mergePartySize = 6;
            mergeDefendRadius = 30f;
            leashSeconds = 25f;
            guardRadius = 30f;
            guardLeashRadius = 45f;
            firstWaveSeconds = 480f;
            waveIntervalSeconds = 240f;
            waveDraftFraction = 0.30f;
            waveMinSize = 6;
            garrisonMinPerNode = 2;
            waveDurationSeconds = 150f;
            waveRetreatFraction = 0.25f;
            waveShareTerritoryWeight = 0.5f;
            waveShareFloor = 0.1f;
            wavePlayerCooldownSeconds = 300f;
            waveSizeVsPower = 0.6f;
            waveSizeByDifficulty = new[] { 0.5f, 1f, 1f, 1.25f };
            claimGraceSeconds = 480f;
            maxCurseUnits = 1000;
            curseUnitsBase = 40;
            curseUnitsPerTerritory = 40;
            maxConcurrentClaims = 2;
            conquestWaves = true;
            shardrootHuntSeconds = 45f;
            shardrootHuntPartySize = 10;
            shardrootMaxHunts = 2;
            shardrootGuardPartySize = 12;
            shardrootGuardRadius = 25f;
            shardrootGuardLeashRadius = 40f;
            shardrootHolderAggroRadius = 40f;
            shardrootHolderWaveIntervalMult = 0.6f;
            initialNodes = 0;
            reseedSeconds = 180f;
            nodeAuraRadius = 20f;
            shardrootGuaranteeSeconds = 720f;
            shardrootCurseBonus = 0.5f;
            feraldisNodeVeilsteel = 40;
            minutesPerTier = 8f;
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
