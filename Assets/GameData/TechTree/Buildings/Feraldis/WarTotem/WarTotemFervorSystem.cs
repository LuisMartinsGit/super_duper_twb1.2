// The War Totem drinks the blood pool it was planted on.
// Canon: docs/Design/Age_1_Feraldis.md — "Blood, Frenzy & War Totems".
//
// Why DRINK rather than simply sit on the pool: blood inside any player's
// influence fades (BloodMap.DecayInsideInfluence, §2.5b rev.3). A totem
// that projected influence over its own pool would erase its own fuel. So
// each pulse it consumes a slice of the surrounding blood and banks it as
// permanent Fervor — which is also precisely §2.6's "feedable (more nearby
// blood -> stronger), non-decaying, killable" anchor.
//
// The Fervor value is consumed by InfluenceMapSystem.DepositWarTotems,
// which scales both deposit rate and radius by it.
//
// BloodMap is managed main-thread state, so this is a SystemBase on the
// totems' own slow pulse.

using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Influence;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.World
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WarTotemFervorSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<WarTotemTag>();
        }

        protected override void OnUpdate()
        {
            if (!BloodMap.Ready) return;
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (fervor, transform) in SystemAPI
                .Query<RefRW<TotemFervor>, RefRO<LocalTransform>>()
                .WithAll<WarTotemTag>()
                .WithNone<UnderConstruction>())
            {
                ref var f = ref fervor.ValueRW;
                f.DrinkTimer -= dt;
                if (f.DrinkTimer > 0f) continue;
                f.DrinkTimer = TotemDrinkInterval;

                if (f.Value >= TotemFervorMax) continue;

                var p = transform.ValueRO.Position;

                // One call both takes the slice and reports the MEAN blood
                // (0..1) that was there — mean, not sum, so a totem behaves
                // the same on a small map as on a large one.
                float mean = BloodMap.Consume(p.x, p.z, TotemDrinkRadius, TotemDrinkFraction);
                if (mean <= 0f) continue;

                f.Value = UnityEngine.Mathf.Min(TotemFervorMax,
                    f.Value + mean * TotemFervorPerPulse);
            }
        }
    }
}
