// File: Assets/GameData/TechTree/Buildings/Alanthor/SiegeYard/SiegeYardVisual.cs
// Procedural visual for the Siege Yard (pid 357) — an open engineering
// work-yard: plank platform, timber gantry crane with rope and hook, a
// half-built ram frame, bolt rack, grindstone, log pile, anvil corner, tool
// wall, and a small ember-glow forge (the single emissive accent). Built in
// the CreateProceduralSmelter idiom: named palette, per-part metallic and
// smoothness contrast, deterministic 1-3 degree tilts. Part names carry
// leading rise-group numbers (1_ platform, 2_ gantry, 3_ workpieces,
// 4_ flags) for BuildingRiseData's staggered construction rise.
// Player-color accents: 4_Stripe_1 / 4_Stripe_2 flags on the gantry posts
// (BuildingFactionColorMarker tints anything named "stripe").
// The orchestrator wires FitSelectionCollider / EntityReference / the faction
// marker after Build returns — this class only assembles geometry.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class SiegeYardVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject($"SiegeYard_{seed}");

            // Palette — sun-bleached deck planks, darker structural timber,
            // raw fresh-cut workpieces, iron fittings, hemp rope, hot embers.
            var deckWood   = new Color(0.47f, 0.36f, 0.24f);
            var timber     = new Color(0.33f, 0.23f, 0.14f);
            var timberDark = new Color(0.24f, 0.16f, 0.10f);
            var freshWood  = new Color(0.58f, 0.44f, 0.27f);
            var bark       = new Color(0.30f, 0.22f, 0.15f);
            var iron       = new Color(0.18f, 0.17f, 0.16f);
            var ironLight  = new Color(0.38f, 0.38f, 0.40f);
            var rope       = new Color(0.56f, 0.47f, 0.31f);
            var stoneDark  = new Color(0.32f, 0.28f, 0.24f);
            var grindstone = new Color(0.52f, 0.48f, 0.44f);
            var embers     = new Color(0.95f, 0.45f, 0.10f);

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

            // ── 1_ Plank platform ──────────────────────────────────────────
            // Three broad deck runs with slight height jitter, edge beams
            // binding the north and south rims.
            for (int i = 0; i < 3; i++)
            {
                float dx = -1.8f + i * 1.8f;
                Make(PrimitiveType.Cube, $"1_Deck_{(char)('A' + i)}",
                    new Vector3(dx, 0.10f + Tilt(0.018f), 0f),
                    new Vector3(1.74f, 0.16f, 5.4f),
                    Quaternion.Euler(0f, Tilt(0.5f), 0f),
                    deckWood * (0.94f + 0.06f * i), 0.03f, 0.12f, false);
            }
            Make(PrimitiveType.Cube, "1_EdgeBeamN", new Vector3(0f, 0.13f, 2.74f),
                new Vector3(5.55f, 0.18f, 0.20f), Quaternion.identity, timberDark, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cube, "1_EdgeBeamS", new Vector3(0f, 0.13f, -2.74f),
                new Vector3(5.55f, 0.18f, 0.20f), Quaternion.identity, timberDark, 0.04f, 0.10f, false);

            // ── 2_ Gantry crane ────────────────────────────────────────────
            // Two heavy posts on the west edge, a crossbeam between them,
            // diagonal braces, a jib arm reaching over the yard, a stay rope
            // from the kingpost, and the lifting rope + iron hook.
            Make(PrimitiveType.Cube, "2_GantryPost_A", new Vector3(-1.95f, 1.85f, -1.6f),
                new Vector3(0.30f, 3.45f, 0.30f), Quaternion.Euler(Tilt(1.4f), 0f, Tilt(1.2f)),
                timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_GantryPost_B", new Vector3(-1.95f, 1.85f, 1.6f),
                new Vector3(0.30f, 3.45f, 0.30f), Quaternion.Euler(Tilt(1.4f), 0f, Tilt(1.2f)),
                timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_GantryCross", new Vector3(-1.95f, 3.55f, 0f),
                new Vector3(0.26f, 0.26f, 3.65f), Quaternion.identity, timberDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_GantryBrace_A", new Vector3(-1.95f, 3.05f, -1.12f),
                new Vector3(0.14f, 1.10f, 0.14f), Quaternion.Euler(38f + Tilt(1.5f), 0f, 0f),
                timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_GantryBrace_B", new Vector3(-1.95f, 3.05f, 1.12f),
                new Vector3(0.14f, 1.10f, 0.14f), Quaternion.Euler(-38f + Tilt(1.5f), 0f, 0f),
                timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_Jib", new Vector3(-0.72f, 3.60f, 0f),
                new Vector3(2.65f, 0.20f, 0.20f), Quaternion.Euler(0f, 0f, -2.5f + Tilt(0.8f)),
                timberDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_JibKing", new Vector3(-1.95f, 3.92f, 0f),
                new Vector3(0.12f, 0.45f, 0.12f), Quaternion.identity, timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "2_JibStay", new Vector3(-0.80f, 3.90f, 0f),
                new Vector3(0.035f, 1.22f, 0.035f), Quaternion.Euler(0f, 0f, 77f),
                rope, 0.02f, 0.05f, false);
            Make(PrimitiveType.Cylinder, "2_CraneRope", new Vector3(0.48f, 2.60f, 0f),
                new Vector3(0.03f, 0.88f, 0.03f), Quaternion.Euler(Tilt(1.2f), 0f, Tilt(1.2f)),
                rope, 0.02f, 0.05f, false);
            Make(PrimitiveType.Cylinder, "2_CraneHook", new Vector3(0.48f, 1.62f, 0f),
                new Vector3(0.16f, 0.045f, 0.16f), Quaternion.Euler(90f, Tilt(15f), 0f),
                ironLight, 0.85f, 0.45f, false);

            // ── 3_ Workpieces / prop vignettes ─────────────────────────────
            // Half-built ram frame: four corner posts (one leaning off-true),
            // a single fitted top rail, and a second rail still leaning
            // against the frame waiting to be raised.
            var ramC = new Vector3(1.35f, 0f, -1.15f);
            for (int i = 0; i < 4; i++)
            {
                float px = ramC.x + ((i % 2 == 0) ? -0.52f : 0.52f);
                float pz = ramC.z + ((i < 2) ? -0.38f : 0.38f);
                float lean = (i == 3) ? 4.5f : Tilt(1.5f);
                Make(PrimitiveType.Cube, $"3_RamPost_{(char)('A' + i)}",
                    new Vector3(px, 0.70f, pz), new Vector3(0.15f, 1.05f, 0.15f),
                    Quaternion.Euler(Tilt(1.5f), 0f, lean), freshWood, 0.04f, 0.12f, false);
            }
            Make(PrimitiveType.Cube, "3_RamRail", new Vector3(ramC.x, 1.24f, ramC.z - 0.38f),
                new Vector3(1.20f, 0.13f, 0.13f), Quaternion.Euler(0f, 0f, Tilt(1f)),
                freshWood, 0.04f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_RamLean", new Vector3(ramC.x + 0.78f, 0.62f, ramC.z + 0.15f),
                new Vector3(0.13f, 1.30f, 0.13f), Quaternion.Euler(Tilt(2f), 0f, -35f),
                freshWood * 0.94f, 0.04f, 0.12f, false);
            // The ram log itself, iron-capped, waiting beside the frame.
            Make(PrimitiveType.Cylinder, "3_RamLog", new Vector3(1.30f, 0.32f, -2.15f),
                new Vector3(0.20f, 0.95f, 0.20f), Quaternion.Euler(0f, Tilt(3f), 90f),
                bark, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cube, "3_RamCap", new Vector3(2.28f, 0.32f, -2.15f),
                new Vector3(0.26f, 0.26f, 0.26f), Quaternion.Euler(0f, Tilt(3f), 0f),
                iron, 0.85f, 0.50f, false);

            // Bolt rack: crossbar on two legs, four ballista bolts leaning.
            Make(PrimitiveType.Cube, "3_RackLeg_A", new Vector3(-1.55f, 0.55f, 2.30f),
                new Vector3(0.10f, 0.80f, 0.10f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)),
                timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "3_RackLeg_B", new Vector3(-0.35f, 0.55f, 2.30f),
                new Vector3(0.10f, 0.80f, 0.10f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)),
                timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "3_RackBar", new Vector3(-0.95f, 0.95f, 2.30f),
                new Vector3(1.45f, 0.10f, 0.10f), Quaternion.Euler(0f, 0f, Tilt(1f)),
                timberDark, 0.05f, 0.10f, false);
            for (int i = 0; i < 4; i++)
            {
                float bx = -1.42f + i * 0.32f;
                Make(PrimitiveType.Cylinder, $"3_Bolt_{(char)('A' + i)}",
                    new Vector3(bx, 0.62f, 2.22f), new Vector3(0.035f, 0.60f, 0.035f),
                    Quaternion.Euler(14f + Tilt(3f), 0f, Tilt(4f)),
                    freshWood, 0.04f, 0.15f, false);
            }

            // Grindstone: upright wheel on an axle between two side supports,
            // with a small crank block.
            Make(PrimitiveType.Cylinder, "3_GrindWheel", new Vector3(2.05f, 0.58f, 0.62f),
                new Vector3(0.58f, 0.07f, 0.58f), Quaternion.Euler(0f, Tilt(3f), 90f),
                grindstone, 0.08f, 0.22f, false);
            Make(PrimitiveType.Cube, "3_GrindFrame_A", new Vector3(2.05f, 0.32f, 0.40f),
                new Vector3(0.09f, 0.52f, 0.30f), Quaternion.identity, timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "3_GrindFrame_B", new Vector3(2.05f, 0.32f, 0.84f),
                new Vector3(0.09f, 0.52f, 0.30f), Quaternion.identity, timber, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "3_GrindAxle", new Vector3(2.05f, 0.58f, 0.62f),
                new Vector3(0.035f, 0.30f, 0.035f), Quaternion.Euler(90f, 0f, 0f),
                iron, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cube, "3_GrindCrank", new Vector3(2.13f, 0.66f, 0.92f),
                new Vector3(0.06f, 0.18f, 0.06f), Quaternion.Euler(Tilt(4f), 0f, 22f),
                iron, 0.80f, 0.45f, false);

            // Log pile: two on the deck, one nested on top, one askew.
            Make(PrimitiveType.Cylinder, "3_Log_A", new Vector3(-0.25f, 0.34f, -2.20f),
                new Vector3(0.17f, 0.82f, 0.17f), Quaternion.Euler(0f, Tilt(2f), 90f),
                bark, 0.04f, 0.08f, false);
            Make(PrimitiveType.Cylinder, "3_Log_B", new Vector3(-0.25f, 0.34f, -1.86f),
                new Vector3(0.17f, 0.82f, 0.17f), Quaternion.Euler(0f, Tilt(2f), 90f),
                bark * 0.92f, 0.04f, 0.08f, false);
            Make(PrimitiveType.Cylinder, "3_Log_C", new Vector3(-0.25f, 0.62f, -2.03f),
                new Vector3(0.16f, 0.78f, 0.16f), Quaternion.Euler(0f, Tilt(2f), 90f),
                bark * 1.06f, 0.04f, 0.08f, false);
            Make(PrimitiveType.Cylinder, "3_Log_D", new Vector3(-0.55f, 0.30f, -1.55f),
                new Vector3(0.14f, 0.70f, 0.14f), Quaternion.Euler(0f, 24f + Tilt(4f), 90f),
                bark * 0.85f, 0.04f, 0.08f, false);

            // Anvil corner: stump, iron body, horn.
            Make(PrimitiveType.Cylinder, "3_AnvilStump", new Vector3(2.15f, 0.42f, 2.15f),
                new Vector3(0.44f, 0.34f, 0.44f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                timberDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "3_AnvilBody", new Vector3(2.15f, 0.86f, 2.15f),
                new Vector3(0.62f, 0.24f, 0.30f), Quaternion.Euler(0f, Tilt(4f), 0f),
                iron, 0.85f, 0.50f, false);
            Make(PrimitiveType.Cube, "3_AnvilHorn", new Vector3(2.48f, 0.86f, 2.15f),
                new Vector3(0.32f, 0.16f, 0.22f), Quaternion.Euler(0f, Tilt(4f), 0f),
                iron, 0.85f, 0.50f, false);

            // Tool wall: a plank board on the north edge hung with two
            // hammers and a saw blade.
            Make(PrimitiveType.Cube, "3_ToolBoard", new Vector3(0.85f, 0.88f, 2.66f),
                new Vector3(1.65f, 1.05f, 0.08f), Quaternion.Euler(0f, 0f, Tilt(0.8f)),
                deckWood * 0.9f, 0.03f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "3_ToolHaft_A", new Vector3(0.42f, 0.92f, 2.60f),
                new Vector3(0.035f, 0.30f, 0.035f), Quaternion.Euler(0f, 0f, 8f + Tilt(2f)),
                timber, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_ToolHead_A", new Vector3(0.44f, 1.24f, 2.60f),
                new Vector3(0.16f, 0.10f, 0.10f), Quaternion.Euler(0f, 0f, 8f),
                iron, 0.85f, 0.50f, false);
            Make(PrimitiveType.Cylinder, "3_ToolHaft_B", new Vector3(0.95f, 0.90f, 2.60f),
                new Vector3(0.035f, 0.26f, 0.035f), Quaternion.Euler(0f, 0f, -6f + Tilt(2f)),
                timber, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_ToolHead_B", new Vector3(0.93f, 1.18f, 2.60f),
                new Vector3(0.14f, 0.09f, 0.09f), Quaternion.Euler(0f, 0f, -6f),
                ironLight, 0.85f, 0.45f, false);
            Make(PrimitiveType.Cube, "3_ToolSaw", new Vector3(1.42f, 1.02f, 2.60f),
                new Vector3(0.50f, 0.13f, 0.02f), Quaternion.Euler(0f, 0f, -14f + Tilt(3f)),
                ironLight, 0.80f, 0.40f, false);

            // Small forge: stone base, darker rim, ember bed (the single
            // emissive accent), and a poker leaning against the rim.
            Make(PrimitiveType.Cube, "3_ForgeBase", new Vector3(-1.05f, 0.30f, -0.85f),
                new Vector3(0.72f, 0.42f, 0.72f), Quaternion.Euler(0f, Tilt(3f), 0f),
                stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_ForgeRim", new Vector3(-1.05f, 0.54f, -0.85f),
                new Vector3(0.82f, 0.10f, 0.82f), Quaternion.Euler(0f, Tilt(3f), 0f),
                stoneDark * 0.8f, 0.06f, 0.14f, false);
            Make(PrimitiveType.Cylinder, "3_ForgeCoals", new Vector3(-1.05f, 0.60f, -0.85f),
                new Vector3(0.52f, 0.035f, 0.52f), Quaternion.identity,
                embers, 0.0f, 0.05f, true);
            Make(PrimitiveType.Cylinder, "3_ForgePoker", new Vector3(-0.62f, 0.50f, -0.72f),
                new Vector3(0.025f, 0.42f, 0.025f), Quaternion.Euler(Tilt(3f), 0f, -32f),
                iron, 0.80f, 0.40f, false);

            // ── 4_ Gantry flags (faction-color accents) ────────────────────
            Make(PrimitiveType.Cylinder, "4_FlagPole_A", new Vector3(-1.95f, 3.85f, -1.6f),
                new Vector3(0.04f, 0.38f, 0.04f), Quaternion.Euler(0f, 0f, Tilt(2f)),
                timberDark, 0.08f, 0.15f, false);
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(-1.68f, 4.10f, -1.6f),
                new Vector3(0.48f, 0.16f, 0.035f), Quaternion.Euler(0f, Tilt(3f), 0f),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "4_FlagPole_B", new Vector3(-1.95f, 3.85f, 1.6f),
                new Vector3(0.04f, 0.38f, 0.04f), Quaternion.Euler(0f, 0f, Tilt(2f)),
                timberDark, 0.08f, 0.15f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(-1.68f, 4.10f, 1.6f),
                new Vector3(0.48f, 0.16f, 0.035f), Quaternion.Euler(0f, Tilt(3f), 0f),
                Color.white, 0.02f, 0.10f, false);

            return root;
        }
    }
}
