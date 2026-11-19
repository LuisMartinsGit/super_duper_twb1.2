// HealingWardSystem.cs
// Runs the Healing Ward (docs/Design/Sects.md, Renewal, 2026-11-18):
//
// 1. (Until 2026-11-18 a SHARED cooldown lived here, copied onto every
//    Litharch; each Litharch now keeps the card's own cooldown — developer:
//    "the cooldown can be per litharch".)
// 2. THE PULSE. Every HealingWardSystem.asset pulseSeconds each standing ward
//    heals every wounded allied unit inside the card's Radius by healPerPulse
//    — the Cleansing Ward's shape (CleanseWardSystem). The presentation keeps
//    a healing aura looping on it (RenewalVfx), as the Cleansing Ward wears
//    its blue one.
//
// Sim time and replicated state only (SimCadence.Periodic for the pulse).

using TheWaningBorder.Abilities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AbilityLifecycleSystem))]
    public partial class HealingWardSystem : SystemBase
    {
        // SimCadence, not a bare float — see SimCadence.cs: every peer reaches
        // the pulse on the same tick with the same accumulator.
        private SimCadence.Periodic _cadence;
        private EntityQuery _wardQuery;
        private EntityQuery _unitQuery;

        protected override void OnCreate()
        {
            _wardQuery = GetEntityQuery(
                ComponentType.ReadOnly<HealingWardTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LocalTransform>());

            _unitQuery = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.Exclude<DeathAnimationState>());

            RequireForUpdate(_wardQuery);
        }

        protected override void OnUpdate()
        {
            float dt = World.Time.DeltaTime;
            PulseWards(dt);
        }

        // ── 2. The pulse ────────────────────────────────────────────────────

        private void PulseWards(float dt)
        {
            if (_wardQuery.IsEmpty) return;
            var cfg = HealingWardSystemConfig.I;
            if (cfg == null) return;
            float pulse = math.max(0.25f, cfg.pulseSeconds);
            float tick = _cadence.DueStep(dt, pulse);
            if (tick <= 0f) return;

            var card = AbilityCatalog.Get(TheWaningBorder.Entities.HealingWard.CardName);
            if (card == null) return;
            float radSq = card.Radius * card.Radius;
            int heal = math.max(0, cfg.healPerPulse);

            var em = EntityManager;
            using var wards = _wardQuery.ToEntityArray(Allocator.Temp);
            using var units = _unitQuery.ToEntityArray(Allocator.Temp);
            using var unitFac = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitXf = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int w = 0; w < wards.Length; w++)
            {
                var ward = wards[w];
                if (em.GetComponentData<Health>(ward).Value <= 0) continue;

                Faction fac = em.GetComponentData<FactionTag>(ward).Value;
                float3 pos = em.GetComponentData<LocalTransform>(ward).Position;

                for (int i = 0; i < units.Length; i++)
                {
                    // Wards treat allies too. docs/Design/Teams.md
                    if (!Alliances.AreAllied(fac, unitFac[i].Value)) continue;

                    float2 d = new float2(unitXf[i].Position.x - pos.x, unitXf[i].Position.z - pos.z);
                    if (math.dot(d, d) > radSq) continue;

                    var hp = em.GetComponentData<Health>(units[i]);
                    if (hp.Value <= 0 || hp.Value >= hp.Max) continue;

                    hp.Value = (int)math.min(hp.Max, hp.Value + heal);
                    em.SetComponentData(units[i], hp);
                }
            }
        }
    }
}
