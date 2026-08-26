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

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectSilenceVigilSystem : ISystem
    {
        // Per-level tuning: warm-up shrinks and the cap grows with the lever.
        private static void GetTuning(byte level, out float warmup, out float ramp, out int cap)
        {
            switch (level)
            {
                case 2:  warmup = 1.0f; ramp = 3.0f; cap = 10; return;
                case 3:  warmup = 0.5f; ramp = 2.5f; cap = 15; return;
                default: warmup = 2.0f; ramp = 4.0f; cap = 6;  return;
            }
        }

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
            // them the state simply never gets stamped. First-time stamps
            // (AddComponentData = structural change) are COLLECTED and
            // applied after the iteration; in-place updates stay inline.
            var newStates = new NativeList<Entity>(8, Allocator.Temp);
            var newArmor = new NativeList<Entity>(8, Allocator.Temp);
            var newArmorBonus = new NativeList<int>(8, Allocator.Temp);

            foreach (var (faction, entity) in SystemAPI
                .Query<RefRO<FactionTag>>()
                .WithAll<HoldPositionTag, UnitTag>()
                .WithEntityAccess())
            {
                byte level = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    SectConfig.Silence, SectLeverKind.Passive);
                if (level == 0) continue;
                GetTuning(level, out float warmup, out float ramp, out int cap);

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
                    newStates.Add(entity);
                    t = dt;
                }

                int bonus;
                if (t < warmup) bonus = 0;
                else if (t >= warmup + ramp) bonus = cap;
                else
                {
                    float fraction = (t - warmup) / ramp;
                    bonus = (int)(fraction * cap);
                }

                if (em.HasComponent<SilenceVigilArmor>(entity))
                {
                    em.SetComponentData(entity, new SilenceVigilArmor { Bonus = bonus });
                }
                else if (bonus > 0)
                {
                    newArmor.Add(entity);
                    newArmorBonus.Add(bonus);
                }
            }

            for (int i = 0; i < newStates.Length; i++)
            {
                if (em.Exists(newStates[i]) && !em.HasComponent<SilenceVigilState>(newStates[i]))
                    em.AddComponentData(newStates[i], new SilenceVigilState { TimeInStance = dt });
            }
            for (int i = 0; i < newArmor.Length; i++)
            {
                if (em.Exists(newArmor[i]) && !em.HasComponent<SilenceVigilArmor>(newArmor[i]))
                    em.AddComponentData(newArmor[i], new SilenceVigilArmor { Bonus = newArmorBonus[i] });
            }
            newStates.Dispose();
            newArmor.Dispose();
            newArmorBonus.Dispose();

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
