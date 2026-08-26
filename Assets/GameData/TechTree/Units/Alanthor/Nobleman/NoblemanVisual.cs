// File: Assets/GameData/TechTree/Units/Alanthor/Nobleman/NoblemanVisual.cs
// Procedural foot-knight visual for the Alanthor Nobleman (pid 346): layered
// pauldrons, breastplate over a tabard, greaves, longsword + heater shield,
// plumed helm. Built entirely from primitives (Smelter idiom — per-part
// URP/Lit material, metallic/smoothness contrast, small deterministic tilts,
// colliders destroyed). Player-color accents (tabard front, shield emblem,
// helm plume) are tinted at runtime by NoblemanAnimator via EntityReference
// (LedgerVisual.TryTint pattern) — the orchestrator adds EntityReference
// after Build returns, so the animator guards for it being absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Input; // EntityReference

namespace TheWaningBorder.Presentation
{
    public static class NoblemanVisual
    {
        /// <summary>
        /// Builds the full Nobleman rig and returns the root. The root sits at
        /// ground level (feet at y=0); total height ~1.95 m. Deterministic:
        /// all jitter flows through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("NoblemanVisual");

            // Palette ---------------------------------------------------------
            var steel      = new Color(0.68f, 0.70f, 0.75f); // polished plate
            var steelDark  = new Color(0.46f, 0.48f, 0.53f); // recessed plate
            var iron       = new Color(0.24f, 0.24f, 0.26f); // visor, sabatons
            var brass      = new Color(0.66f, 0.50f, 0.20f); // buckle, trim
            var clothLight = new Color(0.88f, 0.86f, 0.80f); // tabard base (tinted)
            var clothDark  = new Color(0.34f, 0.30f, 0.26f); // under-padding
            var leather    = new Color(0.42f, 0.26f, 0.16f); // belt, grip
            var skin       = new Color(0.78f, 0.62f, 0.50f);
            var plumeBase  = new Color(0.85f, 0.85f, 0.88f); // plume (tinted)

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
            pelvis.localPosition = new Vector3(0f, 0.98f, 0f);

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
            var legL = LegPivot("LegPivot_L", -0.115f);
            var legR = LegPivot("LegPivot_R",  0.115f);

            Transform ArmPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(torso, false);
                t.localPosition = new Vector3(x, 0.46f, 0f); // shoulder height
                return t;
            }
            var armL = ArmPivot("ArmPivot_L", -0.30f);
            var armR = ArmPivot("ArmPivot_R",  0.30f);

            // Legs (children of the leg pivots; positions relative to hip) -----
            foreach (var (side, pivot, mirror) in new[] { ("L", legL, -1f), ("R", legR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "Thigh_" + side, pivot,
                    new Vector3(0f, -0.22f, 0f), new Vector3(0.145f, 0.155f, 0.145f),
                    Quaternion.Euler(0f, 0f, mirror * 2f), clothDark, 0.05f, 0.15f);
                Make(PrimitiveType.Sphere, "Knee_" + side, pivot,
                    new Vector3(0f, -0.42f, 0.01f), new Vector3(0.13f, 0.11f, 0.13f),
                    Quaternion.identity, steelDark, 0.80f, 0.55f);
                Make(PrimitiveType.Cylinder, "Greave_" + side, pivot,
                    new Vector3(0f, -0.64f, 0f), new Vector3(0.125f, 0.17f, 0.125f),
                    Quaternion.Euler(Jit(1.5f), 0f, 0f), steel, 0.85f, 0.60f);
                Make(PrimitiveType.Cube, "Sabaton_" + side, pivot,
                    new Vector3(0f, -0.90f, 0.055f), new Vector3(0.13f, 0.10f, 0.26f),
                    Quaternion.Euler(0f, mirror * 4f, 0f), iron, 0.70f, 0.40f);
            }

            // Torso ------------------------------------------------------------
            Make(PrimitiveType.Cube, "Skirt", torso,
                new Vector3(0f, -0.02f, 0f), new Vector3(0.34f, 0.16f, 0.24f),
                Quaternion.identity, clothDark, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Tasset_L", torso,
                new Vector3(-0.17f, -0.03f, 0f), new Vector3(0.07f, 0.15f, 0.22f),
                Quaternion.Euler(0f, 0f,  14f), steelDark, 0.80f, 0.50f);
            Make(PrimitiveType.Cube, "Tasset_R", torso,
                new Vector3( 0.17f, -0.03f, 0f), new Vector3(0.07f, 0.15f, 0.22f),
                Quaternion.Euler(0f, 0f, -14f), steelDark, 0.80f, 0.50f);
            Make(PrimitiveType.Cube, "Belt", torso,
                new Vector3(0f, 0.08f, 0f), new Vector3(0.35f, 0.06f, 0.25f),
                Quaternion.identity, leather, 0.10f, 0.25f);
            Make(PrimitiveType.Cube, "BeltBuckle", torso,
                new Vector3(0f, 0.08f, 0.125f), new Vector3(0.07f, 0.05f, 0.02f),
                Quaternion.identity, brass, 0.75f, 0.65f);
            Make(PrimitiveType.Cube, "Breastplate", torso,
                new Vector3(0f, 0.30f, 0f), new Vector3(0.38f, 0.40f, 0.26f),
                Quaternion.Euler(Jit(1f), 0f, 0f), steel, 0.85f, 0.60f);
            Make(PrimitiveType.Cube, "Plackart", torso,
                new Vector3(0f, 0.145f, 0.005f), new Vector3(0.36f, 0.09f, 0.27f),
                Quaternion.identity, steelDark, 0.85f, 0.50f);
            // Tabard hangs over the plate front and back — the front is the
            // faction accent the animator tints.
            Make(PrimitiveType.Cube, "Tabard_Front", torso,
                new Vector3(0f, 0.18f, 0.145f), new Vector3(0.24f, 0.52f, 0.015f),
                Quaternion.Euler(-2f, 0f, Jit(1f)), clothLight, 0.0f, 0.12f);
            Make(PrimitiveType.Cube, "Tabard_Back", torso,
                new Vector3(0f, 0.16f, -0.145f), new Vector3(0.24f, 0.48f, 0.015f),
                Quaternion.Euler(2.5f, 0f, Jit(1f)), clothLight * 0.92f, 0.0f, 0.10f);
            Make(PrimitiveType.Cylinder, "Gorget", torso,
                new Vector3(0f, 0.52f, 0f), new Vector3(0.17f, 0.045f, 0.17f),
                Quaternion.identity, steelDark, 0.80f, 0.50f);

            // Layered pauldrons — three overlapping plates per shoulder --------
            foreach (var (side, mirror) in new[] { ("L", -1f), ("R", 1f) })
            {
                Make(PrimitiveType.Sphere, "Pauldron_" + side + "_1", torso,
                    new Vector3(mirror * 0.255f, 0.50f, 0f), new Vector3(0.20f, 0.15f, 0.20f),
                    Quaternion.Euler(0f, 0f, mirror * -12f), steel, 0.85f, 0.62f);
                Make(PrimitiveType.Sphere, "Pauldron_" + side + "_2", torso,
                    new Vector3(mirror * 0.30f, 0.44f, 0f), new Vector3(0.17f, 0.11f, 0.18f),
                    Quaternion.Euler(0f, 0f, mirror * -24f), steelDark, 0.85f, 0.55f);
                Make(PrimitiveType.Sphere, "Pauldron_" + side + "_3", torso,
                    new Vector3(mirror * 0.335f, 0.385f, 0f), new Vector3(0.14f, 0.09f, 0.16f),
                    Quaternion.Euler(0f, 0f, mirror * -34f), steelDark * 0.9f, 0.85f, 0.50f);
            }

            // Arms (children of the shoulder pivots) ---------------------------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "UpperArm_" + side, pivot,
                    new Vector3(mirror * 0.03f, -0.14f, 0f), new Vector3(0.085f, 0.12f, 0.085f),
                    Quaternion.Euler(0f, 0f, mirror * 6f), steel, 0.80f, 0.55f);
                Make(PrimitiveType.Sphere, "Elbow_" + side, pivot,
                    new Vector3(mirror * 0.045f, -0.27f, 0.01f), new Vector3(0.095f, 0.085f, 0.095f),
                    Quaternion.identity, steelDark, 0.80f, 0.50f);
                Make(PrimitiveType.Cylinder, "Forearm_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.385f, 0.03f), new Vector3(0.075f, 0.105f, 0.075f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), steel, 0.80f, 0.55f);
                Make(PrimitiveType.Cube, "Gauntlet_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.50f, 0.07f), new Vector3(0.09f, 0.10f, 0.11f),
                    Quaternion.Euler(-8f, 0f, 0f), iron, 0.70f, 0.45f);
            }

            // Longsword in the right hand (blade forward-down at rest) ---------
            var sword = new GameObject("Sword").transform;
            sword.SetParent(armR, false);
            sword.localPosition = new Vector3(0.055f, -0.52f, 0.09f);
            sword.localRotation = Quaternion.Euler(24f, 0f, Jit(2f));
            Make(PrimitiveType.Cylinder, "Sword_Grip", sword,
                new Vector3(0f, 0f, 0f), new Vector3(0.035f, 0.09f, 0.035f),
                Quaternion.identity, leather, 0.10f, 0.25f);
            Make(PrimitiveType.Sphere, "Sword_Pommel", sword,
                new Vector3(0f, -0.11f, 0f), new Vector3(0.055f, 0.055f, 0.055f),
                Quaternion.identity, brass, 0.80f, 0.70f);
            Make(PrimitiveType.Cube, "Sword_Guard", sword,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.20f, 0.03f, 0.045f),
                Quaternion.identity, brass, 0.80f, 0.65f);
            Make(PrimitiveType.Cube, "Sword_Blade", sword,
                new Vector3(0f, 0.52f, 0f), new Vector3(0.045f, 0.82f, 0.014f),
                Quaternion.identity, steel, 0.90f, 0.80f);
            Make(PrimitiveType.Cube, "Sword_Tip", sword,
                new Vector3(0f, 0.955f, 0f), new Vector3(0.028f, 0.06f, 0.012f),
                Quaternion.identity, steel, 0.90f, 0.80f);

            // Heater shield on the left arm ------------------------------------
            var shield = new GameObject("Shield").transform;
            shield.SetParent(armL, false);
            shield.localPosition = new Vector3(-0.135f, -0.34f, 0.05f);
            shield.localRotation = Quaternion.Euler(0f, 8f + Jit(2f), 0f);
            Make(PrimitiveType.Cube, "Shield_Face", shield,
                Vector3.zero, new Vector3(0.05f, 0.46f, 0.34f),
                Quaternion.identity, steelDark, 0.60f, 0.45f);
            Make(PrimitiveType.Cube, "Shield_Point", shield,
                new Vector3(0f, -0.27f, 0f), new Vector3(0.05f, 0.10f, 0.20f),
                Quaternion.identity, steelDark, 0.60f, 0.45f);
            Make(PrimitiveType.Cube, "Shield_Rim", shield,
                new Vector3(-0.005f, 0.225f, 0f), new Vector3(0.055f, 0.03f, 0.35f),
                Quaternion.identity, brass, 0.75f, 0.60f);
            // Faction emblem quad — sits proud of the face; tinted at runtime.
            Make(PrimitiveType.Cube, "Shield_Emblem", shield,
                new Vector3(-0.032f, 0.03f, 0f), new Vector3(0.012f, 0.24f, 0.19f),
                Quaternion.identity, clothLight, 0.0f, 0.30f);

            // Head + plumed helm ------------------------------------------------
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0f, 0.60f, 0f);
            head.localRotation = Quaternion.Euler(Jit(1.5f), Jit(3f), 0f);
            Make(PrimitiveType.Sphere, "Head", head,
                new Vector3(0f, 0.07f, 0f), new Vector3(0.185f, 0.20f, 0.185f),
                Quaternion.identity, skin, 0.0f, 0.30f);
            Make(PrimitiveType.Sphere, "Helm_Bowl", head,
                new Vector3(0f, 0.115f, 0f), new Vector3(0.21f, 0.19f, 0.21f),
                Quaternion.identity, steel, 0.85f, 0.65f);
            Make(PrimitiveType.Cylinder, "Helm_Brim", head,
                new Vector3(0f, 0.05f, 0.01f), new Vector3(0.225f, 0.018f, 0.235f),
                Quaternion.Euler(4f, 0f, 0f), steelDark, 0.80f, 0.55f);
            Make(PrimitiveType.Cube, "Helm_Visor", head,
                new Vector3(0f, 0.055f, 0.095f), new Vector3(0.15f, 0.05f, 0.04f),
                Quaternion.identity, iron, 0.70f, 0.35f);
            Make(PrimitiveType.Cylinder, "Plume_Socket", head,
                new Vector3(0f, 0.21f, -0.01f), new Vector3(0.035f, 0.025f, 0.035f),
                Quaternion.identity, brass, 0.75f, 0.60f);
            // Plume — faction accent, tinted at runtime; slight backward arc.
            Make(PrimitiveType.Capsule, "Plume", head,
                new Vector3(0f, 0.30f, -0.05f), new Vector3(0.07f, 0.11f, 0.10f),
                Quaternion.Euler(-28f + Jit(4f), 0f, 0f), plumeBase, 0.0f, 0.15f);
            Make(PrimitiveType.Capsule, "Plume_Tail", head,
                new Vector3(0f, 0.24f, -0.135f), new Vector3(0.05f, 0.08f, 0.07f),
                Quaternion.Euler(-52f, 0f, 0f), plumeBase * 0.95f, 0.0f, 0.12f);

            root.AddComponent<NoblemanAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Nobleman: walk cycle (leg/arm
    /// swing driven by sampled position delta), idle sway, and faction-color
    /// tint of the accent parts (Tabard_Front, Shield_Emblem, Plume,
    /// Plume_Tail) once EntityReference is wired by the orchestrator.
    /// </summary>
    public class NoblemanAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 26f;

        [Tooltip("Arm swing amplitude in degrees at full stride.")]
        public float ArmSwing = 16f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 1.15f;

        [Tooltip("Idle torso sway in degrees.")]
        public float IdleSway = 1.6f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest;
        private Material _tabardMat, _emblemMat, _plumeMat, _plumeTailMat;
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

            _tabardMat    = MatOf("Tabard_Front");
            _emblemMat    = MatOf("Shield_Emblem");
            _plumeMat     = MatOf("Plume");
            _plumeTailMat = MatOf("Plume_Tail");

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
            // Arms counter-swing the legs; blend back to the authored rest pose
            // (sword low, shield braced) when idle.
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(-swing * ArmSwing, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing, 0f, 0f);

            // Idle: slow weight-shift sway + a small head turn; while walking
            // the torso instead takes a slight forward lean and step bob.
            if (_torso != null)
            {
                float idleZ = Mathf.Sin(t * 0.9f) * IdleSway * (1f - _gait);
                float walkLean = 3.5f * _gait;
                float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.5f * _gait;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(walkLean + bob, 0f, idleZ);
            }
            if (_head != null)
            {
                float yaw = Mathf.Sin(t * 0.35f) * 6f * (1f - _gait);
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
            Tint(_tabardMat, fc, 0.15f, false);
            Tint(_emblemMat, fc, 0.0f, true);   // soft emissive so it reads at distance
            Tint(_plumeMat, fc, 0.10f, false);
            Tint(_plumeTailMat, fc * 0.9f, 0.10f, false);
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
                m.SetColor("_EmissionColor", c * 0.45f);
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
