// File: Assets/Scripts/Entities/Buildings/AlanthorWall.cs
// Alanthor wall system: hub (cylinder tower) + segment (data-only graph edge) + instances (small wall pieces)
// Walls form the backbone of Alanthor economy — enclosed areas generate income.
// Each segment spawns multiple small wall instances that block the passability grid.
// Instances can be upgraded to towers (ranged attack) or gates (friendly-only passage).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Factory for Alanthor wall entities.
    /// Walls consist of hubs (connection points), segments (logical graph edges),
    /// and instances (small wall pieces that block pathfinding).
    /// </summary>
    public static class AlanthorWall
    {
        public const int HubPresentationID = 550;
        // Segment no longer has a visual (data-only graph edge)
        public const int InstancePresentationID = 552;
        public const int TowerPresentationID = 553;
        public const int GatePresentationID = 554;

        /// <summary>Spacing between wall instances in meters.</summary>
        public const float InstanceSpacing = 2f;

        /// <summary>Inset from each hub center to avoid overlapping hub footprint.</summary>
        private const float HubInset = 1.0f;

        // Hub defaults (loaded from TechTreeDB "Alanthor_Wall" when available)
        private const float DefaultHubHP = 600f;
        private const float DefaultHubLoS = 8f;
        private const float DefaultHubRadius = 0.8f;

        // Instance defaults
        private const float DefaultInstanceHP = 200f;
        private const float DefaultInstanceLoS = 5f;

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
        /// The segment is a data-only entity (no visual). It spawns wall instances
        /// along the line between the two hubs, each blocking a grid cell.
        /// Also updates the WallHubLink buffers on both hubs.
        /// </summary>
        public static Entity CreateSegment(EntityManager em, Entity hubA, Entity hubB, Faction faction)
        {
            var posA = em.GetComponentData<LocalTransform>(hubA).Position;
            var posB = em.GetComponentData<LocalTransform>(hubB).Position;

            float3 midpoint = (posA + posB) * 0.5f;
            float3 diff = posB - posA;
            float3 dirFlat = math.normalize(new float3(diff.x, 0f, diff.z));
            quaternion rotation = quaternion.LookRotationSafe(dirFlat, math.up());

            // Segment entity: data-only graph edge (no PresentationId, no BuildingSize)
            var entity = em.CreateEntity(
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(WallTag),
                typeof(WallSegmentTag),
                typeof(WallConnection)
            );

            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                midpoint, rotation, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = 1, Max = 1 }); // structural placeholder
            em.SetComponentData(entity, new WallConnection { HubA = hubA, HubB = hubB });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            // Radius needed for some queries but minimal
            em.AddComponentData(entity, new Radius { Value = 0.1f });

            // Buffer for child instances
            em.AddBuffer<WallInstanceRef>(entity);

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

            // Spawn wall instances along the line
            SpawnInstances(em, entity, posA, posB, dirFlat, rotation, faction);

            return entity;
        }

        /// <summary>
        /// Spawn wall instance entities evenly along the line between two hubs.
        /// Each instance is a 1x1 building that blocks the passability grid.
        /// </summary>
        private static void SpawnInstances(
            EntityManager em, Entity segment,
            float3 posA, float3 posB,
            float3 direction, quaternion rotation,
            Faction faction)
        {
            float distance = math.distance(
                new float2(posA.x, posA.z),
                new float2(posB.x, posB.z));

            float usable = distance - 2f * HubInset;
            if (usable < 0.5f)
            {
                // Hubs too close — spawn one instance at midpoint
                float3 mid = (posA + posB) * 0.5f;
                var inst = CreateInstance(em, mid, rotation, faction, segment);
                var buf = em.GetBuffer<WallInstanceRef>(segment);
                buf.Add(new WallInstanceRef { Instance = inst });
                return;
            }

            int count = math.max(1, (int)math.round(usable / InstanceSpacing));
            float actualSpacing = usable / count;

            var buffer = em.GetBuffer<WallInstanceRef>(segment);

            for (int i = 0; i < count; i++)
            {
                float t = HubInset + actualSpacing * (i + 0.5f);
                float3 pos = posA + direction * t;
                // Y will be snapped to terrain by presentation system

                var instance = CreateInstance(em, pos, rotation, faction, segment);
                buffer.Add(new WallInstanceRef { Instance = instance });
            }
        }

        /// <summary>
        /// Create a single wall instance entity at the given position.
        /// </summary>
        public static Entity CreateInstance(
            EntityManager em, float3 position, quaternion rotation,
            Faction faction, Entity parentSegment)
        {
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
                typeof(WallInstanceTag),
                typeof(WallInstanceParent)
            );

            em.SetComponentData(entity, new PresentationId { Id = InstancePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, rotation, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)DefaultInstanceHP, Max = (int)DefaultInstanceHP });
            em.SetComponentData(entity, new LineOfSight { Radius = DefaultInstanceLoS });
            em.SetComponentData(entity, new BuildingSize { Width = 1, Height = 1 });
            em.SetComponentData(entity, new Radius { Value = 0.5f });
            em.SetComponentData(entity, new WallInstanceParent { Segment = parentSegment });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
