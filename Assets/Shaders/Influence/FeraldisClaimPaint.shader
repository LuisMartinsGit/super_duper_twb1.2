// FeraldisClaimPaint.shader
// Converts accumulated blood into Feraldis territory.
//
// Pass 0 — "Claim":
//   For each texel within _ClaimRadius of _Center, checks if the corresponding
//   BloodMap value is >= _Threshold. If so, outputs 1 into the scratch RT.
//
// Pass 1 — "Composite":
//   Reads the scratch RT (_MainTex) and writes its value into the B channel
//   of the InfluenceMap, leaving R, G, A untouched.
//
// The optional _ClearBlood flag is handled on the C# side by a second blit
// (subtracting the claimed region from BloodMap) — not in this shader.

Shader "Influence/FeraldisClaimPaint"
{
    Properties
    {
        // --- Claim pass ---
        _Center      ("Totem UV Position",  Vector) = (0.5, 0.5, 0, 0)
        _ClaimRadius ("Claim Radius (UV)",  Float)  = 0.1
        _Threshold   ("Blood Threshold",    Float)  = 0.1
        _BloodMap    ("Blood Map",          2D)     = "black" {}

        // --- Composite pass ---
        _ChannelMask ("Channel Mask",       Vector) = (0, 0, 1, 0) // default: B channel
        _MainTex     ("Source Tex",         2D)     = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // =====================================================================
        // Pass 0 — Claim: paint territory where blood is sufficient
        // =====================================================================
        Pass
        {
            Name "Claim"

            // Additive so overlapping totems don't fight each other
            Blend One One

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4    _Center;
            float     _ClaimRadius;
            float     _Threshold;
            sampler2D _BloodMap;

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
                // Check 1: is this texel inside the totem's claim radius?
                float dist     = length(i.uv - _Center.xy);
                float inRadius = step(dist, _ClaimRadius); // 1 if inside, 0 outside

                // Check 2: does this texel have enough blood?
                float blood    = tex2D(_BloodMap, i.uv).r;
                float hasBloood = step(_Threshold, blood); // 1 if blood >= threshold

                // Territory = inside radius AND has blood
                float territory = inRadius * hasBloood;

                // Soft edge at the claim boundary to avoid a hard circle outline
                float softness   = _ClaimRadius * 0.05;
                float softEdge   = 1.0 - smoothstep(_ClaimRadius - softness, _ClaimRadius, dist);
                territory *= softEdge;

                // Write to R of scratch RT (composite pass will route it to B of InfluenceMap)
                return half4(territory, 0, 0, 0);
            }
            ENDHLSL
        }

        // =====================================================================
        // Pass 1 — Composite: route scratch R into the target channel of InfluenceMap
        // =====================================================================
        Pass
        {
            Name "Composite"

            Blend One Zero

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _ChannelMask;

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
                float value = tex2D(_MainTex, i.uv).r;
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
