// File: Assets/Scripts/World/Terrain/PaintOnlyPassability.cs
// Per-map opt-in: when this component exists in the map scene, the
// PassabilityGrid ignores its derived terrain rules (slope budget, water
// level) and uses ONLY the hand-painted "NoWalk" terrain layer — painted
// cells are blocked, everything else is passable. Intended for test maps
// where the author wants full manual control of walkability.
//
// Buildings and obstacles still stamp their footprints on top as usual.
// If the terrain has no NoWalk layer, the flag is ignored (with a warning)
// so a misconfigured map doesn't silently become 100% walkable water.

using UnityEngine;

namespace TheWaningBorder.World.Terrain
{
    [DisallowMultipleComponent]
    public class PaintOnlyPassability : MonoBehaviour
    {
        public static PaintOnlyPassability Instance { get; private set; }

        /// <summary>True when the active map opted into paint-only passability.</summary>
        public static bool Active => Instance != null;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
