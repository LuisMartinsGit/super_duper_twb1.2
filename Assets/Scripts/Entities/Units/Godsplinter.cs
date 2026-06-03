// File: Assets/Scripts/Entities/Units/Godsplinter.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Godsplinter unit - massive crystal siege monster.
    /// Hybrid siege/ranged combat: melee siege damage to buildings (2x),
    /// multi-target laser barrage at range. Slow but extremely durable.
    /// No population cost - crystal faction uses crystal resource economy.
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

            if (TechCatalog.TryGetUnit("Godsplinter", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = GodsplinterPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            creator.AddComponent<CrystalTag>(entity);
            creator.AddComponent<CrystalUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = radius });
            creator.AddComponent(entity, new CrystalResourceValue { BuildCost = GodsplinterBuildCost });

            // Godsplinter-specific siege/laser state
            creator.AddComponent(entity, new GodsplinterState
            {
                LaserCooldownTimer = 0,
                SiegeCooldownTimer = 0,
                SiegeRange = GodsplinterSiegeRange,
                LaserRange = GodsplinterLaserRange,
                LaserMaxTargets = GodsplinterLaserMaxTargets,
                IsSieging = 0
            });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            creator.AddComponent(entity, new Defense { Melee = 10, Ranged = 8, Siege = 5, Magic = 5 });

            // Pre-allocate DesiredDestination — same race-fix as Crystalling.
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            return entity;
        }
    }
}
