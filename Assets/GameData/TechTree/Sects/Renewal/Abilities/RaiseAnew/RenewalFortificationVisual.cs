// Procedural Renewal Fortification (Raise Anew II): a walled strongpoint on
// a 6 x 6 m footprint. A square curtain wall with a merloned walk, four
// square corner turrets, a gate slot on the south face, and a central square
// tower rising above the walls with its own parapet, brazier and banner. The
// limestone palette matches the Renewal Tower so the ladder reads as one
// family.
//
// Faction accents: "Stripe_Banner" (tower) and "Stripe_Gate" (gate lintel
// cloth). This building SPAWNS FINISHED, so part names carry no rise numbers.
// Standalone static builder; see RenewalTowerVisual for the orchestration
// contract.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class RenewalFortificationVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("RenewalFortification");

            var stone      = new Color(0.72f, 0.68f, 0.60f);
            var stoneLight = new Color(0.80f, 0.76f, 0.68f);
            var stoneDark  = new Color(0.52f, 0.48f, 0.42f);
            var slitDark   = new Color(0.06f, 0.05f, 0.05f);
            var court      = new Color(0.44f, 0.40f, 0.33f);   // packed-earth courtyard
            var beam       = new Color(0.34f, 0.23f, 0.14f);
            var iron       = new Color(0.19f, 0.18f, 0.17f);
            var embers     = new Color(0.95f, 0.42f, 0.10f);
            var flame      = new Color(1.00f, 0.66f, 0.22f);

            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);
            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            const float half = 2.7f;      // curtain-wall half-extent
            const float wallH = 2.8f;

            // Plinth and courtyard.
            Make(PrimitiveType.Cube, "Plinth", new Vector3(0f, 0.16f, 0f),
                new Vector3(5.9f, 0.32f, 5.9f), Quaternion.Euler(0f, Jit(0.8f), 0f),
                stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "Courtyard", new Vector3(0f, 0.34f, 0f),
                new Vector3(5.2f, 0.06f, 5.2f), Quaternion.identity,
                court, 0.02f, 0.06f, false);

            // Curtain walls, one per face; the south one split for the gate.
            for (int face = 0; face < 4; face++)
            {
                bool alongX = face % 2 == 0;
                float side = face < 2 ? 1f : -1f;
                bool isGate = face == 0;   // +Z face carries the gate
                if (!isGate)
                {
                    var p = alongX ? new Vector3(0f, wallH * 0.5f + 0.3f, side * half)
                                   : new Vector3(side * half, wallH * 0.5f + 0.3f, 0f);
                    var s = alongX ? new Vector3(half * 2f, wallH, 0.55f) : new Vector3(0.55f, wallH, half * 2f);
                    Make(PrimitiveType.Cube, $"Wall_{face}", p, s, Quaternion.identity, stone, 0.05f, 0.14f, false);
                }
                else
                {
                    // Two wall halves flanking a 1.4 m gate opening, lintel above.
                    Make(PrimitiveType.Cube, "Wall_0_L", new Vector3(-1.75f, wallH * 0.5f + 0.3f, half),
                        new Vector3(1.9f, wallH, 0.55f), Quaternion.identity, stone, 0.05f, 0.14f, false);
                    Make(PrimitiveType.Cube, "Wall_0_R", new Vector3(1.75f, wallH * 0.5f + 0.3f, half),
                        new Vector3(1.9f, wallH, 0.55f), Quaternion.identity, stone, 0.05f, 0.14f, false);
                    Make(PrimitiveType.Cube, "GateLintel", new Vector3(0f, 2.55f, half),
                        new Vector3(1.7f, 1.1f, 0.55f), Quaternion.identity, stoneDark, 0.05f, 0.13f, false);
                    Make(PrimitiveType.Cube, "GateVoid", new Vector3(0f, 1.15f, half + 0.02f),
                        new Vector3(1.3f, 1.7f, 0.56f), Quaternion.identity, slitDark, 0f, 0.05f, false);
                    Make(PrimitiveType.Cube, "Stripe_Gate", new Vector3(0f, 2.15f, half + 0.30f),
                        new Vector3(1.5f, 0.28f, 0.04f), Quaternion.identity, Color.white, 0f, 0.12f, false);
                }

                // Wall-walk course band and merlons along the top.
                var bandP = alongX ? new Vector3(0f, wallH + 0.34f, side * half) : new Vector3(side * half, wallH + 0.34f, 0f);
                var bandS = alongX ? new Vector3(half * 2f + 0.1f, 0.10f, 0.65f) : new Vector3(0.65f, 0.10f, half * 2f + 0.1f);
                Make(PrimitiveType.Cube, $"WallBand_{face}", bandP, bandS, Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
                for (int m = 0; m < 5; m++)
                {
                    float t = -2.0f + m * 1.0f;
                    var mp = alongX ? new Vector3(t, wallH + 0.62f, side * (half + 0.05f))
                                    : new Vector3(side * (half + 0.05f), wallH + 0.62f, t);
                    Make(PrimitiveType.Cube, $"WallMerlon_{face}_{m}", mp,
                        new Vector3(0.44f, 0.44f, 0.44f), Quaternion.identity, stoneLight, 0.05f, 0.14f, false);
                }
            }

            // Four square corner turrets, taller than the wall.
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                var c = new Vector3(sx * half, 0f, sz * half);
                Make(PrimitiveType.Cube, $"Turret_{i}", c + new Vector3(0f, 2.15f, 0f),
                    new Vector3(1.3f, 4.1f, 1.3f), Quaternion.identity, stone * 1.02f, 0.05f, 0.14f, false);
                Make(PrimitiveType.Cube, $"TurretCap_{i}", c + new Vector3(0f, 4.26f, 0f),
                    new Vector3(1.5f, 0.14f, 1.5f), Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
                for (int m = 0; m < 4; m++)
                {
                    float mx = (m % 2 == 0) ? -0.5f : 0.5f;
                    float mz = (m < 2) ? -0.5f : 0.5f;
                    Make(PrimitiveType.Cube, $"TurretMerlon_{i}_{m}", c + new Vector3(mx, 4.55f, mz),
                        new Vector3(0.40f, 0.44f, 0.40f), Quaternion.identity, stoneLight, 0.05f, 0.14f, false);
                }
                Make(PrimitiveType.Cube, $"TurretSlit_{i}", c + new Vector3(0f, 2.6f, -sz * 0.68f),
                    new Vector3(0.12f, 0.6f, 0.08f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            }

            // Central tower above the walls.
            Make(PrimitiveType.Cube, "KeepBase", new Vector3(0f, 0.75f, 0f),
                new Vector3(2.9f, 0.8f, 2.9f), Quaternion.identity, stoneDark, 0.05f, 0.13f, false);
            Make(PrimitiveType.Cube, "KeepShaft", new Vector3(0f, 3.9f, 0f),
                new Vector3(2.5f, 5.5f, 2.5f), Quaternion.identity, stone, 0.05f, 0.15f, false);
            Make(PrimitiveType.Cube, "KeepCourse", new Vector3(0f, 4.4f, 0f),
                new Vector3(2.6f, 0.10f, 2.6f), Quaternion.identity, stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "KeepUpper", new Vector3(0f, 6.2f, 0f),
                new Vector3(2.3f, 1.0f, 2.3f), Quaternion.identity, stoneLight, 0.05f, 0.16f, false);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                Make(PrimitiveType.Cube, $"KeepQuoin_{i}", new Vector3(sx * 1.2f, 3.9f, sz * 1.2f),
                    new Vector3(0.26f, 5.5f, 0.26f), Quaternion.identity, stoneDark * 1.08f, 0.05f, 0.13f, false);
            }
            Make(PrimitiveType.Cube, "KeepSlit_S", new Vector3(0f, 4.0f, 1.26f),
                new Vector3(0.14f, 0.7f, 0.08f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "KeepSlit_E", new Vector3(1.26f, 5.0f, 0f),
                new Vector3(0.08f, 0.7f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "KeepSlit_W", new Vector3(-1.26f, 3.2f, 0f),
                new Vector3(0.08f, 0.7f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);

            // Keep crown: slab, deck and merloned parapet.
            Make(PrimitiveType.Cube, "KeepCrown", new Vector3(0f, 6.78f, 0f),
                new Vector3(2.9f, 0.16f, 2.9f), Quaternion.identity, stoneDark, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "KeepDeck", new Vector3(0f, 6.94f, 0f),
                new Vector3(2.7f, 0.16f, 2.7f), Quaternion.identity, stone * 1.05f, 0.05f, 0.20f, false);
            for (int face = 0; face < 4; face++)
            {
                bool alongX = face % 2 == 0;
                float side = face < 2 ? 1f : -1f;
                var wp = alongX ? new Vector3(0f, 7.2f, side * 1.32f) : new Vector3(side * 1.32f, 7.2f, 0f);
                var ws = alongX ? new Vector3(2.8f, 0.36f, 0.20f) : new Vector3(0.20f, 0.36f, 2.8f);
                Make(PrimitiveType.Cube, $"KeepParapet_{face}", wp, ws, Quaternion.identity, stoneLight, 0.05f, 0.15f, false);
                for (int m = 0; m < 3; m++)
                {
                    float t = -1.0f + m * 1.0f;
                    var mp = alongX ? new Vector3(t, 7.6f, side * 1.32f) : new Vector3(side * 1.32f, 7.6f, t);
                    Make(PrimitiveType.Cube, $"KeepMerlon_{face}_{m}", mp,
                        new Vector3(0.44f, 0.44f, 0.44f), Quaternion.identity, stoneLight, 0.05f, 0.14f, false);
                }
            }

            // Brazier on the keep deck and the faction banner down its south face.
            Make(PrimitiveType.Cylinder, "BrazierStand", new Vector3(0f, 7.3f, 0f),
                new Vector3(0.16f, 0.30f, 0.16f), Quaternion.identity, iron, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "BrazierBowl", new Vector3(0f, 7.65f, 0f),
                new Vector3(0.72f, 0.14f, 0.72f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                iron * 1.1f, 0.80f, 0.40f, false);
            Make(PrimitiveType.Sphere, "BrazierCoals", new Vector3(0f, 7.77f, 0f),
                new Vector3(0.56f, 0.20f, 0.56f), Quaternion.identity, embers, 0f, 0.05f, true);
            Make(PrimitiveType.Sphere, "BrazierFlame", new Vector3(0.02f, 8.04f, -0.02f),
                new Vector3(0.38f, 0.58f, 0.38f), Quaternion.Euler(Jit(3f), 0f, Jit(3f)),
                flame, 0f, 0.05f, true);
            Make(PrimitiveType.Cylinder, "BannerRod", new Vector3(0f, 7.25f, 1.46f),
                new Vector3(0.06f, 0.50f, 0.06f), Quaternion.Euler(90f, 90f, 0f), beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "Stripe_Banner", new Vector3(0f, 6.15f, 1.50f),
                new Vector3(0.76f, 2.1f, 0.05f), Quaternion.Euler(Jit(1f), 0f, Jit(1.5f)),
                Color.white, 0f, 0.15f, false);

            return root;
        }
    }
}
