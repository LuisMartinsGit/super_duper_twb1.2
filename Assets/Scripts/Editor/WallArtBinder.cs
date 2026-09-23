// WallArtBinder.cs
// Binds each wall piece's authored art to ITS OWN ScriptableObject — the one
// step that cannot be done without the Editor open, because a prefab's
// internal object ids only exist after Unity has imported the model.
//
// Every wall piece has its own BuildingDefSO, because every piece has its own
// stats and its own art:
//
//   Hub/WallHub.asset           Alanthor_Wall          pid 550  the hub
//   Segment/WallSegment.asset   Alanthor_WallSegment   pid 552  the curtain module
//   Tower/WallTower.asset       Alanthor_WallTower     pid 553  the wall tower
//   Gate/WallGate.asset         Alanthor_WallGate      pid 554  the gatehouse
//
// Run "Waning Border > Walls > Bind Wall Art". It finds every .fbx under the
// wall folder, works out which piece each one is from WHICH FOLDER IT IS IN
// (falling back to the file name), and for each: sets the importer flags the
// runtime needs, remaps the model's materials onto the .mat assets beside it,
// builds a prefab, and writes that prefab and the piece's presentation id
// into the right SO.
//
// A piece with no FBX keeps its procedural visual — the wall never half-draws.
// Re-run after EVERY Blender re-export: exporting resets the whole .fbx.meta.
//
// Pipeline: docs/Art_Pipeline_FBX.md.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;
using TheWaningBorder.Entities;

namespace TheWaningBorder.EditorTools
{
    public static class WallArtBinder
    {
        const string Folder = "Assets/GameData/TechTree/Age0/Buildings/Wall";

        /// <summary>One wall piece: the SO that owns it, its presentation id,
        /// and the words in a file name that mean "this is that piece".</summary>
        private readonly struct Piece
        {
            public readonly string Role, SoPath;
            public readonly int Pid;
            public readonly string[] Keywords;
            public Piece(string role, string soPath, int pid, params string[] keywords)
            { Role = role; SoPath = soPath; Pid = pid; Keywords = keywords; }
        }

        // Order matters: the first match wins, so the narrow words come first.
        // "Wall_segment.fbx" contains both "wall" and "segment", and segment
        // has to be tested before the catch-all "wall".
        static readonly Piece[] Pieces =
        {
            new Piece("Hub", Folder + "/Hub/WallHub.asset",
                      AlanthorWall.HubPresentationID, "hub"),
            new Piece("Gate", Folder + "/Gate/WallGate.asset",
                      AlanthorWall.GatePresentationID, "gate"),
            new Piece("Tower", Folder + "/Tower/WallTower.asset",
                      AlanthorWall.TowerPresentationID, "tower"),
            new Piece("Segment", Folder + "/Segment/WallSegment.asset",
                      AlanthorWall.InstancePresentationID, "segment", "curtain", "wall"),
        };

        [MenuItem("Waning Border/Walls/Bind Wall Art")]
        public static void Bind()
        {
            var models = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    models.Add(path);
            }
            if (models.Count == 0)
            {
                Debug.LogWarning($"[WallArtBinder] no .fbx under {Folder} — " +
                                 "every wall piece stays procedural.");
                return;
            }

            int bound = 0;
            var claimed = new HashSet<string>();
            foreach (string path in models)
            {
                if (!TryClassify(path, claimed, out var piece))
                {
                    Debug.LogWarning($"[WallArtBinder] '{path}' does not say which wall piece it " +
                                     "is — put it in Hub/ Segment/ Gate/ Tower/, or name it " +
                                     "*_hub / *_segment / *_gate / *_tower. (A second model for a " +
                                     "piece that is already bound also lands here.)");
                    continue;
                }
                if (BindOne(path, piece)) { claimed.Add(piece.Role); bound++; }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[WallArtBinder] bound {bound} of {models.Count} model(s). " +
                      "Pieces with no art keep their procedural visual.");
        }

        /// <summary>
        /// Which piece a model is. The FOLDER decides first — a model in
        /// `Hub/` is the hub whatever the .blend was called, and art keeps its
        /// export name often enough that the file name alone mis-assigns
        /// (`Hub/Wall_segment.fbx` reads as a segment). The file name is the
        /// fallback for a model sitting loose in the wall folder.
        ///
        /// A role already taken is not taken twice, so two models that both
        /// read as the same piece cannot silently fight over the slot — the
        /// second is reported instead.
        /// </summary>
        static bool TryClassify(string path, HashSet<string> claimed, out Piece piece)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "").ToLowerInvariant();
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            foreach (var p in Pieces)
            {
                if (claimed.Contains(p.Role)) continue;
                if (folder == p.Role.ToLowerInvariant()) { piece = p; return true; }
            }
            foreach (var p in Pieces)
            {
                if (claimed.Contains(p.Role)) continue;
                foreach (string k in p.Keywords)
                {
                    if (!name.Contains(k)) continue;
                    piece = p;
                    return true;
                }
            }
            piece = default;
            return false;
        }

        static bool BindOne(string fbxPath, in Piece piece)
        {
            var def = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(piece.SoPath);
            if (def == null)
            {
                Debug.LogError($"[WallArtBinder] no BuildingDefSO at {piece.SoPath}");
                return false;
            }
            if (!ConfigureImporter(fbxPath)) return false;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null)
            {
                Debug.LogError($"[WallArtBinder] could not instantiate {fbxPath}");
                return false;
            }

            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.name = piece.Role;
                Prune(instance);
                MarkOwnershipParts(instance);

                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogError($"[WallArtBinder] {Path.GetFileName(fbxPath)} has nothing to " +
                                   "render after pruning.");
                    return false;
                }
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                // The model's OWN prefab path, overwritten in place: one
                // prefab per model, its guid (and every reference to it)
                // preserved, and the pruning + ownership marking re-applied.
                // A second "_Hub.prefab" beside a hand-made one would just be
                // two prefabs nobody could tell apart.
                string prefabPath = Path.ChangeExtension(fbxPath, null) + ".prefab";
                prefabPath = prefabPath.Replace('\\', '/');
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool ok);
                if (!ok || saved == null)
                {
                    Debug.LogError($"[WallArtBinder] could not write {prefabPath}");
                    return false;
                }

                var so = new SerializedObject(def);
                so.FindProperty("prefab").objectReferenceValue = saved;
                so.FindProperty("presentationId").intValue = piece.Pid;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);

                float fit = FitLengthFor(piece);
                float along = Mathf.Max(bounds.size.x, bounds.size.z);
                Debug.Log($"[WallArtBinder] {piece.Role}: {Path.GetFileName(fbxPath)} -> " +
                          $"{Path.GetFileName(prefabPath)} -> {Path.GetFileName(piece.SoPath)} " +
                          $"(pid {piece.Pid}). Measures " +
                          $"{bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00} m; " +
                          $"fitted to {fit:0.0} m (x{fit / Mathf.Max(0.001f, along):0.00}).");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>What the runtime scales this piece's longest horizontal
        /// axis to — the same numbers WallModuleArt fits against, reported at
        /// bind time so a wrong export scale is visible immediately.</summary>
        static float FitLengthFor(in Piece piece)
        {
            if (piece.Pid == AlanthorWall.HubPresentationID) return AlanthorWall.HubWidth;
            if (piece.Pid == AlanthorWall.GatePresentationID) return AlanthorWall.GateSpanMetres;
            return AlanthorWall.InstanceSpacing;
        }

        /// <summary>Read/Write for the mesh combine, no scene junk, and the
        /// model's materials bound to the .mat assets beside it.</summary>
        static bool ConfigureImporter(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[WallArtBinder] {fbxPath} is not a model");
                return false;
            }
            bool changed = false;
            if (!importer.isReadable) { importer.isReadable = true; changed = true; }
            if (importer.importCameras) { importer.importCameras = false; changed = true; }
            if (importer.importLights) { importer.importLights = false; changed = true; }
            if (importer.animationType != ModelImporterAnimationType.None)
            { importer.animationType = ModelImporterAnimationType.None; changed = true; }
            // Material NAMING and extraction are left alone: the artist's
            // already-extracted materials carry the baked textures, and
            // forcing a naming mode would unbind them.
            changed |= RemapMaterials(importer, fbxPath);
            if (changed) importer.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// Point each of the model's materials at the .mat asset of the same
        /// name in the model's folder (or the wall folder above it).
        ///
        /// THE FBX DOES NOT CARRY TEXTURES, and does not need to. Blender
        /// writes a texture link into an FBX only from a very particular node
        /// setup, and a re-export wipes the importer settings every time.
        /// Remapping by name instead means the Unity materials — with the
        /// baked maps on them — are the durable thing, and re-exporting the
        /// mesh cannot disturb them. docs/Art_Pipeline_FBX.md.
        /// </summary>
        static bool RemapMaterials(ModelImporter importer, string fbxPath)
        {
            // What the MODEL actually calls its materials. Remapping a name
            // the model does not have just leaves a dangling entry, so the
            // model's own list is the gate.
            var wanted = new HashSet<string>();
            foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
                if (obj is Material m) wanted.Add(m.name);
            foreach (var kv in importer.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material)) wanted.Add(kv.Key.name);
            if (wanted.Count == 0) return false;

            bool changed = false;
            var existing = importer.GetExternalObjectMap();
            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            var search = dir == Folder ? new[] { Folder } : new[] { dir, Folder };

            foreach (var guid in AssetDatabase.FindAssets("t:Material", search))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || !wanted.Contains(mat.name)) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name);
                if (existing.TryGetValue(id, out var bound) && bound == mat) continue;
                importer.AddRemap(id, mat);
                changed = true;
                Debug.Log($"[WallArtBinder]   material '{mat.name}' -> {path}");
            }
            return changed;
        }

        /// <summary>
        /// Rename the parts that carry the owner's colour to `Stripe_*`.
        ///
        /// The blue posts on the module are the ownership marker, and the
        /// project already has ONE rule for that: BuildingFactionColorMarker
        /// tints any part whose name says "stripe". Renaming them here means
        /// the art needs no convention of its own AND every path that
        /// instantiates the model — not just the baked wall — picks the colour
        /// up for free. The baked path finds the same parts by colour through
        /// WallModuleArt.IsOwnershipPart, so the two agree.
        /// </summary>
        static void MarkOwnershipParts(GameObject root)
        {
            int n = 0;
            foreach (var rend in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (rend == null) continue;
                var mat = rend.sharedMaterial;
                var color = TheWaningBorder.Rendering.WallModuleArt.BaseColorOf(mat);
                if (!TheWaningBorder.Rendering.WallModuleArt.IsOwnershipPart(
                        rend.name, mat != null ? mat.name : null, color)) continue;
                rend.gameObject.name = $"Stripe_Ownership_{n++}";
            }
            if (n > 0)
                Debug.Log($"[WallArtBinder]   {n} part(s) take the player colour " +
                          "(renamed Stripe_Ownership_*).");
        }

        /// <summary>
        /// Drop what a Blender export brings along: the default camera and
        /// lamp, and the ground plane. The plane is found the same way the
        /// runtime finds it — a node whose footprint dwarfs the median — so
        /// the two agree even if this step is skipped.
        /// </summary>
        static void Prune(GameObject root)
        {
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                if (cam != null) Object.DestroyImmediate(cam.gameObject);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                if (light != null) Object.DestroyImmediate(light.gameObject);

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length < 2) return;
            var spans = new List<float>();
            foreach (var f in filters)
            {
                if (f.sharedMesh == null) continue;
                var s = f.sharedMesh.bounds.size;
                spans.Add(Mathf.Max(s.x, s.z));
            }
            if (spans.Count < 2) return;
            spans.Sort();
            float cull = Mathf.Max(spans[spans.Count / 2] * 6f, 0.01f);
            foreach (var f in filters)
            {
                if (f == null || f.sharedMesh == null) continue;
                var s = f.sharedMesh.bounds.size;
                if (Mathf.Max(s.x, s.z) > cull)
                {
                    Debug.Log($"[WallArtBinder]   dropping oversized node '{f.name}' " +
                              $"({s.x:0.0} x {s.z:0.0} m) — reads as the export's ground plane.");
                    Object.DestroyImmediate(f.gameObject);
                }
            }
        }
    }
}
