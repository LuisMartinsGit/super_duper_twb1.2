using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Outrider — Alanthor light cavalry (2026-08-04, design: the Royal
    /// Stable's cheap fast slot under the Cataphract). Raids, screens and
    /// scouts; folds instantly against a braced line. Own presentation id
    /// (349); dedicated art lands in a later visual wave.
    /// </summary>
    public static class Outrider
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "Alanthor_Outrider" — 95 HP / 8.2 spd / 12 dmg / 1.4 cd / LoS 22).
        private const float DefaultHP = 95f;
        private const float DefaultSpeed = 8.2f;
        private const float DefaultDamage = 12f;
        private const float DefaultLoS = 22f;
        private const float DefaultAttackCooldown = 1.4f;
        private const float DefaultRadius = 0.55f;
        public const int PresentationID = 349;

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
            float cooldown = DefaultAttackCooldown;

            if (TechCatalog.TryGetUnit("Alanthor_Outrider", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 0.85f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            // Light cavalry: +30% damage on a connecting charge. Read by
            // CombatDamageHelper while Charging is set.
            creator.AddComponent(entity, new TheWaningBorder.Abilities.InnateChargePct { Pct = 30f });
            creator.AddComponent<CavalryTag>(entity);

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Cavalry });
            creator.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });

            // Royal Stable horn techs (War Horn / Full Gallop) are granted at spawn
            // when researched; TechEffectSystem back-fills cavalry already on the map.
            creator.AddComponent(entity, TheWaningBorder.Abilities.AbilityAssignment
                .BuildCavalryAbilities(faction, def != null ? def.abilities : null));
            creator.AddComponent(entity, default(TheWaningBorder.Abilities.AbilityCooldowns));

            return entity;
        }
    }
}
