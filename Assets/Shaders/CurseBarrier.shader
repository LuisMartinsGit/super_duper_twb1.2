// CurseBarrier.shader
// The veil at the edge of cursed ground (docs/Design/Art_Direction.md §6.4):
// a translucent veil along the curse territory's boundary that reads as
// aurora rising from the ground: drifting curtains of brightness along the
// line, vertical rays streaming up, and bursts that leave the feet and
// climb, in emissive dark cyan and purple. Additive, unlit, double-sided, no depth write.
// UV.x is arc length in METRES (CurseBarrierVfx builds the ribbon that
// way), UV.y is 0 at the ground and 1 at the top.
Shader "TWB/CurseBarrier"
{
    Properties
    {
        _NoiseTex     ("Body noise", 2D) = "gray" {}
        _WispTex      ("Wisp noise", 2D) = "gray" {}
        [HDR] _ColorCyan   ("Cyan",   Color) = (0.05, 0.55, 0.62, 1)
        [HDR] _ColorPurple ("Purple", Color) = (0.55, 0.20, 0.95, 1)
        _Scroll       ("x column drift, y burst rate, w ray rise", Vector) = (0.02, 0.22, 0, 0.35)
        _Tiling       ("Tiling: u per metre, v", Vector) = (0.10, 1.0, 0, 0)
        _BodyOpacity  ("Body", Range(0, 1)) = 0.14
        _WispIntensity("Wisps", Range(0, 6)) = 1.6
        _WispSharpness("Wisp sharpness", Range(0.5, 4)) = 2.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_WispTex);  SAMPLER(sampler_WispTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _NoiseTex_ST;
            float4 _ColorCyan;
            float4 _ColorPurple;
            float4 _Scroll;
            float4 _Tiling;
            float  _BodyOpacity;
            float  _WispIntensity;
            float  _WispSharpness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 wpos = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wpos);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u = i.uv.x * _Tiling.x;       // along the boundary, cycles
                float v = saturate(i.uv.y);         // 0 at the feet, 1 at the peak
                float t = _Time.y;

                // Aurora CURTAINS: brightness varies along the line in slow,
                // drifting columns, so the veil breathes instead of glowing evenly.
                float columns = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 0.35 + t * _Scroll.x, 0.37)).r;
                columns = smoothstep(0.2, 0.85, columns);

                // Vertical RAYS streaming upward: sampled fine along the line and
                // coarse up it (tall thin streaks), scrolled so they climb.
                float rays = SAMPLE_TEXTURE2D(_WispTex, sampler_WispTex, float2(u * 1.6, v * 0.45 - t * _Scroll.w)).r;
                rays *= SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 3.1 + t * 0.02, v * 0.3 - t * _Scroll.w * 0.6)).r * 1.6;
                rays = pow(saturate(rays * 1.8), _WispSharpness);

                // BURSTS from the ground: a bright band leaves the feet and climbs,
                // each column on its own phase, dimming as it rises.
                float phase = frac(t * _Scroll.y + columns * 0.7 + u * 0.05);
                float burst = exp(-pow((v - phase) / 0.22, 2.0)) * (1.0 - phase * 0.6);

                // Rooted: the body sits at the feet; rays and bursts reach higher.
                float body  = pow(1.0 - v, 2.5) * smoothstep(0.0, 0.05, v);
                float reach = pow(saturate(1.0 - v * 0.8), 1.3);

                float3 col = lerp(_ColorCyan.rgb, _ColorPurple.rgb,
                                  saturate(rays * 0.6 + columns * 0.5 + burst * 0.3 - 0.1));
                float3 outc = col * (body * _BodyOpacity * (0.5 + columns)
                                   + (rays * reach + burst * 0.5) * columns * _WispIntensity);
                return half4(outc, 1.0h);
            }
            ENDHLSL
        }
    }
}
