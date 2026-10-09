using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Core.Diagnostics.MatchRecording
{
    /// <summary>
    /// MatchRecorder's tunables (docs/Design/Muster_Rolls_PostGame.md). The
    /// asset is MatchRecorder.asset, beside MatchRecorder.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Diagnostics/MatchRecorder",
                     fileName = "MatchRecorder")]
    public sealed class MatchRecorderConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Sim seconds between faction series samples (the charts).</summary>
        public float seriesIntervalSeconds;

        /// <summary>Sim seconds between unit position / building samples.
        /// Doubles every time the memory cap thins the record.</summary>
        public float positionIntervalSeconds;

        /// <summary>A unit that moved less than this (and kept its state)
        /// since its last stored sample is not stored again.</summary>
        public float minMoveMetres;

        /// <summary>Memory cap: unit position samples held in total. Past it
        /// every track drops every other sample and the period doubles.</summary>
        public int maxUnitSamples;

        /// <summary>Longest side of the territory raster, in cells. The cell
        /// starts at the 2 m build cell and doubles until the map fits.</summary>
        public int partitionMaxCells;
    }
}
