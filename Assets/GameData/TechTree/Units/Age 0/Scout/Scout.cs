using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Scout unit - fast reconnaissance unit.
    /// High movement speed and line of sight, low combat stats.
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Scout
    {
        private const float DefaultHP = 40f;
        private const float DefaultSpeed = 6f;
        // Vision-only by default (Damage<=0 short-circuits TargetingSystem,
        // same gate as Litharch) — but the SO can arm the scout: with
        // def.damage > 0 it targets and fights like any melee unit.
        private const float DefaultDamage = 0f;
        private const float DefaultAttackCooldown = 1.0f;
        private const float DefaultLoS = 20f;
        private const int PresentationID = 206;

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
            float cooldown = DefaultAttackCooldown;
            float los = DefaultLoS;

            if (TechCatalog.TryGetUnit("Scout", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            // The ArmedScouts Hall research gates the attack: until it
            // completes, scouts spawn vision-only (Damage 0 short-circuits
            // TargetingSystem). TechEffectSystem.ApplyArmedScouts upgrades
            // scouts already alive when the research lands.
            bool armed = FactionResearchState.Instance != null &&
                         FactionResearchState.Instance.HasResearched(faction, "ArmedScouts");
            if (!armed) damage = 0;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Scout });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            // MeleeCombatSystem's query requires AttackCooldown — without it an
            // SO-armed scout acquires targets and swings but never deals damage.
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            // MovementSystem's query requires DesiredDestination. AIScoutingBehavior
            // sets it via ecb.SetComponent<DesiredDestination> — that path NREs
            // without the component baked in. AIScoutingBehavior is currently
            // [DisableAutoCreation] so the trap is dormant; baking the component
            // here defangs it. Mirrors Miner.cs:54-58. (task-062 G-3)
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            // Abilities from the SO 'abilities' field (fallback: Scout Sight). Use
            // Celestar is unlocked by the ScoutingCelestarii research.
            creator.AddComponent(entity, new ScoutSightState { BaseLos = los, LastHealth = (int)hp });
            var abilityList = new System.Collections.Generic.List<string>();
            if (def != null && def.abilities != null && def.abilities.Length > 0) abilityList.AddRange(def.abilities);
            else abilityList.Add("Scout Sight");
            bool celestar = FactionResearchState.Instance != null &&
                            FactionResearchState.Instance.HasResearched(faction, "ScoutingCelestarii");
            if (celestar && !abilityList.Contains("Use Celestar")) abilityList.Add("Use Celestar");
            creator.AddComponent(entity, AbilityAssignment.Build(abilityList.ToArray()));
            creator.AddComponent(entity, default(AbilityCooldowns));

            return entity;
        }
    }
}
