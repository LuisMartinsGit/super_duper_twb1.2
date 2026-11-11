// Market.cs
// THE MARKET (Alanthor, Age 1 — docs/Design/Age_1_Alanthor.md § The Market,
// Unification decision 55 (d)). One per player (Market.asset maxPerFaction,
// BuildingFactory.AtFactionCap). The player sets how much iron and how much
// veilstone it buys a minute (MarketRates, set by the SetMarketRate command);
// MarketSystem pays supplies for them at the FIXED prices on Market.asset
// (suppliesPerIron, suppliesPerVeilstone). The price never moves with the
// nodes a player holds, or with anything else. When the bank cannot pay for
// a resource, none of it is bought that second — no debt.
//
// Every number is on Market.asset. Its model is the retired Trading
// Outpost's art (Market/Art/), level 1.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;

namespace TheWaningBorder.Entities
{
    public static class Market
    {
        /// <summary>The Market's building id (Market.asset).</summary>
        public const string BuildingId = "Alanthor_Market";
        /// <summary>After the Farm (512); its SO says the same.</summary>
        public const int PresentationID = 513;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(MarketTag));
            EntityBuild.Add(em, entity, default(MarketRates));
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(MarketTag));
            EntityBuild.Add(ecb, entity, default(MarketRates));
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return EntityBuild.Done(entity);
        }

        private static BuildingDef Def => TechCatalog.Building(BuildingId);

        /// <summary>Supplies for one iron (Market.asset).</summary>
        public static int SuppliesPerIron => Def.suppliesPerIron;
        /// <summary>Supplies for one veilstone (Market.asset).</summary>
        public static int SuppliesPerVeilstone => Def.suppliesPerVeilstone;
        /// <summary>Highest rate per resource, a minute. NO CEILING (decision 56):
        /// the bank is the only limit, so this is only the bound that keeps a
        /// rate inside the lockstep command's integer field. Not tuning.</summary>
        public const int MaxRate = 1000000;
        /// <summary>One click of a rate button, a minute (Market.asset).</summary>
        public static int RateStep => math.max(1, Def.rateStep);

        /// <summary>A requested rate made legal: 0..MaxRate, on the step.</summary>
        public static int ClampRate(int perMinute)
        {
            int step = RateStep;
            int r = math.clamp(perMinute, 0, MaxRate);
            return r / step * step;
        }

        /// <summary>Supplies a minute the rates cost at the fixed prices.</summary>
        public static int SuppliesCostPerMinute(int ironPerMinute, int veilstonePerMinute)
            => ironPerMinute * SuppliesPerIron + veilstonePerMinute * SuppliesPerVeilstone;

        /// <summary>The faction's Market (finished or not; the lowest entity
        /// index if there were ever two), or Null.</summary>
        public static Entity Of(EntityManager em, Faction faction)
        {
            var q = QC_Markets.Get(em, QT_Markets);
            if (q.IsEmptyIgnoreFilter) return Entity.Null;
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction && (best == Entity.Null || ents[i].Index < best.Index))
                    best = ents[i];
            return best;
        }

        static readonly ComponentType[] QT_Markets =
        {
            ComponentType.ReadOnly<MarketTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Markets;

        /// <summary>
        /// The executor of the rate setting (every peer runs it off the wire,
        /// CommandRouter.SetMarketRateDirect): clamps both rates to the SO's
        /// range and step and stores them. The carries are kept.
        /// </summary>
        public static void SetRates(EntityManager em, Entity market, int ironPerMinute, int veilstonePerMinute)
        {
            if (market == Entity.Null || !em.Exists(market) || !em.HasComponent<MarketRates>(market)) return;
            var r = em.GetComponentData<MarketRates>(market);
            r.IronPerMinute = ClampRate(ironPerMinute);
            r.VeilstonePerMinute = ClampRate(veilstonePerMinute);
            em.SetComponentData(market, r);
        }
    }
}
