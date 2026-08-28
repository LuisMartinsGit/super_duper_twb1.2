using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Hut building - housing structure.
    /// Provides population capacity only (no resource generation).
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Hut
    {
        public const int PresentationID = 102;

        // Alanthor "Retaliatory measures" — the auto-fire arrow attack Houses
        // gain when the tech is researched (canon: +12 dmg, defensive range).
        private const float RetaliatoryRange = 12f;
        private const int RetaliatoryDamage = 12;
        private const float RetaliatoryCooldown = 2.5f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Building("Hut");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent<HutTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hut");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });

            // Design §5.1: Feraldis Houses do not contribute pop — they're raider-spawn buildings.
            // FeraldisPopOverride caps the faction at 200 instantly at age-up.
            if (FactionColors.GetFactionCulture(faction) != Cultures.Feraldis)
                creator.AddComponent(entity, new PopulationProvider { Amount = def.populationProvided });

            // Combat type tags
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            creator.AddComponent<BuildingUpgradeable>(entity);

            // Research host: the House offers "Retaliatory measures"
            // (TechTree.json Hut.research). The research UI surfaces only for
            // entities carrying ResearchState.
            creator.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ResearchQueueItem>(entity);

            // Houses built after Retaliatory measures is researched fight back
            // from the start; existing Houses are upgraded by TechEffectSystem.
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null && research.HasResearched(faction, "RetaliatoryMeasures"))
            {
                creator.AddComponent(entity, new BuildingRangedAttack
                {
                    Range = RetaliatoryRange,
                    Damage = RetaliatoryDamage,
                    Cooldown = RetaliatoryCooldown,
                    Timer = 0f,
                    MaxTargets = 1,
                });
                creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            }

            return entity;
        }
    }
    // HutTag is defined in BuildingComponents.cs (global namespace)
}
