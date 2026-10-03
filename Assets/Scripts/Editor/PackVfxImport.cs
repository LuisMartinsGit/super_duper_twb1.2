// PackVfxImport.cs
// Copies purchased effect prefabs (Lana Studio "Casual RPG VFX", Hovl Studio
// "Magic effects pack") out of their vendor folders — which git does not
// track — into the folder of whatever owns each effect (PackVfxTable), and
// makes the copies self-contained and URP-correct:
//   * every material / texture / mesh a prefab uses is duplicated once into
//     Assets/GameData/Art/Vfx/<Pack>/{Materials,Textures,Models}/ and the
//     copied prefab is rewired to the duplicates by GUID;
//   * the duplicated materials move from the packs' built-in particle shaders
//     ("Mobile/Particles/*", "Particles/Standard Unlit" — magenta under URP)
//     to URP Particles/Unlit, keeping blend mode, HDR colour, texture, tiling;
//   * flat ground quads get a ParticleDecalMirror (projected, not z-fighting);
//   * the pack's UVscroll script is swapped for VfxUvScroll, which writes the
//     URP texture property.
// Re-runnable: duplicates that already exist are reused, and a copied prefab is
// overwritten in place (its GUID kept), so references to it survive.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class PackVfxImport
    {
        /// <summary>Vendor root → where its duplicated assets live. Separate
        /// folders per pack: their texture names collide.</summary>
        static readonly (string vendor, string shared)[] Packs =
        {
            ("Assets/Tools/Lana Studio/", "Assets/GameData/Art/Vfx/LanaStudio/"),
            ("Assets/MISC/Hovl Studio/", "Assets/GameData/Art/Vfx/Hovl/"),
        };
        const string UvScrollScript = "Assets/GameSystems/Rendering/Vfx/VfxUvScroll.cs";

        /// <summary>Vendor prefab path → destination prefab path.</summary>
        public static readonly (string source, string dest)[] Table = PackVfxTable.Entries;

        static string VendorOf(string path)
        {
            foreach (var p in Packs) if (path.StartsWith(p.vendor)) return p.vendor;
            return null;
        }
        static string SharedOf(string path)
        {
            foreach (var p in Packs) if (path.StartsWith(p.vendor)) return p.shared;
            return null;
        }

        /// <summary>Re-copy even prefabs that already exist — throws away any
        /// tuning done on the copies. Import Pack Effects leaves them alone.</summary>
        static bool _force;

        [MenuItem("Waning Border/Art/Import Pack Effects (force re-copy, discards tuning)")]
        public static void ImportAllForced()
        {
            _force = true;
            try { ImportAll(); } finally { _force = false; }
        }

        [MenuItem("Waning Border/Art/Import Pack Effects")]
        public static void ImportAll()
        {
            foreach (var p in Packs)
                foreach (var sub in new[] { "Materials", "Textures", "Models" }) EnsureFolder(p.shared + sub);
            var guidMap = new Dictionary<string, string>();
            var report = new System.Text.StringBuilder();
            foreach (var (source, dest) in Table)
                report.AppendLine(ImportOne(source, dest, guidMap));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.AppendLine(LocaliseMaterialTextures());
            report.AppendLine(MarkGroundDecals());
            report.AppendLine(EnsureVeilstoneGlowTrail());
            report.AppendLine(RelinkAbilityEffects());
            WireConfigs();
            AssetDatabase.SaveAssets();
            Debug.Log("[PackVfxImport]\n" + report);
        }

        /// <summary>
        /// Find each imported effect's FLAT ground subsystems — quads lying
        /// face-up whose particles stay at ground height over a simulated
        /// second and a half — and give them a ParticleDecalMirror, so they
        /// are projected onto the terrain instead of z-fighting it. Orbs,
        /// sparks and anything that rises are left as particles.
        /// </summary>
        static string MarkGroundDecals()
        {
            var sb = new System.Text.StringBuilder("ground decals:");
            foreach (var (_, dest) in Table)
            {
                // Effects that play in mid-air (hits, arrow tips) have no ground.
                if (dest.Contains("Hit") || dest.Contains("ArrowTip")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dest);
                if (prefab == null) continue;
                var flatPaths = FlatSubsystems(prefab);
                if (flatPaths.Count == 0) continue;

                var root = PrefabUtility.LoadPrefabContents(dest);
                bool changed = false;
                foreach (var path in flatPaths)
                {
                    var t = Resolve(root.transform, path);
                    if (t == null || t.GetComponent<TheWaningBorder.Rendering.ParticleDecalMirror>() != null) continue;
                    t.gameObject.AddComponent<TheWaningBorder.Rendering.ParticleDecalMirror>();
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, dest);
                PrefabUtility.UnloadPrefabContents(root);
                sb.Append($" {Path.GetFileNameWithoutExtension(dest)}[{flatPaths.Count}]");
            }
            return sb.ToString();
        }

        static List<string> FlatSubsystems(GameObject prefab)
        {
            var result = new List<string>();
            var go = Object.Instantiate(prefab, Vector3.zero, prefab.transform.rotation);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var buf = new ParticleSystem.Particle[512];
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var psr = ps.GetComponent<ParticleSystemRenderer>();
                    if (psr == null || !psr.enabled) continue;
                    bool horizontal = psr.renderMode == ParticleSystemRenderMode.HorizontalBillboard;
                    bool lyingLocal = psr.renderMode == ParticleSystemRenderMode.Billboard
                                      && psr.alignment == ParticleSystemRenderSpace.Local
                                      && Mathf.Abs(ps.transform.forward.y) > 0.9f;
                    if (!horizontal && !lyingLocal) continue;

                    bool grounded = true, any = false;
                    for (float t = 0.2f; t <= 1.6f && grounded; t += 0.2f)
                    {
                        ps.Simulate(t, false, true);
                        int n = ps.GetParticles(buf);
                        bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
                        for (int i = 0; i < n; i++)
                        {
                            any = true;
                            var p = world ? buf[i].position : ps.transform.TransformPoint(buf[i].position);
                            if (Mathf.Abs(p.y) > 0.2f) { grounded = false; break; }
                        }
                    }
                    if (grounded && any) result.Add(PathFrom(go.transform, ps.transform));
                }
            }
            finally { Object.DestroyImmediate(go); }
            return result;
        }

        /// <summary>Child-index path ("0/2"), not names — sibling names repeat
        /// (Shardbound Fury has two "shadow" children).</summary>
        static string PathFrom(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Insert(0, t.GetSiblingIndex().ToString());
            return string.Join("/", parts);
        }

        static Transform Resolve(Transform root, string path)
        {
            if (path.Length == 0) return root;
            var t = root;
            foreach (var part in path.Split('/'))
            {
                int i = int.Parse(part);
                if (i >= t.childCount) return null;
                t = t.GetChild(i);
            }
            return t;
        }

        /// <summary>A copied material still names the texture it was copied
        /// with — the vendor's. Duplicate each such texture and point the
        /// material at the copy.</summary>
        static string LocaliseMaterialTextures()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", System.Array.ConvertAll(Packs, p => p.shared + "Materials")))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null) continue;
                foreach (var prop in mat.GetTexturePropertyNames())
                {
                    var tex = mat.GetTexture(prop);
                    string p = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                    if (p == null || VendorOf(p) == null) continue;
                    var copy = AssetDatabase.LoadAssetAtPath<Texture>(DuplicateDependency(p));
                    var st = mat.GetTextureScale(prop); var off = mat.GetTextureOffset(prop);
                    mat.SetTexture(prop, copy);
                    mat.SetTextureScale(prop, st); mat.SetTextureOffset(prop, off);
                    EditorUtility.SetDirty(mat);
                    n++;
                }
            }
            return $"material textures localised: {n}";
        }

        /// <summary>
        /// Point every AbilityDefSO.vfxPrefab at the <c>&lt;Asset&gt;_Effect.prefab</c>
        /// beside it. Needed after an import because overwriting a placeholder
        /// effect prefab changes its root object's file id, which the SO's
        /// reference names; also fills slots that had no prefab yet.
        /// </summary>
        static string RelinkAbilityEffects()
        {
            var sb = new System.Text.StringBuilder("relinked:");
            foreach (var guid in AssetDatabase.FindAssets("t:AbilityDefSO", new[] { "Assets/GameData/TechTree" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fx = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_Effect.prefab")
                                .Replace('\\', '/');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fx);
                var so = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Abilities.AbilityDefSO>(path);
                if (so == null || prefab == null || so.vfxPrefab == prefab) continue;
                so.vfxPrefab = prefab;
                EditorUtility.SetDirty(so);
                sb.Append(' ').Append(Path.GetFileNameWithoutExtension(path));
            }
            return sb.ToString();
        }

        /// <summary>Create (first run, with first-pass values) or refresh the
        /// prefab references of the five effect configs. Tuned numbers in an
        /// existing asset are left alone.</summary>
        static void WireConfigs()
        {
            const string T = "Assets/GameData/TechTree/";
            const string R = T + "Sects/Renewal/Abilities/";
            GameObject P(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);

            var player = Config<TheWaningBorder.Rendering.AbilityVfxPlayerConfig>(
                "Assets/GameSystems/Rendering/Vfx/AbilityVfxPlayer.asset", c =>
                {
                    c.unitScalePerMetre = 0.5f; c.buildingScalePerMetre = 0.4f;
                    c.areaAuthoredRadius = 4f; c.areaMinScale = 0.8f; c.areaMaxScale = 4f;
                    c.fadeSeconds = 1.5f; c.passivePollSeconds = 0.5f;
                });
            EditorUtility.SetDirty(player);

            var heal = Config<TheWaningBorder.Rendering.UnitHealVfxConfig>(
                "Assets/GameSystems/Rendering/Units/UnitHealVfx.asset", c =>
                {
                    c.scalePerMetre = 0.5f; c.pollSeconds = 0.25f;
                    c.cooldownSeconds = 1.75f; c.minHealFraction = 0.02f;
                });
            heal.healPrefab = P(T + "Age0/Units/Vfx/Unit_Heal.prefab");
            EditorUtility.SetDirty(heal);

            var combat = Config<TheWaningBorder.Rendering.UnitCombatVfxConfig>(
                "Assets/GameSystems/Rendering/Units/UnitCombatVfx.asset", c =>
                {
                    c.scalePerMetre = 0.5f; c.hitHeightFraction = 0.55f; c.hitCooldownSeconds = 0.35f;
                    c.maxHitsPerFrame = 12; c.buffPollSeconds = 0.25f; c.buffThreshold = 0.02f; c.fadeSeconds = 1f;
                });
            combat.hitPrefab = P(T + "Age0/Units/Vfx/Unit_Hit.prefab");
            combat.chargeHitPrefab = P(T + "Age0/Units/Vfx/Unit_ChargeHit.prefab");
            combat.speedUpPrefab = P(T + "Age0/Units/Vfx/Unit_SpeedUp.prefab");
            combat.slowDownPrefab = P(T + "Age0/Units/Vfx/Unit_SlowDown.prefab");
            combat.attackUpPrefab = P(T + "Age0/Units/Vfx/Unit_AttackUp.prefab");
            combat.arrowVeilstoneHitPrefab = P(T + "Civs/Alanthor/Buildings/ArcheryRange/Vfx/ArrowHit_Veilstone.prefab");
            combat.arrowVeilsteelHitPrefab = P(T + "Civs/Alanthor/Buildings/ArcheryRange/Vfx/ArrowHit_Veilsteel.prefab");
            EditorUtility.SetDirty(combat);

            // Arrow tiers: one block per tier, each tuned on its own (the Arrow
            // Trails scenario's panel). First values only when a block is empty.
            var arrows = Config<TheWaningBorder.Rendering.ArrowTrailTiersConfig>(
                "Assets/GameSystems/Rendering/Vfx/ArrowTrailTiers.asset", c => { });
            if (arrows.stoneTrailWidth <= 0f) arrows.stoneTrailWidth = 0.33f;
            if (arrows.ironTrailWidth <= 0f) arrows.ironTrailWidth = 0.33f;
            arrows.veilstone ??= new TheWaningBorder.Rendering.ArrowTrailTiersConfig.TipLook();
            arrows.veilsteel ??= new TheWaningBorder.Rendering.ArrowTrailTiersConfig.TipLook();
            if (arrows.veilstone.tipScale <= 0f) arrows.veilstone.tipScale = 0.2f;
            if (arrows.veilsteel.tipScale <= 0f) arrows.veilsteel.tipScale = 0.2f;
            if (arrows.veilstone.hitScale <= 0f) arrows.veilstone.hitScale = 0.15f;
            if (arrows.veilsteel.hitScale <= 0f) arrows.veilsteel.hitScale = 0.15f;
            arrows.veilstone.tipPrefab = P(T + "Civs/Alanthor/Buildings/ArcheryRange/Vfx/ArrowTip_Veilstone.prefab");
            arrows.veilsteel.tipPrefab = P(T + "Civs/Alanthor/Buildings/ArcheryRange/Vfx/ArrowTip_Veilsteel.prefab");
            EditorUtility.SetDirty(arrows);

            // Building dust / smoke: the copies carry converted URP materials,
            // so the per-effect material override is no longer needed.
            var buildings = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.BuildingEffectSystemConfig>(
                "Assets/GameSystems/Rendering/Buildings/BuildingEffectSystem.asset");
            if (buildings != null)
            {
                buildings.constructionDustPrefab = P(T + "Shared/Buildings/Vfx/Building_ConstructionDust.prefab");
                buildings.damageSmokePrefab = P(T + "Shared/Buildings/Vfx/Building_DamageSmoke.prefab");
                buildings.constructionDustMaterial = null;
                if (buildings.constructionDustIdleSeconds <= 0f) buildings.constructionDustIdleSeconds = 1.5f;
                if (buildings.constructionDustFadeSeconds <= 0f) buildings.constructionDustFadeSeconds = 2f;
                buildings.damageSmokeMaterial = null;
                EditorUtility.SetDirty(buildings);
            }

            var sect = Config<TheWaningBorder.Rendering.SectPowerVfxConfig>(
                "Assets/GameSystems/Rendering/Vfx/SectPowerVfx.asset", c =>
                {
                    TheWaningBorder.Rendering.SectPowerVfxConfig.Landing L(
                        TheWaningBorder.Economy.SectActivePowerKind k, float authored, float min, float max)
                        => new TheWaningBorder.Rendering.SectPowerVfxConfig.Landing
                        { sectId = TheWaningBorder.Economy.SectConfig.Renewal, kind = k,
                          authoredRadius = authored, minScale = min, maxScale = max };
                    c.landings = new[]
                    {
                        L(TheWaningBorder.Economy.SectActivePowerKind.HealCirclePercent, 4f, 0.8f, 4f),
                        L(TheWaningBorder.Economy.SectActivePowerKind.RaiseTower, 1f, 1.5f, 3f),
                        L(TheWaningBorder.Economy.SectActivePowerKind.DeathWard, 4f, 0.8f, 4f),
                    };
                });
            foreach (var l in sect.landings)
                l.prefab = l.kind switch
                {
                    TheWaningBorder.Economy.SectActivePowerKind.HealCirclePercent => P(R + "HandsOfPlenty/HandsOfPlenty_Landing.prefab"),
                    TheWaningBorder.Economy.SectActivePowerKind.RaiseTower => P(R + "RaiseAnew/RaiseAnew_Landing.prefab"),
                    TheWaningBorder.Economy.SectActivePowerKind.DeathWard => P(R + "SecondWind/SecondWind_Landing.prefab"),
                    _ => l.prefab,
                };
            EditorUtility.SetDirty(sect);

            var renewal = Config<TheWaningBorder.Rendering.RenewalVfxConfig>(
                R + "RenewalVfx.asset", c =>
                {
                    c.unitScalePerMetre = 0.5f; c.hospitalAuthoredRadius = 4f; c.hospitalHealRadius = 15f;
                    c.fadeSeconds = 1.5f; c.pollSeconds = 0.5f;
                });
            renewal.regenTailPrefab = P(R + "HandsOfPlenty/HandsOfPlenty_RegenTail.prefab");
            renewal.deathWardPrefab = P(R + "SecondWind/SecondWind_Ward.prefab");
            renewal.fieldHospitalAuraPrefab = P(R + "DeployFieldHospital/FieldHospital_Aura.prefab");
            EditorUtility.SetDirty(renewal);

            const string DecalMatPath = "Assets/GameData/Art/Vfx/VfxDecal.mat";
            var decalMat = AssetDatabase.LoadAssetAtPath<Material>(DecalMatPath);
            var decalShader = Shader.Find("TWB/VfxDecal");
            if (decalMat == null && decalShader != null)
            {
                decalMat = new Material(decalShader);
                AssetDatabase.CreateAsset(decalMat, DecalMatPath);
            }
            var mirror = Config<TheWaningBorder.Rendering.ParticleDecalMirrorConfig>(
                "Assets/GameSystems/Rendering/Vfx/ParticleDecalMirror.asset", c =>
                {
                    c.glow = 1.5f; c.depth = 1.5f; c.maxParticles = 24; c.fallbackLift = 0.08f;
                });
            mirror.decalMaterial = decalMat;
            EditorUtility.SetDirty(mirror);

            var shard = Config<TheWaningBorder.Rendering.ShardrootVfxConfig>(
                T + "Border/ShardrootVfx.asset", c =>
                {
                    c.groundScale = 1.5f; c.unitScalePerMetre = 0.5f; c.fadeSeconds = 1.5f; c.pollSeconds = 0.25f;
                });
            shard.idlePrefab = P(T + "Border/Shardroot_Idle.prefab");
            shard.dropPrefab = P(T + "Border/Shardroot_Drop.prefab");
            shard.pickUpPrefab = P(T + "Border/Shardroot_PickUp.prefab");
            shard.carriedPrefab = P(T + "Border/Shardroot_Carried.prefab");
            EditorUtility.SetDirty(shard);
        }

        [MenuItem("Waning Border/Art/Add Veilstone Glow Trail")]
        public static void EnsureVeilstoneGlowTrailMenu() => Debug.Log("[PackVfxImport] " + EnsureVeilstoneGlowTrail());

        /// <summary>
        /// Lana's dark-magic projectile ships with only a trail_shadow; the
        /// electric one has trail_glow + trail_shadow. Give the Veilstone tip
        /// copy its own trail_glow — Veilsteel's, same width curve and timing,
        /// recoloured dark-magic purple — so both tiers have the same two
        /// editable trails. Re-run after every import (the copy is rewritten
        /// from the vendor prefab); does nothing once the trail exists.
        /// </summary>
        static string EnsureVeilstoneGlowTrail()
        {
            const string Dir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/Vfx/";
            const string Src = Dir + "ArrowTip_Veilsteel.prefab";
            const string Dst = Dir + "ArrowTip_Veilstone.prefab";
            if (!File.Exists(Src) || !File.Exists(Dst)) return "glow trail: tip prefabs missing";

            var dst = PrefabUtility.LoadPrefabContents(Dst);
            try
            {
                if (dst.transform.Find("trail_glow") != null) return "glow trail: Veilstone already has one";
                var src = PrefabUtility.LoadPrefabContents(Src);
                try
                {
                    var glow = src.transform.Find("trail_glow");
                    if (glow == null) return "glow trail: Veilsteel has none to copy";
                    var copy = Object.Instantiate(glow.gameObject, dst.transform);
                    copy.name = "trail_glow";
                    copy.transform.localPosition = glow.localPosition;
                    copy.transform.localRotation = glow.localRotation;
                    copy.transform.localScale = glow.localScale;
                    copy.transform.SetSiblingIndex(0);

                    // Same alpha keys, dark-magic colours: pale violet at the
                    // head fading into deep purple.
                    var tr = copy.GetComponent<TrailRenderer>();
                    var g = tr.colorGradient;
                    var colours = g.colorKeys;
                    for (int i = 0; i < colours.Length; i++)
                    {
                        float t = colours.Length > 1 ? i / (float)(colours.Length - 1) : 0f;
                        colours[i].color = Color.Lerp(new Color(0.78f, 0.62f, 1f), new Color(0.42f, 0.12f, 0.95f), t);
                    }
                    g.SetKeys(colours, g.alphaKeys);
                    tr.colorGradient = g;

                    PrefabUtility.SaveAsPrefabAsset(dst, Dst);
                    return "glow trail: added to Veilstone";
                }
                finally { PrefabUtility.UnloadPrefabContents(src); }
            }
            finally { PrefabUtility.UnloadPrefabContents(dst); }
        }

        static TCfg Config<TCfg>(string path, System.Action<TCfg> firstValues) where TCfg : ScriptableObject
        {
            var c = AssetDatabase.LoadAssetAtPath<TCfg>(path);
            if (c != null) return c;
            c = ScriptableObject.CreateInstance<TCfg>();
            firstValues(c);
            AssetDatabase.CreateAsset(c, path);
            return c;
        }

        static string ImportOne(string src, string dest, Dictionary<string, string> guidMap)
        {
            if (!File.Exists(src)) return "MISSING " + src;
            // A copy that already exists may carry tuning made in the
            // Inspector: keep it. Only placeholders and missing ones are written.
            if (!_force && File.Exists(dest) && !File.ReadAllText(dest).Contains("PLACEHOLDER_"))
                return $"kept {dest} (already copied)";
            foreach (var dep in AssetDatabase.GetDependencies(src, true))
            {
                if (dep == src || VendorOf(dep) == null) continue;
                string oldGuid = AssetDatabase.AssetPathToGUID(dep);
                if (guidMap.ContainsKey(oldGuid)) continue;
                string newPath = dep.EndsWith(".cs") ? UvScrollScript : DuplicateDependency(dep);
                guidMap[oldGuid] = AssetDatabase.AssetPathToGUID(newPath);
            }

            string text = File.ReadAllText(src);
            text = Regex.Replace(text, @"guid: ([0-9a-f]{32})",
                m => guidMap.TryGetValue(m.Groups[1].Value, out var g) ? "guid: " + g : m.Value);
            // VfxUvScroll keeps UVscroll's field names, so the serialized values carry over.
            EnsureFolder(Path.GetDirectoryName(dest).Replace('\\', '/'));
            File.WriteAllText(dest, text);
            AssetDatabase.ImportAsset(dest);
            return $"{Path.GetFileName(src)} -> {dest}";
        }

        /// <summary>Copy one pack asset into the shared folder (once), and
        /// convert it if it is a material.</summary>
        static string DuplicateDependency(string dep)
        {
            string ext = Path.GetExtension(dep).ToLowerInvariant();
            string sub = ext == ".mat" ? "Materials" : ext == ".fbx" ? "Models" : "Textures";
            string target = SharedOf(dep) + sub + "/" + Path.GetFileName(dep);
            if (!File.Exists(target))
            {
                AssetDatabase.CopyAsset(dep, target);
                if (ext == ".mat") ConvertMaterial(target);
            }
            return target;
        }

        /// <summary>Mobile/Particles/{Additive,Alpha Blended} → URP Particles/Unlit.</summary>
        static void ConvertMaterial(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) return;
            // Mobile/Particles/{Additive,Alpha Blended}: the blend is in the
            // shader's name. Particles/Standard Unlit (Hovl): the blend is its
            // _Mode dropdown (4 = Additive; Fade / Transparent are alpha) and
            // the colour is HDR in _Color.
            string shaderName = mat.shader != null ? mat.shader.name : "";
            bool standard = shaderName == "Particles/Standard Unlit";
            bool additive = standard
                ? mat.HasProperty("_Mode") && Mathf.RoundToInt(mat.GetFloat("_Mode")) == 4
                : shaderName.Contains("Additive");
            var color = standard && mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            var scale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            var offset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;

            mat.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", scale);
            mat.SetTextureOffset("_BaseMap", offset);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);    // 2 additive, 0 alpha
            mat.SetFloat("_ColorMode", 0f);
            mat.SetFloat("_SrcBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.SrcAlpha));
            mat.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
        }

        static void EnsureFolder(string path)
        {
            path = path.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
