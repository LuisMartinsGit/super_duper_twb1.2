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
        public const float MainNodeSpreadRadius = 15f;
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
        public const float ResourceNodeSpreadRadius = 8f;
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
        public const float CrystallingHP = 60f;
        public const float CrystallingSpeed = 5.5f;
        public const float CrystallingDamage = 8f;
        public const float CrystallingLoS = 10f;
        public const float CrystallingAttackCooldown = 0.8f;
        public const float CrystallingRadius = 0.4f;
        public const int CrystallingBuildCost = 50;
        public const int CrystallingPresentationID = 320;

        // ==================== Veilstinger (Unit) ====================
        public const float VeilstingerHP = 65f;
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
        public const float GodsplinterHP = 1200f;
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
    }
}
