// AlertSounds.cs
// The sounds that tell a player to look up (docs/Design/Alert_Sounds.md): the
// horn when their units or buildings are being hit, the bells for the Shardroot,
// monuments and the victory countdowns, and the fanfare at the end of a match.
//
// Presentation only: SimSignalPump calls this with what it drains from SimSignals,
// and nothing here is read back by the simulation. Flat 2D, heard wherever the
// camera is, through the AudioListener, so the master volume applies. Clips and
// levels are on AlertSounds.asset, beside this file.

using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using UnityEngine;

namespace TheWaningBorder.Systems.Audio
{
    public sealed class AlertSounds : MonoBehaviour
    {
        private static AlertSoundsConfig _cfg;
        private static AlertSoundsConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<AlertSoundsConfig>());

        private static AlertSounds _instance;
        private AudioSource _source;

        private float _nextEvent, _lastAttack = float.NegativeInfinity;
        private Vector3 _lastAttackAt;

        private static AlertSounds Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("[AlertSounds]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<AlertSounds>();
                _instance._source = go.AddComponent<AudioSource>();
                _instance._source.playOnAwake = false;
                _instance._source.spatialBlend = 0f;
                _instance._source.ignoreListenerPause = true;   // the pause menu must not swallow a match end
                return _instance;
            }
        }

        /// <summary>A hostile hit landed on something of the local player's. The horn
        /// repeats slowly while a fight stays in one place, sooner when a new one opens
        /// elsewhere.</summary>
        public static void UnderAttack(Vector3 at)
        {
            var cfg = Cfg;
            if (cfg == null) return;
            var self = Instance;
            float since = Time.unscaledTime - self._lastAttack;
            if (since < cfg.attackMinGapSeconds) return;
            float radius = cfg.attackAreaRadius;
            bool sameArea = (at - self._lastAttackAt).sqrMagnitude <= radius * radius;
            if (sameArea && since < cfg.attackRepeatSeconds) return;
            self._lastAttack = Time.unscaledTime;
            self._lastAttackAt = at;
            self.Play("UnderAttack", cfg.underAttackClip, cfg.underAttackVolume);
        }

        /// <summary>Something important happened on the map. One event often posts
        /// several signals in the same moment (a carrier falling drops the Shardroot and
        /// detonates), so a short gap keeps them to one sound.</summary>
        public static void MapEvent(SimAlertKind kind)
        {
            var cfg = Cfg;
            if (cfg == null) return;
            var self = Instance;
            if (Time.unscaledTime < self._nextEvent) return;
            self._nextEvent = Time.unscaledTime + cfg.eventMinGapSeconds;
            switch (kind)
            {
                case SimAlertKind.Shardroot: self.Play("Shardroot", cfg.shardrootClip, cfg.shardrootVolume); break;
                case SimAlertKind.Monument:  self.Play("Monument", cfg.monumentClip, cfg.monumentVolume); break;
                case SimAlertKind.Countdown: self.Play("Countdown", cfg.countdownClip, cfg.countdownVolume); break;
            }
        }

        /// <summary>The match was decided. Always plays: nothing else matters now.</summary>
        public static void MatchEnd(bool won)
        {
            var cfg = Cfg;
            if (cfg == null) return;
            if (won) Instance.Play("Victory", cfg.victoryClip, cfg.victoryVolume);
            else Instance.Play("Defeat", cfg.defeatClip, cfg.defeatVolume);
        }

        private void Play(string label, AudioClip clip, float volume)
        {
            if (clip == null)
            {
                Debug.LogError($"[Alert] {label}: AlertSounds.asset has no clip");
                return;
            }
            Debug.Log($"[Alert] {label}");
            _source.PlayOneShot(clip, volume);
        }
    }
}
