// File: Assets/GameData/TechTree/Units/Sects/Lorekeeper/LorekeeperVisual.cs
// Procedural visual for the Sect of Antiquity Lore Keeper: a NON-COMBAT
// scholar — long floor-length robe over a slim frame, deep hood with a
// trimmed cowl, brass-cornered ledger chained at the hip, capped scroll
// case slung diagonally across the back, belt inkpot and a quill held in
// the right hand, spectacle lenses under the hood. Carries NO weapon at
// all; the silhouette is meant to read "clerk", not "soldier", at RTS
// camera distance. Built entirely from primitives (Smelter idiom —
// per-part URP/Lit material, metallic/smoothness contrast, small
// deterministic tilts, colliders destroyed). Player-color accents
// (Tunic_Trim, Hood_Trim, Pennon) are tinted at runtime by
// LorekeeperAnimator via EntityReference (LedgerVisual.TryTint pattern) —
// the orchestrator adds EntityReference after Build returns, so the
// animator guards for it being absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class LorekeeperVisual
    {
        /// <summary>
        /// Builds the full Lore Keeper rig and returns the root. The root
        /// sits at ground level (feet at y=0); figure height ~1.85 m with
        /// the hood peak, robe hem ~0.11 m off the ground. Deterministic:
        /// all jitter flows through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("LorekeeperVisual");

            // Palette ---------------------------------------------------------
            var robe       = new Color(0.29f, 0.27f, 0.33f); // scholar's wool
            var robeDark   = new Color(0.20f, 0.19f, 0.24f); // hood, mantle, legs
            var robeLight  = new Color(0.38f, 0.36f, 0.43f); // sleeves, skirt panel
            var brass      = new Color(0.72f, 0.58f, 0.28f); // clasps, cuffs, caps
            var brassDark  = new Color(0.45f, 0.35f, 0.16f); // chain, hem band
            var parchment  = new Color(0.86f, 0.82f, 0.68f); // pages, scroll
            var clothLight = new Color(0.87f, 0.85f, 0.79f); // accent base (tinted)
            var leather    = new Color(0.43f, 0.27f, 0.16f); // straps, book covers
            var leatherDrk = new Color(0.30f, 0.19f, 0.12f); // shoes, case body
            var rope       = new Color(0.62f, 0.55f, 0.38f); // cord belt
            var glass      = new Color(0.70f, 0.79f, 0.84f); // spectacle lenses
            var hair       = new Color(0.72f, 0.70f, 0.66f); // grey beard
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
            // A lifetime bent over a desk: a touch more forward stoop than a soldier.
            torso.localRotation = Quaternion.Euler(3f + Jit(1.5f), 0f, Jit(1f));

            Transform LegPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(pelvis, false);
                t.localPosition = new Vector3(x, 0f, 0f); // hip height, swings around X
                return t;
            }
            var legL = LegPivot("LegPivot_L", -0.095f);
            var legR = LegPivot("LegPivot_R",  0.095f);

            Transform ArmPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(torso, false);
                t.localPosition = new Vector3(x, 0.44f, 0f); // shoulder height
                return t;
            }
            var armL = ArmPivot("ArmPivot_L", -0.245f);
            var armR = ArmPivot("ArmPivot_R",  0.245f);

            // Legs — slim, dark, mostly hidden under the robe; only the soft
            // shoes clear the hem, which is exactly the read we want.
            foreach (var (side, pivot, mirror) in new[] { ("L", legL, -1f), ("R", legR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "Thigh_" + side, pivot,
                    new Vector3(0f, -0.21f, 0f), new Vector3(0.115f, 0.15f, 0.115f),
                    Quaternion.Euler(0f, 0f, mirror * 2f), robeDark, 0.05f, 0.10f);
                Make(PrimitiveType.Sphere, "Knee_" + side, pivot,
                    new Vector3(0f, -0.40f, 0.01f), new Vector3(0.10f, 0.09f, 0.10f),
                    Quaternion.identity, robeDark * 0.92f, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Shin_" + side, pivot,
                    new Vector3(0f, -0.61f, 0f), new Vector3(0.095f, 0.155f, 0.095f),
                    Quaternion.Euler(Jit(1.5f), 0f, 0f), robeDark, 0.05f, 0.10f);
                Make(PrimitiveType.Cube, "Shoe_" + side, pivot,
                    new Vector3(0f, -0.885f, 0.045f), new Vector3(0.115f, 0.09f, 0.235f),
                    Quaternion.Euler(0f, mirror * 3f, 0f), leatherDrk, 0.08f, 0.20f);
                Make(PrimitiveType.Cube, "Shoe_Strap_" + side, pivot,
                    new Vector3(0f, -0.855f, 0.01f), new Vector3(0.12f, 0.022f, 0.10f),
                    Quaternion.identity, leather, 0.08f, 0.22f);
            }

            // Robe — floor-length, two stacked blocks so the skirt flares ------
            Make(PrimitiveType.Cube, "Robe_Skirt_Lower", torso,
                new Vector3(0f, -0.52f, 0f), new Vector3(0.42f, 0.62f, 0.34f),
                Quaternion.Euler(Jit(1f), 0f, Jit(1f)), robe * 0.94f, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Robe_Skirt_Upper", torso,
                new Vector3(0f, -0.11f, 0f), new Vector3(0.36f, 0.32f, 0.28f),
                Quaternion.identity, robe, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Robe_Hem_Band", torso,
                new Vector3(0f, -0.80f, 0f), new Vector3(0.435f, 0.06f, 0.355f),
                Quaternion.identity, brassDark, 0.55f, 0.30f);
            Make(PrimitiveType.Cube, "Robe_Panel", torso,
                new Vector3(0f, -0.46f, 0.174f), new Vector3(0.16f, 0.70f, 0.012f),
                Quaternion.Euler(0f, 0f, Jit(1f)), robeLight, 0.05f, 0.12f);
            Make(PrimitiveType.Cube, "Robe_Body", torso,
                new Vector3(0f, 0.28f, 0f), new Vector3(0.37f, 0.42f, 0.26f),
                Quaternion.Euler(Jit(1f), 0f, 0f), robe, 0.05f, 0.10f);

            // Cord belt with a knot and two hanging tassels.
            Make(PrimitiveType.Cylinder, "Cord_Belt", torso,
                new Vector3(0f, 0.06f, 0f), new Vector3(0.30f, 0.03f, 0.245f),
                Quaternion.identity, rope, 0.05f, 0.18f);
            Make(PrimitiveType.Sphere, "Cord_Knot", torso,
                new Vector3(-0.02f, 0.05f, 0.125f), new Vector3(0.06f, 0.055f, 0.05f),
                Quaternion.identity, rope * 0.9f, 0.05f, 0.18f);
            Make(PrimitiveType.Cylinder, "Cord_Tassel_L", torso,
                new Vector3(-0.05f, -0.06f, 0.135f), new Vector3(0.018f, 0.10f, 0.018f),
                Quaternion.Euler(Jit(3f), 0f, 4f), rope * 0.85f, 0.05f, 0.18f);
            Make(PrimitiveType.Cylinder, "Cord_Tassel_R", torso,
                new Vector3(0.01f, -0.08f, 0.135f), new Vector3(0.018f, 0.12f, 0.018f),
                Quaternion.Euler(Jit(3f), 0f, -3f), rope * 0.85f, 0.05f, 0.18f);

            // Faction accent: the scholar's stole down the robe front.
            Make(PrimitiveType.Cube, "Tunic_Trim", torso,
                new Vector3(0f, 0.24f, 0.136f), new Vector3(0.10f, 0.52f, 0.012f),
                Quaternion.Euler(-1.5f, 0f, Jit(1f)), clothLight, 0.0f, 0.12f);

            Make(PrimitiveType.Cylinder, "Collar", torso,
                new Vector3(0f, 0.50f, 0f), new Vector3(0.17f, 0.04f, 0.17f),
                Quaternion.identity, robe * 0.88f, 0.05f, 0.10f);
            Make(PrimitiveType.Sphere, "Mantle", torso,
                new Vector3(0f, 0.44f, -0.01f), new Vector3(0.47f, 0.22f, 0.35f),
                Quaternion.Euler(2f, Jit(2f), 0f), robeDark, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Mantle_Back", torso,
                new Vector3(0f, 0.28f, -0.145f), new Vector3(0.40f, 0.38f, 0.035f),
                Quaternion.Euler(Jit(1.5f), 0f, 0f), robeDark, 0.05f, 0.10f);

            // Scroll case slung across the back ---------------------------------
            var caseRot = Quaternion.Euler(8f, 0f, -32f + Jit(2f));
            Make(PrimitiveType.Cylinder, "Scroll_Case", torso,
                new Vector3(-0.05f, 0.26f, -0.20f), new Vector3(0.075f, 0.26f, 0.075f),
                caseRot, leatherDrk, 0.08f, 0.22f);
            Make(PrimitiveType.Cylinder, "Scroll_Case_Cap_Top", torso,
                new Vector3(0.09f, 0.48f, -0.19f), new Vector3(0.085f, 0.035f, 0.085f),
                caseRot, brass, 0.80f, 0.45f);
            Make(PrimitiveType.Cylinder, "Scroll_Case_Cap_Bottom", torso,
                new Vector3(-0.19f, 0.04f, -0.21f), new Vector3(0.085f, 0.035f, 0.085f),
                caseRot, brass, 0.80f, 0.45f);
            Make(PrimitiveType.Cylinder, "Scroll_Roll", torso,
                new Vector3(0.12f, 0.54f, -0.19f), new Vector3(0.055f, 0.055f, 0.055f),
                caseRot, parchment, 0.0f, 0.12f);
            Make(PrimitiveType.Cube, "Scroll_Case_Strap", torso,
                new Vector3(0f, 0.28f, 0f), new Vector3(0.05f, 0.58f, 0.30f),
                Quaternion.Euler(0f, 0f, 30f), leather, 0.08f, 0.22f);

            // The ledger, chained to the belt at the right hip ------------------
            var bookRot = Quaternion.Euler(0f, 6f, 9f + Jit(2f));
            Make(PrimitiveType.Cube, "Book_Cover_Back", torso,
                new Vector3(0.235f, -0.08f, 0.03f), new Vector3(0.035f, 0.26f, 0.19f),
                bookRot, leather, 0.08f, 0.22f);
            Make(PrimitiveType.Cube, "Book_Pages", torso,
                new Vector3(0.212f, -0.08f, 0.03f), new Vector3(0.022f, 0.24f, 0.175f),
                bookRot, parchment, 0.0f, 0.12f);
            Make(PrimitiveType.Cube, "Book_Cover_Front", torso,
                new Vector3(0.192f, -0.08f, 0.03f), new Vector3(0.028f, 0.26f, 0.19f),
                bookRot, leather * 0.85f, 0.08f, 0.22f);
            Make(PrimitiveType.Cube, "Book_Corner_Brass", torso,
                new Vector3(0.19f, -0.18f, 0.10f), new Vector3(0.032f, 0.05f, 0.05f),
                bookRot, brass, 0.80f, 0.45f);
            Make(PrimitiveType.Cube, "Book_Clasp", torso,
                new Vector3(0.185f, -0.07f, 0.115f), new Vector3(0.04f, 0.05f, 0.02f),
                bookRot, brass, 0.80f, 0.45f);
            Make(PrimitiveType.Sphere, "Book_Chain_1", torso,
                new Vector3(0.185f, 0.045f, 0.055f), new Vector3(0.028f, 0.028f, 0.028f),
                Quaternion.identity, brassDark, 0.80f, 0.40f);
            Make(PrimitiveType.Sphere, "Book_Chain_2", torso,
                new Vector3(0.205f, 0.005f, 0.045f), new Vector3(0.026f, 0.026f, 0.026f),
                Quaternion.identity, brassDark, 0.80f, 0.40f);
            Make(PrimitiveType.Sphere, "Book_Chain_3", torso,
                new Vector3(0.222f, -0.03f, 0.038f), new Vector3(0.024f, 0.024f, 0.024f),
                Quaternion.identity, brassDark, 0.80f, 0.40f);
            // Faction accent: the ledger's page ribbon hanging below the book.
            Make(PrimitiveType.Cube, "Pennon", torso,
                new Vector3(0.212f, -0.28f, 0.03f), new Vector3(0.014f, 0.17f, 0.055f),
                Quaternion.Euler(Jit(4f), 0f, 9f), clothLight, 0.0f, 0.12f);

            // Inkpot on the left of the belt -------------------------------------
            Make(PrimitiveType.Cylinder, "Ink_Pot", torso,
                new Vector3(-0.185f, 0.005f, 0.075f), new Vector3(0.06f, 0.045f, 0.06f),
                Quaternion.Euler(0f, 0f, -6f), brassDark, 0.80f, 0.40f);
            Make(PrimitiveType.Sphere, "Ink_Pot_Lid", torso,
                new Vector3(-0.19f, 0.055f, 0.075f), new Vector3(0.062f, 0.03f, 0.062f),
                Quaternion.identity, brass, 0.80f, 0.45f);

            // Sleeved arms (children of the shoulder pivots) ---------------------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "Sleeve_Upper_" + side, pivot,
                    new Vector3(mirror * 0.03f, -0.13f, 0f), new Vector3(0.10f, 0.115f, 0.10f),
                    Quaternion.Euler(0f, 0f, mirror * 6f), robe, 0.05f, 0.10f);
                Make(PrimitiveType.Sphere, "Elbow_" + side, pivot,
                    new Vector3(mirror * 0.045f, -0.26f, 0.01f), new Vector3(0.10f, 0.085f, 0.10f),
                    Quaternion.identity, robe * 0.9f, 0.05f, 0.10f);
                // Wide scholar sleeve: the forearm tube is fatter than the upper.
                Make(PrimitiveType.Cylinder, "Sleeve_Fore_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.375f, 0.025f), new Vector3(0.105f, 0.105f, 0.105f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), robeLight, 0.05f, 0.10f);
                Make(PrimitiveType.Cylinder, "Sleeve_Cuff_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.465f, 0.04f), new Vector3(0.108f, 0.028f, 0.108f),
                    Quaternion.Euler(-10f, 0f, 0f), brassDark, 0.70f, 0.35f);
                Make(PrimitiveType.Sphere, "Hand_" + side, pivot,
                    new Vector3(mirror * 0.06f, -0.51f, 0.06f), new Vector3(0.075f, 0.075f, 0.075f),
                    Quaternion.identity, skin, 0.0f, 0.25f);
            }

            // Quill in the right hand (the Lore Keeper's only "weapon") ----------
            var quill = new GameObject("Quill").transform;
            quill.SetParent(armR, false);
            quill.localPosition = new Vector3(0.06f, -0.51f, 0.06f); // at the hand
            quill.localRotation = Quaternion.Euler(-24f + Jit(3f), 0f, 14f + Jit(3f));
            Make(PrimitiveType.Cylinder, "Quill_Shaft", quill,
                new Vector3(0f, 0.07f, 0f), new Vector3(0.012f, 0.075f, 0.012f),
                Quaternion.identity, parchment * 0.92f, 0.05f, 0.30f);
            Make(PrimitiveType.Cube, "Quill_Feather", quill,
                new Vector3(0f, 0.175f, 0f), new Vector3(0.008f, 0.10f, 0.045f),
                Quaternion.Euler(0f, Jit(6f), 6f), clothLight * 0.96f, 0.0f, 0.12f);
            Make(PrimitiveType.Cylinder, "Quill_Nib", quill,
                new Vector3(0f, -0.02f, 0f), new Vector3(0.009f, 0.02f, 0.009f),
                Quaternion.identity, brassDark, 0.80f, 0.45f);

            // Head, hood and spectacles ------------------------------------------
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0f, 0.57f, 0f);
            head.localRotation = Quaternion.Euler(Jit(1.5f), Jit(3f), 0f);
            Make(PrimitiveType.Sphere, "Head", head,
                new Vector3(0f, 0.07f, 0f), new Vector3(0.175f, 0.19f, 0.175f),
                Quaternion.identity, skin, 0.0f, 0.30f);
            Make(PrimitiveType.Sphere, "Beard", head,
                new Vector3(0f, 0.005f, 0.045f), new Vector3(0.135f, 0.115f, 0.12f),
                Quaternion.Euler(Jit(2f), 0f, 0f), hair, 0.0f, 0.15f);
            // The cowl sits back off the brow so the face and lenses read.
            Make(PrimitiveType.Sphere, "Hood_Cowl", head,
                new Vector3(0f, 0.105f, -0.035f), new Vector3(0.245f, 0.225f, 0.245f),
                Quaternion.Euler(Jit(2f), 0f, Jit(1.5f)), robeDark, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Hood_Peak", head,
                new Vector3(0f, 0.20f, -0.075f), new Vector3(0.17f, 0.15f, 0.14f),
                Quaternion.Euler(26f + Jit(3f), 0f, 0f), robeDark * 0.94f, 0.05f, 0.10f);
            Make(PrimitiveType.Cube, "Hood_Drape_Back", head,
                new Vector3(0f, -0.01f, -0.175f), new Vector3(0.27f, 0.30f, 0.055f),
                Quaternion.Euler(-4f + Jit(2f), 0f, 0f), robeDark, 0.05f, 0.10f);
            // Faction accent: the trim ring around the cowl opening.
            Make(PrimitiveType.Cylinder, "Hood_Trim", head,
                new Vector3(0f, 0.06f, 0.015f), new Vector3(0.265f, 0.016f, 0.265f),
                Quaternion.Euler(14f + Jit(2f), 0f, Jit(1f)), clothLight, 0.0f, 0.12f);
            Make(PrimitiveType.Cylinder, "Spectacle_Rim_L", head,
                new Vector3(-0.052f, 0.095f, 0.083f), new Vector3(0.062f, 0.008f, 0.062f),
                Quaternion.Euler(90f, 0f, 0f), brass, 0.80f, 0.45f);
            Make(PrimitiveType.Cylinder, "Spectacle_Rim_R", head,
                new Vector3(0.052f, 0.095f, 0.083f), new Vector3(0.062f, 0.008f, 0.062f),
                Quaternion.Euler(90f, 0f, 0f), brass, 0.80f, 0.45f);
            Make(PrimitiveType.Cylinder, "Spectacle_Lens_L", head,
                new Vector3(-0.052f, 0.095f, 0.09f), new Vector3(0.05f, 0.006f, 0.05f),
                Quaternion.Euler(90f, 0f, 0f), glass, 0.35f, 0.92f);
            Make(PrimitiveType.Cylinder, "Spectacle_Lens_R", head,
                new Vector3(0.052f, 0.095f, 0.09f), new Vector3(0.05f, 0.006f, 0.05f),
                Quaternion.Euler(90f, 0f, 0f), glass, 0.35f, 0.92f);
            Make(PrimitiveType.Cube, "Spectacle_Bridge", head,
                new Vector3(0f, 0.095f, 0.085f), new Vector3(0.045f, 0.008f, 0.008f),
                Quaternion.identity, brass, 0.80f, 0.45f);

            root.AddComponent<LorekeeperAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Lore Keeper: a short, unhurried
    /// robed walk (small leg swing — the hem hides the stride anyway; the
    /// quill arm barely moves so the writing hand stays steady), an idle
    /// "taking notes" beat where the quill dips and the head nods down at
    /// the page, and faction-color tint of the accent parts (Tunic_Trim,
    /// Hood_Trim, Pennon) once EntityReference is wired by the orchestrator.
    /// </summary>
    public class LorekeeperAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 20f;

        [Tooltip("Arm swing amplitude in degrees at full stride (free arm).")]
        public float ArmSwing = 11f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 0.92f;

        [Tooltip("Idle torso weight-shift sway in degrees.")]
        public float IdleSway = 1.4f;

        [Tooltip("Seconds between idle note-taking beats.")]
        public float NoteInterval = 4.2f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head, _quill;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest, _quillRest;
        private Material _trimMat, _hoodTrimMat, _pennonMat;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _phase;     // walk cycle phase, radians
        private float _gait;      // 0 = idle, 1 = walking (smoothed)
        private float _noteClock; // idle note-taking timer

        void Start()
        {
            _legL  = FindDeep(transform, "LegPivot_L");
            _legR  = FindDeep(transform, "LegPivot_R");
            _armL  = FindDeep(transform, "ArmPivot_L");
            _armR  = FindDeep(transform, "ArmPivot_R");
            _torso = FindDeep(transform, "TorsoPivot");
            _head  = FindDeep(transform, "HeadPivot");
            _quill = FindDeep(transform, "Quill");
            if (_armL != null)  _armLRest  = _armL.localRotation;
            if (_armR != null)  _armRRest  = _armR.localRotation;
            if (_torso != null) _torsoRest = _torso.localRotation;
            if (_head != null)  _headRest  = _head.localRotation;
            if (_quill != null) _quillRest = _quill.localRotation;

            _trimMat     = MatOf("Tunic_Trim");
            _hoodTrimMat = MatOf("Hood_Trim");
            _pennonMat   = MatOf("Pennon");

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
            // The free (left) arm counter-swings the legs; the quill arm is
            // held almost still so the writing hand never scythes around.
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(-swing * ArmSwing, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing * 0.25f, 0f, 0f);

            // Idle: a slow note-taking beat — the quill dips toward the ledger
            // and the head bows to the page, then both settle back.
            float idleAmt = 1f - _gait;
            _noteClock += dt * idleAmt;
            if (_noteClock > NoteInterval) _noteClock -= NoteInterval;
            float noteT = _noteClock / Mathf.Max(NoteInterval, 0.01f);
            // Raised-cosine pulse over the first 30% of the interval.
            float note = noteT < 0.30f
                ? Mathf.Sin(noteT / 0.30f * Mathf.PI)
                : 0f;
            if (_quill != null)
                _quill.localRotation = _quillRest * Quaternion.Euler(28f * note * idleAmt, 0f, 0f);

            if (_torso != null)
            {
                float idleZ = Mathf.Sin(t * 0.7f) * IdleSway * idleAmt;
                float walkLean = 2f * _gait;
                float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.4f * _gait;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(walkLean + bob, 0f, idleZ);
            }
            if (_head != null)
            {
                float yaw = Mathf.Sin(t * 0.35f) * 4f * idleAmt;
                float bow = 14f * note * idleAmt;
                _head.localRotation = _headRest * Quaternion.Euler(bow, yaw, 0f);
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
            Tint(_trimMat, fc, 0.15f, false);
            Tint(_hoodTrimMat, fc, 0.10f, true); // soft emissive so it reads at distance
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
