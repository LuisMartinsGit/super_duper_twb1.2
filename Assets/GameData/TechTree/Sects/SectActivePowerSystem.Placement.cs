// SectActivePowerSystem.Placement.cs
// The sect powers that RAISE A BUILDING are placed like a building
// (docs/Design/Build_Grid.md § Buildings raised by abilities, 2026-11-11):
// Fortify (RaiseTower: the Fortitude ★, Tower / Fortification / Fortress by
// level) and Plenty (the Prosperity ★, a Counting House). The aim is snapped to
// the 2 m grid by the structure's own footprint (its SO's footprintCells, or
// CountingHouse.asset), and the snapped site must pass the placement rules —
// asked by the ghost (ReligionPanelBinder), the AI's pickers, Fire before the
// cooldown is charged and the strike again when it lands, so a refused cast
// spends nothing and the structure stands exactly on its ghost.
//
// Rules per power (the design text each answers to):
//   Fortify — "a point on legal build ground" (Sects.md): every rule a worker's
//             building answers to, own territory included.
//   Plenty  — "on ground a building could stand on" (Sects.md): the crust and
//             geometry rules, no territory gate, and never on another house.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Sect
{
    public static partial class SectActivePowerHelper
    {
        /// <summary>True for a power whose effect is a building on the aim.</summary>
        public static bool RaisesBuilding(SectActivePowerKind kind)
            => kind == SectActivePowerKind.RaiseTower || kind == SectActivePowerKind.Plenty;

        /// <summary>The building id Fortify raises at this magnitude (its level).</summary>
        public static string FortifyBuildingId(float magnitude)
        {
            int level = math.max(1, (int)magnitude);
            return level switch
            {
                1 => RenewalTower.Id,
                2 => RenewalFortification.Id,
                _ => RenewalFortress.Id,
            };
        }

        /// <summary>The footprint (metres) of what <paramref name="kind"/> raises.</summary>
        public static int2 RaisedFootprint(SectActivePowerKind kind, float magnitude)
            => kind == SectActivePowerKind.Plenty
                ? CountingHouse.SizeMeters
                : BuildingSizeConfig.GetSize(FortifyBuildingId(magnitude));

        /// <summary>Where the structure aimed at <paramref name="aim"/> stands.</summary>
        public static float3 RaisedSite(SectActivePowerKind kind, float magnitude, float3 aim)
            => kind == SectActivePowerKind.Plenty
                ? CountingHouse.SiteFor(aim)
                : AbilityBuildingSite.Snap(aim, RaisedFootprint(kind, magnitude));

        /// <summary>
        /// Null when the structure may stand at the SNAPPED <paramref name="site"/>,
        /// else the reason. The one test of a raised building's site.
        /// </summary>
        public static string RaisedSiteRefusal(EntityManager em, Faction faction, SectActivePowerKind kind,
            float magnitude, float3 site)
        {
            if (kind == SectActivePowerKind.Plenty)
                return CountingHouse.SiteRefusal(em, faction, site);
            string id = FortifyBuildingId(magnitude);
            return AbilityBuildingSite.Refusal(em, faction, site, BuildingSizeConfig.GetSize(id), id,
                AbilityBuildingSite.Ground.OwnTerritory);
        }
    }
}
