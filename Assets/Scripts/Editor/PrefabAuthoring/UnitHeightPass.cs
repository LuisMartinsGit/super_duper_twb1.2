// UnitHeightPass.cs
// ONE BODY SIZE FOR EVERY UNIT ON FOOT, THE SPEARMAN'S (developer, 2026-11-14:
// "apply the same scale to all units. Reference is spearman"). The Spearman's
// body (the top of its skinned meshes, weapons left out - the measure
// LegionarySpearmanAuthor uses to match him to the Worker) is the reference;
// every other unit prefab with a body takes the ROOT scale that makes its body
// as tall. A mounted unit is matched by its rider's torso (hips to head) when
// the rider is a humanoid rig, so the mount scales with the man. Engines,
// wagons and prefabs that are only a stub for a code-built visual have no
// body and are left alone (reported).
//
// Root scale only - the same place the Spearman's own factor lives. How large
// units are DRAWN is still UnitWorldScale.asset, for every unit at once.
//   Waning Border > Art > Scale > Units: Body Heights - Measure
//   Waning Border > Art > Scale > Units: Body Heights - Match The Spearman
// Report: Temp/unit-heights.txt

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class UnitHeightPass
    {
        const string Report = "Temp/unit-heights.txt";
        const string Reference = "Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Spearman/Spearman.prefab";

        [MenuItem("Waning Border/Art/Scale/Units: Body Heights - Measure")]
        public static void MeasureOnly() => Run(false);

        [MenuItem("Waning Border/Art/Scale/Units: Body Heights - Match The Spearman")]
        public static void Apply() => Run(true);

        struct Body { public float top, torso; public bool mounted; }

        static Body MeasureBody(GameObject go)
        {
            var b = new Body();
            float y0 = go.transform.position.y;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                b.top = Mathf.Max(b.top, smr.bounds.max.y - y0);
            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
            {
                if (anim.avatar == null || !anim.avatar.isHuman) continue;
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (hips != null && head != null) b.torso = Mathf.Max(b.torso, head.position.y - hips.position.y);
            }
            return b;
        }

        static void Run(bool apply)
        {
            var sb = new StringBuilder();
            var refAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Reference);
            if (refAsset == null) { Debug.LogError("[UnitHeight] no reference prefab " + Reference); return; }
            var refGo = (GameObject)Object.Instantiate(refAsset);
            refGo.transform.position = Vector3.zero;
            Body reference = MeasureBody(refGo);
            Object.DestroyImmediate(refGo);
            sb.AppendLine($"REFERENCE Spearman: body {reference.top:F3} m, torso {reference.torso:F3} m (at its own root scale {refAsset.transform.localScale.x:F3})");

            // Every prefab a unit SO draws, with the units that use it.
            var users = new Dictionary<string, List<UnitDefSO>>();
            foreach (var guid in AssetDatabase.FindAssets("t:UnitDefSO", new[] { "Assets/GameData/TechTree" }))
            {
                var so = AssetDatabase.LoadAssetAtPath<UnitDefSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so == null) continue;
                var prefab = typeof(UnitDefSO).GetField("prefab")?.GetValue(so) as GameObject;
                if (prefab == null) { sb.AppendLine($"  no prefab     {so.id} ({so.unitClass}): code-built visual"); continue; }
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!users.TryGetValue(path, out var list)) users[path] = list = new List<UnitDefSO>();
                list.Add(so);
            }

            foreach (var kv in users.OrderBy(k => k.Key))
            {
                string path = kv.Key;
                string who = string.Join(", ", kv.Value.Select(s => s.id));
                bool mounted = kv.Value.Any(s => s.unitClass != null && s.unitClass.Contains("cavalry"));
                bool vehicle = kv.Value.All(s => s.unitClass != null
                    && (s.unitClass.Contains("siege") || s.unitClass.Contains("trade") || s.unitClass == "economy"));
                string shortPath = path.StartsWith("Assets/GameData/TechTree/") ? path.Substring("Assets/GameData/TechTree/".Length) : path;
                if (path == Reference) { sb.AppendLine($"  reference     {shortPath}"); continue; }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    float was = root.transform.localScale.x;
                    root.transform.localScale = Vector3.one;
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Body body = MeasureBody(root);
                    float k;
                    string how;
                    if (vehicle) { sb.AppendLine($"  engine/wagon  {shortPath}  root {was:F3}  [{who}]"); continue; }
                    if (mounted)
                    {
                        if (body.torso <= 0.05f || reference.torso <= 0.05f)
                        { sb.AppendLine($"  mounted, no humanoid rider  {shortPath}  root {was:F3}, body top {body.top:F2} m  [{who}]"); continue; }
                        k = reference.torso / body.torso; how = $"rider torso {body.torso:F3} m";
                    }
                    else
                    {
                        if (body.top <= 0.05f)
                        { sb.AppendLine($"  no body (stub) {shortPath}  root {was:F3}  [{who}]"); continue; }
                        // Not a man: the Ledger is a knee-high pentapod.
                        if (body.top < 1.2f)
                        { sb.AppendLine($"  not a man     {shortPath}  root {was:F3}, body {body.top:F2} m  [{who}]"); continue; }
                        k = reference.top / body.top; how = $"body {body.top:F3} m";
                    }
                    sb.AppendLine($"  {(apply ? "SET " : "needs")} {k:F3} (was {was:F3})  {how} at scale 1 -> {(mounted ? body.torso : body.top) * k:F3} m  {shortPath}  [{who}]");
                    if (apply && Mathf.Abs(was - k) > 0.0005f)
                    {
                        root.transform.localScale = Vector3.one * k;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (apply) AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, sb.ToString());
            Debug.Log("[UnitHeight]\n" + sb);
        }
    }
}
