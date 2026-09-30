// MineVisual.cs
// THE THREE ORE EXTRACTORS, ONE FAMILY: the iron Mine, the Veilstone Mine and
// the veilsteel extractor (Alanthor_Smelter) share one procedural pithead,
// and the ORE tells them apart:
//
//   Iron       rust-brown ore, dull iron fittings
//   Veilstone  cyan crystal that glows
//   Veilsteel  dark blue-steel crystal, a cold sheen
//
// REDONE 2026-09-29 for the 2 x 2-cell (4 x 4 m) footprint every resource
// building now takes (docs/Design/Build_Grid.md §3) — it stands exactly on
// its node, which is the same size. A pithead that small cannot carry the old
// 8 m layout's shed, rails and cart without turning to clutter, so it is
// built around three things that read at RTS distance: the TIMBER HEADFRAME
// with its sheave wheel (the silhouette that says "mine"), the SHAFT it
// stands over, and the ORE heaped at its foot (the colour that says which).
// A winch and one ore cart sit at the back; the faction pennant flies from
// the headframe.
//
// Built at its final size (procedural visuals are not footprint-fitted) and
// used by both the spawn and the placement ghost, so the ghost is the
// building. Part names carry a rise group (1_ platform .. 4_ props);
// "4_Stripe_" takes the faction tint; nothing is named "roof".

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public enum MineKind : byte { Iron = 0, Veilstone = 1, Veilsteel = 2 }

    public static class MineVisual
    {
        /// <summary>The kind of pithead a building id raises, or null.</summary>
        public static MineKind? KindFor(string buildingId) => buildingId switch
        {
            "Mine"             => MineKind.Iron,
            "VeilstoneMine"    => MineKind.Veilstone,
            "Alanthor_Smelter" => MineKind.Veilsteel,
            _                  => (MineKind?)null,
        };

        /// <summary>Half the footprint, metres — everything stays inside it.</summary>
        private const float Half = 1.9f;

        public static GameObject Build(int seed, MineKind kind)
        {
            var rng = new System.Random(seed);
            var root = new GameObject(kind + "MineVisual");

            var stone      = new Color(0.44f, 0.43f, 0.41f);
            var stoneDark  = new Color(0.31f, 0.30f, 0.29f);
            var timber     = new Color(0.41f, 0.30f, 0.19f);
            var timberDark = new Color(0.29f, 0.21f, 0.13f);
            var iron       = new Color(0.21f, 0.20f, 0.19f);
            var rope       = new Color(0.60f, 0.52f, 0.36f);
            var pennant    = new Color(0.86f, 0.84f, 0.79f);
            var shaft      = new Color(0.03f, 0.03f, 0.03f);
            var lantern    = new Color(1.0f, 0.72f, 0.35f);

            Color ore; float oreMetal, oreSmooth; bool oreGlow;
            switch (kind)
            {
                case MineKind.Veilstone:
                    ore = new Color(0.30f, 0.85f, 0.95f); oreMetal = 0.1f; oreSmooth = 0.8f; oreGlow = true; break;
                case MineKind.Veilsteel:
                    ore = new Color(0.22f, 0.30f, 0.42f); oreMetal = 0.85f; oreSmooth = 0.7f; oreGlow = false; break;
                default:
                    ore = new Color(0.47f, 0.27f, 0.17f); oreMetal = 0.35f; oreSmooth = 0.2f; oreGlow = false; break;
            }

            float Jit(float r) => (float)(rng.NextDouble() * 2.0 - 1.0) * r;
            Quaternion Tilt(float deg) => Quaternion.Euler(Jit(deg), Jit(deg), Jit(deg));
            GameObject Make(PrimitiveType t, string name, Vector3 p, Vector3 s, Quaternion r,
                            Color c, float metal, float smooth, bool glow = false)
                => ProceduralPrimitive.Make(t, name, root.transform, p, s, r, c, metal, smooth, glow);

            // ── 1: a low stone-curbed platform, the whole footprint ────────
            Make(PrimitiveType.Cube, "1_Platform", new Vector3(0f, 0.1f, 0f),
                new Vector3(Half * 2f, 0.2f, Half * 2f), Quaternion.identity, stoneDark, 0.05f, 0.1f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Make(PrimitiveType.Cube, "1_Curb", dir * (Half - 0.1f) + Vector3.up * 0.26f,
                    new Vector3(Half * 2f, 0.14f, 0.2f), Quaternion.Euler(0f, a, 0f), stone, 0.05f, 0.12f);
            }

            // ── 2: the shaft and its timber collar (centre-back) ───────────
            var shaftAt = new Vector3(0f, 0f, -0.35f);
            Make(PrimitiveType.Cube, "2_ShaftMouth", shaftAt + new Vector3(0f, 0.21f, 0f),
                new Vector3(1.1f, 0.03f, 1.1f), Quaternion.identity, shaft, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Make(PrimitiveType.Cube, "2_Collar", shaftAt + dir * 0.62f + Vector3.up * 0.3f,
                    new Vector3(1.36f, 0.18f, 0.16f), Quaternion.Euler(0f, a, 0f), timberDark, 0.05f, 0.18f);
            }

            // ── 3: the headframe — four raked legs, braces, sheave wheel ───
            float frameH = 3.4f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var foot = shaftAt + new Vector3(sx * 0.8f, 0.3f, sz * 0.8f);
                var top = shaftAt + new Vector3(sx * 0.28f, frameH, sz * 0.28f);
                var d = top - foot;
                Make(PrimitiveType.Cube, "3_Leg", (foot + top) * 0.5f, new Vector3(0.14f, d.magnitude, 0.14f),
                    Quaternion.FromToRotation(Vector3.up, d.normalized), timber, 0.05f, 0.2f);
            }
            for (int k = 0; k < 2; k++)
            {
                float y = 1.3f + k * 1.1f;
                float w = 1.45f - k * 0.4f;
                Make(PrimitiveType.Cube, "3_Brace", shaftAt + Vector3.up * y,
                    new Vector3(w, 0.09f, 0.09f), Quaternion.identity, timberDark, 0.05f, 0.2f);
                Make(PrimitiveType.Cube, "3_Brace", shaftAt + Vector3.up * y,
                    new Vector3(0.09f, 0.09f, w), Quaternion.identity, timberDark, 0.05f, 0.2f);
            }
            Make(PrimitiveType.Cube, "3_HeadBeam", shaftAt + Vector3.up * frameH,
                new Vector3(0.75f, 0.14f, 0.75f), Quaternion.identity, timberDark, 0.05f, 0.2f);
            var wheel = Make(PrimitiveType.Cylinder, "3_Sheave", shaftAt + Vector3.up * (frameH + 0.4f),
                new Vector3(0.8f, 0.05f, 0.8f), Quaternion.Euler(0f, 0f, 90f), iron, 0.65f, 0.4f);
            for (int s = 0; s < 3; s++)
                ProceduralPrimitive.Make(PrimitiveType.Cube, "3_Spoke", wheel.transform, Vector3.zero,
                    new Vector3(0.95f, 1.2f, 0.07f), Quaternion.Euler(0f, s * 60f, 0f), iron, 0.65f, 0.4f, false);
            Make(PrimitiveType.Cylinder, "3_HoistRope", shaftAt + Vector3.up * ((frameH + 0.35f) * 0.5f + 0.2f),
                new Vector3(0.03f, (frameH + 0.1f) * 0.5f, 0.03f), Quaternion.identity, rope, 0f, 0.1f);
            Make(PrimitiveType.Cube, "3_Bucket", shaftAt + Vector3.up * 0.95f,
                new Vector3(0.36f, 0.32f, 0.36f), Tilt(3f), iron, 0.55f, 0.35f);

            // ── 4: the ORE, heaped at the front — the part that says which ─
            var heap = new Vector3(0f, 0.2f, 1.05f);
            for (int k = 0; k < 7; k++)
            {
                float r = 0.26f + (float)rng.NextDouble() * 0.22f;
                var p = heap + new Vector3(Jit(0.95f), r * 0.45f, Jit(0.45f));
                OreLump(Make, "4_Ore", p, r, kind, ore, oreMetal, oreSmooth, oreGlow, rng);
            }

            // ── 4: winch and cart at the back corners ──────────────────────
            Make(PrimitiveType.Cylinder, "4_WinchDrum", new Vector3(-1.2f, 0.52f, -1.35f),
                new Vector3(0.42f, 0.28f, 0.42f), Quaternion.Euler(0f, 0f, 90f), timberDark, 0.05f, 0.2f);
            for (int side = -1; side <= 1; side += 2)
                Make(PrimitiveType.Cube, "4_WinchPost", new Vector3(-1.2f + side * 0.32f, 0.4f, -1.35f),
                    new Vector3(0.08f, 0.45f, 0.3f), Quaternion.identity, timber, 0.05f, 0.2f);

            var cartPos = new Vector3(1.2f, 0.48f, -1.3f);
            Make(PrimitiveType.Cube, "4_Cart", cartPos, new Vector3(0.62f, 0.34f, 0.8f),
                Quaternion.Euler(0f, 12f + Jit(4f), 0f), timberDark, 0.1f, 0.2f);
            for (int w = 0; w < 4; w++)
                Make(PrimitiveType.Cylinder, "4_CartWheel",
                    cartPos + new Vector3(w % 2 == 0 ? -0.33f : 0.33f, -0.2f, w < 2 ? -0.26f : 0.26f),
                    new Vector3(0.22f, 0.03f, 0.22f), Quaternion.Euler(0f, 0f, 90f), iron, 0.6f, 0.4f);
            for (int k = 0; k < 3; k++)
                OreLump(Make, "4_CartLoad", cartPos + new Vector3(Jit(0.15f), 0.2f, Jit(0.25f)),
                    0.22f, kind, ore, oreMetal, oreSmooth, oreGlow, rng);

            // ── 4: lantern on the collar, pennant at the head ──────────────
            Make(PrimitiveType.Cube, "4_Lantern", shaftAt + new Vector3(0.7f, 0.62f, 0.7f),
                new Vector3(0.12f, 0.16f, 0.12f), Quaternion.identity, lantern, 0f, 0.5f, glow: true);
            Make(PrimitiveType.Cylinder, "4_PennantStaff", shaftAt + new Vector3(0.28f, frameH + 0.55f, 0.28f),
                new Vector3(0.03f, 0.45f, 0.03f), Quaternion.identity, timberDark, 0.05f, 0.2f);
            Make(PrimitiveType.Cube, "4_Stripe_Pennant", shaftAt + new Vector3(0.28f, frameH + 0.82f, 0.46f),
                new Vector3(0.02f, 0.22f, 0.34f), Quaternion.identity, pennant, 0f, 0.1f);

            return root;
        }

        /// <summary>One piece of ore: a lump for iron, a crystal for the veil ores.</summary>
        private static void OreLump(
            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject> make,
            string name, Vector3 p, float size, MineKind kind, Color c, float metal, float smooth, bool glow,
            System.Random rng)
        {
            var rot = Quaternion.Euler((float)rng.NextDouble() * 40f - 20f,
                                       (float)rng.NextDouble() * 360f,
                                       (float)rng.NextDouble() * 40f - 20f);
            if (kind == MineKind.Iron)
                make(PrimitiveType.Sphere, name, p, new Vector3(size, size * 0.75f, size), rot, c, metal, smooth, glow);
            else
                make(PrimitiveType.Cube, name, p + Vector3.up * size * 0.4f,
                    new Vector3(size * 0.45f, size * 1.6f, size * 0.45f), rot, c, metal, smooth, glow);
        }
    }
}
