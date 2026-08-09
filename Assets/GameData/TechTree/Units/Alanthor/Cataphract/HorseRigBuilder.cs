// File: Assets/GameData/TechTree/Units/Alanthor/Cataphract/HorseRigBuilder.cs
// Shared procedural horse anatomy for the Alanthor cavalry line (Outrider,
// Cataphract, King Lexor). Builds a primitive-composition horse under a
// "Horse" child root: barrel/chest/rump spheres, neck, head + muzzle + ears,
// four two-segment legs hung from named swing pivots ("LegFL", "LegFR",
// "LegBL", "LegBR"), and a sway pivot tail ("Tail"). The gait animators find
// those pivots by name (LedgerVisual FindDeep convention).
//
// Parameterized by bulk (width multiplier) and armored (adds neck cops,
// swaps the mane for barding, iron-shoes the hooves). All randomness flows
// through the caller's System.Random so lockstep clients agree.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class HorseRigBuilder
    {
        private static Shader _lit;

        private static Shader LitShader
        {
            get
            {
                if (_lit == null)
                    _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                return _lit;
            }
        }

        /// <summary>
        /// Creates a collider-stripped primitive part with its own URP/Lit
        /// material (Smelter Make idiom). Public so the cavalry visuals reuse
        /// one part factory instead of three copies.
        /// </summary>
        public static GameObject Part(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Quaternion localRot,
            Color color, float metallic, float smoothness, bool glow = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(LitShader);
                r.material.color = color;
                if (r.material.HasProperty("_Metallic"))   r.material.SetFloat("_Metallic", metallic);
                if (r.material.HasProperty("_Smoothness")) r.material.SetFloat("_Smoothness", smoothness);
                if (glow && r.material.HasProperty("_EmissionColor"))
                {
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", color * 1.6f);
                }
            }
            var c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
            return go;
        }

        /// <summary>
        /// Cylinder stretched between two parent-local points — reins, straps,
        /// limbs, cape stays. Radius is the half-thickness in meters.
        /// </summary>
        public static GameObject Strut(string name, Transform parent,
            Vector3 from, Vector3 to, float radius,
            Color color, float metallic, float smoothness)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            var rot = len > 0.0001f
                ? Quaternion.FromToRotation(Vector3.up, dir / len)
                : Quaternion.identity;
            return Part(PrimitiveType.Cylinder, name, parent,
                (from + to) * 0.5f, new Vector3(radius * 2f, len * 0.5f, radius * 2f),
                rot, color, metallic, smoothness);
        }

        /// <summary>Deterministic jitter in [-degrees, +degrees].</summary>
        public static float Jitter(System.Random rng, float degrees)
            => (float)(rng.NextDouble() * 2.0 - 1.0) * degrees;

        /// <summary>
        /// Builds the horse under <paramref name="parent"/> and returns the
        /// "Horse" child root. Horse faces +Z, hooves rest at local y = 0,
        /// withers around y = 1.45 before any unit scaling. The four leg
        /// swing pivots come back via out params (also findable by name).
        /// </summary>
        public static GameObject Build(Transform parent, System.Random rng, float bulk, bool armored,
            Color coat, Color mane, Color hoof, Color armor,
            out Transform legFL, out Transform legFR, out Transform legBL, out Transform legBR)
        {
            var horse = new GameObject("Horse");
            horse.transform.SetParent(parent, false);
            horse.transform.localPosition = Vector3.zero;
            var h = horse.transform;
            float b = bulk;

            // ── Mass: three overlapping spheres give the barrel its taper. ──
            Part(PrimitiveType.Sphere, "Barrel", h,
                new Vector3(0f, 1.02f, -0.02f), new Vector3(0.55f * b, 0.60f * b, 1.18f),
                Quaternion.Euler(0f, 0f, Jitter(rng, 1f)), coat, 0.02f, 0.28f);
            Part(PrimitiveType.Sphere, "Chest", h,
                new Vector3(0f, 1.04f, 0.48f), new Vector3(0.50f * b, 0.56f * b, 0.52f),
                Quaternion.identity, coat * 1.04f, 0.02f, 0.28f);
            Part(PrimitiveType.Sphere, "Rump", h,
                new Vector3(0f, 1.07f, -0.50f), new Vector3(0.52f * b, 0.58f * b, 0.58f),
                Quaternion.identity, coat * 0.96f, 0.02f, 0.25f);

            // ── Neck + head. Neck leans ~38 degrees toward the muzzle. ──
            float neckLean = 38f + Jitter(rng, 2f);
            Part(PrimitiveType.Cylinder, "Neck", h,
                new Vector3(0f, 1.38f, 0.66f), new Vector3(0.21f * b, 0.34f, 0.27f),
                Quaternion.Euler(neckLean, 0f, Jitter(rng, 1.5f)), coat, 0.02f, 0.28f);
            Part(PrimitiveType.Cube, "Head", h,
                new Vector3(0f, 1.64f, 0.88f), new Vector3(0.17f * b, 0.30f, 0.23f),
                Quaternion.Euler(42f + Jitter(rng, 2f), 0f, Jitter(rng, 1f)), coat * 1.02f, 0.02f, 0.30f);
            Part(PrimitiveType.Cube, "Muzzle", h,
                new Vector3(0f, 1.49f, 1.03f), new Vector3(0.12f * b, 0.17f, 0.15f),
                Quaternion.Euler(42f, 0f, 0f), coat * 0.88f, 0.02f, 0.35f);
            Part(PrimitiveType.Cube, "Ear_L", h,
                new Vector3(-0.065f * b, 1.82f, 0.78f), new Vector3(0.045f, 0.11f, 0.035f),
                Quaternion.Euler(-12f, 0f, -9f + Jitter(rng, 3f)), coat * 0.92f, 0.02f, 0.25f);
            Part(PrimitiveType.Cube, "Ear_R", h,
                new Vector3(0.065f * b, 1.82f, 0.78f), new Vector3(0.045f, 0.11f, 0.035f),
                Quaternion.Euler(-12f, 0f, 9f + Jitter(rng, 3f)), coat * 0.92f, 0.02f, 0.25f);

            if (!armored)
            {
                // Loose mane running down the neck crest.
                Part(PrimitiveType.Cube, "Mane", h,
                    new Vector3(0f, 1.56f, 0.58f), new Vector3(0.055f, 0.36f, 0.11f),
                    Quaternion.Euler(neckLean, 0f, Jitter(rng, 2f)), mane, 0.0f, 0.15f);
                Part(PrimitiveType.Cube, "ManeCrest", h,
                    new Vector3(0f, 1.30f, 0.42f), new Vector3(0.055f, 0.20f, 0.10f),
                    Quaternion.Euler(neckLean + 4f, 0f, 0f), mane * 0.9f, 0.0f, 0.15f);
                Part(PrimitiveType.Cube, "Forelock", h,
                    new Vector3(0f, 1.79f, 0.86f), new Vector3(0.09f, 0.10f, 0.05f),
                    Quaternion.Euler(20f, 0f, Jitter(rng, 4f)), mane, 0.0f, 0.15f);
            }
            else
            {
                // Crinet: overlapping neck cops climbing toward the poll.
                for (int i = 0; i < 3; i++)
                {
                    float t = i / 2f;
                    Part(PrimitiveType.Cube, $"NeckCop_{i + 1}", h,
                        Vector3.Lerp(new Vector3(0f, 1.18f, 0.48f), new Vector3(0f, 1.60f, 0.80f), t),
                        new Vector3(0.26f * b - i * 0.02f, 0.15f, 0.28f),
                        Quaternion.Euler(neckLean, 0f, Jitter(rng, 1.5f)), armor, 0.85f, 0.5f);
                }
            }

            // ── Tail on a sway pivot (animators wag "Tail"). ──
            var tail = new GameObject("Tail");
            tail.transform.SetParent(h, false);
            tail.transform.localPosition = new Vector3(0f, 1.22f, -0.72f);
            Part(PrimitiveType.Cylinder, "TailUpper", tail.transform,
                new Vector3(0f, -0.18f, -0.09f), new Vector3(0.055f, 0.20f, 0.055f),
                Quaternion.Euler(-26f + Jitter(rng, 4f), 0f, Jitter(rng, 2f)), mane, 0.0f, 0.15f);
            Part(PrimitiveType.Cylinder, "TailTip", tail.transform,
                new Vector3(0f, -0.47f, -0.17f), new Vector3(0.04f, 0.15f, 0.04f),
                Quaternion.Euler(-14f, 0f, Jitter(rng, 3f)), mane * 0.85f, 0.0f, 0.12f);

            // ── Legs: swing pivot at shoulder/hip, two segments + hoof cap. ──
            legFL = BuildLeg(h, rng, "LegFL", new Vector3(-0.20f * b, 1.00f, 0.44f), coat, hoof, armored);
            legFR = BuildLeg(h, rng, "LegFR", new Vector3(0.20f * b, 1.00f, 0.44f), coat, hoof, armored);
            legBL = BuildLeg(h, rng, "LegBL", new Vector3(-0.21f * b, 1.02f, -0.48f), coat, hoof, armored);
            legBR = BuildLeg(h, rng, "LegBR", new Vector3(0.21f * b, 1.02f, -0.48f), coat, hoof, armored);

            return horse;
        }

        private static Transform BuildLeg(Transform horse, System.Random rng, string pivotName,
            Vector3 pivotPos, Color coat, Color hoof, bool armored)
        {
            var pivot = new GameObject(pivotName);
            pivot.transform.SetParent(horse, false);
            pivot.transform.localPosition = pivotPos;
            pivot.transform.localRotation = Quaternion.Euler(Jitter(rng, 1.5f), 0f, Jitter(rng, 1f));
            var p = pivot.transform;

            Part(PrimitiveType.Cylinder, "UpperLeg", p,
                new Vector3(0f, -0.27f, 0f), new Vector3(0.095f, 0.26f, 0.095f),
                Quaternion.identity, coat * 0.95f, 0.02f, 0.25f);
            Part(PrimitiveType.Cylinder, "LowerLeg", p,
                new Vector3(0f, -0.70f, 0.02f), new Vector3(0.07f, 0.21f, 0.07f),
                Quaternion.Euler(Jitter(rng, 1.5f), 0f, 0f), coat * 0.86f, 0.02f, 0.22f);
            Part(PrimitiveType.Cube, "Hoof", p,
                new Vector3(0f, -0.955f, 0.03f), new Vector3(0.115f, 0.085f, 0.14f),
                Quaternion.identity, hoof,
                armored ? 0.6f : 0.05f, armored ? 0.45f : 0.30f);

            return p;
        }
    }
}
