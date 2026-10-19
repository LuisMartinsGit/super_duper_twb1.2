// LitharchCleanse.cs
// The Litharch's CLEANSE (Territory_Claims.md §6.9.7, 2026-10-18): an aimed
// channel that pushes cursed ground back.
//
//   * the starting circle (the card's Radius — a 15 m diameter, 2026-10-19)
//     and its falloff band are cleared at once, on the cast's first tick;
//   * while the Litharch channels, the cleared circle GROWS at a constant
//     area a second (CleanseGround's value), so its radius slows as it widens
//     and never runs away with the square of the radius; it stops growing at
//     CleanseMaxRadius;
//   * outside the circle a falloff band (CleanseFalloff) is cleared with a
//     chance that drops to nothing across the band — CurseGroundSystem does
//     both, inside its own deterministic step;
//   * allies inside the STARTING circle heal CleanseHeal HP a second;
//   * the channel ends when the Litharch is attacked (an attacker lands a
//     hit — cursed-ground damage is no attack), is given any other order
//     (CommandHelper.ClearAllCommands, or a new move / attack), is stunned
//     or dies. The cooldown is held at full while it runs, so it counts from
//     the moment the channel ends.
//
// Sim time and replicated state only: the channel is a component on the
// Litharch, read by CurseGroundSystem on its step.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Abilities
{
    public static class LitharchCleanse
    {
        /// <summary>Is <paramref name="e"/> channelling a Cleanse?</summary>
        public static bool IsChanneling(EntityManager em, Entity e)
            => em.HasComponent<CleanseChannel>(e) && em.GetComponentData<CleanseChannel>(e).Active != 0;

        /// <summary>Start the channel (AbilityEffectExecutor, CleanseGround).</summary>
        public static void Begin(EntityManager em, Entity caster, AbilityCard card, float areaPerSecond)
        {
            if (!em.HasComponent<CleanseChannel>(caster) || !em.HasComponent<LocalTransform>(caster)) return;
            float3 here = em.GetComponentData<LocalTransform>(caster).Position;
            float3 aim = em.HasComponent<AbilityAimPoint>(caster)
                ? em.GetComponentData<AbilityAimPoint>(caster).Position : here;
            // Aimed beyond its reach: the circle lands at the edge of the reach,
            // toward the point.
            float2 d = aim.xz - here.xz;
            float dist = math.length(d);
            if (card.Range > 0f && dist > card.Range)
                aim = new float3(here.x + d.x / dist * card.Range, aim.y, here.z + d.y / dist * card.Range);

            // The Litharch stands still to channel: whatever it was doing ends,
            // and any order after this point is a new one.
            TheWaningBorder.Core.Commands.CommandHelper.ClearAllCommands(em, caster);
            TransientState.Clear<LastAttackerEntity>(em, caster);

            int idx = AbilityCatalog.IndexOf(card.Name);
            int slot = 0;
            if (em.HasComponent<UnitAbilities>(caster))
            {
                var ua = em.GetComponentData<UnitAbilities>(caster);
                for (int s = 0; s < 4; s++) if (ua.Get(s) == idx) { slot = s; break; }
            }
            float start = math.max(0f, card.Radius);
            em.SetComponentData(caster, new CleanseChannel
            {
                Active = 1,
                Center = aim,
                StartRadius = start,
                Radius = start,
                MaxRadius = math.max(start, card.EffectValue(AbilityEffectKind.CleanseMaxRadius, start)),
                AreaPerSecond = math.max(0f, areaPerSecond),
                Falloff = math.max(0f, card.EffectValue(AbilityEffectKind.CleanseFalloff, 0f)),
                HealPerSecond = math.max(0f, card.EffectValue(AbilityEffectKind.CleanseHeal, 0f)),
                Slot = slot,
                Cooldown = card.EffectiveCooldown,
                LastHealth = em.HasComponent<Health>(caster) ? em.GetComponentData<Health>(caster).Value : 0,
            });
            // Immediate (2026-10-19): the whole starting circle, and its soft
            // edge, is clean on the cast's first tick.
            TheWaningBorder.Systems.Border.CurseGroundSystem.ClearDisk(
                aim, start, math.max(0f, card.EffectValue(AbilityEffectKind.CleanseFalloff, 0f)));
        }

        /// <summary>End the channel, if one is running. The cooldown, held at
        /// full while it ran, now counts down.</summary>
        public static void Stop(EntityManager em, Entity e)
        {
            if (!IsChanneling(em, e)) return;
            var c = em.GetComponentData<CleanseChannel>(e);
            c.Active = 0;
            em.SetComponentData(e, c);
            SetCooldown(em, e, c.Slot, c.Cooldown);
        }

        internal static void SetCooldown(EntityManager em, Entity e, int slot, float cd)
        {
            if (!em.HasComponent<AbilityCooldowns>(e)) return;
            var c = em.GetComponentData<AbilityCooldowns>(e);
            switch (slot) { case 0: c.C0 = cd; break; case 1: c.C1 = cd; break; case 2: c.C2 = cd; break; default: c.C3 = cd; break; }
            em.SetComponentData(e, c);
        }
    }

    /// <summary>Ticks every running Cleanse: ends it when it breaks, grows the
    /// cleared circle, heals allies in the starting circle and holds the
    /// cooldown. Runs after the ability lifecycle (which starts channels) and
    /// before CurseGroundSystem (which clears the ground they cover).</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AbilityLifecycleSystem))]
    [UpdateBefore(typeof(TheWaningBorder.Systems.Border.CurseGroundSystem))]
    public partial class CleanseChannelSystem : SystemBase
    {
        static readonly ComponentType[] QT_Allies =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadWrite<Health>(),
        };
        static CachedEntityQuery QC_Allies;

        private EntityQuery _channels;

        protected override void OnCreate()
        {
            _channels = GetEntityQuery(ComponentType.ReadWrite<CleanseChannel>(),
                                       ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f || _channels.IsEmpty) return;
            var em = EntityManager;

            using var ents = _channels.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var c = em.GetComponentData<CleanseChannel>(e);
                if (c.Active == 0) continue;

                if (Broken(em, e, in c)) { LitharchCleanse.Stop(em, e); continue; }

                if (em.HasComponent<Health>(e)) c.LastHealth = em.GetComponentData<Health>(e).Value;
                c.Elapsed += dt;
                // Constant area a second: the circle's area grows linearly.
                float area = math.PI * c.StartRadius * c.StartRadius + c.AreaPerSecond * c.Elapsed;
                c.Radius = math.min(c.MaxRadius, math.sqrt(area / math.PI));

                if (c.HealPerSecond > 0f && c.StartRadius > 0f)
                {
                    c.HealAccum += c.HealPerSecond * dt;
                    int whole = (int)c.HealAccum;
                    if (whole > 0)
                    {
                        c.HealAccum -= whole;
                        HealAllies(em, e, c.Center, c.StartRadius, whole);
                    }
                }
                em.SetComponentData(e, c);
                LitharchCleanse.SetCooldown(em, e, c.Slot, c.Cooldown);
            }
        }

        /// <summary>Death, a stun, an attacker's hit, or a new order.</summary>
        private static bool Broken(EntityManager em, Entity e, in CleanseChannel c)
        {
            if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) return true;
            if (TransientState.Active<DeathAnimationState>(em, e)) return true;
            if (em.HasComponent<TheWaningBorder.Entities.Launched>(e)) return true;
            // An attacker landed a hit since the channel began (combat records
            // it on every hit; cursed-ground damage does not).
            if (TransientState.Active<LastAttackerEntity>(em, e)) return true;
            if (TransientState.Active<MoveCommand>(em, e))
            {
                if (c.HadMove == 0) return true;
                if (math.distancesq(em.GetComponentData<MoveCommand>(e).Destination, c.MoveDestination) > 0.01f) return true;
            }
            if (TransientState.Active<AttackCommand>(em, e))
            {
                if (c.HadAttack == 0) return true;
                if (em.GetComponentData<AttackCommand>(e).Target != c.AttackTarget) return true;
            }
            return false;
        }

        private static void HealAllies(EntityManager em, Entity caster, float3 center, float radius, int amount)
        {
            if (!em.HasComponent<FactionTag>(caster)) return;
            var faction = em.GetComponentData<FactionTag>(caster).Value;
            var q = QC_Allies.Get(em, QT_Allies);
            using var ents = q.ToEntityArray(Allocator.Temp);
            float r2 = radius * radius;
            for (int i = 0; i < ents.Length; i++)
            {
                var u = ents[i];
                var f = em.GetComponentData<FactionTag>(u).Value;
                if (f == Faction.Border || Alliances.AreHostile(faction, f)) continue;
                var p = em.GetComponentData<LocalTransform>(u).Position;
                if (math.distancesq(p.xz, center.xz) > r2) continue;
                var h = em.GetComponentData<Health>(u);
                if (h.Value <= 0 || h.Value >= h.Max) continue;
                h.Value = math.min(h.Max, h.Value + amount);
                em.SetComponentData(u, h);
            }
        }
    }
}
