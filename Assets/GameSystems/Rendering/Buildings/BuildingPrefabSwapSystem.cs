// BuildingPrefabSwapSystem.cs
// KEEPS EVERY PLAYER BUILDING'S VIEW ON ITS SO'S ANSWER (2026-10-20,
// docs/Design/Art_Direction.md § 13 — "presentation layer should ONLY accept
// the prefabs through the SO, no exception").
//
// Every ScanInterval it asks, for each finished player building, which model
// its SOs say it shows now (PresentationSpawnSystem.TryResolveBuildingVisual:
// the culture's level SO at level N, the base SO's culture look for a
// building without a ladder, the base SO otherwise) and compares it with the
// prefab its view was built from (BuildingVisualSource). When they differ —
// a level-up, the age-up, a construction site finishing, a Veilstone Mine
// converted into a Trading Outpost — the view is replaced by the new model,
// fitted to the footprint, with the dissolve wave and the level-up flourish.
//
// Replaced, 2026-10-20: the per-level cache, the in-place multi-variant
// branch switch (BuildingVariantVisual), the Resources naming ladder
// (Hall_al_N / house_al_N / Barracks_al_N) and the tech-visual sync.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Rendering
{
    public class BuildingPrefabSwapSystem : MonoBehaviour
    {
        public static BuildingPrefabSwapSystem Instance { get; private set; }

        /// <summary>Upgrades take 20-45 s; half a second reads as instant.</summary>
        private const float ScanInterval = 0.5f;
        private float _scanTimer;

        private Unity.Entities.World _world;
        private EntityManager _em;
        /// <summary>Entities whose next replacement plays without the flourish
        /// (the construction-complete hook already played one).</summary>
        private readonly HashSet<Entity> _quiet = new HashSet<Entity>();

        private static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private CachedEntityQuery _buildings;

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

        /// <summary>Re-resolve every building on the next frame (the age-up).</summary>
        public void RescanNow() => _scanTimer = ScanInterval;

        /// <summary>Re-resolve on the next frame; <paramref name="flourish"/>
        /// false = replace <paramref name="entity"/> without the gold burst.</summary>
        public void RescanNow(Entity entity, bool flourish)
        {
            if (!flourish) _quiet.Add(entity);
            _scanTimer = ScanInterval;
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated) return;
            _scanTimer += Time.deltaTime;
            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;
            Scan();
        }

        private void Scan()
        {
            if (EntityViewManager.Instance == null) return;
            var q = _buildings.Get(_em, QT_Buildings);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                // A site keeps its base form while it rises; a dying building
                // keeps the view its collapse is playing on.
                if (_em.HasComponent<UnderConstruction>(e) || _em.HasComponent<BuildingCollapseState>(e)) continue;
                if (_em.HasComponent<Health>(e) && _em.GetComponentData<Health>(e).Value <= 0) continue;
                if (!PresentationSpawnSystem.TryResolveBuildingVisual(_em, e, out GameObject want) || want == null) continue;
                var view = EntityViewManager.Instance.GetView(e);
                if (view == null) continue;
                var src = view.GetComponent<PresentationSpawnSystem.BuildingVisualSource>();
                if (src != null && src.Prefab == want) continue;
                ReplaceView(e, view, want, flourish: !_quiet.Remove(e));
            }
            if (_quiet.Count > 64) _quiet.Clear();
        }

        /// <summary>
        /// Replace the view with a new instance of <paramref name="prefab"/>:
        /// footprint-fitted, faction-coloured, selectable, lit, with the
        /// damage shader — what the spawn gives every building — then the
        /// dissolve from the old view.
        /// </summary>
        private void ReplaceView(Entity e, GameObject current, GameObject prefab, bool flourish)
        {
            var t = _em.GetComponentData<LocalTransform>(e);
            Vector3 pos = t.Position;
            pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);
            // The +180° building visual offset, as PresentationSpawnSystem applies it.
            Quaternion visualRot = (Quaternion)t.Rotation * Quaternion.Euler(0f, 180f, 0f);

            var newGo = Instantiate(prefab, pos, visualRot);
            newGo.SetActive(true);
            newGo.name = $"Entity_{e.Index}_{prefab.name}";

            var ps = prefab.transform.localScale;
            float authored = (ps.x + ps.y + ps.z) / 3f;
            float baseScale = authored;
            Vector3 centreOffset = Vector3.zero;
            if (_em.HasComponent<BuildingSize>(e))
            {
                var size = _em.GetComponentData<BuildingSize>(e);
                baseScale *= PresentationSpawnSystem.ComputeFootprintFitForSize(newGo, size.Width, size.Height, out centreOffset);
            }
            if (baseScale > 0.001f)
            {
                var tag = newGo.GetComponent<ProceduralScaleTag>() ?? newGo.AddComponent<ProceduralScaleTag>();
                tag.BaseScale = baseScale;
                tag.AuthoredScale = authored;
                tag.BaseOffset = centreOffset;
            }
            float scale = t.Scale * baseScale;
            newGo.transform.localScale = Vector3.one * scale;
            if (centreOffset != Vector3.zero)
                newGo.transform.position = pos - (visualRot * centreOffset) * scale;

            Color accent = new Color(1f, 0.85f, 0.45f);
            if (_em.HasComponent<FactionTag>(e))
            {
                accent = FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value);
                BuildingFactionColorMarker.Apply(newGo, accent);
            }
            PresentationSpawnSystem.FitSelectionCollider(newGo, e, _em);
            var reference = newGo.GetComponent<EntityReference>() ?? newGo.AddComponent<EntityReference>();
            reference.Entity = e;
            BuildingLanternLight.Attach(newGo, e, _em);
            var dmg = newGo.GetComponent<BuildingDamageVisual>() ?? newGo.AddComponent<BuildingDamageVisual>();
            dmg.Entity = e;
            var source = newGo.GetComponent<PresentationSpawnSystem.BuildingVisualSource>()
                      ?? newGo.AddComponent<PresentationSpawnSystem.BuildingVisualSource>();
            source.Prefab = prefab;

            EntityViewManager.Instance.RegisterView(e, newGo);
            BuildingDissolveTransition.Begin(current, newGo, duration: 1.5f, edgeColor: accent);
            if (flourish) BuildingLevelUpEffect.Spawn(newGo, accent);
            TWBLog.Log($"[BuildingPrefabSwap] entity {e.Index} -> {prefab.name}");
        }
    }
}
