// ProceduralBuildingGenerator.cs
// Generates procedural building visuals from Unity primitives.
// Each building type gets a unique shape with culture-aware coloring.
// Location: Assets/Scripts/Presentation/ProceduralBuildingGenerator.cs

using UnityEngine;
using Unity.Entities;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Static factory for procedural building GameObjects.
    /// Era 1 buildings are smaller (~1.0x), Era 2 culture buildings are larger (~1.3-1.5x).
    /// Each building gets faction color on banner/roof and culture tone on walls/trim.
    /// </summary>
    public static class ProceduralBuildingGenerator
    {
        private static Shader _litShader;
        private static Shader LitShader =>
            _litShader ??= Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // ═══════════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Try to create a procedural building for the given PresentationId.
        /// Returns null if the ID is not a known building type handled here.
        /// </summary>
        public static GameObject TryCreate(int presentationId, Vector3 pos, Entity entity, byte culture)
        {
            GameObject result = presentationId switch
            {
                // Era 1 Core
                100 => CreateHall(pos, entity, culture),
                101 => CreateGatherersHut(pos, entity, culture),
                102 => CreateHut(pos, entity, culture),
                510 => CreateBarracks(pos, entity, culture),

                // Era 1 Advanced
                520 => CreateTemple(pos, entity, culture),       // ShrineOfAhridan
                521 => CreateTempleOfRidan(pos, entity, culture),
                530 => CreateVault(pos, entity, culture),        // VaultOfAlmierra
                540 => CreateKeep(pos, entity, culture),         // FiendstoneKeep

                // Runai culture buildings
                350 => CreateRunaiOutpost(pos, entity),
                351 => CreateRunaiTradeHub(pos, entity),
                352 => CreateRunaiBazaar(pos, entity),
                353 => CreateRunaiSiegeWorkshop(pos, entity),
                // 355 is shared between Runai_TradingPost and Alanthor_Garrison — use Create355() instead
                365 => CreateRunaiVault(pos, entity),
                366 => CreateRunaiVeilsteelFoundry(pos, entity),

                // Alanthor culture buildings
                354 => CreateAlanthorTower(pos, entity),
                356 => CreateAlanthorStable(pos, entity),
                357 => CreateAlanthorSiegeYard(pos, entity),
                363 => CreateKingsCourt(pos, entity),
                364 => CreateAlanthorCrucible(pos, entity),

                // Feraldis culture buildings
                358 => CreateFeraldisHuntingLodge(pos, entity),
                359 => CreateFeraldisLoggingStation(pos, entity),
                360 => CreateFeraldisLonghouse(pos, entity),
                361 => CreateFeraldisTotemTower(pos, entity),
                362 => CreateFeraldisSiegeYard(pos, entity),
                367 => CreateFeraldisFoundry(pos, entity),

                // Sect chapels (generic chapel shape)
                >= 390 and <= 401 => CreateChapel(pos, entity, presentationId, culture),

                // Sect uniques (one per sect — culture-typed by ID range)
                >= 410 and <= 421 => CreateSectUnique(pos, entity, presentationId),

                _ => null
            };

            // Wave 3/4 unified treatment: layer foundation + stripes + culture
            // lighting on culture-specific buildings whose Create methods don't
            // build their own. Era 1 buildings (Hall/Hut/GatherersHut/Barracks
            // and the four Era 2 choices) opt out by placing their own Foundation.
            if (result != null && presentationId >= 350 && presentationId <= 421)
            {
                byte tint = ResolveCultureForPresentationId(presentationId, culture);
                AutoDecorateExisting(result, tint);
            }
            return result;
        }

        /// <summary>
        /// For culture-specific building IDs the visual culture is known from the ID
        /// itself (e.g. 350-353/365-366 = Runai), so the auto-decorator can pick the
        /// right light tint regardless of the player faction's age-up state.
        /// </summary>
        private static byte ResolveCultureForPresentationId(int id, byte fallback)
        {
            // Runai: 350-353, 365-366, 352 (Bazaar)
            if ((id >= 350 && id <= 353) || id == 365 || id == 366) return Cultures.Runai;
            // Alanthor: 354, 356, 357, 363 (KingsCourt), 364 (Crucible)
            if (id == 354 || id == 356 || id == 357 || id == 363 || id == 364) return Cultures.Alanthor;
            // Feraldis: 358-362, 367
            if ((id >= 358 && id <= 362) || id == 367) return Cultures.Feraldis;
            // Sect uniques 410-421 follow the chapel mapping (4 per culture).
            if (id >= 410 && id <= 413) return Cultures.Alanthor;
            if (id >= 414 && id <= 417) return Cultures.Runai;
            if (id >= 418 && id <= 421) return Cultures.Feraldis;
            // 355 (shared) and chapels (390-401) defer to the player's culture.
            return fallback;
        }

        /// <summary>
        /// Create building for PresentationId 355 which is shared between
        /// Runai_TradingPost and Alanthor_Garrison. Caller determines which.
        /// </summary>
        public static GameObject Create355(Vector3 pos, Entity entity, bool isAlanthor)
        {
            return isAlanthor
                ? CreateAlanthorGarrison(pos, entity)
                : CreateRunaiTradingPost(pos, entity);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  MATERIAL HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        // Fix #203: MakeMat used to allocate a new Material per primitive.
        // Now delegates to ProceduralMaterialHelper (shared base + MPB).

        private static void SetMat(GameObject go, Color color, float metallic = 0f, float smoothness = 0.3f)
        {
            var r = go.GetComponent<Renderer>();
            ProceduralMaterialHelper.SetProperties(r, color, metallic, smoothness);
        }

        private static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }

        private static GameObject Prim(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Color color,
            float metallic = 0f, float smoothness = 0.3f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            SetMat(go, color, metallic, smoothness);
            DestroyCollider(go);
            return go;
        }

        private static GameObject PrimRot(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Quaternion localRot, Color color,
            float metallic = 0f, float smoothness = 0.3f)
        {
            var go = Prim(type, name, parent, localPos, localScale, color, metallic, smoothness);
            go.transform.localRotation = localRot;
            return go;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHARED: lights, emissive, animation helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Add a point light with optional emissive sphere "bulb" so it reads day and night.</summary>
        private static GameObject AddPointLight(Transform parent, string name, Vector3 localPos,
            Color color, float intensity = 2.5f, float range = 8f, float bulbScale = 0.18f)
        {
            var lightGo = new GameObject(name);
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = localPos;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;

            // Visible bulb so the source is identifiable in daylight too.
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            bulb.transform.SetParent(lightGo.transform, false);
            bulb.transform.localScale = Vector3.one * bulbScale;
            DestroyCollider(bulb);
            ApplyEmissive(bulb, color * 1.5f, baseColor: color);
            return lightGo;
        }

        /// <summary>Tint and self-illuminate a primitive (URP/Lit-compatible).</summary>
        private static void ApplyEmissive(GameObject go, Color emission, Color? baseColor = null)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mat = r.material;
            if (baseColor.HasValue)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor.Value);
                else if (mat.HasProperty("_Color")) mat.color = baseColor.Value;
            }
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
            }
        }

        private static BuildingPartAnimator AnimateRotate(GameObject go, Vector3 axis, float speed)
        {
            var a = go.AddComponent<BuildingPartAnimator>();
            a.Animation = BuildingPartAnimator.Mode.Rotate;
            a.Axis = axis;
            a.Speed = speed;
            return a;
        }

        private static BuildingPartAnimator AnimateSway(GameObject go, Vector3 axis, float amp, float freq)
        {
            var a = go.AddComponent<BuildingPartAnimator>();
            a.Animation = BuildingPartAnimator.Mode.Sway;
            a.Axis = axis;
            a.Amplitude = amp;
            a.Frequency = freq;
            return a;
        }

        private static BuildingPartAnimator AnimateBob(GameObject go, float amp, float freq)
        {
            var a = go.AddComponent<BuildingPartAnimator>();
            a.Animation = BuildingPartAnimator.Mode.Bob;
            a.BobAmplitude = amp;
            a.BobFrequency = freq;
            return a;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Shared building palette + decoration helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly Color HallStone     = new Color(0.55f, 0.53f, 0.50f, 1f); // weathered monolith
        private static readonly Color HallStoneDark = new Color(0.38f, 0.36f, 0.34f, 1f);
        private static readonly Color HallWood      = new Color(0.42f, 0.28f, 0.16f, 1f);
        private static readonly Color HallCanvas    = new Color(0.86f, 0.78f, 0.62f, 1f);
        private static readonly Color HallTorch     = new Color(1.00f, 0.65f, 0.30f, 1f);

        // Per-culture accent colours used by the shared decoration helpers.
        private static readonly Color CultureLightAlanthor = new Color(0.30f, 0.78f, 0.85f); // cyan
        private static readonly Color CultureLightRunai    = new Color(0.95f, 0.55f, 0.30f); // warm tent
        private static readonly Color CultureLightFeraldis = new Color(0.85f, 0.10f, 0.20f); // crimson

        /// <summary>Pick the culture-appropriate point-light tint (warm torch for None).</summary>
        private static Color GetCultureLight(byte culture) => culture switch
        {
            Cultures.Alanthor => CultureLightAlanthor,
            Cultures.Runai    => CultureLightRunai,
            Cultures.Feraldis => CultureLightFeraldis,
            _                 => HallTorch
        };

        /// <summary>
        /// Add a low monolithic stone plinth + 2 asymmetric corner monoliths under
        /// the building so it reads as "built upon ruins". Footprint extends slightly
        /// beyond the supplied size to peek out from under the geometry above.
        /// </summary>
        private static void AddRuinPlinth(Transform root, float sizeX, float sizeZ, float padding = 0.4f)
        {
            float w = sizeX + padding;
            float d = sizeZ + padding;

            Prim(PrimitiveType.Cube, "Foundation_Stone", root,
                new Vector3(0f, 0.10f, 0f), new Vector3(w, 0.20f, d), HallStone);

            // Two opposite corner monoliths at slightly different heights/yaw for ruined feel.
            float cx = w * 0.5f - 0.15f;
            float cz = d * 0.5f - 0.15f;
            PrimRot(PrimitiveType.Cube, "Monolith_BL", root,
                new Vector3(-cx, 0.65f, -cz), new Vector3(0.55f, 1.10f, 0.55f),
                Quaternion.Euler(0f, 8f, 0f), HallStoneDark);
            PrimRot(PrimitiveType.Cube, "Monolith_TR", root,
                new Vector3(cx, 0.55f, cz), new Vector3(0.50f, 0.90f, 0.50f),
                Quaternion.Euler(0f, -10f, 0f), HallStoneDark);
        }

        /// <summary>
        /// Add three stripe-named banner decals (front, left, right) sized to the
        /// building footprint — `ApplyFactionColor` auto-tints these with the
        /// owning faction's player colour so ownership reads from any angle.
        /// </summary>
        private static void AddPlayerDecals(Transform root, float sizeX, float sizeZ, float bannerHeight, float bannerY = 1.2f)
        {
            float halfX = sizeX * 0.5f;
            float halfZ = sizeZ * 0.5f;

            Prim(PrimitiveType.Cube, "Stripe_Front", root,
                new Vector3(0f, bannerY, halfZ + 0.03f),
                new Vector3(0.55f, bannerHeight, 0.04f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_Left", root,
                new Vector3(-halfX - 0.03f, bannerY, 0f),
                new Vector3(0.04f, bannerHeight, 0.55f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_Right", root,
                new Vector3(halfX + 0.03f, bannerY, 0f),
                new Vector3(0.04f, bannerHeight, 0.55f), Color.white);
        }

        /// <summary>
        /// Add corner point lights sized to the building footprint. Useful for buildings
        /// that just want even illumination — call AddBrazier separately for centerpieces.
        /// </summary>
        private static void AddCornerLights(Transform root, float sizeX, float sizeZ, float lightY,
            Color lightColor, float intensity = 1.8f, float range = 7f)
        {
            float cx = sizeX * 0.5f - 0.20f;
            float cz = sizeZ * 0.5f - 0.20f;
            AddPointLight(root, "Light_FL", new Vector3(-cx, lightY,  cz), lightColor, intensity, range, 0.10f);
            AddPointLight(root, "Light_FR", new Vector3( cx, lightY,  cz), lightColor, intensity, range, 0.10f);
            AddPointLight(root, "Light_BL", new Vector3(-cx, lightY, -cz), lightColor, intensity * 0.7f, range, 0.10f);
            AddPointLight(root, "Light_BR", new Vector3( cx, lightY, -cz), lightColor, intensity * 0.7f, range, 0.10f);
        }

        /// <summary>
        /// One-call decoration: ruin plinth + player stripes + corner lights tinted to
        /// the building's culture. Call at the end of an existing Create method to
        /// give it the unified treatment without redesigning its geometry.
        /// </summary>
        private static void DecorateBuilding(Transform root, byte culture,
            float sizeX, float sizeZ, float bannerY, float bannerHeight, float lightY)
        {
            AddRuinPlinth(root, sizeX, sizeZ);
            AddPlayerDecals(root, sizeX, sizeZ, bannerHeight, bannerY);
            AddCornerLights(root, sizeX + 0.3f, sizeZ + 0.3f, lightY, GetCultureLight(culture));
        }

        /// <summary>
        /// Auto-decorate an already-built procedural building: read bounds from its
        /// renderers, then add a stone plinth + player stripes + corner culture-lights
        /// sized to fit. Skips if the building already declares its own foundation
        /// (any child named with "Foundation" prefix) — this lets bespoke buildings
        /// like the Hall opt out without losing their hand-tuned plinth.
        /// </summary>
        public static void AutoDecorateExisting(GameObject root, byte culture)
        {
            if (root == null) return;
            // Skip if this building already has a hand-placed foundation.
            for (int i = 0; i < root.transform.childCount; i++)
            {
                if (root.transform.GetChild(i).name.StartsWith("Foundation"))
                    return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            // Compute aggregate world-space bounds, then localise.
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            Vector3 localMin = root.transform.InverseTransformPoint(b.min);
            Vector3 localMax = root.transform.InverseTransformPoint(b.max);
            Vector3 localCenter = (localMin + localMax) * 0.5f;
            float sizeX = Mathf.Max(0.5f, localMax.x - localMin.x);
            float sizeZ = Mathf.Max(0.5f, localMax.z - localMin.z);
            float topY = localMax.y;

            // Stone plinth slightly larger than the building footprint.
            float padW = sizeX + 0.5f;
            float padD = sizeZ + 0.5f;
            Prim(PrimitiveType.Cube, "Foundation_AutoStone", root.transform,
                new Vector3(localCenter.x, localMin.y - 0.05f, localCenter.z),
                new Vector3(padW, 0.20f, padD), HallStone);

            // 2 ruined monoliths at opposing corners.
            float cx = sizeX * 0.5f;
            float cz = sizeZ * 0.5f;
            PrimRot(PrimitiveType.Cube, "Monolith_AutoBL", root.transform,
                new Vector3(localCenter.x - cx, 0.55f, localCenter.z - cz),
                new Vector3(0.50f, 0.95f, 0.50f),
                Quaternion.Euler(0f, 8f, 0f), HallStoneDark);
            PrimRot(PrimitiveType.Cube, "Monolith_AutoTR", root.transform,
                new Vector3(localCenter.x + cx, 0.45f, localCenter.z + cz),
                new Vector3(0.45f, 0.80f, 0.45f),
                Quaternion.Euler(0f, -10f, 0f), HallStoneDark);

            // 3 stripe banners (front, left, right) at mid-height. Faction-tinted.
            float bannerY = Mathf.Max(0.8f, topY * 0.45f);
            float bannerH = Mathf.Min(0.9f, sizeX * 0.18f + 0.35f);
            Prim(PrimitiveType.Cube, "Stripe_Auto_Front", root.transform,
                new Vector3(localCenter.x, bannerY, localCenter.z + cz + 0.04f),
                new Vector3(Mathf.Min(0.7f, sizeX * 0.25f), bannerH, 0.04f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_Auto_Left", root.transform,
                new Vector3(localCenter.x - cx - 0.04f, bannerY, localCenter.z),
                new Vector3(0.04f, bannerH, Mathf.Min(0.7f, sizeZ * 0.25f)), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_Auto_Right", root.transform,
                new Vector3(localCenter.x + cx + 0.04f, bannerY, localCenter.z),
                new Vector3(0.04f, bannerH, Mathf.Min(0.7f, sizeZ * 0.25f)), Color.white);

            // Corner lights tinted to the building's culture.
            Color lightColor = GetCultureLight(culture);
            float lightY = Mathf.Min(topY * 0.7f + 0.5f, topY + 0.2f);
            float lcx = cx - 0.15f;
            float lcz = cz - 0.15f;
            AddPointLight(root.transform, "Light_AutoFL",
                new Vector3(localCenter.x - lcx, lightY, localCenter.z + lcz),
                lightColor, intensity: 1.8f, range: 7f, bulbScale: 0.10f);
            AddPointLight(root.transform, "Light_AutoFR",
                new Vector3(localCenter.x + lcx, lightY, localCenter.z + lcz),
                lightColor, intensity: 1.8f, range: 7f, bulbScale: 0.10f);
            AddPointLight(root.transform, "Light_AutoBL",
                new Vector3(localCenter.x - lcx, lightY, localCenter.z - lcz),
                lightColor, intensity: 1.4f, range: 6f, bulbScale: 0.10f);
            AddPointLight(root.transform, "Light_AutoBR",
                new Vector3(localCenter.x + lcx, lightY, localCenter.z - lcz),
                lightColor, intensity: 1.4f, range: 6f, bulbScale: 0.10f);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ERA 1 CORE BUILDINGS (smaller, ~1.0x scale)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Hall: a flat congregation of wood-and-canvas structures built atop monolithic
        /// stone ruins, around a gated inner courtyard. Footprint ~6x6 (radius ~3 from
        /// pivot). Culture overlays grow it taller with the requested cultural motifs.
        /// </summary>
        private static GameObject CreateHall(Vector3 pos, Entity entity, byte culture)
        {
            var root = new GameObject($"Hall_{entity.Index}");
            root.transform.position = pos;

            BuildHallBase(root.transform);

            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorHall(root.transform); break;
                case Cultures.Runai:    OverlayRunaiHall(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisHall(root.transform); break;
            }

            return root;
        }

        // ─── Cultureless base: ruins + wood + canvas around a gated courtyard ────
        private static void BuildHallBase(Transform root)
        {
            // Foundation: wide, low monolithic stone slab (whole footprint ~6x6).
            Prim(PrimitiveType.Cube, "Foundation_Stone", root,
                new Vector3(0f, 0.10f, 0f), new Vector3(6.4f, 0.20f, 6.4f), HallStone);

            // Inner courtyard floor (slightly recessed, lighter so it reads as open ground).
            Prim(PrimitiveType.Cube, "CourtyardFloor", root,
                new Vector3(0f, 0.18f, -0.4f), new Vector3(3.2f, 0.04f, 3.2f),
                new Color(0.50f, 0.46f, 0.40f));

            // Four corner monoliths — chunky, slightly rotated, distinct heights for ruined feel.
            float[] cornerH = { 1.6f, 1.3f, 1.7f, 1.4f };
            float[] cornerY = { 0f, 7f, -8f, 4f }; // small yaw tilt so they read as ancient
            int idx = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float h = cornerH[idx];
                var c = PrimRot(PrimitiveType.Cube, $"Monolith_{idx}", root,
                    new Vector3(sx * 2.6f, 0.20f + h * 0.5f, sz * 2.6f),
                    new Vector3(1.0f, h, 1.0f),
                    Quaternion.Euler(0f, cornerY[idx], 0f), HallStoneDark);
                idx++;
            }

            // Back ruined wall (one side of the perimeter is more intact).
            Prim(PrimitiveType.Cube, "RuinWall_Back", root,
                new Vector3(0f, 0.95f, -2.6f), new Vector3(4.2f, 1.5f, 0.6f), HallStone);
            // Side ruined walls — partial.
            Prim(PrimitiveType.Cube, "RuinWall_Left",  root,
                new Vector3(-2.6f, 0.85f,  0.0f), new Vector3(0.6f, 1.3f, 4.2f), HallStone);
            Prim(PrimitiveType.Cube, "RuinWall_Right", root,
                new Vector3( 2.6f, 0.85f,  0.0f), new Vector3(0.6f, 1.3f, 4.2f), HallStone);

            // Gate pillars — front, flanking the entrance. Slightly taller.
            Prim(PrimitiveType.Cube, "GatePillar_L", root,
                new Vector3(-1.4f, 1.20f, 2.6f), new Vector3(0.7f, 2.2f, 0.7f), HallStone);
            Prim(PrimitiveType.Cube, "GatePillar_R", root,
                new Vector3( 1.4f, 1.20f, 2.6f), new Vector3(0.7f, 2.2f, 0.7f), HallStone);

            // Wooden lintel beam connecting the two gate pillars.
            Prim(PrimitiveType.Cube, "GateLintel", root,
                new Vector3(0f, 2.40f, 2.6f), new Vector3(2.6f, 0.30f, 0.45f), HallWood);

            // Faction-coloured banner strip hanging from the lintel ("Stripe_*" gets player tint).
            Prim(PrimitiveType.Cube, "Stripe_GateBanner", root,
                new Vector3(0f, 1.95f, 2.78f), new Vector3(0.55f, 0.80f, 0.04f), Color.white);

            // Wood-and-canvas lean-to roofs along the back and sides ("Roof_*" auto-tinted).
            // The user wants the player colour visible — using "Roof" naming makes
            // ApplyFactionColor swap the canvas to the player's hue.
            PrimRot(PrimitiveType.Cube, "Roof_LeanTo_Back", root,
                new Vector3(0f, 1.85f, -1.85f), new Vector3(4.4f, 0.10f, 1.4f),
                Quaternion.Euler(20f, 0f, 0f), HallCanvas);
            PrimRot(PrimitiveType.Cube, "Roof_LeanTo_Left", root,
                new Vector3(-1.85f, 1.65f, 0f), new Vector3(1.4f, 0.10f, 4.4f),
                Quaternion.Euler(0f, 0f, -20f), HallCanvas);
            PrimRot(PrimitiveType.Cube, "Roof_LeanTo_Right", root,
                new Vector3( 1.85f, 1.65f, 0f), new Vector3(1.4f, 0.10f, 4.4f),
                Quaternion.Euler(0f, 0f, 20f), HallCanvas);

            // Wooden support beams under the lean-tos (4 thin posts).
            for (int i = 0; i < 4; i++)
            {
                float bx = (i % 2 == 0) ? -1.6f : 1.6f;
                float bz = (i < 2) ? -1.4f : 1.4f;
                Prim(PrimitiveType.Cylinder, $"Beam_{i}", root,
                    new Vector3(bx, 0.80f, bz), new Vector3(0.12f, 0.80f, 0.12f), HallWood);
            }

            // Small inner shrine / central wooden mast in courtyard.
            Prim(PrimitiveType.Cube, "ShrineBase", root,
                new Vector3(0f, 0.30f, -0.4f), new Vector3(0.9f, 0.20f, 0.9f), HallStoneDark);
            Prim(PrimitiveType.Cylinder, "ShrineMast", root,
                new Vector3(0f, 1.15f, -0.4f), new Vector3(0.10f, 0.85f, 0.10f), HallWood);

            // Side stripe banners for player ownership readability from any angle.
            Prim(PrimitiveType.Cube, "Stripe_LeftBanner", root,
                new Vector3(-2.93f, 1.30f, 0.5f), new Vector3(0.04f, 0.70f, 0.45f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_RightBanner", root,
                new Vector3( 2.93f, 1.30f, 0.5f), new Vector3(0.04f, 0.70f, 0.45f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_BackBanner", root,
                new Vector3(0f, 1.30f, -2.93f), new Vector3(0.45f, 0.70f, 0.04f), Color.white);

            // Two braziers at the gate, one inside the courtyard — point lights for night.
            AddBrazier(root, new Vector3(-1.4f,  0.75f,  3.0f), "Brazier_GateL");
            AddBrazier(root, new Vector3( 1.4f,  0.75f,  3.0f), "Brazier_GateR");
            AddBrazier(root, new Vector3( 0.0f,  0.75f, -0.4f), "Brazier_Court");
        }

        private static void AddBrazier(Transform root, Vector3 basePos, string name)
        {
            var bowl = Prim(PrimitiveType.Cylinder, name + "_Bowl", root,
                basePos, new Vector3(0.30f, 0.18f, 0.30f), HallStoneDark);
            // Glowing coal in the bowl + actual point light just above.
            var coal = Prim(PrimitiveType.Sphere, name + "_Coal", root,
                basePos + new Vector3(0f, 0.20f, 0f), new Vector3(0.28f, 0.18f, 0.28f), HallTorch);
            ApplyEmissive(coal, HallTorch * 2.5f, baseColor: HallTorch);
            AddPointLight(root, name + "_Light",
                basePos + new Vector3(0f, 0.50f, 0f), HallTorch, intensity: 2.2f, range: 7f, bulbScale: 0.0f);
        }

        // ─── Alanthor overlay: white marble + cyan-tile pagoda + cogwheels + Celestar ────
        private static void OverlayAlanthorHall(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);

            // Replace lean-to canvas with cyan-tiled curved roof segments (japanese pagoda).
            // Layered slightly-curved cubes per side, with an upturned ridge effect.
            AddPagodaTier(root, "PagodaBack",  new Vector3(0f, 2.35f, -1.85f), new Vector3(5.0f, 0.18f, 1.8f), 0f, cyanTile);
            AddPagodaTier(root, "PagodaLeft",  new Vector3(-1.85f, 2.20f, 0f), new Vector3(1.8f, 0.18f, 5.0f), 0f, cyanTile);
            AddPagodaTier(root, "PagodaRight", new Vector3( 1.85f, 2.20f, 0f), new Vector3(1.8f, 0.18f, 5.0f), 0f, cyanTile);

            // Marble round columns at the four perimeter midpoints (taller than gate pillars).
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f * Mathf.Deg2Rad;
                float cx = Mathf.Cos(angle) * 2.6f;
                float cz = Mathf.Sin(angle) * 2.6f;
                Prim(PrimitiveType.Cylinder, $"MarbleCol_{i}", root,
                    new Vector3(cx, 1.55f, cz), new Vector3(0.45f, 1.55f, 0.45f), marble, smoothness: 0.6f);
                // Capital
                Prim(PrimitiveType.Cube, $"MarbleCap_{i}", root,
                    new Vector3(cx, 3.18f, cz), new Vector3(0.65f, 0.18f, 0.65f), marble);
            }

            // Animated brass cogwheels on each side wall (rotating).
            AddCogwheel(root, "Cog_Left",  new Vector3(-2.96f, 1.80f, 1.50f), new Vector3(0f, 0f, 1f),  60f, brass);
            AddCogwheel(root, "Cog_Right", new Vector3( 2.96f, 1.80f, 1.50f), new Vector3(0f, 0f, 1f), -60f, brass);
            AddCogwheel(root, "Cog_Back",  new Vector3( 1.20f, 1.50f,-2.96f), new Vector3(0f, 0f, 1f),  45f, brass);

            // Central tower platform for the Celestar.
            Prim(PrimitiveType.Cylinder, "CelestarBase", root,
                new Vector3(0f, 2.65f, -0.4f), new Vector3(1.1f, 0.55f, 1.1f), marble, smoothness: 0.6f);
            Prim(PrimitiveType.Cylinder, "CelestarTower", root,
                new Vector3(0f, 4.10f, -0.4f), new Vector3(0.55f, 0.90f, 0.55f), marble, smoothness: 0.6f);
            // Telescope barrel on a swivel mount, tilted skyward, slowly tracking.
            var swivel = new GameObject("CelestarSwivel");
            swivel.transform.SetParent(root, false);
            swivel.transform.localPosition = new Vector3(0f, 5.10f, -0.4f);
            AnimateRotate(swivel, Vector3.up, 8f);
            PrimRot(PrimitiveType.Cylinder, "CelestarBarrel", swivel.transform,
                new Vector3(0.25f, 0.20f, 0.0f), new Vector3(0.20f, 0.85f, 0.20f),
                Quaternion.Euler(75f, 0f, 0f), brass, metallic: 0.7f, smoothness: 0.7f);
            // Lens — emissive cyan disc at the tip
            var lens = Prim(PrimitiveType.Cylinder, "CelestarLens", swivel.transform,
                new Vector3(0.55f, 0.75f, 0.0f), new Vector3(0.15f, 0.04f, 0.15f), cyanTile);
            ApplyEmissive(lens, cyanTile * 2.0f, baseColor: cyanTile);

            // Cyan-tinted lights at corners + a soft cool light on the Celestar base.
            AddPointLight(root, "Light_Corner_NE", new Vector3( 2.6f, 3.0f,  2.6f), cyanTile, intensity: 2.0f, range: 8f);
            AddPointLight(root, "Light_Corner_NW", new Vector3(-2.6f, 3.0f,  2.6f), cyanTile, intensity: 2.0f, range: 8f);
            AddPointLight(root, "Light_Celestar",  new Vector3( 0.0f, 4.6f, -0.4f), cyanTile, intensity: 3.0f, range: 10f, bulbScale: 0f);
        }

        private static void AddPagodaTier(Transform root, string name, Vector3 pos, Vector3 size, float rotZ, Color tileColor)
        {
            // Main flat roof slab
            PrimRot(PrimitiveType.Cube, "Roof_" + name, root,
                pos, size, Quaternion.Euler(0f, 0f, rotZ), tileColor);
            // Upturned ridge ends — small wedge cubes at each end
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            PrimRot(PrimitiveType.Cube, "Roof_" + name + "_TipA", root,
                pos + new Vector3(halfX * 0.95f, 0.15f, 0f),
                new Vector3(0.6f, 0.15f, size.z * 0.95f),
                Quaternion.Euler(0f, 0f, 18f), tileColor);
            PrimRot(PrimitiveType.Cube, "Roof_" + name + "_TipB", root,
                pos + new Vector3(-halfX * 0.95f, 0.15f, 0f),
                new Vector3(0.6f, 0.15f, size.z * 0.95f),
                Quaternion.Euler(0f, 0f, -18f), tileColor);
        }

        private static void AddCogwheel(Transform root, string name, Vector3 pos, Vector3 axis, float speed, Color color)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = pos;
            // Hub
            Prim(PrimitiveType.Cylinder, "Hub", pivot.transform,
                Vector3.zero, new Vector3(0.55f, 0.10f, 0.55f), color, metallic: 0.7f, smoothness: 0.5f);
            // Teeth — 8 small cubes around the rim
            for (int t = 0; t < 8; t++)
            {
                float a = t * 45f * Mathf.Deg2Rad;
                float tx = Mathf.Cos(a) * 0.55f;
                float ty = Mathf.Sin(a) * 0.55f;
                var tooth = Prim(PrimitiveType.Cube, $"Tooth{t}", pivot.transform,
                    new Vector3(tx, ty, 0f), new Vector3(0.18f, 0.18f, 0.12f), color, metallic: 0.7f);
                tooth.transform.localRotation = Quaternion.Euler(0f, 0f, t * 45f);
            }
            AnimateRotate(pivot, axis, speed);
        }

        // ─── Runai overlay: sandstone + teardrop domes + silk canopies + tethered balloon ────
        private static void OverlayRunaiHall(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);     // warm orange canopy
            var silk2 = new Color(0.20f, 0.60f, 0.75f);    // cool teal canopy
            var rope = new Color(0.55f, 0.40f, 0.22f);

            // Sandstone column thicken on corner monoliths (taller, rounded caps).
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -2.6f : 2.6f;
                float sz = (i < 2) ? -2.6f : 2.6f;
                Prim(PrimitiveType.Cylinder, $"SandPillar_{i}", root,
                    new Vector3(sx, 1.85f, sz), new Vector3(0.85f, 1.85f, 0.85f), sandstone);
                // Teardrop dome — sphere stretched vertically
                Prim(PrimitiveType.Sphere, $"Dome_{i}", root,
                    new Vector3(sx, 4.10f, sz), new Vector3(1.10f, 1.55f, 1.10f), silk2);
                // Pinnacle (the teardrop "tip")
                Prim(PrimitiveType.Cylinder, $"DomeTip_{i}", root,
                    new Vector3(sx, 5.05f, sz), new Vector3(0.10f, 0.40f, 0.10f), silk2);
            }

            // Silk canopies stretched between the corner pillars (animate sway).
            // Front canopy (open weave so courtyard remains visible).
            var canopyA = PrimRot(PrimitiveType.Cube, "Stripe_CanopyFront", root,
                new Vector3(0f, 3.35f, 2.0f), new Vector3(4.6f, 0.04f, 1.6f),
                Quaternion.Euler(8f, 0f, 0f), silk);
            AnimateSway(canopyA, Vector3.right, 4f, 0.3f);

            var canopyB = PrimRot(PrimitiveType.Cube, "Stripe_CanopyBack", root,
                new Vector3(0f, 3.45f, -2.0f), new Vector3(4.6f, 0.04f, 1.6f),
                Quaternion.Euler(-8f, 0f, 0f), silk);
            AnimateSway(canopyB, Vector3.right, 4f, 0.35f);

            var canopyL = PrimRot(PrimitiveType.Cube, "Stripe_CanopyLeft", root,
                new Vector3(-2.0f, 3.40f, 0f), new Vector3(1.6f, 0.04f, 4.6f),
                Quaternion.Euler(0f, 0f, 8f), silk2);
            AnimateSway(canopyL, Vector3.forward, 4f, 0.4f);

            var canopyR = PrimRot(PrimitiveType.Cube, "Stripe_CanopyRight", root,
                new Vector3( 2.0f, 3.40f, 0f), new Vector3(1.6f, 0.04f, 4.6f),
                Quaternion.Euler(0f, 0f, -8f), silk2);
            AnimateSway(canopyR, Vector3.forward, 4f, 0.45f);

            // Highest tower at one corner (rear-left), thinner and taller, for the balloon tether.
            Prim(PrimitiveType.Cylinder, "Watchtower", root,
                new Vector3(-2.6f, 3.00f, -2.6f), new Vector3(0.55f, 1.25f, 0.55f), sandstone);
            Prim(PrimitiveType.Cube, "WatchtowerTop", root,
                new Vector3(-2.6f, 4.40f, -2.6f), new Vector3(1.10f, 0.20f, 1.10f), sandstone);

            // Tether rope (thin tilted cylinder) from tower to balloon.
            PrimRot(PrimitiveType.Cylinder, "Tether", root,
                new Vector3(-1.6f, 5.50f, -1.6f), new Vector3(0.04f, 1.30f, 0.04f),
                Quaternion.Euler(0f, 45f, 35f), rope);

            // Hot-air balloon (envelope + basket), bobbing gently.
            var balloon = new GameObject("Balloon");
            balloon.transform.SetParent(root, false);
            balloon.transform.localPosition = new Vector3(-0.6f, 6.40f, -0.6f);
            AnimateBob(balloon, 0.18f, 0.25f);
            // Envelope: stretched sphere
            Prim(PrimitiveType.Sphere, "Envelope", balloon.transform,
                new Vector3(0f, 0.25f, 0f), new Vector3(1.40f, 1.70f, 1.40f), silk);
            // Faction stripe band on envelope
            Prim(PrimitiveType.Sphere, "Stripe_BalloonBand", balloon.transform,
                new Vector3(0f, 0.10f, 0f), new Vector3(1.42f, 0.30f, 1.42f), Color.white);
            // Basket
            Prim(PrimitiveType.Cube, "Basket", balloon.transform,
                new Vector3(0f, -0.65f, 0f), new Vector3(0.55f, 0.30f, 0.55f), rope);

            // Warm lights along the canopies for a tent-festival feel.
            AddPointLight(root, "Light_Front", new Vector3( 0.0f, 2.9f,  2.6f), silk,  intensity: 2.5f, range: 9f);
            AddPointLight(root, "Light_Back",  new Vector3( 0.0f, 2.9f, -2.6f), silk,  intensity: 2.0f, range: 8f);
            AddPointLight(root, "Light_Court", new Vector3( 0.0f, 2.0f, -0.4f), silk2, intensity: 1.8f, range: 6f);
            AddPointLight(root, "Light_Tower", new Vector3(-2.6f, 4.6f, -2.6f), silk,  intensity: 2.2f, range: 9f);
        }

        // ─── Feraldis overlay: iron + crimson tiers + central crystal spire + blood altars ────
        private static void OverlayFeraldisHall(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var obsidian = new Color(0.10f, 0.08f, 0.10f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);
            var blood = new Color(0.55f, 0.05f, 0.05f);

            // Replace canvas lean-tos visually with iron-tile slopes (dark crimson, metallic).
            PrimRot(PrimitiveType.Cube, "Roof_IronBack", root,
                new Vector3(0f, 2.10f, -1.85f), new Vector3(4.6f, 0.18f, 1.8f),
                Quaternion.Euler(20f, 0f, 0f), darkRed, metallic: 0.5f);
            PrimRot(PrimitiveType.Cube, "Roof_IronLeft", root,
                new Vector3(-1.85f, 2.00f, 0f), new Vector3(1.8f, 0.18f, 4.6f),
                Quaternion.Euler(0f, 0f, -20f), darkRed, metallic: 0.5f);
            PrimRot(PrimitiveType.Cube, "Roof_IronRight", root,
                new Vector3( 1.85f, 2.00f, 0f), new Vector3(1.8f, 0.18f, 4.6f),
                Quaternion.Euler(0f, 0f, 20f), darkRed, metallic: 0.5f);

            // Tiered fortress walls: a second tier rising above the base ring.
            Prim(PrimitiveType.Cube, "Tier2_Back", root,
                new Vector3(0f, 2.60f, -2.0f), new Vector3(3.6f, 1.6f, 0.9f), iron, metallic: 0.4f);
            Prim(PrimitiveType.Cube, "Tier2_Left", root,
                new Vector3(-2.0f, 2.60f, 0.0f), new Vector3(0.9f, 1.6f, 3.6f), iron, metallic: 0.4f);
            Prim(PrimitiveType.Cube, "Tier2_Right", root,
                new Vector3( 2.0f, 2.60f, 0.0f), new Vector3(0.9f, 1.6f, 3.6f), iron, metallic: 0.4f);

            // Crenellations — 6 per back tier
            for (int i = 0; i < 6; i++)
            {
                float x = -1.5f + i * 0.6f;
                Prim(PrimitiveType.Cube, $"Crenel_{i}", root,
                    new Vector3(x, 3.55f, -2.0f), new Vector3(0.30f, 0.40f, 0.90f), iron);
            }

            // Obsidian shard accents on tier corners.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                PrimRot(PrimitiveType.Cube, $"Obsidian_{sx}_{sz}", root,
                    new Vector3(sx * 1.9f, 3.55f, sz * 1.9f), new Vector3(0.35f, 0.85f, 0.35f),
                    Quaternion.Euler(0f, 35f * sx, 8f * sz), obsidian, metallic: 0.3f, smoothness: 0.85f);
            }

            // Central spire rising above the courtyard, stepped tiers.
            Prim(PrimitiveType.Cylinder, "SpireBase",  root,
                new Vector3(0f, 2.40f, -0.4f), new Vector3(1.30f, 0.80f, 1.30f), iron, metallic: 0.4f);
            Prim(PrimitiveType.Cylinder, "SpireMid",   root,
                new Vector3(0f, 3.60f, -0.4f), new Vector3(0.85f, 1.20f, 0.85f), iron, metallic: 0.4f);
            Prim(PrimitiveType.Cylinder, "SpireUpper", root,
                new Vector3(0f, 5.20f, -0.4f), new Vector3(0.50f, 1.40f, 0.50f), obsidian, metallic: 0.3f);

            // Crimson crystal at the apex — emissive.
            var crystalGo = PrimRot(PrimitiveType.Cube, "BloodCrystal", root,
                new Vector3(0f, 6.70f, -0.4f), new Vector3(0.55f, 1.20f, 0.55f),
                Quaternion.Euler(45f, 30f, 0f), crystal);
            ApplyEmissive(crystalGo, crystal * 2.2f, baseColor: crystal);

            // Blood altars: short stone slabs with red emissive top, scattered around.
            AddBloodAltar(root, new Vector3(-2.0f, 0.30f, 1.6f), iron, blood);
            AddBloodAltar(root, new Vector3( 2.0f, 0.30f, 1.6f), iron, blood);
            AddBloodAltar(root, new Vector3( 1.6f, 0.30f, -1.4f), iron, blood);

            // Crimson lighting — apex (strong) plus altar/courtyard glow.
            AddPointLight(root, "Light_Crystal", new Vector3(0f, 6.7f, -0.4f), crystal, intensity: 4.0f, range: 14f, bulbScale: 0f);
            AddPointLight(root, "Light_Altar_L", new Vector3(-2.0f, 0.7f,  1.6f), blood, intensity: 1.6f, range: 5f, bulbScale: 0f);
            AddPointLight(root, "Light_Altar_R", new Vector3( 2.0f, 0.7f,  1.6f), blood, intensity: 1.6f, range: 5f, bulbScale: 0f);
            AddPointLight(root, "Light_Altar_B", new Vector3( 1.6f, 0.7f, -1.4f), blood, intensity: 1.6f, range: 5f, bulbScale: 0f);
        }

        private static void AddBloodAltar(Transform root, Vector3 pos, Color stone, Color blood)
        {
            Prim(PrimitiveType.Cube, "AltarBase", root, pos,
                new Vector3(0.65f, 0.30f, 0.65f), stone, metallic: 0.3f);
            // Pool of blood (thin slab) on top — emissive
            var pool = Prim(PrimitiveType.Cylinder, "AltarPool", root,
                pos + new Vector3(0f, 0.18f, 0f),
                new Vector3(0.45f, 0.04f, 0.45f), blood);
            ApplyEmissive(pool, blood * 1.8f, baseColor: blood);
        }

        /// <summary>
        /// Hut: a small wood-and-canvas dwelling perched on a low monolithic stone
        /// footing. ~2.5×2.5 footprint. Culture overlays grow it taller in the same
        /// outline with their signature material (Alanthor pagoda, Runai dome+canopy,
        /// Feraldis crimson roof + obsidian).
        /// </summary>
        private static GameObject CreateHut(Vector3 pos, Entity entity, byte culture)
        {
            var root = new GameObject($"Hut_{entity.Index}");
            root.transform.position = pos;

            BuildHutBase(root.transform);

            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorHut(root.transform); break;
                case Cultures.Runai:    OverlayRunaiHut(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisHut(root.transform); break;
            }

            return root;
        }

        private static void BuildHutBase(Transform root)
        {
            // Stone footing (slightly oversized for a "ruin slab" reading).
            Prim(PrimitiveType.Cube, "Foundation_Stone", root,
                new Vector3(0f, 0.10f, 0f), new Vector3(2.6f, 0.20f, 2.6f), HallStone);
            // Two short ruined corner stones (asymmetric, slightly tilted).
            PrimRot(PrimitiveType.Cube, "Monolith_BL", root,
                new Vector3(-1.10f, 0.55f, -1.10f), new Vector3(0.50f, 0.90f, 0.50f),
                Quaternion.Euler(0f, 8f, 0f), HallStoneDark);
            PrimRot(PrimitiveType.Cube, "Monolith_TR", root,
                new Vector3( 1.10f, 0.45f,  1.10f), new Vector3(0.45f, 0.70f, 0.45f),
                Quaternion.Euler(0f, -10f, 0f), HallStoneDark);

            // Wood frame box (the actual hut body).
            Prim(PrimitiveType.Cube, "WoodWalls", root,
                new Vector3(0f, 0.85f, 0f), new Vector3(1.7f, 1.30f, 1.7f), HallWood);
            // Stripe-named canvas wrap so player colour reads on the body.
            Prim(PrimitiveType.Cube, "Stripe_BodyWrap", root,
                new Vector3(0f, 0.95f, 0.86f), new Vector3(1.3f, 0.30f, 0.04f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_BodyWrap_Back", root,
                new Vector3(0f, 0.95f, -0.86f), new Vector3(1.3f, 0.30f, 0.04f), Color.white);

            // Canvas pyramid roof (auto-tinted by ApplyFactionColor → Roof_*).
            PrimRot(PrimitiveType.Cube, "Roof_Canvas", root,
                new Vector3(0f, 1.85f, 0f), new Vector3(1.50f, 0.55f, 1.50f),
                Quaternion.Euler(0f, 45f, 45f), HallCanvas);

            // Door (front) and small entry step.
            Prim(PrimitiveType.Cube, "Door", root,
                new Vector3(0f, 0.65f, 0.86f), new Vector3(0.50f, 0.80f, 0.06f),
                new Color(0.30f, 0.20f, 0.10f));
            Prim(PrimitiveType.Cube, "DoorStep", root,
                new Vector3(0f, 0.22f, 1.05f), new Vector3(0.70f, 0.10f, 0.20f), HallStoneDark);

            // Single porch lantern at the door.
            AddPointLight(root, "Light_Porch",
                new Vector3(0.55f, 1.20f, 0.95f), HallTorch, intensity: 1.6f, range: 5f, bulbScale: 0.12f);
        }

        private static void OverlayAlanthorHut(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);

            // Replace canvas pyramid feel with a small cyan-tile hipped roof.
            PrimRot(PrimitiveType.Cube, "Roof_PagodaA", root,
                new Vector3(0f, 1.95f, 0f), new Vector3(1.95f, 0.16f, 1.95f),
                Quaternion.identity, cyanTile);
            // Up-tipped corner cubes for the pagoda silhouette.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                PrimRot(PrimitiveType.Cube, $"Roof_PagodaTip_{sx}_{sz}", root,
                    new Vector3(sx * 0.85f, 2.05f, sz * 0.85f),
                    new Vector3(0.45f, 0.10f, 0.45f),
                    Quaternion.Euler(0f, 0f, sx * 12f), cyanTile);
            }
            // Small marble pillar accents at front corners — taller than wood.
            Prim(PrimitiveType.Cylinder, "MarbleCol_L", root,
                new Vector3(-0.85f, 1.05f, 0.85f), new Vector3(0.20f, 1.05f, 0.20f), marble, smoothness: 0.6f);
            Prim(PrimitiveType.Cylinder, "MarbleCol_R", root,
                new Vector3( 0.85f, 1.05f, 0.85f), new Vector3(0.20f, 1.05f, 0.20f), marble, smoothness: 0.6f);
            // Tiny rotating cogwheel on the side wall (Alanthor steampunk signature).
            AddCogwheel(root, "Cog_Side",
                new Vector3(-0.95f, 1.05f, 0f), new Vector3(0f, 0f, 1f), 70f, new Color(0.78f, 0.62f, 0.30f));
            // Cool cyan eave light.
            AddPointLight(root, "Light_Eave_L", new Vector3(-0.85f, 2.0f, 0.85f), cyanTile, intensity: 1.6f, range: 5f);
            AddPointLight(root, "Light_Eave_R", new Vector3( 0.85f, 2.0f, 0.85f), cyanTile, intensity: 1.6f, range: 5f);
        }

        private static void OverlayRunaiHut(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);

            // Sandstone ring around the wood walls (thicken the base).
            Prim(PrimitiveType.Cube, "Sand_Front", root,
                new Vector3(0f, 0.75f, 1.0f), new Vector3(2.10f, 1.20f, 0.20f), sandstone);
            Prim(PrimitiveType.Cube, "Sand_Back", root,
                new Vector3(0f, 0.75f, -1.0f), new Vector3(2.10f, 1.20f, 0.20f), sandstone);
            // Teardrop dome on top.
            Prim(PrimitiveType.Sphere, "Dome", root,
                new Vector3(0f, 2.30f, 0f), new Vector3(1.50f, 1.80f, 1.50f), sandstone);
            Prim(PrimitiveType.Cylinder, "DomeTip", root,
                new Vector3(0f, 3.30f, 0f), new Vector3(0.10f, 0.35f, 0.10f), sandstone);
            // Swaying silk awning over the door (faction-stripe coloured).
            var awning = PrimRot(PrimitiveType.Cube, "Stripe_Awning", root,
                new Vector3(0f, 1.65f, 1.20f), new Vector3(1.80f, 0.04f, 0.85f),
                Quaternion.Euler(15f, 0f, 0f), silk);
            AnimateSway(awning, Vector3.right, 5f, 0.35f);
            // Warm tent light.
            AddPointLight(root, "Light_Front", new Vector3(0f, 2.1f, 1.1f), silk, intensity: 1.8f, range: 6f);
        }

        private static void OverlayFeraldisHut(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);

            // Iron-tile crimson roof replaces canvas pyramid (steeper, double-pitched).
            PrimRot(PrimitiveType.Cube, "Roof_IronA", root,
                new Vector3(0f, 1.85f, 0.45f), new Vector3(1.90f, 0.16f, 1.20f),
                Quaternion.Euler(28f, 0f, 0f), darkRed, metallic: 0.4f);
            PrimRot(PrimitiveType.Cube, "Roof_IronB", root,
                new Vector3(0f, 1.85f, -0.45f), new Vector3(1.90f, 0.16f, 1.20f),
                Quaternion.Euler(-28f, 0f, 0f), darkRed, metallic: 0.4f);
            // Small fortress band of iron crenellations at gutter level.
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.55f;
                Prim(PrimitiveType.Cube, $"Crenel_{i}", root,
                    new Vector3(x, 1.65f, 0.95f), new Vector3(0.20f, 0.25f, 0.10f), iron);
            }
            // A tiny crimson crystal embedded in the roof apex (signature).
            var shard = PrimRot(PrimitiveType.Cube, "BloodShard", root,
                new Vector3(0f, 2.20f, 0f), new Vector3(0.22f, 0.45f, 0.22f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 1.8f, baseColor: crystal);
            // Crimson glow.
            AddPointLight(root, "Light_Apex", new Vector3(0f, 2.2f, 0f), crystal, intensity: 2.0f, range: 6f, bulbScale: 0f);
        }

        /// <summary>
        /// GatherersHut: A circular hut raised off the ground by wooden stakes,
        /// with a stepped truncated-cone (flat-top conical) roof, a three-step staircase
        /// leading up to a wooden door, and a small fence enclosing the plot.
        /// Footprint: ~3x3 units. Height: ~2.2 units.
        /// </summary>
        private static GameObject CreateGatherersHut(Vector3 pos, Entity entity, byte culture)
        {
            // Culture colours for walls, roof, and trim details
            var wall  = CultureConfig.GetWallColor(culture);
            var roof  = CultureConfig.GetRoofColor(culture);
            var trim  = CultureConfig.GetTrimColor(culture);

            // Warm brown used for all wooden parts (stakes, door panel)
            var wood  = new Color(0.45f, 0.30f, 0.15f);
            // Slightly lighter brown for the fence so it reads separately
            var fenceCol = new Color(0.55f, 0.40f, 0.22f);

            var root = new GameObject($"GatherersHut_{entity.Index}");
            root.transform.position = pos;

            // ── 1. SUPPORT STAKES ──────────────────────────────────────────────
            // Six thin wooden cylinders arranged in a ring lift the hut body off
            // the ground.  Each runs from Y=0 (ground) to Y=0.56 (under the floor).
            // We centre each cylinder at half its height (0.28) so it sits on the ground.
            const int   stakeCount  = 6;
            const float stakeRadius = 0.58f;
            for (int i = 0; i < stakeCount; i++)
            {
                float a  = (i / (float)stakeCount) * Mathf.PI * 2f;
                float sx = Mathf.Sin(a) * stakeRadius;
                float sz = Mathf.Cos(a) * stakeRadius;
                Prim(PrimitiveType.Cylinder, $"Stake{i}", root.transform,
                    new Vector3(sx, 0.28f, sz),
                    new Vector3(0.09f, 0.28f, 0.09f),
                    wood);
            }

            // ── 2. RAISED FLOOR PLATFORM ───────────────────────────────────────
            // A flat disc (very short cylinder) sits on top of the stakes and
            // forms the elevated floor of the hut.  Top of floor = Y 0.66.
            Prim(PrimitiveType.Cylinder, "Floor", root.transform,
                new Vector3(0f, 0.60f, 0f),
                new Vector3(1.60f, 0.06f, 1.60f),
                trim);

            // ── 3. CIRCULAR WALLS ──────────────────────────────────────────────
            // A cylinder sits on the floor platform and forms the outer walls.
            // Wall bottom = 0.66 (floor top), wall top ≈ 1.50.
            // The front face of this cylinder (the wall surface) is at Z ≈ 0.74.
            Prim(PrimitiveType.Cylinder, "Walls", root.transform,
                new Vector3(0f, 1.08f, 0f),
                new Vector3(1.48f, 0.42f, 1.48f),
                wall);

            // ── 4. CONICAL FLAT-TOP ROOF (stepped tiers) ───────────────────────
            // Unity has no cone primitive, so we stack cylinders of decreasing
            // diameter to fake a tapered cone shape, then cap it with a small
            // flat disc — giving the "truncated cone / flat-top conical" look.
            // Tier 0 is the widest eave, tier 3 and the cap form the flat top.
            Prim(PrimitiveType.Cylinder, "RoofTier0", root.transform,
                new Vector3(0f, 1.56f, 0f), new Vector3(1.72f, 0.07f, 1.72f), roof);
            Prim(PrimitiveType.Cylinder, "RoofTier1", root.transform,
                new Vector3(0f, 1.72f, 0f), new Vector3(1.40f, 0.09f, 1.40f), roof);
            Prim(PrimitiveType.Cylinder, "RoofTier2", root.transform,
                new Vector3(0f, 1.88f, 0f), new Vector3(1.08f, 0.09f, 1.08f), roof);
            Prim(PrimitiveType.Cylinder, "RoofTier3", root.transform,
                new Vector3(0f, 2.04f, 0f), new Vector3(0.76f, 0.09f, 0.76f), roof);
            // Flat cap — the defining feature of a truncated-cone (flat-top) roof
            Prim(PrimitiveType.Cylinder, "RoofCap", root.transform,
                new Vector3(0f, 2.18f, 0f), new Vector3(0.52f, 0.05f, 0.52f), roof);

            // ── 5. STAIRCASE ───────────────────────────────────────────────────
            // Three cube steps lead up from the ground to the raised floor.
            // +Z is the front of the building (where the door is).
            // Rise per step = 0.22 units; tread depth = 0.20 units.
            // Step 3's back face lands exactly at Z=0.74 — the front wall surface.
            //
            // Step 1 — bottom, sits on the ground
            Prim(PrimitiveType.Cube, "Step1", root.transform,
                new Vector3(0f, 0.11f, 1.24f), new Vector3(0.70f, 0.22f, 0.20f), trim);
            // Step 2 — middle
            Prim(PrimitiveType.Cube, "Step2", root.transform,
                new Vector3(0f, 0.33f, 1.04f), new Vector3(0.65f, 0.22f, 0.20f), trim);
            // Step 3 — top landing, level with the floor
            Prim(PrimitiveType.Cube, "Step3", root.transform,
                new Vector3(0f, 0.55f, 0.84f), new Vector3(0.60f, 0.22f, 0.20f), trim);

            // ── 6. WOODEN DOOR ─────────────────────────────────────────────────
            // A trim-coloured frame surrounds a wood-coloured door panel.
            // Door centre height: floor top (0.66) + half door height (0.275) = 0.94.
            // Both sit at the front wall surface (Z ≈ 0.73–0.74).
            Prim(PrimitiveType.Cube, "DoorFrame", root.transform,
                new Vector3(0f, 0.94f, 0.73f), new Vector3(0.56f, 0.65f, 0.07f), trim);
            Prim(PrimitiveType.Cube, "Door", root.transform,
                new Vector3(0f, 0.94f, 0.74f), new Vector3(0.42f, 0.53f, 0.06f), wood);

            // ── 7. FENCE ENCLOSURE ─────────────────────────────────────────────
            // Eight evenly-spaced posts around a circle of radius 1.45.
            // Post #0 (directly in front, at +Z) is skipped to leave an entrance gap.
            // Horizontal rails connect each adjacent pair of placed posts.
            const int   postCount = 8;
            const float fenceR    = 1.45f;
            const float postH     = 0.38f;   // full height of a fence post

            // Posts: skip i=0 (the front entrance gap)
            for (int i = 1; i < postCount; i++)
            {
                float a  = (i / (float)postCount) * Mathf.PI * 2f;
                float px = Mathf.Sin(a) * fenceR;
                float pz = Mathf.Cos(a) * fenceR;
                // Cylinder centre is at half its height so it stands on the ground
                Prim(PrimitiveType.Cylinder, $"FencePost{i}", root.transform,
                    new Vector3(px, postH * 0.5f, pz),
                    new Vector3(0.07f, postH * 0.5f, 0.07f),
                    fenceCol);
            }

            // Rails: connect post i to post i+1, skipping the two segments
            // that would bridge across the entrance gap (segments 0 and 7).
            // The chord length between adjacent posts on the circle is
            //   2 * R * sin(π / postCount).
            float railLen = 2f * fenceR * Mathf.Sin(Mathf.PI / postCount);

            for (int i = 1; i < postCount - 1; i++)
            {
                int   next     = i + 1;
                float a1       = (i    / (float)postCount) * Mathf.PI * 2f;
                float a2       = (next / (float)postCount) * Mathf.PI * 2f;

                // World-space midpoint of the chord (relative to root)
                float mx = (Mathf.Sin(a1) + Mathf.Sin(a2)) * 0.5f * fenceR;
                float mz = (Mathf.Cos(a1) + Mathf.Cos(a2)) * 0.5f * fenceR;

                // At the midpoint angle the tangent direction is (cos θ, 0, -sin θ).
                // A Y-rotation of -θ (in degrees) maps local-X onto that tangent.
                float   midAngle = (a1 + a2) * 0.5f;
                Quaternion rot   = Quaternion.Euler(0f, -midAngle * Mathf.Rad2Deg, 0f);

                // Top rail (near post top)
                var topRail = Prim(PrimitiveType.Cube, $"RailTop{i}", root.transform,
                    new Vector3(mx, postH * 0.88f, mz),
                    new Vector3(railLen, 0.05f, 0.05f),
                    fenceCol);
                topRail.transform.localRotation = rot;

                // Bottom rail (lower on the post)
                var botRail = Prim(PrimitiveType.Cube, $"RailBot{i}", root.transform,
                    new Vector3(mx, postH * 0.40f, mz),
                    new Vector3(railLen, 0.05f, 0.05f),
                    fenceCol);
                botRail.transform.localRotation = rot;
            }

            // ── 8. PLAYER STRIPES + PORCH LIGHT (cultureless base) ───────────
            // Door banner + side cloth stripes ("Stripe_*" gets player tint).
            Prim(PrimitiveType.Cube, "Stripe_DoorBanner", root.transform,
                new Vector3(0f, 1.55f, 0.78f), new Vector3(0.40f, 0.55f, 0.04f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_LeftBanner", root.transform,
                new Vector3(-0.78f, 1.20f, 0.0f), new Vector3(0.04f, 0.40f, 0.30f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_RightBanner", root.transform,
                new Vector3( 0.78f, 1.20f, 0.0f), new Vector3(0.04f, 0.40f, 0.30f), Color.white);

            // Porch lantern by the door for night visibility.
            AddPointLight(root.transform, "Light_Porch",
                new Vector3(0.55f, 1.40f, 0.85f), HallTorch, intensity: 1.6f, range: 5f, bulbScale: 0.10f);

            // ── 9. CULTURE OVERLAY ───────────────────────────────────────────
            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorGatherersHut(root.transform); break;
                case Cultures.Runai:    OverlayRunaiGatherersHut(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisGatherersHut(root.transform); break;
            }

            return root;
        }

        private static void OverlayAlanthorGatherersHut(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);

            // Cyan-tile pagoda cap on top of the conical roof.
            AddPagodaTier(root, "GhPagoda",
                new Vector3(0f, 2.30f, 0f), new Vector3(2.10f, 0.16f, 2.10f), 0f, cyanTile);

            // Slim marble pillars around the perimeter (taller than the wood walls).
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad + Mathf.PI * 0.25f; // diagonal placement
                float cx = Mathf.Cos(a) * 1.45f;
                float cz = Mathf.Sin(a) * 1.45f;
                Prim(PrimitiveType.Cylinder, $"MarbleCol_{i}", root,
                    new Vector3(cx, 1.05f, cz), new Vector3(0.18f, 1.05f, 0.18f), marble, smoothness: 0.6f);
            }

            // Single rotating cogwheel mounted on the wall.
            AddCogwheel(root, "Cog_Side",
                new Vector3(0f, 1.20f, -0.85f), new Vector3(0f, 0f, 1f), 65f, brass);

            AddPointLight(root, "Light_Eave_F", new Vector3(0f, 2.20f, 1.10f), cyanTile, intensity: 1.6f, range: 6f);
            AddPointLight(root, "Light_Eave_B", new Vector3(0f, 2.20f, -1.10f), cyanTile, intensity: 1.6f, range: 6f);
        }

        private static void OverlayRunaiGatherersHut(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);

            // Replace conical roof read with a teardrop dome on top.
            Prim(PrimitiveType.Sphere, "Dome", root,
                new Vector3(0f, 2.55f, 0f), new Vector3(1.55f, 1.85f, 1.55f), sandstone);
            Prim(PrimitiveType.Cylinder, "DomeTip", root,
                new Vector3(0f, 3.55f, 0f), new Vector3(0.10f, 0.40f, 0.10f), sandstone);

            // Swaying silk awning over the entrance staircase.
            var awning = PrimRot(PrimitiveType.Cube, "Stripe_Awning", root,
                new Vector3(0f, 1.85f, 1.10f), new Vector3(2.00f, 0.04f, 1.20f),
                Quaternion.Euler(15f, 0f, 0f), silk);
            AnimateSway(awning, Vector3.right, 4f, 0.32f);

            AddPointLight(root, "Light_Front", new Vector3(0f, 2.30f, 1.10f), silk, intensity: 1.8f, range: 6f);
        }

        private static void OverlayFeraldisGatherersHut(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);

            // Iron crimson dome cap covers the conical roof.
            Prim(PrimitiveType.Sphere, "IronCap", root,
                new Vector3(0f, 2.45f, 0f), new Vector3(1.85f, 1.10f, 1.85f), darkRed, metallic: 0.5f);
            // Crenellation belt at gutter level.
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float cx = Mathf.Sin(a) * 1.55f;
                float cz = Mathf.Cos(a) * 1.55f;
                Prim(PrimitiveType.Cube, $"Crenel_{i}", root,
                    new Vector3(cx, 2.05f, cz), new Vector3(0.18f, 0.30f, 0.18f), iron);
            }
            // Crimson crystal pinnacle.
            var shard = PrimRot(PrimitiveType.Cube, "BloodShard", root,
                new Vector3(0f, 3.30f, 0f), new Vector3(0.30f, 0.65f, 0.30f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 2.0f, baseColor: crystal);

            AddPointLight(root, "Light_Apex", new Vector3(0f, 3.30f, 0f), crystal, intensity: 2.2f, range: 7f, bulbScale: 0f);
        }

        /// <summary>
        /// Barracks: a long timber drill hall raised on a monolithic stone footing,
        /// with an open weapon yard at the front (training dummies, weapon rack).
        /// ~5×3 footprint. Culture overlays grow it taller in the same outline.
        /// </summary>
        private static GameObject CreateBarracks(Vector3 pos, Entity entity, byte culture)
        {
            var root = new GameObject($"Barracks_{entity.Index}");
            root.transform.position = pos;

            BuildBarracksBase(root.transform);

            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorBarracks(root.transform); break;
                case Cultures.Runai:    OverlayRunaiBarracks(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisBarracks(root.transform); break;
            }

            return root;
        }

        private static void BuildBarracksBase(Transform root)
        {
            // Stone footing.
            Prim(PrimitiveType.Cube, "Foundation_Stone", root,
                new Vector3(0f, 0.10f, 0f), new Vector3(5.2f, 0.20f, 3.2f), HallStone);
            // Two ruined corner blocks at the back to read as ancient masonry.
            PrimRot(PrimitiveType.Cube, "Monolith_BL", root,
                new Vector3(-2.30f, 0.85f, -1.30f), new Vector3(0.65f, 1.50f, 0.65f),
                Quaternion.Euler(0f, 6f, 0f), HallStoneDark);
            PrimRot(PrimitiveType.Cube, "Monolith_BR", root,
                new Vector3( 2.30f, 0.75f, -1.30f), new Vector3(0.60f, 1.30f, 0.60f),
                Quaternion.Euler(0f, -8f, 0f), HallStoneDark);

            // Long timber drill-hall body, set toward the back of the slab so a yard
            // sits in front of it.
            Prim(PrimitiveType.Cube, "TimberHall", root,
                new Vector3(0f, 1.05f, -0.55f), new Vector3(4.4f, 1.70f, 1.7f), HallWood);

            // Canvas-and-wood gable roof (auto-tinted by ApplyFactionColor).
            PrimRot(PrimitiveType.Cube, "Roof_GableA", root,
                new Vector3(0f, 2.10f, -0.25f), new Vector3(4.7f, 0.16f, 1.30f),
                Quaternion.Euler(22f, 0f, 0f), HallCanvas);
            PrimRot(PrimitiveType.Cube, "Roof_GableB", root,
                new Vector3(0f, 2.10f, -0.85f), new Vector3(4.7f, 0.16f, 1.30f),
                Quaternion.Euler(-22f, 0f, 0f), HallCanvas);

            // Wide doorway (front of the timber hall).
            Prim(PrimitiveType.Cube, "Door", root,
                new Vector3(0f, 0.85f, 0.32f), new Vector3(1.10f, 1.40f, 0.10f),
                new Color(0.30f, 0.20f, 0.10f));

            // Faction stripes: long banners running along each side, plus one over the door.
            Prim(PrimitiveType.Cube, "Stripe_DoorBanner", root,
                new Vector3(0f, 1.75f, 0.34f), new Vector3(0.65f, 0.55f, 0.04f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_SideL", root,
                new Vector3(-2.23f, 1.30f, -0.55f), new Vector3(0.04f, 0.55f, 1.50f), Color.white);
            Prim(PrimitiveType.Cube, "Stripe_SideR", root,
                new Vector3( 2.23f, 1.30f, -0.55f), new Vector3(0.04f, 0.55f, 1.50f), Color.white);

            // Open weapon yard in front of the hall — training dummies + weapon rack.
            // Yard ground (lighter slab so it reads as packed dirt).
            Prim(PrimitiveType.Cube, "YardFloor", root,
                new Vector3(0f, 0.18f, 1.20f), new Vector3(4.0f, 0.04f, 1.6f),
                new Color(0.50f, 0.46f, 0.40f));

            // Two training dummies (post + cross bar + cloth torso).
            AddTrainingDummy(root, new Vector3(-1.40f, 0f, 1.30f));
            AddTrainingDummy(root, new Vector3( 1.40f, 0f, 1.30f));

            // Weapon rack at the right of the yard.
            Prim(PrimitiveType.Cube, "RackPost_L", root,
                new Vector3(2.35f, 0.55f, 1.50f), new Vector3(0.10f, 0.85f, 0.10f), HallWood);
            Prim(PrimitiveType.Cube, "RackPost_R", root,
                new Vector3(2.85f, 0.55f, 1.50f), new Vector3(0.10f, 0.85f, 0.10f), HallWood);
            Prim(PrimitiveType.Cube, "RackBar", root,
                new Vector3(2.60f, 0.95f, 1.50f), new Vector3(0.65f, 0.07f, 0.07f), HallWood);
            // Weapons leaning on the rack.
            for (int i = 0; i < 3; i++)
            {
                PrimRot(PrimitiveType.Cylinder, $"Weapon{i}", root,
                    new Vector3(2.40f + i * 0.20f, 0.65f, 1.50f),
                    new Vector3(0.05f, 0.55f, 0.05f),
                    Quaternion.Euler(0f, 0f, -10f), new Color(0.5f, 0.5f, 0.55f), 0.6f, 0.5f);
            }

            // Two yard braziers + door lantern.
            AddBrazier(root, new Vector3(-1.95f, 0.30f, 1.85f), "Brazier_YardL");
            AddBrazier(root, new Vector3( 1.95f, 0.30f, 1.85f), "Brazier_YardR");
            AddPointLight(root, "Light_Door",
                new Vector3(0f, 1.85f, 0.45f), HallTorch, intensity: 1.8f, range: 6f, bulbScale: 0.10f);
        }

        private static void AddTrainingDummy(Transform root, Vector3 basePos)
        {
            // Post
            Prim(PrimitiveType.Cylinder, "DummyPost", root,
                basePos + new Vector3(0f, 0.55f, 0f), new Vector3(0.12f, 0.55f, 0.12f), HallWood);
            // Cross bar (arms)
            Prim(PrimitiveType.Cube, "DummyArms", root,
                basePos + new Vector3(0f, 0.95f, 0f), new Vector3(0.65f, 0.08f, 0.08f), HallWood);
            // Cloth torso (auto-tinted as Stripe so player colour shows)
            Prim(PrimitiveType.Cube, "Stripe_DummyTorso", root,
                basePos + new Vector3(0f, 1.05f, 0f), new Vector3(0.30f, 0.45f, 0.20f), Color.white);
            // Helmet ball
            Prim(PrimitiveType.Sphere, "DummyHead", root,
                basePos + new Vector3(0f, 1.35f, 0f), new Vector3(0.25f, 0.25f, 0.25f),
                new Color(0.45f, 0.45f, 0.50f), 0.5f, 0.5f);
        }

        private static void OverlayAlanthorBarracks(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);

            // Cyan pagoda roof along the length of the timber hall.
            AddPagodaTier(root, "BarracksPagoda",
                new Vector3(0f, 2.45f, -0.55f), new Vector3(5.0f, 0.18f, 2.0f), 0f, cyanTile);

            // Marble columns at the four corners of the timber hall (taller).
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Prim(PrimitiveType.Cylinder, $"MarbleCol_{sx}_{sz}", root,
                    new Vector3(sx * 2.05f, 1.40f, -0.55f + sz * 0.75f),
                    new Vector3(0.30f, 1.40f, 0.30f), marble, smoothness: 0.6f);
            }

            // Two animated cogwheels on the long side (drilling automaton vibe).
            AddCogwheel(root, "Cog_DrillL", new Vector3(-1.20f, 1.40f, 0.85f), new Vector3(0f, 0f, 1f), 80f, brass);
            AddCogwheel(root, "Cog_DrillR", new Vector3( 1.20f, 1.40f, 0.85f), new Vector3(0f, 0f, 1f), -80f, brass);

            // Cool cyan eave lights along the long side.
            AddPointLight(root, "Light_Eave_L", new Vector3(-1.80f, 2.45f, 0.30f), cyanTile, intensity: 1.8f, range: 7f);
            AddPointLight(root, "Light_Eave_R", new Vector3( 1.80f, 2.45f, 0.30f), cyanTile, intensity: 1.8f, range: 7f);
        }

        private static void OverlayRunaiBarracks(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);
            var silk2 = new Color(0.20f, 0.60f, 0.75f);

            // Two sandstone teardrop domes flanking the timber hall.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Prim(PrimitiveType.Cylinder, $"SandPillar_{sx}", root,
                    new Vector3(sx * 1.95f, 1.60f, -0.55f), new Vector3(0.55f, 1.60f, 0.55f), sandstone);
                Prim(PrimitiveType.Sphere, $"Dome_{sx}", root,
                    new Vector3(sx * 1.95f, 3.55f, -0.55f), new Vector3(0.95f, 1.30f, 0.95f), silk2);
                Prim(PrimitiveType.Cylinder, $"DomeTip_{sx}", root,
                    new Vector3(sx * 1.95f, 4.35f, -0.55f), new Vector3(0.08f, 0.32f, 0.08f), silk2);
            }

            // Big swaying silk shade over the weapon yard (faction-stripe coloured).
            var canopy = PrimRot(PrimitiveType.Cube, "Stripe_YardCanopy", root,
                new Vector3(0f, 2.45f, 1.20f), new Vector3(4.30f, 0.04f, 2.20f),
                Quaternion.Euler(10f, 0f, 0f), silk);
            AnimateSway(canopy, Vector3.right, 4f, 0.30f);

            // Warm tent lights at the canopy ends.
            AddPointLight(root, "Light_CanopyL", new Vector3(-1.80f, 2.10f, 1.40f), silk, intensity: 2.0f, range: 7f);
            AddPointLight(root, "Light_CanopyR", new Vector3( 1.80f, 2.10f, 1.40f), silk, intensity: 2.0f, range: 7f);
        }

        private static void OverlayFeraldisBarracks(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var obsidian = new Color(0.10f, 0.08f, 0.10f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);
            var blood = new Color(0.55f, 0.05f, 0.05f);

            // Replace canvas gables with iron-tile dark-red roof.
            PrimRot(PrimitiveType.Cube, "Roof_IronA", root,
                new Vector3(0f, 2.20f, -0.25f), new Vector3(4.8f, 0.18f, 1.30f),
                Quaternion.Euler(22f, 0f, 0f), darkRed, metallic: 0.5f);
            PrimRot(PrimitiveType.Cube, "Roof_IronB", root,
                new Vector3(0f, 2.20f, -0.85f), new Vector3(4.8f, 0.18f, 1.30f),
                Quaternion.Euler(-22f, 0f, 0f), darkRed, metallic: 0.5f);

            // Crenellated battlements along the front of the hall.
            for (int i = 0; i < 6; i++)
            {
                float x = -2.0f + i * 0.8f;
                Prim(PrimitiveType.Cube, $"Crenel_{i}", root,
                    new Vector3(x, 1.95f, 0.35f), new Vector3(0.28f, 0.40f, 0.18f), iron);
            }

            // Obsidian shard pylons at the back corners (taller, sharp).
            for (int sx = -1; sx <= 1; sx += 2)
            {
                PrimRot(PrimitiveType.Cube, $"Obsidian_{sx}", root,
                    new Vector3(sx * 1.95f, 2.50f, -1.35f), new Vector3(0.40f, 1.30f, 0.40f),
                    Quaternion.Euler(0f, 30f * sx, 6f * sx), obsidian, metallic: 0.3f, smoothness: 0.85f);
            }

            // Crimson crystal pommel above the doorway (signature glow).
            var shard = PrimRot(PrimitiveType.Cube, "BloodShard", root,
                new Vector3(0f, 2.55f, 0.40f), new Vector3(0.40f, 0.85f, 0.40f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 2.0f, baseColor: crystal);

            // Blood altar in the yard (replaces one brazier feel with menace).
            AddBloodAltar(root, new Vector3(0f, 0.30f, 1.25f), iron, blood);

            // Crimson lights.
            AddPointLight(root, "Light_Apex", new Vector3(0f, 2.6f, 0.40f), crystal, intensity: 2.6f, range: 9f, bulbScale: 0f);
            AddPointLight(root, "Light_AltarYard", new Vector3(0f, 0.7f, 1.25f), blood, intensity: 1.6f, range: 5f, bulbScale: 0f);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ERA 1 ADVANCED BUILDINGS (Era 2 choice — wood + canvas on ruins,
        //  with culture overlays growing taller in each cultural style.)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Shrine of Ahridan (520): small open-air shrine with eternal flame.</summary>
        private static GameObject CreateTemple(Vector3 pos, Entity entity, byte culture)
        {
            // Note: dispatcher entry 520 historically called CreateTemple even
            // though 520 is ShrineOfAhridan; kept the method name to preserve
            // the dispatcher signature, but the design is now a Shrine.
            var root = new GameObject($"Shrine_{entity.Index}");
            root.transform.position = pos;

            BuildShrineBase(root.transform);

            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorShrine(root.transform); break;
                case Cultures.Runai:    OverlayRunaiShrine(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisShrine(root.transform); break;
            }
            return root;
        }

        private static void BuildShrineBase(Transform root)
        {
            AddRuinPlinth(root, 4.4f, 4.4f);

            // Open colonnade (six wood posts in a circle, no walls).
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                float cx = Mathf.Cos(a) * 1.55f;
                float cz = Mathf.Sin(a) * 1.55f;
                Prim(PrimitiveType.Cylinder, $"Post_{i}", root,
                    new Vector3(cx, 1.05f, cz), new Vector3(0.18f, 1.05f, 0.18f), HallWood);
            }

            // Canvas roof (hexagonal-ish — flattened cylinder), auto-tinted.
            Prim(PrimitiveType.Cylinder, "Roof_Canvas", root,
                new Vector3(0f, 2.20f, 0f), new Vector3(3.50f, 0.10f, 3.50f), HallCanvas);

            // Central altar with eternal flame.
            Prim(PrimitiveType.Cube, "AltarStone", root,
                new Vector3(0f, 0.45f, 0f), new Vector3(0.90f, 0.40f, 0.90f), HallStoneDark);
            var flame = Prim(PrimitiveType.Sphere, "EternalFlame", root,
                new Vector3(0f, 0.95f, 0f), new Vector3(0.50f, 0.70f, 0.50f), HallTorch);
            ApplyEmissive(flame, HallTorch * 2.4f, baseColor: HallTorch);
            AddPointLight(root, "Light_Altar",
                new Vector3(0f, 1.30f, 0f), HallTorch, intensity: 3.2f, range: 11f, bulbScale: 0f);

            // Stripe banners hung from each cardinal post.
            AddPlayerDecals(root, 3.10f, 3.10f, bannerHeight: 0.55f, bannerY: 1.45f);
        }

        private static void OverlayAlanthorShrine(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            // Replace wood posts feel with marble columns + cyan pagoda roof.
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                float cx = Mathf.Cos(a) * 1.55f;
                float cz = Mathf.Sin(a) * 1.55f;
                Prim(PrimitiveType.Cylinder, $"MarbleCol_{i}", root,
                    new Vector3(cx, 1.55f, cz), new Vector3(0.30f, 1.55f, 0.30f), marble, smoothness: 0.6f);
            }
            AddPagodaTier(root, "ShrinePagoda",
                new Vector3(0f, 3.20f, 0f), new Vector3(4.20f, 0.18f, 4.20f), 0f, cyanTile);
            AddPointLight(root, "Light_Top",
                new Vector3(0f, 3.20f, 0f), cyanTile, intensity: 2.5f, range: 10f, bulbScale: 0.18f);
        }

        private static void OverlayRunaiShrine(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);
            // Sandstone teardrop dome canopy above the altar.
            Prim(PrimitiveType.Sphere, "Dome", root,
                new Vector3(0f, 3.10f, 0f), new Vector3(2.80f, 2.40f, 2.80f), sandstone);
            Prim(PrimitiveType.Cylinder, "DomeTip", root,
                new Vector3(0f, 4.30f, 0f), new Vector3(0.15f, 0.55f, 0.15f), sandstone);
            // Silk canopies between the posts (sway), faction-stripe coloured.
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad + Mathf.PI / 6f;
                float cx = Mathf.Cos(a) * 1.10f;
                float cz = Mathf.Sin(a) * 1.10f;
                var s = PrimRot(PrimitiveType.Cube, $"Stripe_Drape_{i}", root,
                    new Vector3(cx, 1.85f, cz), new Vector3(0.90f, 0.04f, 0.30f),
                    Quaternion.Euler(0f, i * 60f, 8f), silk);
                AnimateSway(s, Vector3.right, 4f, 0.3f);
            }
        }

        private static void OverlayFeraldisShrine(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);
            var blood = new Color(0.55f, 0.05f, 0.05f);
            // Iron canopy ring (heavier roof).
            Prim(PrimitiveType.Cylinder, "IronRoof", root,
                new Vector3(0f, 2.40f, 0f), new Vector3(3.80f, 0.18f, 3.80f), iron, metallic: 0.5f);
            // Hanging crystal at apex.
            var shard = PrimRot(PrimitiveType.Cube, "BloodShard", root,
                new Vector3(0f, 3.10f, 0f), new Vector3(0.55f, 1.00f, 0.55f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 2.2f, baseColor: crystal);
            // Blood altar replaces eternal flame at center.
            AddBloodAltar(root, new Vector3(0f, 0.30f, 0f), iron, blood);
            AddPointLight(root, "Light_Crystal",
                new Vector3(0f, 3.10f, 0f), crystal, intensity: 3.5f, range: 12f, bulbScale: 0f);
        }

        /// <summary>Temple of Ridan (521): sect hub with circular base + 7 chapel slots.</summary>
        private static GameObject CreateTempleOfRidan(Vector3 pos, Entity entity, byte culture)
        {
            var root = new GameObject($"TempleOfRidan_{entity.Index}");
            root.transform.position = pos;

            BuildTempleBase(root.transform);

            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorTemple(root.transform); break;
                case Cultures.Runai:    OverlayRunaiTemple(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisTemple(root.transform); break;
            }
            return root;
        }

        private static void BuildTempleBase(Transform root)
        {
            AddRuinPlinth(root, 5.4f, 5.4f);

            // Central tower: tiered wood drum on the ruin slab.
            Prim(PrimitiveType.Cylinder, "TowerLow", root,
                new Vector3(0f, 1.40f, 0f), new Vector3(2.10f, 1.30f, 2.10f), HallWood);
            Prim(PrimitiveType.Cylinder, "TowerHigh", root,
                new Vector3(0f, 3.00f, 0f), new Vector3(1.55f, 0.90f, 1.55f), HallWood);
            // Canvas-skirt roof (auto-tinted).
            Prim(PrimitiveType.Cylinder, "Roof_Canvas", root,
                new Vector3(0f, 2.85f, 0f), new Vector3(2.70f, 0.10f, 2.70f), HallCanvas);
            // Spire.
            Prim(PrimitiveType.Cylinder, "Spire", root,
                new Vector3(0f, 4.40f, 0f), new Vector3(0.40f, 0.85f, 0.40f), HallStoneDark);
            Prim(PrimitiveType.Sphere, "SpireTip", root,
                new Vector3(0f, 5.30f, 0f), new Vector3(0.40f, 0.40f, 0.40f), HallTorch);

            // 7 chapel slot markers around the base — with tiny canopies.
            for (int i = 0; i < 7; i++)
            {
                float angle = i * (360f / 7f) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * 2.10f;
                float z = Mathf.Sin(angle) * 2.10f;
                Prim(PrimitiveType.Cube, $"Slot_{i}", root,
                    new Vector3(x, 0.30f, z), new Vector3(0.55f, 0.50f, 0.55f), HallStoneDark);
                // Stripe-named tiny canopy on each slot for player tint.
                Prim(PrimitiveType.Cube, $"Stripe_Slot_{i}", root,
                    new Vector3(x, 0.65f, z), new Vector3(0.50f, 0.05f, 0.50f), Color.white);
            }

            // Ring of braziers + central glowing tip.
            AddBrazier(root, new Vector3(-1.65f, 0.30f, 1.65f), "Brazier_NE");
            AddBrazier(root, new Vector3( 1.65f, 0.30f, 1.65f), "Brazier_NW");
            AddPointLight(root, "Light_Spire",
                new Vector3(0f, 5.30f, 0f), HallTorch, intensity: 3.0f, range: 11f, bulbScale: 0f);
        }

        private static void OverlayAlanthorTemple(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);
            // Marble drum + cyan curved roof + cogwheels on flanks.
            Prim(PrimitiveType.Cylinder, "MarbleDrum", root,
                new Vector3(0f, 1.40f, 0f), new Vector3(2.30f, 1.40f, 2.30f), marble, smoothness: 0.6f);
            AddPagodaTier(root, "TempPagoda",
                new Vector3(0f, 3.10f, 0f), new Vector3(3.40f, 0.18f, 3.40f), 0f, cyanTile);
            AddCogwheel(root, "Cog_E", new Vector3( 2.20f, 1.40f, 0f), new Vector3(0f, 0f, 1f),  60f, brass);
            AddCogwheel(root, "Cog_W", new Vector3(-2.20f, 1.40f, 0f), new Vector3(0f, 0f, 1f), -60f, brass);
            AddPointLight(root, "Light_TopCyan",
                new Vector3(0f, 3.50f, 0f), cyanTile, intensity: 3.0f, range: 11f);
        }

        private static void OverlayRunaiTemple(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);
            // Sandstone drum + teardrop dome.
            Prim(PrimitiveType.Cylinder, "SandDrum", root,
                new Vector3(0f, 1.30f, 0f), new Vector3(2.30f, 1.30f, 2.30f), sandstone);
            Prim(PrimitiveType.Sphere, "Dome", root,
                new Vector3(0f, 3.40f, 0f), new Vector3(2.20f, 1.85f, 2.20f), sandstone);
            Prim(PrimitiveType.Cylinder, "DomeTip", root,
                new Vector3(0f, 4.55f, 0f), new Vector3(0.12f, 0.50f, 0.12f), sandstone);
            // Silk canopies as Stripes between slots (sway).
            for (int i = 0; i < 7; i++)
            {
                float angle = (i + 0.5f) * (360f / 7f) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * 1.85f;
                float z = Mathf.Sin(angle) * 1.85f;
                var s = PrimRot(PrimitiveType.Cube, $"Stripe_Drape_{i}", root,
                    new Vector3(x, 1.40f, z), new Vector3(0.85f, 0.04f, 0.30f),
                    Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 8f), silk);
                AnimateSway(s, Vector3.right, 4f, 0.3f);
            }
            AddPointLight(root, "Light_Dome",
                new Vector3(0f, 3.40f, 0f), silk, intensity: 2.8f, range: 11f);
        }

        private static void OverlayFeraldisTemple(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);
            // Crimson iron tower replaces wooden tier.
            Prim(PrimitiveType.Cylinder, "IronTower", root,
                new Vector3(0f, 1.50f, 0f), new Vector3(2.20f, 1.55f, 2.20f), iron, metallic: 0.4f);
            Prim(PrimitiveType.Cylinder, "IronRoof", root,
                new Vector3(0f, 3.05f, 0f), new Vector3(2.50f, 0.18f, 2.50f), darkRed, metallic: 0.5f);
            // Crystal apex.
            var shard = PrimRot(PrimitiveType.Cube, "BloodCrystal", root,
                new Vector3(0f, 4.50f, 0f), new Vector3(0.55f, 1.20f, 0.55f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 2.2f, baseColor: crystal);
            AddPointLight(root, "Light_Crystal",
                new Vector3(0f, 4.50f, 0f), crystal, intensity: 4.0f, range: 14f, bulbScale: 0f);
        }

        /// <summary>Vault of Almierra (530): banking treasury — heavy stone safe with culture overlays.</summary>
        private static GameObject CreateVault(Vector3 pos, Entity entity, byte culture)
        {
            var root = new GameObject($"Vault_{entity.Index}");
            root.transform.position = pos;

            BuildVaultBase(root.transform);

            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorVault(root.transform); break;
                case Cultures.Runai:    OverlayRunaiVault(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisVault(root.transform); break;
            }
            return root;
        }

        private static void BuildVaultBase(Transform root)
        {
            AddRuinPlinth(root, 3.0f, 3.0f);

            var metal = new Color(0.4f, 0.4f, 0.45f);
            // Heavy stone treasury body (the "ruin" reading is reinforced by the plinth).
            Prim(PrimitiveType.Cube, "Body", root,
                new Vector3(0f, 1.30f, 0f), new Vector3(2.60f, 2.40f, 2.60f), HallStone);
            // Wooden-canvas awning over the door (auto-tinted by Roof).
            PrimRot(PrimitiveType.Cube, "Roof_Awning", root,
                new Vector3(0f, 2.20f, 1.40f), new Vector3(2.80f, 0.10f, 1.40f),
                Quaternion.Euler(15f, 0f, 0f), HallCanvas);
            // Iron reinforcement bands across the front.
            Prim(PrimitiveType.Cube, "Band_Low", root,
                new Vector3(0f, 0.65f, 1.34f), new Vector3(2.40f, 0.12f, 0.05f), metal, 0.7f, 0.6f);
            Prim(PrimitiveType.Cube, "Band_High", root,
                new Vector3(0f, 1.85f, 1.34f), new Vector3(2.40f, 0.12f, 0.05f), metal, 0.7f, 0.6f);
            // Round vault door.
            Prim(PrimitiveType.Sphere, "Door", root,
                new Vector3(0f, 1.20f, 1.32f), new Vector3(1.10f, 1.10f, 0.18f), metal, 0.8f, 0.7f);
            Prim(PrimitiveType.Sphere, "DoorWheel", root,
                new Vector3(0f, 1.20f, 1.42f), new Vector3(0.45f, 0.45f, 0.10f), metal, 0.9f, 0.8f);
            // Corner stone pillars.
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Pillar_{x}_{z}", root,
                    new Vector3(x * 1.30f, 1.30f, z * 1.30f), new Vector3(0.25f, 1.30f, 0.25f), HallStoneDark);
            }
            // Stripes + corner braziers.
            AddPlayerDecals(root, 2.60f, 2.60f, bannerHeight: 0.70f, bannerY: 1.30f);
            AddBrazier(root, new Vector3(-1.55f, 0.30f,  1.55f), "Brazier_FL");
            AddBrazier(root, new Vector3( 1.55f, 0.30f,  1.55f), "Brazier_FR");
        }

        private static void OverlayAlanthorVault(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);
            // Marble re-skin band + cyan pagoda roof + corner cogwheels.
            Prim(PrimitiveType.Cube, "MarbleBand", root,
                new Vector3(0f, 2.10f, 0f), new Vector3(2.80f, 0.30f, 2.80f), marble, smoothness: 0.6f);
            AddPagodaTier(root, "VaultPagoda",
                new Vector3(0f, 2.70f, 0f), new Vector3(3.20f, 0.18f, 3.20f), 0f, cyanTile);
            AddCogwheel(root, "Cog_Door", new Vector3(0f, 1.20f, 1.50f), new Vector3(0f, 0f, 1f), 50f, brass);
            AddPointLight(root, "Light_Top",
                new Vector3(0f, 3.10f, 0f), cyanTile, intensity: 2.4f, range: 10f);
        }

        private static void OverlayRunaiVault(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);
            // Sandstone outer shell + silk awning replaces canvas.
            Prim(PrimitiveType.Cube, "SandShell", root,
                new Vector3(0f, 1.35f, 0f), new Vector3(2.85f, 0.45f, 2.85f), sandstone);
            Prim(PrimitiveType.Sphere, "Dome", root,
                new Vector3(0f, 3.10f, 0f), new Vector3(2.10f, 1.55f, 2.10f), sandstone);
            var awning = PrimRot(PrimitiveType.Cube, "Stripe_Awning", root,
                new Vector3(0f, 2.30f, 1.45f), new Vector3(2.80f, 0.04f, 1.20f),
                Quaternion.Euler(15f, 0f, 0f), silk);
            AnimateSway(awning, Vector3.right, 4f, 0.3f);
            AddPointLight(root, "Light_Dome",
                new Vector3(0f, 3.10f, 0f), silk, intensity: 2.4f, range: 10f);
        }

        private static void OverlayFeraldisVault(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);
            // Iron tier above the body + crimson crenellations + apex crystal.
            Prim(PrimitiveType.Cube, "IronTier", root,
                new Vector3(0f, 2.55f, 0f), new Vector3(2.40f, 0.80f, 2.40f), iron, metallic: 0.4f);
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 0.55f;
                Prim(PrimitiveType.Cube, $"Crenel_{i}", root,
                    new Vector3(x, 3.05f, 1.20f), new Vector3(0.30f, 0.40f, 0.20f), iron);
            }
            var shard = PrimRot(PrimitiveType.Cube, "BloodCrystal", root,
                new Vector3(0f, 3.50f, 0f), new Vector3(0.50f, 0.95f, 0.50f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 2.2f, baseColor: crystal);
            AddPointLight(root, "Light_Crystal",
                new Vector3(0f, 3.50f, 0f), crystal, intensity: 3.6f, range: 12f, bulbScale: 0f);
        }

        /// <summary>Fiendstone Keep (540): fortress with corner towers, gate, battlements.</summary>
        private static GameObject CreateKeep(Vector3 pos, Entity entity, byte culture)
        {
            var root = new GameObject($"Keep_{entity.Index}");
            root.transform.position = pos;

            BuildKeepBase(root.transform);

            switch (culture)
            {
                case Cultures.Alanthor: OverlayAlanthorKeep(root.transform); break;
                case Cultures.Runai:    OverlayRunaiKeep(root.transform);    break;
                case Cultures.Feraldis: OverlayFeraldisKeep(root.transform); break;
            }
            return root;
        }

        private static void BuildKeepBase(Transform root)
        {
            AddRuinPlinth(root, 3.6f, 3.6f);

            // Stone keep body with crenellated battlements (cultureless = grey stone).
            Prim(PrimitiveType.Cube, "Body", root,
                new Vector3(0f, 1.40f, 0f), new Vector3(2.40f, 2.60f, 2.40f), HallStone);
            Prim(PrimitiveType.Cube, "BattlementTop", root,
                new Vector3(0f, 2.85f, 0f), new Vector3(2.70f, 0.20f, 2.70f), HallStone);
            // 6 crenellations across the front
            for (int i = 0; i < 6; i++)
            {
                float x = (i - 2.5f) * 0.42f;
                Prim(PrimitiveType.Cube, $"Crenel_F_{i}", root,
                    new Vector3(x, 3.10f, 1.30f), new Vector3(0.22f, 0.30f, 0.18f), HallStone);
            }
            // 4 corner towers.
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Tower_{x}_{z}", root,
                    new Vector3(x * 1.45f, 1.65f, z * 1.45f), new Vector3(0.65f, 1.65f, 0.65f), HallStone);
                // Canvas tower cap (auto-tinted via Roof).
                PrimRot(PrimitiveType.Cube, $"Roof_TowerCap_{x}_{z}", root,
                    new Vector3(x * 1.45f, 3.45f, z * 1.45f), new Vector3(0.85f, 0.55f, 0.85f),
                    Quaternion.Euler(0f, 45f, 45f), HallCanvas);
            }
            // Wooden gate.
            Prim(PrimitiveType.Cube, "Gate", root,
                new Vector3(0f, 0.85f, 1.25f), new Vector3(0.95f, 1.45f, 0.18f), HallWood);
            // Stripes + braziers at the gate.
            Prim(PrimitiveType.Cube, "Stripe_GateBanner", root,
                new Vector3(0f, 2.20f, 1.30f), new Vector3(0.65f, 0.70f, 0.04f), Color.white);
            AddPlayerDecals(root, 3.10f, 3.10f, bannerHeight: 0.55f, bannerY: 1.65f);
            AddBrazier(root, new Vector3(-1.0f, 0.30f, 1.50f), "Brazier_GateL");
            AddBrazier(root, new Vector3( 1.0f, 0.30f, 1.50f), "Brazier_GateR");
        }

        private static void OverlayAlanthorKeep(Transform root)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);
            Prim(PrimitiveType.Cube, "MarbleBand", root,
                new Vector3(0f, 2.50f, 0f), new Vector3(2.55f, 0.55f, 2.55f), marble, smoothness: 0.6f);
            AddPagodaTier(root, "KeepPagoda",
                new Vector3(0f, 3.40f, 0f), new Vector3(3.30f, 0.18f, 3.30f), 0f, cyanTile);
            AddCogwheel(root, "Cog_F", new Vector3(0f, 2.00f, 1.30f), new Vector3(0f, 0f, 1f), 60f, brass);
            AddPointLight(root, "Light_KeepTop",
                new Vector3(0f, 3.80f, 0f), cyanTile, intensity: 2.8f, range: 11f);
        }

        private static void OverlayRunaiKeep(Transform root)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Prim(PrimitiveType.Sphere, $"Dome_{x}_{z}", root,
                    new Vector3(x * 1.45f, 4.30f, z * 1.45f), new Vector3(0.85f, 1.20f, 0.85f), sandstone);
            }
            Prim(PrimitiveType.Sphere, "MainDome", root,
                new Vector3(0f, 3.80f, 0f), new Vector3(2.30f, 1.80f, 2.30f), sandstone);
            var awning = PrimRot(PrimitiveType.Cube, "Stripe_GateAwning", root,
                new Vector3(0f, 2.55f, 1.45f), new Vector3(2.10f, 0.04f, 1.10f),
                Quaternion.Euler(15f, 0f, 0f), silk);
            AnimateSway(awning, Vector3.right, 4f, 0.3f);
            AddPointLight(root, "Light_MainDome",
                new Vector3(0f, 3.80f, 0f), silk, intensity: 2.8f, range: 11f);
        }

        private static void OverlayFeraldisKeep(Transform root)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var obsidian = new Color(0.10f, 0.08f, 0.10f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);
            // Iron-tile dark-red roof panels over each tower.
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                PrimRot(PrimitiveType.Cube, $"IronCap_{x}_{z}", root,
                    new Vector3(x * 1.45f, 3.50f, z * 1.45f), new Vector3(0.95f, 0.18f, 0.95f),
                    Quaternion.Euler(0f, 45f, 0f), darkRed, metallic: 0.5f);
                PrimRot(PrimitiveType.Cube, $"Obsidian_{x}_{z}", root,
                    new Vector3(x * 1.45f, 4.05f, z * 1.45f), new Vector3(0.30f, 1.00f, 0.30f),
                    Quaternion.Euler(0f, 30f * x, 8f * z), obsidian, metallic: 0.3f, smoothness: 0.85f);
            }
            // Crimson keep roof + apex crystal.
            Prim(PrimitiveType.Cube, "KeepIronRoof", root,
                new Vector3(0f, 3.10f, 0f), new Vector3(2.55f, 0.20f, 2.55f), darkRed, metallic: 0.5f);
            var shard = PrimRot(PrimitiveType.Cube, "BloodCrystal", root,
                new Vector3(0f, 3.95f, 0f), new Vector3(0.55f, 1.20f, 0.55f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 2.2f, baseColor: crystal);
            AddPointLight(root, "Light_Crystal",
                new Vector3(0f, 3.95f, 0f), crystal, intensity: 4.0f, range: 14f, bulbScale: 0f);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  RUNAI CULTURE BUILDINGS (larger ~1.3-1.5x, cyan/sandstone)
        // ═══════════════════════════════════════════════════════════════════════

        private static Color RunaiWall => CultureConfig.GetWallColor(Cultures.Runai);
        private static Color RunaiRoof => CultureConfig.GetRoofColor(Cultures.Runai);
        private static Color RunaiTrim => CultureConfig.GetTrimColor(Cultures.Runai);

        /// <summary>Runai Outpost: Tent-like structure with poles + fabric roof.</summary>
        private static GameObject CreateRunaiOutpost(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"RunaiOutpost_{entity.Index}");
            root.transform.position = pos;

            // Platform base
            Prim(PrimitiveType.Cylinder, "Base", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(2.6f, 0.1f, 2.6f), RunaiWall);

            // Center pole
            Prim(PrimitiveType.Cylinder, "Pole", root.transform,
                new Vector3(0f, 1.5f, 0f), new Vector3(0.12f, 1.5f, 0.12f), RunaiTrim);

            // Tent fabric (flattened sphere for draped look)
            Prim(PrimitiveType.Sphere, "Fabric", root.transform,
                new Vector3(0f, 2.2f, 0f), new Vector3(2.8f, 0.8f, 2.8f), RunaiRoof);

            // Support poles (4 corners)
            for (int i = 0; i < 4; i++)
            {
                float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * 1.1f;
                float z = Mathf.Sin(angle) * 1.1f;
                Prim(PrimitiveType.Cylinder, $"SupportPole_{i}", root.transform,
                    new Vector3(x, 0.8f, z), new Vector3(0.08f, 0.8f, 0.08f), RunaiTrim);
            }

            // Flag streamer
            Prim(PrimitiveType.Cube, "Flag", root.transform,
                new Vector3(0.2f, 3.1f, 0f), new Vector3(0.4f, 0.15f, 0.04f), RunaiRoof);

            return root;
        }

        /// <summary>Runai TradeHub: Open market structure with awnings.</summary>
        private static GameObject CreateRunaiTradeHub(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"RunaiTradeHub_{entity.Index}");
            root.transform.position = pos;

            // Platform
            Prim(PrimitiveType.Cube, "Platform", root.transform,
                new Vector3(0f, 0.08f, 0f), new Vector3(3.5f, 0.15f, 3.0f), RunaiWall);

            // Four tall poles
            float[] px = { -1.4f, 1.4f, -1.4f, 1.4f };
            float[] pz = { -1.2f, -1.2f, 1.2f, 1.2f };
            for (int i = 0; i < 4; i++)
            {
                Prim(PrimitiveType.Cylinder, $"Pole_{i}", root.transform,
                    new Vector3(px[i], 1.3f, pz[i]), new Vector3(0.1f, 1.3f, 0.1f), RunaiTrim);
            }

            // Awning roof (fabric)
            Prim(PrimitiveType.Cube, "Awning", root.transform,
                new Vector3(0f, 2.6f, 0f), new Vector3(3.2f, 0.1f, 2.6f), RunaiRoof);

            // Market counter
            Prim(PrimitiveType.Cube, "Counter", root.transform,
                new Vector3(0f, 0.45f, 0f), new Vector3(2.0f, 0.6f, 0.5f), RunaiWall * 0.85f);

            // Goods crates (detail)
            Prim(PrimitiveType.Cube, "Crate1", root.transform,
                new Vector3(-0.8f, 0.25f, 0.8f), new Vector3(0.4f, 0.5f, 0.4f), RunaiTrim * 0.7f);
            Prim(PrimitiveType.Cube, "Crate2", root.transform,
                new Vector3(0.9f, 0.2f, -0.7f), new Vector3(0.35f, 0.4f, 0.35f), RunaiTrim * 0.8f);

            return root;
        }

        /// <summary>Thessara's Bazaar: Large ornate tent + market stalls.</summary>
        private static GameObject CreateRunaiBazaar(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"RunaiBazaar_{entity.Index}");
            root.transform.position = pos;

            // Large base platform
            Prim(PrimitiveType.Cylinder, "Base", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(4.0f, 0.1f, 4.0f), RunaiWall);

            // Grand central tent (tall)
            Prim(PrimitiveType.Cylinder, "TentBody", root.transform,
                new Vector3(0f, 1.2f, 0f), new Vector3(2.8f, 1.2f, 2.8f), RunaiWall);

            // Domed fabric roof
            Prim(PrimitiveType.Sphere, "Dome", root.transform,
                new Vector3(0f, 2.8f, 0f), new Vector3(3.2f, 1.5f, 3.2f), RunaiRoof);

            // Center spire
            Prim(PrimitiveType.Cylinder, "Spire", root.transform,
                new Vector3(0f, 3.8f, 0f), new Vector3(0.15f, 0.6f, 0.15f), RunaiTrim);
            Prim(PrimitiveType.Sphere, "SpireTip", root.transform,
                new Vector3(0f, 4.5f, 0f), new Vector3(0.25f, 0.25f, 0.25f), RunaiTrim);

            // Side stalls (2)
            for (int side = -1; side <= 1; side += 2)
            {
                Prim(PrimitiveType.Cube, $"Stall_{side}", root.transform,
                    new Vector3(side * 2.2f, 0.4f, 0f), new Vector3(1.0f, 0.8f, 1.2f), RunaiWall * 0.9f);
                Prim(PrimitiveType.Cube, $"StallRoof_{side}", root.transform,
                    new Vector3(side * 2.2f, 0.9f, 0f), new Vector3(1.3f, 0.08f, 1.5f), RunaiRoof);
            }

            return root;
        }

        /// <summary>Runai SiegeWorkshop: Workshop shed + ballista frame.</summary>
        private static GameObject CreateRunaiSiegeWorkshop(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"RunaiSiegeWorkshop_{entity.Index}");
            root.transform.position = pos;

            // Workshop body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1.0f, 0f), new Vector3(2.8f, 2.0f, 2.2f), RunaiWall);

            // Sloped roof
            PrimRot(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 2.2f, -0.2f), new Vector3(3.0f, 0.15f, 2.8f),
                Quaternion.Euler(10f, 0f, 0f), RunaiRoof);

            // Open front (darker recess)
            Prim(PrimitiveType.Cube, "Opening", root.transform,
                new Vector3(0f, 0.7f, 1.15f), new Vector3(1.8f, 1.4f, 0.1f), RunaiTrim * 0.5f);

            // Ballista frame inside (simplified)
            Prim(PrimitiveType.Cube, "BallistaBase", root.transform,
                new Vector3(0f, 0.3f, 0.3f), new Vector3(0.8f, 0.3f, 1.2f), RunaiTrim);
            PrimRot(PrimitiveType.Cylinder, "BallistaArm", root.transform,
                new Vector3(0f, 0.7f, 0.8f), new Vector3(0.08f, 0.6f, 0.08f),
                Quaternion.Euler(0f, 0f, 30f), RunaiTrim);

            return root;
        }

        /// <summary>Runai TradingPost: Small waypoint marker + trade sign.</summary>
        private static GameObject CreateRunaiTradingPost(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"RunaiTradingPost_{entity.Index}");
            root.transform.position = pos;

            // Base stone
            Prim(PrimitiveType.Cylinder, "Base", root.transform,
                new Vector3(0f, 0.15f, 0f), new Vector3(1.6f, 0.15f, 1.6f), RunaiWall);

            // Center post
            Prim(PrimitiveType.Cylinder, "Post", root.transform,
                new Vector3(0f, 1.0f, 0f), new Vector3(0.15f, 1.0f, 0.15f), RunaiTrim);

            // Trade sign (horizontal board)
            Prim(PrimitiveType.Cube, "Sign", root.transform,
                new Vector3(0.4f, 1.6f, 0f), new Vector3(0.7f, 0.35f, 0.06f), RunaiRoof);

            // Goods sacks
            Prim(PrimitiveType.Sphere, "Sack1", root.transform,
                new Vector3(-0.4f, 0.25f, 0.3f), new Vector3(0.35f, 0.35f, 0.35f), RunaiWall * 0.8f);
            Prim(PrimitiveType.Sphere, "Sack2", root.transform,
                new Vector3(0.3f, 0.2f, -0.35f), new Vector3(0.3f, 0.3f, 0.3f), RunaiWall * 0.85f);

            return root;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ALANTHOR CULTURE BUILDINGS (larger ~1.3-1.5x, sage/warm grey)
        // ═══════════════════════════════════════════════════════════════════════

        private static Color AlanthorWall => CultureConfig.GetWallColor(Cultures.Alanthor);
        private static Color AlanthorRoof => CultureConfig.GetRoofColor(Cultures.Alanthor);
        private static Color AlanthorTrim => CultureConfig.GetTrimColor(Cultures.Alanthor);

        /// <summary>Alanthor Tower: Tall stone tower + battlements.</summary>
        private static GameObject CreateAlanthorTower(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"AlanthorTower_{entity.Index}");
            root.transform.position = pos;

            // Foundation
            Prim(PrimitiveType.Cube, "Foundation", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(2.0f, 0.2f, 2.0f), AlanthorTrim);

            // Tower body (tall cylinder)
            Prim(PrimitiveType.Cylinder, "Tower", root.transform,
                new Vector3(0f, 2.2f, 0f), new Vector3(1.4f, 2.2f, 1.4f), AlanthorWall);

            // Battlement ring (wider cap)
            Prim(PrimitiveType.Cylinder, "Battlement", root.transform,
                new Vector3(0f, 4.4f, 0f), new Vector3(1.7f, 0.15f, 1.7f), AlanthorWall);

            // Merlon crenellations (4 cubes on top)
            for (int i = 0; i < 4; i++)
            {
                float angle = (i * 90f) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * 0.7f;
                float z = Mathf.Sin(angle) * 0.7f;
                Prim(PrimitiveType.Cube, $"Merlon_{i}", root.transform,
                    new Vector3(x, 4.7f, z), new Vector3(0.25f, 0.35f, 0.25f), AlanthorWall);
            }

            // Roof cone
            Prim(PrimitiveType.Sphere, "RoofCone", root.transform,
                new Vector3(0f, 4.9f, 0f), new Vector3(1.0f, 0.6f, 1.0f), AlanthorRoof);

            // Arrow slit windows (thin dark cubes)
            for (int i = 0; i < 3; i++)
            {
                float y = 1.5f + i * 1.0f;
                Prim(PrimitiveType.Cube, $"Slit_{i}", root.transform,
                    new Vector3(0f, y, 0.72f), new Vector3(0.06f, 0.3f, 0.05f), AlanthorTrim * 0.3f);
            }

            return root;
        }

        /// <summary>Alanthor Garrison: Barracks complex with courtyard.</summary>
        private static GameObject CreateAlanthorGarrison(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"AlanthorGarrison_{entity.Index}");
            root.transform.position = pos;

            // Courtyard base
            Prim(PrimitiveType.Cube, "Courtyard", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(3.5f, 0.1f, 3.5f), AlanthorTrim);

            // Main building (back wall)
            Prim(PrimitiveType.Cube, "MainBuilding", root.transform,
                new Vector3(0f, 1.2f, -1.2f), new Vector3(3.0f, 2.4f, 1.0f), AlanthorWall);
            Prim(PrimitiveType.Cube, "MainRoof", root.transform,
                new Vector3(0f, 2.5f, -1.2f), new Vector3(3.2f, 0.15f, 1.2f), AlanthorRoof);

            // Side wings (L and R)
            for (int side = -1; side <= 1; side += 2)
            {
                Prim(PrimitiveType.Cube, $"Wing_{side}", root.transform,
                    new Vector3(side * 1.4f, 0.8f, 0.3f), new Vector3(0.7f, 1.6f, 2.0f), AlanthorWall);
                Prim(PrimitiveType.Cube, $"WingRoof_{side}", root.transform,
                    new Vector3(side * 1.4f, 1.7f, 0.3f), new Vector3(0.9f, 0.1f, 2.2f), AlanthorRoof);
            }

            // Gate
            Prim(PrimitiveType.Cube, "Gate", root.transform,
                new Vector3(0f, 0.5f, 1.3f), new Vector3(0.8f, 1.0f, 0.15f), AlanthorTrim * 0.5f);

            return root;
        }

        /// <summary>Alanthor Stable: Long stable building + paddock fence.</summary>
        private static GameObject CreateAlanthorStable(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"AlanthorStable_{entity.Index}");
            root.transform.position = pos;

            // Foundation
            Prim(PrimitiveType.Cube, "Foundation", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(4.0f, 0.1f, 2.5f), AlanthorTrim);

            // Stable body (long)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1.0f, -0.3f), new Vector3(3.5f, 2.0f, 1.5f), AlanthorWall);

            // Sloped roof
            PrimRot(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 2.15f, -0.3f), new Vector3(3.7f, 0.15f, 1.8f),
                Quaternion.Euler(8f, 0f, 0f), AlanthorRoof);

            // Stall divisions (3 stalls visible from front)
            for (int i = -1; i <= 1; i++)
            {
                Prim(PrimitiveType.Cube, $"StallWall_{i}", root.transform,
                    new Vector3(i * 1.1f, 0.5f, 0.45f), new Vector3(0.08f, 1.0f, 0.5f), AlanthorWall * 0.8f);
            }

            // Paddock fence (front)
            Prim(PrimitiveType.Cube, "FenceRail", root.transform,
                new Vector3(0f, 0.45f, 1.0f), new Vector3(3.0f, 0.06f, 0.06f), AlanthorTrim);
            Prim(PrimitiveType.Cube, "FenceRail2", root.transform,
                new Vector3(0f, 0.25f, 1.0f), new Vector3(3.0f, 0.06f, 0.06f), AlanthorTrim);
            // Fence posts
            for (int i = -2; i <= 2; i++)
            {
                Prim(PrimitiveType.Cylinder, $"FencePost_{i}", root.transform,
                    new Vector3(i * 0.75f, 0.35f, 1.0f), new Vector3(0.06f, 0.35f, 0.06f), AlanthorTrim);
            }

            return root;
        }

        /// <summary>Alanthor SiegeYard: Workshop with crane arm.</summary>
        private static GameObject CreateAlanthorSiegeYard(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"AlanthorSiegeYard_{entity.Index}");
            root.transform.position = pos;

            // Workshop base
            Prim(PrimitiveType.Cube, "Base", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(3.5f, 0.1f, 3.0f), AlanthorTrim);

            // Main workshop building
            Prim(PrimitiveType.Cube, "Workshop", root.transform,
                new Vector3(-0.5f, 1.0f, 0f), new Vector3(2.0f, 2.0f, 2.5f), AlanthorWall);
            Prim(PrimitiveType.Cube, "WorkshopRoof", root.transform,
                new Vector3(-0.5f, 2.1f, 0f), new Vector3(2.2f, 0.15f, 2.7f), AlanthorRoof);

            // Crane structure (right side)
            Prim(PrimitiveType.Cylinder, "CranePole", root.transform,
                new Vector3(1.2f, 2.0f, 0f), new Vector3(0.12f, 2.0f, 0.12f), AlanthorTrim);
            PrimRot(PrimitiveType.Cylinder, "CraneArm", root.transform,
                new Vector3(1.2f, 3.8f, 0.5f), new Vector3(0.08f, 1.0f, 0.08f),
                Quaternion.Euler(0f, 0f, 75f), AlanthorTrim);

            // Ballista under construction (on ground)
            Prim(PrimitiveType.Cube, "BallistaFrame", root.transform,
                new Vector3(1.0f, 0.25f, 0f), new Vector3(1.0f, 0.3f, 0.6f), AlanthorTrim * 0.7f);

            return root;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  FERALDIS CULTURE BUILDINGS (larger ~1.3-1.5x, crimson/dark grey)
        // ═══════════════════════════════════════════════════════════════════════

        private static Color FeraldisWall => CultureConfig.GetWallColor(Cultures.Feraldis);
        private static Color FeraldisRoof => CultureConfig.GetRoofColor(Cultures.Feraldis);
        private static Color FeraldisTrim => CultureConfig.GetTrimColor(Cultures.Feraldis);

        /// <summary>Feraldis HuntingLodge: Log cabin with antler decorations.</summary>
        private static GameObject CreateFeraldisHuntingLodge(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"FeraldisHuntingLodge_{entity.Index}");
            root.transform.position = pos;

            // Log cabin body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.9f, 0f), new Vector3(2.5f, 1.8f, 2.0f), FeraldisWall);

            // Log texture — horizontal cylinder "logs" on front
            for (int i = 0; i < 4; i++)
            {
                float y = 0.3f + i * 0.4f;
                Prim(PrimitiveType.Cylinder, $"Log_{i}", root.transform,
                    new Vector3(0f, y, 1.05f), new Vector3(0.12f, 1.3f, 0.12f),
                    FeraldisWall * 1.15f);
            }

            // Peaked roof
            PrimRot(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 2.1f, 0f), new Vector3(1.6f, 0.6f, 1.4f),
                Quaternion.Euler(0f, 0f, 45f), FeraldisRoof);

            // Antler decoration (V shape on front — two angled cylinders)
            PrimRot(PrimitiveType.Cylinder, "AntlerL", root.transform,
                new Vector3(-0.2f, 2.5f, 1.05f), new Vector3(0.05f, 0.35f, 0.05f),
                Quaternion.Euler(0f, 0f, 25f), new Color(0.6f, 0.55f, 0.4f));
            PrimRot(PrimitiveType.Cylinder, "AntlerR", root.transform,
                new Vector3(0.2f, 2.5f, 1.05f), new Vector3(0.05f, 0.35f, 0.05f),
                Quaternion.Euler(0f, 0f, -25f), new Color(0.6f, 0.55f, 0.4f));

            // Door
            Prim(PrimitiveType.Cube, "Door", root.transform,
                new Vector3(0f, 0.4f, 1.05f), new Vector3(0.5f, 0.8f, 0.1f), FeraldisTrim);

            return root;
        }

        /// <summary>Feraldis LoggingStation: Open workshop with log piles.</summary>
        private static GameObject CreateFeraldisLoggingStation(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"FeraldisLoggingStation_{entity.Index}");
            root.transform.position = pos;

            // Open-sided shed
            Prim(PrimitiveType.Cube, "Base", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(3.0f, 0.1f, 2.5f), FeraldisTrim);

            // Four corner posts
            float[] ppx = { -1.3f, 1.3f, -1.3f, 1.3f };
            float[] ppz = { -1.0f, -1.0f, 1.0f, 1.0f };
            for (int i = 0; i < 4; i++)
            {
                Prim(PrimitiveType.Cylinder, $"Post_{i}", root.transform,
                    new Vector3(ppx[i], 1.0f, ppz[i]), new Vector3(0.15f, 1.0f, 0.15f), FeraldisWall);
            }

            // Roof
            PrimRot(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 2.1f, 0f), new Vector3(3.2f, 0.12f, 2.8f),
                Quaternion.Euler(5f, 0f, 0f), FeraldisRoof);

            // Log pile (stack of horizontal cylinders)
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < (3 - r); c++)
            {
                float x = -0.5f + c * 0.35f + r * 0.175f;
                float y = 0.15f + r * 0.3f;
                PrimRot(PrimitiveType.Cylinder, $"Log_{r}_{c}", root.transform,
                    new Vector3(x, y, -0.6f), new Vector3(0.15f, 0.5f, 0.15f),
                    Quaternion.Euler(0f, 0f, 90f), new Color(0.4f, 0.28f, 0.15f));
            }

            // Chopping block
            Prim(PrimitiveType.Cylinder, "ChopBlock", root.transform,
                new Vector3(0.7f, 0.2f, 0.4f), new Vector3(0.4f, 0.2f, 0.4f), new Color(0.35f, 0.25f, 0.12f));

            return root;
        }

        /// <summary>Feraldis Longhouse: Long low building with ridge beam.</summary>
        private static GameObject CreateFeraldisLonghouse(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"FeraldisLonghouse_{entity.Index}");
            root.transform.position = pos;

            // Long body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.8f, 0f), new Vector3(4.0f, 1.6f, 2.0f), FeraldisWall);

            // Ridge beam (thick cylinder along length)
            PrimRot(PrimitiveType.Cylinder, "Ridge", root.transform,
                new Vector3(0f, 1.85f, 0f), new Vector3(0.12f, 2.2f, 0.12f),
                Quaternion.Euler(0f, 0f, 90f), FeraldisTrim);

            // Sloped roof (two angled panels)
            PrimRot(PrimitiveType.Cube, "RoofL", root.transform,
                new Vector3(0f, 1.75f, -0.55f), new Vector3(4.2f, 0.1f, 1.3f),
                Quaternion.Euler(-15f, 0f, 0f), FeraldisRoof);
            PrimRot(PrimitiveType.Cube, "RoofR", root.transform,
                new Vector3(0f, 1.75f, 0.55f), new Vector3(4.2f, 0.1f, 1.3f),
                Quaternion.Euler(15f, 0f, 0f), FeraldisRoof);

            // Smoke hole (small dark cube on roof center)
            Prim(PrimitiveType.Cube, "SmokeHole", root.transform,
                new Vector3(0f, 2.0f, 0f), new Vector3(0.3f, 0.15f, 0.3f), FeraldisTrim * 0.3f);

            // Door (end)
            Prim(PrimitiveType.Cube, "Door", root.transform,
                new Vector3(2.05f, 0.45f, 0f), new Vector3(0.1f, 0.9f, 0.6f), FeraldisTrim);

            return root;
        }

        /// <summary>Feraldis TotemTower: Tall totem pole + fire pit base.</summary>
        private static GameObject CreateFeraldisTotemTower(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"FeraldisTotemTower_{entity.Index}");
            root.transform.position = pos;

            // Fire pit base (ring)
            Prim(PrimitiveType.Cylinder, "FirePit", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(2.0f, 0.1f, 2.0f), FeraldisTrim);

            // Central totem pole (tall, segmented look)
            Prim(PrimitiveType.Cylinder, "TotemBase", root.transform,
                new Vector3(0f, 1.0f, 0f), new Vector3(0.5f, 1.0f, 0.5f), FeraldisWall);
            Prim(PrimitiveType.Cylinder, "TotemMid", root.transform,
                new Vector3(0f, 2.5f, 0f), new Vector3(0.4f, 0.5f, 0.4f), FeraldisWall * 1.1f);
            Prim(PrimitiveType.Cylinder, "TotemTop", root.transform,
                new Vector3(0f, 3.5f, 0f), new Vector3(0.35f, 0.5f, 0.35f), FeraldisWall * 1.2f);

            // Totem face carvings (small cubes as features)
            Prim(PrimitiveType.Cube, "Face1", root.transform,
                new Vector3(0f, 1.5f, 0.28f), new Vector3(0.3f, 0.25f, 0.05f), FeraldisRoof);
            Prim(PrimitiveType.Cube, "Face2", root.transform,
                new Vector3(0f, 2.7f, 0.22f), new Vector3(0.25f, 0.2f, 0.05f), FeraldisRoof);
            Prim(PrimitiveType.Cube, "Face3", root.transform,
                new Vector3(0f, 3.6f, 0.2f), new Vector3(0.2f, 0.18f, 0.05f), FeraldisRoof);

            // Totem wings/arms at mid section
            for (int side = -1; side <= 1; side += 2)
            {
                PrimRot(PrimitiveType.Cube, $"Wing_{side}", root.transform,
                    new Vector3(side * 0.4f, 2.5f, 0f), new Vector3(0.5f, 0.08f, 0.2f),
                    Quaternion.Euler(0f, 0f, side * -20f), FeraldisWall);
            }

            // Fire glow (emissive sphere at base)
            var fire = Prim(PrimitiveType.Sphere, "Fire", root.transform,
                new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.5f, 0.5f), new Color(1f, 0.4f, 0.1f));
            var fireMat = fire.GetComponent<Renderer>().material;
            if (fireMat.HasProperty("_EmissionColor"))
            {
                fireMat.EnableKeyword("_EMISSION");
                fireMat.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.05f) * 2f);
            }

            return root;
        }

        /// <summary>Feraldis SiegeYard: Ram construction bay.</summary>
        private static GameObject CreateFeraldisSiegeYard(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"FeraldisSiegeYard_{entity.Index}");
            root.transform.position = pos;

            // Open yard base
            Prim(PrimitiveType.Cube, "Base", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(3.5f, 0.1f, 2.5f), FeraldisTrim);

            // Back wall (workshop)
            Prim(PrimitiveType.Cube, "BackWall", root.transform,
                new Vector3(0f, 1.0f, -1.0f), new Vector3(3.0f, 2.0f, 0.5f), FeraldisWall);
            Prim(PrimitiveType.Cube, "BackRoof", root.transform,
                new Vector3(0f, 2.1f, -1.0f), new Vector3(3.2f, 0.12f, 0.7f), FeraldisRoof);

            // Side supports
            for (int side = -1; side <= 1; side += 2)
            {
                Prim(PrimitiveType.Cube, $"SideWall_{side}", root.transform,
                    new Vector3(side * 1.5f, 0.6f, 0f), new Vector3(0.3f, 1.2f, 2.0f), FeraldisWall);
            }

            // Ram under construction (horizontal log with metal head)
            PrimRot(PrimitiveType.Cylinder, "RamLog", root.transform,
                new Vector3(0f, 0.35f, 0.2f), new Vector3(0.25f, 1.2f, 0.25f),
                Quaternion.Euler(0f, 0f, 90f), new Color(0.4f, 0.28f, 0.15f));
            Prim(PrimitiveType.Sphere, "RamHead", root.transform,
                new Vector3(1.2f, 0.35f, 0.2f), new Vector3(0.4f, 0.4f, 0.4f),
                new Color(0.35f, 0.35f, 0.38f), 0.6f, 0.5f);

            // Chains (small dark cylinders hanging from roof)
            for (int i = -1; i <= 1; i += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Chain_{i}", root.transform,
                    new Vector3(i * 0.5f, 0.7f, 0.2f), new Vector3(0.04f, 0.35f, 0.04f),
                    new Color(0.25f, 0.25f, 0.28f), 0.5f, 0.4f);
            }

            return root;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SECT CHAPEL (generic shape for all 12 sects)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Generic chapel shape — small shrine with sect-colored accents.</summary>
        private static GameObject CreateChapel(Vector3 pos, Entity entity, int presentationId, byte culture)
        {
            var wall = CultureConfig.GetWallColor(culture);
            var roof = CultureConfig.GetRoofColor(culture);
            var trim = CultureConfig.GetTrimColor(culture);

            // Sect-specific accent color (varies by chapel ID for visual variety)
            float hue = ((presentationId - 390) / 12f);
            var sectAccent = Color.HSVToRGB(hue, 0.6f, 0.8f);

            var root = new GameObject($"Chapel_{presentationId}_{entity.Index}");
            root.transform.position = pos;

            // Small shrine body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.0f), wall);

            // Pointed roof
            PrimRot(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 1.45f, 0f), new Vector3(0.85f, 0.45f, 0.7f),
                Quaternion.Euler(0f, 0f, 45f), roof);

            // Sect symbol (colored sphere on front)
            Prim(PrimitiveType.Sphere, "SectSymbol", root.transform,
                new Vector3(0f, 0.9f, 0.55f), new Vector3(0.25f, 0.25f, 0.1f), sectAccent);

            // Small altar step
            Prim(PrimitiveType.Cube, "Altar", root.transform,
                new Vector3(0f, 0.1f, 0.6f), new Vector3(0.6f, 0.2f, 0.3f), trim);

            return root;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CULTURE MAINS / SUB-BUILDINGS that previously had no generator entry
        //  Auto-decorator handles foundation/stripes/lights — these methods only
        //  define the cultural silhouette.
        // ═══════════════════════════════════════════════════════════════════════

        // ── Alanthor: KingsCourt (363) — main palatial keep ──────────────────
        private static GameObject CreateKingsCourt(Vector3 pos, Entity entity)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);

            var root = new GameObject($"KingsCourt_{entity.Index}");
            root.transform.position = pos;

            // Wide marble plinth + central palace block.
            Prim(PrimitiveType.Cube, "MarblePlinth", root.transform,
                new Vector3(0f, 0.30f, 0f), new Vector3(5.0f, 0.30f, 5.0f), marble, smoothness: 0.6f);
            Prim(PrimitiveType.Cube, "PalaceBody", root.transform,
                new Vector3(0f, 1.55f, -0.40f), new Vector3(3.4f, 2.30f, 3.0f), marble, smoothness: 0.5f);
            // Curved cyan-tile pagoda over the body.
            AddPagodaTier(root.transform, "RoofMain",
                new Vector3(0f, 2.95f, -0.40f), new Vector3(4.0f, 0.20f, 3.5f), 0f, cyanTile);
            // Marble columns flanking the front entrance.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Col_{sx}", root.transform,
                    new Vector3(sx * 1.30f, 1.15f, 1.10f), new Vector3(0.35f, 1.15f, 0.35f), marble, smoothness: 0.6f);
            }
            // Cogwheel rosette on the gable.
            AddCogwheel(root.transform, "Cog_Front",
                new Vector3(0f, 2.55f, 1.60f), new Vector3(0f, 0f, 1f), 35f, brass);
            // Telescope-spire (mini Celestar nod).
            Prim(PrimitiveType.Cylinder, "Spire", root.transform,
                new Vector3(0f, 4.20f, -0.40f), new Vector3(0.50f, 1.20f, 0.50f), marble, smoothness: 0.6f);
            var lens = Prim(PrimitiveType.Sphere, "SpireLens", root.transform,
                new Vector3(0f, 5.00f, -0.40f), new Vector3(0.30f, 0.30f, 0.30f), cyanTile);
            ApplyEmissive(lens, cyanTile * 1.8f, baseColor: cyanTile);
            return root;
        }

        // ── Alanthor: Crucible (364) — magical forge ─────────────────────────
        private static GameObject CreateAlanthorCrucible(Vector3 pos, Entity entity)
        {
            var marble = new Color(0.92f, 0.92f, 0.90f);
            var cyanTile = new Color(0.30f, 0.78f, 0.85f);
            var brass = new Color(0.78f, 0.62f, 0.30f);
            var ember = new Color(1.00f, 0.55f, 0.20f);

            var root = new GameObject($"Crucible_{entity.Index}");
            root.transform.position = pos;
            // Marble forge body with tall chimney and animated cog drive.
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1.20f, 0f), new Vector3(2.8f, 2.0f, 2.4f), marble, smoothness: 0.5f);
            AddPagodaTier(root.transform, "Roof",
                new Vector3(0f, 2.40f, 0f), new Vector3(3.4f, 0.18f, 3.0f), 0f, cyanTile);
            Prim(PrimitiveType.Cylinder, "Chimney", root.transform,
                new Vector3(-0.95f, 3.30f, 0f), new Vector3(0.55f, 1.30f, 0.55f), marble);
            var smoke = Prim(PrimitiveType.Sphere, "ChimneyGlow", root.transform,
                new Vector3(-0.95f, 4.55f, 0f), new Vector3(0.55f, 0.40f, 0.55f), ember);
            ApplyEmissive(smoke, ember * 1.6f, baseColor: ember);
            AddCogwheel(root.transform, "Cog_DriveL", new Vector3(-1.40f, 1.20f, 1.10f), new Vector3(0f, 0f, 1f),  90f, brass);
            AddCogwheel(root.transform, "Cog_DriveR", new Vector3( 1.40f, 1.20f, 1.10f), new Vector3(0f, 0f, 1f), -90f, brass);
            // Forge mouth (emissive).
            var mouth = Prim(PrimitiveType.Cube, "ForgeMouth", root.transform,
                new Vector3(0f, 0.85f, 1.21f), new Vector3(0.85f, 0.55f, 0.10f), ember);
            ApplyEmissive(mouth, ember * 2.0f, baseColor: ember);
            return root;
        }

        // ── Runai: Vault (365) — fortified caravan deposit ────────────────────
        private static GameObject CreateRunaiVault(Vector3 pos, Entity entity)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);
            var rope = new Color(0.55f, 0.40f, 0.22f);

            var root = new GameObject($"RunaiVault_{entity.Index}");
            root.transform.position = pos;
            // Sandstone strongbox + dome cap.
            Prim(PrimitiveType.Cube, "SandBody", root.transform,
                new Vector3(0f, 1.10f, 0f), new Vector3(2.6f, 2.0f, 2.6f), sandstone);
            Prim(PrimitiveType.Sphere, "Dome", root.transform,
                new Vector3(0f, 2.65f, 0f), new Vector3(2.20f, 1.55f, 2.20f), sandstone);
            Prim(PrimitiveType.Cylinder, "DomeTip", root.transform,
                new Vector3(0f, 3.65f, 0f), new Vector3(0.10f, 0.40f, 0.10f), sandstone);
            // Iron-bound vault door with rope tassels.
            Prim(PrimitiveType.Sphere, "Door", root.transform,
                new Vector3(0f, 1.10f, 1.32f), new Vector3(1.0f, 1.0f, 0.18f),
                new Color(0.4f, 0.4f, 0.45f), 0.7f, 0.6f);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Tassel_{sx}", root.transform,
                    new Vector3(sx * 0.55f, 0.50f, 1.32f), new Vector3(0.04f, 0.40f, 0.04f), rope);
            }
            // Silk awning (sway, faction-tinted).
            var awning = PrimRot(PrimitiveType.Cube, "Stripe_Awning", root.transform,
                new Vector3(0f, 2.20f, 1.45f), new Vector3(2.40f, 0.04f, 1.10f),
                Quaternion.Euler(15f, 0f, 0f), silk);
            AnimateSway(awning, Vector3.right, 4f, 0.3f);
            return root;
        }

        // ── Runai: Veilsteel Foundry (366) — caravan-served forge ────────────
        private static GameObject CreateRunaiVeilsteelFoundry(Vector3 pos, Entity entity)
        {
            var sandstone = new Color(0.82f, 0.69f, 0.46f);
            var silk = new Color(0.95f, 0.55f, 0.30f);
            var ember = new Color(1.00f, 0.55f, 0.20f);

            var root = new GameObject($"VeilsteelFoundry_{entity.Index}");
            root.transform.position = pos;
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1.10f, 0f), new Vector3(3.2f, 2.0f, 2.6f), sandstone);
            Prim(PrimitiveType.Sphere, "DomeL", root.transform,
                new Vector3(-1.20f, 2.45f, 0f), new Vector3(1.30f, 1.20f, 1.30f), sandstone);
            Prim(PrimitiveType.Sphere, "DomeR", root.transform,
                new Vector3( 1.20f, 2.45f, 0f), new Vector3(1.30f, 1.20f, 1.30f), sandstone);
            Prim(PrimitiveType.Cylinder, "Chimney", root.transform,
                new Vector3(0f, 3.20f, 0f), new Vector3(0.50f, 1.25f, 0.50f), sandstone);
            var glow = Prim(PrimitiveType.Sphere, "ChimneyGlow", root.transform,
                new Vector3(0f, 4.40f, 0f), new Vector3(0.50f, 0.35f, 0.50f), ember);
            ApplyEmissive(glow, ember * 1.6f, baseColor: ember);
            // Forge-mouth and silk shade over the work yard.
            var mouth = Prim(PrimitiveType.Cube, "ForgeMouth", root.transform,
                new Vector3(0f, 0.80f, 1.31f), new Vector3(1.10f, 0.55f, 0.10f), ember);
            ApplyEmissive(mouth, ember * 2.0f, baseColor: ember);
            var canopy = PrimRot(PrimitiveType.Cube, "Stripe_Canopy", root.transform,
                new Vector3(0f, 2.30f, 1.55f), new Vector3(3.20f, 0.04f, 1.20f),
                Quaternion.Euler(12f, 0f, 0f), silk);
            AnimateSway(canopy, Vector3.right, 4f, 0.32f);
            return root;
        }

        // ── Feraldis: Foundry (367) — iron / crimson forge ───────────────────
        private static GameObject CreateFeraldisFoundry(Vector3 pos, Entity entity)
        {
            var iron = new Color(0.22f, 0.20f, 0.20f);
            var darkRed = new Color(0.45f, 0.12f, 0.10f);
            var ember = new Color(1.00f, 0.30f, 0.15f);
            var crystal = new Color(0.85f, 0.10f, 0.20f);

            var root = new GameObject($"FeraldisFoundry_{entity.Index}");
            root.transform.position = pos;
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1.20f, 0f), new Vector3(3.0f, 2.20f, 2.6f), iron, metallic: 0.4f);
            PrimRot(PrimitiveType.Cube, "Roof_Iron", root.transform,
                new Vector3(0f, 2.60f, 0f), new Vector3(3.4f, 0.20f, 2.8f),
                Quaternion.identity, darkRed, metallic: 0.5f);
            // Twin chimneys belching ember.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Chimney_{sx}", root.transform,
                    new Vector3(sx * 0.95f, 3.45f, 0f), new Vector3(0.45f, 1.25f, 0.45f), iron, metallic: 0.4f);
                var ch = Prim(PrimitiveType.Sphere, $"ChimneyGlow_{sx}", root.transform,
                    new Vector3(sx * 0.95f, 4.65f, 0f), new Vector3(0.45f, 0.30f, 0.45f), ember);
                ApplyEmissive(ch, ember * 1.6f, baseColor: ember);
            }
            // Crimson forge mouth.
            var mouth = Prim(PrimitiveType.Cube, "ForgeMouth", root.transform,
                new Vector3(0f, 0.95f, 1.31f), new Vector3(1.20f, 0.65f, 0.10f), ember);
            ApplyEmissive(mouth, ember * 2.2f, baseColor: ember);
            // Apex blood crystal.
            var shard = PrimRot(PrimitiveType.Cube, "BloodCrystal", root.transform,
                new Vector3(0f, 3.20f, 0f), new Vector3(0.45f, 0.95f, 0.45f),
                Quaternion.Euler(35f, 30f, 0f), crystal);
            ApplyEmissive(shard, crystal * 2.2f, baseColor: crystal);
            return root;
        }

        // ── Sect uniques (410-421): one signature building per sect ──────────
        private static GameObject CreateSectUnique(Vector3 pos, Entity entity, int presentationId)
        {
            var root = new GameObject($"SectUnique_{presentationId}_{entity.Index}");
            root.transform.position = pos;

            // Map sect id (4 per culture) onto culture-typed materials so the
            // visual matches the sect's parent culture.
            // 410-413: Alanthor sects, 414-417: Runai, 418-421: Feraldis.
            byte cultureId = presentationId switch
            {
                >= 410 and <= 413 => Cultures.Alanthor,
                >= 414 and <= 417 => Cultures.Runai,
                _                 => Cultures.Feraldis,
            };

            int sectIdx = (presentationId - 410) % 4;
            // Distinct accent colour per sect within the culture so all four
            // are visually distinguishable on the map.
            var accents = new Color[]
            {
                new Color(0.95f, 0.85f, 0.40f), // golden
                new Color(0.50f, 0.85f, 1.00f), // pale azure
                new Color(0.65f, 0.95f, 0.55f), // verdant
                new Color(0.85f, 0.45f, 0.85f), // arcane violet
            };
            var accent = accents[sectIdx];

            // Cultural body palette
            Color body, roof;
            switch (cultureId)
            {
                case Cultures.Alanthor:
                    body = new Color(0.92f, 0.92f, 0.90f); roof = new Color(0.30f, 0.78f, 0.85f);
                    break;
                case Cultures.Runai:
                    body = new Color(0.82f, 0.69f, 0.46f); roof = new Color(0.95f, 0.55f, 0.30f);
                    break;
                default:
                    body = new Color(0.22f, 0.20f, 0.20f); roof = new Color(0.45f, 0.12f, 0.10f);
                    break;
            }

            // Tall slim sanctum: 2-tier body + roof + sect-coloured emissive shard apex.
            Prim(PrimitiveType.Cube, "BodyLow", root.transform,
                new Vector3(0f, 1.05f, 0f), new Vector3(2.2f, 1.80f, 2.2f), body, smoothness: 0.5f);
            Prim(PrimitiveType.Cube, "BodyHigh", root.transform,
                new Vector3(0f, 2.45f, 0f), new Vector3(1.6f, 1.10f, 1.6f), body, smoothness: 0.5f);
            Prim(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 3.20f, 0f), new Vector3(2.0f, 0.20f, 2.0f), roof);
            // Sect-coloured spire shard
            var apex = PrimRot(PrimitiveType.Cube, "SectApex", root.transform,
                new Vector3(0f, 4.10f, 0f), new Vector3(0.40f, 1.20f, 0.40f),
                Quaternion.Euler(35f, 25f, 0f), accent);
            ApplyEmissive(apex, accent * 2.0f, baseColor: accent);
            // Door + side niches with sect-coloured glow.
            Prim(PrimitiveType.Cube, "Door", root.transform,
                new Vector3(0f, 0.75f, 1.12f), new Vector3(0.65f, 1.20f, 0.10f), HallWood);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var niche = Prim(PrimitiveType.Cube, $"Niche_{sx}", root.transform,
                    new Vector3(sx * 0.85f, 1.50f, 1.13f), new Vector3(0.30f, 0.45f, 0.06f), accent);
                ApplyEmissive(niche, accent * 1.6f, baseColor: accent);
            }
            // Sect light at the apex.
            AddPointLight(root.transform, "Light_Apex",
                new Vector3(0f, 4.10f, 0f), accent, intensity: 3.0f, range: 11f, bulbScale: 0f);
            return root;
        }
    }
}
