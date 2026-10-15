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
        // Its own id (2026-09-29): the same procedural pithead as the Mine
        // (MineVisual), with the veilstone crystals as its ore.
        public const int PresentationID = 566;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            ReadDef(out float hp, out float los, out var defense);

            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("VeilstoneMine");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(VeilstoneMineTag));
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(em, entity, defense);
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            ReadDef(out float hp, out float los, out var defense);

            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("VeilstoneMine");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(VeilstoneMineTag));
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(ecb, entity, defense);
            return EntityBuild.Done(entity);
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
