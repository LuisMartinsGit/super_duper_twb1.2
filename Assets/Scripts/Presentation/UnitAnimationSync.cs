// UnitAnimationSync.cs
// Bridges ECS unit state to Unity Animator parameters.
// Attached to unit GameObjects by PresentationSpawnSystem.
// Location: Assets/Scripts/Presentation/UnitAnimationSync.cs

using UnityEngine;
using Unity.Entities;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// MonoBehaviour that reads ECS state components each frame and drives
    /// Animator parameters for unit animations.
    ///
    /// Standardized Animator Parameters:
    ///   IsMoving (bool)    — unit has an active movement destination
    ///   IsAttacking (bool) — unit is attacking (melee or ranged)
    ///   IsWorking (bool)   — builder constructing or miner gathering
    ///   IsHealing (bool)   — litharch healing a target
    ///   IsDead (trigger)   — set once when health drops to 0
    ///   AttackSpeed (float) — for animation speed scaling
    ///
    /// Works with any Animator Controller that has these parameters.
    /// Missing parameters are silently ignored.
    /// </summary>
    public class UnitAnimationSync : MonoBehaviour
    {
        /// <summary>ECS entity this visual represents.</summary>
        public Entity LinkedEntity;

        private Animator _animator;
        private EntityManager _em;
        private bool _valid;
        private bool _deathTriggered;

        // Cached parameter hashes
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
        private static readonly int IsWorkingHash = Animator.StringToHash("IsWorking");
        private static readonly int IsHealingHash = Animator.StringToHash("IsHealing");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");

        // Track which parameters exist on this animator
        private bool _hasIsMoving, _hasIsAttacking, _hasIsWorking;
        private bool _hasIsHealing, _hasIsDead, _hasAttackSpeed;

        void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            _em = world.EntityManager;

            // Probe which parameters exist
            foreach (var param in _animator.parameters)
            {
                int hash = param.nameHash;
                if (hash == IsMovingHash) _hasIsMoving = true;
                else if (hash == IsAttackingHash) _hasIsAttacking = true;
                else if (hash == IsWorkingHash) _hasIsWorking = true;
                else if (hash == IsHealingHash) _hasIsHealing = true;
                else if (hash == IsDeadHash) _hasIsDead = true;
                else if (hash == AttackSpeedHash) _hasAttackSpeed = true;
            }

            _valid = true;
        }

        void LateUpdate()
        {
            if (!_valid || _animator == null) return;
            if (LinkedEntity == Entity.Null || !_em.Exists(LinkedEntity)) return;
            if (_deathTriggered) return;

            // ── Death check ──
            if (_em.HasComponent<Health>(LinkedEntity))
            {
                var health = _em.GetComponentData<Health>(LinkedEntity);
                if (health.Value <= 0)
                {
                    if (_hasIsDead)
                    {
                        _animator.SetTrigger(IsDeadHash);
                        _deathTriggered = true;
                    }
                    return;
                }
            }

            // ── Death animation state (delay before entity destruction) ──
            if (_em.HasComponent<DeathAnimationState>(LinkedEntity))
            {
                if (_hasIsDead && !_deathTriggered)
                {
                    _animator.SetTrigger(IsDeadHash);
                    _deathTriggered = true;
                }
                return;
            }

            // ── Movement ──
            if (_hasIsMoving)
            {
                bool isMoving = false;
                if (_em.HasComponent<DesiredDestination>(LinkedEntity))
                {
                    var dest = _em.GetComponentData<DesiredDestination>(LinkedEntity);
                    isMoving = dest.Has == 1;
                }
                _animator.SetBool(IsMovingHash, isMoving);
            }

            // ── Attack (ranged via ArcherState or melee via Target) ──
            if (_hasIsAttacking)
            {
                bool isAttacking = false;

                // Ranged: check ArcherState.IsFiring
                if (_em.HasComponent<ArcherState>(LinkedEntity))
                {
                    var archer = _em.GetComponentData<ArcherState>(LinkedEntity);
                    isAttacking = archer.IsFiring == 1;
                }

                // Melee: check Target
                if (!isAttacking && _em.HasComponent<Target>(LinkedEntity))
                {
                    var target = _em.GetComponentData<Target>(LinkedEntity);
                    isAttacking = target.Value != Entity.Null;
                }

                _animator.SetBool(IsAttackingHash, isAttacking);
            }

            // ── Working (miner gathering or builder constructing) ──
            if (_hasIsWorking)
            {
                bool isWorking = false;

                // Miner: check MinerState == Gathering
                if (_em.HasComponent<MinerState>(LinkedEntity))
                {
                    var miner = _em.GetComponentData<MinerState>(LinkedEntity);
                    isWorking = miner.State == (byte)MinerWorkState.Gathering;
                }

                // Builder: check active BuildOrder
                if (!isWorking && _em.HasComponent<BuildOrder>(LinkedEntity))
                {
                    var order = _em.GetComponentData<BuildOrder>(LinkedEntity);
                    isWorking = order.Site != Entity.Null;
                }

                _animator.SetBool(IsWorkingHash, isWorking);
            }

            // ── Healing (Litharch) ──
            if (_hasIsHealing)
            {
                bool isHealing = false;
                if (_em.HasComponent<LitharchState>(LinkedEntity))
                {
                    var litharch = _em.GetComponentData<LitharchState>(LinkedEntity);
                    isHealing = litharch.IsHealing == 1;
                }
                _animator.SetBool(IsHealingHash, isHealing);
            }

            // ── Attack speed (from MoveSpeed as proxy) ──
            if (_hasAttackSpeed)
            {
                float speed = 1f;
                if (_em.HasComponent<MoveSpeed>(LinkedEntity))
                {
                    var ms = _em.GetComponentData<MoveSpeed>(LinkedEntity);
                    speed = Mathf.Max(ms.Value / 4f, 0.5f); // Normalize around base speed 4
                }
                _animator.SetFloat(AttackSpeedHash, speed);
            }
        }
    }
}
