// Pencil ink outlines — full-screen edge pass driven by PencilOutline.cs.
// Values come from PencilOutline.asset; see PencilOutlineConfig.cs for what each one means.
Shader "Hidden/TWB/PencilOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "PencilOutline"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _InkColor;
            float4 _PencilScreen; // xy = target size in pixels, zw = 1 / size. Set from C#: _ScreenParams is not reliable inside a render-graph raster pass.
            float _LineWidth, _MinLineWidth, _ReferenceDistance;
            float _FadeStart, _FadeEnd;
            float _DepthThreshold, _GrazingCompensation;
            float _NormalThreshold, _NormalWeight;
            float _JitterAmount, _JitterScale, _BoilFps;
            float _StrokeBreakup, _StrokeScale, _Grain, _GrainScale;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }

            // Hash without Sine (Dave Hoskins): stable at world-space magnitudes, no sin() precision loss.
            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // 3D value noise. Keyed on WORLD position so the strokes stay put on the ground and
            // buildings as the camera pans and zooms, instead of sliding with the screen.
            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                            lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
            }

            // Edge samples are BILINEAR, not the point sampler URP defaults to. The
            // cross samples sit half a pixel off centre and the stroke wander moves
            // them by fractions of a pixel; with point sampling each sample snaps
            // between texels as the camera moves, and thin lines pop on and off.
            float RawDepthSmooth(float2 uv) { return SampleSceneDepth(uv, sampler_LinearClamp); }
            float EyeDepth(float2 uv) { return LinearEyeDepth(RawDepthSmooth(uv), _ZBufferParams); }
            float3 NormalSmooth(float2 uv)
            {
                float3 n = SampleSceneNormals(uv, sampler_LinearClamp);
                return n / max(length(n), 1e-4); // blended normals are shorter; the sky's are zero
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 texel = _PencilScreen.zw;

                // The world point under this pixel: every noise lookup below is keyed on it.
                float3 anchorWS = ComputeWorldSpacePosition(input.uv, SampleSceneDepth(input.uv), UNITY_MATRIX_I_VP);

                // "Boil": the whole stroke pattern re-rolls a few times a second, like redrawn frames. 0 = frozen.
                float boil = _BoilFps > 0 ? floor(_Time.y * _BoilFps) : 0;
                float3 boilOffset = float3(Hash31(boil.xxx + 0.17), Hash31(boil.xxx + 3.1), Hash31(boil.xxx + 7.3)) * 97.0;

                // Stroke wander: offset where the edge is sampled, so straight edges draw as a wobbly line.
                float3 wp = anchorWS / max(_JitterScale, 1e-3) + boilOffset;
                float2 wander = float2(ValueNoise3(wp), ValueNoise3(wp + 31.7)) * 2.0 - 1.0;
                float2 uv = input.uv + wander * _JitterAmount * texel;

                float rawC = RawDepthSmooth(uv);
                float dC = LinearEyeDepth(rawC, _ZBufferParams);

                // Lines thin with distance but never below the minimum, and never grow past _LineWidth up close.
                float width = max(_MinLineWidth, _LineWidth * saturate(_ReferenceDistance / dC));
                float2 o = texel * width * 0.5;
                float2 uv0 = uv + float2(-o.x, -o.y);
                float2 uv1 = uv + float2( o.x, -o.y);
                float2 uv2 = uv + float2(-o.x,  o.y);
                float2 uv3 = uv + float2( o.x,  o.y);

                // Roberts cross on depth, relative to the centre depth so it holds at every zoom.
                float d0 = EyeDepth(uv0), d1 = EyeDepth(uv1), d2 = EyeDepth(uv2), d3 = EyeDepth(uv3);
                float depthDiff = (abs(d0 - d3) + abs(d1 - d2)) / max(dC, 1e-3);

                // Surfaces seen edge-on change depth fast without being edges: raise the threshold there.
                float3 nC = NormalSmooth(uv);
                float3 posWS = ComputeWorldSpacePosition(uv, rawC, UNITY_MATRIX_I_VP);
                float nDotV = saturate(dot(nC, normalize(GetCameraPositionWS() - posWS)));
                float threshold = _DepthThreshold * lerp(1.0, 1.0 / max(nDotV, 0.1), _GrazingCompensation);
                float edgeDepth = smoothstep(threshold, threshold * 2.0, depthDiff);

                // Roberts cross on normals: creases inside a silhouette.
                float3 n0 = NormalSmooth(uv0), n1 = NormalSmooth(uv1);
                float3 n2 = NormalSmooth(uv2), n3 = NormalSmooth(uv3);
                float normalDiff = length(n0 - n3) + length(n1 - n2);
                float edgeNormal = smoothstep(_NormalThreshold, _NormalThreshold * 1.5, normalDiff) * _NormalWeight;

                float edge = saturate(max(edgeDepth, edgeNormal));

                // Pencil pressure along the stroke, then graphite grain inside it.
                float pressure = smoothstep(0.15, 0.7, ValueNoise3(anchorWS / max(_StrokeScale, 1e-3) + boilOffset * 1.3));
                edge *= lerp(1.0, pressure, _StrokeBreakup);
                edge *= lerp(1.0, ValueNoise3(anchorWS / max(_GrainScale, 1e-3) + boilOffset * 1.7), _Grain);

                edge *= 1.0 - smoothstep(_FadeStart, _FadeEnd, dC);

                return half4(_InkColor.rgb, edge * _InkColor.a);
            }
            ENDHLSL
        }
    }
}
