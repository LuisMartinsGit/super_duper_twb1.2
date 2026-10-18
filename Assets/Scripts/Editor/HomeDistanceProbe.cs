// HomeDistanceProbe.cs
// EDITOR-ONLY, play mode: how far apart are a map's homes ON FOOT?
//   Waning Border > Debug > Measure Home Distances (Temp measure.txt)
//
// Reads the map scene name from Temp/agent-bridge/measure.txt, launches it
// with AI in every seat (fog off), waits for the game's own PassabilityGrid,
// then for every pair of player starts finds the shortest walk on that grid
// and appends one CSV row per pair to Temp/agent-bridge/home_distances.csv:
//
//   map,from,to,straight_m,walk_m
//
// The walk is an 8-connected Dijkstra over the grid (no corner cutting)
// whose path is then string-pulled (straight runs wherever the ground between
// two path points is clear), so it measures what a unit actually walks, not
// the grid's zig-zag. Terrain and obstacle cells (slope, water deeper than
// the wade line, NoWalk paint, trees) block; BUILDING cells do not — the
// homes themselves stand on the start points, and this is a measure of the
// map, not of whatever was built on it.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Terrain;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.EditorTools
{
    public static class HomeDistanceProbe
    {
        private static readonly string Dir = Path.Combine("Temp", "agent-bridge");
        private static string _scene;
        private static float _deadline;
        private static int _readyFrames;

        [MenuItem("Waning Border/Debug/Measure Home Distances (Temp measure.txt)")]
        public static void Measure()
        {
            string req = Path.Combine(Dir, "measure.txt");
            if (!EditorApplication.isPlaying || !File.Exists(req))
            {
                Debug.LogWarning("[HomeDistance] needs play mode and " + req);
                return;
            }
            _scene = File.ReadAllText(req).Trim();

            int players = 2;
            foreach (string guid in AssetDatabase.FindAssets("t:MapInfo"))
            {
                var info = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Core.Maps.MapInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info != null && info.SceneName == _scene && info.PlayerCount > 0) players = info.PlayerCount;
            }

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = players;
            LobbyConfig.SetupSinglePlayer(players);
            for (int i = 0; i < players; i++)
            {
                var slot = LobbyConfig.Slots[i];
                slot.Type = SlotType.AI;
                slot.AIDifficulty = LobbyAIDifficulty.Easy;
                slot.TeamIndex = Alliances.NoTeam;
                slot.StartIndex = PlayerSlot.AutoStart;
            }
            GameSettings.SelectedMapScene = _scene;
            GameSettings.SpawnSeed = 20261017;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            LobbyConfig.ApplyColorSelections();
            Debug.Log($"[HomeDistance] launching {_scene} with {players} AI");
            LoadingScreen.Show(_scene);

            _deadline = Time.realtimeSinceStartup + 240f;
            _readyFrames = 0;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Poll; return; }
            if (Time.realtimeSinceStartup > _deadline)
            {
                EditorApplication.update -= Poll;
                Append($"{_scene},TIMEOUT,,,");
                Debug.LogError("[HomeDistance] timed out waiting for " + _scene);
                return;
            }
            var grid = PassabilityGrid.Instance;
            bool ready = SceneManager.GetSceneByName(_scene).isLoaded
                         && grid != null && grid.IsMaskReady
                         && MapMarkerRegistry.PlayerStarts.Count > 0;
            // a few frames' grace so the start clearing and the obstacle pass land
            if (!ready || ++_readyFrames < 30) return;
            EditorApplication.update -= Poll;
            Run(grid);
        }

        private static void Run(PassabilityGrid grid)
        {
            var starts = new List<PlayerStartMarker>(MapMarkerRegistry.PlayerStarts);
            starts.Sort(MapMarkerRegistry.ComparePlayerStarts);
            int w = grid.Width, h = grid.Height;
            var open = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte c = grid.GetCell(new int2(x, y));
                    open[y * w + x] = c == PassabilityGrid.Passable || c == PassabilityGrid.BuildingBlocked;
                }

            for (int a = 0; a < starts.Count; a++)
            {
                var src = grid.WorldToCell(starts[a].WorldPosition);
                var dist = Dijkstra(open, w, h, src, out var prev);
                for (int b = a + 1; b < starts.Count; b++)
                {
                    var pa = starts[a].WorldPosition; var pb = starts[b].WorldPosition;
                    float straight = math.distance(new float2(pa.x, pa.z), new float2(pb.x, pb.z));
                    var dst = grid.WorldToCell(pb);
                    int di = dst.y * w + dst.x;
                    string walk = "unreachable";
                    if (di >= 0 && di < dist.Length && dist[di] < float.MaxValue)
                    {
                        var path = new List<int2>();
                        for (int i = di; i >= 0; i = prev[i]) path.Add(new int2(i % w, i / w));
                        path.Reverse();
                        walk = (Pulled(open, w, h, path) * grid.CellSize).ToString("0", CultureInfo.InvariantCulture);
                    }
                    Append($"{_scene},{starts[a].Faction},{starts[b].Faction}," +
                           $"{straight.ToString("0", CultureInfo.InvariantCulture)},{walk}");
                }
            }
            Append($"{_scene},DONE,,,");
            Debug.Log($"[HomeDistance] {_scene}: {starts.Count} starts measured");
        }

        private static float[] Dijkstra(bool[] open, int w, int h, int2 src, out int[] prev)
        {
            var dist = new float[w * h];
            prev = new int[w * h];
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            int s = src.y * w + src.x;
            dist[s] = 0f;
            var heap = new SortedSet<(float d, int i)>();
            heap.Add((0f, s));
            const float D = 1.41421356f;
            while (heap.Count > 0)
            {
                var top = heap.Min; heap.Remove(top);
                int cx = top.i % w, cy = top.i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (!open[n] && n != s) continue;
                        // no corner cutting
                        if (dx != 0 && dy != 0 && (!open[cy * w + nx] || !open[ny * w + cx])) continue;
                        float nd = top.d + (dx != 0 && dy != 0 ? D : 1f);
                        if (nd >= dist[n]) continue;
                        if (dist[n] < float.MaxValue) heap.Remove((dist[n], n));
                        dist[n] = nd; prev[n] = top.i;
                        heap.Add((nd, n));
                    }
            }
            return dist;
        }

        /// <summary>Path length in cells after string-pulling: from each anchor,
        /// run straight to the furthest later path point with clear ground.</summary>
        private static float Pulled(bool[] open, int w, int h, List<int2> path)
        {
            float len = 0f;
            int anchor = 0;
            while (anchor < path.Count - 1)
            {
                int next = anchor + 1;
                for (int j = anchor + 2; j < path.Count; j++)
                {
                    if (Clear(open, w, h, path[anchor], path[j])) next = j;
                    else if (j - next > 64) break;   // far past the last clear point
                }
                len += math.distance(new float2(path[anchor]), new float2(path[next]));
                anchor = next;
            }
            return len;
        }

        private static bool Clear(bool[] open, int w, int h, int2 a, int2 b)
        {
            float2 pa = new float2(a) + 0.5f, pb = new float2(b) + 0.5f;
            int steps = (int)math.ceil(math.distance(pa, pb) * 4f);
            for (int k = 1; k < steps; k++)
            {
                float2 p = math.lerp(pa, pb, k / (float)steps);
                int x = (int)p.x, y = (int)p.y;
                if (x < 0 || y < 0 || x >= w || y >= h || !open[y * w + x]) return false;
            }
            return true;
        }

        private static void Append(string line)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "home_distances.csv"), line + "\n");
        }
    }
}
