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
        _Blend    ("Prev->Current Blend", Range(0,1)) = 1
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
            float  _Blend;

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

                // Coverage channels (2026-09-03): RG = current (visible,
                // revealed) coverage, BA = the previous push's — both
                // anti-aliased at the stamp so the half-coverage contour
                // follows the true circle rather than the cell staircase.
                // Each edge is sharpened on its OWN channel; deriving both
                // bands from one alpha ramp made the ramp pass through the
                // explored plateau on its way from clear to black — a
                // synthesized "explored" sliver ringing every visible circle
                // that borders unexplored ground.
                //
                // The prev->current crossfade BEFORE sharpening is what makes
                // 4 Hz stamp data read as continuous motion: coverage is a
                // distance-like field, so lerping two circle fields slides
                // the sharpened contour smoothly between the two positions
                // instead of popping.
                float4 covs = tex2D(_MainTex, uv);
                float2 cov = lerp(covs.ba, covs.rg, _Blend);

                // Moderate sharpen: tight enough to read as a crisp RTS
                // edge, wide enough not to re-expose texel scallops.
                float visS = smoothstep(0.35, 0.65, cov.r);
                float revS = smoothstep(0.35, 0.65, cov.g);

                float a = lerp(lerp(_HiddenA, _ExploredA, revS), 0.0, visS);

                return fixed4(_Tint.rgb, a);
            }
            ENDCG
        }
    }
}
