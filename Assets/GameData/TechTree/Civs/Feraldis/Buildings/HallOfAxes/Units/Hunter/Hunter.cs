using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis AXE THROWER (code id `Feraldis_Hunter`, kept for reference
    /// stability — the code always described this unit as an axe thrower and
    /// the 2026-08-05 rev.2 design makes that its name).
    ///
    /// Shorter range than the Archer, much more damage, and its landed shots
    /// inflict Bleeding — which per the design rule is damage over time AND
    /// blood on the ground, so a line of Axe Throwers paints the field its
    /// own army frenzies on. Never retreats (MinRange 0): it keeps throwing
    /// at point blank.
    /// </summary>
    public static class Hunter
    {
        private const int PresentationID = 338;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_Hunter");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float minRange = def.minAttackRange;
            float maxRange = def.attackRange;
            float cooldown = def.attackCooldown;
        

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            creator.AddComponent<ArcherTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Axe thrower state — MinRange=0 means never retreats, fights at point blank
            creator.AddComponent(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = def.aimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsRetreating = 0,
                IsFiring = 0
            });

            // The signature: every landed axe bleeds the target. Copied onto
            // each shot by RangedCombatSystem, so a thrown axe still bleeds
            // after its thrower dies.
            creator.AddComponent(entity, new InflictsBleed
            {
                DamagePerSecond = TheWaningBorder.Core.Config.FeraldisConstants.AxeBleedDamagePerSecond,
                Duration = TheWaningBorder.Core.Config.FeraldisConstants.AxeBleedDuration,
            });

            // Combat type tags — throwing axes are ranged per design doc.
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
