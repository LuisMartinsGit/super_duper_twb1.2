using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIShardroot"/> (the AI going for the
    /// Shardroot and bringing it home, Game_AI.md § 6l). The asset is
    /// AIShardroot.asset, beside AIShardroot.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIShardroot",
                     fileName = "AIShardroot")]
    public sealed class AIShardrootConfig : ScriptableObject, IComponentConfig
    {
        static AIShardrootConfig _i;
        public static AIShardrootConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIShardrootConfig>());

        /// <summary>Off: the AI ignores the Shardroot (the pre-2026-10-07
        /// behaviour).</summary>
        public bool enabled;

        /// <summary>Seconds between Shardroot decisions per faction.</summary>
        public float thinkInterval;

        /// <summary>Match seconds before the first strike for it.</summary>
        public float earliestSeconds;

        /// <summary>Radius round the artifact whose defenders are weighed
        /// against the strike party, metres.</summary>
        public float assessRadius;

        /// <summary>The party's power must be at least this many times the
        /// defenders' before it goes.</summary>
        public float launchMargin;

        /// <summary>Fewest units a strike party is sent with.</summary>
        public int minPartySize;

        /// <summary>A strike under way is reinforced with newly free units
        /// for this long, seconds; after that the next think plans afresh.</summary>
        public float reinforceSeconds;

        /// <summary>The king goes with the strike and is walked onto the
        /// artifact himself (the hero has right of way on the pickup).</summary>
        public bool kingLeadsStrike;

        /// <summary>An idle King Lexor rides with the army's biggest mission
        /// instead of waiting at home, so his fight abilities find fights.</summary>
        public bool kingJoinsArmy;

        /// <summary>The king joins a mission only once it has at least this
        /// many members.</summary>
        public int kingJoinMinArmy;

        /// <summary>King Lexor is pulled out of the fight to the nearest Hall
        /// when his health falls to this fraction of his max (2026-10-09:
        /// the AI threw him away, and the Shardroot with him). 0 = never.</summary>
        public float kingRetreatHpFraction;

        /// <summary>...and stays out until he has healed back to this
        /// fraction.</summary>
        public float kingRecoveredHpFraction;
    }
}
