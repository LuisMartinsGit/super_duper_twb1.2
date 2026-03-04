Shader "Unlit/FogOfWar"
{
    Properties
    {
        _MainTex ("Fog Alpha", 2D) = "white" {}
        _Tint    ("Tint", Color)   = (0,0,0,1)
        _WorldMin("World Min (x,z)", Vector) = (-125, 0, -125, 0)
        _WorldMax("World Max (x,z)", Vector) = ( 125, 0,  125, 0)
        _Softness("Edge Softness (texels)", Range(0,2)) = 1
    }
    SubShader
    {
        // Draw on top of everything
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;     // x=1/width, y=1/height
            float4 _Tint;
            float4 _WorldMin;
            float4 _WorldMax;
            float  _Softness;

            struct v2f {
                float4 pos  : SV_POSITION;
                float3 wpos : TEXCOORD0;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos  = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // world -> uv
                float2 worldXZ = float2(i.wpos.x, i.wpos.z);
                float2 uv = (worldXZ - _WorldMin.xz) / (_WorldMax.xz - _WorldMin.xz);
                uv = saturate(uv);

                // 3x3 kernel in texture space (small and cheap)
                float2 texel = _MainTex_TexelSize.xy * max(_Softness, 0.0);
                float a = 0.0;

                // simple Gaussian-ish weights
                const float wC = 4.0;
                const float wA = 2.0; // axis neighbors
                const float wD = 1.0; // diagonal neighbors
                float wSum = wC + 4.0*wA + 4.0*wD;

                a += tex2D(_MainTex, uv                 ).a * wC;

                a += tex2D(_MainTex, uv + float2( texel.x, 0) ).a * wA;
                a += tex2D(_MainTex, uv + float2(-texel.x, 0) ).a * wA;
                a += tex2D(_MainTex, uv + float2(0,  texel.y) ).a * wA;
                a += tex2D(_MainTex, uv + float2(0, -texel.y) ).a * wA;

                a += tex2D(_MainTex, uv +  texel               ).a * wD;
                a += tex2D(_MainTex, uv + float2(-texel.x, texel.y)).a * wD;
                a += tex2D(_MainTex, uv + float2( texel.x,-texel.y)).a * wD;
                a += tex2D(_MainTex, uv -  texel               ).a * wD;

                a /= wSum;

                return fixed4(_Tint.rgb, a);
            }
            ENDCG
        }
    }
}
