// ProceduralMapGenerator.cs
// Generates a whole map from region kinds.
//
//   1. empty terrain
//   2. Voronoi regions seeded over it
//   3. a river, if the archetype has one, carved as its OWN regions
//   4. kinds assigned by archetype, honouring that archetype's adjacency rules
//   5. connectivity guaranteed between every player start (islands excepted);
//      where a river is the only thing in the way, a BridgeSiteMarker is placed
//   6. RegionEnvironmentGenerator turns all of that into terrain
//
// The archetype is the only creative input: it decides what mix of kinds the
// map gets and which kinds may touch. Everything after that is consequence.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.World.MapMarkers;
using Kind = TheWaningBorder.World.MapMarkers.RegionSeedMarker.RegionKind;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public enum MapArchetype
    {
        /// <summary>Land clusters in open water. The one archetype with no land route.</summary>
        Islands,
        /// <summary>Water along one edge, mountains inland.</summary>
        Coastal,
        /// <summary>Rolling mountain stands, no water at all.</summary>
        Hills,
        /// <summary>Mountain ridges with passable corridors between them.</summary>
        Valleys,
        /// <summary>A river cutting the map; starts on both banks, bridges between.</summary>
        RiverCrossing,
    }

    public sealed class ProceduralMapGenerator : EditorWindow
    {
        #region Window

        MapArchetype _archetype = MapArchetype.Coastal;
        int _players = 4;
        int _regions = 25;
        int _size = 512;
        int _seed = 12345;
        bool _runEnvironment = true;

        [MenuItem("Waning Border/Maps/Generate Procedural Map…")]
        static void Open() => GetWindow<ProceduralMapGenerator>("Procedural Map").minSize
            = new Vector2(340f, 260f);

        void OnGUI()
        {
            EditorGUILayout.LabelField("Procedural map", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds terrain, seeds regions, assigns kinds by archetype and "
              + "guarantees the starts can reach each other. Runs on the OPEN scene "
              + "and overwrites it.", MessageType.Warning);

            _archetype = (MapArchetype)EditorGUILayout.EnumPopup("Archetype", _archetype);
            _players = EditorGUILayout.IntSlider("Players", _players, 2, 8);
            _regions = EditorGUILayout.IntSlider("Regions", _regions, _players + 4, 60);
            _size = EditorGUILayout.IntPopup("Size (m)", _size,
                new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
            _seed = EditorGUILayout.IntField("Seed", _seed);
            _runEnvironment = EditorGUILayout.Toggle("Run environment pass", _runEnvironment);

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate", GUILayout.Height(32f)))
                Generate(_archetype, _players, _regions, _size, _seed, _runEnvironment);
        }

        #endregion

        #region Archetype rules

        /// <summary>
        /// What a kind may sit next to. A pair missing from here is allowed;
        /// the table only records the combinations that read badly or break the
        /// map, so the rules stay legible.
        /// </summary>
        static bool Forbidden(MapArchetype a, Kind x, Kind y)
        {
            // A start hemmed in by its neighbours has no opening. Enforced for
            // every archetype, including Islands, where the island itself must
            // still hold more than the Hall.
            if (x == Kind.PlayerStart && y == Kind.PlayerStart) return true;

            switch (a)
            {
                case MapArchetype.Coastal:
                    // Cliffs straight into the sea leave no beach to land on.
                    return Pair(x, y, Kind.Water, Kind.Mountain);

                case MapArchetype.Valleys:
                    // A valley is corridors BETWEEN ridges; ridge-on-ridge fills it in.
                    return Pair(x, y, Kind.Mountain, Kind.Mountain);

                case MapArchetype.RiverCrossing:
                    // The banks must be walkable, or the bridge lands on a cliff.
                    return Pair(x, y, Kind.Water, Kind.Mountain);

                default:
                    return false;
            }
        }

        static bool Pair(Kind x, Kind y, Kind a, Kind b) =>
            (x == a && y == b) || (x == b && y == a);

        /// <summary>The mix a given archetype draws its non-start regions from.</summary>
        static Kind[] Palette(MapArchetype a) => a switch
        {
            MapArchetype.Islands => new[]
            {
                Kind.Water, Kind.Water, Kind.Water, Kind.Water,
                Kind.Normal, Kind.Normal, Kind.Forest,
            },
            MapArchetype.Coastal => new[]
            {
                Kind.Water, Kind.Water,
                Kind.Normal, Kind.Normal, Kind.Normal,
                Kind.Forest, Kind.Mountain,
            },
            MapArchetype.Hills => new[]
            {
                Kind.Normal, Kind.Normal, Kind.Normal,
                Kind.Forest, Kind.Forest,
                Kind.Mountain, Kind.Mountain, Kind.Obstacle,
            },
            MapArchetype.Valleys => new[]
            {
                Kind.Normal, Kind.Normal,
                Kind.Mountain, Kind.Mountain, Kind.Mountain,
                Kind.Forest,
            },
            _ => new[]
            {
                Kind.Normal, Kind.Normal, Kind.Normal,
                Kind.Forest, Kind.Forest, Kind.Mountain,
            },
        };

        static bool HasRiver(MapArchetype a) => a == MapArchetype.RiverCrossing;

        /// <summary>Islands are the one archetype that must NOT be walkable end to end.</summary>
        static bool NeedsLandRoute(MapArchetype a) => a != MapArchetype.Islands;

        #endregion

        #region Pipeline

        static void Generate(MapArchetype archetype, int players, int regionCount,
                             int size, int seed, bool runEnvironment)
        {
            Random.InitState(seed);
            _bridged.Clear();

            // 1 — empty terrain.
            var terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                Debug.LogError("[ProcGen] the open scene has no Terrain. Build one first "
                             + "(Waning Border > Maps > Build …), then run this on it.");
                return;
            }
            Flatten(terrain);

            Vector2 min = new Vector2(terrain.transform.position.x, terrain.transform.position.z);
            Vector2 max = min + new Vector2(terrain.terrainData.size.x, terrain.terrainData.size.z);

            // 2 — Voronoi seeds. Player starts first so they are spread furthest.
            var points = SeedPoints(min, max, players, regionCount);

            // 3 — the river, as its own chain of regions.
            var river = HasRiver(archetype) ? RiverChain(points, players, min, max) : new HashSet<int>();

            // 4 — kinds, honouring the archetype's adjacency rules.
            var kinds = AssignKinds(archetype, points, players, river);

            // 5 — connectivity.
            var adjacency = Adjacency(points, min, max);
            var bridges = new List<(int a, int b, Vector2 at)>();
            if (NeedsLandRoute(archetype))
                Connect(archetype, points, kinds, adjacency, players, river, bridges);

            // Commit: markers in the scene.
            var root = Rebuild("~ProceduralRegions");
            var markers = new RegionSeedMarker[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                var go = new GameObject($"Region_{i}_{kinds[i]}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(points[i].x, 0f, points[i].y);

                var m = go.AddComponent<RegionSeedMarker>();
                m.RegionName = $"{kinds[i]} {i}";
                m.Kind = kinds[i];
                m.Shape = Outline(points, i, min, max);
                markers[i] = m;

                if (kinds[i] == Kind.PlayerStart)
                {
                    var start = new GameObject($"PlayerStart_{i}");
                    start.transform.SetParent(root.transform, false);
                    start.transform.position = go.transform.position;
                    start.AddComponent<PlayerStartMarker>();
                }
            }

            foreach (var (a, b, at) in bridges)
            {
                var go = new GameObject($"BridgeSite_{a}_{b}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(at.x, 0f, at.y);

                var bm = go.AddComponent<BridgeSiteMarker>();
                bm.RegionA = a;
                bm.RegionB = b;
                bm.Yaw = Mathf.Atan2(points[b].y - points[a].y, points[b].x - points[a].x)
                       * Mathf.Rad2Deg;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            Debug.Log($"[ProcGen] {archetype}: {points.Count} regions, {players} starts, "
                    + $"{river.Count} river region(s), {bridges.Count} bridge site(s).");

            // 6 — hand it to the environment pass.
            if (runEnvironment) RegionEnvironmentGenerator.Generate();
        }

        static void Flatten(Terrain t)
        {
            int res = t.terrainData.heightmapResolution;
            var h = new float[res, res];
            // A little above zero: the environment pass CUTS water down, and it
            // needs somewhere to cut from.
            float baseline = 0.25f;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++) h[y, x] = baseline;
            t.terrainData.SetHeights(0, 0, h);
        }

        static GameObject Rebuild(string name)
        {
            var existing = GameObject.Find(name);
            if (existing != null) Object.DestroyImmediate(existing);
            return new GameObject(name);
        }

        #endregion

        #region Seeding

        /// <summary>
        /// Player starts first, spread as far apart as the map allows, then the
        /// rest by farthest-point sampling — the same guarantee RegionSeeder
        /// gives a hand-authored map.
        /// </summary>
        static List<Vector2> SeedPoints(Vector2 min, Vector2 max, int players, int total)
        {
            var pts = new List<Vector2>(total);
            Vector2 c = (min + max) * 0.5f;
            float r = Mathf.Min(max.x - min.x, max.y - min.y) * 0.36f;

            for (int i = 0; i < players; i++)
            {
                float a = i / (float)players * Mathf.PI * 2f;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }

            while (pts.Count < total)
            {
                Vector2 best = default;
                float bestD = -1f;

                for (int attempt = 0; attempt < 64; attempt++)
                {
                    var p = new Vector2(Random.Range(min.x, max.x), Random.Range(min.y, max.y));
                    float d = float.MaxValue;
                    foreach (var q in pts) d = Mathf.Min(d, (p - q).sqrMagnitude);
                    if (d > bestD) { bestD = d; best = p; }
                }
                pts.Add(best);
            }
            return pts;
        }

        /// <summary>
        /// A river: the regions nearest a line across the map, avoiding the
        /// player starts so nobody begins in the water.
        /// </summary>
        static HashSet<int> RiverChain(List<Vector2> pts, int players, Vector2 min, Vector2 max)
        {
            var river = new HashSet<int>();

            // A wandering line from one edge to the opposite one.
            bool vertical = Random.value < 0.5f;
            float t0 = Random.Range(0.35f, 0.65f);

            for (int i = players; i < pts.Count; i++)
            {
                float u = vertical
                    ? Mathf.InverseLerp(min.y, max.y, pts[i].y)
                    : Mathf.InverseLerp(min.x, max.x, pts[i].x);

                float centre = t0 + Mathf.Sin(u * Mathf.PI * 1.5f) * 0.08f;
                float across = vertical
                    ? Mathf.InverseLerp(min.x, max.x, pts[i].x)
                    : Mathf.InverseLerp(min.y, max.y, pts[i].y);

                if (Mathf.Abs(across - centre) < 0.07f) river.Add(i);
            }
            return river;
        }

        #endregion

        #region Kinds

        static Kind[] AssignKinds(MapArchetype archetype, List<Vector2> pts,
                                  int players, HashSet<int> river)
        {
            var kinds = new Kind[pts.Count];
            var palette = Palette(archetype);

            for (int i = 0; i < pts.Count; i++)
            {
                if (i < players) { kinds[i] = Kind.PlayerStart; continue; }
                kinds[i] = river.Contains(i) ? Kind.Water : palette[Random.Range(0, palette.Length)];
            }

            // Repair forbidden neighbours by demoting the offender to Normal —
            // the one kind no rule forbids.
            var adjacency = AdjacencyByProximity(pts);
            for (int pass = 0; pass < 4; pass++)
            {
                bool clean = true;

                for (int i = 0; i < kinds.Length; i++)
                foreach (int j in adjacency[i])
                {
                    if (!Forbidden(archetype, kinds[i], kinds[j])) continue;

                    // Never demote a start or a river: they are the map's premise.
                    int victim = kinds[j] == Kind.PlayerStart || river.Contains(j) ? i : j;
                    if (kinds[victim] == Kind.PlayerStart || river.Contains(victim)) continue;

                    kinds[victim] = Kind.Normal;
                    clean = false;
                }

                if (clean) break;
            }

            // Every start needs at least one passable neighbour to walk out into.
            for (int i = 0; i < players; i++)
            {
                bool anyOpen = false;
                foreach (int j in adjacency[i]) if (Passable(kinds[j])) { anyOpen = true; break; }

                if (anyOpen) continue;
                foreach (int j in adjacency[i])
                    if (!river.Contains(j)) { kinds[j] = Kind.Normal; break; }
            }

            return kinds;
        }

        static bool Passable(Kind k) =>
            k == Kind.Normal || k == Kind.PlayerStart || k == Kind.Forest;

        #endregion

        #region Adjacency and connectivity

        /// <summary>
        /// Which regions touch, found by sampling the Voronoi partition — two
        /// regions are neighbours if they own adjacent samples. More honest than
        /// a distance threshold, which calls regions neighbours across a third.
        /// </summary>
        static List<int>[] Adjacency(List<Vector2> pts, Vector2 min, Vector2 max)
        {
            const int Res = 128;
            var sets = new HashSet<int>[pts.Count];
            for (int i = 0; i < sets.Length; i++) sets[i] = new HashSet<int>();

            var cell = new int[Res * Res];
            Vector2 span = max - min;

            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                var p = new Vector2(min.x + (x + 0.5f) / Res * span.x,
                                    min.y + (y + 0.5f) / Res * span.y);
                cell[y * Res + x] = Nearest(pts, p);
            }

            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                int a = cell[y * Res + x];
                if (x + 1 < Res) Link(sets, a, cell[y * Res + x + 1]);
                if (y + 1 < Res) Link(sets, a, cell[(y + 1) * Res + x]);
            }

            var result = new List<int>[pts.Count];
            for (int i = 0; i < pts.Count; i++) result[i] = new List<int>(sets[i]);
            return result;
        }

        static void Link(HashSet<int>[] sets, int a, int b)
        {
            if (a == b) return;
            sets[a].Add(b);
            sets[b].Add(a);
        }

        /// <summary>Cheap neighbour guess used while kinds are still being settled.</summary>
        static List<int>[] AdjacencyByProximity(List<Vector2> pts)
        {
            var result = new List<int>[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                var near = new List<(int idx, float d)>();
                for (int j = 0; j < pts.Count; j++)
                    if (j != i) near.Add((j, (pts[i] - pts[j]).sqrMagnitude));

                near.Sort((l, r) => l.d.CompareTo(r.d));
                result[i] = new List<int>();
                for (int k = 0; k < Mathf.Min(6, near.Count); k++) result[i].Add(near[k].idx);
            }
            return result;
        }

        /// <summary>
        /// Walk out from the first start across passable regions. Anything not
        /// reached is opened up: if a RIVER region is what separates it, the
        /// river stays and a bridge site is recorded instead — a river you can
        /// ford everywhere is not a river.
        /// </summary>
        static void Connect(MapArchetype archetype, List<Vector2> pts, Kind[] kinds,
                            List<int>[] adjacency, int players, HashSet<int> river,
                            List<(int, int, Vector2)> bridges)
        {
            for (int guard = 0; guard < 24; guard++)
            {
                var seen = Reachable(kinds, adjacency, 0);

                int stranded = -1;
                for (int i = 0; i < players; i++) if (!seen.Contains(i)) { stranded = i; break; }
                if (stranded < 0) return;

                // The blocker on the shortest hop from the reached set toward it.
                if (!NearestBlocker(kinds, adjacency, seen, stranded, out int blocker, out int from))
                {
                    Debug.LogWarning($"[ProcGen] start {stranded} cannot be connected — "
                                   + "the archetype's rules left no route.");
                    return;
                }

                if (river.Contains(blocker))
                {
                    // A river you can ford everywhere is not a river: the water
                    // STAYS, and a bridge site is recorded instead. The search
                    // then treats that one region as crossable.
                    bridges.Add((from, blocker, (pts[from] + pts[blocker]) * 0.5f));
                    _bridged.Add(blocker);
                }
                else
                {
                    kinds[blocker] = Kind.Normal;
                }
            }
        }

        /// <summary>
        /// River regions a bridge now spans. Walkable to the connectivity search
        /// only — they stay Water in the world. Cleared per run, or one map's
        /// bridges would silently satisfy the next map's connectivity.
        /// </summary>
        static readonly HashSet<int> _bridged = new();

        static HashSet<int> Reachable(Kind[] kinds, List<int>[] adjacency, int start)
        {
            var seen = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                foreach (int n in adjacency[queue.Dequeue()])
                {
                    if (seen.Contains(n)) continue;
                    if (!Passable(kinds[n]) && !_bridged.Contains(n)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        static bool NearestBlocker(Kind[] kinds, List<int>[] adjacency, HashSet<int> seen,
                                   int target, out int blocker, out int from)
        {
            blocker = from = -1;

            foreach (int s in seen)
            foreach (int n in adjacency[s])
            {
                if (seen.Contains(n)) continue;
                if (kinds[n] == Kind.PlayerStart) continue;
                blocker = n;
                from = s;
                return true;
            }
            return false;
        }

        #endregion

        #region Outlines

        /// <summary>
        /// A polygon for a region: rays out from its seed, stopping where a
        /// neighbouring seed takes over, clamped to the map. Same shape the
        /// hand-authoring editor generates, so both routes produce the same
        /// kind of data.
        /// </summary>
        static Vector2[] Outline(List<Vector2> pts, int index, Vector2 min, Vector2 max)
        {
            const int Rays = 20;
            const float Step = 2f;

            float reach = Mathf.Max(max.x - min.x, max.y - min.y);
            var poly = new Vector2[Rays];
            Vector2 origin = pts[index];

            for (int i = 0; i < Rays; i++)
            {
                float a = i / (float)Rays * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                float last = Step;
                for (float d = Step; d <= reach; d += Step)
                {
                    var p = origin + dir * d;
                    if (p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y) break;
                    if (Nearest(pts, p) != index) break;
                    last = d;
                }

                var edge = origin + dir * last;
                poly[i] = new Vector2(Mathf.Clamp(edge.x, min.x, max.x),
                                      Mathf.Clamp(edge.y, min.y, max.y));
            }
            return poly;
        }

        static int Nearest(List<Vector2> pts, Vector2 p)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = (p - pts[i]).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        #endregion
    }
}
