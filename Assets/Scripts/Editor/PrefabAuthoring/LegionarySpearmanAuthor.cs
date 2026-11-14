// LegionarySpearmanAuthor.cs
// Builds the Age 0 Spearman as ONE prefab with two kits (UnitAgeKit), each a
// rig of its own under the root:
//   Levy       — first age: the Synty POLYGON Fantasy Kingdom peasant with a
//                plain spear held in both hands. No armour, no shield.
//   Legionary  — aged up: the POLYGON Ancient Empire Soldier in steel, the
//                plain helmet, the scutum on the left forearm and a pilum.
// Each rig has its own Animator and avatar; both play the SAME controller
// (Idle / Move / Attack / Death): the levy is a reskin of the legionary. Saturated cloth on either takes
// the player's colour at spawn (UnitPlayerColor).
//
//   Waning Border > Art > Units > Author Legionary Spearman
//       writes Spearman.prefab and Spearman.controller in the unit folder and points
//       Spearman.asset at them.
//   Waning Border > Art > Units > Render Legionary Clip Sheets
//       Temp/legionary/*.png, both kits sampled along their clips.
//
// The prefab root carries ONE scale factor, measured here, that makes the
// Spearman's body as tall as the Worker's.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.EditorTools
{
    public static class LegionarySpearmanAuthor
    {
        const string UnitDir = "Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Spearman/";
        const string PrefabPath = UnitDir + "Spearman.prefab";
        const string ControllerPath = UnitDir + "Spearman.controller";
        const string SoPath = UnitDir + "Spearman.asset";
        const string WorkerPrefab = "Assets/GameData/TechTree/Age0/Buildings/Fortress/Units/Worker/Worker.prefab";
        const string AE = "Assets/Synty/PolygonAncientEmpire/";
        const string KI = "Assets/Tools/Kevin Iglesias/Human Animations/Animations/Male/";
        const string Knight = "Assets/Tools/ExplosiveLLC/Warrior Pack Bundle 2 FREE/Knight Warrior Mecanim Animation Pack/Animations/";
        const string Out = "Temp/legionary/";

        // Levy (first age): the peasant body and a plain spear.
        const string FK = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string LevyBody = FK + "Characters/SM_Chr_Peasant_Male_01.prefab";
        const string LevySpear = FK + "Weapons/SM_Wep_Spear_01.prefab";
        // ONE set of clips for both kits: the levy is a reskin of the
        // legionary and moves exactly like it.
        internal static readonly string[] Clips =
        {
            Knight + "Knight@Idle.FBX",
            Knight + "Knight@Run.FBX",
            Knight + "Knight@Attack1.FBX",
            KI + "Combat/HumanM@Death01.fbx",
        };

        // Where the kit sits on the rig (bone-local).
        internal static readonly Vector3 SpearPos = new Vector3(0.09f, 0.03f, -0.25f);
        internal static readonly Vector3 SpearEuler = new Vector3(-90f, 0f, 0f);
        // Scutum: face away from the back of the forearm, long side across it
        // (upright when the forearm is held level in the guard stance).
        internal static readonly Vector3 ShieldPos = new Vector3(-0.22f, 0.09f, -0.18f);
        internal static readonly Vector3 ShieldFace = new Vector3(0f, 1f, 0f);
        internal static readonly Vector3 ShieldUp = new Vector3(0f, 0f, 1f);
        // Cape: its top against the upper spine, in the rig's frame.
        static Vector3 CapeOffset = new Vector3(0f, 0.16f, -0.13f);
        static Vector3 CapeEuler = new Vector3(0f, 0f, 0f);
        static float CapeScale = 0.9f;

        // The Spearman's root only matches its body to the Worker's. How large
        // units are DRAWN is UnitWorldScale.asset, for every unit at once.
        const float WorldScale = 1f;

        /// <summary>Sets only the root scale of the existing prefab (nothing is re-authored).</summary>
        [MenuItem("Waning Border/Art/Units/Spearman: match Worker height (root only)")]
        public static void ApplyWorldScale()
        {
            var workerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPrefab);
            var worker = (GameObject)Object.Instantiate(workerAsset);
            worker.transform.localScale = Vector3.one;
            float workerHeight = BodyHeight(worker);
            Object.DestroyImmediate(worker);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                float was = root.transform.localScale.x;
                root.transform.localScale = Vector3.one;
                root.transform.position = Vector3.zero;
                float body = BodyHeight(root);
                float k = workerHeight / body * WorldScale;
                root.transform.localScale = Vector3.one * k;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                string msg = $"worker body {workerHeight:0.000} m, spearman body {body:0.000} m at scale 1; root scale {was:0.000} -> {k:0.000}; spearman body now {body * k:0.000} m";
                Directory.CreateDirectory(Out);
                File.WriteAllText(Out + "world_scale.txt", msg);
                Debug.Log("[Spearman] " + msg);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Waning Border/Art/Units/Author Legionary Spearman")]
        public static void Author()
        {
            var log = new StringBuilder();
            var levy = BuildController(ControllerPath, Clips, log);
            var legion = levy;
            if (AssetDatabase.LoadMainAssetAtPath(UnitDir + "SpearmanLegionary.controller") != null)
                AssetDatabase.DeleteAsset(UnitDir + "SpearmanLegionary.controller");
            var go = BuildRig(log, levy, legion);
            try
            {
                go.GetComponent<UnitAgeKit>().Show(false);

                // The Spearman stands as tall as the Worker (developer,
                // 2026-11-13): one factor on the prefab root, measured body
                // to body (skinned meshes only, so the spear does not count).
                float levyHeight = BodyHeight(go);
                var workerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPrefab);
                var worker = workerAsset != null ? (GameObject)Object.Instantiate(workerAsset) : null;
                if (worker != null)
                {
                    float workerHeight = BodyHeight(worker);
                    Object.DestroyImmediate(worker);
                    if (levyHeight > 0.1f && workerHeight > 0.1f)
                    {
                        float k = workerHeight / levyHeight * WorldScale;
                        go.transform.localScale = Vector3.one * k;
                        log.AppendLine($"worker body {workerHeight:0.000} m, spearman body {levyHeight:0.000} m, world scale {WorldScale} -> root scale {k:0.000}");
                    }
                }
                else log.AppendLine("MISSING " + WorkerPrefab);
                var saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                var so = AssetDatabase.LoadMainAssetAtPath(SoPath);
                var ser = new SerializedObject(so);
                ser.FindProperty("prefab").objectReferenceValue = saved;
                ser.FindProperty("animatorController").objectReferenceValue = levy;
                ser.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
                AssetDatabase.SaveAssets();
                log.AppendLine($"saved {PrefabPath}; levy height {LegionaryShot.BoundsOf(go).max.y:0.00}");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Directory.CreateDirectory(Out);
                File.WriteAllText(Out + "author.txt", log.ToString());
                Debug.Log("[LegionarySpearmanAuthor] " + log);
            }
        }

        /// <summary>Top of the shown skinned bodies above the root, in metres.</summary>
        internal static float BodyHeight(GameObject go)
        {
            float top = 0f;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                top = Mathf.Max(top, smr.bounds.max.y - go.transform.position.y);
            return top;
        }

        internal static Transform Bone(GameObject rig, string n, StringBuilder log)
        {
            foreach (var t in rig.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            log.AppendLine("MISSING BONE " + n + " on " + rig.name);
            return null;
        }

        internal static GameObject Attach(string prefabPath, string name, Transform bone, Vector3 lp, Vector3 le, Material mat)
        {
            var a = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(a, bone);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = name;
            inst.transform.localPosition = lp;
            inst.transform.localRotation = Quaternion.Euler(le);
            inst.transform.localScale = Vector3.one;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (mat != null) foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            return inst;
        }

        /// <summary>Slide a spear along its own shaft until the hand grips its middle.</summary>
        internal static void HoldAtMiddle(GameObject spear)
        {
            var t = spear.transform;
            Vector3 grip = t.parent.TransformPoint(new Vector3(SpearPos.x, SpearPos.y, 0f));
            Vector3 axis = t.up;
            t.position += axis * Vector3.Dot(grip - LegionaryShot.BoundsOf(spear).center, axis);
        }

        /// <summary>A Synty character prefab as a plain rig: its one shown body, none of the hidden ones.</summary>
        internal static GameObject Character(string prefabPath, string name, Transform parent, RuntimeAnimatorController controller, bool dropAttachments)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var kill = new List<GameObject>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t == go.transform) continue;
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    if (!t.gameObject.activeSelf) kill.Add(t.gameObject);
                    else { t.localPosition = Vector3.zero; smr.updateWhenOffscreen = false; }
                }
                else if (!t.gameObject.activeSelf && t.GetComponent<Renderer>() != null) kill.Add(t.gameObject);
                else if (dropAttachments && t.name.StartsWith("SM_Chr_Attach_")) kill.Add(t.gameObject);
            }
            foreach (var k in kill) if (k != null) Object.DestroyImmediate(k);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            var anim = go.GetComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return go;
        }

        /// <summary>The Ancient Empire cape down a body's back: placed in the
        /// rig's own frame (the body faces +Z), its top at the shoulders. Worn
        /// by the Swordsman, to tell him from the Spearman.</summary>
        internal static GameObject AttachCape(GameObject root, GameObject body, Material mat, StringBuilder log)
        {
            var spine = Bone(body, "Spine_03", log);
            var cape = Attach(AE + "Prefabs/Characters/Attach/SM_Chr_Attach_Cape_01.prefab", "Cape", spine, Vector3.zero, Vector3.zero, mat);
            cape.transform.rotation = root.transform.rotation * Quaternion.Euler(CapeEuler);
            cape.transform.localScale = Vector3.one * CapeScale;
            var cb = LegionaryShot.BoundsOf(cape);
            Vector3 top = new Vector3(cb.center.x, cb.max.y, cb.center.z);
            cape.transform.position += spine.position + root.transform.rotation * CapeOffset - top;
            return cape;
        }

        internal static GameObject BuildRig(StringBuilder log, RuntimeAnimatorController levyController, RuntimeAnimatorController legionController)
        {
            if (File.Exists("Temp/legionary/tune.txt"))
                foreach (var raw in File.ReadAllLines("Temp/legionary/tune.txt"))
                {
                    var q = raw.Trim().Split(' ');
                    float F(int i) => float.Parse(q[i], System.Globalization.CultureInfo.InvariantCulture);
                    if (q[0] == "capeoffset") CapeOffset = new Vector3(F(1), F(2), F(3));
                    else if (q[0] == "capeeuler") CapeEuler = new Vector3(F(1), F(2), F(3));
                    else if (q[0] == "capescale") CapeScale = F(1);
                }
            var root = new GameObject("Spearman");

            // ── First age: the Fantasy Kingdom peasant and a plain spear ──
            var levy = Character(LevyBody, "Levy", root.transform, levyController, false);
            // The Militia's spear: plain, then the Stone Weapons one.
            var levyHand = Bone(levy, "Hand_R", log);
            var levyWeapons = new[]
            {
                Attach(LevySpear, "Levy_Spear", levyHand, SpearPos, SpearEuler, null),
                Attach(FK + "Weapons/SM_Wep_Spear_05.prefab", "Levy_Spear_T1_Stone", levyHand, SpearPos, SpearEuler, null),
            };
            foreach (var w in levyWeapons) HoldAtMiddle(w);
            levyWeapons[1].SetActive(false);

            // ── Aged up: the Ancient Empire soldier, helmet, scutum, pilum ──
            var legion = Character(AE + "Prefabs/Characters/SM_Chr_Soldier_Male_01.prefab", "Legionary", root.transform, legionController, true);
            var red = AssetDatabase.LoadAssetAtPath<Material>(AE + "Materials/Alts/PolygonAncientEmpire_01_A.mat");
            foreach (var smr in legion.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.sharedMaterial = red;
            // -- Gear that follows the faction (UnitAgeKit) --
            // Garrison level 1 / 2 / 3: a plain helmet and a round shield,
            // then a gold-trimmed crested helmet, then the great crest.
            // Weapons: the plain spear, then one per researched weapon tech.
            var head = Bone(legion, "Head", log);
            var hand = Bone(legion, "Hand_R", log);
            var elbow = Bone(legion, "Elbow_L", log);
            Vector3 shieldEuler = Quaternion.LookRotation(ShieldFace, ShieldUp).eulerAngles;
            var helmets = new[]
            {
                Attach(AE + "Prefabs/Characters/Attach/SM_Chr_Attach_Helmet_01.prefab", "Helmet_L1", head, Vector3.zero, Vector3.zero, red),
                Attach(AE + "Prefabs/Characters/Attach/SM_Chr_Attach_Helmet_03.prefab", "Helmet_L2", head, Vector3.zero, Vector3.zero, red),
                Attach(AE + "Prefabs/Characters/Attach/SM_Chr_Attach_Helmet_02.prefab", "Helmet_L3", head, Vector3.zero, Vector3.zero, red),
            };
            // The scutum at every level: a round shield made him read as the
            // Swordsman (developer, 2026-11-13).
            // One scutum per level, the same shield in three paints
            // (SwordsmanSyntyAuthor.ScutumMesh): wood, white on the player's
            // colour, gold on the player's colour.
            var scuta = new GameObject[3];
            for (int l = 0; l < 3; l++)
            {
                scuta[l] = Attach(AE + "Prefabs/Weapons/SM_Wep_Shield_04.prefab", "Shield_L" + (l + 1), elbow, ShieldPos, shieldEuler, null);
                var mf = scuta[l].GetComponentInChildren<MeshFilter>(true);
                if (mf != null) mf.sharedMesh = SwordsmanSyntyAuthor.ScutumMesh(mf.sharedMesh, l + 1, log);
                if (l > 0) scuta[l].SetActive(false);
            }
            var weapons = new[]
            {
                Attach(AE + "Prefabs/Weapons/SM_Wep_Spear_01.prefab", "Spear_T0", hand, SpearPos, SpearEuler, null),
                Attach(FK + "Weapons/SM_Wep_Spear_05.prefab", "Spear_T1_Stone", hand, SpearPos, SpearEuler, null),
                Attach(AE + "Prefabs/Weapons/SM_Wep_Spear_03.prefab", "Spear_T2_Iron", hand, SpearPos, SpearEuler, null),
                Attach(FK + "Weapons/SM_Wep_Spear_07.prefab", "Spear_T3_Veilstone", hand, SpearPos, SpearEuler, null),
                Attach(FK + "Weapons/SM_Wep_Spear_10.prefab", "Spear_T4_Shard", hand, SpearPos, SpearEuler, null),
            };
            foreach (var w in weapons) HoldAtMiddle(w);

            var kit = root.AddComponent<UnitAgeKit>();
            kit.firstAge = new[] { levy };
            kit.aged = new[] { legion };
            kit.levelLadderId = "Barracks";
            kit.helmets = helmets;
            kit.shields = scuta;
            kit.weapons = weapons;
            kit.firstAgeWeapons = levyWeapons;
            kit.weaponTechs = new[] { "StoneWeapons", "IronWeapons", "VeilstoneWeapons", "ShardInfusedWeapons" };
            SwordsmanSyntyAuthor.SetTopLevelBody(kit, legion, log);
            for (int i = 1; i < helmets.Length; i++) helmets[i].SetActive(false);
            for (int i = 1; i < weapons.Length; i++) weapons[i].SetActive(false);

            // Saturated cloth and the shield face take the player's colour at spawn.
            var playerColor = root.AddComponent<UnitPlayerColor>();
            // The peasant's brown vest and skirt (one swatch of the Fantasy
            // Kingdom atlas) are the player's colour; the shirt stays linen.
            var levyBody = levy.GetComponentInChildren<SkinnedMeshRenderer>();
            if (levyBody != null) playerColor.garmentMaterial = levyBody.sharedMaterial;
            playerColor.garmentColors = new[] { new Color32(140, 100, 68, 255) };
            playerColor.garmentTolerance = 9;
            legion.SetActive(false);
            return root;
        }

        internal static AnimationClip Clip(string fbx)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            return null;
        }

        /// <summary>Idle / Move / Attack / Death from clips[0..3], rebuilt in place so the asset keeps its guid.</summary>
        internal static AnimatorController BuildController(string path, string[] clips, StringBuilder log)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (c == null) c = AnimatorController.CreateAnimatorControllerAtPath(path);

            while (c.parameters.Length > 0) c.RemoveParameter(0);
            c.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            c.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);
            c.AddParameter("IsDead", AnimatorControllerParameterType.Trigger);
            c.AddParameter("AttackSpeed", AnimatorControllerParameterType.Float);
            var pars = c.parameters;
            pars[3].defaultFloat = 1f;
            c.parameters = pars;

            var sm = c.layers[0].stateMachine;
            foreach (var s in sm.states) sm.RemoveState(s.state);
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);

            AnimatorState State(string name, string fbx, Vector3 pos)
            {
                var st = sm.AddState(name, pos);
                st.motion = Clip(fbx);
                st.writeDefaultValues = true;
                if (st.motion == null) log.AppendLine("MISSING CLIP " + fbx);
                else
                {
                    var clip = (AnimationClip)st.motion;
                    log.AppendLine($"{Path.GetFileNameWithoutExtension(path)} {name}: {clip.name} {clip.length:0.00}s loop={clip.isLooping} ground speed {new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude:0.00} m/s");
                }
                return st;
            }
            var idle = State("Idle", clips[0], new Vector3(260, 60));
            var move = State("Move", clips[1], new Vector3(520, 60));
            var attack = State("Attack", clips[2], new Vector3(390, 200));
            var death = State("Death", clips[3], new Vector3(390, -80));
            sm.defaultState = idle;
            attack.speedParameter = "AttackSpeed";
            attack.speedParameterActive = true;

            void Link(AnimatorState from, AnimatorState to, string par, bool value, float blend)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false; t.hasFixedDuration = true; t.duration = blend;
                t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, par);
            }
            Link(idle, move, "IsMoving", true, 0.12f);
            Link(move, idle, "IsMoving", false, 0.12f);
            Link(idle, attack, "IsAttacking", true, 0.06f);
            Link(move, attack, "IsAttacking", true, 0.06f);
            Link(attack, idle, "IsAttacking", false, 0.15f);
            Link(attack, move, "IsMoving", true, 0.12f);
            var die = sm.AddAnyStateTransition(death);
            die.hasExitTime = false; die.hasFixedDuration = true; die.duration = 0.1f;
            die.canTransitionToSelf = false;
            die.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");

            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return c;
        }

        /// <summary>One row of the aged Spearman in each gear state (Temp/legionary/gear_states.png):
        /// level 1, 2, 3, then level 3 with weapon tier 1, 2, 3, then level 2 with the cape.</summary>
        [MenuItem("Waning Border/Art/Units/Render Spearman Gear States")]
        public static void GearStates()
        {
            Directory.CreateDirectory(Out);
            var log = new StringBuilder();
            var stage = new GameObject("GearStage");
            try
            {
                (int level, int weapon, int tech)[] states = { (1, 0, 0), (2, 1, 0), (3, 2, 0), (3, 3, 0), (3, 4, 0) };
                var clip = Clip(Clips[0]);
                for (int i = 0; i < states.Length; i++)
                {
                    var rig = BuildRig(log, null, null);
                    rig.transform.SetParent(stage.transform, false);
                    var kit = rig.GetComponent<UnitAgeKit>();
                    kit.Apply(true, states[i].level, states[i].weapon, states[i].tech, instant: true);
                    var shown = kit.aged[0];
                    if (clip != null) clip.SampleAnimation(shown, 0f);
                    shown.transform.localPosition = Vector3.zero;
                    shown.transform.localRotation = Quaternion.identity;
                    rig.transform.position = new Vector3(-i * 1.9f, 0f, 0f);
                    foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        smr.enabled = false;
                        if (!smr.gameObject.activeInHierarchy) continue;
                        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        smr.BakeMesh(mesh, true);
                        var bg = new GameObject("Baked");
                        bg.transform.SetParent(stage.transform, false);
                        bg.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                        bg.AddComponent<MeshFilter>().sharedMesh = mesh;
                        bg.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                    }
                }
                int n = states.Length;
                var b2 = new Bounds(new Vector3(-(n - 1) * 0.95f, 1.1f, 0f), new Vector3(n * 1.9f, 2.9f, 0.2f));
                LegionaryShot.Shoot(b2, 205f, 10f, 2800, 760, Out + "gear_states.png", true);
                LegionaryShot.Shoot(b2, 25f, 10f, 2800, 760, Out + "gear_states_back.png", true);
            }
            finally
            {
                Object.DestroyImmediate(stage);
                File.WriteAllText(Out + "gear_states.txt", log.ToString());
            }
        }

        [MenuItem("Waning Border/Art/Units/Render Legionary Clip Sheets")]
        public static void Sheets()
        {
            Directory.CreateDirectory(Out);
            var log = new StringBuilder();
            var stage = new GameObject("LegionarySheetStage");
            try
            {
                const int N = 6;
                var rig = new GameObject[N];
                for (int i = 0; i < N; i++)
                {
                    rig[i] = BuildRig(log, null, null);
                    rig[i].transform.SetParent(stage.transform, false);
                }
                var baked = new List<GameObject>();
                void Row(bool agedKit, string tag, string path)
                {
                    foreach (var b in baked) Object.DestroyImmediate(b);
                    baked.Clear();
                    var clip = Clip(path);
                    if (clip == null) { log.AppendLine("MISSING " + path); return; }
                    for (int i = 0; i < N; i++)
                    {
                        var ageKit = rig[i].GetComponent<UnitAgeKit>();
                        ageKit.Show(agedKit);
                        var shown = agedKit ? ageKit.aged[0] : ageKit.firstAge[0];
                        float t = clip.length * i / (clip.isLooping ? N : N - 1);
                        clip.SampleAnimation(shown, t);
                        shown.transform.localPosition = Vector3.zero;
                        shown.transform.localRotation = Quaternion.identity;
                        rig[i].transform.position = new Vector3(-i * 1.9f, 0f, 0f);
                        rig[i].transform.rotation = Quaternion.identity;
                        // The editor skins a mesh once per frame, not per
                        // render: bake each sampled pose into a plain mesh.
                        foreach (var smr in rig[i].GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            smr.enabled = false;
                            if (!smr.gameObject.activeInHierarchy) continue;
                            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                            smr.BakeMesh(mesh, true);
                            var bg = new GameObject("Baked");
                            bg.transform.SetParent(stage.transform, false);
                            bg.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                            bg.AddComponent<MeshFilter>().sharedMesh = mesh;
                            bg.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                            baked.Add(bg);
                        }
                    }
                    var b2 = new Bounds(new Vector3(-(N - 1) * 0.95f, 1.0f, 0f), new Vector3(N * 1.9f, 2.6f, 0.2f));
                    LegionaryShot.Shoot(b2, 205f, 10f, 2400, 640, Out + "sheet_" + tag + ".png", true);
                    log.AppendLine($"{tag}: {clip.name} {clip.length:0.00}s loop={clip.isLooping}");
                }
                string[] names = { "idle", "move", "attack", "death" };
                for (int k = 0; k < 4; k++) Row(false, "levy_" + names[k], Clips[k]);
                for (int k = 0; k < 4; k++) Row(true, "legion_" + names[k], Clips[k]);
            }
            finally
            {
                Object.DestroyImmediate(stage);
                File.WriteAllText(Out + "sheets.txt", log.ToString());
            }
        }
    }
}
