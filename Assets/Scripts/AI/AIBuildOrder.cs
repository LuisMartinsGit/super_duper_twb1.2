// Hardcoded Age-1 build orders for the SimpleAISystem.
// Each strategy is a flat list of steps the AI tries to issue in order.
// A step ADVANCES on issue (not on completion) â€” the AI doesn't wait for the
// trained unit/finished building before moving to the next step.

namespace TheWaningBorder.AI
{
    public enum BuildStepKind : byte
    {
        TrainUnit,         // queue a unit at the appropriate training building
        BuildBuilding,     // place a building near the Hall (uses idle Worker)
        Research,          // queue a tech at the Barracks (or Hall, etc.)
        AgeUp,             // trigger AgeUp on the Hall (60 s wait)
        SetVeilstoneTarget,  // set the AI's target veilstone-miner count (IntArg)
        LaunchAttack,      // send all idle military to attack closest enemy (IntArg = min units)
    }

    /// <summary>
    /// One step in an AI build order. Strings keep this trivially serialisable
    /// and let us hardcode the orders without a content pipeline.
    /// </summary>
    public struct BuildOrderStep
    {
        public BuildStepKind Kind;
        public string Id;        // unitId, buildingId, or techId; ignored for AgeUp/SetVeilstoneTarget
        public bool Optional;    // Easy difficulty may skip optional steps
        public int IntArg;       // numeric arg (e.g. SetVeilstoneTarget count); 0 otherwise

        public static BuildOrderStep Train(string unitId, bool optional = false) =>
            new() { Kind = BuildStepKind.TrainUnit, Id = unitId, Optional = optional };

        public static BuildOrderStep Build(string buildingId, bool optional = false) =>
            new() { Kind = BuildStepKind.BuildBuilding, Id = buildingId, Optional = optional };

        public static BuildOrderStep ResearchTech(string techId, bool optional = false) =>
            new() { Kind = BuildStepKind.Research, Id = techId, Optional = optional };

        public static BuildOrderStep AgeUpStep() =>
            new() { Kind = BuildStepKind.AgeUp, Id = string.Empty, Optional = false };

        /// <summary>
        /// Set the FLOOR for veilstone-miner allocation. The AI normally splits
        /// idle miners 50/50 between iron and veilstone whenever outcroppings are
        /// reachable; this step lets a strategy push the floor higher (e.g.
        /// TechBoom asking for 2 veilstone miners with only 4 total miners,
        /// front-loading veilstone income). The effective target each tick is
        /// max(this floor, totalMiners / 2). Capped at 16.
        /// </summary>
        public static BuildOrderStep SetVeilstoneTarget(int count) =>
            new() { Kind = BuildStepKind.SetVeilstoneTarget, Id = string.Empty, IntArg = count };

        /// <summary>
        /// Send every idle military unit (Melee/Ranged/Siege/Magic, plus
        /// battalion leaders) to attack-move toward the closest enemy economy
        /// target. Priority: enemy Miners â†’ GathererHuts â†’ Halls.
        ///
        /// Blocks the build order until at least <paramref name="minUnits"/>
        /// idle military are available â€” so a "wait for the army to assemble,
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
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 1. ECONOMY BOOM â€” fastest age-up via heavy economy infrastructure
        //    3Mn â†’ 4 GHut â†’ 3Mn â†’ Vault â†’ AgeUp (4 Mn during 60s wait)
        //    Choice: Vault. Culture: Runai or Alanthor.
        //    Veilstone: ramps to 2 once 6 miners exist (heavy iron focus for the
        //    Vault + age-up cost; veilstone needed only for age-up).
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static readonly BuildOrderStep[] EcoBoom =
        {
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout"),       // map vision so the AI can see what to attack
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.SetVeilstoneTarget(2),  // 6 miners â†’ 2 on veilstone for age-up
            BuildOrderStep.Build("VaultOfAlmierra"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Worker"),  // during ageup wait
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout", optional: true),  // second scout once economy is stable
        };

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 2. BALANCED â€” token military + Shrine
        //    Choice: ShrineOfAhridan. Culture: Random.
        //    Veilstone: 2 from mid-eco onward (steady drip for Shrine + age-up).
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static readonly BuildOrderStep[] Balanced =
        {
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout"),       // map vision before military commitment
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.SetVeilstoneTarget(2),  // 6 miners â†’ 2 on veilstone
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),   // was Archer — ranged is an Age-1 unlock (2026-08-11)
            BuildOrderStep.Build("ShrineOfAhridan"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout", optional: true),
            // Commit the standing army at least once after age-up so the AI
            // isn't a passive sandbag in a demo. The maintenance loop in
            // SimpleAISystem takes over from here and keeps pushing waves.
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),   // was Archer — ranged is an Age-1 unlock (2026-08-11)
            BuildOrderStep.LaunchAttack(2),
        };

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 3. TECH BOOM â€” research both Barracks techs before age up
        //    Choice: ShrineOfAhridan. Culture: Runai.
        //    Veilstone: 3 â€” heaviest veilstone demand of any strategy because both
        //    techs and the Shrine cost veilstone on top of age-up.
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static readonly BuildOrderStep[] TechBoom =
        {
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout"),       // map vision while economy ramps
            BuildOrderStep.SetVeilstoneTarget(2),  // start veilstone early â€” techs need it
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.SetVeilstoneTarget(3),  // 6 miners â†’ ramp to 3 on veilstone
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.ResearchTech("Conscription"),
            BuildOrderStep.ResearchTech("StoneWeapons"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Build("ShrineOfAhridan"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            // Commit the upgraded army post-age-up so the tech investment
            // actually shows up on the map. Maintenance loop continues
            // pushing waves after this final step.
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.LaunchAttack(2),
        };

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 4. RUSH â€” three attack waves (1 / 2 / 4 battalions)
        //    Choice: ShrineOfAhridan. Culture: Feraldis.
        //    Veilstone: 1, late â€” every miner is needed on iron for the army
        //    rush; only switch on veilstone when the Shrine + age-up draw near.
        //    Attacks: a LaunchAttack(N) step after each wave blocks the build
        //    order until N idle battalions exist, then sends them to harass
        //    the closest enemy economy. Survivors get re-tasked by the next
        //    wave's LaunchAttack call.
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static readonly BuildOrderStep[] Rush =
        {
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout"),         // find the enemy before sending the rush
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Spearman"),    // Wave #1 (1 battalion)
            BuildOrderStep.LaunchAttack(1),       // â†’ harass enemy miners
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Spearman"),    // Wave #2 (1st batt)
            BuildOrderStep.Train("Spearman"),    // Wave #2 (2nd batt)
            BuildOrderStep.LaunchAttack(2),       // â†’ push, 2 fresh batts (+ wave-1 survivors)
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Spearman"),    // Wave #3 (4 battalions)
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.LaunchAttack(4),       // â†’ big push, 4 fresh batts (+ survivors)
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.SetVeilstoneTarget(1),   // late switch â€” just enough for Shrine + age-up
            BuildOrderStep.Build("ShrineOfAhridan"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
        };

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 5. TURTLE â€” standing army + healers + big stockpile for Alanthor walls
        //    Choice: TempleOfRidan (trains Litharchs). Culture: Alanthor.
        //    Veilstone: 2 mid, then 3 around the Temple/Litharch phase (Litharchs
        //    cost veilstone and the Temple itself is a choice building).
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static readonly BuildOrderStep[] Turtle =
        {
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout"),       // warn of incoming pressure
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.SetVeilstoneTarget(2),  // 4 miners â†’ 2 on veilstone
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Spearman"),   // was Archer — ranged is an Age-1 unlock (2026-08-11)
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Train("Spearman"),   // was Archer — ranged is an Age-1 unlock (2026-08-11)
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.SetVeilstoneTarget(3),  // ramp for Temple + 2 Litharchs
            BuildOrderStep.Build("TempleOfRidan"),
            BuildOrderStep.Train("Litharch"),
            BuildOrderStep.Train("Litharch"),
            BuildOrderStep.AgeUpStep(),
            // Turtle is defensive but still has a standing army â€” push it
            // out at least once. Maintenance loop keeps the pressure on.
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),   // was Archer — ranged is an Age-1 unlock (2026-08-11)
            BuildOrderStep.LaunchAttack(2),
        };

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 6. DEFENSIVE â€” leaner buildings, upgraded standing army
        //    Choice: VaultOfAlmierra. Culture: Feraldis.
        //    Veilstone: 2 around techs (techs + Vault both cost veilstone).
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static readonly BuildOrderStep[] Defensive =
        {
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Scout"),       // map awareness before turtling
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut"),
            BuildOrderStep.Build("GatherersHut", optional: true),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.SetVeilstoneTarget(2),  // 6 miners â†’ 2 on veilstone for techs + Vault
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Hut"),
            BuildOrderStep.Build("Barracks"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.ResearchTech("Conscription"),
            BuildOrderStep.ResearchTech("StoneWeapons"),
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),   // was Archer — ranged is an Age-1 unlock (2026-08-11)
            BuildOrderStep.Build("VaultOfAlmierra"),
            BuildOrderStep.AgeUpStep(),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            BuildOrderStep.Train("Worker"),
            // Commit the Drilled+Armoured standing army at least once so the
            // tech upgrades are visible on the map. Maintenance loop keeps
            // sending fresh waves after this.
            BuildOrderStep.Train("Spearman"),
            BuildOrderStep.Train("Spearman"),   // was Archer — ranged is an Age-1 unlock (2026-08-11)
            BuildOrderStep.LaunchAttack(2),
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
        /// The strategy's PRIOR culture preference — what this personality
        /// leans toward before it has looked at the map. Used as the base
        /// score by <see cref="AICultureChoice"/>, which then bends it with
        /// scouted intel.
        ///
        /// Runai is deliberately absent: it is still an incomplete culture
        /// (CultureConfig.IsComingSoon locks it for the player too), so the
        /// AI must never pick it. Restore it here when Runai ships.
        ///
        /// Returns a signed lean: negative = Alanthor, positive = Feraldis.
        /// </summary>
        public static float CultureLeanFor(AIStrategy strategy) => strategy switch
        {
            // Aggression wants the raiding culture.
            AIStrategy.Rush       => +2.0f,
            AIStrategy.Aggressive => +1.5f,
            // Balanced/eco lean slightly to the fortified culture.
            AIStrategy.EcoBoom    => -0.5f,
            AIStrategy.TechRush   => -0.5f,
            // Defensive play wants walls and towers.
            AIStrategy.Defensive  => -2.0f,
            AIStrategy.Turtle     => -2.5f,
            _                     => 0f,
        };

        /// <summary>
        /// Legacy entry point kept for callers that have no intel context.
        /// Prefer <see cref="AICultureChoice.Pick"/>, which reads the AI's
        /// actual scouting before deciding.
        /// </summary>
        public static byte CultureFor(AIStrategy strategy, uint randomSeed)
        {
            float lean = CultureLeanFor(strategy);
            // Break a dead tie deterministically off the seed.
            byte pick = lean == 0f
                ? ((randomSeed & 1) == 0 ? Cultures.Alanthor : Cultures.Feraldis)
                : (lean > 0f ? Cultures.Feraldis : Cultures.Alanthor);
            // Ship gate — see CultureConfig.Playable.
            return CultureConfig.Playable(pick);
        }
    }
}

