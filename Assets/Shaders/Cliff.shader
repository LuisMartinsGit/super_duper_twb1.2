// Cliff.shader
// URP-Lit-equivalent shader that multiplies the mesh's vertex color into the
// albedo. ProceduralCliffGenerator bakes the top-shelf color and wall gradient
// directly into mesh.colors, so this shader doesn't need a texture atlas —
// it's a thin pass-through that delegates lighting to UniversalFragmentPBR
// (same main light, shadows, SH/GI, reflection probes, fog as URP-Lit).
//
// Modeled on BuildingLitDissolve.shader's URP wiring, with the dissolve removed
// and a COLOR semantic added.

Shader "TheWaningBorder/Cliff"
{
    Properties
    {
        _BaseColor   ("Tint",       Color)         = (1, 1, 1, 1)
        _Metallic    ("Metallic",   Range(0, 1))   = 0
        _Smoothness  ("Smoothness", Range(0, 1))   = 0.15
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
        //  Forward Lit pass
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

            // Match URP-Lit's variant set so shadows/reflection probes/fog
            // behave identically to a stock URP-Lit material in the scene.
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
                float4 _BaseColor;
                float  _Metallic;
                float  _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 color       : COLOR;
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
                OUT.color       = IN.color;
                OUT.vertexLight = VertexLighting(vpi.positionWS, vni.normalWS);
                OUT.fogFactor   = ComputeFogFactor(vpi.positionCS.z);
                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                OUTPUT_SH(vni.normalWS.xyz, OUT.vertexSH);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Albedo: mesh vertex color × material tint. The generator bakes
                // top-shelf color and wall gradient into mesh.colors so this is
                // the only thing that needs to multiply.
                half3 albedo = IN.color.rgb * _BaseColor.rgb;

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
        //  Shadow caster — fall through to URP-Lit's caster via Fallback.
        //  Including the actual ShadowCaster pass keeps us self-contained.
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
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ────────────────────────────────────────────────────────────────────
        //  Depth-only pass (for depth-priming / SSAO / etc.)
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
