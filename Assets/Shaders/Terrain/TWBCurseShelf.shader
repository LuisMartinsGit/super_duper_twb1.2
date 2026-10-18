// TWBCurseShelf.shader
// THE CRYSTAL SHELF (2026-10-16): cursed ground stands up as a shelf of
// crystal, up to _CurseShelfHeight above the terrain. CurseShelfRenderer lays
// a fine grid mesh (0.5 m) on the terrain wherever the curse is; this shader
// lifts each vertex by the SAME jagged front the terrain overlay draws
// (curse coverage from the global _TWB_CultureMask, eroded by the overlay
// noise and the crystal relief), so the shelf's edge is the crystal ground's
// edge and it grows as the curse spreads. The top is the curse look
// (TWBGroundLooks.hlsl, parallax crystal); the walls are the same crystal
// projected sideways, faceted by the mesh. Outside the curse the mesh is
// clipped away and the terrain shows.
//
// Properties are copied from Resources/Curse.mat (and the edge noise from the
// terrain material) by CurseShelfRenderer — edit Curse.mat, not this.
Shader "TWB/Terrain/Curse Shelf"
{
    Properties
    {
        [HideInInspector] _CurseAlbedo("Crystal Colour", 2D) = "white" {}
        [HideInInspector] [Normal] _CurseNormal("Crystal Normal", 2D) = "bump" {}
        [HideInInspector] _CurseHeight("Crystal Height", 2D) = "gray" {}
        [HideInInspector] _CurseRoughness("Crystal Roughness", 2D) = "gray" {}
        [HideInInspector] _CurseOcclusion("Crystal Occlusion", 2D) = "white" {}
        [HideInInspector] _CurseTiling("Tiling", Float) = 6.0
        [HideInInspector] _CurseHeightScale("Relief Depth", Float) = 0.12
        [HideInInspector] _CurseNormalStrength("Normal Strength", Float) = 1.5
        [HideInInspector] _CurseTint("Tint A", Color) = (0.62, 0.35, 0.9, 1)
        [HideInInspector] _CurseTint2("Tint B", Color) = (0.45, 0.85, 0.55, 1)
        [HideInInspector] _CurseSmoothness("Max Smoothness", Range(0,1)) = 0.95
        [HideInInspector] _CurseMetallic("Metallic", Range(0,1)) = 0.35
        [HideInInspector] _CurseSparkleScale("Glint Scale", Float) = 3.0
        [HideInInspector] _CurseJaggedness("Front Jaggedness", Float) = 0.6
        [HideInInspector] _CurseShelfHeight("Shelf Height (m)", Float) = 0.5
        [HideInInspector] _OverlayNoiseScale("Overlay Edge Noise Scale", Float) = 0.15
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_CurseAlbedo); SAMPLER(sampler_CurseAlbedo);
    TEXTURE2D(_CurseNormal); TEXTURE2D(_CurseHeight);
    TEXTURE2D(_CurseRoughness); TEXTURE2D(_CurseOcclusion);
    // The vertex lift samples the height through ITS OWN sampler: the shadow
    // and depth passes never touch _CurseAlbedo, so its sampler is stripped
    // there and borrowing it fails to compile ("Unrecognized sampler").
    SAMPLER(sampler_CurseHeight);
    CBUFFER_START(UnityPerMaterial)
        float4 _CurseAlbedo_ST;
        float4 _CurseNormal_ST;
        float4 _CurseHeight_ST;
        float4 _CurseRoughness_ST;
        float4 _CurseOcclusion_ST;
        half4 _CurseTint;
        half4 _CurseTint2;
        float _CurseTiling;
        float _CurseHeightScale;
        float _CurseNormalStrength;
        float _CurseSmoothness;
        float _CurseMetallic;
        float _CurseSparkleScale;
        float _CurseJaggedness;
        float _CurseShelfHeight;
        float _OverlayNoiseScale;
    CBUFFER_END

    // Globals published by InfluenceMaskTexture.
    TEXTURE2D(_TWB_CultureMask); SAMPLER(sampler_TWB_CultureMask);
    float4 _TWB_MaskST;

    // The shared code also carries the blood look; not drawn here.
    TEXTURE2D(_BloodAlbedo); SAMPLER(sampler_BloodAlbedo);
    TEXTURE2D(_BloodHeight);
    half4 _BloodTint, _BloodDriedTint, _BloodGutsTint;
    float _BloodTiling, _BloodHeightScale, _BloodNormalStrength, _BloodSmoothness,
          _BloodDrySmoothness, _BloodDryness, _BloodPatchScale, _BloodNoiseScale;

    #include "TWBGroundLooks.hlsl"

    // How much shelf stands at this ground point (0..1): the curse overlay's
    // own front (TWB_ApplyCurseLook), evaluated per vertex without parallax.
    float ShelfAmount(float3 groundWS, out float crystalH)
    {
        float2 maskUV = groundWS.xz * _TWB_MaskST.xy + _TWB_MaskST.zw;
        float cov = SAMPLE_TEXTURE2D_LOD(_TWB_CultureMask, sampler_TWB_CultureMask, maskUV, 0).a;
        crystalH = SAMPLE_TEXTURE2D_LOD(_CurseHeight, sampler_CurseHeight,
                                        groundWS.xz / max(_CurseTiling, 0.01), 0).r;
        float nn = TWB_ValueNoise(groundWS.xz * _OverlayNoiseScale) * 0.65
                 + TWB_ValueNoise(groundWS.xz * _OverlayNoiseScale * 3.7) * 0.35;
        nn = lerp(0.25, 0.85, nn);
        return smoothstep(nn - 0.035, nn + 0.035, cov * 1.15 + (crystalH - 0.5) * _CurseJaggedness);
    }

    // Per-PIXEL cut (2026-10-16): the vertices sit on a 0.5 m grid, so
    // deciding the edge per vertex drew it as square stair-steps and lone
    // cubes. Each pixel re-evaluates the front at its own ground point and
    // is discarded outside it, so the outline is the organic jagged crystal
    // edge the ground overlay draws.
    void ClipToFront(float2 groundXZ)
    {
        float h;
        clip(ShelfAmount(float3(groundXZ.x, 0, groundXZ.y), h) - 0.5);
    }

    // The ground point lifted into the shelf: full height inside, with the
    // crystal relief roughening the top.
    float3 ShelfPosition(float3 groundWS, out float amount)
    {
        float h;
        amount = ShelfAmount(groundWS, h);
        float lift = amount * (_CurseShelfHeight + (h - 0.5) * _CurseHeightScale * 2.0);
        return groundWS + float3(0, max(lift, 0.0), 0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  amount     : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float2 groundXZ   : TEXCOORD3;
                float3 groundWS   : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ground = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ShelfPosition(ground, o.amount);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.groundXZ = ground.xz;
                o.groundWS = ground;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                ClipToFront(i.groundXZ);

                // The mesh's own facet normal: along the ground on the top,
                // outward on the walls. TOP is measured against the GROUND's
                // slope, not world up (2026-10-16): on a hillside the whole
                // shelf top tilted past the old "y > 0.75" test and was drawn
                // with the wall projection, smearing it into diagonal stripes.
                float3 geoN = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (geoN.y < 0) geoN = -geoN;
                float3 groundN = normalize(cross(ddy(i.groundWS), ddx(i.groundWS)));
                if (groundN.y < 0) groundN = -groundN;
                bool top = dot(geoN, groundN) > 0.75;

                half3 viewWS = (half3)GetWorldSpaceNormalizeViewDir(i.positionWS);
                // Walls: project the crystal sideways — along the wall's own
                // horizontal run and up it — and skip parallax, which assumes
                // a ground plane.
                float2 run = normalize(float2(-geoN.z, geoN.x) + float2(1e-4, 0));
                float3 lookPos = top ? i.positionWS
                                     : float3(dot(i.positionWS.xz, run), 0, i.positionWS.y * 3.0);
                half3 lookView = top ? viewWS : half3(0, 1, 0);

                half3 albedo = 0;
                half3 normalTS = half3(0, 0, 1);
                half smoothness = 0, metallic = 0, occlusion = 1;
                TWB_ApplyCurseLook(lookPos, 1.0h, lookView, albedo, normalTS, smoothness, metallic, occlusion);

                half3 detailWS = half3(normalTS.x, normalTS.z, normalTS.y);   // terrain frame -> world
                half3 n = top ? normalize(detailWS)
                              : normalize((half3)geoN + (detailWS - half3(0, 1, 0)) * 0.6h);
                if (!top) albedo *= 0.8h;   // the walls catch less sky than the top

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = n;
                input.viewDirectionWS = viewWS;
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fogFactor;
                input.bakedGI = SampleSH(n);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = metallic;
                surf.smoothness = smoothness;
                surf.occlusion = occlusion;
                surf.alpha = 1;
                half4 col = UniversalFragmentPBR(input, surf);
                col.rgb = MixFog(col.rgb, i.fogFactor);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // Shadows.hlsl needs LerpWhiteTo, which lives in CommonMaterial.
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float amount : TEXCOORD0; float2 groundXZ : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ground = TransformObjectToWorld(v.positionOS.xyz);
                o.groundXZ = ground.xz;
                float3 p = ShelfPosition(ground, o.amount);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - p);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(p, float3(0, 1, 0), lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { ClipToFront(i.groundXZ); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float amount : TEXCOORD0; float2 groundXZ : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ground = TransformObjectToWorld(v.positionOS.xyz);
                o.groundXZ = ground.xz;
                float3 p = ShelfPosition(ground, o.amount);
                o.positionCS = TransformWorldToHClip(p);
                return o;
            }
            half frag(Varyings i) : SV_Target { ClipToFront(i.groundXZ); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float amount : TEXCOORD1; float2 groundXZ : TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ground = TransformObjectToWorld(v.positionOS.xyz);
                o.groundXZ = ground.xz;
                o.positionWS = ShelfPosition(ground, o.amount);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                ClipToFront(i.groundXZ);
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (n.y < 0) n = -n;
                return half4(n, 0);
            }
            ENDHLSL
        }
    }
}
