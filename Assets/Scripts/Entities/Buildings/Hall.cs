// File: Assets/Scripts/Entities/Buildings/Hall.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Hall building - main base structure.
    /// Trains Builders, generates Supplies, provides population.
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Hall
    {
        private const float DefaultHP = 2400f;
        private const float DefaultLoS = 35f;
        private const float DefaultSuppliesPerTick = 50f; // 50 supplies every 15 seconds
        private const float DefaultSuppliesInterval = 15f;
        private const float DefaultRadius = 2.0f;
        private const int DefaultPopulation = 20;
        public const int PresentationID = 100;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Hall", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 1 }); // Main base
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new SuppliesIncome { PerTick = DefaultSuppliesPerTick, Interval = DefaultSuppliesInterval });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hall");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            creator.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            creator.AddComponent(entity, new PopulationProvider { Amount = DefaultPopulation });
            creator.AddComponent(entity, new FactionProgress { Culture = Cultures.None });

            // Training queue buffer + rally point + ranged attack
            creator.AddBuffer<TrainQueueItem>(entity);
            creator.AddComponent<HallTag>(entity);
            creator.AddComponent(entity, new RallyPoint { Position = position + new float3(5f, 0, 5f), Has = 1 });
            creator.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 20f, Damage = 12, Cooldown = 2.5f, Timer = 0f, MaxTargets = 1
            });

            // Combat type tags
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });

            // Research capability (Hall can research economy techs)
            creator.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ResearchQueueItem>(entity);

            return entity;
        }
    }
}
