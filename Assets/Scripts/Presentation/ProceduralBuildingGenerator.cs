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
            return presentationId switch
            {
                // Era 1 Core
                100 => CreateHall(pos, entity, culture),
                101 => CreateGatherersHut(pos, entity, culture),
                102 => CreateHut(pos, entity, culture),
                510 => CreateBarracks(pos, entity, culture),

                // Era 1 Advanced
                520 => CreateTemple(pos, entity, culture),
                530 => CreateVault(pos, entity, culture),
                540 => CreateKeep(pos, entity, culture),

                // Runai culture buildings
                350 => CreateRunaiOutpost(pos, entity),
                351 => CreateRunaiTradeHub(pos, entity),
                352 => CreateRunaiBazaar(pos, entity),
                353 => CreateRunaiSiegeWorkshop(pos, entity),
                // 355 is shared between Runai_TradingPost and Alanthor_Garrison — use Create355() instead

                // Alanthor culture buildings
                354 => CreateAlanthorTower(pos, entity),
                356 => CreateAlanthorStable(pos, entity),
                357 => CreateAlanthorSiegeYard(pos, entity),

                // Feraldis culture buildings
                358 => CreateFeraldisHuntingLodge(pos, entity),
                359 => CreateFeraldisLoggingStation(pos, entity),
                360 => CreateFeraldisLonghouse(pos, entity),
                361 => CreateFeraldisTotemTower(pos, entity),
                362 => CreateFeraldisSiegeYard(pos, entity),

                // Sect chapels (generic chapel shape)
                >= 390 and <= 401 => CreateChapel(pos, entity, presentationId, culture),

                _ => null
            };
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
        //  ERA 1 CORE BUILDINGS (smaller, ~1.0x scale)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Hall: Large rectangular base + peaked roof + entrance arch. ~3x2x3 units.</summary>
        private static GameObject CreateHall(Vector3 pos, Entity entity, byte culture)
        {
            var wall = CultureConfig.GetWallColor(culture);
            var roof = CultureConfig.GetRoofColor(culture);
            var trim = CultureConfig.GetTrimColor(culture);

            var root = new GameObject($"Hall_{entity.Index}");
            root.transform.position = pos;

            // Main body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1f, 0f), new Vector3(3f, 2f, 2.5f), wall);

            // Peaked roof (rotated cube as ridge)
            PrimRot(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 2.35f, 0f), new Vector3(2.2f, 0.7f, 1.8f),
                Quaternion.Euler(0f, 0f, 45f), roof);

            // Entrance arch (front)
            Prim(PrimitiveType.Cube, "Door", root.transform,
                new Vector3(0f, 0.5f, 1.3f), new Vector3(0.8f, 1f, 0.3f), trim);

            // Entrance columns
            Prim(PrimitiveType.Cylinder, "PillarL", root.transform,
                new Vector3(-0.5f, 0.75f, 1.3f), new Vector3(0.15f, 0.75f, 0.15f), trim);
            Prim(PrimitiveType.Cylinder, "PillarR", root.transform,
                new Vector3(0.5f, 0.75f, 1.3f), new Vector3(0.15f, 0.75f, 0.15f), trim);

            // Foundation step
            Prim(PrimitiveType.Cube, "Foundation", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(3.4f, 0.1f, 2.9f), trim);

            return root;
        }

        /// <summary>Hut: Small cube base + pyramid roof. ~1.5x1.5x1.5 units.</summary>
        private static GameObject CreateHut(Vector3 pos, Entity entity, byte culture)
        {
            var wall = CultureConfig.GetWallColor(culture);
            var roof = CultureConfig.GetRoofColor(culture);

            var root = new GameObject($"Hut_{entity.Index}");
            root.transform.position = pos;

            // Walls
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.65f, 0f), new Vector3(1.4f, 1.3f, 1.4f), wall);

            // Pyramid roof (rotated cube)
            PrimRot(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 1.6f, 0f), new Vector3(1.1f, 0.55f, 1.1f),
                Quaternion.Euler(0f, 45f, 45f), roof);

            return root;
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

            return root;
        }

        /// <summary>Barracks: Long rectangular building + flat roof + weapon rack. ~3x1.5x2 units.</summary>
        private static GameObject CreateBarracks(Vector3 pos, Entity entity, byte culture)
        {
            var wall = CultureConfig.GetWallColor(culture);
            var roof = CultureConfig.GetRoofColor(culture);
            var trim = CultureConfig.GetTrimColor(culture);

            var root = new GameObject($"Barracks_{entity.Index}");
            root.transform.position = pos;

            // Main body (long)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.85f, 0f), new Vector3(3f, 1.7f, 1.8f), wall);

            // Flat roof with slight overhang
            Prim(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 1.8f, 0f), new Vector3(3.2f, 0.15f, 2.0f), roof);

            // Weapon rack (side detail)
            Prim(PrimitiveType.Cube, "Rack", root.transform,
                new Vector3(1.6f, 0.7f, 0f), new Vector3(0.1f, 1.0f, 0.6f), trim);
            // Weapons on rack (small cylinders)
            Prim(PrimitiveType.Cylinder, "Sword1", root.transform,
                new Vector3(1.65f, 0.9f, -0.15f), new Vector3(0.05f, 0.4f, 0.05f), new Color(0.5f, 0.5f, 0.55f), 0.6f, 0.5f);
            Prim(PrimitiveType.Cylinder, "Sword2", root.transform,
                new Vector3(1.65f, 0.9f, 0.15f), new Vector3(0.05f, 0.4f, 0.05f), new Color(0.5f, 0.5f, 0.55f), 0.6f, 0.5f);

            // Door
            Prim(PrimitiveType.Cube, "Door", root.transform,
                new Vector3(0f, 0.45f, 0.95f), new Vector3(0.6f, 0.9f, 0.1f), trim);

            return root;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ERA 1 ADVANCED BUILDINGS (medium scale ~1.2x)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Temple of Ridan: Tall spire + circular base. ~2.5x4x2.5 units.</summary>
        private static GameObject CreateTemple(Vector3 pos, Entity entity, byte culture)
        {
            var wall = CultureConfig.GetWallColor(culture);
            var roof = CultureConfig.GetRoofColor(culture);
            var trim = CultureConfig.GetTrimColor(culture);

            var root = new GameObject($"Temple_{entity.Index}");
            root.transform.position = pos;

            // Circular base platform
            Prim(PrimitiveType.Cylinder, "Base", root.transform,
                new Vector3(0f, 0.15f, 0f), new Vector3(3.0f, 0.15f, 3.0f), trim);

            // Main tower body
            Prim(PrimitiveType.Cylinder, "Tower", root.transform,
                new Vector3(0f, 1.5f, 0f), new Vector3(1.8f, 1.5f, 1.8f), wall);

            // Upper section (narrower)
            Prim(PrimitiveType.Cylinder, "Upper", root.transform,
                new Vector3(0f, 3.2f, 0f), new Vector3(1.2f, 0.7f, 1.2f), wall);

            // Spire
            Prim(PrimitiveType.Cylinder, "Spire", root.transform,
                new Vector3(0f, 4.3f, 0f), new Vector3(0.4f, 0.8f, 0.4f), roof);
            Prim(PrimitiveType.Sphere, "SpireTip", root.transform,
                new Vector3(0f, 5.2f, 0f), new Vector3(0.35f, 0.35f, 0.35f), roof);

            // Chapel slot markers (7 small cubes around base)
            for (int i = 0; i < 7; i++)
            {
                float angle = i * (360f / 7f) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * 1.7f;
                float z = Mathf.Sin(angle) * 1.7f;
                Prim(PrimitiveType.Cube, $"Slot_{i}", root.transform,
                    new Vector3(x, 0.2f, z), new Vector3(0.3f, 0.4f, 0.3f), trim);
            }

            return root;
        }

        /// <summary>Vault of Almierra: Reinforced cube + vault door + metal bands. ~2.5x2.5x2.5 units.</summary>
        private static GameObject CreateVault(Vector3 pos, Entity entity, byte culture)
        {
            var wall = CultureConfig.GetWallColor(culture);
            var roof = CultureConfig.GetRoofColor(culture);
            var trim = CultureConfig.GetTrimColor(culture);
            var metal = new Color(0.4f, 0.4f, 0.45f);

            var root = new GameObject($"Vault_{entity.Index}");
            root.transform.position = pos;

            // Main vault body (thick cube)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1.25f, 0f), new Vector3(2.5f, 2.5f, 2.5f), wall);

            // Roof cap
            Prim(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 2.6f, 0f), new Vector3(2.7f, 0.15f, 2.7f), roof);

            // Metal reinforcement bands (horizontal)
            Prim(PrimitiveType.Cube, "Band1", root.transform,
                new Vector3(0f, 0.6f, 1.28f), new Vector3(2.3f, 0.1f, 0.05f), metal, 0.7f, 0.6f);
            Prim(PrimitiveType.Cube, "Band2", root.transform,
                new Vector3(0f, 1.5f, 1.28f), new Vector3(2.3f, 0.1f, 0.05f), metal, 0.7f, 0.6f);

            // Vault door (circular — use sphere flattened)
            Prim(PrimitiveType.Sphere, "Door", root.transform,
                new Vector3(0f, 1.0f, 1.28f), new Vector3(1.0f, 1.0f, 0.15f), metal, 0.8f, 0.7f);

            // Corner pillars
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Pillar_{x}_{z}", root.transform,
                    new Vector3(x * 1.2f, 1.25f, z * 1.2f), new Vector3(0.2f, 1.25f, 0.2f), trim);
            }

            return root;
        }

        /// <summary>Fiendstone Keep: Fortress with corner towers + walls. ~3x3x3 units.</summary>
        private static GameObject CreateKeep(Vector3 pos, Entity entity, byte culture)
        {
            var wall = CultureConfig.GetWallColor(culture);
            var roof = CultureConfig.GetRoofColor(culture);
            var trim = CultureConfig.GetTrimColor(culture);

            var root = new GameObject($"Keep_{entity.Index}");
            root.transform.position = pos;

            // Central keep body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 1.25f, 0f), new Vector3(2.2f, 2.5f, 2.2f), wall);

            // Battlements top
            Prim(PrimitiveType.Cube, "BattlementTop", root.transform,
                new Vector3(0f, 2.6f, 0f), new Vector3(2.5f, 0.2f, 2.5f), wall);

            // Corner towers (4)
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Prim(PrimitiveType.Cylinder, $"Tower_{x}_{z}", root.transform,
                    new Vector3(x * 1.3f, 1.5f, z * 1.3f), new Vector3(0.6f, 1.5f, 0.6f), wall);
                // Tower cap
                Prim(PrimitiveType.Cylinder, $"TowerCap_{x}_{z}", root.transform,
                    new Vector3(x * 1.3f, 3.1f, z * 1.3f), new Vector3(0.7f, 0.1f, 0.7f), roof);
            }

            // Gate
            Prim(PrimitiveType.Cube, "Gate", root.transform,
                new Vector3(0f, 0.6f, 1.15f), new Vector3(0.8f, 1.2f, 0.15f), trim);

            // Foundation
            Prim(PrimitiveType.Cube, "Foundation", root.transform,
                new Vector3(0f, 0.05f, 0f), new Vector3(3.2f, 0.1f, 3.2f), trim);

            return root;
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
    }
}
