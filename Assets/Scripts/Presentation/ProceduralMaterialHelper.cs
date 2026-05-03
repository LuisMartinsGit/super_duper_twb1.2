// ProceduralMaterialHelper.cs
// Shared material + MaterialPropertyBlock utility for procedural generation.
// Location: Assets/Scripts/Presentation/ProceduralMaterialHelper.cs
//
// Fix #203: every procedural primitive used to create `new Material(shader)`
// individually. A single forest with 30 trees created 60+ material instances.
// A full map could create thousands of material instances, causing massive GPU
// draw call overhead and memory pressure.
//
// This utility caches the shader + two shared base materials (standard and
// emissive) and provides methods that set per-renderer properties via
// MaterialPropertyBlock instead of creating unique material instances. MPB
// lets each renderer carry its own color / metallic / smoothness without
// allocating a new Material, which means the GPU can batch all primitives
// that share the same base material into fewer draw calls.
//
// Usage:
//   var r = go.GetComponent<Renderer>();
//   ProceduralMaterialHelper.SetColor(r, Color.green);
//   ProceduralMaterialHelper.SetProperties(r, Color.green, metallic: 0.3f, smoothness: 0.5f);
//   ProceduralMaterialHelper.SetEmissive(r, Color.blue, Color.cyan, intensity: 2f);

using UnityEngine;
using UnityEngine.Rendering;

public static class ProceduralMaterialHelper
{
    // ── Cached shader ──
    private static Shader _litShader;
    public static Shader LitShader
    {
        get
        {
            if (_litShader == null)
            {
                _litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (_litShader == null) _litShader = Shader.Find("Standard");
            }
            return _litShader;
        }
    }

    // ── Shared base materials (created once, reused by every renderer) ──
    private static Material _baseLit;
    private static Material _baseLitEmissive;

    /// <summary>Standard Lit base material (no emission). Shared across all
    /// non-emissive procedural renderers.</summary>
    public static Material BaseLit
    {
        get
        {
            if (_baseLit == null)
            {
                _baseLit = new Material(LitShader);
                _baseLit.name = "ProceduralLit_Shared";
            }
            return _baseLit;
        }
    }

    /// <summary>Lit base material with _EMISSION keyword enabled. Shared across
    /// all emissive procedural renderers (crystal units, glowing effects).</summary>
    public static Material BaseLitEmissive
    {
        get
        {
            if (_baseLitEmissive == null)
            {
                _baseLitEmissive = new Material(LitShader);
                _baseLitEmissive.name = "ProceduralLitEmissive_Shared";
                _baseLitEmissive.EnableKeyword("_EMISSION");
            }
            return _baseLitEmissive;
        }
    }

    // ── Reusable MaterialPropertyBlock (not thread-safe — main thread only) ──
    private static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

    // ── Shader property IDs (cached for speed) ──
    private static readonly int _BaseColorId    = Shader.PropertyToID("_BaseColor");
    private static readonly int _ColorId        = Shader.PropertyToID("_Color");
    private static readonly int _MetallicId     = Shader.PropertyToID("_Metallic");
    private static readonly int _SmoothnessId   = Shader.PropertyToID("_Smoothness");
    private static readonly int _EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Assign the shared base Lit material and set the per-renderer color via MPB.
    /// Cheapest call — one material for all callers, one MPB set per renderer.
    /// </summary>
    public static void SetColor(Renderer r, Color color)
    {
        if (r == null) return;
        r.sharedMaterial = BaseLit;
        _mpb.Clear();
        _mpb.SetColor(_BaseColorId, color);
        _mpb.SetColor(_ColorId, color);     // fallback for Standard shader
        r.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// Assign the shared base Lit material and set color + metallic + smoothness.
    /// </summary>
    public static void SetProperties(Renderer r, Color color,
        float metallic = 0f, float smoothness = 0.3f)
    {
        if (r == null) return;
        r.sharedMaterial = BaseLit;
        _mpb.Clear();
        _mpb.SetColor(_BaseColorId, color);
        _mpb.SetColor(_ColorId, color);
        _mpb.SetFloat(_MetallicId, metallic);
        _mpb.SetFloat(_SmoothnessId, smoothness);
        r.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// Assign the emissive base material and set color + emission.
    /// </summary>
    public static void SetEmissive(Renderer r, Color color,
        Color emissiveColor, float intensity = 2f)
    {
        if (r == null) return;
        r.sharedMaterial = BaseLitEmissive;
        _mpb.Clear();
        _mpb.SetColor(_BaseColorId, color);
        _mpb.SetColor(_ColorId, color);
        _mpb.SetColor(_EmissionColorId, emissiveColor * intensity);
        r.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// Assign the emissive base material and set color + metallic + smoothness + emission.
    /// </summary>
    public static void SetEmissiveWithProperties(Renderer r, Color color,
        Color emissiveColor, float intensity = 2f,
        float metallic = 0f, float smoothness = 0.3f)
    {
        if (r == null) return;
        r.sharedMaterial = BaseLitEmissive;
        _mpb.Clear();
        _mpb.SetColor(_BaseColorId, color);
        _mpb.SetColor(_ColorId, color);
        _mpb.SetFloat(_MetallicId, metallic);
        _mpb.SetFloat(_SmoothnessId, smoothness);
        _mpb.SetColor(_EmissionColorId, emissiveColor * intensity);
        r.SetPropertyBlock(_mpb);
    }
}
