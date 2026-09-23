// Trebuchet Emplacement — the PLATFORM half of the pair
// (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet emplacements).
//
// The player places this; EmplacementCrewSystem raises the engine on it the
// moment construction finishes, and raises a replacement 60 s after one is
// killed. Killing the platform kills the engine with it. The platform does
// not shoot — it is ground you have decided to hold.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class TrebuchetEmplacement
    {
        public const int PresentationID = 571;
        public const string Id = "Alanthor_TrebuchetEmplacement";
        /// <summary>The engine this platform mounts.</summary>
        public const string EngineId = "Alanthor_EmplacedTrebuchet";
        /// <summary>Seconds the crew takes to raise a replacement engine.</summary>
        public const float RebuildSeconds = 60f;
        /// <summary>How high above the platform origin the engine stands.</summary>
        public const float MountHeight = 1.4f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(Id);
            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            em.SetComponentData(entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(Id);
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<EmplacementTag>(entity);
            em.AddComponentData(entity, new EmplacementCrew
            {
                Engine = Entity.Null,
                Rebuild = 0f,
                RebuildTime = RebuildSeconds,
                EngineId = EngineId,
                MountHeight = MountHeight,
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(Id);
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(Id);
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<EmplacementTag>(entity);
            ecb.AddComponent(entity, new EmplacementCrew
            {
                Engine = Entity.Null,
                Rebuild = 0f,
                RebuildTime = RebuildSeconds,
                EngineId = EngineId,
                MountHeight = MountHeight,
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }
    }
}
