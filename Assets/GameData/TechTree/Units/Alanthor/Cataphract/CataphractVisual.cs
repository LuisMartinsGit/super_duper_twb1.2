// File: Assets/GameData/TechTree/Units/Alanthor/Cataphract/CataphractVisual.cs
// Procedural visual for the Cataphract (pid 336) — Alanthor heavy shock
// cavalry. The shared HorseRigBuilder chassis in armored trim, wrapped in
// overlapping caparison plates along the barrel, a chamfron head plate, and
// a rider in a closed helm with an upright lance and kite shield. Player
// color lands on the caparison edging strips and the shield face at runtime
// via CataphractAnimator (LedgerVisual TryTint pattern), which also drives
// the 4-beat gait from the root position delta.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Input; // EntityReference

namespace TheWaningBorder.Presentation
{
    public static class CataphractVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("CataphractVisual");

            // Palette — dark bay hide under lamellar iron, cool steel edges.
            var coat        = new Color(0.25f, 0.18f, 0.13f);
            var maneC       = new Color(0.12f, 0.09f, 0.07f);
            var hoofC       = new Color(0.30f, 0.31f, 0.33f); // iron-shod
            var iron        = new Color(0.35f, 0.36f, 0.38f);
            var ironDark    = new Color(0.23f, 0.24f, 0.26f);
            var steel       = new Color(0.56f, 0.58f, 0.61f);
            var leather     = new Color(0.32f, 0.21f, 0.13f);
            var leatherDark = new Color(0.22f, 0.14f, 0.09f);
            var cloth       = new Color(0.33f, 0.33f, 0.37f); // caparison skirt
            var wood        = new Color(0.33f, 0.23f, 0.13f);
            var accentBase  = new Color(0.88f, 0.86f, 0.82f); // tinted at runtime
            var slitDark    = new Color(0.05f, 0.05f, 0.06f);

            var horse = HorseRigBuilder.Build(root.transform, rng, 1.1f, true,
                coat, maneC, hoofC, iron, out _, out _, out _, out _);
            var h = horse.transform;

            // ── Caparison: overlapping plates hanging down both flanks, plus
            //    a spine plate row; each plate carries a tintable edge strip. ──
            int edgeIndex = 1;
            for (int i = 0; i < 4; i++)
            {
                float z = 0.50f - i * 0.32f;
                foreach (int side in new[] { -1, 1 })
                {
                    string sideTag = side < 0 ? "L" : "R";
                    float lean = -14f * side + HorseRigBuilder.Jitter(rng, 2f);
                    Part(PrimitiveType.Cube, $"Caparison_{sideTag}_{i + 1}", h,
                        new Vector3(side * 0.315f, 0.90f, z), new Vector3(0.09f, 0.48f, 0.31f),
                        Quaternion.Euler(0f, 0f, lean), cloth, 0.15f, 0.30f);
                    Part(PrimitiveType.Cube, $"CaparisonEdge_{edgeIndex}", h,
                        new Vector3(side * 0.365f, 0.68f, z), new Vector3(0.075f, 0.06f, 0.315f),
                        Quaternion.Euler(0f, 0f, lean), accentBase, 0.10f, 0.35f);
                    edgeIndex++;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                Part(PrimitiveType.Cube, $"SpinePlate_{i + 1}", h,
                    new Vector3(0f, 1.335f + HorseRigBuilder.Jitter(rng, 0.5f) * 0.01f, 0.30f - i * 0.34f),
                    new Vector3(0.26f, 0.05f, 0.30f),
                    Quaternion.Euler(HorseRigBuilder.Jitter(rng, 2f), 0f, 0f), iron, 0.85f, 0.50f);
            }

            // ── Chamfron: face plate + brow ridge on the head. ──
            Part(PrimitiveType.Cube, "Chamfron", h,
                new Vector3(0f, 1.62f, 0.97f), new Vector3(0.16f, 0.055f, 0.34f),
                Quaternion.Euler(42f, 0f, 0f), iron, 0.85f, 0.55f);
            Part(PrimitiveType.Cube, "ChamfronRidge", h,
                new Vector3(0f, 1.72f, 0.88f), new Vector3(0.05f, 0.045f, 0.20f),
                Quaternion.Euler(42f, 0f, 0f), steel, 0.90f, 0.60f);

            // ── Tack ──
            Part(PrimitiveType.Cube, "Saddle", h,
                new Vector3(0f, 1.37f, -0.08f), new Vector3(0.36f, 0.10f, 0.44f),
                Quaternion.identity, leather, 0.05f, 0.38f);
            Part(PrimitiveType.Cube, "SaddleCantle", h,
                new Vector3(0f, 1.45f, -0.29f), new Vector3(0.19f, 0.11f, 0.06f),
                Quaternion.Euler(9f, 0f, 0f), leather * 0.9f, 0.05f, 0.40f);
            Part(PrimitiveType.Cube, "Girth", h,
                new Vector3(0f, 1.00f, -0.08f), new Vector3(0.63f, 0.60f, 0.07f),
                Quaternion.identity, leatherDark, 0.05f, 0.25f);
            HorseRigBuilder.Strut("Rein_L", h,
                new Vector3(-0.07f, 1.50f, 1.02f), new Vector3(-0.13f, 1.72f, 0.20f),
                0.012f, leatherDark, 0.05f, 0.30f);
            HorseRigBuilder.Strut("Rein_R", h,
                new Vector3(0.07f, 1.50f, 1.02f), new Vector3(0.11f, 1.70f, 0.18f),
                0.012f, leatherDark, 0.05f, 0.30f);

            // ── Rider — fully enclosed, heavier mass than the Outrider. ──
            var rider = new GameObject("Rider");
            rider.transform.SetParent(h, false);
            rider.transform.localPosition = new Vector3(0f, 1.42f, -0.08f);
            var r = rider.transform;

            Part(PrimitiveType.Cube, "Torso", r,
                new Vector3(0f, 0.30f, 0f), new Vector3(0.34f, 0.44f, 0.24f),
                Quaternion.Euler(HorseRigBuilder.Jitter(rng, 1.5f), 0f, HorseRigBuilder.Jitter(rng, 1.5f)),
                ironDark, 0.80f, 0.45f);
            Part(PrimitiveType.Cube, "Breastplate", r,
                new Vector3(0f, 0.33f, 0.125f), new Vector3(0.30f, 0.34f, 0.035f),
                Quaternion.identity, iron, 0.85f, 0.55f);
            Part(PrimitiveType.Cube, "Backplate", r,
                new Vector3(0f, 0.33f, -0.125f), new Vector3(0.30f, 0.34f, 0.035f),
                Quaternion.identity, iron * 0.92f, 0.85f, 0.50f);
            Part(PrimitiveType.Cube, "Tasset_L", r,
                new Vector3(-0.15f, 0.05f, 0.02f), new Vector3(0.13f, 0.16f, 0.24f),
                Quaternion.Euler(0f, 0f, -8f), iron * 0.9f, 0.80f, 0.45f);
            Part(PrimitiveType.Cube, "Tasset_R", r,
                new Vector3(0.15f, 0.05f, 0.02f), new Vector3(0.13f, 0.16f, 0.24f),
                Quaternion.Euler(0f, 0f, 8f), iron * 0.9f, 0.80f, 0.45f);
            Part(PrimitiveType.Sphere, "RiderHead", r,
                new Vector3(0f, 0.64f, 0.02f), new Vector3(0.19f, 0.20f, 0.19f),
                Quaternion.identity, ironDark, 0.60f, 0.35f);
            // Closed helm: dome, skirt, face plate with a dark eye slit.
            Part(PrimitiveType.Sphere, "HelmDome", r,
                new Vector3(0f, 0.70f, 0.02f), new Vector3(0.22f, 0.17f, 0.22f),
                Quaternion.identity, steel, 0.85f, 0.55f);
            Part(PrimitiveType.Cylinder, "HelmSkirt", r,
                new Vector3(0f, 0.60f, 0.02f), new Vector3(0.215f, 0.055f, 0.215f),
                Quaternion.identity, iron, 0.85f, 0.50f);
            Part(PrimitiveType.Cube, "FacePlate", r,
                new Vector3(0f, 0.63f, 0.115f), new Vector3(0.16f, 0.14f, 0.03f),
                Quaternion.Euler(-6f, 0f, 0f), steel * 0.95f, 0.85f, 0.55f);
            Part(PrimitiveType.Cube, "EyeSlit", r,
                new Vector3(0f, 0.665f, 0.132f), new Vector3(0.12f, 0.018f, 0.012f),
                Quaternion.Euler(-6f, 0f, 0f), slitDark, 0.20f, 0.10f);
            Part(PrimitiveType.Sphere, "Pauldron_L", r,
                new Vector3(-0.205f, 0.47f, 0f), new Vector3(0.14f, 0.13f, 0.14f),
                Quaternion.identity, iron, 0.85f, 0.50f);
            Part(PrimitiveType.Sphere, "Pauldron_R", r,
                new Vector3(0.205f, 0.47f, 0f), new Vector3(0.14f, 0.13f, 0.14f),
                Quaternion.identity, iron, 0.85f, 0.50f);
            // Left arm carries the shield, right arm grips the lance.
            HorseRigBuilder.Strut("Arm_L", r,
                new Vector3(-0.21f, 0.44f, 0.02f), new Vector3(-0.30f, 0.24f, 0.10f),
                0.055f, ironDark, 0.75f, 0.40f);
            HorseRigBuilder.Strut("Arm_R", r,
                new Vector3(0.21f, 0.44f, 0.02f), new Vector3(0.30f, 0.18f, 0.06f),
                0.055f, ironDark, 0.75f, 0.40f);
            Part(PrimitiveType.Sphere, "Gauntlet_R", r,
                new Vector3(0.30f, 0.16f, 0.07f), new Vector3(0.085f, 0.085f, 0.085f),
                Quaternion.identity, steel, 0.85f, 0.50f);
            // Armored legs down the flanks.
            HorseRigBuilder.Strut("Cuisse_L", r,
                new Vector3(-0.17f, 0.05f, 0.02f), new Vector3(-0.29f, -0.12f, 0.22f),
                0.065f, ironDark, 0.75f, 0.40f);
            HorseRigBuilder.Strut("Greave_L", r,
                new Vector3(-0.29f, -0.12f, 0.22f), new Vector3(-0.31f, -0.38f, 0.16f),
                0.05f, iron, 0.80f, 0.45f);
            Part(PrimitiveType.Cube, "Sabaton_L", r,
                new Vector3(-0.31f, -0.42f, 0.21f), new Vector3(0.09f, 0.07f, 0.18f),
                Quaternion.identity, iron, 0.80f, 0.45f);
            HorseRigBuilder.Strut("Cuisse_R", r,
                new Vector3(0.17f, 0.05f, 0.02f), new Vector3(0.29f, -0.12f, 0.22f),
                0.065f, ironDark, 0.75f, 0.40f);
            HorseRigBuilder.Strut("Greave_R", r,
                new Vector3(0.29f, -0.12f, 0.22f), new Vector3(0.31f, -0.38f, 0.16f),
                0.05f, iron, 0.80f, 0.45f);
            Part(PrimitiveType.Cube, "Sabaton_R", r,
                new Vector3(0.31f, -0.42f, 0.21f), new Vector3(0.09f, 0.07f, 0.18f),
                Quaternion.identity, iron, 0.80f, 0.45f);

            // ── Lance held upright in the right hand. ──
            var lance = new GameObject("Lance");
            lance.transform.SetParent(r, false);
            lance.transform.localPosition = new Vector3(0.31f, 0.16f, 0.07f);
            var ln = lance.transform;
            Part(PrimitiveType.Cylinder, "LanceShaft", ln,
                new Vector3(0.01f, 0.75f, -0.02f), new Vector3(0.04f, 1.05f, 0.04f),
                Quaternion.Euler(3f, 0f, -2f + HorseRigBuilder.Jitter(rng, 1f)), wood, 0.05f, 0.28f);
            Part(PrimitiveType.Cylinder, "LanceGuard", ln,
                new Vector3(0f, 0.14f, 0f), new Vector3(0.13f, 0.02f, 0.13f),
                Quaternion.Euler(3f, 0f, -2f), steel, 0.85f, 0.55f);
            Part(PrimitiveType.Cube, "LanceTip", ln,
                new Vector3(-0.026f, 1.86f, 0.037f), new Vector3(0.05f, 0.17f, 0.05f),
                Quaternion.Euler(3f, 0f, -2f), steel, 0.90f, 0.60f);
            Part(PrimitiveType.Cylinder, "LanceButt", ln,
                new Vector3(0.024f, -0.32f, -0.04f), new Vector3(0.05f, 0.03f, 0.05f),
                Quaternion.Euler(3f, 0f, -2f), iron, 0.80f, 0.45f);

            // ── Kite shield hung on the left side, face outward. ──
            var shield = new GameObject("Shield");
            shield.transform.SetParent(r, false);
            shield.transform.localPosition = new Vector3(-0.34f, 0.28f, 0.10f);
            shield.transform.localRotation = Quaternion.Euler(0f, 4f, -7f + HorseRigBuilder.Jitter(rng, 2f));
            var sh = shield.transform;
            Part(PrimitiveType.Cube, "ShieldBody", sh,
                new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.42f, 0.34f),
                Quaternion.identity, ironDark, 0.60f, 0.35f);
            Part(PrimitiveType.Cube, "ShieldTaper", sh,
                new Vector3(0f, -0.30f, 0f), new Vector3(0.05f, 0.22f, 0.20f),
                Quaternion.identity, ironDark, 0.60f, 0.35f);
            Part(PrimitiveType.Cube, "ShieldFace", sh,
                new Vector3(-0.03f, 0.02f, 0f), new Vector3(0.012f, 0.38f, 0.30f),
                Quaternion.identity, accentBase, 0.15f, 0.40f);
            Part(PrimitiveType.Sphere, "ShieldBoss", sh,
                new Vector3(-0.045f, 0.06f, 0f), new Vector3(0.09f, 0.09f, 0.045f),
                Quaternion.identity, steel, 0.90f, 0.60f);

            root.AddComponent<CataphractAnimator>();
            return root;
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent,
            Vector3 lp, Vector3 ls, Quaternion lr, Color color, float metal, float smooth)
            => HorseRigBuilder.Part(type, name, parent, lp, ls, lr, color, metal, smooth);
    }

    /// <summary>
    /// Gait/idle/tint driver for the Cataphract rig — same position-delta
    /// gait as the Outrider (heavier tuning) with faction tint on the
    /// caparison edging strips and the shield face.
    /// </summary>
    public class CataphractAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full gait.")]
        public float LegSwingDegrees = 30f;

        [Tooltip("Movement speed (m/s) above which the gait engages.")]
        public float MoveThreshold = 0.25f;

        private Transform _horse;
        private float _horseBaseY;
        private readonly Transform[] _legs = new Transform[4];
        private readonly Quaternion[] _legBase = new Quaternion[4];
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

            CollectByPrefix(transform, "CaparisonEdge", _tintMats);
            CollectByPrefix(transform, "ShieldFace", _tintMats);

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) { _em = world.EntityManager; _emReady = true; }
        }

        void LateUpdate()
        {
            if (!_tinted) TryTint();

            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;
            float t = Time.time;

            Vector3 p = transform.position;
            if (!_hasLastPos) { _lastPos = p; _hasLastPos = true; }
            Vector3 d = p - _lastPos;
            d.y = 0f;
            _lastPos = p;
            float speed = d.magnitude / dt;
            _speedSmooth = Mathf.Lerp(_speedSmooth, speed, 1f - Mathf.Exp(-6f * dt));
            _moveBlend = Mathf.MoveTowards(_moveBlend, _speedSmooth > MoveThreshold ? 1f : 0f, dt * 3f);
            if (_moveBlend > 0.001f)
                _gaitPhase += dt * (Mathf.PI * 2f) * Mathf.Clamp(_speedSmooth * 0.35f, 1.0f, 2.8f);

            for (int i = 0; i < 4; i++)
            {
                if (_legs[i] == null) continue;
                float swing = Mathf.Sin(_gaitPhase + LegPhase[i]) * LegSwingDegrees * _moveBlend;
                _legs[i].localRotation = _legBase[i] * Quaternion.Euler(swing, 0f, 0f);
            }

            if (_horse != null)
            {
                // Heavier horse: shallower, slower bob; ponderous idle shift.
                float bob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * 0.035f * _moveBlend;
                var lp = _horse.localPosition;
                lp.y = _horseBaseY + bob;
                _horse.localPosition = lp;
                float shift = Mathf.Sin(t * 0.35f) * 1.1f * (1f - _moveBlend);
                _horse.localRotation = Quaternion.Euler(0f, 0f, shift);
            }

            if (_tail != null)
            {
                float sway = Mathf.Sin(t * (0.9f + 1.8f * _moveBlend)) * (5f + 5f * _moveBlend);
                _tail.localRotation = _tailBase * Quaternion.Euler(0f, 0f, sway);
            }

            if (_rider != null)
            {
                var rp = _riderBasePos;
                rp.y += Mathf.Sin(_gaitPhase + 0.6f) * 0.015f * _moveBlend;
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
                _tintMats[i].SetColor("_BaseColor", Color.Lerp(_factionColor, Color.white, 0.12f));
            _tinted = true;
        }

        private static void CollectByPrefix(Transform root, string prefix, List<Material> into)
        {
            if (root.name.StartsWith(prefix) && root.TryGetComponent<MeshRenderer>(out var mr))
                into.Add(mr.material); // instance — safe to tint
            for (int i = 0; i < root.childCount; i++)
                CollectByPrefix(root.GetChild(i), prefix, into);
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
