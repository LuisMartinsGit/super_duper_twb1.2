// Procedural Fiendstone Keep — the Age-0 choice fortress, biggest silhouette
// after the Hall (footprint 5x5, ~7 m tall). Massive dark-stone bailey on a
// battered plinth, four crenellated corner towers, a taller central keep
// tower carrying the fire-arrow platform (the Keep auto-fires: a visible
// ballista emplacement with an ember-tipped bolt sits on the deck), arched
// gatehouse with an iron portcullis hint, arrow slits, and two wing-stub
// annex pads on the east/west faces (the Keep levels by building WINGS —
// see KeepWingComponents.cs; the pads read as where a wing will grow).
// Player-color accents: "4_Stripe_1" gatehouse banner and "4_Stripe_2" keep
// banner (tinted by BuildingFactionColorMarker via the "stripe" name rule).
// Emissive accents: gatehouse brazier coals + the ballista's fire-bolt head.
//
// Standalone static builder: the orchestrator (PresentationSpawnSystem) wires
// pid 540 and handles FinishProceduralBuilding (collider / EntityReference /
// faction marker / construction rise) after Build returns. Part names carry
// leading rise-group numbers (1_ plinth, 2_ walls, 3_ towers, 4_ props) so
// BuildingRiseData staggers the construction rise bottom-up.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class FiendstoneKeepVisual
    {
        public static GameObject Build(int seed)
        {
            var rng = new System.Random(seed);

            var root = new GameObject("FiendstoneKeep");

            // ── Palette ─────────────────────────────────────────────────────
            var basalt      = new Color(0.36f, 0.32f, 0.30f);   // dark fiendstone block
            var basaltDark  = new Color(0.24f, 0.21f, 0.19f);   // course bands / shadow lines
            var basaltLight = new Color(0.45f, 0.41f, 0.38f);   // weathered upper courses
            var slitDark    = new Color(0.06f, 0.05f, 0.05f);   // arrow-slit / doorway voids
            var iron        = new Color(0.19f, 0.18f, 0.17f);   // portcullis / fittings
            var beam        = new Color(0.32f, 0.21f, 0.13f);   // dark oak timber
            var plank       = new Color(0.45f, 0.31f, 0.18f);   // deck planking
            var embers      = new Color(0.95f, 0.42f, 0.10f);   // brazier coal bed
            var fireTip     = new Color(1.00f, 0.55f, 0.12f);   // fire-bolt head


            System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
            Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
                ProceduralPrimitive.Make(type, name, root.transform, lp, ls, lr, color, metal, smooth, glow);

            // Tiny deterministic jitter helper (hand-built feel).
            System.Func<float, float> Jit = range => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

            // ══ 1_ PLINTH — battered footing the whole fortress sits on ═════
            Make(PrimitiveType.Cube, "1_Plinth", new Vector3(0f, 0.25f, 0f),
                new Vector3(5.0f, 0.5f, 5.0f), Quaternion.Euler(0f, Jit(0.8f), 0f),
                basaltDark, 0.05f, 0.10f, false);

            // Battered (outward-sloped) skirt slabs on all four faces.
            Make(PrimitiveType.Cube, "1_Batter_N", new Vector3(0f, 0.62f, -2.32f),
                new Vector3(4.6f, 0.75f, 0.5f), Quaternion.Euler(12f, 0f, 0f),
                basalt, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "1_Batter_S", new Vector3(0f, 0.62f, 2.32f),
                new Vector3(4.6f, 0.75f, 0.5f), Quaternion.Euler(-12f, 0f, 0f),
                basalt, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "1_Batter_E", new Vector3(2.32f, 0.62f, 0f),
                new Vector3(0.5f, 0.75f, 4.6f), Quaternion.Euler(0f, 0f, 12f),
                basalt, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "1_Batter_W", new Vector3(-2.32f, 0.62f, 0f),
                new Vector3(0.5f, 0.75f, 4.6f), Quaternion.Euler(0f, 0f, -12f),
                basalt, 0.05f, 0.12f, false);

            // Rough footing stones half-sunk at the plinth corners.
            for (int i = 0; i < 4; i++)
            {
                float ang = (45f + 90f * i) * Mathf.Deg2Rad;
                Make(PrimitiveType.Cube, $"1_FootStone_{i}",
                    new Vector3(Mathf.Cos(ang) * 3.15f, 0.30f + Jit(0.04f), Mathf.Sin(ang) * 3.15f),
                    new Vector3(0.72f + Jit(0.10f), 0.48f, 0.62f + Jit(0.10f)),
                    Quaternion.Euler(Jit(2f), (float)(rng.NextDouble() * 90.0), Jit(2f)),
                    basaltDark * 0.92f, 0.05f, 0.10f, false);
            }

            // Threshold step before the gatehouse.
            Make(PrimitiveType.Cube, "1_GateStep", new Vector3(0f, 0.14f, 2.92f),
                new Vector3(2.1f, 0.28f, 0.8f), Quaternion.Euler(0f, Jit(1f), 0f),
                basalt * 0.95f, 0.05f, 0.12f, false);

            // ══ 2_ WALLS — bailey block, parapet, gatehouse, wing pads ══════
            Make(PrimitiveType.Cube, "2_Bailey", new Vector3(0f, 1.85f, 0f),
                new Vector3(4.3f, 2.7f, 4.3f), Quaternion.Euler(0f, Jit(0.4f), 0f),
                basalt, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "2_CourseBelt", new Vector3(0f, 2.42f, 0f),
                new Vector3(4.38f, 0.12f, 4.38f), Quaternion.identity,
                basaltDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "2_WallCap", new Vector3(0f, 3.28f, 0f),
                new Vector3(4.55f, 0.18f, 4.55f), Quaternion.identity,
                basaltDark, 0.05f, 0.12f, false);

            // Crenellated bailey parapet: three merlons on back/east/west,
            // two flanking the gatehouse on the front (south, +Z).
            for (int i = 0; i < 3; i++)
            {
                float off = -1.5f + 1.5f * i;
                Make(PrimitiveType.Cube, $"2_Merlon_N{i}",
                    new Vector3(off, 3.56f, -2.2f), new Vector3(0.60f, 0.42f, 0.32f),
                    Quaternion.Euler(0f, Jit(1.5f), 0f), basaltLight, 0.05f, 0.14f, false);
                Make(PrimitiveType.Cube, $"2_Merlon_E{i}",
                    new Vector3(2.2f, 3.56f, off), new Vector3(0.32f, 0.42f, 0.60f),
                    Quaternion.Euler(0f, Jit(1.5f), 0f), basaltLight, 0.05f, 0.14f, false);
                Make(PrimitiveType.Cube, $"2_Merlon_W{i}",
                    new Vector3(-2.2f, 3.56f, off), new Vector3(0.32f, 0.42f, 0.60f),
                    Quaternion.Euler(0f, Jit(1.5f), 0f), basaltLight, 0.05f, 0.14f, false);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Make(PrimitiveType.Cube, $"2_Merlon_S{(s + 1) / 2}",
                    new Vector3(s * 1.65f, 3.56f, 2.2f), new Vector3(0.60f, 0.42f, 0.32f),
                    Quaternion.Euler(0f, Jit(1.5f), 0f), basaltLight, 0.05f, 0.14f, false);
            }

            // Arrow slits — dark inset quads proud of the bailey faces.
            Make(PrimitiveType.Cube, "2_Slit_E", new Vector3(2.17f, 2.05f, 0.65f),
                new Vector3(0.10f, 0.72f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_W", new Vector3(-2.17f, 2.25f, -0.55f),
                new Vector3(0.10f, 0.72f, 0.14f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_N", new Vector3(0.70f, 2.10f, -2.17f),
                new Vector3(0.14f, 0.72f, 0.10f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_S1", new Vector3(1.55f, 2.30f, 2.17f),
                new Vector3(0.14f, 0.62f, 0.10f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "2_Slit_S2", new Vector3(-1.55f, 2.30f, 2.17f),
                new Vector3(0.14f, 0.62f, 0.10f), Quaternion.identity, slitDark, 0f, 0.05f, false);

            // Gatehouse — projecting block on the front face with its own
            // cap and merlons.
            Make(PrimitiveType.Cube, "2_Gatehouse", new Vector3(0f, 1.55f, 2.55f),
                new Vector3(2.2f, 3.1f, 0.9f), Quaternion.identity,
                basalt * 1.03f, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "2_GatehouseCap", new Vector3(0f, 3.16f, 2.55f),
                new Vector3(2.4f, 0.16f, 1.1f), Quaternion.identity,
                basaltDark, 0.05f, 0.12f, false);
            for (int i = 0; i < 3; i++)
            {
                Make(PrimitiveType.Cube, $"2_GateMerlon_{i}",
                    new Vector3(-0.85f + 0.85f * i, 3.42f, 2.55f),
                    new Vector3(0.42f, 0.38f, 0.95f),
                    Quaternion.Euler(0f, Jit(1.5f), 0f), basaltLight, 0.05f, 0.14f, false);
            }

            // Arch: lintel + jambs framing the gate void.
            Make(PrimitiveType.Cube, "2_GateArch_Top", new Vector3(0f, 2.38f, 3.02f),
                new Vector3(1.55f, 0.35f, 0.14f), Quaternion.identity,
                basaltLight, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "2_GateJamb_L", new Vector3(-0.74f, 1.30f, 3.02f),
                new Vector3(0.24f, 2.30f, 0.14f), Quaternion.identity,
                basaltLight, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "2_GateJamb_R", new Vector3(0.74f, 1.30f, 3.02f),
                new Vector3(0.24f, 2.30f, 0.14f), Quaternion.identity,
                basaltLight, 0.05f, 0.14f, false);
            Make(PrimitiveType.Cube, "2_GateDoor", new Vector3(0f, 1.15f, 2.98f),
                new Vector3(1.30f, 2.10f, 0.08f), Quaternion.identity,
                slitDark, 0f, 0.05f, false);

            // Iron portcullis hint — vertical bars + two cross bars just
            // proud of the door void.
            for (int i = 0; i < 4; i++)
            {
                Make(PrimitiveType.Cylinder, $"2_Portcullis_V{i}",
                    new Vector3(-0.45f + 0.30f * i, 1.20f, 3.04f),
                    new Vector3(0.05f, 0.95f, 0.05f), Quaternion.identity,
                    iron, 0.80f, 0.45f, false);
            }
            for (int i = 0; i < 2; i++)
            {
                Make(PrimitiveType.Cylinder, $"2_Portcullis_H{i}",
                    new Vector3(0f, 0.85f + 0.75f * i, 3.05f),
                    new Vector3(0.05f, 0.62f, 0.05f), Quaternion.Euler(0f, 0f, 90f),
                    iron, 0.80f, 0.45f, false);
            }

            // Wing-stub annex pads on the east/west faces — the visible
            // anchor points where the Keep's chosen wings will grow.
            for (int s = -1; s <= 1; s += 2)
            {
                string side = s < 0 ? "W" : "E";
                Make(PrimitiveType.Cube, $"2_WingPad_{side}",
                    new Vector3(s * 2.30f, 0.85f, 0f), new Vector3(0.60f, 1.20f, 2.00f),
                    Quaternion.Euler(0f, Jit(0.6f), 0f), basalt * 0.97f, 0.05f, 0.13f, false);
                Make(PrimitiveType.Cube, $"2_WingPadCap_{side}",
                    new Vector3(s * 2.30f, 1.50f, 0f), new Vector3(0.72f, 0.14f, 2.12f),
                    Quaternion.identity, basaltDark, 0.05f, 0.12f, false);
                Make(PrimitiveType.Cube, $"2_WingDoor_{side}",
                    new Vector3(s * 2.62f, 0.75f, 0.55f), new Vector3(0.06f, 0.85f, 0.50f),
                    Quaternion.identity, slitDark, 0f, 0.05f, false);
            }

            // ══ 3_ TOWERS — four corner towers + the central keep tower ═════
            for (int i = 0; i < 4; i++)
            {
                float tx = (i % 2 == 0 ? -1f : 1f) * 1.95f;
                float tz = (i < 2 ? -1f : 1f) * 1.95f;

                Make(PrimitiveType.Cylinder, $"3_Tower_{i}",
                    new Vector3(tx, 2.35f, tz), new Vector3(1.20f, 2.35f, 1.20f),
                    Quaternion.Euler(Jit(0.5f), 0f, Jit(0.5f)),
                    basalt * (0.98f + 0.04f * (i % 2)), 0.05f, 0.14f, false);
                Make(PrimitiveType.Cylinder, $"3_TowerCap_{i}",
                    new Vector3(tx, 4.74f, tz), new Vector3(1.40f, 0.12f, 1.40f),
                    Quaternion.identity, basaltDark, 0.05f, 0.12f, false);

                // Crenellated crown: four merlons around each tower rim.
                for (int j = 0; j < 4; j++)
                {
                    float ang = (45f + 90f * j) * Mathf.Deg2Rad;
                    float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                    Make(PrimitiveType.Cube, $"3_TowerMerlon_{i}_{j}",
                        new Vector3(tx + cx * 0.60f, 5.02f, tz + cz * 0.60f),
                        new Vector3(0.34f, 0.40f, 0.26f),
                        Quaternion.Euler(0f, -Mathf.Atan2(cz, cx) * Mathf.Rad2Deg + 90f + Jit(2f), 0f),
                        basaltLight, 0.05f, 0.14f, false);
                }
            }

            // Central keep tower — the tallest mass, carries the fire-arrow
            // platform on its deck.
            Make(PrimitiveType.Cube, "3_Keep", new Vector3(0f, 4.25f, 0f),
                new Vector3(2.20f, 2.60f, 2.20f), Quaternion.Euler(0f, Jit(0.5f), 0f),
                basalt * 1.05f, 0.05f, 0.15f, false);
            Make(PrimitiveType.Cube, "3_KeepCourse", new Vector3(0f, 5.02f, 0f),
                new Vector3(2.26f, 0.12f, 2.26f), Quaternion.identity,
                basaltDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_KeepSlit_S", new Vector3(0f, 4.45f, 1.12f),
                new Vector3(0.13f, 0.60f, 0.10f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "3_KeepSlit_N", new Vector3(0.35f, 4.65f, -1.12f),
                new Vector3(0.13f, 0.60f, 0.10f), Quaternion.identity, slitDark, 0f, 0.05f, false);
            Make(PrimitiveType.Cube, "3_KeepCap", new Vector3(0f, 5.62f, 0f),
                new Vector3(2.50f, 0.16f, 2.50f), Quaternion.identity,
                basaltDark, 0.05f, 0.12f, false);
            Make(PrimitiveType.Cube, "3_KeepDeck", new Vector3(0f, 5.76f, 0f),
                new Vector3(2.30f, 0.10f, 2.30f), Quaternion.identity,
                basalt * 1.08f, 0.05f, 0.18f, false);

            // Keep crown: eight merlons around the square rim.
            for (int i = 0; i < 8; i++)
            {
                float ang = 45f * i * Mathf.Deg2Rad;
                // Square rim: push the diagonal merlons out to the corners.
                float rr = (i % 2 == 0) ? 1.05f : 1.40f;
                float mx = Mathf.Cos(ang) * rr, mz = Mathf.Sin(ang) * rr;
                mx = Mathf.Clamp(mx, -1.05f, 1.05f);
                mz = Mathf.Clamp(mz, -1.05f, 1.05f);
                Make(PrimitiveType.Cube, $"3_KeepMerlon_{i}",
                    new Vector3(mx, 6.00f, mz), new Vector3(0.40f, 0.44f, 0.30f),
                    Quaternion.Euler(0f, 45f * i + 90f + Jit(2f), 0f),
                    basaltLight, 0.05f, 0.14f, false);
            }

            // ══ 4_ PROPS — ballista platform, brazier, banners ══════════════
            // Fire-arrow emplacement on the keep deck: planked turntable,
            // swivel post, angled stock, two prod arms, winch, and the loaded
            // bolt with an ember-glowing fire head (the Keep auto-fires).
            Make(PrimitiveType.Cylinder, "4_BallistaBase", new Vector3(0f, 5.88f, 0f),
                new Vector3(0.85f, 0.06f, 0.85f), Quaternion.identity,
                plank, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_BallistaPost", new Vector3(0f, 6.02f, 0f),
                new Vector3(0.20f, 0.24f, 0.20f), Quaternion.Euler(0f, Jit(4f), 0f),
                iron, 0.80f, 0.40f, false);
            Make(PrimitiveType.Cube, "4_BallistaStock", new Vector3(0f, 6.28f, 0.12f),
                new Vector3(0.16f, 0.12f, 1.55f), Quaternion.Euler(-14f, 0f, 0f),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_BallistaArm_L", new Vector3(-0.44f, 6.24f, -0.42f),
                new Vector3(0.78f, 0.08f, 0.08f), Quaternion.Euler(0f, 32f, 0f),
                beam * 0.94f, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_BallistaArm_R", new Vector3(0.44f, 6.24f, -0.42f),
                new Vector3(0.78f, 0.08f, 0.08f), Quaternion.Euler(0f, -32f, 0f),
                beam * 0.94f, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cylinder, "4_BallistaWinch", new Vector3(0f, 6.12f, -0.58f),
                new Vector3(0.07f, 0.30f, 0.07f), Quaternion.Euler(0f, 0f, 90f),
                iron, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "4_BallistaBolt", new Vector3(0f, 6.42f, 0.55f),
                new Vector3(0.045f, 0.62f, 0.045f), Quaternion.Euler(76f, 0f, 0f),
                iron * 1.15f, 0.75f, 0.40f, false);
            Make(PrimitiveType.Sphere, "4_BoltFireHead", new Vector3(0f, 6.57f, 1.13f),
                new Vector3(0.16f, 0.16f, 0.16f), Quaternion.identity,
                fireTip, 0f, 0.05f, true);

            // Brazier on the gatehouse cap — stand, bowl, glowing coal bed.
            Make(PrimitiveType.Cylinder, "4_BrazierStand", new Vector3(0.95f, 3.36f, 2.55f),
                new Vector3(0.12f, 0.20f, 0.12f), Quaternion.identity,
                iron, 0.80f, 0.45f, false);
            Make(PrimitiveType.Cylinder, "4_BrazierBowl", new Vector3(0.95f, 3.58f, 2.55f),
                new Vector3(0.46f, 0.12f, 0.46f), Quaternion.Euler(Jit(1.5f), 0f, Jit(1.5f)),
                iron * 1.1f, 0.80f, 0.40f, false);
            Make(PrimitiveType.Sphere, "4_BrazierCoals", new Vector3(0.95f, 3.68f, 2.55f),
                new Vector3(0.36f, 0.16f, 0.36f), Quaternion.identity,
                embers, 0f, 0.05f, true);

            // Faction banners: gatehouse front above the arch, and a long
            // drop banner hung from the keep cap down the south face.
            Make(PrimitiveType.Cube, "4_Stripe_1", new Vector3(0f, 2.78f, 3.08f),
                new Vector3(0.72f, 0.90f, 0.05f), Quaternion.Euler(Jit(1f), 0f, Jit(1.5f)),
                Color.white, 0f, 0.15f, false);
            Make(PrimitiveType.Cylinder, "4_BannerRod", new Vector3(0f, 5.70f, 1.18f),
                new Vector3(0.06f, 0.50f, 0.06f), Quaternion.Euler(90f, 90f, 0f),
                beam, 0.02f, 0.10f, false);
            Make(PrimitiveType.Cube, "4_Stripe_2", new Vector3(0f, 4.85f, 1.20f),
                new Vector3(0.65f, 1.65f, 0.05f), Quaternion.Euler(Jit(1f), 0f, Jit(1.5f)),
                Color.white, 0f, 0.15f, false);

            return root;
        }
    }
}
