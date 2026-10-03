// AbilityVfxPlayer.cs
// Plays each catalog ability's own effect — AbilityDefSO.vfxPrefab, the
// <Name>_Effect prefab in the ability's folder (King Lexor's, the Ledger's,
// the Litharch's Field Hospital, …). Until 2026-10-02 nothing read that slot.
//
//   * An ACTIVE ability: drained from AbilityVfxSignals when it lands.
//       - Area (formed around the caster): played at his feet, scaled to the
//         ability radius.
//       - otherwise on the affected entity, scaled to it, parented to its view
//         so fog of war hides it with the unit or building.
//       - timed abilities loop for their duration then fade; instant ones play once.
//   * A PASSIVE aura (King's Call): kept looping on every unit that carries
//     it, for as long as the unit lives.
// Placeholder effect prefabs (no particle system) are skipped. Presentation only.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core.Settings;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public sealed class AbilityVfxPlayer : MonoBehaviour
    {
        private static AbilityVfxPlayerConfig _cfg;
        private static AbilityVfxPlayerConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<AbilityVfxPlayerConfig>());

        private struct Timed { public GameObject Fx; public float EndsAt; }
        private readonly List<Timed> _timed = new();
        private readonly Dictionary<(Entity, int), GameObject> _passive = new();
        private readonly HashSet<(Entity, int)> _seen = new();
        private readonly List<(Entity, int)> _scratch = new();
        private float _nextPassivePoll;
        private EntityQuery _abilityUnits;
        private EntityManager _queryOwner;

        void OnEnable() => AbilityVfxSignals.Clear();

        void Update()
        {
            var cfg = Cfg;
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (cfg == null || world == null || !world.IsCreated) { AbilityVfxSignals.Clear(); return; }
            var em = world.EntityManager;

            while (AbilityVfxSignals.TryTake(out var landed)) Play(em, landed, cfg);

            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                var t = _timed[i];
                if (t.Fx == null) { _timed.RemoveAt(i); continue; }
                if (Time.time < t.EndsAt) continue;
                VfxSpawn.FadeOut(t.Fx, cfg.fadeSeconds);
                _timed.RemoveAt(i);
            }

            if (Time.time >= _nextPassivePoll)
            {
                _nextPassivePoll = Time.time + cfg.passivePollSeconds;
                UpdatePassives(em, cfg);
            }
        }

        private void Play(EntityManager em, AbilityVfxSignals.Landed l, AbilityVfxPlayerConfig cfg)
        {
            var prefab = l.Card.Vfx;
            if (!VfxSpawn.HasParticles(prefab)) return;
            bool aroundCaster = l.Card.Targeting == AbilityTargeting.Area && !l.Card.AimedAtPoint;
            var anchor = aroundCaster ? l.Caster : l.Target;
            if (!VfxSpawn.TryVisibleView(anchor, out var view)) return;

            float scale = aroundCaster ? AreaScale(l.Card.Radius, cfg) : ScaleFor(em, anchor, view, cfg);
            bool timed = l.Duration > 0f;
            var fx = VfxSpawn.Spawn(prefab, view.transform, VfxSpawn.Foot(view), scale, loop: timed);
            if (fx == null) return;
            if (timed) _timed.Add(new Timed { Fx = fx, EndsAt = Time.time + l.Duration });
            else Destroy(fx, VfxSpawn.OneShotLength(fx));
        }

        private void UpdatePassives(EntityManager em, AbilityVfxPlayerConfig cfg)
        {
            if (_queryOwner != em || _abilityUnits == default)
            {
                _abilityUnits = new EntityQueryBuilder(Allocator.Temp).WithAll<UnitAbilities, UnitTag>().Build(em);
                _queryOwner = em;
            }
            _seen.Clear();
            using (var ents = _abilityUnits.ToEntityArray(Allocator.Temp))
            using (var abil = _abilityUnits.ToComponentDataArray<UnitAbilities>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                    for (int slot = 0; slot < 4; slot++)
                    {
                        int idx = abil[i].Get(slot);
                        if (idx < 0 || idx >= AbilityCatalog.Count) continue;
                        var card = AbilityCatalog.Get(idx);
                        if (card == null || !card.IsPassive || !VfxSpawn.HasParticles(card.Vfx)) continue;
                        var key = (ents[i], slot);
                        if (!EntityViewManager.Instance.TryGetView(ents[i], out var view) || view == null) continue;
                        _seen.Add(key);
                        if (_passive.TryGetValue(key, out var fx) && fx != null && fx.transform.parent == view.transform)
                            continue;
                        if (fx != null) Destroy(fx);
                        _passive[key] = VfxSpawn.Spawn(card.Vfx, view.transform, VfxSpawn.Foot(view),
                                                       ScaleFor(em, ents[i], view, cfg), loop: true);
                    }

            _scratch.Clear();
            foreach (var kv in _passive) if (!_seen.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var k in _scratch)
            {
                VfxSpawn.FadeOut(_passive[k], cfg.fadeSeconds);
                _passive.Remove(k);
            }
        }

        private static float ScaleFor(EntityManager em, Entity e, GameObject view, AbilityVfxPlayerConfig cfg)
            => em.Exists(e) && em.HasComponent<BuildingTag>(e)
                ? VfxSpawn.HalfWidth(view) * cfg.buildingScalePerMetre
                : VfxSpawn.Height(view) * cfg.unitScalePerMetre;

        private static float AreaScale(float radius, AbilityVfxPlayerConfig cfg)
            => Mathf.Clamp(radius / Mathf.Max(0.1f, cfg.areaAuthoredRadius), cfg.areaMinScale, cfg.areaMaxScale);

        void OnDestroy()
        {
            foreach (var t in _timed) if (t.Fx != null) Destroy(t.Fx);
            foreach (var fx in _passive.Values) if (fx != null) Destroy(fx);
            _timed.Clear(); _passive.Clear();
        }
    }
}
