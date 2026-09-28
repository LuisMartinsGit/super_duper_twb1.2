// ReliquaryHelper.cs
// Fire-side helper for the Reliquary's three building actives (Scry, Lockout,
// Vision). Split out of SectAntiquityMechanics.cs on 2026-09-27 so its numbers
// could move to ReliquaryHelper.asset beside it (the component-config rule in
// CLAUDE.md: a config asset sits beside <Component>.cs).

using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    /// <summary>
    /// Fire-side helper for the Reliquary's three abilities. Level gating
    /// per the spec: Lv I = Scry only; Lv II = all three; Lv III = base
    /// cooldowns -30%.
    /// </summary>
    public static class ReliquaryHelper
    {
        // The numbers live in ReliquaryHelper.asset beside this file
        // (ReliquaryHelperConfig). They sit on the spell ladder
        // (docs/Design/Spells.md 8-9): the building-active band (90 s) and the
        // canon radii. The properties keep every call site unchanged.
        static ReliquaryHelperConfig _cfg;
        static ReliquaryHelperConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<ReliquaryHelperConfig>());

        public static float ScryBaseCooldown    => Cfg.scryCooldown;
        public static float LockoutBaseCooldown => Cfg.lockoutCooldown;
        public static float VisionBaseCooldown  => Cfg.visionCooldown;

        public static float ScryRadius      => Cfg.scryRadius;
        public static float ScryDuration    => Cfg.scryDuration;
        public static float LockoutRadius   => Cfg.lockoutRadius;
        public static float LockoutDuration => Cfg.lockoutDuration;
        public static float VisionRadius    => Cfg.visionRadius;
        public static float VisionDuration  => Cfg.visionDuration;

        public static bool AbilityUnlocked(EntityManager em, Faction faction, int ability)
        {
            byte lv = SectQuery.LevelOf(em, faction, SectConfig.Antiquity, SectLeverKind.Building);
            if (lv == 0) lv = 1;                 // owning a Reliquary implies Lv I
            return ability == 0 || lv >= 2;      // 0 = Scry always; Lockout/Vision need Lv II
        }

        private static float CooldownFor(EntityManager em, Faction faction, float baseCd)
        {
            byte lv = SectQuery.LevelOf(em, faction, SectConfig.Antiquity, SectLeverKind.Building);
            return lv >= 3 ? baseCd * Cfg.level3CooldownScale : baseCd;
        }

        /// <summary>Fire ability 0=Scry (ground target), 1=Lockout (ground
        /// target), 2=Vision (self). Returns false when on cooldown/locked.</summary>
        public static bool Fire(EntityManager em, Entity reliquary, int ability, float3 target)
        {
            if (!em.Exists(reliquary) || !em.HasComponent<ReliquaryState>(reliquary)) return false;
            if (!em.HasComponent<FactionTag>(reliquary)) return false;
            var faction = em.GetComponentData<FactionTag>(reliquary).Value;
            if (!AbilityUnlocked(em, faction, ability)) return false;

            var s = em.GetComponentData<ReliquaryState>(reliquary);
            switch (ability)
            {
                case 0:
                    if (s.ScryCooldown > 0f) return false;
                    SectActivePowerHelper.SpawnReveal(em, faction, target, ScryRadius, ScryDuration);
                    s.ScryCooldown = CooldownFor(em, faction, ScryBaseCooldown);
                    break;
                case 1:
                    if (s.LockoutCooldown > 0f) return false;
                    SectActivePowerHelper.ApplyCooldownFreeze(em, faction, target,
                        LockoutRadius, LockoutDuration, surge: false);
                    s.LockoutCooldown = CooldownFor(em, faction, LockoutBaseCooldown);
                    break;
                case 2:
                    if (s.VisionCooldown > 0f) return false;
                    float3 selfPos = em.GetComponentData<LocalTransform>(reliquary).Position;
                    SectActivePowerHelper.SpawnReveal(em, faction, selfPos, VisionRadius, VisionDuration);
                    s.VisionCooldown = CooldownFor(em, faction, VisionBaseCooldown);
                    break;
                default:
                    return false;
            }
            em.SetComponentData(reliquary, s);
            return true;
        }
    }
}
