using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis War Chariot — heavy cavalry that LEAVES A TRAIL OF BLOOD as
    /// it moves (design 2026-08-05 rev.2; replaces the retired Warboar
    /// Rider).
    ///
    /// Strategically it answers the culture's opening problem: every other
    /// route to blood needs something to die first, so a fresh Feraldis
    /// player has no frenzy ground and nowhere to plant a War Totem. A
    /// Chariot can ride out and MANUFACTURE totem ground on the way. That is
    /// why it costs 2 pop and sits behind the Pasture's second level.
    /// </summary>
    public static class WarChariot
    {
        private const float DefaultHP = 180f;
        private const float DefaultSpeed = 7.0f;
        private const float DefaultDamage = 16f;
        private const float DefaultLoS = 20f;
        private const float DefaultCooldown = 1.5f;
        private const float DefaultRadius = 0.6f;
        public const int PresentationID = 339;   // shares the retired Warboar visual

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
            float cooldown = DefaultCooldown;

            if (TechCatalog.TryGetUnit("Feraldis_WarChariot", out var def))
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
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 2 });
            creator.AddComponent<CavalryTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);

            // The signature — BloodTrailSystem paints as it moves.
            creator.AddComponent(entity, new BloodTrail
            {
                BloodPerSecond = ChariotTrailBloodPerSecond,
                MinStep = ChariotTrailMinStep,
                LastPos = position,
                HasLast = 0,
            });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Cavalry });
            creator.AddComponent(entity, new Defense { Melee = 1, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
