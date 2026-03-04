Shader "Custom/AnimatedWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.18, 0.55, 0.68, 0.85)
        _DeepColor ("Deep Color", Color) = (0.04, 0.18, 0.32, 0.92)
        _FoamColor ("Foam Color", Color) = (0.92, 0.96, 1, 0.6)
        _WaveSpeed ("Wave Speed", Float) = 0.25
        _WaveScale ("Wave Scale", Float) = 0.012
        _WaveHeight ("Wave Height", Float) = 0.6
        _RippleScale ("Ripple Scale", Float) = 0.04
        _RippleSpeed ("Ripple Speed", Float) = 0.6
        _FresnelPower ("Fresnel Power", Float) = 3.5
        _SpecularPower ("Specular Power", Float) = 80
        _SpecularIntensity ("Specular Intensity", Float) = 0.7
        _WaterLevel ("Water Level", Float) = 20
    }
    
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float3 normal : TEXCOORD3;
                float waveHeight : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };
            
            float4 _ShallowColor;
            float4 _DeepColor;
            float4 _FoamColor;
            float _WaveSpeed;
            float _WaveScale;
            float _WaveHeight;
            float _RippleScale;
            float _RippleSpeed;
            float _FresnelPower;
            float _SpecularPower;
            float _SpecularIntensity;
            float _WaterLevel;
            
            // Gradient noise for organic patterns
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }
            
            float gradientNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(lerp(dot(hash2(i + float2(0.0, 0.0)), f - float2(0.0, 0.0)),
                                 dot(hash2(i + float2(1.0, 0.0)), f - float2(1.0, 0.0)), u.x),
                            lerp(dot(hash2(i + float2(0.0, 1.0)), f - float2(0.0, 1.0)),
                                 dot(hash2(i + float2(1.0, 1.0)), f - float2(1.0, 1.0)), u.x), u.y);
            }
            
            // Fractal Brownian Motion - organic patterns
            float fbm(float2 p, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                float2 shift = float2(100.0, 100.0);
                float2x2 rot = float2x2(cos(0.5), sin(0.5), -sin(0.5), cos(0.5));
                
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * gradientNoise(p * frequency);
                    p = mul(rot, p) * 2.0 + shift;
                    amplitude *= 0.5;
                }
                return value;
            }
            
            // Gerstner wave - realistic ocean wave shape
            float3 gerstnerWave(float2 pos, float2 dir, float steepness, float wavelength, float time)
            {
                float k = 2.0 * 3.14159 / wavelength;
                float c = sqrt(9.8 / k);
                float2 d = normalize(dir);
                float f = k * (dot(d, pos) - c * time);
                float a = steepness / k;
                
                return float3(
                    d.x * a * cos(f),
                    a * sin(f),
                    d.y * a * cos(f)
                );
            }
            
            v2f vert(appdata v)
            {
                v2f o;
                
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float time = _Time.y * _WaveSpeed;
                
                // Multiple Gerstner waves for realistic ocean movement
                float3 wave = float3(0, 0, 0);
                
                // Primary swell
                wave += gerstnerWave(worldPos.xz, float2(1.0, 0.6), 0.15, 60.0, time);
                
                // Secondary crossing wave
                wave += gerstnerWave(worldPos.xz, float2(-0.7, 0.9), 0.12, 35.0, time * 1.1);
                
                // Shorter wind waves
                wave += gerstnerWave(worldPos.xz, float2(0.4, -0.8), 0.08, 18.0, time * 1.3);
                wave += gerstnerWave(worldPos.xz, float2(-0.5, -0.6), 0.06, 12.0, time * 1.5);
                
                // Add organic noise for chop
                float noise = fbm(worldPos.xz * _RippleScale + time * 0.15, 4);
                wave.y += noise * 0.3 * _WaveHeight;
                
                v.vertex.xyz += wave * _WaveHeight;
                
                // Calculate normal
                float eps = 0.5;
                float3 tangentX = float3(eps, 
                    gerstnerWave(worldPos.xz + float2(eps, 0), float2(1.0, 0.6), 0.15, 60.0, time).y -
                    gerstnerWave(worldPos.xz - float2(eps, 0), float2(1.0, 0.6), 0.15, 60.0, time).y, 0);
                float3 tangentZ = float3(0,
                    gerstnerWave(worldPos.xz + float2(0, eps), float2(1.0, 0.6), 0.15, 60.0, time).y -
                    gerstnerWave(worldPos.xz - float2(0, eps), float2(1.0, 0.6), 0.15, 60.0, time).y, eps);
                float3 normal = normalize(cross(tangentZ, tangentX));
                
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldPos);
                o.normal = lerp(float3(0, 1, 0), normal, 0.7);
                o.waveHeight = wave.y;
                
                UNITY_TRANSFER_FOG(o, o.pos);
                
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                float time = _Time.y;
                
                // Fresnel effect
                float NdotV = saturate(dot(normalize(i.normal), i.viewDir));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);
                
                // Organic surface detail using fbm
                float2 uv1 = i.worldPos.xz * _RippleScale * 1.2 + float2(time * 0.06, time * 0.04);
                float2 uv2 = i.worldPos.xz * _RippleScale * 0.8 + float2(-time * 0.04, time * 0.07);
                float detail1 = fbm(uv1, 5) * 0.5 + 0.5;
                float detail2 = fbm(uv2, 5) * 0.5 + 0.5;
                float surfaceDetail = (detail1 + detail2) * 0.5;
                
                // Depth-based color with organic variation
                float depthVar = fbm(i.worldPos.xz * 0.005 + time * 0.01, 3) * 0.5 + 0.5;
                float4 waterColor = lerp(_ShallowColor, _DeepColor, depthVar * 0.5 + 0.25);
                
                // Add subtle color variation
                waterColor.rgb += (surfaceDetail - 0.5) * 0.06 * _ShallowColor.rgb;
                
                // Foam on wave peaks
                float foamMask = saturate((i.waveHeight - 0.2) * 2.0);
                foamMask *= surfaceDetail;
                foamMask = pow(foamMask, 2.0);
                waterColor = lerp(waterColor, _FoamColor, foamMask * 0.35);
                
                // Specular highlight
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfVec = normalize(lightDir + i.viewDir);
                float NdotH = saturate(dot(normalize(i.normal), halfVec));
                float specular = pow(NdotH, _SpecularPower) * _SpecularIntensity;
                
                // Sun and sky colors
                float3 sunColor = _LightColor0.rgb;
                float3 skyColor = float3(0.55, 0.72, 0.92);
                
                // Reflection
                float3 reflectionColor = lerp(skyColor * 0.5, sunColor * 1.2, specular);
                
                // Compose final color
                float4 finalColor = waterColor;
                finalColor.rgb += specular * sunColor * 0.6;
                finalColor.rgb = lerp(finalColor.rgb, reflectionColor, fresnel * 0.4);
                
                // Subtle subsurface scattering look
                float sss = saturate(dot(i.viewDir, -lightDir) * 0.5 + 0.5);
                finalColor.rgb += _ShallowColor.rgb * sss * 0.08;
                
                // Alpha
                finalColor.a = lerp(waterColor.a * 0.9, 0.96, fresnel * 0.3);
                
                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                
                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "Transparent/Diffuse"
}
