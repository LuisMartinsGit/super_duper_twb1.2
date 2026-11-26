// HorseProbe.cs - what the horse pack's rig is made of: bones, renderers, clips,
// and a picture of the saddled horse along its clips (Temp/horse/).
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class HorseProbe
    {
        const string P = "Assets/LPHorse_Version_2_9/Version_2_9/";

        [MenuItem("Waning Border/Art/Units/Probe: horse pack")]
        public static void Run()
        {
            Directory.CreateDirectory("Temp/horse");
            var sb = new StringBuilder();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(P + "Prefabs/Saddled/LPHorse_Saddled_Brown_01.prefab");
            var go = Object.Instantiate(asset);
            go.transform.position = Vector3.zero;
            void Walk(Transform t, int d)
            {
                var r = t.GetComponent<Renderer>();
                string x = "";
                if (r != null)
                {
                    var m = r is SkinnedMeshRenderer s ? s.sharedMesh : t.GetComponent<MeshFilter>().sharedMesh;
                    x = " [" + r.GetType().Name + " tris " + m.triangles.Length / 3 + " sub " + m.subMeshCount + "]";
                    foreach (var mat in r.sharedMaterials) x += " {" + (mat != null ? mat.name + "|" + mat.shader.name + "|" + (mat.mainTexture != null ? AssetDatabase.GetAssetPath(mat.mainTexture) : "-") : "null") + "}";
                }
                foreach (var c in t.GetComponents<MonoBehaviour>()) x += " <" + (c != null ? c.GetType().Name : "MISSING") + ">";
                sb.AppendLine(new string(' ', d * 2) + t.name + " " + t.position.ToString("0.00") + x);
                foreach (Transform c in t) Walk(c, d + 1);
            }
            Walk(go.transform, 0);
            var an = go.GetComponentInChildren<Animator>();
            sb.AppendLine("animator " + (an != null ? an.name + " avatar " + (an.avatar != null ? an.avatar.name + " human " + an.avatar.isHuman : "none") : "none") + " bounds " + LegionaryShot.BoundsOf(go).ToString());
            foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { P + "Animations/Primary_Cycles", P + "Animations/Primary_Actions", P + "ReactiveRider/Animations" }))
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g));
                sb.AppendLine(c.name + " " + c.length.ToString("0.00") + "s loop " + c.isLooping + " human " + c.humanMotion + " rootMotion " + c.hasRootCurves + "/" + c.hasGenericRootTransform + " avg " + c.averageSpeed.ToString("0.00"));
            }
            Object.DestroyImmediate(go);
            string[] clips = { "Primary_Cycles/Rig_Idle_Center", "Primary_Cycles/Rig_Extended_Trot_RM", "Primary_Cycles/Rig_Gallop_Steady_RootMotion", "Primary_Actions/Rig_Rear_Up_Light_Left" };
            var stage = new GameObject("HorseStage");
            for (int c = 0; c < clips.Length; c++)
            {
                for (int i = stage.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(stage.transform.GetChild(i).gameObject);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(P + "Animations/" + clips[c] + ".anim");
                for (int i = 0; i < 5; i++)
                {
                    var h = Object.Instantiate(asset, stage.transform);
                    var target = h.GetComponentInChildren<Animator>().gameObject;
                    clip.SampleAnimation(target, clip.length * i / 5f);
                    UnitGearIcons.Bake(h);
                    h.transform.position = Quaternion.Euler(0f, 250f, 0f) * new Vector3(i * 3.2f, 0f, 0f);
                }
                var b = new Bounds(Quaternion.Euler(0f, 250f, 0f) * new Vector3(6.4f, 0f, 0f) + Vector3.up * 1.1f, new Vector3(6f, 3f, 16f));
                LegionaryShot.Shoot(b, 250f, 8f, 64, 64, "Temp/horse/w.png", true);
                LegionaryShot.Shoot(b, 250f, 8f, 2000, 420, "Temp/horse/clip" + c + ".png", true);
            }
            Object.DestroyImmediate(stage);
            File.WriteAllText("Temp/horse/probe.txt", sb.ToString());
        }
    }
}
