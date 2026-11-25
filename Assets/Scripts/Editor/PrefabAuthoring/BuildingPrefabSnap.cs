// BuildingPrefabSnap — makes hand-composing a building prefab behave as if its
// root were unscaled (2026-11-24).
//
// A building is scaled to its tile at the prefab ROOT (0.4, 0.3921, ...), so in
// Prefab Mode a dropped piece kept world scale 1 and landed at local 2.5, and a
// 0.25 world snap was 0.625 inside the prefab. While a building prefab is open:
//   * the grid and the move snap are 0.25 x the root scale, i.e. 0.25 in the
//     prefab's own units, with grid snapping on and global handles;
//   * a piece dropped in gets its own authored scale back and lands on the grid.
// Leaving Prefab Mode puts the previous snap settings back.
//
//   Waning Border > Art > Snap Selection to 0.25 (building units)   Ctrl+Alt+G
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    [InitializeOnLoad]
    public static class BuildingPrefabSnap
    {
        const float Step = 0.25f;
        const string Key = "TWB.BuildingPrefabSnap.Saved";
        const string MenuPath = "Waning Border/Art/Snap Selection to 0.25 (building units) %&g";

        static BuildingPrefabSnap()
        {
            ObjectChangeEvents.changesPublished += OnChanges;
            EditorApplication.update += Watch;
        }

        // The stage is watched, not listened to: the closing event fires while
        // the old stage is still current, so a restore hung on it can be lost.
        static Vector3 _applied = new Vector3(-1f, -1f, -1f);

        static void Watch()
        {
            var root = Root();
            Vector3 want = root != null ? root.localScale : Vector3.zero;
            if (want == _applied) return;
            _applied = want;
            Refresh();
        }

        /// <summary>The open building prefab's root, or null.</summary>
        static Transform Root()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null) return null;
            string p = stage.assetPath;
            return p.StartsWith("Assets/GameData/TechTree/") && p.Contains("/Buildings/") && !p.Contains("/Units/")
                ? stage.prefabContentsRoot.transform : null;
        }

        static void Refresh()
        {
            var root = Root();
            bool held = SessionState.GetBool(Key, false);
            if (root == null)
            {
                if (!held) return;
                EditorSnapSettings.gridSize = SessionState.GetVector3(Key + ".grid", Vector3.one * Step);
                EditorSnapSettings.move = SessionState.GetVector3(Key + ".move", Vector3.one * Step);
                EditorSnapSettings.gridSnapEnabled = SessionState.GetBool(Key + ".on", false);
                Tools.pivotRotation = (PivotRotation)SessionState.GetInt(Key + ".pivot", (int)PivotRotation.Global);
                SessionState.SetBool(Key, false);
                return;
            }
            if (!held)
            {
                SessionState.SetVector3(Key + ".grid", EditorSnapSettings.gridSize);
                SessionState.SetVector3(Key + ".move", EditorSnapSettings.move);
                SessionState.SetBool(Key + ".on", EditorSnapSettings.gridSnapEnabled);
                SessionState.SetInt(Key + ".pivot", (int)Tools.pivotRotation);
                SessionState.SetBool(Key, true);
            }
            var step = root.localScale * Step;
            EditorSnapSettings.gridSize = step;
            EditorSnapSettings.move = step;
            Tools.pivotRotation = PivotRotation.Global;     // grid snapping needs global handles
            EditorSnapSettings.gridSnapEnabled = true;
            SceneView.RepaintAll();
        }

        static void OnChanges(ref ObjectChangeEventStream stream)
        {
            Transform root = null;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy) continue;
                if (root == null && (root = Root()) == null) return;
                stream.GetCreateGameObjectHierarchyEvent(i, out var e);
                if (EditorUtility.InstanceIDToObject(e.instanceId) is GameObject go
                    && go.transform != root && go.transform.IsChildOf(root))
                    FixNew(root, go.transform);
            }
        }

        static void FixNew(Transform root, Transform t)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            Vector3 own = source != null ? source.transform.localScale : Vector3.one;
            // Only a piece that kept its WORLD scale: a duplicate or a paste
            // already carries the local scale its author gave it.
            if ((t.lossyScale - own).magnitude > 0.01f * own.magnitude) return;
            if ((t.localScale - own).magnitude <= 0.001f * own.magnitude) return;
            Undo.RecordObject(t, "Fit new piece");
            t.localScale = own;
            SnapToGrid(root, t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        static void SnapToGrid(Transform root, Transform t)
        {
            var p = root.InverseTransformPoint(t.position) / Step;
            t.position = root.TransformPoint(new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), Mathf.Round(p.z)) * Step);
            // The round trip through the root scale leaves float dust.
            var l = t.localPosition;
            t.localPosition = new Vector3(Clean(l.x), Clean(l.y), Clean(l.z));
        }

        static float Clean(float v)
        {
            float r = Mathf.Round(v / Step) * Step;
            return Mathf.Abs(r - v) < 0.0005f ? r : v;
        }

        [MenuItem(MenuPath)]
        static void SnapSelection()
        {
            var root = Root();
            if (root == null) return;
            var moving = new List<Transform>();
            foreach (var t in Selection.transforms)
                if (t != root && t.IsChildOf(root)) moving.Add(t);
            foreach (var t in moving)
            {
                Undo.RecordObject(t, "Snap to 0.25");
                SnapToGrid(root, t);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
        }

        [MenuItem(MenuPath, true)]
        static bool CanSnapSelection() => Root() != null && Selection.transforms.Length > 0;
    }
}
