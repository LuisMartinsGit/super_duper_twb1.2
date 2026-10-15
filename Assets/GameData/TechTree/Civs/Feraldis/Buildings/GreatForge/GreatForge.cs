// GreatForge.cs
// The Feraldis Great Forge: creator + bay orders (add / recipe / upgrade).
// docs/Design/Age_1_Feraldis.md §4 (2026-10-12). The production cycle is
// GreatForgeSystem; every number is GreatForgeSystem.asset or the building SO.

using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Feraldis;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Entities
{
    public static class GreatForge
    {
        public const string BuildingId = "Feraldis_GreatForge";
        public const int PresentationID = 581;   // GreatForge.prefab on its SO (367 stays the retired Fiend Foundry's)

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var e = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(ProductionState));
            EntityBuild.Add(em, e, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, e, new FactionTag { Value = faction });
            EntityBuild.Add(em, e, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, e, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(em, e, new LineOfSight { Radius = def.lineOfSight });
            var grid = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(em, e, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(grid) });
            EntityBuild.Add(em, e, new BuildingSize { Width = grid.x, Height = grid.y });
            EntityBuild.Add(em, e, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(em, e);
            EntityBuild.Add(em, e, default(GreatForgeTag));
            EntityBuild.Add(em, e, default(BuildingUpgradeable));
            EntityBuild.Add(em, e, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(em, e, DefenseOf(def));
            EntityBuild.AddBuffer<ForgeBay>(em, e);
            EntityBuild.Done(e);   // one structural change, then seed
            SeedBays(em.GetBuffer<ForgeBay>(e));
            return e;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var e = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, e, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, e, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, e, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, e, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(ecb, e, new LineOfSight { Radius = def.lineOfSight });
            var grid = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(ecb, e, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(grid) });
            EntityBuild.Add(ecb, e, new BuildingSize { Width = grid.x, Height = grid.y });
            EntityBuild.Add(ecb, e, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, e);
            EntityBuild.Add(ecb, e, default(GreatForgeTag));
            EntityBuild.Add(ecb, e, default(BuildingUpgradeable));
            EntityBuild.Add(ecb, e, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(ecb, e, DefenseOf(def));
            EntityBuild.AddBuffer<ForgeBay>(ecb, e);
            EntityBuild.Done(e);   // the build's adds, then SetBuffer seeds
            SeedBays(ecb.SetBuffer<ForgeBay>(e));
            return e;
        }

        private static Defense DefenseOf(TheWaningBorder.Data.BuildingDef def)
            => def.defense == null ? new Defense { Melee = 1, Ranged = 1 }
               : new Defense { Melee = def.defense.melee, Ranged = def.defense.ranged,
                               Siege = def.defense.siege, Magic = def.defense.magic };

        private static void SeedBays(DynamicBuffer<ForgeBay> bays)
        {
            int n = Mathf.Max(0, GreatForgeSystem.Cfg.startingBays);
            for (int i = 0; i < n; i++) bays.Add(new ForgeBay { Recipe = ForgeRecipe.Hold, Level = 1 });
        }

        // ── bay orders (run on every peer via FeraldisOrders) ───────────────

        /// <summary>The price of the next bay (ramped by how many were already bought).</summary>
        public static Cost NextBayCost(EntityManager em, Entity forge)
        {
            var c = GreatForgeSystem.Cfg;
            int bought = em.GetBuffer<ForgeBay>(forge, true).Length - Mathf.Max(0, c.startingBays);
            float ramp = (c.bayCostRamp != null && c.bayCostRamp.Length > 0)
                ? c.bayCostRamp[Mathf.Clamp(bought, 0, c.bayCostRamp.Length - 1)] : 1f;
            return Cost.Of(supplies: Mathf.RoundToInt(c.baySupplies * ramp), iron: Mathf.RoundToInt(c.bayIron * ramp));
        }

        public static bool CanAddBay(EntityManager em, Entity forge)
            => em.HasBuffer<ForgeBay>(forge) && !em.HasComponent<UnderConstruction>(forge)
               && em.GetBuffer<ForgeBay>(forge, true).Length < GreatForgeSystem.Cfg.maxBays;

        public static void AddBay(EntityManager em, Entity forge)
        {
            if (!CanAddBay(em, forge)) return;
            var faction = em.GetComponentData<FactionTag>(forge).Value;
            if (!FactionEconomy.Spend(em, faction, NextBayCost(em, forge), SpendCategory.Buildings)) return;
            em.GetBuffer<ForgeBay>(forge).Add(new ForgeBay { Recipe = ForgeRecipe.Hold, Level = 1 });
        }

        public static void SetRecipe(EntityManager em, Entity forge, int bay, ForgeRecipe recipe)
        {
            if (!em.HasBuffer<ForgeBay>(forge)) return;
            var bays = em.GetBuffer<ForgeBay>(forge);
            if (bay < 0 || bay >= bays.Length || recipe > ForgeRecipe.ArmsExport) return;
            var b = bays[bay];
            if (b.Recipe == recipe) return;
            b.Recipe = recipe;
            b.Progress = 0f;
            bays[bay] = b;
        }

        public static bool TryUpgradeCost(EntityManager em, Entity forge, int bay, out Cost cost)
        {
            cost = default;
            if (!em.HasBuffer<ForgeBay>(forge)) return false;
            var bays = em.GetBuffer<ForgeBay>(forge, true);
            if (bay < 0 || bay >= bays.Length || bays[bay].Level >= 3) return false;
            var c = GreatForgeSystem.Cfg;
            int i = bays[bay].Level - 1;   // L1->L2 = index 0
            cost = Cost.Of(supplies: At(c.bayUpgradeSupplies, i), iron: At(c.bayUpgradeIron, i),
                           veilstone: At(c.bayUpgradeVeilstone, i));
            return true;
        }

        public static void UpgradeBay(EntityManager em, Entity forge, int bay)
        {
            if (!TryUpgradeCost(em, forge, bay, out var cost)) return;
            var faction = em.GetComponentData<FactionTag>(forge).Value;
            if (!FactionEconomy.Spend(em, faction, cost, SpendCategory.Upgrades)) return;
            var bays = em.GetBuffer<ForgeBay>(forge);
            var b = bays[bay];
            b.Level++;
            bays[bay] = b;
        }

        private static int At(int[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : 0;

        public static string RecipeName(ForgeRecipe r) => r switch
        {
            ForgeRecipe.Soulforge => "Soulforge",
            ForgeRecipe.Veilsteel => "Veilsteel",
            ForgeRecipe.ArmsExport => "Arms Export",
            _ => "Hold",
        };
    }
}
