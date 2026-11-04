// Gives a hand-authored building prefab the footprint marker the Garrison and
// Archery Range carry (2026-11-01, FootprintBounds.cs): a root child named
// "Footprint" with a FootprintBounds component, holding the plinth pieces —
// every direct child that is a Synty castle floor stone standing at the ground
// (local y within 0.05 m of 0, unscaled). The footprint fit then measures the
// slab alone, so stairs or pillars that poke past it neither shrink the
// building nor shift it off its cells.
//   Waning Border > Art > Footprint > Group Plinth: House 1-3
// Re-runnable: a prefab that already has the group is left alone.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class FootprintGroupPass
    {
        const string GroupName = "Footprint";
        const string PlinthPieceName = "SM_Bld_Castle_Floor_Stone_01";
        const float GroundTolerance = 0.05f;

        [MenuItem("Waning Border/Art/Footprint/Group Plinth: House 1-3")]
        public static void GroupHouses()
        {
            for (int l = 1; l <= 3; l++)
                Group("Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House " + l + ".prefab");
        }

        [MenuItem("Waning Border/Art/Footprint/Group Plinth: Tower 1-3")]
        public static void GroupTowers()
        {
            for (int l = 1; l <= 3; l++)
                Group("Assets/GameData/TechTree/Civs/Alanthor/Buildings/Tower/Tower " + l + ".prefab");
        }

        [MenuItem("Waning Border/Art/Footprint/Group Plinth: Fortress 1-3")]
        public static void GroupFortresses()
        {
            for (int l = 1; l <= 3; l++)
                Group("Assets/GameData/TechTree/Civs/Alanthor/Buildings/Fortress/Fortress " + l + ".prefab");
        }

        /// <summary>Removes loose root pieces standing far from the building
        /// (2026-11-02: Barracks_Age_0 carried a ring of house base walls at
        /// x, z = 50..75 m, authoring leftovers, which drew 50 m from every
        /// Age 0 Barracks and framed its icon on an 80 m square).</summary>
        [MenuItem("Waning Border/Art/Footprint/Strip Far Pieces: Barracks_Age_0")]
        public static void StripBarracksAge0()
            => StripFar("Assets/GameData/TechTree/Age0/Buildings/Barracks/Art/Barracks_Age_0.prefab", 30f);

        public static void StripFar(string path, float maxDistance)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogWarning("[FootprintGroupPass] cannot open " + path); return; }
            try
            {
                var far = new List<Transform>();
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var t = root.transform.GetChild(i);
                    var p = t.localPosition; p.y = 0f;
                    if (p.magnitude > maxDistance) far.Add(t);
                }
                foreach (var t in far) Object.DestroyImmediate(t.gameObject);
                if (far.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[FootprintGroupPass] " + path + ": removed " + far.Count + " root pieces farther than " + maxDistance + " m");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void Group(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogWarning("[FootprintGroupPass] cannot open " + path); return; }
            try
            {
                var existing = root.transform.Find(GroupName);
                if (existing != null && existing.GetComponent<TheWaningBorder.Rendering.FootprintBounds>() != null)
                {
                    Debug.Log("[FootprintGroupPass] " + path + " already has a Footprint group (" + existing.childCount + " pieces)");
                    return;
                }

                var slabs = new List<Transform>();
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var t = root.transform.GetChild(i);
                    if (t.name == GroupName) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                    string srcName = src != null ? src.name : t.name;
                    if (!srcName.StartsWith(PlinthPieceName)) continue;
                    if (Mathf.Abs(t.localPosition.y) > GroundTolerance) continue;
                    if ((t.localScale - Vector3.one).sqrMagnitude > 1e-4f) continue;
                    slabs.Add(t);
                }
                if (slabs.Count == 0)
                {
                    Debug.LogWarning("[FootprintGroupPass] " + path + ": no ground-level " + PlinthPieceName + " pieces at the root; nothing grouped");
                    return;
                }

                var group = existing != null ? existing.gameObject : new GameObject(GroupName);
                group.transform.SetParent(root.transform, false);
                group.transform.localPosition = Vector3.zero;
                group.transform.localRotation = Quaternion.identity;
                group.transform.localScale = Vector3.one;
                group.transform.SetSiblingIndex(0);
                if (group.GetComponent<TheWaningBorder.Rendering.FootprintBounds>() == null)
                    group.AddComponent<TheWaningBorder.Rendering.FootprintBounds>();

                Bounds b = default; bool any = false;
                foreach (var s in slabs)
                {
                    s.SetParent(group.transform, true);
                    foreach (var r in s.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[FootprintGroupPass] " + path + ": " + slabs.Count + " plinth pieces under Footprint; slab "
                          + b.size.x.ToString("F2") + " x " + b.size.z.ToString("F2") + " m, centre ("
                          + b.center.x.ToString("F2") + ", " + b.center.z.ToString("F2") + ")");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
