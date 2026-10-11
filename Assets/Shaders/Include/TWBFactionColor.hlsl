// TWBFactionColor.hlsl
// PLAYER COLOUR ON THE GPU (2026-10-10): the per-pixel recolour every
// building shader applies to its albedo texel. It replaces
// BuildingFactionColorMarker's CPU pass, which copied every 2048² atlas per
// faction (GetPixels32 + RGBToHSV on all ~4 M pixels) — the 1-3 s age-up
// freeze, once per player.
//
// The includer declares, inside its UnityPerMaterial CBUFFER:
//     float4 _TWB_FactionHSV;   // xyz = faction hue (0-1) / sat / value, w = mode
// Modes (BuildingFactionColorMarker.Mode*):
//     0  off — the texel is returned untouched
//     1  hue swap — atlas pixels in the marker hue band take the faction's
//        hue, a saturation weighted by their own, and their own value
//     2  mask — the texel is pulled toward the faction colour by
//        _TWB_FactionMask.r (a linear painted mask, same UVs as the albedo)
//
// The maths is the CPU path's, in the same space: Unity's Color.RGBToHSV ran
// on the sRGB bytes, so the texel goes to sRGB, is recoloured there, and comes
// back to linear.
//
// The tuning lives on BuildingFactionColorMarker and is pushed as globals
// (outside the CBUFFER, so the SRP batcher is unaffected):
//     _TWB_FactionHueBand = (hue min 0-1, hue max 0-1, min saturation, 0)
//     _TWB_FactionSwap    = (saturation multiplier, value multiplier, 0, 0)

#ifndef TWB_FACTION_COLOR_INCLUDED
#define TWB_FACTION_COLOR_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_TWB_FactionMask);
float4 _TWB_FactionHueBand;
float4 _TWB_FactionSwap;

// Unity's Color.RGBToHSV / HSVToRGB, in float (hue 0-1).
float3 TWB_RgbToHsv(float3 c)
{
    float mx = max(c.r, max(c.g, c.b));
    float mn = min(c.r, min(c.g, c.b));
    float d = mx - mn;
    float h = 0.0;
    if (d > 1e-6)
    {
        if (mx == c.r)      h = (c.g - c.b) / d;
        else if (mx == c.g) h = 2.0 + (c.b - c.r) / d;
        else                h = 4.0 + (c.r - c.g) / d;
        h /= 6.0;
        if (h < 0.0) h += 1.0;
    }
    float s = mx > 1e-6 ? d / mx : 0.0;
    return float3(h, s, mx);
}

float3 TWB_HsvToRgb(float3 c)
{
    float3 rgb = saturate(abs(frac(c.x + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return c.z * lerp(float3(1.0, 1.0, 1.0), rgb, c.y);
}

// The albedo texel (linear) with the player colour applied. `samp` is the
// albedo's sampler, reused for the mask so no sampler slot is spent.
half3 TWB_FactionRecolor(half3 texel, float2 uv, SAMPLER(samp))
{
    float mode = _TWB_FactionHSV.w;
    if (mode < 0.5) return texel;

    float3 p = LinearToSRGB(saturate((float3)texel));
    float3 fac = _TWB_FactionHSV.xyz;
    float3 outColor;

    if (mode < 1.5)
    {
        float3 hsv = TWB_RgbToHsv(p);
        if (hsv.x < _TWB_FactionHueBand.x || hsv.x > _TWB_FactionHueBand.y
            || hsv.y < _TWB_FactionHueBand.z)
            return texel;
        float s = fac.y * saturate(hsv.y / max(0.001, fac.y)) * _TWB_FactionSwap.x;
        outColor = TWB_HsvToRgb(float3(fac.x, s, hsv.z * _TWB_FactionSwap.y));
    }
    else
    {
        float m = SAMPLE_TEXTURE2D(_TWB_FactionMask, samp, uv).r;
        if (m <= 0.0) return texel;
        float v = max(p.r, max(p.g, p.b));
        float3 tinted = TWB_HsvToRgb(float3(fac.x, fac.y, v * fac.z));
        outColor = lerp(p, tinted, m);
    }
    return (half3)SRGBToLinear(outColor);
}

#endif // TWB_FACTION_COLOR_INCLUDED
