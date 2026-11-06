// LitharchCleanse.cs
// The Litharch's two cleansing casts (Territory_Claims.md §6.9.7):
//
// CLEANSE (2026-10-22; placed like a building since 2026-11-06) plants a
// CLEANSING WARD on the build grid at the aimed site, which must lie within the
// card's reach. The ward clears the starting circle at once and then pulses
// every CleanseWardSystem.asset pulseSeconds, growing the clean circle by the
// card's CleanseGround value (m² a second, times the pulse length) up to its
// CleanseMaxRadius, with a CleanseFalloff band — until it is destroyed. The
// cooldown is charged by the lifecycle at the cast, as for any card; the
// Litharch is free the moment the ward stands.
//
// CLEANSE PULSE (2026-11-06) clears the card's whole Radius in one burst, with
// its CleanseFalloff band, and leaves nothing behind. It lives on a card of its
// own, beside this one, in Abilities/CleansePulse/.
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
            float3 site = TheWaningBorder.Entities.CleanseWard.SiteFor(aim);

            var faction = em.GetComponentData<FactionTag>(caster).Value;
            // The ghost's own test, run again on every peer: a refused cast
            // costs nothing — the cooldown the lifecycle just charged is given
            // back. A cell of slack on the reach, because the Litharch may have
            // taken a step between the click and this tick.
            string why = TheWaningBorder.Entities.CleanseWard.SiteRefusal(em, caster, card, site, BuildGrid.CellSize);
            if (why != null)
            {
                AbilityCooldownUtil.Refund(em, caster, card);
                if (faction == GameSettings.LocalPlayerFaction) TheWaningBorder.Core.SimSignals.Notify(why);
                return;
            }

            var cfg = TheWaningBorder.Systems.Border.CleanseWardSystemConfig.I;
            float pulse = cfg != null ? math.max(0.25f, cfg.pulseSeconds) : 2f;
            float start = math.max(0f, card.Radius);
            TheWaningBorder.Entities.CleanseWard.Create(em, site, faction,
                start,
                math.max(start, card.EffectValue(AbilityEffectKind.CleanseMaxRadius, start)),
                math.max(0f, areaPerSecond) * pulse,
                math.max(0f, card.EffectValue(AbilityEffectKind.CleanseFalloff, 0f)));
        }

        /// <summary>The one burst (AbilityEffectExecutor, CleansePulse). Aimed
        /// beyond its reach it lands at the edge of the reach, toward the
        /// point.</summary>
        public static void Pulse(EntityManager em, Entity caster, AbilityCard card)
        {
            if (!em.HasComponent<LocalTransform>(caster)) return;
            float3 here = em.GetComponentData<LocalTransform>(caster).Position;
            float3 aim = em.HasComponent<AbilityAimPoint>(caster)
                ? em.GetComponentData<AbilityAimPoint>(caster).Position : here;
            float2 d = aim.xz - here.xz;
            float dist = math.length(d);
            if (card.Range > 0f && dist > card.Range)
                aim = new float3(here.x + d.x / dist * card.Range, aim.y, here.z + d.y / dist * card.Range);
            aim.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(aim.x, aim.z);

            float radius = math.max(0f, card.Radius);
            TheWaningBorder.Systems.Border.CurseGroundSystem.ClearDisk(aim, radius,
                math.max(0f, card.EffectValue(AbilityEffectKind.CleanseFalloff, 0f)));
            TheWaningBorder.Rendering.CleansePulseSignals.Post(aim, radius);
        }
    }
}
