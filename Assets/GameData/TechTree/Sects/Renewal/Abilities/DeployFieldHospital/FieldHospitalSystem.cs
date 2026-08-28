// FieldHospitalSystem.cs
// Runs the deployed Field Hospital: a 1 s heal pulse over allied units in
// radius, and the two-minute countdown to its own demolition.
//
// Heal loop follows ShrineHealSystem (cached queries in OnCreate, XZ distance,
// direct Health writes). Expiry follows the unit-death contract: set Health to
// 0 and let DeathSystem destroy the entity — never DestroyEntity from here.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FieldHospitalSystem : SystemBase
    {
        private const float TickInterval = 1f;

        // SimCadence, not a bare float — see SimCadence.cs. Same shape as
        // ShrineHealSystem: both the heal amount and the TimeToLive decrement
        // are scaled by the accumulator, so a machine-dependent starting phase
        // wrote different HP on the first fire rather than merely later.
        private SimCadence.Periodic _cadence;
        private EntityQuery _hospitalQuery;
        private EntityQuery _unitQuery;

        protected override void OnCreate()
        {
            _hospitalQuery = GetEntityQuery(
                ComponentType.ReadOnly<FieldHospitalTag>(),
                ComponentType.ReadWrite<FieldHospitalState>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            _unitQuery = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.Exclude<DeathAnimationState>());

            RequireForUpdate(_hospitalQuery);
        }

        protected override void OnUpdate()
        {
            // DueStep keeps the elapsed value the heal is scaled by, so the
            // arithmetic below is unchanged — what changes is that both peers
            // now reach this line on the same tick with the same accumulator.
            float tick = _cadence.DueStep(World.Time.DeltaTime, TickInterval);
            if (tick <= 0f) return;

            var em = EntityManager;
            using var hospitals = _hospitalQuery.ToEntityArray(Allocator.Temp);
            if (hospitals.Length == 0) return;

            using var units = _unitQuery.ToEntityArray(Allocator.Temp);
            using var unitFac = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitXf = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radSq = TheWaningBorder.Entities.FieldHospital.HealRadius
                        * TheWaningBorder.Entities.FieldHospital.HealRadius;
            int heal = (int)math.max(1f, TheWaningBorder.Entities.FieldHospital.HealPerSecond * tick);

            for (int h = 0; h < hospitals.Length; h++)
            {
                var hos = hospitals[h];

                // Countdown first: an expired hospital does not heal.
                var st = em.GetComponentData<FieldHospitalState>(hos);
                st.TimeToLive -= tick;
                em.SetComponentData(hos, st);

                if (st.TimeToLive <= 0f)
                {
                    if (em.HasComponent<Health>(hos))
                    {
                        var hp = em.GetComponentData<Health>(hos);
                        if (hp.Value > 0)
                        {
                            hp.Value = 0;
                            em.SetComponentData(hos, hp);
                        }
                    }
                    continue;
                }

                Faction fac = em.GetComponentData<FactionTag>(hos).Value;
                float3 pos = em.GetComponentData<LocalTransform>(hos).Position;

                for (int i = 0; i < units.Length; i++)
                {
                    // Field hospitals treat allies too. docs/Design/Teams.md
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
