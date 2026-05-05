// GlowAbilityCommand.cs
// Activates a Lv 5 unit's Glow Ability — fast HP regen burst for 6 seconds
// (60s cooldown). The +30% attack-rate half of the spec is partial: it's
// folded into a SpellBuff multiplier here, but the attack-cooldown read
// path doesn't yet honor that field, so the speed half lands in Phase 5
// polish when MeleeCombatSystem / RangedCombatSystem wire the cooldown
// reduction. The HP-regen half is fully effective via UnitRankSystem.
//
// Audit fix #1.
//
// Location: Assets/Scripts/Core/Commands/CommandTypes/GlowAbilityCommand.cs

using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Core.Commands.Types
{
    public enum GlowAbilityResult
    {
        Ok = 0,
        NotMaxRank,
        OnCooldown,
        AlreadyActive,
        Invalid,
    }

    public static class GlowAbilityCommandHelper
    {
        public static GlowAbilityResult Execute(EntityManager em, Entity unit)
        {
            if (!em.Exists(unit)) return GlowAbilityResult.Invalid;
            if (!em.HasComponent<UnitRank>(unit)) return GlowAbilityResult.NotMaxRank;

            var rank = em.GetComponentData<UnitRank>(unit).Value;
            if (rank < UnitRankConfig.MaxRank) return GlowAbilityResult.NotMaxRank;

            GlowAbilityState state = default;
            if (em.HasComponent<GlowAbilityState>(unit))
                state = em.GetComponentData<GlowAbilityState>(unit);

            if (state.ActiveRemaining > 0f) return GlowAbilityResult.AlreadyActive;
            if (state.CooldownRemaining > 0f) return GlowAbilityResult.OnCooldown;

            state.ActiveRemaining   = UnitRankConfig.GlowAbilityActiveDuration;
            state.CooldownRemaining = UnitRankConfig.GlowAbilityCooldown;
            if (em.HasComponent<GlowAbilityState>(unit))
                em.SetComponentData(unit, state);
            else
                em.AddComponentData(unit, state);

            // Mirror the burst into SpellBuff so combat-pipeline readers
            // (DamageMultiplier proxy for +attack-rate) see the boost. The
            // proper attack-cooldown reduction is Phase 5 polish.
            var buff = new SpellBuff
            {
                DamageMultiplier = 1.30f,
                TimeRemaining = UnitRankConfig.GlowAbilityActiveDuration,
            };
            using var ecb = new Unity.Entities.EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            TheWaningBorder.Systems.Combat.CombatDamageHelper.MergeSpellBuff(em, ecb, unit, buff);
            ecb.Playback(em);
            return GlowAbilityResult.Ok;
        }
    }
}
