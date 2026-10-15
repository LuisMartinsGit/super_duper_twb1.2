// Trebuchet Emplacement — the PLATFORM half of the pair
// (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet emplacements).
//
// UNREACHABLE SINCE 2026-09-25: emplacements are WALL-MOUNT ONLY
// (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet emplacements).
// This free-standing platform is no longer in any build list and
// CommandRouter.IssuePlaceBuilding refuses its id. The factory and its
// recipe row stay registered because the SO still loads (its cost is the
// wall MOUNT's price) and old scenarios/saves may name it.
//
// Were one to exist, EmplacementCrewSystem would raise its engine when
// construction finished; a killed engine leaves it empty until a paid
// Replace Equipment order, exactly like a wall mount.

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
        /// <summary>How high above the platform origin the engine stands.</summary>
        public const float MountHeight = 1.4f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(Id);
            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));

            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(Id);
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(EmplacementTag));
            EntityBuild.Add(em, entity, new EmplacementCrew
            {
                Engine = Entity.Null,
                EngineId = EngineId,
                MountHeight = MountHeight,
            });
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(Id);
            var entity = EntityBuild.CreateEntity(ecb);

            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(Id);
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(EmplacementTag));
            EntityBuild.Add(ecb, entity, new EmplacementCrew
            {
                Engine = Entity.Null,
                EngineId = EngineId,
                MountHeight = MountHeight,
            });
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return EntityBuild.Done(entity);
        }
    }
}
