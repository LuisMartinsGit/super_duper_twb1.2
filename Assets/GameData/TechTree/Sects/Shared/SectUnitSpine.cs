// SectUnitSpine.cs
// Shared body for the sect units and sect heroes (docs/Design/Religion.md §4,
// Sects.md §4). Every number comes from the unit's SO (CLAUDE.md "Factories
// hold no numbers"); each unit file adds only its trait, each hero file its
// hero kit. UnitFactory's dispatcher then adds tags, bonus-vs-tags and the
// transient pre-add set, as for every unit.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class SectUnitSpine
    {
        /// <summary>The common unit body from the SO <paramref name="unitId"/>.
        /// A "ranged"/"magic" class unit gets the archer attack loop.</summary>
        public static Entity Create<TCreator>(TCreator creator, string unitId, int presentationId,
            float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit(unitId);
            bool ranged = def.IsRanged || def.attackRange > 2f;

            var e = creator.CreateEntity();
            creator.AddComponent(e, new PresentationId { Id = presentationId });
            creator.AddComponent(e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(e, new FactionTag { Value = faction });
            creator.AddComponent(e, new UnitTag { Class = ranged ? UnitClass.Ranged : UnitClass.Melee });
            creator.AddComponent(e, new Health { Value = (int)def.hp, Max = (int)def.hp });
            creator.AddComponent(e, new MoveSpeed { Value = def.speed });
            creator.AddComponent(e, new Damage { Value = (int)def.damage });
            creator.AddComponent(e, new AttackCooldown { Cooldown = def.attackCooldown, Timer = 0f });
            creator.AddComponent(e, new LineOfSight { Radius = def.lineOfSight });
            creator.AddComponent(e, new Target { Value = Entity.Null });
            creator.AddComponent(e, new Radius { Value = def.radius });
            creator.AddComponent(e, new PopulationCost { Amount = def.populationCost });
            creator.AddComponent(e, new DamageTypeData
            {
                Value = CombatTypeParse.Damage(def.damageType, ranged ? DamageType.Ranged : DamageType.Melee)
            });
            creator.AddComponent(e, new ArmorTypeData
            {
                Value = CombatTypeParse.Armor(def.armorType, ArmorType.InfantryHeavy)
            });
            var d = def.defense;
            creator.AddComponent(e, new Defense
            {
                Melee = d != null ? d.melee : 0,
                Ranged = d != null ? d.ranged : 0,
                Siege = d != null ? d.siege : 0,
                Magic = d != null ? d.magic : 0,
            });

            if (ranged)
            {
                creator.AddComponent<ArcherTag>(e);
                creator.AddComponent(e, new ArcherState
                {
                    AimTimer = 0,
                    AimTimeRequired = def.aimTime,
                    CooldownTimer = 0,
                    MinRange = def.minAttackRange,
                    MaxRange = def.attackRange,
                    IsRetreating = 0,
                    IsFiring = 0,
                });
            }
            return e;
        }

        /// <summary>
        /// The hero kit on top of the body: unique tag (heroes earn XP through
        /// UniqueUnitTag — HeroLevelSystem), the sect, the SO's abilities, and
        /// hero level 1 (Heroes.md §1).
        /// </summary>
        public static void AddHeroKit<TCreator>(TCreator creator, Entity e, string unitId,
            int uniqueKind, string sectId)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit(unitId);
            creator.AddComponent(e, new UniqueUnitTag { Kind = uniqueKind });
            creator.AddComponent(e, new SectHeroTag { SectIndex = (byte)SectConfig.IndexOf(sectId) });
            creator.AddComponent(e, AbilityAssignment.Build(def.abilities ?? System.Array.Empty<string>()));
            creator.AddComponent(e, default(AbilityCooldowns));
            creator.AddComponent(e, new HeroLevel { Value = HeroProgressionConfig.MinLevel });
            creator.AddComponent(e, new HeroExperience { Xp = 0 });
        }
    }
}
