using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Suicidal — a walking bomb. No attack at all: it runs at the
    /// enemy soaking ranged fire (heavy ranged defense, deep HP pool) and
    /// detonates, leaving a large blood pool.
    ///
    /// The point of the unit is that BOTH outcomes feed Feraldis: reaching
    /// the line detonates it, and being shot down detonates it too. Enemy
    /// fire is converted into the bloodsoaked ground the rest of the army
    /// frenzies on and the War Totems drink.
    ///
    /// It carries Damage { 1 } and a very long AttackCooldown ON PURPOSE —
    /// this is what makes it CHARGE, and it took two passes to get right:
    ///   * MeleeCombatSystem's query requires Damage + AttackCooldown, and
    ///     that system is what re-issues the chase DesiredDestination every
    ///     frame. Without those components the unit acquires a target and
    ///     then stands still (TargetingSystem actively zeroes the
    ///     destination while an AttackCommand is present).
    ///   * TargetingSystem additionally refuses to AUTO-acquire for any unit
    ///     whose Damage.Value is 0 (the deliberate Litharch rule), so a
    ///     literal 0 left it inert until manually ordered.
    /// The nominal 1 damage is never dealt: the detonation trigger (2.5) is
    /// wider than melee range (1.5), so it always blows up before it swings.
    /// Design: docs/Design/Age_1_Feraldis.md (2026-08-05).
    /// </summary>
    public static class Suicidal
    {
        public const int PresentationID = 343;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_Suicidal");
            float hp = def.hp;
            float speed = def.speed;
            float los = def.lineOfSight;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            // See the class remarks: these two are what make it CHARGE.
            // Must be >= 1 or TargetingSystem never auto-acquires.
            creator.AddComponent(entity, new Damage { Value = 1 });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = 99f, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            creator.AddComponent<SuicidalTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);
            creator.AddComponent(entity, new SuicideCharge
            {
                TriggerRadius = SuicideTriggerRadius,
                BlastRadius = SuicideBlastRadius,
                BlastDamage = SuicideBlastDamage,
                BloodAmount = SuicideBloodAmount,
            });

            // Armor profile is the whole design: it shrugs off arrows on the
            // approach but is soft to anything that reaches it.
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 4, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
