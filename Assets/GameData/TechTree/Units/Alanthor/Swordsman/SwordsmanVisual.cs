// File: Assets/GameData/TechTree/Units/Alanthor/Swordsman/SwordsmanVisual.cs
// Procedural line-infantry visual for the Alanthor Garrison Lv1 Swordsman:
// a MEDIUM soldier — a clear step up from the quilted Age-0 Spearman but
// lighter than the Nobleman's full plate. Mail shirt (slightly metallic)
// under a surcoat, nasal helm with cheek guards, arming sword held at rest
// point-down in the right hand, heater shield on the left arm (smaller than
// the Nobleman's), belt with buckle, cloth legs + boots (no greaves). Built
// entirely from primitives (Smelter idiom — per-part URP/Lit material,
// metallic/smoothness contrast, small deterministic tilts, colliders
// destroyed). Player-color accents (Surcoat_Front, Shield_Face,
// Helm_Plume_Tail) are tinted at runtime by SwordsmanAnimator via
// EntityReference (LedgerVisual.TryTint pattern) — the orchestrator adds
// EntityReference after Build returns, so the animator guards for it being
// absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Input; // EntityReference

namespace TheWaningBorder.Presentation
{
    public static class SwordsmanVisual
    {
        /// <summary>
        /// Builds the full Swordsman rig and returns the root. The root sits
        /// at ground level (feet at y=0); figure height ~1.85 m, sword tip
        /// hangs a hand-span above the ground. Deterministic: all jitter
        /// flows through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("SwordsmanVisual");

            // Palette ---------------------------------------------------------
            var iron       = new Color(0.55f, 0.57f, 0.61f); // helm, blade
            var ironDark   = new Color(0.30f, 0.31f, 0.34f); // crossguard, rim
            var mail       = new Color(0.46f, 0.48f, 0.52f); // mail shirt links
            var mailDark   = new Color(0.36f, 0.38f, 0.42f); // mail skirt, cuffs
            var clothDark  = new Color(0.34f, 0.31f, 0.27f); // trousers, under-cloth
            var clothMid   = new Color(0.52f, 0.48f, 0.41f); // surcoat back/skirt
            var clothLight = new Color(0.87f, 0.85f, 0.79f); // accent base (tinted)
            var leather    = new Color(0.43f, 0.27f, 0.16f); // belt, straps, boots
            var leatherDrk = new Color(0.30f, 0.19f, 0.12f); // soles, pouch
            var wood       = new Color(0.48f, 0.36f, 0.22f); // shield back
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

            // Legs — cloth trousers + boots, no greaves ------------------------
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

            // Torso — mail shirt under a surcoat -------------------------------
            // Mail skirt hangs below the belt line (the shirt's lower hem).
            Make(PrimitiveType.Cube, "Mail_Skirt", torso,
                new Vector3(0f, -0.03f, 0f), new Vector3(0.33f, 0.15f, 0.24f),
                Quaternion.identity, mailDark, 0.45f, 0.35f);
            Make(PrimitiveType.Cube, "Mail_Chest", torso,
                new Vector3(0f, 0.29f, 0f), new Vector3(0.36f, 0.38f, 0.25f),
                Quaternion.Euler(Jit(1f), 0f, 0f), mail, 0.45f, 0.35f);
            // Surcoat worn over the mail: front panel (accent), back panel,
            // and a short split skirt below the belt.
            Make(PrimitiveType.Cube, "Surcoat_Front", torso,
                new Vector3(0f, 0.27f, 0.13f), new Vector3(0.22f, 0.42f, 0.015f),
                Quaternion.Euler(-1.5f, 0f, Jit(1f)), clothLight, 0.0f, 0.12f);
            Make(PrimitiveType.Cube, "Surcoat_Back", torso,
                new Vector3(0f, 0.27f, -0.13f), new Vector3(0.22f, 0.42f, 0.015f),
                Quaternion.Euler(1.5f, 0f, Jit(1f)), clothMid, 0.0f, 0.12f);
            Make(PrimitiveType.Cube, "Surcoat_Skirt", torso,
                new Vector3(0f, -0.06f, 0.115f), new Vector3(0.20f, 0.20f, 0.015f),
                Quaternion.Euler(-3f + Jit(1f), 0f, 0f), clothMid * 0.95f, 0.0f, 0.12f);
            Make(PrimitiveType.Cube, "Belt", torso,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.34f, 0.055f, 0.25f),
                Quaternion.identity, leather, 0.08f, 0.25f);
            Make(PrimitiveType.Cube, "Belt_Buckle", torso,
                new Vector3(0f, 0.08f, 0.128f), new Vector3(0.06f, 0.045f, 0.02f),
                Quaternion.identity, iron, 0.85f, 0.50f);
            Make(PrimitiveType.Cube, "Belt_Pouch", torso,
                new Vector3(-0.16f, 0.015f, 0.07f), new Vector3(0.075f, 0.085f, 0.05f),
                Quaternion.Euler(0f, 0f, Jit(2f)), leatherDrk, 0.08f, 0.20f);
            Make(PrimitiveType.Cylinder, "Collar", torso,
                new Vector3(0f, 0.50f, 0f), new Vector3(0.16f, 0.035f, 0.16f),
                Quaternion.identity, mailDark, 0.45f, 0.35f);

            // Mail shoulder rolls (the shirt's shoulders, not pauldrons) -------
            Make(PrimitiveType.Sphere, "Shoulder_Mail_L", torso,
                new Vector3(-0.225f, 0.47f, 0f), new Vector3(0.16f, 0.11f, 0.165f),
                Quaternion.Euler(0f, 0f, 10f), mail, 0.45f, 0.35f);
            Make(PrimitiveType.Sphere, "Shoulder_Mail_R", torso,
                new Vector3( 0.225f, 0.47f, 0f), new Vector3(0.16f, 0.11f, 0.165f),
                Quaternion.Euler(0f, 0f, -10f), mail, 0.45f, 0.35f);

            // Arms — mail sleeves ending in cuffs at the wrist -----------------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "UpperArm_" + side, pivot,
                    new Vector3(mirror * 0.03f, -0.13f, 0f), new Vector3(0.085f, 0.115f, 0.085f),
                    Quaternion.Euler(0f, 0f, mirror * 6f), mail, 0.45f, 0.35f);
                Make(PrimitiveType.Sphere, "Elbow_" + side, pivot,
                    new Vector3(mirror * 0.045f, -0.26f, 0.01f), new Vector3(0.09f, 0.08f, 0.09f),
                    Quaternion.identity, mail * 0.92f, 0.45f, 0.35f);
                Make(PrimitiveType.Cylinder, "Forearm_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.375f, 0.03f), new Vector3(0.07f, 0.10f, 0.07f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), mail, 0.45f, 0.35f);
                Make(PrimitiveType.Cylinder, "Mail_Cuff_" + side, pivot,
                    new Vector3(mirror * 0.052f, -0.455f, 0.048f), new Vector3(0.078f, 0.028f, 0.078f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), mailDark, 0.45f, 0.35f);
                Make(PrimitiveType.Sphere, "Hand_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.485f, 0.06f), new Vector3(0.08f, 0.08f, 0.08f),
                    Quaternion.identity, skin, 0.0f, 0.25f);
            }

            // Arming sword held at rest, point-down, in the right hand ---------
            var sword = new GameObject("Sword").transform;
            sword.SetParent(armR, false);
            sword.localPosition = new Vector3(0.055f, -0.485f, 0.06f); // at the hand
            sword.localRotation = Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f));
            Make(PrimitiveType.Cylinder, "Sword_Grip", sword,
                new Vector3(0f, 0.0f, 0f), new Vector3(0.032f, 0.065f, 0.032f),
                Quaternion.identity, leather, 0.08f, 0.25f);
            Make(PrimitiveType.Sphere, "Sword_Pommel", sword,
                new Vector3(0f, 0.09f, 0f), new Vector3(0.05f, 0.05f, 0.05f),
                Quaternion.identity, iron, 0.85f, 0.55f);
            Make(PrimitiveType.Cube, "Sword_Crossguard", sword,
                new Vector3(0f, -0.075f, 0f), new Vector3(0.17f, 0.022f, 0.032f),
                Quaternion.Euler(0f, 0f, Jit(1.5f)), ironDark, 0.85f, 0.45f);
            // Blade descends from the guard: flattened cube, ridge, then a
            // tapered tip that hangs a hand-span above the ground.
            Make(PrimitiveType.Cube, "Sword_Blade", sword,
                new Vector3(0f, -0.40f, 0f), new Vector3(0.058f, 0.62f, 0.016f),
                Quaternion.identity, iron, 0.85f, 0.55f);
            Make(PrimitiveType.Cube, "Sword_Blade_Ridge", sword,
                new Vector3(0f, -0.40f, 0f), new Vector3(0.018f, 0.60f, 0.022f),
                Quaternion.identity, iron * 0.9f, 0.85f, 0.50f);
            Make(PrimitiveType.Sphere, "Sword_Tip", sword,
                new Vector3(0f, -0.73f, 0f), new Vector3(0.052f, 0.10f, 0.015f),
                Quaternion.identity, iron, 0.85f, 0.55f);

            // Heater shield on the left arm (smaller than the Nobleman's) ------
            var shield = new GameObject("Shield").transform;
            shield.SetParent(armL, false);
            shield.localPosition = new Vector3(-0.125f, -0.30f, 0.05f);
            shield.localRotation = Quaternion.Euler(0f, 90f + Jit(3f), 0f); // face outward
            // Flat-topped body...
            Make(PrimitiveType.Cube, "Shield_Body", shield,
                new Vector3(0.012f, 0.06f, 0f), new Vector3(0.014f, 0.26f, 0.36f),
                Quaternion.identity, wood, 0.10f, 0.20f);
            // ...tapering to a point below (two angled boards meet mid-line).
            Make(PrimitiveType.Cube, "Shield_Point_L", shield,
                new Vector3(0.012f, -0.145f, -0.083f), new Vector3(0.014f, 0.24f, 0.19f),
                Quaternion.Euler(35f, 0f, 0f), wood, 0.10f, 0.20f);
            Make(PrimitiveType.Cube, "Shield_Point_R", shield,
                new Vector3(0.012f, -0.145f, 0.083f), new Vector3(0.014f, 0.24f, 0.19f),
                Quaternion.Euler(-35f, 0f, 0f), wood, 0.10f, 0.20f);
            // Faction accent: the painted shield face (front board, emissive).
            Make(PrimitiveType.Cube, "Shield_Face", shield,
                new Vector3(-0.004f, 0.06f, 0f), new Vector3(0.012f, 0.24f, 0.34f),
                Quaternion.identity, clothLight, 0.05f, 0.30f);
            Make(PrimitiveType.Cube, "Shield_Face_Point", shield,
                new Vector3(-0.004f, -0.13f, 0f), new Vector3(0.012f, 0.20f, 0.17f),
                Quaternion.Euler(0f, 0f, 0f), clothLight, 0.05f, 0.30f);
            Make(PrimitiveType.Cube, "Shield_Rim_Top", shield,
                new Vector3(0.002f, 0.195f, 0f), new Vector3(0.02f, 0.025f, 0.37f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);

            // Head + nasal helm with cheek guards ------------------------------
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
                new Vector3(0f, 0.14f, 0f), new Vector3(0.205f, 0.16f, 0.205f),
                Quaternion.identity, iron, 0.85f, 0.50f);
            Make(PrimitiveType.Cylinder, "Helm_Band", head,
                new Vector3(0f, 0.105f, 0f), new Vector3(0.21f, 0.018f, 0.21f),
                Quaternion.Euler(Jit(1.5f), 0f, Jit(1f)), ironDark, 0.85f, 0.45f);
            // Nasal bar down over the face.
            Make(PrimitiveType.Cube, "Helm_Nasal", head,
                new Vector3(0f, 0.055f, 0.095f), new Vector3(0.028f, 0.10f, 0.022f),
                Quaternion.Euler(6f + Jit(1.5f), 0f, 0f), iron, 0.85f, 0.50f);
            // Cheek guards hanging from the band on both sides.
            Make(PrimitiveType.Cube, "Helm_Cheek_L", head,
                new Vector3(-0.093f, 0.035f, 0.03f), new Vector3(0.022f, 0.11f, 0.10f),
                Quaternion.Euler(0f, Jit(2f), 8f), iron * 0.95f, 0.85f, 0.50f);
            Make(PrimitiveType.Cube, "Helm_Cheek_R", head,
                new Vector3( 0.093f, 0.035f, 0.03f), new Vector3(0.022f, 0.11f, 0.10f),
                Quaternion.Euler(0f, Jit(2f), -8f), iron * 0.95f, 0.85f, 0.50f);
            // Short plume: iron socket + accent tail streaming back.
            Make(PrimitiveType.Cylinder, "Helm_Plume_Socket", head,
                new Vector3(0f, 0.215f, 0f), new Vector3(0.035f, 0.03f, 0.035f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Helm_Plume_Tail", head,
                new Vector3(0f, 0.235f, -0.055f), new Vector3(0.018f, 0.045f, 0.14f),
                Quaternion.Euler(-18f + Jit(3f), 0f, Jit(2f)), clothLight, 0.0f, 0.12f);

            root.AddComponent<SwordsmanAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Swordsman: walk cycle (leg/arm
    /// swing driven by sampled position delta; the sword arm swings at 0.3x
    /// so the point-down blade doesn't scythe around), idle shoulder roll +
    /// occasional sword-tip ground tap, and faction-color tint of the accent
    /// parts (Surcoat_Front, Shield_Face, Helm_Plume_Tail) once
    /// EntityReference is wired by the orchestrator.
    /// </summary>
    public class SwordsmanAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 28f;

        [Tooltip("Arm swing amplitude in degrees at full stride (shield arm).")]
        public float ArmSwing = 15f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 1.05f;

        [Tooltip("Idle shoulder-roll amplitude in degrees.")]
        public float IdleRoll = 2.2f;

        [Tooltip("Seconds between idle sword-tip ground taps.")]
        public float TapInterval = 3.8f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head, _sword;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest;
        private Vector3 _swordRestPos;
        private Material _surcoatMat, _shieldFaceMat, _shieldPointMat, _plumeMat;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _phase;    // walk cycle phase, radians
        private float _gait;     // 0 = idle, 1 = walking (smoothed)
        private float _tapClock; // idle sword-tap timer

        void Start()
        {
            _legL  = FindDeep(transform, "LegPivot_L");
            _legR  = FindDeep(transform, "LegPivot_R");
            _armL  = FindDeep(transform, "ArmPivot_L");
            _armR  = FindDeep(transform, "ArmPivot_R");
            _torso = FindDeep(transform, "TorsoPivot");
            _head  = FindDeep(transform, "HeadPivot");
            _sword = FindDeep(transform, "Sword");
            if (_armL != null)  _armLRest  = _armL.localRotation;
            if (_armR != null)  _armRRest  = _armR.localRotation;
            if (_torso != null) _torsoRest = _torso.localRotation;
            if (_head != null)  _headRest  = _head.localRotation;
            if (_sword != null) _swordRestPos = _sword.localPosition;

            _surcoatMat    = MatOf("Surcoat_Front");
            _shieldFaceMat = MatOf("Shield_Face");
            _shieldPointMat = MatOf("Shield_Face_Point");
            _plumeMat      = MatOf("Helm_Plume_Tail");

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
            // Shield arm counter-swings the legs; the sword arm swings at 0.3x
            // amplitude so the resting point-down blade stays settled.
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(-swing * ArmSwing, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing * 0.3f, 0f, 0f);

            // Idle: slow shoulder roll (torso rocks around Z with a hint of
            // yaw) + a periodic sword-tip ground tap — the sword drops a few
            // cm so the point touches earth, then lifts back to rest. While
            // walking the torso takes a slight forward lean and step bob.
            float idleAmt = 1f - _gait;
            _tapClock += dt * idleAmt;
            if (_tapClock > TapInterval) _tapClock -= TapInterval;
            float tapT = _tapClock / Mathf.Max(TapInterval, 0.01f);
            // Short raised-cosine pulse in the first 18% of the interval.
            float tap = tapT < 0.18f
                ? Mathf.Sin(tapT / 0.18f * Mathf.PI)
                : 0f;
            if (_sword != null)
                _sword.localPosition = _swordRestPos + new Vector3(0f, -0.05f * tap * idleAmt, 0f);

            if (_torso != null)
            {
                float rollZ = Mathf.Sin(t * 0.7f) * IdleRoll * idleAmt;
                float rollY = Mathf.Sin(t * 0.7f + 0.6f) * IdleRoll * 0.4f * idleAmt;
                float walkLean = 3f * _gait;
                float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.5f * _gait;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(walkLean + bob, rollY, rollZ);
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
            Tint(_shieldFaceMat, fc, 0.10f, true);  // soft emissive so it reads at distance
            Tint(_shieldPointMat, fc, 0.10f, true);
            Tint(_surcoatMat, fc, 0.15f, false);
            Tint(_plumeMat, fc, 0.10f, false);
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
