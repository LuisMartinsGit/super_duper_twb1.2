// Procedural Renewal Fortress (Raise Anew III): a genuine keep on an 8 x 8 m
// footprint. Tall curtain walls with a merloned walk, four ROUND corner
// towers with conical slate roofs, a twin-towered gatehouse on the south
// face, and a buttressed central keep that rises well above the towers with
// its own parapet, brazier and banner. Same limestone family as the Tower and
// Fortification, but the round towers and slate cones mark it as the top of
// the ladder at a glance.
//
// Faction accents: "Stripe_Banner" (keep), "Stripe_Gate" (gatehouse cloth)
// and one "Stripe_Pennant" per corner tower. This building SPAWNS FINISHED,
// so part names carry no rise numbers. Standalone static builder; see
// RenewalTowerVisual for the orchestration contract.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class RenewalFortressVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("RenewalFortress");

            var stone      = new Color(0.72f, 0.68f, 0.60f);
            var stoneLight = new Color(0.80f, 0.76f, 0.68f);
            var stoneDark  = new Color(0.52f, 0.48f, 0.42f);
            var slitDark   = new Color(0.06f, 0.05f, 0.05f);
            var slate      = new Color(0.28f, 0.30f, 0.36f);   // tower roofs
            var court      = new Color(0.44f, 0.40f, 0.33f);
            var beam       = new Color(0.34f, 0.23f, 0.14f);
            var iron       = new Color(0.19f, 0.18f, 0.17f);
            var embers     = new Color(0.95f, 0.42f, 0.10f);
            var flame      = new Color(1.00f, 0.66f, 0.22f);

            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);
            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            const float half = 3.6f;      // curtain-wall half-extent
            const float wallH = 3.4f;

            // Plinth and courtyard.
            Make(PrimitiveType.Cube, "Plinth", new Vector3(0f, 0.18f, 0f),
                new Vector3(7.9f, 0.36f, 7.9f), Quaternion.Euler(0f, Jit(0.6f), 0f),
                stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "Courtyard", new Vector3(0f, 0.38f, 0f),
                new Vector3(7.0f, 0.06f, 7.0f), Quaternion.identity, court, 0.02f, 0.06f, false);

            // Curtain walls; the south face is the gatehouse.
            for (int face = 0; face < 4; face++)
            {
                bool alongX = face % 2 == 0;
                float side = face < 2 ? 1f : -1f;
                bool isGate = face == 0;
                if (!isGate)
                {
                    var p = alongX ? new Vector3(0f, wallH * 0.5f + 0.36f, side * half)
                                   : new Vector3(side * half, wallH * 0.5f + 0.36f, 0f);
                    var s = alongX ? new Vector3(half * 2f, wallH, 0.7f) : new Vector3(0.7f, wallH, half * 2f);
                    Make(PrimitiveType.Cube, $"Wall_{face}", p, s, Quaternion.identity, stone, 0.05f, 0.14f, false);
                    // A shallow buttress at the wall's midpoint.
                    var bp = alongX ? new Vector3(0f, 1.6f, side * (half + 0.45f)) : new Vector3(side * (half + 0.45f), 1.6f, 0f);
                    var bs = alongX ? new Vector3(0.9f, 2.5f, 0.35f) : new Vector3(0.35f, 2.5f, 0.9f);
                    Make(PrimitiveType.Cube, $"Buttress_{face}", bp, bs, Quaternion.identity, stoneDark, 0.05f, 0.13f, false);
                }
                else
                {
                    Make(PrimitiveType.Cube, "Wall_0_L", new Vector3(-2.45f, wallH * 0.5f + 0.36f, half),
                        new Vector3(2.3f, wallH, 0.7f), Quaternion.identity, stone, 0.05f, 0.14f, false);
                    Make(PrimitiveType.Cube, "Wall_0_R", new Vector3(2.45f, wallH * 0.5f + 0.36f, half),
                        new Vector3(2.3f, wallH, 0.7f), Quaternion.identity, stone, 0.05f, 0.14f, false);
                    // Gatehouse: two flanking square towers and the arch between.
                    for (int g = 0; g < 2; g++)
                    {
                        float gx = (g == 0) ? -1.25f : 1.25f;
                        Make(PrimitiveType.Cube, $"GateTower_{g}", new Vector3(gx, 2.5f, half + 0.1f),
                            new Vector3(1.1f, 4.9f, 1.2f), Quaternion.identity, stone * 1.02f, 0.05f, 0.14f, false);
                        Make(PrimitiveType.Cube, $"GateTowerCap_{g}", new Vector3(gx, 5.0f, half + 0.1f),
                            new Vector3(1.3f, 0.14f, 1.4f), Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
                        Make(PrimitiveType.Cube, $"GateTowerMerlon_{g}_A", new Vector3(gx - 0.4f, 5.3f, half + 0.1f),
                            new Vector3(0.36f, 0.44f, 0.9f), Quaternion.identity, stoneLight, 0.05f, 0.14f, false);
                        Make(PrimitiveType.Cube, $"GateTowerMerlon_{g}_B", new Vector3(gx + 0.4f, 5.3f, half + 0.1f),
                            new Vector3(0.36f, 0.44f, 0.9f), Quaternion.identity, stoneLight, 0.05f, 0.14f, false);
                    }
                    Make(PrimitiveType.Cube, "GateLintel", new Vector3(0f, 3.15f, half),
                        new Vector3(1.6f, 1.2f, 0.7f), Quaternion.identity, stoneDark, 0.05f, 0.13f, false);
                    Make(PrimitiveType.Cube, "GateVoid", new Vector3(0f, 1.45f, half + 0.02f),
                        new Vector3(1.4f, 2.2f, 0.72f), Quaternion.identity, slitDark, 0f, 0.05f, false);
                    Make(PrimitiveType.Cube, "Stripe_Gate", new Vector3(0f, 2.75f, half + 0.38f),
                        new Vector3(1.5f, 0.30f, 0.04f), Quaternion.identity, Color.white, 0f, 0.12f, false);
                }

                var bandP = alongX ? new Vector3(0f, wallH + 0.40f, side * half) : new Vector3(side * half, wallH + 0.40f, 0f);
                var bandS = alongX ? new Vector3(half * 2f + 0.1f, 0.10f, 0.8f) : new Vector3(0.8f, 0.10f, half * 2f + 0.1f);
                Make(PrimitiveType.Cube, $"WallBand_{face}", bandP, bandS, Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
                for (int m = 0; m < 6; m++)
                {
                    float t = -2.5f + m * 1.0f;
                    if (isGate && Mathf.Abs(t) < 1.6f) continue;   // gatehouse towers sit here
                    var mp = alongX ? new Vector3(t, wallH + 0.70f, side * (half + 0.08f))
                                    : new Vector3(side * (half + 0.08f), wallH + 0.70f, t);
                    Make(PrimitiveType.Cube, $"WallMerlon_{face}_{m}", mp,
                        new Vector3(0.46f, 0.48f, 0.46f), Quaternion.identity, stoneLight, 0.05f, 0.14f, false);
                }
            }

            // Four round corner towers with conical slate roofs and pennants.
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                var c = new Vector3(sx * half, 0f, sz * half);
                Make(PrimitiveType.Cylinder, $"Tower_{i}", c + new Vector3(0f, 2.9f, 0f),
                    new Vector3(1.9f, 2.9f, 1.9f), Quaternion.identity, stone * 1.03f, 0.05f, 0.15f, false);
                Make(PrimitiveType.Cylinder, $"TowerRing_{i}", c + new Vector3(0f, 5.75f, 0f),
                    new Vector3(2.15f, 0.12f, 2.15f), Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
                Make(PrimitiveType.Cylinder, $"TowerParapet_{i}", c + new Vector3(0f, 6.05f, 0f),
                    new Vector3(2.0f, 0.22f, 2.0f), Quaternion.identity, stoneLight, 0.05f, 0.15f, false);
                // Conical roof: three stacked, narrowing discs read as a cone at RTS distance.
                Make(PrimitiveType.Cylinder, $"TowerRoofA_{i}", c + new Vector3(0f, 6.55f, 0f),
                    new Vector3(1.9f, 0.30f, 1.9f), Quaternion.identity, slate, 0.05f, 0.30f, false);
                Make(PrimitiveType.Cylinder, $"TowerRoofB_{i}", c + new Vector3(0f, 7.10f, 0f),
                    new Vector3(1.35f, 0.30f, 1.35f), Quaternion.identity, slate * 1.05f, 0.05f, 0.30f, false);
                Make(PrimitiveType.Cylinder, $"TowerRoofC_{i}", c + new Vector3(0f, 7.65f, 0f),
                    new Vector3(0.75f, 0.30f, 0.75f), Quaternion.identity, slate * 1.1f, 0.05f, 0.30f, false);
                Make(PrimitiveType.Cylinder, $"TowerSpire_{i}", c + new Vector3(0f, 8.25f, 0f),
                    new Vector3(0.06f, 0.45f, 0.06f), Quaternion.identity, iron, 0.6f, 0.4f, false);
                Make(PrimitiveType.Cube, $"Stripe_Pennant_{i}", c + new Vector3(0.24f, 8.55f, 0f),
                    new Vector3(0.44f, 0.26f, 0.03f), Quaternion.Euler(Jit(2f), Jit(6f), -5f), Color.white, 0f, 0.12f, false);
                Make(PrimitiveType.Cube, $"TowerSlit_{i}", c + new Vector3(-sx * 0.96f, 3.4f, 0f),
                    new Vector3(0.08f, 0.7f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            }

            // The keep: a buttressed square mass rising well above the towers.
            Make(PrimitiveType.Cube, "KeepBase", new Vector3(0f, 0.9f, 0f),
                new Vector3(3.9f, 1.0f, 3.9f), Quaternion.identity, stoneDark, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "KeepShaftA", new Vector3(0f, 4.4f, 0f),
                new Vector3(3.4f, 6.0f, 3.4f), Quaternion.identity, stone, 0.05f, 0.15f, false);
            Make(PrimitiveType.Cube, "KeepCourse", new Vector3(0f, 7.45f, 0f),
                new Vector3(3.5f, 0.12f, 3.5f), Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "KeepShaftB", new Vector3(0f, 9.2f, 0f),
                new Vector3(3.2f, 3.4f, 3.2f), Quaternion.identity, stoneLight, 0.05f, 0.16f, false);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                Make(PrimitiveType.Cube, $"KeepButtress_{i}", new Vector3(sx * 1.75f, 3.2f, sz * 1.75f),
                    new Vector3(0.6f, 5.6f, 0.6f), Quaternion.identity, stoneDark * 1.06f, 0.05f, 0.13f, false);
                Make(PrimitiveType.Cube, $"KeepQuoin_{i}", new Vector3(sx * 1.6f, 9.2f, sz * 1.6f),
                    new Vector3(0.26f, 3.4f, 0.26f), Quaternion.identity, stoneDark * 1.08f, 0.05f, 0.13f, false);
            }
            Make(PrimitiveType.Cube, "KeepSlit_S1", new Vector3(-0.7f, 4.2f, 1.71f),
                new Vector3(0.14f, 0.8f, 0.08f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "KeepSlit_S2", new Vector3(0.7f, 4.2f, 1.71f),
                new Vector3(0.14f, 0.8f, 0.08f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "KeepSlit_E", new Vector3(1.71f, 5.6f, 0f),
                new Vector3(0.08f, 0.8f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "KeepSlit_W", new Vector3(-1.71f, 6.4f, 0f),
                new Vector3(0.08f, 0.8f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "KeepSlit_N", new Vector3(0f, 9.2f, -1.61f),
                new Vector3(0.14f, 0.7f, 0.08f), Quaternion.identity, slitDark, 0f, 0.05f, false);

            // Keep crown: corbels, slab, deck, merloned parapet.
            for (int i = 0; i < 8; i++)
            {
                int face = i / 2;
                float t = (i % 2 == 0) ? -0.8f : 0.8f;
                Vector3 p = face switch
                {
                    0 => new Vector3(t, 10.65f, 1.72f),
                    1 => new Vector3(1.72f, 10.65f, t),
                    2 => new Vector3(t, 10.65f, -1.72f),
                    _ => new Vector3(-1.72f, 10.65f, t),
                };
                Make(PrimitiveType.Cube, $"KeepCorbel_{i}", p,
                    new Vector3(0.40f, 0.36f, 0.40f), Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
            }
            Make(PrimitiveType.Cube, "KeepCrown", new Vector3(0f, 10.98f, 0f),
                new Vector3(3.9f, 0.18f, 3.9f), Quaternion.identity, stoneDark, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "KeepDeck", new Vector3(0f, 11.16f, 0f),
                new Vector3(3.7f, 0.18f, 3.7f), Quaternion.identity, stone * 1.05f, 0.05f, 0.20f, false);
            for (int face = 0; face < 4; face++)
            {
                bool alongX = face % 2 == 0;
                float side = face < 2 ? 1f : -1f;
                var wp = alongX ? new Vector3(0f, 11.45f, side * 1.82f) : new Vector3(side * 1.82f, 11.45f, 0f);
                var ws = alongX ? new Vector3(3.8f, 0.40f, 0.22f) : new Vector3(0.22f, 0.40f, 3.8f);
                Make(PrimitiveType.Cube, $"KeepParapet_{face}", wp, ws, Quaternion.identity, stoneLight, 0.05f, 0.15f, false);
                for (int m = 0; m < 4; m++)
                {
                    float t = -1.5f + m * 1.0f;
                    var mp = alongX ? new Vector3(t, 11.88f, side * 1.82f) : new Vector3(side * 1.82f, 11.88f, t);
                    Make(PrimitiveType.Cube, $"KeepMerlon_{face}_{m}", mp,
                        new Vector3(0.46f, 0.48f, 0.46f), Quaternion.identity, stoneLight, 0.05f, 0.14f, false);
                }
            }

            // Brazier on the keep deck and the great banner down its south face.
            Make(PrimitiveType.Cylinder, "BrazierStand", new Vector3(0f, 11.55f, 0f),
                new Vector3(0.18f, 0.34f, 0.18f), Quaternion.identity, iron, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "BrazierBowl", new Vector3(0f, 11.95f, 0f),
                new Vector3(0.84f, 0.16f, 0.84f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                iron * 1.1f, 0.80f, 0.40f, false);
            Make(PrimitiveType.Sphere, "BrazierCoals", new Vector3(0f, 12.08f, 0f),
                new Vector3(0.66f, 0.24f, 0.66f), Quaternion.identity, embers, 0f, 0.05f, true);
            Make(PrimitiveType.Sphere, "BrazierFlame", new Vector3(0.03f, 12.40f, -0.02f),
                new Vector3(0.44f, 0.68f, 0.44f), Quaternion.Euler(Jit(3f), 0f, Jit(3f)),
                flame, 0f, 0.05f, true);
            Make(PrimitiveType.Cylinder, "BannerRod", new Vector3(0f, 11.5f, 1.96f),
                new Vector3(0.07f, 0.60f, 0.07f), Quaternion.Euler(90f, 90f, 0f), beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "Stripe_Banner", new Vector3(0f, 9.9f, 2.0f),
                new Vector3(1.0f, 3.0f, 0.05f), Quaternion.Euler(Jit(1f), 0f, Jit(1.2f)),
                Color.white, 0f, 0.15f, false);

            return root;
        }
    }
}
