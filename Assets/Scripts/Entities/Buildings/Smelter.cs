// File: Assets/Scripts/Entities/Buildings/Smelter.cs
// Alanthor Smelter (Forge) — converts iron + crystal into veilsteel.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Alanthor Smelter building — local storage for iron/crystal,
    /// converts 5 iron + 3 crystal into 1 veilsteel every 5 seconds.
    /// Miners assigned to the smelter fetch resources from GathererHut/Hall.
    /// </summary>
    public static class Smelter
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 1000f;
        private const float DefaultLoS = 14f;
        private const float DefaultRadius = 1.5f;
        private const float DefaultBuildTime = 30f;
        public const int PresentationID = 560;

        // Forge storage defaults
        private const int DefaultMaxIron = 100;
        private const int DefaultMaxCrystal = 50;

        /// <summary>
        /// Create completed Smelter using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Smelter", out var def))
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
                typeof(SmelterTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(ForgeStorage)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Smelter");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new ForgeStorage
            {
                Iron = 0,
                Crystal = 0,
                MaxIron = DefaultMaxIron,
                MaxCrystal = DefaultMaxCrystal,
                ConversionTimer = 0f
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Smelter using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Smelter", out var def))
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
            ecb.AddComponent(entity, new SmelterTag());
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Smelter");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new ForgeStorage
            {
                Iron = 0,
                Crystal = 0,
                MaxIron = DefaultMaxIron,
                MaxCrystal = DefaultMaxCrystal,
                ConversionTimer = 0f
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Smelter under construction using EntityCommandBuffer.
        /// </summary>
        public static Entity CreateUnderConstruction(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;
            float buildTime = DefaultBuildTime;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Smelter", out var def))
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
            ecb.AddComponent(entity, new SmelterTag());
            ecb.AddComponent(entity, new Health { Value = 1, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Smelter");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new UnderConstruction { Progress = 0f, Total = buildTime });
            ecb.AddComponent(entity, new Buildable { BuildTimeSeconds = buildTime });
            ecb.AddComponent(entity, new ForgeStorage
            {
                Iron = 0,
                Crystal = 0,
                MaxIron = DefaultMaxIron,
                MaxCrystal = DefaultMaxCrystal,
                ConversionTimer = 0f
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
