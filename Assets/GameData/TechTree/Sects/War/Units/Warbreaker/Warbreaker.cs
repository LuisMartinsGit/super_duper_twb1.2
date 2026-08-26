// The Warbreaker — Sect of War's unit lever (task-063 spec, Lv I:
// "heavy elite"). A frontline bruiser with well-above-line HP and
// damage but slow feet. The spec's Challenge taunt (forces enemies in
// radius onto the Warbreaker) needs TargetingSystem support and lands
// with the War sect's Lv II/III pass. Trained at the Temple of Ridan
// once War is adopted.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Warbreaker
    {
        private const float DefaultHP = 260f;
        private const float DefaultSpeed = 4.4f;
        private const float DefaultDamage = 18f;
        private const float DefaultLoS = 10f;
        private const float DefaultAttackCooldown = 1.4f;
        public const int PresentationID = 407; // 388-403 taken; see PresentationSpawnSystem table

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;

            if (TechCatalog.TryGetUnit("Sect_Warbreaker", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = DefaultAttackCooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = 0.6f });
            creator.AddComponent(entity, new PopulationCost { Amount = 2 });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            creator.AddComponent(entity, new Defense { Melee = 2, Ranged = 1, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
