using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstinger unit - dual-laser ranged glass cannon for the Border faction.
    /// Fires at up to two targets simultaneously. Very fragile but high damage.
    /// Uses VeilstingerState instead of ArcherState for dual-target tracking.
    /// No population cost - veilstone faction uses veilstone resource economy.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Veilstinger
    {
        /// <summary>
        /// Create Veilstinger using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Veilstinger using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float fireCooldown = 0f; // 0 = combat system's built-in constant
            int buildCost = VeilstingerBuildCost;
            var damageType = DamageType.Magic;
            var armorType = ArmorType.Ranged;
            var defense = new Defense { Melee = 3, Ranged = 2, Siege = 1, Magic = 2 };
            uint tagMask = (uint)UnitTagBits.Ranged;
            var bonusVs = default(BonusVsTags);

            var def = TechCatalog.Unit("Veilstinger");
            if (def.tags != null && def.tags.Length > 0) tagMask = UnitTagParse.Mask(def.tags);
            bonusVs = UnitTagParse.Bonus(def.bonusVsTags);
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float minRange = def.minAttackRange;
            float maxRange = def.attackRange;
            fireCooldown = def.attackCooldown;
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
        

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = VeilstingerPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            creator.AddComponent<BorderTag>(entity);
            creator.AddComponent<BorderUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new VeilstoneWorth { BuildCost = buildCost });

            // Veilstinger-specific dual-target state
            creator.AddComponent(entity, new VeilstingerState
            {
                Target1 = Entity.Null,
                Target2 = Entity.Null,
                AimTimer = 0,
                AimTimeRequired = VeilstingerAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                FireCooldown = fireCooldown,
                IsFiring = 0,
                NextGun = 0
            });

            // Combat type tags (fed from the unit's SO, see stat block above)
            creator.AddComponent(entity, new DamageTypeData { Value = damageType });
            creator.AddComponent(entity, new ArmorTypeData { Value = armorType });
            creator.AddComponent(entity, defense);
            creator.AddComponent(entity, new UnitTagsData { Mask = tagMask });
            if (!bonusVs.IsEmpty) creator.AddComponent(entity, bonusVs);

            // Pre-allocate DesiredDestination — same race-fix as Crystalling.
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            return entity;
        }
    }
}
