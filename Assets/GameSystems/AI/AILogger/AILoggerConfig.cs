using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Switches and numbers for <see cref="AILogger"/>. The asset is
    /// AILogger.asset, beside AILogger.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AILogger",
                     fileName = "AILogger")]
    public sealed class AILoggerConfig : ScriptableObject, IComponentConfig
    {
        static AILoggerConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AILoggerConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AILoggerConfig>());

        /// <summary>
        /// Write the per-unit "CMD" lines (one per AI move / attack /
        /// attack-move order). Off by default since 2026-09-25: they were the
        /// bulk of every AI log and of its main-thread cost, and the group
        /// orders (formation moves, waves, tactics) are still logged. Turn it
        /// on to debug a single unit's orders.
        /// </summary>
        public bool logUnitCommands;

        /// <summary>Seconds between buffered flushes to disk. A flush is a
        /// synchronous write on the main thread.</summary>
        public float flushIntervalSeconds;
    }
}
