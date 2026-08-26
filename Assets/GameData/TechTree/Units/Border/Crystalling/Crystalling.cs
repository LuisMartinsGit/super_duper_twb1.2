using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystalling unit - fast, weak melee veilstone swarm unit.
    /// Cheap veilstone-cost melee infantry for the Border faction (Faction.Border).
    /// No population cost - veilstone faction uses veilstone resource economy.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Crystalling
    {
        /// <summary>
        /// Create Crystalling using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Crystalling using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = CrystallingHP;
            float speed = CrystallingSpeed;
            float damage = CrystallingDamage;
            float los = CrystallingLoS;
            float cooldown = CrystallingAttackCooldown;
            float radius = CrystallingRadius;
            int buildCost = CrystallingBuildCost;
            var damageType = DamageType.Siege;
            var armorType = ArmorType.InfantryLight;
            var defense = new Defense { Melee = 2, Ranged = 1, Siege = 0, Magic = 1 };
            uint tagMask = (uint)(UnitTagBits.Infantry | UnitTagBits.Light);
            var bonusVs = default(BonusVsTags);

            if (TechCatalog.TryGetUnit("Crystalling", out var def))
            {
                if (def.tags != null && def.tags.Length > 0) tagMask = UnitTagParse.Mask(def.tags);
                bonusVs = UnitTagParse.Bonus(def.bonusVsTags);
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
                if (def.cost != null && def.cost.Veilstone > 0) buildCost = def.cost.Veilstone;
                damageType = CombatTypeParse.Damage(def.damageType, damageType);
                armorType = CombatTypeParse.Armor(def.armorType, armorType);
                if (def.defense != null)
                    defense = new Defense
                    {
                        Melee = def.defense.melee,
                        Ranged = def.defense.ranged,
                        Siege = def.defense.siege,
                        Magic = def.defense.magic
                    };
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = CrystallingPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent<BorderTag>(entity);
            creator.AddComponent<BorderUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = radius });
            creator.AddComponent(entity, new VeilstoneWorth { BuildCost = buildCost });

            // Combat type tags (fed from the unit's SO, see stat block above)
            creator.AddComponent(entity, new DamageTypeData { Value = damageType });
            creator.AddComponent(entity, new ArmorTypeData { Value = armorType });
            creator.AddComponent(entity, defense);
            creator.AddComponent(entity, new UnitTagsData { Mask = tagMask });
            if (!bonusVs.IsEmpty) creator.AddComponent(entity, bonusVs);

            // Pre-allocate DesiredDestination — see comment in EntityManager
            // overload above for the race rationale.
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            return entity;
        }
    }
}
