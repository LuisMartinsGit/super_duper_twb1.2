// File: Assets/GameData/TechTree/Buildings/Alanthor/Tower/WatchTowerVisual.cs
// Procedural Watch Tower — the tallest silhouette in the Alanthor set (the
// LoS-28 building; sell the height). Tall tapered stone tower: base course,
// three subtly tapering shaft sections, corbelled crown, crenellated parapet
// (merlon loop, same pattern as AddDeckAndParapets), dark inset arrow slits,
// wooden lookout platform with ladder hints, and a brazier with an emissive
// flame at the top. The faction accent is a Stripe_Banner hung from the
// parapet (tinted by BuildingFactionColorMarker via the "stripe" name rule).
//
// Standalone static builder: the orchestrator (PresentationSpawnSystem) wires
// the pid branch and handles FitSelectionCollider / EntityReference / faction
// marker after Build returns. Part names carry leading rise-group numbers
// (1_ base, 2_ shaft, 3_ crown, 4_ props) so BuildingRiseData staggers the
// construction rise bottom-up.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class WatchTowerVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);

            var root = new GameObject("WatchTower");

            // ── Palette ─────────────────────────────────────────────────────
            var stone      = new Color(0.47f, 0.42f, 0.36f);   // weathered granite
            var stoneLight = new Color(0.55f, 0.50f, 0.44f);   // sun-bleached upper courses
            var stoneDark  = new Color(0.31f, 0.27f, 0.23f);   // course bands / shadow lines
            var slitDark   = new Color(0.06f, 0.05f, 0.05f);   // arrow-slit voids
            var beam       = new Color(0.32f, 0.21f, 0.13f);   // dark oak timber
            var plank      = new Color(0.45f, 0.31f, 0.18f);   // lookout planking
            var iron       = new Color(0.19f, 0.18f, 0.17f);   // brazier metal
            var embers     = new Color(0.95f, 0.42f, 0.10f);   // coal bed
            var flame      = new Color(1.00f, 0.62f, 0.18f);   // brazier flame


            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);

            // Tiny deterministic jitter helpers (hand-built feel).
            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            // ══ 1_ BASE COURSE ══════════════════════════════════════════════
            Make(PrimitiveType.Cube, "1_Plinth", new Vector3(0f, 0.22f, 0f),
                new Vector3(3.8f, 0.44f, 3.8f), Quaternion.Euler(0f, Jit(1.5f), 0f),
                stoneDark, 0.05f, 0.12f, false);

            // Wide splayed base drum — the tower's footing.
            Make(PrimitiveType.Cylinder, "1_BaseDrum", new Vector3(0f, 0.95f, 0f),
                new Vector3(3.35f, 0.55f, 3.35f), Quaternion.identity,
                stone, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cylinder, "1_BaseSkirt", new Vector3(0f, 0.52f, 0f),
                new Vector3(3.55f, 0.10f, 3.55f), Quaternion.identity,
                stoneDark, 0.05f, 0.12f, false);

            // Four rough footing stones half-sunk at the plinth corners.
            for (int i = 0; i < 4; i++)
            {
                float ang = (45f + 90f * i) * Mathf.Deg2Rad;
                Make(PrimitiveType.Cube, $"1_FootStone_{i}",
                    new Vector3(Mathf.Cos(ang) * 1.78f, 0.34f + Jit(0.04f), Mathf.Sin(ang) * 1.78f),
                    new Vector3(0.62f + Jit(0.08f), 0.42f, 0.55f + Jit(0.08f)),
                    Quaternion.Euler(Jit(2f), (float)(rng.NextDouble() * 90.0), Jit(2f)),
                    stoneDark * 0.92f, 0.05f, 0.10f, false);
            }

            // ══ 2_ SHAFT — three subtly tapering sections with course bands ══
            // Section A (widest, darkest — closest to the weather line).
            Make(PrimitiveType.Cylinder, "2_ShaftA", new Vector3(0f, 2.55f, 0f),
                new Vector3(2.95f, 1.35f, 2.95f), Quaternion.Euler(Jit(0.5f), 0f, Jit(0.5f)),
                stone, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cylinder, "2_CourseA", new Vector3(0f, 3.92f, 0f),
                new Vector3(2.90f, 0.07f, 2.90f), Quaternion.identity,
                stoneDark, 0.05f, 0.12f, false);

            // Section B.
            Make(PrimitiveType.Cylinder, "2_ShaftB", new Vector3(0.01f, 5.25f, -0.01f),
                new Vector3(2.68f, 1.30f, 2.68f), Quaternion.Euler(Jit(0.5f), 0f, Jit(0.5f)),
                stone * 1.04f, 0.05f, 0.15f, false);
            Make(PrimitiveType.Cylinder, "2_CourseB", new Vector3(0f, 6.56f, 0f),
                new Vector3(2.62f, 0.07f, 2.62f), Quaternion.identity,
                stoneDark, 0.05f, 0.12f, false);

            // Section C (lightest — sun-bleached top of the shaft).
            Make(PrimitiveType.Cylinder, "2_ShaftC", new Vector3(-0.01f, 7.80f, 0.01f),
                new Vector3(2.44f, 1.22f, 2.44f), Quaternion.Euler(Jit(0.4f), 0f, Jit(0.4f)),
                stoneLight, 0.05f, 0.16f, false);

            // Arrow slits — dark inset quads staggered up the shaft on
            // alternating faces (thin cubes proud of the taper by a hair).
            Make(PrimitiveType.Cube, "2_Slit_S1", new Vector3(0f, 2.45f, 1.44f),
                new Vector3(0.14f, 0.72f, 0.10f), Quaternion.identity, slitDark, 0.0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_E1", new Vector3(1.44f, 3.10f, 0f),
                new Vector3(0.10f, 0.72f, 0.14f), Quaternion.identity, slitDark, 0.0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_N1", new Vector3(0f, 5.05f, -1.31f),
                new Vector3(0.14f, 0.68f, 0.10f), Quaternion.identity, slitDark, 0.0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_W1", new Vector3(-1.31f, 5.75f, 0f),
                new Vector3(0.10f, 0.68f, 0.14f), Quaternion.identity, slitDark, 0.0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_S2", new Vector3(0f, 7.75f, 1.20f),
                new Vector3(0.13f, 0.62f, 0.10f), Quaternion.identity, slitDark, 0.0f, 0.05f, false);

            // ══ 3_ CROWN — corbels, crown ring, deck, crenellated parapet ═══
            // Corbel loop: eight stepped brackets flaring out under the crown.
            for (int i = 0; i < 8; i++)
            {
                float ang = (22.5f + 45f * i) * Mathf.Deg2Rad;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                Make(PrimitiveType.Cube, $"3_Corbel_{i}",
                    new Vector3(cx * 1.32f, 8.68f, cz * 1.32f),
                    new Vector3(0.46f, 0.40f, 0.46f),
                    Quaternion.Euler(0f, -Mathf.Atan2(cz, cx) * Mathf.Rad2Deg, 0f),
                    stoneDark, 0.05f, 0.12f, false);
            }

            // Crown ring overhanging the corbels, then the walkable deck.
            Make(PrimitiveType.Cylinder, "3_CrownRing", new Vector3(0f, 9.02f, 0f),
                new Vector3(3.15f, 0.14f, 3.15f), Quaternion.identity,
                stoneDark, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cylinder, "3_Deck", new Vector3(0f, 9.24f, 0f),
                new Vector3(2.95f, 0.10f, 2.95f), Quaternion.identity,
                stone * 1.06f, 0.05f, 0.20f, false);

            // Parapet drum (low ring wall) with a merlon loop above the rim —
            // the AddDeckAndParapets crenellation pattern bent around a circle.
            Make(PrimitiveType.Cylinder, "3_Parapet", new Vector3(0f, 9.55f, 0f),
                new Vector3(3.05f, 0.26f, 3.05f), Quaternion.identity,
                stoneLight, 0.05f, 0.15f, false);
            for (int i = 0; i < 8; i++)
            {
                float ang = 45f * i * Mathf.Deg2Rad;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                Make(PrimitiveType.Cube, $"3_Merlon_{i}",
                    new Vector3(cx * 1.40f, 10.05f, cz * 1.40f),
                    new Vector3(0.55f, 0.55f, 0.42f),
                    Quaternion.Euler(0f, -Mathf.Atan2(cz, cx) * Mathf.Rad2Deg + 90f + Jit(1.5f), 0f),
                    stoneLight, 0.05f, 0.14f, false);
            }

            // ══ 4_ PROPS — lookout platform, ladder, brazier, banner ════════
            // Wooden lookout platform jutting off the east face below the crown.
            Make(PrimitiveType.Cube, "4_LookoutPlank_A", new Vector3(1.72f, 8.30f, -0.28f),
                new Vector3(1.05f, 0.08f, 0.44f), Quaternion.Euler(0f, Jit(1f), 1.2f),
                plank, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_LookoutPlank_B", new Vector3(1.74f, 8.29f, 0.22f),
                new Vector3(1.08f, 0.08f, 0.44f), Quaternion.Euler(0f, Jit(1f), 0.8f),
                plank * 0.92f, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_LookoutRail", new Vector3(2.18f, 8.72f, 0f),
                new Vector3(0.07f, 0.07f, 1.05f), Quaternion.Euler(Jit(1f), 0f, Jit(1f)),
                beam, 0.02f, 0.08f, false);
            // Angled support struts bracing the platform back into the shaft.
            Make(PrimitiveType.Cube, "4_LookoutStrut_A", new Vector3(1.62f, 7.85f, -0.28f),
                new Vector3(0.10f, 1.05f, 0.10f), Quaternion.Euler(0f, 0f, 38f),
                beam, 0.02f, 0.08f, false);
            Make(PrimitiveType.Cube, "4_LookoutStrut_B", new Vector3(1.62f, 7.85f, 0.22f),
                new Vector3(0.10f, 1.05f, 0.10f), Quaternion.Euler(0f, 0f, 38f),
                beam * 0.94f, 0.02f, 0.08f, false);

            // Ladder hints up to the lookout: two rails + rungs.
            Make(PrimitiveType.Cylinder, "4_LadderRail_A", new Vector3(1.52f, 6.65f, -0.30f),
                new Vector3(0.06f, 1.55f, 0.06f), Quaternion.Euler(0f, 0f, -7f),
                beam, 0.02f, 0.08f, false);
            Make(PrimitiveType.Cylinder, "4_LadderRail_B", new Vector3(1.52f, 6.65f, 0.24f),
                new Vector3(0.06f, 1.55f, 0.06f), Quaternion.Euler(0f, 0f, -7f),
                beam, 0.02f, 0.08f, false);
            for (int i = 0; i < 4; i++)
            {
                float ry = 5.55f + i * 0.72f;
                Make(PrimitiveType.Cylinder, $"4_LadderRung_{i}",
                    new Vector3(1.52f + (ry - 6.65f) * -0.12f, ry, -0.03f),
                    new Vector3(0.05f, 0.34f, 0.05f),
                    Quaternion.Euler(90f, 0f, 0f), plank, 0.02f, 0.08f, false);
            }

            // Brazier at the deck centre — iron stand, bowl, coal bed, flame.
            Make(PrimitiveType.Cylinder, "4_BrazierStand", new Vector3(0f, 9.62f, 0f),
                new Vector3(0.18f, 0.34f, 0.18f), Quaternion.identity,
                iron, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "4_BrazierBowl", new Vector3(0f, 10.02f, 0f),
                new Vector3(0.78f, 0.16f, 0.78f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                iron * 1.1f, 0.80f, 0.40f, false);
            Make(PrimitiveType.Sphere, "4_BrazierCoals", new Vector3(0f, 10.16f, 0f),
                new Vector3(0.62f, 0.22f, 0.62f), Quaternion.identity,
                embers, 0.0f, 0.05f, true);
            Make(PrimitiveType.Sphere, "4_BrazierFlame", new Vector3(0.03f, 10.44f, -0.02f),
                new Vector3(0.40f, 0.62f, 0.40f), Quaternion.Euler(Jit(3f), 0f, Jit(3f)),
                flame, 0.0f, 0.05f, true);

            // Faction banner hung from the parapet down the south face.
            Make(PrimitiveType.Cylinder, "4_BannerRod", new Vector3(0f, 9.62f, 1.56f),
                new Vector3(0.06f, 0.55f, 0.06f), Quaternion.Euler(90f, 90f, 0f),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_Banner", new Vector3(0f, 8.55f, 1.60f),
                new Vector3(0.78f, 2.10f, 0.05f), Quaternion.Euler(Jit(1f), 0f, Jit(1.5f)),
                Color.white, 0.0f, 0.15f, false);

            return root;
        }
    }
}
