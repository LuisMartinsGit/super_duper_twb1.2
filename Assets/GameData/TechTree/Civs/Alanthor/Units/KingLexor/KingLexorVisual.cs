// Procedural visual for King Lexor (pid 251) — the Cataphract chassis made
// regal. Black destrier in gilded barding, brass-trimmed plate, crown spikes
// over the helm, a flowing cape of angled slabs, and a tall back-banner with
// an emissive finial (the rig's single glow accent). Player color lands on
// the cape lining and the banner cloth via KingLexorAnimator, which drives
// the shared 4-beat gait plus a slow cape sway and banner flutter. Root
// carries ProceduralScaleTag BaseScale 1.15 so the king reads larger.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class KingLexorVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("KingLexorVisual");
            root.AddComponent<ProceduralScaleTag>().BaseScale = 1.15f;

            // Palette — midnight coat, brass and gold over dark iron.
            var coat        = new Color(0.12f, 0.10f, 0.10f);
            var maneC       = new Color(0.07f, 0.06f, 0.06f);
            var hoofC       = new Color(0.55f, 0.44f, 0.20f); // gilt-shod
            var brass       = new Color(0.66f, 0.50f, 0.20f);
            var gold        = new Color(0.80f, 0.64f, 0.26f);
            var ironDark    = new Color(0.22f, 0.22f, 0.25f);
            var steel       = new Color(0.55f, 0.56f, 0.60f);
            var leather     = new Color(0.30f, 0.19f, 0.11f);
            var capeOuter   = new Color(0.34f, 0.08f, 0.10f); // royal crimson
            var wood        = new Color(0.30f, 0.21f, 0.12f);
            var accentBase  = new Color(0.88f, 0.86f, 0.82f); // tinted at runtime

            var horse = HorseRigBuilder.Build(root.transform, rng, 1.12f, true,
                coat, maneC, hoofC, brass, out _, out _, out _, out _);
            var h = horse.transform;

            // ── Gilded barding: flank plates with gold edging, spine plates. ──
            for (int i = 0; i < 4; i++)
            {
                float z = 0.50f - i * 0.32f;
                foreach (int side in new[] { -1, 1 })
                {
                    string sideTag = side < 0 ? "L" : "R";
                    float lean = -14f * side + HorseRigBuilder.Jitter(rng, 2f);
                    Part(PrimitiveType.Cube, $"Barding_{sideTag}_{i + 1}", h,
                        new Vector3(side * 0.32f, 0.90f, z), new Vector3(0.09f, 0.48f, 0.31f),
                        Quaternion.Euler(0f, 0f, lean), ironDark, 0.80f, 0.45f);
                    Part(PrimitiveType.Cube, $"BardingTrim_{sideTag}_{i + 1}", h,
                        new Vector3(side * 0.37f, 0.68f, z), new Vector3(0.075f, 0.06f, 0.315f),
                        Quaternion.Euler(0f, 0f, lean), gold, 0.90f, 0.60f);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                Part(PrimitiveType.Cube, $"SpinePlate_{i + 1}", h,
                    new Vector3(0f, 1.34f, 0.30f - i * 0.34f), new Vector3(0.27f, 0.05f, 0.30f),
                    Quaternion.Euler(HorseRigBuilder.Jitter(rng, 2f), 0f, 0f), brass, 0.90f, 0.55f);
            }

            // ── Gilded chamfron with a brass plume socket. ──
            Part(PrimitiveType.Cube, "Chamfron", h,
                new Vector3(0f, 1.62f, 0.97f), new Vector3(0.16f, 0.055f, 0.34f),
                Quaternion.Euler(42f, 0f, 0f), brass, 0.90f, 0.60f);
            Part(PrimitiveType.Cylinder, "ChamfronPlume", h,
                new Vector3(0f, 1.86f, 0.80f), new Vector3(0.04f, 0.10f, 0.04f),
                Quaternion.Euler(-14f, 0f, HorseRigBuilder.Jitter(rng, 3f)), capeOuter, 0.10f, 0.20f);

            // ── Tack — richer than the line trooper's. ──
            Part(PrimitiveType.Cube, "Saddle", h,
                new Vector3(0f, 1.37f, -0.08f), new Vector3(0.36f, 0.10f, 0.44f),
                Quaternion.identity, leather, 0.05f, 0.40f);
            Part(PrimitiveType.Cube, "SaddleTrim", h,
                new Vector3(0f, 1.415f, -0.08f), new Vector3(0.38f, 0.02f, 0.46f),
                Quaternion.identity, gold, 0.90f, 0.60f);
            Part(PrimitiveType.Cube, "Girth", h,
                new Vector3(0f, 1.00f, -0.08f), new Vector3(0.64f, 0.60f, 0.07f),
                Quaternion.identity, leather * 0.8f, 0.05f, 0.25f);
            HorseRigBuilder.Strut("Rein_L", h,
                new Vector3(-0.07f, 1.50f, 1.02f), new Vector3(-0.13f, 1.72f, 0.20f),
                0.012f, leather * 0.7f, 0.05f, 0.30f);
            HorseRigBuilder.Strut("Rein_R", h,
                new Vector3(0.07f, 1.50f, 1.02f), new Vector3(0.11f, 1.70f, 0.18f),
                0.012f, leather * 0.7f, 0.05f, 0.30f);

            // ── The king — crowned closed helm, brass-edged plate. ──
            var rider = new GameObject("Rider");
            rider.transform.SetParent(h, false);
            rider.transform.localPosition = new Vector3(0f, 1.42f, -0.08f);
            var r = rider.transform;

            Part(PrimitiveType.Cube, "Torso", r,
                new Vector3(0f, 0.30f, 0f), new Vector3(0.35f, 0.45f, 0.24f),
                Quaternion.Euler(HorseRigBuilder.Jitter(rng, 1.5f), 0f, HorseRigBuilder.Jitter(rng, 1.5f)),
                ironDark, 0.80f, 0.45f);
            Part(PrimitiveType.Cube, "Breastplate", r,
                new Vector3(0f, 0.33f, 0.125f), new Vector3(0.31f, 0.35f, 0.035f),
                Quaternion.identity, steel, 0.90f, 0.60f);
            Part(PrimitiveType.Cube, "GorgetTrim", r,
                new Vector3(0f, 0.52f, 0.09f), new Vector3(0.24f, 0.045f, 0.05f),
                Quaternion.identity, gold, 0.90f, 0.65f);
            Part(PrimitiveType.Cube, "Tasset_L", r,
                new Vector3(-0.155f, 0.05f, 0.02f), new Vector3(0.13f, 0.16f, 0.24f),
                Quaternion.Euler(0f, 0f, -8f), ironDark, 0.80f, 0.45f);
            Part(PrimitiveType.Cube, "Tasset_R", r,
                new Vector3(0.155f, 0.05f, 0.02f), new Vector3(0.13f, 0.16f, 0.24f),
                Quaternion.Euler(0f, 0f, 8f), ironDark, 0.80f, 0.45f);
            Part(PrimitiveType.Sphere, "RiderHead", r,
                new Vector3(0f, 0.64f, 0.02f), new Vector3(0.19f, 0.20f, 0.19f),
                Quaternion.identity, ironDark, 0.60f, 0.35f);
            Part(PrimitiveType.Sphere, "HelmDome", r,
                new Vector3(0f, 0.70f, 0.02f), new Vector3(0.22f, 0.17f, 0.22f),
                Quaternion.identity, steel, 0.90f, 0.60f);
            Part(PrimitiveType.Cube, "FacePlate", r,
                new Vector3(0f, 0.63f, 0.115f), new Vector3(0.16f, 0.14f, 0.03f),
                Quaternion.Euler(-6f, 0f, 0f), steel * 0.95f, 0.85f, 0.55f);
            Part(PrimitiveType.Cube, "EyeSlit", r,
                new Vector3(0f, 0.665f, 0.132f), new Vector3(0.12f, 0.018f, 0.012f),
                Quaternion.Euler(-6f, 0f, 0f), new Color(0.05f, 0.05f, 0.06f), 0.20f, 0.10f);
            // Crown: a brass circlet ringed by five gold spikes.
            Part(PrimitiveType.Cylinder, "CrownBand", r,
                new Vector3(0f, 0.755f, 0.02f), new Vector3(0.20f, 0.022f, 0.20f),
                Quaternion.identity, brass, 0.90f, 0.65f);
            for (int i = 0; i < 5; i++)
            {
                float ang = i * Mathf.PI * 2f / 5f + 0.25f;
                Part(PrimitiveType.Cube, $"CrownSpike_{i + 1}", r,
                    new Vector3(Mathf.Sin(ang) * 0.095f, 0.815f, 0.02f + Mathf.Cos(ang) * 0.095f),
                    new Vector3(0.028f, 0.075f, 0.028f),
                    Quaternion.Euler(Mathf.Cos(ang) * 10f, 0f, -Mathf.Sin(ang) * 10f),
                    gold, 0.90f, 0.65f);
            }
            Part(PrimitiveType.Sphere, "Pauldron_L", r,
                new Vector3(-0.21f, 0.47f, 0f), new Vector3(0.15f, 0.13f, 0.15f),
                Quaternion.identity, steel, 0.90f, 0.55f);
            Part(PrimitiveType.Sphere, "Pauldron_R", r,
                new Vector3(0.21f, 0.47f, 0f), new Vector3(0.15f, 0.13f, 0.15f),
                Quaternion.identity, steel, 0.90f, 0.55f);
            HorseRigBuilder.Strut("Arm_L", r,
                new Vector3(-0.21f, 0.44f, 0.02f), new Vector3(-0.15f, 0.28f, 0.24f),
                0.055f, ironDark, 0.75f, 0.40f);
            Part(PrimitiveType.Sphere, "Gauntlet_L", r,
                new Vector3(-0.15f, 0.26f, 0.25f), new Vector3(0.085f, 0.085f, 0.085f),
                Quaternion.identity, brass, 0.85f, 0.55f);
            HorseRigBuilder.Strut("Arm_R", r,
                new Vector3(0.21f, 0.44f, 0.02f), new Vector3(0.30f, 0.18f, 0.06f),
                0.055f, ironDark, 0.75f, 0.40f);
            Part(PrimitiveType.Sphere, "Gauntlet_R", r,
                new Vector3(0.30f, 0.16f, 0.07f), new Vector3(0.085f, 0.085f, 0.085f),
                Quaternion.identity, brass, 0.85f, 0.55f);
            HorseRigBuilder.Strut("Cuisse_L", r,
                new Vector3(-0.17f, 0.05f, 0.02f), new Vector3(-0.29f, -0.12f, 0.22f),
                0.065f, ironDark, 0.75f, 0.40f);
            HorseRigBuilder.Strut("Greave_L", r,
                new Vector3(-0.29f, -0.12f, 0.22f), new Vector3(-0.31f, -0.38f, 0.16f),
                0.05f, steel, 0.85f, 0.50f);
            Part(PrimitiveType.Cube, "Sabaton_L", r,
                new Vector3(-0.31f, -0.42f, 0.21f), new Vector3(0.09f, 0.07f, 0.18f),
                Quaternion.identity, brass, 0.85f, 0.55f);
            HorseRigBuilder.Strut("Cuisse_R", r,
                new Vector3(0.17f, 0.05f, 0.02f), new Vector3(0.29f, -0.12f, 0.22f),
                0.065f, ironDark, 0.75f, 0.40f);
            HorseRigBuilder.Strut("Greave_R", r,
                new Vector3(0.29f, -0.12f, 0.22f), new Vector3(0.31f, -0.38f, 0.16f),
                0.05f, steel, 0.85f, 0.50f);
            Part(PrimitiveType.Cube, "Sabaton_R", r,
                new Vector3(0.31f, -0.42f, 0.21f), new Vector3(0.09f, 0.07f, 0.18f),
                Quaternion.identity, brass, 0.85f, 0.55f);

            // ── Royal sword sheathed at the left hip (the king points, his
            //    army fights). ──
            Part(PrimitiveType.Cube, "Scabbard", r,
                new Vector3(-0.20f, -0.02f, -0.10f), new Vector3(0.045f, 0.42f, 0.07f),
                Quaternion.Euler(8f, 0f, 14f), leather * 0.8f, 0.10f, 0.35f);
            Part(PrimitiveType.Cube, "SwordHilt", r,
                new Vector3(-0.255f, 0.22f, -0.135f), new Vector3(0.12f, 0.028f, 0.03f),
                Quaternion.Euler(8f, 0f, 14f), gold, 0.90f, 0.65f);

            // ── Cape: three angled slabs flowing over the rump; the lining
            //    slab is the tint accent. Hung from a sway pivot. ──
            var cape = new GameObject("Cape");
            cape.transform.SetParent(r, false);
            cape.transform.localPosition = new Vector3(0f, 0.50f, -0.12f);
            var cp = cape.transform;
            Part(PrimitiveType.Cube, "CapeUpper", cp,
                new Vector3(0f, -0.14f, -0.10f), new Vector3(0.42f, 0.30f, 0.022f),
                Quaternion.Euler(-24f, 0f, HorseRigBuilder.Jitter(rng, 2f)), capeOuter, 0.05f, 0.18f);
            Part(PrimitiveType.Cube, "CapeMid", cp,
                new Vector3(0f, -0.40f, -0.24f), new Vector3(0.48f, 0.32f, 0.02f),
                Quaternion.Euler(-38f, 0f, HorseRigBuilder.Jitter(rng, 2f)), capeOuter * 0.92f, 0.05f, 0.16f);
            Part(PrimitiveType.Cube, "CapeLower", cp,
                new Vector3(0f, -0.62f, -0.42f), new Vector3(0.52f, 0.30f, 0.02f),
                Quaternion.Euler(-52f, 0f, HorseRigBuilder.Jitter(rng, 3f)), capeOuter * 0.85f, 0.05f, 0.15f);
            Part(PrimitiveType.Cube, "CapeLining", cp,
                new Vector3(0f, -0.40f, -0.215f), new Vector3(0.44f, 0.30f, 0.012f),
                Quaternion.Euler(-38f, 0f, 0f), accentBase, 0.05f, 0.25f);
            Part(PrimitiveType.Sphere, "CapeClasp_L", cp,
                new Vector3(-0.17f, 0.005f, 0.02f), new Vector3(0.05f, 0.05f, 0.05f),
                Quaternion.identity, gold, 0.90f, 0.65f);
            Part(PrimitiveType.Sphere, "CapeClasp_R", cp,
                new Vector3(0.17f, 0.005f, 0.02f), new Vector3(0.05f, 0.05f, 0.05f),
                Quaternion.identity, gold, 0.90f, 0.65f);

            // ── Tall back-banner: pole socketed behind the saddle, tinted
            //    cloth on a flutter pivot, emissive finial at the tip. ──
            HorseRigBuilder.Strut("FlagPole", r,
                new Vector3(0.06f, 0.10f, -0.20f), new Vector3(0.09f, 1.55f, -0.34f),
                0.018f, wood, 0.05f, 0.25f);
            Part(PrimitiveType.Cylinder, "FlagPoleCap", r,
                new Vector3(0.088f, 1.47f, -0.332f), new Vector3(0.035f, 0.02f, 0.035f),
                Quaternion.identity, brass, 0.90f, 0.60f);
            var bannerPivot = new GameObject("BannerPivot");
            bannerPivot.transform.SetParent(r, false);
            bannerPivot.transform.localPosition = new Vector3(0.09f, 1.42f, -0.335f);
            Part(PrimitiveType.Cube, "Banner", bannerPivot.transform,
                new Vector3(0f, -0.26f, -0.14f), new Vector3(0.022f, 0.55f, 0.30f),
                Quaternion.Euler(0f, 0f, HorseRigBuilder.Jitter(rng, 2f)), accentBase, 0.0f, 0.22f);
            Part(PrimitiveType.Sphere, "Finial", r,
                new Vector3(0.092f, 1.56f, -0.345f), new Vector3(0.075f, 0.075f, 0.075f),
                Quaternion.identity, gold, 0.90f, 0.70f, glow: true);

            root.AddComponent<KingLexorAnimator>();
            return root;
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent,
            Vector3 lp, Vector3 ls, Quaternion lr, Color color, float metal, float smooth, bool glow = false)
            => HorseRigBuilder.Part(type, name, parent, lp, ls, lr, color, metal, smooth, glow);
    }

    /// <summary>
    /// Gait/idle/tint driver for King Lexor — the shared position-delta gait
    /// plus a slow cape sway and banner flutter. Faction tint lands on the
    /// cape lining and the banner cloth (albedo only; the gilded finial is
    /// the rig's one emissive accent).
    /// </summary>
    public class KingLexorAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full gait.")]
        public float LegSwingDegrees = 32f;

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
        private Transform _cape;
        private Quaternion _capeBase;
        private Transform _banner;
        private Quaternion _bannerBase;

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
            _cape = FindDeep(transform, "Cape");
            if (_cape != null) _capeBase = _cape.localRotation;
            _banner = FindDeep(transform, "BannerPivot");
            if (_banner != null) _bannerBase = _banner.localRotation;

            CollectMaterial(transform, "CapeLining", _tintMats);
            CollectMaterial(transform, "Banner", _tintMats);

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
                _gaitPhase += dt * (Mathf.PI * 2f) * Mathf.Clamp(_speedSmooth * 0.35f, 1.1f, 3.0f);

            for (int i = 0; i < 4; i++)
            {
                if (_legs[i] == null) continue;
                float swing = Mathf.Sin(_gaitPhase + LegPhase[i]) * LegSwingDegrees * _moveBlend;
                _legs[i].localRotation = _legBase[i] * Quaternion.Euler(swing, 0f, 0f);
            }

            if (_horse != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * 0.04f * _moveBlend;
                var lp = _horse.localPosition;
                lp.y = _horseBaseY + bob;
                _horse.localPosition = lp;
                float shift = Mathf.Sin(t * 0.4f) * 1.2f * (1f - _moveBlend);
                _horse.localRotation = Quaternion.Euler(0f, 0f, shift);
            }

            if (_tail != null)
            {
                float sway = Mathf.Sin(t * (1.0f + 2.0f * _moveBlend)) * (6f + 5f * _moveBlend);
                _tail.localRotation = _tailBase * Quaternion.Euler(0f, 0f, sway);
            }

            if (_rider != null)
            {
                var rp = _riderBasePos;
                rp.y += Mathf.Sin(_gaitPhase + 0.6f) * 0.018f * _moveBlend;
                _rider.localPosition = rp;
            }

            // Slow cape sway: breathes at idle, streams back on the move.
            if (_cape != null)
            {
                float billow = Mathf.Sin(t * 0.8f) * 3.5f + Mathf.Sin(t * 1.7f) * 1.2f;
                float stream = _moveBlend * 12f;
                _cape.localRotation = _capeBase * Quaternion.Euler(
                    -billow - stream, Mathf.Sin(t * 0.5f) * 2f, 0f);
            }

            // Banner flutter: quick, small, slightly faster with speed.
            if (_banner != null)
            {
                float f = 2.6f + 1.5f * _moveBlend;
                _banner.localRotation = _bannerBase * Quaternion.Euler(
                    Mathf.Sin(t * f * 0.9f) * 3f, Mathf.Sin(t * f) * 7f, Mathf.Sin(t * f * 1.3f) * 3f);
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
                _tintMats[i].SetColor("_BaseColor", Color.Lerp(_factionColor, Color.white, 0.10f));
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
