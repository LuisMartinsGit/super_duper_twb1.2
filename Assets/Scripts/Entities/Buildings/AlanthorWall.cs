// File: Assets/Scripts/Entities/Buildings/AlanthorWall.cs
// Alanthor wall system: hub (cylinder tower) + segment (elongated cube connector)
// Walls form the backbone of Alanthor economy — enclosed areas generate income.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Factory for Alanthor wall entities.
    /// Walls consist of hubs (connection points) and segments (connectors between hubs).
    /// </summary>
    public static class AlanthorWall
    {
        public const int HubPresentationID = 550;
        public const int SegmentPresentationID = 551;

        // Hub defaults (loaded from TechTreeDB "Alanthor_Wall" when available)
        private const float DefaultHubHP = 600f;
        private const float DefaultHubLoS = 8f;
        private const float DefaultHubRadius = 0.8f;

        // Segment defaults
        private const float DefaultSegmentHP = 400f;
        private const float DefaultSegmentLoS = 5f;
        private const float DefaultSegmentRadius = 0.3f;

        /// <summary>
        /// Create a wall hub entity (the cylinder connection point).
        /// </summary>
        public static Entity CreateHub(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHubHP;
            float los = DefaultHubLoS;
            float radius = DefaultHubRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Wall", out var def))
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
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(WallTag),
                typeof(WallHubTag)
            );

            em.SetComponentData(entity, new PresentationId { Id = HubPresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Wall");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Dynamic buffer for tracking connections to other hubs
            em.AddBuffer<WallHubLink>(entity);

            return entity;
        }

        /// <summary>
        /// Create a wall segment connecting two hubs.
        /// Automatically positions at the midpoint and rotates to face hub A → hub B.
        /// Also updates the WallHubLink buffers on both hubs.
        /// </summary>
        public static Entity CreateSegment(EntityManager em, Entity hubA, Entity hubB, Faction faction)
        {
            var posA = em.GetComponentData<LocalTransform>(hubA).Position;
            var posB = em.GetComponentData<LocalTransform>(hubB).Position;

            float3 midpoint = (posA + posB) * 0.5f;
            float3 diff = posB - posA;
            float3 dirFlat = math.normalize(new float3(diff.x, 0f, diff.z));

            // Rotation: look from A toward B along the XZ plane
            quaternion rotation = quaternion.LookRotationSafe(dirFlat, math.up());

            float distance = math.distance(
                new float2(posA.x, posA.z),
                new float2(posB.x, posB.z));

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(WallTag),
                typeof(WallSegmentTag),
                typeof(WallConnection)
            );

            em.SetComponentData(entity, new PresentationId { Id = SegmentPresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                midpoint, rotation, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)DefaultSegmentHP, Max = (int)DefaultSegmentHP });
            em.SetComponentData(entity, new LineOfSight { Radius = DefaultSegmentLoS });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Wall");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new WallConnection { HubA = hubA, HubB = hubB });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Update hub connection buffers
            if (em.HasBuffer<WallHubLink>(hubA))
            {
                var bufA = em.GetBuffer<WallHubLink>(hubA);
                bufA.Add(new WallHubLink { ConnectedHub = hubB, Segment = entity });
            }

            if (em.HasBuffer<WallHubLink>(hubB))
            {
                var bufB = em.GetBuffer<WallHubLink>(hubB);
                bufB.Add(new WallHubLink { ConnectedHub = hubA, Segment = entity });
            }

            return entity;
        }
    }
}
