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
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Hut
    {
        private const float DefaultHP = 600f;
        private const float DefaultLoS = 14f;
        private const float DefaultRadius = 1.6f;
        private const int DefaultPopulation = 10;
        public const int PresentationID = 102;

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

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Hut", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent<HutTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hut");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            creator.AddComponent(entity, new PopulationProvider { Amount = DefaultPopulation });

            // Combat type tags
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
    // HutTag is defined in BuildingComponents.cs (global namespace)
}
