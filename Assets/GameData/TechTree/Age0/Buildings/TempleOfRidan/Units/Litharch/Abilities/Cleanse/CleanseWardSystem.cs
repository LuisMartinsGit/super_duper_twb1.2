// CleanseWardSystem.cs
// Pulses every standing Cleansing Ward (Territory_Claims.md §6.9.7,
// 2026-10-22): each pulseSeconds the ward's circle grows by its area a pulse
// (to its maximum) and the cursed ground inside it, and in its falloff band,
// is cleared at once — the curse retreats in bursts, in step with the aura
// the presentation plays on the same pulse count (CleanseWardVfx).
//
// Sim time and replicated state only (SimCadence.Periodic, the same shape as
// FieldHospitalSystem). A ward at 0 HP is DeathSystem's to remove.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CurseGroundSystem))]
    public partial class CleanseWardSystem : SystemBase
    {
        private SimCadence.Periodic _cadence;
        private EntityQuery _wards;

        protected override void OnCreate()
        {
            _wards = GetEntityQuery(
                ComponentType.ReadOnly<CleanseWardTag>(),
                ComponentType.ReadWrite<CleanseWardState>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LocalTransform>());
            RequireForUpdate(_wards);
        }

        protected override void OnUpdate()
        {
            var cfg = CleanseWardSystemConfig.I;
            if (cfg == null) return;
            float pulse = math.max(0.25f, cfg.pulseSeconds);
            float tick = _cadence.DueStep(World.Time.DeltaTime, pulse);
            if (tick <= 0f) return;

            var em = EntityManager;
            using var ents = _wards.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var w = ents[i];
                if (em.GetComponentData<Health>(w).Value <= 0) continue;
                var st = em.GetComponentData<CleanseWardState>(w);
                var pos = em.GetComponentData<LocalTransform>(w).Position;

                // Constant AREA a pulse: the radius slows as the circle widens
                // instead of running away with the square of the radius.
                float area = math.PI * st.Radius * st.Radius + st.AreaPerPulse;
                st.Radius = math.min(st.MaxRadius, math.sqrt(area / math.PI));
                st.PulseCount++;
                em.SetComponentData(w, st);

                CurseGroundSystem.ClearDisk(pos, st.Radius, st.Falloff);
            }
        }
    }
}
