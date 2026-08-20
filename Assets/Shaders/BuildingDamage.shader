// BuildingDamage.shader
// Progressive building battle-damage driven by a single _Damage value
// (0 = pristine, 1 = destroyed), set each frame by BuildingDamageVisual from
// the entity's ECS Health (1 - Value/Max).
//
// The core idea is a TRAVELLING BURN. Each surface region gets a per-region
// ignition point from a low-frequency noise, so regions catch fire at different
// damage levels. A region's local burn timeline is:
//     not-lit  ->  igniting/burning (flames + glowing embers)  ->  charring dark
// As _Damage climbs the fire front sweeps across the building, leaving blackened
// scars behind it — so "some places burn first, then turn dark" while others are
// still clean, and only the active burn front carries flames/embers (keeping the
// fire sparse rather than the whole surface ablaze).
//
// On top of the burn: subtle structural cracks, and blown-out holes at high
// damage. Shading routes through UniversalFragmentPBR so the building keeps the
// scene's main light / shadows / GI / fog.

Shader "TheWaningBorder/BuildingDamage"
{
    Properties
    {
        _BaseColor   ("Base Color", Color)            = (1,1,1,1)
        _BaseMap     ("Base Map", 2D)                 = "white" {}
        _UseBaseMap  ("Use Base Map", Range(0,1))     = 1
        _Metallic    ("Metallic", Range(0,1))         = 0
        _Smoothness  ("Smoothness", Range(0,1))       = 0.35

        _Damage      ("Damage", Range(0,1))           = 0

        // Travelling-burn model.
        _BurnPatchScale ("Burn Patch Scale", Float)        = 0.40
        // Burning only ignites in the upper half of _Damage — see the note on
        // BuildingDamageVisual: while the building is alive _Damage is held to
        // <= 0.5 (cracks + light scorch only, so it still reads as repairable),
        // and the 0.5 -> 1 burn sweep is driven by the death/collapse phase.
        _BurnStart      ("Burn Start (damage)", Range(0,1))= 0.50
        _BurnRange      ("Burn Spread (damage)", Range(0,1))= 0.50
        _BurnDuration   ("Burn->Char Duration", Range(0.05,1)) = 0.35

        // Char (the blackened scar left after a region burns out).
        _SootColor   ("Char Color", Color)            = (0.08,0.07,0.065,1)
        _SootScale   ("Char Noise Scale", Float)      = 0.60
        _SootMax     ("Max Char Strength", Range(0,1))= 0.70
        _ScorchMax   ("Alive Scorch Strength", Range(0,1)) = 0.28

        // Cracks (general structural damage, independent of fire).
        _CrackColor  ("Crack Color", Color)           = (0.03,0.025,0.025,1)
        _CrackScale  ("Crack Noise Scale", Float)     = 3.5
        _CrackWidth  ("Crack Width", Range(0.01,0.4)) = 0.10

        // Holes.
        _HoleScale   ("Hole Noise Scale", Float)      = 1.3
        _HoleMax     ("Max Hole Coverage", Range(0,0.7)) = 0.35

        // Embers + flames (only on the burn front).
        _EmberColor     ("Ember Color (HDR)", Color)   = (4.0,1.0,0.20,1)
        _EmberIntensity ("Ember Intensity", Range(0,8))= 1.5
        _FlameHot       ("Flame Core (HDR)", Color)    = (6.0,2.4,0.30,1)
        _FlameTip       ("Flame Tip (HDR)", Color)     = (3.0,0.45,0.05,1)
        _FlameScale     ("Flame Noise Scale", Float)   = 2.2
        _FlameSpeed     ("Flame Rise Speed", Float)    = 1.6
        _FlameSharpness ("Flame Sharpness", Range(0,1))= 0.55
        _FlameIntensity ("Flame Intensity", Range(0,8))= 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "FORWARD_LIT"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            sampler2D _BaseMap;
            float4    _BaseMap_ST;
            float4    _BaseColor;
            float     _UseBaseMap;
            float     _Metallic;
            float     _Smoothness;
            float     _Damage;
            float     _BurnPatchScale;
            float     _BurnStart;
            float     _BurnRange;
            float     _BurnDuration;
            float4    _SootColor;
            float     _SootScale;
            float     _SootMax;
            float     _ScorchMax;
            float4    _CrackColor;
            float     _CrackScale;
            float     _CrackWidth;
            float     _HoleScale;
            float     _HoleMax;
            float4    _EmberColor;
            float     _EmberIntensity;
            float4    _FlameHot;
            float4    _FlameTip;
            float     _FlameScale;
            float     _FlameSpeed;
            float     _FlameSharpness;
            float     _FlameIntensity;

            float Hash13(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i + float3(0,0,0));
                float n100 = Hash13(i + float3(1,0,0));
                float n010 = Hash13(i + float3(0,1,0));
                float n110 = Hash13(i + float3(1,1,0));
                float n001 = Hash13(i + float3(0,0,1));
                float n101 = Hash13(i + float3(1,0,1));
                float n011 = Hash13(i + float3(0,1,1));
                float n111 = Hash13(i + float3(1,1,1));
                return lerp(
                    lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                    lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
                    f.z);
            }

            float FBM(float3 p)
            {
                float s = 0.0;
                float a = 0.5;
                [unroll]
                for (int i = 0; i < 3; i++) { s += a * ValueNoise(p); p *= 2.0; a *= 0.5; }
                return s;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 baseUV      : TEXCOORD2;
                half3  vertexLight : TEXCOORD3;
                half   fogFactor   : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = vpi.positionCS;
                OUT.worldPos    = vpi.positionWS;
                OUT.worldNormal = vni.normalWS;
                OUT.baseUV      = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                OUT.vertexLight = VertexLighting(vpi.positionWS, vni.normalWS);
                OUT.fogFactor   = ComputeFogFactor(vpi.positionCS.z);
                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                OUTPUT_SH(vni.normalWS.xyz, OUT.vertexSH);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float dmg = saturate(_Damage);
                float t   = _Time.y; // seconds — ember flicker + flame rise

                // ---- MISSING PIECES: blow chunks out at high damage --------
                float holeAmt    = saturate((dmg - 0.60) / 0.40);
                float nHole      = FBM(IN.worldPos * _HoleScale);
                float holeThresh = holeAmt * _HoleMax;
                if (nHole < holeThresh) discard;
                float holeEdge = 1.0 - smoothstep(holeThresh, holeThresh + 0.06, nHole);

                // ---- ALBEDO ------------------------------------------------
                half3 albedo = _BaseColor.rgb;
                if (_UseBaseMap > 0.5) albedo *= tex2D(_BaseMap, IN.baseUV).rgb;

                // ---- CRACKS: the main "alive / repairable" damage. Grow from
                //      ~8% so a battle-worn (but salvageable) building is mostly
                //      cracks + grime, no fire. ----------------------------------
                float crackAmt  = saturate((dmg - 0.08) / 0.62);
                float nCrack    = ValueNoise(IN.worldPos * _CrackScale);
                float crackBand = 1.0 - smoothstep(0.0, _CrackWidth * (0.4 + crackAmt), abs(nCrack - 0.5));
                float crackMask = crackBand * crackAmt * 0.8;
                albedo = lerp(albedo, _CrackColor.rgb, crackMask);

                // ---- LIGHT SCORCH: mild patchy grime over the ALIVE half
                //      (0..0.5). Light darkening only — keeps the repairable
                //      look; the heavy char/fire is the death phase below. -----
                float sootVar    = FBM(IN.worldPos * _SootScale);
                float scorchAmt  = saturate(dmg / 0.5);
                float scorchMask = scorchAmt * smoothstep(0.55, 0.95, sootVar) * _ScorchMax;
                albedo = lerp(albedo, _SootColor.rgb, scorchMask);

                // ---- TRAVELLING BURN (death/collapse phase, _Damage > 0.5) -
                // Per-region ignition point: low-freq noise → each patch lights
                // at a different damage level. localT walks 0 (just lit) → 1
                // (fully charred) over _BurnDuration of damage after ignition.
                float ignite    = FBM(IN.worldPos * _BurnPatchScale);
                float igniteDmg = _BurnStart + ignite * _BurnRange;
                float localT    = saturate((dmg - igniteDmg) / _BurnDuration);
                // Fresh breaches always read as actively burning.
                localT = max(localT, holeEdge * holeAmt * 0.4);

                // Active fire window: flares up, holds, then dies as it chars.
                float fire = smoothstep(0.0, 0.15, localT) * (1.0 - smoothstep(0.45, 0.95, localT));

                // Char trails behind the fire and darkens the burned-out patch.
                float charAmt = smoothstep(0.30, 1.0, localT) * saturate(0.55 + sootVar) * _SootMax;
                albedo = lerp(albedo, _SootColor.rgb, charAmt);

                // ---- EMBERS: coals on the burn front, dying out into char --
                float emberN       = ValueNoise(IN.worldPos * _CrackScale * 1.6 + float3(0, -t * 0.5, 0));
                float emberFlicker = 0.5 + 0.5 * sin(t * 7.0 + emberN * 22.0);
                float emberGlow    = (fire * 0.9 + charAmt * (1.0 - charAmt) * 0.5) * emberFlicker;

                // ---- FLAMES: animated upward licks, only where burning ------
                float3 fp          = IN.worldPos * _FlameScale;
                float flameN       = FBM(fp + float3(0, -t * _FlameSpeed, 0));
                float flameTongue  = smoothstep(_FlameSharpness, 1.0, flameN);
                float flame        = flameTongue * fire;
                half3 flameCol     = lerp(_FlameTip.rgb, _FlameHot.rgb, saturate(flameN * 1.3));

                half3 emission = _EmberColor.rgb * emberGlow * _EmberIntensity
                               + flameCol * flame * _FlameIntensity;

                // ---- LIT PBR (matches scene) -------------------------------
                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.worldPos;
                inputData.normalWS                = normalize(IN.worldNormal);
                inputData.viewDirectionWS         = normalize(GetCameraPositionWS() - IN.worldPos);
                inputData.shadowCoord             = TransformWorldToShadowCoord(IN.worldPos);
                inputData.fogCoord                = IN.fogFactor;
                inputData.vertexLighting          = IN.vertexLight;
                inputData.bakedGI                 = SAMPLE_GI(IN.lightmapUV, IN.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                SurfaceData surfaceData         = (SurfaceData)0;
                surfaceData.albedo              = albedo;
                surfaceData.metallic            = _Metallic * (1.0 - charAmt);
                surfaceData.smoothness          = _Smoothness * (1.0 - max(charAmt, crackMask));
                surfaceData.normalTS            = half3(0, 0, 1);
                surfaceData.emission            = emission;
                surfaceData.occlusion           = 1;
                surfaceData.alpha               = 1;
                surfaceData.specular            = half3(0, 0, 0);
                surfaceData.clearCoatMask       = 0;
                surfaceData.clearCoatSmoothness = 0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }

    // Fallback supplies ShadowCaster / DepthOnly / DepthNormals passes.
    Fallback "Universal Render Pipeline/Lit"
}
