// File: Assets/GameData/TechTree/Units/Age 0/Archer/ArcherVisual.cs
// Procedural leather-and-cloth archer visual for the Age 0 Archer (pid 202):
// lined hood, leather jerkin over a tunic, bracers, recurve bow in the left
// hand (two curved limb segments per side + grip + string), belt quiver with
// five fletched arrows, knife. Built entirely from primitives (Smelter idiom
// — per-part URP/Lit material, metallic/smoothness contrast, small
// deterministic tilts, colliders destroyed). Player-color accents
// (Hood_Lining, Tabard_Front, Fletching_1-5) are tinted at runtime by
// ArcherAnimator via EntityReference (LedgerVisual.TryTint pattern) — the
// orchestrator adds EntityReference after Build returns, so the animator
// guards for it being absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class ArcherVisual
    {
        /// <summary>
        /// Builds the full Archer rig and returns the root. The root sits at
        /// ground level (feet at y=0); figure height ~1.80 m. Deterministic:
        /// all jitter flows through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("ArcherVisual");

            // Palette ---------------------------------------------------------
            var leather    = new Color(0.44f, 0.28f, 0.17f); // jerkin, bracers
            var leatherDrk = new Color(0.30f, 0.19f, 0.12f); // quiver, boots, scabbard
            var clothTunic = new Color(0.46f, 0.44f, 0.34f); // tunic under the jerkin
            var clothDark  = new Color(0.33f, 0.30f, 0.25f); // trousers, hood shell
            var clothLight = new Color(0.87f, 0.85f, 0.79f); // accent base (tinted)
            var wood       = new Color(0.47f, 0.34f, 0.20f); // bow limbs, arrow shafts
            var woodDark   = new Color(0.33f, 0.23f, 0.13f); // bow grip, riser
            var iron       = new Color(0.55f, 0.57f, 0.61f); // knife, buckle
            var sinew      = new Color(0.80f, 0.76f, 0.66f); // bowstring
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
            pelvis.localPosition = new Vector3(0f, 0.92f, 0f);

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
            var legL = LegPivot("LegPivot_L", -0.10f);
            var legR = LegPivot("LegPivot_R",  0.10f);

            Transform ArmPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(torso, false);
                t.localPosition = new Vector3(x, 0.43f, 0f); // shoulder height
                return t;
            }
            var armL = ArmPivot("ArmPivot_L", -0.24f);
            var armR = ArmPivot("ArmPivot_R",  0.24f);

            // Legs (children of the leg pivots; positions relative to hip) -----
            foreach (var (side, pivot, mirror) in new[] { ("L", legL, -1f), ("R", legR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "Thigh_" + side, pivot,
                    new Vector3(0f, -0.21f, 0f), new Vector3(0.12f, 0.145f, 0.12f),
                    Quaternion.Euler(0f, 0f, mirror * 2f), clothDark, 0.05f, 0.10f);
                Make(PrimitiveType.Sphere, "Knee_" + side, pivot,
                    new Vector3(0f, -0.39f, 0.01f), new Vector3(0.105f, 0.09f, 0.105f),
                    Quaternion.identity, clothDark * 0.92f, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Shin_" + side, pivot,
                    new Vector3(0f, -0.59f, 0f), new Vector3(0.095f, 0.15f, 0.095f),
                    Quaternion.Euler(Jit(1.5f), 0f, 0f), clothDark, 0.05f, 0.10f);
                Make(PrimitiveType.Cube, "Boot_" + side, pivot,
                    new Vector3(0f, -0.865f, 0.045f), new Vector3(0.115f, 0.085f, 0.23f),
                    Quaternion.Euler(0f, mirror * 3f, 0f), leatherDrk, 0.08f, 0.20f);
            }

            // Torso — tunic under a laced leather jerkin -----------------------
            Make(PrimitiveType.Cube, "Tunic_Skirt", torso,
                new Vector3(0f, -0.02f, 0f), new Vector3(0.30f, 0.15f, 0.22f),
                Quaternion.identity, clothTunic * 0.94f, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Belt", torso,
                new Vector3(0f, 0.075f, 0f), new Vector3(0.315f, 0.05f, 0.23f),
                Quaternion.identity, leather, 0.08f, 0.25f);
            Make(PrimitiveType.Cube, "Belt_Buckle", torso,
                new Vector3(0f, 0.075f, 0.112f), new Vector3(0.055f, 0.04f, 0.02f),
                Quaternion.identity, iron, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Tunic_Chest", torso,
                new Vector3(0f, 0.28f, 0f), new Vector3(0.33f, 0.37f, 0.23f),
                Quaternion.Euler(Jit(1f), 0f, 0f), clothTunic, 0.05f, 0.10f);
            // Leather jerkin panels layered over the tunic.
            Make(PrimitiveType.Cube, "Jerkin_Chest", torso,
                new Vector3(0f, 0.30f, 0.01f), new Vector3(0.34f, 0.30f, 0.235f),
                Quaternion.Euler(Jit(1f), 0f, Jit(1f)), leather, 0.08f, 0.28f);
            Make(PrimitiveType.Cube, "Jerkin_Panel_L", torso,
                new Vector3(-0.09f, 0.29f, 0.122f), new Vector3(0.13f, 0.28f, 0.015f),
                Quaternion.Euler(0f, 0f, 2f), leather * 0.92f, 0.08f, 0.24f);
            Make(PrimitiveType.Cube, "Jerkin_Panel_R", torso,
                new Vector3( 0.09f, 0.29f, 0.122f), new Vector3(0.13f, 0.28f, 0.015f),
                Quaternion.Euler(0f, 0f, -2f), leather * 0.92f, 0.08f, 0.24f);
            Make(PrimitiveType.Cube, "Jerkin_Lace", torso,
                new Vector3(0f, 0.29f, 0.128f), new Vector3(0.025f, 0.26f, 0.012f),
                Quaternion.Euler(0f, 0f, Jit(2f)), sinew, 0.05f, 0.15f);
            // Faction accent: short tabard strip hanging below the jerkin.
            Make(PrimitiveType.Cube, "Tabard_Front", torso,
                new Vector3(0f, 0.045f, 0.118f), new Vector3(0.17f, 0.24f, 0.013f),
                Quaternion.Euler(-2f, 0f, Jit(1f)), clothLight, 0.0f, 0.12f);
            // Shoulder strap for the quiver belt.
            Make(PrimitiveType.Cube, "Quiver_Strap", torso,
                new Vector3(0.02f, 0.30f, 0f), new Vector3(0.05f, 0.44f, 0.25f),
                Quaternion.Euler(0f, 0f, -28f + Jit(2f)), leatherDrk, 0.08f, 0.22f);

            // Belt quiver on the right hip, five fletched arrows ---------------
            var quiver = new GameObject("Quiver").transform;
            quiver.SetParent(torso, false);
            quiver.localPosition = new Vector3(0.185f, 0.02f, -0.06f);
            quiver.localRotation = Quaternion.Euler(Jit(2f), 0f, -14f + Jit(2f));
            Make(PrimitiveType.Cylinder, "Quiver_Body", quiver,
                Vector3.zero, new Vector3(0.10f, 0.16f, 0.10f),
                Quaternion.identity, leatherDrk, 0.08f, 0.22f);
            Make(PrimitiveType.Cylinder, "Quiver_Band", quiver,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.107f, 0.02f, 0.107f),
                Quaternion.identity, leather, 0.08f, 0.28f);
            for (int i = 0; i < 5; i++)
            {
                float ang = i * (Mathf.PI * 2f / 5f) + (float)rng.NextDouble() * 0.4f;
                float rx = Mathf.Cos(ang) * 0.028f;
                float rz = Mathf.Sin(ang) * 0.028f;
                float lean = Jit(3f);
                Make(PrimitiveType.Cylinder, "Arrow_Shaft_" + (i + 1), quiver,
                    new Vector3(rx, 0.24f, rz), new Vector3(0.012f, 0.13f, 0.012f),
                    Quaternion.Euler(lean, 0f, Jit(3f)), wood, 0.10f, 0.20f);
                // Faction accent: the fletching tips read as a color cluster.
                Make(PrimitiveType.Cube, "Fletching_" + (i + 1), quiver,
                    new Vector3(rx, 0.345f, rz), new Vector3(0.03f, 0.055f, 0.03f),
                    Quaternion.Euler(lean, ang * Mathf.Rad2Deg, 0f), clothLight, 0.0f, 0.12f);
            }

            // Arms (children of the shoulder pivots) ---------------------------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "UpperArm_" + side, pivot,
                    new Vector3(mirror * 0.03f, -0.125f, 0f), new Vector3(0.08f, 0.11f, 0.08f),
                    Quaternion.Euler(0f, 0f, mirror * 6f), clothTunic, 0.05f, 0.10f);
                Make(PrimitiveType.Sphere, "Elbow_" + side, pivot,
                    new Vector3(mirror * 0.045f, -0.25f, 0.01f), new Vector3(0.085f, 0.075f, 0.085f),
                    Quaternion.identity, clothTunic * 0.9f, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Forearm_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.36f, 0.03f), new Vector3(0.065f, 0.095f, 0.065f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), clothTunic, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Bracer_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.40f, 0.043f), new Vector3(0.078f, 0.06f, 0.078f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), leather, 0.08f, 0.30f);
                Make(PrimitiveType.Sphere, "Hand_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.465f, 0.055f), new Vector3(0.075f, 0.075f, 0.075f),
                    Quaternion.identity, skin, 0.0f, 0.25f);
            }

            // Recurve bow in the left hand — held vertical at rest -------------
            var bow = new GameObject("Bow").transform;
            bow.SetParent(armL, false);
            bow.localPosition = new Vector3(-0.06f, -0.465f, 0.055f); // at the hand
            bow.localRotation = Quaternion.Euler(Jit(2f), 0f, 8f + Jit(2f));
            Make(PrimitiveType.Cylinder, "Bow_Grip", bow,
                Vector3.zero, new Vector3(0.032f, 0.075f, 0.032f),
                Quaternion.identity, woodDark, 0.08f, 0.30f);
            // Two limb segments per side: main sweep forward, recurve tip back.
            Make(PrimitiveType.Cylinder, "Bow_Limb_Upper_1", bow,
                new Vector3(0f, 0.235f, 0.055f), new Vector3(0.024f, 0.175f, 0.024f),
                Quaternion.Euler(18f, 0f, 0f), wood, 0.10f, 0.28f);
            Make(PrimitiveType.Cylinder, "Bow_Limb_Upper_2", bow,
                new Vector3(0f, 0.485f, 0.075f), new Vector3(0.018f, 0.10f, 0.018f),
                Quaternion.Euler(-16f, 0f, 0f), wood * 0.92f, 0.10f, 0.28f);
            Make(PrimitiveType.Cylinder, "Bow_Limb_Lower_1", bow,
                new Vector3(0f, -0.235f, 0.055f), new Vector3(0.024f, 0.175f, 0.024f),
                Quaternion.Euler(-18f, 0f, 0f), wood, 0.10f, 0.28f);
            Make(PrimitiveType.Cylinder, "Bow_Limb_Lower_2", bow,
                new Vector3(0f, -0.485f, 0.075f), new Vector3(0.018f, 0.10f, 0.018f),
                Quaternion.Euler(16f, 0f, 0f), wood * 0.92f, 0.10f, 0.28f);
            Make(PrimitiveType.Sphere, "Bow_Nock_Upper", bow,
                new Vector3(0f, 0.575f, 0.048f), new Vector3(0.028f, 0.028f, 0.028f),
                Quaternion.identity, woodDark, 0.08f, 0.30f);
            Make(PrimitiveType.Sphere, "Bow_Nock_Lower", bow,
                new Vector3(0f, -0.575f, 0.048f), new Vector3(0.028f, 0.028f, 0.028f),
                Quaternion.identity, woodDark, 0.08f, 0.30f);
            // String — one thin cylinder between the nocks.
            Make(PrimitiveType.Cylinder, "Bow_String", bow,
                new Vector3(0f, 0f, 0.048f), new Vector3(0.006f, 0.575f, 0.006f),
                Quaternion.identity, sinew, 0.05f, 0.15f);

            // Scabbarded knife on the left hip ---------------------------------
            Make(PrimitiveType.Cube, "Knife_Scabbard", torso,
                new Vector3(-0.17f, 0.02f, 0.055f), new Vector3(0.033f, 0.16f, 0.05f),
                Quaternion.Euler(0f, 0f, 12f + Jit(2f)), leatherDrk, 0.08f, 0.22f);
            Make(PrimitiveType.Cylinder, "Knife_Hilt", torso,
                new Vector3(-0.15f, 0.115f, 0.055f), new Vector3(0.02f, 0.042f, 0.02f),
                Quaternion.Euler(0f, 0f, 12f), leather, 0.08f, 0.25f);
            Make(PrimitiveType.Sphere, "Knife_Pommel", torso,
                new Vector3(-0.14f, 0.158f, 0.055f), new Vector3(0.032f, 0.032f, 0.032f),
                Quaternion.identity, iron, 0.85f, 0.50f);

            // Head + lined hood -------------------------------------------------
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0f, 0.56f, 0f);
            head.localRotation = Quaternion.Euler(Jit(1.5f), Jit(3f), 0f);
            Make(PrimitiveType.Sphere, "Head", head,
                new Vector3(0f, 0.065f, 0.01f), new Vector3(0.175f, 0.19f, 0.175f),
                Quaternion.identity, skin, 0.0f, 0.30f);
            Make(PrimitiveType.Sphere, "Hood_Shell", head,
                new Vector3(0f, 0.085f, -0.02f), new Vector3(0.215f, 0.215f, 0.215f),
                Quaternion.Euler(Jit(2f), 0f, 0f), clothDark, 0.05f, 0.10f);
            // Faction accent: the lining ring visible around the face opening.
            Make(PrimitiveType.Cylinder, "Hood_Lining", head,
                new Vector3(0f, 0.075f, 0.085f), new Vector3(0.185f, 0.012f, 0.20f),
                Quaternion.Euler(78f + Jit(2f), 0f, 0f), clothLight, 0.0f, 0.12f);
            Make(PrimitiveType.Capsule, "Hood_Point", head,
                new Vector3(0f, 0.13f, -0.135f), new Vector3(0.07f, 0.09f, 0.09f),
                Quaternion.Euler(-48f + Jit(4f), 0f, 0f), clothDark * 0.94f, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Hood_Drape", head,
                new Vector3(0f, -0.045f, -0.09f), new Vector3(0.20f, 0.09f, 0.03f),
                Quaternion.Euler(14f, 0f, Jit(2f)), clothDark, 0.05f, 0.10f);

            root.AddComponent<ArcherAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Archer: walk cycle (leg/arm swing
    /// driven by sampled position delta; the bow arm swings less so the bow
    /// stays readable), idle bow lower/raise + head scan, and faction-color
    /// tint of the accent parts (Hood_Lining, Tabard_Front, Fletching_1-5)
    /// once EntityReference is wired by the orchestrator.
    /// </summary>
    public class ArcherAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 27f;

        [Tooltip("Arm swing amplitude in degrees at full stride (free arm).")]
        public float ArmSwing = 16f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 1.0f;

        [Tooltip("Idle bow lower/raise amplitude in degrees.")]
        public float BowIdle = 5f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest;
        private Material _liningMat, _tabardMat;
        private readonly Material[] _fletchMats = new Material[5];
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _phase;    // walk cycle phase, radians
        private float _gait;     // 0 = idle, 1 = walking (smoothed)

        void Start()
        {
            _legL  = FindDeep(transform, "LegPivot_L");
            _legR  = FindDeep(transform, "LegPivot_R");
            _armL  = FindDeep(transform, "ArmPivot_L");
            _armR  = FindDeep(transform, "ArmPivot_R");
            _torso = FindDeep(transform, "TorsoPivot");
            _head  = FindDeep(transform, "HeadPivot");
            if (_armL != null)  _armLRest  = _armL.localRotation;
            if (_armR != null)  _armRRest  = _armR.localRotation;
            if (_torso != null) _torsoRest = _torso.localRotation;
            if (_head != null)  _headRest  = _head.localRotation;

            _liningMat = MatOf("Hood_Lining");
            _tabardMat = MatOf("Tabard_Front");
            for (int i = 0; i < 5; i++)
                _fletchMats[i] = MatOf("Fletching_" + (i + 1));

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
            float idleAmt = 1f - _gait;

            if (_legL != null) _legL.localRotation = Quaternion.Euler( swing * LegSwing, 0f, 0f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-swing * LegSwing, 0f, 0f);
            // Free arm counter-swings the legs; the bow arm swings at reduced
            // amplitude, plus a slow idle lower/raise of the bow when standing.
            float bowDip = Mathf.Sin(t * 0.55f) * BowIdle * idleAmt;
            if (_armL != null)
                _armL.localRotation = _armLRest
                    * Quaternion.Euler(-swing * ArmSwing * 0.4f + bowDip, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing, 0f, 0f);

            // Idle: relaxed weight shift; walking: slight forward lean + bob.
            if (_torso != null)
            {
                float idleZ = Mathf.Sin(t * 0.85f) * 1.5f * idleAmt;
                float walkLean = 3f * _gait;
                float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.5f * _gait;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(walkLean + bob, 0f, idleZ);
            }
            // Head scan — the archer sweeps the horizon while idle.
            if (_head != null)
            {
                float yaw = Mathf.Sin(t * 0.3f) * 11f * idleAmt;
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
            Tint(_liningMat, fc, 0.10f, false);
            Tint(_tabardMat, fc, 0.15f, true);  // soft emissive so it reads at distance
            for (int i = 0; i < _fletchMats.Length; i++)
                Tint(_fletchMats[i], fc, 0.10f, false);
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
