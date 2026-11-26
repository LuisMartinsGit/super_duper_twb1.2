// ArcheryKit.cs
// The Archery Range's units, mixed from the three Synty kits with InfantryKit's
// fusing and palette (developer, 2026-11-25: "do the same for the archers. try
// to make longbowmen look like Yumi archers"). The Archer, the Crossbowman and
// the Longbowman replace the Ancient Empire worker, a green placeholder and an
// older character.
//
//   Level    Archery Range 1 / 2 / 3: a body and headgear each
//   Bow      Archer: plain, Recurve Bows, Compound Bows
//   Arrows   the quiver (or the bolt) follows Stone / Iron / Veilstone Arrows
//   Yumi     a kit bow reshaped: the tall asymmetric bow, gripped a third of
//            the way up, lacquered and bound with gold
//
//   Waning Border > Art > Units > Author Archery Units     (with the infantry:
//       the palette is shared, so both are written every time)
//   Waning Border > Art > Units > Render Archery Units     (Temp/archery/)

using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Rendering;
using Object = UnityEngine.Object;
using L = TheWaningBorder.EditorTools.LegionarySpearmanAuthor;

namespace TheWaningBorder.EditorTools
{
    public static partial class InfantryKit
    {
        const string RangeUnits = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/Units/";
        const string KI = "Assets/Tools/Kevin Iglesias/Human Animations/Animations/Male/";
        const string ArcherSet = "Assets/Tools/ExplosiveLLC/Warrior Pack Bundle 2 FREE/Archer Warrior Mecanim Animation Pack/Animations/";
        const string KMat = "Assets/Synty/PolygonFantasyKingdom/Materials/Alts/PolygonFantasyKingdom_Mat_01_A.mat";
        const string JGeneric = JChr + "Generic_Male_01.prefab";

        // The Kevin Iglesias bow clips aim with the RIGHT hand.
        static readonly string[] BowClips =
        {
            KI + "Combat/Bow/HumanM@BowIdle01.fbx",
            ArcherSet + "Archer@Run.FBX",
            KI + "Combat/Bow/HumanM@BowShot01 - Release.fbx",
            KI + "Combat/HumanM@Death01.fbx",
        };
        // The Longbowman keeps his own controller (draw, then loose, locked to
        // his rate of fire); its clips hold the bow in the LEFT hand, as a yumi is.
        const string LongbowAnim = RangeUnits + "Longbowman/Animations/";
        static readonly string[] YumiClips =
        {
            LongbowAnim + "unarmed idle 01.fbx", LongbowAnim + "standing run forward.fbx",
            LongbowAnim + "standing draw arrow.fbx", LongbowAnim + "standing aim recoil.fbx", LongbowAnim + "standing death backward 01.fbx",
        };

        // Bone-local placements; Temp/archery/tune.txt overrides ("name x y z").
        static Vector3 BowAt = new Vector3(0.09f, 0.03f, 0f), BowTurn = new Vector3(90f, 0f, 0f);
        static Vector3 YumiAt = new Vector3(-0.09f, -0.03f, 0f), YumiTurn = new Vector3(0f, 180f, 0f);
        static Vector3 CrossAt = new Vector3(0.09f, 0.03f, 0f), CrossTurn = new Vector3(0f, -90f, 0f);
        static Vector3 QuiverAt = new Vector3(0.05f, -0.12f, -0.19f), QuiverTurn = new Vector3(-8f, 0f, -24f);
        static Vector3 HipQuiverAt = new Vector3(0.16f, 0.02f, -0.18f), HipQuiverTurn = new Vector3(-58f, 0f, 0f);
        static Vector3 YumiSpan = new Vector3(0.95f, 1.95f, 1.15f);   // below the grip, above it, depth

        static void ReadTune()
        {
            const string f = "Temp/archery/tune.txt";
            if (!File.Exists(f)) return;
            foreach (var raw in File.ReadAllLines(f))
            {
                var p = raw.Trim().Split(' ');
                if (p.Length < 4) continue;
                float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
                var v = new Vector3(F(1), F(2), F(3));
                switch (p[0])
                {
                    case "bowat": BowAt = v; break;
                    case "bowturn": BowTurn = v; break;
                    case "yumiat": YumiAt = v; break;
                    case "yumiturn": YumiTurn = v; break;
                    case "yumispan": YumiSpan = v; break;
                    case "crossat": CrossAt = v; break;
                    case "crossturn": CrossTurn = v; break;
                    case "quiverat": QuiverAt = v; break;
                    case "quiverturn": QuiverTurn = v; break;
                    case "hipat": HipQuiverAt = v; break;
                    case "hipturn": HipQuiverTurn = v; break;
                }
            }
        }

        // ── Recipes ────────────────────────────────────────────────────

        static Part[] ArcherBody(int level) => level switch
        {
            // A kimono over Roman sandals.
            1 => new[] { Body(JGeneric, Zone.All & ~(Zone.Shin | Zone.Thigh)), Body(KPeasantOf, Zone.Thigh), Body(RSoldier, Zone.Shin) },
            // The ashigaru cuirass and sleeves over wide hakama.
            2 => new[] { Body(JSoldier, Zone.Head | Zone.Torso | Zone.UpperArm | Zone.ForeArm), Body(JGeneric, Zone.Thigh | Zone.Shin) },
            // Laced plate over hakama, gilded Roman shoulders.
            _ => new[] { Body(Samurai2, Zone.Head | Zone.Torso | Zone.UpperArm | Zone.ForeArm), Body(JGeneric, Zone.Thigh | Zone.Shin),
                new Part { prefab = RSoldier, material = RMat, islands = RomanShoulders, paint = (c, p) => IsRole(c, Role.Steel) ? Gilt(c) : null } },
        };

        const string KPeasantOf = KPeasant;

        static Part[] ArcherHead(int level) => level switch
        {
            1 => new[] { Samurai("Hair_05") },
            // The ashigaru's conical hat, in the player's lacquer, then gilt under a plume.
            2 => new[] { Jingasa(false) },
            _ => new[] { Jingasa(true) },
        };

        static Part Jingasa(bool gilt) => new Part
        {
            prefab = JAtt + "Helmet_05.prefab", headSpace = true,
            paint = (c, p) =>
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (s < 0.3f) return gilt ? Cloth(Mathf.Lerp(0.6f, 0.95f, v)) : null;   // the neck cloth
                return gilt ? Gilt(c) : Cloth(Mathf.Lerp(0.5f, 0.85f, v));
            },
        };

        static Part[] CrossbowBody(int level) => level <= 2
            ? new[] { Body(KSoldier, Zone.Head | Zone.Torso | Zone.Thigh | Zone.Shin), Body(JSoldier, Zone.UpperArm | Zone.ForeArm), Shells(RSoldier, RomanShoulders) }
            : new[] { Body(KSoldier, Zone.Head | Zone.Thigh | Zone.Shin), Body(Samurai1, Zone.Torso | Zone.UpperArm | Zone.ForeArm),
                new Part { prefab = RSoldier, material = RMat, islands = RomanShoulders, paint = (c, p) => IsRole(c, Role.Steel) ? Gilt(c) : null } };

        // A Corinthian helm; at the top level with the long crest.
        static Part[] CrossbowHead(int level) => new[] { new Part { prefab = RAtt + (level <= 2 ? "Helmet_08" : "Helmet_05") + ".prefab", material = RMat } };

        // The Yumi archer: the gold cuirass over kimono and hakama, one great
        // samurai shoulder on the bow arm, a golden laurel over the topknot.
        static Part[] YumiBody() => new[]
        {
            Body(JGeneric, Zone.Head | Zone.UpperArm | Zone.ForeArm | Zone.Thigh | Zone.Shin),
            Body(Samurai1, Zone.Torso),
            Shells(Samurai3, 1, 2, 3, 7, 8),
        };

        static Part[] YumiHead() => new[] { Samurai("Hair_05"), Roman("Crown_Leaf_02", null, (c, p) => Gilt(c)) };

        static Part[] Bow(int tier) => tier switch
        {
            0 => new[] { Wep(K + "Weapons/K_SM_Prop_Bow_02.prefab") },
            1 => new[] { Wep(K + "Weapons/K_SM_Prop_Bow_01.prefab") },
            _ => new[] { Wep(K + "Weapons/K_SM_Prop_Bow_01.prefab", 1.08f, (c, p) => V(c) > 0.62f ? Gilt(c) : null) },
        };

        /// <summary>A kit bow drawn out into a yumi (its length is its own Y).</summary>
        static Part[] Yumi()
        {
            float Warp(float y) => (y < 0f ? y * YumiSpan.x : y * YumiSpan.y);
            return new[]
            {
                new Part
                {
                    prefab = K + "Weapons/K_SM_Prop_Bow_02.prefab",
                    warp = v => new Vector3(v.x, Warp(v.y), v.z * YumiSpan.z),
                    paint = (c, p) =>
                    {
                        float z = p.y;
                        if (Mathf.Abs(z) < 0.16f) return Is(Role.Matte, 226, 220, 202);              // the grip's wrap
                        if (c.r < 90 && c.g < 80) return null;                                       // the string
                        // Lacquer, bound with gold where the limbs bend.
                        return Mathf.Abs(z) > 0.6f || (Mathf.Abs(z) > 0.16f && Mathf.Abs(z) < 0.26f)
                            ? Is(Role.Gold, 250, 188, 84) : Is(Role.Matte, 52, 26, 24);
                    },
                },
            };
        }

        /// <summary>The quiver of an arrow tier: the fletchings turn stone grey, then white, then veilstone.</summary>
        static Part[] Quiver(int tier, bool thin) => new[]
        {
            new Part
            {
                prefab = K + "Weapons/K_SM_Prop_Quiver_0" + (thin ? "1" : "2") + ".prefab",
                paint = (c, p) =>
                {
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    if (s < 0.3f && v > 0.5f) return tier >= 3 ? Is(Role.Veil, 70, 190, 220) : tier == 2 ? Is(Role.Matte, 236, 232, 222) : tier == 1 ? Is(Role.Matte, 120, 118, 112) : null;
                    if (c.r > 100 && c.r > c.g * 1.4f) return Cloth(0.62f);   // the case, in the player's leather
                    return null;
                },
            },
        };

        /// <summary>The crossbow's dark iron bow, by arrow tier.</summary>
        static (Role, Color32)? Prod(Color32 c, int tier)
        {
            Color.RGBToHSV(c, out _, out float s, out _);
            if (s >= 0.25f) return null;
            byte v = (byte)Mathf.Clamp(Mathf.Max(c.r, Mathf.Max(c.g, c.b)) + 70, 0, 235);
            return Edge(new Color32(v, v, v, 255), tier);
        }

        static Part[] Crossbow(int tier) => new[]
        {
            new Part
            {
                prefab = K.Replace("Prefabs/", "Models/") + "SM_Prop_Crossbow_01.fbx", material = KMat,
                paint = (c, p) => Prod(c, tier),
            },
        };

        // ── Prefabs ────────────────────────────────────────────────────

        static readonly string[] ArrowTechs = { "StoneTippedArrows", "IronTippedArrows", "VeilstoneTippedArrows" };

        static GameObject[] Quivers(GameObject root, GameObject rig, bool thin, bool hip, StringBuilder log)
        {
            var bone = L.Bone(rig, hip ? "Hips" : "Spine_03", log);
            var set = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                var q = Piece((thin ? "QuiverThin_A" : "Quiver_A") + i, bone, Vector3.zero, Vector3.zero, GoldOfTier(i), Quiver(i, thin), log);
                q.transform.rotation = root.transform.rotation * Quaternion.Euler(hip ? HipQuiverTurn : QuiverTurn);
                q.transform.localScale = Vector3.one * 0.7f;
                // Its middle on the point behind the spine (the rig faces +Z).
                q.transform.position += bone.position + root.transform.rotation * (hip ? HipQuiverAt : QuiverAt) - LegionaryShot.BoundsOf(q).center;
                q.SetActive(i == 0);
                set[i] = q;
            }
            return set;
        }

        static GameObject[] OnePerLevel(int n, Func<int, GameObject> make)
        {
            var set = new GameObject[n];
            for (int i = 0; i < n; i++) { set[i] = make(i + 1); set[i].SetActive(i == 0); }
            return set;
        }

        static UnitAgeKit Kit(GameObject root, GameObject rig)
        {
            var kit = root.AddComponent<UnitAgeKit>();
            kit.aged = new[] { rig };
            kit.levelLadderId = "ArcheryRange";
            kit.quiverTechs = ArrowTechs;
            root.AddComponent<UnitPlayerColor>();
            return kit;
        }

        internal static GameObject ArcherRig(StringBuilder log, RuntimeAnimatorController c)
        {
            var root = new GameObject("Archer");
            var rig = L.Skeleton("Body", root.transform, c);
            var head = L.Bone(rig, "Head", log);
            var hand = L.Bone(rig, "Hand_R", log);
            var kit = Kit(root, rig);
            kit.bodies = OnePerLevel(3, l =>
            {
                var b = Body(rig, "Archer_Body_L" + l, GoldOf(l), ArcherBody(l), log);
                Weld(b, rig, Piece("Archer_Head_L" + l, head, Vector3.zero, Vector3.zero, GoldOf(l), ArcherHead(l), log));
                return b;
            });
            kit.weapons = OnePerLevel(3, t => Piece("Bow_T" + (t - 1), hand, BowAt, BowTurn, GoldOf(t), Bow(t - 1), log));
            kit.weaponTechs = new[] { "RecurveBows", "CompoundBows" };
            kit.quivers = Quivers(root, rig, false, false, log);
            Finish(rig);
            return root;
        }

        internal static GameObject CrossbowRig(StringBuilder log, RuntimeAnimatorController c)
        {
            var root = new GameObject("Crossbowman");
            var rig = L.Skeleton("Body", root.transform, c);
            var head = L.Bone(rig, "Head", log);
            var hand = L.Bone(rig, "Hand_R", log);
            var kit = Kit(root, rig);
            // Trained from level 2 on: levels 1 and 2 share the look.
            var b2 = Body(rig, "Crossbow_Body_L2", Role.Brass, CrossbowBody(2), log);
            var b3 = Body(rig, "Crossbow_Body_L3", Role.Gold, CrossbowBody(3), log);
            Weld(b2, rig, Piece("Crossbow_Head_L2", head, Vector3.zero, Vector3.zero, Role.Brass, CrossbowHead(2), log));
            Weld(b3, rig, Piece("Crossbow_Head_L3", head, Vector3.zero, Vector3.zero, Role.Gold, CrossbowHead(3), log));
            b3.SetActive(false);
            kit.bodies = new[] { b2, b2, b3 };
            kit.weapons = OnePerLevel(4, t => Piece("Crossbow_A" + (t - 1), hand, CrossAt, CrossTurn, GoldOfTier(t - 1), Crossbow(t - 1), log));
            kit.weaponTechs = ArrowTechs;
            kit.quivers = Quivers(root, rig, true, true, log);
            Finish(rig);
            return root;
        }

        internal static GameObject YumiRig(StringBuilder log, RuntimeAnimatorController c)
        {
            var root = new GameObject("Longbowman");
            var rig = L.Skeleton("Body", root.transform, c);
            var kit = Kit(root, rig);
            kit.bodies = new[] { Body(rig, "Yumi_Body", Role.Gold, YumiBody(), log) };
            Weld(kit.bodies[0], rig, Piece("Yumi_Head", L.Bone(rig, "Head", log), Vector3.zero, Vector3.zero, Role.Gold, YumiHead(), log));
            kit.weapons = new[] { Piece("Yumi", L.Bone(rig, "Hand_L", log), YumiAt, YumiTurn, Role.Gold, Yumi(), log) };
            kit.quivers = Quivers(root, rig, true, false, log);
            Finish(rig);
            return root;
        }

        static void Save(GameObject go, string dir, string unit, RuntimeAnimatorController controller, StringBuilder log)
        {
            try
            {
                float body = L.BodyHeight(go);
                float want = L.UnitHeight;
                go.transform.localScale = Vector3.one * (want / body);
                Grow(go);
                log.AppendLine($"{unit}: body {body:0.000} -> root scale {want / body:0.000}");
                var saved = PrefabUtility.SaveAsPrefabAsset(go, dir + unit + ".prefab");
                var so = AssetDatabase.LoadMainAssetAtPath(dir + unit + ".asset");
                var ser = new SerializedObject(so);
                ser.FindProperty("prefab").objectReferenceValue = saved;
                ser.FindProperty("animatorController").objectReferenceValue = controller;
                ser.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
            }
            finally { Object.DestroyImmediate(go); }
        }

        internal static void AuthorArchery()
        {
            ReadTune();
            var log = new StringBuilder();
            var archer = L.BuildController(RangeUnits + "Archer/Archer.controller", BowClips, log);
            var cross = L.BuildController(RangeUnits + "Crossbowman/Crossbowman.controller", BowClips, log);
            var yumi = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(RangeUnits + "Longbowman/Longbowman.controller");
            // At rest he holds the yumi as after the shot, upright at his side:
            // hanging from a lowered hand it swept a man's length behind him.
            foreach (var st in ((UnityEditor.Animations.AnimatorController)yumi).layers[0].stateMachine.states)
                if (st.state.name == "Idle")
                {
                    st.state.motion = L.Clip(YumiClips[3]);
                    st.state.speed = 0f;
                    st.state.cycleOffset = 0.97f;
                    EditorUtility.SetDirty(st.state);
                }
            Save(ArcherRig(log, archer), RangeUnits + "Archer/", "Archer", archer, log);
            Save(CrossbowRig(log, cross), RangeUnits + "Crossbowman/", "Crossbowman", cross, log);
            Save(YumiRig(log, yumi), RangeUnits + "Longbowman/", "Longbowman", yumi, log);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Temp/archery");
            File.WriteAllText("Temp/archery/author.txt", log.ToString());
        }

        [MenuItem("Waning Border/Art/Units/Author Archery Units")]
        static void AuthorArcheryMenu() => AuthorAll();

        [MenuItem("Waning Border/Art/Units/Render Archery Units")]
        public static void RenderArchery()
        {
            Directory.CreateDirectory("Temp/archery");
            var states = new[] { (1, 0, 0), (2, 1, 1), (3, 2, 3), (3, 2, 3), (3, 2, 3) };
            RenderClips("archer", "Archer", BowClips, states);
            RenderClips("crossbow", "Crossbowman", BowClips, new[] { (2, 0, 0), (2, 1, 1), (2, 2, 2), (3, 3, 3), (3, 3, 3) });
            RenderClips("yumi", "Longbowman", YumiClips, new[] { (3, 0, 0), (3, 0, 1), (3, 0, 2), (3, 0, 3), (3, 0, 3) });
        }

        static void RenderClips(string tag, string unit, string[] clips, (int level, int weapon, int quiver)[] states)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(RangeUnits + unit + "/" + unit + ".prefab");
            var stage = new GameObject("ArcheryStage");
            try
            {
                for (int c = 0; c < clips.Length; c++)
                {
                    for (int i = stage.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(stage.transform.GetChild(i).gameObject);
                    var clip = L.Clip(clips[c]);
                    for (int i = 0; i < states.Length; i++)
                    {
                        var go = Object.Instantiate(asset, stage.transform);
                        go.transform.localScale = Vector3.one;
                        var kit = go.GetComponent<UnitAgeKit>();
                        kit.Apply(true, states[i].level, states[i].weapon, 0, instant: true, quiver: states[i].quiver);
                        go.GetComponent<UnitPlayerColor>().Apply(new Color(0.12f, 0.35f, 0.85f));
                        var shown = Rig(kit);
                        clip.SampleAnimation(shown, clip.length * i / (states.Length - 1f) * 0.98f);
                        shown.transform.localPosition = Vector3.zero;
                        shown.transform.localRotation = Quaternion.identity;
                        UnitGearIcons.Bake(go);
                        go.transform.position = new Vector3(-i * 1.9f, 0f, 0f);
                    }
                    int n = states.Length;
                    var b = new Bounds(new Vector3(-(n - 1) * 0.95f, 1.25f, 0f), new Vector3(n * 1.9f, 2.9f, 0.2f));
                    string png = $"Temp/archery/{tag}_{c}";
                    LegionaryShot.Shoot(b, 205f, 10f, 64, 64, "Temp/kit_warm.png", true);
                    LegionaryShot.Shoot(b, 205f, 10f, n * 360, 640, png + ".png", true);
                    if (c == 0) LegionaryShot.Shoot(b, 25f, 10f, n * 360, 640, png + "_back.png", true);
                }
            }
            finally { Object.DestroyImmediate(stage); }
        }
    }
}
