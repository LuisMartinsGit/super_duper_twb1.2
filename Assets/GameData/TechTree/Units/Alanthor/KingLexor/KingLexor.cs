using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// King Lexor — Alanthor hero cavalry (King's Court, one per player).
    /// Passive leadership aura (King's Call) buffs allied Alanthor units; active
    /// Liquid Courage (90% DR + attack, with the Veilshift Withdrawal + Life Cling
    /// aftermath chain). One-per-player limit + escalating respawn train-time are
    /// enforced at the training gate (see HeroTrainLimit / CommandRouter /
    /// TrainingSystem). Placeholder art (pid 251 -> capsule); stats placeholder.
    /// </summary>
    public static class KingLexor
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // "King Lexor" — 650 HP / 45 dmg / 1.4 cd / speed 7 / LoS 26 / pop 3).
        private const float DefaultHP = 650f;
        private const float DefaultSpeed = 7f;
        private const float DefaultDamage = 45f;
        private const float DefaultLoS = 26f;
        private const float DefaultAttackCooldown = 1.4f;
        public const int PresentationID = 251;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP, speed = DefaultSpeed, damage = DefaultDamage, los = DefaultLoS;
            float cooldown = DefaultAttackCooldown;
            if (TechCatalog.TryGetUnit("King Lexor", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

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
            creator.AddComponent(entity, new Radius { Value = 0.6f });
            creator.AddComponent(entity, new PopulationCost { Amount = 3 });
            // The King rides as heavy cavalry: +50% on a connecting charge, on top
            // of the flat bonus his own King's Call aura grants.
            creator.AddComponent(entity, new InnateChargePct { Pct = 50f });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Cavalry });
            creator.AddComponent(entity, new Defense { Melee = 3, Ranged = 2, Siege = 0, Magic = 1 });

            // Hero / unique + abilities. Abilities come from the SO 'abilities' field;
            // fall back to the design default (King's Call aura + Liquid Courage active).
            creator.AddComponent(entity, new UniqueUnitTag { Kind = UniqueUnitKind.KingLexor });
            string[] abilityNames = (def != null && def.abilities != null && def.abilities.Length > 0)
                ? def.abilities : new[] { "King's Call", "Liquid Courage" };
            creator.AddComponent(entity, AbilityAssignment.Build(abilityNames));
            creator.AddComponent(entity, default(AbilityCooldowns));

            return entity;
        }
    }
}
