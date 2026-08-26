// File: Assets/GameData/TechTree/Units/Feraldis/Plunderer/Plunderer.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Plunderer — the Raider Camp's output and the entire Feraldis
    /// economy. Free, uncontrollable, and deliberately pathetic in a fight:
    /// 45 HP and 3 damage. It is not a threat, it is a tax collector.
    ///
    /// Its value is <see cref="PlunderPurse"/>: while it is actually raiding
    /// (engaging an enemy, outside its owner's influence AND outside the
    /// curse's), it drains the victim's bank into its owner's. Feraldis does
    /// not gather — it takes.
    ///
    /// Design: docs/Design/Age_1_Feraldis.md § Raider Camp (2026-08-05 rev.3).
    /// </summary>
    public static class Plunderer
    {
        /// <summary>
        /// ONE hit point (2026-08-05 PM). Anything at all kills a Plunderer —
        /// a stray arrow, one swing, a single tick of curse exposure. In
        /// exchange it gets a two-second berserk before it drops
        /// (FeraldisDeathInterceptor). The raid economy was winning games on
        /// its own; the raiders themselves now have to be *escorted* to
        /// survive contact rather than tanking their way through it.
        /// </summary>
        private const float DefaultHP = 1f;
        private const float DefaultSpeed = 6.5f;
        private const float DefaultDamage = 3f;
        private const float DefaultLoS = 16f;
        private const float DefaultCooldown = 1.5f;
        private const float DefaultRadius = 0.4f;
        public const int PresentationID = 341;   // shares the raider visual

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

            if (TechCatalog.TryGetUnit("Feraldis_Plunderer", out var def))
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
            // FREE in every sense — costs no population, so camps can run at
            // full cap without competing with the player's real army.
            creator.AddComponent(entity, new PopulationCost { Amount = 0 });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            // Born with an (empty) destination so the patrol driver only ever
            // has to SET it — no structural change on a unit that spawns
            // continuously. Cheap insurance for the hottest spawn path in
            // the game.
            creator.AddComponent(entity, new DesiredDestination { Has = 0 });
            creator.AddComponent<PlundererTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);
            // Reuses the existing uncontrollable-raider aggression driver.
            creator.AddComponent<FeraldisRaiderTag>(entity);
            creator.AddComponent<NotControllableTag>(entity);
            creator.AddComponent(entity, new PlunderPurse());

            return entity;
        }
    }
}
