// PresentationSpawnSystem.Monument.cs
// The monument's visual: an interim Synty statue (MonumentSystem.asset) with a
// light in the holder's colour, the readout of who holds it.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Rendering;

public partial class PresentationSpawnSystem
{
    private GameObject CreateMonumentVisual(Vector3 center, Entity entity)
    {
        var cfg = MonumentSystemConfig.I;
        var root = new GameObject($"Monument_{entity.Index}");
        root.transform.position = center;

        if (cfg.statuePrefab != null)
        {
            var statue = Object.Instantiate(cfg.statuePrefab, root.transform);
            statue.transform.localPosition = Vector3.zero;
            statue.transform.localRotation = Quaternion.identity;
            statue.transform.localScale = Vector3.one * cfg.statueScale;
        }

        var lightGo = new GameObject("HolderLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = Vector3.up * cfg.lightHeight;
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = cfg.lightRange;
        light.intensity = cfg.lightIntensity;
        light.color = cfg.curseLightColor;

        var holder = root.AddComponent<MonumentHolderLight>();
        holder.Bind(entity, light);
        return root;
    }
}

namespace TheWaningBorder.Rendering
{
    /// <summary>Keeps the monument's light in its holder's colour.</summary>
    public sealed class MonumentHolderLight : MonoBehaviour
    {
        private const float Interval = 0.5f;
        private Entity _entity;
        private Light _light;
        private float _next;

        public void Bind(Entity entity, Light light)
        {
            _entity = entity;
            _light = light;
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _light == null) return;
            var em = world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<MonumentState>(_entity)) return;
            var holder = em.GetComponentData<MonumentState>(_entity).Holder;
            _light.color = (int)holder >= 8 ? MonumentSystemConfig.I.curseLightColor : FactionColors.Get(holder);
        }
    }
}
