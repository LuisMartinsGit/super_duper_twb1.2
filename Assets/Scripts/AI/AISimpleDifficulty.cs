// Difficulty-tier knobs for the SimpleAISystem executor.
//
// AoE4 model (docs/Design/Game_AI.md §2, docs/Research/AoE4_AI_Study.md §2):
// ONE brain, difficulty expressed purely as data — behavior quality only,
// never resource or vision cheats. Each tier is a full profile of knobs
// (think rate, worker targets, attack timing, raiding / counter-composition
// / staging / expansion toggles), the equivalent of Relic's per-difficulty
// personality Lua files.

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Complete per-difficulty tuning profile. All tiers are FAIR — no
    /// gather-rate or vision multipliers (the AoE4 hidden-Hardest-cheat
    /// lesson). If cheat tiers are ever added they must be new, clearly
    /// labeled entries, not silent buffs to existing ones.
    /// </summary>
    public struct AIDifficultyProfile
    {
        /// <summary>Seconds between AI think-ticks. Lower = faster reactions.</summary>
        public float ThinkInterval;
        /// <summary>Chance per tick to skip an Optional build-order step
        /// (deliberately sloppy openings on low tiers).</summary>
        public float OptionalStepSkipChance;
        /// <summary>Worker (miner/builder) count the economy manager grows
        /// toward before age-up…</summary>
        public int WorkerTargetAge0;
        /// <summary>…and after age-up (AoE4: villager targets rise per age).</summary>
        public int WorkerTargetAge1;
        /// <summary>No attack missions launch before this game time (seconds).
        /// AoE4 community measurement: first Hardest attack ≈ 8 min; lower
        /// tiers attack later.</summary>
        public float FirstAttackEarliestSeconds;
        /// <summary>Peel off fast raid parties at the enemy economy alongside
        /// the main attack (AoE4: Hard+ raids constantly, Easy never).</summary>
        public bool RaidingEnabled;
        /// <summary>Adapt the trained unit mix to the observed enemy
        /// composition (AoE4: higher tiers counter-pick, lower don't).</summary>
        public bool CounterCompEnabled;
        /// <summary>Attack missions form up at a staging point near the
        /// target before committing (deliberately BETTER than AoE4, which
        /// always rallies at its homebase).</summary>
        public bool ForwardStaging;
        /// <summary>Sustained-production army ceiling for the maintenance
        /// loop (DesiredMilitary grows to this while affordable).</summary>
        /// <summary>
        /// How large an army this difficulty keeps standing, before the plan's
        /// ArmyScale multiplies it.
        ///
        /// Was 10/20/24/32 across the ladder — an order of magnitude below the
        /// 200-population ceiling, so the AI stopped WANTING soldiers long
        /// before it ran out of housing or money and no faction ever came near
        /// the cap. The ceiling is ~40 workers plus ~160 soldiers, so Normal
        /// sits at 100 and MASSING (1.6x) takes it to 160.
        /// </summary>
        public int SustainArmyCap;

        /// <summary>
        /// Game time (seconds) at which this AI STOPS expanding and starts
        /// banking for Age 1: the wallet tilts to Advancement, the age-up
        /// director buys its choice building, and new Gatherer's Huts pause
        /// until the age-up is issued.
        ///
        /// This is the whole age-up clock. Before it existed, every AI
        /// expanded flat-out until a single hard-coded 300 s gate, and since
        /// founding a hut costs ~120 supplies it spent its income as fast as
        /// it arrived — logged matches ended minute 6 with 10-12 huts and 62
        /// supplies banked against a 700-supply age-up. Nothing was saving,
        /// so nothing aged up.
        ///
        /// Budget backwards from the target age-up time: push, then ~60 s to
        /// raise the 257-supply Shrine, then bank 250 while it builds.
        /// Targets (median, 2026-08-29 per Age_0.md): Expert ~3 min,
        /// Hard ~4, Normal ~5, Easy ~6.
        /// </summary>
        public float AgeUpPushSeconds;

        // ── Attack-wave cadence (2026-08-04: spectated AIs built one army
        // and attacked once at ~20 min; waves make pressure a RHYTHM) ──
        /// <summary>Seconds between wave launches once the first-attack gate
        /// has passed. Lower tiers breathe slower.</summary>
        public float AttackWaveIntervalSeconds;
        /// <summary>Idle-army minimum for wave 1…</summary>
        public int WaveBaseUnits;
        /// <summary>…growing by this per successful wave (increasingly larger
        /// armies), capped near SustainArmyCap. A wave drafts ALL idle
        /// military, so real waves outgrow the minimum as the economy does.</summary>
        public int WaveGrowthUnits;

        // ── Economy spread + parallel production (2026-08-04) ──
        /// <summary>
        /// Gatherer's Huts the maintenance loop grows toward, placed
        /// progressively farther out — income AND (post-age-up) influence,
        /// i.e. MAP CONTROL. This is the main economic separator between
        /// tiers, so the spread is deliberately wide: Easy keeps a modest
        /// home cluster, Expert aims to blanket the map.
        ///
        /// (Raised across the board 2026-08-06: the original 3/5/8/10 was
        /// far too timid — 3 huts is not an economy even for Easy — and the
        /// value was read by nothing at all until the same day, so nobody
        /// had ever felt these numbers.)
        /// </summary>
        public int GathererHutTarget;
        /// <summary>Total military production buildings (Barracks + Archery
        /// Ranges) to build toward, so armies train in parallel.</summary>
        public int ProductionBuildingTarget;
    }

    /// <summary>
    /// Per-difficulty tuning for the SimpleAISystem. Difficulty is data —
    /// every tier runs the identical brain.
    /// </summary>
    public static class AISimpleDifficulty
    {
        public static AIDifficultyProfile GetProfile(AIDifficulty d) => d switch
        {
            AIDifficulty.Easy => new AIDifficultyProfile
            {
                ThinkInterval = 5.0f,
                OptionalStepSkipChance = 0.25f,
                WorkerTargetAge0 = 3,
                WorkerTargetAge1 = 5,
                FirstAttackEarliestSeconds = 480f,
                RaidingEnabled = false,
                CounterCompEnabled = false,
                ForwardStaging = false,
                SustainArmyCap = 55,
                AttackWaveIntervalSeconds = 300f,
                WaveBaseUnits = 4,
                WaveGrowthUnits = 1,
                GathererHutTarget = 8,   // a modest home cluster
                AgeUpPushSeconds = 120f,  // target age-up ~6 min (Easy)
                ProductionBuildingTarget = 20,
            },
            AIDifficulty.Hard => new AIDifficultyProfile
            {
                ThinkInterval = 0.5f,
                OptionalStepSkipChance = 0f,
                WorkerTargetAge0 = 4,
                WorkerTargetAge1 = 5,
                FirstAttackEarliestSeconds = 240f,
                RaidingEnabled = true,
                CounterCompEnabled = true,
                ForwardStaging = true,
                SustainArmyCap = 125,
                AttackWaveIntervalSeconds = 180f,
                WaveBaseUnits = 6,
                WaveGrowthUnits = 2,
                GathererHutTarget = 22,  // pushes well past the home ring
                AgeUpPushSeconds = 60f,   // target age-up ~4 min (Hard)
                ProductionBuildingTarget = 28,
            },
            AIDifficulty.Expert => new AIDifficultyProfile
            {
                ThinkInterval = 0.25f,
                OptionalStepSkipChance = 0f,
                WorkerTargetAge0 = 4,
                WorkerTargetAge1 = 5,
                FirstAttackEarliestSeconds = 180f,
                RaidingEnabled = true,
                CounterCompEnabled = true,
                ForwardStaging = true,
                SustainArmyCap = 150,
                AttackWaveIntervalSeconds = 120f,
                WaveBaseUnits = 6,
                WaveGrowthUnits = 2,
                GathererHutTarget = 30,  // aims to blanket the map
                AgeUpPushSeconds = 30f,   // target age-up ~3 min (Expert)
                ProductionBuildingTarget = 30,
            },
            _ => new AIDifficultyProfile // Normal
            {
                ThinkInterval = 2.0f,
                OptionalStepSkipChance = 0.10f,
                WorkerTargetAge0 = 3,
                WorkerTargetAge1 = 5,
                FirstAttackEarliestSeconds = 360f,
                RaidingEnabled = true,
                CounterCompEnabled = false,
                ForwardStaging = false,
                // Sharpened 2026-08-04 ("defeat me"): a Normal AI that wins
                // the economy must also convert it — bigger ceiling, waves
                // that escalate instead of plateauing.
                SustainArmyCap = 100,
                AttackWaveIntervalSeconds = 240f,
                WaveBaseUnits = 5,
                WaveGrowthUnits = 2,
                GathererHutTarget = 14,  // a real economic footprint
                AgeUpPushSeconds = 90f,   // target age-up ~5 min (Normal)
                // 10 -> 24 (2026-08-31 directive: "military production
                // should be 20-30"): armies die continuously at the fronts
                // now, and production breadth is what keeps the waves fed.
                ProductionBuildingTarget = 24,
            },
        };

        /// <summary>Seconds between AI think-ticks (profile shorthand).</summary>
        public static float GetThinkInterval(AIDifficulty d) => GetProfile(d).ThinkInterval;

        /// <summary>Optional-step skip chance (profile shorthand).</summary>
        public static float GetSkipChance(AIDifficulty d) => GetProfile(d).OptionalStepSkipChance;
    }
}
