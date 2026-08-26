// Shared spine for the twelve sect buildings (docs/Design/Sects.md section 1).
//
// Every sect grants exactly one building, capped at 5 per faction, and each is
// where that sect's unit is trained and its research is bought. That makes the
// four Alanthor buildings identical in structure and different only in stats
// and tag — so the structure lives here once and each building file carries
// only what makes it that building.
//
// The cap itself is NOT enforced here. It is enforced at the three sites the
// project already uses for build limits: the build-menu gate
// (EntityExtractors.Buildings), the click-time check (BuildCommandPannel) and
// the authoritative replicated entry point (CommandRouter.IssuePlaceBuilding).
// SectBuildingCap below is the single number all three read.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class SectBuilding
    {
        /// <summary>
        /// Build limit per faction, for every sect building. Design constant —
        /// docs/Design/Sects.md section 1. Five is a real cap: the placement
        /// panel greys out at five and CommandRouter refuses the sixth.
        /// </summary>
        public const int CapPerFaction = 5;

        /// <summary>
        /// Create a sect building. <typeparamref name="TTag"/> is the
        /// building's marker component — it is what the cap query counts, so
        /// every sect building needs its own.
        /// </summary>
        public static Entity Create<TTag>(EntityManager em, string buildingId, int presentationId,
            float hp, float los, float3 position, Faction faction)
            where TTag : unmanaged, IComponentData
            => CreateInternal<EmCreator, TTag>(new EmCreator(em), buildingId, presentationId,
                                               hp, los, position, faction);

        public static Entity Create<TTag>(EntityCommandBuffer ecb, string buildingId, int presentationId,
            float hp, float los, float3 position, Faction faction)
            where TTag : unmanaged, IComponentData
            => CreateInternal<EcbCreator, TTag>(new EcbCreator(ecb), buildingId, presentationId,
                                                hp, los, position, faction);

        private static Entity CreateInternal<TCreator, TTag>(TCreator creator, string buildingId,
            int presentationId, float hp, float los, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
            where TTag : unmanaged, IComponentData
        {
            // The SO/JSON def wins over the code defaults when one exists —
            // same precedence every other building uses.
            if (TechCatalog.TryGetBuilding(buildingId, out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = presentationId });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent<TTag>(entity);

            var gridSize = BuildingSizeConfig.GetSize(buildingId);
            creator.AddComponent(entity, new Radius
            {
                Value = BuildingSizeConfig.GetLegacyRadius(gridSize)
            });
            creator.AddComponent(entity, new BuildingSize
            {
                Width = gridSize.x,
                Height = gridSize.y,
            });

            // Every sect building trains its sect's unit, so it carries a
            // training queue from birth.
            creator.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<TrainQueueItem>(entity);
            creator.AddComponent(entity, new RallyPoint
            {
                Position = position + new float3(3f, 0f, 3f),
                Has = 1,
            });

            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
