Shader "Hidden/TWB/FogLiveSight"
{
    // One sight circle per instance, MAX-blended into the fog's live layer
    // (FogOfWarManager.RenderLiveSight, 2026-10-16). Coverage uses the CPU
    // stamp's own anti-aliased ramp — half coverage exactly at the sight
    // radius, a cell and a half of falloff — so the live and the CPU layers
    // sharpen to the same contour in the fog shader.
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        ZWrite Off
        ZTest Always
        Cull Off
        BlendOp Max
        Blend One One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Cell;   // fog cell size, world metres

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Radius)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 local : TEXCOORD0;   // metres from the circle centre
                float  r     : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                float r = UNITY_ACCESS_INSTANCED_PROP(Props, _Radius);
                // The unit quad (-0.5..0.5) is scaled to the circle's square
                // by the instance matrix; recover metres from the scale.
                float scale = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                o.local = v.vertex.xy * scale;
                o.r = r;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float d = length(i.local);
                float cov = saturate((i.r + 0.75 * _Cell - d) / (1.5 * _Cell));
                return float4(cov, cov, cov, cov);
            }
            ENDHLSL
        }
    }
}
