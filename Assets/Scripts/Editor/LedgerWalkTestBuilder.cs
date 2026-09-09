// LedgerWalkTestBuilder.cs
// Two jobs, both of which used to be one hand-wiring exercise:
//
//   1. Put LedgerWalker + LedgerRigBinder on the Ledger PREFAB, so any
//      instance of it — spawned in a match, or respawned by the test loop —
//      wires its own legs and walks. The leg wiring used to live in this
//      editor script, which meant only the scene it built could walk; a unit
//      spawned by PresentationSpawnSystem came up with an empty legs array.
//   2. Build the test scene: ground, light, a follow camera, and a director
//      that runs idle -> walk -> disassembly -> respawn on a loop.
//
// The scene is built ADDITIVELY and closed again, so building it never
// discards unsaved work in whatever scene is already open.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.EditorTools
{
    public static class LedgerWalkTestBuilder
    {
        private const string SoPath =
            "Assets/GameData/TechTree/Civs/Alanthor/Units/Ledger/Ledger.asset";
        private const string ScenePath =
            "Assets/GameData/Scenes/Debug/LedgerWalkTest.unity";

        [MenuItem("Waning Border/Units/Build Ledger Walk Test Scene")]
        public static void Build()
        {
            var prefab = LoadUnitPrefab();
            if (prefab == null) return;
            if (!EnsurePrefabComponents(prefab)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            BuildEnvironment(scene);
            BuildCamera(scene);

            var directorGo = new GameObject("LedgerTestDirector");
            SceneManager.MoveGameObjectToScene(directorGo, scene);
            var director = directorGo.AddComponent<LedgerTestDirector>();
            director.ledgerPrefab = prefab;
            director.lifeSeconds = 5f;      // walk for five seconds, then die
            director.idleSeconds = 1.5f;    // stand first, so idle is its own beat
            director.lingerSeconds = 1.5f;
            director.gapSeconds = 0.6f;
            director.wanderRadius = 1.5f;

            // Every ability in the game, at every level it has, cast from the
            // unit on a timer — see LedgerAbilityShowcase.
            var showcase = directorGo.AddComponent<LedgerAbilityShowcase>();
            showcase.interval = 1.6f;
            showcase.startDelay = 3f;
            director.showcase = showcase;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[LedgerTest] saved {ScenePath} — loop: idle 1.5s, walk 5s, " +
                      "disassembly, respawn");
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
        }

        private static GameObject LoadUnitPrefab()
        {
            var so = AssetDatabase.LoadAssetAtPath<Object>(SoPath);
            if (so == null) { Debug.LogError($"[LedgerTest] no SO at {SoPath}"); return null; }
            var prefab = new SerializedObject(so).FindProperty("prefab").objectReferenceValue as GameObject;
            if (prefab == null) Debug.LogError("[LedgerTest] the Ledger SO has no prefab assigned.");
            return prefab;
        }

        /// <summary>
        /// Make sure the prefab ASSET carries the walker and its binder. This is
        /// what makes a Ledger spawned by PresentationSpawnSystem walk at all.
        /// </summary>
        private static bool EnsurePrefabComponents(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[LedgerTest] the SO's prefab is not an asset on disk.");
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<LedgerWalker>() == null) root.AddComponent<LedgerWalker>();
                // Added second: it requires the walker.
                var binder = root.GetComponent<LedgerRigBinder>() ?? root.AddComponent<LedgerRigBinder>();

                // BAKE THE TOE OFFSETS HERE, where the mesh can still be read.
                // The model imports with Read/Write disabled, so at runtime
                // Mesh.vertices is empty and a live measurement finds nothing —
                // which is exactly how the first version passed in the editor
                // and then failed on every spawned instance.
                var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
                var offsets = LedgerRigBinder.MeasureToeOffsets(smr);
                if (offsets == null)
                {
                    Debug.LogError("[LedgerTest] could not measure the toe offsets — the skin " +
                                   "does not name the foot bones. Not saving a half-wired prefab.");
                    return false;
                }
                binder.toeLocalOffsets = offsets;

                int groundLayer = LayerMask.NameToLayer("Ground");
                binder.ground = groundLayer >= 0 ? (1 << groundLayer) : 1;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                for (int i = 0; i < offsets.Length; i++)
                    Debug.Log($"[LedgerTest]   baked toe {i + 1}: local " +
                              $"({offsets[i].x:0.000},{offsets[i].y:0.000},{offsets[i].z:0.000}) " +
                              $"len {offsets[i].magnitude:0.000}");
                Debug.Log($"[LedgerTest] {path}: walker + binder present, {offsets.Length} toe " +
                          $"offsets baked, ground mask {binder.ground.value}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return true;
        }

        private static void BuildEnvironment(Scene scene)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0)
            {
                groundLayer = 0;
                Debug.LogWarning("[LedgerTest] no 'Ground' layer; using Default. " +
                                 "LedgerWalker's ground mask must match or its raycasts miss.");
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.layer = groundLayer;
            SceneManager.MoveGameObjectToScene(ground, scene);

            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, 40f, 0f);
            SceneManager.MoveGameObjectToScene(lightGo, scene);
        }

        /// <summary>
        /// A FIXED camera framing the whole wander circle.
        ///
        /// It used to ride on the current Ledger, which meant that the moment
        /// one died the camera was orphaned at the death site while the next
        /// spawned back at the origin — most of a capture came back as empty
        /// ground. Nothing here needs to track: the wander radius is small
        /// enough that one shot holds the machine, the wreckage where it fell,
        /// and its replacement.
        /// </summary>
        private static Camera BuildCamera(Scene scene)
        {
            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.fieldOfView = 52f;
            camGo.AddComponent<AudioListener>();   // silences the per-frame warning
            // Pulled well back for the ability showcase. Sect powers reach tens
            // of metres, and from the walk-inspection distance a single cast
            // filled the frame and washed the machine out entirely. This gives
            // up some leg detail to make the effects readable, which is the
            // right trade once the scene is casting all 120 of them.
            camGo.transform.position = new Vector3(0f, 7.0f, -12.5f);
            camGo.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            SceneManager.MoveGameObjectToScene(camGo, scene);
            return cam;
        }
    }
}
