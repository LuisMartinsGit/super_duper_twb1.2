// File: Assets/Scripts/Systems/Buildings/BuildingCultureAutoLevelSystem.cs
// Auto-bumps Hall / Barracks / Hut to upgrade Level 1 the moment their
// owning faction picks a culture. Players don't pay or wait for L1 — it's
// the cultural baseline. Manual upgrades cover L2 and L3 only.
//
// Same trigger handles both:
//   1. Existing buildings at the moment age-up completes — every Hall /
//      Barracks / Hut on the map swaps to its L1 prefab in one wave.
//   2. New buildings constructed AFTER age-up — they spawn at L0 (base
//      prefab) for one frame, get auto-bumped to L1 next tick, and swap
//      to the cultured prefab right after construction completes.
//
// The bump captures base stats first (so re-applying is idempotent) then
// calls BuildingUpgradeSystem.ApplyLevel(em, e, 1) — same path manual
// upgrades take. The visual swap is done by BuildingPrefabSwapSystem on
// its next 0.5 s scan.
//
// Throttled to 0.5 s; the work per tick is one O(N) walk across
// upgradeable buildings — already-bumped buildings short-circuit on
// the level check.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(BuildingUpgradeSystem))]
    public partial struct BuildingCultureAutoLevelSystem : ISystem
    {
        private const float ScanInterval = 0.5f;
        private float _scanTimer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingUpgradeable>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _scanTimer += SystemAPI.Time.DeltaTime;
            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;

            var em = state.EntityManager;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingUpgradeable>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];

                // Buildings still being constructed or already in an upgrade
                // animation aren't eligible for the auto-bump. They'll be
                // picked up on a later tick.
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<BuildingUpgrading>(e)) continue;

                // Already at L1+? Nothing to do.
                if (em.HasComponent<BuildingUpgradeState>(e))
                {
                    if (em.GetComponentData<BuildingUpgradeState>(e).Level >= 1) continue;
                }

                // Faction must have picked a culture.
                var faction = em.GetComponentData<FactionTag>(e).Value;
                byte culture = FactionColors.GetFactionCulture(faction);
                if (culture == Cultures.None) continue;

                // Capture base stats once (same shape as the manual command
                // helper) so ApplyLevel can recompute scaled values from base.
                if (!em.HasComponent<BuildingUpgradeState>(e))
                {
                    int baseHp = em.HasComponent<Health>(e)
                        ? em.GetComponentData<Health>(e).Max : 0;
                    float baseAtkCd = em.HasComponent<BuildingRangedAttack>(e)
                        ? em.GetComponentData<BuildingRangedAttack>(e).Cooldown : 0f;
                    int basePop = em.HasComponent<PopulationProvider>(e)
                        ? em.GetComponentData<PopulationProvider>(e).Amount : 0;

                    em.AddComponentData(e, new BuildingUpgradeState
                    {
                        Level                  = 0,
                        BaseHpMax              = baseHp,
                        BaseAttackCooldown     = baseAtkCd,
                        BasePopulationProvider = basePop,
                    });
                }

                BuildingUpgradeSystem.ApplyLevel(em, e, 1);
                UnityEngine.Debug.Log(
                    $"[Upgrade] auto-L1 — {faction} entity {e.Index} (culture picked)");
            }
        }
    }
}
