// TerrainOverlaySplatCleanup.cs
// One-shot editor tool: removes stale splat layers and their painted weights
// from the open scene's terrains.
//
// WHY: the retired CPU terrain painters (InfluenceTerrainPainter,
// VeilFieldSystem's crust paint) wrote splat weights into runtime/overlay
// layers during play and restored them on clean teardown ONLY — any play
// session that crashed or was stopped hard left those painted weights baked
// into the TerrainData asset. Weights pointing at a null/missing layer render
// as Unity's gray CHECKERBOARD in the splat itself (the "textureless"
// patches along the crust rim). Dynamic ground is now painted per-pixel by
// the TWB/Terrain/Lit overlays, so none of these layers belong on the
// terrain anymore.
//
// USE: open the map scene (NOT in play mode) → menu  TWB ▸ Terrain ▸
// Clean Stale Overlay Splat Layers  → save. Undo-able.
//
// Location: Assets/Scripts/World/Terrain/Editor/

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.World.Terrain.EditorTools
{
    public static class TerrainOverlaySplatCleanup
    {
        // Layers the retired painters drove — their weights (if any linger)
        // are collapsed back into the base layer.
        private static readonly string[] RetiredNames =
        {
            "AlanthorInfluence", "RunaiiInfluence", "FeraldisInfluence",
            "CurseInfluence", "Blood",
        };

        [MenuItem("TWB/Terrain/Clean Stale Overlay Splat Layers")]
        public static void Clean()
        {
            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
            {
                Debug.LogWarning("[TerrainCleanup] no active terrain in the open scene.");
                return;
            }
            foreach (var terrain in terrains)
                CleanTerrain(terrain);
        }

        private static void CleanTerrain(UnityEngine.Terrain terrain)
        {
            var data = terrain.terrainData;
            if (data == null) return;

            var layers = data.terrainLayers;
            var keep = new List<int>();
            var dropped = new List<string>();
            for (int i = 0; i < layers.Length; i++)
            {
                if (IsStale(layers[i], out string why)) dropped.Add($"[{i}] {why}");
                else keep.Add(i);
            }

            if (dropped.Count == 0)
            {
                Debug.Log($"[TerrainCleanup] {terrain.name}: nothing stale — no changes.");
                return;
            }
            if (keep.Count == 0)
            {
                Debug.LogWarning($"[TerrainCleanup] {terrain.name}: every layer is stale — aborting (assign a valid base layer first).");
                return;
            }

            int res = data.alphamapResolution;
            var src = data.GetAlphamaps(0, 0, res, res);
            var dst = new float[res, res, keep.Count];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float sum = 0f;
                    for (int k = 0; k < keep.Count; k++)
                    {
                        dst[y, x, k] = src[y, x, keep[k]];
                        sum += dst[y, x, k];
                    }
                    if (sum <= 0.001f)
                    {
                        // Texel was painted entirely in stale layers — hand it
                        // back to the base layer.
                        dst[y, x, 0] = 1f;
                        for (int k = 1; k < keep.Count; k++) dst[y, x, k] = 0f;
                    }
                    else
                    {
                        for (int k = 0; k < keep.Count; k++) dst[y, x, k] /= sum;
                    }
                }

            Undo.RegisterCompleteObjectUndo(data, "Clean stale overlay splat layers");
            var keptLayers = new TerrainLayer[keep.Count];
            for (int k = 0; k < keep.Count; k++) keptLayers[k] = layers[keep[k]];
            data.terrainLayers = keptLayers;
            data.SetAlphamaps(0, 0, dst);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TerrainCleanup] {terrain.name}: removed {dropped.Count} stale layer(s), " +
                $"weights collapsed to base:\n  " + string.Join("\n  ", dropped));
        }

        private static bool IsStale(TerrainLayer l, out string why)
        {
            if (l == null) { why = "null layer slot"; return true; }
            if (l.diffuseTexture == null) { why = $"'{l.name}' has no diffuse texture"; return true; }
            if (l.name.IndexOf("VeilCrust", System.StringComparison.OrdinalIgnoreCase) >= 0)
            { why = $"'{l.name}' is a leftover runtime veil layer"; return true; }
            foreach (var n in RetiredNames)
                if (string.Equals(l.name, n, System.StringComparison.OrdinalIgnoreCase))
                { why = $"'{l.name}' is a retired painter-driven layer"; return true; }
            why = null;
            return false;
        }
    }
}
#endif
