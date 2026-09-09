// Procedural Renewal Tower (Raise Anew I): a modest square watch post in
// fresh-cut limestone, deliberately NOT the round weathered-granite silhouette
// of the Alanthor Watch Tower. Plinth, two-section square shaft with corner
// quoins and arrow slits, corbelled square parapet with merlons, an open
// lookout deck with a brazier, and a faction banner.
//
// Faction accent: "Stripe_Banner" (tinted by BuildingFactionColorMarker via
// the "stripe" name rule). This building SPAWNS FINISHED (no construction
// rise), so part names carry no rise numbers. Standalone static builder: the
// orchestrator (PresentationSpawnSystem) wires the pid branch and handles
// FitSelectionCollider / EntityReference / faction marker after Build returns.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class RenewalTowerVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("RenewalTower");

            var stone      = new Color(0.72f, 0.68f, 0.60f);   // fresh-cut limestone
            var stoneLight = new Color(0.80f, 0.76f, 0.68f);   // upper courses
            var stoneDark  = new Color(0.52f, 0.48f, 0.42f);   // plinth / course bands
            var slitDark   = new Color(0.06f, 0.05f, 0.05f);   // arrow-slit voids
            var beam       = new Color(0.34f, 0.23f, 0.14f);   // oak
            var iron       = new Color(0.19f, 0.18f, 0.17f);   // brazier
            var embers     = new Color(0.95f, 0.42f, 0.10f);
            var flame      = new Color(1.00f, 0.66f, 0.22f);

            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);
            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            // Plinth and footing.
            Make(PrimitiveType.Cube, "Plinth", new Vector3(0f, 0.20f, 0f),
                new Vector3(3.5f, 0.40f, 3.5f), Quaternion.Euler(0f, Jit(1f), 0f),
                stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "Footing", new Vector3(0f, 0.62f, 0f),
                new Vector3(3.0f, 0.46f, 3.0f), Quaternion.identity,
                stone, 0.05f, 0.14f, false);

            // Two-section square shaft, the upper one a hair narrower.
            Make(PrimitiveType.Cube, "ShaftA", new Vector3(0f, 2.55f, 0f),
                new Vector3(2.6f, 3.4f, 2.6f), Quaternion.identity,
                stone, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "CourseA", new Vector3(0f, 4.28f, 0f),
                new Vector3(2.7f, 0.10f, 2.7f), Quaternion.identity,
                stoneDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "ShaftB", new Vector3(0f, 5.85f, 0f),
                new Vector3(2.4f, 3.0f, 2.4f), Quaternion.identity,
                stoneLight, 0.05f, 0.16f, false);

            // Corner quoins: darker dressed-stone strips up each vertical edge.
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                Make(PrimitiveType.Cube, $"Quoin_{i}",
                    new Vector3(sx * 1.24f, 4.0f, sz * 1.24f),
                    new Vector3(0.28f, 6.3f, 0.28f), Quaternion.identity,
                    stoneDark * 1.08f, 0.05f, 0.13f, false);
            }

            // Arrow slits, one per face, staggered in height.
            Make(PrimitiveType.Cube, "Slit_S", new Vector3(0f, 2.6f, 1.31f),
                new Vector3(0.14f, 0.70f, 0.08f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "Slit_E", new Vector3(1.31f, 3.4f, 0f),
                new Vector3(0.08f, 0.70f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "Slit_N", new Vector3(0f, 5.6f, -1.21f),
                new Vector3(0.14f, 0.66f, 0.08f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "Slit_W", new Vector3(-1.21f, 6.3f, 0f),
                new Vector3(0.08f, 0.66f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);

            // Corbels under the parapet, two per face.
            for (int i = 0; i < 8; i++)
            {
                int face = i / 2;
                float t = (i % 2 == 0) ? -0.55f : 0.55f;
                Vector3 p = face switch
                {
                    0 => new Vector3(t, 7.45f, 1.30f),
                    1 => new Vector3(1.30f, 7.45f, t),
                    2 => new Vector3(t, 7.45f, -1.30f),
                    _ => new Vector3(-1.30f, 7.45f, t),
                };
                Make(PrimitiveType.Cube, $"Corbel_{i}", p,
                    new Vector3(0.36f, 0.34f, 0.36f), Quaternion.identity,
                    stoneDark, 0.05f, 0.12f, false);
            }

            // Deck and square parapet with merlons.
            Make(PrimitiveType.Cube, "CrownSlab", new Vector3(0f, 7.72f, 0f),
                new Vector3(3.1f, 0.16f, 3.1f), Quaternion.identity,
                stoneDark, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "Deck", new Vector3(0f, 7.88f, 0f),
                new Vector3(2.9f, 0.16f, 2.9f), Quaternion.identity,
                stone * 1.05f, 0.05f, 0.20f, false);
            for (int face = 0; face < 4; face++)
            {
                bool alongX = face % 2 == 0;
                float side = face < 2 ? 1f : -1f;
                var wallPos = alongX ? new Vector3(0f, 8.14f, side * 1.42f) : new Vector3(side * 1.42f, 8.14f, 0f);
                var wallScale = alongX ? new Vector3(3.0f, 0.36f, 0.20f) : new Vector3(0.20f, 0.36f, 3.0f);
                Make(PrimitiveType.Cube, $"Parapet_{face}", wallPos, wallScale, Quaternion.identity,
                    stoneLight, 0.05f, 0.15f, false);
                for (int m = 0; m < 3; m++)
                {
                    float t = -1.1f + m * 1.1f;
                    var mp = alongX ? new Vector3(t, 8.55f, side * 1.42f) : new Vector3(side * 1.42f, 8.55f, t);
                    Make(PrimitiveType.Cube, $"Merlon_{face}_{m}", mp,
                        new Vector3(0.46f, 0.46f, 0.46f), Quaternion.identity,
                        stoneLight, 0.05f, 0.14f, false);
                }
            }

            // Brazier on the deck.
            Make(PrimitiveType.Cylinder, "BrazierStand", new Vector3(0f, 8.25f, 0f),
                new Vector3(0.16f, 0.30f, 0.16f), Quaternion.identity, iron, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "BrazierBowl", new Vector3(0f, 8.60f, 0f),
                new Vector3(0.70f, 0.14f, 0.70f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                iron * 1.1f, 0.80f, 0.40f, false);
            Make(PrimitiveType.Sphere, "BrazierCoals", new Vector3(0f, 8.72f, 0f),
                new Vector3(0.55f, 0.20f, 0.55f), Quaternion.identity, embers, 0f, 0.05f, true);
            Make(PrimitiveType.Sphere, "BrazierFlame", new Vector3(0.02f, 8.98f, -0.02f),
                new Vector3(0.36f, 0.56f, 0.36f), Quaternion.Euler(Jit(3f), 0f, Jit(3f)),
                flame, 0f, 0.05f, true);

            // Faction banner hung down the south face.
            Make(PrimitiveType.Cylinder, "BannerRod", new Vector3(0f, 8.20f, 1.56f),
                new Vector3(0.06f, 0.50f, 0.06f), Quaternion.Euler(90f, 90f, 0f),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "Stripe_Banner", new Vector3(0f, 7.15f, 1.60f),
                new Vector3(0.74f, 2.0f, 0.05f), Quaternion.Euler(Jit(1f), 0f, Jit(1.5f)),
                Color.white, 0f, 0.15f, false);

            return root;
        }
    }
}
