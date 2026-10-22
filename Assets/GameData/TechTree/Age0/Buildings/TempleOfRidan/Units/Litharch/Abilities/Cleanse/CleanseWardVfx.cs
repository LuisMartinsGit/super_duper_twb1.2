// CleanseWardVfx.cs
// The Cleansing Ward on screen (Territory_Claims.md §6.9.7, 2026-10-22):
//   * CleanseWardVisual builds the ward's view from the prefab on
//     CleanseWardSystem.asset (PresentationSpawnSystem's authored branch),
//     with a plain stone-and-crystal stand-in when the slot is empty;
//   * CleanseWardVfx plays the pulse aura: a one-shot ring at every ward whose
//     PulseCount moved since the last poll, scaled to the circle's radius —
//     the same tick the ground was cleared, so the curse retreats with the
//     burst.
// Presentation only: reads CleanseWardState, writes nothing the simulation
// reads. A ward is drawn only when its view is visible (fog).

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

    public sealed class CleanseWardVfx : MonoBehaviour
    {
        private readonly Dictionary<Entity, int> _seenPulse = new();
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
            if (_queryOwner != em || _wards == default)
            {
                _wards = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<CleanseWardTag, CleanseWardState>().Build(em);
                _queryOwner = em;
                _seenPulse.Clear();
            }

            _alive.Clear();
            using (var ents = _wards.ToEntityArray(Allocator.Temp))
            using (var states = _wards.ToComponentDataArray<CleanseWardState>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    _alive.Add(e);
                    int count = states[i].PulseCount;
                    bool known = _seenPulse.TryGetValue(e, out int last);
                    _seenPulse[e] = count;
                    // A ward seen for the first time plays nothing: its landing
                    // burst is the cast's own effect.
                    if (!known || count == last) continue;
                    if (cfg.pulsePrefab == null) continue;
                    if (!VfxSpawn.TryVisibleView(e, out var view) || view == null) continue;
                    float scale = states[i].Radius / Mathf.Max(0.1f, cfg.pulseAuthoredRadius);
                    VfxSpawn.OneShot(cfg.pulsePrefab, view.transform, VfxSpawn.Foot(view), scale);
                }

            _scratch.Clear();
            foreach (var kv in _seenPulse) if (!_alive.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var e in _scratch) _seenPulse.Remove(e);
        }
    }
}
