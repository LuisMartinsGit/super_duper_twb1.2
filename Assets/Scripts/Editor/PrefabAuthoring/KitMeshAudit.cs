// Every mesh the unit kits wrote: normals that are not unit length (the shader
// divides by them: one such pixel blooms into a white disc), NaN, lost weights.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace TheWaningBorder.EditorTools
{
    public static class KitMeshAudit
    {
        [MenuItem("Waning Border/Art/Units/Audit Kit Meshes")]
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var g in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Art" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(g));
                int badN = 0, nan = 0, badW = 0;
                foreach (var n in m.normals) { if (float.IsNaN(n.x + n.y + n.z)) nan++; else if (n.magnitude < 0.5f) badN++; }
                foreach (var v in m.vertices) if (float.IsNaN(v.x + v.y + v.z)) nan++;
                foreach (var w in m.boneWeights) if (Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) > 0.02f) badW++;
                if (badN + nan + badW > 0) sb.AppendLine(m.name + " zero normals " + badN + " nan " + nan + " bad weights " + badW + " of " + m.vertexCount);
            }
            File.WriteAllText("Temp/mesh-audit.txt", sb.Length == 0 ? "clean" : sb.ToString());
        }
    }
}
