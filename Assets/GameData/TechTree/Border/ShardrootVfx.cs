// ShardrootVfx.cs
// The Shardroot's loot effects (Lana Studio Loot_*), driven by its state:
//   * on the ground (ShardrootPickupTag) — idle loop on the artifact, and a
//     drop burst the moment it appears (unearthed, or fallen from a bearer);
//   * picked up — a pick-up burst on the unit that attuned it, the frame the
//     pickup entity is replaced by a ShardrootBearer;
//   * carried (ShardrootBearer on a unit) — a looping flicker on the bearer.
// Enshrined in a Temple / embedded in a Maw draws nothing here (the gem and
// its light are PresentationSpawnSystem.Shardroot). Presentation only.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Settings;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public sealed class ShardrootVfx : MonoBehaviour
    {
        private static ShardrootVfxConfig _cfg;
        private static ShardrootVfxConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<ShardrootVfxConfig>());

        private readonly Dictionary<Entity, GameObject> _idle = new();
        private readonly Dictionary<Entity, GameObject> _carried = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _scratch = new();
        private EntityQuery _pickups, _bearers;
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
            if (_queryOwner != em || _pickups == default)
            {
                _pickups = new EntityQueryBuilder(Allocator.Temp).WithAll<ShardrootPickupTag>().Build(em);
                _bearers = new EntityQueryBuilder(Allocator.Temp).WithAll<ShardrootBearer, UnitTag>().Build(em);
                _queryOwner = em;
            }

            // Ground: idle loop per pickup, drop burst when one appears.
            _seen.Clear();
            using (var ents = _pickups.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (!EntityViewManager.Instance.TryGetView(e, out var view) || view == null) continue;
                    _seen.Add(e);
                    if (_idle.TryGetValue(e, out var fx) && fx != null) continue;
                    _idle[e] = VfxSpawn.Spawn(cfg.idlePrefab, view.transform, VfxSpawn.Foot(view), cfg.groundScale, loop: true);
                    if (view.activeInHierarchy)
                        VfxSpawn.OneShot(cfg.dropPrefab, view.transform, VfxSpawn.Foot(view), cfg.groundScale);
                }
            bool pickupVanished = Prune(_idle, cfg);

            // Carried: flicker loop per bearer; a NEW bearer the same poll a
            // pickup vanished is the one who just took it.
            _seen.Clear();
            using (var ents = _bearers.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (!EntityViewManager.Instance.TryGetView(e, out var view) || view == null) continue;
                    _seen.Add(e);
                    if (_carried.TryGetValue(e, out var fx) && fx != null && fx.transform.parent == view.transform) continue;
                    bool isNew = fx == null;
                    if (fx != null) Destroy(fx);
                    float s = VfxSpawn.Height(view) * cfg.unitScalePerMetre;
                    _carried[e] = VfxSpawn.Spawn(cfg.carriedPrefab, view.transform, VfxSpawn.Foot(view), s, loop: true);
                    if (isNew && pickupVanished && view.activeInHierarchy)
                        VfxSpawn.OneShot(cfg.pickUpPrefab, view.transform, VfxSpawn.Foot(view), s);
                }
            Prune(_carried, cfg);
        }

        /// <summary>Fade out effects whose entity is gone; true if any went.</summary>
        private bool Prune(Dictionary<Entity, GameObject> live, ShardrootVfxConfig cfg)
        {
            _scratch.Clear();
            foreach (var kv in live) if (!_seen.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var e in _scratch)
            {
                VfxSpawn.FadeOut(live[e], cfg.fadeSeconds);
                live.Remove(e);
            }
            return _scratch.Count > 0;
        }

        void OnDestroy()
        {
            foreach (var fx in _idle.Values) if (fx != null) Destroy(fx);
            foreach (var fx in _carried.Values) if (fx != null) Destroy(fx);
            _idle.Clear(); _carried.Clear();
        }
    }
}
