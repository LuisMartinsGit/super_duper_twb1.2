// File: Assets/GameData/TechTree/Buildings/Alanthor/RoyalStable/RoyalStableVisual.cs
// Procedural visual for the Royal Stable (pid 356) — long timber-framed
// stable hall with a side paddock. Built entirely from primitives in the
// CreateProceduralSmelter idiom: named palette, per-part metallic/smoothness
// contrast, 1-3 degree tilts, prop vignettes, one soft emissive lantern.
// Part names carry leading rise-group numbers (1_ plinth, 2_ walls/frame,
// 3_ top structure, 4_ props) for BuildingRiseData's staggered construction.
// Player-color accents: 4_Stripe_1 / 4_Stripe_2 door banners and 4_Stripe_3
// ridge pennant (BuildingFactionColorMarker tints anything named "stripe").
// The orchestrator wires FitSelectionCollider / EntityReference / the faction
// marker after Build returns — this class only assembles geometry.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class RoyalStableVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject($"RoyalStable_{seed}");

            // Palette — warm stable timber over cool stone, straw and iron props.
            var stone      = new Color(0.46f, 0.42f, 0.36f);
            var stoneDark  = new Color(0.33f, 0.29f, 0.25f);
            var plaster    = new Color(0.71f, 0.65f, 0.54f);
            var timber     = new Color(0.34f, 0.23f, 0.14f);
            var timberDark = new Color(0.25f, 0.17f, 0.10f);
            var plankWood  = new Color(0.40f, 0.29f, 0.18f);
            var dirt       = new Color(0.36f, 0.29f, 0.21f);
            var hay        = new Color(0.78f, 0.63f, 0.24f);
            var iron       = new Color(0.18f, 0.17f, 0.16f);
            var water      = new Color(0.25f, 0.32f, 0.42f);
            var lantern    = new Color(0.95f, 0.72f, 0.35f);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(root.transform, false);
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
                    if (glow && r.material.HasProperty("_EmissionColor"))
                    {
                        r.material.EnableKeyword("_EMISSION");
                        r.material.SetColor("_EmissionColor", color * 1.6f);
                    }
                }
                var c = go.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
                return go;
            };

            // Deterministic hand-built wobble in degrees.
            System.Func<float, float> Tilt = max => (float)(rng.NextDouble() * 2.0 - 1.0) * max;

            // ── 1_ Plinth / ground ─────────────────────────────────────────
            // Hall runs along Z at x = -0.9; the paddock occupies the +X side.
            Make(PrimitiveType.Cube, "1_Plinth", new Vector3(-0.9f, 0.19f, 0f),
                new Vector3(3.6f, 0.38f, 6.2f), Quaternion.identity, stoneDark, 0.05f, 0.15f, false);
            Make(PrimitiveType.Cube, "1_PaddockGround", new Vector3(1.7f, 0.05f, 0f),
                new Vector3(2.5f, 0.10f, 6.0f), Quaternion.identity, dirt, 0.02f, 0.08f, false);
            Make(PrimitiveType.Cube, "1_Threshold", new Vector3(-0.9f, 0.42f, 2.95f),
                new Vector3(1.5f, 0.10f, 0.55f), Quaternion.identity, stone, 0.05f, 0.15f, false);

            // ── 2_ Walls / timber frame ────────────────────────────────────
            Make(PrimitiveType.Cube, "2_HallBody", new Vector3(-0.9f, 1.33f, 0f),
                new Vector3(2.9f, 1.9f, 5.5f), Quaternion.identity, plaster, 0.03f, 0.12f, false);

            // Corner posts, each with its own 1-2 degree lean.
            for (int i = 0; i < 4; i++)
            {
                float px = (i % 2 == 0) ? -2.32f : 0.52f;
                float pz = (i < 2) ? 2.72f : -2.72f;
                Make(PrimitiveType.Cube, $"2_CornerPost_{(char)('A' + i)}",
                    new Vector3(px, 1.42f, pz), new Vector3(0.22f, 2.3f, 0.22f),
                    Quaternion.Euler(Tilt(1.8f), 0f, Tilt(1.8f)), timber, 0.05f, 0.10f, false);
            }

            // Top plates across the gable ends, sill beams down the long sides.
            Make(PrimitiveType.Cube, "2_TopBeamFront", new Vector3(-0.9f, 2.36f, 2.74f),
                new Vector3(3.15f, 0.16f, 0.16f), Quaternion.identity, timberDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_TopBeamBack", new Vector3(-0.9f, 2.36f, -2.74f),
                new Vector3(3.15f, 0.16f, 0.16f), Quaternion.identity, timberDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_SillBeamW", new Vector3(-2.34f, 0.48f, 0f),
                new Vector3(0.14f, 0.14f, 5.6f), Quaternion.identity, timberDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_SillBeamE", new Vector3(0.54f, 0.48f, 0f),
                new Vector3(0.14f, 0.14f, 5.6f), Quaternion.identity, timberDark, 0.05f, 0.10f, false);

            // X-bracing on the west long wall — two crossed pairs.
            for (int i = 0; i < 2; i++)
            {
                float bz = (i == 0) ? 1.45f : -1.45f;
                Make(PrimitiveType.Cube, $"2_BraceX_{(char)('A' + i * 2)}",
                    new Vector3(-2.38f, 1.35f, bz), new Vector3(0.10f, 1.75f, 0.12f),
                    Quaternion.Euler(38f + Tilt(1.5f), 0f, 0f), timber, 0.05f, 0.10f, false);
                Make(PrimitiveType.Cube, $"2_BraceX_{(char)('B' + i * 2)}",
                    new Vector3(-2.38f, 1.35f, bz), new Vector3(0.10f, 1.75f, 0.12f),
                    Quaternion.Euler(-38f + Tilt(1.5f), 0f, 0f), timber, 0.05f, 0.10f, false);
            }

            // Main door on the front gable (faces +Z) with lintel above.
            Make(PrimitiveType.Cube, "2_MainDoor", new Vector3(-0.9f, 1.02f, 2.78f),
                new Vector3(1.25f, 1.65f, 0.09f), Quaternion.identity, timberDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "2_Lintel", new Vector3(-0.9f, 1.95f, 2.80f),
                new Vector3(1.55f, 0.16f, 0.14f), Quaternion.identity, timber, 0.05f, 0.10f, false);

            // Three stall doors on the east wall, opening onto the paddock.
            // The middle one hangs ajar.
            for (int i = 0; i < 3; i++)
            {
                float dz = -1.75f + i * 1.75f;
                bool ajar = (i == 1);
                var pos = ajar ? new Vector3(0.76f, 0.95f, dz + 0.22f) : new Vector3(0.58f, 0.95f, dz);
                var rot = ajar ? Quaternion.Euler(0f, 32f, 0f) : Quaternion.Euler(0f, Tilt(1.2f), 0f);
                Make(PrimitiveType.Cube, $"2_StallDoor_{(char)('A' + i)}", pos,
                    new Vector3(0.08f, 1.45f, 1.00f), rot, timberDark, 0.05f, 0.12f, false);
                Make(PrimitiveType.Cube, $"2_StallFrame_{(char)('A' + i)}",
                    new Vector3(0.58f, 1.75f, dz), new Vector3(0.12f, 0.14f, 1.18f),
                    Quaternion.identity, timber, 0.05f, 0.10f, false);
            }

            // ── 3_ Pitched planked top + gables ────────────────────────────
            // Individually laid plank slabs, four per side, ridge along Z.
            // ("Plank"/"Ridge" names deliberately avoid the "roof" substring so
            // the faction marker never solid-paints them.)
            for (int side = 0; side < 2; side++)
            {
                float sx = (side == 0) ? -1.68f : -0.12f;
                float baseTilt = (side == 0) ? 22f : -22f;
                char sideChar = (side == 0) ? 'W' : 'E';
                for (int i = 0; i < 4; i++)
                {
                    float pz = -2.12f + i * 1.42f;
                    Make(PrimitiveType.Cube, $"3_Plank{sideChar}_{(char)('A' + i)}",
                        new Vector3(sx, 2.86f + Tilt(0.035f), pz),
                        new Vector3(1.95f, 0.09f, 1.40f),
                        Quaternion.Euler(0f, Tilt(0.8f), baseTilt + Tilt(1.4f)),
                        plankWood, 0.04f, 0.16f, false);
                }
            }
            Make(PrimitiveType.Cube, "3_RidgeBeam", new Vector3(-0.9f, 3.30f, 0f),
                new Vector3(0.26f, 0.15f, 5.9f), Quaternion.identity, timberDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_GableFront", new Vector3(-0.9f, 2.62f, 2.70f),
                new Vector3(2.55f, 0.75f, 0.12f), Quaternion.identity, plaster, 0.03f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_GableBack", new Vector3(-0.9f, 2.62f, -2.70f),
                new Vector3(2.55f, 0.75f, 0.12f), Quaternion.identity, plaster, 0.03f, 0.12f, false);

            // ── 4_ Props / accents ─────────────────────────────────────────
            // Two horse-head silhouettes flanking the main door, stacked from
            // a leaning neck, a skull block and a muzzle.
            for (int i = 0; i < 2; i++)
            {
                float hx = (i == 0) ? -1.62f : -0.18f;
                float lean = (i == 0) ? -16f : 16f;   // necks lean outward
                char hc = (i == 0) ? 'A' : 'B';
                Make(PrimitiveType.Cube, $"4_HorseNeck_{hc}",
                    new Vector3(hx, 2.42f, 2.86f), new Vector3(0.16f, 0.52f, 0.20f),
                    Quaternion.Euler(0f, 0f, lean), timberDark, 0.05f, 0.20f, false);
                Make(PrimitiveType.Cube, $"4_HorseSkull_{hc}",
                    new Vector3(hx + (i == 0 ? -0.10f : 0.10f), 2.70f, 2.95f),
                    new Vector3(0.17f, 0.20f, 0.34f),
                    Quaternion.Euler(-18f, 0f, lean * 0.4f), timberDark, 0.05f, 0.20f, false);
                Make(PrimitiveType.Cube, $"4_HorseMuzzle_{hc}",
                    new Vector3(hx + (i == 0 ? -0.14f : 0.14f), 2.62f, 3.10f),
                    new Vector3(0.12f, 0.13f, 0.18f),
                    Quaternion.Euler(-18f, 0f, lean * 0.4f), timberDark * 0.85f, 0.05f, 0.20f, false);
            }

            // Faction-color door banners (tinted by the faction marker).
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(-1.90f, 1.45f, 2.83f),
                new Vector3(0.34f, 1.15f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.5f)),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(0.10f, 1.45f, 2.83f),
                new Vector3(0.34f, 1.15f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.5f)),
                Color.white, 0.02f, 0.10f, false);

            // Ridge pennant on a short pole above the front gable.
            Make(PrimitiveType.Cylinder, "4_PennantPole", new Vector3(-0.9f, 3.72f, 2.55f),
                new Vector3(0.045f, 0.45f, 0.045f), Quaternion.Euler(0f, 0f, Tilt(1.5f)),
                timberDark, 0.10f, 0.20f, false);
            Make(PrimitiveType.Cube, "4_Stripe_3", new Vector3(-0.62f, 4.02f, 2.55f),
                new Vector3(0.52f, 0.17f, 0.035f), Quaternion.Euler(0f, Tilt(2.5f), 0f),
                Color.white, 0.02f, 0.10f, false);

            // Warm lantern by the main door — the single emissive accent.
            Make(PrimitiveType.Cube, "4_LanternHook", new Vector3(-1.62f, 2.02f, 2.88f),
                new Vector3(0.05f, 0.05f, 0.22f), Quaternion.identity, iron, 0.70f, 0.40f, false);
            Make(PrimitiveType.Cube, "4_Lantern", new Vector3(-1.62f, 1.86f, 3.00f),
                new Vector3(0.13f, 0.18f, 0.13f), Quaternion.Euler(0f, Tilt(6f), 0f),
                lantern, 0.05f, 0.15f, true);

            // Paddock fence — six posts with individual leans, doubled rails.
            var postPos = new[]
            {
                new Vector3(2.85f, 0.48f, -2.80f),
                new Vector3(2.85f, 0.48f, -0.93f),
                new Vector3(2.85f, 0.48f,  0.93f),
                new Vector3(2.85f, 0.48f,  2.80f),
                new Vector3(1.68f, 0.48f,  2.80f),
                new Vector3(1.68f, 0.48f, -2.80f),
            };
            for (int i = 0; i < postPos.Length; i++)
            {
                Make(PrimitiveType.Cube, $"4_FencePost_{(char)('A' + i)}", postPos[i],
                    new Vector3(0.13f, 0.95f, 0.13f),
                    Quaternion.Euler(Tilt(2.5f), 0f, Tilt(2.5f)), timber, 0.05f, 0.10f, false);
            }
            // East run (along Z) — two rail heights.
            Make(PrimitiveType.Cylinder, "4_FenceRailE_A", new Vector3(2.85f, 0.55f, 0f),
                new Vector3(0.06f, 2.80f, 0.06f), Quaternion.Euler(90f, 0f, 0f), plankWood, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "4_FenceRailE_B", new Vector3(2.85f, 0.84f, 0f),
                new Vector3(0.06f, 2.80f, 0.06f), Quaternion.Euler(90f + Tilt(0.8f), 0f, 0f), plankWood, 0.05f, 0.12f, false);
            // North + south short runs (along X, hall to the corner posts).
            Make(PrimitiveType.Cylinder, "4_FenceRailN_A", new Vector3(1.72f, 0.55f, 2.80f),
                new Vector3(0.06f, 1.13f, 0.06f), Quaternion.Euler(0f, 0f, 90f), plankWood, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "4_FenceRailN_B", new Vector3(1.72f, 0.84f, 2.80f),
                new Vector3(0.06f, 1.13f, 0.06f), Quaternion.Euler(0f, 0f, 90f + Tilt(0.8f)), plankWood, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "4_FenceRailS_A", new Vector3(1.72f, 0.55f, -2.80f),
                new Vector3(0.06f, 1.13f, 0.06f), Quaternion.Euler(0f, 0f, 90f), plankWood, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "4_FenceRailS_B", new Vector3(1.72f, 0.84f, -2.80f),
                new Vector3(0.06f, 1.13f, 0.06f), Quaternion.Euler(0f, 0f, 90f + Tilt(0.8f)), plankWood, 0.05f, 0.12f, false);

            // Hay bales — flattened cylinders lying on their sides in the paddock.
            Make(PrimitiveType.Cylinder, "4_HayBale_A", new Vector3(1.95f, 0.30f, -1.25f),
                new Vector3(0.52f, 0.40f, 0.52f), Quaternion.Euler(0f, Tilt(12f), 90f), hay, 0.02f, 0.06f, false);
            Make(PrimitiveType.Cylinder, "4_HayBale_B", new Vector3(2.25f, 0.27f, -0.55f),
                new Vector3(0.46f, 0.36f, 0.46f), Quaternion.Euler(0f, 28f + Tilt(6f), 90f), hay * 0.92f, 0.02f, 0.06f, false);

            // Water trough against the east wall with a still-water surface.
            Make(PrimitiveType.Cube, "4_TroughBody", new Vector3(1.00f, 0.28f, 0.95f),
                new Vector3(0.38f, 0.40f, 1.25f), Quaternion.Euler(0f, Tilt(1.5f), 0f), timberDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_TroughWater", new Vector3(1.00f, 0.44f, 0.95f),
                new Vector3(0.29f, 0.03f, 1.14f), Quaternion.identity, water, 0.10f, 0.85f, false);

            // Hitching rail out front — two posts and a crossbar.
            Make(PrimitiveType.Cylinder, "4_HitchPost_A", new Vector3(0.15f, 0.45f, 3.35f),
                new Vector3(0.09f, 0.48f, 0.09f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)), timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "4_HitchPost_B", new Vector3(1.05f, 0.45f, 3.35f),
                new Vector3(0.09f, 0.48f, 0.09f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)), timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "4_HitchRail", new Vector3(0.60f, 0.88f, 3.35f),
                new Vector3(0.06f, 0.55f, 0.06f), Quaternion.Euler(0f, 0f, 90f + Tilt(1f)), plankWood, 0.05f, 0.12f, false);

            return root;
        }
    }
}
