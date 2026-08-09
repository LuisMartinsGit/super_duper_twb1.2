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

        // Research-count snapshot per building, so the tech-visual sync only
        // walks the prefab hierarchy when the faction actually finished a
        // research since the last scan (SyncTechVisuals does per-tech
        // GetComponentsInChildren searches — fine on a change, waste on a
        // 0.5 s treadmill across every hut).
        private readonly Dictionary<Entity, int> _lastTechCount = new();

        // What GameObject we last registered with EntityViewManager for
        // each entity. If on the next scan EntityViewManager's view is a
        // DIFFERENT GameObject, our swap got clobbered (e.g.
        // PresentationSpawnSystem.RefreshFactionVisuals respawned
        // procedurally during age-up completion) and we need to re-swap.
        private readonly Dictionary<Entity, GameObject> _registeredView = new();

        // Throttle the scan — upgrades take 20-45s, no need to poll every
        // frame. 0.5s feels instant after the upgrade completes.
        private const float ScanInterval = 0.5f;
        private float _scanTimer;

        private Unity.Entities.World _world;
        private EntityManager _em;

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] UpgradeScanQueryTypes = {
            ComponentType.ReadOnly<BuildingUpgradeState>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>() };
        private TheWaningBorder.Core.CachedEntityQuery _upgradeScanQuery;

        // The Temple of Ridan tracks its level in TempleLevel (set by
        // TempleUpgradeSystem), NOT BuildingUpgradeState — without this
        // second scan its leveled visuals (TempleOfRidan_al_1..4) never
        // load. Disjoint from the query above: temples carry no
        // BuildingUpgradeState.
        private static readonly ComponentType[] TempleScanQueryTypes = {
            ComponentType.ReadOnly<TempleLevel>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>() };
        private TheWaningBorder.Core.CachedEntityQuery _templeScanQuery;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;
            TWBLog.Log("[BuildingPrefabSwap] system attached + ready (scan every "
                + ScanInterval + "s)");
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// External pre-registration: PresentationSpawnSystem can spawn an
        /// L1 prefab directly when the faction has already aged up, then
        /// call this so the swap system's caches stay in sync — otherwise
        /// the next scan would detect "level 0 cached, level 1 expected"
        /// and re-instantiate the same prefab. Idempotent.
        /// </summary>
        public void RegisterPreSwapped(Entity entity, GameObject view, byte level)
        {
            if (entity == Entity.Null || view == null) return;
            _registeredView[entity] = view;
            _lastLevel[entity] = level;
        }

        /// <summary>
        /// Look up the level-1 prefab path for a building so external
        /// callers (PresentationSpawnSystem) can use the same lookup
        /// ladder as the swap system. Returns null if no prefab found.
        /// </summary>
        public GameObject TryLoadLevel1Prefab(string buildingId, byte culture, int variant, out string resolvedPath)
        {
            resolvedPath = null;
            string code = TheWaningBorder.Core.Settings.BuildingUpgradeConfig.CultureCode(culture);
            if (string.IsNullOrEmpty(code)) return null;
            return ResolvePrefab(buildingId, code, level: 1, variant: variant, out resolvedPath);
        }

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
            var query = _upgradeScanQuery.Get(_em, UpgradeScanQueryTypes);
            using (var ents = query.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                    ProcessSwapCandidate(ents[i],
                        _em.GetComponentData<BuildingUpgradeState>(ents[i]).Level);
            }

            // Temples: level lives in TempleLevel (1..4), same swap flow.
            var templeQuery = _templeScanQuery.Get(_em, TempleScanQueryTypes);
            using (var temples = templeQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < temples.Length; i++)
                {
                    int tl = _em.GetComponentData<TempleLevel>(temples[i]).Level;
                    ProcessSwapCandidate(temples[i], (byte)Mathf.Clamp(tl, 1, 4));
                }
            }

            // Drop entries for despawned buildings so the dict doesn't grow.
            // Cheap walk — only the buildings we've ever seen are in here.
            if (_lastLevel.Count > 64) PruneDestroyed();
        }

        private void ProcessSwapCandidate(Entity e, byte level)
        {
            {
                // Detect external clobber: if EntityViewManager's view differs
                // from the one we registered, somebody else replaced our swap
                // (e.g. AgeUpSystem.RefreshFactionVisuals at age-up complete).
                // Clear our caches for this entity so the swap fires again.
                if (_registeredView.TryGetValue(e, out var lastReg))
                {
                    var current = EntityViewManager.Instance != null
                        ? EntityViewManager.Instance.GetView(e) : null;
                    if (current != lastReg)
                    {
                        TWBLog.Log($"[BuildingPrefabSwap] view clobbered for entity {e.Index} — re-swapping");
                        _lastLevel.Remove(e);
                        _registeredView.Remove(e);
                    }
                }

                // Tech visuals on multi-variant prefabs: reveal newly
                // researched upgrade elements with the dissolve. Gated on the
                // faction's completed-research count so the hierarchy search
                // only runs when something actually finished.
                var techView = EntityViewManager.Instance != null
                    ? EntityViewManager.Instance.GetView(e) : null;
                if (techView != null)
                {
                    var techVariant = techView.GetComponent<BuildingVariantVisual>();
                    if (techVariant != null)
                    {
                        var techFac = _em.GetComponentData<FactionTag>(e).Value;
                        var research = TheWaningBorder.Economy.FactionResearchState.Instance;
                        int done = research != null ? research.GetCompletedCount(techFac) : 0;
                        if (!_lastTechCount.TryGetValue(e, out int seen) || seen != done)
                        {
                            techVariant.SyncTechVisuals(techFac, FactionColors.Get(techFac),
                                withTransition: true);
                            _lastTechCount[e] = done;
                        }
                    }
                }

                if (_lastLevel.TryGetValue(e, out byte cached) && cached == level) return;
                _lastLevel[e] = level;

                if (level == 0) return; // base visual already in place

                TrySwap(e, level);
            }
        }

        private void TrySwap(Entity e, byte level)
        {
            // Multi-variant authored prefab: every culture/level model lives
            // INSIDE the one visual (BuildingVariantVisual) — switch branches
            // in place instead of loading a replacement prefab. Applies to
            // any building type, not just the Hall/Barracks/Hut ladder.
            var inPlaceView = EntityViewManager.Instance != null
                ? EntityViewManager.Instance.GetView(e) : null;
            if (inPlaceView != null)
            {
                var variantVisual = inPlaceView.GetComponent<BuildingVariantVisual>();
                if (variantVisual != null)
                {
                    byte vCulture = ReadCulture(e);
                    Color vAccent = _em.HasComponent<FactionTag>(e)
                        ? FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value)
                        : new Color(1f, 0.85f, 0.45f);
                    // Same dissolve wave + flourish pairing as the legacy
                    // prefab-replacement path below.
                    if (vCulture != Cultures.None
                        && variantVisual.ShowVariantWithTransition(vCulture, level, vAccent))
                    {
                        // The new branch reveals with its earned tech
                        // visuals already in place (no separate waves).
                        var swapFac = _em.GetComponentData<FactionTag>(e).Value;
                        variantVisual.SyncTechVisuals(swapFac, vAccent, withTransition: false);
                        BuildingLevelUpEffect.Spawn(inPlaceView, vAccent);
                        TWBLog.Log($"[BuildingPrefabSwap] in-place variant swap entity {e.Index} -> L{level}");
                    }
                    _registeredView[e] = inPlaceView;
                    return;
                }
            }

            string buildingId = ResolveBuildingId(e);
            if (string.IsNullOrEmpty(buildingId))
            {
                TWBLog.Log($"[BuildingPrefabSwap] skip entity {e.Index}: not Hall/Barracks/Hut");
                return;
            }

            byte culture = ReadCulture(e);
            if (culture == Cultures.None)
            {
                TWBLog.Log($"[BuildingPrefabSwap] skip {buildingId} entity {e.Index}: faction has no culture");
                return;
            }
            string code = BuildingUpgradeConfig.CultureCode(culture);
            if (string.IsNullOrEmpty(code)) return;

            int variant = (buildingId == "Hut") ? 1 + (Mathf.Abs(e.Index) % 2) : 0;

            var prefab = ResolvePrefab(buildingId, code, level, variant, out string resolvedPath);
            if (prefab == null)
            {
                TWBLog.Log($"[BuildingPrefabSwap] no prefab found for {buildingId}_{code}_{level}" +
                          (variant > 0 ? $"_{variant}" : "") + " — keeping procedural visual");
                return;
            }

            // Use entity transform as the source of truth for position/rotation.
            // The prefab's authored scale is preserved via ProceduralScaleTag so
            // PresentationSpawnSystem.SyncTransforms doesn't clobber it on the
            // next frame — same pattern used for procedural unit visuals.
            var t = _em.GetComponentData<LocalTransform>(e);
            Vector3 pos = t.Position;
            pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);

            // Apply the +180° Y building visual offset on top of the
            // entity's rotation — same convention as PresentationSpawnSystem.
            // ECS LocalTransform.Rotation stays clean; SyncTransforms keeps
            // re-applying this offset every frame for the swapped GameObject.
            Quaternion entityRot = t.Rotation;
            Quaternion visualRot = entityRot * Quaternion.Euler(0f, 180f, 0f);

            var newGo = Instantiate(prefab, pos, visualRot);
            newGo.SetActive(true);
            newGo.name = $"Entity_{e.Index}_{buildingId}_L{level}";

            // Preserve the prefab's authored scale across SyncTransforms ticks.
            var ps = prefab.transform.localScale;
            float baseScale = (ps.x + ps.y + ps.z) / 3f;
            if (baseScale > 0.001f)
            {
                var existing = newGo.GetComponent<ProceduralScaleTag>();
                if (existing == null)
                    existing = newGo.AddComponent<ProceduralScaleTag>();
                existing.BaseScale = baseScale;
            }
            // Final scale = entity Scale × prefab BaseScale. Most entity Scale
            // is 1.0 once construction completes; SyncTransforms reapplies
            // every frame from LocalTransform.
            newGo.transform.localScale = Vector3.one * t.Scale * baseScale;

            // Authored prefabs paint team-color regions with a flat marker
            // (default pure blue). Replace with the faction color so each
            // player's L1/L2/L3 buildings carry their lobby color. Applies the
            // roof/stripe rule too, now that it lives in the marker — before,
            // this path ran only the atlas swap and an upgraded building kept
            // whatever colour its roof was authored with.
            if (_em.HasComponent<FactionTag>(e))
            {
                var fac = _em.GetComponentData<FactionTag>(e).Value;
                BuildingFactionColorMarker.Apply(newGo, FactionColors.Get(fac));
            }

            // Selection wiring. This path never did either of these, so once the
            // dissolve destroyed the old visual (~1.5 s later) the upgraded
            // building had no click target and no entity link at all — it could
            // not be selected by clicking again, only by drag-select. Both are
            // required for SelectionSystem's raycast → EntityReference walk.
            PresentationSpawnSystem.FitSelectionCollider(newGo, e, _em);
            var swapRef = newGo.GetComponent<TheWaningBorder.Input.EntityReference>()
                       ?? newGo.AddComponent<TheWaningBorder.Input.EntityReference>();
            swapRef.Entity = e;

            var current = EntityViewManager.Instance != null
                ? EntityViewManager.Instance.GetView(e) : null;

            // Register the new view BEFORE the dissolve transition takes over
            // so other systems reading EntityViewManager always see a valid
            // GameObject during the brief overlap.
            EntityViewManager.Instance?.RegisterView(e, newGo);
            _registeredView[e] = newGo;

            Color accent = _em.HasComponent<FactionTag>(e)
                ? FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value)
                : new Color(1f, 0.85f, 0.45f);

            // Wave-driven dissolve: the old visual is eaten away from the
            // base up while the new visual reveals along the same front.
            // The transition driver destroys `current` when it completes.
            BuildingDissolveTransition.Begin(current, newGo, duration: 1.5f, edgeColor: accent);

            // Level-up flourish: a quick gold pulse / spark burst at the start
            // of the dissolve gives the wave a clear "trigger" cue.
            BuildingLevelUpEffect.Spawn(newGo, accent);

            TWBLog.Log($"[BuildingPrefabSwap] swapped {buildingId} entity {e.Index} → L{level} ({resolvedPath})");
        }

        private GameObject ResolvePrefab(string buildingId, string cultureCode, byte level, int variant,
            out string resolvedPath)
        {
            resolvedPath = null;
            var paths = BuildCandidatePaths(buildingId, cultureCode, level, variant);
            for (int i = 0; i < paths.Count; i++)
            {
                var p = paths[i];
                if (_prefabCache.TryGetValue(p, out var cached))
                {
                    if (cached != null) { resolvedPath = p; return cached; }
                    continue;
                }
                var loaded = Resources.Load<GameObject>(p);
                _prefabCache[p] = loaded; // cache hit OR negative-cache
                if (loaded != null) { resolvedPath = p; return loaded; }
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
            if (_em.HasComponent<TempleOfRidanTag>(e)) return "TempleOfRidan";
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
            for (int i = 0; i < toRemove.Count; i++)
            {
                _lastLevel.Remove(toRemove[i]);
                _registeredView.Remove(toRemove[i]);
                _lastTechCount.Remove(toRemove[i]);
            }
        }
    }
}
