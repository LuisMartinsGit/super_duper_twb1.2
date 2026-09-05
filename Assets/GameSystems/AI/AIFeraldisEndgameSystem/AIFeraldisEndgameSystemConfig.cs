using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIFeraldisEndgameSystem"/>. The asset is AIFeraldisEndgameSystem.asset,
    /// beside AIFeraldisEndgameSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIFeraldisEndgameSystem",
                     fileName = "AIFeraldisEndgameSystem")]
    public sealed class AIFeraldisEndgameSystemConfig : ScriptableObject, IComponentConfig
    {
        static AIFeraldisEndgameSystemConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIFeraldisEndgameSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIFeraldisEndgameSystemConfig>());

        /// <summary>Builders a Feraldis AI keeps back for base expansion.
        /// Everyone else is a soldier.</summary>
        public int keepBuilders;

        /// <summary>Totems must tile new ground, not stack on the bloodiest
        /// cell. Comfortably wider than a totem's own burn radius.</summary>
        public float minTotemSpacing;

        public float thinkInterval;

        /// <summary>Raider Camps the AI wants standing — its entire economy.</summary>
        public int targetRaiderCamps;

        /// <summary>Army it wants before committing to a well assault.</summary>
        public int assaultArmySize;

        /// <summary>How far from the well the escort gathers.</summary>
        public float assaultRange;

        /// <summary>Units that must be moving on the well before the AI will
        /// commit a Corruptor to the walk. Below this it keeps the ritualist
        /// home rather than feeding it to the curse.</summary>
        public int minEscortBeforeDispatch;

        /// <summary>
        /// How long the AI will wait for that escort before going anyway.
        ///
        /// The escort gate was written when a lone ritualist crossing a map at
        /// 60-90 % curse died on the way. It has since become an ABSOLUTE
        /// block: the 2026-08-07 skirmish had Blue sit on `escort 0/4` for the
        /// last three minutes of the match with military 0 — an army it was
        /// never going to have, guarding a walk that is no longer dangerous
        /// (well dormancy holds that map at 1.6 % curse, so the route is
        /// empty). Waiting forever for an escort is strictly worse than an
        /// unescorted attempt on an uncontested well.
        ///
        /// So the gate becomes patience, not a veto: prefer an escort, but
        /// after this long, go.
        /// </summary>
        public float maxEscortWaitSeconds;
    }
}
