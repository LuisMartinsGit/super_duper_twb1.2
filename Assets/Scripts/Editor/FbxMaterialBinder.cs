// FbxMaterialBinder.cs
// Bind a model's material slots to the .mat assets of the same name in the
// Materials/ folder beside it.
//
// An FBX from Maya or Blender carries geometry, UVs and material NAMES — the
// textures live in Unity .mat assets, bound by name through the model
// importer's remap. A re-export resets the .fbx.meta and with it the remap,
// so the model comes back grey; select the FBX and run this to re-bind it.
// Worked example: Age0/Buildings/Hut/House_test_project.fbx.
// (The wall set has its own, wider binder: Waning Border > Walls > Bind Wall Art.)

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class FbxMaterialBinder
    {
        [MenuItem("Waning Border/Art/Bind FBX Materials From Materials Folder")]
        public static void BindSelected()
        {
            int bound = 0;
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                    bound += Bind(path, importer);
            }
            Debug.Log($"[FbxMaterialBinder] bound {bound} material slot(s).");
        }

        [MenuItem("Waning Border/Art/Bind FBX Materials From Materials Folder", true)]
        static bool BindSelectedValidate()
        {
            foreach (var obj in Selection.objects)
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(obj)) is ModelImporter)
                    return true;
            return false;
        }

        static int Bind(string fbxPath, ModelImporter importer)
        {
            string folder = Path.GetDirectoryName(fbxPath).Replace('\\', '/') + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[FbxMaterialBinder] {fbxPath}: no {folder} folder.");
                return 0;
            }

            // The slot names: what the model imports now, plus any already remapped.
            var wanted = new HashSet<string>();
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
                if (o is Material m) wanted.Add(m.name);
            foreach (var kv in importer.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material)) wanted.Add(kv.Key.name);

            int n = 0;
            var existing = importer.GetExternalObjectMap();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || !wanted.Contains(mat.name)) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name);
                if (!(existing.TryGetValue(id, out var cur) && cur == mat))
                    importer.AddRemap(id, mat);
                wanted.Remove(mat.name);
                n++;
            }
            foreach (var missing in wanted)
                Debug.LogWarning($"[FbxMaterialBinder] {fbxPath}: slot '{missing}' has no {missing}.mat in {folder}.");

            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
            return n;
        }
    }
}
