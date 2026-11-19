using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Tuning numbers for <see cref="MeleeCombatSystem"/>. The asset is
    /// MeleeCombatSystem.asset, beside MeleeCombatSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Simulation data — every lockstep peer loads the same asset.
    /// docs/Design/Combat_Pacing.md § Flanking.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Combat/MeleeCombatSystem",
                     fileName = "MeleeCombatSystem")]
    public sealed class MeleeCombatSystemConfig : ScriptableObject, IComponentConfig
    {
        static MeleeCombatSystemConfig _i;
        /// <summary>The one instance, for the ISystem that reads it.</summary>
        public static MeleeCombatSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<MeleeCombatSystemConfig>());

        /// <summary>Damage multiplier on a melee hit that lands outside the
        /// defender's front arc (side or rear — one arc, one bonus).</summary>
        public float flankDamageMultiplier;

        /// <summary>Half-width of the defender's front arc, in degrees either
        /// side of its facing. A hit from inside it (boundary included) is a
        /// normal hit; beyond it is a flank hit.</summary>
        public float frontArcHalfAngleDegrees;

        /// <summary>Seconds a unit stays planted after it starts a swing: it
        /// finishes the attack where it stands and does not chase a target
        /// that stepped away until this has run (never longer than its own
        /// attack cooldown). The presentation plays one attack clip over the
        /// same window.</summary>
        public float swingCommitSeconds;

        /// <summary>Reach, in BUILD-GRID CELLS from the building's edge, of a
        /// melee unit's thrown torch (decision 82; Combat_Pacing.md §
        /// Buildings). A melee unit with a torch attacks a building from
        /// anywhere inside it, so no attack slots apply.</summary>
        public float torchReachCells;
    }
}
