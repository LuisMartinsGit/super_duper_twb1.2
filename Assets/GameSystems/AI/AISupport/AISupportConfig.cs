using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AISupport"/> (the AI's Litharch healers,
    /// Game_AI.md § 6m). The asset is AISupport.asset, beside AISupport.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AISupport",
                     fileName = "AISupport")]
    public sealed class AISupportConfig : ScriptableObject, IComponentConfig
    {
        static AISupportConfig _i;
        public static AISupportConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AISupportConfig>());

        /// <summary>Off: no Litharchs trained or sent out by the AI.</summary>
        public bool enabled;

        /// <summary>Seconds between support decisions per faction.</summary>
        public float thinkInterval;

        /// <summary>One healer per this many combat units.</summary>
        public int combatUnitsPerHealer;

        /// <summary>Never more healers than this.</summary>
        public int maxHealers;

        /// <summary>A healer further than this from the army it follows is
        /// walked to it, metres; inside it, it is left alone so its own
        /// auto-heal search (which a move order cancels) does the work.</summary>
        public float followDistance;

        /// <summary>How far behind the army's centre (toward home) a healer
        /// is walked, metres.</summary>
        public float followBehind;

        /// <summary>The army a healer follows has at least this many members.</summary>
        public int minArmyToFollow;
    }
}
