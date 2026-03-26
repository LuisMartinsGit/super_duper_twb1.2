// SoldierAnimator.cs
// Procedural animation controller for primitive-based soldier models.
// Location: Assets/Scripts/Presentation/SoldierAnimator.cs

using UnityEngine;
using Unity.Entities;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Drives procedural animations for a soldier built from primitives.
    /// Reads ECS state each frame to determine which animation to play:
    /// Idle, Walking, Attacking, or Death.
    /// </summary>
    public class SoldierAnimator : MonoBehaviour
    {
        public Entity Entity;

        // Body part pivots (found on Start)
        private Transform _leftArmPivot;
        private Transform _rightArmPivot;
        private Transform _leftLegPivot;
        private Transform _rightLegPivot;
        private Transform _torso;
        private Transform _head;

        // Animation state
        private enum AnimState { Idle, Walking, Attacking, Death }
        private AnimState _state = AnimState.Idle;
        private float _animTime;
        private float _attackTimer;
        private float _deathTimer;
        private bool _isDead;

        // Position tracking for movement detection
        private Vector3 _lastPosition;
        private float _velocity;

        // ECS access
        private Unity.Entities.World _world;
        private EntityManager _em;

        // Animation parameters
        private const float IdleBobSpeed = 2f;
        private const float IdleBobAmount = 0.01f;
        private const float IdleArmSway = 3f;

        private const float WalkCycleSpeed = 8f;
        private const float WalkLegSwing = 35f;
        private const float WalkArmSwing = 25f;
        private const float WalkBob = 0.03f;

        private const float AttackDuration = 0.4f;
        private const float AttackArmSwing = 90f;
        private const float AttackLunge = 0.1f;

        private const float DeathDuration = 0.8f;
        private const float DeathFallAngle = 90f;

        // Base positions (recorded on start)
        private float _baseY;

        void Start()
        {
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;

            // Cache body part references
            _leftArmPivot = transform.Find("LeftArmPivot");
            _rightArmPivot = transform.Find("RightArmPivot");
            _leftLegPivot = transform.Find("LeftLegPivot");
            _rightLegPivot = transform.Find("RightLegPivot");
            _torso = transform.Find("Torso");
            _head = transform.Find("Head");

            _lastPosition = transform.position;
            _baseY = 0f; // Local offset reference
        }

        void Update()
        {
            if (_isDead)
            {
                UpdateDeathAnimation();
                return;
            }

            // Determine state from ECS
            var newState = DetectState();

            if (newState != _state)
            {
                _state = newState;
                _animTime = 0f;
                if (_state == AnimState.Attacking)
                    _attackTimer = 0f;
                if (_state == AnimState.Death)
                {
                    _deathTimer = 0f;
                    _isDead = true;
                    UpdateDeathAnimation();
                    return;
                }
            }

            _animTime += Time.deltaTime;

            switch (_state)
            {
                case AnimState.Idle:
                    UpdateIdleAnimation();
                    break;
                case AnimState.Walking:
                    UpdateWalkAnimation();
                    break;
                case AnimState.Attacking:
                    UpdateAttackAnimation();
                    break;
            }
        }

        private AnimState DetectState()
        {
            if (_world == null || !_world.IsCreated) return AnimState.Idle;
            if (Entity == Entity.Null || !_em.Exists(Entity)) return AnimState.Idle;

            // Check death
            if (_em.HasComponent<Health>(Entity))
            {
                var health = _em.GetComponentData<Health>(Entity);
                if (health.Value <= 0)
                    return AnimState.Death;
            }

            // Check attacking (cooldown timer active + has valid target)
            if (_em.HasComponent<AttackCooldown>(Entity) && _em.HasComponent<Target>(Entity))
            {
                var cooldown = _em.GetComponentData<AttackCooldown>(Entity);
                var target = _em.GetComponentData<Target>(Entity);
                if (cooldown.Timer > 0f && target.Value != Entity.Null)
                    return AnimState.Attacking;
            }

            // Check movement (position delta)
            var pos = transform.position;
            _velocity = (pos - _lastPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            _lastPosition = pos;

            if (_velocity > 0.1f)
                return AnimState.Walking;

            return AnimState.Idle;
        }

        private void UpdateIdleAnimation()
        {
            float t = _animTime;

            // Subtle body bob
            if (_torso != null)
            {
                var p = _torso.localPosition;
                p.y = 0.85f + Mathf.Sin(t * IdleBobSpeed) * IdleBobAmount;
                _torso.localPosition = p;
            }

            if (_head != null)
            {
                var p = _head.localPosition;
                p.y = 1.25f + Mathf.Sin(t * IdleBobSpeed) * IdleBobAmount;
                _head.localPosition = p;
            }

            // Gentle arm sway
            float armAngle = Mathf.Sin(t * 1.5f) * IdleArmSway;
            if (_leftArmPivot != null)
                _leftArmPivot.localRotation = Quaternion.Euler(armAngle, 0f, 0f);
            if (_rightArmPivot != null)
                _rightArmPivot.localRotation = Quaternion.Euler(-armAngle, 0f, 0f);

            // Reset legs
            if (_leftLegPivot != null)
                _leftLegPivot.localRotation = Quaternion.identity;
            if (_rightLegPivot != null)
                _rightLegPivot.localRotation = Quaternion.identity;
        }

        private void UpdateWalkAnimation()
        {
            float t = _animTime;
            float cycle = Mathf.Sin(t * WalkCycleSpeed);

            // Leg swing (opposite directions)
            if (_leftLegPivot != null)
                _leftLegPivot.localRotation = Quaternion.Euler(cycle * WalkLegSwing, 0f, 0f);
            if (_rightLegPivot != null)
                _rightLegPivot.localRotation = Quaternion.Euler(-cycle * WalkLegSwing, 0f, 0f);

            // Arm swing (opposite to legs)
            if (_leftArmPivot != null)
                _leftArmPivot.localRotation = Quaternion.Euler(-cycle * WalkArmSwing, 0f, 0f);
            if (_rightArmPivot != null)
                _rightArmPivot.localRotation = Quaternion.Euler(cycle * WalkArmSwing, 0f, 0f);

            // Body bob (double frequency)
            if (_torso != null)
            {
                var p = _torso.localPosition;
                p.y = 0.85f + Mathf.Abs(Mathf.Sin(t * WalkCycleSpeed * 2f)) * WalkBob;
                _torso.localPosition = p;
            }

            if (_head != null)
            {
                var p = _head.localPosition;
                p.y = 1.25f + Mathf.Abs(Mathf.Sin(t * WalkCycleSpeed * 2f)) * WalkBob;
                _head.localPosition = p;
            }
        }

        private void UpdateAttackAnimation()
        {
            _attackTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_attackTimer / AttackDuration);

            // Sword arm swings forward then back
            float swingCurve;
            if (t < 0.4f)
            {
                // Wind up
                swingCurve = Mathf.Lerp(0f, -30f, t / 0.4f);
            }
            else if (t < 0.7f)
            {
                // Slash forward
                swingCurve = Mathf.Lerp(-30f, AttackArmSwing, (t - 0.4f) / 0.3f);
            }
            else
            {
                // Recovery
                swingCurve = Mathf.Lerp(AttackArmSwing, 0f, (t - 0.7f) / 0.3f);
            }

            if (_rightArmPivot != null)
                _rightArmPivot.localRotation = Quaternion.Euler(swingCurve, 0f, 0f);

            // Left arm stays back during attack
            if (_leftArmPivot != null)
                _leftArmPivot.localRotation = Quaternion.Euler(-15f, 0f, 0f);

            // Body lunge forward
            if (_torso != null)
            {
                float lunge = 0f;
                if (t > 0.3f && t < 0.7f)
                    lunge = Mathf.Sin((t - 0.3f) / 0.4f * Mathf.PI) * AttackLunge;

                var p = _torso.localPosition;
                p.y = 0.85f;
                p.z = lunge;
                _torso.localPosition = p;
            }

            // Reset legs
            if (_leftLegPivot != null)
                _leftLegPivot.localRotation = Quaternion.identity;
            if (_rightLegPivot != null)
                _rightLegPivot.localRotation = Quaternion.identity;

            // Loop attack if still attacking
            if (t >= 1f)
                _attackTimer = 0f;
        }

        private void UpdateDeathAnimation()
        {
            _deathTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_deathTimer / DeathDuration);

            // Ease out curve for natural fall
            float fallT = 1f - (1f - t) * (1f - t);

            // Body falls forward (rotate around base)
            transform.localRotation = Quaternion.Euler(fallT * DeathFallAngle, transform.localEulerAngles.y, 0f);

            // Arms go limp (swing down)
            if (_leftArmPivot != null)
                _leftArmPivot.localRotation = Quaternion.Euler(fallT * 60f, 0f, -fallT * 20f);
            if (_rightArmPivot != null)
                _rightArmPivot.localRotation = Quaternion.Euler(fallT * 70f, 0f, fallT * 15f);

            // Legs buckle slightly
            if (_leftLegPivot != null)
                _leftLegPivot.localRotation = Quaternion.Euler(-fallT * 30f, 0f, 0f);
            if (_rightLegPivot != null)
                _rightLegPivot.localRotation = Quaternion.Euler(-fallT * 20f, 0f, 0f);
        }
    }
}
