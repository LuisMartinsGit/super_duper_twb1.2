// BuildingLitDissolve.shader
// URP-Lit-equivalent dissolve for BOTH buildings of a level-up: each wears
// this for the 1.5 s wave. Shading goes through
// UniversalFragmentPBR so the lit output matches the rest of the scene —
// same main light, shadows, SH/GI, reflection probes, fresnel — and the
// material carries the source's albedo, NORMAL MAP, emission, occlusion,
// metallic and smoothness (BuildingDissolveTransition.BindLitDissolve), so
// the building keeps its relief and its look while it goes. Only the wave
// clip is custom logic.
//
// Inverted = 0 → OLD mesh: pixels BELOW the wave are clipped (top stays).
// Inverted = 1 → NEW mesh: pixels ABOVE the wave are clipped (bottom stays),
//                so the new building is revealed by the wave and never seen
//                ahead of it. The two halves never overlap, so no depth bias.
//
// EVERY PASS CLIPS (2026-10-30). This shader had only the forward pass, and
// a custom URP shader gets no ShadowCaster / DepthOnly / DepthNormals from
// its Fallback — so during a level-up the dissolving building vanished from
// the depth-normals prepass, the shadow map and SSAO: no ink outline, no
// shadow, the ground's outline drawn straight through the walls. The three
// passes below apply the same wave clip.

Shader "TheWaningBorder/BuildingLitDissolve"
{
    Properties
    {
        _BaseColor          ("Base Color", Color)               = (1,1,1,1)
        _BaseMap            ("Base Map", 2D)                    = "white" {}
        _UseBaseMap         ("Use Base Map", Range(0,1))        = 0
        // Player colour (TWBFactionColor.hlsl): copied from the source material.
        [HideInInspector] _TWB_FactionHSV ("Faction HSV", Vector) = (0,0,0,0)
        [HideInInspector][NoScaleOffset] _TWB_FactionMask ("Faction Mask", 2D) = "black" {}
        _DetailAlbedoMap    ("Detail Albedo Map", 2D)           = "white" {}
        _UseDetailMap       ("Use Detail Map", Range(0,1))      = 0
        [Normal] _BumpMap   ("Normal Map", 2D)                  = "bump" {}
        _BumpScale          ("Normal Scale", Float)             = 1
        _UseBumpMap         ("Use Normal Map", Range(0,1))      = 0
        _OcclusionMap       ("Occlusion Map", 2D)               = "white" {}
        _UseOcclusionMap    ("Use Occlusion Map", Range(0,1))   = 0
        _EmissionMap        ("Emission Map", 2D)                = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color)          = (0,0,0,1)
        _UseEmissionMap     ("Use Emission Map", Range(0,1))    = 0
        _Metallic           ("Metallic", Range(0,1))            = 0
        _Smoothness         ("Smoothness", Range(0,1))          = 0.5

        _DissolveAmount ("Dissolve Amount", Range(0,1)) = 0
        _Inverted       ("Inverted (1 = reveal)", Range(0,1)) = 0
        _WaveOriginY    ("Wave Origin Y", Float)    = 0
        _WaveSpan       ("Wave Span", Float)        = 5
        _NoiseScale     ("Noise Scale", Float)      = 4.5
        _NoiseStrength  ("Noise Strength", Range(0,1)) = 0.30
        _AmountPad      ("Amount Pad", Range(0, 0.5)) = 0.20
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // Shared by every pass: the wave properties, the noise and the clip.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float _DissolveAmount;
        float _Inverted;
        float _WaveOriginY;
        float _WaveSpan;
        float _NoiseScale;
        float _NoiseStrength;
        float _AmountPad;

        float Hash13(float3 p)
        {
            p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash13(i + float3(0,0,0));
            float n100 = Hash13(i + float3(1,0,0));
            float n010 = Hash13(i + float3(0,1,0));
            float n110 = Hash13(i + float3(1,1,0));
            float n001 = Hash13(i + float3(0,0,1));
            float n101 = Hash13(i + float3(1,0,1));
            float n011 = Hash13(i + float3(0,1,1));
            float n111 = Hash13(i + float3(1,1,1));
            return lerp(
                lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                f.z);
        }

        // Discard the pixel on the wrong side of the wave — same threshold
        // formulation as BuildingWaveBand, so the band sits on the seam.
        void WaveClip(float3 worldPos)
        {
            float vert01    = saturate((worldPos.y - _WaveOriginY) / max(_WaveSpan, 0.001));
            float n         = ValueNoise(worldPos * _NoiseScale);
            float threshold = vert01 + (n - 0.5) * _NoiseStrength;
            float amount    = lerp(-_AmountPad, 1.0 + _AmountPad, _DissolveAmount);
            float diff      = (_Inverted > 0.5) ? (threshold - amount) : (amount - threshold);
            clip(-diff);
        }
        ENDHLSL

        Pass
        {
            Name "FORWARD_LIT"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Mirror URP-Lit's full variant set so reflection probes, shadows,
            // light cookies, Forward+ tiles, etc. behave EXACTLY the same as
            // a stock URP-Lit material. Without _FORWARD_PLUS in particular,
            // reflection probes used the Forward path's lookup and "popped in"
            // after the inline variant compile completed mid-dissolve.
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            sampler2D _BaseMap;
            float4    _BaseMap_ST;
            float4    _TWB_FactionHSV;   // player colour, see TWBFactionColor.hlsl
            #include "Assets/Shaders/Include/TWBFactionColor.hlsl"
            SAMPLER(sampler_TWB_FactionMask);
            sampler2D _DetailAlbedoMap;
            float4    _DetailAlbedoMap_ST;
            sampler2D _BumpMap;
            sampler2D _OcclusionMap;
            sampler2D _EmissionMap;
            float4    _BaseColor;
            float4    _EmissionColor;
            float     _UseBaseMap;
            float     _UseDetailMap;
            float     _UseBumpMap;
            float     _BumpScale;
            float     _UseOcclusionMap;
            float     _UseEmissionMap;
            float     _Metallic;
            float     _Smoothness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 baseUV      : TEXCOORD2;
                float2 detailUV    : TEXCOORD3;
                half3  vertexLight : TEXCOORD4;
                half   fogFactor   : TEXCOORD5;
                half4  tangentWS   : TEXCOORD6;   // xyz tangent, w bitangent sign
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 7);
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionHCS = vpi.positionCS;
                OUT.worldPos    = vpi.positionWS;
                OUT.worldNormal = vni.normalWS;
                OUT.tangentWS   = half4(vni.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.baseUV      = IN.uv * _BaseMap_ST.xy        + _BaseMap_ST.zw;
                OUT.detailUV    = IN.uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;

                // Per-vertex contributions URP-Lit sets up — additional lights
                // in vertex mode + fog factor for distance/atmosphere blend.
                OUT.vertexLight = VertexLighting(vpi.positionWS, vni.normalWS);
                OUT.fogFactor   = ComputeFogFactor(vpi.positionCS.z);

                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                OUTPUT_SH(vni.normalWS.xyz, OUT.vertexSH);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                WaveClip(IN.worldPos);

                // Albedo: _BaseColor, optionally modulated by the Base Map
                // (standard URP-Lit albedo workflow) and/or the Detail Albedo
                // Map (URP-Lit's ×2 detail-overlay). Both flags are runtime-
                // settable so the dissolve renders the same look whether your
                // texture lives in Surface Inputs > Base Map or Detail Inputs.
                half3 albedo = _BaseColor.rgb;
                if (_UseBaseMap > 0.5)
                {
                    half3 baseTex = TWB_FactionRecolor(tex2D(_BaseMap, IN.baseUV).rgb, IN.baseUV, sampler_TWB_FactionMask);
                    albedo *= baseTex;
                }
                if (_UseDetailMap > 0.5)
                {
                    half3 detail = tex2D(_DetailAlbedoMap, IN.detailUV).rgb;
                    albedo *= detail * 2.0;
                }

                // The source's normal map, so tiles, planks and stone keep
                // their relief instead of flattening for the wave.
                float3 normalWS = normalize(IN.worldNormal);
                if (_UseBumpMap > 0.5)
                {
                    half3 normalTS = UnpackNormalScale(tex2D(_BumpMap, IN.baseUV), _BumpScale);
                    float sgn = IN.tangentWS.w;
                    float3 bitangent = sgn * cross(normalWS, IN.tangentWS.xyz);
                    normalWS = normalize(TransformTangentToWorld(normalTS,
                        half3x3(IN.tangentWS.xyz, bitangent, normalWS)));
                }

                half occlusion = _UseOcclusionMap > 0.5 ? tex2D(_OcclusionMap, IN.baseUV).g : 1;
                half3 emission = _EmissionColor.rgb;
                if (_UseEmissionMap > 0.5) emission *= tex2D(_EmissionMap, IN.baseUV).rgb;

                // Hand-off to URP's PBR fragment so lighting matches the
                // rest of the scene exactly — main light + shadows, GI / SH
                // ambient, reflection probes, fresnel, energy conservation.
                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.worldPos;
                inputData.normalWS                = normalWS;
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
                surfaceData.emission            = emission;
                surfaceData.occlusion           = occlusion;
                surfaceData.alpha               = 1;
                surfaceData.specular            = half3(0, 0, 0);
                surfaceData.clearCoatMask       = 0;
                surfaceData.clearCoatSmoothness = 0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Atmospheric fog blend — URP-Lit applies this AFTER PBR. Without
                // it, my output sits "in front of" the scene's fog tint, which
                // reads as different lighting / reflection intensity.
                color.rgb = MixFog(color.rgb, IN.fogFactor);

                return color;
            }
            ENDHLSL
        }

        // The shadow this half of the building casts, clipped at the wave.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // CommonMaterial first: Shadows.hlsl uses LerpWhiteTo from it and
            // only gets it through Lighting.hlsl, which this pass does not need.
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionHCS = positionCS;
                OUT.worldPos    = positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                WaveClip(IN.worldPos);
                return 0;
            }
            ENDHLSL
        }

        // Depth for the prepass and the depth texture, clipped at the wave.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.worldPos);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                WaveClip(IN.worldPos);
                return 0;
            }
            ENDHLSL
        }

        // Normals for the depth-normals prepass — what the pencil ink and
        // SSAO read — clipped at the wave.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.worldPos);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                WaveClip(IN.worldPos);
                return half4(normalize(IN.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
