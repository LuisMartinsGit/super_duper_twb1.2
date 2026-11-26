// CavalryKit.cs
// The mounted units (developer, 2026-11-25: "add the riders"): the Outrider, the
// Cataphract and King Lexor, kit riders on the LP Horse pack's horse.
//
//   Horse    the pack's mesh reduced (vertex clustering, which also gives it the
//            kits' faceted look) and repainted into the shared palette; only the
//            bones that move it are kept. Idle, canter or gallop.
//   Rider    the Roman skeleton on the saddle bone: the pack's seated legs, the
//            knights' upper body for guard and strike (MountedRider hands him
//            the horse's state)
//   Level    the Outrider's armour follows the Royal Stable 1 / 2 / 3
//   Lance    Stone / Iron / Veilstone Lances (Lexor: the Garrison's weapon techs)
//   Barding  Iron / Chain / Plate Barding (Lexor: always the gold)
//
//   Waning Border > Art > Units > Render Cavalry   (Temp/cavalry/)

using System;
using System.Collections.Generic;
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
        const string HP = "Assets/LPHorse_Version_2_9/Version_2_9/";
        const string HCoat = "Assets/LPHorse_Version_2_9/Version_2_31/Horse3D/Textures/Horse_Body_";
        const string HorsePrefab = HP + "Prefabs/Not Saddled/LP_Horse_Mesh_2_9_Brown_01 Variant.prefab";
        const string StableUnits = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/RoyalStable/Units/";
        const float HorseScale = 0.72f;
        static readonly string[] LanceTechs = { "StoneBardedLances", "IronBardedLances", "VeilstoneLances" };
        static readonly string[] BardingTechs = { "IronBarding", "VeilstoneBarding", "PlateBarding" };
        // The seat, in the horse's rest space, before HorseScale.
        static Vector3 Seat = new Vector3(0f, 2.18f, 0.06f);

        sealed class Mount
        {
            public string name, dir, id, ladder, coat, gallop;
            public Func<int, Part[]> body, head;
            public int levels;
            public Func<int, Part[]> lance;
            public int lances;
            public string[] lanceTechs;
            public Func<Part[]> shield, back;
            /// <summary>Barding tiers: 0 is what the horse wears unresearched.</summary>
            public int bardFrom, bardTo;
            public bool bardLadder, hold;
        }

        static Mount[] Mounts() => new[]
        {
            new Mount
            {
                name = "Outrider", id = "Alanthor_Outrider", dir = StableUnits + "Outrider/", ladder = "Alanthor_RoyalStable",
                coat = "ChestnutBlack", gallop = "Rig_Gallop_Steady_RootMotion", levels = 3, hold = true,
                body = l => l switch
                {
                    1 => new[] { Paint(Body(KChr + "Rider_01.prefab", Zone.Head | Zone.Torso | Zone.UpperArm | Zone.ForeArm), (c, p) => Reds(c)), Body(JChr + "Shinobi_01.prefab", Zone.Thigh | Zone.Shin) },
                    2 => new[] { Body(JSoldier, Zone.All & ~Zone.Shin), Body(KSoldier, Zone.Shin) },
                    _ => new[] { Body(Samurai2, Zone.All & ~Zone.Shin), Body(KSoldier, Zone.Shin), Gilded(Shells(RSoldier, RomanShoulders)) },
                },
                head = l => l switch
                {
                    1 => new[] { new Part { prefab = KAtt + "Soldier_01.prefab", scale = 1.06f, move = new Vector3(0f, 0.065f, 0.03f) } },
                    2 => new[] { Jingasa(false) },
                    _ => new[] { Samurai("Helmet_03"), Crest("Helmet_03", 0.02f) },
                },
                lance = t => new[] { Armed(J + "Weapons/J_SM_Wep_Spear_02.prefab", t, 0.95f, 0.7f) }, lances = 4, lanceTechs = LanceTechs,
                bardFrom = 0, bardTo = 3, bardLadder = true,
            },
            new Mount
            {
                name = "Cataphract", id = "Alanthor_Cataphract", dir = StableUnits + "Cataphract/", ladder = "Alanthor_RoyalStable",
                coat = "BlackBlack", gallop = "Rig_Left_Lead_Canter_RootMotion", levels = 1, hold = true,
                body = l => new[] { Body(Samurai1, Zone.Torso | Zone.Head | Zone.UpperArm | Zone.ForeArm | Zone.Thigh), Body(Samurai3, Zone.Shin), Gilded(Shells(RSoldier, RomanShoulders)) },
                head = l => new[] { Gilded(Roman("Helmet_05")), Mask("2") },
                lance = t => new[] { Armed(J + "Weapons/J_SM_Wep_Spear_01.prefab", t, 1.2f, 0.9f) }, lances = 4, lanceTechs = LanceTechs,
                shield = () => new[] { new Part { prefab = K + "Weapons/K_SM_Wep_Shield_02.prefab" } },
                back = Cape, bardFrom = 1, bardTo = 4, bardLadder = true,
            },
            new Mount
            {
                name = "KingLexor", id = "King Lexor", dir = LexorDir, coat = "WhiteWhite", gallop = "Rig_Gallop_Steady_RootMotion", levels = 1,
                body = l => new[] { Body(KChr + "King_01.prefab", Zone.Head), Body(Samurai1, Zone.Torso | Zone.UpperArm | Zone.ForeArm | Zone.Thigh | Zone.Shin), Gilded(Shells(RSoldier, RomanShoulders)) },
                head = l => new[] { new Part { prefab = KAtt + "King_Crown_01.prefab", scale = 1.1f } },
                lance = t => new[] { Armed(K + "Weapons/K_SM_Wep_Sword_16.prefab", t, 0.95f) }, lances = 5, lanceTechs = MeleeTechs,
                back = () => new[] { new Part { prefab = KAtt + "King_Cape_01.prefab" } }, bardFrom = 4, bardTo = 4,
            },
        };

        // ── The horse ──────────────────────────────────────────────────

        static readonly Dictionary<string, (Vector3[] v, Vector2[] uv, BoneWeight[] w, int[][] tris, string[] bones)> _horse = new();

        /// <summary>A horse body: the pack's mesh clustered and painted from one coat.</summary>
        static GameObject Horse(GameObject rig, string name, string coat, StringBuilder log)
        {
            var src = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HorsePrefab));
            src.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Vector3[] v; Vector2[] uv; BoneWeight[] w; string[] names; var subs = new List<int[]>();
            try
            {
                var smr = src.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).First();
                var m = smr.sharedMesh;
                w = m.boneWeights; uv = m.uv;
                names = smr.bones.Select(b => b.name).ToArray();
                var bind = m.bindposes;
                var mats = bind.Select((b, i) => smr.bones[i].localToWorldMatrix * b).ToArray();
                var sv = m.vertices;
                v = new Vector3[sv.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    var q = w[i];
                    v[i] = q.weight0 * mats[q.boneIndex0].MultiplyPoint3x4(sv[i]) + q.weight1 * mats[q.boneIndex1].MultiplyPoint3x4(sv[i])
                         + q.weight2 * mats[q.boneIndex2].MultiplyPoint3x4(sv[i]) + q.weight3 * mats[q.boneIndex3].MultiplyPoint3x4(sv[i]);
                }
                for (int s = 0; s < m.subMeshCount; s++) subs.Add(m.GetTriangles(s));
            }
            finally { Object.DestroyImmediate(src); }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HCoat + coat + ".png");
            var (px, tw, th) = KitPart.Pixels(tex);
            Color32 At(Vector2 q) => px[Mathf.Clamp((int)(Mathf.Repeat(q.y, 1f) * th), 0, th - 1) * tw + Mathf.Clamp((int)(Mathf.Repeat(q.x, 1f) * tw), 0, tw - 1)];

            // Cluster: fine on the head and the hooves, coarse on the barrel.
            var cluster = new Dictionary<Vector3Int, int>();
            var sum = new List<Vector3>(); var n = new List<int>(); var rep = new List<int>(); var of = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                bool head = v[i].z > 1.35f && v[i].y > 2.0f, leg = v[i].y < 1.15f;
                float cell = head ? 0.06f : leg ? 0.07f : 0.125f;
                var key = Vector3Int.RoundToInt(v[i] / cell) + (head ? new Vector3Int(5000, 0, 0) : leg ? new Vector3Int(9000, 0, 0) : Vector3Int.zero);
                if (!cluster.TryGetValue(key, out int c)) { c = sum.Count; cluster[key] = c; sum.Add(Vector3.zero); n.Add(0); rep.Add(i); }
                sum[c] += v[i]; n[c]++; of[i] = c;
            }
            var byKey = BoneIndex(rig, out var bones);
            int Map(int b) => byKey.TryGetValue(names[b], out int k) ? k : byKey["Back_Forward"];
            var B = new Builder();
            var seen = new HashSet<(int, int, int)>();
            for (int s = 0; s < subs.Count; s++)
                for (int t = 0; t < subs[s].Length; t += 3)
                {
                    int a = subs[s][t], b = subs[s][t + 1], c = subs[s][t + 2];
                    int ca = of[a], cb = of[b], cc = of[c];
                    if (ca == cb || cb == cc || ca == cc) continue;
                    var sorted = new[] { ca, cb, cc }; Array.Sort(sorted);
                    if (!seen.Add((sorted[0], sorted[1], sorted[2]))) continue;
                    Color32 col = At((uv[a] + uv[b] + uv[c]) / 3f);
                    // Few shades: a painted coat, not a photograph.
                    if (s == 0) { byte Q8(byte x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x / 22f) * 22, 0, 250); col = new Color32(Q8(col.r), Q8(col.g), Q8(col.b), 255);
                        // Never saturated enough to be taken for cloth, never blown out.
                        float g = Mathf.Max(col.g, col.r * 0.56f), bl = Mathf.Max(col.b, col.r * 0.38f);
                        col = new Color32((byte)(col.r * 0.8f), (byte)(g * 0.8f), (byte)(bl * 0.8f), 255); }
                    else col = new Color32(26, 20, 18, 255);
                    var q = CellUv(Role.Matte, col);
                    Vector3 pa = sum[ca] / n[ca], pb = sum[cb] / n[cb], pc = sum[cc] / n[cc];
                    var nrm = Vector3.Cross(pb - pa, pc - pa);
                    // A sliver has no normal: one such pixel blooms into a white disc.
                    if (nrm.magnitude < 1e-7f) continue;
                    nrm /= nrm.magnitude;
                    foreach (var (p, r) in new[] { (pa, rep[ca]), (pb, rep[cb]), (pc, rep[cc]) })
                    {
                        B.t.Add(B.v.Count);
                        B.v.Add(p); B.n.Add(nrm); B.uv.Add(q);
                        var k = w[r];
                        B.w.Add(new BoneWeight { boneIndex0 = Map(k.boneIndex0), weight0 = k.weight0, boneIndex1 = Map(k.boneIndex1), weight1 = k.weight1, boneIndex2 = Map(k.boneIndex2), weight2 = k.weight2, boneIndex3 = Map(k.boneIndex3), weight3 = k.weight3 });
                    }
                }
            foreach (var p in Tack()) B.Add(p, Role.Matte, k => byKey.TryGetValue(k, out int i) ? i : byKey["Back_Forward"], log);
            log.AppendLine($"  {name}: {B.t.Count / 3} tris of {subs.Sum(x => x.Length / 3)}");
            return Skinned(rig, name, B, bones);
        }

        static Dictionary<string, int> BoneIndex(GameObject rig, out List<Transform> bones)
        {
            bones = new List<Transform>();
            var byKey = new Dictionary<string, int>();
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t == rig.transform || t.GetComponent<Renderer>() != null || byKey.ContainsKey(t.name)) continue;
                byKey[t.name] = bones.Count;
                bones.Add(t);
            }
            return byKey;
        }

        static GameObject Skinned(GameObject rig, string name, Builder b, List<Transform> bones)
        {
            // Only the bones this mesh hangs on.
            var used = new SortedSet<int>();
            foreach (var k in b.w) { if (k.weight0 > 0f) used.Add(k.boneIndex0); if (k.weight1 > 0f) used.Add(k.boneIndex1); if (k.weight2 > 0f) used.Add(k.boneIndex2); if (k.weight3 > 0f) used.Add(k.boneIndex3); }
            var list = used.ToList();
            var map = new Dictionary<int, int>();
            for (int i = 0; i < list.Count; i++) map[list[i]] = i;
            int M(int i, float f) => f > 0f ? map[i] : 0;
            for (int i = 0; i < b.w.Count; i++)
            {
                var k = b.w[i];
                b.w[i] = new BoneWeight { boneIndex0 = M(k.boneIndex0, k.weight0), weight0 = k.weight0, boneIndex1 = M(k.boneIndex1, k.weight1), weight1 = k.weight1, boneIndex2 = M(k.boneIndex2, k.weight2), weight2 = k.weight2, boneIndex3 = M(k.boneIndex3, k.weight3), weight3 = k.weight3 };
            }
            var mesh = SaveMesh(name, m =>
            {
                b.Fill(m);
                m.bindposes = list.Select(i => bones[i].worldToLocalMatrix * rig.transform.localToWorldMatrix).ToArray();
            });
            var go = new GameObject(name);
            go.transform.SetParent(rig.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = list.Select(i => bones[i]).ToArray();
            smr.rootBone = null;
            smr.localBounds = new Bounds(new Vector3(0f, 1.6f, 0f), new Vector3(3f, 4f, 6f));
            smr.sharedMaterial = Material();
            return go;
        }

        /// <summary>The horse's rig, its control bones dropped.</summary>
        static GameObject HorseRig(Transform parent, RuntimeAnimatorController controller)
        {
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HorsePrefab), parent);
            go.name = "Horse";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).First();
            var keep = new HashSet<Transform>();
            void Keep(Transform t) { for (; t != null && t != go.transform.parent; t = t.parent) keep.Add(t); }
            var m = smr.sharedMesh; var bw = m.boneWeights; var used = new HashSet<int>();
            foreach (var k in bw) { if (k.weight0 > 0f) used.Add(k.boneIndex0); if (k.weight1 > 0f) used.Add(k.boneIndex1); if (k.weight2 > 0f) used.Add(k.boneIndex2); if (k.weight3 > 0f) used.Add(k.boneIndex3); }
            foreach (int i in used) Keep(smr.bones[i]);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Saddle_Root" || t.name == "Root_Motion") Keep(t);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            var kill = go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && (!keep.Contains(t) || t.GetComponent<Renderer>() != null)).ToList();
            foreach (var t in kill) if (t != null) Object.DestroyImmediate(t.gameObject);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var an = go.GetComponent<Animator>();
            an.runtimeAnimatorController = controller;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return go;
        }

        // ── Tack and barding (rest space of the horse, full size) ──────

        static Part On(string bone, string prefab, Vector3 at, Vector3 euler, float scale, Func<Color32, Vector3, (Role, Color32)?> paint, string mat = null)
            => new Part { bone = bone, prefab = prefab, move = at, euler = euler, scale = scale, paint = paint, material = mat, centred = true };

        /// <summary>The saddle and the cloth under it, in the player's colour.</summary>
        static Part[] Tack() => new[]
        {
            On("Saddle_Root", HP + "Saddle/Prefabs/Saddle_Dark.prefab", new Vector3(0f, 2.0f, 0.3f), Vector3.zero, 0.92f, (c, p) => Is(Role.Matte, (byte)(92 + c.r / 8), 58, 38)),
            // The cloth: a cape laid over the rump.
            On("Back_Back", KAtt + "King_Cape_01.prefab", new Vector3(0f, 2.17f, -0.62f), new Vector3(66f, 0f, 0f), 0.95f, (c, p) => Cloth(Mathf.Lerp(0.6f, 0.9f, V(c)))),
        };

        /// <summary>1 leather, 2 iron, 3 chain with brass, 4 gilded plate.</summary>
        static Part[] Barding(int tier)
        {
            Func<Color32, Vector3, (Role, Color32)?> metal = (c, p) =>
            {
                float v = Mathf.Lerp(0.6f, 1f, V(c));
                if (IsRole(c, Role.Cloth)) return Cloth(0.85f);
                switch (tier)
                {
                    case 1: return (Role.Matte, new Color32((byte)(124 * v), (byte)(84 * v), (byte)(54 * v), 255));
                    case 2: return (Role.Steel, new Color32((byte)(150 * v), (byte)(156 * v), (byte)(168 * v), 255));
                    case 3: return IsRole(c, Role.Gold) ? (Role.Brass, new Color32((byte)(206 * v), (byte)(150 * v), (byte)(82 * v), 255)) : (Role.Steel, new Color32((byte)(96 * v), (byte)(102 * v), (byte)(116 * v), 255));
                    default: return (Role.Gold, new Color32((byte)(250 * v), (byte)(188 * v), (byte)(84 * v), 255));
                }
            };
            Func<Color32, Vector3, (Role, Color32)?> field = (c, p) => IsRole(c, Role.Gold) ? metal(c, p) : tier >= 3 && V(c) > 0.45f ? Cloth(0.8f) : metal(c, p);
            var list = new List<Part>
            {
                // Chest plate and face plate.
                On("Back_Forward", K + "Weapons/K_SM_Wep_Shield_03.prefab", new Vector3(0f, 1.6f, 1.17f), new Vector3(4f, 0f, 0f), 0.95f, field),
                On("Head", K + "Weapons/K_SM_Wep_Shield_04.prefab", new Vector3(0f, 2.56f, 2.1f), new Vector3(-38f, 0f, 0f), 0.46f, metal),
            };
            if (tier >= 2)
                foreach (int s in new[] { -1, 1 })
                {
                    // Flank plates: the scutum laid along the shoulder and the haunch.
                    list.Add(On("Back_Forward", R + "Weapons/R_SM_Wep_Shield_04.prefab", new Vector3(0.5f * s, 1.66f, 0.62f), new Vector3(0f, 90f * s, 0f), 0.72f, field, RMat));
                    list.Add(On("Back_Back", R + "Weapons/R_SM_Wep_Shield_04.prefab", new Vector3(0.5f * s, 1.72f, -0.62f), new Vector3(0f, 90f * s, 0f), 0.72f, field, RMat));
                }
            return list.ToArray();
        }

        static GameObject Fitted(GameObject rig, string name, Part[] parts, Role gold, StringBuilder log)
        {
            var byKey = BoneIndex(rig, out var bones);
            var b = new Builder();
            log.AppendLine("  " + name);
            foreach (var p in parts) b.Add(p, gold, k => byKey.TryGetValue(k, out int i) ? i : byKey["Back_Forward"], log);
            return Skinned(rig, name, b, bones);
        }

        // ── Controllers ────────────────────────────────────────────────

        static AnimationClip HClip(string n) => AssetDatabase.LoadAssetAtPath<AnimationClip>(HP + "Animations/Primary_Cycles/" + n + ".anim");
        const string SeatClip = HP + "ReactiveRider/Animations/HumanRig_HorsebackLegOpen.anim";

        static AnimatorController Fresh(string path, params string[] bools)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (c == null) c = AnimatorController.CreateAnimatorControllerAtPath(path);
            while (c.parameters.Length > 0) c.RemoveParameter(0);
            while (c.layers.Length > 1) c.RemoveLayer(1);
            foreach (var b in bools) c.AddParameter(b, AnimatorControllerParameterType.Bool);
            c.AddParameter("IsDead", AnimatorControllerParameterType.Trigger);
            var sm = c.layers[0].stateMachine;
            foreach (var s in sm.states) sm.RemoveState(s.state);
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            return c;
        }

        static void Link(AnimatorState a, AnimatorState b, string p, bool v, float blend)
        {
            var t = a.AddTransition(b);
            t.hasExitTime = false; t.hasFixedDuration = true; t.duration = blend;
            t.AddCondition(v ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, p);
        }

        static AnimatorController HorseController(string dir, string name, string gallop)
        {
            var c = Fresh(dir + name + ".controller", "IsMoving", "IsAttacking");
            var sm = c.layers[0].stateMachine;
            var idle = sm.AddState("Idle"); idle.motion = HClip("Rig_Idle_Center");
            var move = sm.AddState("Move"); move.motion = HClip(gallop);
            var death = sm.AddState("Death"); death.motion = HClip("Rig_Idle_Center"); death.speed = 0f;
            sm.defaultState = idle;
            Link(idle, move, "IsMoving", true, 0.2f);
            Link(move, idle, "IsMoving", false, 0.25f);
            var die = sm.AddAnyStateTransition(death);
            die.hasExitTime = false; die.duration = 0.1f; die.canTransitionToSelf = false;
            die.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");
            EditorUtility.SetDirty(c);
            return c;
        }

        static AnimatorController RiderController(StringBuilder log)
        {
            string path = ArtDir + "Rider.controller";
            var c = Fresh(path, "IsMoving", "IsAttacking");
            var seat = c.layers[0].stateMachine.AddState("Seated");
            seat.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(SeatClip);
            if (seat.motion == null) log.AppendLine("MISSING " + SeatClip);
            string maskPath = ArtDir + "RiderUpper.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, maskPath); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
            }
            EditorUtility.SetDirty(mask);
            var sm = new AnimatorStateMachine { name = "Upper", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, path);
            c.AddLayer(new AnimatorControllerLayer { name = "Upper", avatarMask = mask, defaultWeight = 1f, stateMachine = sm, blendingMode = AnimatorLayerBlendingMode.Override });
            var idle = sm.AddState("Guard"); idle.motion = L.Clip(L.Clips[0]);
            var attack = sm.AddState("Attack"); attack.motion = L.Clip(L.Clips[2]);
            sm.defaultState = idle;
            Link(idle, attack, "IsAttacking", true, 0.08f);
            Link(attack, idle, "IsAttacking", false, 0.2f);
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return c;
        }

        // ── Prefabs ────────────────────────────────────────────────────

        static GameObject MountRig(Mount e, RuntimeAnimatorController horseC, RuntimeAnimatorController riderC, StringBuilder log)
        {
            var root = new GameObject(e.name);
            var horse = HorseRig(root.transform, horseC);
            Horse(horse, e.name + "_Horse", e.coat, log);
            var barding = new List<GameObject>();
            for (int b = e.bardFrom; b <= e.bardTo; b++)
                if (b == 0) { var none = new GameObject("NoBarding"); none.transform.SetParent(horse.transform, false); barding.Add(none); }
                else barding.Add(Fitted(horse, e.name + "_Barding_" + b, Barding(b), b >= 4 ? Role.Gold : b == 3 ? Role.Brass : Role.Matte, log));
            for (int i = 0; i < barding.Count; i++) barding[i].SetActive(i == 0);

            var saddle = L.Bone(horse, "Saddle_Root", log);
            var rider = L.Skeleton("Rider", root.transform, riderC);
            var head = L.Bone(rider, "Head", log);
            var hand = L.Bone(rider, "Hand_R", log);
            var bodies = OnePerLevel(e.levels, l =>
            {
                var gold = e.levels == 1 ? Role.Gold : GoldOf(l);
                var b = Body(rider, e.name + "_Body_L" + l, gold, e.body(l), log);
                var worn = new List<GameObject> { Piece(e.name + "_Head_L" + l, head, Vector3.zero, Vector3.zero, gold, e.head(l), log) };
                if (e.shield != null)
                    worn.Add(Piece(e.name + "_Shield", L.Bone(rider, "Elbow_L", log), L.ShieldPos, Quaternion.LookRotation(L.ShieldFace, L.ShieldUp).eulerAngles, Role.Gold, e.shield(), log));
                if (e.back != null)
                {
                    var spine = L.Bone(rider, "Spine_03", log);
                    var back = Piece(e.name + "_Back", spine, Vector3.zero, Vector3.zero, Role.Gold, e.back(), log);
                    back.transform.rotation = Quaternion.Euler(26f, 0f, 0f);
                    back.transform.localScale = Vector3.one * 0.9f;
                    var bb = LegionaryShot.BoundsOf(back);
                    back.transform.position += spine.position + new Vector3(0f, 0.18f, -0.1f) - new Vector3(bb.center.x, bb.max.y, bb.max.z);
                    worn.Add(back);
                }
                Weld(b, rider, worn.ToArray());
                return b;
            });
            var lances = OnePerLevel(e.lances, t =>
            {
                var w = Piece(e.name + "_Lance_T" + (t - 1), hand, e.hold ? L.SpearPos : new Vector3(0.09f, 0.03f, 0f), new Vector3(-90f, 0f, 0f), Role.Gold, e.lance(t - 1), log);
                if (e.hold) L.HoldAtMiddle(w);
                return w;
            });
            Finish(rider);

            // Onto the saddle: the rider keeps a man's size whatever the horse's.
            rider.transform.SetParent(saddle, true);
            rider.transform.localScale = Vector3.one / HorseScale * (1f / Mathf.Max(0.01f, saddle.lossyScale.x));
            rider.transform.rotation = Quaternion.identity;
            var seat = AssetDatabase.LoadAssetAtPath<AnimationClip>(SeatClip);
            if (seat != null) seat.SampleAnimation(rider, 0f);
            var hips = L.Bone(rider, "Hips", log);
            rider.transform.position += Seat + new Vector3(0f, 0.12f, 0f) - hips.position;
            rider.transform.rotation = Quaternion.identity;
            log.AppendLine($"  seated hips {hips.position.ToString("0.00")} seat {Seat}");

            var mr = root.AddComponent<MountedRider>();
            mr.horse = horse.GetComponent<Animator>();
            mr.rider = rider.GetComponent<Animator>();
            mr.rider.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var kit = root.AddComponent<UnitAgeKit>();
            kit.aged = new[] { horse };
            kit.levelLadderId = e.ladder;
            kit.bodies = bodies;
            kit.weapons = lances;
            kit.weaponTechs = e.lanceTechs;
            kit.quivers = barding.ToArray();
            kit.quiverTechs = e.bardLadder ? BardingTechs : null;
            root.AddComponent<UnitPlayerColor>();
            return root;
        }

        internal static void AuthorCavalry()
        {
            Directory.CreateDirectory("Temp/cavalry");
            var log = new StringBuilder();
            var riderC = RiderController(log);
            foreach (var e in Mounts())
            {
                var horseC = HorseController(e.dir, e.name, e.gallop);
                var go = MountRig(e, horseC, riderC, log);
                try
                {
                    go.transform.localScale = Vector3.one * HorseScale;
                    var saved = PrefabUtility.SaveAsPrefabAsset(go, e.dir + e.name + ".prefab");
                    var so = AssetDatabase.LoadMainAssetAtPath(e.dir + e.name + ".asset");
                    var ser = new SerializedObject(so);
                    ser.FindProperty("prefab").objectReferenceValue = saved;
                    ser.FindProperty("animatorController").objectReferenceValue = horseC;
                    ser.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(so);
                }
                finally { Object.DestroyImmediate(go); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/cavalry/author.txt", log.ToString());
        }

        internal static IEnumerable<(string id, string prefab)> MountPrefabs() => Mounts().Select(m => (m.id, m.dir + m.name + ".prefab"));

        /// <summary>A mounted prefab posed for a picture.</summary>
        internal static GameObject MountPose(string prefab, int level, int lance, int barding, string horseClip, float t, bool strike, Color? colour = null)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
            var kit = go.GetComponent<UnitAgeKit>();
            kit.Apply(true, level, lance, 0, instant: true, quiver: barding);
            go.GetComponent<UnitPlayerColor>().Apply(colour ?? new Color(0.12f, 0.35f, 0.85f));
            var mr = go.GetComponent<MountedRider>();
            var clip = HClip(horseClip);
            var atRest = mr.horse.GetComponentsInChildren<Transform>().First(x => x.name == "Root_Motion");
            var restAt = atRest.localPosition;
            clip.SampleAnimation(mr.horse.gameObject, clip.length * t);
            atRest.localPosition = restAt;
            // The rider as his two layers give him: the strike's arms over the seated legs.
            var upper = L.Clip(strike ? L.Clips[2] : L.Clips[0]);
            var rider = mr.rider.gameObject;
            Vector3 at = rider.transform.localPosition; var rot = rider.transform.localRotation;
            upper.SampleAnimation(rider, strike ? upper.length * 0.42f : 0f);
            var save = new Dictionary<Transform, Quaternion>();
            foreach (var b in rider.GetComponentsInChildren<Transform>())
                if (ZoneOf(b.name) is Zone.Torso or Zone.UpperArm or Zone.ForeArm or Zone.Head && b.name != "Hips" && b.name != "Root" && b != rider.transform) save[b] = b.localRotation;
            AssetDatabase.LoadAssetAtPath<AnimationClip>(SeatClip).SampleAnimation(rider, 0f);
            foreach (var kv in save) kv.Key.localRotation = kv.Value;
            rider.transform.localPosition = at;
            rider.transform.localRotation = rot;
            UnitGearIcons.Bake(go);
            return go;
        }

        [MenuItem("Waning Border/Art/Units/Render Cavalry")]
        public static void RenderCavalry()
        {
            Directory.CreateDirectory("Temp/cavalry");
            string O = StableUnits + "Outrider/Outrider.prefab", C = StableUnits + "Cataphract/Cataphract.prefab", X = LexorDir + "KingLexor.prefab";
            const string Idle = "Rig_Idle_Center", Gallop = "Rig_Gallop_Steady_RootMotion", Canter = "Rig_Left_Lead_Canter_RootMotion";
            Func<GameObject> F(string p, int l, int w, int b, string clip, float t, bool s) => () => MountPose(p, l, w, b, clip, t, s);
            void Sheet(string png, params Func<GameObject>[] f) => Row("Temp/cavalry/" + png, f.Select<Func<GameObject>, Func<GameObject>>(m => () => { var g = m(); g.transform.rotation = Quaternion.Euler(0f, 88f, 0f); return g; }).ToArray(), false, 3.3f, 3.2f, 500);
            Sheet("outrider.png", F(O, 1, 0, 0, Idle, 0f, false), F(O, 1, 1, 1, Idle, 0f, true), F(O, 2, 2, 2, Gallop, 0.2f, false), F(O, 3, 3, 3, Gallop, 0.7f, true), F(O, 3, 3, 3, Idle, 0f, false));
            Sheet("cataphract.png", F(C, 1, 0, 0, Idle, 0f, false), F(C, 1, 1, 1, Idle, 0f, true), F(C, 1, 2, 2, Canter, 0.3f, false), F(C, 1, 3, 3, Canter, 0.7f, true));
            Sheet("lexor.png", F(X, 1, 0, 0, Idle, 0f, false), F(X, 1, 2, 0, Gallop, 0.1f, false), F(X, 1, 3, 0, Gallop, 0.6f, true), F(X, 1, 4, 0, Idle, 0f, true));
        }
    }
}
