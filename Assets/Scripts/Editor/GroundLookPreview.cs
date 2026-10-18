// GroundLookPreview.cs
// Waning Border > Art > Ground Look Preview (2026-10-16).
//
// Opens a small scene for LOOKING AT and tweaking the two ground looks the
// terrain draws: blood (Resources/Blood.mat) and cursed ground
// (Resources/Curse.mat). Two big flat panels, one per material, each running
// from no coverage at its left edge to full coverage at its right — the look
// shaders draw the terrain's own blood / curse code over a plain ground, so
// what the panels show is what a match shows. Edit the material (it is
// selected for you) and the panels update immediately, no Play needed.
//
// The scene is created the first time and saved, then simply reopened.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.EditorTools
{
    public static class GroundLookPreview
    {
        const string ScenePath = "Assets/GameData/Art/GroundLooks/GroundLookPreview.unity";
        const string BloodPath = "Assets/Resources/Blood.mat";
        const string CursePath = "Assets/Resources/Curse.mat";
        const float PanelSize = 14f;
        const float Gap = 1f;

        [MenuItem("Waning Border/Art/Ground Look Preview (Blood & Curse)")]
        public static void Open()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[GroundLookPreview] Stop Play mode first — or edit Blood.mat / Curse.mat " +
                                 "during Play and watch the terrain update live.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Rebuilt every time: the scene is generated, so its staging can
            // change without anyone having to delete a stale copy.
            Build();

            var blood = AssetDatabase.LoadAssetAtPath<Material>(BloodPath);
            if (blood != null) Selection.activeObject = blood;
        }

        /// <summary>One 60 m cursed panel seen from RTS height (2026-10-17):
        /// tiling REPETITION only shows across many tiles, which the 14 m
        /// panels above cannot hold. Used to check the hex-tiled crystal.</summary>
        [MenuItem("Waning Border/Art/Ground Look Preview - Curse Field (60 m)")]
        public static void OpenCurseField()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[GroundLookPreview] Stop Play mode first."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Staging(out var cam);
            cam.transform.position = new Vector3(0f, 34f, -36f);
            cam.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            Panel("Curse", CursePath, new Vector3(0f, 0f, 4f), 60f);
            var curse = AssetDatabase.LoadAssetAtPath<Material>(CursePath);
            if (curse != null) Selection.activeObject = curse;
        }

        static void Staging(out Camera cam)
        {
            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.9f, 0.78f);
            lightGo.transform.rotation = Quaternion.Euler(22f, 200f, 0f);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
            var camGo = new GameObject("Preview Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 45f;
        }

        static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            // A LOW, warm sun from the far side: grazing back-light is what makes
            // relief and wet highlights readable (the match's dusk light is
            // low too).
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.9f, 0.78f);
            lightGo.transform.rotation = Quaternion.Euler(22f, 200f, 0f);   // BACK-lit: wet and crystal faces reflect toward the camera

            // A sky to REFLECT: an empty scene has none, so glossy blood and
            // crystal faces had nothing to mirror.
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();

            var camGo = new GameObject("Preview Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 45f;
            // Close and low: an RTS camera's angle, near enough to judge the
            // relief and the wet / dry texture.
            camGo.transform.position = new Vector3(0f, 11f, -15f);
            camGo.transform.rotation = Quaternion.Euler(38f, 0f, 0f);

            Panel("Blood", BloodPath, new Vector3(-(PanelSize + Gap) * 0.5f, 0f, 0f));
            Panel("Curse", CursePath, new Vector3((PanelSize + Gap) * 0.5f, 0f, 0f));

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[GroundLookPreview] created " + ScenePath);
        }

        static void Panel(string name, string materialPath, Vector3 centre) => Panel(name, materialPath, centre, PanelSize);

        static void Panel(string name, string materialPath, Vector3 centre, float size)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name + " (none → full coverage, left to right)";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.position = centre;
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // flat; u runs along +X
            quad.transform.localScale = new Vector3(size, size, 1f);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat != null) quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            else Debug.LogError("[GroundLookPreview] missing " + materialPath);

            var label = new GameObject(name + " label");
            var text = label.AddComponent<TextMesh>();
            text.text = name.ToUpperInvariant() + "    none → full";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.characterSize = 0.18f;
            text.fontSize = 48;
            text.anchor = TextAnchor.LowerCenter;
            text.color = Color.white;
            label.transform.position = centre + new Vector3(0f, 0.05f, size * 0.5f + 0.5f);
            label.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
        }
    }
}
