Shader "Unlit/FogOfWar"
{
    Properties
    {
        _MainTex ("Fog Alpha", 2D) = "white" {}
        _Tint    ("Tint", Color)   = (0,0,0,1)
        _WorldMin("World Min (x,z)", Vector) = (-125, 0, -125, 0)
        _WorldMax("World Max (x,z)", Vector) = ( 125, 0,  125, 0)
        _Softness("Edge Softness (texels)", Range(0,2)) = 1
        _ExploredA("Explored Alpha", Range(0,1)) = 0.65
        _HiddenA  ("Hidden Alpha",   Range(0,1)) = 1
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
            float  _ExploredA;
            float  _HiddenA;

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

                // BILINEAR sample + band re-sharpen (2026-08-31). The old
                // path point-sampled the grid and blurred it with a 3x3
                // kernel, which softened the corners but left every 1 m fog
                // texel readable as a square. Bilinear filtering gives a
                // smooth sub-texel gradient between the three plateaus
                // (visible 0, explored, hidden); the smoothsteps below
                // steepen each transition back to a crisp band boundary
                // without ever exposing the texel grid — higher apparent
                // resolution from the same grid, at ONE tap instead of nine.
                float a = tex2D(_MainTex, uv).a;

                float e = _ExploredA;
                float h = max(_HiddenA, e + 0.001);

                // visible -> explored transition, then explored -> hidden.
                // Windows sit at the middle half of each band, so plateau
                // values map exactly to themselves.
                float b1 = smoothstep(0.25 * e, 0.75 * e, a);
                float b2 = smoothstep(e + 0.25 * (h - e), e + 0.75 * (h - e), a);
                a = b1 * e + b2 * (h - e);

                return fixed4(_Tint.rgb, a);
            }
            ENDCG
        }
    }
}
