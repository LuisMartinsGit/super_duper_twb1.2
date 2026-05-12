// File: Assets/Scripts/Entities/Units/Iconoclast.cs
// Feraldis Iconoclast — the high-value Lv 3 unit whose attacks bypass
// Crystal node invulnerability (spec refinement #1). Every other unit's
// damage to a node is refunded by NodeInvulnerabilitySystem; an
// Iconoclast attack is the only path to Destroyed.
//
// Slow + hard-hitting + heavy HP. Trained at a fully-leveled Feraldis
// Longhouse (minBuildingLevel: 3 in TechTree.json). 4 pop slots.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    public static class Iconoclast
    {
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = IconoclastHP;
            float speed = IconoclastSpeed;
            float damage = IconoclastDamage;
            float los = IconoclastLoS;
            float attackRange = IconoclastAttackRange;
            float cooldown = IconoclastAttackCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Feraldis_Iconoclast", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackRange > 0) attackRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = IconoclastPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent<IconoclastTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = IconoclastRadius });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 4 });
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            creator.AddComponent(entity, new Defense { Melee = 4, Ranged = 3, Siege = 1, Magic = 2 });

            return entity;
        }
    }
}
