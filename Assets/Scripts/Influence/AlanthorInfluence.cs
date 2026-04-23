using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages Alanthor faction territory on the Influence Map (R channel).
///
/// Territory is the union of:
///   1. Filled polygon formed by connected wall nodes (painted via AlanthorPaint.compute)
///   2. Circles around each tower / inner-ring tower (painted via CapsulePaint.shader
///      with both endpoint parameters equal, which degenerates into a circle)
///
/// Call <see cref="SetWalls"/> and <see cref="SetTowers"/> from game code whenever
/// walls or towers are placed or destroyed. Both methods automatically request a rebake.
///
/// SETUP:
///   Attach to the same GameObject (or a child) as InfluenceManager.
///   Assign AlanthorPaintCS (AlanthorPaint.compute) and CapsuleBlitMat in the inspector.
/// </summary>
public class AlanthorInfluence : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------
    [Header("GPU Resources")]
    [Tooltip("The AlanthorPaint.compute asset.")]
    [SerializeField] private ComputeShader alanthorPaintCS;

    [Tooltip("Material using CapsulePaint.shader — used for both capsule segments and circle towers.")]
    [SerializeField] private Material capsuleBlitMat;

    [Header("Defaults")]
    [Tooltip("Radius of influence painted around each tower (world units).")]
    [SerializeField] private float towerRadius = 15f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    // A polygon is an ordered list of XZ wall-node positions (world space).
    // We support multiple disconnected enclosures (e.g., two separate walled cities).
    private List<List<Vector2>> _wallPolygons = new();

    // Each tower: world position + optional custom radius (if radius <= 0 use towerRadius).
    private List<(Vector3 position, float radius)> _towers = new();

    private InfluenceManager _manager;

    // Compute kernel index, cached after Init
    private int _kernelPointInPolygon = -1;

    // Intermediate RT used as the target for additive blits
    private RenderTexture _scratchRT;

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    /// <summary>Called by InfluenceManager.Awake().</summary>
    public void Init(InfluenceManager manager)
    {
        _manager = manager;

        // Cache compute kernel (defined as "CSMain" in AlanthorPaint.compute)
        if (alanthorPaintCS != null)
            _kernelPointInPolygon = alanthorPaintCS.FindKernel("CSMain");

        // Scratch RT is the same size as the InfluenceMap, single-channel float
        // We paint Alanthor data here, then blit into the R channel of InfluenceMap.
        _scratchRT = new RenderTexture(
            _manager.InfluenceMap.width,
            _manager.InfluenceMap.height, 0,
            RenderTextureFormat.RFloat)
        {
            name              = "AlanthorScratch",
            enableRandomWrite = true   // compute shader writes here
        };
        _scratchRT.Create();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Replace the current set of wall polygons and trigger a rebake.
    /// Each inner list is one connected enclosure (ordered wall nodes in world XZ).
    /// An open chain (start != end) is allowed — only tower circles will paint in that case.
    /// </summary>
    public void SetWalls(List<List<Vector2>> polygons)
    {
        _wallPolygons = polygons ?? new List<List<Vector2>>();
        _manager.RequestRebakeAlanthor();
    }

    /// <summary>
    /// Replace the tower list and trigger a rebake.
    /// Pass radius <= 0 to use the default inspector radius.
    /// </summary>
    public void SetTowers(List<(Vector3 position, float radius)> towers)
    {
        _towers = towers ?? new List<(Vector3, float)>();
        _manager.RequestRebakeAlanthor();
    }

    // -------------------------------------------------------------------------
    // Rebake — called by InfluenceManager.LateUpdate()
    // -------------------------------------------------------------------------

    /// <summary>
    /// Full repaint of the R channel. Clears scratch, runs polygon compute pass,
    /// then blits tower circles, then composites scratch into InfluenceMap.R.
    /// </summary>
    public void Rebake()
    {
        if (_manager == null) return;

        // 1. Clear scratch RT to black
        ClearRT(_scratchRT);

        // 2. Paint each wall polygon via compute shader
        foreach (var polygon in _wallPolygons)
        {
            if (polygon == null || polygon.Count < 3) continue;

            // Detect open chain: if first point != last point, don't fill polygon
            bool isClosed = Vector2.Distance(polygon[0], polygon[^1]) < 0.01f;
            if (isClosed)
                PaintPolygon(polygon);
        }

        // 3. Paint tower circles (capsule shader with equal endpoints = circle)
        foreach (var (pos, rawRadius) in _towers)
        {
            float r = rawRadius > 0f ? rawRadius : towerRadius;
            PaintCircle(pos, r);
        }

        // 4. Composite scratch into the R channel of InfluenceMap
        // We use a simple max-blend: result.R = max(existing.R, scratch.R)
        // This is done by blending with an additive step via Graphics.Blit and
        // a channel-write mask (handled inside CapsulePaint.shader's R-channel variant).
        // For now, we use a direct blit helper. See CompositeAlanthorChannel().
        CompositeAlanthorChannel();
    }

    // -------------------------------------------------------------------------
    // Private painting helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dispatches AlanthorPaint.compute to rasterize a filled polygon into _scratchRT.
    /// The compute shader runs a point-in-polygon test for each texel.
    /// </summary>
    private void PaintPolygon(List<Vector2> polygon)
    {
        if (_kernelPointInPolygon < 0 || alanthorPaintCS == null) return;

        int n = polygon.Count;

        // Upload polygon vertices as a float2 buffer
        // ComputeBuffer stride: 2 floats = 8 bytes
        var vertexBuffer = new ComputeBuffer(n, 8);
        var verts = new Vector2[n];
        for (int i = 0; i < n; i++) verts[i] = polygon[i];
        vertexBuffer.SetData(verts);

        alanthorPaintCS.SetBuffer(_kernelPointInPolygon, "_Polygon", vertexBuffer);
        alanthorPaintCS.SetInt("_VertexCount", n);
        alanthorPaintCS.SetVector("_TerrainOrigin", new Vector4(
            _manager.TerrainOrigin.x, _manager.TerrainOrigin.y, 0, 0));
        alanthorPaintCS.SetVector("_TerrainSize", new Vector4(
            _manager.TerrainSize.x, _manager.TerrainSize.y, 0, 0));
        alanthorPaintCS.SetInt("_TexSize", _scratchRT.width);
        alanthorPaintCS.SetTexture(_kernelPointInPolygon, "_Output", _scratchRT);

        // Each thread group covers 8×8 texels; dispatch enough groups to cover the RT
        int groups = Mathf.CeilToInt(_scratchRT.width / 8f);
        alanthorPaintCS.Dispatch(_kernelPointInPolygon, groups, groups, 1);

        vertexBuffer.Release();
    }

    /// <summary>
    /// Blits a filled circle into _scratchRT using CapsulePaint.shader.
    /// When _PointA == _PointB the capsule SDF degenerates into a circle SDF.
    /// </summary>
    private void PaintCircle(Vector3 worldPos, float radius)
    {
        if (capsuleBlitMat == null) return;

        Vector2 uv = _manager.WorldToInfluenceUV(worldPos);
        float   uvRadius = radius / _manager.TerrainSize.x; // approximate; good enough for circles

        capsuleBlitMat.SetVector("_PointA", new Vector4(uv.x, uv.y, 0, 0));
        capsuleBlitMat.SetVector("_PointB", new Vector4(uv.x, uv.y, 0, 0)); // same → circle
        capsuleBlitMat.SetFloat("_Radius",  uvRadius);
        capsuleBlitMat.SetFloat("_Softness", 0.005f);

        // Additive blit into scratch (we want circles to add on top of polygon)
        Graphics.Blit(null, _scratchRT, capsuleBlitMat);
    }

    /// <summary>
    /// Copies the scratch RT into the R channel of InfluenceMap without touching G/B/A.
    /// Uses CapsulePaint.shader's channel-write variant by passing a keyword,
    /// or — simpler — uses a manual Graphics.CopyTexture region. Here we use
    /// a dedicated material pass that writes only to R.
    /// </summary>
    private void CompositeAlanthorChannel()
    {
        if (capsuleBlitMat == null) return;

        // The CapsulePaint.shader exposes a "_ChannelMask" property to select
        // which channel to write. Setting it to (1,0,0,0) writes only R.
        capsuleBlitMat.SetVector("_ChannelMask", new Vector4(1, 0, 0, 0));
        capsuleBlitMat.SetTexture("_MainTex", _scratchRT);

        // We need to blit scratch into InfluenceMap preserving other channels.
        // CapsulePaint.shader's "Composite" pass samples _MainTex and writes
        // only the channel indicated by _ChannelMask.
        Graphics.Blit(_scratchRT, _manager.InfluenceMap, capsuleBlitMat, 1); // pass 1 = Composite
    }

    private static void ClearRT(RenderTexture rt)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;
    }

    private void OnDestroy()
    {
        _scratchRT?.Release();
    }
}
