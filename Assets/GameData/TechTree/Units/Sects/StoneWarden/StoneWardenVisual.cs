// File: Assets/GameData/TechTree/Units/Sects/StoneWarden/StoneWardenVisual.cs
// Procedural visual for the Sect of Fortitude Stone Warden: slow heavy
// infantry that projects a damage-reduction dome and can NEVER attack, so
// the rig carries NO weapon of any kind — the silhouette has to read
// "walking wall" from the RTS camera. Granite-grey slab armour, stone-slab
// pauldrons stacked in two lames, a full closed helm with no visor slit at
// all (a blank carved face plate), a very wide braced stance, and an
// enormous tower shield on the left arm that covers the body from ankle to
// crown, ground-spiked at the foot and hung with a pennon. Built entirely
// from primitives (Smelter idiom — per-part URP/Lit material,
// metallic/smoothness contrast, small deterministic tilts, colliders
// destroyed). Player-color accents (Shield_Face, Tunic_Trim, Pennon) are
// tinted at runtime by StoneWardenAnimator via EntityReference
// (LedgerVisual.TryTint pattern) — the orchestrator adds EntityReference
// after Build returns, so the animator guards for it being absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class StoneWardenVisual
    {
        /// <summary>
        /// Builds the full Stone Warden rig and returns the root. The root
        /// sits at ground level (feet at y=0); figure height ~1.76 m to the
        /// helm crown, tower shield spans y=0.09 to y=1.59. The pelvis rides
        /// lower and the legs sit wider than the Spearman's on purpose — this
        /// unit is meant to look planted. Deterministic: all jitter flows
        /// through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("StoneWardenVisual");

            // Palette ---------------------------------------------------------
            var granite     = new Color(0.46f, 0.46f, 0.48f); // slab armour
            var graniteDark = new Color(0.31f, 0.31f, 0.33f); // sabatons, shadowed slabs
            var graniteLite = new Color(0.60f, 0.60f, 0.62f); // face plate, knee cops
            var iron        = new Color(0.50f, 0.52f, 0.55f); // boss, spine, buckles
            var ironDark    = new Color(0.27f, 0.28f, 0.31f); // rims, bands, rivets
            var clothDark   = new Color(0.30f, 0.29f, 0.28f); // under-cloth at the joints
            var clothLight  = new Color(0.87f, 0.85f, 0.79f); // accent base (tinted)
            var leather     = new Color(0.41f, 0.26f, 0.16f); // shield straps
            var moss        = new Color(0.30f, 0.36f, 0.25f); // in the stone crevices

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
            // Pelvis rides at 0.90 (not 0.94): squatter than the line infantry.
            var pelvis = new GameObject("Pelvis").transform;
            pelvis.SetParent(root.transform, false);
            pelvis.localPosition = new Vector3(0f, 0.90f, 0f);

            var torso = new GameObject("TorsoPivot").transform;
            torso.SetParent(pelvis, false);
            torso.localPosition = Vector3.zero;
            torso.localRotation = Quaternion.Euler(Jit(1f), 0f, Jit(0.8f));

            Transform LegPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(pelvis, false);
                t.localPosition = new Vector3(x, 0f, 0f); // hip height, swings around X
                return t;
            }
            // Wide braced stance: nearly 50% further out than the Spearman.
            var legL = LegPivot("LegPivot_L", -0.155f);
            var legR = LegPivot("LegPivot_R",  0.155f);

            Transform ArmPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(torso, false);
                t.localPosition = new Vector3(x, 0.44f, 0f); // shoulder height
                return t;
            }
            var armL = ArmPivot("ArmPivot_L", -0.305f);
            var armR = ArmPivot("ArmPivot_R",  0.305f);

            // Legs — blocky stone columns splayed outward -----------------------
            foreach (var (side, pivot, mirror) in new[] { ("L", legL, -1f), ("R", legR, 1f) })
            {
                Make(PrimitiveType.Cube, "Thigh_" + side, pivot,
                    new Vector3(0f, -0.20f, 0f), new Vector3(0.20f, 0.26f, 0.21f),
                    Quaternion.Euler(0f, 0f, mirror * 4f), granite, 0.45f, 0.22f);
                Make(PrimitiveType.Sphere, "Knee_Cop_" + side, pivot,
                    new Vector3(mirror * 0.012f, -0.375f, 0.015f), new Vector3(0.165f, 0.135f, 0.165f),
                    Quaternion.identity, graniteLite, 0.45f, 0.25f);
                Make(PrimitiveType.Cube, "Shin_" + side, pivot,
                    new Vector3(mirror * 0.02f, -0.58f, 0f), new Vector3(0.18f, 0.28f, 0.19f),
                    Quaternion.Euler(Jit(1.5f), 0f, mirror * 3f), granite * 0.95f, 0.45f, 0.22f);
                Make(PrimitiveType.Cube, "Greave_Band_" + side, pivot,
                    new Vector3(mirror * 0.02f, -0.685f, 0f), new Vector3(0.19f, 0.045f, 0.20f),
                    Quaternion.identity, ironDark, 0.85f, 0.40f);
                Make(PrimitiveType.Cube, "Sabaton_" + side, pivot,
                    new Vector3(mirror * 0.025f, -0.85f, 0.045f), new Vector3(0.21f, 0.10f, 0.30f),
                    Quaternion.Euler(0f, mirror * 4f, 0f), graniteDark, 0.45f, 0.20f);
                Make(PrimitiveType.Cube, "Sabaton_Toe_" + side, pivot,
                    new Vector3(mirror * 0.025f, -0.84f, 0.21f), new Vector3(0.18f, 0.075f, 0.11f),
                    Quaternion.Euler(-8f, mirror * 4f, 0f), graniteDark * 0.92f, 0.45f, 0.20f);
            }

            // Torso — slab cuirass over a heavy fauld ---------------------------
            Make(PrimitiveType.Cube, "Fauld", torso,
                new Vector3(0f, -0.05f, 0f), new Vector3(0.47f, 0.23f, 0.35f),
                Quaternion.identity, granite * 0.94f, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Tasset_L", torso,
                new Vector3(-0.185f, -0.22f, 0.01f), new Vector3(0.19f, 0.26f, 0.29f),
                Quaternion.Euler(0f, 0f, 4f), granite, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Tasset_R", torso,
                new Vector3(0.185f, -0.22f, 0.01f), new Vector3(0.19f, 0.26f, 0.29f),
                Quaternion.Euler(0f, 0f, -4f), granite, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Belt", torso,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.48f, 0.07f, 0.36f),
                Quaternion.identity, ironDark, 0.85f, 0.38f);
            Make(PrimitiveType.Cube, "Belt_Plate", torso,
                new Vector3(0f, 0.10f, 0.185f), new Vector3(0.11f, 0.09f, 0.025f),
                Quaternion.identity, iron, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Cuirass", torso,
                new Vector3(0f, 0.32f, 0f), new Vector3(0.50f, 0.44f, 0.35f),
                Quaternion.Euler(Jit(0.8f), 0f, 0f), granite, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Cuirass_Band", torso,
                new Vector3(0f, 0.21f, 0f), new Vector3(0.505f, 0.045f, 0.355f),
                Quaternion.identity, ironDark, 0.85f, 0.38f);
            Make(PrimitiveType.Cube, "Cuirass_Ridge", torso,
                new Vector3(0f, 0.34f, 0.18f), new Vector3(0.07f, 0.40f, 0.025f),
                Quaternion.identity, graniteLite, 0.45f, 0.25f);
            Make(PrimitiveType.Cube, "Back_Slab", torso,
                new Vector3(0f, 0.30f, -0.19f), new Vector3(0.44f, 0.50f, 0.05f),
                Quaternion.Euler(Jit(1f), 0f, 0f), granite * 0.9f, 0.45f, 0.20f);
            // Faction accent: the surcoat strip laid over the chest slab.
            Make(PrimitiveType.Cube, "Tunic_Trim", torso,
                new Vector3(0f, 0.28f, 0.183f), new Vector3(0.16f, 0.34f, 0.014f),
                Quaternion.Euler(-1.5f, 0f, Jit(1f)), clothLight, 0.0f, 0.12f);
            // Moss in the crevices — this armour has been standing a long time.
            Make(PrimitiveType.Sphere, "Moss_Patch_1", torso,
                new Vector3(-0.20f, 0.13f, 0.14f), new Vector3(0.11f, 0.05f, 0.09f),
                Quaternion.Euler(0f, Jit(8f), 12f), moss, 0.0f, 0.10f);
            Make(PrimitiveType.Sphere, "Moss_Patch_2", torso,
                new Vector3(0.16f, -0.06f, 0.16f), new Vector3(0.09f, 0.045f, 0.08f),
                Quaternion.Euler(0f, Jit(8f), -9f), moss * 0.9f, 0.0f, 0.10f);
            Make(PrimitiveType.Cylinder, "Gorget", torso,
                new Vector3(0f, 0.545f, 0f), new Vector3(0.245f, 0.06f, 0.245f),
                Quaternion.identity, ironDark, 0.85f, 0.38f);

            // Stone-slab pauldrons, two lames each -------------------------------
            Make(PrimitiveType.Cube, "Pauldron_Slab_L", torso,
                new Vector3(-0.30f, 0.535f, 0f), new Vector3(0.31f, 0.13f, 0.35f),
                Quaternion.Euler(0f, 0f, 7f + Jit(1.5f)), granite, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Pauldron_Slab_R", torso,
                new Vector3(0.30f, 0.535f, 0f), new Vector3(0.31f, 0.13f, 0.35f),
                Quaternion.Euler(0f, 0f, -7f + Jit(1.5f)), granite, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Pauldron_Lame_L", torso,
                new Vector3(-0.325f, 0.415f, 0f), new Vector3(0.27f, 0.11f, 0.31f),
                Quaternion.Euler(0f, 0f, 11f), granite * 0.92f, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Pauldron_Lame_R", torso,
                new Vector3(0.325f, 0.415f, 0f), new Vector3(0.27f, 0.11f, 0.31f),
                Quaternion.Euler(0f, 0f, -11f), granite * 0.92f, 0.45f, 0.22f);
            Make(PrimitiveType.Cube, "Pauldron_Rim_L", torso,
                new Vector3(-0.30f, 0.60f, 0f), new Vector3(0.315f, 0.03f, 0.355f),
                Quaternion.Euler(0f, 0f, 7f), ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Pauldron_Rim_R", torso,
                new Vector3(0.30f, 0.60f, 0f), new Vector3(0.315f, 0.03f, 0.355f),
                Quaternion.Euler(0f, 0f, -7f), ironDark, 0.85f, 0.40f);

            // Arms — short, blocky, empty-handed (this unit never attacks) -------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                Make(PrimitiveType.Cube, "UpperArm_" + side, pivot,
                    new Vector3(mirror * 0.025f, -0.15f, 0f), new Vector3(0.145f, 0.24f, 0.15f),
                    Quaternion.Euler(0f, 0f, mirror * 6f), granite * 0.96f, 0.45f, 0.22f);
                Make(PrimitiveType.Sphere, "Elbow_" + side, pivot,
                    new Vector3(mirror * 0.04f, -0.275f, 0.01f), new Vector3(0.135f, 0.115f, 0.135f),
                    Quaternion.identity, clothDark, 0.10f, 0.15f);
                Make(PrimitiveType.Cube, "Forearm_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.385f, 0.025f), new Vector3(0.135f, 0.22f, 0.14f),
                    Quaternion.Euler(-8f, 0f, mirror * 3f), granite, 0.45f, 0.22f);
                Make(PrimitiveType.Cube, "Vambrace_Band_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.465f, 0.03f), new Vector3(0.145f, 0.035f, 0.15f),
                    Quaternion.Euler(-8f, 0f, 0f), ironDark, 0.85f, 0.40f);
                Make(PrimitiveType.Sphere, "Fist_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.525f, 0.055f), new Vector3(0.115f, 0.11f, 0.115f),
                    Quaternion.identity, graniteDark, 0.45f, 0.25f);
            }

            // Tower shield on the left arm — the whole point of the unit --------
            // Spans y=0.09 to y=1.59 in root space: ankle to crown.
            var shield = new GameObject("Shield").transform;
            shield.SetParent(armL, false);
            shield.localPosition = new Vector3(-0.185f, -0.28f, 0.165f);
            shield.localRotation = Quaternion.Euler(0f, Jit(2.5f), Jit(1.5f)); // faces forward
            Make(PrimitiveType.Cube, "Shield_Back", shield,
                new Vector3(0f, -0.22f, -0.02f), new Vector3(0.72f, 1.50f, 0.07f),
                Quaternion.identity, granite * 0.9f, 0.45f, 0.20f);
            // Faction accent: the painted face of the wall.
            Make(PrimitiveType.Cube, "Shield_Face", shield,
                new Vector3(0f, -0.22f, 0.025f), new Vector3(0.63f, 1.39f, 0.022f),
                Quaternion.identity, clothLight, 0.05f, 0.28f);
            Make(PrimitiveType.Cube, "Shield_Spine", shield,
                new Vector3(0f, -0.22f, 0.04f), new Vector3(0.10f, 1.44f, 0.035f),
                Quaternion.identity, iron, 0.85f, 0.42f);
            Make(PrimitiveType.Cube, "Shield_Rim_Edge_L", shield,
                new Vector3(-0.365f, -0.22f, -0.005f), new Vector3(0.055f, 1.54f, 0.11f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Shield_Rim_Edge_R", shield,
                new Vector3(0.365f, -0.22f, -0.005f), new Vector3(0.055f, 1.54f, 0.11f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Shield_Rim_Top", shield,
                new Vector3(0f, 0.51f, -0.005f), new Vector3(0.75f, 0.06f, 0.115f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Shield_Rim_Bottom", shield,
                new Vector3(0f, -0.955f, -0.005f), new Vector3(0.75f, 0.06f, 0.115f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Sphere, "Shield_Boss", shield,
                new Vector3(0f, -0.20f, 0.075f), new Vector3(0.24f, 0.24f, 0.12f),
                Quaternion.identity, iron, 0.85f, 0.50f);
            Make(PrimitiveType.Sphere, "Shield_Rivet_1", shield,
                new Vector3(-0.27f, 0.42f, 0.045f), new Vector3(0.05f, 0.05f, 0.035f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Shield_Rivet_2", shield,
                new Vector3(0.27f, 0.42f, 0.045f), new Vector3(0.05f, 0.05f, 0.035f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Shield_Rivet_3", shield,
                new Vector3(-0.27f, -0.86f, 0.045f), new Vector3(0.05f, 0.05f, 0.035f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Shield_Rivet_4", shield,
                new Vector3(0.27f, -0.86f, 0.045f), new Vector3(0.05f, 0.05f, 0.035f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            // Ground spike at the foot of the shield: it is meant to be planted.
            Make(PrimitiveType.Cube, "Shield_Foot_Spike", shield,
                new Vector3(0f, -1.03f, -0.005f), new Vector3(0.14f, 0.10f, 0.09f),
                Quaternion.Euler(0f, 45f, 0f), graniteDark, 0.55f, 0.30f);
            Make(PrimitiveType.Cube, "Shield_Strap", shield,
                new Vector3(0.12f, -0.22f, -0.075f), new Vector3(0.30f, 0.07f, 0.05f),
                Quaternion.Euler(0f, 0f, Jit(2f)), leather, 0.08f, 0.22f);
            // Faction accent: pennon hung from the shield's top rim.
            Make(PrimitiveType.Cube, "Pennon", shield,
                new Vector3(0f, 0.375f, 0.055f), new Vector3(0.42f, 0.20f, 0.014f),
                Quaternion.Euler(0f, 0f, Jit(1.5f)), clothLight, 0.0f, 0.12f);

            // Head — a fully closed helm with no visor slit at all ---------------
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0f, 0.57f, 0f);
            head.localRotation = Quaternion.Euler(Jit(1f), Jit(2f), 0f);
            Make(PrimitiveType.Sphere, "Helm_Shell", head,
                new Vector3(0f, 0.12f, -0.01f), new Vector3(0.235f, 0.245f, 0.235f),
                Quaternion.Euler(Jit(1.5f), 0f, Jit(1f)), granite, 0.45f, 0.25f);
            // Blank face plate: no eye slot, no breath holes, nothing.
            Make(PrimitiveType.Cube, "Helm_Face_Plate", head,
                new Vector3(0f, 0.10f, 0.095f), new Vector3(0.205f, 0.23f, 0.055f),
                Quaternion.Euler(-4f, 0f, 0f), graniteLite, 0.45f, 0.28f);
            Make(PrimitiveType.Cube, "Helm_Brow", head,
                new Vector3(0f, 0.195f, 0.095f), new Vector3(0.225f, 0.05f, 0.075f),
                Quaternion.Euler(-4f, 0f, 0f), ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Helm_Crown_Ridge", head,
                new Vector3(0f, 0.245f, 0f), new Vector3(0.055f, 0.075f, 0.245f),
                Quaternion.Euler(Jit(1.5f), 0f, 0f), ironDark, 0.85f, 0.42f);
            Make(PrimitiveType.Cube, "Helm_Cheek_L", head,
                new Vector3(-0.115f, 0.09f, 0.045f), new Vector3(0.035f, 0.20f, 0.14f),
                Quaternion.Euler(0f, 0f, 5f), granite * 0.94f, 0.45f, 0.25f);
            Make(PrimitiveType.Cube, "Helm_Cheek_R", head,
                new Vector3(0.115f, 0.09f, 0.045f), new Vector3(0.035f, 0.20f, 0.14f),
                Quaternion.Euler(0f, 0f, -5f), granite * 0.94f, 0.45f, 0.25f);
            Make(PrimitiveType.Cylinder, "Helm_Collar", head,
                new Vector3(0f, -0.01f, 0f), new Vector3(0.25f, 0.045f, 0.25f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Sphere, "Helm_Rivet_L", head,
                new Vector3(-0.10f, 0.185f, 0.075f), new Vector3(0.034f, 0.034f, 0.034f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Helm_Rivet_R", head,
                new Vector3(0.10f, 0.185f, 0.075f), new Vector3(0.034f, 0.034f, 0.034f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);

            root.AddComponent<StoneWardenAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Stone Warden: a very slow, long
    /// stride with a shallow leg swing (a wall does not jog), a shield arm
    /// pinned nearly rigid so the tower shield never scythes around, a
    /// heavy side-to-side weight transfer while walking, an idle "plant the
    /// shield" beat where the shield drops and the whole figure settles, and
    /// faction-color tint of the accent parts (Shield_Face, Tunic_Trim,
    /// Pennon) once EntityReference is wired by the orchestrator.
    /// </summary>
    public class StoneWardenAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 17f;

        [Tooltip("Free (right) arm swing amplitude in degrees at full stride.")]
        public float ArmSwing = 9f;

        [Tooltip("Shield arm swing in degrees — kept near zero on purpose.")]
        public float ShieldArmSwing = 2.5f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 1.30f;

        [Tooltip("Walking side-to-side weight transfer in degrees.")]
        public float WeightRoll = 3.4f;

        [Tooltip("Seconds between idle shield plants.")]
        public float PlantInterval = 5.2f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head, _shield;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest;
        private Vector3 _shieldRestPos;
        private Material _shieldFaceMat, _trimMat, _pennonMat;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _phase;      // walk cycle phase, radians
        private float _gait;       // 0 = idle, 1 = walking (smoothed)
        private float _plantClock; // idle shield-plant timer

        void Start()
        {
            _legL   = FindDeep(transform, "LegPivot_L");
            _legR   = FindDeep(transform, "LegPivot_R");
            _armL   = FindDeep(transform, "ArmPivot_L");
            _armR   = FindDeep(transform, "ArmPivot_R");
            _torso  = FindDeep(transform, "TorsoPivot");
            _head   = FindDeep(transform, "HeadPivot");
            _shield = FindDeep(transform, "Shield");
            if (_armL != null)   _armLRest  = _armL.localRotation;
            if (_armR != null)   _armRRest  = _armR.localRotation;
            if (_torso != null)  _torsoRest = _torso.localRotation;
            if (_head != null)   _headRest  = _head.localRotation;
            if (_shield != null) _shieldRestPos = _shield.localPosition;

            _shieldFaceMat = MatOf("Shield_Face");
            _trimMat       = MatOf("Tunic_Trim");
            _pennonMat     = MatOf("Pennon");

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
            _gait = Mathf.MoveTowards(_gait, moving ? 1f : 0f, dt * 4f);

            // Phase advances with distance so stride matches ground speed.
            _phase += (dist / Mathf.Max(StrideLength, 0.01f)) * 2f * Mathf.PI;

            float t = Time.time;
            float swing = Mathf.Sin(_phase) * _gait;

            if (_legL != null) _legL.localRotation = Quaternion.Euler( swing * LegSwing, 0f, 0f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-swing * LegSwing, 0f, 0f);
            // The shield arm barely moves: a 1.5 m slab swinging 15 degrees
            // would read as a windmill, so it is pinned to a token amount.
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(-swing * ShieldArmSwing, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing, 0f, 0f);

            // Idle: a slow shield plant — the shield drops a few centimeters
            // and stays down, then is hauled back up to carry height.
            float idleAmt = 1f - _gait;
            _plantClock += dt * idleAmt;
            if (_plantClock > PlantInterval) _plantClock -= PlantInterval;
            float plantT = _plantClock / Mathf.Max(PlantInterval, 0.01f);
            // Raised-cosine pulse over the first 40% of the interval: slow.
            float plant = plantT < 0.40f
                ? Mathf.Sin(plantT / 0.40f * Mathf.PI)
                : 0f;
            if (_shield != null)
                _shield.localPosition = _shieldRestPos + new Vector3(0f, -0.05f * plant * idleAmt, 0f);

            if (_torso != null)
            {
                // Walking: heavy roll onto whichever foot is planted.
                float roll = Mathf.Sin(_phase) * WeightRoll * _gait;
                float settle = 1.6f * plant * idleAmt;
                float breathe = Mathf.Sin(t * 0.55f) * 0.9f * idleAmt;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(2.5f * _gait + settle, 0f, roll + breathe);
            }
            if (_head != null)
            {
                // The blank helm turns slowly, like a turret sweeping.
                float yaw = Mathf.Sin(t * 0.22f) * 7f * idleAmt;
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
            Tint(_shieldFaceMat, fc, 0.10f, true); // soft emissive so it reads at distance
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
