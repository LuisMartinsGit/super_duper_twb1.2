// SiteLineOfSightSystem.cs
// A construction site sees only its own ground (docs/Design/Planned_Buildings.md
// §1.1, 2026-10-16). Every building is created with its finished line of
// sight; while it carries UnderConstruction its LineOfSight is held at
// half the footprint's diagonal, sqrt(width^2 + depth^2) / 2 metres, and the finished
// radius is kept aside in SiteSight. When construction ends (UnderConstruction
// removed, by whichever system completes it) the finished radius goes back.
//
// Anything that writes a NEW radius while the site stands — a level applied
// at age-up, say — is taken as the new finished radius, so nothing written
// during construction is lost.
//
// Simulation-side and deterministic: LineOfSight drives the fog grid, which
// the AI and gameplay read, so this runs in the lockstep simulation group on
// component data only.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;

/// <summary>The line of sight a site will have once it is finished.</summary>
public struct SiteSight : IComponentData
{
    public float BuiltRadius;
}

namespace TheWaningBorder.Systems.Work
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SiteLineOfSightSystem : SystemBase
    {
        static readonly ComponentType[] QT_NewSites =
        {
            ComponentType.ReadOnly<UnderConstruction>(),
            ComponentType.ReadOnly<BuildingTag>(),     // building sites only
            ComponentType.ReadWrite<LineOfSight>(),
            ComponentType.Exclude<SiteSight>(),
            ComponentType.Exclude<BorderTag>(),
        };
        static readonly ComponentType[] QT_Sites =
        {
            ComponentType.ReadOnly<UnderConstruction>(),
            ComponentType.ReadWrite<LineOfSight>(),
            ComponentType.ReadWrite<SiteSight>(),
        };
        static readonly ComponentType[] QT_Finished =
        {
            ComponentType.ReadWrite<SiteSight>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_NewSites, QC_Sites, QC_Finished;

        /// <summary>Radius differences below this are the site radius itself
        /// (float noise), not a new finished radius written by someone else.</summary>
        const float Same = 0.001f;

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // 1. New sites: remember the finished radius, hold the site radius.
            var fresh = QC_NewSites.Get(em, QT_NewSites);
            if (!fresh.IsEmptyIgnoreFilter)
            {
                using var ents = fresh.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    float built = em.GetComponentData<LineOfSight>(e).Radius;
                    em.AddComponentData(e, new SiteSight { BuiltRadius = built });
                    em.SetComponentData(e, new LineOfSight { Radius = SiteRadius(em, e) });
                }
            }

            // 2. Standing sites: a radius that is not the site radius was
            //    written by someone else — it becomes the finished radius.
            var sites = QC_Sites.Get(em, QT_Sites);
            if (!sites.IsEmptyIgnoreFilter)
            {
                using var ents = sites.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    float site = SiteRadius(em, e);
                    float now = em.GetComponentData<LineOfSight>(e).Radius;
                    if (math.abs(now - site) <= Same) continue;
                    em.SetComponentData(e, new SiteSight { BuiltRadius = now });
                    em.SetComponentData(e, new LineOfSight { Radius = site });
                }
            }

            // 3. Finished (or otherwise no longer a site): the real sight.
            var done = QC_Finished.Get(em, QT_Finished);
            if (!done.IsEmptyIgnoreFilter)
            {
                using var ents = done.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    float built = em.GetComponentData<SiteSight>(e).BuiltRadius;
                    if (em.HasComponent<LineOfSight>(e))
                        em.SetComponentData(e, new LineOfSight { Radius = built });
                    em.RemoveComponent<SiteSight>(e);
                }
            }
        }

        /// <summary>HALF the footprint's diagonal in metres — a Hut (4 x 4 m,
        /// 2 x 2 tiles) sees sqrt(2) tiles = 2.83 m, a circle just round its corners. Falls back to the
        /// building id's footprint, then to one build cell.</summary>
        static float SiteRadius(EntityManager em, Entity e)
        {
            int2 size;
            if (em.HasComponent<BuildingSize>(e))
            {
                var bs = em.GetComponentData<BuildingSize>(e);
                size = new int2(bs.Width, bs.Height);
            }
            else
            {
                string id = TheWaningBorder.Entities.BuildingIds.Of(e, em);
                size = string.IsNullOrEmpty(id) ? BuildingSizeConfig.SingleCellSize : BuildingSizeConfig.GetSize(id);
            }
            if (size.x <= 0 || size.y <= 0) size = BuildingSizeConfig.SingleCellSize;
            return 0.5f * math.sqrt(size.x * size.x + size.y * size.y);
        }
    }
}
