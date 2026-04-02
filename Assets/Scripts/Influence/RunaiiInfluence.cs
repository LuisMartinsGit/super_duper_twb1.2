using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages Runaii faction territory on the Influence Map (G channel).
///
/// Territory is defined by active trade lanes — each lane is a world-space line
/// segment painted as a "capsule" (rounded pill shape) of configurable width.
///
/// Whenever lanes change, the G channel is fully cleared and all active lanes
/// are re-blitted. This is cheaper than tracking deltas and correct for any
/// lane count a typical RTS session produces.
///
/// Call <see cref="SetLanes"/> from trade-route game logic whenever a lane is
/// established or broken.
///
/// SETUP:
///   Attach to the same GameObject (or a child) as InfluenceManager.
///   Assign CapsuleBlitMat (material using CapsulePaint.shader) in the inspector.
/// </summary>
public class RunaiiInfluence : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------
    [Header("GPU Resources")]
    [Tooltip("Material using CapsulePaint.shader.")]
    [SerializeField] private Material capsuleBlitMat;

    [Header("Defaults")]
    [Tooltip("Half-width of each trade lane in world units.")]
    [SerializeField] private float defaultLaneWidth = 4f;

    [Tooltip("Edge softness in UV space. Higher = softer lane edges.")]
    [SerializeField] private float edgeSoftness = 0.005f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    // Each lane: from/to world positions + half-width override (<=0 uses default).
    private List<(Vector3 from, Vector3 to, float width)> _activeLanes = new();

    private InfluenceManager _manager;

    // Scratch RT for compositing into the G channel without touching R/B/A
    private RenderTexture _scratchRT;

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    /// <summary>Called by InfluenceManager.Awake().</summary>
    public void Init(InfluenceManager manager)
    {
        _manager = manager;

        _scratchRT = new RenderTexture(
            _manager.InfluenceMap.width,
            _manager.InfluenceMap.height, 0,
            RenderTextureFormat.RFloat)
        {
            name = "RunaiiScratch"
        };
        _scratchRT.Create();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Replace the active lane list and schedule a rebake.
    ///
    /// Example usage from trade-route logic:
    /// <code>
    ///   RunaiiInfluence.Instance.SetLanes(new List&lt;(Vector3,Vector3,float)&gt;
    ///   {
    ///       (marketplaceA.position, marketplaceB.position, 0)  // 0 = use default width
    ///   });
    /// </code>
    /// </summary>
    public void SetLanes(List<(Vector3 from, Vector3 to, float width)> lanes)
    {
        _activeLanes = lanes ?? new List<(Vector3, Vector3, float)>();
        _manager.RequestRebakeRunaii();
    }

    /// <summary>
    /// Convenience: add a single lane and schedule a rebake.
    /// </summary>
    public void AddLane(Vector3 from, Vector3 to, float width = 0f)
    {
        _activeLanes.Add((from, to, width));
        _manager.RequestRebakeRunaii();
    }

    /// <summary>
    /// Convenience: remove a lane by matching endpoints (within tolerance) and rebake.
    /// </summary>
    public void RemoveLane(Vector3 from, Vector3 to, float tolerance = 0.5f)
    {
        _activeLanes.RemoveAll(l =>
            Vector3.Distance(l.from, from) < tolerance &&
            Vector3.Distance(l.to,   to)   < tolerance);
        _manager.RequestRebakeRunaii();
    }

    // -------------------------------------------------------------------------
    // Rebake — called by InfluenceManager.LateUpdate()
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears the scratch RT, blits every active lane into it as a capsule,
    /// then composites the result into the G channel of InfluenceMap.
    /// </summary>
    public void Rebake()
    {
        if (_manager == null || capsuleBlitMat == null) return;

        // Step 1: Clear scratch to black (erase previous frame's territory)
        ClearRT(_scratchRT);

        // Step 2: Additive-blit each active lane as a capsule shape
        foreach (var (from, to, rawWidth) in _activeLanes)
        {
            float halfWidth = rawWidth > 0f ? rawWidth : defaultLaneWidth;
            PaintCapsule(from, to, halfWidth);
        }

        // Step 3: Composite scratch into the G channel of InfluenceMap
        // (leaves R, B, A untouched)
        capsuleBlitMat.SetVector("_ChannelMask", new Vector4(0, 1, 0, 0)); // G only
        capsuleBlitMat.SetTexture("_MainTex", _scratchRT);
        Graphics.Blit(_scratchRT, _manager.InfluenceMap, capsuleBlitMat, 1); // pass 1 = Composite
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Blits a single capsule (rounded pill) into _scratchRT.
    /// The CapsulePaint.shader computes an SDF for the segment [_PointA, _PointB]
    /// expanded by _Radius, producing a smooth filled shape.
    /// </summary>
    private void PaintCapsule(Vector3 worldFrom, Vector3 worldTo, float halfWidthWorld)
    {
        Vector2 uvFrom = _manager.WorldToInfluenceUV(worldFrom);
        Vector2 uvTo   = _manager.WorldToInfluenceUV(worldTo);

        // Convert world half-width to UV space (using X dimension as reference)
        float uvRadius = halfWidthWorld / _manager.TerrainSize.x;

        capsuleBlitMat.SetVector("_PointA",   new Vector4(uvFrom.x, uvFrom.y, 0, 0));
        capsuleBlitMat.SetVector("_PointB",   new Vector4(uvTo.x,   uvTo.y,   0, 0));
        capsuleBlitMat.SetFloat("_Radius",    uvRadius);
        capsuleBlitMat.SetFloat("_Softness",  edgeSoftness);
        capsuleBlitMat.SetVector("_ChannelMask", new Vector4(1, 0, 0, 0)); // write R of scratch

        // Pass 0 = Capsule paint (writes white inside capsule shape to scratch R)
        Graphics.Blit(null, _scratchRT, capsuleBlitMat, 0);
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
