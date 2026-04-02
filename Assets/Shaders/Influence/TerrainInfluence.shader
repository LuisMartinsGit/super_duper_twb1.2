// TerrainInfluence.shader
// Custom terrain surface shader that replaces Unity's default terrain material.
//
// Renders the base terrain with up to 4 texture layers (height-blended splatmap),
// then overlays faction influence from the shared InfluenceMap RenderTexture.
//
// Influence channels (read from InfluenceMap):
//   R = Alanthor   → gold/stone tint + carved texture
//   G = Runaii     → teal/silk lane pattern
//   B = Feraldis   → dark red blood ground texture
//   A = Crystal    → purple crystalline overlay (always rendered on top)
//
// Blend priority: Alanthor → Runaii → Feraldis → Crystal (Crystal is always topmost)
//
// HOW TO SET UP:
//   1. Create a new Material, assign this shader.
//   2. Set the material as the terrain's "Material Template" in the Terrain component.
//   3. InfluenceManager.Awake() will push _InfluenceMap and _TerrainSize automatically.
//   4. Assign faction overlay textures and tints in the material inspector.
//
// NOTE: This shader does NOT use Unity's built-in terrain splatmap blending system
//       (TerrainLayers). You must supply base textures via the _Layer0–3 properties.
//       For a production game you might extend this to read TerrainData splatmaps;
//       this version keeps things simple and educational.

Shader "Influence/TerrainInfluence"
{
    Properties
    {
        // -----------------------------------------------------------------------
        // Base terrain layers (simple 4-layer blend, no splatmap in this version)
        // -----------------------------------------------------------------------
        [Header(Base Terrain)]
        _BaseColor      ("Base Color Tint",             Color)  = (1, 1, 1, 1)
        _Layer0Tex      ("Layer 0 - Ground",             2D)    = "gray" {}
        _Layer0Tiling   ("Layer 0 Tiling",               Float) = 10
        // Extend to Layer1–3 if you need more terrain variety

        // -----------------------------------------------------------------------
        // Influence Map (set automatically by InfluenceManager)
        // -----------------------------------------------------------------------
        [Header(Influence)]
        _InfluenceMap   ("Influence Map (ARGB)",         2D)    = "black" {}
        _TerrainSize    ("Terrain World Size XZ",        Vector) = (256, 256, 0, 0)

        // Influence above this value starts blending the faction overlay in
        _InfluenceThreshold ("Influence Threshold",      Float) = 0.1

        // -----------------------------------------------------------------------
        // Alanthor overlay (R channel)
        // -----------------------------------------------------------------------
        [Header(Alanthor R Channel)]
        _AlanthorTex    ("Alanthor Overlay Tex",         2D)    = "white" {}
        _AlanthorTint   ("Alanthor Tint (gold/stone)",   Color) = (0.9, 0.75, 0.2, 1)
        _AlanthorTiling ("Alanthor Tex Tiling",          Float) = 5

        // -----------------------------------------------------------------------
        // Runaii overlay (G channel)
        // -----------------------------------------------------------------------
        [Header(Runaii G Channel)]
        _RunaiiTex      ("Runaii Overlay Tex",           2D)    = "white" {}
        _RunaiiTint     ("Runaii Tint (teal/silk)",      Color) = (0.1, 0.75, 0.7, 1)
        _RunaiiTiling   ("Runaii Tex Tiling",            Float) = 8

        // -----------------------------------------------------------------------
        // Feraldis overlay (B channel)
        // -----------------------------------------------------------------------
        [Header(Feraldis B Channel)]
        _FeraldisBloodTex  ("Feraldis Blood Tex",        2D)    = "white" {}
        _FeraldistTint     ("Feraldis Tint (dark red)",  Color) = (0.45, 0.0, 0.0, 1)
        _FeraldiisTiling   ("Feraldis Tex Tiling",       Float) = 6

        // -----------------------------------------------------------------------
        // Crystal Curse overlay (A channel)
        // -----------------------------------------------------------------------
        [Header(Crystal Curse A Channel)]
        _CrystalTex     ("Crystal Overlay Tex",          2D)    = "white" {}
        _CrystalTint    ("Crystal Tint (purple)",         Color) = (0.6, 0.1, 0.9, 1)
        _CrystalTiling  ("Crystal Tex Tiling",           Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue"      = "Geometry"
        }

        LOD 200

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            // -----------------------------------------------------------------------
            // Uniforms — must match Properties block names exactly
            // -----------------------------------------------------------------------

            // Base terrain
            float4    _BaseColor;
            sampler2D _Layer0Tex;
            float     _Layer0Tiling;

            // Influence
            sampler2D _InfluenceMap;
            float4    _TerrainSize;
            float     _InfluenceThreshold;

            // Alanthor
            sampler2D _AlanthorTex;
            float4    _AlanthorTint;
            float     _AlanthorTiling;

            // Runaii
            sampler2D _RunaiiTex;
            float4    _RunaiiTint;
            float     _RunaiiTiling;

            // Feraldis
            sampler2D _FeraldisBloodTex;
            float4    _FeraldistTint;
            float     _FeraldiisTiling;

            // Crystal
            sampler2D _CrystalTex;
            float4    _CrystalTint;
            float     _CrystalTiling;

            // -----------------------------------------------------------------------
            // Vertex input / output
            // -----------------------------------------------------------------------
            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float2 uv      : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float2 uv        : TEXCOORD0;   // base terrain UV (tiled)
                float3 worldPos  : TEXCOORD1;   // world XYZ for influence UV
                float3 normal    : TEXCOORD2;
                SHADOW_COORDS(3)
            };

            // -----------------------------------------------------------------------
            // Vertex shader
            // -----------------------------------------------------------------------
            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal   = UnityObjectToWorldNormal(v.normal);
                TRANSFER_SHADOW(o);
                return o;
            }

            // -----------------------------------------------------------------------
            // Helpers
            // -----------------------------------------------------------------------

            // Converts world XZ to [0,1] UV for sampling the InfluenceMap.
            // _TerrainSize.xy = world XZ extents.
            // We assume terrain origin is (0,0) in world space here; if it isn't,
            // pass _TerrainOrigin as another uniform and subtract it first.
            float2 WorldToInfluenceUV(float3 worldPos)
            {
                return float2(worldPos.x / _TerrainSize.x, worldPos.z / _TerrainSize.y);
            }

            // Blend an overlay on top of a base color.
            // weight is the influence value (0=no overlay, 1=full overlay).
            // threshold removes sub-threshold influence so faint values don't tint.
            float3 BlendOverlay(float3 base, float3 overlayTex, float3 overlayTint,
                                float weight, float threshold)
            {
                // Remap weight so it goes from 0 at threshold to 1 at 1.0
                float blendFactor = smoothstep(threshold, threshold + 0.2, weight);
                float3 overlay = overlayTex * overlayTint;
                return lerp(base, overlay, blendFactor);
            }

            // -----------------------------------------------------------------------
            // Fragment shader
            // -----------------------------------------------------------------------
            half4 frag(v2f i) : SV_Target
            {
                // --- Base terrain color ---
                float2 baseUV = i.uv * _Layer0Tiling;
                float3 baseColor = tex2D(_Layer0Tex, baseUV).rgb * _BaseColor.rgb;

                // --- Influence map sample ---
                // UV = world XZ normalized to terrain size
                float2 influenceUV = WorldToInfluenceUV(i.worldPos);
                float4 influence   = tex2D(_InfluenceMap, influenceUV);
                // influence.r = Alanthor, .g = Runaii, .b = Feraldis, .a = Crystal

                // --- Apply faction overlays in priority order (Alanthor first, Crystal last) ---
                float3 color = baseColor;

                // 1. Alanthor (R channel) — gold/stone
                float3 alanthorTex = tex2D(_AlanthorTex, i.uv * _AlanthorTiling).rgb;
                color = BlendOverlay(color, alanthorTex, _AlanthorTint.rgb,
                                     influence.r, _InfluenceThreshold);

                // 2. Runaii (G channel) — teal/silk
                float3 runaiitex = tex2D(_RunaiiTex, i.uv * _RunaiiTiling).rgb;
                color = BlendOverlay(color, runaiitex, _RunaiiTint.rgb,
                                     influence.g, _InfluenceThreshold);

                // 3. Feraldis (B channel) — dark blood
                float3 feraldisBloodTex = tex2D(_FeraldisBloodTex, i.uv * _FeraldiisTiling).rgb;
                color = BlendOverlay(color, feraldisBloodTex, _FeraldistTint.rgb,
                                     influence.b, _InfluenceThreshold);

                // 4. Crystal Curse (A channel) — always on top
                float3 crystalTex = tex2D(_CrystalTex, i.uv * _CrystalTiling).rgb;
                color = BlendOverlay(color, crystalTex, _CrystalTint.rgb,
                                     influence.a, _InfluenceThreshold);

                // --- Basic diffuse lighting ---
                float3 lightDir  = normalize(_WorldSpaceLightPos0.xyz);
                float  nDotL     = max(0, dot(normalize(i.normal), lightDir));
                float  shadow    = SHADOW_ATTENUATION(i);
                float3 ambient   = ShadeSH9(half4(i.normal, 1));
                float3 lit       = color * (_LightColor0.rgb * nDotL * shadow + ambient);

                return half4(lit, 1);
            }
            ENDHLSL
        }

        // Shadow caster pass — required for the terrain to cast shadows
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }

    // Fallback to standard diffuse if this shader isn't supported
    FallBack "Diffuse"
}
