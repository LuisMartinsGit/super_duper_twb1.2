// File: Assets/Scripts/Core/Settings/BorderConstants.cs
// Centralised constants for all Border faction entities and AI.
// Factories and BorderAISystem reference these instead of private duplicates.

namespace TheWaningBorder.Core.Config
{
    public static class BorderConstants
    {
        // ==================== Curse-as-a-force (design §2.5) ====================
        /// <summary>false = the curse fields NO army: no per-node attack "waves"
        /// or defenders (BorderArmyAISystem / BorderHordeSystem no-op). The curse
        /// is pure environmental pressure; wells are neutral objectives. Flip to
        /// true only to revive the old curse-faction army for debugging.</summary>
        public const bool CurseFieldsArmies = false;

        // ==================== Main Node ====================
        public const int MainNodeHP = 4000;
        public const float MainNodeRadius = 2.5f;
        public const float MainNodeSpreadRadius = 22f;  // bumped from 15
        public const float MainNodeSpreadPerTick = 1f;
        public const float MainNodeTickInterval = 45f;
        public const int MainNodeBuildCost = 2000;
        public const int MainNodePresentationID = 310;
        public const float MainNodeAttackRange = 18f;
        public const int MainNodeAttackDamage = 25;
        public const float MainNodeAttackCooldown = 1.2f;
        public const int MainNodeAttackMaxTargets = 3;
        /// <summary>LOS radius for the main node — small so the node "sees" its immediate area without revealing the whole map.</summary>
        public const float MainNodeLineOfSight = 8f;

        // ==================== Resource Node ====================
        public const int ResourceNodeHP = 200;
        public const float ResourceNodeRadius = 1.5f;
        public const float ResourceNodeSpreadRadius = 12f;  // bumped from 8
        public const float ResourceNodeSpreadPerTick = 1f;
        public const float ResourceNodeTickInterval = 30f;
        public const int ResourceNodeBuildCost = 150;
        public const int ResourceNodePresentationID = 312;

        // ==================== Enforcement Node ====================
        public const int EnforcementNodeHP = 600;
        public const float EnforcementNodeRadius = 1.5f;
        public const int EnforcementNodeBuildCost = 600;
        public const int EnforcementNodePresentationID = 313;
        public const float EnforcementAuraRadius = 20f;
        public const float EnforcementAuraDefBonus = 0.15f;
        public const float EnforcementAuraAttBonus = 0.15f;
        public const float EnforcementAuraSpeedBonus = 0.1f;

        // ==================== Suppression Node ====================
        public const int SuppressionNodeHP = 600;
        public const float SuppressionNodeRadius = 1.5f;
        public const int SuppressionNodeBuildCost = 600;
        public const int SuppressionNodePresentationID = 314;
        public const float SuppressionAuraRadius = 20f;
        public const float SuppressionAuraDefPenalty = 0.15f;
        public const float SuppressionAuraAttPenalty = 0.15f;
        public const float SuppressionAuraSpeedPenalty = 0.1f;

        // ==================== Restoration Node ====================
        public const int RestorationNodeHP = 400;
        public const float RestorationNodeRadius = 1.5f;
        public const int RestorationNodeBuildCost = 360;
        public const int RestorationNodePresentationID = 315;
        public const float RestorationAuraRadius = 15f;
        public const float RestorationAuraHealPerSecond = 5f;

        // ==================== Turret Node ====================
        public const int TurretNodeHP = 500;
        public const float TurretNodeRadius = 1.5f;
        public const int TurretNodeBuildCost = 300;
        public const int TurretNodePresentationID = 316;
        public const float TurretRange = 25f;
        public const int TurretDamage = 15;
        public const float TurretCooldown = 1.5f;
        public const int TurretMaxTargets = 2;

        // ==================== Crystalling (Unit) ====================
        public const float CrystallingHP = 72f;     // +20% from 60
        public const float CrystallingSpeed = 5.5f;
        public const float CrystallingDamage = 8f;
        public const float CrystallingLoS = 10f;
        public const float CrystallingAttackCooldown = 0.8f;
        public const float CrystallingRadius = 0.4f;
        public const int CrystallingBuildCost = 50;
        public const int CrystallingPresentationID = 320;

        // ==================== Veilstinger (Unit) ====================
        public const float VeilstingerHP = 78f;     // +20% from 65
        public const float VeilstingerSpeed = 4.0f;
        public const float VeilstingerDamage = 18f;
        public const float VeilstingerLoS = 28f;
        public const float VeilstingerMinRange = 8f;
        public const float VeilstingerMaxRange = 24f;
        public const float VeilstingerAimTime = 0.2f;
        public const float VeilstingerRadius = 0.5f;
        public const int VeilstingerBuildCost = 150;
        public const int VeilstingerPresentationID = 321;

        // ==================== Godsplinter (Unit) ====================
        // REBALANCED 2026-08-04 (playtest: "more damage/range/HP than
        // anything else and uncounterable"): the Godsplinter is now the
        // curse's MAGIC SIEGE TANK — Catapult-class numbers with a magic
        // flavor, durable but killable, outranged by nothing it can't be
        // countered by. Arcing single AOE shot, slow fire, slow walk.
        public const float GodsplinterHP = 420f;
        public const float GodsplinterSpeed = 1.8f;
        public const float GodsplinterDamage = 34f;
        public const float GodsplinterLoS = 30f;
        public const float GodsplinterRadius = 1.5f;
        public const float GodsplinterSiegeRange = 4f;
        public const float GodsplinterLaserRange = 26f;     // Catapult-band, not double it
        public const int   GodsplinterLaserMaxTargets = 1;  // single arcing AOE shot
        public const float GodsplinterAoeRadius = 5f;       // splash damage radius on impact
        public const float GodsplinterFireCooldown = 5.0f;  // slow cadence (was 2.0)
        public const float GodsplinterArcFraction = 0.3f;   // shot peaks at ~30 % of horizontal distance
        public const int GodsplinterBuildCost = 500;
        public const int GodsplinterPresentationID = 322;

        // ==================== AI Costs (BorderAISystem) ====================
        public const int AIResourceNodeCost = 360;
        public const int AITurretNodeCost = 600;
        public const int AIRestorationNodeCost = 750;
        public const int AIEnforcementNodeCost = 1200;
        public const int AISuppressionNodeCost = 1200;
        public const int AICrystallingCost = 50;
        public const int AIVeilstingerCost = 150;
        public const int AIGodsplinterCost = 500;
        public const int AIExpansionCost = 9000;

        // ==================== AI Train Times (seconds) ====================
        public const float CrystallingTrainTime = 8f;
        public const float VeilstingerTrainTime = 15f;
        public const float GodsplinterTrainTime = 30f;

        // ==================== AI Sub-Node Limits (per main node) ====================
        public const int MaxResourceNodesPerMain = 3;
        public const int MaxTurretNodesPerMain = 2;
        public const int MaxRestorationNodesPerMain = 1;
        public const int MaxEnforcementNodesPerMain = 1;
        public const int MaxSuppressionNodesPerMain = 1;
        public const int MaxSubNodesPerMain = 6;

        // ==================== Node State Machine (Spec §9, §11) ====================
        // Tunable timers — exposed early per spec §11. "The map wants to be Active":
        // every non-Active state reverts to Active when its timer expires.

        /// <summary>
        /// Curse & Shardroot canon (docs/Design/Curse_And_Shardroot.md §2.2):
        /// EVERY non-Active well state — Cleansed (Purified), Converted
        /// (Pacified) and Destroyed rubble — holds this long, refreshed for
        /// ALL of a player's holds whenever they apply their verb to another
        /// well (tempo rule, see BorderNodeStateHelper.SetState).
        /// </summary>
        public const float WellHoldTime = 600f;                // 10 min

        /// <summary>Purified (Cleansed) hold — reverts to Active on expiry
        /// (the 2026-07 "Cleansed is permanent" rule is superseded).</summary>
        public const float NodeCleansedRevertTime = WellHoldTime;

        /// <summary>Pacified (Converted) hold before reverting to Active.</summary>
        public const float NodeConvertedRevertTime = WellHoldTime;

        /// <summary>
        /// LEGACY single-phase regrow time. Superseded by the two-phase
        /// destruction cycle (NodeRubbleTime rubble → NodeRebuildTime build).
        /// Retained so any external reference still compiles.
        /// </summary>
        public const float NodeDestroyedRegrowTime = 540f;

        /// <summary>
        /// Destruction rework (2026-07): a destroyed main node leaves rubble
        /// and lies dormant this long before it starts rebuilding.
        /// </summary>
        public const float NodeRubbleTime = 540f;   // + NodeRebuildTime (60) = exactly 10 min to respawn (design 2026-08-05 rev.5)   // Destroyed hold (canon 10 min)

        /// <summary>Seconds a destroyed node spends rebuilding (rubble → Active).</summary>
        public const float NodeRebuildTime = 60f;   // reconstruction phase

        /// <summary>Grace seconds after all wells are simultaneously claimed
        /// before the domination win fires (canon: effectively instant — the
        /// grace only absorbs same-tick state churn).</summary>
        public const float NodeVictoryHoldTime = 5f;

        // ==================== Scholar (Alanthor ritualist) ====================
        public const float ScholarHP = 90f;
        public const float ScholarSpeed = 3.0f;
        public const float ScholarLoS = 14f;
        public const float ScholarRadius = 0.5f;
        public const int   ScholarPresentationID = 382;       // After sect-unique unit IDs (370-381)

        // ==================== Iconoclast (Feraldis node breaker, spec refinement #1) ====================
        // High-value, slow, hard-hitting unit gated to a Lv 3 Feraldis
        // Longhouse. Only damage source that can bring a Veilstone node to
        // Destroyed — every other attacker is refunded by NodeInvulnerabilitySystem.
        public const float IconoclastHP = 280f;
        public const float IconoclastSpeed = 3.2f;
        public const float IconoclastDamage = 0f;     // enabler, not damage dealer (aura strips node un-targetability)
        public const float IconoclastLoS = 16f;
        public const float IconoclastRadius = 0.7f;
        public const float IconoclastAttackRange = 1.8f;
        public const float IconoclastAttackCooldown = 1.6f;
        public const int   IconoclastPresentationID = 386;

        /// <summary>
        /// Iconoclast aura radius — within this distance of a veilstone node, the
        /// Iconoclast strips NodeUntargetable, allowing OTHER units (not the
        /// Iconoclast itself) to attack the node. Refinement v2 makes the
        /// Iconoclast an enabler, not a damage dealer.
        /// </summary>
        public const float IconoclastAuraRadius = 12f;

        // ==================== Acolyte (Runai ritualist) ====================
        // Same shape as Scholar — vulnerable caster, escort required. The
        // mechanical difficulty comes from RitualDefenseSystem's
        // RitualDefenseRunaiIntensity multiplier, not from the ritualist
        // itself being weaker.
        public const float AcolyteHP = 90f;
        public const float AcolyteSpeed = 3.0f;
        public const float AcolyteLoS = 14f;
        public const float AcolyteRadius = 0.5f;
        public const int   AcolytePresentationID = 384;

        // ==================== Runai Conversion ritual ====================
        /// <summary>
        /// Channel time for Runai conversion. Set slightly longer than
        /// Purification (35s) so the higher-intensity defense window is
        /// genuinely the spec's "node fights hardest" moment.
        /// </summary>
        public const float ConversionChannelTime = 45f;

        /// <summary>Radius around the converted node within which border defenders flip to Runai's faction.</summary>
        public const float ConversionFlipRadius = 16f;

        // ==================== Ritual (Spec §5, §11) ====================
        /// <summary>
        /// Seconds the ritualist must channel uninterrupted (spec §5.1:
        /// "significant channel time (suggested 30-60 seconds, tunable)"
        /// and §11 "ritual channel time" tunable). Lower end of the range
        /// while the system is new — bump if rituals feel too easy.
        /// </summary>
        public const float PurificationChannelTime = 35f;

        /// <summary>Distance the ritualist must be within to start channeling on a node.</summary>
        public const float RitualRange = 6f;

        /// <summary>
        /// Distance beyond which an in-progress channel is canceled (the
        /// ritualist was dragged off the node).
        ///
        /// RAISED 10 → 14 to match CorruptCancelRange, on the same measured
        /// evidence — see the note there. A channel that starts at the edge of
        /// RitualRange had only 4 m of tolerance, which incidental jostling
        /// around a contested well removes in seconds. Purification and
        /// Conversion channel for 35 s and 45 s respectively, so they are
        /// exposed for even longer than the Corruptor's 40 s.
        ///
        /// The counterplay is killing the ritualist, not bumping into one.
        /// </summary>
        public const float RitualCancelRange = 14f;

        // ==================== Glow Pickup (Spec §4.5) ====================
        /// <summary>Glow amount a successful Purification deposits into the pickup.</summary>
        public const int PurificationGlowYield = 10;

        /// <summary>Glow amount yielded by Feraldis Violent Extraction (slightly higher — destruction is permanent).</summary>
        public const int ViolentExtractionGlowYield = 12;

        /// <summary>Glow amount yielded by Runai Conversion (highest — node fights enslavement hardest).</summary>
        public const int ConversionGlowYield = 14;

        /// <summary>Border units in the final wave that erupts when Feraldis destroys a node (spec §5.3).</summary>
        public const int ViolentExtractionFinalWaveSize = 8;

        /// <summary>Spawn radius for the final wave around the destroyed node.</summary>
        public const float ViolentExtractionFinalWaveRadius = 5f;

        /// <summary>Pickup window before despawn (spec §4.5: 30-60s).</summary>
        public const float GlowPickupTimeout = 45f;

        /// <summary>Presentation ID for free-floating Glow pickups.</summary>
        public const int GlowPickupPresentationID = 383;

        // ==================== Glow Weapon Drop (spec §4.5) ====================

        /// <summary>Presentation ID for dropped Glow weapons.</summary>
        public const int GlowWeaponPresentationID = 385;

        /// <summary>Seconds before a dropped Glow weapon despawns if no one attunes.</summary>
        public const float GlowWeaponPickupTimeout = 45f;

        /// <summary>Distance within which a qualifying unit can attune to a dropped Glow weapon.</summary>
        public const float GlowWeaponClaimRadius = 1.5f;

        /// <summary>Seconds a qualifying unit must stand within radius (uninterrupted) to claim.</summary>
        public const float GlowWeaponAttunementTime = 5f;

        // ==================== God Powers (spec §6.2 + refinement #6) ====================
        // Cooldown-only (no Glow cost). cooldown = base × 0.8^stored_glow.
        // The base value is the cooldown with ZERO stored Glow; storing
        // Glow in the Temple compresses it asymptotically toward 0.

        /// <summary>Base cooldown (seconds at 0 stored Glow) for the generic god power.</summary>
        public const float GodPowerBaseCooldown = 90f;

        /// <summary>Per-Glow cooldown multiplier — each stored Glow multiplies remaining cooldown by this.</summary>
        public const float GodPowerCooldownPerGlow = 0.8f;

        /// <summary>AOE radius of the generic god power cast.</summary>
        public const float GodPowerRadius = 14f;

        /// <summary>Damage dealt to each non-caster unit/building inside the radius.</summary>
        public const int GodPowerDamage = 120;

        // ==================== Runaii Patrol Alert (spec §7.4) ====================

        /// <summary>Distance at which a hostile unit triggers a Runai patrol's controllable-when-threatened mode.</summary>
        public const float PatrolThreatRange = 12f;

        /// <summary>Seconds of "no hostile within range" required to drop a patrol back to autonomous.</summary>
        public const float PatrolAlertTimeout = 8f;

        // ==================== Ritual Defense (spec §5.1, §5.5) ====================
        // The node defends itself: while a ritual is being channeled, the
        // node spawns border units at the ritualist with rising frequency.
        // Spec §5.5: Runai conversion uses higher intensity than the other
        // rituals — the node fights enslavement harder than destruction.

        /// <summary>Spawn interval at the start of a ritual (slowest spawn rate).</summary>
        public const float RitualDefenseMaxInterval = 7f;

        /// <summary>Spawn interval at ritual completion (fastest spawn rate).</summary>
        public const float RitualDefenseMinInterval = 2f;

        /// <summary>Per-ritual cap on defenders so the cap on border pop isn't blown.</summary>
        public const int RitualDefenseMaxDefenders = 18;

        /// <summary>Spawn radius around the node where defenders appear.</summary>
        public const float RitualDefenseSpawnRadius = 4f;

        /// <summary>Multiplier applied to spawn rate for Runai conversion rituals.</summary>
        public const float RitualDefenseRunaiIntensity = 1.6f;

        // ==================== Glow Flow (spec §5.1, §6.3) ====================

        /// <summary>Distance (XZ) at which a unit can attune to a free Glow pickup.</summary>
        public const float GlowAutoPickupRadius = 1.5f;

        /// <summary>
        /// Seconds a unit must stand within GlowAutoPickupRadius (uninterrupted)
        /// to claim a Glow pickup (spec refinement #4 — was instant on touch).
        /// </summary>
        public const float GlowPickupAttunementTime = 20f;

        /// <summary>Distance (XZ) at which a Glow carrier auto-deposits at an owned reliquary.</summary>
        public const float GlowAutoDepositRadius = 3.0f;

        // ==================== Glow Reliquary ====================
        public const int    GlowReliquaryHP = 600;
        public const float  GlowReliquaryRadius = 1.6f;
        public const float  GlowReliquaryLoS = 14f;
        public const int    GlowReliquaryPresentationID = 522;

        /// <summary>Explosion radius when a reliquary holding glow is destroyed.</summary>
        public const float  GlowReliquaryExplodeRadius = 12f;

        /// <summary>Damage per stored Glow point dealt to non-owner units inside the blast radius.</summary>
        public const float  GlowReliquaryExplodeDamagePerGlow = 8f;
    }
}
