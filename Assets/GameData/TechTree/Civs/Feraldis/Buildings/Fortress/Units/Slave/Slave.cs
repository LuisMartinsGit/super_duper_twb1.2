using Unity.Entities;
using TheWaningBorder.Economy;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis_Slave — the Feraldis builder and economy body
    /// (docs/Design/Age_1_Feraldis.md §3). It IS a Worker (builds, repairs)
    /// plus <see cref="SlaveTag"/>: it staffs extractors and the Great Forge,
    /// feeds the Soulforge and can be sacrificed. Its stats are the
    /// Feraldis_Slave SO's; it costs no army population (the slave cap bounds
    /// it — FeraldisEconomy.SlaveCap).
    /// </summary>
    public static class FeraldisSlave
    {
        public const int PresentationID = 426;   // Slave.prefab (tinted Worker variant) on its SO

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var e = Worker.Create(em, position, faction);
            Stamp(em, e);
            return e;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var e = Worker.Create(ecb, position, faction);
            var def = TechCatalog.Unit("Feraldis_Slave");
            ecb.AddComponent<SlaveTag>(e);
            ecb.AddComponent<FeraldisWorkerTag>(e);
            ecb.AddComponent<FeraldisUnitTag>(e);
            ecb.SetComponent(e, new PopulationCost { Amount = def.populationCost });
            // ECB path: the ability is granted by the retrofit sweep once the
            // entity exists (FeraldisCultureRetrofitSystem).
            return e;
        }

        private static void Stamp(EntityManager em, Entity e)
        {
            var def = TechCatalog.Unit("Feraldis_Slave");
            em.AddComponent<SlaveTag>(e);
            if (!em.HasComponent<FeraldisWorkerTag>(e)) em.AddComponent<FeraldisWorkerTag>(e);
            if (!em.HasComponent<FeraldisUnitTag>(e)) em.AddComponent<FeraldisUnitTag>(e);
            em.SetComponentData(e, new PopulationCost { Amount = def.populationCost });
            if (em.HasComponent<Health>(e))
                em.SetComponentData(e, new Health { Value = (int)def.hp, Max = (int)def.hp });
            GrantFieldSacrifice(em, e);
        }

        public const string FieldSacrificeAbility = "Field Sacrifice";

        /// <summary>Every slave carries the Field Sacrifice active (docs/Design/Age_1_Feraldis.md §5).</summary>
        public static void GrantFieldSacrifice(EntityManager em, Entity e)
            => TheWaningBorder.Abilities.AbilityAssignment.AddAbility(
                em, e, TheWaningBorder.Abilities.AbilityCatalog.IndexOf(FieldSacrificeAbility));
    }
}
