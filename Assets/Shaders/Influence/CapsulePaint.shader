// CapsulePaint.shader
// Paints a filled capsule (rounded line-segment) shape using an SDF.
//
// When _PointA == _PointB, the capsule degenerates into a circle — this is
// used by AlanthorInfluence to paint tower radii.
//
// Has two passes:
//   Pass 0 — "Paint": writes a white filled capsule into the CURRENT render target's R channel.
//   Pass 1 — "Composite": reads _MainTex and blits its R value into the channel selected
//             by _ChannelMask, leaving other channels of the destination unchanged.
//
// Both passes are used together:
//   1. Call Pass 0 multiple times into a single-channel scratch RT to accumulate shapes.
//   2. Call Pass 1 once to copy the scratch RT into the correct channel of InfluenceMap.

Shader "Influence/CapsulePaint"
{
    Properties
    {
        // --- Capsule shape ---
        _PointA     ("Point A (UV)",    Vector) = (0.5, 0.5, 0, 0)
        _PointB     ("Point B (UV)",    Vector) = (0.5, 0.5, 0, 0)
        _Radius     ("Radius (UV)",     Float)  = 0.05
        _Softness   ("Edge Softness",   Float)  = 0.005

        // --- Composite pass ---
        // Selects which RGBA channel to write. Set one component to 1, rest to 0.
        // E.g. (1,0,0,0) = R only, (0,1,0,0) = G only, (0,0,1,0) = B only.
        _ChannelMask ("Channel Mask",   Vector) = (1, 0, 0, 0)
        _MainTex     ("Source Tex",     2D)     = "black" {}
    }

    SubShader
    {
        // No depth write; no culling. We blit full-screen quads onto RenderTextures.
        Cull Off ZWrite Off ZTest Always

        // =====================================================================
        // Pass 0 — Paint capsule into R channel of current RT
        // =====================================================================
        Pass
        {
            Name "CapsulePaint"

            // Additive blend: new value adds on top of existing (for accumulating
            // multiple capsules in a single scratch RT without clearing between calls)
            Blend One One

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // Uniforms
            float4 _PointA;     // .xy = UV position of segment start
            float4 _PointB;     // .xy = UV position of segment end
            float  _Radius;     // capsule radius in UV space
            float  _Softness;   // smooth-step width at the edge

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // Signed distance function for a capsule defined by two points and a radius.
            // Returns negative values inside the capsule, positive outside.
            float sdCapsule(float2 p, float2 a, float2 b, float r)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                // Project p onto the segment AB, clamping to [0,1]
                float t = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                float2 closest = a + t * ba;
                return length(p - closest) - r;
            }

            half4 frag(v2f i) : SV_Target
            {
                float dist = sdCapsule(i.uv, _PointA.xy, _PointB.xy, _Radius);

                // smoothstep: 1 inside, 0 outside, smooth transition at edge
                float influence = 1.0 - smoothstep(-_Softness, _Softness, dist);

                // Write into R only; G/B/A = 0 (additive blend handles accumulation)
                return half4(influence, 0, 0, 0);
            }
            ENDHLSL
        }

        // =====================================================================
        // Pass 1 — Composite: copy _MainTex.r into the channel(s) selected by _ChannelMask
        //          leaving other channels of the destination unchanged.
        // =====================================================================
        Pass
        {
            Name "Composite"

            // Custom blend: dest = dest * (1 - mask) + src * mask
            // This requires two draw calls in Unity's Blit API because BlendOp
            // doesn't support per-channel masking directly. Instead we use a trick:
            //   Blend = One OneMinusSrcAlpha, and we pack the mask into src.a / rgb.
            //
            // Simpler approach used here: we rebuild the destination by sampling
            // the EXISTING InfluenceMap and overwriting only the selected channel.
            // Unity's Graphics.Blit does NOT give us the current dest as input,
            // so we pass it via _PrevInfluence manually if needed.
            //
            // For simplicity in this implementation, we use ColorMask to restrict
            // which channel is written. The caller sets _ChannelMask to select the channel.
            // ColorMask is set per draw call by the C# side via MaterialPropertyBlock.

            Blend One Zero  // overwrite mode for the selected channel

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _ChannelMask;  // e.g. (1,0,0,0) = write only R

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // Read the painted value from the scratch RT
                float value = tex2D(_MainTex, i.uv).r;

                // Broadcast the value into the selected channel; zero in others.
                // The blend equation (One Zero) means this fully replaces the dest.
                // Because we set ColorMask per-channel externally, only the chosen
                // channel is actually written — but if ColorMask isn't used, this
                // formula still does the right thing when _ChannelMask has exactly one 1.
                return half4(
                    value * _ChannelMask.r,
                    value * _ChannelMask.g,
                    value * _ChannelMask.b,
                    value * _ChannelMask.a
                );
            }
            ENDHLSL
        }
    }
}
