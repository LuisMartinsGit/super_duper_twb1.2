// MapLobbyImageBaker.cs
// EDITOR-ONLY: composes the BFME2 / Supreme Commander style lobby image for
// a map — the top-down map picture with a numbered, faction-coloured slot
// badge sitting on every player start.
//   Waning Border > Maps > Bake Lobby Image From Open Scene
//
// Why this exists alongside MapInfoBaker: the in-game lobby already draws
// live slot dots over the thumbnail (MapPreviewWidget reads
// MapInfo.PlayerStarts), so the running game does not need this file. What
// it does NOT have is a single flat PNG you can hand to a store page, a
// map-pack readme, or any UI that cannot host the widget. This bakes that.
//
// Runs against whatever map scene is open and is generic — nothing here is
// specific to any one map. Requires MapInfoBaker to have run first, because
// it composes on top of that thumbnail.
//
// Wrapped in #if UNITY_EDITOR for the same reason as MapInfoBaker: this
// project ships one runtime asmdef, so an Editor/ folder alone would not
// keep it out of player builds.

#if UNITY_EDITOR
using System.IO;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapLobbyImageBaker
    {
        private const int   Size        = 768;
        private const float BadgeRadius = 26f;
        private const float RingWidth   = 4f;

        [MenuItem("Waning Border/Maps/Bake Lobby Image From Open Scene")]
        public static void Bake()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || !scene.path.StartsWith(MapRegistry.MapsRoot))
            {
                EditorUtility.DisplayDialog("Lobby Image Baker",
                    $"The open scene is not a map scene.\n\nOpen a scene under\n{MapRegistry.MapsRoot} first.",
                    "OK");
                return;
            }

            string folder = Path.GetDirectoryName(scene.path).Replace('\\', '/');
            string mapName = Path.GetFileName(folder);

            var thumb = LoadThumbnail(folder);
            if (thumb == null)
            {
                EditorUtility.DisplayDialog("Lobby Image Baker",
                    "No thumbnail found in the map folder.\n\n" +
                    "Run  Waning Border > Maps > Bake Map Info From Open Scene  first — " +
                    "the lobby image is composed on top of that capture.",
                    "OK");
                return;
            }

            var canvas = Rescale(thumb, Size);

            // Slot badges, ordered so the numbering is stable between bakes:
            // by faction index, not by scene hierarchy order (which shifts
            // whenever markers are re-created).
            var starts = Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None);
            System.Array.Sort(starts, (a, b) => ((int)a.Faction).CompareTo((int)b.Faction));

            GetMapBounds(out Vector3 min, out Vector3 size);
            for (int i = 0; i < starts.Length; i++)
            {
                Vector3 p = starts[i].WorldPosition;
                float u = Mathf.Clamp01((p.x - min.x) / Mathf.Max(1f, size.x));
                float v = Mathf.Clamp01((p.z - min.z) / Mathf.Max(1f, size.z));
                DrawSlot(canvas, u * Size, v * Size,
                         FactionColors.Get(starts[i].Faction), i + 1);
            }
            canvas.Apply();

            string path = $"{folder}/{mapName} Lobby.png";
            File.WriteAllBytes(path, canvas.EncodeToPNG());
            Object.DestroyImmediate(canvas);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            Debug.Log($"[MapLobbyImageBaker] Wrote {path} with {starts.Length} player slots.",
                      AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        // ── Composition ─────────────────────────────────────────────────

        /// <summary>
        /// One slot badge: a dark outline, a faction-coloured disc, a light
        /// inner ring and the slot number. Drawn in that order so the number
        /// stays legible over every faction colour, including the pale ones.
        /// </summary>
        private static void DrawSlot(Texture2D tex, float cx, float cy, Color faction, int number)
        {
            float outer = BadgeRadius + RingWidth;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - outer - 1));
            int x1 = Mathf.Min(tex.width - 1, Mathf.CeilToInt(cx + outer + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - outer - 1));
            int y1 = Mathf.Min(tex.height - 1, Mathf.CeilToInt(cy + outer + 1));

            var rim = new Color(0.05f, 0.05f, 0.07f, 1f);
            var inner = Color.Lerp(faction, Color.white, 0.55f);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > outer + 1f) continue;

                    Color c;
                    if (d <= BadgeRadius - RingWidth)      c = faction;
                    else if (d <= BadgeRadius)             c = inner;
                    else                                   c = rim;

                    // 1-pixel antialiased edge so badges do not look chewed.
                    float a = Mathf.Clamp01(outer + 1f - d);
                    tex.SetPixel(x, y, Color.Lerp(tex.GetPixel(x, y), c, a));
                }
            }

            DrawDigit(tex, number, cx, cy, rim);
        }

        // 3x5 bitmap digits. A real font would mean rendering through
        // TextMeshPro into a RenderTexture at bake time; for slot numbers
        // 1-8 a hand-rolled glyph is a fraction of the machinery and cannot
        // break when a font asset moves.
        private static readonly byte[][] Glyphs =
        {
            new byte[]{0b111,0b101,0b101,0b101,0b111}, // 0
            new byte[]{0b010,0b110,0b010,0b010,0b111}, // 1
            new byte[]{0b111,0b001,0b111,0b100,0b111}, // 2
            new byte[]{0b111,0b001,0b111,0b001,0b111}, // 3
            new byte[]{0b101,0b101,0b111,0b001,0b001}, // 4
            new byte[]{0b111,0b100,0b111,0b001,0b111}, // 5
            new byte[]{0b111,0b100,0b111,0b101,0b111}, // 6
            new byte[]{0b111,0b001,0b010,0b010,0b010}, // 7
            new byte[]{0b111,0b101,0b111,0b101,0b111}, // 8
        };

        private static void DrawDigit(Texture2D tex, int digit, float cx, float cy, Color c)
        {
            if (digit < 0 || digit >= Glyphs.Length) return;
            var g = Glyphs[digit];
            const int px = 5;   // pixels per glyph cell
            float ox = cx - (3 * px) / 2f;
            float oy = cy + (5 * px) / 2f;

            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    if ((g[row] & (1 << (2 - col))) == 0) continue;
                    int bx = Mathf.RoundToInt(ox + col * px);
                    int by = Mathf.RoundToInt(oy - row * px);
                    for (int dy = 0; dy < px; dy++)
                        for (int dx = 0; dx < px; dx++)
                        {
                            int x = bx + dx, y = by + dy;
                            if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) continue;
                            tex.SetPixel(x, y, c);
                        }
                }
            }
        }

        // ── Plumbing ────────────────────────────────────────────────────

        private static Texture2D LoadThumbnail(string folder)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(" Lobby.png")) continue;   // never compose on ourselves
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// Copy into a fresh readable RGBA32 texture at the target size. The
        /// imported thumbnail is usually non-readable and possibly
        /// compressed, so GetPixels on it would throw — a Blit through a
        /// RenderTexture sidesteps both without touching import settings.
        /// </summary>
        private static Texture2D Rescale(Texture2D src, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var dst = new Texture2D(size, size, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            dst.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        /// <summary>Same bounds rule MapInfoBaker uses, so the badges land
        /// exactly where that baker's normalized marker positions say they
        /// should.</summary>
        private static void GetMapBounds(out Vector3 min, out Vector3 size)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                min = terrain.GetPosition();
                size = terrain.terrainData.size;
                return;
            }

            var markers = Object.FindObjectsByType<MapMarker>(FindObjectsSortMode.None);
            if (markers.Length == 0)
            {
                min = new Vector3(-256f, 0f, -256f);
                size = new Vector3(512f, 100f, 512f);
                return;
            }

            var b = new Bounds(markers[0].transform.position, Vector3.zero);
            foreach (var m in markers) b.Encapsulate(m.transform.position);
            b.Expand(80f);
            min = b.min;
            size = b.size;
            if (size.y < 50f) size.y = 50f;
        }
    }
}
#endif
