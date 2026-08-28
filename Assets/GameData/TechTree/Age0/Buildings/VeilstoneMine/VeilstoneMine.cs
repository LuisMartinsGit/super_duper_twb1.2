using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// The Veilstone Mine. Raised ON a veilstone outcropping (the placement
    /// gate is TerritoryOwnership.OnFreeNodeFor), one per node, and it adds
    /// its level to that node's territory yield.
    ///
    /// Design: docs/Design/Regions.md §4.
    /// </summary>
    public static class VeilstoneMine
    {
        // Shares the Mine's procedural visual: same pit, same headframe, and
        // the presentation layer has no veilstone-specific art yet. A distinct
        // id would only produce an invisible building.
        public const int PresentationID = 364;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            ReadDef(out float hp, out float los, out var defense);

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("VeilstoneMine");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<VeilstoneMineTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, defense);
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

            var gridSize = BuildingSizeConfig.GetSize("VeilstoneMine");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<VeilstoneMineTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, defense);
            return entity;
        }

        private static void ReadDef(out float hp, out float los, out Defense defense)
        {
            defense = new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            var def = TechCatalog.Building("VeilstoneMine");
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
