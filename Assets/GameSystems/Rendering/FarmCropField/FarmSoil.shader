// FarmSoil.shader
// The tilled ground under an Alanthor Farm's crop (docs/Design/Age_1_Alanthor.md
// § The Farm, "The field's look"): a translucent loam tint with concentric
// furrows round the Farm, laid over the terrain on a terrain-following mesh
// FarmCropField builds per Farm. Vertex alpha carries the free ring (0 where
// the ground is blocked, soft at the rim); roads fade it out per pixel from
// _TWB_RoadMask, so a road through the field keeps its own look.
// Transparent: it writes no depth and so needs no depth / shadow passes.

Shader "TheWaningBorder/FarmSoil"
{
    Properties
    {
        _SoilColor     ("Loam", Color)          = (0.25, 0.19, 0.13, 1)
        _FurrowColor   ("Furrow", Color)        = (0.16, 0.12, 0.08, 1)
        _FurrowSpacing ("Furrow spacing", Float) = 0.9
        _Opacity       ("Opacity", Float)       = 0.85
        _FarmCentre    ("Farm centre", Vector)  = (0, 0, 0, 0)
        _RoadCutLow    ("Road cut low", Float)  = 0.25
        _RoadCutHigh   ("Road cut high", Float) = 0.55
        _Tilled        ("Tilled", Range(0,1))   = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent-100"
        }

        Pass
        {
            Name "FORWARD_LIT"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _SoilColor;
            float4 _FurrowColor;
            float  _FurrowSpacing;
            float  _Opacity;
            float4 _FarmCentre;
            float  _RoadCutLow;
            float  _RoadCutHigh;
            float  _Tilled;

            TEXTURE2D(_TWB_RoadMask); SAMPLER(sampler_TWB_RoadMask);
            float4 _TWB_MaskST;
            float  _TWB_RoadEnabled;

            struct Attributes
            {
                float4 positionOS : POSITION;   // world space (identity matrix)
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // a = free ring
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                half   alpha       : TEXCOORD2;
                half   fogFactor   : TEXCOORD3;
            };

            float SoilHash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.alpha       = IN.color.a;
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half road = 0;
                if (_TWB_RoadEnabled > 0.5)
                {
                    float2 uv = IN.positionWS.xz * _TWB_MaskST.xy + _TWB_MaskST.zw;
                    road = smoothstep(_RoadCutLow, _RoadCutHigh,
                        SAMPLE_TEXTURE2D(_TWB_RoadMask, sampler_TWB_RoadMask, uv).r);
                }

                // Concentric furrows round the Farm, wobbling a little.
                float2 d = IN.positionWS.xz - _FarmCentre.xz;
                float r = length(d) + (SoilHash(floor(IN.positionWS.xz * 3.0)) - 0.5) * 0.08;
                float f = 0.5 + 0.5 * cos(r / max(_FurrowSpacing, 0.05) * 6.2831853);
                half3 albedo = lerp(_SoilColor.rgb, _FurrowColor.rgb, f * _Tilled);
                albedo *= 0.92 + 0.16 * SoilHash(floor(IN.positionWS.xz * 7.0));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float3 n = normalize(IN.normalWS);
                half ndl = saturate(dot(n, mainLight.direction));
                half3 lit = albedo * (mainLight.color * ndl * mainLight.shadowAttenuation + SampleSH(n));
                lit = MixFog(lit, IN.fogFactor);

                half a = saturate(IN.alpha) * _Opacity * (1.0 - road);
                return half4(lit, a);
            }
            ENDHLSL
        }
    }
}
