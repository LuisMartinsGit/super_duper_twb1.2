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
        // Vision-only by default (Damage<=0 short-circuits TargetingSystem,
        // same gate as Litharch) — but the SO can arm the scout: with
        // def.damage > 0 it targets and fights like any melee unit.
        private const int PresentationID = 206;
        private const string ArmedScoutsTech = "ArmedScouts";

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Scout");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float cooldown = def.attackCooldown;
            float los = def.lineOfSight;

            // The ArmedScouts research arms the Scout (2026-10-03, unification
            // item 13). The Scout SO's damage is 0 — vision-only, which
            // short-circuits TargetingSystem — and the damage is the TECH's
            // data: ArmedScouts.asset SETs it (`unit:Scout Damage Set 2`).
            // Read here so EVERY spawn path comes out armed once researched;
            // this used to read the Scout SO's own damage, which is 0, so new
            // Scouts came out unarmed. Scouts alive when the research lands are
            // armed by TechEffectSystem's generic sweep from the same entry.
            bool armed = FactionResearchState.Instance != null &&
                         FactionResearchState.Instance.HasResearched(faction, ArmedScoutsTech);
            if (armed)
                damage = math.max(damage, TechCatalog.TechEffect(ArmedScoutsTech, "Damage"));

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
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = def.populationCost });
            // MovementSystem's query requires DesiredDestination. AIScoutingBehavior
            // sets it via ecb.SetComponent<DesiredDestination> — that path NREs
            // without the component baked in. AIScoutingBehavior is currently
            // [DisableAutoCreation] so the trap is dormant; baking the component
            // here defangs it. Mirrors Worker.cs:54-58. (task-062 G-3)
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
