// File: Assets/Scripts/Entities/Buildings/RoyalStable.cs
// Alanthor Royal Stable — heavy-cavalry trainer (Cataphract, plus any
// future cavalry units listed in the TechTree's "trains" array).
//
// Mirrors Barracks.cs in shape: standard training building with a
// TrainQueueItem buffer, rally point, ranged-armor structure tag, and
// BuildingUpgradeable so culture level-up bumps apply uniformly.
// Resolves via "Alanthor_RoyalStable" id end-to-end (BuildingFactory.Create,
// CommandRouter.ResolveBuildingIdForTrainer, BuildingSizeConfig.GetSize,
// BuildCosts._byId).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class RoyalStable
    {
        // Defaults used when TechTreeDB is unavailable. Live values come
        // from Assets/Resources/TechTree.json → Alanthor_RoyalStable.
        private const float DefaultHP = 1000f;
        private const float DefaultLoS = 18f;
        public const int PresentationID = 356;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float los = DefaultLoS;

            if (TechTreeDB.Instance != null
                && TechTreeDB.Instance.TryGetBuilding("Alanthor_RoyalStable", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(RoyalStableTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(TrainingState),
                typeof(Radius),
                typeof(BuildingSize)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_RoyalStable");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });

            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint
            {
                Position = position + new float3(3f, 0, 3f),
                Has = 1
            });

            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float los = DefaultLoS;

            if (TechTreeDB.Instance != null
                && TechTreeDB.Instance.TryGetBuilding("Alanthor_RoyalStable", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<RoyalStableTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_RoyalStable");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint
            {
                Position = position + new float3(3f, 0, 3f),
                Has = 1
            });

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }
    }
}
