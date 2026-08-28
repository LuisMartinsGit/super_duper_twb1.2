using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Ledger — Alanthor Automaton (King's Court). Roams to allied economy
    /// buildings and "automates" them: +30% yield for 30 s, then the building is
    /// Under Automation (60 s lockout). Driven by the Automate Facility ability
    /// (auto-cast by AbilityAuraSystem). Non-combatant. Placeholder art (pid 250
    /// -> capsule) per the tree; stats are placeholders.
    /// </summary>
    public static class Ledger
    {
        public const int PresentationID = 250;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Ledger");
            float hp = def.hp;
            float speed = def.speed;
            float los = def.lineOfSight;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = 0 });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            // Fully autonomous: the player can select it to see it, but cannot
            // command it (NotControllableTag blocks LocalPlayer orders; the
            // AbilityAuraSystem steers it via CommandSource.AI). LedgerTag lets
            // the auto-cast AI find it.
            creator.AddComponent<LedgerTag>(entity);
            creator.AddComponent<NotControllableTag>(entity);
            string[] abilityNames = (def != null && def.abilities != null && def.abilities.Length > 0)
                ? def.abilities : new[] { "Automate Facility" };
            creator.AddComponent(entity, AbilityAssignment.Build(abilityNames));
            creator.AddComponent(entity, default(AbilityCooldowns));

            return entity;
        }
    }
}
