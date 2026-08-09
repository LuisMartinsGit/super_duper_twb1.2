// File: Assets/GameData/TechTree/Buildings/Age 0/ShrineOfRidan/ShrineHealSystem.cs
// Shrine of Ridan / Temple of Ridan healing aura (Age 0 design):
// every completed Shrine/Temple heals friendly units within HealRadius by a
// percentage of their Max HP per second. The rate climbs the Shrine tech
// ladder (researched at the Shrine):
//   base                1% / s
//   HeightenedMasses    3% / s
//   PiousMasses         6% / s
//   FervoredMasses     15% / s
// Culture modifier: Runai +30%, Feraldis -30%, Alanthor/none neutral.
//
// Managed SystemBase: reads FactionResearchState + FactionColors (managed
// singletons). Ticks on a 1 s cadence per the design ("1 s ticks").

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ShrineHealSystem : SystemBase
    {
        private const float TickInterval = 1f;
        private const float HealRadius = 10f;

        private float _acc;
        private EntityQuery _shrineQuery;
        private EntityQuery _templeQuery;
        private EntityQuery _unitQuery;

        protected override void OnCreate()
        {
            _shrineQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShrineTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>());

            _templeQuery = GetEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>());

            _unitQuery = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.Exclude<DeathAnimationState>());
        }

        protected override void OnUpdate()
        {
            _acc += World.Time.DeltaTime;
            if (_acc < TickInterval) return;
            float tick = _acc;
            _acc = 0f;

            if (_shrineQuery.IsEmptyIgnoreFilter && _templeQuery.IsEmptyIgnoreFilter) return;

            var research = FactionResearchState.Instance;
            var em = EntityManager;

            using var units = _unitQuery.ToEntityArray(Allocator.Temp);
            using var unitFactions = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitXf = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var unitHp = _unitQuery.ToComponentDataArray<Health>(Allocator.Temp);
            if (units.Length == 0) return;

            HealFrom(_shrineQuery, research, em, units, unitFactions, unitXf, unitHp, tick);
            HealFrom(_templeQuery, research, em, units, unitFactions, unitXf, unitHp, tick);
        }

        private static void HealFrom(EntityQuery buildings, FactionResearchState research,
            EntityManager em, NativeArray<Entity> units, NativeArray<FactionTag> unitFactions,
            NativeArray<LocalTransform> unitXf, NativeArray<Health> unitHp, float tick)
        {
            if (buildings.IsEmptyIgnoreFilter) return;

            using var bEntities = buildings.ToEntityArray(Allocator.Temp);
            using var bFactions = buildings.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bXf = buildings.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = HealRadius * HealRadius;

            for (int b = 0; b < bFactions.Length; b++)
            {
                Faction faction = bFactions[b].Value;
                float rate = HealRateFor(research, faction);
                if (rate <= 0f) continue;

                // Shrine simple upgrade (design 2026-07-04): the aura itself
                // strengthens with the building level — +25% / +50%.
                if (em.HasComponent<BuildingUpgradeState>(bEntities[b]))
                {
                    int lv = em.GetComponentData<BuildingUpgradeState>(bEntities[b]).Level;
                    if (lv >= 2) rate *= 1.5f;
                    else if (lv == 1) rate *= 1.25f;
                }

                var pos = bXf[b].Position;
                for (int i = 0; i < units.Length; i++)
                {
                    if (unitFactions[i].Value != faction) continue;

                    var hp = em.GetComponentData<Health>(units[i]);
                    if (hp.Value <= 0 || hp.Value >= hp.Max) continue;

                    float dx = unitXf[i].Position.x - pos.x;
                    float dz = unitXf[i].Position.z - pos.z;
                    if (dx * dx + dz * dz > radiusSq) continue;

                    int heal = math.max(1, (int)math.round(hp.Max * rate * tick));
                    hp.Value = math.min(hp.Max, hp.Value + heal);
                    em.SetComponentData(units[i], hp);
                }
            }
        }

        /// <summary>Heal rate (% Max HP / s) from the Shrine tech ladder + culture modifier.</summary>
        private static float HealRateFor(FactionResearchState research, Faction faction)
        {
            float rate = 0.01f; // base 1%/s
            if (research != null)
            {
                if (research.HasResearched(faction, "FervoredMasses")) rate = 0.15f;
                else if (research.HasResearched(faction, "PiousMasses")) rate = 0.06f;
                else if (research.HasResearched(faction, "HeightenedMasses")) rate = 0.03f;
            }

            // Culture modifier: Runai +30%, Feraldis -30%.
            byte culture = FactionColors.GetFactionCulture(faction);
            if (culture == Cultures.Runai) rate *= 1.3f;
            else if (culture == Cultures.Feraldis) rate *= 0.7f;

            return rate;
        }
    }
}
