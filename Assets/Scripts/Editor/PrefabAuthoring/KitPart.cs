// KitPart.cs
// One Synty kit mesh read into rest-pose world space (T-pose, feet at the
// origin, facing +Z): the body of a character prefab, or the mesh of a rigid
// piece. The kits' skeletons agree on every shared bone to 3 cm, so a part read
// this way can be put on another kit's skeleton by bone name.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public sealed class KitPart
    {
        public Vector3[] verts;
        public Vector3[] normals;
        public Vector2[] uv;
        public int[] tris;
        public BoneWeight[] weights;   // null on a rigid piece
        public string[] bones;         // BoneKey names, for weights
        public Material material;

        Color32[] _px, _skin, _hair;
        int _w, _h, _sw, _sh;
        Color _skinTone, _hairTone;

        static readonly Dictionary<Texture, (Color32[], int, int)> _cache = new();

        internal static (Color32[], int, int) Pixels(Texture t)
        {
            if (t == null) return (null, 0, 0);
            if (_cache.TryGetValue(t, out var c)) return c;
            int w = Mathf.Min(t.width, 2048), h = Mathf.Min(t.height, 2048);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return _cache[t] = (px, w, h);
        }

        /// <summary>The largest shown skinned body of the prefab (or the one named
        /// <paramref name="meshName"/>, shown or not); a prefab with none is read as a rigid piece.</summary>
        public static KitPart Load(string prefabPath, string meshName = null, string materialPath = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) throw new FileNotFoundException(prefabPath);
            var go = Object.Instantiate(asset);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            try
            {
                var k = new KitPart();
                SkinnedMeshRenderer smr = null;
                foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (s.sharedMesh == null) continue;
                    if (meshName != null) { if (s.name == meshName) smr = s; }
                    else if (s.gameObject.activeSelf && (smr == null || s.sharedMesh.vertexCount > smr.sharedMesh.vertexCount)) smr = s;
                }
                if (smr != null)
                {
                    var m = smr.sharedMesh;
                    k.tris = m.triangles;
                    k.uv = m.uv;
                    k.weights = m.boneWeights;
                    k.bones = smr.bones.Select(b => b != null ? BoneKey(b) : "").ToArray();
                    var bind = m.bindposes;
                    var sv = m.vertices;
                    var sn = m.normals;
                    k.verts = new Vector3[sv.Length];
                    k.normals = new Vector3[sv.Length];
                    var mats = new Matrix4x4[bind.Length];
                    for (int i = 0; i < bind.Length; i++)
                        mats[i] = smr.bones[i] != null ? smr.bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;
                    for (int i = 0; i < sv.Length; i++)
                    {
                        var w = k.weights[i];
                        Vector3 v = Vector3.zero, n = Vector3.zero;
                        void Add(int b, float f)
                        {
                            if (f <= 0f) return;
                            v += f * mats[b].MultiplyPoint3x4(sv[i]);
                            n += f * mats[b].MultiplyVector(sn[i]);
                        }
                        Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1);
                        Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                        k.verts[i] = v;
                        k.normals[i] = n.normalized;
                    }
                    k.material = smr.sharedMaterial;
                }
                else
                {
                    var mf = go.GetComponentsInChildren<MeshFilter>(false).First(f => f.sharedMesh != null);
                    var m = mf.sharedMesh;
                    var l2w = mf.transform.localToWorldMatrix;
                    k.tris = m.triangles;
                    k.uv = m.uv;
                    k.verts = m.vertices.Select(v => l2w.MultiplyPoint3x4(v)).ToArray();
                    k.normals = m.normals.Select(v => l2w.MultiplyVector(v).normalized).ToArray();
                    k.material = mf.GetComponent<MeshRenderer>().sharedMaterial;
                }
                if (materialPath != null) k.material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                k.ReadMaterial();
                return k;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Kits disagree on finger names ("Finger_01 1", "Finger_01_R"):
        /// a finger is its hand and its stem.</summary>
        public static string BoneKey(Transform b)
        {
            for (var t = b.parent; t != null; t = t.parent)
                if (t.name == "Hand_L" || t.name == "Hand_R")
                {
                    string stem = b.name.Split(' ')[0];
                    if (stem.EndsWith("_L") || stem.EndsWith("_R")) stem = stem.Substring(0, stem.Length - 2);
                    return t.name + "/" + stem.ToLowerInvariant();
                }
            return b.name;
        }

        void ReadMaterial()
        {
            Texture T(string p) => material.HasProperty(p) ? material.GetTexture(p) : null;
            var albedo = T("_Albedo_Map");
            if (albedo == null) albedo = T("_BaseMap");
            if (albedo == null) albedo = T("_MainTex");
            (_px, _w, _h) = Pixels(albedo);
            (_skin, _sw, _sh) = Pixels(T("_Skin_Mask"));
            int hw, hh;
            (_hair, hw, hh) = Pixels(T("_Hair_Mask"));
            if (_hair != null && (hw != _sw || hh != _sh)) _hair = null;
            if (_skin != null) _skinTone = material.GetColor("_Skin_Color");
            if (_hair != null) _hairTone = material.GetColor("_Hair_Color");
        }

        /// <summary>The colour a texture point is drawn in (skin and hair masks applied).</summary>
        public Color32 ColourAt(Vector2 q)
        {
            if (_px == null) return new Color32(255, 0, 255, 255);
            if (_skin != null && Px(_skin, _sw, _sh, q).r > 127) return (Color32)_skinTone;
            if (_hair != null && Px(_hair, _sw, _sh, q).r > 127) return (Color32)_hairTone;
            return Px(_px, _w, _h, q);
        }

        static Color32 Px(Color32[] px, int w, int h, Vector2 q)
            => px[Mathf.Clamp((int)(Mathf.Repeat(q.y, 1f) * h), 0, h - 1) * w
                  + Mathf.Clamp((int)(Mathf.Repeat(q.x, 1f) * w), 0, w - 1)];

        /// <summary>Triangle index lists of the mesh's separate shells, largest first.</summary>
        public List<List<int>> Islands()
        {
            var id = new Dictionary<Vector3Int, int>();
            var weld = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var key = Vector3Int.RoundToInt(verts[i] * 5000f);
                if (!id.TryGetValue(key, out weld[i])) { weld[i] = id.Count; id[key] = weld[i]; }
            }
            var up = Enumerable.Range(0, id.Count).ToArray();
            int Root(int a) { while (up[a] != a) a = up[a] = up[up[a]]; return a; }
            for (int t = 0; t < tris.Length; t += 3)
            {
                up[Root(weld[tris[t]])] = Root(weld[tris[t + 1]]);
                up[Root(weld[tris[t + 1]])] = Root(weld[tris[t + 2]]);
            }
            var groups = new Dictionary<int, List<int>>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int r = Root(weld[tris[t]]);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
                l.Add(tris[t]); l.Add(tris[t + 1]); l.Add(tris[t + 2]);
            }
            return groups.Values.OrderByDescending(l => l.Count).ThenBy(l => verts[l[0]].x).ToList();
        }
    }
}
