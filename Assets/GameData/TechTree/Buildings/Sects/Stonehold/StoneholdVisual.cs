// File: Assets/GameData/TechTree/Buildings/Sects/Stonehold/StoneholdVisual.cs
// Procedural visual for the Stonehold — the Sect of Fortitude's squat
// windowless blockhouse (the wall-keepers). Built entirely from primitives in
// the CreateProceduralSmelter idiom: named palette, per-part metallic /
// smoothness contrast, 1-3 degree tilts, prop vignettes, one low ember glow.
// Silhouette: a heavy footing and glacis skirt, three battered granite courses
// that step inward as they rise, four leaning corner buttresses, arrow slits
// and nothing else for openings, an iron-banded door in a projecting porch,
// and a machicolated flat deck ringed by a crenellated parapet. The building
// is meant to be shot at, so it is broader than it is tall.
// Part names carry leading rise-group numbers (1_ footing, 2_ walls, 3_ deck
// and parapet, 4_ props) for BuildingRiseData's staggered construction.
// Player-color accents: 4_Stripe_1 / 4_Stripe_2 porch banners and 4_Stripe_3
// deck pennant (BuildingFactionColorMarker tints anything named "stripe").
// "Deck"/"Slab"/"Merlon" names deliberately avoid the "roof" substring so the
// faction marker never solid-paints the granite.
// The orchestrator wires FitSelectionCollider / EntityReference / the faction
// marker after Build returns — this class only assembles geometry.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class StoneholdVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject($"Stonehold_{seed}");

            // Palette — dark grey granite, mortar lines, cold iron, no warmth.
            var granite      = new Color(0.36f, 0.36f, 0.38f);
            var graniteLight = new Color(0.45f, 0.45f, 0.47f);
            var graniteDark  = new Color(0.24f, 0.24f, 0.26f);
            var mortar       = new Color(0.31f, 0.30f, 0.29f);
            var rubble       = new Color(0.32f, 0.31f, 0.30f);
            var iron         = new Color(0.16f, 0.16f, 0.17f);
            var ironLight    = new Color(0.28f, 0.28f, 0.30f);
            var oakDark      = new Color(0.20f, 0.14f, 0.09f);
            var slitDark     = new Color(0.04f, 0.04f, 0.05f);
            var embers       = new Color(0.92f, 0.38f, 0.10f);


            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);

            // Deterministic hand-built wobble in degrees.
            System.Func<float, float> Tilt = max => (float)(rng.NextDouble() * 2.0 - 1.0) * max;

            // ── 1_ Footing / glacis ────────────────────────────────────────
            Make(PrimitiveType.Cube, "1_Footing", new Vector3(0f, 0.17f, 0f),
                new Vector3(5.6f, 0.34f, 5.6f), Quaternion.Euler(0f, Tilt(0.5f), 0f),
                graniteDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "1_Glacis", new Vector3(0f, 0.52f, 0f),
                new Vector3(5.20f, 0.36f, 5.20f), Quaternion.identity,
                mortar, 0.05f, 0.11f, false);
            Make(PrimitiveType.Cube, "1_Threshold", new Vector3(0f, 0.16f, 3.05f),
                new Vector3(1.90f, 0.32f, 0.80f), Quaternion.Euler(0f, Tilt(0.8f), 0f),
                graniteDark, 0.05f, 0.10f, false);
            for (int i = 0; i < 4; i++)
            {
                float fx = (i % 2 == 0) ? -2.55f : 2.55f;
                float fz = (i < 2) ? 2.55f : -2.55f;
                Make(PrimitiveType.Cube, $"1_FootBlock_{i}",
                    new Vector3(fx, 0.20f + Tilt(0.03f), fz),
                    new Vector3(0.95f + Tilt(0.08f), 0.42f, 0.92f + Tilt(0.08f)),
                    Quaternion.Euler(Tilt(1.8f), (float)(rng.NextDouble() * 90.0), Tilt(1.8f)),
                    graniteDark * 0.92f, 0.05f, 0.09f, false);
            }

            // ── 2_ Battered walls / buttresses / slits / door ──────────────
            // Three courses, each stepping inward — the batter that shrugs off
            // siege shot. No windows exist anywhere on this building.
            Make(PrimitiveType.Cube, "2_WallCourseA", new Vector3(0f, 1.08f, 0f),
                new Vector3(4.85f, 0.80f, 4.85f), Quaternion.identity,
                granite, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "2_WallCourseB", new Vector3(0f, 1.86f, 0f),
                new Vector3(4.60f, 0.78f, 4.60f), Quaternion.identity,
                granite * 1.05f, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "2_WallCourseC", new Vector3(0f, 2.62f, 0f),
                new Vector3(4.36f, 0.76f, 4.36f), Quaternion.identity,
                graniteLight, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "2_MortarBandA", new Vector3(0f, 1.48f, 0f),
                new Vector3(4.90f, 0.08f, 4.90f), Quaternion.identity,
                mortar, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "2_MortarBandB", new Vector3(0f, 2.25f, 0f),
                new Vector3(4.66f, 0.08f, 4.66f), Quaternion.identity,
                mortar, 0.05f, 0.10f, false);

            // Four corner buttress blocks, each leaning its top back toward
            // the core so the corners read as reinforced mass.
            for (int i = 0; i < 4; i++)
            {
                float bx = (i % 2 == 0) ? -2.10f : 2.10f;
                float bz = (i < 2) ? 2.10f : -2.10f;
                float leanZ = (bx > 0f) ? 2.0f : -2.0f;
                float leanX = (bz > 0f) ? -2.0f : 2.0f;
                Make(PrimitiveType.Cube, $"2_CornerButtress_{(char)('A' + i)}",
                    new Vector3(bx, 1.55f, bz), new Vector3(0.98f, 2.75f, 0.98f),
                    Quaternion.Euler(leanX + Tilt(0.4f), Tilt(0.8f), leanZ + Tilt(0.4f)),
                    granite * 0.94f, 0.05f, 0.11f, false);
                Make(PrimitiveType.Cube, $"2_ButtressCap_{(char)('A' + i)}",
                    new Vector3(bx * 0.96f, 3.02f, bz * 0.96f),
                    new Vector3(1.08f, 0.20f, 1.08f),
                    Quaternion.Euler(0f, Tilt(1.5f), 0f), graniteDark, 0.05f, 0.13f, false);
            }

            // Arrow slits — the only openings. Two per face on course B, one
            // per face higher up on course C.
            for (int face = 0; face < 4; face++)
            {
                char fc = (char)('A' + face);
                bool alongZ = (face < 2);                 // faces looking down +Z / -Z
                float sgn = (face % 2 == 0) ? 1f : -1f;
                for (int i = 0; i < 2; i++)
                {
                    float off = (i == 0) ? -1.20f : 1.20f;
                    var posB = alongZ
                        ? new Vector3(off, 1.86f, sgn * 2.31f)
                        : new Vector3(sgn * 2.31f, 1.86f, off);
                    var sclB = alongZ
                        ? new Vector3(0.14f, 0.62f, 0.10f)
                        : new Vector3(0.10f, 0.62f, 0.14f);
                    Make(PrimitiveType.Cube, $"2_ArrowSlit{fc}_{(char)('A' + i)}",
                        posB, sclB, Quaternion.identity, slitDark, 0.0f, 0.05f, false);
                }
                var posC = alongZ
                    ? new Vector3(0f, 2.66f, sgn * 2.19f)
                    : new Vector3(sgn * 2.19f, 2.66f, 0f);
                var sclC = alongZ
                    ? new Vector3(0.14f, 0.52f, 0.10f)
                    : new Vector3(0.10f, 0.52f, 0.14f);
                Make(PrimitiveType.Cube, $"2_ArrowSlitHigh{fc}", posC, sclC,
                    Quaternion.identity, slitDark, 0.0f, 0.05f, false);
            }

            // Projecting entry porch (+Z) with the heavy iron-banded door.
            Make(PrimitiveType.Cube, "2_DoorPorch", new Vector3(0f, 1.08f, 2.45f),
                new Vector3(1.75f, 1.95f, 0.60f), Quaternion.Euler(0f, Tilt(0.6f), 0f),
                granite * 0.98f, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "2_PorchCap", new Vector3(0f, 2.14f, 2.48f),
                new Vector3(1.95f, 0.22f, 0.72f), Quaternion.identity,
                graniteDark, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "2_Door", new Vector3(0f, 0.98f, 2.78f),
                new Vector3(1.18f, 1.58f, 0.12f), Quaternion.identity,
                oakDark, 0.05f, 0.10f, false);
            for (int i = 0; i < 3; i++)
            {
                Make(PrimitiveType.Cube, $"2_DoorBand_{(char)('A' + i)}",
                    new Vector3(0f, 0.44f + i * 0.54f, 2.85f),
                    new Vector3(1.20f, 0.13f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(0.5f)),
                    iron, 0.85f, 0.35f, false);
            }
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -0.44f : 0.44f;
                float sy = (i < 2) ? 0.44f : 1.52f;
                Make(PrimitiveType.Sphere, $"2_DoorStud_{(char)('A' + i)}",
                    new Vector3(sx, sy, 2.88f), new Vector3(0.11f, 0.11f, 0.07f),
                    Quaternion.identity, ironLight, 0.90f, 0.45f, false);
            }
            Make(PrimitiveType.Cube, "2_DoorJambW", new Vector3(-0.70f, 0.98f, 2.80f),
                new Vector3(0.16f, 1.70f, 0.10f), Quaternion.identity, ironLight, 0.80f, 0.32f, false);
            Make(PrimitiveType.Cube, "2_DoorJambE", new Vector3(0.70f, 0.98f, 2.80f),
                new Vector3(0.16f, 1.70f, 0.10f), Quaternion.identity, ironLight, 0.80f, 0.32f, false);

            // ── 3_ Machicolation, deck, crenellated parapet ────────────────
            // Two corbels per face carrying the overhanging deck slab.
            for (int face = 0; face < 4; face++)
            {
                char fc = (char)('A' + face);
                bool alongZ = (face < 2);
                float sgn = (face % 2 == 0) ? 1f : -1f;
                for (int i = 0; i < 2; i++)
                {
                    float off = (i == 0) ? -1.05f : 1.05f;
                    var pos = alongZ
                        ? new Vector3(off, 3.05f, sgn * 2.30f)
                        : new Vector3(sgn * 2.30f, 3.05f, off);
                    var scl = alongZ
                        ? new Vector3(0.62f, 0.30f, 0.44f)
                        : new Vector3(0.44f, 0.30f, 0.62f);
                    Make(PrimitiveType.Cube, $"3_Corbel{fc}_{(char)('A' + i)}",
                        pos, scl, Quaternion.Euler(0f, Tilt(1.2f), 0f),
                        graniteDark, 0.05f, 0.12f, false);
                }
            }
            Make(PrimitiveType.Cube, "3_DeckSlab", new Vector3(0f, 3.22f, 0f),
                new Vector3(4.95f, 0.24f, 4.95f), Quaternion.Euler(0f, Tilt(0.4f), 0f),
                graniteDark, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "3_DeckSurface", new Vector3(0f, 3.40f, 0f),
                new Vector3(4.55f, 0.12f, 4.55f), Quaternion.identity,
                graniteLight * 1.02f, 0.05f, 0.18f, false);

            // Parapet runs on all four sides, then the merlon loop above them.
            Make(PrimitiveType.Cube, "3_ParapetN", new Vector3(0f, 3.72f, -2.28f),
                new Vector3(4.90f, 0.52f, 0.28f), Quaternion.identity, granite, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "3_ParapetS", new Vector3(0f, 3.72f, 2.28f),
                new Vector3(4.90f, 0.52f, 0.28f), Quaternion.identity, granite, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "3_ParapetW", new Vector3(-2.28f, 3.72f, 0f),
                new Vector3(0.28f, 0.52f, 4.90f), Quaternion.identity, granite, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "3_ParapetE", new Vector3(2.28f, 3.72f, 0f),
                new Vector3(0.28f, 0.52f, 4.90f), Quaternion.identity, granite, 0.05f, 0.13f, false);

            float[] merlonOff = { -1.62f, -0.54f, 0.54f, 1.62f };
            for (int face = 0; face < 4; face++)
            {
                char fc = (char)('A' + face);
                bool alongZ = (face < 2);
                float sgn = (face % 2 == 0) ? 1f : -1f;
                for (int i = 0; i < merlonOff.Length; i++)
                {
                    var pos = alongZ
                        ? new Vector3(merlonOff[i], 4.22f, sgn * 2.28f)
                        : new Vector3(sgn * 2.28f, 4.22f, merlonOff[i]);
                    var scl = alongZ
                        ? new Vector3(0.72f, 0.50f, 0.34f)
                        : new Vector3(0.34f, 0.50f, 0.72f);
                    Make(PrimitiveType.Cube, $"3_Merlon{fc}_{(char)('A' + i)}",
                        pos, scl, Quaternion.Euler(0f, Tilt(1.5f), 0f),
                        graniteLight, 0.05f, 0.14f, false);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                float cx = (i % 2 == 0) ? -2.28f : 2.28f;
                float cz = (i < 2) ? 2.28f : -2.28f;
                Make(PrimitiveType.Cube, $"3_CornerMerlon_{(char)('A' + i)}",
                    new Vector3(cx, 4.28f, cz), new Vector3(0.62f, 0.64f, 0.62f),
                    Quaternion.Euler(0f, Tilt(2f), 0f), graniteLight * 0.96f, 0.05f, 0.14f, false);
            }

            // ── 4_ Props / accents ─────────────────────────────────────────
            // Faction banners hung either side of the entry porch.
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(-1.20f, 1.55f, 2.44f),
                new Vector3(0.42f, 1.40f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.2f)),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(1.20f, 1.55f, 2.44f),
                new Vector3(0.42f, 1.40f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.2f)),
                Color.white, 0.02f, 0.10f, false);

            // Deck pennant on a short iron pole at the front-west corner.
            Make(PrimitiveType.Cylinder, "4_PennantPole", new Vector3(-2.28f, 4.98f, 2.28f),
                new Vector3(0.06f, 0.52f, 0.06f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                iron, 0.85f, 0.35f, false);
            Make(PrimitiveType.Cube, "4_Stripe_3", new Vector3(-1.98f, 5.30f, 2.28f),
                new Vector3(0.60f, 0.20f, 0.035f), Quaternion.Euler(0f, Tilt(2.5f), 0f),
                Color.white, 0.02f, 0.10f, false);

            // Rubble spill and dressed replacement blocks stacked by the wall.
            for (int i = 0; i < 3; i++)
            {
                Make(PrimitiveType.Cube, $"4_Rubble_{(char)('A' + i)}",
                    new Vector3(-2.85f + i * 0.42f, 0.22f + Tilt(0.03f), -1.10f + i * 0.55f),
                    new Vector3(0.40f + Tilt(0.07f), 0.34f, 0.38f + Tilt(0.07f)),
                    Quaternion.Euler(Tilt(9f), (float)(rng.NextDouble() * 90.0), Tilt(9f)),
                    rubble * (0.92f + i * 0.04f), 0.05f, 0.09f, false);
            }
            Make(PrimitiveType.Cube, "4_BlockStack_A", new Vector3(2.80f, 0.24f, -1.35f),
                new Vector3(0.82f, 0.38f, 0.62f), Quaternion.Euler(0f, Tilt(4f), 0f),
                graniteLight * 0.9f, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "4_BlockStack_B", new Vector3(2.74f, 0.60f, -1.30f),
                new Vector3(0.70f, 0.34f, 0.55f), Quaternion.Euler(0f, Tilt(7f), Tilt(1.5f)),
                graniteLight, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "4_MalletHaft", new Vector3(2.55f, 0.98f, -1.05f),
                new Vector3(0.07f, 0.38f, 0.07f), Quaternion.Euler(0f, 0f, 26f),
                oakDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_MalletHead", new Vector3(2.24f, 1.28f, -1.05f),
                new Vector3(0.26f, 0.20f, 0.20f), Quaternion.Euler(0f, 0f, 26f),
                iron, 0.88f, 0.40f, false);

            // Timber shore braced against the west wall by a working crew.
            Make(PrimitiveType.Cylinder, "4_ShoreBeam", new Vector3(-3.05f, 1.05f, 1.05f),
                new Vector3(0.13f, 1.30f, 0.13f), Quaternion.Euler(0f, 0f, -34f),
                oakDark * 1.4f, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_ShoreFoot", new Vector3(-3.55f, 0.22f, 1.05f),
                new Vector3(0.40f, 0.28f, 0.40f), Quaternion.Euler(0f, Tilt(5f), 0f),
                rubble, 0.05f, 0.09f, false);

            // Iron fire-pot on the deck — the one dim emissive accent.
            Make(PrimitiveType.Cylinder, "4_FirePotStand", new Vector3(1.55f, 3.62f, -1.55f),
                new Vector3(0.20f, 0.22f, 0.20f), Quaternion.identity,
                iron, 0.85f, 0.40f, false);
            Make(PrimitiveType.Cylinder, "4_FirePotBowl", new Vector3(1.55f, 3.88f, -1.55f),
                new Vector3(0.60f, 0.14f, 0.60f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                ironLight, 0.85f, 0.38f, false);
            Make(PrimitiveType.Sphere, "4_FirePotCoals", new Vector3(1.55f, 4.00f, -1.55f),
                new Vector3(0.46f, 0.16f, 0.46f), Quaternion.identity,
                embers, 0.0f, 0.05f, true);

            return root;
        }
    }
}
