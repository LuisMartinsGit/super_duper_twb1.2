// Arranges and dresses the hand-built Garrison_1..3 and ArcheryRange_1..3
// (2026-11-25): pieces sorted into the numbered construction groups, stripes
// and banners in the player colour, props, point lights, ground markers.
// Nothing the author placed is scaled or re-posed inside the composition,
// except where a step below says so. ONE-SHOT per prefab: a prefab that
// already has its Banners group is skipped, so later hand edits are safe.
// Frame: prefab root at scale 1. Front is +Z.
//   Waning Border > Art > Garrison + Range > Arrange And Dress

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TheWaningBorder.Systems.Buildings;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class GarrisonRangePass
    {
        const string B = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/";
        static readonly StringBuilder Log = new StringBuilder();
        static Dictionary<string, string> _props;

        [MenuItem("Waning Border/Art/Garrison + Range/Arrange And Dress")]
        public static void Run()
        {
            Log.Clear(); _props = null;
            for (int n = 1; n <= 3; n++)
            {
                int k = n;
                One(B + "Garrison/Garrison_" + n + ".prefab", r => Garrison(r, k));
                One(B + "ArcheryRange/ArcheryRange_" + n + ".prefab", r => Range(r, k));
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/garrison-range.txt", Log.ToString());
            Debug.Log("[GarrisonRange]\n" + Log);
        }

        static void One(string path, System.Action<Transform> act)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var t = root.transform;
                foreach (Transform c in t)
                    if (c.name.EndsWith("- Banners")) { Log.AppendLine("SKIP " + path); return; }
                Vector3 scale = t.localScale;
                t.localScale = Vector3.one;
                t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);   // the game ignores an authored root position
                int before = t.GetComponentsInChildren<Renderer>(false).Length;
                Log.AppendLine("== " + path);
                RemoveDuplicates(t);
                act(t);
                GildedTwins(t);
                Stripes(t);
                t.localScale = scale;
                Log.AppendLine($"   renderers {before} -> {t.GetComponentsInChildren<Renderer>(false).Length}");
                foreach (Transform c in t) Log.AppendLine($"   {c.name} ({c.childCount})");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        static IEnumerable<Transform> Kids(Transform t)
        {
            var l = new List<Transform>();
            foreach (Transform c in t) l.Add(c);
            return l;
        }

        static Bounds Box(Transform t)
        {
            bool any = false; Bounds b = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        static Transform Group(Transform root, string name, int order)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root, false);
            return g;
        }

        static Transform Find(Transform root, string starts)
        {
            foreach (Transform c in root) if (c.name.StartsWith(starts)) return c;
            return null;
        }

        static string MatKey(Transform t) => string.Join("|", t.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials).Select(m => m == null ? "-" : m.name));

        /// <summary>Pieces laid exactly on a twin (same source, pose, materials).</summary>
        static void RemoveDuplicates(Transform root)
        {
            var seen = new HashSet<string>();
            var dead = new List<GameObject>();
            foreach (var g in Kids(root).Concat(new[] { root }))
                foreach (var c in Kids(g))
                {
                    if (g != root && c.parent != g) continue;
                    if (c.GetComponentInChildren<Renderer>(true) == null || Groupish(c)) continue;
                    var mf = c.GetComponentInChildren<MeshFilter>(true);
                    if (mf == null || mf.sharedMesh == null) continue;
                    Vector3 p = c.position; Vector3 f = c.forward, u = c.up, s = c.lossyScale;
                    string key = string.Format(CultureInfo.InvariantCulture,
                        "{0} {1:F2} {2:F2} {3:F2} {4:F1} {5:F1} {6:F1} {7:F1} {8:F2} {9}", mf.sharedMesh.name, p.x, p.y, p.z,
                        f.x, f.z, u.x, u.y, s.x, MatKey(c)) + c.gameObject.activeInHierarchy;
                    if (!seen.Add(key.Replace("-0.0", "0.0"))) dead.Add(c.gameObject);
                }
            foreach (var d in dead.Distinct()) Object.DestroyImmediate(d);
            Log.AppendLine($"   exact duplicates removed: {dead.Distinct().Count()}");
        }

        /// <summary>A plain roof piece exactly inside a gilded twin (two materials
        /// in one place flicker): the gilded one stays.</summary>
        static void GildedTwins(Transform root)
        {
            var roofs = root.GetComponentsInChildren<MeshRenderer>(true).Where(x => x.name.Contains("Roof_")).ToList();
            int cut = 0;
            foreach (var g in roofs.Where(x => x.sharedMaterials.Any(m => m != null && m.name.Contains("Gold"))).ToList())
                foreach (var q in roofs.ToList())
                {
                    if (q == null || q == g || q.sharedMaterials.Any(m => m != null && m.name.Contains("Gold"))) continue;
                    if (q.GetComponent<MeshFilter>().sharedMesh != g.GetComponent<MeshFilter>().sharedMesh) continue;
                    if ((q.transform.position - g.transform.position).sqrMagnitude > 1e-4f || Quaternion.Angle(q.transform.rotation, g.transform.rotation) > 1f) continue;
                    roofs.Remove(q); Object.DestroyImmediate(q.gameObject); cut++;
                }
            Log.AppendLine("   plain twins of gilded pieces removed: " + cut);
        }

        static bool Groupish(Transform t) => t.childCount > 0 && t.GetComponent<Renderer>() == null && !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject);

        static GameObject Place(Transform group, string prop, float x, float y, float z, float yaw, float s = 1f, Material player = null)
        {
            if (_props == null)
            {
                _props = new Dictionary<string, string>();
                foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Synty" }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    _props[Path.GetFileNameWithoutExtension(p)] = p;
                }
            }
            if (!_props.TryGetValue(prop, out var path)) { Log.AppendLine("   MISSING " + prop); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), group);
            go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * s;
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            if (player != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterials;
                    for (int i = 0; i < m.Length; i++) m[i] = player;
                    r.sharedMaterials = m;
                }
            return go;
        }

        /// <summary>Like Place, with its lowest point on y (hay bales pivot at the middle).</summary>
        static void Sit(Transform group, string prop, float x, float y, float z, float yaw, float s = 1f)
        {
            var go = Place(group, prop, x, y, z, yaw, s);
            if (go != null) go.transform.position += Vector3.up * (y - Box(go.transform).min.y);
        }

        static void Lamp(Transform group, float x, float y, float z, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(group, false);
            go.transform.position = new Vector3(x, y, z);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.69f, 0.353f);    // BuildingLanternLight.asset's colour
            l.range = range;                            // metres; not scaled by the root
            l.intensity = intensity * 0.35f;
            l.shadows = LightShadows.None;
        }

        static Material BannerMat => AssetDatabase.LoadAssetAtPath<Material>(GarrisonRangeMaterials.BannerMat);

        /// <summary>The painted blue stripes: the stucco with a player-colour mask.</summary>
        static void Stripes(Transform root)
        {
            int slots = 0;
            for (int n = 1; n <= 2; n++)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(GarrisonRangeMaterials.StuccoMat(n));
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterials; bool hit = false;
                    for (int i = 0; i < m.Length; i++)
                        if (m[i] != null && m[i].name == "Stucco_White_Pattern_0" + n) { m[i] = mat; hit = true; slots++; }
                    if (hit) r.sharedMaterials = m;
                }
            }
            Log.AppendLine($"   striped slots: {slots}");
        }

        static void Logic(Transform root, float half, float groundY, float spawnZ, float exitZ, bool reset)
        {
            var g = Find(root, "0 [snap]");
            g.localPosition = Vector3.zero;
            void Mark(string name, float x, float z)
            {
                var t = g.Find(name);
                if (t == null) { t = new GameObject(name).transform; t.SetParent(g, false); }
                t.localPosition = new Vector3(x, groundY, z);
                t.localRotation = Quaternion.identity;
            }
            Mark("N_corner", -half, -half); Mark("W_corner", half, -half);
            Mark("S_corner", half, half); Mark("E_corner", -half, half);
            Mark("NW_corner", 0, -half); Mark("SW_corner", half, 0);
            Mark("SE_corner", 0, half); Mark("NE_corner", -half, 0);
            Mark(BuildingAnchors.FootprintCenterName, 0, 0);
            Mark(BuildingAnchors.UnitSpawnName, 0, spawnZ);
            Mark(BuildingAnchors.ExitPointName, 0, exitZ);
            var f = g.Find("Footprint");
            if (f != null) f.localPosition = new Vector3(0, groundY, 0);
        }

        static void Copy(Transform group, Transform src, Vector3 offset)
        {
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(src.gameObject);
            if (prefab == null) return;
            string p = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(src.gameObject);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p), group);
            go.transform.SetPositionAndRotation(src.position + offset, src.rotation);
            go.transform.localScale = src.localScale;
            var a = src.GetComponentsInChildren<Renderer>(true);
            var b = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < a.Length && i < b.Length; i++) b[i].sharedMaterials = a[i].sharedMaterials;
        }

        /// <summary>A second course under the outer foundation walls, for the downhill side.</summary>
        static void Skirt(Transform foundation)
        {
            var outer = Kids(foundation).Where(c => c.name.Contains("Wall_Large_01")).ToList();
            var all = Box(foundation);
            foreach (var c in outer)
            {
                var b = Box(c);
                bool edge = b.min.x < all.min.x + 0.6f || b.max.x > all.max.x - 0.6f
                         || b.min.z < all.min.z + 0.6f || b.max.z > all.max.z - 0.6f;
                // Only walls running ALONG an edge, not ones that end at it.
                bool along = (b.size.x > b.size.z) == (b.min.z < all.min.z + 0.9f || b.max.z > all.max.z - 0.9f);
                if (edge && along) Copy(foundation, c, Vector3.down * 4.45f);
            }
        }

        static void Order(Transform root, List<Transform> order)
        {
            for (int i = 0; i < order.Count; i++) order[i].SetSiblingIndex(i);
        }

        // ── Garrison ──────────────────────────────────────────────────────

        static void Garrison(Transform root, int n)
        {
            float F = n == 1 ? 0f : n == 2 ? 0.62f : 0.64f;         // court floor
            float ground = n == 1 ? 0f : -1f;                        // L2 / L3: the bottom of the gate steps
            var floor = Find(root, "7 ["); var walls1 = Find(root, "10 ["); var walls2 = Find(root, "11 [");
            var roof2 = Find(root, "13 ["); var decor = Find(root, "14 ["); var props = Find(root, "15 [");
            var third = n == 3 ? Group(root, "14 [disolve] - Building_main_third_floor", 14) : null;
            var roof = Group(root, "15 [disolve] - Building_roof_main", 15);
            var gate = n > 1 ? Group(root, "16 [rise clean] - Gate", 16) : null;
            decor.name = "17 [fade-in] - Decor";
            props.name = "18 [fade-in] - Props";
            var banners = Group(root, "19 [fade-in] - Banners", 19);
            var lights = Group(root, "20 [snap] - Lights", 20);

            // The main building sat in Decor, so it faded in whole at the end.
            foreach (var c in Kids(decor))
            {
                var b = Box(c);
                if (c.name.Contains("Roof_")) c.SetParent(n == 3 && b.max.y < 6.6f ? roof2 : roof, true);
                else if (c.name.Contains("Base_Floor")) c.SetParent(b.min.y > F + 3f ? walls2 : floor, true);
                else if (c.name.Contains("Balcony_06") && b.min.y > F + 2.5f) c.SetParent(walls2, true);
                else c.SetParent(walls1, true);
            }
            foreach (var c in Kids(root))
            {
                if (c.name.Contains("[") ) continue;
                if (c.name.Contains("Torii")) c.SetParent(gate, true);
                else if (c.name.Contains("J_SM_Bld_Stairs")) c.SetParent(Find(root, "2 ["), true);
                else if (c.name.Contains("R_SM_Bld_Pillar_05")) c.SetParent(Find(root, "4 ["), true);
                else if (c.name.Contains("Balcony_Pillar")) c.SetParent(Find(root, "5 ["), true);
                else if (c.name.Contains("Wall_Window") || c.name.Contains("Shop_Front")) c.SetParent(third != null ? third : walls2, true);
                else Log.AppendLine("   left loose: " + c.name);
            }
            if (n == 1) Skirt(Find(root, "1 ["));

            // The two cloth flags give way to the moving Samurai cloth.
            foreach (var c in Kids(props)) if (c.name.Contains("Prop_Flag_01")) Object.DestroyImmediate(c.gameObject);

            float clothY = n == 1 ? 2.45f : F + 2.55f;
            var bm = BannerMat;
            Place(banners, "J_SM_Prop_Banner_06", -5f, clothY, 8.3f, 0, 1f, bm);
            Place(banners, "J_SM_Prop_Banner_06", 5f, clothY, 8.3f, 0, 1f, bm);
            Place(banners, "J_SM_Prop_Banner_04", -1.95f, F, 2.2f, 270, 0.9f, bm);
            Place(banners, "J_SM_Prop_Banner_04", 1.95f, F, 2.2f, 90, 0.9f, bm);
            if (n >= 2)
            {
                Place(banners, "J_SM_Prop_Banner_02", -6.9f, F, -4.5f, 90, 0.9f, bm);
                Place(banners, "J_SM_Prop_Banner_02", 6.9f, F, -3.3f, 270, 0.9f, bm);
            }
            if (n == 3)
            {
                Place(banners, "J_SM_Prop_Banner_08", -2.5f, 8.2f, -0.42f, 0, 0.75f, bm);
                Place(banners, "J_SM_Prop_Banner_08", 2.5f, 8.2f, -0.42f, 0, 0.75f, bm);
            }

            // Decor: what stands still and is not stores.
            Sit(decor, "J_SM_Prop_Lantern_Stone_01", -2.0f, F, 4.1f, 0);
            Sit(decor, "J_SM_Prop_Lantern_Stone_01", 2.0f, F, 4.1f, 0);
            Sit(decor, "J_SM_Prop_Lantern_Post_01", 6.75f, F, -0.6f, 270);
            Sit(decor, "J_SM_Prop_Lantern_Post_01", -5.0f, F, 0.2f, 180);
            Sit(decor, "J_SM_Prop_Water_Basin_01", 6.7f, F, -2.0f, 270);
            if (n >= 2)
            {
                Sit(decor, "J_SM_Prop_Statue_02", -1.9f, F, 1.0f, 0);
                Sit(decor, "J_SM_Prop_Statue_02", 1.9f, F, 1.0f, 0);
            }
            // Props.
            Sit(props, "J_SM_Prop_StrawBag_Pile_01", -5.3f, F, -2.4f, 90, 0.55f);
            Sit(props, "J_SM_Prop_Ammo_Crate_02", 5.1f, F, 0.8f, 15);
            Sit(props, "J_SM_Prop_Armor_Stand_01", -5.9f, F, 2.2f, 180);
            if (n == 1)
            {
                // The pavilions are still open frames: use them as stores.
                Sit(props, "J_SM_Prop_Drum_Stand_01", 5f, F, 5f, 0);
                Sit(props, "J_SM_Prop_Drum_01", 5f, F + 0.3f, 5f, 0);
                Sit(props, "J_SM_Prop_Rack_01", 6.9f, F, 4.6f, 270);
                Sit(props, "J_SM_Prop_Rack_01_Insert_01", 6.9f, F + 0.13f, 4.6f, 270);
                Sit(props, "J_SM_Prop_Armor_Stand_02", 3.3f, F, 6.9f, 200);
                Sit(props, "K_SM_Prop_Dummy_Armour_01", -4.2f, F, 5.6f, 0);
                Sit(props, "K_SM_Prop_Dummy_Armour_02", -6.1f, F, 5.2f, 15);
                Sit(props, "J_SM_Prop_Sake_Barrel_Stack_01", -5.2f, F, 3.5f, 0);
                Sit(props, "K_SM_Prop_Crate_Wood_01", -3.5f, F, 3.6f, 20, 0.8f);
            }

            float bx = n == 1 ? -1.5f : -1.75f, bz = n == 1 ? 7.5f : 7.25f;
            Lamp(lights, bx, F + 1.6f, bz, 3f, 3f);
            Lamp(lights, 1.6f, F + 1.6f, bz, 3f, 3f);
            Lamp(lights, -2.0f, F + 1.3f, 4.4f, 2f, 1.5f);
            Lamp(lights, 2.0f, F + 1.3f, 4.4f, 2f, 1.5f);
            Lamp(lights, 6.6f, F + 2.0f, -0.6f, 2.5f, 1.8f);
            Lamp(lights, -5.0f, F + 2.0f, -0.3f, 2.5f, 1.8f);
            Lamp(lights, 0f, F + 4f, 1.2f, 3f, 2.2f);               // the hall door

            Logic(root, n == 1 ? 8f : 8.4f, ground, 3.8f, 13.2f, true);
            Number(root);
        }

        // ── Archery Range ─────────────────────────────────────────────────

        static void Range(Transform root, int n)
        {
            float H = n == 1 ? 0f : 1.89f;       // hall floor
            float F = n == 1 ? -1.36f : 0.15f;   // yard floor
            float ground = n == 1 ? -1.5f : -0.2f;

            // Garrison_1's groups came along when the prefab was duplicated:
            // keep Logic and the foundation, drop the hidden Garrison parts.
            var found = Find(root, "1 [");
            found.name = "1 [rise] - Foundation_walls";
            int dropped = 0;
            foreach (var c in Kids(root))
            {
                if (!c.name.Contains("[") || c == found || c.name.StartsWith("0 [")) continue;
                bool visible = c.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy);
                if (visible) { Log.AppendLine("   KEPT " + c.name); continue; }
                dropped += c.GetComponentsInChildren<Renderer>(true).Length;
                Object.DestroyImmediate(c.gameObject);
            }
            Log.AppendLine($"   hidden Garrison leftovers dropped: {dropped}");

            var deck = Group(root, "2 [rise] - Foundation_floors", 2);
            var pillars = Group(root, "3 [rise sequential] - Pillars_building_main", 3);
            var yardPosts = Group(root, "4 [rise sequential] - Courtyard_pillars", 4);
            var stairs = Group(root, "5 [rise clean] - Stairs", 5);
            var walls = Group(root, "6 [disolve] - Building_main_walls", 6);
            var trim = Group(root, "7 [disolve] - Building_main_trim", 7);
            var yardWalls = Group(root, "8 [disolve sequential] - Courtyard_outer_wall", 8);
            var frieze = Group(root, "9 [disolve] - Building_main_frieze", 9);
            var roof = Group(root, "10 [disolve] - Building_roof_lower", 10);
            var roofTop = Group(root, "11 [disolve] - Building_roof_upper", 11);
            var gate = Group(root, "12 [rise clean] - Gate", 12);
            var decor = Group(root, "13 [fade-in] - Decor", 13);
            var props = Group(root, "14 [fade-in] - Props", 14);
            var banners = Group(root, "15 [fade-in] - Banners", 15);
            var lights = Group(root, "16 [snap] - Lights", 16);

            foreach (var c in Kids(root))
            {
                if (c.name.Contains("[")) continue;
                var b = Box(c); string s = c.name;
                if (c.GetComponentInChildren<Renderer>(true) == null) { Object.DestroyImmediate(c.gameObject); continue; }
                Transform to =
                    s.Contains("Torii") ? gate :
                    s.Contains("Castle_Floor") || s.Contains("Base_Floor") || s.Contains("Platform_Wall") ? deck :
                    s.Contains("Bridge_Pillar") ? pillars :
                    s.Contains("Dobei_Pillar") || s.Contains("SM_Bld_Wall_Pillar") ? yardPosts :
                    s.Contains("Stairs") ? stairs :
                    s.Contains("Dobei") ? yardWalls :
                    s.Contains("Roof_Trim") ? roofTop :
                    s.Contains("Roof_") ? (n == 3 ? (b.max.y > 8.3f ? roofTop : roof) : (s.Contains("Roof_Edge") ? roof : roofTop)) :
                    s.Contains("Wall_Block") ? (b.min.y > H + 2.5f ? frieze : yardWalls) :
                    s.Contains("Balcony_06") ? trim :
                    s.Contains("Awning") || s.Contains("Beam") || s.Contains("Bamboo") || s.Contains("Railing") ? decor :
                    walls;
                c.SetParent(to, true);
            }

            if (n == 1)
            {
                // The yard had no floor (terrain showed) and no way out.
                var sample = Kids(deck).First(c => c.name.Contains("Castle_Floor"));
                foreach (float x in new[] { 0f, 5f, 10f })
                    foreach (float z in new[] { 5f, 10f })
                        Copy(deck, sample, new Vector3(x, -1.4f, z) - sample.position);
                var mid = Kids(found).First(c => { var b = Box(c); return c.name.Contains("Wall_Large_01") && b.min.z > 9f && b.min.x > -0.5f && b.max.x < 5.5f; });
                mid.position += Vector3.down * 1.5f;
                var pier = Kids(found).First(c => c.name.Contains("Pillar_01"));
                Copy(found, pier, new Vector3(-0.1f, -6.5f, 10f) - pier.position);
                Copy(found, pier, new Vector3(5.1f, -6.5f, 10f) - pier.position);
            }
            Skirt(found);

            var bm = BannerMat;
            // Cloth on the hall's four front pillars.
            float cy = H + 3.1f;
            foreach (float x in new[] { 0f, 5f }) Place(banners, "J_SM_Prop_Banner_06", x, cy, 0.78f, 0, 0.9f, bm);
            foreach (float x in new[] { -4.95f, 9.95f }) Place(banners, "J_SM_Prop_Banner_06", x, cy, 0.5f, 0, 0.8f, bm);
            if (n < 3)
            {
                Place(banners, "J_SM_Prop_Banner_04", -5f, 0.3f, 10f, 90, 1f, bm);
                Place(banners, "J_SM_Prop_Banner_04", 10f, 0.3f, 10f, 270, 1f, bm);
            }
            else
            {
                Place(banners, "J_SM_Prop_Banner_02", -1.9f, F, 8.5f, 90, 0.9f, bm);
                Place(banners, "J_SM_Prop_Banner_02", 8.2f, F, 8.5f, 270, 0.9f, bm);
            }
            if (n >= 2)
            {
                Place(banners, "J_SM_Prop_Banner_04", -0.4f, F, 7.6f, 270, 0.85f, bm);
                Place(banners, "J_SM_Prop_Banner_04", 5.4f, F, 7.6f, 90, 0.85f, bm);
            }

            // The butts stand under the hall, facing the yard.
            foreach (float x in new[] { -1.2f, 0.6f, 4.6f, 6.4f })
            {
                Sit(props, "J_SM_Prop_Target_01", x, F, 2.3f, 90);
            }
            Sit(props, "K_SM_Prop_Target_01", 8.0f, F, 2.2f, 0, 1.2f);
            Sit(props, "K_SM_Prop_Hay_Bale_01", -0.3f, F, 1.75f, 5);
            Sit(props, "K_SM_Prop_Hay_Bale_01", 5.5f, F, 1.75f, -6);
            Sit(props, "K_SM_Prop_Hay_Bale_01", 7.3f, F, 3.4f, 60, 0.8f);
            Sit(props, "K_SM_Prop_Barrel_Arrows_01", -1.5f, F, 6.6f, 0);
            Sit(props, "K_SM_Prop_Barrel_Arrows_01", 0.2f, F, 6.0f, 40);
            Sit(props, "K_SM_Prop_Barrel_Arrows_01", 5.2f, F, 6.0f, 80);
            Sit(props, "K_SM_Prop_Camp_Weapon_Rack_01", 7.6f, F, 5.4f, 270);
            Sit(props, "K_SM_Prop_Bench_Seat_02", 6.6f, F, 8.6f, 0);
            Sit(props, "J_SM_Prop_Ammo_Crate_02", -1.3f, F, 7.7f, 70);
            Sit(props, "J_SM_Prop_Ammo_Crate_01", 4.3f, F, 6.9f, 20);
            if (n >= 2)
            {
                Sit(props, "J_SM_Prop_Rack_01", 7.9f, F, 7.2f, 270);
                Sit(props, "J_SM_Prop_Rack_01_Insert_01", 7.9f, F + 0.13f, 7.2f, 270);
                Sit(props, "J_SM_Prop_StrawBag_Pile_01", -1.4f, F, 4.4f, 90, 0.7f);
                Sit(props, "K_SM_Prop_Crate_Wood_01", -1.6f, F, 5.5f, 15, 0.8f);
            }
            if (n == 3)
            {
                Sit(props, "J_SM_Prop_Drum_Stand_01", 8.9f, H, 0.75f, 0, 0.8f);
                Sit(props, "J_SM_Prop_Drum_01", 8.9f, H + 0.25f, 0.75f, 0, 0.8f);
                Sit(props, "J_SM_Prop_Armor_Stand_02", -4.0f, H, 0.75f, 0);
                Sit(decor, "J_SM_Prop_Statue_02", -0.1f, F, 8.5f, 0);
                Sit(decor, "J_SM_Prop_Statue_02", 5.1f, F, 8.5f, 0);
            }
            else if (n == 2) Sit(props, "J_SM_Prop_Armor_Stand_01", -4.0f, H, 0.75f, 0);

            // Stone lanterns at the gate: on the L1 piers, beside the torii later.
            float ly = n == 1 ? 0.2f : F, lz = n == 1 ? 10f : 9.2f;
            float lx0 = n == 1 ? -0.1f : 0.2f, lx1 = n == 1 ? 5.1f : 4.8f;
            if (n == 3) { lz = 7.0f; lx0 = 0.3f; lx1 = 4.7f; }
            Sit(decor, "J_SM_Prop_Lantern_Stone_01", lx0, ly, lz, 0);
            Sit(decor, "J_SM_Prop_Lantern_Stone_01", lx1, ly, lz, 0);
            Sit(decor, "K_SM_Prop_Camp_Brazier_01", 0.6f, F, 3.8f, 0, 1.5f);
            Sit(decor, "K_SM_Prop_Camp_Brazier_01", 4.4f, F, 3.8f, 0, 1.5f);
            Sit(decor, "J_SM_Prop_Lantern_Stone_02", 1.0f, H, 0.95f, 0, 0.8f);
            Sit(decor, "J_SM_Prop_Lantern_Stone_02", 4.0f, H, 0.95f, 0, 0.8f);

            Lamp(lights, lx0, ly + 1.2f, lz + 0.2f, 2.2f, 1.6f);
            Lamp(lights, lx1, ly + 1.2f, lz + 0.2f, 2.2f, 1.6f);
            Lamp(lights, 0.6f, F + 1.7f, 4.0f, 3f, 3f);
            Lamp(lights, 4.4f, F + 1.7f, 4.0f, 3f, 3f);
            Lamp(lights, 2.5f, H + 2.2f, 1.5f, 3f, 2.2f);
            Lamp(lights, 8.2f, H + 2.2f, 1.2f, 2.4f, 1.6f);
            Lamp(lights, -3.4f, H + 2.2f, 1.2f, 2.4f, 1.6f);

            // Centre the plot on the root: the walls run x -5..10, z -5..10.
            foreach (Transform c in root)
                if (!c.name.StartsWith("0 [")) c.position += new Vector3(-2.5f, 0f, -2.5f);
            Logic(root, 7.8f, ground, 2.5f, 13.2f, true);

            foreach (var c in Kids(root))
                if (c.childCount == 0) Object.DestroyImmediate(c.gameObject);
            Number(root);
        }

        /// <summary>Groups in build order in the hierarchy.</summary>
        static void Number(Transform root)
        {
            foreach (var c in Kids(root))
                if (c.name.Contains("[") && c.childCount == 0) Object.DestroyImmediate(c.gameObject);
            var list = Kids(root).OrderBy(c => { int i = c.name.IndexOf(' '); return i > 0 && int.TryParse(c.name.Substring(0, i), out int v) ? v : 999; }).ToList();
            Order(root, list);
        }
    }
}
