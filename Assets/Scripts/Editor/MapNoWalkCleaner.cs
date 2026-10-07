// MapNoWalkCleaner.cs
// Strips the scattered NoWalk patches out of a map's TerrainData (2026-10-06,
// developer: "Mirror Marches and Veilmarch have random blotches of NoWalk;
// I need those maps to be free").
//
// The patches are the flora pass's: MapGenKit.ScatterFlora plants terrain
// trees and rocks and paints NoWalk under every cell one stands on, because a
// terrain tree is not an entity and the paint is how it blocks. So the clean
// removes both halves together: every tree instance standing on NoWalk paint,
// and the paint itself, whose weight is handed to the walkable layer that
// dominates the ground round it (the look of the surrounding ground carries
// on through the patch). Trees on walkable ground are left alone.
//
// Edits the TerrainData ASSET, so the change ships with the map. Run once per
// map; running it again finds nothing to do.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapNoWalkCleaner
    {
        const string MirrorMarches = "Assets/GameData/Scenes/Maps/Mirror Marches/MirrorMarches TerrainData.asset";
        const string Veilmarch = "Assets/GameData/Scenes/Maps/Veilmarch/Veilmarch TerrainData.asset";

        /// <summary>How far round a NoWalk texel the walkable layer that
        /// replaces it is looked for, in alphamap texels.</summary>
        const int NeighbourReach = 6;

        [MenuItem("Waning Border/Maps/Clear NoWalk Patches: Mirror Marches")]
        public static void ClearMirrorMarches() => Clear(MirrorMarches);

        [MenuItem("Waning Border/Maps/Clear NoWalk Patches: Veilmarch")]
        public static void ClearVeilmarch() => Clear(Veilmarch);

        public static void Clear(string path)
        {
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (td == null) { Debug.LogError($"[MapNoWalkCleaner] no TerrainData at {path}"); return; }
            Clear(td);
        }

        /// <summary>The clean, on a TerrainData already in hand (the map
        /// builders call it after their flora pass).</summary>
        public static void Clear(TerrainData td)
        {

            var layers = td.terrainLayers;
            int noWalk = -1;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null && layers[i].name.ToLowerInvariant().Contains("nowalk")) { noWalk = i; break; }
            if (noWalk < 0) { Debug.Log($"[MapNoWalkCleaner] {td.name}: no NoWalk layer — nothing to do"); return; }

            int W = td.alphamapWidth, H = td.alphamapHeight, L = td.alphamapLayers;
            var a = td.GetAlphamaps(0, 0, W, H);   // [z, x, layer]
            var blocked = new bool[H, W];
            int painted = 0;
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                    if (a[z, x, noWalk] > 0f) { blocked[z, x] = true; painted++; }

            // 1. Trees and rocks standing on the paint.
            var trees = td.treeInstances;
            var keep = new List<TreeInstance>(trees.Length);
            int removed = 0;
            for (int i = 0; i < trees.Length; i++)
            {
                var t = trees[i];
                int tx = Mathf.Clamp(Mathf.FloorToInt(t.position.x * W), 0, W - 1);
                int tz = Mathf.Clamp(Mathf.FloorToInt(t.position.z * H), 0, H - 1);
                if (a[tz, tx, noWalk] >= 0.5f) { removed++; continue; }
                keep.Add(t);
            }
            if (removed > 0) td.SetTreeInstances(keep.ToArray(), snapToHeightmap: true);

            // 2. The paint: each texel's NoWalk weight goes to the walkable
            //    layer with the most weight in the clean ground round it.
            var sum = new float[L];
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    if (!blocked[z, x]) continue;
                    System.Array.Clear(sum, 0, L);
                    for (int r = 1; r <= NeighbourReach; r++)
                    {
                        for (int dz = -r; dz <= r; dz++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                                int nx = x + dx, nz = z + dz;
                                if (nx < 0 || nz < 0 || nx >= W || nz >= H || blocked[nz, nx]) continue;
                                for (int l = 0; l < L; l++) if (l != noWalk) sum[l] += a[nz, nx, l];
                            }
                        bool any = false;
                        for (int l = 0; l < L; l++) if (sum[l] > 0f) { any = true; break; }
                        if (any) break;
                    }
                    int best = noWalk == 0 ? 1 : 0;
                    for (int l = 0; l < L; l++)
                        if (l != noWalk && sum[l] > sum[best]) best = l;
                    a[z, x, best] += a[z, x, noWalk];
                    a[z, x, noWalk] = 0f;
                }
            td.SetAlphamaps(0, 0, a);

            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MapNoWalkCleaner] {td.name}: {painted} NoWalk texel(s) repainted, " +
                      $"{removed} tree/rock instance(s) on them removed");
        }
    }
}
