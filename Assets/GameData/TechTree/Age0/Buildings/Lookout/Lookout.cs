// Lookout.cs
// The Lookout (docs/Design/Age_0.md § Lookout; developer, 2026-11-18: "a
// tower that provides LOS but no attack, does not evolve to a tower on age
// up. cheap."). Every culture builds it, in every age. Every stat comes from
// Lookout.asset; the factory holds no numbers.
//
// What it deliberately does NOT get: BuildingRangedAttack (it never
// shoots), a garrison buffer, BuildingUpgradeable / a production queue (no
// levels), and no tag any age-up conversion reads — so it stays a Lookout
// for the whole match.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Lookout
    {
        public const string BuildingId = "Lookout";

        /// <summary>Lookout.asset's presentationId (the BuildingFactory
        /// recipe and the entity carry the same number).</summary>
        public const int PresentationID = 545;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Building(BuildingId);
            float hp = def.hp;
            float los = def.lineOfSight;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent<LookoutTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }
    }
}
