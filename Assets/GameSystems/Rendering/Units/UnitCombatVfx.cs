// UnitCombatVfx.cs
// Combat effects on units (docs/Design/Vfx_Assignments.md §1):
//   * hits — melee: Hovl Holy hit, or Hovl Stones hit when the attacker was
//     charging; ballista bolts and special projectiles: Holy hit; ARROWS by
//     the shooter's arrow-tip tier — none below Veilstone, Lana Hit_dark_magic
//     on Veilstone, Lana Hit_electric on Veilsteel; drained from CombatVfxSignals,
//     at most one per unit per hitCooldownSeconds and maxHitsPerFrame overall;
//   * buff states, looping while they last —
//       speed up   SpellBuff.SpeedMultiplier above 1   (Lana Fog_speedFast)
//       slowed     SpellDebuff.SpeedReduction above 0  (Lana Fog_speedSlow)
//       attack up  SpellBuff.DamageMultiplier above 1, or a War Totem aura
//                  (Lana Fog_electric) — King's Call allies, Liquid Courage, …
// Hits are anchored in the WORLD where they landed; buff loops are parented to
// the unit's view so they follow it and fog of war hides them with it.
// Presentation only.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public sealed class UnitCombatVfx : MonoBehaviour
    {
        private static UnitCombatVfxConfig _cfg;
        private static UnitCombatVfxConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<UnitCombatVfxConfig>());

        private enum Buff { SpeedUp, SlowDown, AttackUp }
        private const int BuffCount = 3;

        private readonly Dictionary<Entity, float> _nextHit = new();
        private readonly Dictionary<(Entity, Buff), GameObject> _loops = new();
        private readonly HashSet<(Entity, Buff)> _want = new();
        private readonly List<(Entity, Buff)> _scratch = new();
        private float _nextPoll;
        private EntityQuery _buffed, _slowed, _totem;
        private EntityManager _queryOwner;

        void OnEnable() => CombatVfxSignals.Clear();

        void Update()
        {
            var cfg = Cfg;
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (cfg == null || world == null || !world.IsCreated) { CombatVfxSignals.Clear(); return; }
            var em = world.EntityManager;

            PlayHits(em, cfg);
            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + cfg.buffPollSeconds;
                UpdateBuffs(em, cfg);
            }
        }

        private void PlayHits(EntityManager em, UnitCombatVfxConfig cfg)
        {
            int spawned = 0;
            while (CombatVfxSignals.TryTake(out var hit))
            {
                if (spawned >= cfg.maxHitsPerFrame) continue;          // drain, but cap what we draw
                if (_nextHit.TryGetValue(hit.Target, out var next) && Time.time < next) continue;
                GameObject prefab;
                bool arrowTier = false;
                if (hit.Source == HitSource.Arrow)
                {
                    var tier = ArrowTrailTiers.Of(hit.AttackerFaction);
                    prefab = tier == ArrowTrailTier.Veilsteel ? cfg.arrowVeilsteelHitPrefab
                           : tier == ArrowTrailTier.Veilstone ? cfg.arrowVeilstoneHitPrefab
                           : null;
                    arrowTier = true;
                }
                else prefab = hit.Source == HitSource.Melee && hit.Charge ? cfg.chargeHitPrefab : cfg.hitPrefab;
                if (prefab == null || !VfxSpawn.TryVisibleView(hit.Target, out var view)) continue;

                // World-anchored, not parented: the hit stays where it landed
                // while the unit walks on. (Only spawned when the unit is
                // visible, so fog of war cannot leak it.)
                float h = VfxSpawn.Height(view);
                VfxSpawn.OneShot(prefab, null, VfxSpawn.Foot(view) + Vector3.up * h * cfg.hitHeightFraction,
                                 arrowTier ? ArrowTrailTiers.HitScale(ArrowTrailTiers.Of(hit.AttackerFaction)) : h * cfg.scalePerMetre);
                _nextHit[hit.Target] = Time.time + cfg.hitCooldownSeconds;
                spawned++;
            }
            if (_nextHit.Count > 2048) _nextHit.Clear();   // dead units' entries; cheap to rebuild
        }

        private void UpdateBuffs(EntityManager em, UnitCombatVfxConfig cfg)
        {
            if (_queryOwner != em || _buffed == default)
            {
                _buffed = new EntityQueryBuilder(Allocator.Temp).WithAll<SpellBuff, UnitTag>().Build(em);
                _slowed = new EntityQueryBuilder(Allocator.Temp).WithAll<SpellDebuff, UnitTag>().Build(em);
                _totem = new EntityQueryBuilder(Allocator.Temp).WithAll<TotemAuraBuff, UnitTag>().Build(em);
                _queryOwner = em;
            }
            _want.Clear();
            float t = cfg.buffThreshold;

            using (var ents = _buffed.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (!em.IsComponentEnabled<SpellBuff>(e)) continue;
                    var b = em.GetComponentData<SpellBuff>(e);
                    if (b.SpeedMultiplier > 1f + t) _want.Add((e, Buff.SpeedUp));
                    if (b.SpeedMultiplier > 0f && b.SpeedMultiplier < 1f - t) _want.Add((e, Buff.SlowDown));
                    if (b.DamageMultiplier > 1f + t) _want.Add((e, Buff.AttackUp));
                }
            using (var ents = _slowed.ToEntityArray(Allocator.Temp))
            using (var debuffs = _slowed.ToComponentDataArray<SpellDebuff>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                    if (debuffs[i].SpeedReduction > t) _want.Add((ents[i], Buff.SlowDown));
            using (var ents = _totem.ToEntityArray(Allocator.Temp))
            using (var totems = _totem.ToComponentDataArray<TotemAuraBuff>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                    if (totems[i].AttackBonus > t) _want.Add((ents[i], Buff.AttackUp));

            foreach (var key in _want)
            {
                if (!EntityViewManager.Instance.TryGetView(key.Item1, out var view) || view == null) continue;
                if (_loops.TryGetValue(key, out var fx) && fx != null && fx.transform.parent == view.transform) continue;
                if (fx != null) Destroy(fx);
                var prefab = key.Item2 switch
                {
                    Buff.SpeedUp => cfg.speedUpPrefab,
                    Buff.SlowDown => cfg.slowDownPrefab,
                    _ => cfg.attackUpPrefab,
                };
                if (prefab == null) continue;
                _loops[key] = VfxSpawn.Spawn(prefab, view.transform, VfxSpawn.Foot(view),
                                             VfxSpawn.Height(view) * cfg.scalePerMetre, loop: true);
            }

            _scratch.Clear();
            foreach (var kv in _loops) if (!_want.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var k in _scratch)
            {
                VfxSpawn.FadeOut(_loops[k], cfg.fadeSeconds);
                _loops.Remove(k);
            }
        }

        void OnDestroy()
        {
            foreach (var fx in _loops.Values) if (fx != null) Destroy(fx);
            _loops.Clear();
        }
    }
}
