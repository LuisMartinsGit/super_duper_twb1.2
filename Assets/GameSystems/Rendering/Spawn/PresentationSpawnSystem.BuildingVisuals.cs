// PresentationSpawnSystem.BuildingVisuals.cs
// A PLAYER BUILDING'S VISUAL COMES FROM ITS SO, AND ONLY FROM ITS SO
// (2026-10-20, docs/Design/Art_Direction.md § 13). Developer: "presentation
// layer should ONLY accept the prefabs through the SO, no exception. if
// something breaks, author a new SO, FBX and .blend."
//
//   a plan (and the placement ghost)       -> the FINISHED look: completed
//                                              culture, the faction's level
//                                              of the type (ResolveFinishedVisual)
//   level 0 / under construction           -> the BuildingDefSO's prefab
//   level N with a culture                  -> that culture's BuildingLevelDefSO prefab
//   no level ladder, culture complete       -> the BuildingDefSO's culturePrefabs entry
//   a sect chapel                           -> ChapelArt.asset (chapels are not catalog buildings)
//
// No multi-variant branches, no procedural builders, no Resources ladder.
// A building whose SO has no model is a DATA bug: logged once per SO, drawn
// as the fallback cube so it is impossible to miss. Walls, the curse's nodes
// and the map's resource nodes are not player buildings and keep their own
// paths (the wall phase follows).
//
// BuildingPrefabSwapSystem keeps the view in step afterwards: each scan it
// resolves the same answer and replaces the view (dissolve) when it differs.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Rendering;

public partial class PresentationSpawnSystem : MonoBehaviour
{
    private static readonly HashSet<string> _missingModelLogged = new HashSet<string>();

    /// <summary>
    /// The model this player building should show right now, from its SO.
    /// False for anything that is not a player building (walls, curse
    /// nodes, map nodes) — the caller's other paths own those. A player
    /// building whose SO lacks its model answers true with a null prefab
    /// (and the data bug logged once).
    /// </summary>
    public static bool TryResolveBuildingVisual(EntityManager em, Entity e, out GameObject prefab)
    {
        prefab = null;
        if (!em.Exists(e)) return false;
        bool plan = em.HasComponent<PlannedBuilding>(e);
        if (!em.HasComponent<BuildingTag>(e) && !plan) return false;
        if (em.HasComponent<BorderTag>(e)) return false;                  // the curse's
        if (!em.HasComponent<FactionTag>(e)) return false;
        var faction = em.GetComponentData<FactionTag>(e).Value;
        if (faction == Faction.Border) return false;
        if (em.HasComponent<WallHubTag>(e) || em.HasComponent<WallSegmentTag>(e)
            || em.HasComponent<WallInstanceTag>(e) || em.HasComponent<WallTag>(e)
            || em.HasComponent<WallGateTag>(e) || em.HasComponent<WallTowerTag>(e))
            return false;                                                  // the wall set (its own phase)

        // A plan shows what it will become once built — the same answer the
        // placement ghost gave (docs/Design/Planned_Buildings.md: "the same
        // look as the placement ghost"). It used to show the level-0 base
        // form, so in Age 1 a Garrison ghost turned into an Age 0 Barracks
        // the moment it was placed. Breaking ground destroys the plan and
        // spawns a fresh site entity, which rises from the base form.
        if (plan)
        {
            string planId = em.GetComponentData<PlannedBuilding>(e).BuildingId.ToString();
            if (!ResolveFinishedVisual(em, planId, faction, out prefab, out string planMissing))
            {
                ReportMissing(planMissing);
                prefab = null;
            }
            return true;
        }

        if (em.HasComponent<ChapelTag>(e))
        {
            string sect = em.GetComponentData<ChapelTag>(e).SectId.ToString();
            prefab = ChapelArt.For(sect);
            if (prefab == null) ReportMissing("ChapelArt: " + sect);
            return true;
        }

        string id = TheWaningBorder.Entities.BuildingIds.Of(e, em);
        if (string.IsNullOrEmpty(id) && em.HasComponent<PresentationId>(e))
            id = TechCatalog.BuildingIdForPresentation(em.GetComponentData<PresentationId>(e).Id);
        if (string.IsNullOrEmpty(id) || !TechCatalog.TryGetBuilding(id, out _)) return false;

        byte culture = CultureConfig.GetCompletedCulture(em, faction);
        int level = 0;
        if (!plan && !em.HasComponent<UnderConstruction>(e) && culture != Cultures.None)
        {
            level = em.HasComponent<BuildingUpgradeState>(e)
                ? em.GetComponentData<BuildingUpgradeState>(e).Level
                : 1;                                                       // a ladderless building's culture look
        }
        string ladder = TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, e);
        if (!TechCatalog.TryGetBuildingVisual(id, ladder, culture, level, out prefab, out string missing))
        {
            ReportMissing(missing);
            prefab = null;
        }
        return true;
    }

    /// <summary>
    /// The model a <paramref name="buildingId"/> built now by
    /// <paramref name="faction"/> will show once it finishes: its completed
    /// culture and the faction-wide level of the type (FactionBuildingLevels;
    /// L1 when none is recorded, a ladderless building's culture look at 1),
    /// a chapel's ChapelArt. The placement ghost and the plan both use it.
    /// </summary>
    public static bool ResolveFinishedVisual(EntityManager em, string buildingId, Faction faction,
        out GameObject prefab, out string missing)
    {
        prefab = null; missing = null;
        if (string.IsNullOrEmpty(buildingId)) { missing = "(no building id)"; return false; }
        if (buildingId.StartsWith("Chapel_"))
        {
            string sect = buildingId.Substring("Chapel_".Length);
            prefab = ChapelArt.For(sect);
            if (prefab == null) missing = "ChapelArt: " + sect;
            return prefab != null;
        }
        byte culture = CultureConfig.GetCompletedCulture(em, faction);
        int level = 0;
        if (culture != Cultures.None)
            level = TechCatalog.HasBuildingLevels(culture, buildingId)
                ? Mathf.Max(1, (int)TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, faction, buildingId))
                : 1;
        return TechCatalog.TryGetBuildingVisual(buildingId, buildingId, culture, level, out prefab, out missing)
               && prefab != null;
    }

    private static void ReportMissing(string what)
    {
        if (string.IsNullOrEmpty(what) || !_missingModelLogged.Add(what)) return;
        Debug.LogError($"[BuildingVisual] '{what}' has no model on its SO — every player building's visual " +
                       "comes from its SO only (Art_Direction.md § 13). Author its SO, FBX and .blend.");
    }

    /// <summary>The prefab a building's view was built from (so the swap
    /// can tell whether the SO's answer changed).</summary>
    public sealed class BuildingVisualSource : MonoBehaviour
    {
        public GameObject Prefab;
    }

    /// <summary>Spawn a player building's view from its SO model.</summary>
    private GameObject SpawnBuildingFromSO(Entity entity, GameObject prefab, LocalTransform transform, Vector3 pos)
    {
        var src = prefab != null ? prefab : _fallbackBuildingPrefab;
        var go = Instantiate(src, pos, Quaternion.identity);
        go.SetActive(true);
        StripThirdPartyControllers(go);
        go.name = $"Entity_{entity.Index}_{src.name}";
        var ps = src.transform.localScale;
        float authoredScale = (ps.x + ps.y + ps.z) / 3f;
        float baseScale = authoredScale * ComputeFootprintFit(go, entity, out Vector3 fitOffset);
        if (baseScale > 0.001f)
        {
            var tag = go.GetComponent<ProceduralScaleTag>() ?? go.AddComponent<ProceduralScaleTag>();
            tag.BaseScale = baseScale;
            tag.AuthoredScale = authoredScale;
            tag.BaseOffset = fitOffset;
            go.transform.localScale = Vector3.one * transform.Scale * baseScale;
        }
        var finished = FinishProceduralBuilding(go, entity, transform);
        var source = finished.GetComponent<BuildingVisualSource>() ?? finished.AddComponent<BuildingVisualSource>();
        source.Prefab = prefab;
        // Raised already finished (a conjured building, a starting building,
        // a loaded save): no rise to play.
        if (!_em.HasComponent<UnderConstruction>(entity) && !_em.HasComponent<PlannedBuilding>(entity))
        {
            var rise = finished.GetComponent<BuildingRiseData>();
            if (rise != null) rise.ApplyRise(1f, 0f);
        }
        return finished;
    }
}
