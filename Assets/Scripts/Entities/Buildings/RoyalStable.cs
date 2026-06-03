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
    /// <summary>
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class RoyalStable
    {
        // Defaults used when TechTreeDB is unavailable. Live values come
        // from Assets/Resources/TechTree.json → Alanthor_RoyalStable.
        private const float DefaultHP = 1000f;
        private const float DefaultLoS = 18f;
        public const int PresentationID = 356;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float los = DefaultLoS;

            if (TechCatalog.IsReady
                && TechCatalog.TryGetBuilding("Alanthor_RoyalStable", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent<RoyalStableTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_RoyalStable");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            creator.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            creator.AddBuffer<TrainQueueItem>(entity);
            creator.AddComponent(entity, new RallyPoint
            {
                Position = position + new float3(3f, 0, 3f),
                Has = 1
            });

            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            creator.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }
    }
}
