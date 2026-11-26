// InfantryKit.cs
// The look of the Barracks / Garrison infantry, mixed from the three Synty kits
// (developer, 2026-11-25: "mix up items from all kits to create something
// unique"): R_ Ancient Empire, J_ Samurai Empire, K_ Fantasy Kingdom. This file
// holds the RECIPES and the two things that make a mix possible:
//
//   Fusing    A body is several kit parts on ONE skeleton (the Roman soldier's,
//             whose clips are already tuned). A part is a whole body, some body
//             ZONES of it (torso, thighs ...), or some of its loose shells.
//   Palette   Every triangle is re-pointed at one small shared texture whose
//             cells have a ROLE: gold, steel, cloth (the player's colour),
//             glow, matte. One material draws the whole soldier, the gold of
//             every kit is the same gold, and it shines: the cells carry the
//             emission the buildings' gold has (GoldGlowPass) and a metallic
//             highlight. Kit atlases are up to 4096 px and are not used at all.
//
// LegionarySpearmanAuthor and SwordsmanSyntyAuthor assemble the prefabs.
// docs/Design/Age_1_Alanthor.md, The infantry's look.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheWaningBorder.EditorTools
{
    public static partial class InfantryKit
    {
        internal const string R = "Assets/Synty/PolygonAncientEmpire/Prefabs/";
        internal const string J = "Assets/Synty/PolygonSamuraiEmpire/Prefabs/";
        internal const string K = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string RChr = R + "Characters/R_SM_Chr_", JChr = J + "Characters/J_SM_Chr_", KChr = K + "Characters/K_SM_Chr_";
        const string RAtt = R + "Characters/Attach/R_SM_Chr_Attach_", JAtt = J + "Characters/Chr_Attach/J_SM_Chr_Attach_";
        const string RMat = "Assets/Synty/PolygonAncientEmpire/Materials/Alts/PolygonAncientEmpire_01_A.mat";
        const string ArtDir = "Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Art/";

        internal enum Role { Matte, Cloth, Gold, Brass, Steel, Veil, Shard, Holy, Lamp }

        /// <summary>Body zones, by the bone a triangle mostly hangs on.</summary>
        [Flags]
        internal enum Zone { Head = 1, Torso = 2, UpperArm = 4, ForeArm = 8, Thigh = 16, Shin = 32, All = 63 }

        /// <summary>One kit part of a recipe.</summary>
        internal sealed class Part
        {
            public string prefab, mesh, material;
            public Zone zones = Zone.All;
            /// <summary>Loose shells to take (KitIslandProbe numbers); null: every shell.</summary>
            public int[] islands;
            /// <summary>Only triangles whose painted colour passes.</summary>
            public Func<Color32, bool> only;
            /// <summary>Repaint: a colour's role and, for Matte, its new colour.</summary>
            public Func<Color32, Vector3, (Role role, Color32 colour)?> paint;
            public Vector3 move, euler;
            public float scale = 1f;
            /// <summary>Has a painted picture: its textured triangles keep it (a patch).</summary>
            public bool painted;
            /// <summary>Reshapes the piece, in its own frame, before it is placed.</summary>
            public Func<Vector3, Vector3> warp;
            /// <summary>A hat modelled on the standing body: moved to the head bone's frame.</summary>
            public bool headSpace;
            /// <summary>The bone a rigid piece hangs on in a fused body (the horse's barding).</summary>
            public string bone = "Head";
            /// <summary>Turned and scaled about its own middle, which lands on <see cref="move"/>.</summary>
            public bool centred;
        }

        // ── Palette ────────────────────────────────────────────────────

        const int Cell = 8, Grid = 32, Size = 512, PatchPx = 128;   // cells bottom left, painted patches above
        static readonly Dictionary<(Role, Color32), int> _cells = new();
        static readonly List<(Role role, Color32 colour)> _order = new();

        /// <summary>Forget the palette: called once before a full author run, so its
        /// cells are exactly what the prefabs use.</summary>
        static void ResetPalette() { _cells.Clear(); _order.Clear(); _patches.Clear(); }

        /// <summary>A painted area of a kit atlas (the lion, the chest crest),
        /// repainted texel by texel into the palette.</summary>
        static readonly List<(string key, Rect uv, (Role role, Color32 colour)[] px)> _patches = new();

        static int PatchIndex(string key, Rect uv, Func<Vector2, (Role, Color32)> at)
        {
            int i = _patches.FindIndex(p => p.key == key);
            if (i >= 0) return i;
            var px = new (Role, Color32)[PatchPx * PatchPx];
            for (int y = 0; y < PatchPx; y++)
                for (int x = 0; x < PatchPx; x++)
                    px[y * PatchPx + x] = at(new Vector2(uv.x + uv.width * (x + 0.5f) / PatchPx, uv.y + uv.height * (y + 0.5f) / PatchPx));
            _patches.Add((key, uv, px));
            if (_patches.Count > 8) throw new InvalidOperationException("no room for another patch");
            return _patches.Count - 1;
        }

        static Vector2 PatchUv(int i, Vector2 q)
        {
            var uv = _patches[i].uv;
            var o = new Vector2(i % 4 * PatchPx, Grid * Cell + i / 4 * PatchPx);
            return (o + new Vector2(Mathf.Clamp((q.x - uv.x) / uv.width, 0.004f, 0.996f), Mathf.Clamp((q.y - uv.y) / uv.height, 0.004f, 0.996f)) * PatchPx) / Size;
        }

        static Vector2 CellUv(Role role, Color32 c)
        {
            // Few, distinct shades: neighbouring colours share a cell.
            c = new Color32(Q(c.r), Q(c.g), Q(c.b), 255);
            if (!_cells.TryGetValue((role, c), out int i))
            {
                i = _order.Count;
                if (i >= Grid * Grid) throw new InvalidOperationException("infantry palette full");
                _cells[(role, c)] = i;
                _order.Add((role, c));
            }
            return new Vector2(i % Grid + 0.5f, i / Grid + 0.5f) * Cell / Size;
        }

        static byte Q(byte v) => (byte)Mathf.Min(255, (v + 2) / 5 * 5);

        static readonly Color GoldTint = new Color(1.1f, 0.85f, 0.45f);   // as GoldGlowPass
        const float EmissionScale = 6f;                                    // HDR range of the map

        internal static Material Material()
        {
            Directory.CreateDirectory(ArtDir);
            string path = ArtDir + "Infantry.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        /// <summary>Write the palette textures and the material that uses them.</summary>
        internal static void WritePalette(StringBuilder log)
        {
            var albedo = new Color32[Size * Size];
            var glow = new Color32[Size * Size];
            var metal = new Color32[Size * Size];
            for (int i = 0; i < albedo.Length; i++) { albedo[i] = new Color32(90, 90, 90, 255); glow[i] = new Color32(0, 0, 0, 255); metal[i] = new Color32(0, 0, 0, 40); }
            void Paint(int at, Role role, Color32 c)
            {
                Color lin = ((Color)c).linear, e = Color.black;
                float metallic = 0f, smooth = 0.16f;
                switch (role)
                {
                    case Role.Gold: e = lin * GoldTint * 0.6f; metallic = 0.55f; smooth = 0.66f; break;
                    case Role.Brass: e = lin * GoldTint * 0.22f; metallic = 0.5f; smooth = 0.55f; break;
                    case Role.Steel: metallic = 0.3f; smooth = 0.42f; break;
                    case Role.Veil: e = new Color(0.08f, 0.6f, 0.807f) * 2.0f; metallic = 0.2f; smooth = 0.6f; break;
                    case Role.Shard: e = new Color(0.16f, 0.75f, 1f) * 3.2f; smooth = 0.6f; break;
                    case Role.Holy: e = new Color(1f, 0.8f, 0.39f) * 1.9f; smooth = 0.6f; break;    // EmissiveLadder.Sanctum's gold
                    case Role.Lamp: e = new Color(1f, 0.43f, 0.1f) * 2.1f; break;                   // EmissiveLadder.Window
                    case Role.Cloth: smooth = 0.22f; break;
                }
                Color g = new Color(e.r / EmissionScale, e.g / EmissionScale, e.b / EmissionScale, 1f).gamma;
                g.a = 1f;
                byte mb = (byte)(metallic * 255f);
                albedo[at] = new Color32(c.r, c.g, c.b, 255);
                glow[at] = g;
                metal[at] = new Color32(mb, mb, mb, (byte)(smooth * 255f));
            }
            for (int i = 0; i < _order.Count; i++)
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                        Paint((i / Grid * Cell + y) * Size + i % Grid * Cell + x, _order[i].role, _order[i].colour);
            for (int i = 0; i < _patches.Count; i++)
                for (int y = 0; y < PatchPx; y++)
                    for (int x = 0; x < PatchPx; x++)
                    {
                        var (role, c) = _patches[i].px[y * PatchPx + x];
                        Paint((Grid * Cell + i / 4 * PatchPx + y) * Size + i % 4 * PatchPx + x, role, c);
                    }
            var mat = Material();
            mat.SetTexture("_BaseMap", Save("Infantry_Albedo.png", albedo, true));
            mat.SetTexture("_EmissionMap", Save("Infantry_Glow.png", glow, true));
            mat.SetTexture("_MetallicGlossMap", Save("Infantry_Metal.png", metal, false));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_EmissionColor", new Color(EmissionScale, EmissionScale, EmissionScale, 1f));
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_WorkflowMode", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.EnableKeyword("_EMISSION");
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            log.AppendLine($"palette: {_order.Count} cells, " + string.Join(" ", _order.GroupBy(o => o.role).Select(g => g.Key + "=" + g.Count())));
        }

        static Texture2D Save(string name, Color32[] px, bool srgb)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            string path = ArtDir + name;
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            // Flat cells: no mips (they would bleed one role into the next) and
            // no block compression.
            if (imp.mipmapEnabled || imp.sRGBTexture != srgb || imp.filterMode != FilterMode.Point
                || imp.textureCompression != TextureImporterCompression.Uncompressed)
            {
                imp.mipmapEnabled = false;
                imp.sRGBTexture = srgb;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.alphaIsTransparency = false;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ── Roles ──────────────────────────────────────────────────────

        /// <summary>What a kit colour is, and the shade it takes in the palette.</summary>
        static (Role, Color32) Classify(Color32 c, Role gold)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // Cloth and lacquer: the kits' reds, blues and navies are the
            // player's colour, kept as light and dark of one pure red that
            // UnitPlayerColor swaps in a match.
            bool red = c.r > 60 && c.r > c.g * 2 && c.r > c.b * 2;
            bool blue = h > 0.49f && h < 0.75f && s > 0.32f && v > 0.12f;
            if (red || blue) return Cloth(v < 0.5f ? Mathf.Lerp(0.42f, 0.72f, Mathf.InverseLerp(0.2f, 0.5f, v)) : Mathf.Lerp(0.72f, 0.95f, (v - 0.5f) * 2f));
            if (h > 0.074f && h < 0.16f && s > 0.40f && v > 0.40f)
            {
                float k = Mathf.Lerp(0.62f, 1f, Mathf.InverseLerp(0.4f, 0.95f, v));
                if (gold == Role.Gold) return (Role.Gold, new Color32((byte)(250 * k), (byte)(188 * k), (byte)(84 * k), 255));
                if (gold == Role.Brass) return (Role.Brass, new Color32((byte)(206 * k), (byte)(150 * k), (byte)(82 * k), 255));
                return (Role.Matte, new Color32((byte)(122 * k), (byte)(88 * k), (byte)(60 * k), 255));
            }
            bool steel = v > 0.4f && v < 0.85f && s < 0.25f && ((h > 0.45f && h < 0.75f) || s < 0.04f);
            if (steel) return (Role.Steel, new Color32((byte)(c.r * 0.96f), (byte)(c.g * 0.98f), c.b, 255));
            return (Role.Matte, c);
        }

        static (Role, Color32) Cloth(float v) => (Role.Cloth, new Color32((byte)(255 * v), (byte)(26 * v), (byte)(26 * v), 255));
        static (Role, Color32)? Is(Role r, byte a, byte b, byte c) => (r, new Color32(a, b, c, 255));
        static float V(Color32 c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
        static bool IsRole(Color32 c, Role r) => Classify(c, Role.Gold).Item1 == r;

        // ── Building meshes ────────────────────────────────────────────

        static Zone ZoneOf(string bone)
        {
            if (bone.StartsWith("Hand") || bone.StartsWith("Elbow")) return Zone.ForeArm;
            if (bone.StartsWith("Shoulder")) return Zone.UpperArm;
            if (bone.StartsWith("UpperLeg")) return Zone.Thigh;
            if (bone.StartsWith("LowerLeg") || bone.StartsWith("Ankle") || bone.StartsWith("Ball") || bone.StartsWith("Toe")) return Zone.Shin;
            if (bone.StartsWith("Hips") || bone.StartsWith("Spine") || bone.StartsWith("Clavicle") || bone == "Root") return Zone.Torso;
            return Zone.Head;
        }

        /// <summary>The Roman skeleton's head bone, standing at rest.</summary>
        static Vector3 HeadAt
        {
            get
            {
                if (_headAt.HasValue) return _headAt.Value;
                var a = AssetDatabase.LoadAssetAtPath<GameObject>(RSoldier);
                foreach (var t in a.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head") { _headAt = a.transform.InverseTransformPoint(t.position); break; }
                return _headAt.Value;
            }
        }
        static Vector3? _headAt;

        sealed class Builder
        {
            public readonly List<Vector3> v = new(), n = new();
            public readonly List<Vector2> uv = new();
            public readonly List<BoneWeight> w = new();
            public readonly List<int> t = new();

            public void Add(Part p, Role gold, Func<string, int> boneIndex, StringBuilder log)
            {
                var k = KitPart.Load(p.prefab, p.mesh, p.material);
                var tri = new List<int>();
                if (p.islands == null) tri.AddRange(k.tris);
                else
                {
                    var shells = k.Islands();
                    foreach (int i in p.islands) if (i < shells.Count) tri.AddRange(shells[i]); else log.AppendLine($"no shell {i} in {p.prefab}");
                }
                var rot = Quaternion.Euler(p.euler);
                var middle = Vector3.zero;
                if (p.centred)
                {
                    var mb = new Bounds(k.verts[0], Vector3.zero);
                    foreach (var x in k.verts) mb.Encapsulate(x);
                    middle = mb.center;
                }
                int[] map = null;
                if (boneIndex != null && k.weights != null) map = k.bones.Select(boneIndex).ToArray();
                bool Textured(int i)
                {
                    Vector2 ua = k.uv[tri[i]], ub = k.uv[tri[i + 1]], uc = k.uv[tri[i + 2]];
                    var lo = Vector2.Min(ua, Vector2.Min(ub, uc));
                    var hi = Vector2.Max(ua, Vector2.Max(ub, uc));
                    return hi.x - lo.x > 0.004f && hi.y - lo.y > 0.004f;
                }
                // One patch per separate picture (the lion, the rim's key pattern):
                // textured triangles whose atlas areas touch are one picture.
                var areas = new List<Rect>();
                Rect Area(int i, float grow)
                {
                    Vector2 ua = k.uv[tri[i]], ub = k.uv[tri[i + 1]], uc = k.uv[tri[i + 2]];
                    var lo = Vector2.Min(ua, Vector2.Min(ub, uc)) - Vector2.one * grow;
                    var hi = Vector2.Max(ua, Vector2.Max(ub, uc)) + Vector2.one * grow;
                    return new Rect(lo, hi - lo);
                }
                if (p.painted)
                {
                    for (int i = 0; i < tri.Count; i += 3)
                    {
                        if (!Textured(i)) continue;
                        var box = Area(i, 0.001f);
                        for (int j = areas.Count - 1; j >= 0; j--)
                            if (areas[j].Overlaps(box))
                            {
                                box = Rect.MinMaxRect(Mathf.Min(box.xMin, areas[j].xMin), Mathf.Min(box.yMin, areas[j].yMin),
                                    Mathf.Max(box.xMax, areas[j].xMax), Mathf.Max(box.yMax, areas[j].yMax));
                                areas.RemoveAt(j);
                                j = areas.Count;
                            }
                        areas.Add(box);
                    }
                    foreach (var box in areas)
                        log.AppendLine($"    patch {Patch(box)}: uv {box.min.ToString("0.000")} .. {box.max.ToString("0.000")}");
                }
                int Patch(Rect box) => PatchIndex(p.prefab + gold + box.min.ToString("0.000"), box, at =>
                {
                    var pc = k.ColourAt(at);
                    return p.paint?.Invoke(pc, Vector3.zero) ?? Classify(pc, gold);
                });
                var seen = new Dictionary<(int, Vector2), int>();
                int kept = 0;
                for (int i = 0; i < tri.Count; i += 3)
                {
                    int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                    if (p.zones != Zone.All && k.weights != null)
                    {
                        // The triangle belongs to the zone most of its corners hang on.
                        Zone za = ZoneOf(k.bones[k.weights[a].boneIndex0]), zb = ZoneOf(k.bones[k.weights[b].boneIndex0]), zc = ZoneOf(k.bones[k.weights[c].boneIndex0]);
                        var z = zb == zc ? zb : za;
                        if ((p.zones & z) == 0) continue;
                    }
                    var col = k.ColourAt((k.uv[a] + k.uv[b] + k.uv[c]) / 3f);
                    if (p.only != null && !p.only(col)) continue;
                    var mid = (k.verts[a] + k.verts[b] + k.verts[c]) / 3f;
                    var (role, shade) = p.paint?.Invoke(col, mid) ?? Classify(col, gold);
                    bool pic = p.painted && Textured(i);
                    int patch = -1;
                    if (pic) patch = Patch(areas.First(r => r.Contains(Area(i, 0f).center)));
                    var cell = pic ? Vector2.zero : CellUv(role, shade);
                    foreach (int x in new[] { a, b, c })
                    {
                        var q = pic ? PatchUv(patch, k.uv[x]) : cell;
                        if (!seen.TryGetValue((x, q), out int at))
                        {
                            at = v.Count;
                            seen[(x, q)] = at;
                            var at0 = k.verts[x];
                            if (p.headSpace && k.weights != null) at0 -= HeadAt;
                            if (p.warp != null) at0 = p.warp(at0);
                            if (p.centred) at0 -= middle;
                            v.Add(rot * (at0 * p.scale) + p.move);
                            n.Add(rot * k.normals[x]);
                            uv.Add(q);
                            if (boneIndex != null)
                            {
                                var s = k.weights != null ? k.weights[x] : default;
                                w.Add(map == null ? new BoneWeight { boneIndex0 = boneIndex(p.bone), weight0 = 1f } : new BoneWeight
                                {
                                    boneIndex0 = map[s.boneIndex0], weight0 = s.weight0,
                                    boneIndex1 = map[s.boneIndex1], weight1 = s.weight1,
                                    boneIndex2 = map[s.boneIndex2], weight2 = s.weight2,
                                    boneIndex3 = map[s.boneIndex3], weight3 = s.weight3,
                                });
                            }
                        }
                        t.Add(at);
                    }
                    kept++;
                }
                log.AppendLine($"    {Path.GetFileNameWithoutExtension(p.prefab)} {p.zones}: {kept} tris");
            }

            public void Fill(Mesh m)
            {
                m.Clear();
                m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(v);
                for (int i = 0; i < n.Count; i++) if (!(n[i].sqrMagnitude > 0.25f)) n[i] = Vector3.up;
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                if (w.Count > 0) m.boneWeights = w.ToArray();
                m.RecalculateBounds();
            }
        }

        static Mesh SaveMesh(string name, Action<Mesh> fill)
        {
            Directory.CreateDirectory(ArtDir);
            string path = ArtDir + name + ".asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = m == null;
            if (fresh) m = new Mesh { name = name };
            fill(m);
            if (fresh) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A fused body on <paramref name="rig"/>'s skeleton (the rig is at rest, at the origin).</summary>
        internal static GameObject Body(GameObject rig, string name, Role gold, Part[] parts, StringBuilder log)
        {
            var bones = new List<Transform>();
            var byKey = new Dictionary<string, int>();
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t == rig.transform || t.GetComponent<Renderer>() != null) continue;
                string key = KitPart.BoneKey(t);
                if (byKey.ContainsKey(key)) continue;
                byKey[key] = bones.Count;
                bones.Add(t);
            }
            int Index(string key)
            {
                if (byKey.TryGetValue(key, out int i)) return i;
                int slash = key.IndexOf('/');
                if (slash > 0) return byKey[key.Substring(0, slash)];
                if (key.Length > 0) log.AppendLine($"    bone {key} not on the rig: to the head or chest");
                return key.Contains("Hair") || key.Contains("Eye") || key.Contains("Jaw") ? byKey["Head"] : byKey["Spine_03"];
            }
            var b = new Builder();
            log.AppendLine("  " + name);
            foreach (var p in parts) b.Add(p, gold, Index, log);
            Grip(rig, b, bones);
            var mesh = SaveMesh(name, m =>
            {
                b.Fill(m);
                m.bindposes = bones.Select(x => x.worldToLocalMatrix * rig.transform.localToWorldMatrix).ToArray();
            });
            log.AppendLine($"    = {b.t.Count / 3} tris, {b.v.Count} verts");
            var go = new GameObject(name);
            go.transform.SetParent(rig.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones.ToArray();
            smr.rootBone = null;
            // Room for the clips' reach; the top stays the body's own, which the
            // authors measure the unit's height by.
            var mb = mesh.bounds;
            smr.localBounds = new Bounds(mb.center, new Vector3(2.2f, mb.size.y, 2.2f));
            smr.sharedMaterial = Material();
            smr.updateWhenOffscreen = false;
            return go;
        }

        /// <summary>A fused rigid piece (helmet, shield, weapon) hung on <paramref name="bone"/>.</summary>
        internal static GameObject Piece(string name, Transform bone, Vector3 lp, Vector3 le, Role gold, Part[] parts, StringBuilder log)
        {
            var b = new Builder();
            log.AppendLine("  " + name);
            foreach (var p in parts) b.Add(p, gold, null, log);
            var mesh = SaveMesh(name, b.Fill);
            var go = new GameObject(name);
            go.transform.SetParent(bone, false);
            go.transform.localPosition = lp;
            go.transform.localRotation = Quaternion.Euler(le);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Material();
            return go;
        }

        // ── Recipes: bodies ────────────────────────────────────────────

        static Part Body(string prefab, Zone z) => new Part { prefab = prefab, zones = z, material = prefab.StartsWith(RChr) ? RMat : null };
        static Part Shells(string prefab, params int[] islands) => new Part { prefab = prefab, islands = islands, material = prefab.StartsWith(RChr) ? RMat : null };

        const string RSoldier = RChr + "Soldier_Male_01.prefab", KSoldier = KChr + "Soldier_Male_01.prefab", KPeasant = KChr + "Peasant_Male_01.prefab",
            JSoldier = JChr + "Soldier_Male_01.prefab", Samurai1 = JChr + "Samurai_Male_01.prefab", Samurai2 = JChr + "Samurai_Male_02.prefab", Samurai3 = JChr + "Samurai_Male_03.prefab";
        static readonly int[] RomanShoulders = { 3, 4, 5, 6 };
        static readonly int[] SamuraiShoulders = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        const Zone Bare = Zone.All & ~Zone.Shin;

        /// <summary>The Militia: the peasant in the player's vest under a straw rain cape.</summary>
        internal static Part[] LevyBody() => new[]
        {
            new Part { prefab = KPeasant, paint = (c, p) =>
                // The brown vest and skirt are the player's colour.
                Mathf.Abs(c.r - 140) < 10 && Mathf.Abs(c.g - 100) < 10 && Mathf.Abs(c.b - 68) < 10 ? Cloth(0.8f) : null },
        };

        internal static Part[] SpearmanBody(int level) => level switch
        {
            // Ashigaru cuirass and Roman shoulder guards, sandalled.
            1 => new[] { new Part { prefab = JSoldier, zones = Bare, painted = true }, Body(RSoldier, Zone.Shin), Shells(RSoldier, RomanShoulders) },
            // Laced samurai plate, Roman shoulder guards, knight's boots.
            2 => new[] { Body(Samurai2, Bare), Body(KSoldier, Zone.Shin), Shells(RSoldier, RomanShoulders) },
            // The gold-chested samurai, gilded Roman shoulder guards.
            _ => new[] { Body(Samurai1, Zone.All), new Part { prefab = RSoldier, material = RMat, islands = RomanShoulders, paint = (c, p) => IsRole(c, Role.Steel) ? Gilt(c) : null } },
        };

        internal static Part[] SwordsmanBody(int level) => level switch
        {
            // A knight's coat over ashigaru sleeves, Roman sandals.
            1 => new[] { Body(KSoldier, Zone.Head | Zone.Torso | Zone.Thigh), Body(JSoldier, Zone.UpperArm | Zone.ForeArm), Body(RSoldier, Zone.Shin) },
            // The coat under laced samurai shoulders, samurai sleeves and greaves.
            2 => new[] { Body(KSoldier, Zone.Head | Zone.Torso | Zone.Thigh), Body(Samurai2, Zone.UpperArm | Zone.ForeArm | Zone.Shin), Shells(Samurai3, SamuraiShoulders) },
            // Full samurai plate over the knight's skirt.
            _ => new[] { Body(Samurai3, Zone.Head | Zone.Torso | Zone.UpperArm | Zone.ForeArm | Zone.Shin), Body(KSoldier, Zone.Thigh) },
        };

        static (Role, Color32)? Gilt(Color32 c)
        {
            float k = Mathf.Lerp(0.62f, 1f, Mathf.InverseLerp(0.35f, 0.75f, V(c)));
            return (Role.Gold, new Color32((byte)(250 * k), (byte)(188 * k), (byte)(84 * k), 255));
        }

        // ── Recipes: head ──────────────────────────────────────────────

        static Part Roman(string piece, Func<Color32, bool> only = null, Func<Color32, Vector3, (Role, Color32)?> paint = null)
            => new Part { prefab = RAtt + piece + ".prefab", material = RMat, only = only, paint = paint };
        static Part Samurai(string piece) => new Part { prefab = JAtt + piece + ".prefab" };
        static bool IsCrest(Color32 c) => IsRole(c, Role.Cloth);

        internal static Part[] LevyHat() => new[] { Samurai("Hat_02") };

        internal static Part[] SpearmanHelmet(int level) => level switch
        {
            1 => new[] { Roman("Helmet_01") },
            // Kabuto under a Roman crest.
            2 => new[] { Samurai("Helmet_03"), Crest("Helmet_03", 0.02f) },
            // The horned kabuto, the mask, and the great transverse crest.
            _ => new[] { Samurai("Helmet_04"), Samurai("Mask_01"), Crest("Helmet_02", 0.02f) },
        };

        static Part Crest(string piece, float up)
        {
            var p = Roman(piece, IsCrest);
            p.move = new Vector3(0f, up, 0f);
            return p;
        }

        static Part Mane() => new Part
        {
            prefab = JAtt + "Helmet_Fur_01.prefab", move = new Vector3(0f, -0.06f, -0.09f), scale = 0.9f,
            paint = (c, p) => Cloth(Mathf.Lerp(0.6f, 0.95f, V(c))),
        };

        internal static Part[] SwordsmanHelmet(int level) => level switch
        {
            1 => new[] { new Part { prefab = KChr.Replace("K_SM_Chr_", "Attachments/K_SM_Chr_Attach_") + "Soldier_01.prefab", scale = 1.06f, move = new Vector3(0f, 0.065f, 0.03f) } },
            // The moon-crested kabuto over a half mask.
            2 => new[] { Samurai("Helmet_02") },
            // The moon kabuto, the mask, and a mane in the player's colour.
            _ => new[] { Samurai("Helmet_02"), Samurai("Mask_02"), Mane() },
        };

        // ── Recipes: shields ───────────────────────────────────────────

        static Part Scutum(Func<Color32, Vector3, (Role, Color32)?> paint)
            => new Part { prefab = R + "Weapons/R_SM_Wep_Shield_04.prefab", material = RMat, paint = paint };

        internal static Part[] SpearmanShield(int level) => level switch
        {
            // Wood; then white wings on the player's colour; then gold ones.
            1 => new[] { Scutum((c, p) => IsRole(c, Role.Cloth) ? Is(Role.Matte, 138, 98, 62) : IsRole(c, Role.Gold) ? Is(Role.Matte, 92, 62, 40) : null) },
            2 => new[] { Scutum((c, p) => IsRole(c, Role.Gold) && V(c) > 0.7f ? Is(Role.Matte, 232, 228, 214) : null) },
            _ => new[] { Scutum((c, p) => IsRole(c, Role.Gold) || (!IsRole(c, Role.Cloth) && V(c) > 0.35f) ? Gilt(c) : null) },
        };

        internal static Part[] SwordsmanShield(int level) => level switch
        {
            1 => new[] { new Part { prefab = R + "Weapons/R_SM_Wep_Shield_02.prefab", material = RMat, painted = true } },
            2 => new[] { new Part { prefab = R + "Weapons/R_SM_Wep_Shield_01.prefab", material = RMat, painted = true } },
            // The knight's gold-rimmed shield, its field the player's colour.
            _ => new[] { new Part { prefab = K + "Weapons/K_SM_Wep_Shield_03.prefab", paint = (c, p) => IsRole(c, Role.Cloth) ? Is(Role.Veil, 95, 216, 232) : V(c) > 0.5f && !IsRole(c, Role.Gold) ? Cloth(0.85f) : null } },
        };

        internal static Part[] Cape() => new[] { Roman("Cape_01") };

        // ── Recipes: weapons ───────────────────────────────────────────

        static Part Wep(string path, float scale = 1f, Func<Color32, Vector3, (Role, Color32)?> paint = null)
            => new Part { prefab = path, scale = scale, paint = paint, material = path.StartsWith(R) ? RMat : null };

        static (Role, Color32)? StonePaint(Color32 c) => IsRole(c, Role.Steel) ? Is(Role.Matte, (byte)(c.r * 0.62f), (byte)(c.g * 0.6f), (byte)(c.b * 0.56f)) : null;
        static (Role, Color32)? VeilPaint(Color32 c) => IsRole(c, Role.Steel) ? Is(Role.Veil, (byte)(c.r * 0.42f), (byte)(c.g * 0.85f), c.b) : null;
        static (Role, Color32)? ShardPaint(Color32 c) => IsRole(c, Role.Steel) || IsRole(c, Role.Cloth) && c.r > 200 ? Is(Role.Shard, 70, 190, 225) : null;

        internal static readonly string[] TierNames = { "T0", "T1_Stone", "T2_Iron", "T3_Veilstone", "T4_Shard" };

        internal static Part[] Spear(int tier) => tier switch
        {
            0 => new[] { Wep(R + "Weapons/R_SM_Wep_Spear_01.prefab") },
            1 => new[] { Wep(K + "Weapons/K_SM_Wep_Spear_12.prefab", 1.2f, (c, p) => StonePaint(c)) },
            2 => new[] { Wep(J + "Weapons/J_SM_Wep_Spear_01.prefab") },
            3 => new[] { Wep(J + "Weapons/J_SM_Wep_Spear_02.prefab", 0.95f, (c, p) => VeilPaint(c)) },
            _ => new[] { Wep(J + "Weapons/J_SM_Wep_Spear_04.prefab", 1f, (c, p) => ShardPaint(c)) },
        };

        internal static Part[] Sword(int tier) => tier switch
        {
            0 => new[] { Wep(R + "Weapons/R_SM_Wep_Sword_01.prefab") },
            1 => new[] { Wep(K + "Weapons/K_SM_Wep_Sword_09.prefab", 1f, (c, p) => StonePaint(c)) },
            2 => new[] { Wep(J + "Weapons/J_SM_Wep_Sword_01.prefab", 0.85f) },
            3 => new[] { Wep(J + "Weapons/J_SM_Wep_Sword_01.prefab", 0.9f, (c, p) => VeilPaint(c)) },
            _ => new[] { Wep(K + "Weapons/K_SM_Wep_Sword_15.prefab", 0.8f, (c, p) => ShardPaint(c)) },
        };

        /// <summary>Level 1 carries no gold, level 2 brass, level 3 gold that shines.</summary>
        internal static Role GoldOf(int level) => level <= 1 ? Role.Matte : level == 2 ? Role.Brass : Role.Gold;

        /// <summary>Weapons: gold trim from the veilstone tier on.</summary>
        internal static Role GoldOfTier(int tier) => tier >= 3 ? Role.Gold : tier == 2 ? Role.Brass : Role.Matte;

        // ── One run ────────────────────────────────────────────────────

        [MenuItem("Waning Border/Art/Units/Author Garrison Infantry")]
        public static void AuthorAll()
        {
            ResetPalette();
            LegionarySpearmanAuthor.Author();
            SwordsmanSyntyAuthor.Author();
            AuthorArchery();
            AuthorElites();
            AuthorCivilians();
            AuthorCavalry();
            var log = new StringBuilder();
            WritePalette(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[InfantryKit] " + log);
            Directory.CreateDirectory("Temp/infantry");
            File.WriteAllText("Temp/infantry/palette.txt", log.ToString());
        }

        /// <summary>Both prefabs at every level and weapon tier, front and back
        /// (Temp/infantry/states_*.png), drawn in blue.</summary>
        [MenuItem("Waning Border/Art/Units/Render Garrison Infantry")]
        public static void RenderAll()
        {
            Directory.CreateDirectory("Temp/infantry");
            RenderRow("spearman", "Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Spearman/Spearman.prefab",
                new[] { (false, 1, 0), (false, 1, 1), (true, 1, 0), (true, 2, 1), (true, 2, 2), (true, 3, 3), (true, 3, 4) });
            RenderRow("swordsman", "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Units/Swordsman/Swordsman.prefab",
                new[] { (true, 1, 0), (true, 1, 1), (true, 2, 2), (true, 3, 3), (true, 3, 4) });
        }

        static void RenderRow(string tag, string prefab, (bool aged, int level, int weapon)[] states)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            var clipNames = new[] { "idle", "move", "attack", "death" };
            var stage = new GameObject("InfantryStage");
            try
            {
                for (int c = 0; c < LegionarySpearmanAuthor.Clips.Length; c++)
                {
                    for (int i = stage.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(stage.transform.GetChild(i).gameObject);
                    var clip = LegionarySpearmanAuthor.Clip((tag == "swordsman" ? SwordsmanSyntyAuthor.Clips : LegionarySpearmanAuthor.Clips)[c]);
                    for (int i = 0; i < states.Length; i++)
                    {
                        var rig = Object.Instantiate(asset, stage.transform);
                        rig.transform.localScale = Vector3.one;
                        var kit = rig.GetComponent<TheWaningBorder.Rendering.UnitAgeKit>();
                        kit.Apply(states[i].aged, states[i].level, states[i].weapon, 0, instant: true);
                        rig.GetComponent<TheWaningBorder.Rendering.UnitPlayerColor>().Apply(new Color(0.12f, 0.35f, 0.85f));
                        var shown = Rig(kit);
                        // Each figure a step further through the clip.
                        float t = c == 0 ? 0f : clip.length * i / (states.Length - 1f) * 0.98f;
                        clip.SampleAnimation(shown, t);
                        shown.transform.localPosition = Vector3.zero;
                        shown.transform.localRotation = Quaternion.identity;
                        UnitGearIcons.Bake(rig);
                        rig.transform.position = new Vector3(-i * 1.9f, 0f, 0f);
                    }
                    int n = states.Length;
                    var b = new Bounds(new Vector3(-(n - 1) * 0.95f, 1.15f, 0f), new Vector3(n * 1.9f, 2.9f, 0.2f));
                    string png = $"Temp/infantry/{tag}_{clipNames[c]}";
                    LegionaryShot.Shoot(b, 205f, 10f, 64, 64, "Temp/kit_warm.png", true);
                    LegionaryShot.Shoot(b, 205f, 10f, n * 400, 640, png + ".png", true);
                    if (c == 0) LegionaryShot.Shoot(b, 25f, 10f, n * 400, 640, png + "_back.png", true);
                }
            }
            finally { Object.DestroyImmediate(stage); }
        }
    }
}
