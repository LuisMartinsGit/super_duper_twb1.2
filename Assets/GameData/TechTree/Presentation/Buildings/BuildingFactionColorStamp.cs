// BuildingFactionColorStamp.cs
// The owning player's color, recorded on a building visual root the first time
// BuildingFactionColorMarker.Apply runs on it.
//
// Why a component rather than a static map: a building's visual is re-created
// (prefab-swap upgrades) and re-parented (multi-variant branch switches), and
// several passes legitimately take its materials away for a while — the
// level-up dissolve binds its own instances, the battle-damage shader replaces
// every slot on first hit. Any of those can end with the authored blue back on
// screen. Keeping the color ON the GameObject lets whoever finishes rewriting
// the materials call BuildingFactionColorMarker.Reapply(go) and put the team
// color back, without an ECS lookup and without knowing which faction owns it.
//
// Location: Assets/GameData/TechTree/Presentation/Buildings/BuildingFactionColorStamp.cs

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Marker holding the faction color last applied to this building visual.
    /// Written by <see cref="BuildingFactionColorMarker.Apply"/>, read by
    /// <see cref="BuildingFactionColorMarker.Reapply"/>. Not authored on any
    /// prefab — always added at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingFactionColorStamp : MonoBehaviour
    {
        /// <summary>Owning player's pool color (never the culture tone).</summary>
        public Color Value = Color.white;
    }
}
