// High-ground rule for ranged units (directive 2026-07-04):
// shooting a target BELOW you grants more range and more damage; shooting a
// target ABOVE you costs range and damage. Computed PER SHOT from the raw
// height difference between shooter and target.

using Unity.Mathematics;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Shared height-advantage multiplier for ranged combat. One multiplier
    /// serves both range and damage: 4 % per meter of height difference
    /// (shooter minus target), clamped to plus/minus 20 % (reached at 5 m).
    /// Used by RangedCombatSystem (every ArcherState unit — Archer,
    /// Crossbowmen, Longbowman, Ballista) and VeilstingerCombatSystem so the
    /// rule is symmetric between players and the Border. Deterministic —
    /// pure math on lockstep-simulated positions.
    /// </summary>
    public static class HeightAdvantage
    {
        /// <summary>Multiplier change per meter of height difference.</summary>
        public const float PerMeter = 0.04f;

        /// <summary>Cap on the bonus/penalty (0.20 = plus/minus 20 %).</summary>
        public const float MaxEffect = 0.20f;

        /// <summary>
        /// Range/damage multiplier for a shot from <paramref name="shooterY"/>
        /// at <paramref name="targetY"/>. Higher shooter → &gt; 1, lower → &lt; 1.
        /// </summary>
        public static float Multiplier(float shooterY, float targetY)
        {
            float effect = (shooterY - targetY) * PerMeter;
            return 1f + math.clamp(effect, -MaxEffect, MaxEffect);
        }

        /// <summary>Apply the height multiplier to a damage value (min 1).</summary>
        public static int ScaleDamage(int baseDamage, float shooterY, float targetY)
        {
            return math.max(1, (int)math.round(baseDamage * Multiplier(shooterY, targetY)));
        }
    }
}
