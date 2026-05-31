// File: Assets/Scripts/Systems/Work/AutoConstructionSystem.cs
//
// Ticks UnderConstruction.Progress on buildings flagged with AutoConstructTag
// at 1 progress / real second, so they self-build without needing an idle
// builder on site. Currently the only consumer is the per-hub "Build Wall"
// action: the second (and onward) wall hubs and the wall instances along
// the segment are spawned with AutoConstructTag + UnderConstruction { Total = 30 }
// and finish ~30 s later with no builder dispatch.
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

namespace TheWaningBorder.Systems.Work
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(BuildingConstructionSystem))]
    public partial struct AutoConstructionSystem : ISystem
    {
        [BurstCompile]
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
            em.RemoveComponent<UnderConstruction>(site);
            em.RemoveComponent<AutoConstructTag>(site);
            if (em.HasComponent<Buildable>(site))
                em.RemoveComponent<Buildable>(site);

            if (em.HasComponent<Health>(site))
            {
                var hp = em.GetComponentData<Health>(site);
                hp.Value = hp.Max;
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
