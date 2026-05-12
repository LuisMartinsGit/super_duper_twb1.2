// File: Assets/Scripts/Core/Settings/CrystalConstants.cs
// Centralised constants for all Crystal faction entities and AI.
// Factories and CrystalAISystem reference these instead of private duplicates.

namespace TheWaningBorder.Core.Config
{
    public static class CrystalConstants
    {
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
        public const float GodsplinterHP = 1440f;   // +20% from 1200
        public const float GodsplinterSpeed = 1.8f;
        public const float GodsplinterDamage = 40f;
        public const float GodsplinterLoS = 20f;
        public const float GodsplinterRadius = 1.5f;
        public const float GodsplinterSiegeRange = 4f;
        public const float GodsplinterLaserRange = 22f;
        public const int GodsplinterLaserMaxTargets = 4;
        public const int GodsplinterBuildCost = 500;
        public const int GodsplinterPresentationID = 322;

        // ==================== AI Costs (CrystalAISystem) ====================
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

        /// <summary>Seconds a Cleansed node persists before reverting to Active.</summary>
        public const float NodeCleansedRevertTime = 300f;     // 5 min

        /// <summary>Seconds a Converted node persists before reverting to Active.</summary>
        public const float NodeConvertedRevertTime = 300f;    // 5 min

        /// <summary>Seconds a Destroyed node remains dormant before regrowing to Active.</summary>
        public const float NodeDestroyedRegrowTime = 540f;    // 9 min (spec: 8-10 min)

        /// <summary>Seconds a culture must hold all-claimed map to trigger node victory.</summary>
        public const float NodeVictoryHoldTime = 300f;        // 5 min

        // ==================== Scholar (Alanthor ritualist) ====================
        public const float ScholarHP = 90f;
        public const float ScholarSpeed = 3.0f;
        public const float ScholarLoS = 14f;
        public const float ScholarRadius = 0.5f;
        public const int   ScholarPresentationID = 382;       // After sect-unique unit IDs (370-381)

        // ==================== Iconoclast (Feraldis node breaker, spec refinement #1) ====================
        // High-value, slow, hard-hitting unit gated to a Lv 3 Feraldis
        // Longhouse. Only damage source that can bring a Crystal node to
        // Destroyed — every other attacker is refunded by NodeInvulnerabilitySystem.
        public const float IconoclastHP = 280f;
        public const float IconoclastSpeed = 3.2f;
        public const float IconoclastDamage = 25f;
        public const float IconoclastLoS = 16f;
        public const float IconoclastRadius = 0.7f;
        public const float IconoclastAttackRange = 1.8f;
        public const float IconoclastAttackCooldown = 1.6f;
        public const int   IconoclastPresentationID = 386;

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

        /// <summary>Radius around the converted node within which curse defenders flip to Runai's faction.</summary>
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

        /// <summary>Distance beyond which an in-progress channel is canceled (scholar wandered too far).</summary>
        public const float RitualCancelRange = 10f;

        // ==================== Glow Pickup (Spec §4.5) ====================
        /// <summary>Glow amount a successful Purification deposits into the pickup.</summary>
        public const int PurificationGlowYield = 10;

        /// <summary>Glow amount yielded by Feraldis Violent Extraction (slightly higher — destruction is permanent).</summary>
        public const int ViolentExtractionGlowYield = 12;

        /// <summary>Glow amount yielded by Runai Conversion (highest — node fights enslavement hardest).</summary>
        public const int ConversionGlowYield = 14;

        /// <summary>Curse units in the final wave that erupts when Feraldis destroys a node (spec §5.3).</summary>
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

        // ==================== Runaii Patrol Alert (spec §7.4) ====================

        /// <summary>Distance at which a hostile unit triggers a Runai patrol's controllable-when-threatened mode.</summary>
        public const float PatrolThreatRange = 12f;

        /// <summary>Seconds of "no hostile within range" required to drop a patrol back to autonomous.</summary>
        public const float PatrolAlertTimeout = 8f;

        // ==================== Ritual Defense (spec §5.1, §5.5) ====================
        // The node defends itself: while a ritual is being channeled, the
        // node spawns curse units at the ritualist with rising frequency.
        // Spec §5.5: Runai conversion uses higher intensity than the other
        // rituals — the node fights enslavement harder than destruction.

        /// <summary>Spawn interval at the start of a ritual (slowest spawn rate).</summary>
        public const float RitualDefenseMaxInterval = 7f;

        /// <summary>Spawn interval at ritual completion (fastest spawn rate).</summary>
        public const float RitualDefenseMinInterval = 2f;

        /// <summary>Per-ritual cap on defenders so the cap on curse pop isn't blown.</summary>
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
