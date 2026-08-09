// File: Assets/GameData/TechTree/Units/Border/Godsplinter/Godsplinter.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Godsplinter unit - massive veilstone siege monster.
    /// Hybrid siege/ranged combat: melee siege damage to buildings (2x),
    /// multi-target laser barrage at range. Slow but extremely durable.
    /// No population cost - veilstone faction uses veilstone resource economy.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Godsplinter
    {
        /// <summary>
        /// Create Godsplinter using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Godsplinter using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = GodsplinterHP;
            float speed = GodsplinterSpeed;
            float damage = GodsplinterDamage;
            float los = GodsplinterLoS;
            float radius = GodsplinterRadius;
            float laserRange = GodsplinterLaserRange;
            float laserCooldown = 0f; // 0 = BorderConstants.GodsplinterFireCooldown
            int buildCost = GodsplinterBuildCost;
            var damageType = DamageType.Siege;
            var armorType = ArmorType.InfantryHeavy;
            var defense = new Defense { Melee = 10, Ranged = 8, Siege = 5, Magic = 5 };
            float siegeRange = GodsplinterSiegeRange;
            float siegeCooldown = 0f; // 0 = combat system's built-in constant
            float aoeRadius = 0f;     // 0 = BorderConstants.GodsplinterAoeRadius
            uint tagMask = (uint)UnitTagBits.Siege;
            var bonusVs = default(BonusVsTags);

            if (TechCatalog.TryGetUnit("Godsplinter", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackRange > 0) laserRange = def.attackRange;
                if (def.attackCooldown > 0) laserCooldown = def.attackCooldown;
                if (def.siegeRange > 0) siegeRange = def.siegeRange;
                if (def.siegeCooldown > 0) siegeCooldown = def.siegeCooldown;
                if (def.aoeRadius > 0) aoeRadius = def.aoeRadius;
                if (def.tags != null && def.tags.Length > 0) tagMask = UnitTagParse.Mask(def.tags);
                bonusVs = UnitTagParse.Bonus(def.bonusVsTags);
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

            creator.AddComponent(entity, new PresentationId { Id = GodsplinterPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            creator.AddComponent<BorderTag>(entity);
            creator.AddComponent<BorderUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = radius });
            creator.AddComponent(entity, new VeilstoneWorth { BuildCost = buildCost });

            // Godsplinter-specific siege/laser state
            creator.AddComponent(entity, new GodsplinterState
            {
                LaserCooldownTimer = 0,
                SiegeCooldownTimer = 0,
                SiegeRange = siegeRange,
                LaserRange = laserRange,
                LaserCooldown = laserCooldown,
                SiegeCooldown = siegeCooldown,
                AoeRadius = aoeRadius,
                LaserMaxTargets = GodsplinterLaserMaxTargets,
                IsSieging = 0
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
