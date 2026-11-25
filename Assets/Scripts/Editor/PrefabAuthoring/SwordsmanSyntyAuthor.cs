// SwordsmanSyntyAuthor.cs
// Builds the Alanthor Swordsman from Synty assets only (developer,
// 2026-11-13: "author a swordsman from the synty assets (not present at
// age 0)"). It replaces the figure PresentationSpawnSystem used to build from
// primitives (SwordsmanVisual).
//
//   Level    Garrison 1 / 2 / 3: a body, a helmet and a shield each
//   Weapon   a sword per weapon tier (none, Stone, Iron, Veilstone, Shard)
// all mixed from the three Synty kits by InfantryKit's recipes,
// on the Knight clips the Spearman uses (sword and shield), the Kevin
// Iglesias death. The gear follows the faction through UnitAgeKit, which has
// no first-age kit here: the unit does not exist before a culture.
//
//   Waning Border > Art > Units > Author Garrison Infantry  (both units)
//   Waning Border > Art > Units > Render Garrison Infantry  (Temp/infantry/)

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
        const string Out = "Temp/swordsman/";

        // The sword in the fist: its blade along the hand, as the spear.
        static Vector3 SwordPos = new Vector3(0.09f, 0.03f, 0f);
        static Vector3 SwordEuler = new Vector3(-90f, 0f, 0f);

        internal static readonly string[] Clips = L.Clips;

        public static void Author()
        {
            var log = new StringBuilder();
            var controller = L.BuildController(ControllerPath, Clips, log);
            var go = BuildRig(log, controller);
            try
            {
                float body = L.BodyHeight(go);
                if (body > 0.1f) go.transform.localScale = Vector3.one * (L.UnitHeight / body);
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

        internal static GameObject BuildRig(StringBuilder log, RuntimeAnimatorController controller)
        {
            var root = new GameObject("Swordsman");
            var body = L.Skeleton("Body", root.transform, controller);
            var head = L.Bone(body, "Head", log);
            var hand = L.Bone(body, "Hand_R", log);
            var elbow = L.Bone(body, "Elbow_L", log);
            Vector3 shieldEuler = Quaternion.LookRotation(L.ShieldFace, L.ShieldUp).eulerAngles;
            var bodies = new GameObject[3];
            var helmets = new GameObject[3];
            var shields = new GameObject[3];
            for (int l = 0; l < 3; l++)
            {
                var gold = InfantryKit.GoldOf(l + 1);
                bodies[l] = InfantryKit.Body(body, "Swordsman_Body_L" + (l + 1), gold, InfantryKit.SwordsmanBody(l + 1), log);
                helmets[l] = InfantryKit.Piece("Swordsman_Helmet_L" + (l + 1), head, Vector3.zero, Vector3.zero, gold, InfantryKit.SwordsmanHelmet(l + 1), log);
                shields[l] = InfantryKit.Piece("Swordsman_Shield_L" + (l + 1), elbow, L.ShieldPos, shieldEuler, gold, InfantryKit.SwordsmanShield(l + 1), log);
            }
            var swords = new GameObject[5];
            for (int i = 0; i < swords.Length; i++)
                swords[i] = InfantryKit.Piece("Sword_" + InfantryKit.TierNames[i], hand, SwordPos, SwordEuler, InfantryKit.GoldOfTier(i), InfantryKit.Sword(i), log);

            // The cape is his alone: it is what tells him from the Spearman
            // at a glance (developer, 2026-11-13).
            L.AttachCape(root, body, log);

            var kit = root.AddComponent<UnitAgeKit>();
            kit.aged = new[] { body };
            kit.levelLadderId = "Barracks";
            kit.bodies = bodies;
            kit.helmets = helmets;
            kit.shields = shields;
            kit.weapons = swords;
            kit.weaponTechs = new[] { "StoneWeapons", "IronWeapons", "VeilstoneWeapons", "ShardInfusedWeapons" };
            for (int i = 1; i < 3; i++) { bodies[i].SetActive(false); helmets[i].SetActive(false); shields[i].SetActive(false); }
            for (int i = 1; i < swords.Length; i++) swords[i].SetActive(false);

            root.AddComponent<UnitPlayerColor>();
            return root;
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
        // Cloth that is the player's colour in a match is drawn neutral here.
        static readonly Color Neutral = new Color(0.78f, 0.74f, 0.66f);

        /// <summary>Sample a pose onto the shown body and bake its skinned
        /// meshes (the editor skins once a frame, not per render).</summary>
        internal static void Pose(GameObject rig, GameObject shown, AnimationClip clip, Transform stage)
        {
            if (clip != null) clip.SampleAnimation(shown, 0f);
            shown.transform.localPosition = Vector3.zero;
            shown.transform.localRotation = Quaternion.identity;
            Bake(rig);
        }

        /// <summary>Replace each shown skinned mesh by a plain one in its current pose.</summary>
        internal static void Bake(GameObject rig)
        {
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
                Object.DestroyImmediate(smr);
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
                for (int l = 1; l <= 3; l++)
                    Unit(UnitsDir + "Alanthor_Archer_L" + l + ".png", InfantryKit.ArcherRig(log, null), true, l);
                for (int l = 2; l <= 3; l++)
                    Unit(UnitsDir + "Alanthor_Crossbowman_L" + l + ".png", InfantryKit.CrossbowRig(log, null), true, l);
                InfantryKit.EliteIcons((id, rig) => Unit(UnitsDir + id + "_L0.png", rig, true, 1), log);
                Unit(UnitsDir + "Alanthor_Longbowman_L3.png", InfantryKit.YumiRig(log, null), true, 3);

                // The weapons of each tier: the spear's head and the sword,
                // crossed - the tech upgrades both.
                string[] techs = { "StoneWeapons", "IronWeapons", "VeilstoneWeapons", "ShardInfusedWeapons" };
                for (int tierIndex = 1; tierIndex <= techs.Length; tierIndex++)
                {
                    string tech = techs[tierIndex - 1];
                    var gold = InfantryKit.GoldOfTier(tierIndex);
                    var go = InfantryKit.Piece("Spear_" + InfantryKit.TierNames[tierIndex], stage.transform, Vector3.zero, Vector3.zero, gold, InfantryKit.Spear(tierIndex), log);
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
                    var sword = InfantryKit.Piece("Sword_" + InfantryKit.TierNames[tierIndex], stage.transform, Vector3.zero, Vector3.zero, gold, InfantryKit.Sword(tierIndex), log);
                    {
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
                    Object.DestroyImmediate(sword);
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
