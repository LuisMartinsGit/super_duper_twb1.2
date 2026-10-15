// FeraldisTechStampSystem.cs
// Feraldis research that changes what a unit IS rather than a number on it,
// stamped on every matching unit — standing and newly trained — once a second
// (docs/Design/Age_1_Feraldis.md §7.6):
//   Fire on the Move  Corpse Engines and Harpoon Rigs fire while driving
//   Ride-By           Raiders strike without stopping
//   Blood-Burners     every Feraldis cavalry unit gets the Blood-Burners active

using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisTechStampSystem : SystemBase
    {
        private SimCadence.Periodic _cadence;
        public const string BloodBurnersAbility = "Blood-Burners";

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, 1f)) return;
            var em = EntityManager;
            var add = new NativeList<Entity>(Allocator.Temp);

            foreach (var (fac, e) in SystemAPI.Query<RefRO<FactionTag>>().WithAny<CorpseEngineTag, HarpoonRigTag>()
                         .WithNone<FiresOnMove>().WithEntityAccess())
                if (FeraldisCombat.Researched(fac.ValueRO.Value, "Feraldis_FireOnTheMove")) add.Add(e);
            foreach (var (fac, e) in SystemAPI.Query<RefRO<FactionTag>>().WithAll<FeraldisRaiderTag>()
                         .WithNone<FiresOnMove, NotControllableTag>().WithEntityAccess())
                if (FeraldisCombat.Researched(fac.ValueRO.Value, "Feraldis_RideBy")) add.Add(e);
            for (int i = 0; i < add.Length; i++) em.AddComponent<FiresOnMove>(add[i]);
            add.Clear();

            int burners = AbilityCatalog.IndexOf(BloodBurnersAbility);
            if (burners >= 0)
            {
                uint cav = UnitTagParse.Tag("cavalry");
                foreach (var (fac, tags, e) in SystemAPI.Query<RefRO<FactionTag>, RefRO<UnitTagsData>>()
                             .WithAll<FeraldisUnitTag>().WithEntityAccess())
                {
                    if ((tags.ValueRO.Mask & cav) == 0) continue;
                    if (!FeraldisCombat.Researched(fac.ValueRO.Value, "Feraldis_BloodBurners")) continue;
                    if (em.HasComponent<UnitAbilities>(e))
                    {
                        var ua = em.GetComponentData<UnitAbilities>(e);
                        if (ua.S0 == burners || ua.S1 == burners || ua.S2 == burners || ua.S3 == burners) continue;
                    }
                    add.Add(e);
                }
                for (int i = 0; i < add.Length; i++) AbilityAssignment.AddAbility(em, add[i], burners);
            }
            add.Clear();

            // Overcharge: the War Rig gains the active once researched.
            int over = AbilityCatalog.IndexOf(OverchargeAbility);
            if (over >= 0)
            {
                foreach (var (fac, e) in SystemAPI.Query<RefRO<FactionTag>>().WithAll<WarRigTag>().WithEntityAccess())
                    if (FeraldisCombat.Researched(fac.ValueRO.Value, "Feraldis_Overcharge")) add.Add(e);
                for (int i = 0; i < add.Length; i++) AbilityAssignment.AddAbility(em, add[i], over);
            }
            add.Dispose();
        }

        public const string OverchargeAbility = "Overcharge";
    }
}
