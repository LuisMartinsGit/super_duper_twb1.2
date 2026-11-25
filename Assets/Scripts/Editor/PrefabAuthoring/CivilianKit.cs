// CivilianKit.cs
// The Worker, the Scout and the Litharch (developer, 2026-11-25), mixed from the
// three Synty kits with InfantryKit's fusing and palette. One look each.
//
//   Worker    a smith's apron over a kimono, Roman sandals, a headcloth, a hammer;
//             his own controller (idle, walk, build, mine, death)
//   Scout     a hooded rider in shinobi trousers and a straw cape, a staff, and a
//             paper lantern that glows, lights the ground and swings from the hand
//   Litharch  the healing and cleansing priest: white and gold vestments, a
//             mitre ringed with a golden laurel, a white beard, a living staff with a glowing crystal;
//             the Mage clips, plus a death
//
//   Waning Border > Art > Units > Render Civilians   (Temp/civilians/)

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TheWaningBorder.Rendering;
using Object = UnityEngine.Object;
using L = TheWaningBorder.EditorTools.LegionarySpearmanAuthor;

namespace TheWaningBorder.EditorTools
{
    public static partial class InfantryKit
    {
        const string FortUnits = "Assets/GameData/TechTree/Age0/Buildings/Fortress/Units/";
        const string LitharchDir = "Assets/GameData/TechTree/Age0/Buildings/TempleOfRidan/Units/Litharch/";
        const string WorkerAnim = FortUnits + "Worker/Animations/";
        static readonly string[] WorkerClips =
        {
            WorkerAnim + "idle.fbx", WorkerAnim + "Walk.fbx", WorkerAnim + "Build/left_loop.fbx", WorkerAnim + "death.fbx", WorkerAnim + "Mine.fbx",
        };

        static Part[] Fixed(Part p) => new[] { p };

        /// <summary>The knights' kit's dull reds (a scarf, a stole): the player's colour.</summary>
        static (Role, Color32)? Reds(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return (h < 0.045f || h > 0.94f) && s > 0.45f && v > 0.3f ? Cloth(Mathf.Lerp(0.5f, 0.95f, v)) : null;
        }

        static (Role, Color32)? Vestment(Color32 c)
        {
            if (IsRole(c, Role.Gold)) return Gilt(c);
            return Reds(c);
        }

        static Elite[] Civilians() => new[]
        {
            new Elite
            {
                name = "Worker", id = "Worker", dir = FortUnits + "Worker/", clips = WorkerClips, gold = Role.Matte, tiers = 1, keepController = true,
                body = () => new[] { Body(KChr + "Blacksmith_Male_01.prefab", Zone.Head | Zone.Torso | Zone.Thigh), Body(JGeneric, Zone.UpperArm | Zone.ForeArm), Body(RSoldier, Zone.Shin) },
                head = () => new[] { new Part { prefab = JAtt + "Hat_04.prefab", paint = (c, p) => Cloth(Mathf.Lerp(0.5f, 0.85f, V(c))) } },
                // His build and mine clips swing the LEFT arm.
                off = t => Fixed(new Part { prefab = K + "Items/K_SM_Item_Hammer_04.prefab", scale = 1.15f }),
            },
            new Elite
            {
                name = "Scout", id = "Scout", dir = FortUnits + "Scout/", clips = L.Clips, gold = Role.Brass, tiers = 1,
                body = () => new[] { Paint(Body(KChr + "Rider_01.prefab", Zone.Torso | Zone.UpperArm | Zone.ForeArm), (c, p) => Reds(c)), Body(JChr + "Shinobi_01.prefab", Zone.Head | Zone.Thigh | Zone.Shin) },
                head = () => new[] { new Part { prefab = JAtt + "Hood_01.prefab", headSpace = true, paint = (c, p) => Cloth(Mathf.Lerp(0.45f, 0.85f, V(c))) } },
                back = () => new[] { Paint(Samurai("Cape_02"), (c, p) => (Role.Matte, c)) },
                backAt = new Vector3(0f, 0.2f, -0.16f), backScale = 1f,
                main = t => Fixed(new Part { prefab = J + "Weapons/J_SM_Wep_Staff_01.prefab", scale = 0.85f }),
                mainAt = new Vector3(0.09f, 0.03f, -0.1f),
                after = (root, rig, log) => Lantern(rig, log),
            },
            new Elite
            {
                name = "Litharch", id = "Litharch", dir = LitharchDir, clips = MageClips, tiers = 1, keepController = true,
                body = () => new[] { Paint(Body(KChr + "Priest_01.prefab", Zone.All), (c, p) => Vestment(c)) },
                head = () => new[]
                {
                    new Part { prefab = KAtt + "Priest_Hat_01.prefab", paint = (c, p) => Vestment(c) },
                    new Part { prefab = JAtt + "Facial_Hair_04.prefab", paint = (c, p) => Is(Role.Matte, (byte)(170 + 70 * V(c)), (byte)(168 + 70 * V(c)), (byte)(160 + 70 * V(c))) },
                    new Part { prefab = RAtt + "Crown_Leaf_02.prefab", material = RMat, scale = 1.04f, paint = (c, p) => Gilt(c) },
                },
                back = () => new[] { new Part { prefab = KAtt + "Mage_Cape_01.prefab", paint = (c, p) => IsRole(c, Role.Gold) ? Gilt(c) : Is(Role.Matte, 232, 228, 218) } },
                main = t => Fixed(new Part { prefab = K + "Weapons/K_SM_Wep_Staff_09.prefab", scale = 1.2f, paint = (c, p) =>
                {
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    if (h > 0.45f && h < 0.6f && v > 0.5f) return Is(Role.Holy, 255, 232, 176);          // the crystal
                    if (h > 0.2f && h < 0.42f) return Is(Role.Gold, (byte)(250 * Mathf.Lerp(0.65f, 1f, v)), (byte)(188 * Mathf.Lerp(0.65f, 1f, v)), 84);  // the leaves
                    return Is(Role.Matte, (byte)Mathf.Min(255, c.r + 90), (byte)Mathf.Min(255, c.g + 95), (byte)Mathf.Min(255, c.b + 95));   // pale wood
                } }),
            },
        };

        /// <summary>The Scout's lantern: paper that glows, hung by its handle, with a small warm light.</summary>
        static void Lantern(GameObject rig, StringBuilder log)
        {
            var hand = L.Bone(rig, "Hand_L", log);
            var hook = new GameObject("Lantern");
            hook.transform.SetParent(hand, false);
            hook.transform.localPosition = new Vector3(-0.09f, -0.03f, 0f);
            hook.transform.rotation = Quaternion.identity;
            hook.AddComponent<HangUpright>();
            var paper = Piece("Scout_Lantern", hook.transform, Vector3.zero, Vector3.zero, Role.Brass, new[]
            {
                new Part { prefab = J + "Props/J_SM_Prop_Lantern_04.prefab", painted = true, paint = (c, p) =>
                    IsRole(c, Role.Cloth) ? Cloth(0.75f) : V(c) > 0.6f ? Is(Role.Lamp, 255, 214, 140) : null },
            }, log);
            paper.transform.localScale = Vector3.one * 0.8f;
            var b = LegionaryShot.BoundsOf(paper);
            paper.transform.position += hook.transform.position + Vector3.up * 0.02f - new Vector3(b.center.x, b.max.y, b.center.z);
            var lamp = new GameObject("LanternLight");
            lamp.transform.SetParent(hook.transform, false);
            lamp.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.69f, 0.35f);
            light.range = 5f;
            light.intensity = 5f;
            light.shadows = LightShadows.None;
        }

        internal static void AuthorCivilians()
        {
            Directory.CreateDirectory("Temp/civilians");
            var log = new StringBuilder();
            foreach (var e in Civilians())
            {
                RuntimeAnimatorController c = e.keepController
                    ? AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(e.dir + e.name + ".controller")
                    : L.BuildController(e.dir + e.name + ".controller", e.clips, log);
                if (e.name == "Litharch") AddDeath((AnimatorController)c, e.clips[3]);
                Save(EliteRig(e, c, log), e.dir, e.name, c, log);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/civilians/author.txt", log.ToString());
        }

        /// <summary>The Litharch's controller had no death state.</summary>
        static void AddDeath(AnimatorController c, string clip)
        {
            var sm = c.layers[0].stateMachine;
            if (sm.states.Any(s => s.state.name == "Death")) return;
            var st = sm.AddState("Death");
            st.motion = L.Clip(clip);
            var t = sm.AddAnyStateTransition(st);
            t.hasExitTime = false;
            t.duration = 0.1f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");
            EditorUtility.SetDirty(c);
        }

        [MenuItem("Waning Border/Art/Units/Render Civilians")]
        public static void RenderCivilians()
        {
            Directory.CreateDirectory("Temp/civilians");
            foreach (var e in Civilians())
                Row("Temp/civilians/" + e.name + ".png", e.clips.SelectMany(c => new[] { 0.15f, 0.55f }.Select<float, Func<GameObject>>(t => () =>
                {
                    var go = Posed(e.dir + e.name + ".prefab", c, t, 1, 0);
                    foreach (var h in go.GetComponentsInChildren<HangUpright>()) h.transform.rotation = Quaternion.identity;
                    foreach (var l in go.GetComponentsInChildren<Light>()) l.enabled = false;
                    return go;
                })).ToArray(), true);
        }
    }
}
