// Spell.cs
// A spell authored as a PREFAB you can drop into a scene and edit in the
// Inspector. One Spell component per spell prefab, organised into six sections:
//
//   Triggers          — what makes the spell fire (activation / cooldown / cast time)
//   Target            — how it is applied (shape / who / range / radius / duration)
//   Effects           — what it does (a list of gameplay effects)
//   Power-Up VFX      — the wind-up / channel effect on the caster
//   Cast VFX          — the impact / nova on cast
//   Ground Circle VFX — the flat pattern drawn on the ground (sized by Radius)
//
// This is authoring/presentation data — the Spell Showcase scenario instantiates
// these prefabs and calls PlayVfx to demo them. The gameplay sim still runs its
// own spec tables; editing a spell prefab changes how a cast LOOKS and documents
// its design, it does not rebalance the live game.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Abilities.Vfx
{
    /// <summary>What makes a spell fire.</summary>
    public enum SpellActivation : byte
    {
        Active = 0,        // fired by the player / AI
        Passive = 1,       // always on (auras, passive self-buffs)
        OnDeath = 2,       // fires when the owner dies
        OnLowHealth = 3,   // fires when the owner drops below HpThreshold (Guild ward)
        OnEnemyInRange = 4,// fires when a hostile enters range
    }

    /// <summary>How a spell's effect is applied.</summary>
    public enum SpellTargetShape : byte
    {
        SelfCast = 0,      // affects the caster only
        SingleTarget = 1,  // one target entity in range
        Area = 2,          // AoE around a target point
        Aura = 3,          // continuous radius around the caster
        Global = 4,        // whole faction (no range)
    }

    /// <summary>Who a spell's effects land on.</summary>
    public enum SpellAffects : byte
    {
        Self = 0,
        Allies = 1,
        Enemies = 2,
        EconomyBuildings = 3,
        Everyone = 4,
    }

    /// <summary>The mechanic a spell effect represents (authoring/design metadata).</summary>
    public enum SpellEffectKind : byte
    {
        None = 0,
        Damage = 1,          // burst damage (Value = amount)
        Heal = 2,            // heal (Value = amount)
        ArmorBuff = 3,       // +Value armor for Duration
        DamageBuff = 4,      // xValue outgoing damage for Duration
        SpeedSlow = 5,       // -Value fraction move speed (0.5 = -50%)
        SpeedBuff = 6,       // xValue move speed for Duration
        Root = 7,            // fully immobilise for Duration
        RevealFog = 8,       // reveal fog of war (Value = radius, 0 = spell radius)
        BurnGround = 9,      // burning ground, Value dps for Duration
        FreezeCooldowns = 10,// halt enemy cooldown recovery for Duration
        DamageReduction = 11,// take Value fraction less damage (0.9 = -90%)
        ChargeBonus = 12,    // +Value flat charge damage
        ResourceYield = 13,  // +Value% resource yield for Duration
        LosRamp = 14,        // line-of-sight grows while stationary
        HpFloor = 15,        // clamp HP so it never drops below Value
    }

    /// <summary>One effect entry on a spell.</summary>
    [Serializable]
    public struct SpellEffect
    {
        public SpellEffectKind kind;
        public float value;
        [Tooltip("Seconds (0 = instant, -1 = permanent / passive).")]
        public float duration;
    }

    [AddComponentMenu("Waning Border/Spell")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class Spell : MonoBehaviour
    {
        [Header("Identity")]
        public string spellId;
        public string displayName;

        // ── TRIGGERS — what triggers this spell ─────────────────────────────
        [Header("TRIGGERS  —  what triggers this spell")]
        public SpellActivation activation = SpellActivation.Active;
        [Tooltip("Seconds before it can fire again (0 = none).")]
        public float cooldown;
        [Tooltip("Wind-up seconds before the effect lands (0 = instant).")]
        public float castTime;
        [Tooltip("For OnLowHealth: fraction of max HP (0..1) that arms the trigger.")]
        [Range(0f, 1f)] public float hpThreshold;

        // ── TARGET — how the spell is applied ───────────────────────────────
        [Header("TARGET  —  how the spell is applied")]
        public SpellTargetShape shape = SpellTargetShape.Area;
        public SpellAffects affects = SpellAffects.Enemies;
        [Tooltip("Cast / aim range in world units (0 = self / unlimited).")]
        public float range;
        [Tooltip("Area radius in world units — also SIZES the ground circle.")]
        public float radius = 8f;
        [Tooltip("Seconds the applied effect lasts (0 = instant, -1 = permanent).")]
        public float duration;

        // ── EFFECTS — what this spell does ──────────────────────────────────
        [Header("EFFECTS  —  what this spell does")]
        public SpellEffect[] effects = new SpellEffect[0];

        // Playback speed applied to every VFX slot below: 1 = normal,
        // <1 = slow, >1 = fast, 0 = frozen (a fully-drawn snapshot held still).
        // Always applies — no mode to set first.
        private const string SpeedTip =
            "Playback speed: 1 = normal, 0.1 = 10% (slow), 0 = frozen snapshot, >1 = faster.";

        // ── POWER-UP VFX (wind-up / channel on the caster) ──────────────────
        [Header("POWER-UP VFX  (wind-up / channel)")]
        public GameObject powerUpPrefab;
        public bool powerUpTint = false;
        [ColorUsage(true, true)] public Color powerUpColor = Color.white;
        [Tooltip(SpeedTip)] [Range(0f, 3f)] public float powerUpSpeed = 1f;

        // ── CAST VFX (impact / nova) ────────────────────────────────────────
        [Header("CAST VFX  (impact / nova)")]
        public GameObject castPrefab;
        public bool castTint = false;
        [ColorUsage(true, true)] public Color castColor = Color.white;
        [Tooltip(SpeedTip)] [Range(0f, 3f)] public float castSpeed = 1f;

        // ── GROUND CIRCLE VFX ───────────────────────────────────────────────
        [Header("GROUND CIRCLE VFX")]
        public GameObject circlePrefab;
        public bool circleTint = true;
        [ColorUsage(true, true)] public Color circleColor = Color.white;
        [Tooltip(SpeedTip)] [Range(0f, 3f)] public float circleSpeed = 1f;

        // ── EDITOR PREVIEW ──────────────────────────────────────────────────
        [Header("EDITOR PREVIEW")]
        [Tooltip("Continuously loop this spell's VFX in the editor while editing it.")]
        public bool previewInEditor = true;
        [Tooltip("Seconds between preview re-casts (one-shot effects repeat on this beat).")]
        public float previewInterval = 4f;

        /// <summary>On-screen lifetime for a showcase cast.</summary>
        public float DisplayLife => Mathf.Max(2f, duration > 0f ? duration : 3f);

        /// <summary>Play this spell's VFX at its own position (used by the showcase).</summary>
        public void PlayVfx() => SpellVfxPlayer.Cast(this, transform.position);

#if UNITY_EDITOR
        // Edit-mode looping preview: instantiates the three VFX slots as
        // throwaway (HideAndDontSave) children and drives their particle
        // simulation off EditorApplication.update, re-casting every
        // previewInterval so one-shot effects repeat. Nothing here is
        // serialised — it never touches the saved prefab/scene.
        [NonSerialized] private readonly List<GameObject> _pvGo = new List<GameObject>();
        [NonSerialized] private readonly List<ParticleSystem> _pvRoot = new List<ParticleSystem>();
        [NonSerialized] private readonly List<float> _pvSpeed = new List<float>(); // <=0 = frozen
        [NonSerialized] private double _pvStart;
        [NonSerialized] private double _pvLastTick;
        [NonSerialized] private double _pvLastRepaint;
        [NonSerialized] private bool _pvDirty;

        private void OnEnable()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.update += EditorPreviewTick;
        }

        private void OnDisable()
        {
            UnityEditor.EditorApplication.update -= EditorPreviewTick;
            ClearPreview();
        }

        // Field edits (colour, radius, anim, prefab swaps) rebuild the preview
        // on the next tick — DestroyImmediate isn't allowed straight from
        // OnValidate, so just flag it.
        private void OnValidate() => _pvDirty = true;

        private void EditorPreviewTick()
        {
            if (this == null) { UnityEditor.EditorApplication.update -= EditorPreviewTick; return; }
            if (Application.isPlaying || UnityEditor.EditorUtility.IsPersistent(this))
            { ClearPreview(); return; }
            if (!previewInEditor || !isActiveAndEnabled)
            { if (_pvGo.Count > 0) ClearPreview(); return; }

            double now = UnityEditor.EditorApplication.timeSinceStartup;
            float interval = Mathf.Max(1f, previewInterval);
            if (_pvDirty || _pvGo.Count == 0 || now - _pvStart >= interval)
            {
                BuildPreview(now);
                _pvLastTick = now;
            }

            // Advance INCREMENTALLY by the real frame delta scaled by each slot's
            // speed (restart:false). This is what actually honours speed — the
            // earlier absolute Simulate(elapsed*speed, restart:true) re-seeded the
            // systems every frame and played far too fast.
            float dt = (float)(now - _pvLastTick);
            _pvLastTick = now;
            if (dt > 0.2f) dt = 0.2f;   // clamp editor stalls
            if (dt > 0f)
            {
                for (int i = 0; i < _pvRoot.Count; i++)
                {
                    var root = _pvRoot[i];
                    float sp = _pvSpeed[i];
                    if (root == null || sp <= 0f) continue; // frozen keeps its snapshot
                    root.Simulate(dt * sp, true, false, false);
                }
            }

            // Edit mode doesn't repaint on its own — nudge the scene view so the
            // preview actually animates.
            if (now - _pvLastRepaint > 0.03)
            {
                UnityEditor.SceneView.RepaintAll();
                _pvLastRepaint = now;
            }
        }

        private void BuildPreview(double now)
        {
            ClearPreview();
            _pvDirty = false;
            _pvStart = now;
            Vector3 pos = transform.position;

            void Add(GameObject go, float speed)
            {
                if (go == null) return;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                var root = go.GetComponent<ParticleSystem>();
                bool frozen = speed <= 0.001f;
                if (frozen && root != null) { root.Simulate(1.2f, true, true, false); root.Pause(true); }
                _pvGo.Add(go);
                _pvRoot.Add(root);
                _pvSpeed.Add(frozen ? 0f : speed);
            }

            if (circlePrefab != null)
                Add(SpellVfxPlayer.SpawnCircleSlot(this, pos, transform), circleSpeed);
            if (castPrefab != null)
                Add(SpellVfxPlayer.SpawnCastSlot(castPrefab, radius, castTint, castColor, pos, transform), castSpeed);
            if (powerUpPrefab != null)
                Add(SpellVfxPlayer.SpawnCastSlot(powerUpPrefab, radius, powerUpTint, powerUpColor, pos, transform), powerUpSpeed);
        }

        private void ClearPreview()
        {
            for (int i = 0; i < _pvGo.Count; i++)
                if (_pvGo[i] != null) DestroyImmediate(_pvGo[i]);
            _pvGo.Clear();
            _pvRoot.Clear();
            _pvSpeed.Clear();
        }
#endif
    }
}
