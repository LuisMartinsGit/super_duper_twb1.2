// KitWeld.cs
// What keeps an army of kit units cheap to draw (2026-11-25, measured with
// BattlePerfProbe: 500 units were 8,600 draw calls and 5 ms of animation):
//   Weld     a body and what is always worn with it (helmet, shield, cape) are
//            one skinned mesh; only the weapon, which follows research on its
//            own, stays a piece
//   Grip     fingers are closed once, when the body is built, and hang on the
//            hand: 24 of a rig's 50 bones are gone
//   Finish   a mesh lists only the bones it uses and the rig keeps only those

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using L = TheWaningBorder.EditorTools.LegionarySpearmanAuthor;

namespace TheWaningBorder.EditorTools
{
    public static partial class InfantryKit
    {
        /// <summary>Melt <paramref name="pieces"/> into a body: each follows the bone it hung on.</summary>
        internal static void Weld(GameObject bodyGo, GameObject rig, params GameObject[] pieces)
        {
            var smr = bodyGo.GetComponent<SkinnedMeshRenderer>();
            var m = smr.sharedMesh;
            var v = m.vertices.ToList(); var n = m.normals.ToList(); var uv = m.uv.ToList(); var w = m.boneWeights.ToList(); var t = m.triangles.ToList();
            var bind = m.bindposes;
            var bones = smr.bones.ToList();
            foreach (var piece in pieces)
            {
                if (piece == null) continue;
                var pm = piece.GetComponent<MeshFilter>().sharedMesh;
                int bone = bones.IndexOf(piece.transform.parent);
                if (bone < 0) { Debug.LogError("[KitWeld] " + piece.name + " hangs on " + piece.transform.parent.name + ", which the body does not have"); continue; }
                var mat = rig.transform.worldToLocalMatrix * piece.transform.localToWorldMatrix;
                int at = v.Count;
                foreach (var x in pm.vertices) v.Add(mat.MultiplyPoint3x4(x));
                foreach (var x in pm.normals) n.Add(mat.MultiplyVector(x).normalized);
                uv.AddRange(pm.uv);
                for (int i = 0; i < pm.vertexCount; i++) w.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                foreach (int i in pm.triangles) t.Add(at + i);
                string path = AssetDatabase.GetAssetPath(pm);
                Object.DestroyImmediate(piece);
                if (path.StartsWith(ArtDir)) AssetDatabase.DeleteAsset(path);
            }
            m.Clear();
            m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.boneWeights = w.ToArray();
            m.bindposes = bind;
            m.RecalculateBounds();
            EditorUtility.SetDirty(m);
        }

        static bool IsFinger(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name == "Hand_L" || p.name == "Hand_R")
                    return t.name.Contains("inger") || t.name.Contains("humb");
            return false;
        }

        /// <summary>Close the fingers of a body being built (as the guard clip holds a
        /// weapon) and hang them on the hand.</summary>
        static void Grip(GameObject rig, Builder b, List<Transform> bones)
        {
            var clip = L.Clip(L.Clips[0]);
            if (clip == null || rig.GetComponent<Animator>() == null) return;
            var all = rig.GetComponentsInChildren<Transform>(true);
            var rest = all.Select(x => (x.localPosition, x.localRotation, x.localScale)).ToArray();
            var fingers = new Dictionary<int, (int hand, Matrix4x4 rest)>();
            for (int i = 0; i < bones.Count; i++)
            {
                if (!IsFinger(bones[i])) continue;
                var h = bones[i];
                while (h.name != "Hand_L" && h.name != "Hand_R") h = h.parent;
                fingers[i] = (bones.IndexOf(h), bones[i].worldToLocalMatrix);
            }
            if (fingers.Count == 0) return;
            var handRest = fingers.Values.Select(f => f.hand).Distinct().ToDictionary(h => h, h => bones[h].localToWorldMatrix);
            clip.SampleAnimation(rig, 0f);
            var move = fingers.ToDictionary(f => f.Key, f => handRest[f.Value.hand] * bones[f.Value.hand].worldToLocalMatrix * bones[f.Key].localToWorldMatrix * f.Value.rest);
            for (int i = 0; i < all.Length; i++) { all[i].localPosition = rest[i].localPosition; all[i].localRotation = rest[i].localRotation; all[i].localScale = rest[i].localScale; }

            for (int i = 0; i < b.v.Count; i++)
            {
                var k = b.w[i];
                var pairs = new (int bone, float w)[] { (k.boneIndex0, k.weight0), (k.boneIndex1, k.weight1), (k.boneIndex2, k.weight2), (k.boneIndex3, k.weight3) };
                if (!pairs.Any(p => p.w > 0f && fingers.ContainsKey(p.bone))) continue;
                Vector3 v = Vector3.zero, n = Vector3.zero; float sum = 0f;
                var to = new Dictionary<int, float>();
                foreach (var p in pairs)
                {
                    if (p.w <= 0f) continue;
                    bool f = fingers.TryGetValue(p.bone, out var info);
                    v += p.w * (f ? move[p.bone].MultiplyPoint3x4(b.v[i]) : b.v[i]);
                    n += p.w * (f ? move[p.bone].MultiplyVector(b.n[i]) : b.n[i]);
                    int target = f ? info.hand : p.bone;
                    to.TryGetValue(target, out float was);
                    to[target] = was + p.w;
                    sum += p.w;
                }
                b.v[i] = v / sum;
                b.n[i] = n.normalized;
                var o = to.OrderByDescending(x => x.Value).Take(4).ToArray();
                var bw = new BoneWeight();
                if (o.Length > 0) { bw.boneIndex0 = o[0].Key; bw.weight0 = o[0].Value / sum; }
                if (o.Length > 1) { bw.boneIndex1 = o[1].Key; bw.weight1 = o[1].Value / sum; }
                if (o.Length > 2) { bw.boneIndex2 = o[2].Key; bw.weight2 = o[2].Value / sum; }
                if (o.Length > 3) { bw.boneIndex3 = o[3].Key; bw.weight3 = o[3].Value / sum; }
                b.w[i] = bw;
            }
        }

        /// <summary>Last step of a rig: meshes keep the bones they use, the rig keeps those bones
        /// and whatever carries a piece.</summary>
        internal static void Finish(GameObject rig)
        {
            var keep = new HashSet<Transform> { rig.transform };
            void Keep(Transform t) { for (; t != null && t != rig.transform.parent; t = t.parent) if (!keep.Add(t)) break; }
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                var w = m.boneWeights;
                var used = new SortedSet<int>();
                foreach (var k in w)
                {
                    if (k.weight0 > 0f) used.Add(k.boneIndex0);
                    if (k.weight1 > 0f) used.Add(k.boneIndex1);
                    if (k.weight2 > 0f) used.Add(k.boneIndex2);
                    if (k.weight3 > 0f) used.Add(k.boneIndex3);
                }
                var list = used.ToList();
                if (list.Count < smr.bones.Length)
                {
                    var map = list.Select((x, i) => (x, i)).ToDictionary(p => p.x, p => p.i);
                    int M(int i, float f) => f > 0f ? map[i] : 0;
                    for (int i = 0; i < w.Length; i++)
                    {
                        var k = w[i];
                        w[i] = new BoneWeight { boneIndex0 = M(k.boneIndex0, k.weight0), weight0 = k.weight0, boneIndex1 = M(k.boneIndex1, k.weight1), weight1 = k.weight1, boneIndex2 = M(k.boneIndex2, k.weight2), weight2 = k.weight2, boneIndex3 = M(k.boneIndex3, k.weight3), weight3 = k.weight3 };
                    }
                    var bind = m.bindposes;
                    var bones = smr.bones;
                    m.boneWeights = w;
                    m.bindposes = list.Select(i => bind[i]).ToArray();
                    smr.bones = list.Select(i => bones[i]).ToArray();
                    EditorUtility.SetDirty(m);
                }
                foreach (var b in smr.bones) Keep(b);
                Keep(smr.transform);
            }
            // A piece, a light, a script: anything that is more than a bone.
            foreach (var c in rig.GetComponentsInChildren<Component>(true)) if (c != null && !(c is Transform)) Keep(c.transform);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true).Where(x => x.name == "Hand_L" || x.name == "Hand_R" || x.name == "Head" || x.name == "Hips").ToList()) Keep(t);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true).Where(x => !keep.Contains(x)).ToList())
                if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        /// <summary>After a unit's height is measured: room in the bounds for the crest and the clips' reach.</summary>
        internal static void Grow(GameObject root)
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var b = smr.localBounds;
                b.Encapsulate(smr.sharedMesh.bounds);
                smr.localBounds = b;
            }
        }

        /// <summary>The object a unit's clips are sampled on.</summary>
        internal static GameObject Rig(TheWaningBorder.Rendering.UnitAgeKit kit) => kit.GetComponentInChildren<Animator>(true).gameObject;
    }
}
