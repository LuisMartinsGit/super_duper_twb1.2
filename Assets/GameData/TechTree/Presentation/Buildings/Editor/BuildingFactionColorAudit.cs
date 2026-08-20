// BuildingFactionColorAudit.cs
// Editor audit for the building team-color rule (BuildingFactionColorMarker).
//
// The rule has three ways to reach a surface, and a building that satisfies
// NONE of them renders in its authored colors for every player:
//   1. a GameObject named *roof*    -> solid faction color
//   2. a GameObject named *stripe*  -> faction tint over the authored albedo
//   3. an albedo atlas carrying pixels in the marker hue band -> pixel swap
//      (or, for untextured materials, a flat _BaseColor near the marker blue)
//
// Whether a given prefab qualifies is invisible from the code: it depends on
// mesh naming and on what is actually painted into the atlas. This walks every
// building prefab the game can spawn and reports which rule each one lands on,
// plus the atlases that are not Read/Write enabled (those still recolor, via a
// GPU copy, but the copy is avoidable).
//
// Location: Assets/GameData/TechTree/Presentation/Buildings/Editor/BuildingFactionColorAudit.cs

#if UNITY_EDITOR

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Presentation.Editor
{
    public static class BuildingFactionColorAudit
    {
        // Folders that hold spawnable building visuals. Prefabs elsewhere
        // (source FBX prefabs, MISC scratch) are not what the game loads.
        private static readonly string[] SearchFolders =
        {
            "Assets/GameData/TechTree/Buildings",
            "Assets/Resources/Prefabs/Buildings",
        };

        // Sub-paths under those roots whose prefabs are raw model imports rather
        // than spawnable visuals.
        private static readonly string[] IgnoreFragments = { "/FBX/", "/Source/" };

        [MenuItem("Waning Border/Buildings/Audit Faction Colors")]
        public static void Audit() => Run(fixReadWrite: false);

        [MenuItem("Waning Border/Buildings/Audit Faction Colors (Enable Read-Write)")]
        public static void AuditAndFix() => Run(fixReadWrite: true);

        private static void Run(bool fixReadWrite)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
            var report = new StringBuilder();
            report.AppendLine("[BuildingFactionColorAudit] building team-color coverage");
            report.AppendLine();

            var unreadable = new SortedDictionary<string, string>(); // path -> texture name
            int covered = 0, uncovered = 0, scanned = 0;
            var uncoveredNames = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldIgnore(path)) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                scanned++;
                int roofs = 0, stripes = 0, atlasHits = 0, flatHits = 0, slots = 0;
                var atlases = new HashSet<Texture2D>();

                foreach (var r in renderers)
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                    string n = r.gameObject.name;
                    if (n.IndexOf("roof", System.StringComparison.OrdinalIgnoreCase) >= 0) { roofs++; continue; }
                    if (n.IndexOf("stripe", System.StringComparison.OrdinalIgnoreCase) >= 0) { stripes++; continue; }

                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) continue;
                        slots++;

                        var tex = AlbedoOf(mat);
                        if (tex != null)
                        {
                            if (!atlases.Add(tex)) continue;
                            if (!tex.isReadable)
                            {
                                string texPath = AssetDatabase.GetAssetPath(tex);
                                if (!string.IsNullOrEmpty(texPath)) unreadable[texPath] = tex.name;
                            }
                            if (HasMarkerPixels(tex)) atlasHits++;
                        }
                        else if (IsMarkerFlatColor(mat))
                        {
                            flatHits++;
                        }
                    }
                }

                bool hasCoverage = roofs > 0 || stripes > 0 || atlasHits > 0 || flatHits > 0;
                if (hasCoverage) covered++;
                else { uncovered++; uncoveredNames.Add(path); }

                report.AppendLine(
                    $"  {(hasCoverage ? "OK  " : "NONE")} {System.IO.Path.GetFileNameWithoutExtension(path)}" +
                    $"  roof={roofs} stripe={stripes} atlas={atlasHits}/{atlases.Count} flat={flatHits} slots={slots}");
            }

            report.AppendLine();
            report.AppendLine($"  scanned {scanned} prefabs — {covered} team-colored, {uncovered} with NO rule match");
            if (uncovered > 0)
            {
                report.AppendLine("  no rule matched (renders identically for every player):");
                foreach (var p in uncoveredNames) report.AppendLine($"    {p}");
                report.AppendLine("  fix by naming the team part *roof* / *stripe*, or painting its atlas region " +
                                  $"in the marker hue band ({BuildingFactionColorMarker.HueMin:F0}-" +
                                  $"{BuildingFactionColorMarker.HueMax:F0} degrees).");
            }

            if (unreadable.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"  {unreadable.Count} atlas texture(s) without Read/Write " +
                                  "(recolor works, but pays a GPU copy per faction):");
                foreach (var kvp in unreadable) report.AppendLine($"    {kvp.Key}");
                if (fixReadWrite) report.AppendLine("  enabling Read/Write on all of them...");
                else report.AppendLine("  run \"Audit Faction Colors (Enable Read-Write)\" to fix them.");
            }

            Debug.Log(report.ToString());

            if (!fixReadWrite || unreadable.Count == 0) return;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var texPath in unreadable.Keys)
                {
                    var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    if (importer == null || importer.isReadable) continue;
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[BuildingFactionColorAudit] enabled Read/Write on {unreadable.Count} texture(s).");
        }

        private static bool ShouldIgnore(string path)
        {
            for (int i = 0; i < IgnoreFragments.Length; i++)
                if (path.IndexOf(IgnoreFragments[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // Mirrors BuildingFactionColorMarker.AlbedoProps — only the main albedo
        // slots are candidates for the swap.
        private static Texture2D AlbedoOf(Material mat)
        {
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") is Texture2D bm) return bm;
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") is Texture2D mt) return mt;
            return null;
        }

        private static bool IsMarkerFlatColor(Material mat)
        {
            Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                    : mat.HasProperty("_Color") ? mat.GetColor("_Color")
                    : Color.white;
            var m = BuildingFactionColorMarker.Marker;
            float dr = c.r - m.r, dg = c.g - m.g, db = c.b - m.b;
            return dr * dr + dg * dg + db * db <= BuildingFactionColorMarker.ToleranceSquared;
        }

        // Same hue/saturation test the runtime swap uses. Reads through the
        // asset importer so an un-readable texture can still be inspected.
        private static readonly Dictionary<Texture2D, bool> _markerCache = new Dictionary<Texture2D, bool>();

        private static bool HasMarkerPixels(Texture2D tex)
        {
            if (_markerCache.TryGetValue(tex, out bool cached)) return cached;

            bool result = false;
            Color32[] pixels = null;

            if (tex.isReadable)
            {
                pixels = tex.GetPixels32();
            }
            else
            {
                // Temporarily readable copy through the GPU — same trick the
                // runtime uses, so the audit sees exactly what it would.
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;
                Texture2D copy = null;
                try
                {
                    Graphics.Blit(tex, rt);
                    RenderTexture.active = rt;
                    copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false);
                    copy.ReadPixels(new Rect(0f, 0f, tex.width, tex.height), 0, 0);
                    copy.Apply(false, false);
                    pixels = copy.GetPixels32();
                }
                catch (System.Exception)
                {
                    pixels = null;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                    if (copy != null) Object.DestroyImmediate(copy);
                }
            }

            if (pixels != null)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    Color.RGBToHSV(new Color(p.r / 255f, p.g / 255f, p.b / 255f, 1f),
                        out float h, out float s, out _);
                    float deg = h * 360f;
                    if (deg < BuildingFactionColorMarker.HueMin || deg > BuildingFactionColorMarker.HueMax) continue;
                    if (s < BuildingFactionColorMarker.MinSaturation) continue;
                    result = true;
                    break;
                }
            }

            _markerCache[tex] = result;
            return result;
        }
    }
}

#endif
