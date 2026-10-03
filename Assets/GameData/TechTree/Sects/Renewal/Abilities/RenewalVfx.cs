// RenewalVfx.cs
// The Sect of Renewal's lasting effects, shown on what they affect (the
// landing of each power is SectPowerVfx's per-power override):
//   * Hands of Plenty III regen tail (SectRegenTail) — a regeneration loop on
//     every unit healing over time;
//   * Second Wind (SectDeathWard) — a ward on every unit that cannot die;
//   * Field Hospital (FieldHospitalTag) — a healing-area loop over the tent
//     for its whole life, sized to its heal radius.
// Individual heals still get the shared heal effect from UnitHealVfx.
// Presentation only.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Settings;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public sealed class RenewalVfx : MonoBehaviour
    {
        private static RenewalVfxConfig _cfg;
        private static RenewalVfxConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RenewalVfxConfig>());

        private sealed class Layer
        {
            public readonly Dictionary<Entity, GameObject> Live = new();
            public EntityQuery Query;
        }
        private readonly Layer _regen = new(), _ward = new(), _hospital = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _scratch = new();
        private EntityManager _queryOwner;
        private float _nextPoll;

        void Update()
        {
            var cfg = Cfg;
            if (cfg == null || Time.time < _nextPoll) return;
            _nextPoll = Time.time + cfg.pollSeconds;
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (_queryOwner != em || _regen.Query == default)
            {
                _regen.Query = new EntityQueryBuilder(Allocator.Temp).WithAll<SectRegenTail, UnitTag>().Build(em);
                _ward.Query = new EntityQueryBuilder(Allocator.Temp).WithAll<SectDeathWard, UnitTag>().Build(em);
                _hospital.Query = new EntityQueryBuilder(Allocator.Temp).WithAll<FieldHospitalTag>().Build(em);
                _queryOwner = em;
            }

            float hospitalScale = cfg.hospitalHealRadius / Mathf.Max(0.1f, cfg.hospitalAuthoredRadius);
            Sync(_regen, cfg.regenTailPrefab, cfg, v => VfxSpawn.Height(v) * cfg.unitScalePerMetre);
            Sync(_ward, cfg.deathWardPrefab, cfg, v => VfxSpawn.Height(v) * cfg.unitScalePerMetre);
            Sync(_hospital, cfg.fieldHospitalAuraPrefab, cfg, _ => hospitalScale);
        }

        /// <summary>Keep exactly one looping effect on each entity the layer's
        /// query matches; fade the rest out.</summary>
        private void Sync(Layer layer, GameObject prefab, RenewalVfxConfig cfg, System.Func<GameObject, float> scale)
        {
            if (prefab == null) return;
            _seen.Clear();
            using (var ents = layer.Query.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (!EntityViewManager.Instance.TryGetView(e, out var view) || view == null) continue;
                    _seen.Add(e);
                    if (layer.Live.TryGetValue(e, out var fx) && fx != null && fx.transform.parent == view.transform)
                        continue;
                    if (fx != null) Destroy(fx);
                    layer.Live[e] = VfxSpawn.Spawn(prefab, view.transform, VfxSpawn.Foot(view), scale(view), loop: true);
                }

            _scratch.Clear();
            foreach (var kv in layer.Live) if (!_seen.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var e in _scratch)
            {
                VfxSpawn.FadeOut(layer.Live[e], cfg.fadeSeconds);
                layer.Live.Remove(e);
            }
        }

        void OnDestroy()
        {
            foreach (var l in new[] { _regen, _ward, _hospital })
            {
                foreach (var fx in l.Live.Values) if (fx != null) Destroy(fx);
                l.Live.Clear();
            }
        }
    }
}
