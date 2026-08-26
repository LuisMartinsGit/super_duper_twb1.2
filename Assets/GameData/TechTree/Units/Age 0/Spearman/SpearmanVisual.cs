// File: Assets/GameData/TechTree/Units/Age 0/Spearman/SpearmanVisual.cs
// Procedural line-infantry visual for the Age 0 Spearman (pid 359): a LIGHT
// soldier — quilted gambeson (cloth, no plate), kettle helm with brim,
// upright 2.4 m spear (leaf blade + butt spike), round shield with boss on
// the left arm, scabbarded knife, simple boots. Built entirely from
// primitives (Smelter idiom — per-part URP/Lit material, metallic/smoothness
// contrast, small deterministic tilts, colliders destroyed). Player-color
// accents (Shield_Face, Tunic_Trim, shoulder Pennon) are tinted at runtime
// by SpearmanAnimator via EntityReference (LedgerVisual.TryTint pattern) —
// the orchestrator adds EntityReference after Build returns, so the
// animator guards for it being absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class SpearmanVisual
    {
        /// <summary>
        /// Builds the full Spearman rig and returns the root. The root sits
        /// at ground level (feet at y=0); figure height ~1.85 m, spear tip
        /// ~2.4 m. Deterministic: all jitter flows through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("SpearmanVisual");

            // Palette ---------------------------------------------------------
            var iron       = new Color(0.55f, 0.57f, 0.61f); // helm, blade
            var ironDark   = new Color(0.30f, 0.31f, 0.34f); // butt spike, boss rim
            var clothPad   = new Color(0.58f, 0.52f, 0.40f); // gambeson quilting
            var clothDark  = new Color(0.36f, 0.32f, 0.26f); // trousers, under-cloth
            var clothLight = new Color(0.87f, 0.85f, 0.79f); // accent base (tinted)
            var leather    = new Color(0.43f, 0.27f, 0.16f); // belt, straps, boots
            var leatherDrk = new Color(0.30f, 0.19f, 0.12f); // scabbard, soles
            var wood       = new Color(0.48f, 0.36f, 0.22f); // spear shaft, shield back
            var skin       = new Color(0.78f, 0.62f, 0.50f);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            System.Func<PrimitiveType, string, Transform, Vector3, Vector3, Quaternion, Color, float, float, GameObject>
            Make = (type, name, parent, lp, ls, lr, color, metal, smooth) =>
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = lp;
                go.transform.localRotation = lr;
                go.transform.localScale = ls;
                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = new Material(shader);
                    r.material.color = color;
                    if (r.material.HasProperty("_Metallic"))   r.material.SetFloat("_Metallic", metal);
                    if (r.material.HasProperty("_Smoothness")) r.material.SetFloat("_Smoothness", smooth);
                }
                var c = go.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
                return go;
            };

            // Small deterministic hand-built lean (whole figure).
            float Jit(float range) => (float)(rng.NextDouble() * 2.0 - 1.0) * range;
            root.transform.localRotation = Quaternion.Euler(0f, Jit(2f), 0f);

            // Pivot skeleton (empties the animator drives by name) -------------
            var pelvis = new GameObject("Pelvis").transform;
            pelvis.SetParent(root.transform, false);
            pelvis.localPosition = new Vector3(0f, 0.94f, 0f);

            var torso = new GameObject("TorsoPivot").transform;
            torso.SetParent(pelvis, false);
            torso.localPosition = Vector3.zero;
            torso.localRotation = Quaternion.Euler(Jit(1.5f), 0f, Jit(1f));

            Transform LegPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(pelvis, false);
                t.localPosition = new Vector3(x, 0f, 0f); // hip height, swings around X
                return t;
            }
            var legL = LegPivot("LegPivot_L", -0.105f);
            var legR = LegPivot("LegPivot_R",  0.105f);

            Transform ArmPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(torso, false);
                t.localPosition = new Vector3(x, 0.44f, 0f); // shoulder height
                return t;
            }
            var armL = ArmPivot("ArmPivot_L", -0.26f);
            var armR = ArmPivot("ArmPivot_R",  0.26f);

            // Legs (children of the leg pivots; positions relative to hip) -----
            foreach (var (side, pivot, mirror) in new[] { ("L", legL, -1f), ("R", legR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "Thigh_" + side, pivot,
                    new Vector3(0f, -0.21f, 0f), new Vector3(0.13f, 0.15f, 0.13f),
                    Quaternion.Euler(0f, 0f, mirror * 2f), clothDark, 0.05f, 0.10f);
                Make(PrimitiveType.Sphere, "Knee_" + side, pivot,
                    new Vector3(0f, -0.40f, 0.01f), new Vector3(0.115f, 0.10f, 0.115f),
                    Quaternion.identity, clothDark * 0.92f, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Shin_" + side, pivot,
                    new Vector3(0f, -0.61f, 0f), new Vector3(0.105f, 0.155f, 0.105f),
                    Quaternion.Euler(Jit(1.5f), 0f, 0f), clothDark, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Boot_Cuff_" + side, pivot,
                    new Vector3(0f, -0.76f, 0f), new Vector3(0.12f, 0.05f, 0.12f),
                    Quaternion.identity, leather, 0.08f, 0.22f);
                Make(PrimitiveType.Cube, "Boot_" + side, pivot,
                    new Vector3(0f, -0.885f, 0.05f), new Vector3(0.12f, 0.09f, 0.24f),
                    Quaternion.Euler(0f, mirror * 3f, 0f), leatherDrk, 0.08f, 0.20f);
            }

            // Torso — quilted gambeson, no plate -------------------------------
            Make(PrimitiveType.Cube, "Gambeson_Skirt", torso,
                new Vector3(0f, -0.02f, 0f), new Vector3(0.32f, 0.16f, 0.23f),
                Quaternion.identity, clothPad * 0.94f, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Belt", torso,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.33f, 0.055f, 0.24f),
                Quaternion.identity, leather, 0.08f, 0.25f);
            Make(PrimitiveType.Cube, "Belt_Buckle", torso,
                new Vector3(0f, 0.08f, 0.118f), new Vector3(0.06f, 0.045f, 0.02f),
                Quaternion.identity, iron, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Gambeson_Chest", torso,
                new Vector3(0f, 0.29f, 0f), new Vector3(0.35f, 0.38f, 0.24f),
                Quaternion.Euler(Jit(1f), 0f, 0f), clothPad, 0.05f, 0.10f);
            // Horizontal quilt ridges — the padded-cloth read.
            Make(PrimitiveType.Cube, "Gambeson_Quilt_1", torso,
                new Vector3(0f, 0.18f, 0.005f), new Vector3(0.355f, 0.025f, 0.245f),
                Quaternion.identity, clothPad * 0.85f, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Gambeson_Quilt_2", torso,
                new Vector3(0f, 0.29f, 0.005f), new Vector3(0.355f, 0.025f, 0.245f),
                Quaternion.identity, clothPad * 0.85f, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Gambeson_Quilt_3", torso,
                new Vector3(0f, 0.40f, 0.005f), new Vector3(0.355f, 0.025f, 0.245f),
                Quaternion.identity, clothPad * 0.85f, 0.05f, 0.10f);
            // Faction accent: vertical trim band down the tunic front.
            Make(PrimitiveType.Cube, "Tunic_Trim", torso,
                new Vector3(0f, 0.26f, 0.126f), new Vector3(0.09f, 0.42f, 0.012f),
                Quaternion.Euler(-1.5f, 0f, Jit(1f)), clothLight, 0.0f, 0.12f);
            Make(PrimitiveType.Cylinder, "Collar", torso,
                new Vector3(0f, 0.50f, 0f), new Vector3(0.155f, 0.035f, 0.155f),
                Quaternion.identity, clothPad * 0.9f, 0.05f, 0.10f);

            // Padded shoulder rolls (cloth, not pauldrons) ---------------------
            Make(PrimitiveType.Sphere, "Shoulder_Pad_L", torso,
                new Vector3(-0.225f, 0.47f, 0f), new Vector3(0.155f, 0.11f, 0.16f),
                Quaternion.Euler(0f, 0f, 10f), clothPad, 0.05f, 0.10f);
            Make(PrimitiveType.Sphere, "Shoulder_Pad_R", torso,
                new Vector3( 0.225f, 0.47f, 0f), new Vector3(0.155f, 0.11f, 0.16f),
                Quaternion.Euler(0f, 0f, -10f), clothPad, 0.05f, 0.10f);
            // Small shoulder pennon — faction accent on a short staff.
            Make(PrimitiveType.Cylinder, "Pennon_Staff", torso,
                new Vector3(-0.255f, 0.60f, -0.02f), new Vector3(0.015f, 0.09f, 0.015f),
                Quaternion.Euler(Jit(2f), 0f, -8f), wood, 0.10f, 0.20f);
            Make(PrimitiveType.Cube, "Pennon", torso,
                new Vector3(-0.275f, 0.665f, -0.02f), new Vector3(0.012f, 0.075f, 0.13f),
                Quaternion.Euler(0f, Jit(3f), -8f), clothLight, 0.0f, 0.12f);

            // Arms (children of the shoulder pivots) ---------------------------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "UpperArm_" + side, pivot,
                    new Vector3(mirror * 0.03f, -0.13f, 0f), new Vector3(0.085f, 0.115f, 0.085f),
                    Quaternion.Euler(0f, 0f, mirror * 6f), clothPad, 0.05f, 0.10f);
                Make(PrimitiveType.Sphere, "Elbow_" + side, pivot,
                    new Vector3(mirror * 0.045f, -0.26f, 0.01f), new Vector3(0.09f, 0.08f, 0.09f),
                    Quaternion.identity, clothPad * 0.9f, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Forearm_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.375f, 0.03f), new Vector3(0.07f, 0.10f, 0.07f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), clothDark, 0.05f, 0.10f);
                Make(PrimitiveType.Sphere, "Hand_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.485f, 0.06f), new Vector3(0.08f, 0.08f, 0.08f),
                    Quaternion.identity, skin, 0.0f, 0.25f);
            }

            // Spear held upright in the right hand -----------------------------
            var spear = new GameObject("Spear").transform;
            spear.SetParent(armR, false);
            spear.localPosition = new Vector3(0.055f, -0.485f, 0.06f); // at the hand
            spear.localRotation = Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f));
            Make(PrimitiveType.Cylinder, "Spear_Shaft", spear,
                new Vector3(0f, 0.25f, 0f), new Vector3(0.035f, 1.20f, 0.035f), // 2.4 m
                Quaternion.identity, wood, 0.10f, 0.22f);
            Make(PrimitiveType.Cylinder, "Spear_Grip_Wrap", spear,
                new Vector3(0f, 0.0f, 0f), new Vector3(0.045f, 0.10f, 0.045f),
                Quaternion.identity, leather, 0.08f, 0.25f);
            Make(PrimitiveType.Cylinder, "Spear_Socket", spear,
                new Vector3(0f, 1.44f, 0f), new Vector3(0.045f, 0.045f, 0.045f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            // Leaf blade: flattened stretched sphere.
            Make(PrimitiveType.Sphere, "Spear_Blade", spear,
                new Vector3(0f, 1.60f, 0f), new Vector3(0.085f, 0.24f, 0.022f),
                Quaternion.identity, iron, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Spear_Blade_Ridge", spear,
                new Vector3(0f, 1.57f, 0f), new Vector3(0.03f, 0.17f, 0.032f),
                Quaternion.identity, iron * 0.9f, 0.85f, 0.45f);
            Make(PrimitiveType.Cylinder, "Spear_Butt_Spike", spear,
                new Vector3(0f, -0.985f, 0f), new Vector3(0.028f, 0.055f, 0.028f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);

            // Round shield on the left arm -------------------------------------
            var shield = new GameObject("Shield").transform;
            shield.SetParent(armL, false);
            shield.localPosition = new Vector3(-0.125f, -0.32f, 0.05f);
            shield.localRotation = Quaternion.Euler(0f, 90f + Jit(3f), 0f); // face outward
            Make(PrimitiveType.Cylinder, "Shield_Back", shield,
                new Vector3(0.012f, 0f, 0f), new Vector3(0.46f, 0.012f, 0.46f),
                Quaternion.Euler(0f, 0f, 90f), wood, 0.10f, 0.20f);
            // Faction accent: the painted shield face.
            Make(PrimitiveType.Cylinder, "Shield_Face", shield,
                new Vector3(-0.008f, 0f, 0f), new Vector3(0.44f, 0.012f, 0.44f),
                Quaternion.Euler(0f, 0f, 90f), clothLight, 0.05f, 0.30f);
            Make(PrimitiveType.Cylinder, "Shield_Rim", shield,
                new Vector3(0.002f, 0f, 0f), new Vector3(0.475f, 0.016f, 0.475f),
                Quaternion.Euler(0f, 0f, 90f), ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Shield_Boss", shield,
                new Vector3(-0.035f, 0f, 0f), new Vector3(0.10f, 0.12f, 0.12f),
                Quaternion.identity, iron, 0.85f, 0.50f);

            // Scabbarded knife on the right hip --------------------------------
            Make(PrimitiveType.Cube, "Knife_Scabbard", torso,
                new Vector3(0.175f, 0.02f, 0.06f), new Vector3(0.035f, 0.17f, 0.055f),
                Quaternion.Euler(0f, 0f, -12f + Jit(2f)), leatherDrk, 0.08f, 0.22f);
            Make(PrimitiveType.Cylinder, "Knife_Hilt", torso,
                new Vector3(0.155f, 0.12f, 0.06f), new Vector3(0.022f, 0.045f, 0.022f),
                Quaternion.Euler(0f, 0f, -12f), leather, 0.08f, 0.25f);
            Make(PrimitiveType.Sphere, "Knife_Pommel", torso,
                new Vector3(0.145f, 0.165f, 0.06f), new Vector3(0.035f, 0.035f, 0.035f),
                Quaternion.identity, iron, 0.85f, 0.50f);

            // Head + kettle helm -----------------------------------------------
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0f, 0.57f, 0f);
            head.localRotation = Quaternion.Euler(Jit(1.5f), Jit(3f), 0f);
            Make(PrimitiveType.Sphere, "Head", head,
                new Vector3(0f, 0.07f, 0f), new Vector3(0.18f, 0.195f, 0.18f),
                Quaternion.identity, skin, 0.0f, 0.30f);
            Make(PrimitiveType.Cube, "Chin_Strap", head,
                new Vector3(0f, 0.045f, 0f), new Vector3(0.185f, 0.02f, 0.185f),
                Quaternion.identity, leather, 0.08f, 0.22f);
            Make(PrimitiveType.Sphere, "Helm_Dome", head,
                new Vector3(0f, 0.135f, 0f), new Vector3(0.20f, 0.155f, 0.20f),
                Quaternion.identity, iron, 0.85f, 0.55f);
            // Kettle brim — wide flat disc, slight forward dip.
            Make(PrimitiveType.Cylinder, "Helm_Brim", head,
                new Vector3(0f, 0.10f, 0.005f), new Vector3(0.29f, 0.012f, 0.29f),
                Quaternion.Euler(4f + Jit(1.5f), 0f, Jit(1f)), iron * 0.92f, 0.85f, 0.50f);
            Make(PrimitiveType.Sphere, "Helm_Knop", head,
                new Vector3(0f, 0.205f, 0f), new Vector3(0.04f, 0.035f, 0.04f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);

            root.AddComponent<SpearmanAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Spearman: walk cycle (leg/arm
    /// swing driven by sampled position delta; the spear arm swings less so
    /// the upright spear stays planted-looking), idle spear-butt tap +
    /// weight-shift sway, and faction-color tint of the accent parts
    /// (Shield_Face, Tunic_Trim, Pennon) once EntityReference is wired by
    /// the orchestrator.
    /// </summary>
    public class SpearmanAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 28f;

        [Tooltip("Arm swing amplitude in degrees at full stride (shield arm).")]
        public float ArmSwing = 15f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 1.05f;

        [Tooltip("Idle torso weight-shift sway in degrees.")]
        public float IdleSway = 1.8f;

        [Tooltip("Seconds between idle spear-butt taps.")]
        public float TapInterval = 3.4f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head, _spear;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest;
        private Vector3 _spearRestPos;
        private Material _shieldMat, _trimMat, _pennonMat;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _phase;    // walk cycle phase, radians
        private float _gait;     // 0 = idle, 1 = walking (smoothed)
        private float _tapClock; // idle spear-tap timer

        void Start()
        {
            _legL  = FindDeep(transform, "LegPivot_L");
            _legR  = FindDeep(transform, "LegPivot_R");
            _armL  = FindDeep(transform, "ArmPivot_L");
            _armR  = FindDeep(transform, "ArmPivot_R");
            _torso = FindDeep(transform, "TorsoPivot");
            _head  = FindDeep(transform, "HeadPivot");
            _spear = FindDeep(transform, "Spear");
            if (_armL != null)  _armLRest  = _armL.localRotation;
            if (_armR != null)  _armRRest  = _armR.localRotation;
            if (_torso != null) _torsoRest = _torso.localRotation;
            if (_head != null)  _headRest  = _head.localRotation;
            if (_spear != null) _spearRestPos = _spear.localPosition;

            _shieldMat = MatOf("Shield_Face");
            _trimMat   = MatOf("Tunic_Trim");
            _pennonMat = MatOf("Pennon");

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) { _em = world.EntityManager; _emReady = true; }
        }

        void LateUpdate()
        {
            if (!_tinted) TryTint();

            // Planar speed from position delta (SyncTransforms moves the root).
            Vector3 pos = transform.position;
            float dist = 0f;
            if (_hasPrevPos)
            {
                Vector3 d = pos - _prevPos;
                d.y = 0f;
                dist = d.magnitude;
            }
            _prevPos = pos;
            _hasPrevPos = true;

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float speed = dist / dt;
            bool moving = speed > 0.15f;
            _gait = Mathf.MoveTowards(_gait, moving ? 1f : 0f, dt * 5f);

            // Phase advances with distance so stride matches ground speed.
            _phase += (dist / Mathf.Max(StrideLength, 0.01f)) * 2f * Mathf.PI;

            float t = Time.time;
            float swing = Mathf.Sin(_phase) * _gait;

            if (_legL != null) _legL.localRotation = Quaternion.Euler( swing * LegSwing, 0f, 0f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-swing * LegSwing, 0f, 0f);
            // Shield arm counter-swings the legs; the spear arm swings at a
            // third amplitude so the upright shaft doesn't scythe around.
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(-swing * ArmSwing, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing * 0.35f, 0f, 0f);

            // Idle: weight-shift sway + a periodic spear-butt tap (the spear
            // lifts a few cm and drops back). While walking the torso takes a
            // slight forward lean and step bob instead.
            float idleAmt = 1f - _gait;
            _tapClock += dt * idleAmt;
            if (_tapClock > TapInterval) _tapClock -= TapInterval;
            float tapT = _tapClock / Mathf.Max(TapInterval, 0.01f);
            // Short raised-cosine pulse in the first 18% of the interval.
            float tap = tapT < 0.18f
                ? Mathf.Sin(tapT / 0.18f * Mathf.PI)
                : 0f;
            if (_spear != null)
                _spear.localPosition = _spearRestPos + new Vector3(0f, 0.06f * tap * idleAmt, 0f);

            if (_torso != null)
            {
                float idleZ = Mathf.Sin(t * 0.8f) * IdleSway * idleAmt;
                float walkLean = 3f * _gait;
                float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.5f * _gait;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(walkLean + bob, 0f, idleZ);
            }
            if (_head != null)
            {
                float yaw = Mathf.Sin(t * 0.4f) * 5f * idleAmt;
                _head.localRotation = _headRest * Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private void TryTint()
        {
            // EntityReference is added by the orchestrator AFTER Build returns
            // — keep polling until it exists and the entity link is live.
            if (_entityRef == null)
            {
                _entityRef = GetComponent<EntityReference>();
                if (_entityRef == null) return;
            }
            if (!_emReady) return;
            var e = _entityRef.Entity;
            if (e == Entity.Null || !_em.Exists(e) || !_em.HasComponent<FactionTag>(e)) return;

            var fc = FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value);
            Tint(_shieldMat, fc, 0.10f, true);  // soft emissive so it reads at distance
            Tint(_trimMat, fc, 0.15f, false);
            Tint(_pennonMat, fc, 0.10f, false);
            _tinted = true;
        }

        private static void Tint(Material m, Color c, float whiten, bool emissive)
        {
            if (m == null) return;
            var baseCol = Color.Lerp(c, Color.white, whiten);
            m.SetColor("_BaseColor", baseCol);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseCol);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 0.35f);
            }
        }

        private Material MatOf(string partName)
        {
            var t = FindDeep(transform, partName);
            if (t != null && t.TryGetComponent<MeshRenderer>(out var r))
                return r.material; // instance — safe to tint per unit
            return null;
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
