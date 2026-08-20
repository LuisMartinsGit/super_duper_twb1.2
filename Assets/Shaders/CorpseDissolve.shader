// CorpseDissolve.shader
// Minimal URP unlit dissolve used for fading out unit corpses.
// Keeps the unit's albedo (already faction-recoloured into the atlas by
// SyntyTeamColorRecolor) and clips fragments by a procedural value-noise
// threshold driven by _Dissolve (0 = fully visible, 1 = gone), with an
// emissive edge along the cut. Driven from code by CorpseDissolver.
Shader "TheWaningBorder/CorpseDissolve"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Dissolve  ("Dissolve", Range(0,1)) = 0
        _EdgeWidth ("Edge Width", Range(0.001,0.4)) = 0.08
        [HDR] _EdgeColor ("Edge Color", Color) = (4,1.2,0.25,1)
        _NoiseScale ("Noise Scale", Float) = 14
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _EdgeColor;
                float  _Dissolve;
                float  _EdgeWidth;
                float  _NoiseScale;
            CBUFFER_END

            // Cheap hash → value noise so we don't need a noise texture asset.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                float n = valueNoise(IN.uv * _NoiseScale);
                float threshold = _Dissolve;

                // Remove fragments the dissolve has consumed.
                clip(n - threshold);

                // Glow along the leading edge of the cut.
                float edge = 1.0 - smoothstep(threshold, threshold + _EdgeWidth, n);
                half3 rgb = lerp(baseCol.rgb, _EdgeColor.rgb, saturate(edge));

                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
