using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Alanthor Sawyer — a timber yard raised beside a forest, which raises what
    /// that forest pays its territory's owner.
    ///
    /// Design: docs/Design/Regions.md §4. Forests are impassable but CLAIMABLE:
    /// they belong to their territory, take on their owner's culture decorations
    /// and produce supplies. The Sawyer is the only way to improve that output,
    /// which is what makes a forested territory worth taking rather than merely
    /// worth having.
    ///
    /// It earns nothing on its own. The multiplier is applied by
    /// TerritoryIncomeSystem to the FOREST supply of the territory the Sawyer
    /// stands in — so a Sawyer in a territory with no forest is wasted stone,
    /// and BuildCommandPannel gates placement on a forest being in range for
    /// exactly that reason.
    ///
    /// Deliberately unarmed and ordinary-HP: it is an economic commitment inside
    /// ground you already hold, and burning one is how an opponent answers a
    /// player who has gone wide on timber.
    /// </summary>
    public static class Sawyer
    {
        /// <summary>
        /// Reuses the Feraldis Logging Station's procedural visual (359).
        ///
        /// That building was CUT (2026-08-05 rev.4 — Feraldis huts became Raider
        /// Camps), so its timber-yard mesh is registered
        /// (PresentationSpawnSystem "Procedural/FeraldisLoggingStation") and
        /// otherwise unused. Right subject, no new art, and presentation ids are
        /// deliberately shared across entities anyway — the display name comes
        /// from the building id, not the pid.
        ///
        /// ART PASS: it is Feraldis-styled on an Alanthor building. Give the
        /// Sawyer its own id and mesh when Alanthor gets its art pass.
        /// </summary>
        public const int PresentationID = 359;

        public const string BuildingId = "Alanthor_Sawyer";

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            float hp = def.hp, los = def.lineOfSight;
            var defense = Defence(def);

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SawyerTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, defense);
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            float hp = def.hp, los = def.lineOfSight;
            var defense = Defence(def);

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });

            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SawyerTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, defense);
            return entity;
        }

        private static Defense Defence(TheWaningBorder.Data.BuildingDef def)
        {
            if (def?.defense == null)
                return new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            return new Defense
            {
                Melee = def.defense.melee,
                Ranged = def.defense.ranged,
                Siege = def.defense.siege,
                Magic = def.defense.magic,
            };
        }
    }
}
