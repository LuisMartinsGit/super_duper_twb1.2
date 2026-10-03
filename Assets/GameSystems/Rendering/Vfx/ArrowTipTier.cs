// ArrowTipTier.cs
// Marks an arrow's tip effect (ArrowTrailTiers.ApplyArrow) with the tier it was
// made for and the edit version of the prefab it was copied from, so a pooled
// arrow re-dressed at the same tier keeps its effect — unless the prefab was
// edited since (Inspector, live), in which case it is rebuilt from the prefab.
// Presentation only.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class ArrowTipTier : MonoBehaviour
    {
        public ArrowTrailTier Tier;
        public int PrefabVersion;
    }
}
