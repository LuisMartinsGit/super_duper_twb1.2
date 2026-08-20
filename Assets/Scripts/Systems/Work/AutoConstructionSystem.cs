// File: Assets/Scripts/Systems/Work/AutoConstructionSystem.cs
//
// Ticks UnderConstruction.Progress on buildings flagged with AutoConstructTag
// at 1 progress / real second, so they self-build without needing an idle
// builder on site. Consumers:
//   - the per-hub "Build Wall" action: the second (and onward) wall hubs and
//     the wall instances along the segment are spawned with AutoConstructTag
//     + UnderConstruction { Total = 30 } and finish ~30 s later.
//   - the three choice buildings (Shrine / Vault / Keep): placed from the
//     top-bar special-building buttons with Total = 90. Builders sent to the
//     site ACCELERATE the build (+0.25 progress/s each on top of this
//     system's 1.0/s — see BuildingConstructionSystem), so 4 workers halve
//     the timer.
//
// Completion mirrors the minimal subset of BuildingConstructionSystem.CompleteConstruction
// that wall hubs / instances actually need:
//   - remove UnderConstruction (+ Buildable if present)
//   - restore Health to Max
//   - apply DeferredDefense if present
// Wall hubs / segments don't trigger any of the BuildingConstructionSystem
// special-case finalisers (ShrineTag RP grant, GathererHut income setup,
// Feraldis raider spawn), so we don't need to duplicate that branch.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Work
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(BuildingConstructionSystem))]
    public partial struct AutoConstructionSystem : ISystem
    {
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AutoConstructTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot candidate entities — we make structural changes on
            // completion (RemoveComponent<UnderConstruction>, RemoveComponent<AutoConstructTag>)
            // which would invalidate live SystemAPI.Query iterators if we
            // mutated mid-walk.
            var query = SystemAPI.QueryBuilder()
                .WithAll<AutoConstructTag, UnderConstruction>()
                .Build();
            using var sites = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < sites.Length; i++)
            {
                Entity site = sites[i];
                if (!em.Exists(site)) continue;
                if (!em.HasComponent<UnderConstruction>(site)) continue;

                var uc = em.GetComponentData<UnderConstruction>(site);
                uc.Progress += dt;

                if (uc.Progress >= uc.Total)
                {
                    Complete(em, site);
                }
                else
                {
                    // HP-as-delta — same pattern as BuildingConstructionSystem so
                    // combat damage taken mid-construction survives. (task-062 Q-23)
                    if (em.HasComponent<Health>(site))
                    {
                        var hp = em.GetComponentData<Health>(site);
                        float ratio = math.clamp(uc.Progress / uc.Total, 0f, 1f);
                        int newProgressHp = math.max(1, (int)math.round(hp.Max * ratio));
                        int delta = newProgressHp - uc.LastProgressHp;
                        if (delta != 0)
                        {
                            hp.Value = math.clamp(hp.Value + delta, 1, hp.Max);
                            em.SetComponentData(site, hp);
                        }
                        uc.LastProgressHp = newProgressHp;
                    }
                    em.SetComponentData(site, uc);
                }
            }
        }

        private static void Complete(EntityManager em, Entity site)
        {
            // Progress-HP watermark, read before the component goes — the
            // health step below needs it to tell build progress apart from
            // combat damage. Mirrors BuildingConstructionSystem.
            int lastProgressHp = em.HasComponent<UnderConstruction>(site)
                ? em.GetComponentData<UnderConstruction>(site).LastProgressHp
                : 0;

            em.RemoveComponent<UnderConstruction>(site);
            em.RemoveComponent<AutoConstructTag>(site);

            // Post-game chart milestone: choice building completed via the
            // self-build path (mirrors BuildingConstructionSystem).
            if (em.HasComponent<ChoiceBuildingTag>(site) && em.HasComponent<FactionTag>(site))
                TheWaningBorder.UI.HUD.GameStatsTracker.RecordEvent(
                    em.GetComponentData<FactionTag>(site).Value,
                    TheWaningBorder.UI.HUD.GameEventKind.SpecialBuilding);
            if (em.HasComponent<Buildable>(site))
                em.RemoveComponent<Buildable>(site);

            // Choice buildings self-build through this system, so the Shrine's
            // one-time +1 Religion Point grant must fire here too — the
            // BuildingConstructionSystem path only runs when a builder lands
            // the finishing tick. (Mirrors CompleteConstruction's ShrineTag
            // branch; TryAwardShrineBonus latches per faction, so a builder
            // finish followed by this path can't double-grant.)
            if (em.HasComponent<ShrineTag>(site) && em.HasComponent<FactionTag>(site))
            {
                var faction = em.GetComponentData<FactionTag>(site).Value;
                FactionReligionPointsHelper.TryAwardShrineBonus(em, faction);
            }

            // Finish the HP ramp WITHOUT healing combat damage — add only the
            // progress still owed. Slamming to Max here undid the per-tick
            // delta the loop above keeps precisely so mid-build damage
            // survives, so a site nearly razed while building popped out
            // pristine. Mirrors BuildingConstructionSystem.CompleteConstruction.
            if (em.HasComponent<Health>(site))
            {
                var hp = em.GetComponentData<Health>(site);
                int remainingProgress = hp.Max - lastProgressHp;
                hp.Value = math.clamp(hp.Value + math.max(0, remainingProgress), 1, hp.Max);
                em.SetComponentData(site, hp);
            }

            // Safety net: a construction scale tween (if any) leaves Scale != 1.
            // Mirrors BuildingConstructionSystem.CompleteConstruction.
            if (em.HasComponent<LocalTransform>(site))
            {
                var lt = em.GetComponentData<LocalTransform>(site);
                lt.Scale = 1f;
                em.SetComponentData(site, lt);
            }

            if (em.HasComponent<DeferredDefense>(site))
            {
                var def = em.GetComponentData<DeferredDefense>(site);
                var d = new Defense
                {
                    Melee = def.Melee,
                    Ranged = def.Ranged,
                    Siege = def.Siege,
                    Magic = def.Magic
                };
                if (em.HasComponent<Defense>(site)) em.SetComponentData(site, d);
                else em.AddComponentData(site, d);
                em.RemoveComponent<DeferredDefense>(site);
            }
        }
    }
}
