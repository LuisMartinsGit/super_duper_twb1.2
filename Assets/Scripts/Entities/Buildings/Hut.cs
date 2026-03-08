// File: Assets/Scripts/Entities/Buildings/Hut.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Hut building - housing structure.
    /// Provides population capacity only (no resource generation).
    /// </summary>
    public static class Hut
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 600f;
        private const float DefaultLoS = 14f;
        private const float DefaultRadius = 1.6f;
        private const int DefaultPopulation = 10;
        private const int PresentationID = 102;

        /// <summary>
        /// Create Hut using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Hut", out var def))
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
                typeof(HutTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(PopulationProvider)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hut");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new PopulationProvider { Amount = DefaultPopulation });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Hut using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Hut", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<HutTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hut");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new PopulationProvider { Amount = DefaultPopulation });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
    // HutTag is defined in BuildingComponents.cs (global namespace)
}