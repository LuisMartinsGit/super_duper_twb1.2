// SectSilenceVigilSystem.cs
// Implements Silence's Lv I "Steadfast Vigil" passive: a unit holding
// position (HoldPositionTag) belonging to a Silence-adopted faction
// accumulates a defense bonus that ramps over time. The bonus is exposed
// via the SilenceVigilArmor component, which CombatDamageHelper.GetSpellBuffArmorBonus
// folds into the matrix damage formula on the defender side.
//
// Ramp (Lv I): 0..2s held = 0 armor, 2..6s = linear 0 -> +6, 6s+ = +6.
// Phase 4 raises the cap and shortens the warm-up window.
//
// On HoldPosition release the system removes both SilenceVigilState and
// SilenceVigilArmor on the same frame so the bonus drops the instant
// the unit moves — preventing a "carry the buff out of the stance" leak
// that would happen if we mirrored the bonus into a SpellBuff with a
// non-zero TimeRemaining.
//
// task-063 phase 2e.
//
// Location: Assets/Scripts/Systems/Sect/SectSilenceVigilSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectSilenceVigilSystem : ISystem
    {
        // Lv I tuning. Phase 4: faster warm-up + higher cap for Lv II / Lv III.
        private const float WarmupSeconds = 2f;
        private const float RampSeconds   = 4f;   // ramp window AFTER warm-up
        private const int   ArmorCap      = 6;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HoldPositionTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Ramp: tick TimeInStance, then write the matching armor bump.
            // Skip units whose faction doesn't have Silence adopted — for
            // them the state simply never gets stamped.
            foreach (var (faction, entity) in SystemAPI
                .Query<RefRO<FactionTag>>()
                .WithAll<HoldPositionTag, UnitTag>()
                .WithEntityAccess())
            {
                if (!SectQuery.IsAdoptedAtLeast(em, faction.ValueRO.Value,
                        SectConfig.Silence, SectLeverKind.Passive)) continue;

                float t;
                if (em.HasComponent<SilenceVigilState>(entity))
                {
                    var st = em.GetComponentData<SilenceVigilState>(entity);
                    st.TimeInStance += dt;
                    em.SetComponentData(entity, st);
                    t = st.TimeInStance;
                }
                else
                {
                    em.AddComponentData(entity, new SilenceVigilState { TimeInStance = dt });
                    t = dt;
                }

                int bonus;
                if (t < WarmupSeconds) bonus = 0;
                else if (t >= WarmupSeconds + RampSeconds) bonus = ArmorCap;
                else
                {
                    float fraction = (t - WarmupSeconds) / RampSeconds;
                    bonus = (int)(fraction * ArmorCap);
                }

                if (em.HasComponent<SilenceVigilArmor>(entity))
                    em.SetComponentData(entity, new SilenceVigilArmor { Bonus = bonus });
                else if (bonus > 0)
                    em.AddComponentData(entity, new SilenceVigilArmor { Bonus = bonus });
            }

            // Sweep: any unit that has SilenceVigilState/Armor but is no
            // longer holding position must lose both immediately.
            var toClear = new NativeList<Entity>(8, Allocator.Temp);
            foreach (var (st, entity) in SystemAPI
                .Query<RefRO<SilenceVigilState>>()
                .WithNone<HoldPositionTag>()
                .WithEntityAccess())
            {
                toClear.Add(entity);
            }
            for (int i = 0; i < toClear.Length; i++)
            {
                var e = toClear[i];
                if (!em.Exists(e)) continue;
                if (em.HasComponent<SilenceVigilState>(e)) em.RemoveComponent<SilenceVigilState>(e);
                if (em.HasComponent<SilenceVigilArmor>(e)) em.RemoveComponent<SilenceVigilArmor>(e);
            }
            toClear.Dispose();
        }
    }
}
