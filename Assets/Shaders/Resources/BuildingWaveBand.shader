// BuildingWaveBand.shader
// Additive overlay for the level-up wave. Drawn on a duplicate of the
// building's geometry — sits on top of the original PBR-lit mesh and emits
// only at the wave front + a fading trail behind it. The original materials
// are never touched, so the building keeps its normal lighting throughout.
//
// Rendering: transparent + additive (Blend SrcAlpha One). No depth write,
// equal depth test so we hug the surface beneath without z-fighting.

Shader "TheWaningBorder/BuildingWaveBand"
{
    Properties
    {
        _DissolveAmount ("Dissolve Amount", Range(0,1)) = 0
        _WaveOriginY    ("Wave Origin Y",   Float)      = 0
        _WaveSpan       ("Wave Span",       Float)      = 5

        _NoiseScale     ("Noise Scale",     Float)      = 4.5
        _NoiseStrength  ("Noise Strength",  Range(0,1)) = 0.30

        _BandWidth      ("Band Width",      Range(0, 0.5)) = 0.06
        _TrailLength    ("Trail Length",    Range(0, 0.6)) = 0.18
        _Intensity      ("Intensity",       Range(0, 4))   = 1.0

        [HDR]_EdgeColor ("Edge Color (HDR)", Color)        = (1.0, 0.6, 0.2, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+10"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "WAVE_BAND"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _DissolveAmount;
                float  _WaveOriginY;
                float  _WaveSpan;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _BandWidth;
                float  _TrailLength;
                float  _Intensity;
                float4 _EdgeColor;
            CBUFFER_END

            // Hash + 3D value noise — same form as the dissolve shader so the
            // band wobble matches the previous look.
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

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.worldPos    = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Vertical position normalised across the building height,
                // wobbled by 3D noise so the wave reads as ripples climbing
                // the silhouette rather than a flat plane.
                float vert01    = saturate((IN.worldPos.y - _WaveOriginY) / max(_WaveSpan, 0.001));
                float n         = ValueNoise(IN.worldPos * _NoiseScale);
                float threshold = vert01 + (n - 0.5) * _NoiseStrength;

                // Pad the amount slightly so the wave fully exits at top/bottom.
                float amount = lerp(-_BandWidth, 1.0 + _BandWidth, _DissolveAmount);

                // distAhead > 0  : pixel is above the wave front (hasn't been
                //                  reached yet) → no glow
                // distAhead == 0 : pixel is at the wave → peak glow
                // distAhead < 0  : pixel is behind the wave → trail glow
                //                  (fades over _TrailLength)
                float distAhead = threshold - amount;

                float a;
                if (distAhead > _BandWidth)
                {
                    a = 0.0;
                }
                else if (distAhead > 0.0)
                {
                    // Rising edge — quick ramp into the band
                    a = saturate((_BandWidth - distAhead) / max(_BandWidth, 0.001));
                }
                else
                {
                    // Trail — fades over _TrailLength behind the wave
                    a = saturate(1.0 + distAhead / max(_TrailLength, 0.001));
                    // Soften the falloff so the trail tapers rather than line-segments out.
                    a = a * a;
                }

                a *= _Intensity;
                if (a <= 0.001) discard;

                return half4(_EdgeColor.rgb * a, a * _EdgeColor.a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
