// SwordsmanSyntyAuthor.cs
// Builds the Alanthor Swordsman from Synty assets only (developer,
// 2026-11-13: "author a swordsman from the synty assets (not present at
// age 0)"). It replaces the figure PresentationSpawnSystem used to build from
// primitives (SwordsmanVisual).
//
//   Body     POLYGON Ancient Empire SM_Chr_Soldier_Male_01 (the Spearman's
//            armour; the two are told apart by their gear)
//   Level    Garrison 1 / 2 / 3: a plain bronze helmet, then a crested one,
//            then the great crest; a bronze round shield, then the lion shield
//   Weapon   a sword per weapon tier (none, Stone, Iron, Veilstone, Shard)
// on the Knight clips the Spearman uses (sword and shield), the Kevin
// Iglesias death. The gear follows the faction through UnitAgeKit, which has
// no first-age kit here: the unit does not exist before a culture.
//
//   Waning Border > Art > Units > Author Synty Swordsman
//   Waning Border > Art > Units > Render Swordsman Gear States  (Temp/swordsman/)

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Rendering;
using L = TheWaningBorder.EditorTools.LegionarySpearmanAuthor;

namespace TheWaningBorder.EditorTools
{
    public static class SwordsmanSyntyAuthor
    {
        const string UnitDir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Units/Swordsman/";
        const string PrefabPath = UnitDir + "Swordsman.prefab";
        const string ControllerPath = UnitDir + "Swordsman.controller";
        const string SoPath = UnitDir + "Swordsman.asset";
        const string WorkerPrefab = "Assets/GameData/TechTree/Age0/Buildings/Fortress/Units/Worker/Worker.prefab";
        const string AE = "Assets/Synty/PolygonAncientEmpire/";
        const string FK = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string Out = "Temp/swordsman/";

        // The sword in the fist: its blade along the hand, as the spear.
        static Vector3 SwordPos = new Vector3(0.09f, 0.03f, 0f);
        static Vector3 SwordEuler = new Vector3(-90f, 0f, 0f);

        internal static readonly string[] Swords =
        {
            AE + "Prefabs/Weapons/SM_Wep_Sword_01.prefab",   // none
            FK + "Weapons/SM_Wep_Sword_01.prefab",           // Stone
            AE + "Prefabs/Weapons/SM_Wep_Sword_02.prefab",   // Iron
            AE + "Prefabs/Weapons/SM_Wep_Sword_03.prefab",   // Veilstone
            FK + "Weapons/SM_Wep_Sword_08.prefab",           // Shard
        };

        [MenuItem("Waning Border/Art/Units/Author Synty Swordsman")]
        public static void Author()
        {
            var log = new StringBuilder();
            var controller = L.BuildController(ControllerPath, L.Clips, log);
            var go = BuildRig(log, controller);
            try
            {
                float body = L.BodyHeight(go);
                var workerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPrefab);
                if (workerAsset != null)
                {
                    var worker = (GameObject)Object.Instantiate(workerAsset);
                    worker.transform.localScale = Vector3.one;
                    float workerHeight = L.BodyHeight(worker);
                    Object.DestroyImmediate(worker);
                    if (body > 0.1f && workerHeight > 0.1f)
                    {
                        go.transform.localScale = Vector3.one * (workerHeight / body);
                        log.AppendLine($"worker body {workerHeight:0.000} m, swordsman body {body:0.000} m -> root scale {workerHeight / body:0.000}");
                    }
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                var so = AssetDatabase.LoadMainAssetAtPath(SoPath);
                var ser = new SerializedObject(so);
                ser.FindProperty("prefab").objectReferenceValue = saved;
                ser.FindProperty("animatorController").objectReferenceValue = controller;
                ser.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
                AssetDatabase.SaveAssets();
                log.AppendLine("saved " + PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Directory.CreateDirectory(Out);
                File.WriteAllText(Out + "author.txt", log.ToString());
                Debug.Log("[SwordsmanSyntyAuthor] " + log);
            }
        }

        const string GoldMeshPath = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Art/Soldier_Body_Gilded.asset";

        // The soldier's armour is three steel swatches of the atlas (one column,
        // three shades); the gold the helmet trims use sits one column over, in
        // two shades. Which steel shades are moved onto it: bit 0 light, bit 1
        // mid, bit 2 dark.
        const float SteelU = 0.4306f, GoldU = 0.4843f, SwatchTolerance = 0.012f;
        static readonly float[] SteelV = { 0.0658f, 0.0445f, 0.0264f };
        static readonly float[] GoldV = { 0.0461f, 0.0461f, 0.0258f };
        static int GiltShades = 0b111;
        // "Some metal pieces": only the armour above this height on the body
        // (the shoulder guards and the collar) is gilded; the banded cuirass
        // below it stays steel.
        static float GiltAboveY = 1.36f;

        /// <summary>
        /// The soldier body with part of its steel gilded: a copy of the mesh
        /// whose vertices on the chosen steel swatches are re-pointed at the
        /// atlas's gold. No texture is made or changed, so the player-colour
        /// recolouring of the cloth is untouched. Saved beside the Garrison's
        /// art; updated in place so prefabs keep their reference.
        /// </summary>
        internal static Mesh GildedBody(Mesh source, StringBuilder log)
        {
            if (File.Exists("Temp/swordsman/tune.txt"))
                foreach (var raw in File.ReadAllLines("Temp/swordsman/tune.txt"))
                {
                    var q = raw.Trim().Split(' ');
                    if (q.Length == 2 && q[0] == "gilt") GiltShades = int.Parse(q[1]);
                    if (q.Length == 2 && q[0] == "gilty") GiltAboveY = float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture);
                }
            var uv = source.uv;
            var verts = source.vertices;
            int moved = 0;
            for (int i = 0; i < uv.Length; i++)
            {
                if (Mathf.Abs(uv[i].x - SteelU) > SwatchTolerance) continue;
                if (verts[i].y < GiltAboveY) continue;
                for (int s = 0; s < SteelV.Length; s++)
                {
                    if ((GiltShades & (1 << s)) == 0 || Mathf.Abs(uv[i].y - SteelV[s]) > SwatchTolerance * 0.8f) continue;
                    uv[i] = new Vector2(uv[i].x + (GoldU - SteelU), uv[i].y + (GoldV[s] - SteelV[s]));
                    moved++;
                    break;
                }
            }
            log.AppendLine($"gilded body: {moved} of {uv.Length} vertices moved to gold (shades {GiltShades})");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(GoldMeshPath);
            if (existing != null && existing.vertexCount == source.vertexCount)
            {
                existing.uv = uv;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var mesh = Object.Instantiate(source);
            mesh.name = "Soldier_Body_Gilded";
            mesh.uv = uv;
            Directory.CreateDirectory(Path.GetDirectoryName(GoldMeshPath));
            if (existing != null) AssetDatabase.DeleteAsset(GoldMeshPath);
            AssetDatabase.CreateAsset(mesh, GoldMeshPath);
            return mesh;
        }

        const string ArtDir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Art/";

        // Swatches of the Ancient Empire atlas (01_A), as flat colours.
        static readonly Vector2 SwRed = new Vector2(0.3874f, 0.0465f);        // cloth red: the player's colour in a match
        static readonly Vector2 SwRedPainted = new Vector2(0.2283f, 0.1466f); // the scutum's second red
        static readonly Vector2 SwGoldLight = new Vector2(0.4839f, 0.0637f);  // the scutum's wings
        static readonly Vector2 SwGold = new Vector2(0.4843f, 0.0461f);
        static readonly Vector2 SwBronzeRim = new Vector2(0.3041f, 0.2490f);  // the scutum's rim and back
        static readonly Vector2 SwWhite = new Vector2(0.8600f, 0.3382f);
        static readonly Vector2 SwWoodLight = new Vector2(0.2933f, 0.2678f);
        static readonly Vector2 SwWoodDark = new Vector2(0.2923f, 0.2295f);

        /// <summary>
        /// A copy of <paramref name="source"/> with every vertex that sits on
        /// one of the <c>from</c> swatches moved onto the matching <c>to</c>
        /// swatch (a flat colour: the vertex takes the swatch's own point).
        /// Saved as an asset and updated in place on a re-run, so prefabs keep
        /// their reference. No texture is made or changed.
        /// </summary>
        internal static Mesh Recoloured(Mesh source, string assetName, (Vector2 from, Vector2 to)[] map, StringBuilder log)
        {
            const float tol = 0.02f;
            var uv = source.uv;
            int moved = 0;
            for (int i = 0; i < uv.Length; i++)
                for (int m = 0; m < map.Length; m++)
                    if (Mathf.Abs(uv[i].x - map[m].from.x) < tol && Mathf.Abs(uv[i].y - map[m].from.y) < tol)
                    { uv[i] = map[m].to; moved++; break; }
            log.AppendLine($"{assetName}: {moved} of {uv.Length} vertices recoloured");
            string path = ArtDir + assetName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && existing.vertexCount == source.vertexCount)
            {
                existing.uv = uv;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var mesh = Object.Instantiate(source);
            mesh.name = assetName;
            mesh.uv = uv;
            Directory.CreateDirectory(ArtDir);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// The scutum of each Garrison level (developer, 2026-11-13: "low
        /// levels must have the scutum in wood -> white + blue -> gold +
        /// blue"): level 1 bare wood; level 2 the player's colour with white
        /// wings; level 3 the player's colour with gold wings and a gold rim.
        /// </summary>
        internal static Mesh ScutumMesh(Mesh source, int level, StringBuilder log)
        {
            switch (level)
            {
                case 1:
                    return Recoloured(source, "Scutum_L1_Wood", new[]
                    {
                        (SwRed, SwWoodLight), (SwRedPainted, SwWoodLight), (SwGoldLight, SwWoodDark),
                    }, log);
                case 2:
                    return Recoloured(source, "Scutum_L2_White", new[]
                    {
                        (SwRedPainted, SwRed), (SwGoldLight, SwWhite),
                    }, log);
                default:
                    return Recoloured(source, "Scutum_L3_Gold", new[]
                    {
                        (SwRedPainted, SwRed), (SwGoldLight, SwGold), (SwBronzeRim, SwGold),
                    }, log);
            }
        }

        /// <summary>Give a kit the gilded body for its top level.</summary>
        internal static void SetTopLevelBody(UnitAgeKit kit, GameObject body, StringBuilder log)
        {
            SkinnedMeshRenderer smr = null;
            // The body is the largest skinned mesh (the others are the face parts).
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.gameObject.activeSelf && r.sharedMesh != null
                    && (smr == null || r.sharedMesh.vertexCount > smr.sharedMesh.vertexCount)) smr = r;
            if (smr == null) { log.AppendLine("no body renderer for the gilded armour"); return; }
            kit.topLevelBody = smr;
            kit.baseMesh = smr.sharedMesh;
            kit.topLevelMesh = GildedBody(smr.sharedMesh, log);
            kit.topLevel = 3;
        }

        internal static GameObject BuildRig(StringBuilder log, RuntimeAnimatorController controller)
        {
            var root = new GameObject("Swordsman");
            var body = L.Character(AE + "Prefabs/Characters/SM_Chr_Soldier_Male_01.prefab", "Body", root.transform, controller, true);
            var red = AssetDatabase.LoadAssetAtPath<Material>(AE + "Materials/Alts/PolygonAncientEmpire_01_A.mat");
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.sharedMaterial = red;

            var head = L.Bone(body, "Head", log);
            var hand = L.Bone(body, "Hand_R", log);
            var elbow = L.Bone(body, "Elbow_L", log);
            Vector3 shieldEuler = Quaternion.LookRotation(L.ShieldFace, L.ShieldUp).eulerAngles;
            const string Att = AE + "Prefabs/Characters/Attach/SM_Chr_Attach_Helmet_0";
            var helmets = new[]
            {
                L.Attach(Att + "8.prefab", "Helmet_L1", head, Vector3.zero, Vector3.zero, red),
                L.Attach(Att + "5.prefab", "Helmet_L2", head, Vector3.zero, Vector3.zero, red),
                L.Attach(Att + "7.prefab", "Helmet_L3", head, Vector3.zero, Vector3.zero, red),
            };
            var bronze = L.Attach(AE + "Prefabs/Weapons/SM_Wep_Shield_02.prefab", "Shield_L1", elbow, L.ShieldPos, shieldEuler, null);
            var lion = L.Attach(AE + "Prefabs/Weapons/SM_Wep_Shield_01.prefab", "Shield_L2", elbow, L.ShieldPos, shieldEuler, null);
            var swords = new GameObject[Swords.Length];
            for (int i = 0; i < Swords.Length; i++)
            {
                swords[i] = L.Attach(Swords[i], "Sword_T" + i, hand, SwordPos, SwordEuler, null);
                var sb = LegionaryShot.BoundsOf(swords[i]);
                log.AppendLine($"sword {i} bounds {sb.size} centre {sb.center - hand.position}");
            }

            // The cape is his alone: it is what tells him from the Spearman
            // at a glance (developer, 2026-11-13).
            L.AttachCape(root, body, red, log);

            var kit = root.AddComponent<UnitAgeKit>();
            kit.aged = new[] { body };
            kit.levelLadderId = "Barracks";
            kit.helmets = helmets;
            kit.shields = new[] { bronze, lion, lion };
            kit.weapons = swords;
            kit.weaponTechs = new[] { "StoneWeapons", "IronWeapons", "VeilstoneWeapons", "ShardInfusedWeapons" };
            SetTopLevelBody(kit, body, log);
            for (int i = 1; i < helmets.Length; i++) helmets[i].SetActive(false);
            lion.SetActive(false);
            for (int i = 1; i < swords.Length; i++) swords[i].SetActive(false);

            root.AddComponent<UnitPlayerColor>();
            return root;
        }

        /// <summary>Lists the atlas colours the soldier body and the helmets use, by
        /// where on the body they sit (Temp/swordsman/soldier_atlas.txt).</summary>
        [MenuItem("Waning Border/Art/Units/Probe: soldier atlas colours")]
        public static void SoldierAtlas()
        {
            var sb = new StringBuilder();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AE + "Materials/Alts/PolygonAncientEmpire_01_A.mat");
            Texture tex = null;
            foreach (var p in mat.GetTexturePropertyNames())
                if (mat.GetTexture(p) != null && tex == null) tex = mat.GetTexture(p);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            void Mesh(string label, Mesh mesh)
            {
                sb.AppendLine(label + " verts " + mesh.vertexCount);
                var uv = mesh.uv; var v = mesh.vertices;
                var groups = new Dictionary<int, (int n, float ymin, float ymax, float ax, float u, float w)>();
                for (int i = 0; i < v.Length; i++)
                {
                    int px = (int)(Mathf.Repeat(uv[i].x, 1f) * tex.width), py = (int)(Mathf.Repeat(uv[i].y, 1f) * tex.height);
                    Color32 c = copy.GetPixel(px, py);
                    int key = (c.r / 6 << 16) | (c.g / 6 << 8) | (c.b / 6);
                    groups.TryGetValue(key, out var g);
                    if (g.n == 0) { g.ymin = 99f; g.ymax = -99f; }
                    g.n++; g.ymin = Mathf.Min(g.ymin, v[i].y); g.ymax = Mathf.Max(g.ymax, v[i].y); g.ax += Mathf.Abs(v[i].x);
                    g.u += uv[i].x; g.w += uv[i].y;
                    groups[key] = g;
                }
                foreach (var kv in groups)
                    if (kv.Value.n >= 10)
                        sb.AppendLine($"  rgb~({(kv.Key >> 16) * 6},{((kv.Key >> 8) & 255) * 6},{(kv.Key & 255) * 6}) verts={kv.Value.n} y[{kv.Value.ymin:0.00}..{kv.Value.ymax:0.00}] |x|={kv.Value.ax / kv.Value.n:0.00} uv=({kv.Value.u / kv.Value.n:0.0000},{kv.Value.w / kv.Value.n:0.0000})");
            }
            var soldier = AssetDatabase.LoadAssetAtPath<GameObject>(AE + "Prefabs/Characters/SM_Chr_Soldier_Male_01.prefab");
            foreach (var smr in soldier.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.gameObject.activeSelf) Mesh("BODY " + smr.name, smr.sharedMesh);
            for (int h = 1; h <= 8; h++)
            {
                var helm = AssetDatabase.LoadAssetAtPath<GameObject>(AE + "Prefabs/Characters/Attach/SM_Chr_Attach_Helmet_0" + h + ".prefab");
                var mf = helm != null ? helm.GetComponentInChildren<MeshFilter>(true) : null;
                if (mf != null) Mesh("HELMET " + h, mf.sharedMesh);
            }
            for (int sh = 1; sh <= 4; sh++)
            {
                var shield = AssetDatabase.LoadAssetAtPath<GameObject>(AE + "Prefabs/Weapons/SM_Wep_Shield_0" + sh + ".prefab");
                var mf = shield != null ? shield.GetComponentInChildren<MeshFilter>(true) : null;
                if (mf != null) Mesh("SHIELD " + sh, mf.sharedMesh);
            }
            Object.DestroyImmediate(copy);
            Directory.CreateDirectory(Out);
            File.WriteAllText(Out + "soldier_atlas.txt", sb.ToString());
        }

        /// <summary>The Swordsman at level 1, 2, 3 and with each sword.</summary>
        [MenuItem("Waning Border/Art/Units/Render Swordsman Gear States")]
        public static void GearStates()
        {
            Directory.CreateDirectory(Out);
            var log = new StringBuilder();
            var stage = new GameObject("SwordStage");
            try
            {
                (int level, int weapon)[] states = { (1, 0), (2, 1), (3, 2), (3, 3), (3, 4) };
                var clip = L.Clip(L.Clips[0]);
                for (int i = 0; i < states.Length; i++)
                {
                    var rig = BuildRig(log, null);
                    rig.transform.SetParent(stage.transform, false);
                    var kit = rig.GetComponent<UnitAgeKit>();
                    kit.Apply(true, states[i].level, states[i].weapon, 0, instant: true);
                    UnitGearIcons.Pose(rig, kit.aged[0], clip, stage.transform);
                    rig.transform.position = new Vector3(-i * 1.9f, 0f, 0f);
                }
                int n = states.Length;
                var b = new Bounds(new Vector3(-(n - 1) * 0.95f, 1.1f, 0f), new Vector3(n * 1.9f, 2.9f, 0.2f));
                LegionaryShot.Shoot(b, 205f, 10f, 2400, 760, Out + "gear_states.png", true);
            }
            finally
            {
                Object.DestroyImmediate(stage);
                File.WriteAllText(Out + "gear_states.txt", log.ToString());
            }
        }
    }

    /// <summary>
    /// Pictures for the action buttons (ActionIcons): each Garrison unit as it
    /// looks at each level, and the weapon of each weapon tech.
    ///   Waning Border > Art > Units > Render Unit and Weapon Icons
    /// writes Assets/Resources/UI/Icons/Units/<unit id>_L<level>.png and
    /// Assets/Resources/UI/Icons/Tech/<tech id>.png.
    /// </summary>
    public static class UnitGearIcons
    {
        const string UnitsDir = "Assets/Resources/UI/Icons/Units/";
        const string TechDir = "Assets/Resources/UI/Icons/Tech/";
        const string FK = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string AE = "Assets/Synty/PolygonAncientEmpire/Prefabs/";
        // Cloth that is the player's colour in a match is drawn neutral here.
        static readonly Color Neutral = new Color(0.78f, 0.74f, 0.66f);

        /// <summary>Sample a pose onto the shown body and bake its skinned
        /// meshes (the editor skins once a frame, not per render).</summary>
        internal static void Pose(GameObject rig, GameObject shown, AnimationClip clip, Transform stage)
        {
            if (clip != null) clip.SampleAnimation(shown, 0f);
            shown.transform.localPosition = Vector3.zero;
            shown.transform.localRotation = Quaternion.identity;
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.enabled = false;
                if (!smr.gameObject.activeInHierarchy) continue;
                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                smr.BakeMesh(mesh, true);
                var bg = new GameObject("Baked");
                bg.transform.SetParent(smr.transform.parent, false);
                bg.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                bg.AddComponent<MeshFilter>().sharedMesh = mesh;
                bg.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
            }
        }

        [MenuItem("Waning Border/Art/Units/Render Unit and Weapon Icons")]
        public static void Render()
        {
            Directory.CreateDirectory(UnitsDir);
            Directory.CreateDirectory(TechDir);
            var log = new StringBuilder();
            var stage = new GameObject("IconStage");
            bool warm = false;
            try
            {
                var idle = LegionarySpearmanAuthor.Clip(LegionarySpearmanAuthor.Clips[0]);
                void Unit(string file, GameObject rig, bool aged, int level)
                {
                    rig.transform.SetParent(stage.transform, false);
                    var kit = rig.GetComponent<UnitAgeKit>();
                    kit.Apply(aged, level, 0, 0, instant: true);
                    var pc = rig.GetComponent<UnitPlayerColor>();
                    if (pc != null) pc.Apply(Neutral);
                    var shown = aged ? kit.aged[0] : kit.firstAge[0];
                    Pose(rig, shown, idle, stage.transform);
                    var b = LegionaryShot.BoundsOf(rig);
                    if (!warm) { LegionaryShot.Shoot(b, 200f, 8f, 64, 64, "Temp/icon_warm.png"); warm = true; }
                    LegionaryShot.Shoot(b, 200f, 8f, 256, 256, file, false, true);
                    log.AppendLine(file);
                    Object.DestroyImmediate(rig);
                }
                Unit(UnitsDir + "Spearman_L0.png", LegionarySpearmanAuthor.BuildRig(log, null, null), false, 1);
                for (int l = 1; l <= 3; l++)
                    Unit(UnitsDir + "Spearman_L" + l + ".png", LegionarySpearmanAuthor.BuildRig(log, null, null), true, l);
                for (int l = 1; l <= 3; l++)
                    Unit(UnitsDir + "Alanthor_Swordsman_L" + l + ".png", SwordsmanSyntyAuthor.BuildRig(log, null), true, l);

                // The weapons of each tier: the spear's head and the sword,
                // crossed - the tech upgrades both.
                (string tech, string prefab)[] weapons =
                {
                    ("StoneWeapons", FK + "Weapons/SM_Wep_Spear_05.prefab"),
                    ("IronWeapons", AE + "Weapons/SM_Wep_Spear_03.prefab"),
                    ("VeilstoneWeapons", FK + "Weapons/SM_Wep_Spear_07.prefab"),
                    ("ShardInfusedWeapons", FK + "Weapons/SM_Wep_Spear_10.prefab"),
                };
                int tierIndex = 0;
                foreach (var (tech, prefab) in weapons)
                {
                    tierIndex++;
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                    if (asset == null) { log.AppendLine("MISSING " + prefab); continue; }
                    var go = (GameObject)Object.Instantiate(asset, stage.transform);
                    // Leaning, and turned about its own shaft so the flat of the blade faces the camera.
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 0f, -35f) * Quaternion.Euler(0f, 90f, 0f));
                    // The head end only: the top third of the shaft fills the picture.
                    var b = LegionaryShot.BoundsOf(go);
                    Vector3 tip = go.transform.TransformPoint(go.transform.InverseTransformPoint(b.center) + Vector3.up * 10f);
                    Vector3 dir = go.transform.up;
                    float len = Vector3.Dot(b.size, new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z)));
                    Vector3 headEnd = b.center + dir * (len * 0.5f - 0.27f);
                    var crop = new Bounds(headEnd, new Vector3(0.6f, 0.6f, 0.2f));
                    // The sword of the same tier, leaning the other way, its
                    // middle on the middle of the picture.
                    var swordAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SwordsmanSyntyAuthor.Swords[tierIndex]);
                    GameObject sword = null;
                    if (swordAsset != null)
                    {
                        sword = (GameObject)Object.Instantiate(swordAsset, stage.transform);
                        // Its length is its own Y, as the spear's: lean it the
                        // other way, the flat of the blade to the camera.
                        sword.transform.SetPositionAndRotation(Vector3.zero,
                            Quaternion.Euler(0f, 0f, 35f) * Quaternion.Euler(0f, 90f, 0f));
                        var sb = LegionaryShot.BoundsOf(sword);
                        float fit = 0.62f / Mathf.Max(0.05f, Mathf.Max(sb.size.x, sb.size.y));
                        sword.transform.localScale *= fit;
                        sb = LegionaryShot.BoundsOf(sword);
                        sword.transform.position += crop.center - sb.center + new Vector3(0f, 0f, 0.1f);
                    }
                    LegionaryShot.Shoot(crop, 180f, 0f, 256, 256, TechDir + tech + ".png", true, true);
                    if (sword != null) Object.DestroyImmediate(sword);
                    log.AppendLine(TechDir + tech + ".png");
                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                Object.DestroyImmediate(stage);
                AssetDatabase.Refresh();
                Directory.CreateDirectory("Temp/swordsman");
                File.WriteAllText("Temp/swordsman/icons.txt", log.ToString());
                Debug.Log("[UnitGearIcons] " + log);
            }
        }
    }
}
