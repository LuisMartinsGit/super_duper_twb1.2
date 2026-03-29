// File: Assets/Scripts/Entities/Buildings/Hall.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Hall building - main base structure.
    /// Trains Builders, generates Supplies, provides population.
    /// </summary>
    public static class Hall
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 2400f;
        private const float DefaultLoS = 35f;
        private const float DefaultSuppliesPerTick = 50f; // 50 supplies every 15 seconds
        private const float DefaultSuppliesInterval = 15f;
        private const float DefaultRadius = 2.0f;
        private const int DefaultPopulation = 20;
        private const int PresentationID = 100;

        /// <summary>
        /// Create Hall using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Hall", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(SuppliesIncome),
                typeof(TrainingState),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(PopulationProvider),
                typeof(FactionProgress)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 1 }); // Main base
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new SuppliesIncome { PerTick = 50f, Interval = 15f });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hall");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.SetComponentData(entity, new PopulationProvider { Amount = DefaultPopulation });
            em.SetComponentData(entity, new FactionProgress { Culture = Cultures.None });
            
            // Add training queue buffer + rally point
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponent<HallTag>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(5f, 0, 5f), Has = 1 });
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = 20f, Damage = 12, Cooldown = 2.5f, Timer = 0f, MaxTargets = 1
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });

            // Research capability (Hall can research economy techs)
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            return entity;
        }

        /// <summary>
        /// Create Hall using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Hall", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 1 }); // Main base
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new SuppliesIncome { PerTick = 50f, Interval = 15f });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hall");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent(entity, new PopulationProvider { Amount = DefaultPopulation });
            ecb.AddComponent(entity, new FactionProgress { Culture = Cultures.None });
            
            // Add training queue buffer
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent<HallTag>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(5f, 0, 5f), Has = 1 });
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 20f, Damage = 12, Cooldown = 2.5f, Timer = 0f, MaxTargets = 1
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });

            // Research capability (Hall can research economy techs)
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            return entity;
        }

    }
}