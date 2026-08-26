// Procedural Field Hospital — a small canvas field tent: two sloped canvas
// slabs with sag bulges (the Hut's canvas-sag trick), an open front, two cots
// (frame + blanket + pillow), supply crates, a water barrel, guy ropes with
// stakes, a banner pole, and one soft white emissive lantern.
//
// Faction accents: "Stripe_1" (pennant at the banner-pole tip) and "Stripe_2"
// (tent eave trim) — tinted by BuildingFactionColorMarker via the "stripe"
// name rule. No part name contains "roof" (the canvas must keep its albedo).
//
// This building SPAWNS FINISHED (no construction rise), so part names carry
// no rise numbers. Standalone static builder: the orchestrator wires the pid
// branch and handles FitSelectionCollider / EntityReference / faction marker
// after Build returns.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class FieldHospitalVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);

            var root = new GameObject("FieldHospital");

            // ── Palette ─────────────────────────────────────────────────────
            var canvas     = new Color(0.89f, 0.84f, 0.73f);   // sun-bleached tent cloth
            var canvasSag  = new Color(0.82f, 0.77f, 0.66f);   // shadowed sag folds
            var groundPad  = new Color(0.38f, 0.33f, 0.26f);   // trampled earth pad
            var beam       = new Color(0.33f, 0.22f, 0.14f);   // dark oak poles
            var plank      = new Color(0.47f, 0.33f, 0.19f);   // cot frames / crates
            var blanket    = new Color(0.58f, 0.62f, 0.55f);   // wool grey-green
            var linen      = new Color(0.92f, 0.90f, 0.84f);   // pillows / dressings
            var rope       = new Color(0.72f, 0.63f, 0.46f);   // hemp guy lines
            var iron       = new Color(0.20f, 0.19f, 0.18f);   // barrel hoops / lantern
            var water      = new Color(0.25f, 0.32f, 0.42f);   // barrel water disc
            var lantern    = new Color(1.00f, 0.96f, 0.86f);   // soft white glow

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
                        r.material.SetColor("_EmissionColor", color * 1.3f);
                    }
                }
                var c = go.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
                return go;
            };

            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            // ── Ground pad — trampled earth under the tent ──────────────────
            Make(PrimitiveType.Cube, "GroundPad", new Vector3(0f, 0.03f, 0f),
                new Vector3(4.6f, 0.06f, 3.8f), Quaternion.Euler(0f, Jit(2f), 0f),
                groundPad, 0.02f, 0.06f, false);

            // ── Tent frame — ridge pole on two end poles (open front at +Z) ─
            Make(PrimitiveType.Cylinder, "RidgePole", new Vector3(0f, 2.02f, -0.25f),
                new Vector3(0.09f, 1.45f, 0.09f), Quaternion.Euler(90f, 0f, Jit(0.8f)),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "EndPole_F", new Vector3(0f, 1.02f, 1.18f),
                new Vector3(0.10f, 1.02f, 0.10f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "EndPole_B", new Vector3(0f, 1.02f, -1.68f),
                new Vector3(0.10f, 1.02f, 0.10f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                beam * 0.94f, 0.02f, 0.10f, false);

            // ── Canvas: two sloped slabs meeting at the ridge ───────────────
            Make(PrimitiveType.Cube, "CanvasSlope_L", new Vector3(-1.02f, 1.32f, -0.25f),
                new Vector3(2.45f, 0.05f, 3.05f), Quaternion.Euler(0f, Jit(0.6f), 38f),
                canvas, 0.0f, 0.05f, false);
            Make(PrimitiveType.Cube, "CanvasSlope_R", new Vector3(1.02f, 1.32f, -0.25f),
                new Vector3(2.45f, 0.05f, 3.05f), Quaternion.Euler(0f, Jit(0.6f), -38f),
                canvas * 0.98f, 0.0f, 0.05f, false);

            // Sag bulges — thin darker panels pushed just below the slopes
            // (the Hut trick: offset slabs faking cloth tension between ties).
            Make(PrimitiveType.Cube, "CanvasBulge_L", new Vector3(-1.06f, 1.22f, -0.25f),
                new Vector3(1.55f, 0.04f, 2.05f), Quaternion.Euler(Jit(1f), 0f, 40f),
                canvasSag, 0.0f, 0.04f, false);
            Make(PrimitiveType.Cube, "CanvasBulge_R", new Vector3(1.06f, 1.22f, 0.15f),
                new Vector3(1.40f, 0.04f, 1.75f), Quaternion.Euler(Jit(1f), 0f, -40f),
                canvasSag, 0.0f, 0.04f, false);
            Make(PrimitiveType.Cube, "CanvasBulge_R2", new Vector3(1.10f, 1.10f, -1.10f),
                new Vector3(0.95f, 0.04f, 0.85f), Quaternion.Euler(Jit(1.5f), Jit(1.5f), -41f),
                canvasSag * 0.97f, 0.0f, 0.04f, false);

            // Back wall flap closing the -Z end, slightly askew.
            Make(PrimitiveType.Cube, "CanvasBack", new Vector3(0f, 0.95f, -1.74f),
                new Vector3(3.30f, 1.90f, 0.05f), Quaternion.Euler(Jit(1f), Jit(1.5f), 0f),
                canvasSag, 0.0f, 0.05f, false);
            Make(PrimitiveType.Cube, "CanvasBackFold", new Vector3(0.72f, 0.70f, -1.71f),
                new Vector3(0.85f, 1.40f, 0.04f), Quaternion.Euler(0f, Jit(2f), 4f),
                canvas * 0.95f, 0.0f, 0.04f, false);

            // Faction trim band along the front eave line (accent 2).
            Make(PrimitiveType.Cube, "Stripe_2", new Vector3(0f, 1.42f, 1.28f),
                new Vector3(3.35f, 0.18f, 0.04f), Quaternion.Euler(0f, 0f, Jit(1f)),
                Color.white, 0.0f, 0.12f, false);

            // ── Guy ropes + stakes at the four corners ──────────────────────
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? 1f : -1f;
                float px = sx * 2.05f, pz = sz * 1.10f - 0.25f;
                Make(PrimitiveType.Cylinder, $"GuyRope_{i}",
                    new Vector3(px * 0.88f, 0.62f, pz),
                    new Vector3(0.025f, 0.62f, 0.025f),
                    Quaternion.Euler(Jit(3f), 0f, sx * -52f),
                    rope, 0.0f, 0.08f, false);
                Make(PrimitiveType.Cube, $"Stake_{i}",
                    new Vector3(px * 1.12f, 0.10f, pz + Jit(0.05f)),
                    new Vector3(0.08f, 0.26f, 0.08f),
                    Quaternion.Euler(Jit(4f), Jit(10f), sx * 12f),
                    beam, 0.02f, 0.08f, false);
            }

            // ── Two cots under the canvas ───────────────────────────────────
            for (int i = 0; i < 2; i++)
            {
                float cx = (i == 0) ? -0.85f : 0.85f;
                float cy = 0.28f;
                float yaw = Jit(3f);
                var q = Quaternion.Euler(0f, yaw, 0f);
                Make(PrimitiveType.Cube, $"CotFrame_{i}", new Vector3(cx, cy, -0.45f),
                    new Vector3(0.78f, 0.10f, 1.85f), q, plank, 0.02f, 0.10f, false);
                for (int l = 0; l < 4; l++)
                {
                    float lx = (l % 2 == 0) ? -0.30f : 0.30f;
                    float lz = (l < 2) ? 0.78f : -0.78f;
                    Make(PrimitiveType.Cube, $"CotLeg_{i}_{l}",
                        new Vector3(cx + lx, 0.12f, -0.45f + lz),
                        new Vector3(0.08f, 0.24f, 0.08f), q,
                        beam, 0.02f, 0.08f, false);
                }
                Make(PrimitiveType.Cube, $"CotBlanket_{i}", new Vector3(cx, cy + 0.08f, -0.72f),
                    new Vector3(0.74f, 0.09f, 1.15f),
                    Quaternion.Euler(Jit(1f), yaw, Jit(1.5f)),
                    blanket * (i == 0 ? 1f : 0.93f), 0.0f, 0.05f, false);
                Make(PrimitiveType.Cube, $"CotPillow_{i}", new Vector3(cx, cy + 0.10f, 0.28f),
                    new Vector3(0.48f, 0.10f, 0.34f),
                    Quaternion.Euler(Jit(2f), yaw + Jit(4f), 0f),
                    linen, 0.0f, 0.07f, false);
            }

            // ── Supply crates by the front-right corner ─────────────────────
            Make(PrimitiveType.Cube, "Crate_A", new Vector3(1.85f, 0.26f, 1.30f),
                new Vector3(0.52f, 0.52f, 0.52f), Quaternion.Euler(0f, Jit(8f), 0f),
                plank, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "Crate_B", new Vector3(1.82f, 0.72f, 1.28f),
                new Vector3(0.42f, 0.40f, 0.42f), Quaternion.Euler(Jit(1.5f), 18f + Jit(6f), 0f),
                plank * 0.90f, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "Crate_C", new Vector3(1.30f, 0.20f, 1.52f),
                new Vector3(0.40f, 0.40f, 0.40f), Quaternion.Euler(0f, Jit(10f), 0f),
                plank * 1.06f, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "CrateLid", new Vector3(1.28f, 0.43f, 1.50f),
                new Vector3(0.46f, 0.05f, 0.46f), Quaternion.Euler(Jit(2f), Jit(8f), 2.5f),
                beam, 0.02f, 0.10f, false);

            // ── Water barrel at the front-left corner ───────────────────────
            Make(PrimitiveType.Cylinder, "BarrelStaves", new Vector3(-1.80f, 0.42f, 1.35f),
                new Vector3(0.58f, 0.42f, 0.58f), Quaternion.Euler(Jit(1f), 0f, Jit(1f)),
                plank * 0.85f, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "BarrelHoop_T", new Vector3(-1.80f, 0.74f, 1.35f),
                new Vector3(0.61f, 0.03f, 0.61f), Quaternion.identity, iron, 0.55f, 0.40f, false);
            Make(PrimitiveType.Cylinder, "BarrelHoop_B", new Vector3(-1.80f, 0.16f, 1.35f),
                new Vector3(0.61f, 0.03f, 0.61f), Quaternion.identity, iron, 0.55f, 0.40f, false);
            Make(PrimitiveType.Cylinder, "BarrelWater", new Vector3(-1.80f, 0.80f, 1.35f),
                new Vector3(0.50f, 0.015f, 0.50f), Quaternion.identity, water, 0.10f, 0.85f, false);

            // ── Banner pole with faction pennant (accent 1) ─────────────────
            Make(PrimitiveType.Cylinder, "BannerPole", new Vector3(-2.05f, 1.35f, -0.90f),
                new Vector3(0.07f, 1.35f, 0.07f), Quaternion.Euler(Jit(1.5f), 0f, 2f),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Sphere, "BannerFinial", new Vector3(-2.00f, 2.74f, -0.90f),
                new Vector3(0.13f, 0.13f, 0.13f), Quaternion.identity,
                iron, 0.60f, 0.45f, false);
            Make(PrimitiveType.Cube, "Stripe_1", new Vector3(-1.72f, 2.50f, -0.90f),
                new Vector3(0.55f, 0.34f, 0.03f), Quaternion.Euler(Jit(2f), Jit(4f), -6f),
                Color.white, 0.0f, 0.12f, false);

            // ── Lantern by the tent mouth — soft white emissive ─────────────
            Make(PrimitiveType.Cylinder, "LanternPost", new Vector3(0.62f, 0.72f, 1.45f),
                new Vector3(0.06f, 0.72f, 0.06f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "LanternArm", new Vector3(0.50f, 1.42f, 1.45f),
                new Vector3(0.30f, 0.05f, 0.05f), Quaternion.Euler(0f, 0f, Jit(2f)),
                iron, 0.55f, 0.35f, false);
            Make(PrimitiveType.Cube, "LanternGlow", new Vector3(0.36f, 1.28f, 1.45f),
                new Vector3(0.16f, 0.22f, 0.16f), Quaternion.Euler(0f, Jit(8f), 0f),
                lantern, 0.0f, 0.20f, true);

            return root;
        }
    }
}
