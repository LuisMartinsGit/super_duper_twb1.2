// Crystal.shader
// Translucent crystal material ported from a Blender Principled BSDF setup.
//
// Visual recipe (Blender source):
//   - Principled BSDF: Transmission 0.91, IOR 1.45, Roughness 0, Metallic 0
//   - Base color:  orange (configurable via _Color)
//   - Bump chain:  Noise(1.7) -> ColorRamp -> Bump(0.1) -> Voronoi(3.5) -> Bump(0.06)
//   - Displacement: Noise(7.0) -> ColorRamp -> vertex offset (scale 0.1)
//   - Emission:    same as base color, strength 1.0
//
// Built-in RP, forward rendering, transparent queue.

Shader "Custom/Crystal"
{
    Properties
    {
        [Header(Color)]
        _Color          ("Base Color",        Color)          = (1.0, 0.55, 0.0, 1.0)
        _Opacity        ("Opacity (1-Transmission)", Range(0,1)) = 0.09
        _EmissionStr    ("Emission Strength",  Range(0,4))     = 1.0

        [Header(Refraction)]
        _IOR            ("Index of Refraction", Range(1,2.5))  = 1.45
        _ChromaShift    ("Chromatic Aberration", Range(0,0.05)) = 0.01

        [Header(Surface Noise Bump)]
        _NoiseScale1    ("Noise Scale",        Float)          = 1.7
        _BumpStr1       ("Bump Strength",      Range(0,1))     = 0.1
        _RampPos1       ("ColorRamp Midpoint", Range(0,1))     = 0.337

        [Header(Voronoi Bump)]
        _VoronoiScale   ("Voronoi Scale",      Float)          = 3.5
        _BumpStr2       ("Bump Strength",      Range(0,1))     = 0.06

        [Header(Displacement)]
        _NoiseScale2    ("Displacement Noise Scale", Float)    = 7.0
        _RampPos2       ("Displacement Ramp Midpoint",Range(0,1))= 0.469
        _DispScale      ("Displacement Scale", Range(0,0.5))   = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CRYSTAL_FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            half   _Opacity;
            half   _EmissionStr;
            half   _IOR;
            half   _ChromaShift;

            half   _NoiseScale1;
            half   _BumpStr1;
            half   _RampPos1;

            half   _VoronoiScale;
            half   _BumpStr2;

            half   _NoiseScale2;
            half   _RampPos2;
            half   _DispScale;

            sampler2D _CameraOpaqueTexture;
            float4    _CameraOpaqueTexture_TexelSize;

            // ---------------------------------------------------------------
            // Hash (no leading underscores - reserved in HLSL)
            // ---------------------------------------------------------------
            float3 hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            float valueNoise3D(float3 p)
            {
                float3 ip = floor(p);
                float3 fp = frac(p);
                fp = fp * fp * (3.0 - 2.0 * fp);

                float a = dot(hash33(ip + float3(0,0,0)), float3(1,1,1)) / 3.0;
                float b = dot(hash33(ip + float3(1,0,0)), float3(1,1,1)) / 3.0;
                float c = dot(hash33(ip + float3(0,1,0)), float3(1,1,1)) / 3.0;
                float d = dot(hash33(ip + float3(1,1,0)), float3(1,1,1)) / 3.0;
                float e = dot(hash33(ip + float3(0,0,1)), float3(1,1,1)) / 3.0;
                float g = dot(hash33(ip + float3(1,0,1)), float3(1,1,1)) / 3.0;
                float h = dot(hash33(ip + float3(0,1,1)), float3(1,1,1)) / 3.0;
                float k = dot(hash33(ip + float3(1,1,1)), float3(1,1,1)) / 3.0;

                return lerp(lerp(lerp(a,b,fp.x), lerp(c,d,fp.x), fp.y),
                            lerp(lerp(e,g,fp.x), lerp(h,k,fp.x), fp.y), fp.z);
            }

            // fBm with fixed 8 octaves (Blender Detail=15 clamped for perf)
            float fbm3D(float3 p)
            {
                float v = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                [unroll]
                for (int j = 0; j < 8; j++)
                {
                    v += amp * valueNoise3D(p * freq);
                    freq *= 2.0;
                    amp  *= 0.5;
                }
                return v;
            }

            // ---------------------------------------------------------------
            // Voronoi F1 (Euclidean)
            // ---------------------------------------------------------------
            float voronoiF1(float3 p)
            {
                float3 ip = floor(p);
                float3 fp = frac(p);
                float minDist = 100.0;

                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            float3 nb = float3(x, y, z);
                            float3 pt = hash33(ip + nb);
                            float3 diff = nb + pt - fp;
                            float  ds = dot(diff, diff);
                            minDist = min(minDist, ds);
                        }
                    }
                }
                return sqrt(minDist);
            }

            // ColorRamp: black->white with adjustable midpoint
            float colorRamp(float t, float midpoint)
            {
                return saturate(t / max(midpoint, 0.001));
            }

            // Normal perturbation from height via finite differences
            float3 perturbNormal(float3 wNormal, float hC, float hDx, float hDy, float str)
            {
                float dHdu = hDx - hC;
                float dHdv = hDy - hC;

                // Build tangent frame (branchless fallback)
                float3 up = abs(wNormal.z) < 0.999 ? float3(0,0,1) : float3(0,1,0);
                float3 T = normalize(cross(wNormal, up));
                float3 B = cross(wNormal, T);

                return normalize(wNormal - str * dHdu * T - str * dHdv * B);
            }

            // ---------------------------------------------------------------
            // Structs
            // ---------------------------------------------------------------
            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 worldNorm  : TEXCOORD1;
                float4 grabPos    : TEXCOORD2;
                float3 objPos     : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);

                float3 objP = v.vertex.xyz;

                // Vertex displacement: Noise #2 -> ColorRamp -> offset along normal
                float noise2 = fbm3D(objP * _NoiseScale2);
                float dispH  = colorRamp(noise2, _RampPos2);
                v.vertex.xyz += v.normal * dispH * _DispScale;

                o.pos       = UnityObjectToClipPos(v.vertex);
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.objPos    = objP;
                o.grabPos   = ComputeScreenPos(o.pos);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N     = normalize(i.worldNorm);
                float3 V     = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L     = normalize(_WorldSpaceLightPos0.xyz);
                float3 H     = normalize(L + V);
                float3 objP  = i.objPos;

                // === Noise #1 -> ColorRamp -> Bump #1 ===
                float eps = 0.02;
                float h1_c  = colorRamp(fbm3D(objP * _NoiseScale1), _RampPos1);
                float h1_dx = colorRamp(fbm3D((objP + float3(eps,0,0)) * _NoiseScale1), _RampPos1);
                float h1_dy = colorRamp(fbm3D((objP + float3(0,eps,0)) * _NoiseScale1), _RampPos1);

                N = perturbNormal(N, h1_c, h1_dx, h1_dy, _BumpStr1 * 5.0);

                // === Voronoi -> Bump #2 (chained) ===
                float v_c  = voronoiF1(objP * _VoronoiScale);
                float v_dx = voronoiF1((objP + float3(eps,0,0)) * _VoronoiScale);
                float v_dy = voronoiF1((objP + float3(0,eps,0)) * _VoronoiScale);

                N = perturbNormal(N, v_c, v_dx, v_dy, _BumpStr2 * 5.0);

                // === Lighting ===
                float NdotL = max(dot(N, L), 0.0);
                float NdotH = max(dot(N, H), 0.0);
                float NdotV = max(dot(N, V), 0.001);

                // Fresnel (Schlick) with IOR-derived F0
                float f0 = pow((_IOR - 1.0) / (_IOR + 1.0), 2.0);
                float fresnel = f0 + (1.0 - f0) * pow(1.0 - NdotV, 5.0);

                // Sharp specular (roughness ~ 0)
                float spec = pow(NdotH, 512.0) * fresnel;

                // Diffuse tint (subtle for glass)
                float3 diffuse = _Color.rgb * NdotL * _LightColor0.rgb * _Opacity;

                // === Refraction (GrabPass) ===
                float2 grabUV = i.grabPos.xy / i.grabPos.w;
                float2 refractOffset = N.xy * (1.0 - 1.0 / _IOR) * 0.08;

                float2 uvR = grabUV + refractOffset * (1.0 + _ChromaShift);
                float2 uvG = grabUV + refractOffset;
                float2 uvB = grabUV + refractOffset * (1.0 - _ChromaShift);

                float3 refracted;
                refracted.r = tex2D(_CameraOpaqueTexture, uvR).r;
                refracted.g = tex2D(_CameraOpaqueTexture, uvG).g;
                refracted.b = tex2D(_CameraOpaqueTexture, uvB).b;

                refracted *= lerp(float3(1,1,1), _Color.rgb, 0.4);

                // === Emission ===
                float3 emission = _Color.rgb * _EmissionStr * 0.15;

                // === Reflection probe ===
                float3 reflDir = reflect(-V, N);
                float3 envReflect = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflDir).rgb;

                // === Composite ===
                float3 col = refracted * (1.0 - fresnel)
                           + envReflect * fresnel * _Color.rgb
                           + spec * _LightColor0.rgb
                           + diffuse
                           + emission;

                float alpha = saturate(_Opacity + fresnel * 0.5 + spec);

                return fixed4(col, alpha);
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
            };

            v2fShadow vertShadow(appdata_base v)
            {
                v2fShadow o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            fixed4 fragShadow(v2fShadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
