// Editor-only tool inside the single runtime asmdef: the Editor/ folder
// convention does NOT apply within an .asmdef, so without this guard the
// file is compiled into PLAYER builds and fails them (UnityEditor missing).
#if UNITY_EDITOR
// ScenarioPlaceholderGenerator.cs  (editor-only)
// Creates the data-driven scenario folder structure + placeholders, and keeps
// the runtime ScenarioLibrary (in Resources) in sync. Uses AssetDatabase so all
// GUID/sprite/scene references are generated correctly.
//
//   Tools ▸ TWB ▸ Scenarios ▸ Generate Placeholder Scenarios (A, B, C)
//   Tools ▸ TWB ▸ Scenarios ▸ Rebuild Scenario Library
//
// Each placeholder produces:
//   Assets/GameData/Scenarios/<Name>/<Name>.unity   (copy of ScenarioMap)
//   Assets/GameData/Scenarios/<Name>/<Name>.jpg      (solid placeholder image, imported as Sprite)
//   Assets/GameData/Scenarios/<Name>/<Name>.asset     (ScenarioDefinition)
// and registers the scene in Build Settings.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ScenarioPlaceholderGenerator
{
    private const string ScenariosRoot = "Assets/GameData/Scenarios";
    private const string SourceScene = "Assets/GameData/Scenes/Scenarios/ScenarioMap.unity";
    private const string LibraryPath = "Assets/Resources/ScenarioLibrary.asset";

    private static readonly string[] PlaceholderNames = { "ScenarioA", "ScenarioB", "ScenarioC" };

    private static readonly Color32[] Swatches =
    {
        new Color32(58, 96, 122, 255),
        new Color32(122, 74, 58, 255),
        new Color32(74, 108, 66, 255),
        new Color32(96, 74, 116, 255),
    };

    [MenuItem("Tools/TWB/Scenarios/Generate Placeholder Scenarios (A, B, C)")]
    public static void GeneratePlaceholders()
    {
        if (!File.Exists(SourceScene))
        {
            EditorUtility.DisplayDialog("Scenario Generator",
                $"Source scene not found:\n{SourceScene}", "OK");
            return;
        }

        EnsureFolder("Assets/GameData", "Scenarios");

        for (int i = 0; i < PlaceholderNames.Length; i++)
            CreateOneScenario(PlaceholderNames[i], i);

        RebuildLibrary();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ScenarioGenerator] Generated {PlaceholderNames.Length} placeholder scenarios under {ScenariosRoot}.");
    }

    [MenuItem("Tools/TWB/Scenarios/Rebuild Scenario Library")]
    public static void RebuildLibraryMenu()
    {
        RebuildLibrary();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CreateOneScenario(string name, int index)
    {
        string folder = $"{ScenariosRoot}/{name}";
        EnsureFolder(ScenariosRoot, name);

        // 1) Scene — copy of ScenarioMap (shares the base terrain; fork later if needed).
        string scenePath = $"{folder}/{name}.unity";
        if (!File.Exists(scenePath))
            AssetDatabase.CopyAsset(SourceScene, scenePath);
        AddSceneToBuildSettings(scenePath);

        // 2) Thumbnail — solid placeholder JPG imported as a Sprite.
        string jpgPath = $"{folder}/{name}.jpg";
        if (!File.Exists(jpgPath))
        {
            var tex = new Texture2D(512, 288, TextureFormat.RGBA32, false);
            Color32 fill = Swatches[index % Swatches.Length];
            var pixels = new Color32[512 * 288];
            for (int p = 0; p < pixels.Length; p++) pixels[p] = fill;
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(jpgPath, tex.EncodeToJPG(85));
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(jpgPath);
        }
        var importer = AssetImporter.GetAtPath(jpgPath) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(jpgPath);

        // 3) Description SO.
        string soPath = $"{folder}/{name}.asset";
        var def = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(soPath);
        bool isNew = def == null;
        if (isNew) def = ScriptableObject.CreateInstance<ScenarioDefinition>();

        def.DisplayName = Prettify(name);
        if (isNew || string.IsNullOrEmpty(def.Description))
            def.Description =
                $"{Prettify(name)} — placeholder.\n\n" +
                "Replace this description and the thumbnail (" + name + ".jpg), and build " +
                "out " + name + ".unity. Fields live in " + name + ".asset.";
        def.Thumbnail = sprite;
        def.SceneName = name;

        if (isNew) AssetDatabase.CreateAsset(def, soPath);
        EditorUtility.SetDirty(def);
    }

    private static void RebuildLibrary()
    {
        EnsureFolder("Assets", "Resources");

        var lib = AssetDatabase.LoadAssetAtPath<ScenarioLibrary>(LibraryPath);
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<ScenarioLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }

        if (!AssetDatabase.IsValidFolder(ScenariosRoot))
        {
            lib.Scenarios = new List<ScenarioDefinition>();
        }
        else
        {
            lib.Scenarios = AssetDatabase
                .FindAssets("t:ScenarioDefinition", new[] { ScenariosRoot })
                .Select(g => AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null)
                .OrderBy(d => d.name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        EditorUtility.SetDirty(lib);
        Debug.Log($"[ScenarioGenerator] Library rebuilt: {lib.Scenarios.Count} scenario(s).");
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static void EnsureFolder(string parent, string child)
    {
        string full = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(full))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void AddSceneToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        int existing = scenes.FindIndex(s => s.path == path);
        if (existing >= 0)
        {
            if (!scenes[existing].enabled)
            {
                scenes[existing] = new EditorBuildSettingsScene(path, true);
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            return;
        }
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static string Prettify(string name)
    {
        // "ScenarioA" -> "Scenario A"
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

#endif // UNITY_EDITOR
