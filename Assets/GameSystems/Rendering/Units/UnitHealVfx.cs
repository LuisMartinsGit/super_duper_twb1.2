// UnitHealVfx.cs
// Plays the heal effect (Lana Studio Regeneration_health) on every UNIT that is
// healed, whatever healed it: Litharch, Temple aura, Mending Hall,
// Field Hospital, Hands of Plenty and its regen tail, Second Wind's expiry
// heal, Scar Guard's Rapid Mend, the War Totem, Sanctify. There is no heal
// event in the sim, so — like DamageNumbersUI — this watches Health for rises.
//
// Not heals, and not shown:
//   * building repair and construction (buildings carry no UnitTag);
//   * a Max HP change (research raises Max and Value together);
//   * the Life Cling / Second Wind HP floors, which push HP back up while the
//     unit is being hit — a unit under either keeps its baseline moving
//     silently;
//   * trickles under minHealFraction (rank regen).
// Presentation only.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Settings;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public sealed class UnitHealVfx : MonoBehaviour
    {
        private static UnitHealVfxConfig _cfg;
        private static UnitHealVfxConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<UnitHealVfxConfig>());

        private readonly Dictionary<Entity, (int value, int max)> _last = new();
        private readonly Dictionary<Entity, float> _nextAllowed = new();
        private readonly HashSet<Entity> _alive = new();
        private readonly List<Entity> _scratch = new();
        private float _nextPoll;
        private EntityQuery _units;
        private EntityManager _queryOwner;

        void Update()
        {
            var cfg = Cfg;
            if (cfg == null || cfg.healPrefab == null || Time.time < _nextPoll) return;
            _nextPoll = Time.time + cfg.pollSeconds;
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (_queryOwner != em || _units == default)
            {
                _units = new EntityQueryBuilder(Allocator.Temp).WithAll<UnitTag, Health>().Build(em);
                _queryOwner = em;
                _last.Clear(); _nextAllowed.Clear();
            }

            _alive.Clear();
            using var ents = _units.ToEntityArray(Allocator.Temp);
            using var hps = _units.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var h = hps[i];
                _alive.Add(e);
                bool known = _last.TryGetValue(e, out var last);
                _last[e] = (h.Value, h.Max);
                if (!known || h.Max != last.max || h.Max <= 0) continue;

                int gained = h.Value - last.value;
                if (gained <= 0 || gained < cfg.minHealFraction * h.Max) continue;
                if (em.HasComponent<TheWaningBorder.Abilities.LifeCling>(e) || em.HasComponent<SectDeathWard>(e)) continue;
                if (_nextAllowed.TryGetValue(e, out var next) && Time.time < next) continue;
                if (!VfxSpawn.TryVisibleView(e, out var view)) continue;

                _nextAllowed[e] = Time.time + cfg.cooldownSeconds;
                VfxSpawn.OneShot(cfg.healPrefab, view.transform, VfxSpawn.Foot(view),
                                 VfxSpawn.Height(view) * cfg.scalePerMetre);
            }

            _scratch.Clear();
            foreach (var e in _last.Keys) if (!_alive.Contains(e)) _scratch.Add(e);
            foreach (var e in _scratch) { _last.Remove(e); _nextAllowed.Remove(e); }
        }
    }
}
