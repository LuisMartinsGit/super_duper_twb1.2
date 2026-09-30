// HutFloorProbe.cs
// Two menu items for the "the new Hut does not sit on the floor" question.
//
//   Wire Hut Prefab      point the Hut's BuildingDefSO at the new art. Its
//                        `prefab` field held a GUID that exists nowhere in
//                        the project, so TryGetPrefab(102) failed and the Hut
//                        drew as a procedural placeholder. A GameObject
//                        reference to a prefab VARIANT cannot be hand-written
//                        into the .asset YAML -- the root's fileID is derived,
//                        not stored -- so Unity has to author it.
//
//   Build Hut + Measure  spawn a Hut into a RUNNING match in front of the
//                        camera, then report the gap between the mesh's
//                        lowest vertex and the terrain under it. That number
//                        is the whole question: 0 means it sits on the floor.
//
// Delete this file once the answer is recorded.

using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.EditorTools
{
    public static class HutFloorProbe
    {
        private const string NewArt =
            "Assets/GameData/TechTree/Age0/Buildings/Hut/House_test_project (2).prefab";
        private const string HutSO =
            "Assets/GameData/TechTree/Age0/Buildings/Hut/Hut.asset";

        [MenuItem("Waning Border/Debug/Wire Hut Prefab")]
        public static void WireHutPrefab()
        {
            var art = AssetDatabase.LoadAssetAtPath<GameObject>(NewArt);
            if (art == null) { Debug.LogError("[HutProbe] no prefab at " + NewArt); return; }

            var so = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(HutSO);
            if (so == null) { Debug.LogError("[HutProbe] no BuildingDefSO at " + HutSO); return; }

            var sobj = new SerializedObject(so);
            var prop = sobj.FindProperty("prefab");
            var before = prop.objectReferenceValue;
            prop.objectReferenceValue = art;
            sobj.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();

            // What the model actually occupies, so the wiring can be checked
            // without entering play mode: a base at y=0 is the convention the
            // spawn path assumes (it sets y = terrain height and nothing else).
            var mf = art.GetComponentsInChildren<MeshFilter>(true);
            var b = new Bounds();
            bool any = false;
            foreach (var m in mf)
            {
                if (m.sharedMesh == null) continue;
                var wb = m.sharedMesh.bounds;
                wb.center += m.transform.position;     // prefab-local
                if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
            }
            Debug.Log($"[HutProbe][v3] Hut.prefab: {(before == null ? "WAS DANGLING" : before.name)} -> {art.name}; " +
                      $"presentationId={so.presentationId}; " +
                      (any ? $"model y {b.min.y:F3} .. {b.max.y:F3} (base should be 0)" : "no meshes"));
        }

        /// <summary>
        /// Instantiate the prefab ALONE at the origin and read its real
        /// renderer bounds. Every in-match measurement so far was polluted --
        /// by the Hall inside the sample radius, by my own bounds maths
        /// ignoring the FBX's 0.01 scale. This asks the prefab directly:
        /// min.y below zero is exactly how far the model sits under its own
        /// pivot, and therefore how far it sinks when the spawn path puts
        /// that pivot on the ground.
        /// </summary>
        /// <summary>
        /// Lift the prefab's model root by exactly the distance its lowest
        /// renderer hangs below the pivot, so the pivot IS the base -- which
        /// is what the spawn path assumes when it sets y = terrain height.
        ///
        /// The model needs it because its geometry sits 1.2 m under its own
        /// origin; the FBX vertex data reads as base-at-zero only because the
        /// offset lives in the node hierarchy, not the vertices. Unity's
        /// renderer bounds are the authority and this reads them, so the
        /// number is measured rather than typed.
        /// </summary>
        [MenuItem("Waning Border/Debug/Sit Hut Prefab On Its Base")]
        public static void SitOnBase()
        {
            var art = AssetDatabase.LoadAssetAtPath<GameObject>(NewArt);
            if (art == null) { Debug.LogError("[HutProbe] no prefab at " + NewArt); return; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(art);
            go.transform.position = Vector3.zero;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float lift = -b.min.y;

            // The model root is the prefab's single child (the FBX instance).
            var root = go.transform.childCount > 0 ? go.transform.GetChild(0) : go.transform;
            var lp = root.localPosition;
            root.localPosition = new Vector3(0f, lp.y + lift, 0f);

            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);

            var rs2 = go.GetComponentsInChildren<Renderer>(true);
            var b2 = rs2[0].bounds;
            for (int i = 1; i < rs2.Length; i++) b2.Encapsulate(rs2[i].bounds);
            Debug.Log($"[HutProbe][SIT] lifted {lift:F4} m; root local y {lp.y:F4} -> {root.localPosition.y:F4}; " +
                      $"bounds now Y {b2.min.y:F4} .. {b2.max.y:F4} (base should be 0.000)");
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// What the hut's renderers are actually drawing with. "Things behind
        /// render in front" is per-OBJECT sorting, which is what you get when
        /// a material is on the transparent queue or has ZWrite off: the depth
        /// buffer never records the near geometry, so draw order decides and
        /// draw order is roughly arbitrary. This reads queue, ZWrite and
        /// shader off the live objects rather than guessing.
        /// </summary>
        /// <summary>
        /// The hut's six materials import as URP TRANSPARENT with ZWrite off,
        /// so nothing writes depth and the sub-meshes sort per OBJECT: the
        /// pillars behind the house draw over the roof in front of them.
        ///
        /// Nothing in the FBX asked for that. It carries no Opacity, no
        /// TransparencyFactor, no TransparentColor -- its one ShadingModel is
        /// literally "Unknown" -- so this is Unity's fallback for a material
        /// it cannot interpret, not something Maya set.
        ///
        /// Embedded materials are read-only sub-assets, so they are EXTRACTED
        /// to .mat files beside the FBX and the importer remapped to them.
        /// That also survives a re-export from Maya: the remap is by name, so
        /// the opaque materials stay attached to the new mesh.
        /// </summary>
        [MenuItem("Waning Border/Debug/Make Hut Materials Opaque")]
        public static void MakeOpaque()
        {
            const string Fbx = "Assets/GameData/TechTree/Age0/Buildings/Hut/House_test_project (2).fbx";
            string dir = System.IO.Path.GetDirectoryName(Fbx) + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(Fbx), "Materials");

            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (importer == null) { Debug.LogError("[HutProbe][OPAQUE] no ModelImporter at " + Fbx); return; }

            int extracted = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var mat = o as Material;
                if (mat == null) continue;
                string dest = $"{dir}/{mat.name}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(dest) == null)
                {
                    string err = AssetDatabase.ExtractAsset(mat, dest);
                    if (!string.IsNullOrEmpty(err))
                    { Debug.LogWarning($"[HutProbe][OPAQUE] extract {mat.name}: {err}"); continue; }
                }
                extracted++;
            }
            AssetDatabase.WriteImportSettingsIfDirty(Fbx);
            AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate);

            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                // URP's Opaque surface, set the way the shader GUI sets it:
                // the float, the blend factors, the keyword and the tag all
                // have to agree or the shader keeps its transparent path.
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 1f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_AlphaClip", 0f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.DisableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                EditorUtility.SetDirty(m);
                fixedCount++;
                Debug.Log($"[HutProbe][OPAQUE]   {m.name,-26} queue={m.renderQueue} " +
                          $"ZWrite={m.GetFloat("_ZWrite")} Surface={m.GetFloat("_Surface")}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[HutProbe][OPAQUE] extracted {extracted}, made {fixedCount} opaque into {dir}");
        }

        [MenuItem("Waning Border/Debug/Probe Hut Materials")]
        public static void ProbeMaterials()
        {
            if (!EditorApplication.isPlaying)
            { Debug.LogWarning("[HutProbe] enter a running MATCH first"); return; }

            var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var hut = all.Where(r => r.transform.root.name.Contains("Hut")
                                  || r.transform.root.name.Contains("House")).ToList();
            if (hut.Count == 0)
            {
                // Fall back to whatever carries the new art's mesh names.
                string[] parts = { "Base", "Muret", "Murs", "Piliers", "Toit", "Lanterne" };
                hut = all.Where(r => parts.Contains(r.name)).ToList();
            }
            if (hut.Count == 0) { Debug.LogWarning("[HutProbe][MAT] no hut renderers found"); return; }

            Debug.Log($"[HutProbe][MAT] {hut.Count} renderer(s); root='{hut[0].transform.root.name}'");
            foreach (var r in hut)
            {
                var m = r.sharedMaterial;
                if (m == null) { Debug.Log($"[HutProbe][MAT]   {r.name,-26} NULL MATERIAL"); continue; }
                int zw = m.HasProperty("_ZWrite") ? (int)m.GetFloat("_ZWrite") : -1;
                int surf = m.HasProperty("_Surface") ? (int)m.GetFloat("_Surface") : -1;
                int cull = m.HasProperty("_Cull") ? (int)m.GetFloat("_Cull") : -1;
                Debug.Log($"[HutProbe][MAT]   {r.name,-26} mat='{m.name}' shader='{m.shader.name}' " +
                          $"queue={m.renderQueue} ZWrite={zw} Surface={surf} Cull={cull} " +
                          $"castShadows={r.shadowCastingMode} order={r.sortingOrder}");
            }
        }

        [MenuItem("Waning Border/Debug/Measure Hut Prefab Alone")]
        public static void MeasurePrefabAlone()
        {
            var art = AssetDatabase.LoadAssetAtPath<GameObject>(NewArt);
            if (art == null) { Debug.LogError("[HutProbe] no prefab at " + NewArt); return; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(art);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Debug.LogError("[HutProbe] prefab has no renderers"); Object.DestroyImmediate(go); return; }
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

            Debug.Log($"[HutProbe][ALONE] renderers={rs.Length}  " +
                      $"Y {b.min.y:F3} .. {b.max.y:F3}  (height {b.size.y:F3} m)  " +
                      $"X {b.min.x:F2}..{b.max.x:F2}  Z {b.min.z:F2}..{b.max.z:F2}  " +
                      $"=> LIFT NEEDED = {-b.min.y:F3} m");
            foreach (var r in rs)
                Debug.Log($"[HutProbe][ALONE]   {r.name,-28} y {r.bounds.min.y:F3} .. {r.bounds.max.y:F3}");
            Object.DestroyImmediate(go);
        }

        [MenuItem("Waning Border/Debug/Build Hut + Measure Floor Gap")]
        public static void BuildHutAndMeasure()
        {
            if (!EditorApplication.isPlaying)
            { Debug.LogWarning("[HutProbe] enter a running MATCH first"); return; }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            { Debug.LogError("[HutProbe] no ECS world — not a match scene"); return; }
            var em = world.EntityManager;

            var cam = TheWaningBorder.Core.PresentationState.GameplayCamera;
            if (cam == null) { Debug.LogError("[HutProbe] no gameplay camera"); return; }

            // BESIDE THE PLAYER'S HALL, not in front of the camera. The
            // camera starts looking at whatever the rig framed, and on a map
            // with hills that put the hut up a slope with the camera inside
            // the rock -- a screenshot of granite. The Hall is on the flat,
            // cleared ground every start has.
            float3 pos = float3.zero;
            var hallQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var halls = hallQ.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                foreach (var h in halls)
                {
                    if (em.GetComponentData<FactionTag>(h).Value != GameSettings.LocalPlayerFaction) continue;
                    pos = em.GetComponentData<LocalTransform>(h).Position;
                    break;
                }
            }
            hallQ.Dispose();
            if (pos.Equals(float3.zero))
            { Debug.LogError("[HutProbe] no Hall for the local faction yet"); return; }

            pos.x += 14f;                       // clear of the Hall's footprint
            pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);

            var hut = TheWaningBorder.Entities.BuildingFactory.Create(
                em, "Hut", pos, GameSettings.LocalPlayerFaction);
            if (hut == Entity.Null) { Debug.LogError("[HutProbe] factory returned nothing"); return; }

            // Finished, not a construction site: the question is about the
            // completed building's art.
            if (em.HasComponent<UnderConstruction>(hut)) em.RemoveComponent<UnderConstruction>(hut);

            // TAKE THE CAMERA OFF THE RIG FOR THE SHOT. FocusOn only moves the
            // rig's target; the camera still sits back along a tilted arm, and
            // on a map with hills that puts it INSIDE the hillside -- two
            // screenshots of granite. Disable the controller and frame the hut
            // directly: low and close, because the question is where the walls
            // meet the floor.
            var rig = Object.FindFirstObjectByType<TheWaningBorder.CameraRig.CameraController>();
            var before = cam.transform.position;
            if (rig != null) rig.enabled = false;
            // Detach from the arm, or the rig's hierarchy keeps dragging it.
            cam.transform.SetParent(null, true);
            var eye = (Vector3)pos + new Vector3(10f, 7f, 10f);
            cam.transform.position = eye;
            cam.transform.LookAt((Vector3)pos + Vector3.up * 1.2f);
            cam.nearClipPlane = 0.05f;
            Debug.Log($"[HutProbe] CAM rig={(rig == null ? "NOT FOUND" : "disabled")} " +
                      $"name={cam.name} depth={cam.depth} enabled={cam.enabled} " +
                      $"before={before} after={cam.transform.position} " +
                      $"allCameras={Camera.allCamerasCount}");
            Debug.Log($"[HutProbe] hut at ({pos.x:F1}, {pos.y:F2}, {pos.z:F1}) " +
                      $"map={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            EditorApplication.delayCall += () => Measure(pos);
        }

        private static void Measure(float3 pos)
        {
            float ground = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);
            var views = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => (r.bounds.center - (Vector3)pos).magnitude < 14f)
                .ToList();
            if (views.Count == 0) { Debug.LogWarning("[HutProbe] no renderer near the hut yet"); return; }

            var b = views[0].bounds;
            foreach (var r in views.Skip(1)) b.Encapsulate(r.bounds);
            float gap = b.min.y - ground;
            Debug.Log($"[HutProbe] RESULT  terrain y={ground:F3}  mesh base y={b.min.y:F3}  " +
                      $"GAP={gap:+0.000;-0.000;0.000} m  (0 = sitting on the floor)  " +
                      $"renderers={views.Count}  height={b.size.y:F2} m");
        }
    }
}
