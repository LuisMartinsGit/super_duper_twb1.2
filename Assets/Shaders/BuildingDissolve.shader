// BuildingDissolve.shader
// Wave-driven dissolve / reveal for the level-up swap. The same material is
// applied to both the OLD building (Inverted=0 — eaten away from the base up)
// and the NEW building (Inverted=1 — revealed from the base up). A glowing
// edge band travels with the wave front so it reads as a wave of energy
// rolling up the silhouette.
//
// Unlit on purpose — the transition only runs for ~1.5s, and skipping
// lighting keeps the shader independent of any URP version. The original
// material's _BaseColor / _Color and _BaseMap / _MainTex are copied onto the
// instance at runtime by BuildingDissolveTransition.

Shader "TheWaningBorder/BuildingDissolve"
{
    Properties
    {
        _BaseMap          ("Base Map", 2D)            = "white" {}
        _BaseColor        ("Base Color", Color)       = (1,1,1,1)

        _DissolveAmount   ("Dissolve Amount", Range(0,1)) = 0
        _Inverted         ("Inverted (1 = reveal)", Range(0,1)) = 0

        _WaveOriginY      ("Wave Origin Y", Float)    = 0
        _WaveSpan         ("Wave Span (height)", Float) = 5
        _NoiseScale       ("Noise Scale", Float)      = 6
        _NoiseStrength    ("Noise Strength", Range(0,1)) = 0.30

        _EdgeWidth        ("Edge Width", Range(0,0.5)) = 0.10
        [HDR]_EdgeColor   ("Edge Color (HDR)", Color) = (3.0, 1.8, 0.5, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "DISSOLVE_UNLIT"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _DissolveAmount;
                float  _Inverted;
                float  _WaveOriginY;
                float  _WaveSpan;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _EdgeWidth;
                float4 _EdgeColor;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // Fast value-noise on world-space position so the wave edge is
            // organic rather than a hard horizontal slice.
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
                float n000 = Hash13(i + float3(0, 0, 0));
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));
                return lerp(
                    lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                    f.z);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv          = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                OUT.worldPos    = wp;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Vertical sweep normalised to [0..1] across the building.
                // Pad the amount slightly so the wave fully clears top/bottom.
                float vert01 = saturate((IN.worldPos.y - _WaveOriginY) / max(_WaveSpan, 0.001));

                // Surface noise pushes the wave front around so edges look like
                // ripples climbing the wall instead of a flat plane.
                float n = ValueNoise(IN.worldPos * _NoiseScale);
                float threshold = vert01 + (n - 0.5) * _NoiseStrength;

                // Pad the amount slightly outside [0..1] so the wave fully
                // enters and exits, leaving no rim hairline at the extremes.
                float amount = lerp(-_EdgeWidth, 1.0 + _EdgeWidth, _DissolveAmount);

                // OLD mesh (Inverted=0): pixels BELOW the wave are gone.
                // NEW mesh (Inverted=1): pixels ABOVE the wave are gone.
                float diff = (_Inverted > 0.5) ? (threshold - amount) : (amount - threshold);
                if (diff > 0) discard;

                // Glow band hugging the wave front on the visible side.
                float edgeT = saturate((_EdgeWidth - abs(diff)) / max(_EdgeWidth, 0.001));

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half3 col    = lerp(albedo.rgb, _EdgeColor.rgb, edgeT);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
