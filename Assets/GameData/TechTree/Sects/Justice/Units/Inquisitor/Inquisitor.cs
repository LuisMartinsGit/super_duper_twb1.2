// The Inquisitor — Sect of Justice's unit lever (task-063 spec, Lv I:
// "slow caster, cleanse 1 debuff from ally on cooldown"). A non-combat
// support caster: InquisitorCleanseSystem strips a debuff (CodexFrozen)
// from a nearby ally every CleanseCooldown seconds. Carries no Damage
// component so combat systems never enlist it. Trained at the Temple of
// Ridan once Justice is adopted.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Inquisitor
    {
        private const float DefaultHP = 110f;
        private const float DefaultSpeed = 3.2f;
        private const float DefaultLoS = 14f;
        public const int PresentationID = 406; // 388-403 taken; see PresentationSpawnSystem table

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

            if (TechCatalog.TryGetUnit("Sect_Inquisitor", out var def))
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
            creator.AddComponent<InquisitorTag>(entity);
            creator.AddComponent(entity, new InquisitorState { CleanseCooldown = 0f });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 2 });

            return entity;
        }
    }
}
