using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Pasture — trains the Raider (L1) and War Chariot (L2).
    /// Design: docs/Design/Age_1_Feraldis.md (2026-08-05 rev.2).
    /// </summary>
    public static class Pasture
    {
        public const int PresentationID = 363;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            ReadDef(out float hp, out float los, out var defense);

            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius), typeof(ProductionState));
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Pasture");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(em, entity);
            EntityBuild.Add(em, entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            EntityBuild.Add(em, entity, default(PastureTag));
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(em, entity, defense);
            EntityBuild.Add(em, entity, default(BuildingUpgradeable));
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            ReadDef(out float hp, out float los, out var defense);

            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Pasture");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, entity);
            EntityBuild.Add(ecb, entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            EntityBuild.Add(ecb, entity, default(PastureTag));
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(ecb, entity, defense);
            EntityBuild.Add(ecb, entity, default(BuildingUpgradeable));
            return EntityBuild.Done(entity);
        }

        private static void ReadDef(out float hp, out float los, out Defense defense)
        {
            defense = new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            var def = TechCatalog.Building("Feraldis_Pasture");
            hp = def.hp;
            los = def.lineOfSight;
            if (def.defense != null)
                defense = new Defense
                {
                    Melee = def.defense.melee,
                    Ranged = def.defense.ranged,
                    Siege = def.defense.siege,
                    Magic = def.defense.magic,
                };
        }
    }
}
