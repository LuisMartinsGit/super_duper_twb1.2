// ProceduralCliffGenerator.cs
// Extrudes a chunky low-poly cliff along a Unity Splines SplineContainer and
// writes the result to a Mesh asset cached under Assets/GeneratedMeshes/Cliffs/.
// All settings (cross-section, noise, colors, ledges, material) live directly
// on this component so they're editable with the cliff GameObject selected.
// Top-shelf and wall colors are baked into vertex colors; the shader at
// Assets/Shaders/Cliff.shader (TheWaningBorder/Cliff) consumes them.
//
// Workflow:
//   1. Empty GameObject → Add Component → Spline Container (Unity Splines), place knots.
//   2. Add this component, hit "Bake Mesh" from the cog. Tweak fields live thereafter.
// Location: Assets/GameData/TechTree/Presentation/Procedural/ProceduralCliffGenerator.cs

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TheWaningBorder.Presentation
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public class ProceduralCliffGenerator : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────────
        //  Spline & live-preview
        // ────────────────────────────────────────────────────────────────────

        [Tooltip("Spline tracing the top edge of the cliff. Can live on this GameObject or any other.")]
        public SplineContainer Spline;

        [Tooltip("Random seed for deterministic per-vertex noise. Bump to reshuffle.")]
        public int Seed = 1;

        [Tooltip("If enabled, the mesh rebuilds automatically when the spline knots or any field below changes. Edits write into the saved Mesh asset in place. Editor-only.")]
        public bool LivePreview = true;

        // ────────────────────────────────────────────────────────────────────
        //  Cross-section
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One horizontal "level" of the cliff cross-section.
        /// Levels are walked top-down — index 0 is the highest point in the
        /// profile, the last index is the deepest. Bake stitches each level
        /// between consecutive spline samples to form the wall.
        /// </summary>
        [System.Serializable]
        public struct CliffLevel
        {
            [Tooltip("Label for this level (Inspector clarity only). e.g. 'Outer Top (Lip)', 'Wall Mid'.")]
            public string Name;

            [Tooltip("Lateral outset from the spline path (world units). Positive = outward from the cliff face, negative = inward into the mountain.")]
            public float X;

            [Tooltip("Vertical height in cliff-local space (world units). Higher Y = closer to the cliff top.")]
            public float Y;
        }

        [Header("Cross-section (cliff levels, top → bottom)")]
        [Tooltip("Each entry is one horizontal level of the cliff. Adjust X to push the lip out or pull the wall in; adjust Y to make the cliff taller or shorter at that level.")]
        public CliffLevel[] Levels = new[]
        {
            new CliffLevel { Name = "Inner Top",        X = -5.0f, Y =  4.0f }, // terrain rests flush over this
            new CliffLevel { Name = "Outer Top (Lip)", X =  2.5f, Y =  4.0f }, // overhanging cliff edge
            new CliffLevel { Name = "Under-Overhang",  X =  1.0f, Y =  2.5f }, // wall starts, recessed
            new CliffLevel { Name = "Wall Mid",         X =  0.7f, Y =  0.5f }, // mostly vertical
            new CliffLevel { Name = "Wall Base",        X =  0.5f, Y = -1.5f }, // meets ground level
            new CliffLevel { Name = "Hidden Base",      X = -1.0f, Y = -3.0f }, // sunk under terrain
        };

        [Tooltip("Spline samples per world unit of length. Higher = more rings, finer detail, heavier mesh.")]
        [Range(0.1f, 4f)] public float SamplesPerUnit = 0.7f;

        // ────────────────────────────────────────────────────────────────────
        //  Stylization
        // ────────────────────────────────────────────────────────────────────

        [Header("Stylization")]
        [Tooltip("Per-vertex displacement amplitude in world units. 0 disables noise. Lower preserves overhang silhouette.")]
        [Range(0f, 2f)] public float NoiseAmplitude = 0.3f;

        [Tooltip("Vertical jitter scale relative to NoiseAmplitude. 0 keeps profile heights crisp.")]
        [Range(0f, 1f)] public float VerticalJitter = 0.5f;

        // ────────────────────────────────────────────────────────────────────
        //  Colors
        // ────────────────────────────────────────────────────────────────────

        [Header("Colors")]
        [Tooltip("How many profile segments at the top form the flat shelf. Default 1: the first segment (inner-top → outer-top) is the shelf, everything below is wall.")]
        [Min(1)] public int TopShelfSegmentCount = 1;

        [Tooltip("Solid color for the top shelf segment(s).")]
        [ColorUsage(showAlpha: false)] public Color TopShelfColor = new Color(0.55f, 0.50f, 0.40f, 1f);

        [Tooltip("Vertical color ramp for the cliff wall. Sampled top→bottom (t=0 at the wall's top edge, t=1 at the base).")]
        public Gradient WallColor;

        // ────────────────────────────────────────────────────────────────────
        //  Random ledges
        // ────────────────────────────────────────────────────────────────────

        [Header("Random ledges")]
        [Tooltip("Ledges per world unit of spline length. 0 disables ledges. A ledge bulges one profile point outward over a short stretch of the spline, tinted with TopShelfColor so it reads as a flat outcropping.")]
        [Range(0f, 0.5f)] public float LedgeDensity = 0.05f;

        [Tooltip("Width of each ledge along the spline (world units).")]
        [Range(0.5f, 10f)] public float LedgeWidth = 3f;

        [Tooltip("Maximum outward distance the ledge profile point is pushed at the centre of a ledge (world units).")]
        [Range(0f, 5f)] public float LedgeOutset = 1.5f;

        [Tooltip("Index of the cliff level that bulges outward when a ledge fires. Default 3 = 'Wall Mid' in the stock setup. Match this to whichever level you want the outcropping to project from.")]
        [Min(0)] public int LedgeProfilePoint = 3;

        [Tooltip("Separate seed for ledge placement, so re-rolling the main Seed (vertex noise) doesn't reshuffle where the ledges sit.")]
        public int LedgeSeed = 7;

        // ────────────────────────────────────────────────────────────────────
        //  Material
        // ────────────────────────────────────────────────────────────────────

        [Header("Material (auto-created if empty)")]
        [Tooltip("Vertex-color-aware material. If empty, the generator creates one beside the mesh asset using the TheWaningBorder/Cliff shader.")]
        public Material Material;

        // ────────────────────────────────────────────────────────────────────
        //  Live preview state (editor-only)
        // ────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        [System.NonSerialized] bool _previewDirty;
        [System.NonSerialized] bool _splineSubscribed;
#endif

        // ────────────────────────────────────────────────────────────────────
        //  Bake (manual / context-menu entry point)
        // ────────────────────────────────────────────────────────────────────

        [ContextMenu("Bake Mesh")]
        public void Bake()
        {
            if (!ValidateInputs(verbose: true)) return;

            var mesh = BuildMesh();
            ApplyMesh(mesh);
#if UNITY_EDITOR
            SaveMeshAsset(mesh);
            EnsureMaterialAssigned();
            SceneView.RepaintAll();
#endif
        }

        bool ValidateInputs(bool verbose)
        {
            if (Spline == null || Spline.Spline == null)
            {
                if (verbose) Debug.LogWarning("[Cliff] No SplineContainer assigned.", this);
                return false;
            }
            if (Levels == null || Levels.Length < 2)
            {
                if (verbose) Debug.LogWarning("[Cliff] Profile needs at least 2 cliff levels.", this);
                return false;
            }
            if (TopShelfSegmentCount >= Levels.Length)
            {
                if (verbose) Debug.LogWarning("[Cliff] TopShelfSegmentCount must be smaller than the cliff level count (need at least one wall segment).", this);
                return false;
            }
            return true;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Defaults
        // ────────────────────────────────────────────────────────────────────

        void Reset()
        {
            // Field initializers cover everything except Gradient (a class — `new Gradient()`
            // produces an empty one with no color keys). Seed it explicitly here so a
            // newly-added component has a sensible default ramp.
            WallColor = DefaultWallGradient();
        }

        static Gradient DefaultWallGradient()
        {
            var g = new Gradient();
            g.colorKeys = new[]
            {
                new GradientColorKey(new Color(0.62f, 0.55f, 0.44f), 0.0f), // sandy top
                new GradientColorKey(new Color(0.45f, 0.38f, 0.30f), 0.5f), // mid brown
                new GradientColorKey(new Color(0.32f, 0.26f, 0.20f), 1.0f), // dark base
            };
            g.alphaKeys = new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            };
            return g;
        }

        Gradient EffectiveWallColor()
        {
            return (WallColor != null && WallColor.colorKeys != null && WallColor.colorKeys.Length > 0)
                ? WallColor : DefaultWallGradient();
        }

        // ────────────────────────────────────────────────────────────────────
        //  Live preview (editor-only)
        // ────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        void OnEnable()
        {
            SubscribeToSpline();
            // Repaint once so re-opening a scene shows the current shape even
            // if nothing has changed yet.
            if (LivePreview) ScheduleRebake();
        }

        void OnDisable()
        {
            UnsubscribeFromSpline();
            EditorApplication.delayCall -= RebakeIfDirty;
        }

        void OnValidate()
        {
            // Tracks the spline reference too — switching to a different
            // SplineContainer re-routes the change subscription.
            SubscribeToSpline();
            if (LivePreview) ScheduleRebake();
        }

        // UnityEngine.Splines.Spline.Changed is a static event covering ALL
        // splines. We filter to ours in the handler. Must be fully qualified
        // because this class has a field named Spline (of type SplineContainer)
        // that would otherwise shadow the static class.
        void SubscribeToSpline()
        {
            if (_splineSubscribed) return;
            UnityEngine.Splines.Spline.Changed += OnAnySplineChanged;
            _splineSubscribed = true;
        }

        void UnsubscribeFromSpline()
        {
            if (!_splineSubscribed) return;
            UnityEngine.Splines.Spline.Changed -= OnAnySplineChanged;
            _splineSubscribed = false;
        }

        void OnAnySplineChanged(Spline spline, int knotIndex, SplineModification modificationType)
        {
            if (!LivePreview || Spline == null || Spline.Spline != spline) return;
            ScheduleRebake();
        }

        void ScheduleRebake()
        {
            if (_previewDirty) return;
            _previewDirty = true;
            EditorApplication.delayCall += RebakeIfDirty;
        }

        void RebakeIfDirty()
        {
            if (this == null) return;
            if (!_previewDirty) return;
            _previewDirty = false;
            if (!LivePreview) return;
            if (!ValidateInputs(verbose: false)) return;

            var freshMesh = BuildMesh();
            WritePreviewMesh(freshMesh);
            EnsureMaterialAssigned();
            // Force the scene view to redraw immediately. Without this, vertex
            // / color changes only show up the next time the user moves the
            // mouse over the scene view.
            SceneView.RepaintAll();
        }

        void WritePreviewMesh(Mesh freshMesh)
        {
            var mf = GetComponent<MeshFilter>();
            var existing = mf.sharedMesh;

            if (existing != null && AssetDatabase.Contains(existing))
            {
                existing.Clear();
                existing.indexFormat = freshMesh.indexFormat;
                existing.vertices  = freshMesh.vertices;
                existing.normals   = freshMesh.normals;
                existing.uv        = freshMesh.uv;
                existing.colors    = freshMesh.colors;
                existing.triangles = freshMesh.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                DestroyImmediate(freshMesh);
            }
            else
            {
                if (existing != null && !AssetDatabase.Contains(existing))
                    DestroyImmediate(existing);
                mf.sharedMesh = freshMesh;
            }
        }
#endif

        // ────────────────────────────────────────────────────────────────────
        //  Mesh build
        // ────────────────────────────────────────────────────────────────────

        Mesh BuildMesh()
        {
            float length = Spline.CalculateLength();
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(length * SamplesPerUnit) + 1);
            int P = Levels.Length;
            int shelfSegs = Mathf.Clamp(TopShelfSegmentCount, 1, P - 2);
            int wallSegs  = (P - 1) - shelfSegs;

            var ringOrigins = new Vector3[sampleCount];
            var ringRights  = new Vector3[sampleCount];
            Vector3 localUp = transform.InverseTransformDirection(Vector3.up);

            for (int s = 0; s < sampleCount; s++)
            {
                float t = (sampleCount == 1) ? 0f : s / (float)(sampleCount - 1);
                Spline.Evaluate(t, out float3 wp, out float3 wt, out _);
                Vector3 worldTan = ((Vector3)wt).sqrMagnitude > 1e-8f ? ((Vector3)wt).normalized : Vector3.forward;
                Vector3 worldRight = Vector3.Cross(Vector3.up, worldTan);
                if (worldRight.sqrMagnitude < 1e-6f) worldRight = Vector3.right;
                worldRight.Normalize();

                ringOrigins[s] = transform.InverseTransformPoint(wp);
                ringRights[s]  = transform.InverseTransformDirection(worldRight);
            }

            // Pre-sample the wall gradient at every profile-point boundary.
            var pointColors = new Color[P];
            Color shelfCol = TopShelfColor;
            var grad = EffectiveWallColor();
            for (int p = 0; p < P; p++)
            {
                if (p <= shelfSegs)
                    pointColors[p] = (p == shelfSegs) ? grad.Evaluate(0f) : shelfCol;
                else
                    pointColors[p] = grad.Evaluate((p - shelfSegs) / (float)wallSegs);
            }

            // Ledge schedule.
            int ledgeCount = Mathf.Max(0, Mathf.RoundToInt(length * LedgeDensity));
            var ledgeCenters    = new float[ledgeCount];
            var ledgeHalfWidthT = new float[ledgeCount];
            float invLen = 1f / Mathf.Max(length, 0.01f);
            for (int i = 0; i < ledgeCount; i++)
            {
                float baseT = ledgeCount > 1 ? (i + 0.5f) / ledgeCount : 0.5f;
                float jitter = (LedgeHash(i, 0) - 0.5f) * (0.8f / Mathf.Max(ledgeCount, 1));
                ledgeCenters[i] = Mathf.Clamp01(baseT + jitter);
                float widthMul = 0.7f + LedgeHash(i, 1) * 0.6f;
                ledgeHalfWidthT[i] = (LedgeWidth * widthMul * 0.5f) * invLen;
            }

            var ringLedgeFactor = new float[sampleCount];
            for (int s = 0; s < sampleCount; s++)
            {
                float ringT = sampleCount > 1 ? s / (float)(sampleCount - 1) : 0f;
                float maxF = 0f;
                for (int i = 0; i < ledgeCount; i++)
                {
                    float halfW = ledgeHalfWidthT[i];
                    if (halfW <= 0f) continue;
                    float d = Mathf.Abs(ringT - ledgeCenters[i]);
                    if (d < halfW)
                    {
                        float f = 1f - d / halfW;
                        f = f * f * (3f - 2f * f);
                        if (f > maxF) maxF = f;
                    }
                }
                ringLedgeFactor[s] = maxF;
            }

            int ledgeProfileIdx = Mathf.Clamp(LedgeProfilePoint, 0, P - 1);
            float ledgeOutset   = LedgeOutset;

            int quadsPerRingPair = P - 1;
            int ringPairs = sampleCount - 1;
            int triCount  = ringPairs * quadsPerRingPair * 2;
            int vertCount = triCount * 3;

            var verts  = new Vector3[vertCount];
            var norms  = new Vector3[vertCount];
            var uvs    = new Vector2[vertCount];
            var cols   = new Color[vertCount];
            var tris   = new int[triCount * 3];

            float vAmp = NoiseAmplitude * VerticalJitter;

            int vi = 0, ti = 0;
            for (int s = 0; s < ringPairs; s++)
            {
                Vector3 o0 = ringOrigins[s],     r0 = ringRights[s];
                Vector3 o1 = ringOrigins[s + 1], r1 = ringRights[s + 1];

                float uA = s       / (float)ringPairs;
                float uB = (s + 1) / (float)ringPairs;

                for (int p = 0; p < P - 1; p++)
                {
                    Vector2 a2 = new Vector2(Levels[p].X,     Levels[p].Y);
                    Vector2 b2 = new Vector2(Levels[p + 1].X, Levels[p + 1].Y);

                    float outA = (p     == ledgeProfileIdx) ? ringLedgeFactor[s]     * ledgeOutset : 0f;
                    float outB = (p + 1 == ledgeProfileIdx) ? ringLedgeFactor[s]     * ledgeOutset : 0f;
                    float outC = (p + 1 == ledgeProfileIdx) ? ringLedgeFactor[s + 1] * ledgeOutset : 0f;
                    float outD = (p     == ledgeProfileIdx) ? ringLedgeFactor[s + 1] * ledgeOutset : 0f;

                    Vector3 a = o0 + r0 * (a2.x + outA) + localUp * a2.y;
                    Vector3 b = o0 + r0 * (b2.x + outB) + localUp * b2.y;
                    Vector3 c = o1 + r1 * (b2.x + outC) + localUp * b2.y;
                    Vector3 d = o1 + r1 * (a2.x + outD) + localUp * a2.y;

                    bool aShelf = (p     <= shelfSegs);
                    bool bShelf = (p + 1 <= shelfSegs);
                    bool cShelf = (p + 1 <= shelfSegs);
                    bool dShelf = (p     <= shelfSegs);

                    if (!aShelf) a += r0 * Noise(s,     p,     0) + localUp * Noise(s,     p,     1, vAmp);
                    if (!bShelf) b += r0 * Noise(s,     p + 1, 0) + localUp * Noise(s,     p + 1, 1, vAmp);
                    if (!cShelf) c += r1 * Noise(s + 1, p + 1, 0) + localUp * Noise(s + 1, p + 1, 1, vAmp);
                    if (!dShelf) d += r1 * Noise(s + 1, p,     0) + localUp * Noise(s + 1, p,     1, vAmp);

                    Color cA, cB, cC, cD;
                    if (p < shelfSegs)
                    {
                        cA = cB = cC = cD = shelfCol;
                    }
                    else
                    {
                        Color colTop = pointColors[p];
                        Color colBot = pointColors[p + 1];
                        cA = (p     == ledgeProfileIdx) ? Color.Lerp(colTop, shelfCol, ringLedgeFactor[s])     : colTop;
                        cD = (p     == ledgeProfileIdx) ? Color.Lerp(colTop, shelfCol, ringLedgeFactor[s + 1]) : colTop;
                        cB = (p + 1 == ledgeProfileIdx) ? Color.Lerp(colBot, shelfCol, ringLedgeFactor[s])     : colBot;
                        cC = (p + 1 == ledgeProfileIdx) ? Color.Lerp(colBot, shelfCol, ringLedgeFactor[s + 1]) : colBot;
                    }

                    float vA = p       / (float)quadsPerRingPair;
                    float vB = (p + 1) / (float)quadsPerRingPair;

                    Vector3 n1 = SafeNormal(a, b, c);
                    int t1 = vi;
                    verts[vi] = a; norms[vi] = n1; uvs[vi] = new Vector2(uA, vA); cols[vi++] = cA;
                    verts[vi] = b; norms[vi] = n1; uvs[vi] = new Vector2(uA, vB); cols[vi++] = cB;
                    verts[vi] = c; norms[vi] = n1; uvs[vi] = new Vector2(uB, vB); cols[vi++] = cC;
                    tris[ti++] = t1; tris[ti++] = t1 + 1; tris[ti++] = t1 + 2;

                    Vector3 n2 = SafeNormal(a, c, d);
                    int t2 = vi;
                    verts[vi] = a; norms[vi] = n2; uvs[vi] = new Vector2(uA, vA); cols[vi++] = cA;
                    verts[vi] = c; norms[vi] = n2; uvs[vi] = new Vector2(uB, vB); cols[vi++] = cC;
                    verts[vi] = d; norms[vi] = n2; uvs[vi] = new Vector2(uB, vA); cols[vi++] = cD;
                    tris[ti++] = t2; tris[ti++] = t2 + 1; tris[ti++] = t2 + 2;
                }
            }

            var mesh = new Mesh { name = $"Cliff_{gameObject.name}" };
            mesh.indexFormat = vertCount > 65530
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.uv        = uvs;
            mesh.colors    = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        float Noise(int s, int p, int channel) => Noise(s, p, channel, NoiseAmplitude);

        float Noise(int s, int p, int channel, float amp)
        {
            if (amp <= 0f) return 0f;
            unchecked
            {
                uint h = (uint)(s * 73856093) ^ (uint)(p * 19349663) ^ (uint)(channel * 83492791) ^ (uint)(Seed * 2654435761);
                h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
                float r = (h & 0xFFFF) / 65535f;
                return (r - 0.5f) * 2f * amp;
            }
        }

        float LedgeHash(int i, int channel)
        {
            unchecked
            {
                uint h = (uint)(i * 2654435761) ^ (uint)(channel * 374761393) ^ (uint)(LedgeSeed * 668265263);
                h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        static Vector3 SafeNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float m = n.magnitude;
            return m > 1e-8f ? n / m : Vector3.up;
        }

        void ApplyMesh(Mesh mesh)
        {
            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (Material != null) mr.sharedMaterial = Material;

            // Mesh collider — needed for mouse picking and for any physics-
            // based collision the user wires up. Cheap to keep in sync with
            // the visual mesh.
            var mc = GetComponent<MeshCollider>();
            if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            // task-112 M4: NavMeshStaticObstacle marker deleted with the
            // rest of the NavMesh stack. Cliffs no longer feed the pathing
            // system as a Mesh source -- the new cost field stamps
            // BuildingTag / ObstacleTag entities directly via
            // BuildingCostStampSystem, and cliffs that need to block
            // movement should carry an ObstacleTag instead.
        }

        // ────────────────────────────────────────────────────────────────────
        //  Editor asset I/O
        // ────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        void SaveMeshAsset(Mesh mesh)
        {
            const string root = "Assets/GeneratedMeshes";
            const string folder = root + "/Cliffs";
            if (!AssetDatabase.IsValidFolder(root))   AssetDatabase.CreateFolder("Assets", "GeneratedMeshes");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(root, "Cliffs");

            string path = $"{folder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                existing.indexFormat = mesh.indexFormat;
                existing.vertices  = mesh.vertices;
                existing.normals   = mesh.normals;
                existing.uv        = mesh.uv;
                existing.colors    = mesh.colors;
                existing.triangles = mesh.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                GetComponent<MeshFilter>().sharedMesh = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
                GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(this);
        }

        void EnsureMaterialAssigned()
        {
            if (Material != null)
            {
                GetComponent<MeshRenderer>().sharedMaterial = Material;
                return;
            }

            var shader = Shader.Find("TheWaningBorder/Cliff");
            if (shader == null)
            {
                Debug.LogWarning("[Cliff] Shader 'TheWaningBorder/Cliff' not found. Add Assets/Shaders/Cliff.shader or assign a vertex-color material on the Material field.", this);
                return;
            }

            const string root = "Assets/GeneratedMeshes/Cliffs";
            const string matFolder = root + "/Materials";
            if (!AssetDatabase.IsValidFolder(matFolder)) AssetDatabase.CreateFolder(root, "Materials");

            string matName = gameObject.name;
            string matPath = $"{matFolder}/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = matName };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }

            Material = mat;
            EditorUtility.SetDirty(this);
            GetComponent<MeshRenderer>().sharedMaterial = mat;
            AssetDatabase.SaveAssets();
        }
#endif
    }
}
