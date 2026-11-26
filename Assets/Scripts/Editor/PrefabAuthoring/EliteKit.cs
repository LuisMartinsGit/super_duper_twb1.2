// EliteKit.cs
// The sect units, the sect heroes and King Lexor, mixed from the three Synty
// kits with InfantryKit's fusing and palette (developer, 2026-11-25). They are
// elite soldiers: full gold, one fixed look, and drawn larger than everyone
// else (UnitWorldScale.asset eliteScale). ONLY the weapon changes, with the
// weapon techs ("sect units have no variance other than weapon tech").
//
//   Renewal     Scar Guard (two-handed axe), Surgeon (katana and knife)
//   Fortitude   Sallyguard (mace, tower shield), Warden (war hammer, gold scutum)
//   Prosperity  Bounty Hunter (two sickles), Guildmaster (rapier and dagger)
//   Antiquity   Artificer (crossbow), Archivist of Hours (crystal staff)
//   King Lexor  CavalryKit
//
// Written by Author Archery Units / Author Garrison Infantry (one run writes
// everything: the palette is shared).
//   Waning Border > Art > Units > Render Elite Units   (Temp/elite/)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        const string Sects = "Assets/GameData/TechTree/Sects/";
        const string Pack = "Assets/Tools/ExplosiveLLC/Warrior Pack Bundle 2 FREE/";
        const string KAtt = K + "Characters/Attachments/K_SM_Chr_Attach_";
        static readonly string[] MeleeTechs = { "StoneWeapons", "IronWeapons", "VeilstoneWeapons", "ShardInfusedWeapons" };

        static readonly string[] TwoHandClips = Set("2 Handed", "2Handed");
        static readonly string[] MageClips = Set("Mage", "Mage");
        static readonly string[] DuelClips =
        {
            KI + "Combat/1H/HumanM@CombatIdle1H01.fbx", KI + "Movement/Run/HumanM@Run01_Forward.fbx",
            KI + "Combat/1H/HumanM@Attack1H01_R.fbx", KI + "Combat/HumanM@Death01.fbx",
        };

        static string[] Set(string pack, string file) => new[]
        {
            Pack + pack + " Warrior Mecanim Animation Pack/Animations/" + file + "@Idle.FBX",
            Pack + pack + " Warrior Mecanim Animation Pack/Animations/" + file + "@Run.FBX",
            Pack + pack + " Warrior Mecanim Animation Pack/Animations/" + file + "@Attack1.FBX",
            KI + "Combat/HumanM@Death01.fbx",
        };

        sealed class Elite
        {
            public string name, dir, id;
            public string[] clips;
            public Func<Part[]> body, head, shield, back;
            public Func<int, Part[]> main, off;
            public bool ranged, keepController;
            public Role gold = Role.Gold;
            /// <summary>Weapon looks; 0: one per weapon tech.</summary>
            public int tiers;
            public Action<GameObject, GameObject, StringBuilder> after;
            public float backScale = 0.9f;
            public Vector3 backAt = new Vector3(0f, 0.16f, -0.17f);
            public Vector3 mainAt = new Vector3(0.09f, 0.03f, 0f), mainTurn = new Vector3(-90f, 0f, 0f);
            public Vector3 offAt = new Vector3(-0.09f, -0.03f, 0f), offTurn = new Vector3(90f, 0f, 0f);
        }

        /// <summary>A weapon's edge by tier: dull, stone, steel, veilstone, shard.</summary>
        static (Role, Color32)? Edge(Color32 c, int tier)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            bool edge = s < 0.25f && v > 0.3f;
            if (!edge) return null;
            switch (tier)
            {
                case 0: return (Role.Matte, new Color32((byte)(150 * (v + 0.2f)), (byte)(150 * (v + 0.2f)), (byte)(156 * (v + 0.2f)), 255));
                case 1: return (Role.Matte, new Color32((byte)(120 * (v + 0.1f)), (byte)(112 * (v + 0.1f)), (byte)(100 * (v + 0.1f)), 255));
                case 2: return (Role.Steel, new Color32((byte)(215 * Mathf.Min(1f, v + 0.2f)), (byte)(222 * Mathf.Min(1f, v + 0.2f)), (byte)(235 * Mathf.Min(1f, v + 0.2f)), 255));
                case 3: return Is(Role.Veil, 70, 180, 215);
                default: return Is(Role.Shard, 70, 190, 225);
            }
        }

        /// <summary>A weapon whose part above <paramref name="from"/> (its own length, from the grip) is the edge.</summary>
        static Part Armed(string path, int tier, float scale = 1f, float from = 0.14f) => Wep(path, scale, (c, p) => p.y > from ? Edge(c, tier) : null);

        static (Role, Color32)? White(Color32 c) => IsRole(c, Role.Cloth) ? (Role.Matte, new Color32((byte)(236 * (0.55f + 0.45f * V(c))), (byte)(232 * (0.55f + 0.45f * V(c))), (byte)(222 * (0.55f + 0.45f * V(c))), 255)) : null;
        static Part Paint(Part p, Func<Color32, Vector3, (Role, Color32)?> paint) { p.paint = paint; return p; }
        static Part Gilded(Part p) => Paint(p, (c, v) => IsRole(c, Role.Steel) ? Gilt(c) : null);

        static Part Mask(string n) => Samurai("Mask_0" + n);

        static Elite[] Elites() => new[]
        {
            new Elite
            {
                // The executioner's hood and bare harness, one Roman shoulder, samurai legs.
                name = "ScarGuard", id = "Sect_ScarGuard", dir = Sects + "Renewal/Units/ScarGuard/", clips = TwoHandClips,
                body = () => new[] { Body(KChr + "Headsman_01.prefab", Zone.Head | Zone.Torso), Body(Samurai2, Zone.UpperArm | Zone.ForeArm), Body(Samurai1, Zone.Thigh | Zone.Shin), Gilded(Shells(RSoldier, 4, 5)) },
                head = () => new[] { Mask("2") },
                main = t => new[] { Armed(K + "Weapons/K_SM_Wep_Axe_04.prefab", t, 1f, 0.5f) },
                mainAt = new Vector3(0.09f, 0.03f, 0.1f),
            },
            new Elite
            {
                // White samurai plate over hakama, the fox mask and a white mane.
                name = "Surgeon", id = "Sect_Surgeon", dir = Sects + "Renewal/Units/Surgeon/", clips = DuelClips,
                body = () => new[] { Paint(Body(Samurai3, Zone.Head | Zone.Torso), (c, p) => White(c)), Body(Samurai3, Zone.UpperArm | Zone.ForeArm), Body(JGeneric, Zone.Thigh | Zone.Shin), Paint(Shells(Samurai3, SamuraiShoulders), (c, p) => White(c)) },
                head = () => new[] { Mask("4"), Paint(Mane(), (c, p) => null) },
                main = t => new[] { Armed(J + "Weapons/J_SM_Wep_Sword_01.prefab", t, 0.9f) },
                off = t => new[] { Armed(J + "Weapons/J_SM_Wep_Knife_01.prefab", t) },
            },
            new Elite
            {
                // A walking wall: samurai plate, Roman shoulders, the knight's helm and mask.
                name = "Sallyguard", id = "Sect_Sallyguard", dir = Sects + "Fortitude/Units/Sallyguard/", clips = L.Clips,
                body = () => new[] { Body(KSoldier, Zone.Head | Zone.Torso | Zone.Shin), Body(Samurai1, Zone.UpperArm | Zone.ForeArm | Zone.Thigh), Gilded(Shells(RSoldier, RomanShoulders)) },
                head = () => new[] { new Part { prefab = KAtt + "Soldier_01.prefab", scale = 1.06f, move = new Vector3(0f, 0.065f, 0.03f) }, Mask("1") },
                shield = () => new[] { new Part { prefab = K + "Weapons/K_SM_Wep_Shield_04.prefab", scale = 1.25f } },
                main = t => new[] { Armed(K + "Weapons/K_SM_Wep_Mace_01.prefab", t, 0.95f, 0.45f) },
            },
            new Elite
            {
                // The horned daimyo of the walls, in gold, with the gilt scutum.
                name = "Warden", id = "Sect_Warden", dir = Sects + "Fortitude/Units/Warden/", clips = L.Clips,
                body = () => new[] { Body(Samurai1, Zone.All), Gilded(Shells(RSoldier, RomanShoulders)) },
                head = () => new[] { Samurai("Helmet_04"), Mask("2"), Mane() },
                shield = () => new[] { Scutum((c, p) => IsRole(c, Role.Gold) || (!IsRole(c, Role.Cloth) && V(c) > 0.35f) ? Gilt(c) : null) },
                back = Cape,
                main = t => new[] { Armed(K + "Weapons/K_SM_Wep_Hammer_01.prefab", t, 0.9f, 0.6f) },
            },
            new Elite
            {
                // The Bounty Hunter: shinobi black, the hood in the player's colour, a gold mask, two sickles.
                name = "Collector", id = "Sect_Collector", dir = Sects + "Prosperity/Units/Collector/", clips = DuelClips,
                body = () => new[] { Body(JChr + "Shinobi_01.prefab", Zone.All) },
                head = () => new[] { new Part { prefab = JAtt + "Hood_01.prefab", headSpace = true, paint = (c, p) => Cloth(Mathf.Lerp(0.4f, 0.8f, V(c))) },
                    Paint(Mask("1"), (c, p) => IsRole(c, Role.Cloth) ? Gilt(new Color32(150, 150, 150, 255)) : null) },
                main = t => new[] { Armed(J + "Weapons/J_SM_Wep_Sickle_01.prefab", t, 1.25f, 0.2f) },
                off = t => new[] { Armed(J + "Weapons/J_SM_Wep_Sickle_01.prefab", t, 1.25f, 0.2f) },
            },
            new Elite
            {
                // A merchant prince: the king's coat and mantle, samurai sleeves and legs, a golden laurel.
                name = "Guildmaster", id = "Sect_Guildmaster", dir = Sects + "Prosperity/Units/Guildmaster/", clips = DuelClips,
                body = () => new[] { Body(KChr + "King_01.prefab", Zone.Head | Zone.Torso | Zone.Thigh), Body(Samurai1, Zone.UpperArm | Zone.ForeArm), Body(Samurai3, Zone.Shin) },
                head = () => new[] { Roman("Crown_Leaf_02", null, (c, p) => Gilt(c)) },
                back = () => new[] { new Part { prefab = KAtt + "King_Cape_01.prefab" } },
                backScale = 0.95f,
                main = t => new[] { Armed(K + "Weapons/K_SM_Wep_Sword_13.prefab", t, 0.75f) },
                off = t => new[] { Armed(K + "Weapons/K_SM_Wep_Dagger_03.prefab", t) },
            },
            new Elite
            {
                // Ashigaru plate, the smith's apron and goggles, a frame of stores on the back.
                name = "Artificer", id = "Sect_Artificer", dir = Sects + "Antiquity/Units/Artificer/", clips = BowClips, ranged = true,
                body = () => new[] { Body(KChr + "Blacksmith_Male_01.prefab", Zone.Head | Zone.Thigh | Zone.Shin), Body(Samurai2, Zone.Torso | Zone.UpperArm | Zone.ForeArm) },
                head = () => new[] { Samurai("Helmet_03"), new Part { prefab = KAtt + "Blacksmith_Male_Goggles_01.prefab", scale = 1.05f } },
                back = () => new[] { Paint(Samurai("Backpack_01"), (c, p) => (Role.Matte, c)) },
                backScale = 1f, backAt = new Vector3(0f, 0.2f, -0.24f),
                main = t => new[] { new Part { prefab = K.Replace("Prefabs/", "Models/") + "SM_Prop_Crossbow_01.fbx", material = KMat, scale = 1.1f, paint = (c, p) => Prod(c, t >= 3 ? 4 : t) } },
                mainTurn = new Vector3(0f, -90f, 0f),
            },
            new Elite
            {
                // Court robes and the tall cap, a sage's beard, a staff whose crystal always glows.
                name = "ArchivistOfHours", id = "Sect_ArchivistOfHours", dir = Sects + "Antiquity/Units/ArchivistOfHours/", clips = MageClips, ranged = true,
                body = () => new[] { Body(JChr + "Nobility_Male_01.prefab", Zone.All), Shells(Samurai3, SamuraiShoulders) },
                head = () => new[] { Samurai("Hat_03"), Samurai("Facial_Hair_04") },
                back = () => new[] { new Part { prefab = KAtt + "Mage_Cape_01.prefab" } },
                main = t => new[] { new Part { prefab = K + "Weapons/K_SM_Wep_Staff_04.prefab", scale = 1.15f,
                    paint = (c, p) => p.y > 0.55f && V(c) > 0.5f ? t >= 3 ? Is(Role.Shard, 70, 190, 225) : Is(Role.Veil, (byte)(60 + 10 * t), (byte)(150 + 15 * t), 205) : V(c) < 0.35f ? Is(Role.Gold, 200, 150, 67) : null } },
            },
        };

        static GameObject EliteRig(Elite e, RuntimeAnimatorController c, StringBuilder log)
        {
            var root = new GameObject(e.name);
            var rig = L.Skeleton("Body", root.transform, c);
            var kit = root.AddComponent<UnitAgeKit>();
            kit.aged = new[] { rig };
            var body = Body(rig, e.name + "_Body", e.gold, e.body(), log);
            var worn = new List<GameObject>();
            worn.Add(Piece(e.name + "_Head", L.Bone(rig, "Head", log), Vector3.zero, Vector3.zero, e.gold, e.head(), log));
            if (e.shield != null)
                worn.Add(Piece(e.name + "_Shield", L.Bone(rig, "Elbow_L", log), L.ShieldPos, Quaternion.LookRotation(L.ShieldFace, L.ShieldUp).eulerAngles, e.gold, e.shield(), log));
            if (e.back != null)
            {
                var spine = L.Bone(rig, "Spine_03", log);
                var back = Piece(e.name + "_Back", spine, Vector3.zero, Vector3.zero, e.gold, e.back(), log);
                back.transform.rotation = root.transform.rotation;
                back.transform.localScale = Vector3.one * e.backScale;
                var bb = LegionaryShot.BoundsOf(back);
                back.transform.position += spine.position + e.backAt - new Vector3(bb.center.x, bb.max.y, bb.center.z);
                worn.Add(back);
            }
            Weld(body, rig, worn.ToArray());
            int tiers = e.tiers > 0 ? e.tiers : e.ranged ? 4 : 5;
            if (e.main != null)
            {
                var right = L.Bone(rig, "Hand_R", log);
                kit.weapons = OnePerLevel(tiers, t => Piece(e.name + "_Main_T" + (t - 1), right, e.mainAt, e.mainTurn, e.gold, e.main(t - 1), log));
                kit.weaponTechs = tiers == 1 ? null : e.ranged ? ArrowTechs : MeleeTechs;
            }
            if (e.off != null)
            {
                var left = L.Bone(rig, "Hand_L", log);
                kit.quivers = OnePerLevel(tiers, t => Piece(e.name + "_Off_T" + (t - 1), left, e.offAt, e.offTurn, e.gold, e.off(t - 1), log));
                kit.quiverTechs = kit.weaponTechs;
            }
            root.AddComponent<UnitPlayerColor>();
            e.after?.Invoke(root, rig, log);
            Finish(rig);
            return root;
        }

        internal static void AuthorElites()
        {
            Directory.CreateDirectory("Temp/elite");
            var log = new StringBuilder();
            foreach (var e in Elites())
            {
                var c = L.BuildController(e.dir + e.name + ".controller", e.clips, log);
                Save(EliteRig(e, c, log), e.dir, e.name, c, log);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/elite/author.txt", log.ToString());
        }

        const string LexorDir = "Assets/GameData/TechTree/Civs/Alanthor/Units/KingLexor/";

        /// <summary>Hands each elite's rig to <paramref name="shoot"/> for its button picture.</summary>
        internal static void EliteIcons(Action<string, GameObject> shoot, StringBuilder log)
        {
            foreach (var e in Elites().Concat(Civilians())) shoot(e.id, EliteRig(e, null, log));
        }

        internal static IEnumerable<string> ElitePrefabs() => Elites().Select(e => e.dir + e.name + ".prefab");

        // ── Pictures ───────────────────────────────────────────────────

        [MenuItem("Waning Border/Art/Units/Render Elite Units")]
        public static void RenderElites()
        {
            Directory.CreateDirectory("Temp/elite");
            var elites = Elites();
            for (int c = 0; c < 4; c++)
            {
                int clip = c;
                Row("Temp/elite/all_" + c + ".png", elites.Select<Elite, Func<GameObject>>((e, i) => () =>
                    Posed(e.dir + e.name + ".prefab", e.clips[clip], clip == 2 ? 0.45f : clip == 3 ? 0.9f : 0.2f, 1, e.ranged ? 3 : 4)).ToArray(), c == 0);
            }
            // Each unit's weapon tiers: the only thing that changes.
            foreach (var e in elites)
                Row("Temp/elite/" + e.name + "_tiers.png", Enumerable.Range(0, e.ranged ? 4 : 5).Select<int, Func<GameObject>>(t => () => Posed(e.dir + e.name + ".prefab", e.clips[0], 0.2f, 1, t)).ToArray(), false);
        }

        /// <summary>Every variant of the Archery Range units: level by level, and each tier.</summary>
        [MenuItem("Waning Border/Art/Units/Render Archery Variants")]
        public static void RenderArcheryVariants()
        {
            Directory.CreateDirectory("Temp/archery");
            string A = RangeUnits + "Archer/Archer.prefab", C = RangeUnits + "Crossbowman/Crossbowman.prefab", Y = RangeUnits + "Longbowman/Longbowman.prefab";
            Func<GameObject> F(string p, string clip, float t, int level, int weapon, int quiver) => () => Posed(p, clip, t, level, weapon, quiver);
            var list = new List<Func<GameObject>>();
            for (int l = 1; l <= 3; l++) for (int b = 0; b <= 2; b++) list.Add(F(A, BowClips[2], 0.02f, l, b, l));
            Row("Temp/archery/variants_archer.png", list.ToArray(), true);
            list.Clear();
            for (int l = 2; l <= 3; l++) for (int b = 0; b <= 3; b++) list.Add(F(C, BowClips[2], 0.02f, l, b, b));
            Row("Temp/archery/variants_crossbowman.png", list.ToArray(), true);
            list.Clear();
            for (int b = 0; b <= 3; b++) { list.Add(F(Y, YumiClips[3], 0.97f, 3, 0, b)); list.Add(F(Y, YumiClips[3], 0.05f, 3, 0, b)); }
            Row("Temp/archery/variants_yumi.png", list.ToArray(), true);
        }

        static GameObject Posed(string prefab, string clipPath, float t, int level, int weapon, int quiver = -1)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
            var kit = go.GetComponent<UnitAgeKit>();
            kit.Apply(true, level, weapon, 0, instant: true, quiver: quiver < 0 ? weapon : quiver);
            go.GetComponent<UnitPlayerColor>().Apply(new Color(0.12f, 0.35f, 0.85f));
            var clip = L.Clip(clipPath);
            var shown = Rig(kit);
            if (clip != null) clip.SampleAnimation(shown, clip.length * t);
            shown.transform.localPosition = Vector3.zero;
            shown.transform.localRotation = Quaternion.identity;
            UnitGearIcons.Bake(go);
            return go;
        }

        static void Row(string png, Func<GameObject>[] make, bool back, float pitchX = 1.75f, float height = 2.7f, int px = 260)
        {
            var stage = new GameObject("RowStage");
            try
            {
                // Laid out across the camera's view, so the row is level.
                var rot = Quaternion.Euler(0f, 205f, 0f);
                for (int i = 0; i < make.Length; i++)
                {
                    var go = make[i]();
                    go.transform.SetParent(stage.transform, true);
                    go.transform.position = rot * new Vector3(i * pitchX, 0f, 0f);
                }
                int n = make.Length;
                var c = rot * new Vector3((n - 1) * 0.5f * pitchX, 0f, 0f) + Vector3.up * height * 0.42f;
                var size = rot * new Vector3(n * pitchX, 0f, 0f);
                var b = new Bounds(c, new Vector3(Mathf.Abs(size.x), height, Mathf.Abs(size.z)));
                LegionaryShot.Shoot(b, 205f, 10f, 64, 64, "Temp/kit_warm.png", true);
                LegionaryShot.Shoot(b, 205f, 10f, n * px, (int)(px * height / pitchX * 1.12f), png, true);
                if (back) LegionaryShot.Shoot(b, 25f, 10f, n * px, (int)(px * height / pitchX * 1.12f), png.Replace(".png", "_back.png"), true);
            }
            finally { Object.DestroyImmediate(stage); }
        }
    }
}
