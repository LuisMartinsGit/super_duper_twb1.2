using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Cataphract unit - Alanthor culture heavy cavalry.
    /// Heavily armored mounted unit. Slower than light cavalry but very durable.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Cataphract
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "Alanthor_Cataphract" — 160 HP / 6.6 spd / 18 dmg / 1.6 cd /
        // LoS 20 / pop 2).
        private const int PresentationID = 336;

        /// <summary>Create Cataphract using EntityManager.</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create Cataphract using EntityCommandBuffer for deferred creation.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Alanthor_Cataphract");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float cooldown = def.attackCooldown;

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 2 });
            // Heavy shock cavalry: +50% damage on a connecting charge. Read by
            // CombatDamageHelper while Charging is set.
            creator.AddComponent(entity, new TheWaningBorder.Abilities.InnateChargePct { Pct = 50f });
            creator.AddComponent<CavalryTag>(entity);

            // Combat type tags (calculator: defense 2/1/0/0)
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Cavalry });
            creator.AddComponent(entity, new Defense { Melee = 2, Ranged = 1, Siege = 0, Magic = 0 });

            // Royal Stable horn techs (War Horn / Full Gallop) are granted at spawn
            // when researched; TechEffectSystem back-fills cavalry already on the map.
            creator.AddComponent(entity, TheWaningBorder.Abilities.AbilityAssignment
                .BuildCavalryAbilities(faction, def != null ? def.abilities : null));
            creator.AddComponent(entity, default(TheWaningBorder.Abilities.AbilityCooldowns));

            return entity;
        }
    }
}
