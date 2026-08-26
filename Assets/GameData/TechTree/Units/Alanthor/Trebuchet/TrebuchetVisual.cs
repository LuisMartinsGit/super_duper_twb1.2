// File: Assets/GameData/TechTree/Units/Alanthor/Trebuchet/TrebuchetVisual.cs
// Procedural visual for the Alanthor Trebuchet (pid 348): a plank platform
// on four spoked wheels, A-frame uprights carrying a long throwing arm on an
// axle — counterweight box at the short end, sling ropes + pouch at the tip —
// plus a winch drum with crank handles. Built from primitives (Smelter idiom:
// per-part URP/Lit material, metallic/smoothness contrast, deterministic
// tilts, colliders destroyed). Player-color accent: the pennant at the arm
// tip, tinted at runtime by TrebuchetAnimator via EntityReference.
// The animator also reads the sim's TrebuchetState (co-located
// TrebuchetComponents.cs) and poses the arm: PACKED = arm lowered flat along
// the frame for travel; DEPLOYED = arm cocked back, counterweight raised.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class TrebuchetVisual
    {
        /// <summary>
        /// Builds the trebuchet and returns the root. Forward = +Z (it throws
        /// over the front; the long arm trails to the rear when cocked).
        /// Footprint ~2.2 x 3.0 m, apex at ~2.3 m; root origin at ground level.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("TrebuchetVisual");

            // Palette ---------------------------------------------------------
            var timber     = new Color(0.40f, 0.29f, 0.18f); // frame beams
            var timberDark = new Color(0.29f, 0.20f, 0.13f); // wheels, uprights
            var plank      = new Color(0.50f, 0.38f, 0.24f); // deck planks
            var plankWorn  = new Color(0.45f, 0.34f, 0.22f); // alternating deck
            var iron       = new Color(0.20f, 0.19f, 0.18f); // axle, bands
            var ironWorn   = new Color(0.31f, 0.29f, 0.27f); // hubs, fittings
            var rope       = new Color(0.62f, 0.52f, 0.34f); // sling, guys
            var canvas     = new Color(0.72f, 0.66f, 0.54f); // sling pouch
            var pennantCol = new Color(0.86f, 0.84f, 0.79f); // pennant (tinted)

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

            float Jit(float range) => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            var frame = new GameObject("Frame").transform;
            frame.SetParent(root.transform, false);
            frame.localRotation = Quaternion.Euler(0f, Jit(1f), 0f);

            // Plank deck platform ----------------------------------------------
            for (int i = 0; i < 5; i++)
            {
                float x = -0.80f + i * 0.40f;
                Make(PrimitiveType.Cube, $"Deck_Plank_{i + 1}", frame,
                    new Vector3(x, 0.52f + Jit(0.01f), 0f), new Vector3(0.38f, 0.06f, 2.90f),
                    Quaternion.Euler(0f, Jit(0.8f), 0f), (i % 2 == 0) ? plank : plankWorn, 0.05f, 0.15f);
            }
            Make(PrimitiveType.Cube, "Deck_Rail_L", frame,
                new Vector3(-1.02f, 0.46f, 0f), new Vector3(0.14f, 0.18f, 3.00f),
                Quaternion.identity, timberDark, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Deck_Rail_R", frame,
                new Vector3( 1.02f, 0.46f, 0f), new Vector3(0.14f, 0.18f, 3.00f),
                Quaternion.identity, timberDark, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Deck_CrossBeam_Front", frame,
                new Vector3(0f, 0.44f, 1.30f), new Vector3(2.00f, 0.13f, 0.13f),
                Quaternion.identity, timber, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Deck_CrossBeam_Rear", frame,
                new Vector3(0f, 0.44f, -1.30f), new Vector3(2.00f, 0.13f, 0.13f),
                Quaternion.identity, timber, 0.05f, 0.12f);

            // A-frame uprights — two legs per side meeting at the axle apex ----
            foreach (var (side, x) in new[] { ("L", -0.78f), ("R", 0.78f) })
            {
                Make(PrimitiveType.Cube, $"A_Leg_{side}_Front", frame,
                    new Vector3(x, 1.42f, 0.42f), new Vector3(0.15f, 1.90f, 0.15f),
                    Quaternion.Euler(24f + Jit(1f), 0f, 0f), timberDark, 0.05f, 0.12f);
                Make(PrimitiveType.Cube, $"A_Leg_{side}_Rear", frame,
                    new Vector3(x, 1.42f, -0.42f), new Vector3(0.15f, 1.90f, 0.15f),
                    Quaternion.Euler(-24f + Jit(1f), 0f, 0f), timberDark, 0.05f, 0.12f);
                Make(PrimitiveType.Cube, $"A_Brace_{side}", frame,
                    new Vector3(x, 1.10f, 0f), new Vector3(0.10f, 0.10f, 1.10f),
                    Quaternion.identity, timber, 0.05f, 0.12f);
                Make(PrimitiveType.Sphere, $"A_Apex_{side}", frame,
                    new Vector3(x, 2.28f, 0f), new Vector3(0.24f, 0.24f, 0.24f),
                    Quaternion.identity, ironWorn, 0.80f, 0.45f);
            }

            // Axle spanning the apexes ------------------------------------------
            Make(PrimitiveType.Cylinder, "Axle", frame,
                new Vector3(0f, 2.28f, 0f), new Vector3(0.12f, 0.90f, 0.12f),
                Quaternion.Euler(0f, 0f, 90f), iron, 0.85f, 0.45f);

            // Throwing arm on its pivot ----------------------------------------
            // Long end trails to the REAR (-Z); counterweight hangs at the
            // short front end. The animator rotates this pivot around X:
            // 0 = packed (arm flat along the frame), about -52 = cocked back.
            var armPivot = new GameObject("Arm_Pivot").transform;
            armPivot.SetParent(frame, false);
            armPivot.localPosition = new Vector3(0f, 2.28f, 0f);
            Make(PrimitiveType.Cube, "Arm_Main", armPivot,
                new Vector3(0f, 0f, -0.65f), new Vector3(0.17f, 0.20f, 3.10f),
                Quaternion.identity, timber, 0.05f, 0.15f);
            Make(PrimitiveType.Cube, "Arm_Tip", armPivot,
                new Vector3(0f, 0f, -2.55f), new Vector3(0.12f, 0.14f, 0.80f),
                Quaternion.Euler(Jit(1f), 0f, 0f), timberDark, 0.05f, 0.15f);
            Make(PrimitiveType.Cube, "Arm_Splint_L", armPivot,
                new Vector3(-0.11f, 0f, -0.10f), new Vector3(0.05f, 0.16f, 1.30f),
                Quaternion.identity, timberDark, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Arm_Splint_R", armPivot,
                new Vector3( 0.11f, 0f, -0.10f), new Vector3(0.05f, 0.16f, 1.30f),
                Quaternion.identity, timberDark, 0.05f, 0.12f);
            // Counterweight box + iron banding at the short (front) end.
            Make(PrimitiveType.Cube, "Counterweight_Box", armPivot,
                new Vector3(0f, -0.42f, 0.95f), new Vector3(0.62f, 0.58f, 0.52f),
                Quaternion.Euler(0f, Jit(1.5f), 0f), timberDark, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Counterweight_Band_1", armPivot,
                new Vector3(0f, -0.42f, 0.95f), new Vector3(0.65f, 0.08f, 0.55f),
                Quaternion.identity, iron, 0.80f, 0.40f);
            Make(PrimitiveType.Cube, "Counterweight_Hanger", armPivot,
                new Vector3(0f, -0.12f, 0.95f), new Vector3(0.10f, 0.28f, 0.10f),
                Quaternion.identity, iron, 0.80f, 0.40f);
            // Sling ropes trail off the tip; the pouch swings between them.
            Make(PrimitiveType.Cylinder, "Sling_Rope_1", armPivot,
                new Vector3(-0.045f, -0.28f, -2.86f), new Vector3(0.03f, 0.34f, 0.03f),
                Quaternion.Euler(-32f, 0f, 3f), rope, 0.0f, 0.10f);
            Make(PrimitiveType.Cylinder, "Sling_Rope_2", armPivot,
                new Vector3( 0.045f, -0.28f, -2.86f), new Vector3(0.03f, 0.34f, 0.03f),
                Quaternion.Euler(-32f, 0f, -3f), rope, 0.0f, 0.10f);
            Make(PrimitiveType.Sphere, "Sling_Pouch", armPivot,
                new Vector3(0f, -0.50f, -2.98f), new Vector3(0.26f, 0.20f, 0.30f),
                Quaternion.identity, canvas, 0.0f, 0.10f);
            // Pennant at the very tip — the faction accent.
            Make(PrimitiveType.Cylinder, "Pennant_Pole", armPivot,
                new Vector3(0f, 0.22f, -2.92f), new Vector3(0.025f, 0.22f, 0.025f),
                Quaternion.Euler(Jit(2f), 0f, Jit(2f)), timberDark, 0.05f, 0.15f);
            Make(PrimitiveType.Cube, "Pennant", armPivot,
                new Vector3(0f, 0.38f, -3.06f), new Vector3(0.015f, 0.14f, 0.30f),
                Quaternion.Euler(0f, Jit(4f), -6f), pennantCol, 0.0f, 0.20f);

            // Winch at the rear deck — drum, supports, crank handles, rope ----
            Make(PrimitiveType.Cube, "Winch_Support_L", frame,
                new Vector3(-0.42f, 0.74f, -1.22f), new Vector3(0.10f, 0.36f, 0.10f),
                Quaternion.Euler(Jit(1f), 0f, 0f), timberDark, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Winch_Support_R", frame,
                new Vector3( 0.42f, 0.74f, -1.22f), new Vector3(0.10f, 0.36f, 0.10f),
                Quaternion.Euler(Jit(1f), 0f, 0f), timberDark, 0.05f, 0.12f);
            Make(PrimitiveType.Cylinder, "Winch_Drum", frame,
                new Vector3(0f, 0.90f, -1.22f), new Vector3(0.22f, 0.40f, 0.22f),
                Quaternion.Euler(0f, 0f, 90f), timber, 0.05f, 0.20f);
            Make(PrimitiveType.Cylinder, "Winch_Handle_L", frame,
                new Vector3(-0.52f, 0.90f, -1.10f), new Vector3(0.04f, 0.14f, 0.04f),
                Quaternion.Euler(90f, 0f, 0f), ironWorn, 0.75f, 0.40f);
            Make(PrimitiveType.Cylinder, "Winch_Handle_R", frame,
                new Vector3( 0.52f, 0.90f, -1.34f), new Vector3(0.04f, 0.14f, 0.04f),
                Quaternion.Euler(90f, 0f, 0f), ironWorn, 0.75f, 0.40f);
            Make(PrimitiveType.Cylinder, "Winch_Rope", frame,
                new Vector3(0f, 1.46f, -1.05f), new Vector3(0.035f, 0.62f, 0.035f),
                Quaternion.Euler(-14f, 0f, 0f), rope, 0.0f, 0.10f);

            // Guy ropes steadying the A-frame ----------------------------------
            Make(PrimitiveType.Cylinder, "Guy_Rope_L", frame,
                new Vector3(-0.90f, 1.45f, 0.72f), new Vector3(0.03f, 1.05f, 0.03f),
                Quaternion.Euler(38f, 0f, 8f), rope, 0.0f, 0.10f);
            Make(PrimitiveType.Cylinder, "Guy_Rope_R", frame,
                new Vector3( 0.90f, 1.45f, 0.72f), new Vector3(0.03f, 1.05f, 0.03f),
                Quaternion.Euler(38f, 0f, -8f), rope, 0.0f, 0.10f);

            // Four spoked wheels — rim cylinder + crossed spokes + hub ---------
            foreach (var (name, x, z) in new[] {
                ("Wheel_FL", -1.10f,  1.15f), ("Wheel_FR", 1.10f,  1.15f),
                ("Wheel_BL", -1.10f, -1.15f), ("Wheel_BR", 1.10f, -1.15f) })
            {
                var wheel = new GameObject(name).transform;
                wheel.SetParent(root.transform, false);
                wheel.localPosition = new Vector3(x, 0.38f, z);
                // Cylinder axis Y -> lay it along X so the wheel stands in the
                // travel plane and spins around local Y when rolled.
                wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Make(PrimitiveType.Cylinder, name + "_Rim", wheel,
                    Vector3.zero, new Vector3(0.76f, 0.05f, 0.76f),
                    Quaternion.identity, timberDark, 0.05f, 0.15f);
                Make(PrimitiveType.Cube, name + "_Spoke_A", wheel,
                    Vector3.zero, new Vector3(0.09f, 0.12f, 0.70f),
                    Quaternion.identity, timber, 0.05f, 0.12f);
                Make(PrimitiveType.Cube, name + "_Spoke_B", wheel,
                    Vector3.zero, new Vector3(0.70f, 0.12f, 0.09f),
                    Quaternion.identity, timber, 0.05f, 0.12f);
                Make(PrimitiveType.Cylinder, name + "_Hub", wheel,
                    Vector3.zero, new Vector3(0.20f, 0.08f, 0.20f),
                    Quaternion.identity, ironWorn, 0.80f, 0.40f);
            }

            root.AddComponent<TrebuchetAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Trebuchet. Reads the sim's
    /// TrebuchetState via EntityReference + EntityManager (both guarded —
    /// EntityReference is added by the orchestrator after Build returns) and
    /// poses the throwing arm: PACKED (Deployed == 0, Timer == 0) keeps the
    /// arm flat along the frame for travel; while setting up, the arm winds
    /// back with Timer progress; DEPLOYED holds the cocked pose. Wheels roll
    /// with sampled ground movement, and the tip pennant is tinted in the
    /// owning player's color.
    /// </summary>
    public class TrebuchetAnimator : MonoBehaviour
    {
        [Tooltip("Wheel radius in meters (matches the built rim).")]
        public float WheelRadius = 0.38f;

        [Tooltip("Arm pivot X angle in degrees when fully cocked (deployed). Negative pulls the long rear arm down.")]
        public float CockedAngle = -52f;

        [Tooltip("Seconds the sim needs to deploy (TrebuchetState.Timer full scale).")]
        public float DeploySeconds = 3f;

        [Tooltip("Degrees per second the visual arm may move (keeps pose changes smooth).")]
        public float ArmDegreesPerSecond = 45f;

        private Transform[] _wheels = System.Array.Empty<Transform>();
        private Transform _armPivot;
        private Material _pennantMat;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _armAngle; // current pivot X angle, degrees

        void Start()
        {
            var wheelList = new System.Collections.Generic.List<Transform>();
            CollectByPrefix(transform, "Wheel_", wheelList);
            // Keep only the pivot roots (children carry _Rim/_Spoke/_Hub suffixes).
            wheelList.RemoveAll(t => t.name.Contains("_Rim") || t.name.Contains("_Spoke") || t.name.Contains("_Hub"));
            _wheels = wheelList.ToArray();

            _armPivot = FindDeep(transform, "Arm_Pivot");

            var pennant = FindDeep(transform, "Pennant");
            if (pennant != null && pennant.TryGetComponent<MeshRenderer>(out var r))
                _pennantMat = r.material; // instance — safe to tint per unit

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) { _em = world.EntityManager; _emReady = true; }
        }

        void LateUpdate()
        {
            if (!_tinted) TryTint();

            // Wheels roll with covered ground distance.
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
            if (dist > 0f && WheelRadius > 0.01f)
            {
                float deg = (dist / WheelRadius) * Mathf.Rad2Deg;
                for (int i = 0; i < _wheels.Length; i++)
                    if (_wheels[i] != null)
                        _wheels[i].Rotate(0f, deg, 0f, Space.Self);
            }

            // Arm pose from sim state: 0 = packed, 1 = cocked.
            float deployBlend = 0f;
            if (_emReady && _entityRef != null)
            {
                var e = _entityRef.Entity;
                if (e != Entity.Null && _em.Exists(e) && _em.HasComponent<TrebuchetState>(e))
                {
                    var st = _em.GetComponentData<TrebuchetState>(e);
                    deployBlend = (st.Deployed != 0)
                        ? 1f
                        : Mathf.Clamp01(st.Timer / Mathf.Max(DeploySeconds, 0.01f));
                }
            }
            float target = CockedAngle * deployBlend;
            _armAngle = Mathf.MoveTowards(_armAngle, target, ArmDegreesPerSecond * Time.deltaTime);
            if (_armPivot != null)
                _armPivot.localRotation = Quaternion.Euler(_armAngle, 0f, 0f);
        }

        private void TryTint()
        {
            // EntityReference is added by the orchestrator AFTER Build returns.
            if (_entityRef == null)
            {
                _entityRef = GetComponent<EntityReference>();
                if (_entityRef == null) return;
            }
            if (!_emReady) return;
            var e = _entityRef.Entity;
            if (e == Entity.Null || !_em.Exists(e) || !_em.HasComponent<FactionTag>(e)) return;

            var fc = FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value);
            if (_pennantMat != null)
            {
                var baseCol = Color.Lerp(fc, Color.white, 0.10f);
                _pennantMat.SetColor("_BaseColor", baseCol);
                if (_pennantMat.HasProperty("_Color")) _pennantMat.SetColor("_Color", baseCol);
                if (_pennantMat.HasProperty("_EmissionColor"))
                {
                    _pennantMat.EnableKeyword("_EMISSION");
                    _pennantMat.SetColor("_EmissionColor", fc * 0.35f);
                }
            }
            _tinted = true;
        }

        private static void CollectByPrefix(Transform root, string prefix,
            System.Collections.Generic.List<Transform> into)
        {
            if (root.name.StartsWith(prefix)) into.Add(root);
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
