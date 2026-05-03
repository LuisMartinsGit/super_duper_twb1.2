using UnityEngine;

/// <summary>
/// Central manager for the GPU-driven Terrain Influence System.
///
/// Owns two RenderTextures that are read by the terrain shader:
///   InfluenceMap  — four channels, one per faction/effect (R=Alanthor, G=Runaii, B=Feraldis, A=Crystal)
///   BloodMap      — single-channel accumulation buffer for Feraldis blood splats
///
/// All faction sub-components hold a reference to this manager and call
/// RequestRebake() when their data changes. The manager then dispatches
/// the appropriate channel-clear + repaint each LateUpdate.
///
/// SETUP:
///   1. Attach this component to a dedicated GameObject ("InfluenceManager").
///   2. Assign the Terrain whose material you want to drive.
///   3. Replace the terrain's material with a mat using TerrainInfluence.shader.
///   4. The faction sub-components (AlanthorInfluence etc.) will find this via Instance.
/// </summary>
public class InfluenceManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------
    public static InfluenceManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------
    [Header("Render Textures")]
    [Tooltip("Resolution of both RTs. 512 is a good default; use 1024 for large terrains.")]
    [SerializeField] private int rtResolution = 512;

    [Header("Terrain")]
    [Tooltip("The Unity Terrain this system should paint onto.")]
    [SerializeField] private Terrain targetTerrain;

    // -------------------------------------------------------------------------
    // Public properties — faction scripts read these
    // -------------------------------------------------------------------------

    /// <summary>
    /// ARGB32 RenderTexture read by the terrain shader.
    /// R = Alanthor, G = Runaii, B = Feraldis, A = Crystal Curse.
    /// </summary>
    public RenderTexture InfluenceMap { get; private set; }

    /// <summary>
    /// Single-channel RenderTexture used by Feraldis blood accumulation.
    /// Not directly sampled by terrain shader; FeraldisClaimPaint.shader
    /// reads it and writes results into InfluenceMap.B.
    /// </summary>
    public RenderTexture BloodMap { get; private set; }

    /// <summary>
    /// World-space XZ footprint of the terrain. Used to convert world
    /// positions to [0,1] UV space when writing influence splats.
    /// </summary>
    public Vector2 TerrainSize { get; private set; }

    /// <summary>
    /// World-space XZ origin of the terrain (bottom-left corner).
    /// </summary>
    public Vector2 TerrainOrigin { get; private set; }

    // -------------------------------------------------------------------------
    // Private — rebake flags, set by faction components
    // -------------------------------------------------------------------------
    private bool _rebakeAlanthor;
    private bool _rebakeRunaii;
    private bool _rebakeFeraldis;

    // References to faction sub-components (auto-found on Awake)
    private AlanthorInfluence  _alanthor;
    private RunaiiInfluence    _runaii;
    private FeraldisInfluence  _feraldis;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Enforce singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Grab faction sub-components (they live on the same GameObject or children)
        _alanthor = GetComponentInChildren<AlanthorInfluence>();
        _runaii   = GetComponentInChildren<RunaiiInfluence>();
        _feraldis = GetComponentInChildren<FeraldisInfluence>();

        // Create the two shared RenderTextures
        InfluenceMap = CreateRT(rtResolution, RenderTextureFormat.ARGB32, "InfluenceMap");
        BloodMap     = CreateRT(rtResolution, RenderTextureFormat.RFloat,  "BloodMap");

        // Cache terrain dimensions so faction scripts can do world→UV math
        if (targetTerrain != null)
        {
            Vector3 size = targetTerrain.terrainData.size;
            TerrainSize   = new Vector2(size.x, size.z);
            TerrainOrigin = new Vector2(targetTerrain.transform.position.x,
                                        targetTerrain.transform.position.z);

            // Push both RTs and terrain size into the terrain material
            Material mat = targetTerrain.materialTemplate;
            if (mat != null)
            {
                mat.SetTexture("_InfluenceMap", InfluenceMap);
                mat.SetTexture("_BloodMap",     BloodMap);
                mat.SetVector("_TerrainSize",   new Vector4(TerrainSize.x, TerrainSize.y, 0, 0));
            }
            // (Empty else branches removed — task-062 Q-54.)
        }

        // Tell each faction sub-component about us and trigger their first bake
        _alanthor?.Init(this);
        _runaii?.Init(this);
        _feraldis?.Init(this);
    }

    private void LateUpdate()
    {
        // Process any pending rebake requests accumulated this frame.
        // LateUpdate ensures all game-logic updates have run first.
        if (_rebakeAlanthor)
        {
            _alanthor?.Rebake();
            _rebakeAlanthor = false;
        }
        if (_rebakeRunaii)
        {
            _runaii?.Rebake();
            _rebakeRunaii = false;
        }
        if (_rebakeFeraldis)
        {
            _feraldis?.Rebake();
            _rebakeFeraldis = false;
        }
    }

    private void OnDestroy()
    {
        // Release GPU resources when the scene unloads
        InfluenceMap?.Release();
        BloodMap?.Release();
        if (Instance == this) Instance = null;
    }

    // -------------------------------------------------------------------------
    // Public API — called by faction components
    // -------------------------------------------------------------------------

    /// <summary>
    /// Schedule a repaint of the Alanthor channel next LateUpdate.
    /// Typically called when a wall or tower is placed/destroyed.
    /// </summary>
    public void RequestRebakeAlanthor()  => _rebakeAlanthor  = true;

    /// <summary>
    /// Schedule a repaint of the Runaii channel next LateUpdate.
    /// Typically called when a trade lane is added or removed.
    /// </summary>
    public void RequestRebakeRunaii()    => _rebakeRunaii    = true;

    /// <summary>
    /// Schedule a repaint of the Feraldis channel next LateUpdate.
    /// Typically called when a totem is placed/destroyed or a claim changes.
    /// (Blood accumulation happens immediately via SpillBlood, not via rebake.)
    /// </summary>
    public void RequestRebakeFeraldis() => _rebakeFeraldis  = true;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a world XZ position to [0,1] UV space on the influence map.
    /// </summary>
    public Vector2 WorldToInfluenceUV(Vector3 worldPos)
    {
        return new Vector2(
            (worldPos.x - TerrainOrigin.x) / TerrainSize.x,
            (worldPos.z - TerrainOrigin.y) / TerrainSize.y
        );
    }

    // Creates a RenderTexture with standard settings
    private static RenderTexture CreateRT(int res, RenderTextureFormat fmt, string name)
    {
        var rt = new RenderTexture(res, res, 0, fmt)
        {
            name            = name,
            wrapMode        = TextureWrapMode.Clamp,
            filterMode      = FilterMode.Bilinear,
            enableRandomWrite = true   // needed for compute shader UAV writes
        };
        rt.Create();

        // Clear to black so channels start at zero influence
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;

        return rt;
    }
}
