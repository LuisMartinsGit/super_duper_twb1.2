// UnitAnimationSync.cs
// Bridges ECS unit state to Unity Animator parameters.
// Attached to unit GameObjects by PresentationSpawnSystem.

using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// MonoBehaviour that reads ECS state components each frame and drives
    /// Animator parameters for unit animations.
    ///
    /// Standardized Animator Parameters:
    ///   IsMoving (bool)    — unit has an active movement destination
    ///   IsAttacking (bool) — unit is attacking (melee or ranged)
    ///   IsWorking (bool)   — worker constructing or worker gathering (generic)
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
    ///
    /// 3,000-unit pass (2026-10-15): tunables in UnitAnimationSync.asset
    /// (<see cref="UnitAnimationSyncConfig"/>). Off-screen units poll slower
    /// and are re-polled the frame they come into view; parameters are written
    /// only when they change; render settings (culling, keep-state, skinned
    /// mesh flags) are applied at spawn by <see cref="ConfigureView"/>; an
    /// optional distance LOD and distance shadow cull exist, both OFF in the
    /// shipped config.
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

        /// <summary>State-boolean poll cadence (2026-08-31 profile pass;
        /// tunable since 2026-10-15). The booleans feed animator TRANSITIONS,
        /// which blend over longer than the on-screen interval anyway, so it
        /// is visually identical to per-frame. Off-screen units poll slower
        /// and are re-polled the frame they come into view. The bow draw cue
        /// is exempt: it is phase-locked to the arrow spawn and runs per-frame
        /// in <see cref="UpdateDrawCue"/>.</summary>
        private float _nextPoll;

        // ── Render-side state (2026-10-15, 3,000-unit pass) ──────────────
        private static UnitAnimationSyncConfig _cfg;
        internal static UnitAnimationSyncConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<UnitAnimationSyncConfig>());

        /// <summary>Sim position at the last poll — the on-screen and LOD
        /// tests read this instead of a Transform (no native call per unit
        /// per frame). Stale by at most one off-screen poll interval, which
        /// <see cref="UnitAnimationSyncConfig.visibilityPad"/> covers.</summary>
        private Vector3 _lastPos;
        private bool _wasVisible;

        // Last value written per Animator parameter (-1 = unknown). Each
        // SetBool/SetFloat is a native call; most polls change nothing.
        // Reset in OnEnable, because an Animator that does NOT keep its state
        // on disable comes back with default parameters.
        private sbyte _vMoving = -1, _vAttacking = -1, _vWorking = -1;
        private sbyte _vBuilding = -1, _vMining = -1, _vHealing = -1;
        private float _vAttackSpeed = float.NaN;

        // Distance LOD: this component disabled the Animator and steps it.
        private bool _lodManual;
        private float _lodAccum;
        private int _lodPhase;

        // Distance shadow cull (only when the config asks for it).
        private Renderer[] _shadowRenderers;
        private UnityEngine.Rendering.ShadowCastingMode[] _shadowModes;
        private bool _shadowsCulled;

        /// <summary>
        /// Apply the render settings every animated unit view gets, the moment
        /// it is instantiated (PresentationSpawnSystem): Animator culling and
        /// keep-state, skinned-mesh offscreen/motion-vector/quality flags.
        /// None of these change how an on-screen unit looks with the shipped
        /// config; see <see cref="UnitAnimationSyncConfig"/>.
        /// </summary>
        public static void ConfigureView(GameObject view, Animator animator)
        {
            var cfg = Cfg;
            if (cfg == null)
            {
                // Config missing (already logged loudly by Require): keep the
                // pre-config behaviour rather than leave the prefab default.
                if (animator != null) animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                return;
            }

            if (animator != null)
            {
                animator.cullingMode = cfg.cullingMode;
                animator.keepAnimatorStateOnDisable = cfg.keepStateOnDisable;
            }

            var smrs = view.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < smrs.Length; i++)
            {
                var smr = smrs[i];
                if (cfg.forceNoUpdateWhenOffscreen) smr.updateWhenOffscreen = false;
                if (cfg.disableSkinnedMotionVectors) smr.skinnedMotionVectors = false;
                if (cfg.skinQuality != SkinQuality.Auto) smr.quality = cfg.skinQuality;
            }
        }

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

            var cfg = Cfg;
            float interval = cfg != null ? cfg.onscreenPollInterval : 0.1f;

            // Stagger the polls across units so a whole army does not pay its
            // poll on the same frame — that synchronization would just
            // rebuild the spike this throttle removes.
            _nextPoll = Time.unscaledTime
                + ((GetInstanceID() & 15) / 16f) * interval;
            _lodPhase = GetInstanceID() & 0x7fff;

            if (LinkedEntity != Entity.Null && _em.Exists(LinkedEntity)
                && _em.HasComponent<LocalTransform>(LinkedEntity))
                _lastPos = _em.GetComponentData<LocalTransform>(LinkedEntity).Position;
            else
                _lastPos = transform.position;

            if (cfg != null && cfg.shadowCullDistance > 0f)
            {
                var all = GetComponentsInChildren<Renderer>(true);
                int n = 0;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] is SkinnedMeshRenderer || all[i] is MeshRenderer) all[n++] = all[i];
                _shadowRenderers = new Renderer[n];
                _shadowModes = new UnityEngine.Rendering.ShadowCastingMode[n];
                for (int i = 0; i < n; i++)
                {
                    _shadowRenderers[i] = all[i];
                    _shadowModes[i] = all[i].shadowCastingMode;
                }
            }
        }

        // ── Central tick (2026-09-25) ────────────────────────────────────
        // Every unit used to carry its own LateUpdate: hundreds of native ->
        // managed calls a frame, each re-resolving the ECS world. They now
        // register while ENABLED (a fog-hidden view is disabled, and Unity
        // did not call its LateUpdate either) and UnitAnimationTicker runs
        // them all from one LateUpdate with one world lookup.
        internal int TickIndex = -1;

        void OnEnable()
        {
            UnitAnimationTicker.Register(this);
            _vMoving = _vAttacking = _vWorking = _vBuilding = _vMining = _vHealing = -1;
            _vAttackSpeed = float.NaN;
            _wasVisible = false;   // re-poll on the first visible frame
        }

        void OnDisable()
        {
            UnitAnimationTicker.Unregister(this);
            ExitLod();
        }

        /// <summary>Hand the Animator back to Unity if the LOD disabled it.
        /// Every path that stops driving this unit (death, entity gone, view
        /// hidden) calls this, so a corpse or a re-shown view never stays
        /// frozen on a manually stepped rig.</summary>
        private void ExitLod()
        {
            if (!_lodManual) return;
            _lodManual = false;
            _lodAccum = 0f;
            if (_animator != null) _animator.enabled = true;
        }

        internal void Tick(EntityManager em, in UnitAnimationTicker.Frame f)
        {
            if (!_valid || _animator == null) return;
            if (_em != em) _em = em;
            if (LinkedEntity == Entity.Null || !_em.Exists(LinkedEntity)) { ExitLod(); return; }
            if (_deathTriggered) { ExitLod(); return; }

            // Phase-locked bow cue stays PER-FRAME — its whole purpose is to
            // land the visible release on the exact arrow-spawn tick, and a
            // 10 Hz poll would jitter it by up to 100 ms.
            if (_hasDrawBow && AttackCycleSeconds > 0f)
                UpdateDrawCue();

            // On-screen test against the last polled position: pure math on
            // the once-per-frame frustum planes, no native call.
            var cfg = f.Cfg;
            bool visible = ProceduralRigCulling.Visible(_lastPos, f.VisibilityPad);
            bool becameVisible = visible && !_wasVisible;
            _wasVisible = visible;

            if (cfg != null && cfg.lodEnabled) StepLod(cfg, visible, in f);

            if (_shadowRenderers != null && f.HaveCamera)
            {
                float d = cfg.shadowCullDistance;
                bool far = (_lastPos - f.CameraPos).sqrMagnitude > d * d;
                if (far != _shadowsCulled)
                {
                    _shadowsCulled = far;
                    for (int i = 0; i < _shadowRenderers.Length; i++)
                        if (_shadowRenderers[i] != null)
                            _shadowRenderers[i].shadowCastingMode = far
                                ? UnityEngine.Rendering.ShadowCastingMode.Off
                                : _shadowModes[i];
                }
            }

            // Everything below is transition booleans — throttled, staggered;
            // a unit that just came into view is polled immediately.
            if (!becameVisible && f.Now < _nextPoll) return;
            _nextPoll = f.Now + (visible ? f.OnscreenInterval : f.OffscreenInterval);

            if (_em.HasComponent<LocalTransform>(LinkedEntity))
                _lastPos = _em.GetComponentData<LocalTransform>(LinkedEntity).Position;

            // ── Death check ──
            if (_em.HasComponent<Health>(LinkedEntity))
            {
                var health = _em.GetComponentData<Health>(LinkedEntity);
                if (health.Value <= 0)
                {
                    if (_hasIsDead)
                    {
                        ExitLod();
                        _animator.SetTrigger(IsDeadHash);
                        _deathTriggered = true;
                    }
                    return;
                }
            }

            // ── Death animation state (delay before entity destruction) ──
            if (_em.HasComponent<DeathAnimationState>(LinkedEntity)
                && _em.IsComponentEnabled<DeathAnimationState>(LinkedEntity))
            {
                if (_hasIsDead && !_deathTriggered)
                {
                    ExitLod();
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
                SetBoolCached(IsMovingHash, isMoving, ref _vMoving);
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

                // Melee: check Target — but only for a unit that can swing. A
                // DISARMED unit (Damage 0: the Scout before Armed Scouts, the
                // Litharch before Warrior Priests) keeps a target an attack
                // order gave it, yet MeleeCombatSystem skips it outright, so
                // reading the target alone played the attack clip in place
                // forever. It idles instead.
                if (!isAttacking && _em.HasComponent<Target>(LinkedEntity)
                    && !(_em.HasComponent<Damage>(LinkedEntity)
                         && _em.GetComponentData<Damage>(LinkedEntity).Value <= 0))
                {
                    var target = _em.GetComponentData<Target>(LinkedEntity);
                    isAttacking = target.Value != Entity.Null;
                }

                SetBoolCached(IsAttackingHash, isAttacking, ref _vAttacking);
            }

            // ── Working (worker gathering or worker constructing) ──
            if (_hasIsWorking)
            {
                bool isWorking = false;

                // Worker: check WorkerState == Gathering
                if (_em.HasComponent<WorkerState>(LinkedEntity))
                {
                    var worker = _em.GetComponentData<WorkerState>(LinkedEntity);
                    isWorking = worker.State == WorkerActivity.Gathering;
                }

                // Worker: check active BuildOrder
                if (!isWorking && TransientState.Active<BuildOrder>(_em, LinkedEntity))
                {
                    var order = _em.GetComponentData<BuildOrder>(LinkedEntity);
                    isWorking = order.Site != Entity.Null;
                }

                SetBoolCached(IsWorkingHash, isWorking, ref _vWorking);
            }

            // ── Worker granular work states (build / mine) ──
            // The Worker splits the generic IsWorking
            // flag into mutually-exclusive animations. Build/repair wins (the
            // unit is at a construction site); otherwise the WorkerState says
            // whether it is gathering at a deposit. At most one bool is ever true.
            if (_hasIsBuilding || _hasIsMining)
            {
                bool building = false, mining = false;

                if (TransientState.Active<BuildOrder>(_em, LinkedEntity))
                    building = _em.GetComponentData<BuildOrder>(LinkedEntity).Site != Entity.Null;
                if (!building && TransientState.Active<RepairOrder>(_em, LinkedEntity))
                    building = _em.GetComponentData<RepairOrder>(LinkedEntity).Site != Entity.Null;

                if (!building && _em.HasComponent<WorkerState>(LinkedEntity))
                {
                    var worker = _em.GetComponentData<WorkerState>(LinkedEntity);
                    mining = worker.State == WorkerActivity.Gathering;
                }

                if (_hasIsBuilding) SetBoolCached(IsBuildingHash, building, ref _vBuilding);
                if (_hasIsMining)   SetBoolCached(IsMiningHash, mining, ref _vMining);
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
                SetBoolCached(IsHealingHash, isHealing, ref _vHealing);
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
                if (!(speed == _vAttackSpeed))
                {
                    _animator.SetFloat(AttackSpeedHash, speed);
                    _vAttackSpeed = speed;
                }
            }

        }

        private void SetBoolCached(int hash, bool value, ref sbyte last)
        {
            sbyte v = value ? (sbyte)1 : (sbyte)0;
            if (v == last) return;
            _animator.SetBool(hash, value);
            last = v;
        }

        /// <summary>
        /// Distance animation LOD (off in the shipped config — see
        /// <see cref="UnitAnimationSyncConfig.lodEnabled"/>). A visible unit
        /// farther than lodDistance has its Animator disabled and stepped
        /// manually every lodFrameInterval frames with the accumulated delta,
        /// so clips keep real speed at a lower sample rate. Near or off-screen
        /// units hand the Animator back to Unity (off-screen it is culled).
        /// Relies on keepAnimatorStateOnDisable: disabling an Animator that
        /// does not keep its state resets it.
        /// </summary>
        private void StepLod(UnitAnimationSyncConfig cfg, bool visible, in UnitAnimationTicker.Frame f)
        {
            bool want = visible && f.HaveCamera && _animator.keepAnimatorStateOnDisable
                && (_lastPos - f.CameraPos).sqrMagnitude > cfg.lodDistance * cfg.lodDistance;
            if (!want) { ExitLod(); return; }

            if (!_lodManual)
            {
                // Only take over an Animator Unity is running. One a unit's
                // own script disabled (the Ledger's procedural gait) is left
                // alone, or this would switch it back on.
                if (!_animator.enabled) return;
                _animator.enabled = false;
                _lodManual = true;
                _lodAccum = 0f;
            }

            _lodAccum += f.DeltaTime;
            int every = Mathf.Max(1, cfg.lodFrameInterval);
            if ((f.FrameCount + _lodPhase) % every == 0)
            {
                _animator.Update(_lodAccum);
                _lodAccum = 0f;
            }
        }

        // ── Draw cue (phase-locked to the reload) ──
        // Fire the draw→shoot ONCE per shot, cued AttackCycleSeconds before
        // the reload completes, so the bow's release lands on the actual
        // arrow spawn instead of a free-running loop drifting out of sync.
        // CooldownTimer counts down to the next shot (interval =
        // max(aim, cooldown); see RangedCombatSystem); it jumps back to the
        // full cooldown right after firing, which re-arms the cue.
        // Called per-frame from LateUpdate, deliberately outside the poll
        // throttle — see _nextPoll.
        private void UpdateDrawCue()
        {
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

    /// <summary>
    /// Runs every enabled <see cref="UnitAnimationSync"/> from ONE LateUpdate.
    /// Created on first registration, lives for the session. Order among the
    /// units is irrelevant: each writes only its own Animator.
    /// </summary>
    internal sealed class UnitAnimationTicker : MonoBehaviour
    {
        private static UnitAnimationTicker _instance;
        private static readonly System.Collections.Generic.List<UnitAnimationSync> _all = new();

        /// <summary>Everything a unit tick needs that is the same for every
        /// unit this frame — read once here instead of once per unit (each
        /// Time.* / camera read is a native call).</summary>
        internal struct Frame
        {
            public UnitAnimationSyncConfig Cfg;
            public float Now;
            public float DeltaTime;
            public int FrameCount;
            public bool HaveCamera;
            public Vector3 CameraPos;
            public float VisibilityPad;
            public float OnscreenInterval;
            public float OffscreenInterval;
        }

        internal static void Register(UnitAnimationSync s)
        {
            if (s.TickIndex >= 0) return;
            s.TickIndex = _all.Count;
            _all.Add(s);
            if (_instance == null)
            {
                var go = new GameObject("UnitAnimationTicker");
                go.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<UnitAnimationTicker>();
            }
        }

        internal static void Unregister(UnitAnimationSync s)
        {
            int i = s.TickIndex;
            if (i < 0 || i >= _all.Count || _all[i] != s) { s.TickIndex = -1; return; }
            int last = _all.Count - 1;
            if (i != last)
            {
                var moved = _all[last];
                _all[i] = moved;
                moved.TickIndex = i;
            }
            _all.RemoveAt(last);
            s.TickIndex = -1;
        }

        void LateUpdate()
        {
            // Guard against the ECS world being disposed (player returned to
            // main menu mid-game); Exists throws ObjectDisposedException if the
            // world is gone. Re-acquired on the next valid frame.
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var cfg = UnitAnimationSync.Cfg;
            var f = new Frame
            {
                Cfg = cfg,
                Now = Time.unscaledTime,
                DeltaTime = Time.deltaTime,
                FrameCount = Time.frameCount,
                VisibilityPad = cfg != null ? cfg.visibilityPad : 8f,
                OnscreenInterval = cfg != null ? cfg.onscreenPollInterval : 0.1f,
                OffscreenInterval = cfg != null ? cfg.offscreenPollInterval : 0.1f,
            };
            f.HaveCamera = ProceduralRigCulling.TryGetCameraPosition(out f.CameraPos);

            // Backwards: a tick that disables its own view swap-removes it.
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                if (i >= _all.Count) continue;
                var s = _all[i];
                if (s == null)
                {
                    // Destroyed without OnDisable (domain teardown): swap-remove.
                    int last = _all.Count - 1;
                    _all[i] = _all[last];
                    if (_all[i] != null) _all[i].TickIndex = i;
                    _all.RemoveAt(last);
                    continue;
                }
                s.Tick(em, in f);
            }
        }
    }
}
