// File: Assets/GameData/TechTree/Units/Alanthor/Outrider/OutriderVisual.cs
// Procedural visual for the Outrider (pid 349) — Alanthor light cavalry.
// A lean unarmored horse (HorseRigBuilder) carrying a light rider in an open
// helm with a spear at rest, minimal tack, and two player-color accents: the
// saddle cloth and a small back-pennant. OutriderAnimator (attached inside
// Build) drives a 4-beat leg swing from the root position delta, idle weight
// shift + tail sway, and tints the accent materials with the owning player's
// color once the orchestrator has wired EntityReference onto the root.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Input; // EntityReference

namespace TheWaningBorder.Presentation
{
    public static class OutriderVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("OutriderVisual");

            // Palette — chestnut courser, oiled leather tack, plain steel.
            var coat        = new Color(0.43f, 0.29f, 0.18f);
            var maneC       = new Color(0.16f, 0.12f, 0.08f);
            var hoofC       = new Color(0.17f, 0.14f, 0.11f);
            var leather     = new Color(0.40f, 0.26f, 0.15f);
            var leatherDark = new Color(0.27f, 0.17f, 0.10f);
            var tunic       = new Color(0.34f, 0.37f, 0.29f);
            var tunicDark   = new Color(0.25f, 0.27f, 0.21f);
            var steel       = new Color(0.55f, 0.56f, 0.58f);
            var skin        = new Color(0.78f, 0.62f, 0.50f);
            var wood        = new Color(0.36f, 0.25f, 0.14f);
            var accentBase  = new Color(0.88f, 0.86f, 0.82f); // tinted at runtime

            var horse = HorseRigBuilder.Build(root.transform, rng, 0.95f, false,
                coat, maneC, hoofC, steel, out _, out _, out _, out _);
            var h = horse.transform;

            // ── Tack ──
            Part(PrimitiveType.Cube, "SaddleCloth", h,
                new Vector3(0f, 1.29f, -0.06f), new Vector3(0.54f, 0.05f, 0.62f),
                Quaternion.Euler(0f, 0f, HorseRigBuilder.Jitter(rng, 1.5f)), accentBase, 0.0f, 0.25f);
            Part(PrimitiveType.Cube, "Saddle", h,
                new Vector3(0f, 1.355f, -0.06f), new Vector3(0.34f, 0.09f, 0.42f),
                Quaternion.identity, leather, 0.05f, 0.38f);
            Part(PrimitiveType.Cube, "SaddlePommel", h,
                new Vector3(0f, 1.42f, 0.13f), new Vector3(0.10f, 0.08f, 0.06f),
                Quaternion.Euler(-8f, 0f, 0f), leather * 0.9f, 0.05f, 0.40f);
            Part(PrimitiveType.Cube, "SaddleCantle", h,
                new Vector3(0f, 1.43f, -0.26f), new Vector3(0.17f, 0.10f, 0.06f),
                Quaternion.Euler(9f, 0f, 0f), leather * 0.9f, 0.05f, 0.40f);
            Part(PrimitiveType.Cube, "Girth", h,
                new Vector3(0f, 1.00f, -0.06f), new Vector3(0.545f, 0.60f, 0.065f),
                Quaternion.identity, leatherDark, 0.05f, 0.25f);
            Part(PrimitiveType.Cube, "Bridle", h,
                new Vector3(0f, 1.52f, 0.99f), new Vector3(0.135f, 0.03f, 0.17f),
                Quaternion.Euler(42f, 0f, 0f), leatherDark, 0.05f, 0.30f);
            HorseRigBuilder.Strut("Rein_L", h,
                new Vector3(-0.07f, 1.52f, 1.00f), new Vector3(-0.13f, 1.70f, 0.22f),
                0.012f, leatherDark, 0.05f, 0.30f);
            HorseRigBuilder.Strut("Rein_R", h,
                new Vector3(0.07f, 1.52f, 1.00f), new Vector3(0.11f, 1.68f, 0.20f),
                0.012f, leatherDark, 0.05f, 0.30f);

            // ── Rider (pivot bounces in the animator) ──
            var rider = new GameObject("Rider");
            rider.transform.SetParent(h, false);
            rider.transform.localPosition = new Vector3(0f, 1.40f, -0.06f);
            var r = rider.transform;

            Part(PrimitiveType.Cube, "Torso", r,
                new Vector3(0f, 0.30f, 0f), new Vector3(0.30f, 0.42f, 0.20f),
                Quaternion.Euler(HorseRigBuilder.Jitter(rng, 2f), 0f, HorseRigBuilder.Jitter(rng, 2f)),
                tunic, 0.05f, 0.20f);
            Part(PrimitiveType.Cube, "JerkinFront", r,
                new Vector3(0f, 0.30f, 0.105f), new Vector3(0.26f, 0.36f, 0.025f),
                Quaternion.identity, leather, 0.05f, 0.30f);
            Part(PrimitiveType.Cube, "Belt", r,
                new Vector3(0f, 0.12f, 0f), new Vector3(0.32f, 0.05f, 0.22f),
                Quaternion.identity, leatherDark, 0.05f, 0.30f);
            Part(PrimitiveType.Sphere, "RiderHead", r,
                new Vector3(0f, 0.63f, 0.02f), new Vector3(0.19f, 0.20f, 0.19f),
                Quaternion.identity, skin, 0.0f, 0.30f);
            Part(PrimitiveType.Sphere, "HelmDome", r,
                new Vector3(0f, 0.70f, 0.02f), new Vector3(0.21f, 0.15f, 0.21f),
                Quaternion.Euler(0f, 0f, HorseRigBuilder.Jitter(rng, 2f)), steel, 0.70f, 0.45f);
            Part(PrimitiveType.Cylinder, "HelmBrim", r,
                new Vector3(0f, 0.645f, 0.02f), new Vector3(0.235f, 0.015f, 0.235f),
                Quaternion.identity, steel * 0.9f, 0.70f, 0.40f);
            Part(PrimitiveType.Cube, "NasalBar", r,
                new Vector3(0f, 0.61f, 0.115f), new Vector3(0.025f, 0.10f, 0.02f),
                Quaternion.identity, steel * 0.85f, 0.70f, 0.40f);
            Part(PrimitiveType.Sphere, "Shoulder_L", r,
                new Vector3(-0.185f, 0.46f, 0f), new Vector3(0.11f, 0.11f, 0.11f),
                Quaternion.identity, tunicDark, 0.05f, 0.20f);
            Part(PrimitiveType.Sphere, "Shoulder_R", r,
                new Vector3(0.185f, 0.46f, 0f), new Vector3(0.11f, 0.11f, 0.11f),
                Quaternion.identity, tunicDark, 0.05f, 0.20f);
            // Left arm forward to the reins, right arm down to the spear.
            HorseRigBuilder.Strut("Arm_L", r,
                new Vector3(-0.19f, 0.44f, 0.02f), new Vector3(-0.13f, 0.28f, 0.26f),
                0.05f, tunic * 0.95f, 0.05f, 0.20f);
            Part(PrimitiveType.Sphere, "Hand_L", r,
                new Vector3(-0.13f, 0.27f, 0.27f), new Vector3(0.075f, 0.075f, 0.075f),
                Quaternion.identity, skin, 0.0f, 0.30f);
            HorseRigBuilder.Strut("Arm_R", r,
                new Vector3(0.19f, 0.44f, 0.02f), new Vector3(0.27f, 0.15f, 0.04f),
                0.05f, tunic * 0.95f, 0.05f, 0.20f);
            Part(PrimitiveType.Sphere, "Hand_R", r,
                new Vector3(0.27f, 0.13f, 0.05f), new Vector3(0.075f, 0.075f, 0.075f),
                Quaternion.identity, skin, 0.0f, 0.30f);
            // Seated legs hugging the flanks.
            HorseRigBuilder.Strut("Thigh_L", r,
                new Vector3(-0.16f, 0.06f, 0.02f), new Vector3(-0.27f, -0.10f, 0.22f),
                0.06f, tunicDark, 0.05f, 0.18f);
            HorseRigBuilder.Strut("Shin_L", r,
                new Vector3(-0.27f, -0.10f, 0.22f), new Vector3(-0.29f, -0.36f, 0.16f),
                0.045f, leatherDark, 0.05f, 0.25f);
            Part(PrimitiveType.Cube, "Boot_L", r,
                new Vector3(-0.29f, -0.40f, 0.21f), new Vector3(0.09f, 0.07f, 0.17f),
                Quaternion.Euler(0f, HorseRigBuilder.Jitter(rng, 5f), 0f), leatherDark, 0.05f, 0.30f);
            HorseRigBuilder.Strut("Thigh_R", r,
                new Vector3(0.16f, 0.06f, 0.02f), new Vector3(0.27f, -0.10f, 0.22f),
                0.06f, tunicDark, 0.05f, 0.18f);
            HorseRigBuilder.Strut("Shin_R", r,
                new Vector3(0.27f, -0.10f, 0.22f), new Vector3(0.29f, -0.36f, 0.16f),
                0.045f, leatherDark, 0.05f, 0.25f);
            Part(PrimitiveType.Cube, "Boot_R", r,
                new Vector3(0.29f, -0.40f, 0.21f), new Vector3(0.09f, 0.07f, 0.17f),
                Quaternion.Euler(0f, HorseRigBuilder.Jitter(rng, 5f), 0f), leatherDark, 0.05f, 0.30f);

            // ── Spear at rest in the right hand, butt near the stirrup. ──
            var spear = new GameObject("Spear");
            spear.transform.SetParent(r, false);
            spear.transform.localPosition = new Vector3(0.28f, 0.10f, 0.05f);
            var sp = spear.transform;
            Part(PrimitiveType.Cylinder, "SpearShaft", sp,
                new Vector3(0.02f, 0.55f, -0.06f), new Vector3(0.032f, 0.80f, 0.032f),
                Quaternion.Euler(6f, 0f, -3f + HorseRigBuilder.Jitter(rng, 1f)), wood, 0.05f, 0.25f);
            Part(PrimitiveType.Cube, "SpearTip", sp,
                new Vector3(0.062f, 1.37f, -0.145f), new Vector3(0.05f, 0.15f, 0.05f),
                Quaternion.Euler(6f, 0f, -3f), steel, 0.85f, 0.55f);
            Part(PrimitiveType.Cylinder, "SpearButt", sp,
                new Vector3(-0.022f, -0.28f, 0.025f), new Vector3(0.045f, 0.03f, 0.045f),
                Quaternion.Euler(6f, 0f, -3f), steel * 0.8f, 0.70f, 0.40f);

            // ── Back-pennant: short pole off the cantle, cloth tinted. ──
            HorseRigBuilder.Strut("PennantPole", r,
                new Vector3(0f, 0.40f, -0.13f), new Vector3(0.02f, 1.06f, -0.31f),
                0.014f, wood, 0.05f, 0.25f);
            Part(PrimitiveType.Cube, "BackPennant", r,
                new Vector3(0.02f, 0.97f, -0.29f), new Vector3(0.02f, 0.19f, 0.29f),
                Quaternion.Euler(12f, 0f, HorseRigBuilder.Jitter(rng, 3f)), accentBase, 0.0f, 0.20f);

            root.AddComponent<OutriderAnimator>();
            return root;
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent,
            Vector3 lp, Vector3 ls, Quaternion lr, Color color, float metal, float smooth)
            => HorseRigBuilder.Part(type, name, parent, lp, ls, lr, color, metal, smooth);
    }

    /// <summary>
    /// Gait/idle/tint driver for the Outrider rig. Movement is sampled from
    /// the root position delta (SyncTransforms drives the root), so no
    /// Animator assets and no ECS queries beyond the tint lookup.
    /// </summary>
    public class OutriderAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full gait.")]
        public float LegSwingDegrees = 36f;

        [Tooltip("Movement speed (m/s) above which the gait engages.")]
        public float MoveThreshold = 0.25f;

        private Transform _horse;
        private float _horseBaseY;
        private readonly Transform[] _legs = new Transform[4];
        private readonly Quaternion[] _legBase = new Quaternion[4];
        // 4-beat sequence: FL, FR, BL, BR staggered by quarter phases.
        private static readonly float[] LegPhase =
            { 0f, Mathf.PI, Mathf.PI * 0.5f, Mathf.PI * 1.5f };
        private static readonly string[] LegNames = { "LegFL", "LegFR", "LegBL", "LegBR" };

        private Transform _tail;
        private Quaternion _tailBase;
        private Transform _rider;
        private Vector3 _riderBasePos;

        private Vector3 _lastPos;
        private bool _hasLastPos;
        private float _speedSmooth;
        private float _gaitPhase;
        private float _moveBlend;

        private readonly List<Material> _tintMats = new List<Material>();
        private readonly List<Material> _tintGlowMats = new List<Material>();
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Color _factionColor = Color.white;

        void Start()
        {
            _horse = FindDeep(transform, "Horse");
            if (_horse != null) _horseBaseY = _horse.localPosition.y;
            for (int i = 0; i < 4; i++)
            {
                _legs[i] = FindDeep(transform, LegNames[i]);
                if (_legs[i] != null) _legBase[i] = _legs[i].localRotation;
            }
            _tail = FindDeep(transform, "Tail");
            if (_tail != null) _tailBase = _tail.localRotation;
            _rider = FindDeep(transform, "Rider");
            if (_rider != null) _riderBasePos = _rider.localPosition;

            CollectMaterial(transform, "SaddleCloth", _tintMats);
            CollectMaterial(transform, "BackPennant", _tintGlowMats);

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) { _em = world.EntityManager; _emReady = true; }
        }

        void LateUpdate()
        {
            // EntityReference is wired by the orchestrator a few frames after
            // Build returns — keep retrying until the tint lands.
            if (!_tinted) TryTint();

            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;
            float t = Time.time;

            // Movement from position delta (planar).
            Vector3 p = transform.position;
            if (!_hasLastPos) { _lastPos = p; _hasLastPos = true; }
            Vector3 d = p - _lastPos;
            d.y = 0f;
            _lastPos = p;
            float speed = d.magnitude / dt;
            _speedSmooth = Mathf.Lerp(_speedSmooth, speed, 1f - Mathf.Exp(-6f * dt));
            _moveBlend = Mathf.MoveTowards(_moveBlend, _speedSmooth > MoveThreshold ? 1f : 0f, dt * 3f);
            if (_moveBlend > 0.001f)
                _gaitPhase += dt * (Mathf.PI * 2f) * Mathf.Clamp(_speedSmooth * 0.35f, 1.2f, 3.2f);

            // 4-beat leg swing, eased out when standing.
            for (int i = 0; i < 4; i++)
            {
                if (_legs[i] == null) continue;
                float swing = Mathf.Sin(_gaitPhase + LegPhase[i]) * LegSwingDegrees * _moveBlend;
                _legs[i].localRotation = _legBase[i] * Quaternion.Euler(swing, 0f, 0f);
            }

            // Body: gallop bob while moving, slow weight shift while idle.
            if (_horse != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * 0.045f * _moveBlend;
                var lp = _horse.localPosition;
                lp.y = _horseBaseY + bob;
                _horse.localPosition = lp;
                float shift = Mathf.Sin(t * 0.45f) * 1.4f * (1f - _moveBlend);
                _horse.localRotation = Quaternion.Euler(0f, 0f, shift);
            }

            // Tail sway — always alive, quicker on the move.
            if (_tail != null)
            {
                float sway = Mathf.Sin(t * (1.1f + 2.2f * _moveBlend)) * (7f + 6f * _moveBlend);
                _tail.localRotation = _tailBase * Quaternion.Euler(0f, 0f, sway);
            }

            // Rider counter-bounce against the horse bob.
            if (_rider != null)
            {
                var rp = _riderBasePos;
                rp.y += Mathf.Sin(_gaitPhase + 0.6f) * 0.02f * _moveBlend;
                _rider.localPosition = rp;
            }
        }

        private void TryTint()
        {
            if (_entityRef == null)
            {
                _entityRef = GetComponent<EntityReference>();
                if (_entityRef == null) return; // orchestrator not done yet
            }
            if (!_emReady)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                _em = world.EntityManager;
                _emReady = true;
            }
            var e = _entityRef.Entity;
            if (e == Entity.Null || !_em.Exists(e) || !_em.HasComponent<FactionTag>(e)) return;

            _factionColor = FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value);
            for (int i = 0; i < _tintMats.Count; i++)
                _tintMats[i].SetColor("_BaseColor", Color.Lerp(_factionColor, Color.white, 0.15f));
            for (int i = 0; i < _tintGlowMats.Count; i++)
            {
                _tintGlowMats[i].SetColor("_BaseColor", Color.Lerp(_factionColor, Color.white, 0.10f));
                _tintGlowMats[i].EnableKeyword("_EMISSION");
                _tintGlowMats[i].SetColor("_EmissionColor", _factionColor * 0.45f);
            }
            _tinted = true;
        }

        private static void CollectMaterial(Transform root, string partName, List<Material> into)
        {
            var part = FindDeep(root, partName);
            if (part != null && part.TryGetComponent<MeshRenderer>(out var mr))
                into.Add(mr.material); // instance — safe to tint
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
