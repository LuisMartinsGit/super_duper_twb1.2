// PresentationSpawnSystem.PlacementPreview.cs
// THE PLACEMENT GHOST IS THE FINISHED BUILDING, FROM ITS SO (2026-10-20,
// docs/Design/Art_Direction.md § 13). The ghost shows the model the building
// will take once it finishes for this faction: its completed culture, its
// faction-wide level of the type (FactionBuildingLevels, Overview.md §
// Building levels), a chapel's ChapelArt — ResolveFinishedVisual, the answer
// the plan it leaves behind shows too. Null when the SO has no model.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

public partial class PresentationSpawnSystem : MonoBehaviour
{
    /// <summary>
    /// A new, active instance of the look a <paramref name="buildingId"/>
    /// built now by <paramref name="faction"/> will have when it finishes.
    /// <paramref name="exactProcedural"/> is always false (no procedural
    /// art is left): the caller footprint-fits the instance.
    /// </summary>
    public static GameObject CreateFinishedBuildingPreview(EntityManager em, string buildingId,
        Faction faction, out bool exactProcedural)
    {
        exactProcedural = false;
        if (!ResolveFinishedVisual(em, buildingId, faction, out var prefab, out string missing))
        {
            ReportMissing(missing);
            return null;
        }
        var go = Instantiate(prefab);
        go.SetActive(true);
        StripThirdPartyControllers(go);
        return go;
    }

    /// <summary>
    /// The root scale and pivot offset the ghost of <paramref name="buildingId"/>
    /// takes — exactly what the finished building's view gets: the ladder's
    /// top level sets both (FootprintFit.cs), so the ghost, the site and
    /// every level stand at one scale.
    /// </summary>
    public static float ComputeFinishedViewScale(EntityManager em, string buildingId, Faction faction,
        GameObject preview, float width, float height, out Vector3 centreOffset)
    {
        ResolveFinishedVisual(em, buildingId, faction, out var prefab, out _);
        byte culture = CultureConfig.GetCompletedCulture(em, faction);
        return ComputeBuildingViewScale(preview, prefab, buildingId, culture, width, height,
                                        out _, out centreOffset);
    }
}
