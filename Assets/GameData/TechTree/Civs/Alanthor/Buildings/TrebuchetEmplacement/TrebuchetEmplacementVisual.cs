// Procedural Trebuchet Emplacement — the PLATFORM, not the engine
// (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet emplacements).
//
// Bigger and heavier than the ballista's: a stone revetment, a braced timber
// bed with the pivot cradle the engine's frame drops into, a counterweight
// pit sunk behind it, a stone pile and a winch. The middle stays empty — the
// engine that stands there is a separate entity.
//
// Part names carry leading rise-group numbers so BuildingRiseData staggers
// the construction rise bottom-up.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class TrebuchetEmplacementVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("TrebuchetEmplacement");

            var stone     = new Color(0.47f, 0.42f, 0.36f);
            var stoneDark = new Color(0.31f, 0.27f, 0.23f);
            var beam      = new Color(0.30f, 0.20f, 0.12f);
            var plank     = new Color(0.45f, 0.31f, 0.18f);
            var iron      = new Color(0.22f, 0.22f, 0.25f);
            var rope      = new Color(0.58f, 0.50f, 0.34f);

            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);

            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            // ── 1_ Revetment ──
            Make(PrimitiveType.Cube, "1_Footing", new Vector3(0f, 0.20f, 0f),
                new Vector3(6.0f, 0.40f, 6.0f), Quaternion.Euler(0f, Jit(1.0f), 0f),
                stoneDark, 0.05f, 0.12f, false);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + Jit(5f);
                float r = 2.55f;
                Make(PrimitiveType.Cube, $"1_Revetment_{i}",
                    new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, 0.55f, Mathf.Sin(a * Mathf.Deg2Rad) * r),
                    new Vector3(1.05f, 0.70f, 0.85f), Quaternion.Euler(0f, a, 0f),
                    stone, 0.05f, 0.14f, false);
            }

            // ── 2_ Bed and cradle ──
            for (int i = -2; i <= 2; i++)
                Make(PrimitiveType.Cube, $"2_Sleeper_{i + 2}", new Vector3(0f, 0.62f, i * 1.05f),
                    new Vector3(4.6f, 0.24f, 0.32f), Quaternion.identity, beam, 0.02f, 0.15f, false);
            Make(PrimitiveType.Cube, "2_Bed", new Vector3(0f, 0.80f, 0f),
                new Vector3(4.4f, 0.14f, 4.4f), Quaternion.identity, plank, 0.02f, 0.18f, false);
            // The pivot cradle: two A-frame feet the engine's axle sits in.
            for (int side = -1; side <= 1; side += 2)
            {
                Make(PrimitiveType.Cube, side < 0 ? "2_CradleL" : "2_CradleR",
                    new Vector3(0f, 1.25f, side * 1.10f),
                    new Vector3(0.55f, 0.90f, 0.42f), Quaternion.identity, beam, 0.02f, 0.15f, false);
                Make(PrimitiveType.Cylinder, side < 0 ? "2_CradleCapL" : "2_CradleCapR",
                    new Vector3(0f, 1.72f, side * 1.10f),
                    new Vector3(0.46f, 0.10f, 0.46f), Quaternion.Euler(90f, 0f, 0f),
                    iron, 0.8f, 0.45f, false);
            }

            // ── 3_ Counterweight pit behind the bed (−X, the friendly side) ──
            Make(PrimitiveType.Cube, "3_Pit", new Vector3(-2.05f, 0.52f, 0f),
                new Vector3(1.35f, 0.30f, 3.0f), Quaternion.identity, stoneDark, 0.05f, 0.10f, false);
            for (int side = -1; side <= 1; side += 2)
                Make(PrimitiveType.Cube, side < 0 ? "3_PitWallL" : "3_PitWallR",
                    new Vector3(-2.05f, 0.85f, side * 1.55f),
                    new Vector3(1.45f, 0.55f, 0.28f), Quaternion.identity, stone, 0.05f, 0.14f, false);

            // ── 4_ Props: a stone pile, the winch, coils of rope ──
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                Make(PrimitiveType.Sphere, $"4_Stone_{i}",
                    new Vector3(2.15f + Jit(0.12f), 0.95f + (i > 2 ? 0.42f : 0f),
                                Mathf.Sin(a * Mathf.Deg2Rad) * 0.85f),
                    Vector3.one * (0.48f + Jit(0.06f)), Quaternion.Euler(Jit(30f), Jit(30f), Jit(30f)),
                    stone, 0.05f, 0.12f, false);
            }
            Make(PrimitiveType.Cylinder, "4_Winch", new Vector3(-1.55f, 1.10f, 1.85f),
                new Vector3(0.40f, 0.70f, 0.40f), Quaternion.Euler(0f, 0f, 90f), beam, 0.02f, 0.18f, false);
            Make(PrimitiveType.Cylinder, "4_Rope", new Vector3(-1.55f, 1.10f, 1.85f),
                new Vector3(0.52f, 0.28f, 0.52f), Quaternion.Euler(0f, 0f, 90f), rope, 0.0f, 0.15f, false);

            // Faction accent — a standard at the pit's edge.
            Make(PrimitiveType.Cylinder, "4_Pole", new Vector3(-2.35f, 1.65f, -1.90f),
                new Vector3(0.08f, 1.20f, 0.08f), Quaternion.identity, iron, 0.6f, 0.4f, false);
            Make(PrimitiveType.Cube, "Stripe_Standard", new Vector3(-2.35f, 2.35f, -1.50f),
                new Vector3(0.05f, 0.75f, 0.62f), Quaternion.identity, Color.white, 0f, 0.2f, false);

            return root;
        }
    }
}
