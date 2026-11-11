// FarmCrop.shader
// The cereal on an Alanthor Farm's crop ring (docs/Design/Age_1_Alanthor.md
// § The Farm, "The field's look"). Drawn by FarmCropField with
// Graphics.RenderMeshPrimitives: one tuft mesh (a few stalks with ears and
// leaves) instanced over the ring, every instance read from _CropInstances
// by SV_InstanceID. No per-object matrix is used — the instance buffer holds
// world positions.
//
// The vertex stage does all the work:
//   * growth — _Growth (0 sprouts .. 1 ripe) sets the stalks' height, the
//     tuft's spread and the ears' size; the fragment ripens the colour.
//   * roads — the instance root samples _TWB_RoadMask (RoadNetwork's global,
//     same _TWB_MaskST as the terrain overlays) and the stalks shrink to
//     nothing on a road, shorter on its trampled rim, so roads stay visible.
//   * wind — a slow travelling sway plus gusts, bending with height².
//
// Every colour and tunable is set from FarmCropField.asset each frame; the
// property defaults below are only what the material shows before that.
// Has its own ShadowCaster / DepthOnly / DepthNormals passes — URP takes
// none from a Fallback (memory: urp-fallback-passes).

Shader "TheWaningBorder/FarmCrop"
{
    Properties
    {
        _ColorBase      ("Stalk foot", Color)        = (0.17, 0.24, 0.10, 1)
        _ColorYoung     ("Young green", Color)       = (0.36, 0.50, 0.20, 1)
        _ColorStalkRipe ("Ripe straw", Color)        = (0.60, 0.53, 0.33, 1)
        _ColorEarYoung  ("Young ear", Color)         = (0.44, 0.54, 0.24, 1)
        _ColorEarRipe   ("Ripe ear", Color)          = (0.70, 0.56, 0.30, 1)
        _ColorVariation ("Colour variation", Float)  = 0.12
        _Growth         ("Growth", Range(0,1))       = 1
        _SproutHeight   ("Sprout height", Float)     = 0.12
        _EarAppear      ("Ears appear at", Float)    = 0.45
        _RipenStart     ("Ripen from", Float)        = 0.6
        _Translucency   ("Translucency", Float)      = 0.35
        _Smoothness     ("Smoothness", Float)        = 0.18
        _FootOcclusion  ("Foot occlusion", Float)    = 0.4
        _WindStrength   ("Wind strength", Float)     = 0.12
        _WindSpeed      ("Wind speed", Float)        = 1.3
        _WindScale      ("Wind scale", Float)        = 0.08
        _GustStrength   ("Gust strength", Float)     = 0.1
        _WindDir        ("Wind direction", Vector)   = (0.8, 0, 0.6, 0)
        _RoadCutLow     ("Road cut low", Float)      = 0.25
        _RoadCutHigh    ("Road cut high", Float)     = 0.55
        _NormalUp       ("Normal up-bias", Float)    = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct CropInstance
        {
            float4 posHeight;   // xyz root on the terrain, w stalk height (m)
            float4 extra;       // x yaw (rad), y seed 0..1, z edge thinning 0..1, w unused
        };
        StructuredBuffer<CropInstance> _CropInstances;

        // Per farm (MaterialPropertyBlock) and per material (the config).
        float  _Growth;
        float  _SproutHeight;
        float  _EarAppear;
        float  _WindStrength;
        float  _WindSpeed;
        float  _WindScale;
        float  _GustStrength;
        float4 _WindDir;
        float  _RoadCutLow;
        float  _RoadCutHigh;
        float  _NormalUp;

        // RoadNetwork / InfluenceMaskTexture globals (Roads.md §7).
        TEXTURE2D(_TWB_RoadMask); SAMPLER(sampler_TWB_RoadMask);
        float4 _TWB_MaskST;
        float  _TWB_RoadEnabled;

        struct CropAttributes
        {
            float4 positionOS : POSITION;   // tuft-local, metres at height 1
            float3 normalOS   : NORMAL;
            float4 uv0        : TEXCOORD0;  // x height along the tuft 0..1, y part (0 stalk, 1 ear, 2 leaf)
            float3 earOffset  : TEXCOORD1;  // ear vertices: offset from the ear's base
            uint   instanceID : SV_InstanceID;
        };

        struct CropVertex
        {
            float3 positionWS;
            float3 normalWS;
            float  t;
            float  part;
            float  seed;
            float  growth;
        };

        float CropHash(float2 p)
        {
            p = frac(p * float2(0.1031, 0.1030));
            p += dot(p, p.yx + 33.33);
            return frac((p.x + p.y) * p.x);
        }

        float CropNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(CropHash(i), CropHash(i + float2(1, 0)), f.x),
                        lerp(CropHash(i + float2(0, 1)), CropHash(i + float2(1, 1)), f.x), f.y);
        }

        float2 Rot(float2 v, float s, float c) { return float2(v.x * c - v.y * s, v.x * s + v.y * c); }

        CropVertex CropTransform(CropAttributes IN)
        {
            CropInstance inst = _CropInstances[IN.instanceID];
            float3 root = inst.posHeight.xyz;
            float  seed = inst.extra.y;

            // Growth with a little per-tuft spread, so the field is not one height.
            float g = saturate(_Growth * (0.88 + 0.24 * seed));
            float heightK = lerp(_SproutHeight, 1.0, smoothstep(0.0, 0.85, g));
            float widthK  = lerp(0.45, 1.0, smoothstep(0.0, 0.6, g));
            float earK    = smoothstep(_EarAppear, saturate(_EarAppear + 0.3), g);

            // Roads cut through: nothing on the road, shorter on its rim.
            float cut = 1.0;
            if (_TWB_RoadEnabled > 0.5)
            {
                float2 uv = root.xz * _TWB_MaskST.xy + _TWB_MaskST.zw;
                float road = SAMPLE_TEXTURE2D_LOD(_TWB_RoadMask, sampler_TWB_RoadMask, uv, 0).r;
                cut = 1.0 - smoothstep(_RoadCutLow, _RoadCutHigh, road);
            }
            // The ring's outer edge thins out instead of stopping at a circle.
            cut *= 1.0 - inst.extra.z;

            float h = inst.posHeight.w * heightK * cut;
            float w = widthK * saturate(cut * 3.0);

            float s, c;
            sincos(inst.extra.x, s, c);

            float3 lp = IN.positionOS.xyz;
            float2 xz = Rot(lp.xz, s, c) * w;
            float  y  = lp.y * h;
            float3 ear = IN.earOffset;
            ear.xz = Rot(ear.xz, s, c);
            ear *= earK * saturate(cut * 3.0);

            float3 local = float3(xz.x, y, xz.y) + ear;

            // Wind: a travelling wave across the field plus slower gusts,
            // bending with height squared so the foot stays planted.
            float t = IN.uv0.x;
            float2 wdir = normalize(_WindDir.xz + 1e-4);
            float phase = dot(root.xz, wdir) * _WindScale * 6.2831853 - _Time.y * _WindSpeed + seed * 1.7;
            float gust = CropNoise(root.xz * 0.035 - wdir * _Time.y * 0.25);
            float sway = sin(phase) * _WindStrength + (gust - 0.35) * _GustStrength;
            float bend = t * t * max(h, 0.0);
            local.xz += wdir * sway * bend + float2(-wdir.y, wdir.x) * sin(phase * 1.7 + seed * 5.0) * _WindStrength * 0.35 * bend;
            local.y  -= abs(sway) * bend * 0.25;

            float3 n = IN.normalOS;
            n.xz = Rot(n.xz, s, c);
            n = normalize(lerp(n, float3(0, 1, 0), _NormalUp));

            CropVertex o;
            o.positionWS = root + local;
            o.normalWS   = n;
            o.t          = t;
            o.part       = IN.uv0.y;
            o.seed       = seed;
            o.growth     = g;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "FORWARD_LIT"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _ColorBase;
            float4 _ColorYoung;
            float4 _ColorStalkRipe;
            float4 _ColorEarYoung;
            float4 _ColorEarRipe;
            float  _ColorVariation;
            float  _RipenStart;
            float  _Translucency;
            float  _Smoothness;
            float  _FootOcclusion;

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 data        : TEXCOORD2;   // t, part, seed, growth
                half   fogFactor   : TEXCOORD3;
                half3  vertexSH    : TEXCOORD4;
            };

            Varyings vert(CropAttributes IN)
            {
                CropVertex v = CropTransform(IN);
                Varyings OUT;
                OUT.positionWS  = v.positionWS;
                OUT.normalWS    = v.normalWS;
                OUT.positionHCS = TransformWorldToHClip(v.positionWS);
                OUT.data        = float4(v.t, v.part, v.seed, v.growth);
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                OUT.vertexSH    = SampleSH(v.normalWS);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float t = IN.data.x, part = IN.data.y, seed = IN.data.z, g = IN.data.w;

                // Ripening runs unevenly: low-frequency patches lead or lag.
                float patch = CropNoise(IN.positionWS.xz * 0.12);
                float ripe = saturate((g - _RipenStart) / max(1.0 - _RipenStart, 1e-3) + (patch - 0.5) * 0.45);

                // The foot darkens toward green-brown straw as the crop ripens,
                // so a ripe field reads gold from above, not dark green.
                half3 foot = lerp(_ColorBase.rgb, _ColorStalkRipe.rgb * 0.55, ripe);
                half3 stalkTop = lerp(_ColorYoung.rgb, _ColorStalkRipe.rgb, ripe);
                half3 albedo;
                if (part > 1.5)        // leaf: stays greener, dries at the tip
                    albedo = lerp(foot, lerp(_ColorYoung.rgb, _ColorStalkRipe.rgb * 0.85, ripe * 0.8), saturate(t * 1.6));
                else if (part > 0.5)   // ear
                    albedo = lerp(_ColorEarYoung.rgb, _ColorEarRipe.rgb, ripe);
                else                   // stalk
                    albedo = lerp(foot, stalkTop, smoothstep(0.0, 0.5, t));

                // Per-tuft brightness and a touch of hue drift.
                float v = (seed - 0.5) * 2.0 * _ColorVariation;
                albedo *= 1.0 + v;
                albedo = lerp(albedo, albedo * half3(1.04, 1.0, 0.9), saturate(patch - 0.4));

                float3 normalWS = normalize(isFront ? IN.normalWS : lerp(-IN.normalWS, float3(0, 1, 0), 0.5));
                half occlusion = lerp(_FootOcclusion, 1.0, saturate(t * 1.3));

                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.positionWS;
                inputData.normalWS                = normalWS;
                inputData.viewDirectionWS         = normalize(GetCameraPositionWS() - IN.positionWS);
                inputData.shadowCoord             = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord                = IN.fogFactor;
                inputData.bakedGI                 = IN.vertexSH;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo;
                surfaceData.metallic   = 0;
                surfaceData.smoothness = part > 0.5 && part < 1.5 ? _Smoothness * 1.5 : _Smoothness;
                surfaceData.normalTS   = half3(0, 0, 1);
                surfaceData.occlusion  = occlusion;
                surfaceData.alpha      = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Light through the blades when the sun is behind them.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half back = saturate(dot(-mainLight.direction, inputData.viewDirectionWS));
                color.rgb += albedo * mainLight.color * (back * back) * _Translucency
                           * mainLight.shadowAttenuation * saturate(t + 0.2);

                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(CropAttributes IN) : SV_POSITION
            {
                CropVertex v = CropTransform(IN);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - v.positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(v.positionWS, v.normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(CropAttributes IN) : SV_POSITION
            {
                return TransformWorldToHClip(CropTransform(IN).positionWS);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // Normals for SSAO and the pencil ink. Biased hard toward up: a field
        // reads as one surface, so the ink does not scribble every stalk.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            Varyings vert(CropAttributes IN)
            {
                CropVertex v = CropTransform(IN);
                Varyings OUT;
                OUT.positionHCS = TransformWorldToHClip(v.positionWS);
                OUT.normalWS    = normalize(lerp(v.normalWS, float3(0, 1, 0), 0.75));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
