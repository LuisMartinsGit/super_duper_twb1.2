// File: Assets/Scripts/Entities/Units/Lorekeeper.cs
// The Lorekeeper — Sect of Antiquity's unit lever (task-063 spec,
// implemented 2026-07-05). A non-combat support scholar:
//   * Reveals stealthed enemies in a radius (Lv I 6m, Lv II+ 12m —
//     LorekeeperDetectionSystem).
//   * Lv III: far-sight, LineOfSight raised to 24m ("sight through fog").
//   * Garrison synergy: standing within ReliquarySystem.GarrisonRange of a
//     Reliquary accelerates its ability cooldowns (-15/-30/-50% by level,
//     doubled by Reliquary Lv III).
// Trained at the Antiquity chapel. Carries no Damage component, so the
// combat systems never enlist it.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Lorekeeper
    {
        private const float DefaultHP = 90f;
        private const float DefaultSpeed = 3.4f;
        private const float DefaultLoS = 16f;
        public const int PresentationID = 387; // after Iconoclast (386)

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float los = DefaultLoS;

            if (TechCatalog.TryGetUnit("Sect_Lorekeeper", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Support });
            creator.AddComponent<LorekeeperTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Armor identity: unarmored support (no Damage — never fights).
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 1 });

            return entity;
        }
    }
}
