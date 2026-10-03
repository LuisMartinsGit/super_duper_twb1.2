// Fortress.cs
// THE CAPITAL (docs/Design/Age_0.md § The Shelter, 2026-10-03): every
// player's starting building. In Age 0 it is called the SHELTER (the SO's
// displayName); at age-up it becomes the FORTRESS for every culture, and
// from Age 1 it levels L1-L3 (the culture's BuildingLevelDefSOs, "Fortress
// - Lvl N"). The id stays "Fortress" in both ages — only the name changes.
// There is no Hall, King's Court or Town Hall any more.
//
// It carries HallTag on purpose, so every capital rule keyed on that tag
// works on it unchanged — the territory claim (TerritoryOwnership.Claim<HallTag>),
// the one-claim-per-territory cap, curse conquest immunity for its region,
// AI home anchoring and army targeting, and the victory bookkeeping.
// FortressTag on top is what names it (BuildingIds). It trains Workers and
// Scouts (plus Alanthor's Ledger and King Lexor, culture-gated) and hosts
// the capital research — both straight from its SO (trains[] and every
// tech whose researchAt is "Fortress").

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

/// <summary>Marks the capital (the Shelter / Fortress). Always accompanied
/// by <see cref="HallTag"/>, the capital marker every capital rule reads.</summary>
public struct FortressTag : IComponentData { }

namespace TheWaningBorder.Entities
{
    public static class Fortress
    {
        /// <summary>The Fortress's OWN art (2026-10-01): Fortress.prefab beside
        /// this file, authored by Waning Border > Art > Author Castle Prefabs.
        /// It borrowed the Hall's hand-made prefab before.</summary>
        public const int PresentationID = 105;

        /// <summary>No extra scale: the authored prefab is fitted to the 10x10
        /// footprint by the spawner, as every authored building is.</summary>
        private const float VisualScale = 1f;

        /// <summary>The capital's name from age-up on. Its Age 0 name, the
        /// Shelter, is the SO's displayName.</summary>
        public const string AgedName = "Fortress";

        /// <summary>Rename a capital to <see cref="AgedName"/> — at age-up
        /// and for a capital raised after its owner aged up (both through
        /// AgeUpSystem.TransformCapitalForCulture).</summary>
        public static void ApplyAgedName(EntityManager em, Entity capital)
        {
            var name = new DisplayName();
            name.Value.CopyFromTruncated(AgedName);
            if (em.HasComponent<DisplayName>(capital)) em.SetComponentData(capital, name);
            else em.AddComponentData(capital, name);
        }

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
            creator.AddComponent(entity, new PopulationProvider { Amount = def.populationProvided });
            creator.AddComponent(entity, new FactionProgress { Culture = Cultures.None });

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

            creator.AddComponent(entity, new ProductionState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ProductionQueueItem>(entity);

            return entity;
        }
    }
}
