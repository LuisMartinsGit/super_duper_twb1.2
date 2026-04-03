// ProceduralUnitGenerator.cs
// Generates procedural unit visuals from Unity primitives.
// Each unit type gets a unique silhouette with culture-aware accent coloring.
// Faction color is applied AFTER by PresentationSpawnSystem.ApplyFactionColor.
// Location: Assets/Scripts/Presentation/ProceduralUnitGenerator.cs

using UnityEngine;
using Unity.Entities;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Static factory for procedural unit GameObjects built from primitives.
    /// Human infantry scaled to 1.8 units tall, mounted ~2.5, siege ~2.8.
    /// White/neutral base = faction color applied later. Culture accents are baked in.
    /// </summary>
    public static class ProceduralUnitGenerator
    {
        private static Shader _litShader;
        private static Shader LitShader =>
            _litShader ??= Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Neutral body color — PresentationSpawnSystem overwrites with faction color
        private static readonly Color Skin = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color White = Color.white;
        private static readonly Color Metal = new Color(0.5f, 0.5f, 0.55f);
        private static readonly Color DarkMetal = new Color(0.3f, 0.3f, 0.35f);
        private static readonly Color Wood = new Color(0.45f, 0.32f, 0.18f);
        private static readonly Color DarkWood = new Color(0.35f, 0.22f, 0.12f);
        private static readonly Color Leather = new Color(0.55f, 0.40f, 0.25f);
        private static readonly Color Stone = new Color(0.55f, 0.55f, 0.52f);
        private static readonly Color DarkStone = new Color(0.38f, 0.38f, 0.36f);

        // Culture accent colors
        private static readonly Color RunaiAccent = new Color(0.76f, 0.65f, 0.45f);
        private static readonly Color AlanthorAccent = new Color(0.45f, 0.45f, 0.42f);
        private static readonly Color FeraldisAccent = new Color(0.70f, 0.18f, 0.15f);

        // ===============================================================
        //  PUBLIC API
        // ===============================================================

        // Scale factors to bring procedural models to correct world height.
        // Models are authored at ~0.4-0.6 units for infantry, ~0.85-1.0 for mounted/siege.
        // These factors scale them to: human infantry=1.8, mounted=2.5, siege=2.8.
        private const float InfantryScale = 3.5f;   // ~0.5 * 3.5 = 1.75 ≈ 1.8
        private const float MountedScale  = 2.9f;   // ~0.85 * 2.9 = 2.47 ≈ 2.5
        private const float SiegeScale    = 3.1f;   // ~0.9 * 3.1 = 2.79 ≈ 2.8

        /// <summary>
        /// Try to create a procedural unit for the given PresentationId.
        /// Returns null if the ID is not a known unit type handled here.
        /// Human infantry are scaled to ~1.8 units tall, mounted ~2.5, siege ~2.8.
        /// </summary>
        public static GameObject TryCreate(int presentationId, Vector3 pos, Entity entity)
        {
            GameObject go = presentationId switch
            {
                // Era 1 Base Units
                200 => CreateBuilder(pos, entity),
                201 => CreateSwordsman(pos, entity),
                202 => CreateArcher(pos, entity),
                203 => CreateMiner(pos, entity),
                206 => CreateScout(pos, entity),
                207 => CreateLitharch(pos, entity),
                210 => CreateBerserker(pos, entity),

                // Runai Culture Units
                330 => CreateSpearman(pos, entity),
                331 => CreateSkirmisher(pos, entity),
                332 => CreateRaider(pos, entity),
                333 => CreateCatapult(pos, entity),

                // Alanthor Culture Units
                334 => CreateSentinel(pos, entity),
                335 => CreateCrossbowman(pos, entity),
                336 => CreateCataphract(pos, entity),
                337 => CreateBallista(pos, entity),

                // Feraldis Culture Units
                338 => CreateHunter(pos, entity),
                339 => CreateWarboarRider(pos, entity),
                340 => CreateSiegeRam(pos, entity),

                // Sect Unique Units
                370 => CreateScarGuard(pos, entity, presentationId),
                371 => CreateGolemAutark(pos, entity, presentationId),
                372 => CreateStoneWarden(pos, entity, presentationId),
                373 => CreateArchivistAdept(pos, entity, presentationId),
                374 => CreateFlameWarden(pos, entity, presentationId),
                375 => CreateVaultKeeper(pos, entity, presentationId),
                376 => CreateGlassmarkArcanist(pos, entity, presentationId),
                377 => CreateJudicator(pos, entity, presentationId),
                378 => CreateAshblade(pos, entity, presentationId),
                379 => CreateBrandbreaker(pos, entity, presentationId),
                380 => CreateChaincaster(pos, entity, presentationId),
                381 => CreateNullblade(pos, entity, presentationId),

                _ => null
            };

            if (go == null) return null;

            // Store the procedural scale on the root so PresentationSpawnSystem
            // can multiply it with the ECS LocalTransform.Scale each frame.
            float scale = GetUnitScale(presentationId);
            var tag = go.AddComponent<ProceduralScaleTag>();
            tag.BaseScale = scale;
            go.transform.localScale = Vector3.one * scale;

            return go;
        }

        /// <summary>
        /// Get the scale factor for a unit presentation ID.
        /// Infantry (human on foot) → 1.8 units tall.
        /// Mounted (rider + mount) → 2.5 units tall.
        /// Siege engines → 2.8 units tall.
        /// </summary>
        private static float GetUnitScale(int pid)
        {
            return pid switch
            {
                // Mounted units
                332 => MountedScale,  // Raider (camel/horse mount)
                336 => MountedScale,  // Cataphract (armored horse)
                339 => MountedScale,  // WarboarRider (boar mount)

                // Siege engines
                333 => SiegeScale,    // Catapult
                337 => SiegeScale,    // Ballista
                340 => SiegeScale,    // SiegeRam

                // All other units are human infantry
                _ => InfantryScale
            };
        }

        // ===============================================================
        //  MATERIAL / PRIMITIVE HELPERS
        // ===============================================================

        private static Material MakeMat(Color color, float metallic = 0f, float smoothness = 0.3f)
        {
            var mat = new Material(LitShader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        private static Material MakeEmissiveMat(Color color, Color emissiveColor, float intensity = 2f)
        {
            var mat = MakeMat(color);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emissiveColor * intensity);
            }
            return mat;
        }

        private static void SetMat(GameObject go, Color color, float metallic = 0f, float smoothness = 0.3f)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material = MakeMat(color, metallic, smoothness);
        }

        private static void SetEmissiveMat(GameObject go, Color color, Color emissiveColor, float intensity = 2f)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material = MakeEmissiveMat(color, emissiveColor, intensity);
        }

        private static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
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

        private static GameObject PrimEmissive(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Color color, Color emissiveColor, float intensity = 2f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            SetEmissiveMat(go, color, emissiveColor, intensity);
            DestroyCollider(go);
            return go;
        }

        private static Color SectAccent(int presentationId)
        {
            return Color.HSVToRGB((presentationId - 370) / 12f, 0.7f, 0.85f);
        }

        // ===============================================================
        //  ERA 1 BASE UNITS (neutral colors, faction color applied later)
        // ===============================================================

        /// <summary>Builder (200): Short stocky figure with hammer tool. ~0.4 units tall.</summary>
        private static GameObject CreateBuilder(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Builder_{entity.Index}");
            root.transform.position = pos;

            // Stocky body (wide cube)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.15f, 0f), new Vector3(0.18f, 0.2f, 0.12f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.32f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Skin);

            // Legs (two small cubes)
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.05f, 0.02f, 0f), new Vector3(0.06f, 0.06f, 0.06f), Leather);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.05f, 0.02f, 0f), new Vector3(0.06f, 0.06f, 0.06f), Leather);

            // Hammer handle (cylinder)
            PrimRot(PrimitiveType.Cylinder, "HammerHandle", root.transform,
                new Vector3(0.14f, 0.25f, 0f), new Vector3(0.02f, 0.1f, 0.02f),
                Quaternion.Euler(0f, 0f, -30f), Wood);

            // Hammer head (small cube)
            PrimRot(PrimitiveType.Cube, "HammerHead", root.transform,
                new Vector3(0.2f, 0.33f, 0f), new Vector3(0.06f, 0.04f, 0.04f),
                Quaternion.Euler(0f, 0f, -30f), Metal, 0.6f, 0.5f);

            return root;
        }

        /// <summary>Swordsman (201): Medium figure with sword blade and small shield. ~0.5 units tall.</summary>
        private static GameObject CreateSwordsman(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Swordsman_{entity.Index}");
            root.transform.position = pos;

            // Body (armored torso)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.16f, 0.22f, 0.1f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.11f, 0.11f, 0.11f), Skin);

            // Helmet crest (small cube on top)
            Prim(PrimitiveType.Cube, "Helmet", root.transform,
                new Vector3(0f, 0.45f, 0f), new Vector3(0.04f, 0.04f, 0.1f), Metal, 0.5f, 0.5f);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);

            // Sword blade (thin tall cube on right side)
            PrimRot(PrimitiveType.Cube, "SwordBlade", root.transform,
                new Vector3(0.14f, 0.3f, 0f), new Vector3(0.02f, 0.22f, 0.04f),
                Quaternion.Euler(0f, 0f, -10f), Metal, 0.7f, 0.6f);

            // Sword guard (tiny cube)
            Prim(PrimitiveType.Cube, "SwordGuard", root.transform,
                new Vector3(0.12f, 0.2f, 0f), new Vector3(0.06f, 0.015f, 0.015f), DarkMetal, 0.5f, 0.4f);

            // Shield (flat cube on left arm)
            Prim(PrimitiveType.Cube, "Shield", root.transform,
                new Vector3(-0.13f, 0.22f, 0f), new Vector3(0.03f, 0.14f, 0.1f), White);

            return root;
        }

        /// <summary>Archer (202): Slim figure with bow and quiver on back. ~0.5 units tall.</summary>
        private static GameObject CreateArcher(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Archer_{entity.Index}");
            root.transform.position = pos;

            // Slim body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.12f, 0.22f, 0.08f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Hood/cap
            Prim(PrimitiveType.Cube, "Hood", root.transform,
                new Vector3(0f, 0.42f, -0.01f), new Vector3(0.1f, 0.06f, 0.08f), Leather);

            // Legs (slim)
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.03f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), Leather);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.03f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), Leather);

            // Bow (curved thin cylinder on left side)
            PrimRot(PrimitiveType.Cylinder, "Bow", root.transform,
                new Vector3(-0.12f, 0.28f, 0f), new Vector3(0.02f, 0.16f, 0.02f),
                Quaternion.Euler(0f, 0f, 15f), Wood);

            // Bow tips (small spheres at ends to suggest curvature)
            Prim(PrimitiveType.Sphere, "BowTop", root.transform,
                new Vector3(-0.1f, 0.43f, 0f), new Vector3(0.02f, 0.02f, 0.02f), Wood);
            Prim(PrimitiveType.Sphere, "BowBot", root.transform,
                new Vector3(-0.14f, 0.13f, 0f), new Vector3(0.02f, 0.02f, 0.02f), Wood);

            // Quiver on back (small cube)
            PrimRot(PrimitiveType.Cube, "Quiver", root.transform,
                new Vector3(0f, 0.3f, -0.07f), new Vector3(0.04f, 0.14f, 0.04f),
                Quaternion.Euler(10f, 0f, 0f), Leather);

            // Arrow tips sticking out of quiver
            Prim(PrimitiveType.Cylinder, "Arrow1", root.transform,
                new Vector3(0.01f, 0.4f, -0.07f), new Vector3(0.01f, 0.04f, 0.01f), Metal, 0.5f, 0.4f);
            Prim(PrimitiveType.Cylinder, "Arrow2", root.transform,
                new Vector3(-0.01f, 0.39f, -0.07f), new Vector3(0.01f, 0.04f, 0.01f), Metal, 0.5f, 0.4f);

            return root;
        }

        /// <summary>Miner (203): Short stocky figure with pickaxe and ore sack. ~0.4 units tall.</summary>
        private static GameObject CreateMiner(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Miner_{entity.Index}");
            root.transform.position = pos;

            // Stocky body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.14f, 0f), new Vector3(0.18f, 0.18f, 0.12f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.3f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Skin);

            // Hard hat (flat cylinder on top)
            Prim(PrimitiveType.Cylinder, "Hat", root.transform,
                new Vector3(0f, 0.37f, 0f), new Vector3(0.13f, 0.02f, 0.13f), new Color(0.85f, 0.75f, 0.1f));

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.05f, 0.02f, 0f), new Vector3(0.06f, 0.06f, 0.06f), Leather);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.05f, 0.02f, 0f), new Vector3(0.06f, 0.06f, 0.06f), Leather);

            // Pickaxe handle (angled cylinder)
            PrimRot(PrimitiveType.Cylinder, "PickHandle", root.transform,
                new Vector3(0.14f, 0.22f, 0f), new Vector3(0.02f, 0.12f, 0.02f),
                Quaternion.Euler(0f, 0f, -40f), Wood);

            // Pickaxe head (small angled cube)
            PrimRot(PrimitiveType.Cube, "PickHead", root.transform,
                new Vector3(0.22f, 0.32f, 0f), new Vector3(0.1f, 0.025f, 0.025f),
                Quaternion.Euler(0f, 0f, -40f), Metal, 0.6f, 0.5f);

            // Ore sack on back (sphere)
            Prim(PrimitiveType.Sphere, "OreSack", root.transform,
                new Vector3(0f, 0.16f, -0.09f), new Vector3(0.1f, 0.1f, 0.08f), Leather);

            return root;
        }

        /// <summary>Scout (206): Slim tall figure with no weapon, small cape behind. ~0.55 units tall.</summary>
        private static GameObject CreateScout(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Scout_{entity.Index}");
            root.transform.position = pos;

            // Slim tall body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.1f, 0.24f, 0.08f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Long legs (tall and thin)
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.03f, 0.04f, 0f), new Vector3(0.04f, 0.12f, 0.04f), Leather);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.03f, 0.04f, 0f), new Vector3(0.04f, 0.12f, 0.04f), Leather);

            // Cape (angled flat cube behind)
            PrimRot(PrimitiveType.Cube, "Cape", root.transform,
                new Vector3(0f, 0.2f, -0.08f), new Vector3(0.12f, 0.2f, 0.015f),
                Quaternion.Euler(10f, 0f, 0f), White);

            // Light belt pouch
            Prim(PrimitiveType.Cube, "Pouch", root.transform,
                new Vector3(0.06f, 0.12f, 0.02f), new Vector3(0.03f, 0.03f, 0.03f), Leather);

            return root;
        }

        /// <summary>Litharch (207): Tall robed figure with staff + sphere tip. ~0.6 units tall.</summary>
        private static GameObject CreateLitharch(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Litharch_{entity.Index}");
            root.transform.position = pos;

            // Robed body (wider at base, narrower at top - two cubes)
            Prim(PrimitiveType.Cube, "RobeLower", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(0.18f, 0.14f, 0.14f), White);
            Prim(PrimitiveType.Cube, "RobeUpper", root.transform,
                new Vector3(0f, 0.26f, 0f), new Vector3(0.14f, 0.2f, 0.1f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.44f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Hood (cube over head, slightly larger)
            Prim(PrimitiveType.Cube, "Hood", root.transform,
                new Vector3(0f, 0.46f, -0.02f), new Vector3(0.11f, 0.08f, 0.1f), White);

            // Staff (tall thin cylinder)
            Prim(PrimitiveType.Cylinder, "Staff", root.transform,
                new Vector3(0.12f, 0.28f, 0f), new Vector3(0.02f, 0.28f, 0.02f), Wood);

            // Staff orb tip (sphere)
            PrimEmissive(PrimitiveType.Sphere, "StaffOrb", root.transform,
                new Vector3(0.12f, 0.57f, 0f), new Vector3(0.06f, 0.06f, 0.06f),
                new Color(0.6f, 0.8f, 1f), new Color(0.4f, 0.6f, 1f), 1.5f);

            return root;
        }

        /// <summary>Berserker (210): Bulky figure with dual axes, no shield. ~0.5 units tall.</summary>
        private static GameObject CreateBerserker(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Berserker_{entity.Index}");
            root.transform.position = pos;

            // Bulky body (wide and thick)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.2f, 0.22f, 0.14f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Skin);

            // Wild hair / fur collar
            Prim(PrimitiveType.Cube, "FurCollar", root.transform,
                new Vector3(0f, 0.33f, 0f), new Vector3(0.22f, 0.04f, 0.16f), Leather);

            // Thick legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.05f, 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.07f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.05f, 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.07f), White);

            // Right axe (handle + head)
            PrimRot(PrimitiveType.Cylinder, "AxeR_Handle", root.transform,
                new Vector3(0.16f, 0.28f, 0f), new Vector3(0.02f, 0.1f, 0.02f),
                Quaternion.Euler(0f, 0f, -20f), Wood);
            PrimRot(PrimitiveType.Cube, "AxeR_Head", root.transform,
                new Vector3(0.22f, 0.36f, 0f), new Vector3(0.06f, 0.07f, 0.02f),
                Quaternion.Euler(0f, 0f, -20f), Metal, 0.6f, 0.5f);

            // Left axe (handle + head)
            PrimRot(PrimitiveType.Cylinder, "AxeL_Handle", root.transform,
                new Vector3(-0.16f, 0.28f, 0f), new Vector3(0.02f, 0.1f, 0.02f),
                Quaternion.Euler(0f, 0f, 20f), Wood);
            PrimRot(PrimitiveType.Cube, "AxeL_Head", root.transform,
                new Vector3(-0.22f, 0.36f, 0f), new Vector3(0.06f, 0.07f, 0.02f),
                Quaternion.Euler(0f, 0f, 20f), Metal, 0.6f, 0.5f);

            return root;
        }

        // ===============================================================
        //  RUNAI CULTURE UNITS (sandstone accent)
        // ===============================================================

        /// <summary>Spearman (330): Medium figure with long spear and small round shield. ~0.5 units tall.</summary>
        private static GameObject CreateSpearman(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Spearman_{entity.Index}");
            root.transform.position = pos;

            // Body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.14f, 0.22f, 0.1f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Runai headwrap (flat cube)
            Prim(PrimitiveType.Cube, "Headwrap", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.11f, 0.04f, 0.11f), RunaiAccent);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), White);

            // Long spear (tall thin cylinder)
            Prim(PrimitiveType.Cylinder, "SpearShaft", root.transform,
                new Vector3(0.1f, 0.3f, 0f), new Vector3(0.015f, 0.3f, 0.015f), Wood);

            // Spear tip
            Prim(PrimitiveType.Cube, "SpearTip", root.transform,
                new Vector3(0.1f, 0.6f, 0f), new Vector3(0.025f, 0.04f, 0.015f), Metal, 0.7f, 0.6f);

            // Small round shield (flattened sphere on left arm)
            Prim(PrimitiveType.Sphere, "Shield", root.transform,
                new Vector3(-0.12f, 0.22f, 0.02f), new Vector3(0.12f, 0.12f, 0.03f), RunaiAccent);

            return root;
        }

        /// <summary>Skirmisher (331): Slim figure with javelins on back, light armor. ~0.48 units tall.</summary>
        private static GameObject CreateSkirmisher(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Skirmisher_{entity.Index}");
            root.transform.position = pos;

            // Slim body with light armor
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.12f, 0.2f, 0.08f), White);

            // Light armor vest overlay
            Prim(PrimitiveType.Cube, "ArmorVest", root.transform,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.13f, 0.12f, 0.09f), RunaiAccent);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.37f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.03f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), Leather);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.03f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), Leather);

            // Javelins on back (3 thin cylinders angled)
            PrimRot(PrimitiveType.Cylinder, "Javelin1", root.transform,
                new Vector3(-0.02f, 0.35f, -0.06f), new Vector3(0.012f, 0.18f, 0.012f),
                Quaternion.Euler(8f, 0f, 3f), Wood);
            PrimRot(PrimitiveType.Cylinder, "Javelin2", root.transform,
                new Vector3(0.01f, 0.34f, -0.06f), new Vector3(0.012f, 0.18f, 0.012f),
                Quaternion.Euler(8f, 0f, -2f), Wood);
            PrimRot(PrimitiveType.Cylinder, "Javelin3", root.transform,
                new Vector3(0.04f, 0.33f, -0.06f), new Vector3(0.012f, 0.18f, 0.012f),
                Quaternion.Euler(8f, 0f, -5f), Wood);

            return root;
        }

        /// <summary>Raider (332): Figure mounted on small mount, curved sword. ~0.85 units tall.</summary>
        private static GameObject CreateRaider(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Raider_{entity.Index}");
            root.transform.position = pos;

            // Mount body (elongated sphere)
            Prim(PrimitiveType.Sphere, "MountBody", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.2f, 0.45f), RunaiAccent);

            // Mount head (small sphere in front)
            Prim(PrimitiveType.Sphere, "MountHead", root.transform,
                new Vector3(0f, 0.28f, 0.22f), new Vector3(0.1f, 0.1f, 0.12f), RunaiAccent);

            // Mount legs (4 thin cylinders)
            Prim(PrimitiveType.Cylinder, "MountLegFL", root.transform,
                new Vector3(-0.1f, 0.06f, 0.12f), new Vector3(0.03f, 0.06f, 0.03f), RunaiAccent * 0.85f);
            Prim(PrimitiveType.Cylinder, "MountLegFR", root.transform,
                new Vector3(0.1f, 0.06f, 0.12f), new Vector3(0.03f, 0.06f, 0.03f), RunaiAccent * 0.85f);
            Prim(PrimitiveType.Cylinder, "MountLegBL", root.transform,
                new Vector3(-0.1f, 0.06f, -0.12f), new Vector3(0.03f, 0.06f, 0.03f), RunaiAccent * 0.85f);
            Prim(PrimitiveType.Cylinder, "MountLegBR", root.transform,
                new Vector3(0.1f, 0.06f, -0.12f), new Vector3(0.03f, 0.06f, 0.03f), RunaiAccent * 0.85f);

            // Rider body
            Prim(PrimitiveType.Cube, "RiderBody", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.12f, 0.18f, 0.08f), White);

            // Rider head
            Prim(PrimitiveType.Sphere, "RiderHead", root.transform,
                new Vector3(0f, 0.56f, 0f), new Vector3(0.09f, 0.09f, 0.09f), Skin);

            // Turban
            Prim(PrimitiveType.Sphere, "Turban", root.transform,
                new Vector3(0f, 0.6f, 0f), new Vector3(0.1f, 0.06f, 0.1f), RunaiAccent);

            // Curved sword (right side)
            PrimRot(PrimitiveType.Cube, "Sword", root.transform,
                new Vector3(0.12f, 0.48f, 0.04f), new Vector3(0.02f, 0.18f, 0.03f),
                Quaternion.Euler(0f, 0f, -15f), Metal, 0.7f, 0.6f);

            return root;
        }

        /// <summary>Catapult (333): Wheeled platform with throwing arm. ~0.9 units tall at peak.</summary>
        private static GameObject CreateCatapult(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Catapult_{entity.Index}");
            root.transform.position = pos;

            // Platform base (flat cube)
            Prim(PrimitiveType.Cube, "Platform", root.transform,
                new Vector3(0f, 0.15f, 0f), new Vector3(0.5f, 0.08f, 0.7f), Wood);

            // Wheels (4 cylinders rotated on side)
            PrimRot(PrimitiveType.Cylinder, "WheelFL", root.transform,
                new Vector3(-0.28f, 0.1f, 0.2f), new Vector3(0.18f, 0.02f, 0.18f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelFR", root.transform,
                new Vector3(0.28f, 0.1f, 0.2f), new Vector3(0.18f, 0.02f, 0.18f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelBL", root.transform,
                new Vector3(-0.28f, 0.1f, -0.2f), new Vector3(0.18f, 0.02f, 0.18f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelBR", root.transform,
                new Vector3(0.28f, 0.1f, -0.2f), new Vector3(0.18f, 0.02f, 0.18f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);

            // Arm support frame (two vertical cubes)
            Prim(PrimitiveType.Cube, "FrameL", root.transform,
                new Vector3(-0.12f, 0.32f, 0f), new Vector3(0.04f, 0.28f, 0.04f), Wood);
            Prim(PrimitiveType.Cube, "FrameR", root.transform,
                new Vector3(0.12f, 0.32f, 0f), new Vector3(0.04f, 0.28f, 0.04f), Wood);

            // Crossbar
            Prim(PrimitiveType.Cube, "Crossbar", root.transform,
                new Vector3(0f, 0.45f, 0f), new Vector3(0.28f, 0.03f, 0.03f), Wood);

            // Throwing arm (long angled cylinder)
            PrimRot(PrimitiveType.Cylinder, "Arm", root.transform,
                new Vector3(0f, 0.55f, 0.15f), new Vector3(0.03f, 0.3f, 0.03f),
                Quaternion.Euler(30f, 0f, 0f), DarkWood);

            // Bucket at arm end (small cube)
            Prim(PrimitiveType.Cube, "Bucket", root.transform,
                new Vector3(0f, 0.82f, 0.4f), new Vector3(0.08f, 0.04f, 0.08f), RunaiAccent);

            return root;
        }

        // ===============================================================
        //  ALANTHOR CULTURE UNITS (grey-green accent)
        // ===============================================================

        /// <summary>Sentinel (334): Heavy armored figure with tower shield and short sword. ~0.5 units tall.</summary>
        private static GameObject CreateSentinel(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Sentinel_{entity.Index}");
            root.transform.position = pos;

            // Wide armored body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.18f, 0.22f, 0.12f), White);

            // Shoulder pauldrons
            Prim(PrimitiveType.Cube, "PauldronL", root.transform,
                new Vector3(-0.11f, 0.3f, 0f), new Vector3(0.06f, 0.04f, 0.1f), AlanthorAccent, 0.3f, 0.4f);
            Prim(PrimitiveType.Cube, "PauldronR", root.transform,
                new Vector3(0.11f, 0.3f, 0f), new Vector3(0.06f, 0.04f, 0.1f), AlanthorAccent, 0.3f, 0.4f);

            // Head with full helm
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.11f, 0.11f, 0.11f), Metal, 0.5f, 0.5f);

            // Helm visor slit (tiny dark cube)
            Prim(PrimitiveType.Cube, "Visor", root.transform,
                new Vector3(0f, 0.38f, 0.056f), new Vector3(0.08f, 0.015f, 0.01f), DarkMetal);

            // Heavy legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.05f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.05f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);

            // Tower shield (large flat cube on left)
            Prim(PrimitiveType.Cube, "TowerShield", root.transform,
                new Vector3(-0.15f, 0.2f, 0.02f), new Vector3(0.03f, 0.28f, 0.16f), AlanthorAccent, 0.2f, 0.3f);

            // Short sword (right side)
            Prim(PrimitiveType.Cube, "ShortSword", root.transform,
                new Vector3(0.12f, 0.25f, 0f), new Vector3(0.02f, 0.14f, 0.03f), Metal, 0.7f, 0.6f);

            return root;
        }

        /// <summary>Crossbowman (335): Medium armored figure with T-shaped crossbow. ~0.5 units tall.</summary>
        private static GameObject CreateCrossbowman(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Crossbowman_{entity.Index}");
            root.transform.position = pos;

            // Armored body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.15f, 0.22f, 0.1f), White);

            // Heavy chest plate detail
            Prim(PrimitiveType.Cube, "ChestPlate", root.transform,
                new Vector3(0f, 0.24f, 0.05f), new Vector3(0.14f, 0.12f, 0.02f), AlanthorAccent, 0.3f, 0.4f);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Helm (half sphere on top)
            Prim(PrimitiveType.Sphere, "Helm", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.11f, 0.06f, 0.11f), Metal, 0.5f, 0.5f);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);

            // Crossbow stock (horizontal cube pointing forward)
            Prim(PrimitiveType.Cube, "XbowStock", root.transform,
                new Vector3(0.1f, 0.26f, 0.06f), new Vector3(0.03f, 0.03f, 0.16f), Wood);

            // Crossbow limbs (horizontal cube perpendicular — the T shape)
            Prim(PrimitiveType.Cube, "XbowLimbs", root.transform,
                new Vector3(0.1f, 0.26f, 0.15f), new Vector3(0.16f, 0.02f, 0.02f), Wood);

            // Bolt (very thin cylinder)
            Prim(PrimitiveType.Cylinder, "Bolt", root.transform,
                new Vector3(0.1f, 0.27f, 0.1f), new Vector3(0.008f, 0.06f, 0.008f), Metal, 0.5f, 0.4f);

            return root;
        }

        /// <summary>Cataphract (336): Armored rider on armored horse, lance. ~1.0 units tall.</summary>
        private static GameObject CreateCataphract(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Cataphract_{entity.Index}");
            root.transform.position = pos;

            // Horse body (large elongated cube)
            Prim(PrimitiveType.Cube, "HorseBody", root.transform,
                new Vector3(0f, 0.25f, 0f), new Vector3(0.22f, 0.2f, 0.5f), AlanthorAccent, 0.2f, 0.3f);

            // Horse head (cube angled forward)
            PrimRot(PrimitiveType.Cube, "HorseHead", root.transform,
                new Vector3(0f, 0.35f, 0.28f), new Vector3(0.1f, 0.14f, 0.1f),
                Quaternion.Euler(-20f, 0f, 0f), AlanthorAccent, 0.2f, 0.3f);

            // Horse armor plate (front)
            Prim(PrimitiveType.Cube, "HorseArmor", root.transform,
                new Vector3(0f, 0.3f, 0.18f), new Vector3(0.18f, 0.08f, 0.12f), Metal, 0.5f, 0.5f);

            // Horse legs (4)
            Prim(PrimitiveType.Cylinder, "HorseLegFL", root.transform,
                new Vector3(-0.09f, 0.08f, 0.15f), new Vector3(0.04f, 0.08f, 0.04f), AlanthorAccent * 0.85f);
            Prim(PrimitiveType.Cylinder, "HorseLegFR", root.transform,
                new Vector3(0.09f, 0.08f, 0.15f), new Vector3(0.04f, 0.08f, 0.04f), AlanthorAccent * 0.85f);
            Prim(PrimitiveType.Cylinder, "HorseLegBL", root.transform,
                new Vector3(-0.09f, 0.08f, -0.15f), new Vector3(0.04f, 0.08f, 0.04f), AlanthorAccent * 0.85f);
            Prim(PrimitiveType.Cylinder, "HorseLegBR", root.transform,
                new Vector3(0.09f, 0.08f, -0.15f), new Vector3(0.04f, 0.08f, 0.04f), AlanthorAccent * 0.85f);

            // Rider torso
            Prim(PrimitiveType.Cube, "RiderBody", root.transform,
                new Vector3(0f, 0.48f, 0f), new Vector3(0.14f, 0.2f, 0.1f), White);

            // Rider head
            Prim(PrimitiveType.Sphere, "RiderHead", root.transform,
                new Vector3(0f, 0.64f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Metal, 0.5f, 0.5f);

            // Helm plume
            Prim(PrimitiveType.Cube, "Plume", root.transform,
                new Vector3(0f, 0.7f, -0.02f), new Vector3(0.02f, 0.04f, 0.08f), White);

            // Lance (long cylinder)
            PrimRot(PrimitiveType.Cylinder, "Lance", root.transform,
                new Vector3(0.12f, 0.55f, 0.2f), new Vector3(0.02f, 0.4f, 0.02f),
                Quaternion.Euler(20f, 0f, 5f), Wood);

            // Lance tip
            Prim(PrimitiveType.Cube, "LanceTip", root.transform,
                new Vector3(0.14f, 0.9f, 0.46f), new Vector3(0.03f, 0.05f, 0.02f), Metal, 0.7f, 0.6f);

            return root;
        }

        /// <summary>Ballista (337): Large wheeled siege engine with bolt rail. ~0.9 units tall.</summary>
        private static GameObject CreateBallista(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Ballista_{entity.Index}");
            root.transform.position = pos;

            // Base platform (cube)
            Prim(PrimitiveType.Cube, "Base", root.transform,
                new Vector3(0f, 0.15f, 0f), new Vector3(0.45f, 0.1f, 0.6f), Wood);

            // Wheels (4 side-mounted cylinders)
            PrimRot(PrimitiveType.Cylinder, "WheelFL", root.transform,
                new Vector3(-0.26f, 0.12f, 0.18f), new Vector3(0.2f, 0.025f, 0.2f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelFR", root.transform,
                new Vector3(0.26f, 0.12f, 0.18f), new Vector3(0.2f, 0.025f, 0.2f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelBL", root.transform,
                new Vector3(-0.26f, 0.12f, -0.18f), new Vector3(0.2f, 0.025f, 0.2f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelBR", root.transform,
                new Vector3(0.26f, 0.12f, -0.18f), new Vector3(0.2f, 0.025f, 0.2f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);

            // Vertical frame supports
            Prim(PrimitiveType.Cube, "FrameL", root.transform,
                new Vector3(-0.14f, 0.35f, -0.05f), new Vector3(0.04f, 0.32f, 0.04f), Wood);
            Prim(PrimitiveType.Cube, "FrameR", root.transform,
                new Vector3(0.14f, 0.35f, -0.05f), new Vector3(0.04f, 0.32f, 0.04f), Wood);

            // Bolt rail (long cylinder pointing forward)
            PrimRot(PrimitiveType.Cylinder, "BoltRail", root.transform,
                new Vector3(0f, 0.4f, 0.15f), new Vector3(0.04f, 0.3f, 0.04f),
                Quaternion.Euler(80f, 0f, 0f), AlanthorAccent, 0.3f, 0.4f);

            // Limbs / torsion arms (two angled cubes)
            PrimRot(PrimitiveType.Cube, "LimbL", root.transform,
                new Vector3(-0.18f, 0.42f, 0.05f), new Vector3(0.14f, 0.025f, 0.025f),
                Quaternion.Euler(0f, -20f, 0f), Wood);
            PrimRot(PrimitiveType.Cube, "LimbR", root.transform,
                new Vector3(0.18f, 0.42f, 0.05f), new Vector3(0.14f, 0.025f, 0.025f),
                Quaternion.Euler(0f, 20f, 0f), Wood);

            // Bolt (thin long cylinder)
            PrimRot(PrimitiveType.Cylinder, "Bolt", root.transform,
                new Vector3(0f, 0.42f, 0.28f), new Vector3(0.015f, 0.2f, 0.015f),
                Quaternion.Euler(85f, 0f, 0f), Metal, 0.6f, 0.5f);

            return root;
        }

        // ===============================================================
        //  FERALDIS CULTURE UNITS (crimson accent)
        // ===============================================================

        /// <summary>Hunter (338): Crouched slim figure with short bow and pelt on shoulder. ~0.4 units tall.</summary>
        private static GameObject CreateHunter(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"Hunter_{entity.Index}");
            root.transform.position = pos;

            // Crouched body (lower, slightly angled)
            PrimRot(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.14f, 0f), new Vector3(0.12f, 0.18f, 0.08f),
                Quaternion.Euler(10f, 0f, 0f), White);

            // Head (lower due to crouch)
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.3f, 0.02f), new Vector3(0.09f, 0.09f, 0.09f), Skin);

            // Legs (bent)
            PrimRot(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0.02f), new Vector3(0.04f, 0.08f, 0.04f),
                Quaternion.Euler(15f, 0f, 0f), Leather);
            PrimRot(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0.02f), new Vector3(0.04f, 0.08f, 0.04f),
                Quaternion.Euler(15f, 0f, 0f), Leather);

            // Short bow (left side)
            PrimRot(PrimitiveType.Cylinder, "Bow", root.transform,
                new Vector3(-0.1f, 0.2f, 0.02f), new Vector3(0.018f, 0.1f, 0.018f),
                Quaternion.Euler(0f, 0f, 12f), Wood);

            // Animal pelt on shoulder (small sphere)
            Prim(PrimitiveType.Sphere, "Pelt", root.transform,
                new Vector3(-0.07f, 0.28f, -0.02f), new Vector3(0.08f, 0.06f, 0.06f), FeraldisAccent);

            // Pelt tail (tiny cylinder)
            PrimRot(PrimitiveType.Cylinder, "PeltTail", root.transform,
                new Vector3(-0.08f, 0.24f, -0.06f), new Vector3(0.015f, 0.04f, 0.015f),
                Quaternion.Euler(30f, 0f, 0f), FeraldisAccent);

            return root;
        }

        /// <summary>WarboarRider (339): Rider on stocky boar with tusks. ~0.85 units tall.</summary>
        private static GameObject CreateWarboarRider(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"WarboarRider_{entity.Index}");
            root.transform.position = pos;

            // Boar body (wide sphere — stocky)
            Prim(PrimitiveType.Sphere, "BoarBody", root.transform,
                new Vector3(0f, 0.18f, 0f), new Vector3(0.35f, 0.25f, 0.4f), FeraldisAccent * 0.7f);

            // Boar head (smaller sphere in front)
            Prim(PrimitiveType.Sphere, "BoarHead", root.transform,
                new Vector3(0f, 0.22f, 0.22f), new Vector3(0.15f, 0.13f, 0.15f), FeraldisAccent * 0.7f);

            // Tusks (two small angled cylinders)
            PrimRot(PrimitiveType.Cylinder, "TuskL", root.transform,
                new Vector3(-0.06f, 0.2f, 0.32f), new Vector3(0.02f, 0.05f, 0.02f),
                Quaternion.Euler(-40f, 15f, 0f), new Color(0.9f, 0.88f, 0.8f));
            PrimRot(PrimitiveType.Cylinder, "TuskR", root.transform,
                new Vector3(0.06f, 0.2f, 0.32f), new Vector3(0.02f, 0.05f, 0.02f),
                Quaternion.Euler(-40f, -15f, 0f), new Color(0.9f, 0.88f, 0.8f));

            // Boar legs (4 short thick)
            Prim(PrimitiveType.Cylinder, "BoarLegFL", root.transform,
                new Vector3(-0.12f, 0.05f, 0.1f), new Vector3(0.05f, 0.05f, 0.05f), FeraldisAccent * 0.6f);
            Prim(PrimitiveType.Cylinder, "BoarLegFR", root.transform,
                new Vector3(0.12f, 0.05f, 0.1f), new Vector3(0.05f, 0.05f, 0.05f), FeraldisAccent * 0.6f);
            Prim(PrimitiveType.Cylinder, "BoarLegBL", root.transform,
                new Vector3(-0.12f, 0.05f, -0.1f), new Vector3(0.05f, 0.05f, 0.05f), FeraldisAccent * 0.6f);
            Prim(PrimitiveType.Cylinder, "BoarLegBR", root.transform,
                new Vector3(0.12f, 0.05f, -0.1f), new Vector3(0.05f, 0.05f, 0.05f), FeraldisAccent * 0.6f);

            // Rider body
            Prim(PrimitiveType.Cube, "RiderBody", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.12f, 0.18f, 0.08f), White);

            // Rider head
            Prim(PrimitiveType.Sphere, "RiderHead", root.transform,
                new Vector3(0f, 0.56f, 0f), new Vector3(0.09f, 0.09f, 0.09f), Skin);

            // Fur mantle
            Prim(PrimitiveType.Cube, "FurMantle", root.transform,
                new Vector3(0f, 0.5f, -0.04f), new Vector3(0.16f, 0.06f, 0.08f), FeraldisAccent);

            // Weapon — heavy mace
            PrimRot(PrimitiveType.Cylinder, "MaceHandle", root.transform,
                new Vector3(0.12f, 0.48f, 0f), new Vector3(0.02f, 0.1f, 0.02f),
                Quaternion.Euler(0f, 0f, -25f), Wood);
            Prim(PrimitiveType.Sphere, "MaceHead", root.transform,
                new Vector3(0.18f, 0.58f, 0f), new Vector3(0.06f, 0.06f, 0.06f), Metal, 0.5f, 0.4f);

            return root;
        }

        /// <summary>SiegeRam (340): Covered mobile structure with ram log. ~0.8 units tall.</summary>
        private static GameObject CreateSiegeRam(Vector3 pos, Entity entity)
        {
            var root = new GameObject($"SiegeRam_{entity.Index}");
            root.transform.position = pos;

            // Roof structure (long cube)
            Prim(PrimitiveType.Cube, "Roof", root.transform,
                new Vector3(0f, 0.45f, 0f), new Vector3(0.4f, 0.06f, 0.8f), FeraldisAccent);

            // Side walls (two flat cubes)
            Prim(PrimitiveType.Cube, "WallL", root.transform,
                new Vector3(-0.18f, 0.3f, 0f), new Vector3(0.03f, 0.24f, 0.7f), Wood);
            Prim(PrimitiveType.Cube, "WallR", root.transform,
                new Vector3(0.18f, 0.3f, 0f), new Vector3(0.03f, 0.24f, 0.7f), Wood);

            // Back wall
            Prim(PrimitiveType.Cube, "WallBack", root.transform,
                new Vector3(0f, 0.3f, -0.38f), new Vector3(0.36f, 0.24f, 0.03f), Wood);

            // Ram log (long cylinder poking out front)
            Prim(PrimitiveType.Cylinder, "RamLog", root.transform,
                new Vector3(0f, 0.25f, 0.3f), new Vector3(0.1f, 0.35f, 0.1f), DarkWood);

            // Ram log rotated to point forward
            var ramLog = root.transform.Find("RamLog");
            if (ramLog != null) ramLog.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Ram head cap (metal tip)
            Prim(PrimitiveType.Sphere, "RamHead", root.transform,
                new Vector3(0f, 0.25f, 0.65f), new Vector3(0.12f, 0.12f, 0.08f), Metal, 0.6f, 0.5f);

            // Wheels (4)
            PrimRot(PrimitiveType.Cylinder, "WheelFL", root.transform,
                new Vector3(-0.22f, 0.1f, 0.22f), new Vector3(0.16f, 0.02f, 0.16f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelFR", root.transform,
                new Vector3(0.22f, 0.1f, 0.22f), new Vector3(0.16f, 0.02f, 0.16f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelBL", root.transform,
                new Vector3(-0.22f, 0.1f, -0.22f), new Vector3(0.16f, 0.02f, 0.16f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);
            PrimRot(PrimitiveType.Cylinder, "WheelBR", root.transform,
                new Vector3(0.22f, 0.1f, -0.22f), new Vector3(0.16f, 0.02f, 0.16f),
                Quaternion.Euler(0f, 0f, 90f), DarkWood);

            return root;
        }

        // ===============================================================
        //  SECT UNIQUE UNITS (hue-shifted accent per sect)
        // ===============================================================

        /// <summary>ScarGuard (370): Hulking figure, massive double shield wall, scarred face. ~0.55 units tall.</summary>
        private static GameObject CreateScarGuard(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"ScarGuard_{entity.Index}");
            root.transform.position = pos;

            // Hulking body (wide)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.22f, 0.24f, 0.14f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Skin);

            // Scar detail (thin dark cube across face)
            PrimRot(PrimitiveType.Cube, "Scar", root.transform,
                new Vector3(0f, 0.43f, 0.06f), new Vector3(0.08f, 0.01f, 0.005f),
                Quaternion.Euler(0f, 0f, 25f), FeraldisAccent);

            // Thick legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.06f, 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.07f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.06f, 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.07f), White);

            // Shield wall — two overlapping flat cubes
            Prim(PrimitiveType.Cube, "ShieldA", root.transform,
                new Vector3(-0.16f, 0.22f, 0.04f), new Vector3(0.03f, 0.3f, 0.18f), accent, 0.2f, 0.3f);
            Prim(PrimitiveType.Cube, "ShieldB", root.transform,
                new Vector3(-0.13f, 0.22f, 0.06f), new Vector3(0.03f, 0.28f, 0.16f), accent * 0.85f, 0.2f, 0.3f);

            return root;
        }

        /// <summary>GolemAutark (371): Large stone golem with sphere joints, glowing eyes. ~0.6 units tall.</summary>
        private static GameObject CreateGolemAutark(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"GolemAutark_{entity.Index}");
            root.transform.position = pos;

            // Oversized cube body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.25f, 0f), new Vector3(0.28f, 0.3f, 0.2f), Stone, 0.1f, 0.2f);

            // Head (cube — stone-like)
            Prim(PrimitiveType.Cube, "Head", root.transform,
                new Vector3(0f, 0.48f, 0f), new Vector3(0.16f, 0.14f, 0.14f), Stone, 0.1f, 0.2f);

            // Glowing eyes (two small emissive spheres)
            PrimEmissive(PrimitiveType.Sphere, "EyeL", root.transform,
                new Vector3(-0.04f, 0.5f, 0.07f), new Vector3(0.03f, 0.03f, 0.02f),
                accent, accent, 3f);
            PrimEmissive(PrimitiveType.Sphere, "EyeR", root.transform,
                new Vector3(0.04f, 0.5f, 0.07f), new Vector3(0.03f, 0.03f, 0.02f),
                accent, accent, 3f);

            // Sphere joints at shoulders
            Prim(PrimitiveType.Sphere, "ShoulderL", root.transform,
                new Vector3(-0.18f, 0.38f, 0f), new Vector3(0.08f, 0.08f, 0.08f), DarkStone, 0.1f, 0.2f);
            Prim(PrimitiveType.Sphere, "ShoulderR", root.transform,
                new Vector3(0.18f, 0.38f, 0f), new Vector3(0.08f, 0.08f, 0.08f), DarkStone, 0.1f, 0.2f);

            // Arms (cubes from shoulder joints)
            Prim(PrimitiveType.Cube, "ArmL", root.transform,
                new Vector3(-0.22f, 0.25f, 0f), new Vector3(0.08f, 0.18f, 0.08f), Stone, 0.1f, 0.2f);
            Prim(PrimitiveType.Cube, "ArmR", root.transform,
                new Vector3(0.22f, 0.25f, 0f), new Vector3(0.08f, 0.18f, 0.08f), Stone, 0.1f, 0.2f);

            // Sphere joints at hips
            Prim(PrimitiveType.Sphere, "HipL", root.transform,
                new Vector3(-0.08f, 0.1f, 0f), new Vector3(0.06f, 0.06f, 0.06f), DarkStone, 0.1f, 0.2f);
            Prim(PrimitiveType.Sphere, "HipR", root.transform,
                new Vector3(0.08f, 0.1f, 0f), new Vector3(0.06f, 0.06f, 0.06f), DarkStone, 0.1f, 0.2f);

            // Legs (cube)
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.08f, 0.02f, 0f), new Vector3(0.08f, 0.1f, 0.08f), Stone, 0.1f, 0.2f);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.08f, 0.02f, 0f), new Vector3(0.08f, 0.1f, 0.08f), Stone, 0.1f, 0.2f);

            return root;
        }

        /// <summary>StoneWarden (372): Heavy stone figure with stone hammer, grey metallic. ~0.55 units tall.</summary>
        private static GameObject CreateStoneWarden(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"StoneWarden_{entity.Index}");
            root.transform.position = pos;

            // Heavy stone body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.2f, 0.24f, 0.14f), Stone, 0.3f, 0.4f);

            // Head
            Prim(PrimitiveType.Cube, "Head", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Stone, 0.3f, 0.4f);

            // Stone crown/helm detail
            Prim(PrimitiveType.Cube, "Crown", root.transform,
                new Vector3(0f, 0.5f, 0f), new Vector3(0.14f, 0.03f, 0.14f), accent, 0.2f, 0.3f);

            // Thick legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.06f, 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.07f), Stone, 0.3f, 0.4f);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.06f, 0.04f, 0f), new Vector3(0.07f, 0.1f, 0.07f), Stone, 0.3f, 0.4f);

            // Stone hammer handle
            PrimRot(PrimitiveType.Cylinder, "HammerHandle", root.transform,
                new Vector3(0.16f, 0.3f, 0f), new Vector3(0.025f, 0.14f, 0.025f),
                Quaternion.Euler(0f, 0f, -25f), DarkStone);

            // Stone hammer head (cube — larger than normal)
            PrimRot(PrimitiveType.Cube, "HammerHead", root.transform,
                new Vector3(0.25f, 0.42f, 0f), new Vector3(0.1f, 0.07f, 0.07f),
                Quaternion.Euler(0f, 0f, -25f), Stone, 0.4f, 0.5f);

            return root;
        }

        /// <summary>ArchivistAdept (373): Robed scholar with floating book and glasses. ~0.55 units tall.</summary>
        private static GameObject CreateArchivistAdept(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"ArchivistAdept_{entity.Index}");
            root.transform.position = pos;

            // Robed body
            Prim(PrimitiveType.Cube, "RobeLower", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(0.16f, 0.14f, 0.12f), White);
            Prim(PrimitiveType.Cube, "RobeUpper", root.transform,
                new Vector3(0f, 0.26f, 0f), new Vector3(0.12f, 0.18f, 0.08f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Glasses (two tiny cubes)
            Prim(PrimitiveType.Cube, "GlassL", root.transform,
                new Vector3(-0.025f, 0.43f, 0.05f), new Vector3(0.025f, 0.015f, 0.005f), Metal, 0.5f, 0.6f);
            Prim(PrimitiveType.Cube, "GlassR", root.transform,
                new Vector3(0.025f, 0.43f, 0.05f), new Vector3(0.025f, 0.015f, 0.005f), Metal, 0.5f, 0.6f);
            // Glasses bridge
            Prim(PrimitiveType.Cube, "GlassBridge", root.transform,
                new Vector3(0f, 0.435f, 0.051f), new Vector3(0.02f, 0.005f, 0.003f), Metal, 0.5f, 0.6f);

            // Floating book (small rotated cube hovering above right hand)
            PrimRot(PrimitiveType.Cube, "Book", root.transform,
                new Vector3(0.14f, 0.4f, 0.04f), new Vector3(0.06f, 0.05f, 0.015f),
                Quaternion.Euler(5f, -15f, 10f), accent);

            // Book pages (slightly lighter inner)
            PrimRot(PrimitiveType.Cube, "BookPages", root.transform,
                new Vector3(0.14f, 0.4f, 0.042f), new Vector3(0.055f, 0.045f, 0.01f),
                Quaternion.Euler(5f, -15f, 10f), new Color(0.9f, 0.88f, 0.8f));

            return root;
        }

        /// <summary>FlameWarden (374): Armored figure with flame aura around fist and torch staff. ~0.55 units tall.</summary>
        private static GameObject CreateFlameWarden(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var flame = new Color(1f, 0.5f, 0.1f);
            var root = new GameObject($"FlameWarden_{entity.Index}");
            root.transform.position = pos;

            // Armored body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.16f, 0.22f, 0.1f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Helm
            Prim(PrimitiveType.Cube, "Helm", root.transform,
                new Vector3(0f, 0.43f, 0f), new Vector3(0.11f, 0.05f, 0.11f), accent, 0.3f, 0.4f);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);

            // Flame aura around left fist (emissive orange sphere)
            PrimEmissive(PrimitiveType.Sphere, "FlameAura", root.transform,
                new Vector3(-0.14f, 0.24f, 0.02f), new Vector3(0.1f, 0.1f, 0.1f),
                flame, flame, 3f);

            // Fist inside aura (small dark cube)
            Prim(PrimitiveType.Cube, "Fist", root.transform,
                new Vector3(-0.14f, 0.24f, 0.02f), new Vector3(0.04f, 0.04f, 0.04f), DarkMetal);

            // Torch staff (right side)
            Prim(PrimitiveType.Cylinder, "TorchStaff", root.transform,
                new Vector3(0.12f, 0.28f, 0f), new Vector3(0.02f, 0.24f, 0.02f), Wood);

            // Torch flame tip
            PrimEmissive(PrimitiveType.Sphere, "TorchFlame", root.transform,
                new Vector3(0.12f, 0.52f, 0f), new Vector3(0.05f, 0.07f, 0.05f),
                flame, flame, 2.5f);

            return root;
        }

        /// <summary>VaultKeeper (375): Heavy armored figure with key weapon and chest shield. ~0.55 units tall.</summary>
        private static GameObject CreateVaultKeeper(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"VaultKeeper_{entity.Index}");
            root.transform.position = pos;

            // Heavy armored body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.2f, 0.24f, 0.14f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.42f, 0f), new Vector3(0.11f, 0.11f, 0.11f), Metal, 0.5f, 0.5f);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.06f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.06f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);

            // Chest shield (front-facing flat cube with accent)
            Prim(PrimitiveType.Cube, "ChestShield", root.transform,
                new Vector3(0f, 0.24f, 0.075f), new Vector3(0.16f, 0.16f, 0.02f), accent, 0.3f, 0.4f);

            // Key weapon handle (cylinder)
            PrimRot(PrimitiveType.Cylinder, "KeyHandle", root.transform,
                new Vector3(0.16f, 0.3f, 0f), new Vector3(0.025f, 0.16f, 0.025f),
                Quaternion.Euler(0f, 0f, -15f), Metal, 0.6f, 0.5f);

            // Key head (elongated cube at top)
            PrimRot(PrimitiveType.Cube, "KeyHead", root.transform,
                new Vector3(0.2f, 0.46f, 0f), new Vector3(0.08f, 0.04f, 0.02f),
                Quaternion.Euler(0f, 0f, -15f), accent, 0.4f, 0.5f);

            // Key teeth (small cube notches)
            PrimRot(PrimitiveType.Cube, "KeyTeeth", root.transform,
                new Vector3(0.22f, 0.44f, 0f), new Vector3(0.03f, 0.06f, 0.015f),
                Quaternion.Euler(0f, 0f, -15f), Metal, 0.6f, 0.5f);

            return root;
        }

        /// <summary>GlassmarkArcanist (376): Slim robed figure, crystal orb in hand, mirror shield. ~0.55 units tall.</summary>
        private static GameObject CreateGlassmarkArcanist(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"GlassmarkArcanist_{entity.Index}");
            root.transform.position = pos;

            // Slim robed body
            Prim(PrimitiveType.Cube, "RobeLower", root.transform,
                new Vector3(0f, 0.1f, 0f), new Vector3(0.14f, 0.12f, 0.1f), White);
            Prim(PrimitiveType.Cube, "RobeUpper", root.transform,
                new Vector3(0f, 0.24f, 0f), new Vector3(0.1f, 0.18f, 0.07f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.4f, 0f), new Vector3(0.09f, 0.09f, 0.09f), Skin);

            // Crystal orb in right hand (emissive sphere)
            PrimEmissive(PrimitiveType.Sphere, "CrystalOrb", root.transform,
                new Vector3(0.12f, 0.3f, 0.04f), new Vector3(0.07f, 0.07f, 0.07f),
                accent, accent, 2f);

            // Mirror shield on left (reflective flat cube)
            Prim(PrimitiveType.Cube, "MirrorShield", root.transform,
                new Vector3(-0.12f, 0.24f, 0.02f), new Vector3(0.02f, 0.16f, 0.12f),
                new Color(0.85f, 0.88f, 0.9f), 0.8f, 0.9f);

            // Mirror highlight (thinner brighter cube)
            Prim(PrimitiveType.Cube, "MirrorHighlight", root.transform,
                new Vector3(-0.115f, 0.26f, 0.02f), new Vector3(0.005f, 0.1f, 0.08f),
                Color.white, 0.9f, 0.95f);

            return root;
        }

        /// <summary>Judicator (377): Tall armored judge with gavel weapon and heavy cape. ~0.6 units tall.</summary>
        private static GameObject CreateJudicator(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"Judicator_{entity.Index}");
            root.transform.position = pos;

            // Tall armored body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.24f, 0f), new Vector3(0.16f, 0.28f, 0.1f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.46f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Judge's coif/helm
            Prim(PrimitiveType.Cube, "Coif", root.transform,
                new Vector3(0f, 0.5f, 0f), new Vector3(0.11f, 0.05f, 0.11f), accent, 0.2f, 0.3f);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.06f, 0.12f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.06f, 0.12f, 0.06f), White);

            // Heavy scale cape (wide angled flat cube behind)
            PrimRot(PrimitiveType.Cube, "Cape", root.transform,
                new Vector3(0f, 0.2f, -0.08f), new Vector3(0.2f, 0.26f, 0.02f),
                Quaternion.Euler(8f, 0f, 0f), accent);

            // Gavel handle (cylinder)
            PrimRot(PrimitiveType.Cylinder, "GavelHandle", root.transform,
                new Vector3(0.14f, 0.34f, 0f), new Vector3(0.02f, 0.12f, 0.02f),
                Quaternion.Euler(0f, 0f, -20f), Wood);

            // Gavel head (cube)
            PrimRot(PrimitiveType.Cube, "GavelHead", root.transform,
                new Vector3(0.2f, 0.44f, 0f), new Vector3(0.08f, 0.05f, 0.05f),
                Quaternion.Euler(0f, 0f, -20f), DarkMetal, 0.5f, 0.4f);

            return root;
        }

        /// <summary>Ashblade (378): Dark figure with glowing ember blade and floating ash particles. ~0.5 units tall.</summary>
        private static GameObject CreateAshblade(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var ember = new Color(1f, 0.35f, 0.05f);
            var ash = new Color(0.25f, 0.22f, 0.2f);
            var root = new GameObject($"Ashblade_{entity.Index}");
            root.transform.position = pos;

            // Dark body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.14f, 0.22f, 0.09f), DarkMetal);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.1f, 0.1f), ash);

            // Hood
            Prim(PrimitiveType.Cube, "Hood", root.transform,
                new Vector3(0f, 0.42f, -0.02f), new Vector3(0.1f, 0.06f, 0.09f), DarkMetal);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), DarkMetal);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), DarkMetal);

            // Ember blade (thin emissive cube)
            var blade = PrimRot(PrimitiveType.Cube, "EmberBlade", root.transform,
                new Vector3(0.14f, 0.32f, 0f), new Vector3(0.02f, 0.22f, 0.035f),
                Quaternion.Euler(0f, 0f, -10f), ember);
            SetEmissiveMat(blade, ember, ember, 2.5f);

            // Blade guard
            Prim(PrimitiveType.Cube, "BladeGuard", root.transform,
                new Vector3(0.12f, 0.22f, 0f), new Vector3(0.06f, 0.015f, 0.015f), DarkMetal);

            // Floating ash particles (small dark spheres at various heights)
            Prim(PrimitiveType.Sphere, "Ash1", root.transform,
                new Vector3(0.08f, 0.48f, 0.06f), new Vector3(0.015f, 0.015f, 0.015f), ash);
            Prim(PrimitiveType.Sphere, "Ash2", root.transform,
                new Vector3(-0.06f, 0.44f, -0.04f), new Vector3(0.012f, 0.012f, 0.012f), ash);
            Prim(PrimitiveType.Sphere, "Ash3", root.transform,
                new Vector3(0.1f, 0.52f, -0.02f), new Vector3(0.01f, 0.01f, 0.01f), ash);
            Prim(PrimitiveType.Sphere, "Ash4", root.transform,
                new Vector3(-0.03f, 0.5f, 0.05f), new Vector3(0.013f, 0.013f, 0.013f), ash);

            return root;
        }

        /// <summary>Brandbreaker (379): Muscular figure with broken chain weapon (sphere on cylinder), branded marks. ~0.5 units tall.</summary>
        private static GameObject CreateBrandbreaker(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"Brandbreaker_{entity.Index}");
            root.transform.position = pos;

            // Muscular body (wide and thick)
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.2f, 0.22f, 0.12f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.11f, 0.11f, 0.11f), Skin);

            // Branded marks on torso (thin accent cubes)
            PrimRot(PrimitiveType.Cube, "Brand1", root.transform,
                new Vector3(0.04f, 0.24f, 0.065f), new Vector3(0.06f, 0.01f, 0.003f),
                Quaternion.Euler(0f, 0f, 15f), accent);
            PrimRot(PrimitiveType.Cube, "Brand2", root.transform,
                new Vector3(-0.03f, 0.18f, 0.065f), new Vector3(0.05f, 0.01f, 0.003f),
                Quaternion.Euler(0f, 0f, -20f), accent);

            // Thick legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.06f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.06f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);

            // Chain weapon — chain segment (thin cylinder from hand)
            PrimRot(PrimitiveType.Cylinder, "Chain", root.transform,
                new Vector3(0.18f, 0.3f, 0f), new Vector3(0.015f, 0.1f, 0.015f),
                Quaternion.Euler(0f, 0f, -35f), Metal, 0.5f, 0.4f);

            // Wrecking ball at chain end (sphere)
            Prim(PrimitiveType.Sphere, "Ball", root.transform,
                new Vector3(0.28f, 0.42f, 0f), new Vector3(0.07f, 0.07f, 0.07f), DarkMetal, 0.6f, 0.5f);

            // Broken chain links on wrists (tiny cubes)
            Prim(PrimitiveType.Cube, "WristChainL", root.transform,
                new Vector3(-0.13f, 0.2f, 0f), new Vector3(0.03f, 0.02f, 0.03f), Metal, 0.5f, 0.4f);
            Prim(PrimitiveType.Cube, "WristChainR", root.transform,
                new Vector3(0.13f, 0.2f, 0f), new Vector3(0.03f, 0.02f, 0.03f), Metal, 0.5f, 0.4f);

            return root;
        }

        /// <summary>Chaincaster (380): Medium figure with chains trailing from hands, heavy gauntlets. ~0.5 units tall.</summary>
        private static GameObject CreateChaincaster(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var root = new GameObject($"Chaincaster_{entity.Index}");
            root.transform.position = pos;

            // Body
            Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.15f, 0.22f, 0.1f), White);

            // Head
            Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Skin);

            // Helm with chain motif
            Prim(PrimitiveType.Cube, "Helm", root.transform,
                new Vector3(0f, 0.43f, 0f), new Vector3(0.1f, 0.04f, 0.1f), accent, 0.3f, 0.4f);

            // Legs
            Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);
            Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.06f, 0.1f, 0.06f), White);

            // Heavy gauntlets (oversized cubes on hands)
            Prim(PrimitiveType.Cube, "GauntletL", root.transform,
                new Vector3(-0.13f, 0.2f, 0.02f), new Vector3(0.06f, 0.05f, 0.06f), Metal, 0.5f, 0.5f);
            Prim(PrimitiveType.Cube, "GauntletR", root.transform,
                new Vector3(0.13f, 0.2f, 0.02f), new Vector3(0.06f, 0.05f, 0.06f), Metal, 0.5f, 0.5f);

            // Chains trailing from left hand (thin cylinders at angles)
            PrimRot(PrimitiveType.Cylinder, "ChainL1", root.transform,
                new Vector3(-0.18f, 0.15f, 0.04f), new Vector3(0.012f, 0.08f, 0.012f),
                Quaternion.Euler(10f, 0f, 30f), Metal, 0.4f, 0.3f);
            PrimRot(PrimitiveType.Cylinder, "ChainL2", root.transform,
                new Vector3(-0.22f, 0.08f, 0.06f), new Vector3(0.012f, 0.06f, 0.012f),
                Quaternion.Euler(20f, 0f, 40f), Metal, 0.4f, 0.3f);

            // Chains trailing from right hand
            PrimRot(PrimitiveType.Cylinder, "ChainR1", root.transform,
                new Vector3(0.18f, 0.15f, 0.04f), new Vector3(0.012f, 0.08f, 0.012f),
                Quaternion.Euler(10f, 0f, -30f), Metal, 0.4f, 0.3f);
            PrimRot(PrimitiveType.Cylinder, "ChainR2", root.transform,
                new Vector3(0.22f, 0.08f, 0.06f), new Vector3(0.012f, 0.06f, 0.012f),
                Quaternion.Euler(20f, 0f, -40f), Metal, 0.4f, 0.3f);

            return root;
        }

        /// <summary>Nullblade (381): Ethereal figure with void blade, semi-transparent body. ~0.5 units tall.</summary>
        private static GameObject CreateNullblade(Vector3 pos, Entity entity, int pid)
        {
            var accent = SectAccent(pid);
            var voidColor = new Color(0.08f, 0.05f, 0.12f);
            var ghostWhite = new Color(0.85f, 0.85f, 0.9f, 0.6f);
            var root = new GameObject($"Nullblade_{entity.Index}");
            root.transform.position = pos;

            // Semi-transparent body (ghostly white)
            var body = Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.13f, 0.22f, 0.08f), ghostWhite);
            MakeTransparent(body, ghostWhite);

            // Head (semi-transparent)
            var head = Prim(PrimitiveType.Sphere, "Head", root.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.1f, 0.1f, 0.1f), ghostWhite);
            MakeTransparent(head, ghostWhite);

            // Hood (solid accent color for contrast)
            Prim(PrimitiveType.Cube, "Hood", root.transform,
                new Vector3(0f, 0.42f, -0.02f), new Vector3(0.1f, 0.06f, 0.08f), accent);

            // Legs (semi-transparent)
            var legL = Prim(PrimitiveType.Cube, "LegL", root.transform,
                new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), ghostWhite);
            MakeTransparent(legL, ghostWhite);
            var legR = Prim(PrimitiveType.Cube, "LegR", root.transform,
                new Vector3(0.04f, 0.04f, 0f), new Vector3(0.05f, 0.1f, 0.05f), ghostWhite);
            MakeTransparent(legR, ghostWhite);

            // Void blade (very dark thin cube — the darkest possible)
            Prim(PrimitiveType.Cube, "VoidBlade", root.transform,
                new Vector3(0.12f, 0.3f, 0f), new Vector3(0.02f, 0.24f, 0.035f), voidColor);

            // Void blade edge glow (slightly emissive thin line)
            var edgeGlow = Prim(PrimitiveType.Cube, "BladeGlow", root.transform,
                new Vector3(0.125f, 0.3f, 0f), new Vector3(0.005f, 0.22f, 0.03f), accent);
            SetEmissiveMat(edgeGlow, accent, accent, 1.5f);

            // Blade guard
            Prim(PrimitiveType.Cube, "BladeGuard", root.transform,
                new Vector3(0.1f, 0.2f, 0f), new Vector3(0.05f, 0.015f, 0.015f), voidColor);

            return root;
        }

        /// <summary>
        /// Helper to set a primitive's material to transparent/fade mode.
        /// </summary>
        private static void MakeTransparent(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mat = r.material;
            // URP transparent setup
            mat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
            mat.SetFloat("_Blend", 0f);   // 0=Alpha
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        }
    }

    /// <summary>
    /// Attached to procedurally generated unit GameObjects to store their base scale.
    /// PresentationSpawnSystem.SyncTransforms multiplies this by ECS LocalTransform.Scale.
    /// </summary>
    public class ProceduralScaleTag : MonoBehaviour
    {
        public float BaseScale = 1f;
    }
}
