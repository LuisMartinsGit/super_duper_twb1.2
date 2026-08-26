// Procedural visual for the Reliquary — the Sect of Antiquity's vaulted stone
// archive (the holy librarians). Built entirely from primitives in the
// CreateProceduralSmelter idiom: named palette, per-part metallic/smoothness
// contrast, 1-3 degree tilts, prop vignettes, one soft emissive lantern.
// Silhouette: a stepped limestone plinth, a heavy rectangular archive block
// braced by battered buttresses, tall narrow shuttered window slots with brass
// sills, and a barrel vault laid along Z with ribbed courses and a brass
// oculus over the door. Outdoor lectern and a scroll rack sit on the steps.
// Part names carry leading rise-group numbers (1_ plinth, 2_ walls/frame,
// 3_ vault, 4_ props) for BuildingRiseData's staggered construction.
// Player-color accents: 4_Stripe_1 / 4_Stripe_2 door hangings and 4_Stripe_3
// ridge pennant (BuildingFactionColorMarker tints anything named "stripe").
// No part name contains "vault-roof" or the "roof" substring — that name rule
// solid-paints a part in the faction color, which would flatten the stonework.
// The orchestrator wires FitSelectionCollider / EntityReference / the faction
// marker after Build returns — this class only assembles geometry.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class ReliquaryVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject($"Reliquary_{seed}");

            // Palette — pale limestone archive, brass fittings, oak and parchment.
            var limestone      = new Color(0.76f, 0.72f, 0.62f);
            var limestoneLight = new Color(0.84f, 0.80f, 0.71f);
            var limestoneDark  = new Color(0.55f, 0.51f, 0.43f);
            var stoneShadow    = new Color(0.39f, 0.36f, 0.30f);
            var brass          = new Color(0.72f, 0.56f, 0.22f);
            var brassDark      = new Color(0.47f, 0.36f, 0.14f);
            var oak            = new Color(0.31f, 0.21f, 0.13f);
            var oakDark        = new Color(0.22f, 0.14f, 0.08f);
            var slitDark       = new Color(0.06f, 0.06f, 0.07f);
            var parchment      = new Color(0.86f, 0.80f, 0.62f);
            var candle         = new Color(0.98f, 0.80f, 0.45f);


            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);

            // Deterministic hand-built wobble in degrees.
            System.Func<float, float> Tilt = max => (float)(rng.NextDouble() * 2.0 - 1.0) * max;

            // ── 1_ Plinth / ground ─────────────────────────────────────────
            // Two stepped courses; the archive block sits on the upper one and
            // the entrance stair spills off the front (+Z) face.
            Make(PrimitiveType.Cube, "1_PlinthLower", new Vector3(0f, 0.16f, 0f),
                new Vector3(5.0f, 0.32f, 6.4f), Quaternion.Euler(0f, Tilt(0.6f), 0f),
                stoneShadow, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "1_PlinthUpper", new Vector3(0f, 0.46f, 0f),
                new Vector3(4.5f, 0.28f, 5.9f), Quaternion.identity,
                limestoneDark, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "1_StepA", new Vector3(0f, 0.14f, 3.42f),
                new Vector3(2.20f, 0.28f, 0.44f), Quaternion.Euler(0f, Tilt(0.8f), 0f),
                limestoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "1_StepB", new Vector3(0f, 0.38f, 3.08f),
                new Vector3(1.95f, 0.24f, 0.40f), Quaternion.Euler(0f, Tilt(0.8f), 0f),
                limestone, 0.05f, 0.14f, false);

            // Rough footing stones half-sunk at the plinth corners.
            for (int i = 0; i < 4; i++)
            {
                float fx = (i % 2 == 0) ? -2.22f : 2.22f;
                float fz = (i < 2) ? 2.92f : -2.92f;
                Make(PrimitiveType.Cube, $"1_FootStone_{i}",
                    new Vector3(fx, 0.19f + Tilt(0.03f), fz),
                    new Vector3(0.72f + Tilt(0.08f), 0.38f, 0.66f + Tilt(0.08f)),
                    Quaternion.Euler(Tilt(2f), (float)(rng.NextDouble() * 90.0), Tilt(2f)),
                    stoneShadow * 0.94f, 0.05f, 0.10f, false);
            }

            // ── 2_ Walls / buttresses / openings ───────────────────────────
            Make(PrimitiveType.Cube, "2_ArchiveBody", new Vector3(0f, 1.98f, 0f),
                new Vector3(3.6f, 2.72f, 5.2f), Quaternion.identity,
                limestone, 0.04f, 0.14f, false);
            Make(PrimitiveType.Cube, "2_BaseCourse", new Vector3(0f, 0.72f, 0f),
                new Vector3(3.86f, 0.20f, 5.46f), Quaternion.identity,
                limestoneDark, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "2_StringCourse", new Vector3(0f, 3.26f, 0f),
                new Vector3(3.86f, 0.16f, 5.44f), Quaternion.identity,
                limestoneDark, 0.05f, 0.13f, false);

            // Three battered buttresses per long side, each leaning its top a
            // touch back into the wall, with a sloped weathering cap on top.
            for (int side = 0; side < 2; side++)
            {
                float sx = (side == 0) ? -1.98f : 1.98f;
                float lean = (side == 0) ? 2.2f : -2.2f;
                char sc = (side == 0) ? 'W' : 'E';
                for (int i = 0; i < 3; i++)
                {
                    float bz = -1.85f + i * 1.85f;
                    Make(PrimitiveType.Cube, $"2_Buttress{sc}_{(char)('A' + i)}",
                        new Vector3(sx, 1.72f, bz), new Vector3(0.60f, 2.30f, 0.74f),
                        Quaternion.Euler(0f, Tilt(0.7f), lean + Tilt(0.5f)),
                        limestone * 0.95f, 0.04f, 0.12f, false);
                    Make(PrimitiveType.Cube, $"2_ButtressCap{sc}_{(char)('A' + i)}",
                        new Vector3(sx - lean * 0.06f, 2.94f, bz),
                        new Vector3(0.66f, 0.22f, 0.80f),
                        Quaternion.Euler(0f, 0f, lean * 6f), limestoneDark, 0.05f, 0.16f, false);
                }
            }

            // Tall narrow shuttered window slots — two per long side, each with
            // a brass sill and a hinged oak shutter leaf beside it.
            for (int side = 0; side < 2; side++)
            {
                float sx = (side == 0) ? -1.83f : 1.83f;
                char sc = (side == 0) ? 'W' : 'E';
                for (int i = 0; i < 2; i++)
                {
                    float wz = (i == 0) ? -0.92f : 0.92f;
                    Make(PrimitiveType.Cube, $"2_WindowSlot{sc}_{(char)('A' + i)}",
                        new Vector3(sx, 2.05f, wz), new Vector3(0.12f, 1.60f, 0.34f),
                        Quaternion.identity, slitDark, 0.0f, 0.05f, false);
                    Make(PrimitiveType.Cube, $"2_WindowSill{sc}_{(char)('A' + i)}",
                        new Vector3(sx, 1.20f, wz), new Vector3(0.17f, 0.08f, 0.50f),
                        Quaternion.Euler(0f, 0f, Tilt(0.8f)), brass, 0.75f, 0.42f, false);
                    Make(PrimitiveType.Cube, $"2_Shutter{sc}_{(char)('A' + i)}",
                        new Vector3(sx + (side == 0 ? -0.09f : 0.09f), 2.05f, wz + 0.34f),
                        new Vector3(0.07f, 1.52f, 0.30f),
                        Quaternion.Euler(0f, (side == 0 ? -22f : 22f) + Tilt(2f), 0f),
                        oak, 0.05f, 0.12f, false);
                }
            }

            // Front face (+Z): arched oak door, brass banding, flanking pilasters
            // and a narrow slot outboard of each pilaster.
            Make(PrimitiveType.Cube, "2_Door", new Vector3(0f, 1.32f, 2.64f),
                new Vector3(1.20f, 1.90f, 0.10f), Quaternion.identity, oakDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cylinder, "2_DoorArch", new Vector3(0f, 2.27f, 2.62f),
                new Vector3(1.44f, 0.10f, 1.44f), Quaternion.Euler(90f, 0f, 0f),
                limestoneLight, 0.04f, 0.16f, false);
            Make(PrimitiveType.Cube, "2_DoorBand_A", new Vector3(0f, 1.72f, 2.70f),
                new Vector3(1.16f, 0.10f, 0.04f), Quaternion.Euler(0f, 0f, Tilt(0.6f)),
                brassDark, 0.80f, 0.40f, false);
            Make(PrimitiveType.Cube, "2_DoorBand_B", new Vector3(0f, 0.94f, 2.70f),
                new Vector3(1.16f, 0.10f, 0.04f), Quaternion.Euler(0f, 0f, Tilt(0.6f)),
                brassDark, 0.80f, 0.40f, false);
            Make(PrimitiveType.Sphere, "2_DoorBoss", new Vector3(0.42f, 1.32f, 2.72f),
                new Vector3(0.13f, 0.13f, 0.08f), Quaternion.identity, brass, 0.85f, 0.50f, false);
            for (int i = 0; i < 2; i++)
            {
                float px = (i == 0) ? -0.95f : 0.95f;
                Make(PrimitiveType.Cube, $"2_Pilaster_{(char)('A' + i)}",
                    new Vector3(px, 1.72f, 2.66f), new Vector3(0.22f, 2.20f, 0.16f),
                    Quaternion.Euler(0f, 0f, Tilt(0.7f)), limestoneLight, 0.04f, 0.15f, false);
                Make(PrimitiveType.Cube, $"2_PilasterCap_{(char)('A' + i)}",
                    new Vector3(px, 2.90f, 2.66f), new Vector3(0.32f, 0.16f, 0.24f),
                    Quaternion.identity, limestoneDark, 0.05f, 0.16f, false);
                float wx = (i == 0) ? -1.56f : 1.56f;
                Make(PrimitiveType.Cube, $"2_WindowSlotFront_{(char)('A' + i)}",
                    new Vector3(wx, 2.02f, 2.63f), new Vector3(0.24f, 1.30f, 0.10f),
                    Quaternion.identity, slitDark, 0.0f, 0.05f, false);
            }

            // ── 3_ Barrel vault laid along Z ───────────────────────────────
            // The cylinder's own end caps read as the semicircular gables that
            // rise above the wall head; ribs band it at four stations.
            Make(PrimitiveType.Cylinder, "3_Vault", new Vector3(0f, 3.38f, 0f),
                new Vector3(3.42f, 2.72f, 3.42f), Quaternion.Euler(90f, 0f, Tilt(0.4f)),
                limestone * 1.04f, 0.04f, 0.17f, false);
            float[] ribZ = { -2.30f, -0.78f, 0.78f, 2.30f };
            for (int i = 0; i < ribZ.Length; i++)
            {
                Make(PrimitiveType.Cylinder, $"3_VaultRib_{(char)('A' + i)}",
                    new Vector3(0f, 3.38f, ribZ[i]), new Vector3(3.56f, 0.09f, 3.56f),
                    Quaternion.Euler(90f, 0f, Tilt(0.5f)), limestoneDark, 0.05f, 0.13f, false);
            }
            Make(PrimitiveType.Cube, "3_RidgeCap", new Vector3(0f, 5.10f, 0f),
                new Vector3(0.36f, 0.14f, 5.62f), Quaternion.identity,
                limestoneDark, 0.05f, 0.15f, false);

            // Brass-ringed oculus in the front gable, above the door arch.
            Make(PrimitiveType.Cylinder, "3_OculusRing", new Vector3(0f, 4.22f, 2.76f),
                new Vector3(0.98f, 0.06f, 0.98f), Quaternion.Euler(90f, 0f, 0f),
                brass, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "3_Oculus", new Vector3(0f, 4.22f, 2.80f),
                new Vector3(0.74f, 0.05f, 0.74f), Quaternion.Euler(90f, 0f, 0f),
                slitDark, 0.0f, 0.06f, false);

            // Two corbels catching the vault overhang at the front gable.
            for (int i = 0; i < 2; i++)
            {
                float cx = (i == 0) ? -1.45f : 1.45f;
                Make(PrimitiveType.Cube, $"3_GableCorbel_{(char)('A' + i)}",
                    new Vector3(cx, 3.42f, 2.70f), new Vector3(0.34f, 0.30f, 0.42f),
                    Quaternion.Euler(0f, 0f, Tilt(1.2f)), limestoneDark, 0.05f, 0.13f, false);
            }

            // ── 4_ Props / accents ─────────────────────────────────────────
            // Outdoor reading lectern on the east side of the stair.
            Make(PrimitiveType.Cylinder, "4_LecternPost", new Vector3(1.55f, 0.78f, 3.02f),
                new Vector3(0.16f, 0.46f, 0.16f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)),
                oak, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "4_LecternSlope", new Vector3(1.55f, 1.28f, 3.02f),
                new Vector3(0.54f, 0.06f, 0.42f), Quaternion.Euler(-28f, Tilt(3f), 0f),
                oakDark, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "4_LecternBook", new Vector3(1.55f, 1.34f, 2.99f),
                new Vector3(0.38f, 0.05f, 0.30f), Quaternion.Euler(-28f, Tilt(3f), 0f),
                parchment, 0.02f, 0.08f, false);

            // Scroll rack on the west side — two uprights, two shelves, scrolls.
            for (int i = 0; i < 2; i++)
            {
                float uz = (i == 0) ? 2.62f : 3.38f;
                Make(PrimitiveType.Cylinder, $"4_RackUpright_{(char)('A' + i)}",
                    new Vector3(-1.62f, 0.87f, uz), new Vector3(0.10f, 0.55f, 0.10f),
                    Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)), oak, 0.05f, 0.10f, false);
            }
            Make(PrimitiveType.Cube, "4_RackShelf_A", new Vector3(-1.62f, 0.72f, 3.00f),
                new Vector3(0.42f, 0.06f, 0.88f), Quaternion.Euler(0f, Tilt(1.2f), 0f),
                oakDark, 0.05f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_RackShelf_B", new Vector3(-1.62f, 1.20f, 3.00f),
                new Vector3(0.42f, 0.06f, 0.88f), Quaternion.Euler(0f, Tilt(1.2f), 0f),
                oakDark, 0.05f, 0.10f, false);
            for (int i = 0; i < 4; i++)
            {
                float sy = (i < 2) ? 0.83f : 1.31f;
                float sz = 2.82f + (i % 2) * 0.36f;
                Make(PrimitiveType.Cylinder, $"4_Scroll_{(char)('A' + i)}",
                    new Vector3(-1.62f + Tilt(0.05f), sy, sz),
                    new Vector3(0.11f, 0.28f, 0.11f),
                    Quaternion.Euler(90f + Tilt(2f), 0f, 0f),
                    parchment * (0.92f + i * 0.02f), 0.02f, 0.08f, false);
            }

            // Faction-color door hangings (tinted by the faction marker).
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(-1.28f, 1.70f, 2.70f),
                new Vector3(0.34f, 1.25f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.5f)),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(1.28f, 1.70f, 2.70f),
                new Vector3(0.34f, 1.25f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.5f)),
                Color.white, 0.02f, 0.10f, false);

            // Ridge pennant on a short brass pole above the front gable.
            Make(PrimitiveType.Cylinder, "4_PennantPole", new Vector3(0f, 5.56f, 2.50f),
                new Vector3(0.05f, 0.48f, 0.05f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                brassDark, 0.80f, 0.40f, false);
            Make(PrimitiveType.Cube, "4_Stripe_3", new Vector3(0.30f, 5.88f, 2.50f),
                new Vector3(0.56f, 0.18f, 0.035f), Quaternion.Euler(0f, Tilt(2.5f), 0f),
                Color.white, 0.02f, 0.10f, false);

            // Hanging candle lantern over the door — the single emissive accent.
            Make(PrimitiveType.Cube, "4_LanternArm", new Vector3(0f, 2.62f, 2.78f),
                new Vector3(0.05f, 0.05f, 0.26f), Quaternion.identity, brassDark, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cube, "4_Lantern", new Vector3(0f, 2.44f, 2.90f),
                new Vector3(0.14f, 0.19f, 0.14f), Quaternion.Euler(0f, Tilt(6f), 0f),
                candle, 0.05f, 0.15f, true);

            // Weathered stone tablet leaning against the east buttress.
            Make(PrimitiveType.Cube, "4_StoneTablet", new Vector3(2.36f, 0.94f, -0.42f),
                new Vector3(0.58f, 1.05f, 0.14f), Quaternion.Euler(0f, Tilt(4f), -13f),
                limestoneDark, 0.04f, 0.11f, false);
            Make(PrimitiveType.Cube, "4_TabletFoot", new Vector3(2.44f, 0.42f, -0.42f),
                new Vector3(0.42f, 0.20f, 0.34f), Quaternion.Euler(0f, Tilt(6f), 0f),
                stoneShadow, 0.04f, 0.10f, false);

            // Two brass-lidded ink urns flanking the entrance stair.
            for (int i = 0; i < 2; i++)
            {
                float ux = (i == 0) ? -0.95f : 0.95f;
                char uc = (char)('A' + i);
                Make(PrimitiveType.Cylinder, $"4_Urn_{uc}", new Vector3(ux, 0.53f, 3.30f),
                    new Vector3(0.32f, 0.21f, 0.32f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)),
                    limestoneDark, 0.04f, 0.18f, false);
                Make(PrimitiveType.Sphere, $"4_UrnLid_{uc}", new Vector3(ux, 0.78f, 3.30f),
                    new Vector3(0.26f, 0.12f, 0.26f), Quaternion.identity,
                    brass, 0.82f, 0.48f, false);
            }

            return root;
        }
    }
}
