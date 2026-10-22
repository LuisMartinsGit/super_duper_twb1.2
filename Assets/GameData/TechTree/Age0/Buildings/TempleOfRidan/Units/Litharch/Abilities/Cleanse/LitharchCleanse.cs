// LitharchCleanse.cs
// The Litharch's CLEANSE (Territory_Claims.md §6.9.7, 2026-10-22): an aimed
// cast that PLANTS A CLEANSING WARD at the aim point (clamped to the card's
// reach). The ward clears the starting circle at once and then pulses every
// CleanseWardSystem.asset pulseSeconds, growing the clean circle by the
// card's CleanseGround value (m² a second, times the pulse length) up to its
// CleanseMaxRadius, with a CleanseFalloff band — until it is destroyed. The
// cooldown is charged by the lifecycle at the cast, as for any card; the
// Litharch is free the moment the ward stands.
//
// Sim time and replicated state only.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Abilities
{
    public static class LitharchCleanse
    {
        /// <summary>Plant the ward (AbilityEffectExecutor, CleanseGround).
        /// <paramref name="areaPerSecond"/> is the effect's value.</summary>
        public static void Begin(EntityManager em, Entity caster, AbilityCard card, float areaPerSecond)
        {
            if (!em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return;
            float3 here = em.GetComponentData<LocalTransform>(caster).Position;
            float3 aim = em.HasComponent<AbilityAimPoint>(caster)
                ? em.GetComponentData<AbilityAimPoint>(caster).Position : here;
            // Aimed beyond its reach: the ward lands at the edge of the reach,
            // toward the point.
            float2 d = aim.xz - here.xz;
            float dist = math.length(d);
            if (card.Range > 0f && dist > card.Range)
                aim = new float3(here.x + d.x / dist * card.Range, aim.y, here.z + d.y / dist * card.Range);
            aim.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(aim.x, aim.z);

            var faction = em.GetComponentData<FactionTag>(caster).Value;
            // The team's cap and spacing (CleanseWardSystem.asset): a refused
            // cast costs nothing — the cooldown the lifecycle just charged is
            // given back.
            if (!TheWaningBorder.Entities.CleanseWard.CanPlant(em, faction, aim, out string why))
            {
                AbilityCooldownUtil.Refund(em, caster, card);
                if (faction == GameSettings.LocalPlayerFaction) TheWaningBorder.Core.SimSignals.Notify(why);
                return;
            }

            var cfg = TheWaningBorder.Systems.Border.CleanseWardSystemConfig.I;
            float pulse = cfg != null ? math.max(0.25f, cfg.pulseSeconds) : 2f;
            float start = math.max(0f, card.Radius);
            TheWaningBorder.Entities.CleanseWard.Create(em, aim, faction,
                start,
                math.max(start, card.EffectValue(AbilityEffectKind.CleanseMaxRadius, start)),
                math.max(0f, areaPerSecond) * pulse,
                math.max(0f, card.EffectValue(AbilityEffectKind.CleanseFalloff, 0f)));
        }
    }
}
