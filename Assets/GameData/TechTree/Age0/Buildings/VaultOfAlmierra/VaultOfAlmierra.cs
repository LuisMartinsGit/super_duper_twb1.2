// Vault of Almierra — Age 0 choice building (interest on banked supplies).
//
// Extracted from BuildingFactory (2026-08-12): each building's creation
// code lives with its data, per the TechTree co-location convention.
// BuildingFactory keeps only the id -> recipe dispatch.

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    public static class VaultOfAlmierra
    {
        /// <summary>
        /// Create Vault of Almiérra — simple, capped-interest resource storage.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("VaultOfAlmierra");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(em, 
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius)
            );

            EntityBuild.Add(em, entity, new PresentationId { Id = 530 });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("VaultOfAlmierra");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            EntityBuild.Add(em, entity, default(VaultTag));
            EntityBuild.Add(em, entity, default(ChoiceBuildingTag));
            EntityBuild.Add(em, entity, new VaultStorage
            {
                ResourceType = 0,
                StoredAmount = 0f,
                Principal = 0f,
                InterestRate = def.interestPerMinute,   // the SO (VaultOfAlmierra.asset)
                LockTimer = 0f,
                LockDuration = 180f
            });

            // Banking tech ladder (interest grades + resource unlocks) researches here.
            EntityBuild.Add(em, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(em, entity);

            // Simple upgrade ladder (interest yields + wall productivity).
            EntityBuild.Add(em, entity, default(BuildingUpgradeable));

            // Combat type tags
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return EntityBuild.Done(entity);
        }

        /// <summary>
        /// Create Vault of Almierra using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("VaultOfAlmierra");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(ecb);

            EntityBuild.Add(ecb, entity, new PresentationId { Id = 530 });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("VaultOfAlmierra");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            EntityBuild.Add(ecb, entity, default(VaultTag));
            EntityBuild.Add(ecb, entity, default(ChoiceBuildingTag));
            EntityBuild.Add(ecb, entity, new VaultStorage
            {
                ResourceType = 0,
                StoredAmount = 0f,
                Principal = 0f,
                InterestRate = def.interestPerMinute,   // the SO (VaultOfAlmierra.asset)
                LockTimer = 0f,
                LockDuration = 180f
            });

            // Banking tech ladder (interest grades + resource unlocks) researches here.
            EntityBuild.Add(ecb, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, entity);

            // Simple upgrade ladder (interest yields + wall productivity).
            EntityBuild.Add(ecb, entity, default(BuildingUpgradeable));

            // Combat type tags
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return EntityBuild.Done(entity);
        }
    }
}
