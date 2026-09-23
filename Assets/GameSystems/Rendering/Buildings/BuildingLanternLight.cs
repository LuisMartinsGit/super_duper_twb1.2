// BuildingLanternLight.cs
// One warm point light per LARGE building — the pool of lantern light on
// the ground that marks the important structures at dusk
// (docs/Design/Art_Direction.md §4.4). Huts, towers and wall pieces get
// none: the budget is a design choice, not a GPU one (Forward+, shadows
// off).
//
// The light is dark while the building is under construction — an unlit
// window is the "not finished yet" read (§6.3) — and comes on when the
// site completes. Attached by PresentationSpawnSystem on both building
// visual paths (prefab and procedural); values live in
// BuildingLanternLight.asset beside this file.

using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    public sealed class BuildingLanternLight : MonoBehaviour
    {
        private static BuildingLanternLightConfig _cfg;
        private static BuildingLanternLightConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<BuildingLanternLightConfig>());

        public Entity Entity;

        private Light _light;
        private EntityManager _em;
        private bool _valid;
        private float _nextCheck;

        /// <summary>
        /// Attach a lantern to <paramref name="go"/> if the building is large
        /// enough to earn one. Curse structures (the well, pockets) are
        /// buildings in ECS terms but never lanterns — they glow on their own
        /// rung of the ladder.
        /// </summary>
        public static void Attach(GameObject go, Entity entity, EntityManager em)
        {
            if (go == null || Cfg == null) return;
            if (go.GetComponent<BuildingLanternLight>() != null) return;
            if (!em.HasComponent<BuildingSize>(entity)) return;
            if (em.HasComponent<FactionTag>(entity)
                && em.GetComponentData<FactionTag>(entity).Value == Faction.Border) return;

            var size = em.GetComponentData<BuildingSize>(entity);
            if (Mathf.Min(size.Width, size.Height) < Cfg.minFootprintMetres) return;

            var lantern = go.AddComponent<BuildingLanternLight>();
            lantern.Entity = entity;
        }

        void Start()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            _em = world.EntityManager;
            _valid = true;

            // Parented so it rides the construction rise, but placed in WORLD
            // units: building roots carry an arbitrary authored scale, so a
            // local offset of 3 could land anywhere from 3 cm to 30 m up.
            var lightGo = new GameObject("LanternLight");
            lightGo.transform.SetParent(transform, worldPositionStays: false);
            lightGo.transform.position = transform.position + Vector3.up * Cfg.height;

            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Cfg.color;
            _light.intensity = Cfg.intensity;
            _light.range = Cfg.range;
            _light.shadows = LightShadows.None;
            _light.enabled = false;          // construction gate below
        }

        void LateUpdate()
        {
            if (!_valid || _light == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + Cfg.constructionPollSeconds;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (_em != world.EntityManager) _em = world.EntityManager;
            if (Entity == Entity.Null || !_em.Exists(Entity)) { _light.enabled = false; return; }

            _light.enabled = !_em.HasComponent<UnderConstruction>(Entity);
        }
    }
}
