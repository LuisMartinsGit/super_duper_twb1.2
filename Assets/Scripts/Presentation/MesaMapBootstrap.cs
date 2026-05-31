// MesaMapBootstrap.cs
// Editor-time map generator: spawns two mesa cliffs as children of this
// component. Each mesa is a closed SplineContainer + ProceduralCliffGenerator
// arranged so the cliff edge traces the mesa perimeter. Designed as a quick
// way to lay out test maps without hand-placing splines.
//
// Workflow:
//   1. Empty GameObject in the scene → Add Component → Mesa Map Bootstrap.
//   2. Tweak Mesa 1 / Mesa 2 (position, size, knot count, peanut pinch).
//   3. Cog → "Generate Mesas". Both mesas appear as children, baked.
//   4. Re-press Generate at any time to rebuild after changing inputs.
//
// Location: Assets/Scripts/Presentation/MesaMapBootstrap.cs

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace TheWaningBorder.Presentation
{
    [ExecuteAlways]
    public class MesaMapBootstrap : MonoBehaviour
    {
        [System.Serializable]
        public struct MesaConfig
        {
            [Tooltip("Name of the generated mesa GameObject (also used for the baked Mesh / Material asset filenames).")]
            public string Name;

            [Tooltip("World position of the mesa centre. The cliff base sits at roughly position.y.")]
            public Vector3 Position;

            [Tooltip("(x, y, z) = (horizontal X radius × 2, visible cliff height, horizontal Z radius × 2). The cliff cross-section's Y values scale by Height / 4.")]
            public Vector3 Size;

            [Tooltip("Number of knots in the closed perimeter spline. More knots = smoother outline. 8–14 is a good range.")]
            [Range(4, 32)] public int KnotCount;

            [Tooltip("0 = pure ellipse. >0 = peanut/dumbbell pinch (the waist along the Z axis pulls inward).")]
            [Range(0f, 0.7f)] public float PeanutPinch;

            [Tooltip("Per-mesa seed for noise / ledge placement so the two mesas don't share identical patterns.")]
            public int Seed;
        }

        [Header("Mesa 1")]
        public MesaConfig Mesa1 = new MesaConfig
        {
            Name        = "Mesa_North",
            Position    = new Vector3(0f, 0f, 0f),
            Size        = new Vector3(22f, 8f, 20f),
            KnotCount   = 10,
            PeanutPinch = 0f,
            Seed        = 1,
        };

        [Header("Mesa 2")]
        public MesaConfig Mesa2 = new MesaConfig
        {
            Name        = "Mesa_South",
            Position    = new Vector3(55f, 0f, 30f),
            Size        = new Vector3(34f, 6f, 18f),
            KnotCount   = 14,
            PeanutPinch = 0.35f,
            Seed        = 7,
        };

        // ────────────────────────────────────────────────────────────────────

        [ContextMenu("Generate Mesas")]
        public void Generate()
        {
            ClearChildren();
            CreateMesa(Mesa1);
            CreateMesa(Mesa2);
        }

        [ContextMenu("Clear Mesas")]
        public void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        void CreateMesa(MesaConfig config)
        {
            string goName = string.IsNullOrEmpty(config.Name) ? "Mesa" : config.Name;
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = config.Position;

            // Build the closed perimeter spline first so the cliff generator's
            // OnEnable sees populated knots when it eventually rebakes.
            var container = go.AddComponent<SplineContainer>();
            var spline    = container.Spline;
            spline.Closed = true;

            float halfX = Mathf.Max(0.1f, config.Size.x * 0.5f);
            float halfZ = Mathf.Max(0.1f, config.Size.z * 0.5f);
            int knots   = Mathf.Max(4, config.KnotCount);

            for (int i = 0; i < knots; i++)
            {
                float angle = (i / (float)knots) * Mathf.PI * 2f;
                float ca = Mathf.Cos(angle);
                float sa = Mathf.Sin(angle);
                // Peanut pinch: pull the radius inward where sin²(angle) is
                // large — that's the Z extremes, giving a dumbbell shape
                // elongated along X.
                float r = 1f - config.PeanutPinch * (sa * sa);
                float x = ca * halfX * r;
                float z = sa * halfZ * r;
                spline.Add(new BezierKnot(new float3(x, 0f, z)), TangentMode.AutoSmooth);
            }

            // Add the cliff generator and scale the cross-section to match the
            // requested visible cliff height. Stock profile has the top shelf
            // at Y=4 in local space; multiplying all Y values by (Height / 4)
            // places the shelf at Y = Height relative to the mesa origin.
            var cliff = go.AddComponent<ProceduralCliffGenerator>();
            cliff.Spline    = container;
            cliff.Seed      = config.Seed;
            cliff.LedgeSeed = config.Seed * 13 + 1;

            float scaleY = config.Size.y / 4f;
            for (int j = 0; j < cliff.Levels.Length; j++)
            {
                var lvl = cliff.Levels[j];
                lvl.Y *= scaleY;
                cliff.Levels[j] = lvl;
            }

            cliff.Bake();
        }

#if UNITY_EDITOR
        // Wire-frame footprint of each mesa drawn in the scene view so the user
        // can see where the cliffs will land before pressing Generate.
        void OnDrawGizmos()
        {
            DrawMesaGizmo(Mesa1, new Color(0.55f, 0.85f, 0.55f, 0.7f));
            DrawMesaGizmo(Mesa2, new Color(0.85f, 0.65f, 0.45f, 0.7f));
        }

        void DrawMesaGizmo(MesaConfig config, Color color)
        {
            Gizmos.color = color;
            int seg = Mathf.Max(32, config.KnotCount * 4);
            float halfX = config.Size.x * 0.5f;
            float halfZ = config.Size.z * 0.5f;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float r = 1f - config.PeanutPinch * (sa * sa);
                Vector3 pt = config.Position + new Vector3(ca * halfX * r, 0f, sa * halfZ * r);
                if (i > 0) Gizmos.DrawLine(prev, pt);
                prev = pt;
            }
        }
#endif
    }
}
