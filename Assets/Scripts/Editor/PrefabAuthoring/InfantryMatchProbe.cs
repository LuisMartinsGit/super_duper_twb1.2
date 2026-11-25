// InfantryMatchProbe.cs
// Play mode: every gear state of both Garrison units lined up beside the Blue
// capital (views only, no entities), photographed in the match's own light and
// grading (Temp/infantry/match.png) - the place to judge how the gold shines.
//   Waning Border > Debug > Infantry Probe: Line Up In Match

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.EditorTools
{
    public static class InfantryMatchProbe
    {
        [MenuItem("Waning Border/Debug/Infantry Probe: Line Up In Match")]
        public static void Run()
        {
            AlanthorArtProbe.CameraBlue();
            var main = Camera.main;
            if (!Application.isPlaying || main == null) return;
            // The ground the camera looks at, and the way it looks.
            Vector3 fwd = Vector3.ProjectOnPlane(main.transform.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
            var old = GameObject.Find("InfantryLineUp");
            if (old != null) Object.DestroyImmediate(old);
            var stage = new GameObject("InfantryLineUp");
            Vector3 at = main.transform.position + main.transform.forward * (main.transform.position.y / Mathf.Max(0.2f, -main.transform.forward.y)) - fwd * 17f;
            var terrain = Terrain.activeTerrain;
            (string prefab, bool aged, int level, int weapon)[] line =
            {
                ("Age0/Buildings/Barracks/Units/Spearman/Spearman", false, 1, 0),
                ("Age0/Buildings/Barracks/Units/Spearman/Spearman", true, 1, 1),
                ("Age0/Buildings/Barracks/Units/Spearman/Spearman", true, 2, 2),
                ("Age0/Buildings/Barracks/Units/Spearman/Spearman", true, 3, 3),
                ("Age0/Buildings/Barracks/Units/Spearman/Spearman", true, 3, 4),
                ("Civs/Alanthor/Buildings/Garrison/Units/Swordsman/Swordsman", true, 1, 1),
                ("Civs/Alanthor/Buildings/Garrison/Units/Swordsman/Swordsman", true, 2, 2),
                ("Civs/Alanthor/Buildings/Garrison/Units/Swordsman/Swordsman", true, 3, 3),
                ("Civs/Alanthor/Buildings/Garrison/Units/Swordsman/Swordsman", true, 3, 4),
            };
            var all = new System.Collections.Generic.List<(string prefab, bool aged, int level, int weapon)>(line);
            const string Range = "Civs/Alanthor/Buildings/ArcheryRange/Units/";
            for (int l = 1; l <= 3; l++) all.Add((Range + "Archer/Archer", true, l, l - 1));
            all.Add((Range + "Crossbowman/Crossbowman", true, 2, 2));
            all.Add((Range + "Crossbowman/Crossbowman", true, 3, 3));
            all.Add((Range + "Longbowman/Longbowman", true, 3, 3));
            all.Add((line[4].prefab, true, 3, 4));
            int normal = all.Count;
            foreach (var p in InfantryKit.ElitePrefabs())
                all.Add((p.Replace("Assets/GameData/TechTree/", "").Replace(".prefab", ""), true, 3, 3));
            foreach (var p in new[] { "Age0/Buildings/Fortress/Units/Worker/Worker", "Age0/Buildings/Fortress/Units/Scout/Scout", "Age0/Buildings/TempleOfRidan/Units/Litharch/Litharch" })
                all.Insert(0, (p, true, 1, 0));
            normal += 3;
            line = all.ToArray();
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < line.Length; i++)
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameData/TechTree/" + line[i].prefab + ".prefab"), stage.transform);
                Vector3 p = at + right * ((i % 8 - 3.5f) * 1.5f) - fwd * (i / 8 * 2.6f);
                if (terrain != null) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-fwd));
                go.transform.localScale *= UnitWorldScale.Value * (i >= normal ? UnitWorldScale.EliteValue : 1f);
                go.GetComponent<UnitAgeKit>().Apply(line[i].aged, line[i].level, line[i].weapon, 0, instant: true, quiver: line[i].weapon);
                go.GetComponent<UnitPlayerColor>().Apply(FactionColors.Get(Faction.Blue));
                mid += p / line.Length;
            }

            var camGo = new GameObject("InfantryCam");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(main);
            var data = cam.GetUniversalAdditionalCameraData();
            var src = main.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = src.renderPostProcessing;
            data.volumeLayerMask = src.volumeLayerMask;
            data.volumeTrigger = src.volumeTrigger;
            cam.fieldOfView = 34f;
            cam.transform.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(32f, 0f, 0f);
            cam.transform.position = mid + Vector3.up * 0.5f - cam.transform.forward * 13f;
            cam.nearClipPlane = 0.1f;
            var rt = new RenderTexture(2200, 1400, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory("Temp/infantry");
            File.WriteAllBytes("Temp/infantry/match.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
        }

        static readonly string[] SpawnIds =
        {
            "Worker", "Scout", "Litharch", "Spearman", "Alanthor_Archer", "Alanthor_Crossbowman", "Alanthor_Longbowman", "Sect_ScarGuard", "Sect_Surgeon", "Sect_Sallyguard", "Sect_Warden",
            "Sect_Collector", "Sect_Guildmaster", "Sect_Artificer", "Sect_ArchivistOfHours", "KingLexor", "Ledger",
        };

        /// <summary>Spawns one real unit of each kind by the Blue capital; run again to log
        /// each view's scale and animator and photograph them (Temp/infantry/spawned.*).</summary>
        [MenuItem("Waning Border/Debug/Infantry Probe: Spawn And Report")]
        public static void SpawnReport()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            AlanthorArtProbe.CameraBlue();
            if (_spawned.Count == 0)
            {
                var m = Camera.main.transform;
                Vector3 at = m.position + m.forward * (m.position.y / Mathf.Max(0.2f, -m.forward.y)) - Vector3.ProjectOnPlane(m.forward, Vector3.up).normalized * 17f;
                for (int i = 0; i < SpawnIds.Length; i++)
                    try { _spawned.Add((SpawnIds[i], TheWaningBorder.Entities.UnitFactory.Create(em, SpawnIds[i], new Unity.Mathematics.float3(at.x + (i % 7) * 1.8f, at.y, at.z - i / 7 * 2.4f), Faction.Blue))); }
                    catch (System.Exception e) { Debug.LogError("[InfantryProbe] " + SpawnIds[i] + ": " + e.Message); }
                return;
            }
            var sb = new System.Text.StringBuilder();
            Bounds b = default; bool first = true;
            foreach (var (id, e) in _spawned)
            {
                if (!em.Exists(e) || !EntityViewManager.Instance.TryGetView(e, out var go) || go == null) { sb.AppendLine(id + ": no view"); continue; }
                var an = go.GetComponentInChildren<Animator>();
                var bb = LegionaryShot.BoundsOf(go);
                sb.AppendLine($"{id}: view {go.name} root scale {go.transform.localScale.y:0.000} animator {(an != null && an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "-")} kit {go.GetComponentInChildren<UnitAgeKit>() != null}");
                if (first) { b = bb; first = false; } else if ((bb.center - b.center).magnitude < 25f) b.Encapsulate(bb);
            }
            File.WriteAllText("Temp/infantry/spawned.txt", sb.ToString());
            LegionaryShot.Shoot(b, 200f, 28f, 1600, 900, "Temp/infantry/spawned.png");
            _spawned.Clear();
        }
        static readonly System.Collections.Generic.List<(string id, Unity.Entities.Entity e)> _spawned = new();
    }
}
