// TWBTerrainLitAdd.shader
// THE ADD PASS of TWB/Terrain/Lit: terrain layers 5-8 are drawn by this
// shader, additively. A copy of TWBTerrainLit.shader's ForwardLit and
// GBuffer passes with additive blending and the add-pass define, so the
// ground overlays and the anti-tiling reach every layer, not only the
// first four. Keep the two files' passes in step.
// Original header:
// Copy of "Universal Render Pipeline/Terrain/Lit" (URP 17.x / Unity 6) with
// the TWB dynamic ground-overlay system injected (TWBTerrainOverlays.hlsl,
// called from TWBTerrainLitPasses.hlsl): Alanthor white-slate bricks, blood
// spatters/puddles, and curse crystallized grass, all driven per-pixel by a
// world-space coverage mask built each frame by InfluenceMaskTexture.
// Assign a material using this shader as the terrain's Material template.
Shader "Hidden/TWB/Terrain/Lit (Add Pass)"
{
    Properties
    {
        [HideInInspector] [ToggleUI] _EnableHeightBlend("EnableHeightBlend", Float) = 0.0
        _HeightTransition("Height Transition", Range(0, 1.0)) = 0.0
        // Layer count is passed down to guide height-blend enable/disable, due
        // to the fact that heigh-based blend will be broken with multipass.
        [HideInInspector] [PerRendererData] _NumLayersCount ("Total Layer Count", Float) = 1.0

        // ── TWB ground overlays ────────────────────────────────────────
        [Header(TWB Overlays)]
        _AlanthorAlbedo("Alanthor Slate Albedo", 2D) = "white" {}
        _AlanthorNormal("Alanthor Slate Normal", 2D) = "bump" {}
        _AlanthorTiling("Alanthor Tiling (m)", Float) = 6.0
        _AlanthorSmoothness("Alanthor Smoothness", Range(0,1)) = 0.35
        _FeraldisTint("Feraldis Tint (placeholder)", Color) = (0.78, 0.45, 0.32, 1)
        _RunaiTint("Runai Tint (placeholder)", Color) = (0.88, 0.82, 0.55, 1)
        // Blood's look is NOT edited here: Resources/Blood.mat owns it and
        // TerrainOverlayMaterialBinder copies it onto the terrain (2026-10-16).
        [HideInInspector] _BloodAlbedo("Blood Albedo", 2D) = "white" {}
        [HideInInspector] _BloodTint("Blood Tint", Color) = (0.42, 0.04, 0.04, 1)
        [HideInInspector] _BloodTiling("Blood Tiling (m)", Float) = 4.0
        [HideInInspector] _BloodSmoothness("Blood Wet Smoothness", Range(0,1)) = 0.75
        [HideInInspector] _BloodNoiseScale("Blood Spatter Noise Scale", Float) = 0.9
        [HideInInspector] _BloodHeight("Blood Height", 2D) = "gray" {}
        [HideInInspector] _BloodDriedTint("Blood Dried Tint", Color) = (0.14, 0.03, 0.02, 1)
        [HideInInspector] _BloodGutsTint("Blood Guts Tint", Color) = (0.55, 0.18, 0.16, 1)
        [HideInInspector] _BloodHeightScale("Blood Relief Depth (m)", Float) = 0.05
        [HideInInspector] _BloodNormalStrength("Blood Normal Strength", Float) = 6
        [HideInInspector] _BloodDrySmoothness("Blood Dry Smoothness", Range(0,1)) = 0.2
        [HideInInspector] _BloodDryness("Blood Dryness", Range(0,1)) = 0.5
        [HideInInspector] _BloodPatchScale("Blood Wet/Dry Patch Scale", Float) = 0.35
        // Curse ground's look: Resources/Curse.mat owns it (2026-10-16).
        [HideInInspector] _CurseAlbedo("Curse Rock Albedo", 2D) = "white" {}
        [HideInInspector] _CurseNormal("Curse Rock Normal", 2D) = "bump" {}
        [HideInInspector] _CurseTiling("Curse Tiling (m)", Float) = 12.0
        [HideInInspector] _CurseTint("Curse Tint (purple pole)", Color) = (0.62, 0.35, 0.9, 1)
        [HideInInspector] _CurseTint2("Curse Tint (greenish pole)", Color) = (0.45, 0.85, 0.55, 1)
        [HideInInspector] _CurseSmoothness("Curse Smoothness", Range(0,1)) = 0.65
        [HideInInspector] _CurseSparkleScale("Curse Sparkle Scale", Float) = 3.0
        [HideInInspector] _CurseHeight("Curse Height", 2D) = "gray" {}
        [HideInInspector] _CurseRoughness("Curse Roughness", 2D) = "gray" {}
        [HideInInspector] _CurseOcclusion("Curse Occlusion", 2D) = "white" {}
        [HideInInspector] _CurseHeightScale("Curse Relief Depth (m)", Float) = 0.12
        [HideInInspector] _CurseNormalStrength("Curse Normal Strength", Float) = 1.5
        [HideInInspector] _CurseMetallic("Curse Metallic", Range(0,1)) = 0.35
        [HideInInspector] _CurseJaggedness("Curse Front Jaggedness", Float) = 0.6
        _OverlayNoiseScale("Overlay Edge Noise Scale", Float) = 0.15

        [Header(Region Boundaries)]
        _RegionEdgeTint("Region Edge Tint (multiplier)", Color) = (0.32, 0.30, 0.28, 1)
        _RegionEdgeStrength("Region Edge Strength", Range(0,1)) = 0.55

        // set by terrain engine
        [HideInInspector] _Control("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Splat3("Layer 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Splat2("Layer 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Splat1("Layer 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Splat0("Layer 0 (R)", 2D) = "grey" {}
        [HideInInspector] _Normal3("Normal 3 (A)", 2D) = "bump" {}
        [HideInInspector] _Normal2("Normal 2 (B)", 2D) = "bump" {}
        [HideInInspector] _Normal1("Normal 1 (G)", 2D) = "bump" {}
        [HideInInspector] _Normal0("Normal 0 (R)", 2D) = "bump" {}
        [HideInInspector] _Mask3("Mask 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Mask2("Mask 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Mask1("Mask 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Mask0("Mask 0 (R)", 2D) = "grey" {}
        [HideInInspector][Gamma] _Metallic0("Metallic 0", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic1("Metallic 1", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic2("Metallic 2", Range(0.0, 1.0)) = 0.0
        [HideInInspector][Gamma] _Metallic3("Metallic 3", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _Smoothness0("Smoothness 0", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness1("Smoothness 1", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness2("Smoothness 2", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _Smoothness3("Smoothness 3", Range(0.0, 1.0)) = 0.5

        // used in fallback on old cards & base map
        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)

        [HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}

        [ToggleUI] _EnableInstancedPerPixelNormal("Enable Instanced per-pixel normal", Float) = 1.0
    }

    HLSLINCLUDE

    #pragma multi_compile_fragment __ _ALPHATEST_ON

    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry-99" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "False"}

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment
            #define TERRAIN_SPLAT_ADDPASS

            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local_fragment _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _MASKMAP
            // Sample normal in pixel shader when doing instancing
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "TWBTerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags{"LightMode" = "UniversalGBuffer"}
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore

            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment
            #define TERRAIN_SPLAT_ADDPASS

            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

            //#pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature_local _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _MASKMAP
            // Sample normal in pixel shader when doing instancing
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
            #define TERRAIN_GBUFFER 1

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "TWBTerrainLitPasses.hlsl"
            ENDHLSL
        }

    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
