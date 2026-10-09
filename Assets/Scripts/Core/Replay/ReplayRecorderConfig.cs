using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Core.Replay
{
    /// <summary>
    /// Tuning numbers for replays and saved games (docs/Design/Replays_And_Saves.md):
    /// how many replays are kept, and how hard playback may push the simulation
    /// when it fast-forwards. The asset is ReplayRecorder.asset, beside
    /// ReplayRecorder.cs. No field initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Replay/ReplayRecorder",
                     fileName = "ReplayRecorder")]
    public sealed class ReplayRecorderConfig : ScriptableObject, IComponentConfig
    {
        static ReplayRecorderConfig _i;
        public static ReplayRecorderConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ReplayRecorderConfig>());

        /// <summary>Newest replays kept in the Replays folder; older ones are deleted.</summary>
        public int replaysKept;

        /// <summary>Real milliseconds per frame a fast-forward (loading a save,
        /// skipping ahead in a replay) may spend simulating ticks.</summary>
        public float fastForwardFrameBudgetMs;

        /// <summary>Most ticks one frame may run while a replay plays at speed.</summary>
        public int watchMaxTicksPerFrame;
    }
}
