// Procedural visual for the Alanthor Battering Ram (pid 347): a heavy timber
// frame — four corner posts, an angled protective canopy of individual
// planks, an iron-capped log ram slung on two ropes, and four spoked wheels.
// Built from primitives (Smelter idiom — per-part URP/Lit material,
// metallic/smoothness contrast, deterministic tilts, colliders destroyed).
// Player-color accent: the canopy edge trim boards, tinted at runtime by
// BatteringRamAnimator via EntityReference (added by the orchestrator after
// Build returns — the animator guards for it being absent).

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class BatteringRamVisual
    {
        /// <summary>
        /// Builds the ram and returns the root. Forward = +Z (ram head at the
        /// front). Footprint ~1.8 x 3.2 m, canopy ridge at ~2.0 m. Wheels sit
        /// so the frame clears the ground; root origin is at ground level.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("BatteringRamVisual");

            // Palette ---------------------------------------------------------
            var timber     = new Color(0.38f, 0.27f, 0.17f); // structural beams
            var timberDark = new Color(0.28f, 0.19f, 0.12f); // posts, rails
            var plank      = new Color(0.48f, 0.36f, 0.22f); // canopy planks
            var plankWorn  = new Color(0.43f, 0.33f, 0.21f); // alternating planks
            var iron       = new Color(0.20f, 0.19f, 0.18f); // ram cap, bands
            var ironWorn   = new Color(0.30f, 0.28f, 0.26f); // wheel hubs
            var rope       = new Color(0.62f, 0.52f, 0.34f); // slings
            var log        = new Color(0.33f, 0.23f, 0.14f); // the ram log
            var trimBase   = new Color(0.85f, 0.83f, 0.78f); // canopy trim (tinted)


            System.Func<PrimitiveType, string, Transform, Vector3, Vector3, Quaternion, Color, float, float, GameObject>
            Make = (type, name, parent, lp, ls, lr, color, metal, smooth) =>
                ProceduralPrimitive.Make(type, name, parent, lp, ls, lr, color, metal, smooth, false);

            float Jit(float range) => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            var frame = new GameObject("Frame").transform;
            frame.SetParent(root.transform, false);
            frame.localPosition = new Vector3(0f, 0f, 0f);
            frame.localRotation = Quaternion.Euler(0f, Jit(1f), 0f);

            // Base rails + cross beams (axle height ~0.42) ---------------------
            Make(PrimitiveType.Cube, "Rail_L", frame,
                new Vector3(-0.72f, 0.50f, 0f), new Vector3(0.16f, 0.16f, 3.10f),
                Quaternion.identity, timberDark, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Rail_R", frame,
                new Vector3( 0.72f, 0.50f, 0f), new Vector3(0.16f, 0.16f, 3.10f),
                Quaternion.identity, timberDark, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "CrossBeam_Front", frame,
                new Vector3(0f, 0.50f, 1.40f), new Vector3(1.56f, 0.14f, 0.14f),
                Quaternion.identity, timber, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "CrossBeam_Rear", frame,
                new Vector3(0f, 0.50f, -1.40f), new Vector3(1.56f, 0.14f, 0.14f),
                Quaternion.identity, timber, 0.05f, 0.12f);

            // Four corner posts, leaning inward toward the ridge ---------------
            foreach (var (name, x, z) in new[] {
                ("Post_FL", -0.66f,  1.30f), ("Post_FR", 0.66f,  1.30f),
                ("Post_BL", -0.66f, -1.30f), ("Post_BR", 0.66f, -1.30f) })
            {
                float lean = (x < 0f) ? -16f : 16f;
                Make(PrimitiveType.Cube, name, frame,
                    new Vector3(x * 0.82f, 1.18f, z), new Vector3(0.14f, 1.45f, 0.14f),
                    Quaternion.Euler(Jit(1.5f), 0f, -lean), timberDark, 0.05f, 0.12f);
            }

            // Ridge beam + side stringers the canopy planks rest on ------------
            Make(PrimitiveType.Cube, "Ridge_Beam", frame,
                new Vector3(0f, 1.92f, 0f), new Vector3(0.15f, 0.15f, 3.05f),
                Quaternion.Euler(0f, 0f, Jit(1f)), timber, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Stringer_L", frame,
                new Vector3(-0.62f, 1.44f, 0f), new Vector3(0.10f, 0.10f, 3.00f),
                Quaternion.identity, timber, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Stringer_R", frame,
                new Vector3( 0.62f, 1.44f, 0f), new Vector3(0.10f, 0.10f, 3.00f),
                Quaternion.identity, timber, 0.05f, 0.12f);

            // Protective canopy — individual angled planks, 6 per side ---------
            for (int i = 0; i < 6; i++)
            {
                float z = -1.25f + i * 0.50f;
                float sag = Jit(0.02f);
                Make(PrimitiveType.Cube, $"Canopy_Plank_L{i + 1}", frame,
                    new Vector3(-0.38f, 1.70f + sag, z), new Vector3(0.86f, 0.045f, 0.46f),
                    Quaternion.Euler(Jit(1.5f), Jit(2f), 32f + Jit(2f)), (i % 2 == 0) ? plank : plankWorn, 0.05f, 0.15f);
                Make(PrimitiveType.Cube, $"Canopy_Plank_R{i + 1}", frame,
                    new Vector3( 0.38f, 1.70f - sag, z), new Vector3(0.86f, 0.045f, 0.46f),
                    Quaternion.Euler(Jit(1.5f), Jit(2f), -32f - Jit(2f)), (i % 2 == 0) ? plankWorn : plank, 0.05f, 0.15f);
            }

            // Canopy edge trim — the faction accent boards along the eaves -----
            Make(PrimitiveType.Cube, "Trim_Edge_L", frame,
                new Vector3(-0.76f, 1.46f, 0f), new Vector3(0.06f, 0.16f, 3.02f),
                Quaternion.Euler(0f, 0f, 32f), trimBase, 0.0f, 0.25f);
            Make(PrimitiveType.Cube, "Trim_Edge_R", frame,
                new Vector3( 0.76f, 1.46f, 0f), new Vector3(0.06f, 0.16f, 3.02f),
                Quaternion.Euler(0f, 0f, -32f), trimBase, 0.0f, 0.25f);
            Make(PrimitiveType.Cube, "Trim_Front", frame,
                new Vector3(0f, 1.94f, 1.56f), new Vector3(0.30f, 0.22f, 0.04f),
                Quaternion.Euler(0f, 0f, 45f), trimBase, 0.0f, 0.25f);

            // The ram — a slung log with an iron head, hanging from the ridge --
            // Ram_Pivot sits at the ridge so the whole assembly (ropes + log)
            // swings around it like a pendulum driven along Z.
            var ramPivot = new GameObject("Ram_Pivot").transform;
            ramPivot.SetParent(frame, false);
            ramPivot.localPosition = new Vector3(0f, 1.88f, 0.10f);
            Make(PrimitiveType.Cylinder, "Rope_Front", ramPivot,
                new Vector3(0f, -0.42f, 0.75f), new Vector3(0.045f, 0.42f, 0.045f),
                Quaternion.Euler(Jit(1f), 0f, 0f), rope, 0.0f, 0.10f);
            Make(PrimitiveType.Cylinder, "Rope_Rear", ramPivot,
                new Vector3(0f, -0.42f, -0.75f), new Vector3(0.045f, 0.42f, 0.045f),
                Quaternion.Euler(Jit(1f), 0f, 0f), rope, 0.0f, 0.10f);
            Make(PrimitiveType.Cylinder, "Ram_Log", ramPivot,
                new Vector3(0f, -0.86f, 0.10f), new Vector3(0.34f, 1.55f, 0.34f),
                Quaternion.Euler(90f, 0f, 0f), log, 0.05f, 0.10f);
            Make(PrimitiveType.Cylinder, "Ram_Head", ramPivot,
                new Vector3(0f, -0.86f, 1.72f), new Vector3(0.40f, 0.14f, 0.40f),
                Quaternion.Euler(90f, 0f, 0f), iron, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Ram_Head_Tip", ramPivot,
                new Vector3(0f, -0.86f, 1.90f), new Vector3(0.30f, 0.30f, 0.22f),
                Quaternion.identity, iron, 0.85f, 0.50f);
            Make(PrimitiveType.Cylinder, "Ram_Band_1", ramPivot,
                new Vector3(0f, -0.86f, 0.72f), new Vector3(0.365f, 0.035f, 0.365f),
                Quaternion.Euler(90f, 0f, 0f), ironWorn, 0.80f, 0.40f);
            Make(PrimitiveType.Cylinder, "Ram_Band_2", ramPivot,
                new Vector3(0f, -0.86f, -0.55f), new Vector3(0.365f, 0.035f, 0.365f),
                Quaternion.Euler(90f, 0f, 0f), ironWorn, 0.80f, 0.40f);
            Make(PrimitiveType.Cylinder, "Ram_Butt", ramPivot,
                new Vector3(0f, -0.86f, -1.62f), new Vector3(0.30f, 0.06f, 0.30f),
                Quaternion.Euler(90f, 0f, 0f), timberDark, 0.05f, 0.10f);

            // Four spoked wheels — rim cylinder + two crossed spoke bars + hub -
            foreach (var (name, x, z) in new[] {
                ("Wheel_FL", -0.86f,  1.10f), ("Wheel_FR", 0.86f,  1.10f),
                ("Wheel_BL", -0.86f, -1.10f), ("Wheel_BR", 0.86f, -1.10f) })
            {
                var wheel = new GameObject(name).transform;
                wheel.SetParent(root.transform, false);
                wheel.localPosition = new Vector3(x, 0.42f, z);
                // Cylinder axis Y -> rotate so the wheel stands in the XZ travel
                // plane and spins around local Y when the animator rolls it.
                wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Make(PrimitiveType.Cylinder, name + "_Rim", wheel,
                    Vector3.zero, new Vector3(0.84f, 0.055f, 0.84f),
                    Quaternion.identity, timberDark, 0.05f, 0.15f);
                Make(PrimitiveType.Cube, name + "_Spoke_A", wheel,
                    Vector3.zero, new Vector3(0.10f, 0.13f, 0.78f),
                    Quaternion.identity, timber, 0.05f, 0.12f);
                Make(PrimitiveType.Cube, name + "_Spoke_B", wheel,
                    Vector3.zero, new Vector3(0.78f, 0.13f, 0.10f),
                    Quaternion.identity, timber, 0.05f, 0.12f);
                Make(PrimitiveType.Cylinder, name + "_Hub", wheel,
                    Vector3.zero, new Vector3(0.22f, 0.09f, 0.22f),
                    Quaternion.identity, ironWorn, 0.80f, 0.40f);
            }

            root.AddComponent<BatteringRamAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Battering Ram: the four wheels
    /// roll with sampled ground movement, and while the ram stands still the
    /// slung log swings fore-and-aft on its ropes (reads as battering — the
    /// ram only ever stops to hit something or to wait). Also tints the
    /// canopy trim accent parts in the owning player's color via
    /// EntityReference (added by the orchestrator after Build; guarded).
    /// </summary>
    public class BatteringRamAnimator : MonoBehaviour
    {
        [Tooltip("Wheel radius in meters (matches the built rim).")]
        public float WheelRadius = 0.42f;

        [Tooltip("Ram swing amplitude in degrees while battering.")]
        public float SwingAmplitude = 14f;

        [Tooltip("Ram swing frequency in Hz while battering.")]
        public float SwingFrequency = 0.55f;

        private Transform[] _wheels = System.Array.Empty<Transform>();
        private Transform _ramPivot;
        private Material[] _trimMats = System.Array.Empty<Material>();
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _swing;      // current swing angle, degrees
        private float _swingPhase; // radians
        private float _still;      // seconds spent stationary (smoothes onset)

        void Start()
        {
            var wheelList = new System.Collections.Generic.List<Transform>();
            CollectByPrefix(transform, "Wheel_", wheelList);
            // Only the pivot roots (children carry the _Rim/_Spoke suffix names).
            wheelList.RemoveAll(t => t.name.Contains("_Rim") || t.name.Contains("_Spoke") || t.name.Contains("_Hub"));
            _wheels = wheelList.ToArray();

            _ramPivot = FindDeep(transform, "Ram_Pivot");

            var mats = new System.Collections.Generic.List<Material>();
            foreach (var trimName in new[] { "Trim_Edge_L", "Trim_Edge_R", "Trim_Front" })
            {
                var t = FindDeep(transform, trimName);
                if (t != null && t.TryGetComponent<MeshRenderer>(out var r))
                    mats.Add(r.material); // instance — safe to tint per unit
            }
            _trimMats = mats.ToArray();

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) { _em = world.EntityManager; _emReady = true; }
        }

        void LateUpdate()
        {
            if (!_tinted) TryTint();

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
            bool moving = (dist / dt) > 0.1f;

            // Wheels roll with covered ground distance.
            if (dist > 0f && WheelRadius > 0.01f)
            {
                float deg = (dist / WheelRadius) * Mathf.Rad2Deg;
                for (int i = 0; i < _wheels.Length; i++)
                    if (_wheels[i] != null)
                        _wheels[i].Rotate(0f, deg, 0f, Space.Self);
            }

            // Ram swing: builds up while stationary (battering), settles fast
            // once the machine starts rolling again.
            _still = moving ? 0f : _still + dt;
            float targetAmp = (_still > 0.4f) ? SwingAmplitude : 0f;
            _amp = Mathf.MoveTowards(_amp, targetAmp, dt * 20f);
            _swingPhase += dt * SwingFrequency * 2f * Mathf.PI;
            // Asymmetric stroke: slow draw back, hard strike forward.
            float wave = Mathf.Sin(_swingPhase);
            float shaped = Mathf.Sign(wave) * Mathf.Pow(Mathf.Abs(wave), wave > 0f ? 0.6f : 1.4f);
            _swing = shaped * _amp;
            if (_ramPivot != null)
                _ramPivot.localRotation = Quaternion.Euler(-_swing, 0f, 0f);
        }

        private float _amp; // smoothed swing amplitude, degrees

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
            var baseCol = Color.Lerp(fc, Color.white, 0.12f);
            for (int i = 0; i < _trimMats.Length; i++)
            {
                var m = _trimMats[i];
                if (m == null) continue;
                m.SetColor("_BaseColor", baseCol);
                if (m.HasProperty("_Color")) m.SetColor("_Color", baseCol);
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
