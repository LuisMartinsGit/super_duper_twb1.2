// File: Assets/Scripts/Presentation/VeilstingerGunTracker.cs
// Manages Veilstinger gun visuals: rotates leftgun/rightgun toward their respective
// ECS targets, and provides world-space gun positions for projectile spawning.

using UnityEngine;
using Unity.Entities;
using Unity.Transforms;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Attached to Veilstinger visual GameObjects by PresentationSpawnSystem.
    /// Each frame, reads VeilstingerState from the linked ECS entity to:
    /// 1. Rotate leftgun/rightgun to LookAt their respective targets
    /// 2. Expose gun tip world positions (used by ProjectileVisualSystem for spawn offset)
    /// </summary>
    public class VeilstingerGunTracker : MonoBehaviour
    {
        /// <summary>The ECS entity this visual represents.</summary>
        [HideInInspector] public Entity Entity;

        /// <summary>Left gun child transform (fires at Target1).</summary>
        public Transform LeftGun { get; private set; }

        /// <summary>Right gun child transform (fires at Target2).</summary>
        public Transform RightGun { get; private set; }

        private Unity.Entities.World _world;
        private EntityManager _em;
        private bool _initialized;

        // Store default local rotations so we can compose aim rotation from rest pose
        private Quaternion _leftGunDefaultLocalRot;
        private Quaternion _rightGunDefaultLocalRot;

        void Start()
        {
            // Find gun child objects in the prefab hierarchy
            LeftGun = FindChildRecursive(transform, "leftgun");
            RightGun = FindChildRecursive(transform, "rightgun");

            if (LeftGun == null)
                Debug.LogWarning($"[VeilstingerGunTracker] 'leftgun' child not found on {gameObject.name}");
            else
                _leftGunDefaultLocalRot = LeftGun.localRotation;

            if (RightGun == null)
                Debug.LogWarning($"[VeilstingerGunTracker] 'rightgun' child not found on {gameObject.name}");
            else
                _rightGunDefaultLocalRot = RightGun.localRotation;

            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
            {
                _em = _world.EntityManager;
                _initialized = true;
            }
        }

        void LateUpdate()
        {
            if (!_initialized || _world == null || !_world.IsCreated) return;
            if (Entity == Entity.Null || !_em.Exists(Entity)) return;
            if (!_em.HasComponent<VeilstingerState>(Entity)) return;

            var vs = _em.GetComponentData<VeilstingerState>(Entity);

            // Rotate left gun toward Target1
            if (LeftGun != null && vs.Target1 != Entity.Null && _em.Exists(vs.Target1))
            {
                if (_em.HasComponent<LocalTransform>(vs.Target1))
                {
                    var targetPos = (Vector3)_em.GetComponentData<LocalTransform>(vs.Target1).Position;
                    AimGunAt(LeftGun, targetPos);
                }
            }

            // Rotate right gun toward Target2
            if (RightGun != null && vs.Target2 != Entity.Null && _em.Exists(vs.Target2))
            {
                if (_em.HasComponent<LocalTransform>(vs.Target2))
                {
                    var targetPos = (Vector3)_em.GetComponentData<LocalTransform>(vs.Target2).Position;
                    AimGunAt(RightGun, targetPos);
                }
            }
            // If only one target, both guns aim at it
            else if (RightGun != null && vs.Target1 != Entity.Null && _em.Exists(vs.Target1))
            {
                if (_em.HasComponent<LocalTransform>(vs.Target1))
                {
                    var targetPos = (Vector3)_em.GetComponentData<LocalTransform>(vs.Target1).Position;
                    AimGunAt(RightGun, targetPos);
                }
            }
        }

        /// <summary>
        /// Aims a gun transform at a target position.
        ///
        /// The gun model's barrel may point along any local axis. We compute the
        /// desired world-space "look" direction, convert to local space relative
        /// to the gun's parent, and then apply a correction rotation so the barrel
        /// (not necessarily local-Z) aligns with the target.
        ///
        /// The default local rotation encodes which way the barrel originally faces.
        /// We: 1) compute a LookRotation in the parent's space, 2) multiply by the
        /// inverse of the default rotation to undo the rest pose, then re-apply it
        /// so the barrel aims correctly without the "point up and twist" artifact.
        /// </summary>
        private void AimGunAt(Transform gun, Vector3 targetWorldPos)
        {
            Vector3 dir = targetWorldPos - gun.position;
            if (dir.sqrMagnitude < 0.001f) return;

            // Get the default local rotation for this gun
            Quaternion defaultLocal = (gun == LeftGun) ? _leftGunDefaultLocalRot : _rightGunDefaultLocalRot;

            // Compute desired world rotation (barrel → target)
            Quaternion desiredWorldRot = Quaternion.LookRotation(dir, transform.up);

            // Convert desired world rotation to local rotation in parent space
            Transform parent = gun.parent != null ? gun.parent : transform;
            Quaternion parentWorldRot = parent.rotation;
            Quaternion desiredLocal = Quaternion.Inverse(parentWorldRot) * desiredWorldRot;

            // Compose: apply the default rest pose, then rotate the barrel axis toward target
            // defaultLocal tells us the "rest" orientation of the gun.
            // We want: gun.localRotation = desiredLocal * correction
            // where correction maps the gun's natural forward to world forward.
            // Since LookRotation assumes Z-forward, and the gun's barrel might be along a different axis,
            // we pre-multiply by the default to preserve the barrel axis mapping.
            gun.localRotation = desiredLocal * defaultLocal;
        }

        /// <summary>
        /// Returns the world-space tip position of the left gun.
        /// Used by VeilstingerCombatSystem (via PresentationSpawnSystem lookup) for projectile spawn.
        /// Falls back to entity center + offset if gun not found.
        /// </summary>
        public Vector3 GetLeftGunTipWorld()
        {
            if (LeftGun != null)
                return LeftGun.position + LeftGun.forward * GetGunLength(LeftGun);
            return transform.position + Vector3.up * 1.2f + transform.right * -0.5f;
        }

        /// <summary>
        /// Returns the world-space tip position of the right gun.
        /// Falls back to entity center + offset if gun not found.
        /// </summary>
        public Vector3 GetRightGunTipWorld()
        {
            if (RightGun != null)
                return RightGun.position + RightGun.forward * GetGunLength(RightGun);
            return transform.position + Vector3.up * 1.2f + transform.right * 0.5f;
        }

        /// <summary>
        /// Estimates gun barrel length from the gun object's local bounds.
        /// </summary>
        private float GetGunLength(Transform gun)
        {
            var renderer = gun.GetComponentInChildren<Renderer>();
            if (renderer != null)
                return renderer.bounds.extents.z;
            return 0.3f; // Default tip offset
        }

        /// <summary>
        /// Recursively search for a child by name (case-insensitive).
        /// </summary>
        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                    return child;

                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
