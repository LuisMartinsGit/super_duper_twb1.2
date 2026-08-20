// File: Assets/GameData/TechTree/Units/Sects/ScarGuard/ScarGuardVisual.cs
// Procedural visual for the Sect of Renewal Scar Guard: HEAVY frontline
// infantry that hits harder the closer it is to dying, so the whole rig is
// built to read "held together with staples" at RTS camera distance —
// battered plate with riveted repair patches in three mismatched metals,
// deliberately asymmetric pauldrons and tassets, bandage wrappings over
// both arms, a cracked greathelm stapled shut across the face, and a
// notched, blood-darkened cleaver hanging point-down in the right hand.
// Built entirely from primitives (Smelter idiom — per-part URP/Lit
// material, metallic/smoothness contrast, small deterministic tilts,
// colliders destroyed). Player-color accents (Tabard_Front, Tunic_Trim,
// Pennon) are tinted at runtime by ScarGuardAnimator via EntityReference
// (LedgerVisual.TryTint pattern) — the orchestrator adds EntityReference
// after Build returns, so the animator guards for it being absent.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Input; // EntityReference

namespace TheWaningBorder.Presentation
{
    public static class ScarGuardVisual
    {
        /// <summary>
        /// Builds the full Scar Guard rig and returns the root. The root
        /// sits at ground level (feet at y=0); figure height ~1.88 m to the
        /// greathelm crown, cleaver tip hangs a hand-span above the ground.
        /// Deterministic: all jitter flows through the seeded Random.
        /// </summary>
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("ScarGuardVisual");

            // Palette ---------------------------------------------------------
            var iron       = new Color(0.52f, 0.54f, 0.58f); // the original harness
            var ironDark   = new Color(0.28f, 0.29f, 0.32f); // rivets, staples, straps
            var ironPale   = new Color(0.68f, 0.69f, 0.72f); // fresh replacement plate
            var ironRust   = new Color(0.44f, 0.31f, 0.22f); // scavenged rusted plate
            var bandage    = new Color(0.82f, 0.79f, 0.70f); // arm wrappings
            var bandStain  = new Color(0.55f, 0.42f, 0.34f); // soaked-through wrap
            var clothDark  = new Color(0.32f, 0.30f, 0.27f); // under-cloth, trousers
            var clothLight = new Color(0.87f, 0.85f, 0.79f); // accent base (tinted)
            var leather    = new Color(0.43f, 0.27f, 0.16f); // belt, boot cuffs
            var leatherDrk = new Color(0.30f, 0.19f, 0.12f); // boots, grip wrap
            var wood       = new Color(0.44f, 0.33f, 0.20f); // cleaver haft
            var bloodDark  = new Color(0.34f, 0.15f, 0.13f); // dried on the blade

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
            // Favours the patched right side: a permanent lopsided set.
            torso.localRotation = Quaternion.Euler(2f + Jit(1.5f), 0f, -2.5f + Jit(1f));

            Transform LegPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(pelvis, false);
                t.localPosition = new Vector3(x, 0f, 0f); // hip height, swings around X
                return t;
            }
            var legL = LegPivot("LegPivot_L", -0.12f);
            var legR = LegPivot("LegPivot_R",  0.12f);

            Transform ArmPivot(string name, float x)
            {
                var t = new GameObject(name).transform;
                t.SetParent(torso, false);
                t.localPosition = new Vector3(x, 0.44f, 0f); // shoulder height
                return t;
            }
            var armL = ArmPivot("ArmPivot_L", -0.30f);
            var armR = ArmPivot("ArmPivot_R",  0.30f);

            // Legs — plate over cloth; the two knee cops are different metals.
            foreach (var (side, pivot, mirror) in new[] { ("L", legL, -1f), ("R", legR, 1f) })
            {
                var copMetal = mirror < 0f ? ironPale : ironRust;
                Make(PrimitiveType.Cylinder, "Thigh_" + side, pivot,
                    new Vector3(0f, -0.21f, 0f), new Vector3(0.145f, 0.15f, 0.145f),
                    Quaternion.Euler(0f, 0f, mirror * 2f), clothDark, 0.05f, 0.10f);
                Make(PrimitiveType.Cube, "Thigh_Plate_" + side, pivot,
                    new Vector3(0f, -0.22f, 0.025f), new Vector3(0.16f, 0.21f, 0.155f),
                    Quaternion.Euler(Jit(2f), 0f, mirror * 2f), iron, 0.80f, 0.35f);
                Make(PrimitiveType.Sphere, "Knee_Cop_" + side, pivot,
                    new Vector3(0f, -0.40f, 0.015f), new Vector3(0.14f, 0.12f, 0.14f),
                    Quaternion.identity, copMetal, 0.80f, 0.38f);
                Make(PrimitiveType.Cylinder, "Shin_" + side, pivot,
                    new Vector3(0f, -0.61f, 0f), new Vector3(0.115f, 0.155f, 0.115f),
                    Quaternion.Euler(Jit(1.5f), 0f, 0f), clothDark, 0.05f, 0.10f);
                Make(PrimitiveType.Cube, "Greave_" + side, pivot,
                    new Vector3(0f, -0.62f, 0.02f), new Vector3(0.14f, 0.26f, 0.145f),
                    Quaternion.Euler(Jit(2f), 0f, 0f), iron * 0.95f, 0.80f, 0.35f);
                Make(PrimitiveType.Cylinder, "Boot_Cuff_" + side, pivot,
                    new Vector3(0f, -0.76f, 0f), new Vector3(0.15f, 0.05f, 0.15f),
                    Quaternion.identity, leather, 0.08f, 0.22f);
                Make(PrimitiveType.Cube, "Boot_" + side, pivot,
                    new Vector3(0f, -0.885f, 0.05f), new Vector3(0.15f, 0.09f, 0.26f),
                    Quaternion.Euler(0f, mirror * 3f, 0f), leatherDrk, 0.08f, 0.20f);
            }
            // Asymmetry: only the right greave carries a bolted-on scrap patch.
            Make(PrimitiveType.Cube, "Greave_Patch_R", legR,
                new Vector3(0.055f, -0.60f, 0.03f), new Vector3(0.055f, 0.15f, 0.11f),
                Quaternion.Euler(0f, 0f, 7f + Jit(2f)), ironRust, 0.80f, 0.30f);
            Make(PrimitiveType.Sphere, "Greave_Patch_Rivet_R", legR,
                new Vector3(0.075f, -0.55f, 0.03f), new Vector3(0.028f, 0.028f, 0.028f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);

            // Torso — the battered cuirass and its repair history ---------------
            Make(PrimitiveType.Cube, "Fauld", torso,
                new Vector3(0f, -0.05f, 0f), new Vector3(0.41f, 0.21f, 0.31f),
                Quaternion.identity, iron * 0.93f, 0.80f, 0.32f);
            // Mismatched tassets: the left is a stub, the right a long scrap slab.
            Make(PrimitiveType.Cube, "Tasset_L", torso,
                new Vector3(-0.16f, -0.17f, 0.01f), new Vector3(0.16f, 0.20f, 0.25f),
                Quaternion.Euler(0f, 0f, 5f + Jit(2f)), iron, 0.80f, 0.35f);
            Make(PrimitiveType.Cube, "Tasset_R", torso,
                new Vector3(0.175f, -0.22f, 0.01f), new Vector3(0.185f, 0.30f, 0.26f),
                Quaternion.Euler(0f, 0f, -7f + Jit(2f)), ironRust, 0.80f, 0.28f);
            Make(PrimitiveType.Cube, "Belt", torso,
                new Vector3(0f, 0.09f, 0f), new Vector3(0.43f, 0.06f, 0.32f),
                Quaternion.identity, leather, 0.08f, 0.25f);
            Make(PrimitiveType.Cube, "Belt_Buckle", torso,
                new Vector3(0f, 0.09f, 0.158f), new Vector3(0.075f, 0.055f, 0.022f),
                Quaternion.identity, ironPale, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Cuirass", torso,
                new Vector3(0f, 0.30f, 0f), new Vector3(0.44f, 0.40f, 0.30f),
                Quaternion.Euler(Jit(1f), 0f, 0f), iron, 0.80f, 0.35f);
            Make(PrimitiveType.Cube, "Cuirass_Ridge", torso,
                new Vector3(0f, 0.30f, 0.155f), new Vector3(0.05f, 0.38f, 0.02f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);

            // Three riveted repair patches, each a different metal and angle.
            Make(PrimitiveType.Cube, "Patch_Plate_1", torso,
                new Vector3(-0.115f, 0.36f, 0.15f), new Vector3(0.17f, 0.20f, 0.03f),
                Quaternion.Euler(0f, 0f, 8f + Jit(3f)), ironPale, 0.80f, 0.38f);
            Make(PrimitiveType.Cube, "Patch_Plate_2", torso,
                new Vector3(0.205f, 0.24f, 0.05f), new Vector3(0.03f, 0.22f, 0.19f),
                Quaternion.Euler(Jit(3f), 0f, -11f), ironRust, 0.80f, 0.26f);
            Make(PrimitiveType.Cube, "Patch_Plate_3", torso,
                new Vector3(0.06f, 0.16f, 0.152f), new Vector3(0.15f, 0.13f, 0.028f),
                Quaternion.Euler(0f, 0f, -6f + Jit(3f)), ironPale * 0.9f, 0.80f, 0.32f);
            Make(PrimitiveType.Cube, "Patch_Plate_Back", torso,
                new Vector3(-0.05f, 0.32f, -0.152f), new Vector3(0.20f, 0.24f, 0.03f),
                Quaternion.Euler(0f, 0f, -9f + Jit(3f)), ironRust, 0.80f, 0.26f);

            // Rivets around the patch edges — the "stapled together" read.
            Make(PrimitiveType.Sphere, "Rivet_1", torso,
                new Vector3(-0.175f, 0.43f, 0.155f), new Vector3(0.028f, 0.028f, 0.022f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Rivet_2", torso,
                new Vector3(-0.05f, 0.44f, 0.155f), new Vector3(0.028f, 0.028f, 0.022f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Rivet_3", torso,
                new Vector3(-0.175f, 0.28f, 0.155f), new Vector3(0.028f, 0.028f, 0.022f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Rivet_4", torso,
                new Vector3(-0.05f, 0.29f, 0.155f), new Vector3(0.028f, 0.028f, 0.022f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Rivet_5", torso,
                new Vector3(0.215f, 0.33f, 0.115f), new Vector3(0.026f, 0.026f, 0.026f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Rivet_6", torso,
                new Vector3(0.215f, 0.15f, 0.115f), new Vector3(0.026f, 0.026f, 0.026f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);

            // Staples bridging the splits in the original breastplate.
            Make(PrimitiveType.Cube, "Staple_1", torso,
                new Vector3(-0.02f, 0.47f, 0.152f), new Vector3(0.10f, 0.022f, 0.03f),
                Quaternion.Euler(0f, 0f, 22f + Jit(4f)), ironDark, 0.85f, 0.42f);
            Make(PrimitiveType.Cube, "Staple_2", torso,
                new Vector3(0.02f, 0.40f, 0.152f), new Vector3(0.09f, 0.02f, 0.03f),
                Quaternion.Euler(0f, 0f, 30f + Jit(4f)), ironDark, 0.85f, 0.42f);
            Make(PrimitiveType.Cube, "Staple_3", torso,
                new Vector3(0.07f, 0.33f, 0.152f), new Vector3(0.085f, 0.02f, 0.03f),
                Quaternion.Euler(0f, 0f, 26f + Jit(4f)), ironDark, 0.85f, 0.42f);

            // Faction accents: the torn tabard down the front and a waist strip.
            Make(PrimitiveType.Cube, "Tabard_Front", torso,
                new Vector3(-0.01f, 0.20f, 0.162f), new Vector3(0.15f, 0.50f, 0.012f),
                Quaternion.Euler(-1.5f, 0f, 2f + Jit(1.5f)), clothLight, 0.0f, 0.12f);
            Make(PrimitiveType.Cube, "Tunic_Trim", torso,
                new Vector3(-0.01f, -0.09f, 0.16f), new Vector3(0.22f, 0.14f, 0.012f),
                Quaternion.Euler(0f, 0f, -3f + Jit(2f)), clothLight, 0.0f, 0.12f);

            Make(PrimitiveType.Cylinder, "Gorget", torso,
                new Vector3(0f, 0.52f, 0f), new Vector3(0.20f, 0.05f, 0.20f),
                Quaternion.identity, ironDark, 0.85f, 0.40f);

            // Deliberately mismatched shoulders: a huge slab left, a scrap right.
            Make(PrimitiveType.Sphere, "Pauldron_L", torso,
                new Vector3(-0.285f, 0.505f, 0f), new Vector3(0.28f, 0.21f, 0.28f),
                Quaternion.Euler(0f, 0f, 12f), iron, 0.80f, 0.35f);
            Make(PrimitiveType.Cube, "Pauldron_L_Lame", torso,
                new Vector3(-0.315f, 0.40f, 0f), new Vector3(0.19f, 0.09f, 0.25f),
                Quaternion.Euler(0f, 0f, 16f + Jit(2f)), ironPale, 0.80f, 0.38f);
            Make(PrimitiveType.Sphere, "Pauldron_R", torso,
                new Vector3(0.275f, 0.485f, 0f), new Vector3(0.20f, 0.145f, 0.215f),
                Quaternion.Euler(0f, 0f, -9f), ironRust, 0.80f, 0.26f);
            Make(PrimitiveType.Cube, "Pauldron_R_Strap", torso,
                new Vector3(0.22f, 0.44f, 0f), new Vector3(0.05f, 0.16f, 0.24f),
                Quaternion.Euler(0f, 0f, -14f), leather, 0.08f, 0.22f);
            // Faction accent: a rag pennon knotted through the left pauldron.
            Make(PrimitiveType.Cube, "Pennon", torso,
                new Vector3(-0.315f, 0.60f, -0.03f), new Vector3(0.016f, 0.20f, 0.11f),
                Quaternion.Euler(-10f + Jit(4f), Jit(5f), 14f), clothLight, 0.0f, 0.12f);
            // Spare bandage roll hooked on the belt — Renewal's calling card.
            Make(PrimitiveType.Cylinder, "Bandage_Roll", torso,
                new Vector3(-0.19f, 0.03f, 0.10f), new Vector3(0.075f, 0.045f, 0.075f),
                Quaternion.Euler(0f, 0f, 90f), bandage, 0.0f, 0.12f);

            // Arms — plate under bandage wrappings ------------------------------
            foreach (var (side, pivot, mirror) in new[] { ("L", armL, -1f), ("R", armR, 1f) })
            {
                Make(PrimitiveType.Cylinder, "UpperArm_" + side, pivot,
                    new Vector3(mirror * 0.03f, -0.13f, 0f), new Vector3(0.105f, 0.12f, 0.105f),
                    Quaternion.Euler(0f, 0f, mirror * 6f), iron * 0.9f, 0.80f, 0.32f);
                Make(PrimitiveType.Cylinder, "Bandage_Upper_" + side, pivot,
                    new Vector3(mirror * 0.032f, -0.175f, 0f), new Vector3(0.118f, 0.055f, 0.118f),
                    Quaternion.Euler(Jit(4f), 0f, mirror * 6f), bandage, 0.0f, 0.12f);
                Make(PrimitiveType.Sphere, "Elbow_" + side, pivot,
                    new Vector3(mirror * 0.045f, -0.26f, 0.01f), new Vector3(0.105f, 0.095f, 0.105f),
                    Quaternion.identity, ironDark, 0.85f, 0.38f);
                Make(PrimitiveType.Cylinder, "Forearm_" + side, pivot,
                    new Vector3(mirror * 0.05f, -0.375f, 0.03f), new Vector3(0.09f, 0.10f, 0.09f),
                    Quaternion.Euler(-10f, 0f, mirror * 3f), clothDark, 0.05f, 0.10f);
                // The right wrap is soaked through; the left is still clean.
                Make(PrimitiveType.Cylinder, "Bandage_Fore_" + side, pivot,
                    new Vector3(mirror * 0.052f, -0.40f, 0.03f), new Vector3(0.10f, 0.07f, 0.10f),
                    Quaternion.Euler(-10f + Jit(4f), 0f, 0f),
                    mirror < 0f ? bandage : bandStain, 0.0f, 0.12f);
                Make(PrimitiveType.Sphere, "Hand_" + side, pivot,
                    new Vector3(mirror * 0.055f, -0.49f, 0.06f), new Vector3(0.09f, 0.085f, 0.09f),
                    Quaternion.identity, ironDark, 0.85f, 0.40f);
            }

            // Notched cleaver, hanging point-down in the right hand -------------
            var cleaver = new GameObject("Cleaver").transform;
            cleaver.SetParent(armR, false);
            cleaver.localPosition = new Vector3(0.055f, -0.49f, 0.06f); // at the hand
            cleaver.localRotation = Quaternion.Euler(6f + Jit(2f), 0f, 8f + Jit(2.5f));
            Make(PrimitiveType.Cylinder, "Cleaver_Haft", cleaver,
                new Vector3(0f, 0.03f, 0f), new Vector3(0.038f, 0.14f, 0.038f),
                Quaternion.identity, wood, 0.10f, 0.22f);
            Make(PrimitiveType.Cylinder, "Cleaver_Grip", cleaver,
                new Vector3(0f, 0.0f, 0f), new Vector3(0.046f, 0.09f, 0.046f),
                Quaternion.identity, leatherDrk, 0.08f, 0.25f);
            Make(PrimitiveType.Sphere, "Cleaver_Pommel", cleaver,
                new Vector3(0f, 0.175f, 0f), new Vector3(0.055f, 0.05f, 0.055f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Cleaver_Guard", cleaver,
                new Vector3(0f, -0.115f, 0f), new Vector3(0.15f, 0.035f, 0.055f),
                Quaternion.Euler(0f, 0f, Jit(2f)), ironDark, 0.85f, 0.42f);
            // Broad chopping blade — short, heavy, and wider at the tip.
            Make(PrimitiveType.Cube, "Cleaver_Blade", cleaver,
                new Vector3(0.01f, -0.37f, 0f), new Vector3(0.22f, 0.46f, 0.028f),
                Quaternion.Euler(0f, 0f, -2f), iron * 0.95f, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Cleaver_Spine", cleaver,
                new Vector3(-0.10f, -0.37f, 0f), new Vector3(0.045f, 0.47f, 0.042f),
                Quaternion.Euler(0f, 0f, -2f), ironDark, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Cleaver_Edge", cleaver,
                new Vector3(0.115f, -0.37f, 0f), new Vector3(0.03f, 0.44f, 0.018f),
                Quaternion.Euler(0f, 0f, -2f), ironPale, 0.85f, 0.60f);
            // A chunk chopped out of the edge, and old blood along the spine.
            Make(PrimitiveType.Cube, "Cleaver_Notch", cleaver,
                new Vector3(0.115f, -0.24f, 0f), new Vector3(0.045f, 0.055f, 0.032f),
                Quaternion.Euler(0f, 0f, 38f + Jit(4f)), ironDark, 0.80f, 0.30f);
            Make(PrimitiveType.Cube, "Cleaver_Blood", cleaver,
                new Vector3(-0.04f, -0.44f, 0.017f), new Vector3(0.09f, 0.20f, 0.006f),
                Quaternion.Euler(0f, 0f, -2f), bloodDark, 0.25f, 0.20f);

            // Head — cracked greathelm, stapled shut ----------------------------
            var head = new GameObject("HeadPivot").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0f, 0.57f, 0f);
            head.localRotation = Quaternion.Euler(Jit(1.5f), Jit(3f), 0f);
            Make(PrimitiveType.Sphere, "Head", head,
                new Vector3(0f, 0.07f, 0f), new Vector3(0.17f, 0.185f, 0.17f),
                Quaternion.identity, clothDark, 0.05f, 0.15f);
            Make(PrimitiveType.Cylinder, "Helm_Bandage", head,
                new Vector3(0f, 0.005f, 0f), new Vector3(0.20f, 0.03f, 0.20f),
                Quaternion.Euler(Jit(3f), 0f, Jit(2f)), bandage, 0.0f, 0.12f);
            Make(PrimitiveType.Cylinder, "Helm_Barrel", head,
                new Vector3(0f, 0.115f, 0f), new Vector3(0.225f, 0.135f, 0.225f),
                Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)), iron, 0.85f, 0.40f);
            Make(PrimitiveType.Sphere, "Helm_Crown", head,
                new Vector3(0f, 0.235f, 0f), new Vector3(0.225f, 0.13f, 0.225f),
                Quaternion.identity, iron * 0.95f, 0.85f, 0.42f);
            Make(PrimitiveType.Cube, "Helm_Face", head,
                new Vector3(0f, 0.115f, 0.10f), new Vector3(0.20f, 0.24f, 0.035f),
                Quaternion.Euler(-3f, 0f, 0f), iron * 0.92f, 0.85f, 0.40f);
            Make(PrimitiveType.Cube, "Helm_Slit", head,
                new Vector3(0f, 0.165f, 0.118f), new Vector3(0.155f, 0.022f, 0.02f),
                Quaternion.identity, new Color(0.06f, 0.06f, 0.07f), 0.10f, 0.10f);
            // The crack runs diagonally across the face, held by two staples.
            Make(PrimitiveType.Cube, "Helm_Crack", head,
                new Vector3(0.02f, 0.12f, 0.121f), new Vector3(0.024f, 0.26f, 0.016f),
                Quaternion.Euler(0f, 0f, 34f + Jit(3f)), ironDark * 0.7f, 0.50f, 0.20f);
            Make(PrimitiveType.Cube, "Helm_Staple_1", head,
                new Vector3(0.055f, 0.185f, 0.124f), new Vector3(0.075f, 0.02f, 0.024f),
                Quaternion.Euler(0f, 0f, -56f + Jit(4f)), ironPale, 0.85f, 0.45f);
            Make(PrimitiveType.Cube, "Helm_Staple_2", head,
                new Vector3(-0.01f, 0.075f, 0.124f), new Vector3(0.07f, 0.02f, 0.024f),
                Quaternion.Euler(0f, 0f, -56f + Jit(4f)), ironPale, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Helm_Rivet_L", head,
                new Vector3(-0.10f, 0.10f, 0.09f), new Vector3(0.03f, 0.03f, 0.03f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            Make(PrimitiveType.Sphere, "Helm_Rivet_R", head,
                new Vector3(0.10f, 0.10f, 0.09f), new Vector3(0.03f, 0.03f, 0.03f),
                Quaternion.identity, ironDark, 0.85f, 0.45f);
            // One horn socket left, its horn long gone: more asymmetry.
            Make(PrimitiveType.Cylinder, "Helm_Horn_Socket", head,
                new Vector3(-0.125f, 0.235f, 0f), new Vector3(0.05f, 0.035f, 0.05f),
                Quaternion.Euler(0f, 0f, 62f), ironRust, 0.80f, 0.30f);

            root.AddComponent<ScarGuardAnimator>();
            return root;
        }
    }

    /// <summary>
    /// Runtime animator for the procedural Scar Guard: a heavy, limping walk
    /// (the patched right leg takes a shorter stride and the torso dips onto
    /// it, so the figure lurches), a cleaver arm that swings at a third
    /// amplitude so the blade does not scythe around, an idle labored-breath
    /// heave plus an occasional cleaver shoulder-rest, and faction-color tint
    /// of the accent parts (Tabard_Front, Tunic_Trim, Pennon) once
    /// EntityReference is wired by the orchestrator.
    /// </summary>
    public class ScarGuardAnimator : MonoBehaviour
    {
        [Tooltip("Leg swing amplitude in degrees at full stride.")]
        public float LegSwing = 24f;

        [Tooltip("Fraction of the swing the injured right leg takes (the limp).")]
        public float LimpFactor = 0.68f;

        [Tooltip("Arm swing amplitude in degrees at full stride (free arm).")]
        public float ArmSwing = 13f;

        [Tooltip("Stride length in meters per full walk cycle.")]
        public float StrideLength = 0.98f;

        [Tooltip("Idle labored-breath amplitude in degrees.")]
        public float BreathAmount = 2.6f;

        [Tooltip("Seconds between idle cleaver shoulder-rests.")]
        public float RestInterval = 4.6f;

        private Transform _legL, _legR, _armL, _armR, _torso, _head, _cleaver;
        private Quaternion _armLRest, _armRRest, _torsoRest, _headRest, _cleaverRest;
        private Material _tabardMat, _trimMat, _pennonMat;
        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;
        private bool _tinted;
        private Vector3 _prevPos;
        private bool _hasPrevPos;
        private float _phase;     // walk cycle phase, radians
        private float _gait;      // 0 = idle, 1 = walking (smoothed)
        private float _restClock; // idle cleaver-rest timer

        void Start()
        {
            _legL    = FindDeep(transform, "LegPivot_L");
            _legR    = FindDeep(transform, "LegPivot_R");
            _armL    = FindDeep(transform, "ArmPivot_L");
            _armR    = FindDeep(transform, "ArmPivot_R");
            _torso   = FindDeep(transform, "TorsoPivot");
            _head    = FindDeep(transform, "HeadPivot");
            _cleaver = FindDeep(transform, "Cleaver");
            if (_armL != null)    _armLRest    = _armL.localRotation;
            if (_armR != null)    _armRRest    = _armR.localRotation;
            if (_torso != null)   _torsoRest   = _torso.localRotation;
            if (_head != null)    _headRest    = _head.localRotation;
            if (_cleaver != null) _cleaverRest = _cleaver.localRotation;

            _tabardMat = MatOf("Tabard_Front");
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

            // The right leg is the patched one: it swings short, which reads
            // as a limp once the torso rolls onto it below.
            if (_legL != null) _legL.localRotation = Quaternion.Euler( swing * LegSwing, 0f, 0f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-swing * LegSwing * LimpFactor, 0f, 0f);
            if (_armL != null)
                _armL.localRotation = _armLRest * Quaternion.Euler(-swing * ArmSwing, 0f, 0f);
            if (_armR != null)
                _armR.localRotation = _armRRest * Quaternion.Euler( swing * ArmSwing * 0.32f, 0f, 0f);

            // Idle: heavy breathing plus a periodic heave where the cleaver
            // is hauled up onto the shoulder and lowered again.
            float idleAmt = 1f - _gait;
            _restClock += dt * idleAmt;
            if (_restClock > RestInterval) _restClock -= RestInterval;
            float restT = _restClock / Mathf.Max(RestInterval, 0.01f);
            // Raised-cosine pulse over the first 26% of the interval.
            float rest = restT < 0.26f
                ? Mathf.Sin(restT / 0.26f * Mathf.PI)
                : 0f;
            if (_cleaver != null)
                _cleaver.localRotation = _cleaverRest * Quaternion.Euler(-34f * rest * idleAmt, 0f, 0f);

            if (_torso != null)
            {
                float breath = Mathf.Sin(t * 1.5f) * BreathAmount * idleAmt;
                float walkLean = 5f * _gait;
                // Roll onto the short leg on its half of the cycle: the lurch.
                float lurch = Mathf.Max(0f, -Mathf.Sin(_phase)) * 4.5f * _gait;
                _torso.localRotation = _torsoRest
                    * Quaternion.Euler(walkLean + breath * 0.4f, 0f, breath - lurch);
            }
            if (_head != null)
            {
                float yaw = Mathf.Sin(t * 0.33f) * 4f * idleAmt;
                float sag = 4f * idleAmt + 6f * rest * idleAmt;
                _head.localRotation = _headRest * Quaternion.Euler(sag, yaw, 0f);
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
            Tint(_tabardMat, fc, 0.10f, true); // soft emissive so it reads at distance
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
