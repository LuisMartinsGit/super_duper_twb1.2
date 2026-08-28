using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Hall of Axes — trains the culture's ranged roster (Archer,
    /// Hunter, Firethrower). Design: docs/Design/Age_1_Feraldis.md.
    ///
    /// A building in its own right, NOT a cultured Archery Range — that
    /// building is Alanthor-only (2026-08-27). Modelled on Pasture, the other
    /// stand-alone Feraldis trainer.
    /// </summary>
    public static class HallOfAxes
    {
        public const int PresentationID = 369;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            ReadDef(out float hp, out float los, out var defense);

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_HallOfAxes");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponent<HallOfAxesTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, defense);
            em.AddComponent<BuildingUpgradeable>(entity);
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            ReadDef(out float hp, out float los, out var defense);

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_HallOfAxes");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent<HallOfAxesTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, defense);
            ecb.AddComponent<BuildingUpgradeable>(entity);
            return entity;
        }

        private static void ReadDef(out float hp, out float los, out Defense defense)
        {
            defense = new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            var def = TechCatalog.Building("Feraldis_HallOfAxes");
            hp = def.hp;
            los = def.lineOfSight;
            if (def.defense != null)
                defense = new Defense
                {
                    Melee = def.defense.melee,
                    Ranged = def.defense.ranged,
                    Siege = def.defense.siege,
                    Magic = def.defense.magic,
                };
        }
    }
}
