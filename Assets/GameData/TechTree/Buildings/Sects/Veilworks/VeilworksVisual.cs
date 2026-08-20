// File: Assets/GameData/TechTree/Buildings/Sects/Veilworks/VeilworksVisual.cs
// Procedural visual for the Veilworks — the Sect of Reclamation's smelter for
// cursed matter (the curse-harvesters). Built entirely from primitives in the
// CreateProceduralSmelter idiom: named palette, per-part metallic/smoothness
// contrast, 1-3 degree tilts, prop vignettes, and — unusually for this set —
// a whole family of emissive parts, because purple is the curse colour and
// this building is the only one allowed to stand on cursed ground.
// Silhouette: a violet-stained rock apron under a sooty stone platform, a
// squat iron-banded furnace drum with a glowing mouth, a tapering three-stage
// chimney with a vented cowl, condenser pipes running out to a quench tank,
// a quench trough, and racks of raw veilstone shards that glow on their own.
// Part names carry leading rise-group numbers (1_ platform, 2_ furnace and
// pipework, 3_ chimney and gantry, 4_ props) for BuildingRiseData's staggered
// construction. Player-color accents: 4_Stripe_1 / 4_Stripe_2 gantry banners
// and 4_Stripe_3 cowl pennant (BuildingFactionColorMarker tints anything
// named "stripe"). "Cowl"/"Cap"/"Gantry" names deliberately avoid the "roof"
// substring so the faction marker never solid-paints the ironwork.
// The orchestrator wires FitSelectionCollider / EntityReference / the faction
// marker after Build returns — this class only assembles geometry.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class VeilworksVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject($"Veilworks_{seed}");

            // Palette — sooty iron and dark stone, lit only by curse-violet.
            var darkStone   = new Color(0.27f, 0.25f, 0.27f);
            var stoneShadow = new Color(0.18f, 0.17f, 0.19f);
            var cursedRock  = new Color(0.24f, 0.17f, 0.29f);
            var soot        = new Color(0.13f, 0.12f, 0.13f);
            var iron        = new Color(0.21f, 0.20f, 0.21f);
            var ironLight   = new Color(0.33f, 0.32f, 0.34f);
            var brassDark   = new Color(0.44f, 0.34f, 0.15f);
            var timberDark  = new Color(0.24f, 0.17f, 0.11f);
            var slag        = new Color(0.30f, 0.24f, 0.30f);
            var veilGlow    = new Color(0.62f, 0.28f, 0.95f);   // raw shard light
            var veilDeep    = new Color(0.38f, 0.15f, 0.60f);   // cooled residue
            var furnaceGlow = new Color(0.74f, 0.34f, 1.00f);   // the mouth
            var quenchGlow  = new Color(0.44f, 0.22f, 0.72f);   // spent bath

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

            // ── 1_ Cursed apron / platform ─────────────────────────────────
            Make(PrimitiveType.Cube, "1_CursedApron", new Vector3(0f, 0.05f, 0f),
                new Vector3(5.9f, 0.10f, 6.5f), Quaternion.Euler(0f, Tilt(1.2f), 0f),
                cursedRock, 0.03f, 0.14f, false);
            Make(PrimitiveType.Cube, "1_Platform", new Vector3(0f, 0.21f, 0f),
                new Vector3(5.0f, 0.42f, 5.6f), Quaternion.Euler(0f, Tilt(0.5f), 0f),
                darkStone, 0.06f, 0.13f, false);
            Make(PrimitiveType.Cube, "1_PlatformKerb", new Vector3(0f, 0.38f, 0f),
                new Vector3(5.25f, 0.10f, 5.85f), Quaternion.identity,
                stoneShadow, 0.06f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "1_FurnaceSkirt", new Vector3(0f, 0.50f, -0.50f),
                new Vector3(3.25f, 0.10f, 3.25f), Quaternion.identity,
                stoneShadow, 0.08f, 0.14f, false);
            Make(PrimitiveType.Cube, "1_StokePad", new Vector3(0f, 0.16f, 2.95f),
                new Vector3(2.30f, 0.32f, 0.80f), Quaternion.Euler(0f, Tilt(1f), 0f),
                soot, 0.05f, 0.10f, false);
            for (int i = 0; i < 4; i++)
            {
                float fx = (i % 2 == 0) ? -2.35f : 2.35f;
                float fz = (i < 2) ? 2.55f : -2.55f;
                Make(PrimitiveType.Cube, $"1_FootBlock_{i}",
                    new Vector3(fx, 0.18f + Tilt(0.03f), fz),
                    new Vector3(0.80f + Tilt(0.08f), 0.38f, 0.76f + Tilt(0.08f)),
                    Quaternion.Euler(Tilt(2f), (float)(rng.NextDouble() * 90.0), Tilt(2f)),
                    stoneShadow * 0.92f, 0.06f, 0.10f, false);
            }
            // Violet residue crusting the apron where the slag runs off.
            for (int i = 0; i < 3; i++)
            {
                Make(PrimitiveType.Cube, $"1_ResidueCrust_{(char)('A' + i)}",
                    new Vector3(-1.35f + i * 1.45f, 0.12f, 2.55f + Tilt(0.10f)),
                    new Vector3(0.85f + Tilt(0.12f), 0.06f, 0.55f + Tilt(0.10f)),
                    Quaternion.Euler(0f, (float)(rng.NextDouble() * 40.0), 0f),
                    veilDeep * 0.85f, 0.05f, 0.30f, false);
            }

            // ── 2_ Furnace drum / pipework / quench ────────────────────────
            Make(PrimitiveType.Cylinder, "2_FurnaceDrum", new Vector3(0f, 1.55f, -0.50f),
                new Vector3(2.90f, 1.05f, 2.90f), Quaternion.Euler(Tilt(0.4f), 0f, Tilt(0.4f)),
                darkStone * 0.92f, 0.15f, 0.20f, false);
            Make(PrimitiveType.Cylinder, "2_DrumBand_A", new Vector3(0f, 0.95f, -0.50f),
                new Vector3(3.02f, 0.09f, 3.02f), Quaternion.identity,
                iron, 0.85f, 0.34f, false);
            Make(PrimitiveType.Cylinder, "2_DrumBand_B", new Vector3(0f, 2.18f, -0.50f),
                new Vector3(3.02f, 0.09f, 3.02f), Quaternion.identity,
                iron, 0.85f, 0.34f, false);
            Make(PrimitiveType.Cylinder, "2_DrumShoulder", new Vector3(0f, 2.44f, -0.50f),
                new Vector3(2.55f, 0.22f, 2.55f), Quaternion.identity,
                soot, 0.30f, 0.22f, false);

            // Three squat buttress piers propping the drum.
            for (int i = 0; i < 3; i++)
            {
                float ang = (150f + 60f * i) * Mathf.Deg2Rad;
                float px = Mathf.Cos(ang) * 1.52f;
                float pz = Mathf.Sin(ang) * 1.52f - 0.50f;
                Make(PrimitiveType.Cube, $"2_DrumPier_{(char)('A' + i)}",
                    new Vector3(px, 0.98f, pz), new Vector3(0.52f, 1.00f, 0.52f),
                    Quaternion.Euler(0f, -Mathf.Atan2(Mathf.Sin(ang), Mathf.Cos(ang)) * Mathf.Rad2Deg, Tilt(1.5f)),
                    stoneShadow, 0.08f, 0.14f, false);
            }

            // The furnace mouth on the front (+Z) face — the brightest thing
            // on the building, framed by iron and a soot-blacked lintel.
            Make(PrimitiveType.Cube, "2_MouthFrame", new Vector3(0f, 1.18f, 0.90f),
                new Vector3(1.30f, 1.20f, 0.22f), Quaternion.Euler(0f, Tilt(0.6f), 0f),
                iron, 0.80f, 0.30f, false);
            Make(PrimitiveType.Cube, "2_FurnaceMouth", new Vector3(0f, 1.14f, 1.02f),
                new Vector3(0.94f, 0.88f, 0.10f), Quaternion.identity,
                furnaceGlow, 0.0f, 0.06f, true);
            Make(PrimitiveType.Cube, "2_MouthLintel", new Vector3(0f, 1.90f, 0.94f),
                new Vector3(1.55f, 0.20f, 0.34f), Quaternion.Euler(0f, 0f, Tilt(0.8f)),
                soot, 0.20f, 0.18f, false);
            Make(PrimitiveType.Cube, "2_MouthSill", new Vector3(0f, 0.62f, 1.02f),
                new Vector3(1.40f, 0.14f, 0.46f), Quaternion.Euler(0f, Tilt(1f), 0f),
                ironLight, 0.82f, 0.36f, false);
            Make(PrimitiveType.Cube, "2_StokeDeck", new Vector3(0f, 0.50f, 1.72f),
                new Vector3(2.30f, 0.14f, 1.30f), Quaternion.Euler(Tilt(0.8f), 0f, 0f),
                timberDark, 0.05f, 0.12f, false);

            // Condenser run: three pipe segments stepping down from the drum
            // shoulder out to a squat tank on the west side.
            Make(PrimitiveType.Cylinder, "2_CondenserPipe_A", new Vector3(-1.55f, 2.15f, -0.50f),
                new Vector3(0.24f, 0.55f, 0.24f), Quaternion.Euler(0f, 0f, 74f),
                iron, 0.80f, 0.32f, false);
            Make(PrimitiveType.Cylinder, "2_CondenserPipe_B", new Vector3(-2.10f, 1.90f, -0.50f),
                new Vector3(0.22f, 0.42f, 0.22f), Quaternion.Euler(0f, 0f, 26f),
                iron * 1.1f, 0.80f, 0.32f, false);
            Make(PrimitiveType.Cylinder, "2_CondenserPipe_C", new Vector3(-2.22f, 1.30f, -0.42f),
                new Vector3(0.20f, 0.40f, 0.20f), Quaternion.Euler(12f, 0f, Tilt(2f)),
                iron, 0.80f, 0.32f, false);
            Make(PrimitiveType.Cylinder, "2_CondenserTank", new Vector3(-2.10f, 0.86f, -0.30f),
                new Vector3(1.05f, 0.44f, 1.05f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                ironLight * 0.9f, 0.78f, 0.34f, false);
            Make(PrimitiveType.Cylinder, "2_CondenserLid", new Vector3(-2.10f, 1.32f, -0.30f),
                new Vector3(1.14f, 0.07f, 1.14f), Quaternion.identity,
                iron, 0.85f, 0.38f, false);
            Make(PrimitiveType.Cylinder, "2_CondenserValve", new Vector3(-2.10f, 1.44f, -0.30f),
                new Vector3(0.34f, 0.09f, 0.34f), Quaternion.Euler(0f, Tilt(8f), 0f),
                brassDark, 0.85f, 0.45f, false);

            // Quench trough on the east side, holding a spent violet bath.
            Make(PrimitiveType.Cube, "2_QuenchTrough", new Vector3(2.00f, 0.72f, 0.20f),
                new Vector3(0.86f, 0.60f, 2.10f), Quaternion.Euler(0f, Tilt(1.5f), 0f),
                iron, 0.75f, 0.30f, false);
            Make(PrimitiveType.Cube, "2_QuenchBath", new Vector3(2.00f, 0.96f, 0.20f),
                new Vector3(0.70f, 0.05f, 1.94f), Quaternion.identity,
                quenchGlow, 0.10f, 0.80f, true);
            Make(PrimitiveType.Cube, "2_QuenchSpout", new Vector3(1.55f, 1.36f, 0.20f),
                new Vector3(0.60f, 0.16f, 0.24f), Quaternion.Euler(0f, 0f, -16f),
                ironLight, 0.82f, 0.36f, false);
            Make(PrimitiveType.Cube, "2_BellowsBox", new Vector3(-1.45f, 0.78f, 1.55f),
                new Vector3(0.90f, 0.56f, 0.70f), Quaternion.Euler(0f, Tilt(4f), Tilt(1.5f)),
                timberDark, 0.06f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "2_BellowsPipe", new Vector3(-1.00f, 0.86f, 1.20f),
                new Vector3(0.16f, 0.42f, 0.16f), Quaternion.Euler(0f, 46f, 74f),
                iron, 0.80f, 0.32f, false);

            // ── 3_ Tapering chimney / cowl / gantry ────────────────────────
            Make(PrimitiveType.Cylinder, "3_ChimneyA", new Vector3(0f, 3.38f, -0.50f),
                new Vector3(1.75f, 0.90f, 1.75f), Quaternion.Euler(Tilt(0.4f), 0f, Tilt(0.4f)),
                darkStone * 0.86f, 0.20f, 0.20f, false);
            Make(PrimitiveType.Cylinder, "3_ChimneyBand_A", new Vector3(0f, 4.24f, -0.50f),
                new Vector3(1.84f, 0.08f, 1.84f), Quaternion.identity,
                iron, 0.85f, 0.34f, false);
            Make(PrimitiveType.Cylinder, "3_ChimneyB", new Vector3(0.02f, 4.92f, -0.51f),
                new Vector3(1.34f, 0.66f, 1.34f), Quaternion.Euler(Tilt(0.4f), 0f, Tilt(0.4f)),
                darkStone * 0.80f, 0.20f, 0.21f, false);
            Make(PrimitiveType.Cylinder, "3_ChimneyBand_B", new Vector3(0f, 5.56f, -0.50f),
                new Vector3(1.42f, 0.08f, 1.42f), Quaternion.identity,
                iron, 0.85f, 0.34f, false);
            Make(PrimitiveType.Cylinder, "3_ChimneyC", new Vector3(-0.02f, 6.02f, -0.49f),
                new Vector3(1.00f, 0.48f, 1.00f), Quaternion.Euler(Tilt(0.5f), 0f, Tilt(0.5f)),
                soot, 0.22f, 0.22f, false);
            Make(PrimitiveType.Cylinder, "3_ChimneyCowl", new Vector3(0f, 6.56f, -0.50f),
                new Vector3(1.30f, 0.09f, 1.30f), Quaternion.Euler(Tilt(1f), 0f, Tilt(1f)),
                ironLight, 0.85f, 0.40f, false);
            for (int i = 0; i < 3; i++)
            {
                float ang = (30f + 120f * i) * Mathf.Deg2Rad;
                Make(PrimitiveType.Cube, $"3_CowlLeg_{(char)('A' + i)}",
                    new Vector3(Mathf.Cos(ang) * 0.52f, 6.42f, Mathf.Sin(ang) * 0.52f - 0.50f),
                    new Vector3(0.09f, 0.28f, 0.09f),
                    Quaternion.Euler(0f, -Mathf.Atan2(Mathf.Sin(ang), Mathf.Cos(ang)) * Mathf.Rad2Deg, 0f),
                    iron, 0.85f, 0.38f, false);
            }
            Make(PrimitiveType.Cylinder, "3_FlueThroat", new Vector3(0f, 6.30f, -0.50f),
                new Vector3(0.66f, 0.06f, 0.66f), Quaternion.identity,
                veilDeep, 0.0f, 0.10f, true);

            // Gantry walk hooked around the chimney base, reached by a ladder.
            Make(PrimitiveType.Cylinder, "3_GantryDeck", new Vector3(0f, 2.62f, -0.50f),
                new Vector3(3.05f, 0.06f, 3.05f), Quaternion.Euler(0f, Tilt(1f), 0f),
                timberDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "3_GantryRing", new Vector3(0f, 2.56f, -0.50f),
                new Vector3(3.15f, 0.07f, 3.15f), Quaternion.identity,
                iron, 0.82f, 0.34f, false);
            for (int i = 0; i < 6; i++)
            {
                float ang = (30f + 60f * i) * Mathf.Deg2Rad;
                Make(PrimitiveType.Cylinder, $"3_GantryStanchion_{(char)('A' + i)}",
                    new Vector3(Mathf.Cos(ang) * 1.46f, 2.90f, Mathf.Sin(ang) * 1.46f - 0.50f),
                    new Vector3(0.05f, 0.28f, 0.05f),
                    Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)), iron, 0.85f, 0.38f, false);
            }
            Make(PrimitiveType.Cylinder, "3_GantryRail", new Vector3(0f, 3.18f, -0.50f),
                new Vector3(2.98f, 0.04f, 2.98f), Quaternion.Euler(Tilt(0.8f), 0f, Tilt(0.8f)),
                ironLight, 0.85f, 0.40f, false);

            // ── 4_ Props / accents ─────────────────────────────────────────
            // Ladder up the back of the drum to the gantry.
            Make(PrimitiveType.Cylinder, "4_LadderRail_A", new Vector3(-0.28f, 1.52f, -2.02f),
                new Vector3(0.06f, 1.10f, 0.06f), Quaternion.Euler(-8f, 0f, 0f),
                iron, 0.85f, 0.36f, false);
            Make(PrimitiveType.Cylinder, "4_LadderRail_B", new Vector3(0.28f, 1.52f, -2.02f),
                new Vector3(0.06f, 1.10f, 0.06f), Quaternion.Euler(-8f, 0f, 0f),
                iron, 0.85f, 0.36f, false);
            for (int i = 0; i < 4; i++)
            {
                float ry = 0.72f + i * 0.52f;
                Make(PrimitiveType.Cylinder, $"4_LadderRung_{(char)('A' + i)}",
                    new Vector3(0f, ry, -2.02f - (ry - 1.52f) * 0.14f),
                    new Vector3(0.05f, 0.30f, 0.05f), Quaternion.Euler(0f, 0f, 90f),
                    ironLight, 0.85f, 0.38f, false);
            }

            // Shard rack west of the stoke deck — raw veilstone, still lit.
            for (int i = 0; i < 2; i++)
            {
                float uz = (i == 0) ? 1.62f : 2.52f;
                Make(PrimitiveType.Cylinder, $"4_RackUpright_{(char)('A' + i)}",
                    new Vector3(-2.05f, 0.85f, uz), new Vector3(0.09f, 0.52f, 0.09f),
                    Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)), iron, 0.82f, 0.34f, false);
            }
            Make(PrimitiveType.Cube, "4_RackShelf_A", new Vector3(-2.05f, 0.68f, 2.07f),
                new Vector3(0.52f, 0.06f, 1.00f), Quaternion.Euler(0f, Tilt(1.5f), 0f),
                timberDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "4_RackShelf_B", new Vector3(-2.05f, 1.18f, 2.07f),
                new Vector3(0.52f, 0.06f, 1.00f), Quaternion.Euler(0f, Tilt(1.5f), 0f),
                timberDark, 0.05f, 0.12f, false);
            for (int i = 0; i < 4; i++)
            {
                float sy = (i < 2) ? 0.82f : 1.32f;
                float sz = 1.80f + (i % 2) * 0.52f;
                Make(PrimitiveType.Cube, $"4_Shard_{(char)('A' + i)}",
                    new Vector3(-2.05f + Tilt(0.06f), sy, sz),
                    new Vector3(0.16f, 0.26f + Tilt(0.04f), 0.16f),
                    Quaternion.Euler(Tilt(11f), (float)(rng.NextDouble() * 90.0), Tilt(11f)),
                    veilGlow * (0.92f + i * 0.03f), 0.0f, 0.55f, true);
            }

            // Loose shards spilled on the apron by the quench trough.
            for (int i = 0; i < 3; i++)
            {
                Make(PrimitiveType.Cube, $"4_LooseShard_{(char)('A' + i)}",
                    new Vector3(2.70f + Tilt(0.14f), 0.24f, 1.55f - i * 0.62f),
                    new Vector3(0.14f, 0.34f + Tilt(0.06f), 0.14f),
                    Quaternion.Euler(18f + Tilt(12f), (float)(rng.NextDouble() * 90.0), Tilt(14f)),
                    veilGlow * (0.86f + i * 0.05f), 0.0f, 0.55f, true);
            }

            // Slag heap and a pair of long tongs left leaning on the drum.
            for (int i = 0; i < 2; i++)
            {
                Make(PrimitiveType.Sphere, $"4_SlagLump_{(char)('A' + i)}",
                    new Vector3(-2.85f + i * 0.55f, 0.20f, -1.85f + i * 0.42f),
                    new Vector3(0.62f + Tilt(0.08f), 0.34f, 0.58f + Tilt(0.08f)),
                    Quaternion.Euler(Tilt(8f), (float)(rng.NextDouble() * 90.0), Tilt(8f)),
                    slag * (0.9f + i * 0.06f), 0.15f, 0.16f, false);
            }
            Make(PrimitiveType.Cylinder, "4_Tongs_A", new Vector3(1.05f, 1.05f, 1.42f),
                new Vector3(0.05f, 0.72f, 0.05f), Quaternion.Euler(24f, 0f, -14f),
                iron, 0.85f, 0.38f, false);
            Make(PrimitiveType.Cylinder, "4_Tongs_B", new Vector3(1.15f, 1.05f, 1.42f),
                new Vector3(0.05f, 0.72f, 0.05f), Quaternion.Euler(24f, 0f, -8f),
                iron, 0.85f, 0.38f, false);
            Make(PrimitiveType.Cube, "4_CoalBin", new Vector3(1.85f, 0.66f, 2.20f),
                new Vector3(0.85f, 0.48f, 0.75f), Quaternion.Euler(0f, Tilt(5f), 0f),
                timberDark * 0.9f, 0.05f, 0.11f, false);
            Make(PrimitiveType.Cube, "4_CoalHeap", new Vector3(1.85f, 0.94f, 2.20f),
                new Vector3(0.72f, 0.16f, 0.62f), Quaternion.Euler(Tilt(3f), Tilt(8f), Tilt(3f)),
                soot, 0.10f, 0.14f, false);

            // Faction banners hung from the gantry rail, front-left and front-right.
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(-1.05f, 2.10f, 0.72f),
                new Vector3(0.40f, 1.00f, 0.05f), Quaternion.Euler(0f, -28f + Tilt(2f), Tilt(1.5f)),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(1.05f, 2.10f, 0.72f),
                new Vector3(0.40f, 1.00f, 0.05f), Quaternion.Euler(0f, 28f + Tilt(2f), Tilt(1.5f)),
                Color.white, 0.02f, 0.10f, false);

            // Cowl pennant on a slim iron mast beside the flue.
            Make(PrimitiveType.Cylinder, "4_PennantMast", new Vector3(0.62f, 6.86f, -0.50f),
                new Vector3(0.05f, 0.42f, 0.05f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                iron, 0.85f, 0.38f, false);
            Make(PrimitiveType.Cube, "4_Stripe_3", new Vector3(0.92f, 7.14f, -0.50f),
                new Vector3(0.54f, 0.18f, 0.035f), Quaternion.Euler(0f, Tilt(3f), 0f),
                Color.white, 0.02f, 0.10f, false);

            return root;
        }
    }
}
