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
    /// WorkerCommandPanel / BuildCommandHelper.
    ///
    /// Design: docs/Design/Age_1_Feraldis.md (2026-08-05).
    /// </summary>
    public static class WarTotem
    {

        /// <summary>
        /// Shares the Totem Tower's procedural visual (pid 361). Presentation
        /// ids select the VISUAL and are deliberately shared across entities
        /// (Outrider/Cataphract both use 336) — a totem should look like a
        /// totem, and the display name comes from the building id, not the pid.
        /// </summary>
        public const int PresentationID = 361;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Feraldis_WarTotem");
            float hp = def.hp, los = def.lineOfSight;
            var defense = new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            if (def.defense != null)
                defense = new Defense
                {
                    Melee = def.defense.melee,
                    Ranged = def.defense.ranged,
                    Siege = def.defense.siege,
                    Magic = def.defense.magic,
                };

            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_WarTotem");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(WarTotemTag));
            EntityBuild.Add(em, entity, new TotemFervor { Value = 0f, DrinkTimer = 0f });
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(em, entity, defense);
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Feraldis_WarTotem");
            float hp = def.hp, los = def.lineOfSight;
            var defense = new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            if (def.defense != null)
                defense = new Defense
                {
                    Melee = def.defense.melee,
                    Ranged = def.defense.ranged,
                    Siege = def.defense.siege,
                    Magic = def.defense.magic,
                };

            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize("Feraldis_WarTotem");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(WarTotemTag));
            EntityBuild.Add(ecb, entity, new TotemFervor { Value = 0f, DrinkTimer = 0f });
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(ecb, entity, defense);
            return EntityBuild.Done(entity);
        }
    }
}
