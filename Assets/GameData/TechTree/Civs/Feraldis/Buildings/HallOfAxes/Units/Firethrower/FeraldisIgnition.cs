// "Fire eats blood" — the Firethrower's signature. Canon:
// docs/Design/Age_1_Feraldis.md.
//
// A Firethrower shot that lands on bloodsoaked ground sets the BLOOD alight:
// the pool is CONSUMED (BloodMap.Drain) and the patch burns for a few
// seconds, damaging everything standing in it. On clean ground the shot is
// just a mediocre ranged attack.
//
// That consumption is the whole design. Every other part of the Feraldis kit
// accumulates blood — frenzy ground, totem fuel — and the Firethrower is the
// one thing that SPENDS it for immediate area damage. Burning your own
// frenzy carpet to wipe a clumped army is meant to be a real decision.
//
// The burn itself reuses the engine's existing BurningGround entity +
// BurningGroundSystem (1 s tick, self-destroys at expiry), so no new
// damage-over-time plumbing is introduced.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Influence;

namespace TheWaningBorder.Systems.Combat
{
    public static class FeraldisIgnition
    {
        /// <summary>
        /// Try to set bloodsoaked ground alight at an impact point. No-op on
        /// clean ground, which is what makes the Firethrower situational.
        /// Returns true if the ground caught.
        ///
        /// Structural: creates a BurningGround entity via the ECB, so this is
        /// safe to call from inside a query iteration.
        /// </summary>
        public static bool TryIgnite(EntityManager em, EntityCommandBuffer ecb,
            in IgnitesBlood spec, float3 at)
        {
            if (spec.Radius <= 0f || spec.Duration <= 0f) return false;
            if (!BloodMap.Ready) return false;
            if (BloodMap.SampleWorld(at.x, at.z)
                < TheWaningBorder.Core.Config.FeraldisConstants.IgnitionBloodThreshold)
                return false;

            // The blood is SPENT — it becomes fire, not a lasting stain.
            BloodMap.Drain(at.x, at.z, spec.Radius);

            var fire = ecb.CreateEntity();
            ecb.AddComponent(fire, new BurningGround
            {
                DPS = spec.DamagePerSecond,
                TimeRemaining = spec.Duration,
                Radius = spec.Radius,
            });
            ecb.AddComponent(fire, LocalTransform.FromPositionRotationScale(
                at, quaternion.identity, 1f));
            return true;
        }
    }
}
