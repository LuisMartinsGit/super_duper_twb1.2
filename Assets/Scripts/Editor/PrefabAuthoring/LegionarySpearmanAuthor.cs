// LegionarySpearmanAuthor.cs
// Builds the Age 0 Spearman as ONE prefab with two kits (UnitAgeKit) on one
// skeleton:
//   Levy       first age (the Militia): the peasant and a plain spear.
//   Legionary  aged up: a body, helmet and scutum per Garrison level and a
//              spear per weapon tech, mixed from the three Synty kits by
//              InfantryKit's recipes.
// The skeleton is the Roman soldier's; both play the SAME controller
// (Idle / Move / Attack / Death). Cloth takes the player's colour at spawn
// (UnitPlayerColor). Run it through InfantryKit.AuthorAll, which also writes the
// palette both infantry units share.
//
//   Waning Border > Art > Units > Author Garrison Infantry
//       writes Spearman.prefab and Spearman.controller in the unit folder and points
//       Spearman.asset at them.
//   Waning Border > Art > Units > Render Garrison Infantry  (Temp/infantry/)
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
        const string AE = "Assets/Synty/PolygonAncientEmpire/";
        const string KI = "Assets/Tools/Kevin Iglesias/Human Animations/Animations/Male/";
        const string Knight = "Assets/Tools/ExplosiveLLC/Warrior Pack Bundle 2 FREE/Knight Warrior Mecanim Animation Pack/Animations/";
        const string Out = "Temp/legionary/";

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
        static Vector3 CapeOffset = new Vector3(0f, 0.16f, -0.165f);
        static Vector3 CapeEuler = new Vector3(0f, 0f, 0f);
        static float CapeScale = 0.9f;

        /// <summary>The height every unit's body is drawn at (the first Worker's).</summary>
        internal const float UnitHeight = 1.734f;

        // The Spearman's root only matches its body to the Worker's. How large
        // units are DRAWN is UnitWorldScale.asset, for every unit at once.
        const float WorldScale = 1f;

        /// <summary>Sets only the root scale of the existing prefab (nothing is re-authored).</summary>
        [MenuItem("Waning Border/Art/Units/Spearman: match Worker height (root only)")]
        public static void ApplyWorldScale()
        {
            float workerHeight = UnitHeight;
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
                if (levyHeight > 0.1f)
                {
                    float k = UnitHeight / levyHeight * WorldScale;
                    go.transform.localScale = Vector3.one * k;
                    log.AppendLine($"spearman body {levyHeight:0.000} m -> root scale {k:0.000}");
                    InfantryKit.Grow(go);
                }
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

        /// <summary>The Roman soldier's skeleton, avatar and Animator with none of its meshes.</summary>
        internal static GameObject Skeleton(string name, Transform parent, RuntimeAnimatorController controller)
        {
            var go = Character(AE + "Prefabs/Characters/R_SM_Chr_Soldier_Male_01.prefab", name, parent, controller, true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r.gameObject);
            return go;
        }

        /// <summary>The Ancient Empire cape down a body's back: placed in the
        /// rig's own frame (the body faces +Z), its top at the shoulders. Worn
        /// by the Swordsman, to tell him from the Spearman.</summary>
        internal static GameObject AttachCape(GameObject root, GameObject body, StringBuilder log)
        {
            var spine = Bone(body, "Spine_03", log);
            var cape = InfantryKit.Piece("Swordsman_Cape", spine, Vector3.zero, Vector3.zero, InfantryKit.Role.Gold, InfantryKit.Cape(), log);
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

            // One skeleton for both ages (InfantryKit: a second rig, hidden, still
            // had every bone moved with the unit).
            var rig = Skeleton("Body", root.transform, legionController);
            var head = Bone(rig, "Head", log);
            var hand = Bone(rig, "Hand_R", log);
            var elbow = Bone(rig, "Elbow_L", log);
            GameObject Group(string name, Transform parent)
            {
                var g = new GameObject(name);
                g.transform.SetParent(parent, false);
                return g;
            }

            // ── First age: the Militia, a peasant in a straw hat ──
            var levy = InfantryKit.Body(rig, "Levy_Body", InfantryKit.Role.Matte, InfantryKit.LevyBody(), log);
            InfantryKit.Weld(levy, rig, InfantryKit.Piece("Levy_Hat", head, Vector3.zero, Vector3.zero, InfantryKit.Role.Matte, InfantryKit.LevyHat(), log));
            var levyHand = Group("Levy_Spears", hand);
            var levyWeapons = new GameObject[2];
            for (int i = 0; i < 2; i++)
            {
                levyWeapons[i] = InfantryKit.Piece("Spear_" + InfantryKit.TierNames[i], levyHand.transform, SpearPos, SpearEuler, InfantryKit.Role.Matte, InfantryKit.Spear(i), log);
                HoldAtMiddle(levyWeapons[i]);
                levyWeapons[i].SetActive(i == 0);
            }

            // ── Aged up: one body per Garrison level, its helmet and scutum
            // melted in, and one spear per weapon tech (InfantryKit's recipes).
            var legion = Group("Legionary", rig.transform);
            Vector3 shieldEuler = Quaternion.LookRotation(ShieldFace, ShieldUp).eulerAngles;
            var bodies = new GameObject[3];
            for (int l = 0; l < 3; l++)
            {
                var gold = InfantryKit.GoldOf(l + 1);
                bodies[l] = InfantryKit.Body(rig, "Spearman_Body_L" + (l + 1), gold, InfantryKit.SpearmanBody(l + 1), log);
                // The scutum at every level: a round shield made him read as
                // the Swordsman (developer, 2026-11-13).
                InfantryKit.Weld(bodies[l], rig,
                    InfantryKit.Piece("Spearman_Helmet_L" + (l + 1), head, Vector3.zero, Vector3.zero, gold, InfantryKit.SpearmanHelmet(l + 1), log),
                    InfantryKit.Piece("Spearman_Shield_L" + (l + 1), elbow, ShieldPos, shieldEuler, gold, InfantryKit.SpearmanShield(l + 1), log));
                bodies[l].transform.SetParent(legion.transform, false);
                bodies[l].SetActive(l == 0);
            }
            var spears = Group("Spears", hand);
            var weapons = new GameObject[5];
            for (int i = 0; i < weapons.Length; i++)
            {
                weapons[i] = InfantryKit.Piece("Spear_" + InfantryKit.TierNames[i], spears.transform, SpearPos, SpearEuler, InfantryKit.GoldOfTier(i), InfantryKit.Spear(i), log);
                HoldAtMiddle(weapons[i]);
                weapons[i].SetActive(i == 0);
            }
            InfantryKit.Finish(rig);

            var kit = root.AddComponent<UnitAgeKit>();
            kit.firstAge = new[] { levy, levyHand };
            kit.aged = new[] { legion, spears };
            kit.levelLadderId = "Barracks";
            kit.bodies = bodies;
            kit.weapons = weapons;
            kit.firstAgeWeapons = levyWeapons;
            kit.weaponTechs = new[] { "StoneWeapons", "IronWeapons", "VeilstoneWeapons", "ShardInfusedWeapons" };

            // The cloth cells take the player's colour at spawn.
            root.AddComponent<UnitPlayerColor>();
            legion.SetActive(false);
            spears.SetActive(false);
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
    }
}
