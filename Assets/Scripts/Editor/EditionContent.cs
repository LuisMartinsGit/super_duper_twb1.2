// EditionContent.cs
// Strips the cultures an edition does not ship out of a player build, for the
// length of that one build. docs/Design/Alpha_Build.md § Editions.
//
//   A (demo)  strips Feraldis and Runai     B  strips Runai     C  strips nothing
//
// WHAT IS STRIPPED: every unit / building / tech / level def of the culture
// from Resources/TechTreeCatalog.asset (so its SOs, prefabs, models, materials
// and animator controllers are no longer referenced and Unity leaves them out),
// and the culture's model branch inside shared building prefabs (Barracks,
// Fortress, Hut, … carry an "Alanthor" / "Feraldis" / "Runai" child that
// BuildingVariantVisual switches between — the Feraldis branch of the Age 0
// Barracks would otherwise ship its art in the demo).
//
// WHAT IS NOT: the C# (Civs/Feraldis code compiles into the same assembly as
// the factories that dispatch to it; it ships inert, with no data to act on),
// and the SECTS — every culture can adopt every sect, so the twelve sect
// folders and the chapel prefabs are base-game content in every edition.
//
// HOW: the touched assets are copied to Library/EditionStripBackup before they
// are edited, and copied back when the scope is disposed. If the Editor dies
// mid-build, the backup survives (Library/ persists, Temp/ does not) and
// RestoreLeftovers puts the project back on the next domain reload — the
// strip must never be able to leave the repo with Feraldis deleted from it.
//
// Verified after the build by PlayerBuild (EditionContent.FindLeaks over the
// BuildReport): any packed asset from a stripped culture fails the build.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheWaningBorder.Data;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class EditionContent
    {
        private const string CatalogPath = "Assets/Resources/TechTreeCatalog.asset";
        private const string TechTreeRoot = "Assets/GameData/TechTree/";

        private static string BackupDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "EditionStripBackup"));
        private static string ManifestPath => Path.Combine(BackupDir, "manifest.txt");

        /// <summary>Culture folder names an edition strips.</summary>
        public static string[] StrippedCultures(string edition) => edition switch
        {
            "A" => new[] { "Feraldis", "Runai" },
            "B" => new[] { "Runai" },
            _   => Array.Empty<string>(),
        };

        /// <summary>
        /// Folders a culture owns outside Civs/&lt;Culture&gt;/. The Fiendstone
        /// Keep is filed under Age0/ (it is BUILT in Age 0) but is the Feraldis
        /// landmark: it ages a faction into Feraldis and nothing else.
        /// </summary>
        private static IEnumerable<string> CultureFolders(string culture)
        {
            yield return TechTreeRoot + "Civs/" + culture + "/";
            if (culture == "Feraldis") yield return TechTreeRoot + "Age0/Buildings/FiendstoneKeep/";
            // Thessara's Crossing likewise is the Runai landmark (Age_1_Runai.md §2).
            if (culture == "Runai") yield return TechTreeRoot + "Age0/Buildings/ThessarasCrossing/";
        }

        /// <summary>Model-branch names BuildingVariantVisual reads as this culture.</summary>
        private static string[] BranchNames(string culture) =>
            culture == "Runai" ? new[] { "Runai", "Runaii" } : new[] { culture };

        // ── the scope ─────────────────────────────────────────────────────

        public sealed class Scope : IDisposable
        {
            public string Error { get; internal set; }
            internal bool Active;

            public void Dispose()
            {
                if (!Active) return;
                Active = false;
                RestoreBackups("build finished");
            }
        }

        /// <summary>
        /// Strip for one build. Never throws: a failure is reported as
        /// <see cref="Scope.Error"/>, with the project already restored.
        /// </summary>
        public static Scope Strip(string edition)
        {
            var scope = new Scope();
            var cultures = StrippedCultures(edition);
            if (cultures.Length == 0) return scope;   // C ships everything; nothing to touch

            // A leftover backup means a previous strip never restored. Restore
            // it first, or this run would back up the STRIPPED files as the
            // originals and lose the real ones for good.
            RestoreLeftovers();

            try
            {
                Directory.CreateDirectory(BackupDir);
                scope.Active = true;

                var stripped = StripCatalog(cultures);
                int branches = StripPrefabBranches(cultures);
                int configs = StripListedAssets<TheWaningBorder.Core.Settings.ComponentConfigCatalog>(
                    ConfigCatalogPath, c => c.configs, cultures);
                // Ability cards are identified on the wire by their INDEX, so
                // only a culture's TRAILING block may go (the Feraldis cards are
                // appended last). A stripped card mid-list would renumber every
                // card after it — refused loudly instead.
                int cards = StripTrailingAbilities(cultures);
                if (configs > 0 || cards > 0)
                    Debug.Log($"[EditionContent] Also stripped {configs} component config(s) and {cards} ability card(s).");

                AssetDatabase.SaveAssets();
                Debug.Log($"[EditionContent] Edition {edition}: stripped {stripped.Count} def(s) of " +
                          $"{string.Join(" + ", cultures)} from the catalog and {branches} model " +
                          "branch(es) from shared building prefabs.");
            }
            catch (Exception e)
            {
                scope.Dispose();
                scope.Error = $"Edition {edition} strip failed, project restored: {e}";
            }
            return scope;
        }

        // ── the catalog ───────────────────────────────────────────────────

        private static List<string> StripCatalog(string[] cultures)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TechTreeCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("No TechTreeCatalog at " + CatalogPath);

            Backup(CatalogPath);

            var stripped = new List<string>();
            bool Drop(ScriptableObject so, string id, string techCulture = null)
            {
                if (so == null) return false;
                if (!Owned(AssetDatabase.GetAssetPath(so), id, techCulture, cultures)) return false;
                if (!string.IsNullOrEmpty(id)) stripped.Add(id);
                return true;
            }

            catalog.units.RemoveAll(u => Drop(u, u != null ? u.id : null));
            catalog.buildings.RemoveAll(b => Drop(b, b != null ? b.id : null));
            catalog.technologies.RemoveAll(t => Drop(t, t != null ? t.id : null, t != null ? t.culture : null));
            catalog.buildingLevels.RemoveAll(l => Drop(l, null));

            catalog.editionStripped = stripped.Distinct().ToList();
            EditorUtility.SetDirty(catalog);
            return catalog.editionStripped;
        }

        /// <summary>A def belongs to a stripped culture by folder, by id prefix
        /// (Feraldis_Spearman) or, for a tech, by its culture gate.</summary>
        private static bool Owned(string path, string id, string techCulture, string[] cultures)
        {
            foreach (var c in cultures)
            {
                foreach (var folder in CultureFolders(c))
                    if (!string.IsNullOrEmpty(path) && path.StartsWith(folder, StringComparison.Ordinal))
                        return true;
                if (!string.IsNullOrEmpty(id) && id.StartsWith(c + "_", StringComparison.Ordinal))
                    return true;
                if (!string.IsNullOrEmpty(techCulture)
                    && string.Equals(techCulture.Trim(), c, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ── other Resources catalogs ──────────────────────────────────────

        private const string ConfigCatalogPath = "Assets/Resources/ComponentConfigCatalog.asset";
        private const string AbilityCatalogPath = "Assets/Resources/AbilityCatalog.asset";

        /// <summary>Drop every entry of a catalog list whose asset lives in a stripped culture's folders.</summary>
        private static int StripListedAssets<TCatalog>(string path, Func<TCatalog, System.Collections.IList> list, string[] cultures)
            where TCatalog : ScriptableObject
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TCatalog>(path);
            if (catalog == null) return 0;
            var items = list(catalog);
            var folders = cultures.SelectMany(CultureFolders).ToArray();
            int removed = 0;
            bool backedUp = false;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var o = items[i] as UnityEngine.Object;
                string p = o != null ? AssetDatabase.GetAssetPath(o) : null;
                if (string.IsNullOrEmpty(p) || !folders.Any(f => p.StartsWith(f, StringComparison.Ordinal))) continue;
                if (!backedUp) { Backup(path); backedUp = true; }
                items.RemoveAt(i);
                removed++;
            }
            if (removed > 0) EditorUtility.SetDirty(catalog);
            return removed;
        }

        private static int StripTrailingAbilities(string[] cultures)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Abilities.AbilityCatalogSO>(AbilityCatalogPath);
            if (catalog == null) return 0;
            var folders = cultures.SelectMany(CultureFolders).ToArray();
            bool Owned(TheWaningBorder.Abilities.AbilityDefSO a)
            {
                string p = a != null ? AssetDatabase.GetAssetPath(a) : null;
                return !string.IsNullOrEmpty(p) && folders.Any(f => p.StartsWith(f, StringComparison.Ordinal));
            }
            var list = catalog.abilities;
            int tail = list.Count;
            while (tail > 0 && Owned(list[tail - 1])) tail--;
            for (int i = 0; i < tail; i++)
                if (Owned(list[i]))
                    throw new InvalidOperationException(
                        $"Ability card {i} ({AssetDatabase.GetAssetPath(list[i])}) belongs to a stripped culture but is " +
                        "not at the end of AbilityCatalog — stripping it would renumber every card after it. " +
                        "Move the culture's cards to the end of the list.");
            int removed = list.Count - tail;
            if (removed == 0) return 0;
            Backup(AbilityCatalogPath);
            list.RemoveRange(tail, removed);
            EditorUtility.SetDirty(catalog);
            return removed;
        }

        // ── shared building prefabs ───────────────────────────────────────

        /// <summary>
        /// Deletes a stripped culture's branch from every building prefab that
        /// carries per-culture branches. A branch is recognised by the shape
        /// BuildingVariantVisual.Scan reads: a child named for a culture whose
        /// parent also holds an "Alanthor" or "Lv0" child.
        /// </summary>
        private static int StripPrefabBranches(string[] cultures)
        {
            var names = new HashSet<string>(cultures.SelectMany(BranchNames), StringComparer.OrdinalIgnoreCase);
            var skip = cultures.SelectMany(CultureFolders).ToArray();
            int removed = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { TechTreeRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (skip.Any(f => path.StartsWith(f, StringComparison.Ordinal))) continue;   // unreferenced once stripped

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || !asset.GetComponentsInChildren<Transform>(true).Any(t => IsBranch(t, names)))
                    continue;

                Backup(path);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var branches = root.GetComponentsInChildren<Transform>(true).Where(t => IsBranch(t, names)).ToList();
                    foreach (var b in branches)
                    {
                        if (b == null) continue;   // already gone with a removed ancestor
                        UnityEngine.Object.DestroyImmediate(b.gameObject);
                        removed++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return removed;
        }

        private static bool IsBranch(Transform t, HashSet<string> names)
        {
            if (t.parent == null || !names.Contains(t.name)) return false;
            var parent = t.parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                string sibling = parent.GetChild(i).name;
                if (string.Equals(sibling, "Alanthor", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sibling, "Lv0", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ── verification ──────────────────────────────────────────────────

        /// <summary>
        /// Every packed asset that came from a stripped culture's folders.
        /// Scripts are excluded: the code ships by design (see the header).
        /// </summary>
        public static List<string> FindLeaks(BuildReport report, string edition)
        {
            var folders = StrippedCultures(edition).SelectMany(CultureFolders).ToArray();
            var leaks = new List<string>();
            if (folders.Length == 0 || report == null) return leaks;

            foreach (var packed in report.packedAssets)
                foreach (var content in packed.contents)
                {
                    string path = content.sourceAssetPath;
                    if (string.IsNullOrEmpty(path) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    if (folders.Any(f => path.StartsWith(f, StringComparison.Ordinal)) && !leaks.Contains(path))
                        leaks.Add(path);
                }
            return leaks;
        }

        // ── backup / restore ──────────────────────────────────────────────

        private static void Backup(string assetPath)
        {
            string source = Path.GetFullPath(assetPath);
            string copy = Path.Combine(BackupDir, Guid.NewGuid().ToString("N"));
            File.Copy(source, copy, overwrite: true);
            File.AppendAllText(ManifestPath, assetPath + "|" + Path.GetFileName(copy) + "\n");
        }

        private static void RestoreBackups(string why)
        {
            if (!File.Exists(ManifestPath)) return;

            var restored = new List<string>();
            foreach (var line in File.ReadAllLines(ManifestPath))
            {
                var parts = line.Split('|');
                if (parts.Length != 2) continue;
                string copy = Path.Combine(BackupDir, parts[1]);
                if (!File.Exists(copy)) continue;
                File.Copy(copy, Path.GetFullPath(parts[0]), overwrite: true);
                restored.Add(parts[0]);
            }

            foreach (var path in restored)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Directory.Delete(BackupDir, recursive: true);

            Debug.Log($"[EditionContent] Restored {restored.Count} stripped asset(s) ({why}).");
        }

        /// <summary>Crash recovery: a backup left behind by a strip that never
        /// finished is restored on the next domain reload.</summary>
        [InitializeOnLoadMethod]
        private static void RestoreLeftovers()
        {
            if (!File.Exists(ManifestPath)) return;
            if (BuildPipeline.isBuildingPlayer) return;   // a live build's own scope will restore
            Debug.LogWarning("[EditionContent] Found an unfinished edition strip from an earlier build — " +
                             "restoring the original catalog and prefabs now.");
            RestoreBackups("left over from an interrupted build");
        }
    }
}
