// EconomyPanel.Farms.cs
// The Farms and the Market in the Economy panel (decision 55,
// docs/Design/Age_1_Alanthor.md § The Farm / § The Market): income that comes
// from no territory, so it gets its own lines under the territories and is
// added to the total. A Farm row is what its Workers pay now
// (Farm.SuppliesPerMinute, the number FarmSystem pays) and their mean crop
// field (the free share of their rings, decision 57); the Market row is
// what it buys a minute and the supplies it spends for it (a negative
// supplies figure). Own buildings only. Presentation only.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.World;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class EconomyPanel
    {
        private static readonly ComponentType[] QT_Farms =
        {
            ComponentType.ReadOnly<FarmTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        private static CachedEntityQuery QC_Farms;

        /// <summary>Appends the Farms and Market lines (none when the player
        /// has neither) and adds them into <paramref name="total"/>. Returns
        /// the lines written.</summary>
        private int AppendFarmsAndMarket(EntityManager em, Faction me, ref TerritoryYield total)
        {
            int lines = 0;

            var q = QC_Farms.Get(em, QT_Farms);
            if (!q.IsEmptyIgnoreFilter)
            {
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                int farms = 0, workers = 0, supplies = 0, fieldSum = 0, fields = 0;
                for (int k = 0; k < ents.Length; k++)
                {
                    if (facs[k].Value != me) continue;
                    farms++;
                    workers += WallGarrison.OccupantCount(em, ents[k]);
                    supplies += Farm.SuppliesPerMinute(em, ents[k]);
                    int field = FarmRing.CachedFreePermille(em, ents[k]);
                    if (field >= 0) { fieldSum += field; fields++; }
                }
                if (farms > 0)
                {
                    var y = new TerritoryYield { Supplies = supplies };
                    Row(fields > 0
                            ? string.Format(Loc.T("Farms ({0}, {1} Workers, field {2}%)"), farms, workers,
                                fieldSum / fields / 10)
                            : string.Format(Loc.T("Farms ({0}, {1} Workers)"), farms, workers), y, false);
                    total.Supplies += supplies;
                    lines++;
                }
            }

            var market = Market.Of(em, me);
            if (market != Entity.Null && !em.HasComponent<UnderConstruction>(market)
                && em.HasComponent<MarketRates>(market))
            {
                var r = em.GetComponentData<MarketRates>(market);
                var y = new TerritoryYield
                {
                    Supplies = -Market.SuppliesCostPerMinute(r.IronPerMinute, r.VeilstonePerMinute),
                    Iron = r.IronPerMinute,
                    Veilstone = r.VeilstonePerMinute,
                };
                Row(Loc.T("Market"), y, false);
                total.Supplies += y.Supplies;
                total.Iron += y.Iron;
                total.Veilstone += y.Veilstone;
                lines++;
            }
            return lines;
        }
    }
}
