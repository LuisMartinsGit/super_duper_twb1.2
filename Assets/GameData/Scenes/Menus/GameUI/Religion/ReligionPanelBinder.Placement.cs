// ReligionPanelBinder.Placement.cs
// Casting a sect power that RAISES A BUILDING — Fortify (Tower / Fortification /
// Fortress) and Plenty (a Counting House) — is building placement
// (docs/Design/Build_Grid.md § Buildings raised by abilities, 2026-11-11): the
// structure's ghost on the 2 m grid, its footprint outline, white or red by the
// simulation's own test on the snapped site (SectActivePowerHelper
// .RaisedSiteRefusal, re-run by Fire before the cooldown), and a refused click
// that names the rule and keeps placing. The command carries the snapped site.

using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.UI.World;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        private static readonly Color RaisedRingColor = new Color(0.96f, 0.77f, 0.32f, 0.6f);

        /// <summary>Begin placing the building <paramref name="spec"/> raises;
        /// false when the power raises none.</summary>
        private static bool TryBeginRaisedPlacement(string sectId, int slot, SectActivePowerSpec spec, Faction faction)
        {
            if (!SectActivePowerHelper.RaisesBuilding(spec.Kind)) return false;

            var kind = spec.Kind;
            float magnitude = spec.Magnitude;
            int2 size = SectActivePowerHelper.RaisedFootprint(kind, magnitude);
            GameObject ghost;
            float ring;
            if (kind == SectActivePowerKind.Plenty)
            {
                var cfg = CountingHouseConfig.I;
                ghost = AbilityBuildingGhost.ForModel(cfg.modelPrefab, cfg.modelScale, size);
                ring = cfg.captureRadius;              // the ring it is captured in
            }
            else
            {
                string id = SectActivePowerHelper.FortifyBuildingId(magnitude);
                ghost = AbilityBuildingGhost.ForBuilding(id);
                ring = TechCatalog.TryGetBuilding(id, out var def) && def.attack != null && def.attack.range > 0f
                    ? def.attack.range                 // the ground it will shoot over
                    : math.max(size.x, size.y) * 0.5f;
            }

            GroundPlacement.Begin(ghost, size, ring, RaisedRingColor, 0f,
                () => float3.zero,                     // no caster to walk off: a sect power has no reach
                (float3 cursor, out float3 site) =>
                {
                    site = SectActivePowerHelper.RaisedSite(kind, magnitude, cursor);
                    var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (w == null || !w.IsCreated) return null;
                    var em = w.EntityManager;
                    // Fire's order: the site, then the sight of its centre.
                    string why = SectActivePowerHelper.RaisedSiteRefusal(em, faction, kind, magnitude, site);
                    if (why != null) return why;
                    return SectActivePowerHelper.AimInSight(em, faction, kind, site)
                        ? null
                        : Loc.T(TheWaningBorder.Abilities.CastTargetRules.Describe(
                            TheWaningBorder.Abilities.CastTargetRefusal.NotInSight,
                            TheWaningBorder.Abilities.CastTargetKind.None));
                },
                site =>
                {
                    var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (w == null || !w.IsCreated) return;
                    CommandRouter.IssueSectPower(w.EntityManager, faction, sectId, slot, site);
                });
            return true;
        }
    }
}
