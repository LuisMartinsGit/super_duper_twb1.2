using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages Feraldis faction territory on the Influence Map (B channel).
///
/// Uses a two-stage system:
///
///   Stage 1 — Blood Accumulation
///     When any non-crystal unit dies, call <see cref="SpillBlood(Vector3, float)"/>.
///     Each call blits a soft organic splat into BloodMap (R channel only).
///     Blood accumulates additively and persists indefinitely by default.
///
///   Stage 2 — Totem Claiming
///     When a Feraldis player places a Totem, call <see cref="AddTotem"/>.
///     Each totem triggers FeraldisClaimPaint.shader which reads BloodMap
///     and converts pixels above <see cref="bloodThreshold"/> within the
///     totem's claim radius into Feraldis territory (B channel of InfluenceMap).
///
/// Destroying a totem calls <see cref="RemoveTotem"/>, which marks it inactive
/// and schedules a rebake (territory within its radius will be cleared).
///
/// SETUP:
///   Attach to the same GameObject (or a child) as InfluenceManager.
///   Assign BloodSplatMat (BloodSplat.shader) and ClaimMat (FeraldisClaimPaint.shader).
/// </summary>
public class FeraldisInfluence : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------
    [Header("GPU Resources")]
    [Tooltip("Material using BloodSplat.shader — for blood accumulation.")]
    [SerializeField] private Material bloodSplatMat;

    [Tooltip("Material using FeraldisClaimPaint.shader — converts blood into territory.")]
    [SerializeField] private Material claimMat;

    [Header("Blood Settings")]
    [Tooltip("Minimum splat radius in world units for a small-unit death.")]
    [SerializeField] private float minSplatRadius = 3f;

    [Tooltip("Maximum splat radius in world units for a large-unit death.")]
    [SerializeField] private float maxSplatRadius = 12f;

    [Header("Totem Settings")]
    [Tooltip("Blood value above which a texel is considered claimed (0–1).")]
    [SerializeField] private float bloodThreshold = 0.1f;

    [Tooltip("If true, blood pixels are cleared from BloodMap after claiming. " +
             "False = blood stays (multiple overlapping totems keep claiming it).")]
    [SerializeField] private bool clearBloodAfterClaim = false;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private struct TotemData
    {
        public Vector3 position;
        public float   claimRadius;
        public bool    active;
    }

    private List<TotemData> _totems = new();
    private InfluenceManager _manager;

    // Scratch RT for building the B-channel composite
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
            name = "FeraldisClaimScratch"
        };
        _scratchRT.Create();
    }

    // -------------------------------------------------------------------------
    // Public API — Blood
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spill blood at a world position. Call this when any non-crystal unit dies.
    ///
    /// <paramref name="amount"/> is a 0–1 value controlling splat size:
    ///   0 = minimum radius (small unit), 1 = maximum radius (large unit/hero).
    ///
    /// This immediately blits into BloodMap — no rebake required.
    /// </summary>
    public void SpillBlood(Vector3 worldPos, float amount)
    {
        if (_manager == null || bloodSplatMat == null) return;

        amount = Mathf.Clamp01(amount);
        float radiusWorld = Mathf.Lerp(minSplatRadius, maxSplatRadius, amount);
        float radiusUV    = radiusWorld / _manager.TerrainSize.x;

        Vector2 uv = _manager.WorldToInfluenceUV(worldPos);

        // Random per-splat seed drives the organic offset inside BloodSplat.shader
        float seed = Random.value * 1000f;

        bloodSplatMat.SetVector("_Center", new Vector4(uv.x, uv.y, 0, 0));
        bloodSplatMat.SetFloat("_Radius",  radiusUV);
        bloodSplatMat.SetFloat("_Amount",  amount);
        bloodSplatMat.SetFloat("_Seed",    seed);

        // Additive blit: blood accumulates over time
        // BloodSplat.shader writes only to the destination's R channel
        Graphics.Blit(null, _manager.BloodMap, bloodSplatMat);
    }

    // -------------------------------------------------------------------------
    // Public API — Totems
    // -------------------------------------------------------------------------

    /// <summary>
    /// Register a new Totem and trigger an immediate claim bake.
    /// Returns an index you can pass to <see cref="RemoveTotem"/> later.
    /// </summary>
    public int AddTotem(Vector3 worldPos, float claimRadius)
    {
        _totems.Add(new TotemData
        {
            position    = worldPos,
            claimRadius = claimRadius,
            active      = true
        });
        _manager.RequestRebakeFeraldis();
        return _totems.Count - 1;
    }

    /// <summary>
    /// Deactivate a totem by index. Territory within its radius will be
    /// cleared on the next rebake (territory is re-derived from active totems only).
    /// </summary>
    public void RemoveTotem(int index)
    {
        if (index < 0 || index >= _totems.Count) return;
        var t = _totems[index];
        t.active = false;
        _totems[index] = t;
        _manager.RequestRebakeFeraldis();
    }

    // -------------------------------------------------------------------------
    // Rebake — called by InfluenceManager.LateUpdate()
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the B channel of InfluenceMap from scratch by re-evaluating
    /// all active totems against the current BloodMap.
    /// </summary>
    public void Rebake()
    {
        if (_manager == null || claimMat == null) return;

        // Clear scratch
        ClearRT(_scratchRT);

        // For each active totem, run FeraldisClaimPaint.shader which:
        //   - samples BloodMap
        //   - fills pixels within claim radius where blood >= threshold
        //   - writes result to scratch
        foreach (var totem in _totems)
        {
            if (!totem.active) continue;
            PaintTotemClaim(totem.position, totem.claimRadius);
        }

        // Composite scratch → B channel of InfluenceMap
        claimMat.SetVector("_ChannelMask", new Vector4(0, 0, 1, 0)); // write B only
        claimMat.SetTexture("_MainTex", _scratchRT);
        Graphics.Blit(_scratchRT, _manager.InfluenceMap, claimMat, 1); // pass 1 = Composite
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void PaintTotemClaim(Vector3 worldPos, float claimRadiusWorld)
    {
        Vector2 uvCenter   = _manager.WorldToInfluenceUV(worldPos);
        float   uvRadius   = claimRadiusWorld / _manager.TerrainSize.x;

        // Pass 0 of FeraldisClaimPaint.shader:
        //   Reads _BloodMap, checks each texel:
        //     - if distance to _Center <= _ClaimRadius AND blood >= _Threshold → output 1
        claimMat.SetVector("_Center",      new Vector4(uvCenter.x, uvCenter.y, 0, 0));
        claimMat.SetFloat("_ClaimRadius",  uvRadius);
        claimMat.SetFloat("_Threshold",    bloodThreshold);
        claimMat.SetTexture("_BloodMap",   _manager.BloodMap);
        claimMat.SetInt("_ClearBlood",     clearBloodAfterClaim ? 1 : 0);

        // Additive blit into scratch so overlapping totems combine
        Graphics.Blit(null, _scratchRT, claimMat, 0); // pass 0 = Claim
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
