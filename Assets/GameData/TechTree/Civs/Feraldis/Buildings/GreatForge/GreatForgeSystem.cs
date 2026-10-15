// GreatForgeSystem.cs
// Runs every Great Forge bay on a short fixed cycle on the lockstep clock.
// docs/Design/Age_1_Feraldis.md §4 (2026-10-12):
//
//   Soulforge   consumes the slaves crewing the Forge, one at a time → veilstone
//   Veilsteel   veilstone + iron → veilsteel
//   Arms Export veilsteel → iron (only iron: Feraldis supplies come from raiding)
//
// Speed = bay level rate × (1 + crew stoking bonus) × Draught Furnaces; inputs
// are discounted by the best Bellows tier held. A cycle the faction cannot
// afford is skipped whole and its progress kept.

using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class GreatForgeSystem : SystemBase
    {
        private static GreatForgeSystemConfig _cfg;
        public static GreatForgeSystemConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<GreatForgeSystemConfig>());

        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Forges =
        {
            ComponentType.ReadOnly<GreatForgeTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadWrite<ForgeBay>(),
        };
        static CachedEntityQuery QC_Forges;

        static readonly ComponentType[] QT_Crew =
        {
            ComponentType.ReadOnly<StaffAssignment>(),
            ComponentType.ReadOnly<SlaveTag>(),
        };
        static CachedEntityQuery QC_Crew;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            var c = Cfg;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Mathf.Max(0.1f, c.cycleSeconds))) return;
            var em = EntityManager;
            float minutes = c.cycleSeconds / 60f;

            var q = QC_Forges.Get(em, QT_Forges);
            using var forges = q.ToEntityArray(Allocator.Temp);
            for (int f = 0; f < forges.Length; f++)
            {
                var forge = forges[f];
                if (em.HasComponent<UnderConstruction>(forge)) continue;
                var faction = em.GetComponentData<FactionTag>(forge).Value;
                float speedShared = CrewSpeed(em, forge) * DraughtSpeed(faction);
                float keep = 1f - Discount(faction);

                int bayCount = em.GetBuffer<ForgeBay>(forge).Length;
                for (int i = 0; i < bayCount; i++)
                {
                    var bays = em.GetBuffer<ForgeBay>(forge);
                    var bay = bays[i];
                    if (bay.Recipe == ForgeRecipe.Hold) continue;
                    float rate = LevelRate(bay.Level) * speedShared * minutes;

                    switch (bay.Recipe)
                    {
                        case ForgeRecipe.Soulforge:
                            bay.Progress = Mathf.Min(bay.Progress + c.soulforgeSlavesPerMinute * rate, 1f);
                            if (bay.Progress >= 1f && TryTakeCrewSlave(em, forge))
                            {
                                bay.Progress -= 1f;
                                FactionEconomy.Add(em, faction, Cost.Of(veilstone: c.soulforgeVeilstonePerSlave), IncomeSource.Trade);
                            }
                            break;

                        case ForgeRecipe.Veilsteel:
                            bay.Progress += c.veilsteelOut * rate;
                            if (bay.Progress >= 1f && c.veilsteelOut > 0f)
                            {
                                int units = Mathf.FloorToInt(bay.Progress);
                                var spend = Cost.Of(
                                    veilstone: Mathf.CeilToInt(units * c.veilsteelInVeilstone / c.veilsteelOut * keep),
                                    iron: Mathf.CeilToInt(units * c.veilsteelInIron / c.veilsteelOut * keep));
                                if (FactionEconomy.Spend(em, faction, spend, SpendCategory.Trade))
                                {
                                    FactionEconomy.Add(em, faction, Cost.Of(veilsteel: units), IncomeSource.Trade);
                                    bay.Progress -= units;
                                }
                                else bay.Progress = Mathf.Min(bay.Progress, 1f);
                            }
                            break;

                        case ForgeRecipe.ArmsExport:
                            // Progress counts veilsteel consumed.
                            bay.Progress += c.exportInVeilsteel * rate;
                            if (bay.Progress >= 1f && c.exportInVeilsteel > 0f)
                            {
                                int steel = Mathf.FloorToInt(bay.Progress);
                                int spendSteel = Mathf.Max(1, Mathf.CeilToInt(steel * keep));
                                int iron = Mathf.FloorToInt(steel * c.exportOutIron / c.exportInVeilsteel);
                                if (iron > 0 && FactionEconomy.Spend(em, faction, Cost.Of(veilsteel: spendSteel), SpendCategory.Trade))
                                {
                                    FactionEconomy.Add(em, faction, Cost.Of(iron: iron), IncomeSource.Trade);
                                    bay.Progress -= steel;
                                }
                                else bay.Progress = Mathf.Min(bay.Progress, 1f);
                            }
                            break;
                    }
                    bays = em.GetBuffer<ForgeBay>(forge);   // re-fetch: TryTakeCrewSlave may change structure
                    bays[i] = bay;
                }
            }
        }

        public static float LevelRate(byte level)
        {
            var r = Cfg.bayLevelRate;
            if (r == null || r.Length == 0) return 1f;
            return r[Mathf.Clamp(level - 1, 0, r.Length - 1)];
        }

        /// <summary>1 + the stoking crew's bonus (fill × work multiplier).</summary>
        public static float CrewSpeed(EntityManager em, Entity forge)
        {
            if (!em.HasComponent<ExtractorCrew>(forge)) return 1f;
            var crew = em.GetComponentData<ExtractorCrew>(forge);
            float fill = crew.Slots > 0 ? Mathf.Clamp01(crew.Count / (float)crew.Slots) : 0f;
            return 1f + Cfg.crewSpeedAtFull * fill * Mathf.Max(1f, crew.WorkMultiplier);
        }

        public static float DraughtSpeed(Faction faction)
        {
            var r = FactionResearchState.Instance;
            var c = Cfg;
            return r != null && !string.IsNullOrEmpty(c.draughtTech) && r.HasResearched(faction, c.draughtTech)
                ? 1f + c.draughtSpeedPercent / 100f : 1f;
        }

        /// <summary>The best Bellows discount held, 0..1.</summary>
        public static float Discount(Faction faction)
        {
            var r = FactionResearchState.Instance;
            var c = Cfg;
            if (r == null || c.bellowsTechs == null) return 0f;
            float best = 0f;
            for (int i = 0; i < c.bellowsTechs.Length && i < c.bellowsDiscountPercent.Length; i++)
                if (r.HasResearched(faction, c.bellowsTechs[i])) best = Mathf.Max(best, c.bellowsDiscountPercent[i]);
            return Mathf.Clamp01(best / 100f);
        }

        /// <summary>Feed one crewing slave to the Soulforge (deterministic pick: lowest entity index).</summary>
        private static bool TryTakeCrewSlave(EntityManager em, Entity forge)
        {
            var q = QC_Crew.Get(em, QT_Crew);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var asg = q.ToComponentDataArray<StaffAssignment>(Allocator.Temp);
            float radius = FeraldisEconomy.Cfg.crewRadius;
            Entity pick = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
            {
                if (asg[i].Building != forge) continue;
                if (!em.HasComponent<LocalTransform>(ents[i])) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                if (TargetGeometry.SurfaceDistXZ(em, p, forge) > radius) continue;
                if (pick == Entity.Null || ents[i].Index < pick.Index) pick = ents[i];
            }
            if (pick == Entity.Null) return false;
            em.RemoveComponent<StaffAssignment>(pick);
            FeraldisSacrifice.KillOwn(em, pick);
            return true;
        }

        /// <summary>Per-minute spend and earn of one bay, for the panel.</summary>
        public static void PerMinute(EntityManager em, Entity forge, int bayIndex, out Cost spend, out Cost earn)
        {
            spend = default; earn = default;
            if (!em.HasBuffer<ForgeBay>(forge)) return;
            var bays = em.GetBuffer<ForgeBay>(forge, true);
            if (bayIndex < 0 || bayIndex >= bays.Length) return;
            var bay = bays[bayIndex];
            var c = Cfg;
            var faction = em.GetComponentData<FactionTag>(forge).Value;
            float rate = LevelRate(bay.Level) * CrewSpeed(em, forge) * DraughtSpeed(faction);
            float keep = 1f - Discount(faction);
            switch (bay.Recipe)
            {
                case ForgeRecipe.Soulforge:
                    earn = Cost.Of(veilstone: Mathf.RoundToInt(c.soulforgeSlavesPerMinute * rate * c.soulforgeVeilstonePerSlave));
                    break;
                case ForgeRecipe.Veilsteel:
                    spend = Cost.Of(veilstone: Mathf.RoundToInt(c.veilsteelInVeilstone * rate * keep),
                                    iron: Mathf.RoundToInt(c.veilsteelInIron * rate * keep));
                    earn = Cost.Of(veilsteel: Mathf.RoundToInt(c.veilsteelOut * rate));
                    break;
                case ForgeRecipe.ArmsExport:
                    spend = Cost.Of(veilsteel: Mathf.RoundToInt(c.exportInVeilsteel * rate * keep));
                    earn = Cost.Of(iron: Mathf.RoundToInt(c.exportOutIron * rate));
                    break;
            }
        }
    }
}
