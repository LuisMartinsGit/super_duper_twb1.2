// BuildingTransientSeedSystem.cs
// ONE ARCHETYPE PER BUILDING FOR LIFE (2026-10-11).
//
// Units stopped minting archetypes on 2026-09-03 (TransientState.cs): their
// transient components are pre-added DISABLED by the factories and only the
// enable bit flips. Buildings never got that treatment, and the 0.0.40
// Mirror Marches match ended with 2,080 archetypes, 1,950 of them EMPTY — 541
// of the 730 archetypes the perf log captured were buildings. Every time a
// building first took a hit, fell under Heavy Bureaucracy, got a sect
// regeneration tail, a spell buff, an automation or yield boost, or started
// an upgrade, the component was ADDED, and every (building type x subset of
// those components) is a permanent archetype. Each structural change after
// that walks the bigger registry: the late-game slowdown the units' pass
// fixed, coming back through the buildings.
//
// This system gives every building, on the first tick it exists, every one
// of those components at once — the enableable ones DISABLED (TransientState:
// disabled reads as absent everywhere, WithNone included), BuildingDamageState
// stamped "last damaged long ago" so the out-of-combat repair checks read
// exactly as they did when it was absent. After that a building changes
// archetype only for what it genuinely becomes (finished, collapsing, a
// level stamp).
//
// SIM-side and tick-stepped (OrderFirst in SimulationSystemGroup): every
// lockstep peer adds the same components to the same buildings on the same
// tick, so chunk layout — and with it every query's iteration order — stays
// identical. A presentation-side stamp would fork the peers
// (project memory: frame-timed structural change).

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Systems.Buildings
{
    /// <summary>Marks a building whose transient components are in place.</summary>
    public struct BuildingTransientsSeeded : IComponentData { }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class BuildingTransientSeedSystem : SystemBase
    {
        /// <summary>"Last damaged" for a building never damaged: far enough
        /// back that every out-of-combat window reads as elapsed.</summary>
        private const double NeverDamaged = -1e9;

        private EntityQuery _unseeded;

        /// <summary>The enableable transients, pre-added disabled.</summary>
        private static readonly ComponentType[] Enableable =
        {
            ComponentType.ReadWrite<LastAttackerEntity>(),
            ComponentType.ReadWrite<LastDamagedByFaction>(),
            ComponentType.ReadWrite<DamageDealtTotal>(),
            ComponentType.ReadWrite<SectShutdown>(),
            ComponentType.ReadWrite<SectRegenTail>(),
            ComponentType.ReadWrite<SpellBuff>(),
            ComponentType.ReadWrite<VenerationFervor>(),
            ComponentType.ReadWrite<AutoYieldBoost>(),
            ComponentType.ReadWrite<UnderAutomation>(),
            ComponentType.ReadWrite<BuildingUpgrading>(),
            // Alanthor sect powers (Sects.md §4, 2026-10-15).
            ComponentType.ReadWrite<OvertimeBoost>(),
            ComponentType.ReadWrite<WardDischargeCharge>(),
            // Temporary fog reveal: a shooting tower, a claim-blocking
            // structure (Territory_Claims.md §3.1). With it the list plus
            // BuildingDamageState + the seeded tag is 15 — ComponentTypeSet's
            // limit: a further transient needs a second add.
            ComponentType.ReadWrite<FogReveal>(),
        };

        protected override void OnCreate()
        {
            _unseeded = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag>()
                .WithNone<BuildingTransientsSeeded>()
                .Build(this);
            RequireForUpdate(_unseeded);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            using var ents = _unseeded.ToEntityArray(Allocator.Temp);
            var missing = new FixedList128Bytes<ComponentType>();

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!em.Exists(e)) continue;

                missing.Clear();
                for (int c = 0; c < Enableable.Length; c++)
                    if (!em.HasComponent(e, Enableable[c])) missing.Add(Enableable[c]);
                bool needDamage = !em.HasComponent<BuildingDamageState>(e);
                if (needDamage) missing.Add(ComponentType.ReadWrite<BuildingDamageState>());
                missing.Add(ComponentType.ReadWrite<BuildingTransientsSeeded>());

                // One structural change per building, however many it lacked.
                em.AddComponent(e, new ComponentTypeSet(missing));

                // Freshly added enableables start ENABLED; as far as the game
                // is concerned they must not exist yet.
                for (int c = 0; c < Enableable.Length; c++)
                {
                    bool wasMissing = false;
                    for (int m = 0; m < missing.Length; m++)
                        if (missing[m] == Enableable[c]) { wasMissing = true; break; }
                    if (wasMissing) em.SetComponentEnabled(e, Enableable[c], false);
                }
                if (needDamage)
                    em.SetComponentData(e, new BuildingDamageState { LastDamagedAt = NeverDamaged });
            }
        }
    }
}
