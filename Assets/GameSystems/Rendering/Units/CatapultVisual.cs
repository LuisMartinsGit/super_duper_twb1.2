// Shared siege-engine driver — used by the catapult prefab (both cultures'
// Catapults) and the Ballista prefab, so it lives in cross-unit Rendering,
// not in either unit's folder.
// No Animator/clips: the Synty models are rigid-part hierarchies, so parts are
// driven directly from ECS combat state. Two modes, either or both:
//   ArmChildName  — a throwing arm that snaps forward when a shot fires and
//                   winds back over the reload (the catapult).
//   BoltChildName — a loaded bolt that vanishes when the shot fires and
//                   reappears once the reload completes (the ballista, whose
//                   projectile is rendered in flight by ProjectileVisualSystem).
// Class name stays CatapultVisual (referenced by the editor-side
// GameDataMaintenanceTool and the authored prefabs).

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Rendering
{
    public class CatapultVisual : MonoBehaviour
    {
        public string ArmChildName = "SM_Wep_Catapult_Arm_01";

        public string BoltChildName = "";

        public float ReleasedAngle = 75f;

        public float SnapSeconds = 0.12f;

        public float HoldSeconds = 0.6f;

        public float RewindSeconds = 2.2f;

        public float TemplateElevation = 30f;

        public float PitchTrim = 0f;

        public float MuzzleHeight = 2.4f;

        public float MuzzleForward = 1.2f;

        public GameObject ShotFxTemplate;

        private Transform _arm;
        private Transform _bolt;
        private Quaternion _armedPose;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _valid;

        private float _prevCooldown = -1f;
        private float _releaseTime = float.NegativeInfinity;

        void Start()
        {
            // Bare `World` resolves to the TheWaningBorder.World namespace here.
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            _em = world.EntityManager;
            _entityRef = GetComponent<EntityReference>();

            // The nested shot template is a leftover authoring blueprint:
            // keep it dormant.
            if (ShotFxTemplate != null) ShotFxTemplate.SetActive(false);

            if (!string.IsNullOrEmpty(ArmChildName)) _arm = FindDeep(transform, ArmChildName);
            if (!string.IsNullOrEmpty(BoltChildName)) _bolt = FindDeep(transform, BoltChildName);
            if (_arm != null) _armedPose = _arm.localRotation;
            _valid = _arm != null || _bolt != null;
        }

        void LateUpdate()
        {
            if (!_valid || _entityRef == null) return;
            var e = _entityRef.Entity;
            if (e == Entity.Null || !_em.Exists(e) || !_em.HasComponent<ArcherState>(e)) return;

            // A shot fired the moment CooldownTimer jumps upward (RangedCombat
            // resets it to the full reload right after spawning the stone).
            var archer = _em.GetComponentData<ArcherState>(e);
            float cd = archer.CooldownTimer;
            if (_prevCooldown >= 0f && cd > _prevCooldown + 0.5f)
            {
                // Arm only. The stone (or bolt) in the air is the simulation's
                // own projectile, drawn by ProjectileVisualSystem (decision
                // 118) — the engine used to launch a second, unsynchronised
                // particle stone here, which could burst on the engine itself
                // and landed apart from the damage.
                _releaseTime = Time.time;
            }
            _prevCooldown = cd;

            // Release blend: 0 = armed, 1 = fully released.
            float t = Time.time - _releaseTime;
            float blend;
            if (t < SnapSeconds)
                blend = t / SnapSeconds;                       // snapping forward
            else if (t < SnapSeconds + HoldSeconds)
                blend = 1f;                                     // resting released
            else
                blend = 1f - Mathf.Clamp01((t - SnapSeconds - HoldSeconds) / RewindSeconds); // winding back

            if (_arm != null)
                _arm.localRotation = _armedPose * Quaternion.Euler(ReleasedAngle * blend, 0f, 0f);

            // The bolt is gone from the moment of release until the reload
            // has wound back; before the first shot it is simply loaded.
            if (_bolt != null)
            {
                bool loaded = t >= SnapSeconds + HoldSeconds + RewindSeconds;
                if (_bolt.gameObject.activeSelf != loaded) _bolt.gameObject.SetActive(loaded);
            }
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            if (root.name == childName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
