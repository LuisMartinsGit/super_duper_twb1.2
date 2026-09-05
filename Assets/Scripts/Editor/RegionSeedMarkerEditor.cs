// RegionSeedMarkerEditor.cs
// Draw a territory's border by hand in the scene view.
//
// Regions.md §3b: territories have FIXED shapes and ownership is the only
// variable. Before this the shape was whatever nearest-seed produced, so the
// only authoring gesture available was nudging a seed and seeing what happened
// to every neighbouring territory at the same time.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.World.MapMarkers;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    /// <summary>
    /// Scene handles for <see cref="RegionSeedMarker.Shape"/>.
    /// </summary>
    [CustomEditor(typeof(RegionSeedMarker))]
    public sealed class RegionSeedMarkerEditor : UnityEditor.Editor
    {
        /// <summary>Rays cast outward when seeding a shape from the Voronoi cell.</summary>
        const int SeedRays = 24;

        /// <summary>Step along each ray, metres. Half a build cell.</summary>
        const float RayStep = 1f;

        const float RayMax = 600f;

        static readonly Color LineColor = new Color(0.95f, 0.80f, 0.35f, 1f);
        static readonly Color FillColor = new Color(0.95f, 0.80f, 0.35f, 0.08f);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var marker = (RegionSeedMarker)target;
            int count = marker.Shape != null ? marker.Shape.Length : 0;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Territory shape", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                count == 0
                    ? "No shape authored — this territory is still the nearest-seed (Voronoi) "
                    + "cell. Generate a starting shape, then drag the handles in the scene."
                    : $"{count} points. Drag the handles in the scene view; click a midpoint "
                    + "dot to insert, shift-click a point to delete.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(count == 0 ? "Generate From Voronoi Cell" : "Re-generate"))
                    Generate(marker);

                using (new EditorGUI.DisabledScope(count == 0))
                    if (GUILayout.Button("Clear Shape"))
                    {
                        Undo.RecordObject(marker, "Clear Territory Shape");
                        marker.Shape = new Vector2[0];
                        EditorUtility.SetDirty(marker);
                    }
            }
        }

        void OnSceneGUI()
        {
            var marker = (RegionSeedMarker)target;
            var shape = marker.Shape;
            if (shape == null || shape.Length < 2) return;

            var world = new Vector3[shape.Length];
            for (int i = 0; i < shape.Length; i++) world[i] = Lift(shape[i]);

            Handles.color = FillColor;
            Handles.DrawAAConvexPolygon(world);
            Handles.color = LineColor;
            for (int i = 0, j = shape.Length - 1; i < shape.Length; j = i++)
                Handles.DrawAAPolyLine(3f, world[j], world[i]);

            MoveHandles(marker, shape, world);
            MidpointInserts(marker, shape, world);
        }

        #region Handles

        void MoveHandles(RegionSeedMarker marker, Vector2[] shape, Vector3[] world)
        {
            for (int i = 0; i < shape.Length; i++)
            {
                float size = HandleUtility.GetHandleSize(world[i]) * 0.06f;

                // Shift-click deletes, but never below a triangle: two points
                // is not a territory, and RegionMap treats <3 as unauthored.
                if (Event.current.shift && shape.Length > 3)
                {
                    Handles.color = Color.red;
                    if (Handles.Button(world[i], Quaternion.identity, size, size, Handles.DotHandleCap))
                    {
                        Undo.RecordObject(marker, "Delete Shape Point");
                        var list = new List<Vector2>(shape);
                        list.RemoveAt(i);
                        marker.Shape = list.ToArray();
                        EditorUtility.SetDirty(marker);
                        return;
                    }
                    continue;
                }

                Handles.color = LineColor;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(
                    world[i], size * 1.6f, Vector3.zero, Handles.DotHandleCap);

                if (!EditorGUI.EndChangeCheck()) continue;

                Undo.RecordObject(marker, "Move Shape Point");
                marker.Shape[i] = Snap(new Vector2(moved.x, moved.z));
                EditorUtility.SetDirty(marker);
            }
        }

        void MidpointInserts(RegionSeedMarker marker, Vector2[] shape, Vector3[] world)
        {
            if (Event.current.shift) return;

            for (int i = 0, j = shape.Length - 1; i < shape.Length; j = i++)
            {
                Vector3 mid = (world[j] + world[i]) * 0.5f;
                float size = HandleUtility.GetHandleSize(mid) * 0.04f;

                Handles.color = new Color(1f, 1f, 1f, 0.55f);
                if (!Handles.Button(mid, Quaternion.identity, size, size, Handles.DotHandleCap))
                    continue;

                Undo.RecordObject(marker, "Insert Shape Point");
                var list = new List<Vector2>(shape);
                list.Insert(i, Snap(new Vector2(mid.x, mid.z)));
                marker.Shape = list.ToArray();
                EditorUtility.SetDirty(marker);
                return;
            }
        }

        /// <summary>Points snap to the build grid, so borders line up with what can be built on them.</summary>
        static Vector2 Snap(Vector2 p)
        {
            float c = BuildGrid.CellSize;
            return new Vector2(Mathf.Round(p.x / c) * c, Mathf.Round(p.y / c) * c);
        }

        /// <summary>Put a point on the terrain, lifted clear of z-fighting.</summary>
        static Vector3 Lift(Vector2 p)
        {
            float y = 0f;
            var t = UnityEngine.Terrain.activeTerrain;
            if (t != null) y = t.SampleHeight(new Vector3(p.x, 0f, p.y)) + t.transform.position.y;
            return new Vector3(p.x, y + 0.5f, p.y);
        }

        #endregion

        #region Generate

        /// <summary>
        /// A starting polygon traced from the nearest-seed cell this marker owns
        /// today: cast rays out from the seed and stop where another seed takes
        /// over. Deliberately the RAW cell — no warp, no claimability — because
        /// this is a first draft to drag into shape, not a faithful copy of a
        /// partition that is about to stop being the border.
        /// </summary>
        static void Generate(RegionSeedMarker marker)
        {
            var all = Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None);
            var seeds = new List<Vector2>(all.Length);
            int self = -1;

            foreach (var m in all)
            {
                if (m == marker) self = seeds.Count;
                var p = m.transform.position;
                seeds.Add(new Vector2(p.x, p.z));
            }

            if (self < 0 || seeds.Count == 0) return;

            Vector2 origin = seeds[self];
            var pts = new Vector2[SeedRays];

            for (int i = 0; i < SeedRays; i++)
            {
                float a = i / (float)SeedRays * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                float last = RayStep;
                for (float d = RayStep; d <= RayMax; d += RayStep)
                {
                    if (Nearest(seeds, origin + dir * d) != self) break;
                    last = d;
                }
                pts[i] = Snap(origin + dir * last);
            }

            Undo.RecordObject(marker, "Generate Territory Shape");
            marker.Shape = pts;
            EditorUtility.SetDirty(marker);
        }

        static int Nearest(List<Vector2> seeds, Vector2 p)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < seeds.Count; i++)
            {
                float d = (p - seeds[i]).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        #endregion
    }
}
