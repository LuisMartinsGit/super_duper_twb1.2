// TWBTerrainOverlays.hlsl
// SC2-creep-style dynamic ground overlays for the TWB terrain shader.
//
// A small world-space coverage mask (128², built each frame from
// PlayerInfluenceMap / BloodMap by InfluenceMaskTexture, eased on the CPU)
// is sampled per-pixel by world XZ and blended over the splat result:
//
//   _TWB_CultureMask  R = Alanthor   → white slate bricks (textured)
//                     G = Feraldis   → placeholder tint (not in demo)
//                     B = Runai      → placeholder tint (not in demo)
//                     A = Curse      → "crystallized grass": the ground's own
//                                      albedo hue-shifted to veilstone purple
//                                      with sharp sparkle glints
//   _TWB_BloodMask    R = Blood     → spatters at the rim, puddles at the
//                                      core (coverage eroded by high-freq
//                                      noise), wet smoothness
//
// Boundaries erode through value noise (the SC2 trick): as coverage rises,
// the edge advances through the noise field, so fronts are organic and
// perfectly continuous — no splatmap writes, no CPU painting, no ticks.

#ifndef TWB_TERRAIN_OVERLAYS_INCLUDED
#define TWB_TERRAIN_OVERLAYS_INCLUDED

TEXTURE2D(_TWB_CultureMask);    SAMPLER(sampler_TWB_CultureMask);
TEXTURE2D(_TWB_BloodMask);      SAMPLER(sampler_TWB_BloodMask);
float4 _TWB_MaskST;             // xy: 1/worldSize, zw: -worldMin/worldSize
float  _TWB_OverlaysEnabled;    // set to 1 by InfluenceMaskTexture

TEXTURE2D(_AlanthorAlbedo);     SAMPLER(sampler_AlanthorAlbedo);
TEXTURE2D(_AlanthorNormal);
float  _AlanthorTiling;
float  _AlanthorSmoothness;

// Alanthor cliffs → masonry terraces (slope-aware, triplanar, banded).
TEXTURE2D(_TerraceAlbedo);
half4  _TerraceTint;
float  _TerraceTiling;
float  _TerraceCourseHeight;
float  _TerraceSlopeStart;      // 1 − normal.y where masonry starts
float  _TerraceSlopeFull;       // fully masonry at/above this steepness
half4  _FeraldisTint;
half4  _RunaiTint;

TEXTURE2D(_BloodAlbedo);        SAMPLER(sampler_BloodAlbedo);
half4  _BloodTint;
float  _BloodTiling;
float  _BloodSmoothness;
float  _BloodNoiseScale;

TEXTURE2D(_CurseAlbedo);        SAMPLER(sampler_CurseAlbedo);
TEXTURE2D(_CurseNormal);
float  _CurseTiling;
half4  _CurseTint;              // purple pole
half4  _CurseTint2;             // greenish pole
float  _CurseSmoothness;
float  _CurseSparkleScale;

float  _OverlayNoiseScale;      // edge-erosion noise frequency (cycles/m)

// ── Cheap hash value noise (no texture needed) ─────────────────────────
// NOTE: the classic frac(sin(dot)) hash DEGENERATES at world-scale
// coordinates (fp32 sin precision collapses for large inputs), turning the
// noise into a regular grid of constant cells — which rendered as gray
// checkered tiles across the overlays. This sin-free hash (Dave Hoskins'
// hash12) stays uniform at any world position.
float TWB_Hash(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float TWB_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = TWB_Hash(i);
    float b = TWB_Hash(i + float2(1, 0));
    float c = TWB_Hash(i + float2(0, 1));
    float d = TWB_Hash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Organic front: the boundary erodes through two octaves of noise as
// coverage rises. coverage 0 → 0 everywhere, 1 → 1 everywhere; in between
// the edge is fingered, and because coverage is eased on the CPU the front
// creeps continuously.
half TWB_Edge(half coverage, float2 wpos, half feather)
{
    float n = TWB_ValueNoise(wpos * _OverlayNoiseScale) * 0.65
            + TWB_ValueNoise(wpos * _OverlayNoiseScale * 3.7) * 0.35;
    // Keep the threshold band strictly inside (0, 1): n - feather must stay
    // positive so ZERO coverage is exactly zero everywhere (no ghost
    // patches where the noise dips), and coverage is over-driven slightly
    // so FULL coverage saturates past n + feather.
    n = lerp(0.25, 0.85, n);
    return smoothstep(n - feather, n + feather, coverage * 1.15h);
}

void ApplyTWBGroundOverlays(float3 positionWS, half3 geoNormalWS,
    inout half3 albedo, inout half3 normalTS, inout half smoothness,
    inout half metallic, inout half occlusion)
{
    if (_TWB_OverlaysEnabled < 0.5)
        return;

    float2 maskUV = positionWS.xz * _TWB_MaskST.xy + _TWB_MaskST.zw;
    half4 m = SAMPLE_TEXTURE2D(_TWB_CultureMask, sampler_TWB_CultureMask, maskUV);
    half blood = SAMPLE_TEXTURE2D(_TWB_BloodMask, sampler_TWB_BloodMask, maskUV).r;

    // ── Alanthor: slate paving on flats, masonry terraces on cliffs ────
    half aBlend = TWB_Edge(m.r, positionWS.xz, 0.18h);
    if (aBlend > 0.003h)
    {
        // Steepness of the GEOMETRIC surface (1 − up): flats stay paving,
        // cliff faces inside Alanthor territory become dressed stone.
        half steep = (half)smoothstep(_TerraceSlopeStart, _TerraceSlopeFull,
            1.0 - saturate(geoNormalWS.y));

        float2 aUV = positionWS.xz / max(_AlanthorTiling, 0.01);
        half3 ground = SAMPLE_TEXTURE2D(_AlanthorAlbedo, sampler_AlanthorAlbedo, aUV).rgb;

        if (steep > 0.003h)
        {
            // Triplanar masonry — terrain UVs smear on cliffs, so project
            // from the sides in world space, weighted by facing axis.
            float tile = max(_TerraceTiling, 0.01);
            half3 wallX = SAMPLE_TEXTURE2D(_TerraceAlbedo, sampler_AlanthorAlbedo, positionWS.zy / tile).rgb;
            half3 wallZ = SAMPLE_TEXTURE2D(_TerraceAlbedo, sampler_AlanthorAlbedo, positionWS.xy / tile).rgb;
            half2 axisW = abs(geoNormalWS.xz);
            axisW /= max(axisW.x + axisW.y, 1e-4h);
            half3 wall = (wallX * axisW.x + wallZ * axisW.y) * _TerraceTint.rgb;

            // Stacked-course banding by world height: recessed mortar lines
            // between courses plus a hashed brightness step per course —
            // this is what makes a cliff read as BUILT terraces/ramparts
            // rather than merely stone-textured rock.
            float courseH = max(_TerraceCourseHeight, 0.05);
            float course = frac(positionWS.y / courseH);
            half mortar = (half)(smoothstep(0.0, 0.10, course) * smoothstep(1.0, 0.90, course));
            half courseStep = lerp(0.90h, 1.10h,
                (half)TWB_Hash(float2(floor(positionWS.y / courseH), 7.31)));
            wall *= lerp(0.55h, 1.0h, mortar) * courseStep;

            ground = lerp(ground, wall, steep);
        }

        albedo = lerp(albedo, ground, aBlend);
        smoothness = lerp(smoothness, _AlanthorSmoothness * (1.0h - 0.5h * steep), aBlend);
        #if defined(_NORMALMAP)
            half3 aNrm = UnpackNormal(SAMPLE_TEXTURE2D(_AlanthorNormal, sampler_AlanthorAlbedo, aUV));
            // On steep faces the course banding carries the detail; the
            // paving normal (projected top-down) would just smear there.
            aNrm = lerp(aNrm, half3(0.0h, 0.0h, 1.0h), steep);
            normalTS = normalize(lerp(normalTS, aNrm, aBlend));
        #endif
    }

    // ── Feraldis / Runai: placeholder tints (not shipping in demo) ─────
    half fBlend = TWB_Edge(m.g, positionWS.xz, 0.18h);
    albedo = lerp(albedo, albedo * _FeraldisTint.rgb, fBlend);
    half rBlend = TWB_Edge(m.b, positionWS.xz, 0.18h);
    albedo = lerp(albedo, albedo * _RunaiTint.rgb, rBlend);

    // ── Blood: spatters at the rim, merged puddles at the core ─────────
    // High-frequency noise erodes the coverage so low blood reads as
    // scattered droplets and heavy blood pools into connected puddles.
    if (blood > 0.003h)
    {
        float spatN = TWB_ValueNoise(positionWS.xz * _BloodNoiseScale) * 0.7
                    + TWB_ValueNoise(positionWS.xz * _BloodNoiseScale * 3.1) * 0.3;
        half spat = smoothstep(spatN - 0.08, spatN + 0.08, blood * 1.25h);
        if (spat > 0.003h)
        {
            float2 bUV = positionWS.xz / max(_BloodTiling, 0.01);
            half3 bAlb = SAMPLE_TEXTURE2D(_BloodAlbedo, sampler_BloodAlbedo, bUV).rgb * _BloodTint.rgb;
            albedo = lerp(albedo, bAlb, spat);
            smoothness = lerp(smoothness, _BloodSmoothness, spat); // wet sheen
        }
    }

    // ── Curse: crystallized rock (always on top) ───────────────────────
    // The authored ground is fully REPLACED by the rocky substance
    // textures, tinted between the purple and greenish crystal poles by
    // slow noise, with sparse sharp glints and a glassy smoothness.
    half cBlend = TWB_Edge(m.a, positionWS.xz, 0.22h);
    if (cBlend > 0.003h)
    {
        float2 cUV = positionWS.xz / max(_CurseTiling, 0.01);
        half3 rock = SAMPLE_TEXTURE2D(_CurseAlbedo, sampler_CurseAlbedo, cUV).rgb;
        float tone = TWB_ValueNoise(positionWS.xz * _OverlayNoiseScale * 0.6);
        half3 tint = lerp(_CurseTint.rgb, _CurseTint2.rgb, (half)tone);
        half3 cursed = rock * tint * 1.5h;
        float glint = TWB_ValueNoise(positionWS.xz * _CurseSparkleScale);
        glint = pow(saturate(glint), 12.0) * 3.0;
        cursed += (half)glint * tint;
        albedo = lerp(albedo, cursed, cBlend);
        smoothness = lerp(smoothness, _CurseSmoothness, cBlend);
        metallic = lerp(metallic, 0.25h, cBlend);
        #if defined(_NORMALMAP)
            half3 cNrm = UnpackNormal(SAMPLE_TEXTURE2D(_CurseNormal, sampler_CurseAlbedo, cUV));
            normalTS = normalize(lerp(normalTS, cNrm, cBlend));
        #endif
    }
}

#endif // TWB_TERRAIN_OVERLAYS_INCLUDED
