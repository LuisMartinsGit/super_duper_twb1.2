using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIStrengthMap"/>. The asset is
    /// AIStrengthMap.asset, beside AIStrengthMap.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIStrengthMap",
                     fileName = "AIStrengthMap")]
    public sealed class AIStrengthMapConfig : ScriptableObject, IComponentConfig
    {
        static AIStrengthMapConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIStrengthMapConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIStrengthMapConfig>());

        /// <summary>Seconds one full incremental walk of every unit and
        /// building takes — how stale a strength read may be. Operator
        /// direction 2026-09-25: five seconds is fine.</summary>
        public float refreshSeconds;

        /// <summary>Spatial-hash cell edge in metres. Reads scan only the
        /// cells their radius touches, so this should sit near the common
        /// query radii (24-40 m).</summary>
        public float cellSize;

        /// <summary>Floor on entities recorded per frame, so a small match
        /// still finishes its walk well inside the refresh window.</summary>
        public int minEntitiesPerFrame;

        /// <summary>Stop walking when nothing has read the map for this
        /// long (no AI in the match); the next read rebuilds at once.</summary>
        public float idleStopSeconds;

        /// <summary>Extra metres a focus-fire candidate search reaches past
        /// its radius — a unit can walk this far between refreshes. The live
        /// re-check then applies the exact radius.</summary>
        public float candidateMargin;
    }
}
