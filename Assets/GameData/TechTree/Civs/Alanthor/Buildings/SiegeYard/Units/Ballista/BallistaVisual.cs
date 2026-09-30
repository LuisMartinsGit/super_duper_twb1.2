// BallistaVisual.cs
// Procedural visual for the Ballista (pid 385) — the mobile engine AND the
// wall-mounted Emplaced Ballista, which reuse it. Replaces the human Hunter
// model the ballista used to render as (both sat on pid 338 and the Hunter's
// prefab won the catalog).
//
// A two-wheeled timber carriage carrying a long stock on a swivel post; at
// the front a pair of torsion boxes hold the two bow arms, the bowstring runs
// back to the trigger, and a loaded bolt (the same procedural mesh the shot
// flies as, BallistaBoltVisual) rests in the groove. Forward = +Z, root at
// ground level, ~1.8 x 2.6 m. Same primitive idiom as TrebuchetVisual:
// per-part URP/Lit material, metallic contrast, deterministic tilts,
// colliders stripped. The pennant ("4_Stripe_Pennant") takes the faction tint.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class BallistaVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);
            var root = new GameObject("BallistaVisual");

            var timber     = new Color(0.42f, 0.30f, 0.19f);
            var timberDark = new Color(0.30f, 0.21f, 0.13f);
            var iron       = new Color(0.21f, 0.20f, 0.19f);
            var ironWorn   = new Color(0.33f, 0.31f, 0.29f);
            var rope       = new Color(0.64f, 0.55f, 0.37f);
            var pennantCol = new Color(0.86f, 0.84f, 0.79f);

            float Jit(float r) => (float)(rng.NextDouble() * 2.0 - 1.0) * r;
            GameObject Make(PrimitiveType t, string name, Transform parent, Vector3 p, Vector3 s,
                            Quaternion r, Color c, float metal, float smooth)
                => ProceduralPrimitive.Make(t, name, parent, p, s, r, c, metal, smooth, false);

            var carriage = new GameObject("Carriage").transform;
            carriage.SetParent(root.transform, false);
            carriage.localRotation = Quaternion.Euler(0f, Jit(1.5f), 0f);

            // Axle + two spoked wheels.
            Make(PrimitiveType.Cylinder, "Axle", carriage, new Vector3(0f, 0.42f, -0.35f),
                new Vector3(0.08f, 0.85f, 0.08f), Quaternion.Euler(0f, 0f, 90f), iron, 0.6f, 0.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                var wheel = Make(PrimitiveType.Cylinder, side < 0 ? "Wheel_L" : "Wheel_R", carriage,
                    new Vector3(side * 0.78f, 0.42f, -0.35f), new Vector3(0.84f, 0.05f, 0.84f),
                    Quaternion.Euler(0f, 0f, 90f), timberDark, 0.05f, 0.15f);
                Make(PrimitiveType.Cylinder, "Hub", wheel.transform, Vector3.zero,
                    new Vector3(0.25f, 1.6f, 0.25f), Quaternion.identity, ironWorn, 0.6f, 0.4f);
                for (int k = 0; k < 3; k++)
                    Make(PrimitiveType.Cube, "Spoke", wheel.transform, Vector3.zero,
                        new Vector3(0.95f, 0.6f, 0.08f), Quaternion.Euler(0f, k * 60f, 0f), timber, 0.05f, 0.2f);
            }

            // Trail beams back to the ground, and the swivel post.
            for (int side = -1; side <= 1; side += 2)
                Make(PrimitiveType.Cube, "Trail", carriage, new Vector3(side * 0.28f, 0.28f, -1.05f),
                    new Vector3(0.12f, 0.12f, 1.4f), Quaternion.Euler(-14f, side * 6f, 0f), timber, 0.05f, 0.2f);
            Make(PrimitiveType.Cube, "Bed", carriage, new Vector3(0f, 0.5f, -0.35f),
                new Vector3(0.7f, 0.1f, 0.5f), Quaternion.identity, timberDark, 0.05f, 0.2f);
            Make(PrimitiveType.Cylinder, "SwivelPost", carriage, new Vector3(0f, 0.72f, -0.35f),
                new Vector3(0.18f, 0.22f, 0.18f), Quaternion.identity, timberDark, 0.05f, 0.2f);

            // The stock (tilted a touch upward), with its groove rails.
            var stock = new GameObject("Stock").transform;
            stock.SetParent(carriage, false);
            stock.localPosition = new Vector3(0f, 0.98f, -0.2f);
            stock.localRotation = Quaternion.Euler(-4f + Jit(1f), 0f, 0f);
            Make(PrimitiveType.Cube, "Stock_Beam", stock, Vector3.zero,
                new Vector3(0.2f, 0.14f, 2.3f), Quaternion.identity, timber, 0.05f, 0.25f);
            for (int side = -1; side <= 1; side += 2)
                Make(PrimitiveType.Cube, "Groove_Rail", stock, new Vector3(side * 0.07f, 0.09f, 0.1f),
                    new Vector3(0.03f, 0.04f, 2.0f), Quaternion.identity, timberDark, 0.05f, 0.2f);

            // Torsion boxes, the two bow arms and their iron caps.
            float armZ = 0.75f;
            Make(PrimitiveType.Cube, "TorsionFrame", stock, new Vector3(0f, 0.05f, armZ),
                new Vector3(0.62f, 0.34f, 0.18f), Quaternion.identity, timberDark, 0.05f, 0.2f);
            for (int side = -1; side <= 1; side += 2)
            {
                Make(PrimitiveType.Cylinder, "TorsionSpring", stock, new Vector3(side * 0.22f, 0.05f, armZ),
                    new Vector3(0.12f, 0.2f, 0.12f), Quaternion.identity, rope, 0f, 0.1f);
                var arm = Make(PrimitiveType.Cube, side < 0 ? "Arm_L" : "Arm_R", stock,
                    new Vector3(side * 0.62f, 0.05f, armZ - 0.2f),
                    new Vector3(0.72f, 0.08f, 0.09f), Quaternion.Euler(0f, side * 28f, 0f), timber, 0.05f, 0.25f);
                Make(PrimitiveType.Cube, "ArmCap", arm.transform, new Vector3(side * 0.5f, 0f, 0f),
                    new Vector3(0.1f, 1.3f, 1.3f), Quaternion.identity, iron, 0.6f, 0.4f);

                // Bowstring: arm tip back to the trigger at the rear.
                var tip = new Vector3(side * 0.93f, 0.08f, armZ - 0.37f);
                var nock = new Vector3(0f, 0.1f, -0.55f);
                var mid = (tip + nock) * 0.5f;
                var dir = nock - tip;
                Make(PrimitiveType.Cylinder, "Bowstring", stock, mid,
                    new Vector3(0.02f, dir.magnitude * 0.5f, 0.02f),
                    Quaternion.FromToRotation(Vector3.up, dir.normalized), rope, 0f, 0.1f);
            }
            Make(PrimitiveType.Cube, "Trigger", stock, new Vector3(0f, 0.12f, -0.6f),
                new Vector3(0.14f, 0.08f, 0.12f), Quaternion.identity, iron, 0.6f, 0.4f);
            Make(PrimitiveType.Cylinder, "Winch", stock, new Vector3(0f, 0.0f, -1.0f),
                new Vector3(0.12f, 0.24f, 0.12f), Quaternion.Euler(0f, 0f, 90f), timberDark, 0.05f, 0.2f);

            // The loaded bolt — the same mesh the shot flies as.
            var bolt = BallistaBoltVisual.Build();
            bolt.name = "LoadedBolt";
            bolt.transform.SetParent(stock, false);
            bolt.transform.localPosition = new Vector3(0f, 0.14f, 0.35f);
            bolt.transform.localScale = Vector3.one * 0.85f;

            // Pennant on a short staff at the rear (faction tint).
            Make(PrimitiveType.Cylinder, "PennantStaff", carriage, new Vector3(0.3f, 1.25f, -1.1f),
                new Vector3(0.03f, 0.45f, 0.03f), Quaternion.identity, timberDark, 0.05f, 0.2f);
            Make(PrimitiveType.Cube, "4_Stripe_Pennant", carriage, new Vector3(0.3f, 1.55f, -1.25f),
                new Vector3(0.02f, 0.2f, 0.3f), Quaternion.identity, pennantCol, 0f, 0.1f);

            return root;
        }
    }
}
