// GroundTargetRing.shader
// BFME2-style ability ground-targeting ring: bright outer ring, soft inner
// fill, a rotating dashed inner ring, and a gentle pulse. Additive blend,
// Overlay queue + ZTest Always so it reads on top of terrain AND the
// fog-of-war overlay. Pure procedural (UV distance) — one quad, any radius.
Shader "TWB/GroundTargetRing"
{
    Properties
    {
        _Color ("Color", Color) = (1.0, 0.85, 0.35, 1.0)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2.0 - 1.0;   // centered [-1, 1]
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r = length(i.uv);
                if (r > 1.0) discard;

                float pulse = 0.85 + 0.15 * sin(_Time.y * 3.2);

                // Crisp outer ring.
                float ring = smoothstep(0.90, 0.945, r) * (1.0 - smoothstep(0.965, 1.0, r));

                // Rotating dashed inner ring.
                float ang = atan2(i.uv.y, i.uv.x) + _Time.y * 0.7;
                float dash = step(0.5, frac(ang * 3.8197)); // 12 dashes / 2*pi
                float dashRing = smoothstep(0.76, 0.79, r) * (1.0 - smoothstep(0.83, 0.86, r)) * dash * 0.8;

                // Soft radial fill, brighter toward the rim.
                float fill = saturate(r * r) * 0.10;

                // Center dot for the exact cast point.
                float dot0 = 1.0 - smoothstep(0.02, 0.05, r);

                float a = (ring + dashRing + fill + dot0 * 0.6) * pulse;
                return fixed4(_Color.rgb, a * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
