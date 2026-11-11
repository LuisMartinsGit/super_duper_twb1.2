using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// The Farm's level-3 low stone wall (docs/Design/Age_1_Alanthor.md § The
    /// level-3 wall; decision 57 (f)). The asset is FarmStoneWallSystem.asset,
    /// beside FarmStoneWallSystem.cs. No field initialisers: the values live in
    /// the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Farm Stone Wall",
                     fileName = "FarmStoneWallSystem")]
    public sealed class FarmStoneWallSystemConfig : ScriptableObject, IComponentConfig
    {
        static FarmStoneWallSystemConfig _i;
        public static FarmStoneWallSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<FarmStoneWallSystemConfig>());

        // ── One piece ──
        /// <summary>Hit points of one piece.</summary>
        public int maxHealth;
        public int defenseMelee;
        public int defenseRanged;
        public int defenseSiege;
        public int defenseMagic;
        /// <summary>The wall's thickness, metres: the strip it blocks for movement
        /// (never thinner than a nav cell's diagonal, so no unit slips through).</summary>
        public float thicknessMeters;

        // ── Rebuilding ──
        /// <summary>A destroyed piece comes back this long after it fell, seconds…</summary>
        public float rebuildSeconds;
        /// <summary>…once nothing has damaged the Farm or any of its pieces for this long, seconds.</summary>
        public float outOfCombatSeconds;

        // ── Layout (FarmBoundaryLayout) ──
        /// <summary>Length of one piece along the line, metres.</summary>
        public float pieceLength;
        /// <summary>The rim runs this far inside the ring's outer circle (a
        /// neighbour's rim this far outside its circle), metres.</summary>
        public float inset;
        /// <summary>How far behind a piece the "does crop grow here" probe looks, metres.</summary>
        public float cropProbe;
        /// <summary>A piece within this distance of a road of the simulation road
        /// graph (either side of the straight line between its two nodes) is an
        /// opening, metres.</summary>
        public float roadOpeningHalfWidth;
        /// <summary>Width of the opening on the rim facing the Farm's door, metres.</summary>
        public float doorOpeningMeters;
        /// <summary>The door faces the Farm's local +Z turned by this many degrees
        /// (the fence and the wall both read it).</summary>
        public float doorYawOffsetDegrees;

        // ── Presentation ──
        /// <summary>Synty low stone wall pieces; each wall piece shows one,
        /// picked by where it stands.</summary>
        public GameObject[] piecePrefabs;
        /// <summary>Height each piece's art is fitted to, metres.</summary>
        public float pieceHeight;
        /// <summary>How often a piece's view checks whether its Farm is shown, seconds.</summary>
        public float viewPollSeconds;
    }
}
