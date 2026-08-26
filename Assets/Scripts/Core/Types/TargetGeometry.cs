// One definition of "how far am I from that thing, and where do I stand to
// work on it" — shared by combat, mining, construction, repair and healing.
//
// The rule every caller needs: range is measured to the target's SURFACE, not
// its pivot. A 7x7 Temple's pivot is 3.5 m from its own wall; a melee unit with
// 1.5 m reach standing against that wall is 3.8 m from the pivot and, on the
// centre-distance metric, permanently out of range — it can never attack a
// large building at all. Each system used to open-code (or omit) this, so the
// same building read as a different size depending on who was looking at it.
//
// Buildings with a BuildingSize footprint use the exact axis-aligned rect (grid
// cells are 1 m, centred on the transform). Everything else uses the circle
// model (centre distance minus Radius), which is what units have always used.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core
{
    /// <summary>The target's footprint, resolved once so callers can reuse it.</summary>
    public struct TargetExtent
    {
        public float3 Center;
        /// <summary>True when the target is an axis-aligned rect (has BuildingSize).</summary>
        public bool IsBox;
        /// <summary>Half-width (X) and half-depth (Z) — box targets only.</summary>
        public float HalfW, HalfH;
        /// <summary>Circle radius — non-box targets only.</summary>
        public float Radius;

        /// <summary>Horizontal distance from <paramref name="fromPos"/> to the
        /// target's surface. Zero when inside the footprint.</summary>
        public float SurfaceDistXZ(float3 fromPos)
        {
            if (IsBox)
            {
                float dx = math.max(math.abs(fromPos.x - Center.x) - HalfW, 0f);
                float dz = math.max(math.abs(fromPos.z - Center.z) - HalfH, 0f);
                return math.sqrt(dx * dx + dz * dz);
            }
            return math.max(MathUtil.DistXZ(fromPos, Center) - Radius, 0f);
        }

        /// <summary>
        /// Where to stand to act on this target from <paramref name="fromPos"/>:
        /// the closest point on the surface, pushed <paramref name="standOff"/>
        /// metres back out along the approach direction. Always OUTSIDE the
        /// footprint, so the destination is never an impassable cell.
        /// </summary>
        public float3 ApproachPoint(float3 fromPos, float standOff)
        {
            float3 surface;
            if (IsBox)
            {
                surface = new float3(
                    math.clamp(fromPos.x, Center.x - HalfW, Center.x + HalfW),
                    Center.y,
                    math.clamp(fromPos.z, Center.z - HalfH, Center.z + HalfH));
            }
            else
            {
                float3 away = fromPos - Center;
                away.y = 0f;
                float len = math.length(away);
                surface = len > 1e-4f
                    ? Center + (away / len) * Radius
                    : Center;
                surface.y = Center.y;
            }

            float3 outward = fromPos - surface;
            outward.y = 0f;
            float outLen = math.length(outward);
            if (outLen < 1e-4f)
            {
                // Standing exactly on the surface — any outward bearing works;
                // pick the one away from the centre so we never aim inward.
                outward = fromPos - Center;
                outward.y = 0f;
                outLen = math.length(outward);
                if (outLen < 1e-4f) return fromPos;
            }

            return surface + (outward / outLen) * standOff;
        }
    }

    public static class TargetGeometry
    {
        /// <summary>Resolve a target's footprint. Missing components degrade to a
        /// point target, which is the pre-existing behavior for loose entities.</summary>
        public static TargetExtent Extent(EntityManager em, Entity target)
        {
            var e = new TargetExtent();
            if (target == Entity.Null || !em.Exists(target)) return e;

            if (em.HasComponent<LocalTransform>(target))
                e.Center = em.GetComponentData<LocalTransform>(target).Position;

            if (em.HasComponent<BuildingSize>(target))
            {
                var bs = em.GetComponentData<BuildingSize>(target);
                e.IsBox = true;
                e.HalfW = bs.Width * 0.5f;
                e.HalfH = bs.Height * 0.5f;
            }
            else if (em.HasComponent<Radius>(target))
            {
                e.Radius = em.GetComponentData<Radius>(target).Value;
            }

            return e;
        }

        /// <summary>Horizontal distance from a position to a target's surface.</summary>
        public static float SurfaceDistXZ(EntityManager em, float3 fromPos, Entity target)
            => Extent(em, target).SurfaceDistXZ(fromPos);

        /// <summary>
        /// Surface distance when the caller ALREADY has the target's position —
        /// for hot scan loops (TargetingSystem's per-candidate sweep) where
        /// re-reading LocalTransform per candidate would be wasteful. Costs one
        /// extra HasComponent in the common (non-building) case.
        /// </summary>
        public static float SurfaceDistXZ(EntityManager em, float3 fromPos, float3 targetPos, Entity target)
        {
            if (em.HasComponent<BuildingSize>(target))
            {
                var bs = em.GetComponentData<BuildingSize>(target);
                float dx = math.max(math.abs(fromPos.x - targetPos.x) - bs.Width * 0.5f, 0f);
                float dz = math.max(math.abs(fromPos.z - targetPos.z) - bs.Height * 0.5f, 0f);
                return math.sqrt(dx * dx + dz * dz);
            }

            float radius = em.HasComponent<Radius>(target)
                ? em.GetComponentData<Radius>(target).Value : 0f;
            return math.max(MathUtil.DistXZ(fromPos, targetPos) - radius, 0f);
        }

        /// <summary>
        /// Stop <paramref name="self"/> where it stands and turn it to face
        /// <paramref name="targetPos"/>. The single call every "in range → start
        /// working" branch makes, so mining, building, repairing, healing and
        /// attacking all behave identically: planted, and looking at the job.
        /// </summary>
        public static void StopAndFace(EntityManager em, Entity self, float3 targetPos, float dt)
        {
            if (em.HasComponent<DesiredDestination>(self))
                em.SetComponentData(self, new DesiredDestination { Has = 0 });

            Face(em, self, targetPos, dt);
        }

        /// <summary>
        /// ECB variant, for systems that must defer the write (structural-change
        /// ordering). Rotation still goes through the EntityManager — it is not a
        /// structural change, and deferring it by a frame reads as a visible snap.
        /// </summary>
        public static void StopAndFace(EntityCommandBuffer ecb, EntityManager em, Entity self,
            float3 targetPos, float dt)
        {
            if (em.HasComponent<DesiredDestination>(self))
                ecb.SetComponent(self, new DesiredDestination { Has = 0 });

            Face(em, self, targetPos, dt);
        }

        /// <summary>Turn toward a world position without touching movement state.</summary>
        public static void Face(EntityManager em, Entity self, float3 targetPos, float dt,
            float turnSpeed = MathUtil.DefaultTurnSpeed)
        {
            if (!em.HasComponent<LocalTransform>(self)) return;

            var xf = em.GetComponentData<LocalTransform>(self);
            var turned = MathUtil.TurnTowardXZ(xf.Rotation, xf.Position, targetPos, dt, turnSpeed);
            if (turned.Equals(xf.Rotation)) return;

            xf.Rotation = turned;
            em.SetComponentData(self, xf);
        }
    }
}
