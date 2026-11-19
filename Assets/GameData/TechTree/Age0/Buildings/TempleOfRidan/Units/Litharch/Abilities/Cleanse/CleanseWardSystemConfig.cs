using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// The Cleansing Ward's numbers (Territory_Claims.md §6.9.7). The asset is
    /// CleanseWardSystem.asset, beside CleanseWardSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else. The
    /// circle's radii and growth are the Cleanse CARD's (Cleanse.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Border/CleanseWardSystem",
                     fileName = "CleanseWardSystem")]
    public sealed class CleanseWardSystemConfig : ScriptableObject, IComponentConfig
    {
        static CleanseWardSystemConfig _i;
        public static CleanseWardSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<CleanseWardSystemConfig>());

        /// <summary>Seconds between pulses: each one clears and grows the
        /// ground circle (the aura grows with it).</summary>
        public float pulseSeconds;

        // ── How many, how close (2026-10-22, the Field Hospital's rule) ──
        /// <summary>Most wards a team (a faction and its allies) may have
        /// standing at once; a cast past the cap is refused (0 = no cap).</summary>
        public int capPerTeam;
        /// <summary>Least distance (m) between two of the team's wards; a cast
        /// closer than this to one is refused.</summary>
        public float minSpacingMetres;

        // ── The ward as a building ──
        public int maxHealth;
        public float bodyRadius;
        /// <summary>The ward's side in 2 m build cells: the footprint it snaps
        /// to and is placed by (2026-11-06).</summary>
        public int footprintCells;
        public float sight;
        public int defenseMelee;
        public int defenseRanged;
        public int defenseSiege;
        public int defenseMagic;

        // ── Presentation ──
        /// <summary>The ward's model (Waning Border > Art > Build Cleansing Ward Prefab).</summary>
        public GameObject wardPrefab;
        /// <summary>The ward's calm blue aura (CleanseWard_Aura.prefab, a blue
        /// copy of the Field Hospital's healing-area loop, 2026-11-18): kept
        /// looping on every standing ward, scaled to its clean circle's radius
        /// over <see cref="auraAuthoredRadius"/>, and played once where a
        /// Cleanse Pulse lands. No burst on the ward's pulses.</summary>
        public GameObject auraPrefab;
        /// <summary>The radius (m) the aura prefab is authored at.</summary>
        public float auraAuthoredRadius;
        /// <summary>Seconds a ward's aura takes to fade once the ward is gone.</summary>
        public float auraFadeSeconds;
        /// <summary>How often the presentation polls the wards (aura on / off / resize).</summary>
        public float pollSeconds;
    }
}
