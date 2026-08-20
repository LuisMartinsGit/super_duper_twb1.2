// File: Assets/GameData/TechTree/Buildings/Sects/MendingHall/MendingHallVisual.cs
// Procedural visual for the Mending Hall — the Sect of Renewal's open-sided
// infirmary (the menders). Built entirely from primitives in the
// CreateProceduralSmelter idiom: named palette, per-part metallic/smoothness
// contrast, 1-3 degree tilts, prop vignettes, one soft emissive lamp.
// Silhouette: a low broad stone platform, an open timber colonnade down both
// long sides (knee walls only, so the wounded can be carried straight in), a
// solid back wall, and a slack canvas awning slung over the middle on a ridge
// pole. Cots, a stretcher and a water basin fill the aisle.
// Part names carry leading rise-group numbers (1_ platform, 2_ colonnade,
// 3_ awning, 4_ props) for BuildingRiseData's staggered construction.
// Player-color accents: 4_Stripe_1 / 4_Stripe_2 eave valances and 4_Stripe_3
// entrance pennant (BuildingFactionColorMarker tints anything named "stripe").
// "Awning"/"Canvas"/"Ridge" names deliberately avoid the "roof" substring so
// the faction marker never solid-paints the cloth.
// The orchestrator wires FitSelectionCollider / EntityReference / the faction
// marker after Build returns — this class only assembles geometry.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class MendingHallVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject($"MendingHall_{seed}");

            // Palette — warm bleached timber, off-white cloth, cool stone base.
            var stoneBase   = new Color(0.58f, 0.55f, 0.48f);
            var stoneDark   = new Color(0.40f, 0.37f, 0.32f);
            var gravel      = new Color(0.52f, 0.49f, 0.43f);
            var timber      = new Color(0.66f, 0.55f, 0.40f);
            var timberDark  = new Color(0.43f, 0.34f, 0.23f);
            var cloth       = new Color(0.90f, 0.88f, 0.82f);
            var clothShade  = new Color(0.79f, 0.76f, 0.69f);
            var linen       = new Color(0.94f, 0.92f, 0.87f);
            var water       = new Color(0.36f, 0.50f, 0.55f);
            var copper      = new Color(0.64f, 0.43f, 0.24f);
            var herb        = new Color(0.41f, 0.51f, 0.28f);
            var lamp        = new Color(0.98f, 0.84f, 0.55f);

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
                    if (r.material.HasProperty("_BaseColor"))  r.material.SetColor("_BaseColor", color);
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

            // ── 1_ Platform / ground ───────────────────────────────────────
            Make(PrimitiveType.Cube, "1_Platform", new Vector3(0f, 0.22f, 0f),
                new Vector3(5.2f, 0.44f, 6.2f), Quaternion.Euler(0f, Tilt(0.5f), 0f),
                stoneBase, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "1_PlatformKerb", new Vector3(0f, 0.40f, 0f),
                new Vector3(5.5f, 0.12f, 6.5f), Quaternion.identity,
                stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "1_Ramp", new Vector3(0f, 0.24f, 3.42f),
                new Vector3(2.45f, 0.16f, 0.95f), Quaternion.Euler(-13f, Tilt(0.8f), 0f),
                stoneBase * 1.05f, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "1_GravelApron", new Vector3(0f, 0.04f, 4.25f),
                new Vector3(2.6f, 0.08f, 1.5f), Quaternion.Euler(0f, Tilt(1.5f), 0f),
                gravel, 0.02f, 0.07f, false);
            for (int i = 0; i < 4; i++)
            {
                float fx = (i % 2 == 0) ? -2.40f : 2.40f;
                float fz = (i < 2) ? 2.85f : -2.85f;
                Make(PrimitiveType.Cube, $"1_FootPad_{i}",
                    new Vector3(fx, 0.10f + Tilt(0.02f), fz),
                    new Vector3(0.70f + Tilt(0.07f), 0.22f, 0.68f + Tilt(0.07f)),
                    Quaternion.Euler(Tilt(1.5f), (float)(rng.NextDouble() * 90.0), Tilt(1.5f)),
                    stoneDark * 0.94f, 0.05f, 0.10f, false);
            }

            // ── 2_ Colonnade / frame ───────────────────────────────────────
            // Four columns per long side; nothing solid between them but a
            // knee wall, so the hall reads as open on both flanks.
            float[] colZ = { -2.20f, -0.73f, 0.73f, 2.20f };
            for (int side = 0; side < 2; side++)
            {
                float sx = (side == 0) ? -2.15f : 2.15f;
                char sc = (side == 0) ? 'W' : 'E';
                for (int i = 0; i < colZ.Length; i++)
                {
                    char cc = (char)('A' + i);
                    Make(PrimitiveType.Cube, $"2_ColumnBase{sc}_{cc}",
                        new Vector3(sx, 0.50f, colZ[i]), new Vector3(0.40f, 0.14f, 0.40f),
                        Quaternion.Euler(0f, Tilt(1.5f), 0f), stoneDark, 0.05f, 0.13f, false);
                    Make(PrimitiveType.Cylinder, $"2_Column{sc}_{cc}",
                        new Vector3(sx, 1.62f, colZ[i]), new Vector3(0.26f, 1.05f, 0.26f),
                        Quaternion.Euler(Tilt(1.2f), 0f, Tilt(1.2f)), timber, 0.04f, 0.12f, false);
                    Make(PrimitiveType.Cube, $"2_ColumnCap{sc}_{cc}",
                        new Vector3(sx, 2.74f, colZ[i]), new Vector3(0.40f, 0.15f, 0.40f),
                        Quaternion.Euler(0f, Tilt(1.5f), 0f), timberDark, 0.04f, 0.12f, false);
                }
                // Knee wall and top plate running the length of the side.
                Make(PrimitiveType.Cube, $"2_KneeWall{sc}", new Vector3(sx, 0.78f, 0f),
                    new Vector3(0.20f, 0.44f, 5.3f), Quaternion.identity,
                    stoneBase * 0.96f, 0.05f, 0.13f, false);
                Make(PrimitiveType.Cube, $"2_TopPlate{sc}", new Vector3(sx, 2.86f, 0f),
                    new Vector3(0.24f, 0.18f, 5.3f), Quaternion.identity,
                    timberDark, 0.04f, 0.12f, false);
                // Two knee braces per side, springing from the outer columns.
                for (int i = 0; i < 2; i++)
                {
                    float bz = (i == 0) ? -1.85f : 1.85f;
                    Make(PrimitiveType.Cube, $"2_Brace{sc}_{(char)('A' + i)}",
                        new Vector3(sx, 2.42f, bz), new Vector3(0.10f, 0.72f, 0.10f),
                        Quaternion.Euler((i == 0) ? -38f : 38f, 0f, Tilt(1f)),
                        timber * 0.94f, 0.04f, 0.10f, false);
                }
            }

            // Solid back wall (-Z) with two high vent slots; open front (+Z)
            // carries only a header beam between the front columns.
            Make(PrimitiveType.Cube, "2_BackWall", new Vector3(0f, 1.62f, -2.86f),
                new Vector3(4.5f, 2.30f, 0.28f), Quaternion.identity,
                stoneBase, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "2_BackWallCap", new Vector3(0f, 2.84f, -2.86f),
                new Vector3(4.7f, 0.16f, 0.38f), Quaternion.identity,
                stoneDark, 0.05f, 0.13f, false);
            for (int i = 0; i < 2; i++)
            {
                float vx = (i == 0) ? -1.15f : 1.15f;
                Make(PrimitiveType.Cube, $"2_BackVent_{(char)('A' + i)}",
                    new Vector3(vx, 2.30f, -2.72f), new Vector3(0.70f, 0.30f, 0.10f),
                    Quaternion.identity, timberDark * 0.7f, 0.0f, 0.06f, false);
            }
            Make(PrimitiveType.Cube, "2_FrontHeader", new Vector3(0f, 2.84f, 2.20f),
                new Vector3(4.6f, 0.20f, 0.22f), Quaternion.Euler(0f, 0f, Tilt(0.5f)),
                timberDark, 0.04f, 0.12f, false);
            Make(PrimitiveType.Cube, "2_FrontTieBeam", new Vector3(0f, 2.52f, 2.20f),
                new Vector3(4.3f, 0.12f, 0.14f), Quaternion.Euler(0f, 0f, Tilt(0.6f)),
                timber, 0.04f, 0.10f, false);

            // ── 3_ Canvas awning ───────────────────────────────────────────
            // Ridge pole along Z, purlins, then four slack cloth panels per
            // slope — each panel carries its own sag and sway.
            Make(PrimitiveType.Cylinder, "3_RidgePole", new Vector3(0f, 4.02f, 0f),
                new Vector3(0.15f, 3.05f, 0.15f), Quaternion.Euler(90f, 0f, 0f),
                timberDark, 0.04f, 0.12f, false);
            for (int side = 0; side < 2; side++)
            {
                float sx = (side == 0) ? -1.08f : 1.08f;
                float baseTilt = (side == 0) ? 28f : -28f;
                char sc = (side == 0) ? 'W' : 'E';
                for (int i = 0; i < 4; i++)
                {
                    float pz = -2.10f + i * 1.40f;
                    var tone = (i % 2 == 0) ? cloth : clothShade;
                    Make(PrimitiveType.Cube, $"3_Canvas{sc}_{(char)('A' + i)}",
                        new Vector3(sx, 3.42f + Tilt(0.04f), pz),
                        new Vector3(2.45f, 0.07f, 1.36f),
                        Quaternion.Euler(0f, Tilt(1.0f), baseTilt + Tilt(2.2f)),
                        tone, 0.02f, 0.08f, false);
                }
                Make(PrimitiveType.Cylinder, $"3_Purlin{sc}", new Vector3(sx, 3.34f, 0f),
                    new Vector3(0.09f, 2.85f, 0.09f), Quaternion.Euler(90f, 0f, 0f),
                    timber, 0.04f, 0.10f, false);
            }
            Make(PrimitiveType.Cube, "3_RidgeCap", new Vector3(0f, 4.16f, 0f),
                new Vector3(0.42f, 0.08f, 5.7f), Quaternion.Euler(0f, 0f, Tilt(0.4f)),
                clothShade, 0.02f, 0.08f, false);
            // Gable ties closing the two ends of the awning frame.
            Make(PrimitiveType.Cube, "3_GableTieFront", new Vector3(0f, 3.30f, 2.72f),
                new Vector3(4.3f, 0.11f, 0.13f), Quaternion.Euler(0f, 0f, Tilt(0.6f)),
                timber, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cube, "3_GableTieBack", new Vector3(0f, 3.30f, -2.72f),
                new Vector3(4.3f, 0.11f, 0.13f), Quaternion.Euler(0f, 0f, Tilt(0.6f)),
                timber, 0.04f, 0.10f, false);
            // Two king posts carrying the ridge pole off the tie beams.
            for (int i = 0; i < 2; i++)
            {
                float kz = (i == 0) ? -2.60f : 2.60f;
                Make(PrimitiveType.Cube, $"3_KingPost_{(char)('A' + i)}",
                    new Vector3(0f, 3.50f, kz), new Vector3(0.14f, 1.05f, 0.14f),
                    Quaternion.Euler(0f, 0f, Tilt(1.2f)), timberDark, 0.04f, 0.10f, false);
            }

            // ── 4_ Props / accents ─────────────────────────────────────────
            // Three cots down the west aisle, one with its blanket thrown back.
            for (int i = 0; i < 3; i++)
            {
                float cz = -1.60f + i * 1.60f;
                char cc = (char)('A' + i);
                Make(PrimitiveType.Cube, $"4_CotFrame_{cc}", new Vector3(-1.28f, 0.72f, cz),
                    new Vector3(0.88f, 0.09f, 1.80f), Quaternion.Euler(0f, Tilt(2f), 0f),
                    timberDark, 0.04f, 0.10f, false);
                Make(PrimitiveType.Cube, $"4_CotMattress_{cc}", new Vector3(-1.28f, 0.82f, cz),
                    new Vector3(0.80f, 0.13f, 1.68f), Quaternion.Euler(0f, Tilt(2f), 0f),
                    linen * (0.97f - i * 0.03f), 0.02f, 0.07f, false);
                Make(PrimitiveType.Cube, $"4_CotBlanket_{cc}",
                    new Vector3(-1.28f, 0.90f, cz - 0.42f + Tilt(0.06f)),
                    new Vector3(0.82f, 0.07f, 0.70f), Quaternion.Euler(Tilt(2f), Tilt(3f), 0f),
                    clothShade * (0.95f + i * 0.02f), 0.02f, 0.07f, false);
                Make(PrimitiveType.Cylinder, $"4_CotLeg_{cc}", new Vector3(-1.28f, 0.58f, cz),
                    new Vector3(0.07f, 0.14f, 0.07f), Quaternion.identity,
                    timberDark, 0.04f, 0.10f, false);
            }

            // Stretcher propped against the east colonnade — two poles + canvas.
            Make(PrimitiveType.Cylinder, "4_StretcherPole_A", new Vector3(1.78f, 1.25f, -0.45f),
                new Vector3(0.06f, 0.95f, 0.06f), Quaternion.Euler(14f, 0f, Tilt(2f)),
                timber, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "4_StretcherPole_B", new Vector3(1.78f, 1.25f, 0.05f),
                new Vector3(0.06f, 0.95f, 0.06f), Quaternion.Euler(14f, 0f, Tilt(2f)),
                timber, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_StretcherCanvas", new Vector3(1.78f, 1.25f, -0.20f),
                new Vector3(0.05f, 1.70f, 0.44f), Quaternion.Euler(14f, 0f, 0f),
                cloth, 0.02f, 0.07f, false);

            // Water basin near the entrance — pedestal, copper bowl, still water.
            Make(PrimitiveType.Cylinder, "4_BasinPedestal", new Vector3(1.30f, 0.74f, 1.72f),
                new Vector3(0.34f, 0.30f, 0.34f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                stoneDark, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cylinder, "4_BasinBowl", new Vector3(1.30f, 1.10f, 1.72f),
                new Vector3(0.72f, 0.12f, 0.72f), Quaternion.Euler(Tilt(1.2f), 0f, Tilt(1.2f)),
                copper, 0.70f, 0.42f, false);
            Make(PrimitiveType.Cylinder, "4_BasinWater", new Vector3(1.30f, 1.20f, 1.72f),
                new Vector3(0.62f, 0.02f, 0.62f), Quaternion.identity,
                water, 0.10f, 0.88f, false);

            // Herb bundles hung from the east top plate to dry.
            for (int i = 0; i < 3; i++)
            {
                float hz = -1.30f + i * 1.30f;
                Make(PrimitiveType.Cube, $"4_HerbBundle_{(char)('A' + i)}",
                    new Vector3(1.98f, 2.50f, hz), new Vector3(0.16f, 0.42f, 0.16f),
                    Quaternion.Euler(Tilt(4f), (float)(rng.NextDouble() * 40.0), Tilt(4f)),
                    herb * (0.92f + i * 0.05f), 0.02f, 0.08f, false);
            }

            // Faction-color valances hung along both eaves.
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(-2.22f, 2.62f, 0f),
                new Vector3(0.05f, 0.34f, 5.1f), Quaternion.Euler(Tilt(1f), 0f, 0f),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(2.22f, 2.62f, 0f),
                new Vector3(0.05f, 0.34f, 5.1f), Quaternion.Euler(Tilt(1f), 0f, 0f),
                Color.white, 0.02f, 0.10f, false);

            // Entrance pennant on a slim pole beside the ramp.
            Make(PrimitiveType.Cylinder, "4_PennantPole", new Vector3(-1.85f, 1.42f, 3.30f),
                new Vector3(0.06f, 0.98f, 0.06f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                timberDark, 0.04f, 0.12f, false);
            Make(PrimitiveType.Cube, "4_Stripe_3", new Vector3(-1.58f, 2.10f, 3.30f),
                new Vector3(0.58f, 0.46f, 0.035f), Quaternion.Euler(0f, Tilt(3f), 0f),
                Color.white, 0.02f, 0.10f, false);

            // Hanging oil lamp over the aisle — the single emissive accent.
            Make(PrimitiveType.Cylinder, "4_LampChain", new Vector3(0.35f, 2.72f, 1.10f),
                new Vector3(0.035f, 0.32f, 0.035f), Quaternion.identity,
                timberDark, 0.65f, 0.35f, false);
            Make(PrimitiveType.Sphere, "4_Lamp", new Vector3(0.35f, 2.32f, 1.10f),
                new Vector3(0.24f, 0.26f, 0.24f), Quaternion.Euler(Tilt(4f), 0f, Tilt(4f)),
                lamp, 0.05f, 0.18f, true);

            // Wash bucket and a folded linen stack left on the platform edge.
            Make(PrimitiveType.Cylinder, "4_Bucket", new Vector3(-1.90f, 0.62f, 1.05f),
                new Vector3(0.28f, 0.18f, 0.28f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)),
                timber * 0.9f, 0.04f, 0.12f, false);
            Make(PrimitiveType.Cube, "4_LinenStack", new Vector3(-1.88f, 0.56f, 0.20f),
                new Vector3(0.44f, 0.20f, 0.42f), Quaternion.Euler(0f, Tilt(6f), Tilt(2f)),
                linen, 0.02f, 0.07f, false);

            return root;
        }
    }
}
