// MinimapPings.cs
// Lightweight world-event pings for the minimap (user request 2026-08-04):
//   RED    — a local-player unit/building is taking damage
//   PURPLE — curse events (node corrupting, blood pool contaminating)
//   GOLD   — a sect power was fired
// Static registry; presentation systems Post() from anywhere, the
// MinimapPanelBinder draws live pings as flashing diamonds over its
// overlay each refresh. Self-pruning by expiry; near-duplicate same-color
// pings merge so damage spam reads as one hot spot, not confetti.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.UI.GameUI
{
    public static class MinimapPings
    {
        public struct Ping
        {
            public float3 Pos;
            public Color32 Color;
            public float Expiry;   // Time.time based (presentation-only)
            public bool Big;
        }

        public static readonly Color32 Damage = new Color32(255, 60, 40, 255);
        public static readonly Color32 Curse = new Color32(190, 90, 255, 255);
        public static readonly Color32 Power = new Color32(255, 210, 70, 255);

        private const int MaxPings = 64;
        private const float MergeRadius = 8f;

        private static readonly List<Ping> _pings = new();

        public static void Post(float3 worldPos, Color32 color, float duration, bool big = false)
        {
            float mergeSq = MergeRadius * MergeRadius;
            for (int i = 0; i < _pings.Count; i++)
            {
                if (_pings[i].Color.r != color.r || _pings[i].Color.g != color.g
                    || _pings[i].Color.b != color.b) continue;
                if (math.distancesq(_pings[i].Pos, worldPos) > mergeSq) continue;
                var p = _pings[i];
                p.Expiry = Mathf.Max(p.Expiry, Time.time + duration);
                _pings[i] = p;
                return;
            }

            if (_pings.Count >= MaxPings) _pings.RemoveAt(0);
            _pings.Add(new Ping
            {
                Pos = worldPos,
                Color = color,
                Expiry = Time.time + duration,
                Big = big,
            });
        }

        /// <summary>Live pings (pruned). Flash phase is the caller's job.</summary>
        public static List<Ping> Live()
        {
            for (int i = _pings.Count - 1; i >= 0; i--)
                if (Time.time > _pings[i].Expiry)
                    _pings.RemoveAt(i);
            return _pings;
        }
    }
}
