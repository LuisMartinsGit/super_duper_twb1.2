// BuildingPrefabSwapSystem.cs
// Swaps a building's visual GameObject when its BuildingUpgradeState.Level
// changes. Lookup convention (matches the user's prefab naming in
// Assets/Resources/Prefabs/Buildings/):
//
//   Prefabs/Buildings/{Base}_{cultureCode}_{level}            e.g. Hall_al_2
//   Prefabs/Buildings/{base}_{cultureCode}_{level}_{variant}  e.g. house_al_2_1
//
// The lookup order tries the canonical naming first, then a few fallback
// variants. If nothing is found, the existing procedural visual is left
// in place (per spec — "if the prefab is not present, use the current
// sprite as a fallback").
//
// House variant {1, 2} is picked deterministically from the entity index
// so a given house always shows the same variant across a session.
//
// Polls level vs cached level each Update; structural ECS changes are
// not required because we only swap GameObjects, not entity components.
//
// Location: Assets/Scripts/Presentation/BuildingPrefabSwapSystem.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Presentation
{
    public class BuildingPrefabSwapSystem : MonoBehaviour
    {
        public static BuildingPrefabSwapSystem Instance { get; private set; }

        // Last-known level per building. Drives the diff: if current !=
        // cached, attempt a swap and update cache. Negative cache hit
        // (no prefab found) writes the level too so we don't retry on
        // every frame.
        private readonly Dictionary<Entity, byte> _lastLevel = new();

        // Resolved prefabs cached by path so Resources.Load runs at most
        // once per (base, culture, level, variant) triple per session.
        // null means "definitely-missing" — checked positively too.
        private readonly Dictionary<string, GameObject> _prefabCache = new();

        // Throttle the scan — upgrades take 20-45s, no need to poll every
        // frame. 0.5s feels instant after the upgrade completes.
        private const float ScanInterval = 0.5f;
        private float _scanTimer;

        private Unity.Entities.World _world;
        private EntityManager _em;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (_world == null || !_world.IsCreated) return;
            _scanTimer += Time.deltaTime;
            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;

            ScanAndSwap();
        }

        private void ScanAndSwap()
        {
            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingUpgradeState>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                byte level = _em.GetComponentData<BuildingUpgradeState>(e).Level;

                if (_lastLevel.TryGetValue(e, out byte cached) && cached == level) continue;
                _lastLevel[e] = level;

                if (level == 0) continue; // base visual already in place

                TrySwap(e, level);
            }

            // Drop entries for despawned buildings so the dict doesn't grow.
            // Cheap walk — only the buildings we've ever seen are in here.
            if (_lastLevel.Count > 64) PruneDestroyed();
        }

        private void TrySwap(Entity e, byte level)
        {
            string buildingId = ResolveBuildingId(e);
            if (string.IsNullOrEmpty(buildingId)) return;

            byte culture = ReadCulture(e);
            if (culture == Cultures.None) return;
            string code = BuildingUpgradeConfig.CultureCode(culture);
            if (string.IsNullOrEmpty(code)) return;

            // Houses (Hut) get a deterministic variant per entity so the same
            // house always shows the same prefab between frames. 2 variants.
            int variant = (buildingId == "Hut") ? 1 + (Mathf.Abs(e.Index) % 2) : 0;

            var prefab = ResolvePrefab(buildingId, code, level, variant);
            if (prefab == null) return; // procedural fallback — leave visual alone

            var current = EntityViewManager.Instance != null
                ? EntityViewManager.Instance.GetView(e) : null;

            // Compute spawn position from current view if we have one (preserves
            // the snap-to-grid Y), otherwise from the LocalTransform.
            Vector3 pos;
            Quaternion rot;
            Vector3 scale;
            if (current != null)
            {
                pos = current.transform.position;
                rot = current.transform.rotation;
                scale = current.transform.localScale;
            }
            else
            {
                var t = _em.GetComponentData<LocalTransform>(e);
                pos = t.Position;
                rot = t.Rotation;
                scale = Vector3.one * t.Scale;
            }

            var newGo = Instantiate(prefab, pos, rot);
            newGo.transform.localScale = scale;
            newGo.name = $"Entity_{e.Index}_Upgraded_L{level}";

            // Replace registration. Destroy the old visual on the next frame
            // so any in-flight presentation systems finish their tick safely.
            if (current != null) Destroy(current);
            EntityViewManager.Instance?.RegisterView(e, newGo);
        }

        private GameObject ResolvePrefab(string buildingId, string cultureCode, byte level, int variant)
        {
            // Build candidate paths in priority order. First Resources.Load
            // hit wins. Empty / null on full miss → procedural fallback.
            var paths = BuildCandidatePaths(buildingId, cultureCode, level, variant);
            for (int i = 0; i < paths.Count; i++)
            {
                var p = paths[i];
                if (_prefabCache.TryGetValue(p, out var cached)) { if (cached != null) return cached; continue; }
                var loaded = Resources.Load<GameObject>(p);
                _prefabCache[p] = loaded; // cache hit OR negative-cache
                if (loaded != null) return loaded;
            }
            return null;
        }

        private static List<string> BuildCandidatePaths(string buildingId, string code, byte level, int variant)
        {
            var list = new List<string>(6);
            string root = "Prefabs/Buildings/";

            switch (buildingId)
            {
                case "Hall":
                    list.Add($"{root}Hall_{code}_{level}");                 // Hall_al_2
                    break;
                case "Barracks":
                    list.Add($"{root}Barracks_{code}_{level}");
                    list.Add($"{root}{CultureFolder(code)}/Barracks_{code}_{level}");
                    list.Add($"{root}{CultureFolder(code)}/BARACKS_{CultureFull(code)}"); // user's existing typo'd asset
                    break;
                case "Hut":
                    if (variant > 0)
                        list.Add($"{root}house_{code}_{level}_{variant}");  // house_al_2_1
                    list.Add($"{root}house_{code}_{level}");                 // single-variant fallback
                    list.Add($"{root}{CultureFolder(code)}/House");          // existing per-culture House.prefab
                    break;
            }
            return list;
        }

        private static string CultureFolder(string code) => code switch
        {
            "al" => "Alanthor",
            "ru" => "Runai",
            "fe" => "Feraldis",
            _    => string.Empty,
        };

        private static string CultureFull(string code) => code switch
        {
            "al" => "alanthor",
            "ru" => "runai",
            "fe" => "feraldis",
            _    => string.Empty,
        };

        private string ResolveBuildingId(Entity e)
        {
            if (_em.HasComponent<HallTag>(e))     return "Hall";
            if (_em.HasComponent<BarracksTag>(e)) return "Barracks";
            if (_em.HasComponent<HutTag>(e))      return "Hut";
            return string.Empty;
        }

        private byte ReadCulture(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e)) return Cultures.None;
            var faction = _em.GetComponentData<FactionTag>(e).Value;
            return FactionColors.GetFactionCulture(faction);
        }

        private void PruneDestroyed()
        {
            var toRemove = new List<Entity>(8);
            foreach (var kvp in _lastLevel)
                if (!_em.Exists(kvp.Key)) toRemove.Add(kvp.Key);
            for (int i = 0; i < toRemove.Count; i++) _lastLevel.Remove(toRemove[i]);
        }
    }
}
