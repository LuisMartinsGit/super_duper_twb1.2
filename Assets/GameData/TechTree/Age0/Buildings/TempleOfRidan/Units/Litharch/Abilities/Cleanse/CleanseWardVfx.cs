// CleanseWardVfx.cs
// The Cleansing Ward on screen (Territory_Claims.md §6.9.7, 2026-10-22):
//   * CleanseWardVisual builds the ward's view from the prefab on
//     CleanseWardSystem.asset (PresentationSpawnSystem's authored branch),
//     with a plain stone-and-crystal stand-in when the slot is empty;
//   * CleanseWardVfx keeps the ward's calm blue aura (2026-11-18: a blue copy
//     of the Field Hospital's healing-area loop, CleanseWard_Aura.prefab,
//     replacing the old per-pulse burst rings) looping on every standing
//     ward, sized to its clean circle and growing with it, and plays that aura
//     once where a Cleanse Pulse lands. Prefab, authored radius and fade are on
//     CleanseWardSystem.asset.
// Presentation only: reads CleanseWardState, writes nothing the simulation
// reads. The aura is parented to the ward's view, so fog hides it with it.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Systems.Border;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public static class CleanseWardVisual
    {
        public static GameObject Build()
        {
            var cfg = CleanseWardSystemConfig.I;
            if (cfg != null && cfg.wardPrefab != null)
            {
                var go = Object.Instantiate(cfg.wardPrefab);
                go.name = "CleanseWard";
                go.SetActive(true);
                return go;
            }
            // No art bound yet (a data gap, logged once by the config
            // validator): a stone plinth with a crystal on it.
            var root = new GameObject("CleanseWard");
            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "Plinth";
            plinth.transform.SetParent(root.transform, false);
            plinth.transform.localScale = new Vector3(1.4f, 0.6f, 1.4f);
            plinth.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var crystal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            crystal.name = "Crystal";
            crystal.transform.SetParent(root.transform, false);
            crystal.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            crystal.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var r = crystal.GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = new Color(0.75f, 0.95f, 1f);
                if (r.material.HasProperty("_EmissionColor"))
                {
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", new Color(0.6f, 0.9f, 1f) * 2f);
                }
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return root;
        }
    }

    /// <summary>
    /// A Cleanse Pulse has no entity for the presentation to poll, so its one
    /// aura is posted here by the executor and drained by CleanseWardVfx.
    /// Presentation only: nothing the simulation reads.
    /// </summary>
    public static class CleansePulseSignals
    {
        private const int Cap = 16;
        private static readonly Queue<(Vector3 At, float Radius)> Pending = new();

        public static void Post(Unity.Mathematics.float3 at, float radius)
        {
            if (Pending.Count >= Cap) Pending.Dequeue();   // headless: nobody drains
            Pending.Enqueue((new Vector3(at.x, at.y, at.z), radius));
        }

        public static bool TryTake(out Vector3 at, out float radius)
        {
            at = default; radius = 0f;
            if (Pending.Count == 0) return false;
            (at, radius) = Pending.Dequeue();
            return true;
        }
    }

    public sealed class CleanseWardVfx : MonoBehaviour
    {
        private readonly Dictionary<Entity, (GameObject Fx, float Radius)> _live = new();
        private readonly HashSet<Entity> _alive = new();
        private readonly List<Entity> _scratch = new();
        private EntityQuery _wards;
        private EntityManager _queryOwner;
        private float _nextPoll;

        void Update()
        {
            var cfg = CleanseWardSystemConfig.I;
            if (cfg == null || Time.time < _nextPoll) return;
            _nextPoll = Time.time + Mathf.Max(0.02f, cfg.pollSeconds);
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            float authored = Mathf.Max(0.1f, cfg.auraAuthoredRadius);

            // Cleanse Pulses: the aura played once, seen and heard only where
            // the local player can see the ground.
            while (CleansePulseSignals.TryTake(out var pulseAt, out float pulseRadius))
            {
                if (!TheWaningBorder.World.FogOfWar.FactionSight.CanSee(
                        em, GameSettings.LocalPlayerFaction, pulseAt, false)
                    && !GameSettings.IsSpectating) continue;
                if (cfg.auraPrefab != null)
                    VfxSpawn.OneShot(cfg.auraPrefab, transform, pulseAt, pulseRadius / authored);
                CleanseSfx.PlayBurst(pulseAt);
            }

            if (_queryOwner != em || _wards == default)
            {
                _wards = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<CleanseWardTag, CleanseWardState>().Build(em);
                _queryOwner = em;
                Clear();
            }

            // Wards: one looping aura each, as the Field Hospital wears its
            // own, resized when the clean circle grows.
            _alive.Clear();
            if (cfg.auraPrefab != null)
                using (var ents = _wards.ToEntityArray(Allocator.Temp))
                using (var states = _wards.ToComponentDataArray<CleanseWardState>(Allocator.Temp))
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var e = ents[i];
                        if (EntityViewManager.Instance == null
                            || !EntityViewManager.Instance.TryGetView(e, out var view) || view == null) continue;
                        _alive.Add(e);
                        float radius = states[i].Radius;
                        if (_live.TryGetValue(e, out var live) && live.Fx != null
                            && live.Fx.transform.parent == view.transform)
                        {
                            if (!Mathf.Approximately(live.Radius, radius))
                            {
                                VfxSpawn.SetWorldScale(live.Fx.transform,
                                    cfg.auraPrefab.transform.localScale * (radius / authored));
                                _live[e] = (live.Fx, radius);
                            }
                            continue;
                        }
                        if (live.Fx != null) Destroy(live.Fx);
                        var fx = VfxSpawn.Spawn(cfg.auraPrefab, view.transform, VfxSpawn.Foot(view),
                                                radius / authored, loop: true);
                        _live[e] = (fx, radius);
                    }

            _scratch.Clear();
            foreach (var kv in _live) if (!_alive.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var e in _scratch)
            {
                VfxSpawn.FadeOut(_live[e].Fx, Mathf.Max(0f, cfg.auraFadeSeconds));
                _live.Remove(e);
            }
        }

        private void Clear()
        {
            foreach (var live in _live.Values) if (live.Fx != null) Destroy(live.Fx);
            _live.Clear();
        }

        void OnDestroy() => Clear();
    }
}
