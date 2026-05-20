// File: Assets/Scripts/Presentation/BuildingVisualSinkDepth.cs
//
// Tag set on a building's visual root carrying the depth the building
// must sink to be fully underground at construction-progress = 0. Computed
// once from renderer bounds when the visual is finalised, so SyncTransforms
// doesn't have to walk the hierarchy every frame and the rise animation
// uses the actual silhouette instead of the entity's logical Radius.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class BuildingVisualSinkDepth : MonoBehaviour
    {
        public float Value;
    }
}
