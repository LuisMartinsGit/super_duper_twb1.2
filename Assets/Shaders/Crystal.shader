// Crystal.shader
// Translucent crystal material ported from a Blender Principled BSDF setup.
//
// Visual recipe (Blender source):
//   - Principled BSDF: Transmission 0.91, IOR 1.45, Roughness 0, Metallic 0
//   - Base color:  orange (configurable via _Color)
//   - Bump chain:  Noise(1.7) → ColorRamp → Bump(0.1) → Voronoi(3.5) → Bump(0.06)
//   - Displacement: Noise(7.0) → ColorRamp → vertex offset (scale 0.1)
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
        _NoiseDetail1   ("Noise Detail",       Float)          = 15.0
        _BumpStr1       ("Bump Strength",      Range(0,1))     = 0.1
        _RampPos1       ("ColorRamp Midpoint", Range(0,1))     = 0.337

        [Header(Voronoi Bump)]
        _VoronoiScale   ("Voronoi Scale",      Float)          = 3.5
        _BumpStr2       ("Bump Strength",      Range(0,1))     = 0.06

        [Header(Displacement)]
        _NoiseScale2    ("Displacement Noise Scale", Float)    = 7.0
        _NoiseDetail2   ("Displacement Noise Detail",Float)    = 15.0
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

        // --- Pass 0: grab the screen behind this object for refraction ---
        GrabPass { "_CrystalGrab" }

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
            #pragma target 3.5

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            // ---------------------------------------------------------------
            // Properties
            // ---------------------------------------------------------------
            fixed4 _Color;
            half   _Opacity;
            half   _EmissionStr;
            half   _IOR;
            half   _ChromaShift;

            half   _NoiseScale1;
            half   _NoiseDetail1;
            half   _BumpStr1;
            half   _RampPos1;

            half   _VoronoiScale;
            half   _BumpStr2;

            half   _NoiseScale2;
            half   _NoiseDetail2;
            half   _RampPos2;
            half   _DispScale;

            sampler2D _CrystalGrab;
            float4    _CrystalGrab_TexelSize;

            // ---------------------------------------------------------------
            // Noise helpers (3D value noise, matches Blender "Noise Texture")
            // ---------------------------------------------------------------
            float3 _hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            float _valueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                float a = dot(_hash33(i + float3(0,0,0)), float3(1,1,1)) / 3.0;
                float b = dot(_hash33(i + float3(1,0,0)), float3(1,1,1)) / 3.0;
                float c = dot(_hash33(i + float3(0,1,0)), float3(1,1,1)) / 3.0;
                float d = dot(_hash33(i + float3(1,1,0)), float3(1,1,1)) / 3.0;
                float e = dot(_hash33(i + float3(0,0,1)), float3(1,1,1)) / 3.0;
                float g = dot(_hash33(i + float3(1,0,1)), float3(1,1,1)) / 3.0;
                float h = dot(_hash33(i + float3(0,1,1)), float3(1,1,1)) / 3.0;
                float k = dot(_hash33(i + float3(1,1,1)), float3(1,1,1)) / 3.0;

                return lerp(lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y),
                            lerp(lerp(e,g,f.x), lerp(h,k,f.x), f.y), f.z);
            }

            // fBm with "detail" octaves (Blender Detail parameter)
            float fbm3D(float3 p, float octaves)
            {
                float v = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                int oct = (int)clamp(octaves, 1, 16);
                for (int i = 0; i < oct; i++)
                {
                    v += amp * _valueNoise3D(p * freq);
                    freq *= 2.0;
                    amp  *= 0.5;
                }
                return v;
            }

            // ---------------------------------------------------------------
            // Voronoi F1 (Euclidean) — matches Blender's Voronoi Texture
            // ---------------------------------------------------------------
            float voronoiF1(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float minDist = 1e10;

                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    float3 neighbor = float3(x, y, z);
                    float3 point    = _hash33(i + neighbor);
                    float3 diff     = neighbor + point - f;
                    float  d        = dot(diff, diff);
                    minDist = min(minDist, d);
                }
                return sqrt(minDist);
            }

            // ---------------------------------------------------------------
            // ColorRamp helper (Blender black→white with adjustable midpoint)
            // ---------------------------------------------------------------
            float colorRamp(float t, float midpoint)
            {
                return saturate(t / max(midpoint, 0.001));
            }

            // ---------------------------------------------------------------
            // Normal-from-height (central differences on a height function)
            // ---------------------------------------------------------------
            float3 perturbNormal(float3 worldPos, float3 worldNormal,
                                 float height, float3 hDx, float3 hDy, float strength)
            {
                // hDx / hDy = height sampled at small offsets along tangent axes
                // We pass the actual height values for center, +du, +dv
                // and reconstruct partial derivatives.
                // (Caller packs: hDx.x = h_center, hDx.y = h_du, hDy.y = h_dv)
                float dHdu = (hDx.y - hDx.x);
                float dHdv = (hDy.y - hDy.x);

                float3 T = normalize(cross(worldNormal, float3(0,0,1)));
                if (length(cross(worldNormal, float3(0,0,1))) < 0.001)
                    T = normalize(cross(worldNormal, float3(0,1,0)));
                float3 B = cross(worldNormal, T);

                float3 perturbed = normalize(worldNormal
                    - strength * dHdu * T
                    - strength * dHdv * B);
                return perturbed;
            }

            // ---------------------------------------------------------------
            // Vertex / Fragment
            // ---------------------------------------------------------------
            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 worldNorm  : TEXCOORD1;
                float4 grabPos    : TEXCOORD2;
                float3 objPos     : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                SHADOW_COORDS(5)
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                // --- Vertex displacement (Noise #2 → ColorRamp → offset along normal) ---
                float3 objP = v.vertex.xyz;
                float noise2 = fbm3D(objP * _NoiseScale2, _NoiseDetail2);
                float dispH  = colorRamp(noise2, _RampPos2);
                v.vertex.xyz += v.normal * dispH * _DispScale;

                o.pos       = UnityObjectToClipPos(v.vertex);
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.objPos    = objP;
                o.grabPos   = ComputeGrabScreenPos(o.pos);

                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N     = normalize(i.worldNorm);
                float3 V     = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L     = normalize(_WorldSpaceLightPos0.xyz);
                float3 H     = normalize(L + V);
                float3 objP  = i.objPos;

                // === Noise #1 → ColorRamp → Bump #1 ===
                float eps = 0.02;
                float n1_c  = fbm3D(objP * _NoiseScale1, _NoiseDetail1);
                float n1_dx = fbm3D((objP + float3(eps,0,0)) * _NoiseScale1, _NoiseDetail1);
                float n1_dy = fbm3D((objP + float3(0,eps,0)) * _NoiseScale1, _NoiseDetail1);

                float h1_c  = colorRamp(n1_c,  _RampPos1);
                float h1_dx = colorRamp(n1_dx, _RampPos1);
                float h1_dy = colorRamp(n1_dy, _RampPos1);

                N = perturbNormal(i.worldPos, N, h1_c,
                    float3(h1_c, h1_dx, 0),
                    float3(h1_c, h1_dy, 0),
                    _BumpStr1 * 5.0);

                // === Voronoi → Bump #2 (chained from Bump #1 normal) ===
                float v_c  = voronoiF1(objP * _VoronoiScale);
                float v_dx = voronoiF1((objP + float3(eps,0,0)) * _VoronoiScale);
                float v_dy = voronoiF1((objP + float3(0,eps,0)) * _VoronoiScale);

                N = perturbNormal(i.worldPos, N, v_c,
                    float3(v_c, v_dx, 0),
                    float3(v_c, v_dy, 0),
                    _BumpStr2 * 5.0);

                // === Lighting (GGX-ish specular, no roughness → sharp highlights) ===
                float NdotL = max(dot(N, L), 0.0);
                float NdotH = max(dot(N, H), 0.0);
                float NdotV = max(dot(N, V), 0.001);

                // Fresnel (Schlick) with IOR-derived F0
                float f0 = pow((_IOR - 1.0) / (_IOR + 1.0), 2.0);
                float fresnel = f0 + (1.0 - f0) * pow(1.0 - NdotV, 5.0);

                // Sharp specular (roughness ≈ 0)
                float spec = pow(NdotH, 512.0) * fresnel;

                // Diffuse tint (very subtle for glass)
                float3 diffuse = _Color.rgb * NdotL * _LightColor0.rgb * _Opacity;

                // === Refraction (GrabPass + distortion) ===
                float2 grabUV = i.grabPos.xy / i.grabPos.w;
                float2 refractOffset = N.xy * (1.0 - 1.0 / _IOR) * 0.08;

                // Chromatic aberration
                float2 uvR = grabUV + refractOffset * (1.0 + _ChromaShift);
                float2 uvG = grabUV + refractOffset;
                float2 uvB = grabUV + refractOffset * (1.0 - _ChromaShift);

                float3 refracted;
                refracted.r = tex2D(_CrystalGrab, uvR).r;
                refracted.g = tex2D(_CrystalGrab, uvG).g;
                refracted.b = tex2D(_CrystalGrab, uvB).b;

                // Tint refracted light by crystal color
                refracted *= lerp(float3(1,1,1), _Color.rgb, 0.4);

                // === Emission ===
                float3 emission = _Color.rgb * _EmissionStr * 0.15;

                // === Composite ===
                float  reflectivity = fresnel;
                float3 envReflect   = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0,
                                        reflect(-V, N)).rgb;

                float3 col = refracted * (1.0 - reflectivity)
                           + envReflect * reflectivity * _Color.rgb
                           + spec * _LightColor0.rgb
                           + diffuse
                           + emission;

                // Shadow
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                col *= lerp(0.5, 1.0, atten);

                float alpha = saturate(_Opacity + fresnel * 0.5 + spec);

                UNITY_APPLY_FOG(i.fogCoord, col);
                return fixed4(col, alpha);
            }
            ENDCG
        }

        // --- Pass 1: shadow caster (so the crystal casts faint shadows) ---
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
