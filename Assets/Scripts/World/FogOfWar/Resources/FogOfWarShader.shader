Shader "Unlit/FogOfWar"
{
    // FULL-SCREEN, DEPTH-BASED fog of war (2026-10-16).
    //
    // The fog used to be a terrain-hugging sheet drawn over everything, so
    // every pixel was fogged by the GROUND point the sheet passed through —
    // the top of a tree was fogged as the ground BEHIND it, and as the camera
    // panned the tree tops slid in and out of the fog (parallax). Now one
    // full-screen triangle-pair reads the camera depth texture, rebuilds each
    // pixel's real world position, and looks the fog up THERE: a tree top
    // gets its own trunk's fog, a unit is fogged where it stands.
    //
    // Two sources of "visible":
    //   _MainTex  the CPU grid, 4 Hz, crossfaded prev->current: STATIC sight
    //             (buildings) in R/B, explored in G/A.
    //   _LiveVis  the GPU live layer, redrawn every sim tick from the moving
    //             sighted units — no stamp latency, so the fog edge keeps up
    //             with the unit instead of trailing it by up to half a second.
    //   _LiveRev  the same circles accumulated (never cleared), so ground a
    //             unit just uncovered counts as explored before the next CPU
    //             stamp lands — no black flash behind a fast scout.
    Properties
    {
        _MainTex ("Fog Coverage (static vis, revealed, prev)", 2D) = "black" {}
        _LiveVis ("Live Sight", 2D) = "black" {}
        _LiveRev ("Live Explored", 2D) = "black" {}
        _Tint    ("Tint", Color)   = (0,0,0,1)
        _WorldMin("World Min (x,z)", Vector) = (-125, 0, -125, 0)
        _WorldMax("World Max (x,z)", Vector) = ( 125, 0,  125, 0)
        _ExploredA("Explored Alpha", Range(0,1)) = 0.65
        _HiddenA  ("Hidden Alpha",   Range(0,1)) = 1
        _Blend    ("Prev->Current Blend", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "FogOfWar"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_LiveVis); SAMPLER(sampler_LiveVis);
            TEXTURE2D(_LiveRev); SAMPLER(sampler_LiveRev);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float4 _WorldMin;
                float4 _WorldMax;
                float  _ExploredA;
                float  _HiddenA;
                float  _Blend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            // The mesh is a quad already in clip space (-1..1): no matrices,
            // so it covers the whole screen whatever the camera does.
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = float4(v.positionOS.xy, 0.5, 1.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.positionCS.xy / _ScaledScreenParams.xy;

                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(screenUV);
                    bool sky = depth <= 0.0;
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(screenUV));
                    bool sky = depth >= 1.0;
                #endif
                // Nothing rendered here (the void past the map): no fog, as
                // the old sheet never covered it either.
                if (sky) return half4(0, 0, 0, 0);

                float3 worldPos = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                float2 uv = (worldPos.xz - _WorldMin.xz) / (_WorldMax.xz - _WorldMin.xz);

                // Beyond the fog grid (which already pads the map) is never
                // seen: the same opaque black as unexplored ground.
                if (any(uv < 0.0) || any(uv > 1.0))
                    return half4(_Tint.rgb, _HiddenA);

                // Coverage channels: RG = current (static visible, revealed),
                // BA = the previous push's. Crossfading BEFORE sharpening
                // slides the contour between pushes instead of popping.
                float4 covs = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float2 cov = lerp(covs.ba, covs.rg, _Blend);

                float live    = SAMPLE_TEXTURE2D(_LiveVis, sampler_LiveVis, uv).r;
                float liveRev = SAMPLE_TEXTURE2D(_LiveRev, sampler_LiveRev, uv).r;

                float vis = max(cov.r, live);
                float rev = max(cov.g, max(liveRev, live));

                // Moderate sharpen: tight enough to read as a crisp RTS
                // edge, wide enough not to re-expose texel scallops.
                float visS = smoothstep(0.35, 0.65, vis);
                float revS = smoothstep(0.35, 0.65, rev);

                float a = lerp(lerp(_HiddenA, _ExploredA, revS), 0.0, visS);
                return half4(_Tint.rgb, a);
            }
            ENDHLSL
        }
    }
}
