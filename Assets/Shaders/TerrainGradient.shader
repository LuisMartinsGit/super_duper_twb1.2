// TerrainGradient.shader
// Companion shader to Cliff.shader. Colors the terrain by world-space Y using a
// 3-stop wall gradient + a flat top color above a configurable shelf height.
// Uses no UVs — there are no textures sampled by UV — so the steep-slope
// "stretched texture" problem with Unity Terrain's splat-mapped Lit shader goes
// away entirely.
//
// To use: create a Material, set its shader to TheWaningBorder/TerrainGradient,
// dial in the colors + ShelfHeight to match your cliff GameObject, then assign
// the Material to the Unity Terrain via Terrain → Terrain Settings → Material.
//
// Modeled on Cliff.shader / BuildingLitDissolve.shader so lighting matches the
// rest of the scene (UniversalFragmentPBR, shadows, fog, GI, reflection probes).

Shader "TheWaningBorder/TerrainGradient"
{
    Properties
    {
        _TopColor      ("Top (Shelf) Color", Color)            = (0.55, 0.50, 0.40, 1)
        _WallTopColor  ("Wall Top Color",    Color)            = (0.62, 0.55, 0.44, 1)
        _WallMidColor  ("Wall Mid Color",    Color)            = (0.45, 0.38, 0.30, 1)
        _WallBaseColor ("Wall Base Color",   Color)            = (0.32, 0.26, 0.20, 1)

        _ShelfHeight   ("Shelf Height (world Y)", Float)        = 8
        _BaseHeight    ("Base Height (world Y)",  Float)        = 0
        _BlendBand     ("Shelf Blend Band",       Range(0, 4)) = 0.5

        _Metallic      ("Metallic",    Range(0, 1)) = 0
        _Smoothness    ("Smoothness",  Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // ────────────────────────────────────────────────────────────────────
        //  Forward Lit
        // ────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "FORWARD_LIT"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _WallTopColor;
                float4 _WallMidColor;
                float4 _WallBaseColor;
                float  _ShelfHeight;
                float  _BaseHeight;
                float  _BlendBand;
                float  _Metallic;
                float  _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                half3  vertexLight : TEXCOORD2;
                half   fogFactor   : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = vpi.positionCS;
                OUT.worldPos    = vpi.positionWS;
                OUT.worldNormal = vni.normalWS;
                OUT.vertexLight = VertexLighting(vpi.positionWS, vni.normalWS);
                OUT.fogFactor   = ComputeFogFactor(vpi.positionCS.z);
                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                OUTPUT_SH(vni.normalWS.xyz, OUT.vertexSH);
                return OUT;
            }

            // Color the terrain by world-space Y. Above the shelf it's a flat
            // TopColor (with a short cross-fade so the shelf seam doesn't read
            // as a hard ring). Below the shelf it's a 3-stop wall gradient
            // walked top → mid → base, with the wall mid sitting at the
            // halfway point between shelf and base.
            half3 ColorByHeight(float y)
            {
                float blend = max(_BlendBand, 0.0001);

                // Fade from WallTop up to TopColor across [_ShelfHeight - blend, _ShelfHeight].
                float topFade = saturate((y - (_ShelfHeight - blend)) / blend);

                // Wall gradient param: 0 at shelf, 1 at base.
                float wallSpan = max(_ShelfHeight - _BaseHeight, 0.0001);
                float wallT = saturate((_ShelfHeight - y) / wallSpan);

                half3 wallCol = (wallT < 0.5)
                    ? lerp(_WallTopColor.rgb, _WallMidColor.rgb, wallT * 2.0)
                    : lerp(_WallMidColor.rgb, _WallBaseColor.rgb, (wallT - 0.5) * 2.0);

                return lerp(wallCol, _TopColor.rgb, topFade);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 albedo = ColorByHeight(IN.worldPos.y);

                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.worldPos;
                inputData.normalWS                = normalize(IN.worldNormal);
                inputData.viewDirectionWS         = normalize(GetCameraPositionWS() - IN.worldPos);
                inputData.shadowCoord             = TransformWorldToShadowCoord(IN.worldPos);
                inputData.fogCoord                = IN.fogFactor;
                inputData.vertexLighting          = IN.vertexLight;
                inputData.bakedGI                 = SAMPLE_GI(IN.lightmapUV, IN.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                SurfaceData surfaceData         = (SurfaceData)0;
                surfaceData.albedo              = albedo;
                surfaceData.metallic            = _Metallic;
                surfaceData.smoothness          = _Smoothness;
                surfaceData.normalTS            = half3(0, 0, 1);
                surfaceData.emission            = half3(0, 0, 0);
                surfaceData.occlusion           = 1;
                surfaceData.alpha               = 1;
                surfaceData.specular            = half3(0, 0, 0);
                surfaceData.clearCoatMask       = 0;
                surfaceData.clearCoatSmoothness = 0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb   = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────
        //  Shadow caster
        // ────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────
        //  Depth-only
        // ────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
