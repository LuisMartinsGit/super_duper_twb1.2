// Yellow grass with vertex-shader wind sway, designed for the
// GathererHutGrassPainter combined-mesh tufts.
//
// The cross-quad mesh built by GathererHutGrassPainter has uv.y = 0
// at each blade's base and uv.y = 1 at its tip. This shader sways the
// world XZ position by a sin/cos pair scaled by uv.y so the base stays
// planted while the tip drifts. The phase of the sway is keyed off the
// world XZ position itself so neighbouring tufts wave out of phase
// (wind looks organic, not synchronized).
//
// Single forward pass, alpha-clip, no shadows — grass tufts are too
// small / thin for shadow casts to be worth the cost in this RTS.
// SRP Batcher compatible (all material props in one UnityPerMaterial CBUFFER).

Shader "TheWaningBorder/YellowGrass"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 0.95, 0.55, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.4

        _WindStrength("Wind Strength (m)", Range(0, 1)) = 0.18
        _WindSpeed("Wind Speed (Hz)", Range(0, 6)) = 1.4
        _WindFreq("Wind Spatial Freq", Range(0, 1)) = 0.25
        _GustStrength("Gust Strength (m)", Range(0, 1)) = 0.08
        _GustSpeed("Gust Speed", Range(0, 2)) = 0.35

        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue"      = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull   [_Cull]
            ZWrite On
            ZTest  LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _WindStrength;
                half   _WindSpeed;
                half   _WindFreq;
                half   _GustStrength;
                half   _GustSpeed;
                float  _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);

                // Local sway: each blade's tip drifts on a per-position phase.
                float t     = _Time.y * _WindSpeed;
                float phase = (posWS.x + posWS.z) * _WindFreq;
                float2 swayLocal;
                swayLocal.x = sin(t + phase)            * _WindStrength;
                swayLocal.y = cos(t * 0.7 + phase * 1.3) * _WindStrength * 0.6;

                // Slow large-scale gust drifting across the field — pushes
                // every tuft in roughly the same direction with a long
                // wavelength, so the disc reads as one wind body.
                float gustT = _Time.y * _GustSpeed;
                float2 gust;
                gust.x = sin(gustT + posWS.x * 0.05) * _GustStrength;
                gust.y = cos(gustT * 0.8 + posWS.z * 0.05) * _GustStrength * 0.6;

                // uv.y in [0,1] gates the sway to the upper part of the
                // blade. Squared so the base is rock-steady and the tip
                // moves more than the middle (looks like real bending).
                float bend = IN.uv.y * IN.uv.y;
                posWS.xz += (swayLocal + gust) * bend;

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 col = tex * _BaseColor;
                clip(col.a - _Cutoff);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
