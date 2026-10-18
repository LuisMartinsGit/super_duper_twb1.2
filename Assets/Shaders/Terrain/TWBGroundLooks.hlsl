// TWBGroundLooks.hlsl
// The BLOOD and CURSE ground looks, shared (2026-10-16) between the terrain
// (TWBTerrainOverlays.hlsl) and the two look materials' previews
// (TWBBloodLook.shader, TWBCurseLook.shader). One copy of the code, so what
// the preview shows is exactly what the terrain draws.
//
// Both looks are full surfaces, not tints: a HEIGHT map drives parallax
// occlusion relief (the texture is displaced by its own depth along the view
// ray, so lumps stand up and pits sink as the camera moves), the same height
// gives NORMALS, and the looks set albedo, smoothness, metallic and
// occlusion so the terrain's PBR lighting adds the reflections.
//
// Tangent frame: the terrain's, T = +X, B = +Z, N = +Y (normalTS.x runs
// along world X, normalTS.y along world Z).
//
// The INCLUDING shader declares the uniforms these read before including:
//   _BloodAlbedo + sampler_BloodAlbedo, _BloodHeight, _BloodTint,
//   _BloodDriedTint, _BloodGutsTint, _BloodTiling, _BloodHeightScale,
//   _BloodNormalStrength, _BloodSmoothness, _BloodDrySmoothness,
//   _BloodDryness, _BloodPatchScale, _BloodNoiseScale
//   _CurseAlbedo + sampler_CurseAlbedo, _CurseNormal, _CurseHeight,
//   _CurseRoughness, _CurseOcclusion, _CurseTint, _CurseTint2, _CurseTiling,
//   _CurseHeightScale, _CurseNormalStrength, _CurseSmoothness,
//   _CurseMetallic, _CurseJaggedness, _CurseSparkleScale
//   _OverlayNoiseScale
// Every texture is sampled through its look's ONE sampler, so the terrain
// shader stays inside the sampler limit.

#ifndef TWB_GROUND_LOOKS_INCLUDED
#define TWB_GROUND_LOOKS_INCLUDED

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

float TWB_Fbm(float2 p)
{
    return TWB_ValueNoise(p) * 0.5 + TWB_ValueNoise(p * 2.03) * 0.3 + TWB_ValueNoise(p * 4.11) * 0.2;
}

// Cellular (Voronoi) cracks: distance to the nearest cell border, in cell
// units (0 on a border). Cells are the plates dried blood splits into.
float TWB_CellEdge(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float f1 = 8.0, f2 = 8.0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 o = float2(TWB_Hash(i + g), TWB_Hash(i + g + 19.19));
        float d = length(g + o - f);
        if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) { f2 = d; }
    }
    return f2 - f1;
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

// ── Relief helpers ─────────────────────────────────────────────────────

// Parallax occlusion mapping: march the view ray down through the height
// field (1 = surface top) and return the uv where it lands. viewTS is the
// direction TO the camera in the terrain's tangent frame; depthUV is the
// relief depth in uv units. Gradients are passed in so sampling inside the
// loop stays filtered.
float2 TWB_ParallaxUV(TEXTURE2D_PARAM(heightTex, heightSampler), float2 uv,
    float2 dx, float2 dy, float3 viewTS, float depthUV, int steps)
{
    float layer = 1.0 / steps;
    float2 delta = viewTS.xy / max(viewTS.z, 0.25) * depthUV * layer;
    float depth = 0.0;
    float2 cur = uv;
    float surf = 1.0 - SAMPLE_TEXTURE2D_GRAD(heightTex, heightSampler, cur, dx, dy).r;
    [loop] for (int i = 0; i < steps && depth < surf; i++)
    {
        cur -= delta;
        surf = 1.0 - SAMPLE_TEXTURE2D_GRAD(heightTex, heightSampler, cur, dx, dy).r;
        depth += layer;
    }
    // Linear refinement between the last two layers.
    float2 prev = cur + delta;
    float after = surf - depth;
    float before = (1.0 - SAMPLE_TEXTURE2D_GRAD(heightTex, heightSampler, prev, dx, dy).r) - (depth - layer);
    float w = after / (after - before + 1e-5);
    return lerp(cur, prev, saturate(w));
}

// Tangent-space normal from a height map's slope (central differences).
half3 TWB_HeightNormal(TEXTURE2D_PARAM(heightTex, heightSampler), float2 uv,
    float2 dx, float2 dy, float texel, float strength)
{
    float hL = SAMPLE_TEXTURE2D_GRAD(heightTex, heightSampler, uv - float2(texel, 0), dx, dy).r;
    float hR = SAMPLE_TEXTURE2D_GRAD(heightTex, heightSampler, uv + float2(texel, 0), dx, dy).r;
    float hD = SAMPLE_TEXTURE2D_GRAD(heightTex, heightSampler, uv - float2(0, texel), dx, dy).r;
    float hU = SAMPLE_TEXTURE2D_GRAD(heightTex, heightSampler, uv + float2(0, texel), dx, dy).r;
    return normalize(half3((hL - hR) * strength, (hD - hU) * strength, 1.0h));
}

// ── Hex tiling (2026-10-17) ────────────────────────────────────────────
// A texture repeated on a plain grid shows its period: on a large cursed
// field the eye finds the 5 m crystal tile at once. Hex tiling (Mikkelsen,
// "Practical Real-Time Hex-Tiling", 2022, after Heitz & Neyret) lays a
// triangle grid over the uv; each grid vertex reads the texture with its
// own random rotation and offset, and every pixel blends the three vertices
// of its triangle. Weights are sharpened and biased by the texture's own
// height, so at a seam the taller crystals win instead of a soft cross-fade.
// The same texture, no visible period.

// Two hash values from a grid vertex (Hoskins hash22: sin-free, so it stays
// uniform at any coordinate — see TWB_Hash).
float2 TWB_Hash22(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// The triangle of the hex grid holding st: its three vertices (grid ids)
// and barycentric weights.
void TWB_HexGrid(float2 st, out float3 w, out float2 v1, out float2 v2, out float2 v3)
{
    st *= 3.4641016;                                   // 2 sqrt(3)
    float2 skew = float2(st.x - 0.57735027 * st.y, 1.15470054 * st.y);
    float2 base = floor(skew);
    float3 t = float3(frac(skew), 0.0);
    t.z = 1.0 - t.x - t.y;
    float s = step(0.0, -t.z);
    float s2 = 2.0 * s - 1.0;
    w = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
    v1 = base + float2(s, s);
    v2 = base + float2(s, 1.0 - s);
    v3 = base + float2(1.0 - s, s);
}

// One vertex's sampling frame: uv rotated about the vertex's centre and
// offset, and the rotated gradients (so mip selection stays right).
// rot maps a texture-space direction back to st space: d_st = mul(rot, d_tex).
void TWB_HexTile(float2 st, float2 v, float density, float2 dx, float2 dy,
    out float2 uv, out float2 tdx, out float2 tdy, out float2x2 rot)
{
    float a = TWB_Hash22(v + 0.5).x * 6.2831853;
    float cs = cos(a), sn = sin(a);
    rot = float2x2(cs, -sn, sn, cs);
    float2 cen = float2(v.x + 0.5 * v.y, v.y * 0.8660254) / (3.4641016 * density);
    uv = mul(st - cen, rot) + cen + TWB_Hash22(v + 41.7);
    tdx = mul(dx, rot);
    tdy = mul(dy, rot);
}

// Blend weights: the barycentrics sharpened, then biased toward the taller
// texel so the seams follow the texture's own relief.
float3 TWB_HexWeights(float3 w, float3 heights, float contrast)
{
    float3 W = pow(max(w, 0.0), 7.0) * lerp(1.0, heights, contrast);
    return W / max(W.x + W.y + W.z, 1e-5);
}

// ── Blood: drying blood and guts ───────────────────────────────────────
// Where the blood mask is low, high-frequency noise breaks it into
// droplets; where it is high it pools. The guts height map gives the pool
// relief (parallax + normals): chunks stand proud, blood settles in the
// hollows. WET blood — in the hollows, in thick pools, in patches — is a
// bright glossy red film that smooths the relief; DRY blood — on lumps, at
// thin edges, in other patches — is dark brown and matte, and keeps every
// bump. Guts chunks read as lighter flesh; noise streaks add darker and
// lighter paths through it all.
void TWB_ApplyBloodLook(float3 positionWS, half blood, half3 viewWS,
    inout half3 albedo, inout half3 normalTS, inout half smoothness, inout half occlusion)
{
    float2 wpos = positionWS.xz;
    float tiling = max(_BloodTiling, 0.01);
    float2 uv0 = wpos / tiling;
    float2 dx = ddx(uv0), dy = ddy(uv0);
    if (blood <= 0.003h) return;

    float3 viewTS = float3(viewWS.x, viewWS.z, viewWS.y);
    float2 uv = TWB_ParallaxUV(TEXTURE2D_ARGS(_BloodHeight, sampler_BloodAlbedo), uv0, dx, dy,
                               viewTS, _BloodHeightScale / tiling, 10);
    half h = SAMPLE_TEXTURE2D_GRAD(_BloodHeight, sampler_BloodAlbedo, uv, dx, dy).r;

    // Coverage: spatter noise vs the blood amount, with the relief biting
    // in so the outline follows the guts (chunks reach out first).
    float spatN = TWB_ValueNoise(wpos * _BloodNoiseScale) * 0.7
                + TWB_ValueNoise(wpos * _BloodNoiseScale * 3.1) * 0.3;
    half spat = smoothstep(spatN - 0.06, spatN + 0.06, blood * 1.25h + (h - 0.5h) * 0.3h);
    if (spat <= 0.003h) return;

    // Wet or dry. Patches from slow noise against the dryness setting;
    // hollows stay wet, lumps dry; thin blood (the spatter rim) dries first.
    half patch = (half)TWB_Fbm(wpos * _BloodPatchScale);
    half wet = saturate((patch - _BloodDryness) * 7.0h + 0.5h);
    wet = saturate(wet + (0.5h - h) * 0.9h);
    wet *= saturate(blood * 1.6h);
    half dry = 1.0h - wet;

    // Dried blood CRACKS into plates as it shrinks in the sun: dark seams
    // that also dent the normal. Only where it is dry.
    float crackD = TWB_CellEdge(wpos * _BloodPatchScale * 14.0);
    half crack = (half)(1.0 - smoothstep(0.0, 0.09, crackD)) * dry;

    half3 detail = SAMPLE_TEXTURE2D_GRAD(_BloodAlbedo, sampler_BloodAlbedo, uv, dx, dy).rgb;
    half3 col = lerp(_BloodDriedTint.rgb, _BloodTint.rgb, wet);
    half chunks = smoothstep(0.6h, 0.85h, h);
    col = lerp(col, _BloodGutsTint.rgb, chunks * (1.0h - wet * 0.6h));
    half streak = (half)TWB_Fbm(wpos * _BloodPatchScale * 5.3 + 17.0);
    col *= lerp(0.6h, 1.3h, streak) * lerp(0.65h, 1.15h, h);
    col *= detail;
    col *= 1.0h - crack * 0.75h;                     // dark crack seams
    // A darker drying rim where a pool thins out to nothing.
    col *= lerp(0.55h, 1.0h, smoothstep(0.0h, 0.45h, spat));
    albedo = lerp(albedo, col, spat);

    float texel = 2.5 / 1024.0;
    half3 n = TWB_HeightNormal(TEXTURE2D_ARGS(_BloodHeight, sampler_BloodAlbedo), uv, dx, dy,
                               texel, _BloodNormalStrength);
    // Crack seams: a dent across the seam, from the cell-edge gradient.
    float2 cp = wpos * _BloodPatchScale * 14.0;
    float ce = 0.02;
    float2 cg = float2(TWB_CellEdge(cp + float2(ce, 0)) - crackD, TWB_CellEdge(cp + float2(0, ce)) - crackD) / ce;
    n = normalize(half3(n.xy + (half2)cg * crack * 0.15h, n.z));
    n = normalize(lerp(n, half3(0, 0, 1), wet * 0.7h));   // a liquid film smooths the relief
    normalTS = normalize(lerp(normalTS, n, spat));

    smoothness = lerp(smoothness, lerp(_BloodDrySmoothness, _BloodSmoothness, wet), spat);
    occlusion = lerp(occlusion, occlusion * lerp(0.4h, 1.0h, h) * (1.0h - crack * 0.5h), spat);
}

// ── Curse: veilstone crystal ground ────────────────────────────────────
// The authored ground is REPLACED by faceted crystal: the crystal set's
// height map gives parallax depth and (with its normal map) sharp facet
// normals; the facets are recoloured between the purple and greenish poles
// by slow noise; smoothness comes from the roughness map so the faces
// REFLECT, with a metallic sheen; occlusion darkens the cracks; glints sit
// on the crystal tips. The front is JAGGED: the crystal relief is added to
// the coverage, so tall crystals break through before the ground between
// them, and the feather is tight. Returns the blend (0..1).
//
// HEX-TILED (2026-10-17): every crystal map is read through TWB_HexTile, so
// the 5 m tile never repeats visibly. Parallax and the height-derived normal
// run once, on the triangle's dominant vertex, and the parallax shift is
// carried to the other two — three times the texture reads of a plain tile
// would otherwise land on the 12-step relief march.
#define TWB_CURSE_HEX_DENSITY 0.6      // grid vertices per texture tile (lower = bigger patches)
#define TWB_CURSE_HEX_CONTRAST 0.7     // how far height decides the seams

half TWB_ApplyCurseLook(float3 positionWS, half coverage, half3 viewWS,
    inout half3 albedo, inout half3 normalTS, inout half smoothness,
    inout half metallic, inout half occlusion)
{
    float2 wpos = positionWS.xz;
    float tiling = max(_CurseTiling, 0.01);
    float2 st = wpos / tiling;
    float2 dx = ddx(st), dy = ddy(st);
    if (coverage <= 0.003h) return 0.0h;

    float3 w; float2 v1, v2, v3;
    TWB_HexGrid(st * TWB_CURSE_HEX_DENSITY, w, v1, v2, v3);
    float2 uv1, uv2, uv3, dx1, dx2, dx3, dy1, dy2, dy3;
    float2x2 r1, r2, r3;
    TWB_HexTile(st, v1, TWB_CURSE_HEX_DENSITY, dx, dy, uv1, dx1, dy1, r1);
    TWB_HexTile(st, v2, TWB_CURSE_HEX_DENSITY, dx, dy, uv2, dx2, dy2, r2);
    TWB_HexTile(st, v3, TWB_CURSE_HEX_DENSITY, dx, dy, uv3, dx3, dy3, r3);

    // Relief on the dominant vertex; its shift, taken back to st space, moves
    // all three.
    bool d1 = w.x >= w.y && w.x >= w.z;
    bool d2 = !d1 && w.y >= w.z;
    float2 uvD = d1 ? uv1 : (d2 ? uv2 : uv3);
    float2 dxD = d1 ? dx1 : (d2 ? dx2 : dx3);
    float2 dyD = d1 ? dy1 : (d2 ? dy2 : dy3);
    float2x2 rD = d1 ? r1 : (d2 ? r2 : r3);
    float3 viewTS = float3(viewWS.x, viewWS.z, viewWS.y);
    float3 viewD = float3(mul(viewTS.xy, rD), viewTS.z);
    float2 uvP = TWB_ParallaxUV(TEXTURE2D_ARGS(_CurseHeight, sampler_CurseAlbedo), uvD, dxD, dyD,
                                viewD, _CurseHeightScale / tiling, 12);
    float2 shift = mul(rD, uvP - uvD);
    uv1 += mul(shift, r1); uv2 += mul(shift, r2); uv3 += mul(shift, r3);
    uvD += mul(shift, rD);

    float3 hs = float3(SAMPLE_TEXTURE2D_GRAD(_CurseHeight, sampler_CurseAlbedo, uv1, dx1, dy1).r,
                       SAMPLE_TEXTURE2D_GRAD(_CurseHeight, sampler_CurseAlbedo, uv2, dx2, dy2).r,
                       SAMPLE_TEXTURE2D_GRAD(_CurseHeight, sampler_CurseAlbedo, uv3, dx3, dy3).r);
    float3 W = TWB_HexWeights(w, hs, TWB_CURSE_HEX_CONTRAST);
    half h = (half)dot(hs, W);

    float nn = TWB_ValueNoise(wpos * _OverlayNoiseScale) * 0.65
             + TWB_ValueNoise(wpos * _OverlayNoiseScale * 3.7) * 0.35;
    nn = lerp(0.25, 0.85, nn);
    half cBlend = smoothstep(nn - 0.035, nn + 0.035,
                             coverage * 1.15h + (h - 0.5h) * _CurseJaggedness);
    if (cBlend <= 0.003h) return cBlend;

    half3 crystal = SAMPLE_TEXTURE2D_GRAD(_CurseAlbedo, sampler_CurseAlbedo, uv1, dx1, dy1).rgb * (half)W.x
                  + SAMPLE_TEXTURE2D_GRAD(_CurseAlbedo, sampler_CurseAlbedo, uv2, dx2, dy2).rgb * (half)W.y
                  + SAMPLE_TEXTURE2D_GRAD(_CurseAlbedo, sampler_CurseAlbedo, uv3, dx3, dy3).rgb * (half)W.z;
    half lum = dot(crystal, half3(0.299h, 0.587h, 0.114h));
    float tone = TWB_ValueNoise(wpos * _OverlayNoiseScale * 0.6);
    half3 tint = lerp(_CurseTint.rgb, _CurseTint2.rgb, (half)tone);
    // Facets: the crystal map's tone pushed to high contrast (deep, dark
    // crystal bodies, bright faces and edges), then tinted.
    half facet = saturate((lum - 0.45h) * 2.6h + 0.5h);
    facet *= facet;
    half3 cursed = tint * (0.12h + facet * 1.6h);
    float glint = TWB_ValueNoise(wpos * _CurseSparkleScale);
    glint = pow(saturate(glint), 12.0) * 3.0 * smoothstep(0.55, 0.85, h);
    cursed += (half)glint * tint;
    albedo = lerp(albedo, cursed, cBlend);

    // Normals: each vertex's normal turned back into st space (its xy is a
    // direction in that vertex's rotated texture frame), then blended.
    half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_CurseNormal, sampler_CurseAlbedo, uv1, dx1, dy1), _CurseNormalStrength);
    half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_CurseNormal, sampler_CurseAlbedo, uv2, dx2, dy2), _CurseNormalStrength);
    half3 n3 = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_CurseNormal, sampler_CurseAlbedo, uv3, dx3, dy3), _CurseNormalStrength);
    n1.xy = (half2)mul(r1, (float2)n1.xy);
    n2.xy = (half2)mul(r2, (float2)n2.xy);
    n3.xy = (half2)mul(r3, (float2)n3.xy);
    half3 nMap = n1 * (half)W.x + n2 * (half)W.y + n3 * (half)W.z;
    half3 nHeight = TWB_HeightNormal(TEXTURE2D_ARGS(_CurseHeight, sampler_CurseAlbedo), uvD, dxD, dyD,
                                     1.0 / 1024.0, _CurseNormalStrength * 4.0);
    nHeight.xy = (half2)mul(rD, (float2)nHeight.xy);
    half3 n = normalize(half3(nMap.xy + nHeight.xy, nMap.z));
    normalTS = normalize(lerp(normalTS, n, cBlend));

    half rough = SAMPLE_TEXTURE2D_GRAD(_CurseRoughness, sampler_CurseAlbedo, uv1, dx1, dy1).r * (half)W.x
               + SAMPLE_TEXTURE2D_GRAD(_CurseRoughness, sampler_CurseAlbedo, uv2, dx2, dy2).r * (half)W.y
               + SAMPLE_TEXTURE2D_GRAD(_CurseRoughness, sampler_CurseAlbedo, uv3, dx3, dy3).r * (half)W.z;
    smoothness = lerp(smoothness, (1.0h - rough) * _CurseSmoothness, cBlend);
    metallic = lerp(metallic, _CurseMetallic, cBlend);
    half ao = SAMPLE_TEXTURE2D_GRAD(_CurseOcclusion, sampler_CurseAlbedo, uv1, dx1, dy1).r * (half)W.x
            + SAMPLE_TEXTURE2D_GRAD(_CurseOcclusion, sampler_CurseAlbedo, uv2, dx2, dy2).r * (half)W.y
            + SAMPLE_TEXTURE2D_GRAD(_CurseOcclusion, sampler_CurseAlbedo, uv3, dx3, dy3).r * (half)W.z;
    occlusion = lerp(occlusion, occlusion * ao, cBlend);
    return cBlend;
}

#endif
