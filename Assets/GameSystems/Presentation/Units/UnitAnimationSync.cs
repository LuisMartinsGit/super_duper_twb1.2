// UnitAnimationSync.cs
// Bridges ECS unit state to Unity Animator parameters.
// Attached to unit GameObjects by PresentationSpawnSystem.

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
    ///   IsWorking (bool)   — builder constructing or miner gathering (generic)
    ///   IsBuilding (bool)  — Worker constructing/repairing at a site
    ///   IsMining (bool)    — Worker gathering at a deposit
    ///   IsHealing (bool)   — litharch healing a target
    ///   IsDead (trigger)   — set once when health drops to 0
    ///   AttackSpeed (float) — for animation speed scaling
    ///
    /// IsBuilding/IsMining are the Worker's granular split of the generic
    /// IsWorking flag. They are driven mutually exclusively (at most one is
    /// true) so a controller never double-transitions. Controllers that only
    /// have IsWorking keep working unchanged — missing parameters are ignored.
    ///
    /// Works with any Animator Controller that has these parameters.
    /// Missing parameters are silently ignored.
    /// </summary>
    public class UnitAnimationSync : MonoBehaviour
    {
        /// <summary>ECS entity this visual represents.</summary>
        public Entity LinkedEntity;

        /// <summary>
        /// If &gt; 0, this unit fires a phase-locked draw→shoot once per shot:
        /// the draw is cued (via the "DrawBow" trigger) this many seconds before
        /// the reload completes, so the bow's visible release lands on the actual
        /// arrow spawn instead of a free-running loop drifting out of sync. This
        /// is the tunable "draw lead": INCREASE it if the arrow releases before
        /// the bow's visible loose, DECREASE it if the arrow releases after.
        /// 0 = legacy MoveSpeed-proxy AttackSpeed. Set by PresentationSpawnSystem.
        /// </summary>
        public float AttackCycleSeconds = 0f;

        private Animator _animator;
        private EntityManager _em;
        private bool _valid;
        private bool _deathTriggered;

        // Cached parameter hashes
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
        private static readonly int IsWorkingHash = Animator.StringToHash("IsWorking");
        private static readonly int IsBuildingHash = Animator.StringToHash("IsBuilding");
        private static readonly int IsMiningHash = Animator.StringToHash("IsMining");
        private static readonly int IsHealingHash = Animator.StringToHash("IsHealing");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
        private static readonly int DrawBowHash = Animator.StringToHash("DrawBow");

        // Track which parameters exist on this animator
        private bool _hasIsMoving, _hasIsAttacking, _hasIsWorking;
        private bool _hasIsBuilding, _hasIsMining;
        private bool _hasIsHealing, _hasIsDead, _hasAttackSpeed, _hasDrawBow;

        // Phase-locked draw cue: armed once per shot so DrawBow fires exactly once.
        private bool _drawArmed;

        // Natural attack clip length (seconds); 0 = no attack clip found.
        private float _attackClipLength;

        void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            _em = world.EntityManager;

            // The controller is assigned by PresentationSpawnSystem right after this
            // component is added; if it somehow hasn't landed yet, bail without
            // touching `parameters` (which logs "Animator is not playing an
            // AnimatorController"). _valid stays false so LateUpdate no-ops.
            if (_animator.runtimeAnimatorController == null) return;

            // Natural length of the attack clip, used to scale the Attack
            // state so exactly one swing plays per attack cooldown tick.
            foreach (var clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && clip.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _attackClipLength = clip.length;
                    break;
                }
            }

            // Probe which parameters exist
            foreach (var param in _animator.parameters)
            {
                int hash = param.nameHash;
                if (hash == IsMovingHash) _hasIsMoving = true;
                else if (hash == IsAttackingHash) _hasIsAttacking = true;
                else if (hash == IsWorkingHash) _hasIsWorking = true;
                else if (hash == IsBuildingHash) _hasIsBuilding = true;
                else if (hash == IsMiningHash) _hasIsMining = true;
                else if (hash == IsHealingHash) _hasIsHealing = true;
                else if (hash == IsDeadHash) _hasIsDead = true;
                else if (hash == AttackSpeedHash) _hasAttackSpeed = true;
                else if (hash == DrawBowHash) _hasDrawBow = true;
            }

            _valid = true;
        }

        void LateUpdate()
        {
            if (!_valid || _animator == null) return;
            // Guard against the ECS world being disposed (player returned to
            // main menu mid-game); `_em.Exists` throws ObjectDisposedException
            // if the world is gone. Re-acquire silently on the next valid frame.
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (_em != world.EntityManager) _em = world.EntityManager;
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
                    isWorking = miner.State == MinerWorkState.Gathering;
                }

                // Builder: check active BuildOrder
                if (!isWorking && _em.HasComponent<BuildOrder>(LinkedEntity))
                {
                    var order = _em.GetComponentData<BuildOrder>(LinkedEntity);
                    isWorking = order.Site != Entity.Null;
                }

                _animator.SetBool(IsWorkingHash, isWorking);
            }

            // ── Worker granular work states (build / mine) ──
            // The Worker (unified Builder + Miner) splits the generic IsWorking
            // flag into mutually-exclusive animations. Build/repair wins (the
            // unit is at a construction site); otherwise the MinerState says
            // whether it is gathering at a deposit. At most one bool is ever true.
            if (_hasIsBuilding || _hasIsMining)
            {
                bool building = false, mining = false;

                if (_em.HasComponent<BuildOrder>(LinkedEntity))
                    building = _em.GetComponentData<BuildOrder>(LinkedEntity).Site != Entity.Null;
                if (!building && _em.HasComponent<RepairOrder>(LinkedEntity))
                    building = _em.GetComponentData<RepairOrder>(LinkedEntity).Site != Entity.Null;

                if (!building && _em.HasComponent<MinerState>(LinkedEntity))
                {
                    var miner = _em.GetComponentData<MinerState>(LinkedEntity);
                    mining = miner.State == MinerWorkState.Gathering;
                }

                if (_hasIsBuilding) _animator.SetBool(IsBuildingHash, building);
                if (_hasIsMining)   _animator.SetBool(IsMiningHash, mining);
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

            // ── Attack speed ──
            // Phase-locked units (AttackCycleSeconds > 0) play their draw→shoot
            // at NATURAL speed — the timing comes from WHEN the draw is cued, not
            // from stretching the clips — so AttackSpeed stays 1. Units with an
            // AttackCooldown scale the Attack state so one clip cycle spans one
            // cooldown tick — swings land at the actual attack rate. Units with
            // neither keep the legacy MoveSpeed proxy.
            if (_hasAttackSpeed)
            {
                float speed = 1f;
                if (!(AttackCycleSeconds > 0f))
                {
                    if (_attackClipLength > 0f && _em.HasComponent<AttackCooldown>(LinkedEntity))
                    {
                        var cd = _em.GetComponentData<AttackCooldown>(LinkedEntity);
                        if (cd.Cooldown > 0.01f)
                            speed = Mathf.Clamp(_attackClipLength / cd.Cooldown, 0.25f, 4f);
                    }
                    else if (_em.HasComponent<MoveSpeed>(LinkedEntity))
                    {
                        var ms = _em.GetComponentData<MoveSpeed>(LinkedEntity);
                        speed = Mathf.Max(ms.Value / 4f, 0.5f); // Normalize around base speed 4
                    }
                }
                _animator.SetFloat(AttackSpeedHash, speed);
            }

            // ── Draw cue (phase-locked to the reload) ──
            // Fire the draw→shoot ONCE per shot, cued AttackCycleSeconds before
            // the reload completes, so the bow's release lands on the actual
            // arrow spawn instead of a free-running loop drifting out of sync.
            // CooldownTimer counts down to the next shot (interval =
            // max(aim, cooldown); see RangedCombatSystem); it jumps back to the
            // full cooldown right after firing, which re-arms the cue.
            if (_hasDrawBow && AttackCycleSeconds > 0f
                && _em.HasComponent<ArcherState>(LinkedEntity))
            {
                float cd = _em.GetComponentData<ArcherState>(LinkedEntity).CooldownTimer;
                if (cd > AttackCycleSeconds)
                {
                    _drawArmed = false; // reloaded — arm for the next shot
                }
                else if (!_drawArmed && cd > 0f)
                {
                    // Only draw while actually engaged and standing still (a
                    // moving/chasing/retreating archer isn't about to loose).
                    bool hasTarget = _em.HasComponent<Target>(LinkedEntity)
                        && _em.GetComponentData<Target>(LinkedEntity).Value != Entity.Null;
                    bool moving = _em.HasComponent<DesiredDestination>(LinkedEntity)
                        && _em.GetComponentData<DesiredDestination>(LinkedEntity).Has == 1;
                    if (hasTarget && !moving)
                    {
                        _animator.SetTrigger(DrawBowHash);
                        _drawArmed = true;
                    }
                }
            }
        }
    }
}
