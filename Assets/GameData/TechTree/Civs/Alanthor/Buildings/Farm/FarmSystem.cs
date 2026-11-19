// FarmSystem.cs
// The Farm's pay (docs/Design/Age_1_Alanthor.md § The Farm, decisions 55 (c)
// and 57): every second each finished Farm adds Farm.SuppliesPerMinute —
// Workers inside x its level's suppliesPerWorkerPerMinute x the free share of
// its crop ring, rounded down — sixtieths of a supply to its own carry and
// pays the whole units into its owner's bank as IncomeSource.Farm (its own
// income line; the Vault's full bars raise it like any income). An
// unfinished Farm, or one with nobody inside, pays nothing.
//
// ITS OWN HANDS (decision 83): the same tick grows each Farm's Workers
// (GrowFarmhands) before it pays.
//
// THE FIELD: every FieldSeconds (and at once for a Farm never computed) each
// Farm — sites included, so the UI can show theirs — recomputes its crop
// ring's free share (FarmRing.FreePermille, a fixed lattice, integer
// per-mille) into FarmField. Never per frame.
//
// Lockstep: SimCadence clock (epoch-reset, so nothing carries in from the
// menu or a previous match), integer arithmetic, the carry and the field on
// the Farm entity, and Farms visited in entity-index order.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Research;
using TheWaningBorder.Systems.Training;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FarmSystem : SystemBase
    {
        /// <summary>The pay clock, seconds (engine-side, as CountingHouseSystem).</summary>
        private const float TickSeconds = 1f;
        /// <summary>How often every Farm's field is recomputed, in pay ticks
        /// (engine-side cadence, not balance).</summary>
        private const int FieldEveryTicks = 2;

        private int _fieldTick;

        private SimCadence.Periodic _tick;
        private readonly List<Entity> _farms = new List<Entity>();

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<FarmTag>()));
        }

        protected override void OnUpdate()
        {
            if (!_tick.Due(SystemAPI.Time.DeltaTime, TickSeconds)) return;
            var em = EntityManager;

            RefreshFields(em);

            _farms.Clear();
            foreach (var (_, e) in SystemAPI.Query<RefRO<FarmPay>>().WithAll<FarmTag>()
                         .WithNone<UnderConstruction>().WithEntityAccess())
                _farms.Add(e);
            if (_farms.Count == 0) return;
            _farms.Sort((a, b) => a.Index.CompareTo(b.Index));

            GrowFarmhands(em);

            for (int i = 0; i < _farms.Count; i++)
            {
                var farm = _farms[i];
                if (!em.Exists(farm) || !em.HasComponent<FactionTag>(farm)) continue;
                if (em.HasComponent<Health>(farm) && em.GetComponentData<Health>(farm).Value <= 0) continue;
                int perMinute = Farm.SuppliesPerMinute(em, farm);
                if (perMinute <= 0) continue;

                var pay = em.GetComponentData<FarmPay>(farm);
                pay.SuppliesSixtieths += perMinute;
                int whole = pay.SuppliesSixtieths / 60;
                pay.SuppliesSixtieths %= 60;
                em.SetComponentData(farm, pay);
                if (whole > 0)
                    FactionEconomy.Add(em, em.GetComponentData<FactionTag>(farm).Value,
                        Cost.Of(supplies: whole), IncomeSource.Farm);
            }
        }

        /// <summary>
        /// THE FARM'S OWN HANDS (decision 83, Age_1_Alanthor.md § The Farm):
        /// every pay tick each finished, living Farm with a free slot counts a
        /// second toward its next Worker; at Farm.asset garrisonWorkerSeconds
        /// one is born straight into the slot, free, if its owner has
        /// population room (else the clock waits at the mark). A full Farm's
        /// clock is reset, so a slot that frees up takes the whole interval.
        /// Structural (units created and absorbed): runs on the copied list.
        /// </summary>
        private void GrowFarmhands(EntityManager em)
        {
            int need = (int)System.Math.Ceiling(TechCatalog.Building(Farm.BuildingId).garrisonWorkerSeconds);
            if (need <= 0) return;
            _bornThisTick.Clear();
            for (int i = 0; i < _farms.Count; i++)
            {
                var farm = _farms[i];
                if (!em.Exists(farm) || !em.HasComponent<FactionTag>(farm)) continue;
                if (em.HasComponent<Health>(farm) && em.GetComponentData<Health>(farm).Value <= 0) continue;
                // A Farm from a save made before decision 83 has no clock yet.
                if (!em.HasComponent<FarmWorkerClock>(farm)) em.AddComponentData(farm, default(FarmWorkerClock));
                var clock = em.GetComponentData<FarmWorkerClock>(farm);
                if (!WallGarrison.HasFreeSlot(em, farm))
                {
                    if (clock.Seconds != 0) em.SetComponentData(farm, new FarmWorkerClock());
                    continue;
                }
                if (clock.Seconds < need) clock.Seconds++;
                if (clock.Seconds >= need)
                {
                    var faction = em.GetComponentData<FactionTag>(farm).Value;
                    _bornThisTick.TryGetValue(faction, out int born);
                    int pop = WorkerPopulation();
                    if (TrainingSystem.HasPopulationCapacity(em, faction, pop, born)
                        && BearWorker(em, farm, faction))
                    {
                        _bornThisTick[faction] = born + pop;
                        clock.Seconds = 0;
                    }
                }
                em.SetComponentData(farm, clock);
            }
        }

        private static int WorkerPopulation()
            => System.Math.Max(1, TechCatalog.Unit("Worker").populationCost);

        /// <summary>One Worker, made exactly as a trained one (the factory,
        /// then the completed research and spawn passives), put in the Farm's
        /// first free slot.</summary>
        private static bool BearWorker(EntityManager em, Entity farm, Faction faction)
        {
            var at = em.GetComponentData<Unity.Transforms.LocalTransform>(farm).Position;
            var worker = UnitFactory.Create(em, "Worker", at, faction);
            if (worker == Entity.Null) return false;
            TechEffectSystem.ApplyCompletedTechEffects(em, worker, faction);
            TheWaningBorder.Abilities.AlanthorActiveHelper.ApplySpawnPassives(em, worker, faction, "Worker");
            WallGarrison.Enter(em, worker, farm);
            return true;
        }

        private readonly Dictionary<Faction, int> _bornThisTick = new Dictionary<Faction, int>();

        /// <summary>Recompute the crop fields: every Farm on the cadence, and
        /// any Farm whose field was never computed at once.</summary>
        private void RefreshFields(EntityManager em)
        {
            if (SimCadence.Epoch != _fieldEpoch) { _fieldEpoch = SimCadence.Epoch; _fieldTick = 0; }
            bool all = (_fieldTick++ % FieldEveryTicks) == 0;

            _fieldFarms.Clear();
            foreach (var (_, e) in SystemAPI.Query<RefRO<FarmPay>>().WithAll<FarmTag, BuildingTag>().WithEntityAccess())
                _fieldFarms.Add(e);
            if (_fieldFarms.Count == 0) return;
            _fieldFarms.Sort((a, b) => a.Index.CompareTo(b.Index));

            for (int i = 0; i < _fieldFarms.Count; i++)
            {
                var farm = _fieldFarms[i];
                if (!em.Exists(farm)) continue;
                // A Farm from a save made before decision 57 has no field yet.
                if (!em.HasComponent<FarmField>(farm)) em.AddComponentData(farm, default(FarmField));
                var field = em.GetComponentData<FarmField>(farm);
                if (!all && field.Computed != 0) continue;
                field.FreePermille = FarmRing.FreePermille(em, farm);
                field.Computed = 1;
                em.SetComponentData(farm, field);
            }
        }

        private int _fieldEpoch = -1;
        private readonly List<Entity> _fieldFarms = new List<Entity>();
    }
}
