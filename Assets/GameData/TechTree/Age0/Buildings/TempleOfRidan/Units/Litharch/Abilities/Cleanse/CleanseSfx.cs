// CleanseSfx.cs
// The SOUND of a Cleansing Ward (Territory_Claims.md §6.9.7, 2026-10-22): a
// burst at the ward when it lands, and a softer one on every pulse, both at
// the ward's position, so a player can pick out a ward by ear.
//
// Presentation only. It reads CleanseWardState and never writes anything the
// simulation reads, so it cannot affect lockstep. A ward is heard only while
// its view is visible (fog of war hides views), and every source plays
// through the AudioListener, so the master volume applies. Clips and levels
// are on CleanseSfx.asset, beside this file.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Settings;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public sealed class CleanseSfx : MonoBehaviour
    {
        private static CleanseSfxConfig _cfg;
        private static CleanseSfxConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<CleanseSfxConfig>());

        private readonly Dictionary<Entity, int> _seenPulse = new();
        private readonly HashSet<Entity> _alive = new();
        private readonly List<Entity> _scratch = new();
        private readonly Stack<AudioSource> _pool = new();
        private float _nextPoll;
        private EntityQuery _wards;
        private EntityManager _queryOwner;

        void Update()
        {
            var cfg = Cfg;
            if (cfg == null || Time.time < _nextPoll) return;
            _nextPoll = Time.time + Mathf.Max(0.02f, cfg.pollSeconds);
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { _seenPulse.Clear(); return; }
            var em = world.EntityManager;
            if (_queryOwner != em || _wards == default)
            {
                _wards = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<CleanseWardTag, CleanseWardState, Unity.Transforms.LocalTransform>().Build(em);
                _queryOwner = em;
                _seenPulse.Clear();
            }

            _alive.Clear();
            using (var ents = _wards.ToEntityArray(Allocator.Temp))
            using (var states = _wards.ToComponentDataArray<CleanseWardState>(Allocator.Temp))
            using (var xfs = _wards.ToComponentDataArray<Unity.Transforms.LocalTransform>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    _alive.Add(e);
                    int count = states[i].PulseCount;
                    bool known = _seenPulse.TryGetValue(e, out int last);
                    _seenPulse[e] = count;
                    if (known && count == last) continue;
                    if (!VfxSpawn.TryVisibleView(e, out var view) || view == null) continue;
                    var p = xfs[i].Position;
                    Play(cfg, new Vector3(p.x, p.y, p.z), known ? cfg.pulseVolume : cfg.burstVolume);
                }

            _scratch.Clear();
            foreach (var kv in _seenPulse) if (!_alive.Contains(kv.Key)) _scratch.Add(kv.Key);
            foreach (var e in _scratch) _seenPulse.Remove(e);
        }

        private void Play(CleanseSfxConfig cfg, Vector3 at, float volume)
        {
            if (cfg.burstClip == null || volume <= 0f) return;
            var src = _pool.Count > 0 ? _pool.Pop() : NewSource(cfg);
            if (src == null) return;
            src.transform.position = at;
            src.volume = volume * AudioListener.volume;
            src.PlayOneShot(cfg.burstClip);
            StartCoroutine(ReturnWhenDone(src, cfg.burstClip.length + 0.1f));
        }

        private System.Collections.IEnumerator ReturnWhenDone(AudioSource src, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (src != null) _pool.Push(src);
        }

        private AudioSource NewSource(CleanseSfxConfig cfg)
        {
            var go = new GameObject("CleanseWardSfx");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = cfg.spatialBlend;
            src.minDistance = cfg.minDistance;
            src.maxDistance = cfg.maxDistance;
            src.rolloffMode = AudioRolloffMode.Linear;
            return src;
        }

        void OnDisable() => _seenPulse.Clear();
    }
}
