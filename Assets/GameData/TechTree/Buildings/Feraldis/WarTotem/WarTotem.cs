// File: Assets/GameData/TechTree/Buildings/Feraldis/WarTotem/WarTotem.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis War Totem — planted on a blood pool, it drinks the pool into
    /// permanent Fervor and projects the Feraldis claim.
    ///
    /// Cheap, unarmed, and fragile by design: the totem is an investment in
    /// ground you have already bled on, and killing one is how an opponent
    /// answers Feraldis expansion. Placement is blood-gated in
    /// BuilderCommandPanel / BuildCommandHelper.
    ///
    /// Design: docs/Design/Age_1_Feraldis.md (2026-08-05).
    /// </summary>
    public static class WarTotem
    {
        private const float DefaultHP = 500f;
        private const float DefaultLoS = 16f;

        /// <summary>
        /// Shares the Totem Tower's procedural visual (pid 361). Presentation
        /// ids select the VISUAL and are deliberately shared across entities
        /// (Outrider/Cataphract both use 336) — a totem should look like a
        /// totem, and the display name comes from the building id, not the pid.
        /// </summary>
        public const int PresentationID = 361;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP, los = DefaultLoS;
            var defense = new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            if (TechCatalog.TryGetBuilding("Feraldis_WarTotem", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.defense != null)
                    defense = new Defense
                    {
                        Melee = def.defense.melee,
                        Ranged = def.defense.ranged,
                        Siege = def.defense.siege,
                        Magic = def.defense.magic,
                    };
            }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_WarTotem");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<WarTotemTag>(entity);
            em.AddComponentData(entity, new TotemFervor { Value = 0f, DrinkTimer = 0f });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, defense);
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP, los = DefaultLoS;
            var defense = new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            if (TechCatalog.TryGetBuilding("Feraldis_WarTotem", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.defense != null)
                    defense = new Defense
                    {
                        Melee = def.defense.melee,
                        Ranged = def.defense.ranged,
                        Siege = def.defense.siege,
                        Magic = def.defense.magic,
                    };
            }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_WarTotem");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<WarTotemTag>(entity);
            ecb.AddComponent(entity, new TotemFervor { Value = 0f, DrinkTimer = 0f });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, defense);
            return entity;
        }
    }
}
