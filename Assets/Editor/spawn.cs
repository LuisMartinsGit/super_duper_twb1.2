#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor tool to generate seamlessly tileable crystal material and terrain layer assets.
/// Access via: Assets > Create > Crystal Material
/// </summary>
public static class CrystalAssetCreator
{
    // Settings - modify these before generating
    private static int resolution = 1024;
    private static int cellCount = 48;
    private static float edgeWidth = 0.06f;
    private static int seed = 12345;

    private static Color primaryColor = new Color(0.55f, 0.15f, 0.75f);
    private static Color secondaryColor = new Color(0.2f, 0.65f, 0.85f);
    private static Color edgeColor = new Color(1f, 0.95f, 1f);
    private static float edgeGlow = 0.75f;

    private static float metallic = 0.8f;
    private static float smoothness = 0.9f;
    private static float normalStrength = 1.5f;

    private static Vector2[] voronoiPoints;

    [MenuItem("Assets/Create/Crystal Material")]
    public static void CreateCrystalMaterial()
    {
        // Get save location
        string path = EditorUtility.SaveFolderPanel("Save Crystal Assets", "Assets", "CrystalMaterial");
        
        if (string.IsNullOrEmpty(path))
            return;

        // Convert to relative path
        if (path.StartsWith(Application.dataPath))
        {
            path = "Assets" + path.Substring(Application.dataPath.Length);
        }

        Generate(path, "Crystal");
    }

    [MenuItem("Assets/Create/Crystal Material (Purple)")]
    public static void CreatePurpleCrystal()
    {
        primaryColor = new Color(0.55f, 0.15f, 0.75f);
        secondaryColor = new Color(0.3f, 0.1f, 0.5f);
        edgeColor = new Color(1f, 0.8f, 1f);
        QuickGenerate("PurpleCrystal");
    }

    [MenuItem("Assets/Create/Crystal Material (Blue Ice)")]
    public static void CreateBlueCrystal()
    {
        primaryColor = new Color(0.2f, 0.5f, 0.9f);
        secondaryColor = new Color(0.1f, 0.8f, 0.95f);
        edgeColor = new Color(0.9f, 0.95f, 1f);
        QuickGenerate("BlueCrystal");
    }

    [MenuItem("Assets/Create/Crystal Material (Green Emerald)")]
    public static void CreateGreenCrystal()
    {
        primaryColor = new Color(0.1f, 0.6f, 0.3f);
        secondaryColor = new Color(0.2f, 0.8f, 0.4f);
        edgeColor = new Color(0.8f, 1f, 0.9f);
        QuickGenerate("GreenCrystal");
    }

    [MenuItem("Assets/Create/Crystal Material (Red Ruby)")]
    public static void CreateRedCrystal()
    {
        primaryColor = new Color(0.7f, 0.1f, 0.15f);
        secondaryColor = new Color(0.9f, 0.2f, 0.3f);
        edgeColor = new Color(1f, 0.85f, 0.9f);
        QuickGenerate("RedCrystal");
    }

    private static void QuickGenerate(string name)
    {
        string path = "Assets/CrystalMaterials";
        
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder("Assets", "CrystalMaterials");
        }

        Generate(path, name);
    }

    private static void Generate(string folderPath, string assetName)
    {
        EditorUtility.DisplayProgressBar("Generating Crystal", "Initializing...", 0f);

        try
        {
            // Initialize voronoi with tileable points
            Random.InitState(seed);
            voronoiPoints = new Vector2[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                voronoiPoints[i] = new Vector2(Random.value, Random.value);
            }

            // Generate textures
            EditorUtility.DisplayProgressBar("Generating Crystal", "Creating albedo...", 0.2f);
            Texture2D albedo = GenerateAlbedo();

            EditorUtility.DisplayProgressBar("Generating Crystal", "Creating normal map...", 0.4f);
            Texture2D normal = GenerateNormal();

            EditorUtility.DisplayProgressBar("Generating Crystal", "Creating mask map...", 0.6f);
            Texture2D mask = GenerateMask();

            EditorUtility.DisplayProgressBar("Generating Crystal", "Creating emission map...", 0.65f);
            Texture2D emission = GenerateEmission();

            // Save textures
            EditorUtility.DisplayProgressBar("Generating Crystal", "Saving textures...", 0.7f);
            
            string albedoPath = $"{folderPath}/{assetName}_Albedo.png";
            string normalPath = $"{folderPath}/{assetName}_Normal.png";
            string maskPath = $"{folderPath}/{assetName}_Mask.png";
            string emissionPath = $"{folderPath}/{assetName}_Emission.png";

            System.IO.File.WriteAllBytes(albedoPath, albedo.EncodeToPNG());
            System.IO.File.WriteAllBytes(normalPath, normal.EncodeToPNG());
            System.IO.File.WriteAllBytes(maskPath, mask.EncodeToPNG());
            System.IO.File.WriteAllBytes(emissionPath, emission.EncodeToPNG());

            AssetDatabase.Refresh();

            // Configure imports
            ConfigureTexture(albedoPath, false);
            ConfigureTexture(normalPath, true);
            ConfigureTexture(maskPath, false);
            ConfigureTexture(emissionPath, false);

            // Create material
            EditorUtility.DisplayProgressBar("Generating Crystal", "Creating material...", 0.85f);
            
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material mat = new Material(shader);
            
            Texture2D albedoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            Texture2D maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            Texture2D emissionTex = AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath);

            // Standard shader
            mat.SetTexture("_MainTex", albedoTex);
            mat.SetTexture("_BumpMap", normalTex);
            mat.SetFloat("_BumpScale", normalStrength);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Glossiness", smoothness);
            mat.SetFloat("_Smoothness", smoothness);
            mat.EnableKeyword("_NORMALMAP");

            // Emission
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", emissionTex);
            mat.SetColor("_EmissionColor", Color.white);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // URP shader
            mat.SetTexture("_BaseMap", albedoTex);
            mat.SetTexture("_MetallicGlossMap", maskTex);
            mat.SetTexture("_EmissionMap", emissionTex);

            string matPath = $"{folderPath}/{assetName}_Material.mat";
            AssetDatabase.CreateAsset(mat, matPath);

            // Create terrain layer
            EditorUtility.DisplayProgressBar("Generating Crystal", "Creating terrain layer...", 0.95f);
            
            TerrainLayer layer = new TerrainLayer();
            layer.diffuseTexture = albedoTex;
            layer.normalMapTexture = normalTex;
            layer.maskMapTexture = maskTex;
            layer.normalScale = normalStrength;
            layer.metallic = metallic;
            layer.smoothness = smoothness;
            layer.tileSize = new Vector2(10f, 10f);

            string layerPath = $"{folderPath}/{assetName}_TerrainLayer.terrainlayer";
            AssetDatabase.CreateAsset(layer, layerPath);

            // Log texture paths for use in other scripts
            Debug.Log($"Crystal textures created:\n" +
                $"  Albedo: {albedoPath}\n" +
                $"  Normal: {normalPath}\n" +
                $"  Mask: {maskPath}\n" +
                $"  Emission: {emissionPath}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Select the material
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            Debug.Log($"Crystal assets created at: {folderPath}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void ConfigureTexture(string path, bool isNormal)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal;
            importer.maxTextureSize = resolution;
            importer.anisoLevel = 8;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }
    }

    private static Texture2D GenerateAlbedo()
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = (float)x / resolution;
                float v = (float)y / resolution;

                GetTileableVoronoi(u, v, out int cell, out float edge);

                float t = (cell * 0.618034f) % 1f;
                Color baseCol = Color.Lerp(primaryColor, secondaryColor, t);

                float edgeFactor = 1f - Mathf.Clamp01(edge / edgeWidth);
                edgeFactor *= edgeFactor;
                Color final = Color.Lerp(baseCol, edgeColor, edgeFactor * edgeGlow);

                // Tileable noise
                float noise = TileableNoise(u, v, 4f) * 0.12f;
                final *= (1f + noise);

                final.r = Mathf.Clamp01(final.r);
                final.g = Mathf.Clamp01(final.g);
                final.b = Mathf.Clamp01(final.b);
                final.a = 1f;

                tex.SetPixel(x, y, final);
            }
        }

        tex.Apply(true);
        return tex;
    }

    private static Texture2D GenerateNormal()
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true);
        float[,] heights = new float[resolution, resolution];

        // Build height map
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = (float)x / resolution;
                float v = (float)y / resolution;

                GetTileableVoronoi(u, v, out int cell, out float edge);

                float cellHeight = (cell * 0.618034f) % 1f;
                float e = Mathf.Clamp01(edge / edgeWidth);
                e = e * e * (3f - 2f * e);

                heights[x, y] = cellHeight * e;
                
                // Tileable noise for jagged detail
                heights[x, y] += TileableNoise(u, v, 8f) * 0.2f;
                heights[x, y] += TileableNoise(u, v, 16f) * 0.1f;
            }
        }

        // Convert to normals with wrapping for seamless edges
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Wrap around for seamless tiling
                int xL = (x - 1 + resolution) % resolution;
                int xR = (x + 1) % resolution;
                int yD = (y - 1 + resolution) % resolution;
                int yU = (y + 1) % resolution;

                float dx = (heights[xR, y] - heights[xL, y]) * normalStrength;
                float dy = (heights[x, yU] - heights[x, yD]) * normalStrength;

                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;

                tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
            }
        }

        tex.Apply(true);
        return tex;
    }

    private static Texture2D GenerateMask()
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = (float)x / resolution;
                float v = (float)y / resolution;

                GetTileableVoronoi(u, v, out int cell, out float edge);

                float edgeFactor = 1f - Mathf.Clamp01(edge / edgeWidth);

                float m = Mathf.Lerp(metallic, metallic * 0.6f, edgeFactor);
                float s = Mathf.Lerp(smoothness, smoothness * 0.4f, edgeFactor);
                float ao = 1f - edgeFactor * 0.25f;

                tex.SetPixel(x, y, new Color(m, ao, 0f, s));
            }
        }

        tex.Apply(true);
        return tex;
    }

    private static Texture2D GenerateEmission()
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = (float)x / resolution;
                float v = (float)y / resolution;

                GetTileableVoronoi(u, v, out int cell, out float edge);

                // Emission strongest at edges (crystal facet boundaries glow)
                float edgeFactor = 1f - Mathf.Clamp01(edge / edgeWidth);
                edgeFactor = edgeFactor * edgeFactor * edgeFactor; // Sharp falloff

                // Also some glow in facet centers
                float centerGlow = Mathf.Clamp01(edge / edgeWidth) * 0.3f;

                float emissionStrength = Mathf.Max(edgeFactor * edgeGlow, centerGlow);

                // Color varies by cell
                float t = (cell * 0.618034f) % 1f;
                Color emitColor = Color.Lerp(primaryColor, secondaryColor, t);
                emitColor = Color.Lerp(emitColor, edgeColor, edgeFactor);

                Color final = emitColor * emissionStrength;
                final.a = 1f;

                tex.SetPixel(x, y, final);
            }
        }

        tex.Apply(true);
        return tex;
    }

    /// <summary>
    /// Tileable Voronoi - checks distance across wrapped edges
    /// </summary>
    private static void GetTileableVoronoi(float x, float y, out int closestCell, out float edgeDist)
    {
        float min1 = float.MaxValue;
        float min2 = float.MaxValue;
        closestCell = 0;

        Vector2 p = new Vector2(x, y);

        for (int i = 0; i < voronoiPoints.Length; i++)
        {
            // Check all 9 tile offsets for seamless wrapping
            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oy = -1; oy <= 1; oy++)
                {
                    Vector2 wrappedPoint = voronoiPoints[i] + new Vector2(ox, oy);
                    float d = Vector2.Distance(p, wrappedPoint);
                    
                    if (d < min1)
                    {
                        min2 = min1;
                        min1 = d;
                        closestCell = i;
                    }
                    else if (d < min2)
                    {
                        min2 = d;
                    }
                }
            }
        }

        edgeDist = min2 - min1;
    }

    /// <summary>
    /// Tileable Perlin noise using 4-corner blending
    /// </summary>
    private static float TileableNoise(float x, float y, float scale)
    {
        float sx = x * scale;
        float sy = y * scale;

        // Sample noise at 4 corners and blend for seamless tiling
        float n1 = Mathf.PerlinNoise(sx, sy);
        float n2 = Mathf.PerlinNoise(sx - scale, sy);
        float n3 = Mathf.PerlinNoise(sx, sy - scale);
        float n4 = Mathf.PerlinNoise(sx - scale, sy - scale);

        // Bilinear blend based on position
        float xBlend = x;
        float yBlend = y;

        float top = Mathf.Lerp(n1, n2, xBlend);
        float bottom = Mathf.Lerp(n3, n4, xBlend);

        return Mathf.Lerp(top, bottom, yBlend);
    }
}
#endif