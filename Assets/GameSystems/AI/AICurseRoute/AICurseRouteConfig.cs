using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AICurseRoute"/>. The asset is
    /// AICurseRoute.asset, beside AICurseRoute.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AICurseRoute",
                     fileName = "AICurseRoute")]
    public sealed class AICurseRouteConfig : ScriptableObject, IComponentConfig
    {
        static AICurseRouteConfig _i;
        public static AICurseRouteConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AICurseRouteConfig>());

        /// <summary>Off: armies march straight, through the curse if it is
        /// in the way.</summary>
        public bool enabled;

        /// <summary>Metres added to the cursed radius round each curse node
        /// when routing (a formation is wider than its centre line).</summary>
        public float marginMeters;

        /// <summary>The detour grid's cell, metres.</summary>
        public float cellMeters;

        /// <summary>An army is "at" a waypoint once its centre is this close,
        /// metres; then it marches on to the next.</summary>
        public float arriveMeters;
    }
}
