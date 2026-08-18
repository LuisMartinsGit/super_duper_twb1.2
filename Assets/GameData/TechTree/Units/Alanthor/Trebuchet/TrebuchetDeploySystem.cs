// TrebuchetDeploySystem.cs
// Drives the Trebuchet pack/unpack cycle.
// Location: Assets/GameData/TechTree/Units/Alanthor/Trebuchet/TrebuchetDeploySystem.cs

using Unity.Burst;
using Unity.Entities;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Deterministic pack/unpack driver for TrebuchetState holders:
    /// - moving (an active DesiredDestination, Has != 0) packs the engine
    ///   instantly: Deployed = 0, Timer = 0;
    /// - standing with a live Target accumulates Timer; at DeployTime (3 s)
    ///   Deployed flips to 1.
    /// RangedCombatSystem's fire path skips shooters with Deployed == 0, so
    /// an undeployed trebuchet plants and faces but never looses a stone.
    /// A deployed engine STAYS deployed while stationary (even between
    /// targets) — only movement packs it again. No randomness, no ECB:
    /// direct state writes only.
    ///
    /// Ordering: after TargetingSystem (fresh Target / DesiredDestination
    /// intent), before RangedCombatSystem (which reads Deployed this frame).
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    [UpdateBefore(typeof(RangedCombatSystem))]
    public partial struct TrebuchetDeploySystem : ISystem
    {
        /// <summary>Seconds of standing with a live target before the engine may fire.</summary>
        public const float DeployTime = 3f;

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TrebuchetState>();
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            foreach (var (trebState, target, entity) in SystemAPI
                .Query<RefRW<TrebuchetState>, RefRO<Target>>()
                .WithEntityAccess())
            {
                ref var treb = ref trebState.ValueRW;

                // Moving / holding an active destination -> packed. Uses the
                // same Has != 0 signal as RangedCombatSystem's isMoving gate
                // (component PRESENCE means nothing — movement consumes Has).
                bool moving = em.HasComponent<DesiredDestination>(entity)
                    && em.GetComponentData<DesiredDestination>(entity).Has != 0;
                if (moving)
                {
                    treb.Deployed = 0;
                    treb.Timer = 0f;
                    continue;
                }

                if (treb.Deployed != 0) continue;

                // Stationary: set-up progresses only against a live target.
                var tgt = target.ValueRO.Value;
                if (tgt == Entity.Null || !em.Exists(tgt))
                {
                    treb.Timer = 0f;
                    continue;
                }

                treb.Timer += dt;
                if (treb.Timer >= DeployTime)
                    treb.Deployed = 1;
            }
        }
    }
}
