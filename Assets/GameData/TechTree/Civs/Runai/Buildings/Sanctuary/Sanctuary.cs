// Sanctuary.cs
// The Runai Sanctuary (docs/Design/Age_1_Runai.md §5): built atop a veilstone
// outcrop, Inactive or Cursed. It never drains the outcrop; it produces
// veilstone (Inactive) or veilsteel (Cursed) into its depot stock, pays a slow
// religion trickle, sleeps the curse node under it, and hosts the curse
// ladder research. Its production and the ladder's effects are
// RunaiSanctuarySystem; levels Sanctuary_Lvl1..3.

using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using Unity.Collections;
using Unity.Transforms;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class Sanctuary
    {
        public const string BuildingId = "Runai_Sanctuary";
        public const int PresentationID = 592;   // Sanctuary.asset (borrows the Veilsteel Foundry's model)

        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));

        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            // A research host (the curse ladder), so it carries a production queue.
            var e = RunaiBuildingBody.Base(c, BuildingId, PresentationID, p, f, trainer: true);
            c.AddComponent<RunaiSanctuaryTag>(e);
            c.AddComponent<BuildingUpgradeable>(e);
            c.AddComponent(e, new SanctuaryState { Outcrop = Entity.Null });
            return e;
        }
    
        static readonly ComponentType[] QT_VeilstoneMines =
        {
            ComponentType.ReadOnly<VeilstoneMineTag>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_VeilstoneMines;

        /// <summary>
        /// At a Runai age-up every Veilstone Mine the faction owns becomes a
        /// Sanctuary IN PLACE, on its outcrop (docs/Design/Age_1_Runai.md §2,
        /// §5.1): the Runai do not mine the curse's crystal. Same entity —
        /// its health fraction kept. Idempotent.
        /// </summary>
        public static void ConvertMinesForCulture(EntityManager em, Faction faction, byte culture)
        {
            if (culture != Cultures.Runai) return;
            var q = QC_VeilstoneMines.Get(em, QT_VeilstoneMines);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var def = TechCatalog.Building(BuildingId);
            var size = BuildingSizeConfig.GetSize(BuildingId);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var e = ents[i];
                em.RemoveComponent<VeilstoneMineTag>(e);
                if (em.HasComponent<BuildingUpgradeState>(e)) em.RemoveComponent<BuildingUpgradeState>(e);
                if (em.HasBuffer<ProductionQueueItem>(e)) em.GetBuffer<ProductionQueueItem>(e).Clear();
                else em.AddBuffer<ProductionQueueItem>(e);
                if (!em.HasComponent<ProductionState>(e))
                    em.AddComponentData(e, new ProductionState { Busy = 0, Remaining = 0 });
                if (!em.HasComponent<RallyPoint>(e))
                    em.AddComponentData(e, new RallyPoint { Position = em.GetComponentData<LocalTransform>(e).Position, Has = 0 });
                em.AddComponent<RunaiSanctuaryTag>(e);
                em.AddComponentData(e, new SanctuaryState { Outcrop = Entity.Null });
                if (!em.HasComponent<BuildingUpgradeable>(e)) em.AddComponent<BuildingUpgradeable>(e);
                em.SetComponentData(e, new PresentationId { Id = PresentationID });
                if (em.HasComponent<BuildingSize>(e))
                    em.SetComponentData(e, new BuildingSize { Width = size.x, Height = size.y });
                if (em.HasComponent<DisplayName>(e))
                    em.SetComponentData(e, new DisplayName { Value = DisplayNames.ForBuildingFixed(BuildingId) });
                if (em.HasComponent<Health>(e) && def.hp > 0)
                {
                    var h = em.GetComponentData<Health>(e);
                    float frac = h.Max > 0 ? (float)h.Value / h.Max : 1f;
                    int max = (int)def.hp;
                    em.SetComponentData(e, new Health { Value = math.max(1, (int)math.round(max * frac)), Max = max });
                }
            }
        }
    }
}
