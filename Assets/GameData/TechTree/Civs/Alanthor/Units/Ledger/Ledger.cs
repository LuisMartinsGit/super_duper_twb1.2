using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Ledger — Alanthor's automaton hero (trained at the Fortress, one per player).
    /// A hero like any other (docs/Design/Heroes.md §1): it fights, takes orders and
    /// levels from kills. Automate Facility casts itself on economy buildings in sight.
    /// </summary>
    public static class Ledger
    {
        public const int PresentationID = 250;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EmCreator(em), position, faction));

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => EntityBuild.Done(CreateInternal(new EcbCreator(ecb), position, faction));

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Ledger");
            float hp = def.hp;
            float speed = def.speed;
            float los = def.lineOfSight;
            float damage = def.damage;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = def.attackCooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = def.populationCost });
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            var d = def.defense;
            creator.AddComponent(entity, new Defense
            {
                Melee = d != null ? d.melee : 0, Ranged = d != null ? d.ranged : 0,
                Siege = d != null ? d.siege : 0, Magic = d != null ? d.magic : 0,
            });

            creator.AddComponent<LedgerTag>(entity);
            creator.AddComponent(entity, new UniqueUnitTag { Kind = UniqueUnitKind.Ledger });
            creator.AddComponent(entity, new HeroLevel { Value = HeroProgressionConfig.MinLevel });
            creator.AddComponent(entity, new HeroExperience { Xp = 0 });
            string[] abilityNames = (def != null && def.abilities != null && def.abilities.Length > 0)
                ? def.abilities : new[] { "Automate Facility" };
            creator.AddComponent(entity, AbilityAssignment.Build(abilityNames));
            creator.AddComponent(entity, default(AbilityCooldowns));

            return entity;
        }
    }
}
