// CastleArtPreview.cs
// Renders every prefab CastleArtAuthor makes to Temp/castle_preview/*.png —
// off-screen, with a temporary camera and light, leaving the open scene as it
// was. Variant prefabs (watch tower, Fortress) are rendered once per level.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class CastleArtPreview
    {
        const string Out = "Temp/castle_preview/";
        static readonly Vector3 Stage = new Vector3(5000f, 0f, 5000f);

        [MenuItem("Waning Border/Art/Preview Castle Prefabs")]
        public static void PreviewAll()
        {
            Directory.CreateDirectory(Out);
            const string stone = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Wall/Stone/";
            foreach (var part in new[] { "Curtain", "Hub", "Gate", "Tower", "Mount" })
                for (int lv = 1; lv <= 3; lv++)
                    Render(stone + $"{part}_L{lv}.prefab", $"wall_{part}_L{lv}", null);

            const string tower = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Tower/WatchTower.prefab";
            const string fort = "Assets/GameData/TechTree/Age0/Buildings/Fortress/Fortress.prefab";
            foreach (var (path, name) in new[] { (tower, "watchtower"), (fort, "fortress") })
            {
                Render(path, name + "_Lv0", "Lv0");
                for (int lv = 1; lv <= 3; lv++) Render(path, $"{name}_Alanthor_Lv{lv}", "Alanthor/Lv" + lv);
            }

            // The whole set together, per level, at its shared scale.
            for (int lv = 1; lv <= 3; lv++) RenderAssembly(stone, lv);

            // The gate exactly as the game builds it (fitted to the 9 m span,
            // turned to run along Z, doors bound), shut and open.
            for (int lv = 1; lv <= 3; lv++) RenderGateDoors(stone, lv);
            Debug.Log("[CastleArtPreview] wrote " + Out);
        }

        [MenuItem("Waning Border/Art/Preview House")]
        public static void PreviewHouse()
        {
            Directory.CreateDirectory(Out);
            Render("Assets/GameData/TechTree/Age0/Buildings/Hut/House_test_project.fbx", "house", null);
            Debug.Log("[CastleArtPreview] wrote " + Out + "house.png");
        }

        /// <summary>The house with the damage plume on its roof, and with a
        /// construction puff at its foot — both simulated a few seconds in,
        /// using the prefabs and URP materials from BuildingEffectSystem.asset.</summary>
        [MenuItem("Waning Border/Art/Preview Building Smoke")]
        public static void PreviewSmoke()
        {
            Directory.CreateDirectory(Out);
            var cfg = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.BuildingEffectSystemConfig>(
                "Assets/GameSystems/Rendering/Buildings/BuildingEffectSystem.asset");
            var house = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameData/TechTree/Age0/Buildings/Hut/House_test_project.fbx");
            if (cfg == null || house == null) return;
            foreach (var (prefab, mat, name, roof, scale) in new[]
            {
                (cfg.damageSmokePrefab, cfg.damageSmokeMaterial, "smoke_plume", true,
                 2.8f * cfg.damageSmokeScalePerMetre * (1f + cfg.damageSmokeExtraScaleAtZero * 0.5f)),
                (cfg.constructionDustPrefab, cfg.constructionDustMaterial, "construction_puff", false,
                 2.8f * cfg.constructionDustScalePerMetre),
            })
            {
                var root = new GameObject("smoke") { hideFlags = HideFlags.DontSave };
                root.transform.position = Stage;
                Object.Instantiate(house, root.transform).transform.localPosition = Vector3.zero;
                var fx = Object.Instantiate(prefab, root.transform);
                fx.transform.localPosition = roof ? new Vector3(0.6f, 3.2f, -0.4f) : Vector3.zero;
                fx.transform.localScale = Vector3.one * scale;
                foreach (var r in fx.GetComponentsInChildren<ParticleSystemRenderer>()) r.sharedMaterial = mat;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    ps.Simulate(roof ? 4f : 0.6f, true, true);
                }
                Shoot(root, name, new Vector3(-0.8f, 0.45f, -1f));
                Object.DestroyImmediate(root);
            }
            Debug.Log("[CastleArtPreview] wrote smoke previews");
        }

        /// <summary>Every imported Lana effect (PackVfxTable), simulated a
        /// moment in, to Temp/castle_preview/lana_*.png — checks the URP
        /// materials draw (not magenta, not invisible).</summary>
        [MenuItem("Waning Border/Art/Preview Lana Effects")]
        public static void PreviewLana()
        {
            Directory.CreateDirectory(Out);
            foreach (var (_, dest) in PackVfxTable.Entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dest);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, Stage, prefab.transform.rotation);
                go.hideFlags = HideFlags.DontSave;
                // Hit effects are over in ~0.1 s (flash) to 1 s (sparks):
                // snapshot them mid-flash, everything else a moment in.
                float at = dest.Contains("Hit") ? 0.08f : 0.9f;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Simulate(at, false, true);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                        ps.Simulate(at, true, true);
                Shoot(go, "lana_" + Path.GetFileNameWithoutExtension(dest), new Vector3(-0.8f, 0.6f, -1f));
                Object.DestroyImmediate(go);
            }
            Debug.Log("[CastleArtPreview] wrote Lana previews");
        }

        /// <summary>Effects with ground decals over a SLOPED plane, mirrored
        /// once in edit mode — checks the rings project onto the ground
        /// instead of cutting through it.</summary>
        [MenuItem("Waning Border/Art/Preview Ground Decals")]
        public static void PreviewGroundDecals()
        {
            Directory.CreateDirectory(Out);
            foreach (var (_, dest) in PackVfxTable.Entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dest);
                if (prefab == null || prefab.GetComponentInChildren<TheWaningBorder.Rendering.ParticleDecalMirror>(true) == null)
                    continue;
                var root = new GameObject("decal_preview") { hideFlags = HideFlags.DontSave };
                root.transform.position = Stage;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.SetParent(root.transform, false);
                ground.transform.localScale = Vector3.one * 0.8f;
                ground.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);   // a slope
                var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.35f, 0.33f, 0.3f) };
                ground.GetComponent<Renderer>().sharedMaterial = lit;

                var fx = Object.Instantiate(prefab, root.transform);
                fx.transform.localPosition = Vector3.zero;
                fx.transform.localRotation = prefab.transform.rotation;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Simulate(0.8f, false, true);
                foreach (var m in fx.GetComponentsInChildren<TheWaningBorder.Rendering.ParticleDecalMirror>(true))
                    m.PreviewNow();
                var log = new System.Text.StringBuilder();
                foreach (var d in fx.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true))
                {
                    if (!d.enabled || d.material == null) continue;
                    var tex = d.material.GetTexture("Base_Map") as Texture2D;
                    log.AppendLine($"  {d.transform.parent.name}: shader {d.material.shader.name} tex {(tex != null ? tex.name + " " + tex.format : "NULL")} " +
                                   $"tint {d.material.GetColor("_Tint")} glow {d.material.GetFloat("_Glow")} fade {d.fadeFactor:0.00} size {d.size}");
                    break;
                }
                File.AppendAllText(Out + "decal_debug.txt", Path.GetFileNameWithoutExtension(dest) + "\n" + log);
                Shoot(ground, "decal_" + Path.GetFileNameWithoutExtension(dest), new Vector3(-0.6f, 0.9f, -1f));
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(lit);
            }
            Debug.Log("[CastleArtPreview] wrote ground decal previews");
        }

        /// <summary>Veilstone / Veilsteel arrows (tip effect in place of the
        /// head) and their hits at in-game scale, beside a 2 m capsule for a
        /// unit — checks they read arrow-sized, not unit-sized.</summary>
        [MenuItem("Waning Border/Art/Preview Arrow Tiers")]
        public static void PreviewArrowTiers()
        {
            Directory.CreateDirectory(Out);
            var combat = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.UnitCombatVfxConfig>(
                "Assets/GameSystems/Rendering/Units/UnitCombatVfx.asset");
            foreach (var tier in new[] { TheWaningBorder.Rendering.ArrowTrailTier.Veilstone, TheWaningBorder.Rendering.ArrowTrailTier.Veilsteel })
            {
                var root = new GameObject("arrow_preview") { hideFlags = HideFlags.DontSave };
                root.transform.position = Stage;
                var unit = GameObject.CreatePrimitive(PrimitiveType.Capsule);   // 2 m tall
                unit.transform.SetParent(root.transform, false);
                unit.transform.localPosition = new Vector3(0f, 1f, 0f);

                // The arrow, built like ProjectileVisualSystem's template, in flight beside the unit.
                var arrow = new GameObject("Arrow");
                arrow.transform.SetParent(root.transform, false);
                arrow.transform.localPosition = new Vector3(-1.2f, 1.4f, 0f);
                arrow.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shaft.transform.SetParent(arrow.transform, false);
                shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shaft.transform.localScale = new Vector3(0.03f, 0.25f, 0.03f);
                var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tip.name = "Tip";
                tip.transform.SetParent(arrow.transform, false);
                tip.transform.localPosition = new Vector3(0f, 0f, 0.25f);
                tip.transform.localScale = new Vector3(0.08f, 0.08f, 0.12f);
                TheWaningBorder.Rendering.ArrowTrailTiers.ApplyArrow(arrow, null, tier);
                foreach (var ps in arrow.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(0.5f, false, true);

                // Its hit, on the unit's other side, at the in-game scale.
                var hitPrefab = tier == TheWaningBorder.Rendering.ArrowTrailTier.Veilsteel
                    ? combat.arrowVeilsteelHitPrefab : combat.arrowVeilstoneHitPrefab;
                var hit = Object.Instantiate(hitPrefab, root.transform);
                hit.transform.localPosition = new Vector3(1.2f, 1.1f, 0f);
                hit.transform.localScale = hitPrefab.transform.localScale * TheWaningBorder.Rendering.ArrowTrailTiers.HitScale(tier);
                foreach (var ps in hit.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(0.15f, false, true);

                Shoot(unit, "arrow_" + tier, new Vector3(0f, 0.25f, -1f));
                Object.DestroyImmediate(root);
            }
            Debug.Log("[CastleArtPreview] wrote arrow tier previews");
        }

        static void RenderGateDoors(string stone, int lv)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(stone + $"Gate_L{lv}.prefab");
            if (prefab == null) return;
            var art = TheWaningBorder.Rendering.WallModuleArt.ForPrefab(prefab,
                TheWaningBorder.Entities.AlanthorWall.GateSpanMetres, orientAlongZ: true);
            if (art == null) { Debug.LogWarning($"[CastleArtPreview] gate L{lv} art did not prepare"); return; }
            var root = new GameObject("gate") { hideFlags = HideFlags.DontSave };
            root.transform.position = Stage;
            TheWaningBorder.Rendering.WallModuleArt.Instantiate(art, root.transform, "Gatehouse");
            var doors = root.AddComponent<TheWaningBorder.Rendering.WallGateDoors>();
            int leaves = doors.BindDoors(root.transform);
            doors.SnapToState();   // no entity: shut
            Shoot(root, $"gate_L{lv}_shut", new Vector3(-1f, 0.4f, -0.35f));
            // Open: drive the private swing to fully open.
            var t = typeof(TheWaningBorder.Rendering.WallGateDoors);
            t.GetField("_open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
             .SetValue(doors, 1f);
            t.GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
             .Invoke(doors, null);
            Shoot(root, $"gate_L{lv}_open", new Vector3(-1f, 0.4f, -0.35f));
            Debug.Log($"[CastleArtPreview] gate L{lv}: {leaves} door leaves bound");
            Object.DestroyImmediate(root);
        }

        /// <summary>hub | wall | wall | wall tower | wall | gate | wall | hub,
        /// laid along X in kit metres — the same relative sizes the game draws
        /// (every piece shares the curtain's scale).</summary>
        static void RenderAssembly(string stone, int lv)
        {
            var root = new GameObject("assembly") { hideFlags = HideFlags.DontSave };
            root.transform.position = Stage;
            void Add(string part, float x)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(stone + $"{part}_L{lv}.prefab");
                if (p == null) return;
                var g = Object.Instantiate(p, root.transform);
                g.transform.localPosition = new Vector3(x, 0f, 0f);
            }
            Add("Hub", 0f);
            Add("Curtain", 5.8f); Add("Curtain", 10.8f);
            Add("Tower", 15.8f);
            Add("Curtain", 20.8f);
            Add("Gate", 30.8f);
            Add("Curtain", 40.8f);
            Add("Hub", 46.6f);
            Shoot(root, $"assembly_L{lv}", new Vector3(-0.35f, 0.45f, -1f));
            Shoot(root, $"assembly_L{lv}_inner", new Vector3(0.4f, 0.6f, 1f));
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Render kit pieces listed one per line in Temp/kit_list.txt (paths
        /// under the Fantasy Kingdom Prefabs folder, no extension) to
        /// Temp/castle_preview/kit_*.png, with their bounds in
        /// Temp/castle_preview/kit_bounds.txt — for choosing pieces by eye.
        /// </summary>
        [MenuItem("Waning Border/Art/Preview Kit Pieces")]
        public static void PreviewKit()
        {
            Directory.CreateDirectory(Out);
            const string kit = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
            var sb = new System.Text.StringBuilder();
            foreach (var raw in File.ReadAllLines("Temp/kit_list.txt"))
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;
                string name = "kit_" + Path.GetFileName(p);
                Render(kit + p + ".prefab", name, null);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kit + p + ".prefab");
                if (prefab == null) { sb.AppendLine(p + " | MISSING"); continue; }
                var go = Object.Instantiate(prefab);
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    sb.AppendLine($"{p} | size {b.size.x:F2} {b.size.y:F2} {b.size.z:F2} | min {b.min.x:F2} {b.min.y:F2} {b.min.z:F2} | max {b.max.x:F2} {b.max.y:F2} {b.max.z:F2}");
                }
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Out + "kit_bounds.txt", sb.ToString());
        }

        static void Render(string path, string name, string branch)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning($"[CastleArtPreview] missing {path}"); return; }
            var go = Object.Instantiate(prefab, Stage, Quaternion.identity);
            go.hideFlags = HideFlags.DontSave;
            if (branch != null) ShowOnly(go.transform, branch);
            Shoot(go, name, new Vector3(-0.8f, 0.55f, -1f));
            Shoot(go, name + "_back", new Vector3(0.9f, 0.45f, 0.8f));
            Object.DestroyImmediate(go);
        }

        static void RenderRow(string path, string name, int count)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;
            var root = new GameObject("row") { hideFlags = HideFlags.DontSave };
            root.transform.position = Stage;
            for (int i = 0; i < count; i++)
            {
                var g = Object.Instantiate(prefab, root.transform);
                g.transform.localPosition = new Vector3(i * 5f, 0f, 0f);
            }
            Shoot(root, name, new Vector3(-0.8f, 0.55f, -1f));
            Object.DestroyImmediate(root);
        }

        /// <summary>Hide every variant branch but <paramref name="branch"/>
        /// ("Lv0" or "Alanthor/Lv2").</summary>
        static void ShowOnly(Transform root, string branch)
        {
            foreach (Transform c in root) c.gameObject.SetActive(false);
            var parts = branch.Split('/');
            var top = root.Find(parts[0]);
            if (top == null) return;
            top.gameObject.SetActive(true);
            if (parts.Length > 1)
                foreach (Transform c in top) c.gameObject.SetActive(c.name == parts[1]);
        }

        static void Shoot(GameObject go, string name, Vector3 viewDir)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) if (r.gameObject.activeInHierarchy) b.Encapsulate(r.bounds);

            var camGo = new GameObject("PreviewCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.6f, 0.66f);
            cam.fieldOfView = 35f;
            float size = b.extents.magnitude;
            var dir = viewDir.normalized;
            cam.transform.position = b.center + dir * size * 2.6f;
            cam.transform.LookAt(b.center);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = size * 10f + 50f;

            var lightGo = new GameObject("PreviewLight") { hideFlags = HideFlags.DontSave };
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var rt = new RenderTexture(900, 640, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderSettings.fog = fog;
            File.WriteAllBytes(Out + name + ".png", tex.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
        }
    }
}
