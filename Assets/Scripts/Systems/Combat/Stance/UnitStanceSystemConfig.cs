using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Stance tunables for <see cref="UnitStanceSystem"/> — docs/Design/Stances.md
    /// §8. The asset is UnitStanceSystem.asset, beside UnitStanceSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Combat/UnitStanceSystem",
                     fileName = "UnitStanceSystem")]
    public sealed class UnitStanceSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>How far (m) an AGGRESSIVE unit chases an auto-acquired
        /// target from its guard point before dropping it and walking home.
        /// Also the leash for every attack-move / patrol engagement, measured
        /// from where the unit acquired.</summary>
        public float aggressiveLeash;

        /// <summary>A DEFENSIVE (or support) unit returns fire on an enemy
        /// that hit it within this long (s) — as long as that enemy is inside
        /// its own attack reach. It never moves to do so.</summary>
        public float retaliationWindow;

        /// <summary>After a leash breaks, no auto-acquisition for this long
        /// (s), so a fleeing target cannot tow the unit straight back out.</summary>
        public float leashReacquireCooldown;

        /// <summary>A unit hit within this long (s) counts as under fire: a
        /// marching attack-move formation member fights back.</summary>
        public float underFireWindow;
    }
}
