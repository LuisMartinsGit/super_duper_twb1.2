// Procedural Ballista Emplacement — the PLATFORM, not the engine
// (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet emplacements).
//
// A low timber-and-stone firing platform: a stone footing, a planked deck on
// joists, a waist-high mantlet of pavises on the outer face, a bolt rack and
// a spare-bolt bundle. Deliberately squat and empty in the middle — the
// engine that stands there is a separate entity, and the platform has to
// read as a thing that survives it.
//
// Part names carry leading rise-group numbers (1_ footing, 2_ deck,
// 3_ mantlet, 4_ props) so BuildingRiseData staggers the construction rise
// bottom-up.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class BallistaEmplacementVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("BallistaEmplacement");

            var stone     = new Color(0.47f, 0.42f, 0.36f);
            var stoneDark = new Color(0.31f, 0.27f, 0.23f);
            var beam      = new Color(0.32f, 0.21f, 0.13f);
            var plank     = new Color(0.45f, 0.31f, 0.18f);
            var iron      = new Color(0.22f, 0.22f, 0.25f);

            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);

            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            // ── 1_ Footing ──
            Make(PrimitiveType.Cube, "1_Footing", new Vector3(0f, 0.18f, 0f),
                new Vector3(4.0f, 0.36f, 4.0f), Quaternion.Euler(0f, Jit(1.2f), 0f),
                stoneDark, 0.05f, 0.12f, false);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + Jit(6f);
                float r = 1.65f;
                Make(PrimitiveType.Cube, $"1_Block_{i}",
                    new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, 0.42f, Mathf.Sin(a * Mathf.Deg2Rad) * r),
                    new Vector3(0.75f, 0.50f, 0.75f), Quaternion.Euler(0f, a, 0f),
                    stone, 0.05f, 0.14f, false);
            }

            // ── 2_ Deck on joists ──
            for (int i = -2; i <= 2; i++)
                Make(PrimitiveType.Cube, $"2_Joist_{i + 2}", new Vector3(0f, 0.60f, i * 0.72f),
                    new Vector3(3.5f, 0.18f, 0.22f), Quaternion.identity, beam, 0.02f, 0.15f, false);
            Make(PrimitiveType.Cube, "2_Deck", new Vector3(0f, 0.74f, 0f),
                new Vector3(3.4f, 0.12f, 3.4f), Quaternion.identity, plank, 0.02f, 0.18f, false);

            // The turntable ring the engine's pintle drops into.
            Make(PrimitiveType.Cylinder, "2_Pintle", new Vector3(0f, 0.84f, 0f),
                new Vector3(0.95f, 0.06f, 0.95f), Quaternion.identity, iron, 0.8f, 0.45f, false);

            // ── 3_ Mantlet: pavises across the OUTER face (+X) ──
            for (int i = -1; i <= 1; i++)
                Make(PrimitiveType.Cube, $"3_Pavise_{i + 1}",
                    new Vector3(1.70f, 1.35f, i * 1.05f),
                    new Vector3(0.18f, 1.15f, 0.90f), Quaternion.Euler(0f, 0f, Jit(2.5f)),
                    beam, 0.05f, 0.2f, false);
            Make(PrimitiveType.Cube, "3_MantletRail", new Vector3(1.70f, 1.95f, 0f),
                new Vector3(0.24f, 0.12f, 3.2f), Quaternion.identity, iron, 0.7f, 0.4f, false);

            // ── 4_ Props: bolt rack and a bundle of spares ──
            Make(PrimitiveType.Cube, "4_Rack", new Vector3(-1.45f, 1.05f, 0.9f),
                new Vector3(0.45f, 0.55f, 1.10f), Quaternion.Euler(0f, Jit(4f), 0f),
                beam, 0.02f, 0.15f, false);
            for (int i = 0; i < 5; i++)
                Make(PrimitiveType.Cylinder, $"4_Bolt_{i}",
                    new Vector3(-1.45f + Jit(0.05f), 1.55f, 0.55f + i * 0.17f),
                    new Vector3(0.07f, 0.55f, 0.07f), Quaternion.Euler(Jit(8f), 0f, Jit(8f)),
                    plank, 0.05f, 0.2f, false);
            Make(PrimitiveType.Cylinder, "4_Barrel", new Vector3(-1.35f, 1.05f, -1.05f),
                new Vector3(0.65f, 0.45f, 0.65f), Quaternion.identity, beam, 0.02f, 0.2f, false);

            // Faction accent — a pennant on the inner rail.
            Make(PrimitiveType.Cylinder, "4_Pole", new Vector3(-1.60f, 1.70f, -0.20f),
                new Vector3(0.07f, 1.00f, 0.07f), Quaternion.identity, iron, 0.6f, 0.4f, false);
            Make(PrimitiveType.Cube, "Stripe_Pennant", new Vector3(-1.60f, 2.25f, 0.18f),
                new Vector3(0.05f, 0.60f, 0.55f), Quaternion.identity, Color.white, 0f, 0.2f, false);

            return root;
        }
    }
}
