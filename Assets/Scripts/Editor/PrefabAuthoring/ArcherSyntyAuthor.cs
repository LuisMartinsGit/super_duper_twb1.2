// ArcherSyntyAuthor.cs
// Builds the Alanthor Archer from Synty assets only (developer, 2026-11-13:
// "the archers must be made using Synty assets"). It replaces the figure
// PresentationSpawnSystem used to build from primitives (ArcherVisual).
//
//   Body    POLYGON Ancient Empire  SM_Chr_Worker_Male_01   belted tunic, no armour
//   Bow     POLYGON Fantasy Kingdom SM_Prop_Bow_02          in the right hand (the aiming hand of the clips)
//   Quiver  POLYGON Fantasy Kingdom SM_Prop_Quiver_02       on the back
// on clips from the sets the Spearman uses (developer, 2026-11-13): the
// Kevin Iglesias bow idle, release and death, and the ExplosiveLLC Warrior
// Pack Bundle 2 Archer run. Archer.controller is rebuilt in place (its guid is
// kept); the Mixamo clips the unit used to carry are deleted. The tunic is saturated cloth on the
// pack atlas, so UnitPlayerColor draws it in the player's colour.
//
//   Waning Border > Art > Units > Author Synty Archer
//       writes Archer.prefab in the unit folder and points Archer.asset at it
//   Waning Border > Art > Units > Render Synty Archer Sheets
//       Temp/archer/sheet_<clip>.png - five poses a clip, to check the grips
//
// The body stands as tall as the Worker (one factor on the prefab root, as
// the Spearman); how large units are DRAWN is UnitWorldScale.asset.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.EditorTools
{
    public static class ArcherSyntyAuthor
    {
        const string UnitDir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/Units/Archer/";
        const string PrefabPath = UnitDir + "Archer.prefab";
        const string ControllerPath = UnitDir + "Archer.controller";
        const string SoPath = UnitDir + "Archer.asset";
        const string WorkerPrefab = "Assets/GameData/TechTree/Age0/Buildings/Fortress/Units/Worker/Worker.prefab";
        const string AE = "Assets/Synty/PolygonAncientEmpire/Prefabs/";
        const string FK = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string Body = AE + "Characters/SM_Chr_Worker_Male_01.prefab";
        const string Bow = FK + "Weapons/SM_Prop_Bow_02.prefab";
        const string Quiver = FK + "Weapons/SM_Prop_Quiver_02.prefab";
        const string Out = "Temp/archer/";

        const string ArcherSet = "Assets/Tools/ExplosiveLLC/Warrior Pack Bundle 2 FREE/Archer Warrior Mecanim Animation Pack/Animations/";
        const string KI = "Assets/Tools/Kevin Iglesias/Human Animations/Animations/Male/";

        // Idle, Move, Attack, Death - the order LegionarySpearmanAuthor.BuildController takes.
        // The Kevin Iglesias bow clips sit correctly on the Synty body; the
        // ExplosiveLLC Archer idle and attack do not (an arm behind the head,
        // no draw pose), so only its run is used.
        static readonly string[] Clips =
        {
            KI + "Combat/Bow/HumanM@BowIdle01.fbx",
            ArcherSet + "Archer@Run.FBX",
            KI + "Combat/Bow/HumanM@BowShot01 - Release.fbx",
            KI + "Combat/HumanM@Death01.fbx",
        };
        static readonly string[] SheetClips = { Clips[0], Clips[1], Clips[2] };

        // Where the kit sits on the rig (bone-local).
        // These clips aim with the RIGHT hand. The bow's length is its own Z;
        // turned onto the hand's Y it stands upright at full draw with the
        // string toward the body.
        static Vector3 BowPos = new Vector3(0.09f, 0.03f, 0f);
        static Vector3 BowEuler = new Vector3(90f, 0f, 0f);
        // The quiver is placed in the RIG'S frame (the body faces +Z): behind
        // the upper spine, leaning toward the right shoulder.
        static Vector3 QuiverOffset = new Vector3(0.05f, -0.12f, -0.17f);
        static Vector3 QuiverEuler = new Vector3(-8f, 0f, -24f);
        static float QuiverScale = 0.7f;
        static string BowHand = "Hand_R";

        /// <summary>Optional overrides for tuning without a recompile:
        /// Temp/archer/tune.txt, lines "name x y z" (bowpos, boweuler,
        /// quiveroffset, quivereuler) and "quiverscale s".</summary>
        static void ReadTune()
        {
            const string f = "Temp/archer/tune.txt";
            if (!File.Exists(f)) return;
            foreach (var raw in File.ReadAllLines(f))
            {
                var p = raw.Trim().Split(' ');
                if (p.Length < 2) continue;
                float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
                switch (p[0])
                {
                    case "bowpos": BowPos = new Vector3(F(1), F(2), F(3)); break;
                    case "boweuler": BowEuler = new Vector3(F(1), F(2), F(3)); break;
                    case "quiveroffset": QuiverOffset = new Vector3(F(1), F(2), F(3)); break;
                    case "quivereuler": QuiverEuler = new Vector3(F(1), F(2), F(3)); break;
                    case "quiverscale": QuiverScale = F(1); break;
                    case "bowhand": BowHand = p[1]; break;
                }
            }
        }

        [MenuItem("Waning Border/Art/Units/Author Synty Archer")]
        public static void Author()
        {
            var log = new StringBuilder();
            RuntimeAnimatorController controller = LegionarySpearmanAuthor.BuildController(ControllerPath, Clips, log);
            // The clips the unit used to carry (Mixamo) are not used any more.
            if (AssetDatabase.IsValidFolder(UnitDir + "Animations"))
            {
                AssetDatabase.DeleteAsset(UnitDir + "Animations");
                log.AppendLine("deleted " + UnitDir + "Animations");
            }
            var go = BuildRig(log, controller);
            try
            {
                float body = LegionarySpearmanAuthor.BodyHeight(go);
                var workerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPrefab);
                if (workerAsset != null)
                {
                    var worker = (GameObject)Object.Instantiate(workerAsset);
                    worker.transform.localScale = Vector3.one;
                    float workerHeight = LegionarySpearmanAuthor.BodyHeight(worker);
                    Object.DestroyImmediate(worker);
                    if (body > 0.1f && workerHeight > 0.1f)
                    {
                        float k = workerHeight / body;
                        go.transform.localScale = Vector3.one * k;
                        log.AppendLine($"worker body {workerHeight:0.000} m, archer body {body:0.000} m -> root scale {k:0.000}");
                    }
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                var so = AssetDatabase.LoadMainAssetAtPath(SoPath);
                var ser = new SerializedObject(so);
                ser.FindProperty("prefab").objectReferenceValue = saved;
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
                Debug.Log("[ArcherSyntyAuthor] " + log);
            }
        }

        static GameObject BuildRig(StringBuilder log, RuntimeAnimatorController controller)
        {
            ReadTune();
            var root = new GameObject("Archer");
            var body = LegionarySpearmanAuthor.Character(Body, "Body", root.transform, controller, false);

            var bones = new List<string>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) bones.Add(t.name);
            log.AppendLine("bones: " + string.Join(" ", bones));

            var bow = LegionarySpearmanAuthor.Attach(Bow, "Bow", LegionarySpearmanAuthor.Bone(body, BowHand, log), BowPos, BowEuler, null);
            var spine = LegionarySpearmanAuthor.Bone(body, "Spine_03", log);
            var quiver = LegionarySpearmanAuthor.Attach(Quiver, "Quiver", spine, Vector3.zero, Vector3.zero, null);
            quiver.transform.rotation = root.transform.rotation * Quaternion.Euler(QuiverEuler);
            quiver.transform.localScale = Vector3.one * QuiverScale;
            // Its middle on the point behind the spine.
            Vector3 want = spine.position + root.transform.rotation * QuiverOffset;
            quiver.transform.position += want - LegionaryShot.BoundsOf(quiver).center;
            log.AppendLine($"bow bounds {LegionaryShot.BoundsOf(bow).size}, quiver bounds {LegionaryShot.BoundsOf(quiver).size}");

            // The tunic's saturated cloth takes the player's colour at spawn.
            root.AddComponent<UnitPlayerColor>();
            return root;
        }

        [MenuItem("Waning Border/Art/Units/Render Synty Archer Sheets")]
        public static void Sheets()
        {
            Directory.CreateDirectory(Out);
            var log = new StringBuilder();
            var stage = new GameObject("ArcherSheetStage");
            try
            {
                const int N = 5;
                var rig = new GameObject[N];
                for (int i = 0; i < N; i++)
                {
                    rig[i] = BuildRig(log, null);
                    rig[i].transform.SetParent(stage.transform, false);
                }
                var baked = new List<GameObject>();
                string[] names = { "idle", "move", "attack" };
                for (int k = 0; k < SheetClips.Length; k++)
                {
                    foreach (var b in baked) Object.DestroyImmediate(b);
                    baked.Clear();
                    var clip = LegionarySpearmanAuthor.Clip(SheetClips[k]);
                    if (clip == null) { log.AppendLine("MISSING " + SheetClips[k]); continue; }
                    for (int i = 0; i < N; i++)
                    {
                        var shown = rig[i].transform.Find("Body").gameObject;
                        clip.SampleAnimation(shown, clip.length * i / (clip.isLooping ? N : N - 1));
                        shown.transform.localPosition = Vector3.zero;
                        shown.transform.localRotation = Quaternion.identity;
                        rig[i].transform.position = new Vector3(-i * 1.9f, 0f, 0f);
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
                    LegionaryShot.Shoot(b2, 205f, 10f, 2200, 700, Out + "sheet_" + names[k] + ".png", true);
                    LegionaryShot.Shoot(b2, 25f, 10f, 2200, 700, Out + "sheet_" + names[k] + "_back.png", true);
                    log.AppendLine($"{names[k]}: {clip.name} {clip.length:0.00}s loop={clip.isLooping}");
                }
            }
            finally
            {
                Object.DestroyImmediate(stage);
                File.WriteAllText(Out + "sheets.txt", log.ToString());
            }
        }
    }
}
