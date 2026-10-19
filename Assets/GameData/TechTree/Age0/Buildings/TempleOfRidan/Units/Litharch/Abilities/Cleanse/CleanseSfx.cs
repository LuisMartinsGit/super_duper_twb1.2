// CleanseSfx.cs
// The SOUND of a Litharch's Cleanse (Territory_Claims.md §6.9.7, 2026-10-19):
// a positional loop at every channelling Litharch, and a burst at the aim
// point when the cast lands (the starting circle is cleared on that tick).
//
// Presentation only. It reads CleanseChannel and never writes anything the
// simulation reads, so it cannot affect lockstep. A channel is heard only
// while its Litharch's view is visible (fog of war hides views), and every
// source plays through the AudioListener, so the master volume applies.
// Clips and levels are on CleanseSfx.asset, beside this file.

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

        private sealed class Voice
        {
            public AudioSource Source;
            public float Target;     // volume it fades toward
        }

        private readonly Dictionary<Entity, Voice> _voices = new();
        private readonly HashSet<Entity> _wasActive = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _scratch = new();
        private readonly Stack<AudioSource> _pool = new();
        private float _nextPoll;
        private EntityQuery _channels;
        private EntityManager _queryOwner;

        void Update()
        {
            var cfg = Cfg;
            if (cfg == null) return;
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + Mathf.Max(0.02f, cfg.pollSeconds);
                Poll(cfg);
            }
            Fade(cfg);
        }

        void OnDisable()
        {
            foreach (var v in _voices.Values) Release(v.Source);
            _voices.Clear();
            _wasActive.Clear();
        }

        private void Poll(CleanseSfxConfig cfg)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                foreach (var v in _voices.Values) v.Target = 0f;
                _wasActive.Clear();
                return;
            }
            var em = world.EntityManager;
            if (_queryOwner != em || _channels == default)
            {
                // Built once per world (not per poll): a repeated
                // CreateEntityQuery would grow the world's query registry.
                _channels = new EntityQueryBuilder(Allocator.Temp).WithAll<CleanseChannel>().Build(em);
                _queryOwner = em;
                _wasActive.Clear();
            }

            _seen.Clear();
            using var ents = _channels.ToEntityArray(Allocator.Temp);
            using var chans = _channels.ToComponentDataArray<CleanseChannel>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                bool active = chans[i].Active != 0;
                bool started = active && !_wasActive.Contains(e);
                if (active) _seen.Add(e);

                bool visible = VfxSpawn.TryVisibleView(e, out var view);
                if (started && visible && cfg.burstClip != null)
                {
                    var c = chans[i].Center;
                    PlayBurst(cfg, new Vector3(c.x, c.y, c.z));
                }

                if (active && visible && cfg.loopClip != null)
                {
                    if (!_voices.TryGetValue(e, out var v))
                    {
                        v = new Voice { Source = Acquire(cfg) };
                        v.Source.volume = 0f;
                        v.Source.Play();
                        _voices[e] = v;
                    }
                    v.Source.transform.position = view.transform.position;
                    v.Target = Mathf.Clamp01(cfg.loopVolume);
                }
                else if (_voices.TryGetValue(e, out var v))
                {
                    v.Target = 0f;   // ended, or out of sight: fade out
                }
            }

            _wasActive.Clear();
            foreach (var e in _seen) _wasActive.Add(e);

            // Voices whose Litharch is gone fade out too.
            foreach (var kv in _voices)
                if (!_seen.Contains(kv.Key)) kv.Value.Target = 0f;
        }

        private void Fade(CleanseSfxConfig cfg)
        {
            if (_voices.Count == 0) return;
            float step = cfg.fadeSeconds > 0f ? Time.unscaledDeltaTime / cfg.fadeSeconds : 1f;
            _scratch.Clear();
            foreach (var kv in _voices)
            {
                var v = kv.Value;
                if (v.Source == null) { _scratch.Add(kv.Key); continue; }
                v.Source.volume = Mathf.MoveTowards(v.Source.volume, v.Target, step * Mathf.Max(0.01f, cfg.loopVolume));
                if (v.Target <= 0f && v.Source.volume <= 0f)
                {
                    Release(v.Source);
                    _scratch.Add(kv.Key);
                }
            }
            foreach (var e in _scratch) _voices.Remove(e);
        }

        private AudioSource Acquire(CleanseSfxConfig cfg)
        {
            AudioSource src = null;
            while (_pool.Count > 0 && src == null) src = _pool.Pop();
            if (src == null)
            {
                var go = new GameObject("CleanseLoop");
                go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>();
            }
            src.gameObject.SetActive(true);
            Configure(src, cfg);
            src.clip = cfg.loopClip;
            src.loop = true;
            return src;
        }

        private void Release(AudioSource src)
        {
            if (src == null) return;
            src.Stop();
            src.gameObject.SetActive(false);
            _pool.Push(src);
        }

        private void PlayBurst(CleanseSfxConfig cfg, Vector3 at)
        {
            var go = new GameObject("CleanseBurst");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            Configure(src, cfg);
            src.clip = cfg.burstClip;
            src.loop = false;
            src.volume = Mathf.Clamp01(cfg.burstVolume);
            src.Play();
            Destroy(go, cfg.burstClip.length + 0.1f);
        }

        private static void Configure(AudioSource src, CleanseSfxConfig cfg)
        {
            src.playOnAwake = false;
            src.spatialBlend = Mathf.Clamp01(cfg.spatialBlend);
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = Mathf.Max(0.1f, cfg.minDistance);
            src.maxDistance = Mathf.Max(src.minDistance + 0.1f, cfg.maxDistance);
            src.dopplerLevel = 0f;
        }
    }
}
