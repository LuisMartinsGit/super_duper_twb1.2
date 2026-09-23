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
//   _TWB_BloodMask    G = Region boundary → baked once (the partition is
//                                      static); darkens the ground along
//                                      the line. See RegionMap.
//   _TWB_RoadMask     R = coverage, G = finished, B = plaza, A = lateral
//                     → the road network (docs/Design/Roads.md); culture
//                     paving is drawn ONLY here, earth everywhere else on
//                     it, with wheel ruts from the lateral coordinate
//   _TWB_RoadDir      RG = road tangent → Age 1 bricks align to the road
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

// Region boundary look. Tint is a MULTIPLIER on the ground beneath, so
// the line darkens whatever it crosses instead of painting over it.
half4  _RegionEdgeTint;
half   _RegionEdgeStrength;

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

// ── Roads & plazas (docs/Design/Roads.md) ─────────────────────────────
// Built by RoadNetwork on construction events, never per frame. Shares
// _TWB_MaskST with the culture mask. R = coverage, G = finished
// (stone-eligible), B = plaza. WHERE is decided there; WHAT it looks like
// is decided here: culture paving where a culture holds the ground AND
// the network is finished, trampled earth everywhere else.
TEXTURE2D(_TWB_RoadMask);       SAMPLER(sampler_TWB_RoadMask);
TEXTURE2D(_TWB_RoadDir);        SAMPLER(sampler_TWB_RoadDir);
TEXTURE2D(_TWB_RoadEarthAlbedo); SAMPLER(sampler_TWB_RoadEarthAlbedo);
float  _TWB_RoadEnabled;        // set to 1 by RoadNetwork
float  _TWB_RoadTiling;         // metres per repeat of the earth texture
float  _TWB_RoadDarken;         // earthen core multiplier (1 = none)
float  _TWB_RoadFeather;        // noise-erosion feather at the edge
// Carriage tracks: two wheel ruts drawn from the mask's exact lateral
// coordinate (A channel, 0.5 = centreline), a strip of the original ground
// surviving between them. Roads only — a plaza carries no lateral.
float  _TWB_RutOffset;          // rut centre, 0 = centreline, 1 = rim
float  _TWB_RutWidth;           // rut half-width in lateral units
float  _TWB_RutDepth;           // rut floor darkening, 0..1
float  _TWB_RoadCentreStrip;    // original ground showing between the ruts, 0..1

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
    half2 bloodMask = SAMPLE_TEXTURE2D(_TWB_BloodMask, sampler_TWB_BloodMask, maskUV).rg;
    half blood      = bloodMask.r;
    half regionEdge = bloodMask.g;   // baked once by InfluenceMaskTexture

    // ── Roads & plazas: where the network is, and whether it is finished ─
    half road = 0.0h, roadFinished = 0.0h, plazaHere = 0.0h, lateral = 0.0h;
    float2 roadDir = float2(0.0, 0.0);
    if (_TWB_RoadEnabled > 0.5)
    {
        half4 rm = SAMPLE_TEXTURE2D(_TWB_RoadMask, sampler_TWB_RoadMask, maskUV);
        // Coverage already fades in the raster; the noise erosion then eats
        // into that gradient, so plazas are lobed and roads ragged, and both
        // blend into the ground instead of stopping at a line.
        road = TWB_Edge(rm.r, positionWS.xz, (half)_TWB_RoadFeather);
        roadFinished = rm.g;
        plazaHere = rm.b;
        lateral = rm.a * 2.0h - 1.0h;
        roadDir = SAMPLE_TEXTURE2D(_TWB_RoadDir, sampler_TWB_RoadDir, maskUV).rg * 2.0 - 1.0;
    }
    // A road texel (not plaza) with a real tangent: the ruts and the brick
    // alignment both key off this.
    half onRoad = road * (1.0h - plazaHere) * (half)saturate(length(roadDir) * 2.0);
    half3 groundBefore = albedo;

    // Culture paving happens only where a culture holds the ground AND the
    // network here is complete. Everything else on the network is earth.
    half cultureHeld = saturate(m.r + m.g + m.b);
    half paved = road * roadFinished * (half)smoothstep(0.35, 0.65, cultureHeld);
    half earthen = road * (1.0h - paved);
    if (earthen > 0.003h)
    {
        float2 eUV = positionWS.xz / max(_TWB_RoadTiling, 0.01);
        half3 earth = SAMPLE_TEXTURE2D(_TWB_RoadEarthAlbedo, sampler_TWB_RoadEarthAlbedo, eUV).rgb;
        // Worn toward the core: the more travelled, the barer.
        earth *= lerp(1.0h, (half)_TWB_RoadDarken, road);

        // Carriage tracks. The lateral coordinate is exact from the raster,
        // so the ruts sit on the road wherever it bends; a little world noise
        // keeps them from being ruler-straight.
        half wobble = (half)(TWB_ValueNoise(positionWS.xz * 0.35) - 0.5) * 0.18h;
        half lat = lateral + wobble;
        half rutA = (half)exp(-pow((abs(lat) - _TWB_RutOffset) / max(_TWB_RutWidth, 0.01), 2.0));
        half rut = rutA * onRoad;
        earth *= 1.0h - rut * (half)_TWB_RutDepth;
        // Between the ruts the ground survives: a strip of moss down the
        // middle of a cart track.
        half centre = (half)exp(-pow(lat / 0.28, 2.0)) * onRoad * (half)_TWB_RoadCentreStrip;
        earth = lerp(earth, groundBefore, centre);

        albedo = lerp(albedo, earth, earthen);
        smoothness = lerp(smoothness, lerp(0.08h, 0.18h, rut), earthen);
    }

    // ── Alanthor: slate paving ON THE NETWORK, masonry terraces on cliffs ─
    // Paving is network-only (Roads.md); the cliff treatment stays
    // territory-wide because it dresses cliff FACES, not ground.
    half aTerritory = TWB_Edge(m.r, positionWS.xz, 0.18h);
    half aBlend = aTerritory * paved;
    if (aTerritory > 0.003h)
    {
        // Steepness of the GEOMETRIC surface (1 − up): flats stay paving,
        // cliff faces inside Alanthor territory become dressed stone.
        half steep = (half)smoothstep(_TerraceSlopeStart, _TerraceSlopeFull,
            1.0 - saturate(geoNormalWS.y));

        // Bricks follow the road: on a road the paving UV is rotated into
        // the road's frame (along, across); on a plaza and on cliffs it is
        // world-aligned, and the two are blended so a road entering a plaza
        // does not snap.
        float2 worldUV = positionWS.xz / max(_AlanthorTiling, 0.01);
        float2 rd = normalize(roadDir + float2(1e-4, 0.0));
        float2 alongUV = float2(dot(positionWS.xz, rd), dot(positionWS.xz, float2(-rd.y, rd.x)))
                       / max(_AlanthorTiling, 0.01);
        half3 groundW = SAMPLE_TEXTURE2D(_AlanthorAlbedo, sampler_AlanthorAlbedo, worldUV).rgb;
        half3 groundR = SAMPLE_TEXTURE2D(_AlanthorAlbedo, sampler_AlanthorAlbedo, alongUV).rgb;
        half3 ground = lerp(groundW, groundR, onRoad);
        float2 aUV = lerp(worldUV, alongUV, onRoad);

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

        // Flats blend by the network, cliff faces by the territory.
        half aMix = lerp(aBlend, aTerritory, steep);
        albedo = lerp(albedo, ground, aMix);
        smoothness = lerp(smoothness, _AlanthorSmoothness * (1.0h - 0.5h * steep), aMix);
        #if defined(_NORMALMAP)
            half3 aNrm = UnpackNormal(SAMPLE_TEXTURE2D(_AlanthorNormal, sampler_AlanthorAlbedo, aUV));
            // On steep faces the course banding carries the detail; the
            // paving normal (projected top-down) would just smear there.
            aNrm = lerp(aNrm, half3(0.0h, 0.0h, 1.0h), steep);
            normalTS = normalize(lerp(normalTS, aNrm, aMix));
        #endif
    }

    // ── Feraldis / Runai: paving on the network, placeholder-tinted ────
    // Their own stone ships with their art passes (Roads.md §5); until
    // then the Alanthor set under the culture tint marks their roads.
    half fBlend = TWB_Edge(m.g, positionWS.xz, 0.18h) * paved;
    half rBlend = TWB_Edge(m.b, positionWS.xz, 0.18h) * paved;
    if (fBlend + rBlend > 0.003h)
    {
        float2 rd2 = normalize(roadDir + float2(1e-4, 0.0));
        float2 pUV = lerp(positionWS.xz,
                          float2(dot(positionWS.xz, rd2), dot(positionWS.xz, float2(-rd2.y, rd2.x))),
                          onRoad) / max(_AlanthorTiling, 0.01);
        half3 pave = SAMPLE_TEXTURE2D(_AlanthorAlbedo, sampler_AlanthorAlbedo, pUV).rgb;
        albedo = lerp(albedo, pave * _FeraldisTint.rgb, fBlend);
        albedo = lerp(albedo, pave * _RunaiTint.rgb, rBlend);
        smoothness = lerp(smoothness, _AlanthorSmoothness, saturate(fBlend + rBlend));
    }

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

    // ── Region boundaries (docs/Design/Regions.md) ────────────────────────
    // Drawn LAST so a boundary stays readable across cursed, bloodied and
    // cultured ground alike — it is a map-structure line, not another
    // material, and it must not disappear inside whoever owns the ground.
    //
    // Rendered as a DARKENING rather than a colour: the line has to sit under
    // every culture's palette without reading as a fourth faction, and it must
    // not compete with the influence border (the 0.5 contour), which is the
    // brighter, coloured, moving line. Regions are fixed terrain structure and
    // should read quieter than territory that is actually changing hands.
    if (regionEdge > 0.004h)
    {
        // Used RAW. The falloff shaping (a square, so the core darkens hard
        // and the line stays thin) is applied on the CPU in
        // InfluenceMaskTexture.BakeRegionEdges, which also pre-compensates the
        // sRGB decode this sampler performs. Squaring HERE as well crushed the
        // line through gamma twice and left an invisible smudge — see the
        // comment on that bake.
        //
        // NOT named `line`: that is a reserved HLSL keyword (the geometry-shader
        // primitive type, with `point` / `triangle` / `lineadj`), and using it
        // fails with a bare "unexpected token" that says nothing about why.
        half edgeAmt = saturate(regionEdge * _RegionEdgeStrength);
        albedo = lerp(albedo, albedo * _RegionEdgeTint.rgb, edgeAmt);
        smoothness = lerp(smoothness, smoothness * 0.6h, edgeAmt);
    }
}

#endif // TWB_TERRAIN_OVERLAYS_INCLUDED
