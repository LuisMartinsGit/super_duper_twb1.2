// SpellShowcaseDriver.cs
// Drives the "Spell Showcase": lays every spell prefab in a grid on a flat
// textureless plane, instantiates it, and repeatedly re-casts its VFX, with a
// floating label so you can tell which spell is which.
//
// Spells are PREFABS (a Spell component per prefab). The driver loads every
// Spell prefab under Resources/Spells (or you can hand-assign a list). Because
// each spell is a real prefab you can also just drop it into any scene and edit
// it in the Inspector.
//
// Self-building: on Start it creates the ground plane / camera / light if the
// scene doesn't already have them. Purely a dev/authoring tool.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Abilities.Vfx
{
    [AddComponentMenu("Waning Border/Spell Showcase Driver")]
    public sealed class SpellShowcaseDriver : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Spell prefabs to display. Leave empty to auto-load every Spell under Resources/Spells.")]
        public List<Spell> spellPrefabs = new List<Spell>();
        [Tooltip("Resources sub-folder scanned when the list above is empty.")]
        public string spellsResourceFolder = "Spells";

        [Header("Layout")]
        [Tooltip("World-space spacing between spell cells.")]
        public float cellSpacing = 16f;
        [Tooltip("Seconds between re-casts of the whole set.")]
        public float recastInterval = 4f;

        [Header("Scene build")]
        public bool buildGround = true;
        public bool buildCamera = true;
        public bool buildLight = true;
        [Tooltip("Disable the loaded map's Unity Terrain + water so only the flat plane shows.")]
        public bool hideSceneTerrain = false;
        [Tooltip("World-space center the flat plane + spell grid are built around.")]
        public Vector3 center = Vector3.zero;
        [Tooltip("Flat plane colour (textureless).")]
        public Color groundColor = new Color(0.32f, 0.33f, 0.35f);

        private readonly List<Spell> _prefabs = new List<Spell>();
        private readonly List<Spell> _instances = new List<Spell>();
        private readonly List<Vector3> _cells = new List<Vector3>();
        private float _timer;
        private Camera _cam;
        private GUIStyle _labelStyle;

        private void Start()
        {
            LoadPrefabs();
            if (_prefabs.Count == 0)
                Debug.LogWarning($"[SpellShowcase] No spell prefabs found under Resources/{spellsResourceFolder}.");

            if (hideSceneTerrain) HideSceneTerrain();
            LayOutGrid();
            if (buildGround) BuildGround();
            if (buildLight) EnsureLight();
            if (buildCamera) EnsureCamera();
            else _cam = Camera.main;

            InstantiateSpells();

            // Cast once immediately so there's something on screen at t=0.
            CastAll();
            _timer = 0f;
        }

        private void LoadPrefabs()
        {
            _prefabs.Clear();
            foreach (var s in spellPrefabs)
                if (s != null) _prefabs.Add(s);

            if (_prefabs.Count == 0)
            {
                // Resources.LoadAll<GameObject> keeps prefab roots; filter to Spells.
                foreach (var go in Resources.LoadAll<GameObject>(spellsResourceFolder))
                {
                    if (go == null) continue;
                    var spell = go.GetComponent<Spell>();
                    if (spell != null) _prefabs.Add(spell);
                }
            }
            // Stable, grouped order (Sect / Guild / Hero read together).
            _prefabs.Sort((a, b) => string.CompareOrdinal(a.spellId, b.spellId));
        }

        private void InstantiateSpells()
        {
            _instances.Clear();
            for (int i = 0; i < _prefabs.Count; i++)
            {
                var inst = Instantiate(_prefabs[i], _cells[i], Quaternion.identity, transform);
                inst.name = _prefabs[i].spellId;
                _instances.Add(inst);
            }
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= recastInterval)
            {
                _timer = 0f;
                CastAll();
            }
        }

        private void CastAll()
        {
            for (int i = 0; i < _instances.Count; i++)
                if (_instances[i] != null) _instances[i].PlayVfx();
        }

        private void LayOutGrid()
        {
            _cells.Clear();
            int n = _prefabs.Count;
            if (n == 0) return;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt(n / (float)cols);
            // Centre the grid on `center`.
            float ox = -(cols - 1) * cellSpacing * 0.5f;
            float oz = (rows - 1) * cellSpacing * 0.5f;
            for (int i = 0; i < n; i++)
            {
                int c = i % cols;
                int r = i / cols;
                _cells.Add(center + new Vector3(ox + c * cellSpacing, 0f, oz - r * cellSpacing));
            }
        }

        /// <summary>Disable the loaded map's terrain + water so the flat plane is
        /// the only ground — used when this runs as the SpellShowcase scenario on
        /// top of a normal map scene.</summary>
        private void HideSceneTerrain()
        {
            // Stop MapMagic FIRST: it streams/welds terrain LOD tiles every frame,
            // and if we hide a tile out from under it, its Update NREs
            // (Weld.WeldDraftToMain -> Terrain.terrainData). Disabling its driver
            // components halts generation so hiding the terrain is safe.
            DisableMapMagic();

            // Disable the Terrain COMPONENT (not the GameObject) so it stops
            // rendering while its terrainData stays valid for any lookups.
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (t == null) continue;
                t.drawHeightmap = false;
                t.drawTreesAndFoliage = false;
                t.enabled = false;
            }

            // Water planes are plain renderers named "*water*".
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (mr == null) continue;
                string n = mr.gameObject.name.ToLowerInvariant();
                if (n.Contains("water") || n.Contains("ocean") || n.Contains("sea"))
                    mr.enabled = false;
            }
        }

        /// <summary>Halt MapMagic's terrain-generation components (referenced by
        /// reflection — the MapMagic assembly isn't a dependency of this one). Its
        /// per-frame Update drives LOD streaming/welding that crashes once the
        /// terrain is hidden, so we switch it off before touching the terrain.</summary>
        private static void DisableMapMagic()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                var full = mb.GetType().FullName;
                if (full != null && full.StartsWith("MapMagic"))
                    mb.enabled = false;
            }
        }

        private void BuildGround()
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "ShowcaseGround";
            plane.transform.SetParent(transform, false);
            // Plane primitive is 10x10 units at scale 1 — size it to comfortably
            // hold the grid plus margin.
            int cells = Mathf.Max(1, _prefabs.Count);
            float span = Mathf.CeilToInt(Mathf.Sqrt(cells)) * cellSpacing + cellSpacing;
            plane.transform.position = center;
            plane.transform.localScale = Vector3.one * (span / 10f);

            var mr = plane.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MakeFlatMaterial(groundColor);
        }

        private static Material MakeFlatMaterial(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            var mat = new Material(shader);
            // URP/Lit uses _BaseColor; Standard uses _Color. Set whichever exists.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            return mat;
        }

        private void EnsureLight()
        {
            if (Object.FindFirstObjectByType<Light>() != null) return;
            var go = new GameObject("ShowcaseLight");
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = Color.white;
            l.intensity = 1.1f;
            go.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }

        private void EnsureCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("ShowcaseCamera");
                go.tag = "MainCamera";
                go.transform.SetParent(transform, false);
                _cam = go.AddComponent<Camera>();
            }
            // Frame the whole grid from a comfortable RTS-ish angle.
            int cells = Mathf.Max(1, _prefabs.Count);
            float span = Mathf.CeilToInt(Mathf.Sqrt(cells)) * cellSpacing;
            float dist = span * 1.1f + 20f;
            _cam.transform.position = center + new Vector3(0f, dist * 0.85f, -dist * 0.7f);
            _cam.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            _cam.farClipPlane = Mathf.Max(_cam.farClipPlane, dist * 3f);
        }

        private void OnGUI()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 13,
                };
                _labelStyle.normal.textColor = Color.white;
            }

            for (int i = 0; i < _instances.Count && i < _cells.Count; i++)
            {
                var s = _instances[i];
                if (s == null) continue;

                Vector3 sp = _cam.WorldToScreenPoint(_cells[i] + Vector3.up * 0.5f);
                if (sp.z <= 0f) continue; // behind camera
                float y = Screen.height - sp.y; // GUI space is top-left origin

                string text = string.IsNullOrEmpty(s.displayName) ? s.spellId : s.displayName;
                var rect = new Rect(sp.x - 90f, y - 46f, 180f, 20f);
                // Cheap drop-shadow for legibility over bright particles.
                var shadow = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
                var prev = _labelStyle.normal.textColor;
                _labelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(shadow, text, _labelStyle);
                _labelStyle.normal.textColor = prev;
                GUI.Label(rect, text, _labelStyle);
            }
        }
    }
}
