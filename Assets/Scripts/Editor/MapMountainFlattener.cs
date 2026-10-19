// MapMountainFlattener.cs
// Lowers the mountains of a HAND-SCULPTED map (no builder: Sundered Crown,
// Hollow Table, Twin Spans, the shipped 704 m Sundered Reach) to a THIRD of
// their height, keeping their shape and ruggedness, and paints them NoWalk so
// they stay impassable (2026-10-19, developer: "I want them 1/3 as high with
// the same ruggedness"). The built maps do the same in their own builders
// (tools/maps/layoutkit.py, MapBuilderVeilmarchGrand, MapBuilderRiverlands).
//
//   Waning Border > Maps > Flatten Mountains To Third (Open Scene)
//
// What is a mountain: every connected mass of ground that was impassable on
// the ORIGINAL heights — above the 24 m mountain line (RegionMap /
// RegionSeeder's MountainHeight) or steeper than the walk limit
// (PassabilityGrid.MaxWalkableSlope, sampled 1.5 m apart, as the grid does) —
// and that reaches the 24 m line somewhere. A cliff or a hill that never
// reaches 24 m is not a mountain and is left alone; water is never touched.
//
// The ground a mountain stands on is relaxed in from its foot (the walkable
// ground around it), so a mass between two levels has no seam, and every
// sample becomes  ground + (height - ground) / 3 : the relief is scaled, the
// noise with it, the foot meets the plain where it did. The lowered flanks
// are under the slope limit, so the mass is painted with the NoWalk layer —
// the paint, not the height, keeps it impassable (PassabilityGrid reads it in
// every mode). Claimability is the region's KIND at runtime (RegionMap), so
// it is unchanged.
//
// Edits the TerrainData asset (undoable once with Ctrl+Z). Run it ONCE per
// map: a second run finds no ground above 24 m on a map whose peaks were
// under 72 m over its plain, but a taller map would be lowered again — the
// dialog shows what it found before anything changes. Afterwards: Bake Map
// Info From Open Scene, then Bake Lobby Image From Open Scene.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapMountainFlattener
    {
        const float Scale = 1f / 3f;
        const float MountainLine = 24f;        // RegionMap / RegionSeeder MountainHeight
        const float WaterLine = 4f;            // PassabilityGrid / RegionMap WaterHeight
        const float MaxWalkableSlope = 1.0f;   // PassabilityGrid.MaxWalkableSlope
        const float SlopeStep = 1.5f;          // PassabilityGrid.SlopeCheckStep (m)
        const int RelaxIterations = 400;

        /// <summary>True when driven by a script (no dialogs: the confirm is
        /// skipped and the reports go to the Console).</summary>
        public static bool Unattended;

        static bool Ask(string title, string msg, string ok, string cancel = null)
        {
            if (Unattended) { Debug.Log($"[{title}] {msg}"); return true; }
            return cancel == null ? EditorUtility.DisplayDialog(title, msg, ok)
                                  : EditorUtility.DisplayDialog(title, msg, ok, cancel);
        }

        [MenuItem("Waning Border/Maps/Flatten Mountains To Third (Open Scene)")]
        public static void FlattenOpenScene()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            if (terrains.Length == 0)
            {
                Ask("Flatten Mountains", "The open scene has no Terrain.", "OK");
                return;
            }
            int done = 0;
            foreach (var t in terrains)
                if (t != null && t.terrainData != null && Flatten(t)) done++;
            if (done > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                AssetDatabase.SaveAssets();
            }
        }

        static bool Flatten(Terrain terrain)
        {
            var td = terrain.terrainData;
            int res = td.heightmapResolution;
            float sy = td.size.y;
            float cell = td.size.x / (res - 1);
            var raw = td.GetHeights(0, 0, res, res);          // [z, x], 0..1
            var h = new float[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++) h[z * res + x] = raw[z, x] * sy;

            // ── the ORIGINAL impassable ground: above the line, or too steep ──
            int st = Mathf.Max(1, Mathf.RoundToInt(SlopeStep / cell));
            float span = 2f * st * cell;
            var blocked = new bool[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;
                    if (h[i] <= WaterLine) continue;
                    if (h[i] >= MountainLine) { blocked[i] = true; continue; }
                    int xl = Mathf.Max(0, x - st), xr = Mathf.Min(res - 1, x + st);
                    int zd = Mathf.Max(0, z - st), zu = Mathf.Min(res - 1, z + st);
                    float dX = (h[z * res + xr] - h[z * res + xl]) / span;
                    float dZ = (h[zu * res + x] - h[zd * res + x]) / span;
                    blocked[i] = dX * dX + dZ * dZ > MaxWalkableSlope * MaxWalkableSlope;
                }

            // ── mountains: the blocked masses that reach the line ──
            var mtn = new bool[res * res];
            var comp = new List<int>();
            var queue = new Queue<int>();
            var seen = new bool[res * res];
            int masses = 0, count = 0;
            float peak = 0f;
            for (int s = 0; s < res * res; s++)
            {
                if (!blocked[s] || seen[s]) continue;
                comp.Clear(); bool reaches = false;
                seen[s] = true; queue.Enqueue(s);
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue(); comp.Add(i);
                    if (h[i] >= MountainLine) reaches = true;
                    int x = i % res, z = i / res;
                    if (x > 0) Visit(i - 1); if (x < res - 1) Visit(i + 1);
                    if (z > 0) Visit(i - res); if (z < res - 1) Visit(i + res);
                }
                if (!reaches) continue;
                masses++;
                foreach (int i in comp) { mtn[i] = true; count++; peak = Mathf.Max(peak, h[i]); }
            }
            void Visit(int j) { if (blocked[j] && !seen[j]) { seen[j] = true; queue.Enqueue(j); } }

            if (count == 0)
            {
                Ask("Flatten Mountains",
                    $"{terrain.name}: no ground reaches the {MountainLine:0} m mountain line — nothing to lower " +
                    "(already flattened, or this map has no mountains).", "OK");
                return false;
            }

            // ── the ground under each mountain: nearest foot, then relaxed ──
            var g = new float[res * res];
            var gNear = new float[res * res];
            var dist = new int[res * res];
            for (int i = 0; i < res * res; i++) { dist[i] = -1; g[i] = h[i]; }
            for (int i = 0; i < res * res; i++)
            {
                if (mtn[i]) continue;
                int x = i % res, z = i / res;
                bool foot = (x > 0 && mtn[i - 1]) || (x < res - 1 && mtn[i + 1])
                         || (z > 0 && mtn[i - res]) || (z < res - 1 && mtn[i + res]);
                if (foot) { dist[i] = 0; gNear[i] = h[i]; queue.Enqueue(i); }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % res, z = i / res;
                if (x > 0) Spread(i, i - 1); if (x < res - 1) Spread(i, i + 1);
                if (z > 0) Spread(i, i - res); if (z < res - 1) Spread(i, i + res);
            }
            void Spread(int from, int to)
            {
                if (!mtn[to] || dist[to] >= 0) return;
                dist[to] = dist[from] + 1; gNear[to] = gNear[from]; queue.Enqueue(to);
            }
            var inner = new List<int>(count);
            for (int i = 0; i < res * res; i++)
                if (mtn[i]) { g[i] = gNear[i]; inner.Add(i); }
            var g2 = new float[inner.Count];
            for (int it = 0; it < RelaxIterations; it++)
            {
                for (int k = 0; k < inner.Count; k++)
                {
                    int i = inner[k], x = i % res, z = i / res;
                    float a = x > 0 ? g[i - 1] : g[i], b = x < res - 1 ? g[i + 1] : g[i];
                    float c = z > 0 ? g[i - res] : g[i], d = z < res - 1 ? g[i + res] : g[i];
                    g2[k] = 0.25f * (a + b + c + d);
                }
                for (int k = 0; k < inner.Count; k++) g[inner[k]] = g2[k];
            }

            // ── the report, then the edit ──
            var oldRel = new List<float>(count);
            var newH = new float[res * res];
            System.Array.Copy(h, newH, h.Length);
            float newPeak = 0f;
            foreach (int i in inner)
            {
                // never sink a sample under the ground it stands on
                newH[i] = Mathf.Max(g[i] + (h[i] - g[i]) * Scale, Mathf.Min(h[i], g[i]));
                oldRel.Add(h[i] - gNear[i]);
                newPeak = Mathf.Max(newPeak, newH[i]);
            }
            oldRel.Sort();
            float med = oldRel[oldRel.Count / 2], max = oldRel[oldRel.Count - 1];
            float area = count * cell * cell;
            if (!Ask("Flatten Mountains To Third",
                    $"{terrain.name}: {masses} mountain mass(es), {area / 10000f:0.0} ha.\n\n" +
                    $"Relief over the ground beside them: median {med:0.0} m, max {max:0.0} m -> " +
                    $"about {med * Scale:0.0} / {max * Scale:0.0} m.\nPeak {peak:0.0} m -> {newPeak:0.0} m.\n\n" +
                    "They are painted NoWalk so they stay impassable. Edits the TerrainData asset.",
                    "Flatten", "Cancel"))
                return false;

            Undo.RegisterCompleteObjectUndo(td, "Flatten Mountains To Third");
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++) raw[z, x] = Mathf.Clamp01(newH[z * res + x] / sy);
            td.SetHeights(0, 0, raw);

            int painted = PaintNoWalk(td, mtn, res, terrain.gameObject.scene.path);
            // terrain trees and rocks on the lowered ground come down with it
            td.SetTreeInstances(td.treeInstances, snapToHeightmap: true);
            EditorUtility.SetDirty(td);
            Debug.Log($"[MapMountainFlattener] {terrain.name}: {masses} mass(es), {count} samples, relief median " +
                      $"{med:0.0} -> {med * Scale:0.0} m, max {max:0.0} -> {max * Scale:0.0} m, peak {peak:0.0} -> " +
                      $"{newPeak:0.0} m; NoWalk painted on {painted} alphamap texels. Now Bake Map Info " +
                      "and Bake Lobby Image From Open Scene.");
            return true;
        }

        /// <summary>Full NoWalk weight on every alphamap texel over a mountain
        /// sample. Adds the map's NoWalk layer to the terrain when it has none.</summary>
        static int PaintNoWalk(TerrainData td, bool[] mtn, int res, string scenePath)
        {
            int idx = MapGenKit.FindNoWalkLayer(td);
            if (idx < 0)
            {
                var layer = FindNoWalkAsset(scenePath);
                if (layer == null)
                {
                    Debug.LogError("[MapMountainFlattener] the terrain has no NoWalk layer and none was found " +
                                   "beside the scene — the lowered mountains will NOT block. Add one and re-paint.");
                    return 0;
                }
                var list = new List<TerrainLayer>(td.terrainLayers) { layer };
                td.terrainLayers = list.ToArray();
                idx = list.Count - 1;
            }
            int aw = td.alphamapWidth, ah = td.alphamapHeight, L = td.alphamapLayers;
            var a = td.GetAlphamaps(0, 0, aw, ah);
            int painted = 0;
            for (int az = 0; az < ah; az++)
            {
                int hz = Mathf.Clamp(Mathf.RoundToInt((az + 0.5f) / ah * (res - 1)), 0, res - 1);
                for (int ax = 0; ax < aw; ax++)
                {
                    int hx = Mathf.Clamp(Mathf.RoundToInt((ax + 0.5f) / aw * (res - 1)), 0, res - 1);
                    if (!mtn[hz * res + hx]) continue;
                    for (int l = 0; l < L; l++) a[az, ax, l] = l == idx ? 1f : 0f;
                    painted++;
                }
            }
            td.SetAlphamaps(0, 0, a);
            return painted;
        }

        static TerrainLayer FindNoWalkAsset(string scenePath)
        {
            string dir = string.IsNullOrEmpty(scenePath) ? null : System.IO.Path.GetDirectoryName(scenePath);
            TerrainLayer any = null;
            foreach (var guid in AssetDatabase.FindAssets("NoWalk t:TerrainLayer"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains("nowalk")) continue;
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
                if (l == null) continue;
                if (dir != null && p.Replace('\\', '/').StartsWith(dir.Replace('\\', '/'))) return l;
                any ??= l;
            }
            return any;
        }
    }
}
