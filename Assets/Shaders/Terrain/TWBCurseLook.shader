// TWBCurseLook.shader
// THE CURSE LOOK (2026-10-16): the one material — Resources/Curse.mat — that
// says what cursed ground looks like. TerrainOverlayMaterialBinder copies its
// Curse properties onto the terrain at match start, and every frame in the
// Editor so edits show live in Play mode.
//
// PREVIEW: this shader draws the terrain's OWN curse code
// (TWBGroundLooks.hlsl) over a plain ground, lit by URP's PBR lighting like
// the terrain (so the crystal faces reflect), with the curse ramping from
// none at the left edge (u = 0) to full at the right (u = 1) — the jagged
// front and the full crystal field side by side. Waning Border > Art >
// Ground Look Preview opens a scene with it on a big panel. "Preview only"
// fields never reach the terrain (the front's noise scale is the terrain
// material's own, shared with every overlay edge).
Shader "TWB/Terrain/Curse Look"
{
    Properties
    {
        [Header(Crystal maps)]
        _CurseAlbedo("Crystal Colour", 2D) = "white" {}
        [Normal] _CurseNormal("Crystal Normal", 2D) = "bump" {}
        _CurseHeight("Crystal Height (relief)", 2D) = "gray" {}
        _CurseRoughness("Crystal Roughness", 2D) = "gray" {}
        _CurseOcclusion("Crystal Occlusion", 2D) = "white" {}
        _CurseTiling("Tiling (m per repeat)", Float) = 6.0
        _CurseHeightScale("Relief Depth (m)", Float) = 0.12
        _CurseNormalStrength("Normal Strength", Float) = 1.5
        [Header(Colour and shine)]
        _CurseTint("Tint (purple pole)", Color) = (0.62, 0.35, 0.9, 1)
        _CurseTint2("Tint (greenish pole)", Color) = (0.45, 0.85, 0.55, 1)
        _CurseSmoothness("Max Smoothness (reflection)", Range(0,1)) = 0.95
        _CurseMetallic("Metallic", Range(0,1)) = 0.35
        _CurseSparkleScale("Glint Scale (per m)", Float) = 3.0
        [Header(Front)]
        _CurseJaggedness("Front Jaggedness", Float) = 0.6
        _CurseShelfHeight("Shelf Height above ground (m)", Float) = 0.5
        [Header(Preview only)]
        _PreviewGround("Preview Ground Colour", Color) = (0.36, 0.33, 0.24, 1)
        _PreviewSize("Preview Size (m)", Float) = 40.0
        _PreviewEdgeNoise("Preview Front Noise Scale", Float) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Preview"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_CurseAlbedo); SAMPLER(sampler_CurseAlbedo);
            TEXTURE2D(_CurseNormal); TEXTURE2D(_CurseHeight);
            TEXTURE2D(_CurseRoughness); TEXTURE2D(_CurseOcclusion);
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
                half4 _PreviewGround;
                float _PreviewSize;
                float _PreviewEdgeNoise;
            CBUFFER_END
            #define _OverlayNoiseScale _PreviewEdgeNoise

            // The shared code also carries the blood look; it is not drawn
            // here, but its inputs must exist for it to compile.
            TEXTURE2D(_BloodAlbedo); SAMPLER(sampler_BloodAlbedo);
            TEXTURE2D(_BloodHeight);
            half4 _BloodTint, _BloodDriedTint, _BloodGutsTint;
            float _BloodTiling, _BloodHeightScale, _BloodNormalStrength, _BloodSmoothness,
                  _BloodDrySmoothness, _BloodDryness, _BloodPatchScale, _BloodNoiseScale;

            #include "TWBGroundLooks.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 lookPos = float3(i.uv.x * _PreviewSize, 0, i.uv.y * _PreviewSize);
                half3 viewWS = (half3)GetWorldSpaceNormalizeViewDir(i.positionWS);

                half3 albedo = _PreviewGround.rgb;
                half3 normalTS = half3(0, 0, 1);
                half smoothness = 0.15h, metallic = 0.0h, occlusion = 1.0h;
                TWB_ApplyCurseLook(lookPos, (half)saturate(i.uv.x), viewWS,
                                   albedo, normalTS, smoothness, metallic, occlusion);

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalize(half3(normalTS.x, normalTS.z, normalTS.y));
                input.viewDirectionWS = viewWS;
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = metallic;
                surf.smoothness = smoothness;
                surf.occlusion = occlusion;
                surf.normalTS = normalTS;
                surf.alpha = 1;
                return UniversalFragmentPBR(input, surf);
            }
            ENDHLSL
        }
    }
}
