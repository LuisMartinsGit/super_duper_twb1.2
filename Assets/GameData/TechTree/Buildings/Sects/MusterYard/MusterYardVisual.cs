// Procedural visual for the Muster Yard - the Sect of War's stockade of
// training posts and armourers' racks. Built from primitives in the
// CreateProceduralSmelter idiom: named palette, per-part metallic /
// smoothness contrast, 1-3 degree tilts, prop vignettes, one low forge glow.
// Silhouette: an open packed-earth yard ringed by a low sharpened palisade,
// a row of pells (training posts) scarred by practice, weapon and shield
// racks along the west run, an armourer's lean-to with anvil and forge on the
// east, and a raised sergeant's platform at the back. Low and wide - this is
// a working yard, not a fortification.
// Part names carry leading rise-group numbers (1_ ground and palisade,
// 2_ posts and racks, 3_ lean-to and platform, 4_ props) for
// BuildingRiseData's staggered construction.
// Player-color accents: 4_Stripe_1 / 4_Stripe_2 gate banners and 4_Stripe_3
// platform pennant (BuildingFactionColorMarker tints anything named "stripe").
// Names deliberately avoid the "roof" substring so the faction marker never
// solid-paints the lean-to thatch.
// The orchestrator wires FitSelectionCollider / EntityReference / the faction
// marker after Build returns - this class only assembles geometry.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class MusterYardVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject($"MusterYard_{seed}");

            // Palette - packed earth, weathered oak, cold iron, one forge ember.
            var earth      = new Color(0.34f, 0.28f, 0.21f);
            var earthDark  = new Color(0.26f, 0.21f, 0.16f);
            var oak        = new Color(0.35f, 0.24f, 0.14f);
            var oakLight   = new Color(0.46f, 0.33f, 0.20f);
            var oakDark    = new Color(0.22f, 0.15f, 0.09f);
            var thatch     = new Color(0.52f, 0.43f, 0.24f);
            var iron       = new Color(0.17f, 0.17f, 0.18f);
            var ironLight  = new Color(0.30f, 0.30f, 0.32f);
            var leather    = new Color(0.30f, 0.20f, 0.13f);
            var embers     = new Color(0.95f, 0.42f, 0.11f);


            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);

            // Deterministic hand-built wobble in degrees.
            System.Func<float, float> Tilt = max => (float)(rng.NextDouble() * 2.0 - 1.0) * max;

            // -- 1_ Yard floor and palisade ring --------------------------------
            Make(PrimitiveType.Cube, "1_YardFloor", new Vector3(0f, 0.06f, 0f),
                new Vector3(5.60f, 0.12f, 5.60f), Quaternion.Euler(0f, Tilt(0.4f), 0f),
                earth, 0.02f, 0.06f, false);
            Make(PrimitiveType.Cube, "1_TrampledRing", new Vector3(0f, 0.13f, -0.35f),
                new Vector3(3.90f, 0.04f, 3.20f), Quaternion.Euler(0f, Tilt(1.2f), 0f),
                earthDark, 0.02f, 0.05f, false);

            // Sharpened stakes on three runs; the +Z face is the open gate.
            for (int i = 0; i < 9; i++)
            {
                float x = -2.60f + i * 0.65f;
                Make(PrimitiveType.Cylinder, $"1_StakeN_{i}",
                    new Vector3(x, 0.62f, -2.72f), new Vector3(0.16f, 0.56f, 0.16f),
                    Quaternion.Euler(Tilt(2.4f), Tilt(20f), Tilt(2.4f)),
                    i % 2 == 0 ? oak : oakDark, 0.04f, 0.09f, false);
            }
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -2.72f : 2.72f;
                for (int i = 0; i < 8; i++)
                {
                    float z = -2.40f + i * 0.62f;
                    Make(PrimitiveType.Cylinder, $"1_Stake{(side == 0 ? "W" : "E")}_{i}",
                        new Vector3(sx, 0.60f, z), new Vector3(0.15f, 0.54f, 0.15f),
                        Quaternion.Euler(Tilt(2.4f), Tilt(20f), Tilt(2.4f)),
                        i % 2 == 0 ? oakDark : oak, 0.04f, 0.09f, false);
                }
            }

            // Gate posts and lintel on the open (+Z) side.
            Make(PrimitiveType.Cylinder, "1_GatePostW", new Vector3(-1.15f, 0.95f, 2.70f),
                new Vector3(0.24f, 0.90f, 0.24f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                oakLight, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "1_GatePostE", new Vector3(1.15f, 0.95f, 2.70f),
                new Vector3(0.24f, 0.90f, 0.24f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                oakLight, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cube, "1_GateLintel", new Vector3(0f, 1.92f, 2.70f),
                new Vector3(2.85f, 0.22f, 0.20f), Quaternion.Euler(0f, 0f, Tilt(0.9f)),
                oak, 0.04f, 0.10f, false);

            // -- 2_ Pells and racks ---------------------------------------------
            // Four training posts in a row, each scarred by a different amount
            // of use - the leaning one has taken the most.
            float[] pellX = { -1.65f, -0.55f, 0.55f, 1.65f };
            for (int i = 0; i < pellX.Length; i++)
            {
                Make(PrimitiveType.Cylinder, $"2_Pell_{(char)('A' + i)}",
                    new Vector3(pellX[i], 0.86f, -0.30f + Tilt(0.08f)),
                    new Vector3(0.28f, 0.80f, 0.28f),
                    Quaternion.Euler(Tilt(4.5f), Tilt(30f), Tilt(4.5f)),
                    i == 2 ? oakDark : oak, 0.04f, 0.09f, false);
                Make(PrimitiveType.Cube, $"2_PellCollar_{(char)('A' + i)}",
                    new Vector3(pellX[i], 1.52f, -0.30f), new Vector3(0.34f, 0.10f, 0.34f),
                    Quaternion.Euler(0f, Tilt(6f), 0f), iron, 0.85f, 0.35f, false);
                Make(PrimitiveType.Cube, $"2_PellBase_{(char)('A' + i)}",
                    new Vector3(pellX[i], 0.20f, -0.30f), new Vector3(0.52f, 0.16f, 0.52f),
                    Quaternion.Euler(0f, Tilt(5f), 0f), earthDark, 0.02f, 0.06f, false);
            }

            // Weapon rack on the west run - a frame of spears and practice swords.
            Make(PrimitiveType.Cube, "2_RackFrameW", new Vector3(-2.25f, 0.72f, 0.70f),
                new Vector3(0.18f, 1.20f, 2.10f), Quaternion.Euler(0f, Tilt(1.2f), 0f),
                oakDark, 0.04f, 0.09f, false);
            Make(PrimitiveType.Cube, "2_RackShelfW", new Vector3(-2.18f, 1.18f, 0.70f),
                new Vector3(0.36f, 0.10f, 2.05f), Quaternion.identity,
                oak, 0.04f, 0.09f, false);
            for (int i = 0; i < 6; i++)
            {
                float z = -0.18f + i * 0.36f;
                Make(PrimitiveType.Cylinder, $"2_Spear_{i}",
                    new Vector3(-2.05f, 1.28f, z), new Vector3(0.05f, 0.86f, 0.05f),
                    Quaternion.Euler(Tilt(3f), 0f, 6f + Tilt(3f)),
                    oakLight, 0.04f, 0.10f, false);
                Make(PrimitiveType.Cube, $"2_SpearHead_{i}",
                    new Vector3(-1.96f, 2.18f, z), new Vector3(0.07f, 0.26f, 0.05f),
                    Quaternion.Euler(0f, 0f, 6f), ironLight, 0.88f, 0.45f, false);
            }

            // Shield rack leaning against the north palisade.
            for (int i = 0; i < 4; i++)
            {
                Make(PrimitiveType.Cylinder, $"2_Shield_{(char)('A' + i)}",
                    new Vector3(-1.20f + i * 0.80f, 0.52f, -2.35f),
                    new Vector3(0.62f, 0.06f, 0.62f),
                    Quaternion.Euler(76f + Tilt(4f), Tilt(12f), 0f),
                    i % 2 == 0 ? leather : oakDark, 0.06f, 0.14f, false);
                Make(PrimitiveType.Sphere, $"2_ShieldBoss_{(char)('A' + i)}",
                    new Vector3(-1.20f + i * 0.80f, 0.56f, -2.28f),
                    new Vector3(0.16f, 0.10f, 0.16f), Quaternion.identity,
                    ironLight, 0.90f, 0.48f, false);
            }

            // -- 3_ Armourer's lean-to and sergeant's platform -------------------
            Make(PrimitiveType.Cube, "3_LeanToPostA", new Vector3(1.55f, 0.85f, 1.55f),
                new Vector3(0.16f, 1.50f, 0.16f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                oakDark, 0.04f, 0.09f, false);
            Make(PrimitiveType.Cube, "3_LeanToPostB", new Vector3(2.45f, 0.62f, 1.55f),
                new Vector3(0.16f, 1.05f, 0.16f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                oakDark, 0.04f, 0.09f, false);
            Make(PrimitiveType.Cube, "3_LeanToPostC", new Vector3(1.55f, 0.85f, -0.35f),
                new Vector3(0.16f, 1.50f, 0.16f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                oakDark, 0.04f, 0.09f, false);
            Make(PrimitiveType.Cube, "3_LeanToPostD", new Vector3(2.45f, 0.62f, -0.35f),
                new Vector3(0.16f, 1.05f, 0.16f), Quaternion.Euler(Tilt(1.5f), 0f, Tilt(1.5f)),
                oakDark, 0.04f, 0.09f, false);
            Make(PrimitiveType.Cube, "3_LeanToThatch", new Vector3(2.02f, 1.62f, 0.60f),
                new Vector3(1.35f, 0.14f, 2.30f), Quaternion.Euler(0f, Tilt(1f), 21f),
                thatch, 0.02f, 0.07f, false);
            Make(PrimitiveType.Cube, "3_LeanToBench", new Vector3(2.05f, 0.62f, 0.62f),
                new Vector3(0.95f, 0.12f, 1.90f), Quaternion.Euler(0f, Tilt(1.4f), 0f),
                oak, 0.04f, 0.09f, false);

            // Sergeant's platform at the back of the yard.
            Make(PrimitiveType.Cube, "3_PlatformDeck", new Vector3(0f, 0.52f, -2.05f),
                new Vector3(2.30f, 0.16f, 0.95f), Quaternion.Euler(0f, Tilt(0.6f), 0f),
                oakLight, 0.04f, 0.11f, false);
            Make(PrimitiveType.Cube, "3_PlatformStep", new Vector3(0f, 0.26f, -1.48f),
                new Vector3(1.40f, 0.14f, 0.42f), Quaternion.identity,
                oak, 0.04f, 0.10f, false);
            for (int i = 0; i < 2; i++)
            {
                Make(PrimitiveType.Cube, $"3_PlatformLeg_{(char)('A' + i)}",
                    new Vector3(i == 0 ? -0.95f : 0.95f, 0.24f, -2.05f),
                    new Vector3(0.18f, 0.48f, 0.18f), Quaternion.identity,
                    oakDark, 0.04f, 0.09f, false);
            }

            // -- 4_ Props and accents -------------------------------------------
            // Anvil and forge under the lean-to - the one emissive accent.
            Make(PrimitiveType.Cube, "4_AnvilBlock", new Vector3(1.95f, 0.30f, -0.05f),
                new Vector3(0.34f, 0.36f, 0.34f), Quaternion.Euler(0f, Tilt(6f), 0f),
                oakDark, 0.04f, 0.09f, false);
            Make(PrimitiveType.Cube, "4_Anvil", new Vector3(1.95f, 0.58f, -0.05f),
                new Vector3(0.46f, 0.20f, 0.24f), Quaternion.Euler(0f, Tilt(4f), 0f),
                iron, 0.90f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "4_ForgeRing", new Vector3(2.35f, 0.34f, 1.35f),
                new Vector3(0.62f, 0.30f, 0.62f), Quaternion.identity,
                ironLight, 0.85f, 0.38f, false);
            Make(PrimitiveType.Sphere, "4_ForgeCoals", new Vector3(2.35f, 0.62f, 1.35f),
                new Vector3(0.46f, 0.16f, 0.46f), Quaternion.identity,
                embers, 0.0f, 0.05f, true);
            Make(PrimitiveType.Cylinder, "4_HammerHaft", new Vector3(1.72f, 0.82f, 0.30f),
                new Vector3(0.06f, 0.30f, 0.06f), Quaternion.Euler(0f, 0f, 62f),
                oakLight, 0.04f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_HammerHead", new Vector3(1.44f, 0.94f, 0.30f),
                new Vector3(0.22f, 0.16f, 0.16f), Quaternion.Euler(0f, 0f, 62f),
                iron, 0.90f, 0.45f, false);

            // Slaked barrel and a spill of practice staves by the gate.
            Make(PrimitiveType.Cylinder, "4_Barrel", new Vector3(-2.10f, 0.42f, 2.20f),
                new Vector3(0.52f, 0.40f, 0.52f), Quaternion.Euler(Tilt(2f), 0f, Tilt(2f)),
                oak, 0.04f, 0.11f, false);
            Make(PrimitiveType.Cylinder, "4_BarrelHoop", new Vector3(-2.10f, 0.62f, 2.20f),
                new Vector3(0.56f, 0.05f, 0.56f), Quaternion.identity,
                iron, 0.88f, 0.40f, false);
            for (int i = 0; i < 3; i++)
            {
                Make(PrimitiveType.Cylinder, $"4_Stave_{(char)('A' + i)}",
                    new Vector3(-1.55f + i * 0.16f, 0.16f + i * 0.10f, 1.95f),
                    new Vector3(0.05f, 0.62f, 0.05f),
                    Quaternion.Euler(88f, Tilt(24f), Tilt(6f)),
                    oakLight * (0.94f + i * 0.03f), 0.04f, 0.10f, false);
            }

            // Faction banners on the gate posts and the sergeant's pennant.
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(-1.15f, 1.38f, 2.60f),
                new Vector3(0.38f, 0.98f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.2f)),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(1.15f, 1.38f, 2.60f),
                new Vector3(0.38f, 0.98f, 0.05f), Quaternion.Euler(0f, 0f, Tilt(1.2f)),
                Color.white, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "4_PennantPole", new Vector3(-1.05f, 1.35f, -2.05f),
                new Vector3(0.07f, 0.82f, 0.07f), Quaternion.Euler(Tilt(1.8f), 0f, Tilt(1.8f)),
                iron, 0.85f, 0.35f, false);
            Make(PrimitiveType.Cube, "4_Stripe_3", new Vector3(-0.72f, 2.02f, -2.05f),
                new Vector3(0.62f, 0.22f, 0.035f), Quaternion.Euler(0f, Tilt(2.5f), 0f),
                Color.white, 0.02f, 0.10f, false);

            return root;
        }
    }
}
