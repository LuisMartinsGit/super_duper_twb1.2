// File: Assets/GameData/TechTree/Presentation/Units/CatapultVisual.cs
// Shared siege arm-driver — used by both the Alanthor Ballista and the
// Runai Catapult prefabs, so it lives in cross-unit Presentation, not in
// either unit's folder.
// Procedural throwing-arm animation for the Ballista's placeholder visual
// prefab (Synty SM_Wep_Catapult_01). No Animator/clips: the Synty model is a
// rigid-part hierarchy, so the arm child is driven directly from ECS combat
// state — snap release when a shot fires, then a slow wind-back over the
// reload. Class name stays CatapultVisual (referenced by the editor-side
// GameDataMaintenanceTool and the authored prefab) until dedicated ballista
// art replaces the Synty catapult model.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Input; // EntityReference

namespace TheWaningBorder.Presentation
{
    public class CatapultVisual : MonoBehaviour
    {
        [Tooltip("Arm child name inside the Synty catapult prefab.")]
        public string ArmChildName = "SM_Wep_Catapult_Arm_01";

        [Tooltip("Local X rotation added to the arm's authored pose when fully released (thrown forward).")]
        public float ReleasedAngle = 75f;

        [Tooltip("Seconds for the release snap (arm flying forward).")]
        public float SnapSeconds = 0.12f;

        [Tooltip("Seconds the arm rests released before the crew winds it back.")]
        public float HoldSeconds = 0.6f;

        [Tooltip("Seconds to wind the arm back to the armed pose.")]
        public float RewindSeconds = 2.2f;

        [Header("Shot effect (Synty FX_CatapultShot, used as-is)")]
        [Tooltip("Elevation in degrees above horizontal at which the authored template launches its stone (the template's up-tilt). The per-shot pitch is solved ballistically from this, the stone's start speed, and the target distance.")]
        public float TemplateElevation = 30f;

        [Tooltip("Extra degrees added to every solved pitch (positive tilts down/shorter). Use to trim if drag or spawn height make shots land consistently long or short.")]
        public float PitchTrim = 0f;

        [Tooltip("Launch height above the catapult's position. Must clear the engine's own collider — the FX stone has world collision and would otherwise burst on the catapult that fired it.")]
        public float MuzzleHeight = 2.4f;

        [Tooltip("Launch offset forward along the aim, also clearing the engine's collider (matches the arm's release point).")]
        public float MuzzleForward = 1.2f;

        [Tooltip("Shot effect template — nested inside the prefab (inactive) so its particle settings can be tweaked/overridden per-catapult. Kept at localScale 1 so spawned shots come out at world scale 1 despite the 0.7 root. Falls back to Resources Prefabs/Effects/FX_CatapultShot when unassigned.")]
        public GameObject ShotFxTemplate;

        private GameObject _shotFx;
        private float _stoneSpeed = 20f;
        private float _stoneGravity = 9.81f;

        private Transform _arm;
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

            _shotFx = ShotFxTemplate != null
                ? ShotFxTemplate
                : Resources.Load<GameObject>("Prefabs/Effects/FX_CatapultShot");

            // The template is a spawn blueprint — keep it dormant even if it
            // was left active while being authored/previewed in the editor.
            if (ShotFxTemplate != null) ShotFxTemplate.SetActive(false);

            // Read the STONE's ballistics off the template (the stone is the
            // one system with world collision) so pitch solving tracks any
            // start-speed / gravity tweaks made on the authored FX.
            if (_shotFx != null)
            {
                foreach (var ps in _shotFx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (!ps.collision.enabled) continue;
                    var m = ps.main;
                    if (m.startSpeed.constant > 0.1f) _stoneSpeed = m.startSpeed.constant;
                    float gMod = Mathf.Abs(m.gravityModifier.constant);
                    if (gMod > 0.01f) _stoneGravity = gMod * Mathf.Abs(Physics.gravity.y);
                    break;
                }
            }

            _arm = FindDeep(transform, ArmChildName);
            if (_arm == null) return;
            _armedPose = _arm.localRotation;
            _valid = true;
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
                _releaseTime = Time.time;
                // Flat-trajectory siege (the Ballista): the ECS bolt renders
                // itself via ProjectileVisualSystem, so the canned stone FX
                // would be a second, unsynchronised projectile. Arm only.
                if (archer.Trajectory != ShotTrajectory.Flat)
                    SpawnShotFx(e);
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

            _arm.localRotation = _armedPose * Quaternion.Euler(ReleasedAngle * blend, 0f, 0f);
        }

        /// <summary>
        /// Fire the self-contained Synty catapult FX (stone + trail + impact
        /// are all inside the prefab — used as-is, per design). Only the
        /// launch angle changes: yaw faces the target, and pitch is blended
        /// between the near/far offsets by target distance so the canned
        /// stone lands closer or farther.
        /// </summary>
        private void SpawnShotFx(Entity e)
        {
            if (_shotFx == null) return;

            Vector3 pivot = transform.position;
            Quaternion bodyYaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Quaternion yawDelta = Quaternion.identity; // extra yaw from body facing to the target
            bool hasTargetPos = false;
            Vector3 targetPos = Vector3.zero;

            if (_em.HasComponent<Target>(e))
            {
                var tgt = _em.GetComponentData<Target>(e).Value;
                if (tgt != Entity.Null && _em.Exists(tgt)
                    && _em.HasComponent<Unity.Transforms.LocalTransform>(tgt))
                {
                    targetPos = (Vector3)_em.GetComponentData<Unity.Transforms.LocalTransform>(tgt).Position;
                    hasTargetPos = true;
                    Vector3 flat = targetPos - pivot;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > 0.01f)
                        yawDelta = Quaternion.LookRotation(flat.normalized, Vector3.up)
                                   * Quaternion.Inverse(bodyYaw);
                }
            }

            Vector3 origin;
            Quaternion rot;
            if (ShotFxTemplate != null)
            {
                // Use the authored template pose VERBATIM — only swing it
                // around the catapult to face the target, then pitch by the
                // ballistic solve below. Whatever orientation/position was
                // tuned on the nested template is what fires.
                origin = pivot + yawDelta * (ShotFxTemplate.transform.position - pivot);
                rot = yawDelta * ShotFxTemplate.transform.rotation;
            }
            else
            {
                // Resources fallback: synthetic muzzle clear of the collider.
                var aim = yawDelta * bodyYaw;
                origin = pivot + Vector3.up * MuzzleHeight + (aim * Vector3.forward) * MuzzleForward;
                rot = aim;
            }

            // Impact-synchronised solve. The DAMAGE lands when the ECS
            // projectile does — RangedCombatSystem gives catapult lobs a hang
            // time of clamp(distance/9, 2..3) s. Solve the stone's launch
            // speed AND pitch so it covers the muzzle→target trajectory in
            // exactly that time:  vx = x/T,  vy = (y + g*T^2/2) / T.
            // The visible impact then coincides with the Health write instead
            // of trailing it by seconds (the old fixed-speed solve).
            float pitch = PitchTrim;
            float launchSpeed = _stoneSpeed;
            if (hasTargetPos)
            {
                float g = _stoneGravity;
                Vector3 d3 = targetPos - origin;
                float x = new Vector2(d3.x, d3.z).magnitude;
                float y = d3.y;
                if (x > 0.5f)
                {
                    float simDistance = Vector3.Distance(targetPos, pivot);
                    float T = Mathf.Clamp(simDistance / 9f, 2f, 3f);
                    float vx = x / T;
                    float vy = (y + 0.5f * g * T * T) / T;
                    launchSpeed = Mathf.Sqrt(vx * vx + vy * vy);
                    float theta = Mathf.Atan2(vy, vx);
                    pitch = TemplateElevation - theta * Mathf.Rad2Deg + PitchTrim;
                }
            }
            rot = Quaternion.AngleAxis(pitch, rot * Vector3.right) * rot;

            var fx = Instantiate(_shotFx, origin, rot);
            fx.SetActive(true); // the template ships inactive

            // Push the solved speed into the stone particle (the one system
            // with collision) so the clone actually flies the solved arc.
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps.collision.enabled) continue;
                var stoneMain = ps.main;
                stoneMain.startSpeed = launchSpeed;
                break;
            }

            // ONE volley per shot: the Synty FX is authored looping for its
            // demo scene — a looping clone left behind would keep lobbing
            // stones from the old spot after the catapult moves away.
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var psMain = ps.main;
                psMain.loop = false;
            }

            Destroy(fx, 12f); // stone particle lifetime is 10 s
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
