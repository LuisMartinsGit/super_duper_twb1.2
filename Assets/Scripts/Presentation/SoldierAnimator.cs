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
        private bool _initialized;

        // ECS access
        private Unity.Entities.World _world;
        private EntityManager _em;
        private bool _ecsReady;

        // Animation parameters
        private const float IdleBobSpeed = 2.5f;
        private const float IdleBobAmount = 0.04f;
        private const float IdleArmSway = 10f;

        private const float WalkCycleSpeed = 8f;
        private const float WalkLegSwing = 35f;
        private const float WalkArmSwing = 25f;
        private const float WalkBob = 0.05f;

        private const float AttackDuration = 0.5f;
        private const float AttackArmSwing = 100f;
        private const float AttackLunge = 0.15f;

        private const float DeathDuration = 0.8f;
        private const float DeathFallAngle = 90f;

        void Start()
        {
            CacheBodyParts();
            _lastPosition = transform.position;
            TryInitECS();
        }

        private void CacheBodyParts()
        {
            _leftArmPivot = transform.Find("LeftArmPivot");
            _rightArmPivot = transform.Find("RightArmPivot");
            _leftLegPivot = transform.Find("LeftLegPivot");
            _rightLegPivot = transform.Find("RightLegPivot");
            _torso = transform.Find("Torso");
            _head = transform.Find("Head");
            _initialized = _leftArmPivot != null && _rightArmPivot != null;
        }

        private void TryInitECS()
        {
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
            {
                _em = _world.EntityManager;
                _ecsReady = true;
            }
        }

        void Update()
        {
            // Retry ECS init if it wasn't ready at Start
            if (!_ecsReady)
            {
                TryInitECS();
                if (!_ecsReady) return;
            }

            // Retry body part cache if not found at Start
            if (!_initialized)
                CacheBodyParts();

            if (_isDead)
            {
                UpdateDeathAnimation();
                return;
            }

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
            if (!_ecsReady || _world == null || !_world.IsCreated)
                return AnimState.Idle;
            if (Entity == Entity.Null || !_em.Exists(Entity))
                return AnimState.Idle;

            // Check death
            if (_em.HasComponent<Health>(Entity))
            {
                var health = _em.GetComponentData<Health>(Entity);
                if (health.Value <= 0)
                    return AnimState.Death;
            }

            // Check attacking: cooldown timer active means we just attacked or are attacking,
            // OR we have a target in melee range (within radius * 3)
            if (_em.HasComponent<AttackCooldown>(Entity) && _em.HasComponent<Target>(Entity))
            {
                var target = _em.GetComponentData<Target>(Entity);
                if (target.Value != Entity.Null && _em.Exists(target.Value))
                {
                    var cooldown = _em.GetComponentData<AttackCooldown>(Entity);
                    if (cooldown.Timer > 0f)
                        return AnimState.Attacking;

                    // Also check proximity: if we have a target and are close, we're in combat
                    if (_em.HasComponent<Unity.Transforms.LocalTransform>(Entity) &&
                        _em.HasComponent<Unity.Transforms.LocalTransform>(target.Value))
                    {
                        var myPos = _em.GetComponentData<Unity.Transforms.LocalTransform>(Entity).Position;
                        var targetPos = _em.GetComponentData<Unity.Transforms.LocalTransform>(target.Value).Position;
                        float dist = Unity.Mathematics.math.distance(myPos, targetPos);
                        float attackRange = 2.5f;
                        if (_em.HasComponent<Radius>(Entity))
                            attackRange = _em.GetComponentData<Radius>(Entity).Value * 4f;
                        if (dist <= attackRange)
                            return AnimState.Attacking;
                    }
                }
            }

            // Check movement via ECS DesiredDestination (reliable, not frame-order dependent)
            bool isMoving = false;

            // Direct destination on this entity
            if (_em.HasComponent<DesiredDestination>(Entity))
            {
                var dest = _em.GetComponentData<DesiredDestination>(Entity);
                if (dest.Has == 1)
                    isMoving = true;
            }

            // Battalion member: check leader's destination
            if (!isMoving && _em.HasComponent<BattalionMemberData>(Entity))
            {
                var memberData = _em.GetComponentData<BattalionMemberData>(Entity);
                var leader = memberData.Leader;
                if (leader != Entity.Null && _em.Exists(leader) &&
                    _em.HasComponent<DesiredDestination>(leader))
                {
                    var leaderDest = _em.GetComponentData<DesiredDestination>(leader);
                    if (leaderDest.Has == 1)
                        isMoving = true;
                }
            }

            // Fallback: position delta
            if (!isMoving)
            {
                var pos = transform.position;
                _velocity = (pos - _lastPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
                _lastPosition = pos;
                if (_velocity > 0.1f)
                    isMoving = true;
            }
            else
            {
                _lastPosition = transform.position;
            }

            if (isMoving)
                return AnimState.Walking;

            return AnimState.Idle;
        }

        private void UpdateIdleAnimation()
        {
            float t = _animTime;

            // Body bob
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

            // Arm sway
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

            // Sword arm: wind up -> slash -> recovery
            float swingCurve;
            if (t < 0.3f)
            {
                // Wind up (arm back)
                swingCurve = Mathf.Lerp(0f, -40f, t / 0.3f);
            }
            else if (t < 0.6f)
            {
                // Slash forward
                swingCurve = Mathf.Lerp(-40f, AttackArmSwing, (t - 0.3f) / 0.3f);
            }
            else
            {
                // Recovery
                swingCurve = Mathf.Lerp(AttackArmSwing, 0f, (t - 0.6f) / 0.4f);
            }

            if (_rightArmPivot != null)
                _rightArmPivot.localRotation = Quaternion.Euler(swingCurve, 0f, 0f);

            // Left arm braces back
            if (_leftArmPivot != null)
                _leftArmPivot.localRotation = Quaternion.Euler(-20f, 0f, 0f);

            // Body lunge forward
            if (_torso != null)
            {
                float lunge = 0f;
                if (t > 0.2f && t < 0.7f)
                    lunge = Mathf.Sin((t - 0.2f) / 0.5f * Mathf.PI) * AttackLunge;

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

            // Loop attack while still in combat
            if (t >= 1f)
                _attackTimer = 0f;
        }

        private void UpdateDeathAnimation()
        {
            _deathTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_deathTimer / DeathDuration);

            // Ease out curve for natural fall
            float fallT = 1f - (1f - t) * (1f - t);

            // Body falls forward — apply to a pivot offset, not root
            // (root rotation is overwritten by SyncTransforms)
            if (_torso != null)
            {
                _torso.localRotation = Quaternion.Euler(fallT * DeathFallAngle, 0f, 0f);
                var p = _torso.localPosition;
                p.y = 0.85f - fallT * 0.6f;
                _torso.localPosition = p;
            }

            if (_head != null)
            {
                _head.localRotation = Quaternion.Euler(fallT * DeathFallAngle, 0f, 0f);
                var p = _head.localPosition;
                p.y = 1.25f - fallT * 0.8f;
                _head.localPosition = p;
            }

            // Arms go limp (swing down)
            if (_leftArmPivot != null)
            {
                _leftArmPivot.localRotation = Quaternion.Euler(fallT * 80f, 0f, -fallT * 20f);
                var p = _leftArmPivot.localPosition;
                p.y = 1.0f - fallT * 0.5f;
                _leftArmPivot.localPosition = p;
            }
            if (_rightArmPivot != null)
            {
                _rightArmPivot.localRotation = Quaternion.Euler(fallT * 90f, 0f, fallT * 15f);
                var p = _rightArmPivot.localPosition;
                p.y = 1.0f - fallT * 0.5f;
                _rightArmPivot.localPosition = p;
            }

            // Legs buckle
            if (_leftLegPivot != null)
            {
                _leftLegPivot.localRotation = Quaternion.Euler(-fallT * 40f, 0f, 0f);
                var p = _leftLegPivot.localPosition;
                p.y = 0.55f - fallT * 0.3f;
                _leftLegPivot.localPosition = p;
            }
            if (_rightLegPivot != null)
            {
                _rightLegPivot.localRotation = Quaternion.Euler(-fallT * 30f, 0f, 0f);
                var p = _rightLegPivot.localPosition;
                p.y = 0.55f - fallT * 0.3f;
                _rightLegPivot.localPosition = p;
            }
        }
    }
}
