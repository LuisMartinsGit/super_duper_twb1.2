// File: Assets/GameData/TechTree/Units/Sects/GolemAutark/GolemAutarkVisual.cs
// Procedural visual for the Sect of Reclamation Golem Autark: a curse-immune
// CONSTRUCT, not a person — blocky stone limbs held apart by glowing violet
// joint rods so the gaps at every joint stay visible, a violet crystal core
// sunk into the chest cavity behind an iron ring, veilstone shards growing
// out of the shoulders and back, no face at all (a blank carved mask with
// two recessed glyph slits), and heavy stone fists instead of a weapon —
// this thing fights and harvests with its hands. Purple is the curse colour
// in this game, so the core, the joint rods, the shards and the mask glyphs
// are all EMISSIVE violet. It keeps the same pivot names as the human rigs
// (Pelvis / TorsoPivot / LegPivot_* / ArmPivot_* / HeadPivot) so the shared
// walk-cycle animation drives it unchanged. Built entirely from primitives
// (Smelter idiom — per-part URP/Lit material, metallic/smoothness contrast,
// small deterministic tilts, colliders destroyed). Player-color accents
// (Tunic_Trim, Sigil_Plate, Pennon) are tinted at runtime by
// GolemAutarkAnimator via EntityReference (LedgerVisual.TryTint pattern) —
// the orchestrator adds EntityReference after Build returns, so the animator
// guards for it being absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Presentation
{
    public static class GolemAutarkVisual
    {
        /// <summary>
        /// Builds the full Golem Autark rig and returns the root. The root
        /// sits at ground level (feet at y=0); the construct stands ~2.05 m
        /// to the crown shard, taller and heavier than any human unit.
        /// Deterministic: all jitter flows through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("GolemAutarkVisual");

            // Palette ---------------------------------------------------------
            var stone      = new Color(0.44f, 0.42f, 0.40f); // limb and torso blocks
            var stoneDark  = new Color(0.27f, 0.26f, 0.25f); // feet, cavity, back slab
            var stoneLight = new Color(0.57f, 0.55f, 0.52f); // mask, bevels
            var iron       = new Color(0.42f, 0.42f, 0.46f); // core ring, bands
            var ironDark   = new Color(0.25f, 0.25f, 0.28f); // bolts, staff
            var veil       = new Color(0.56f, 0.28f, 0.86f); // veilstone shards
            var veilDeep   = new Color(0.34f, 0.13f, 0.58f); // joint rods
            var veilBright = new Color(0.74f, 0.47f, 1.00f); // the core itself
            var crust      = new Color(0.23f, 0.17f, 0.29f); // curse residue
            var clothLight = new Color(0.87f, 0.85f, 0.79f); // accent base (tinted)

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

            // Crystal parts are ordinary Make parts with the emission switched
            // on — the curse glow is what separates the construct from rubble.
            System.Func<PrimitiveType, string, Transform, Vector3, Vector3, Quaternion, Color, float, GameObject>
            MakeGlow = (type, name, parent, lp, ls, lr, color, glow) =>
            {
                var go = Make(type, name, parent, lp, ls, lr, color, 0.25f, 0.88f);
                var r = go.GetComponent<Renderer>();
                if (r != null && r.material.HasProperty("_EmissionColor"))
                {
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", color * glow);
                }
                return go;
            };

            // Small deterministic hand-built lean (whole figure).
            float Jit(float range) => (float)(rng.NextDouble() * 2.0 - 1.0) * range;
            root.transform.localRotation = Quaternion.Euler(0f, Jit(2f), 0f);

            // Pivot skeleton (empties the animator drives by name) -------------
            // Same names as the human rigs; only the proportions change.
            var pelvis = new GameObject("Pelvis").transform;
            pelvis.SetParent(root.transform, false);
            pelvis.localPosition = new Vector3(0f, 1.00f, 0f);

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
            var legL = LegPivot("LegPivot_L", -0.155f);
            var legR = LegPivot("LegPivot_R",  0.155f);

            Transform ArmPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(torso, false);
                t.localPosition = new Vector3(x, 0.46f, 0f); // shoulder height
                return t;
            }
            var armL = ArmPivot("ArmPivot_L", -0.34f);
            var armR = ArmPivot("ArmPivot_R",  0.34f);

            // Legs — stacked stone blocks with a lit rod bridging every gap ----
            foreach (var (side, pivot, mirror) in new[] { ("L", legL, -1f), ("R", legR, 1f) })
            {
                Make(PrimitiveType.Cube, "Thigh_Block_" + side, pivot,
                    new Vector3(0f, -0.22f, 0f), new Vector3(0.21f, 0.30f, 0.22f),
                    Quaternion.Euler(Jit(1.5f), 0f, mirror * 3f), stone, 0.30f, 0.20f);
                MakeGlow(PrimitiveType.Cylinder, "Knee_Rod_" + side, pivot,
                    new Vector3(0f, -0.43f, 0f), new Vector3(0.07f, 0.05f, 0.07f),
                    Quaternion.identity, veilDeep, 1.30f);
                Make(PrimitiveType.Cube, "Shin_Block_" + side, pivot,
                    new Vector3(0f, -0.67f, 0f), new Vector3(0.19f, 0.34f, 0.20f),
                    Quaternion.Euler(Jit(1.5f), 0f, mirror * 2f), stone * 0.95f, 0.30f, 0.20f);
                MakeGlow(PrimitiveType.Cylinder, "Ankle_Rod_" + side, pivot,
                    new Vector3(0f, -0.885f, 0f), new Vector3(0.06f, 0.045f, 0.06f),
                    Quaternion.identity, veilDeep, 1.30f);
                Make(PrimitiveType.Cube, "Foot_Block_" + side, pivot,
                    new Vector3(0f, -0.945f, 0.04f), new Vector3(0.23f, 0.11f, 0.32f),
                    Quaternion.Euler(0f, mirror * 4f, 0f), stoneDark, 0.30f, 0.18f);
                Make(PrimitiveType.Cube, "Foot_Claw_" + side, pivot,
                    new Vector3(0f, -0.94f, 0.235f), new Vector3(0.20f, 0.08f, 0.13f),
                    Quaternion.Euler(-9f, mirror * 4f, 0f), stoneDark * 0.9f, 0.30f, 0.18f);
                Make(PrimitiveType.Cube, "Shin_Crust_" + side, pivot,
                    new Vector3(mirror * 0.10f, -0.72f, 0.02f), new Vector3(0.045f, 0.16f, 0.13f),
                    Quaternion.Euler(0f, 0f, mirror * 8f), crust, 0.10f, 0.15f);
            }

            // Torso — hip block, lit spine gap, chest block with the core ------
            Make(PrimitiveType.Cube, "Hip_Block", torso,
                new Vector3(0f, -0.05f, 0f), new Vector3(0.46f, 0.26f, 0.36f),
                Quaternion.identity, stone, 0.30f, 0.20f);
            Make(PrimitiveType.Cube, "Hip_Bevel", torso,
                new Vector3(0f, -0.185f, 0f), new Vector3(0.40f, 0.06f, 0.31f),
                Quaternion.identity, stoneDark, 0.30f, 0.18f);
            // The waist is a visible gap: only the lit spine rod crosses it.
            MakeGlow(PrimitiveType.Cylinder, "Spine_Rod", torso,
                new Vector3(0f, 0.13f, -0.02f), new Vector3(0.10f, 0.08f, 0.10f),
                Quaternion.identity, veilDeep, 1.25f);
            Make(PrimitiveType.Cube, "Chest_Block", torso,
                new Vector3(0f, 0.36f, 0f), new Vector3(0.54f, 0.44f, 0.38f),
                Quaternion.Euler(Jit(0.8f), 0f, 0f), stone, 0.30f, 0.20f);
            Make(PrimitiveType.Cube, "Chest_Bevel", torso,
                new Vector3(0f, 0.555f, 0f), new Vector3(0.48f, 0.10f, 0.34f),
                Quaternion.identity, stoneLight, 0.30f, 0.24f);
            Make(PrimitiveType.Cube, "Back_Slab", torso,
                new Vector3(0f, 0.34f, -0.205f), new Vector3(0.46f, 0.46f, 0.05f),
                Quaternion.Euler(Jit(1f), 0f, 0f), stoneDark, 0.30f, 0.18f);

            // The core: a recessed cavity, the crystal, its halo and iron ring.
            Make(PrimitiveType.Cube, "Chest_Cavity", torso,
                new Vector3(0f, 0.34f, 0.17f), new Vector3(0.25f, 0.27f, 0.06f),
                Quaternion.identity, stoneDark * 0.8f, 0.20f, 0.15f);
            MakeGlow(PrimitiveType.Sphere, "Core_Crystal", torso,
                new Vector3(0f, 0.34f, 0.185f), new Vector3(0.18f, 0.21f, 0.14f),
                Quaternion.Euler(0f, 0f, Jit(8f)), veilBright, 2.20f);
            MakeGlow(PrimitiveType.Cylinder, "Core_Halo", torso,
                new Vector3(0f, 0.34f, 0.183f), new Vector3(0.27f, 0.012f, 0.27f),
                Quaternion.Euler(90f, 0f, 0f), veil, 1.60f);
            Make(PrimitiveType.Cylinder, "Core_Ring", torso,
                new Vector3(0f, 0.34f, 0.172f), new Vector3(0.32f, 0.018f, 0.32f),
                Quaternion.Euler(90f, 0f, 0f), iron, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Core_Bolt_1", torso,
                new Vector3(-0.15f, 0.49f, 0.178f), new Vector3(0.04f, 0.04f, 0.03f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Core_Bolt_2", torso,
                new Vector3(0.15f, 0.49f, 0.178f), new Vector3(0.04f, 0.04f, 0.03f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Core_Bolt_3", torso,
                new Vector3(-0.15f, 0.19f, 0.178f), new Vector3(0.04f, 0.04f, 0.03f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Core_Bolt_4", torso,
                new Vector3(0.15f, 0.19f, 0.178f), new Vector3(0.04f, 0.04f, 0.03f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);

            // Faction accents: an inlaid band under the core and a hip plaque.
            Make(PrimitiveType.Cube, "Tunic_Trim", torso,
                new Vector3(0f, 0.155f, 0.196f), new Vector3(0.33f, 0.06f, 0.018f),
                Quaternion.Euler(0f, 0f, Jit(1f)), clothLight, 0.10f, 0.25f);
            Make(PrimitiveType.Cube, "Sigil_Plate", torso,
                new Vector3(0f, -0.05f, 0.186f), new Vector3(0.19f, 0.15f, 0.018f),
                Quaternion.Euler(0f, 0f, Jit(1.5f)), clothLight, 0.10f, 0.25f);

            // Shoulders: stone blocks with veilstone growing straight out ------
            Make(PrimitiveType.Cube, "Shoulder_Block_L", torso,
                new Vector3(-0.345f, 0.545f, 0f), new Vector3(0.26f, 0.24f, 0.32f),
                Quaternion.Euler(0f, 0f, 6f + Jit(1.5f)), stone, 0.30f, 0.20f);
            Make(PrimitiveType.Cube, "Shoulder_Block_R", torso,
                new Vector3(0.345f, 0.545f, 0f), new Vector3(0.26f, 0.24f, 0.32f),
                Quaternion.Euler(0f, 0f, -6f + Jit(1.5f)), stone, 0.30f, 0.20f);
            MakeGlow(PrimitiveType.Cube, "Shard_Shoulder_L1", torso,
                new Vector3(-0.375f, 0.74f, -0.03f), new Vector3(0.075f, 0.34f, 0.075f),
                Quaternion.Euler(14f + Jit(5f), 45f, 22f + Jit(5f)), veil, 1.40f);
            MakeGlow(PrimitiveType.Cube, "Shard_Shoulder_L2", torso,
                new Vector3(-0.30f, 0.685f, 0.09f), new Vector3(0.055f, 0.22f, 0.055f),
                Quaternion.Euler(-16f + Jit(5f), 45f, 12f), veil * 0.9f, 1.30f);
            // Asymmetric on purpose: the right shoulder grew only one spur.
            MakeGlow(PrimitiveType.Cube, "Shard_Shoulder_R1", torso,
                new Vector3(0.365f, 0.70f, -0.01f), new Vector3(0.065f, 0.26f, 0.065f),
                Quaternion.Euler(10f + Jit(5f), 45f, -26f + Jit(5f)), veil, 1.40f);
            MakeGlow(PrimitiveType.Cube, "Shard_Back_1", torso,
                new Vector3(-0.11f, 0.60f, -0.24f), new Vector3(0.07f, 0.40f, 0.07f),
                Quaternion.Euler(-30f + Jit(4f), 45f, 12f), veil, 1.40f);
            MakeGlow(PrimitiveType.Cube, "Shard_Back_2", torso,
                new Vector3(0.06f, 0.53f, -0.255f), new Vector3(0.06f, 0.31f, 0.06f),
                Quaternion.Euler(-38f + Jit(4f), 45f, -8f), veil * 0.92f, 1.30f);
            MakeGlow(PrimitiveType.Cube, "Shard_Back_3", torso,
                new Vector3(0.16f, 0.30f, -0.245f), new Vector3(0.05f, 0.22f, 0.05f),
                Quaternion.Euler(-24f + Jit(4f), 45f, -18f), veil * 0.85f, 1.20f);
            Make(PrimitiveType.Sphere, "Crust_1", torso,
                new Vector3(-0.14f, 0.585f, -0.20f), new Vector3(0.17f, 0.09f, 0.10f),
                Quaternion.Euler(0f, Jit(8f), 16f), crust, 0.10f, 0.15f);
            Make(PrimitiveType.Sphere, "Crust_2", torso,
                new Vector3(0.10f, 0.51f, -0.21f), new Vector3(0.14f, 0.08f, 0.09f),
                Quaternion.Euler(0f, Jit(8f), -12f), crust * 0.9f, 0.10f, 0.15f);

            // Faction accent: a pennon staked into the back by its owners.
            Make(PrimitiveType.Cylinder, "Pennon_Staff", torso,
                new Vector3(0.20f, 0.62f, -0.20f), new Vector3(0.022f, 0.16f, 0.022f),
                Quaternion.Euler(-14f, 0f, -10f), ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Pennon", torso,
                new Vector3(0.245f, 0.72f, -0.215f), new Vector3(0.014f, 0.13f, 0.17f),
                Quaternion.Euler(-14f + Jit(4f), Jit(5f), -10f), clothLight, 0.0f, 0.12f);

            // Arms — blocky, over-long, with lit rods at every joint ------------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                MakeGlow(PrimitiveType.Cylinder, "Shoulder_Rod_" + side, pivot,
                    new Vector3(mirror * 0.01f, -0.02f, 0f), new Vector3(0.07f, 0.05f, 0.07f),
                    Quaternion.Euler(0f, 0f, 90f), veilDeep, 1.25f);
                Make(PrimitiveType.Cube, "UpperArm_Block_" + side, pivot,
                    new Vector3(mirror * 0.02f, -0.18f, 0f), new Vector3(0.17f, 0.26f, 0.18f),
                    Quaternion.Euler(0f, 0f, mirror * 5f), stone, 0.30f, 0.20f);
                MakeGlow(PrimitiveType.Cylinder, "Elbow_Rod_" + side, pivot,
                    new Vector3(mirror * 0.035f, -0.34f, 0f), new Vector3(0.06f, 0.045f, 0.06f),
                    Quaternion.identity, veilDeep, 1.25f);
                // Forearms are heavier than the uppers: a harvester's build.
                Make(PrimitiveType.Cube, "Forearm_Block_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.50f, 0.02f), new Vector3(0.19f, 0.26f, 0.20f),
                    Quaternion.Euler(-6f, 0f, mirror * 3f), stone * 0.96f, 0.30f, 0.20f);
                MakeGlow(PrimitiveType.Cylinder, "Wrist_Rod_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.645f, 0.025f), new Vector3(0.055f, 0.035f, 0.055f),
                    Quaternion.identity, veilDeep, 1.25f);
                Make(PrimitiveType.Cube, "Fist_Block_" + side, pivot,
                    new Vector3(mirror * 0.06f, -0.755f, 0.03f), new Vector3(0.22f, 0.20f, 0.22f),
                    Quaternion.Euler(Jit(3f), mirror * 4f, 0f), stoneDark, 0.30f, 0.20f);
            }
            // A veilstone spur driven through the right fist: the "weapon".
            MakeGlow(PrimitiveType.Cube, "Knuckle_Shard_R", armR,
                new Vector3(0.06f, -0.755f, 0.17f), new Vector3(0.06f, 0.06f, 0.20f),
                Quaternion.Euler(0f, 0f, 45f), veil, 1.40f);

            // Head — a carved mask, no face -------------------------------------
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0f, 0.62f, 0f);
            head.localRotation = Quaternion.Euler(Jit(1f), Jit(2.5f), 0f);
            MakeGlow(PrimitiveType.Cylinder, "Neck_Rod", head,
                new Vector3(0f, 0f, -0.01f), new Vector3(0.09f, 0.05f, 0.09f),
                Quaternion.identity, veilDeep, 1.25f);
            Make(PrimitiveType.Cube, "Head_Block", head,
                new Vector3(0f, 0.16f, -0.01f), new Vector3(0.28f, 0.28f, 0.28f),
                Quaternion.Euler(Jit(1.5f), 0f, Jit(1f)), stone, 0.30f, 0.20f);
            Make(PrimitiveType.Cube, "Mask_Face", head,
                new Vector3(0f, 0.15f, 0.145f), new Vector3(0.245f, 0.265f, 0.05f),
                Quaternion.Euler(-3f, 0f, 0f), stoneLight, 0.30f, 0.26f);
            Make(PrimitiveType.Cube, "Mask_Groove_Brow", head,
                new Vector3(0f, 0.215f, 0.172f), new Vector3(0.205f, 0.024f, 0.02f),
                Quaternion.identity, stoneDark, 0.20f, 0.15f);
            Make(PrimitiveType.Cube, "Mask_Groove_Spine", head,
                new Vector3(0f, 0.115f, 0.172f), new Vector3(0.024f, 0.17f, 0.02f),
                Quaternion.identity, stoneDark, 0.20f, 0.15f);
            // Two recessed glyph slits where eyes would be. There are no eyes.
            MakeGlow(PrimitiveType.Cube, "Mask_Glyph_L", head,
                new Vector3(-0.062f, 0.185f, 0.175f), new Vector3(0.065f, 0.018f, 0.018f),
                Quaternion.Euler(0f, 0f, 6f), veilBright, 1.80f);
            MakeGlow(PrimitiveType.Cube, "Mask_Glyph_R", head,
                new Vector3(0.062f, 0.185f, 0.175f), new Vector3(0.065f, 0.018f, 0.018f),
                Quaternion.Euler(0f, 0f, -6f), veilBright, 1.80f);
            Make(PrimitiveType.Cube, "Head_Band", head,
                new Vector3(0f, 0.285f, -0.01f), new Vector3(0.29f, 0.035f, 0.29f),
                Quaternion.identity, iron, 0.85f, 0.40f);
            MakeGlow(PrimitiveType.Cube, "Crown_Shard", head,
                new Vector3(-0.02f, 0.40f, -0.05f), new Vector3(0.06f, 0.26f, 0.06f),
                Quaternion.Euler(-18f + Jit(5f), 45f, 9f + Jit(4f)), veil, 1.50f);

            root.AddComponent<GolemAutarkAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Golem Autark: a slow, stiff-legged
    /// construct walk (long stride, shallow swing, no torso bounce — stone
    /// does not bob), heavy stone arms that swing straight, an idle where the
    /// whole frame settles instead of breathing, a continuous violet pulse
    /// across the core and the veilstone shards (the shards lag the core so
    /// the light looks like it is bleeding outward from the chest), and
    /// faction-color tint of the accent parts (Tunic_Trim, Sigil_Plate,
    /// Pennon) once EntityReference is wired by the orchestrator.
    /// </summary>
    public class GolemAutarkAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 19f;

        [Tooltip("Arm swing amplitude in degrees at full stride.")]
        public float ArmSwing = 12f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 1.35f;

        [Tooltip("Walking side-to-side weight transfer in degrees.")]
        public float WeightRoll = 2.8f;

        [Tooltip("Core pulses per second.")]
        public float PulseRate = 0.55f;

        [Tooltip("How far the core emission swings around its base level.")]
        public float PulseDepth = 0.45f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest;
        private Material[] _coreMats;   // pulse in phase
        private Material[] _shardMats;  // pulse a beat behind the core
        private Color[] _coreBase, _shardBase;
        private Material _trimMat, _sigilMat, _pennonMat;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _phase; // walk cycle phase, radians
        private float _gait;  // 0 = idle, 1 = walking (smoothed)

        private static readonly string[] CoreParts =
        {
            "Core_Crystal", "Core_Halo", "Mask_Glyph_L", "Mask_Glyph_R"
        };

        private static readonly string[] ShardParts =
        {
            "Shard_Shoulder_L1", "Shard_Shoulder_L2", "Shard_Shoulder_R1",
            "Shard_Back_1", "Shard_Back_2", "Shard_Back_3",
            "Knuckle_Shard_R", "Crown_Shard"
        };

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

            CollectGlow(CoreParts, out _coreMats, out _coreBase);
            CollectGlow(ShardParts, out _shardMats, out _shardBase);

            _trimMat   = MatOf("Tunic_Trim");
            _sigilMat  = MatOf("Sigil_Plate");
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
            _gait = Mathf.MoveTowards(_gait, moving ? 1f : 0f, dt * 4f);

            // Phase advances with distance so stride matches ground speed.
            _phase += (dist / Mathf.Max(StrideLength, 0.01f)) * 2f * Mathf.PI;

            float t = Time.time;
            float swing = Mathf.Sin(_phase) * _gait;

            if (_legL != null) _legL.localRotation = Quaternion.Euler( swing * LegSwing, 0f, 0f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-swing * LegSwing, 0f, 0f);
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(-swing * ArmSwing, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing, 0f, 0f);

            float idleAmt = 1f - _gait;

            if (_torso != null)
            {
                // Stone does not bob: the walk is a flat roll, no vertical bounce.
                float roll = Mathf.Sin(_phase) * WeightRoll * _gait;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(2f * _gait, 0f, roll);
            }
            if (_head != null)
            {
                // A slow mechanical sweep, far slower than a human head turn.
                float yaw = Mathf.Sin(t * 0.18f) * 8f * idleAmt;
                _head.localRotation = _headRest * Quaternion.Euler(0f, yaw, 0f);
            }

            // Curse glow: the core breathes, the shards follow a beat later.
            float corePulse = 1f + Mathf.Sin(t * PulseRate * 2f * Mathf.PI) * PulseDepth;
            float shardPulse = 1f + Mathf.Sin((t - 0.35f) * PulseRate * 2f * Mathf.PI) * PulseDepth * 0.7f;
            ApplyGlow(_coreMats, _coreBase, corePulse);
            ApplyGlow(_shardMats, _shardBase, shardPulse);
        }

        private static void ApplyGlow(Material[] mats, Color[] baseCols, float scale)
        {
            if (mats == null || baseCols == null) return;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                mats[i].SetColor("_EmissionColor", baseCols[i] * scale);
            }
        }

        private void CollectGlow(string[] names, out Material[] mats, out Color[] baseCols)
        {
            mats = new Material[names.Length];
            baseCols = new Color[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var m = MatOf(names[i]);
                mats[i] = m;
                baseCols[i] = (m != null && m.HasProperty("_EmissionColor"))
                    ? m.GetColor("_EmissionColor")
                    : Color.black;
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
            Tint(_trimMat, fc, 0.10f, true); // soft emissive so it reads at distance
            Tint(_sigilMat, fc, 0.15f, false);
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
