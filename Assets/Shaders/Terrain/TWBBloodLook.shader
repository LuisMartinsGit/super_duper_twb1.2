// TWBBloodLook.shader
// THE BLOOD LOOK (2026-10-16): the one material — Resources/Blood.mat — that
// says what blood on the ground looks like. TerrainOverlayMaterialBinder
// copies its Blood properties onto the terrain at match start, and every
// frame in the Editor so edits show live in Play mode.
//
// PREVIEW: this shader draws the terrain's OWN blood code
// (TWBGroundLooks.hlsl) over a plain ground, lit by URP's PBR lighting like
// the terrain, with blood ramping from none at the left edge (u = 0) to full
// at the right (u = 1). Waning Border > Art > Ground Look Preview opens a
// scene with it on a big panel. "Preview only" fields never reach the
// terrain.
Shader "TWB/Terrain/Blood Look"
{
    Properties
    {
        [Header(Surface)]
        _BloodHeight("Guts Height (relief)", 2D) = "gray" {}
        _BloodAlbedo("Detail Albedo (multiplied)", 2D) = "white" {}
        _BloodTiling("Tiling (m per repeat)", Float) = 3.0
        _BloodHeightScale("Relief Depth (m)", Float) = 0.05
        _BloodNormalStrength("Normal Strength", Float) = 6
        [Header(Colour)]
        _BloodTint("Wet Blood", Color) = (0.45, 0.03, 0.03, 1)
        _BloodDriedTint("Dried Blood", Color) = (0.14, 0.03, 0.02, 1)
        _BloodGutsTint("Guts / Flesh", Color) = (0.55, 0.18, 0.16, 1)
        [Header(Wet and dry)]
        _BloodSmoothness("Wet Smoothness", Range(0,1)) = 0.85
        _BloodDrySmoothness("Dry Smoothness", Range(0,1)) = 0.2
        _BloodDryness("Dryness (0 all wet, 1 all dry)", Range(0,1)) = 0.5
        _BloodPatchScale("Wet/Dry Patch Scale (per m)", Float) = 0.35
        _BloodNoiseScale("Spatter Noise Scale (per m)", Float) = 0.9
        [Header(Preview only)]
        _PreviewGround("Preview Ground Colour", Color) = (0.36, 0.33, 0.24, 1)
        _PreviewSize("Preview Size (m)", Float) = 12.0
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

            TEXTURE2D(_BloodAlbedo); SAMPLER(sampler_BloodAlbedo);
            TEXTURE2D(_BloodHeight);
            CBUFFER_START(UnityPerMaterial)
                float4 _BloodAlbedo_ST;
                float4 _BloodHeight_ST;
                half4 _BloodTint;
                half4 _BloodDriedTint;
                half4 _BloodGutsTint;
                float _BloodTiling;
                float _BloodHeightScale;
                float _BloodNormalStrength;
                float _BloodSmoothness;
                float _BloodDrySmoothness;
                float _BloodDryness;
                float _BloodPatchScale;
                float _BloodNoiseScale;
                half4 _PreviewGround;
                float _PreviewSize;
            CBUFFER_END

            // The shared code also carries the curse look; it is not drawn
            // here, but its inputs must exist for it to compile.
            TEXTURE2D(_CurseAlbedo); SAMPLER(sampler_CurseAlbedo);
            TEXTURE2D(_CurseNormal); TEXTURE2D(_CurseHeight);
            TEXTURE2D(_CurseRoughness); TEXTURE2D(_CurseOcclusion);
            half4 _CurseTint, _CurseTint2;
            float _CurseTiling, _CurseHeightScale, _CurseNormalStrength, _CurseSmoothness,
                  _CurseMetallic, _CurseJaggedness, _CurseSparkleScale;
            static const float _OverlayNoiseScale = 0.15;

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
                // The panel lies flat with u along +X and v along +Z — the
                // terrain's own frame — so the look runs exactly as on the ground.
                float3 lookPos = float3(i.uv.x * _PreviewSize, 0, i.uv.y * _PreviewSize);
                half3 viewWS = (half3)GetWorldSpaceNormalizeViewDir(i.positionWS);

                half3 albedo = _PreviewGround.rgb;
                half3 normalTS = half3(0, 0, 1);
                half smoothness = 0.15h, occlusion = 1.0h;
                TWB_ApplyBloodLook(lookPos, (half)saturate(i.uv.x), viewWS, albedo, normalTS, smoothness, occlusion);

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
