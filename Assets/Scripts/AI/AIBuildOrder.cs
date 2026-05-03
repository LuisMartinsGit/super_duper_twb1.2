// Hardcoded Age-1 build orders for the SimpleAISystem.
// Each strategy is a flat list of steps the AI tries to issue in order.
// A step ADVANCES on issue (not on completion) — the AI doesn't wait for the
// trained unit/finished building before moving to the next step.
// Location: Assets/Scripts/AI/AIBuildOrder.cs

namespace TheWaningBorder.AI
{
    public enum BuildStepKind : byte
    {
        TrainUnit,         // queue a unit at the appropriate training building
        BuildBuilding,     // place a building near the Hall (uses idle Builder)
        Research,          // queue a tech at the Barracks (or Hall, etc.)
        AgeUp,             // trigger AgeUp on the Hall (60 s wait)
        SetCrystalTarget,  // set the AI's target crystal-miner count (IntArg)
        LaunchAttack,      // send all idle military to attack closest enemy (IntArg = min units)
    }

    /// <summary>
    /// One step in an AI build order. Strings keep this trivially serialisable
    /// and let us hardcode the orders without a content pipeline.
    /// </summary>
    public struct BuildOrderStep
    {
        public BuildStepKind Kind;
        public string Id;        // unitId, buildingId, or techId; ignored for AgeUp/SetCrystalTarget
        public bool Optional;    // Easy difficulty may skip optional steps
        public int IntArg;       // numeric arg (e.g. SetCrystalTarget count); 0 otherwise

        public static BuildOrderStep Train(string unitId, bool optional = false) =>
            new() { Kind = BuildStepKind.TrainUnit, Id = unitId, Optional = optional };

        public static BuildOrderStep Build(string buildingId, bool optional = false) =>
            new() { Kind = BuildStepKind.BuildBuilding, Id = buildingId, Optional = optional };

        public static BuildOrderStep ResearchTech(string techId, bool optional = false) =>
            new() { Kind = BuildStepKind.Research, Id = techId, Optional = optional };

        public static BuildOrderStep AgeUpStep() =>
            new() { Kind = BuildStepKind.AgeUp, Id = string.Empty, Optional = false };

        /// <summary>
        /// Set the FLOOR for crystal-miner allocation. The AI normally splits
        /// idle miners 50/50 between iron and crystal whenever cadavers are
        /// reachable; this step lets a strategy push the floor higher (e.g.
        /// TechBoom asking for 2 crystal miners with only 4 total miners,
        /// front-loading crystal income). The effective target each tick is
        /// max(this floor, totalMiners / 2). Capped at 16.
        /// </summary>
        public static BuildOrderStep SetCrystalTarget(int count) =>
            new() { Kind = BuildStepKind.SetCrystalTarget, Id = string.Empty, IntArg = count };

        /// <summary>
        /// Send every idle military unit (Melee/Ranged/Siege/Magic, plus
        /// battalion leaders) to attack-move toward the closest enemy economy
        /// target. Priority: enemy Miners → GathererHuts → Halls.
        ///
        /// Blocks the build order until at least <paramref name="minUnits"/>
        /// idle military are available — so a "wait for the army to assemble,
        /// then commit it" rhythm falls out naturally. Use this after each
        /// wave's Train steps in attack-oriented strategies.
        /// </summary>
        public static BuildOrderStep LaunchAttack(int minUnits) =>
            new() { Kind = BuildStepKind.LaunchAttack, Id = string.Empty, IntArg = minUnits };
    }

    /// <summary>
    /// The 6 hardcoded Age-1 build orders. See the design notes for the full
    /// rationale and timing tables.
    /// </summary>
    public static class AIBuildOrder
    {
        // ─────────────────────────────────────────────────────────────────
        // 1. ECONOMY BOOM — fastest age-up via heavy economy infrastructure
        //    3Mn → 4 GHut → 3Mn → Vault → AgeUp (4 Mn during 60s wait)
        //    Choice: Vault. Culture: Runai or Alanthor.
        //    Crystal: ramps to 2 once 6 miners exist (heavy iron focus for the
        //    Vault + age-up cost; crystal needed only for age-up).
        // ─────────────────────────────────────────────────────────────────
        public static readonly BuildOrderStep[] EcoBoom =
        {
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.SetCrystalTarget(2),  // 6 miners → 2 on crystal for age-up
            BuildOrderStep.Build("VaultOfAlmierra"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Miner"),  // during ageup wait
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
        };

        // ─────────────────────────────────────────────────────────────────
        // 2. BALANCED — token military + Shrine
        //    Choice: ShrineOfAhridan. Culture: Random.
        //    Crystal: 2 from mid-eco onward (steady drip for Shrine + age-up).
        // ─────────────────────────────────────────────────────────────────
        public static readonly BuildOrderStep[] Balanced =
        {
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.SetCrystalTarget(2),  // 6 miners → 2 on crystal
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.Train("Archer"),
            BuildOrderStep.Build("ShrineOfAhridan"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
        };

        // ─────────────────────────────────────────────────────────────────
        // 3. TECH BOOM — research both Barracks techs before age up
        //    Choice: ShrineOfAhridan. Culture: Runai.
        //    Crystal: 3 — heaviest crystal demand of any strategy because both
        //    techs and the Shrine cost crystal on top of age-up.
        // ─────────────────────────────────────────────────────────────────
        public static readonly BuildOrderStep[] TechBoom =
        {
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.SetCrystalTarget(2),  // start crystal early — techs need it
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.SetCrystalTarget(3),  // 6 miners → ramp to 3 on crystal
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.ResearchTech("BasicDrills"),
            BuildOrderStep.ResearchTech("WoodenArmor"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.Build("ShrineOfAhridan"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
        };

        // ─────────────────────────────────────────────────────────────────
        // 4. RUSH — three attack waves (1 / 2 / 4 battalions)
        //    Choice: ShrineOfAhridan. Culture: Feraldis.
        //    Crystal: 1, late — every miner is needed on iron for the army
        //    rush; only switch on crystal when the Shrine + age-up draw near.
        //    Attacks: a LaunchAttack(N) step after each wave blocks the build
        //    order until N idle battalions exist, then sends them to harass
        //    the closest enemy economy. Survivors get re-tasked by the next
        //    wave's LaunchAttack call.
        // ─────────────────────────────────────────────────────────────────
        public static readonly BuildOrderStep[] Rush =
        {
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Swordsman"),    // Wave #1 (1 battalion)
            BuildOrderStep.LaunchAttack(1),       // → harass enemy miners
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Swordsman"),    // Wave #2 (1st batt)
            BuildOrderStep.Train("Swordsman"),    // Wave #2 (2nd batt)
            BuildOrderStep.LaunchAttack(2),       // → push, 2 fresh batts (+ wave-1 survivors)
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Swordsman"),    // Wave #3 (4 battalions)
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.LaunchAttack(4),       // → big push, 4 fresh batts (+ survivors)
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.SetCrystalTarget(1),   // late switch — just enough for Shrine + age-up
            BuildOrderStep.Build("ShrineOfAhridan"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
        };

        // ─────────────────────────────────────────────────────────────────
        // 5. TURTLE — standing army + healers + big stockpile for Alanthor walls
        //    Choice: TempleOfRidan (trains Litharchs). Culture: Alanthor.
        //    Crystal: 2 mid, then 3 around the Temple/Litharch phase (Litharchs
        //    cost crystal and the Temple itself is a choice building).
        // ─────────────────────────────────────────────────────────────────
        public static readonly BuildOrderStep[] Turtle =
        {
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.SetCrystalTarget(2),  // 4 miners → 2 on crystal
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Archer"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Archer"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.SetCrystalTarget(3),  // ramp for Temple + 2 Litharchs
            BuildOrderStep.Build("TempleOfRidan"),
            BuildOrderStep.Train("Litharch"),
            BuildOrderStep.Train("Litharch"),
            BuildOrderStep.AgeUpStep(),
        };

        // ─────────────────────────────────────────────────────────────────
        // 6. DEFENSIVE — leaner buildings, upgraded standing army
        //    Choice: VaultOfAlmierra. Culture: Feraldis.
        //    Crystal: 2 around techs (techs + Vault both cost crystal).
        // ─────────────────────────────────────────────────────────────────
        public static readonly BuildOrderStep[] Defensive =
        {
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.SetCrystalTarget(2),  // 6 miners → 2 on crystal for techs + Vault
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.ResearchTech("BasicDrills"),
            BuildOrderStep.ResearchTech("WoodenArmor"),
            BuildOrderStep.Train("Swordsman"),
            BuildOrderStep.Train("Archer"),
            BuildOrderStep.Build("VaultOfAlmierra"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
            BuildOrderStep.Train("Miner"),
        };

        /// <summary>
        /// Returns the build order array for the given strategy.
        /// AIStrategy.Aggressive maps to Balanced, AIStrategy.TechRush maps to
        /// TechBoom (legacy enum names preserved for compatibility).
        /// </summary>
        public static BuildOrderStep[] For(AIStrategy strategy) => strategy switch
        {
            AIStrategy.EcoBoom    => EcoBoom,
            AIStrategy.Aggressive => Balanced,   // legacy alias
            AIStrategy.TechRush   => TechBoom,   // legacy alias
            AIStrategy.Rush       => Rush,
            AIStrategy.Defensive  => Defensive,
            AIStrategy.Turtle     => Turtle,
            _                     => Balanced,
        };

        /// <summary>
        /// Maps a strategy to its preferred Age-2 culture. The SimpleAISystem
        /// passes this as the AgeUpState.Culture when it triggers age-up.
        /// </summary>
        public static byte CultureFor(AIStrategy strategy, uint randomSeed) => strategy switch
        {
            AIStrategy.EcoBoom    => (randomSeed & 1) == 0 ? Cultures.Runai : Cultures.Alanthor,
            AIStrategy.Aggressive => CulturePicks[randomSeed % 3], // Balanced → random
            AIStrategy.TechRush   => Cultures.Runai,               // TechBoom → Runai
            AIStrategy.Rush       => Cultures.Feraldis,
            AIStrategy.Defensive  => Cultures.Feraldis,
            AIStrategy.Turtle     => Cultures.Alanthor,
            _                     => Cultures.Runai,
        };

        private static readonly byte[] CulturePicks =
        {
            Cultures.Runai,
            Cultures.Alanthor,
            Cultures.Feraldis,
        };
    }
}
