// BuildingArtDiagnostics.cs
// Read-only probe for the imported building art.
//
// The construction rise (BuildingRiseData.ApplyRise) moves each piece along
// its LOCAL Y. That only reads as "rising out of the ground" when the piece's
// parent chain has no rotation — otherwise local +Y points sideways in world
// space and the parts slide in horizontally instead.
//
// Blender's FBX exporter converts Z-up to Y-up by folding a -90 deg X rotation
// into the exported root rather than into vertex data, so this is exactly the
// kind of thing that has to be MEASURED on the imported asset rather than
// assumed from export settings.

#if UNITY_EDITOR

using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Presentation.EditorTools
{
    public static class BuildingArtDiagnostics
    {
        private static readonly string[] Prefabs =
        {
            "Assets/GameData/TechTree/Buildings/Age 0/Hall/Hall.prefab",
            "Assets/GameData/TechTree/Buildings/Age 0/Hut/Hut.prefab",
            "Assets/GameData/TechTree/Buildings/Age 0/Barracks/Barracks.prefab",
            "Assets/GameData/TechTree/Buildings/Age 0/ArcheryRange/ArcheryRange.prefab",
            "Assets/GameData/TechTree/Buildings/Alanthor/RoyalStable/RoyalStable.prefab",
            // Reference point: the hand-authored prefab whose rise is known good.
            "Assets/GameData/TechTree/Buildings/Age 0/GatherersHut/Gatherer'sHut.prefab",
        };

        [MenuItem("Waning Border/Buildings/Diagnose Building Art Orientation")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[BuildingArtDiag] rise axis check — 'localUp' is world up expressed in the piece's parent space.");
            sb.AppendLine("                  localUp must be ~(0,1,0) for ApplyRise to lift pieces vertically.");

            foreach (var path in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  MISSING {path}"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    sb.AppendLine($"  {prefab.name}:");
                    sb.AppendLine($"     root rot={Fmt(go.transform.localEulerAngles)} scale={Fmt(go.transform.localScale)}");

                    var lv0 = FindDeep(go.transform, "Lv0");
                    if (lv0 == null) { sb.AppendLine("     no Lv0!"); continue; }

                    sb.AppendLine($"     Lv0 rot={Fmt(lv0.localEulerAngles)} " +
                                  $"worldRot={Fmt(lv0.rotation.eulerAngles)} children={lv0.childCount}");

                    // Drive the REAL BuildingRiseData, not a re-implementation:
                    // Init() then ApplyRise(0, sink) puts every piece at its
                    // fully-sunk pose. A correct rise moves each piece straight
                    // DOWN by exactly `sink` world units — delta (0,-sink,0).
                    // Any X/Z component is the sideways fly-in; a magnitude
                    // above sink is the unit-scale overshoot.
                    const float sink = 5f;
                    var before = new Vector3[lv0.childCount];
                    for (int i = 0; i < lv0.childCount; i++)
                        before[i] = lv0.GetChild(i).position;

                    var rise = go.AddComponent<BuildingRiseData>();
                    rise.Init();
                    rise.ApplyRise(0f, sink);

                    int shown = 0;
                    for (int i = 0; i < lv0.childCount && shown < 3; i++, shown++)
                    {
                        var c = lv0.GetChild(i);
                        Vector3 d = c.position - before[i];
                        bool vertical = Mathf.Abs(d.x) < 0.01f && Mathf.Abs(d.z) < 0.01f;
                        bool rightDepth = Mathf.Abs(Mathf.Abs(d.y) - sink) < 0.01f;
                        sb.AppendLine($"       {c.name}: delta={Fmt(d)} " +
                                      $"{(vertical ? "VERTICAL" : "*** SIDEWAYS ***")} " +
                                      $"{(rightDepth ? $"depth=ok({sink})" : "*** WRONG DEPTH ***")}");
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }

            Debug.Log(sb.ToString());
        }

        public static void RunBatch()
        {
            Run();
            EditorApplication.Exit(0);
        }

        private static string Fmt(Vector3 v) =>
            $"({v.x:F2},{v.y:F2},{v.z:F2})";

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}

#endif // UNITY_EDITOR
