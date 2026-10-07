using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World.Terrain
{
    /// <summary>
    /// The size of the clear ground around every player start
    /// (<see cref="StartClearing"/>, docs/Design/Territory_Claims.md §11).
    /// The asset is StartClearing.asset, beside StartClearing.cs.
    ///
    /// No field initialisers: the value lives in the asset and nowhere else.
    /// Read while the map is laid out on every lockstep peer, so every peer
    /// must load the SAME asset — it ships with the build like every other
    /// config.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Start Clearing",
                     fileName = "StartClearing")]
    public sealed class StartClearingConfig : ScriptableObject, IComponentConfig
    {
        // ── Radius of the clearing around each player start, in BUILD CELLS
        // (BuildGrid.CellSize metres each). No resource node stands in it,
        // and no impassable paint, forest stand or tree survives in it. ──
        public int radiusCells;
    }
}
