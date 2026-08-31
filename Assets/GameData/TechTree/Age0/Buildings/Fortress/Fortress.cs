// Fortress.cs
// THE CAPITAL (docs/Design/Age_0.md, 2026-08-31): every player's starting
// building. Larger and far more formidable than a Hall, and NOT buildable —
// PlayerSpawnSystem places exactly one per player at match start; expansion
// stays the Hall's job.
//
// Mechanically the Fortress IS a Hall plus more: it carries HallTag on
// purpose, so every Hall-driven rule works on it unchanged — the territory
// claim (TerritoryOwnership.Claim<HallTag>), the one-claim-per-territory
// cap, curse conquest immunity for its region, AI home anchoring and army
// targeting, and the victory bookkeeping. FortressTag on top is what names
// it (BuildingIds checks it FIRST) and lets anything treat the capital
// specially. Its Hall research bench is inherited at catalog load — see
// TechCatalog.RebuildResearchLists.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

/// <summary>Marks the capital. Always accompanied by <see cref="HallTag"/> —
/// the Fortress is a Hall with more, never instead.</summary>
public struct FortressTag : IComponentData { }

namespace TheWaningBorder.Entities
{
    public static class Fortress
    {
        /// <summary>Reuses the Hall's art (same silhouette language); the
        /// footprint and the visual scale below are what read as "bigger".</summary>
        public const int PresentationID = Hall.PresentationID;

        /// <summary>Uniform visual scale over the Hall prefab — the model
        /// grows with its 10x10 footprint (Hall is 8x8).</summary>
        private const float VisualScale = 1.25f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Building("Fortress");
            float hp = def.hp;
            float los = def.lineOfSight;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, VisualScale));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 1 }); // THE base
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new SuppliesIncome { PerTick = def.suppliesPerTick, Interval = def.suppliesInterval });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Fortress");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            creator.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            creator.AddComponent(entity, new PopulationProvider { Amount = def.populationProvided });
            creator.AddComponent(entity, new FactionProgress { Culture = Cultures.None });

            creator.AddBuffer<TrainQueueItem>(entity);
            creator.AddComponent<HallTag>(entity);      // the capital IS a Hall — see header
            creator.AddComponent<FortressTag>(entity);
            creator.AddComponent<BuildingUpgradeable>(entity);
            creator.AddComponent(entity, new RallyPoint { Position = position + new float3(6f, 0, 6f), Has = 1 });
            // Garrison attack straight from the SO — "much more formidable"
            // is data, not code (no constants ladder here).
            creator.AddComponent(entity, new BuildingRangedAttack
            {
                Range = def.attack.range,
                Damage = (int)def.attack.damage,
                Cooldown = def.attack.cooldown,
                Timer = 0f,
                MaxTargets = def.attack.maxTargets,
            });

            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });

            creator.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ResearchQueueItem>(entity);

            return entity;
        }
    }
}
