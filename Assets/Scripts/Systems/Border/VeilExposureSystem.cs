// VeilExposureSystem.cs
// §2.5b — the curse as HOSTILE GROUND (2026-08-03 rev.2). The crust is
// walkable; this system makes it cost you:
//
//   * DEBUFF   — units on crust carry the depth-scaled BorderDebuff
//                (speed/att/def), exactly like the old wall model's debuff.
//   * EXPOSURE — units on crust accrue ExposureState seconds; past the
//                grace window they take saturation-scaled damage over time.
//                Crossing a thin finger is free; marching deep crust is a
//                toll; camping in it is lethal. Exposure recovers (faster)
//                off-crust. The grace + thin-haze scaling means base-ring
//                haze essentially cannot kill a worker — early neglect
//                costs tempo, not corpses.
//   * CRUMBLE  — a completed building standing in DEEP crust (engulfed by
//                later growth) slowly loses HP: the loud, slow, savable
//                local backstop for total neglect.
//
// No target selection anywhere: every unit pays for exactly the ground its
// owner chose to put it on — the §2.5b fairness contract.
//
// Kills go through the NORMAL death pipeline (Health -> 0, DeathSystem
// destroys) — never DestroyEntity here (see the unit-death contract).
//
// Mutually exclusive with the retired CrustPhysical wall model (whose
// debuff/catch path lives in VeilFieldSystem.ApplyCrustDebuffs): this
// system only enables when ExposureEnabled && !CrustPhysical, so the two
// can never fight over the shared BorderDebuff/VeilDebuffTag.
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VeilExposureSystem : SystemBase
    {
        private const float TickInterval = 1f;

        // Same debuff ladder the wall model used (kept in lockstep with
        // VeilFieldSystem's values by design — one knob when tuning).
        private static readonly BorderDebuff CrustDebuff =
            new BorderDebuff { DefPenalty = 0.15f, AttPenalty = 0.15f, SpeedPenalty = 0.2f };
        private static readonly BorderDebuff DeepDebuff =
            new BorderDebuff { DefPenalty = 0.3f, AttPenalty = 0.3f, SpeedPenalty = 0.35f };

        private SimCadence.Periodic _acc;
        private EntityQuery _unitQuery;
        private EntityQuery _buildingQuery;
        private EntityQuery _hallQuery; // flee targets for exposed workers

        protected override void OnCreate()
        {
            Enabled = ExposureEnabled && !CrustPhysical;
            if (!Enabled) return;
            RequireForUpdate<VeilField>();

            _unitQuery = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            _buildingQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag, LocalTransform, Health>()
                .WithNone<UnderConstruction>()
                .Build(this);
            _hallQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HallTag, FactionTag, LocalTransform>()
                .WithNone<UnderConstruction>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            if (!_acc.Due(SystemAPI.Time.DeltaTime, TickInterval)) return;

            var field = SystemAPI.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return;

            var em = EntityManager;
            TickUnits(em, in field);
            TickBuildings(em, in field);
        }

        /// <summary>Exposure damage/s for a saturation value: linear from
        /// ExposureDpsMin at CrustThreshold to ExposureDpsMax at 255.</summary>
        private static float DpsFor(byte sat)
        {
            float t = (sat - VeilField.CrustThreshold)
                / (float)(255 - VeilField.CrustThreshold);
            return math.lerp(ExposureDpsMin, ExposureDpsMax, math.saturate(t));
        }

        private void TickUnits(EntityManager em, in VeilField field)
        {
            using var ents = _unitQuery.ToEntityArray(Allocator.Temp);
            using var facs = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = _unitQuery.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == Faction.Border) continue; // the curse is home here
                if (hps[i].Value <= 0) continue; // dying — DeathSystem owns it now

                // Veil-Touched (Reclamation) and Border-Hardened: warded units
                // take no exposure at all, and their accrued seconds stop
                // climbing so the ward is a real reprieve rather than a pause
                // before the same death. docs/Design/Sects.md section 4.
                if (em.HasComponent<SectCurseWard>(ents[i])) continue;

                byte sat = field.SaturationAt(xfs[i].Position);
                bool onCrust = sat >= VeilField.CrustThreshold;
                bool hasVeilTag = em.HasComponent<VeilDebuffTag>(ents[i]);

                float seconds = em.HasComponent<ExposureState>(ents[i])
                    ? em.GetComponentData<ExposureState>(ents[i]).Seconds : 0f;

                if (onCrust)
                {
                    float prev = seconds;
                    seconds += TickInterval;

                    // Depth-scaled stat debuff, immediately.
                    var debuff = sat >= VeilField.DeepThreshold ? DeepDebuff : CrustDebuff;
                    if (em.HasComponent<BorderDebuff>(ents[i]))
                        em.SetComponentData(ents[i], debuff);
                    else
                        em.AddComponentData(ents[i], debuff);
                    if (!hasVeilTag) em.AddComponent<VeilDebuffTag>(ents[i]);

                    // Workers auto-flee BEFORE the damage grace ends — an
                    // unattended worker never dies to haze; early neglect
                    // costs tempo, not corpses. Crossing-edge triggered so
                    // the move order is issued once per excursion; the
                    // UserMoveOrder it adds also makes the mining state
                    // machines release the worker (their interrupt path).
                    if (prev < ExposureFleeSeconds && seconds >= ExposureFleeSeconds
                        && em.HasComponent<MinerTag>(ents[i])
                        && TryNearestHall(facs[i].Value, xfs[i].Position, out float3 hall))
                    {
                        TheWaningBorder.Core.Commands.Types.MoveCommandHelper
                            .Execute(em, ents[i], hall);
                    }

                    // Damage only past the grace window.
                    if (seconds > ExposureGraceSeconds)
                    {
                        var hp = hps[i];
                        hp.Value -= (int)math.ceil(DpsFor(sat) * TickInterval);
                        if (hp.Value < 0) hp.Value = 0;
                        em.SetComponentData(ents[i], hp);
                        // Exposure kills shed no blood (loop damping): the
                        // curse must not feed its own blood-spawner.
                        if (hp.Value == 0 && !em.HasComponent<CurseKilledTag>(ents[i]))
                            em.AddComponent<CurseKilledTag>(ents[i]);
                    }
                }
                else
                {
                    seconds = math.max(0f, seconds - TickInterval * ExposureRecoverMul);
                    if (hasVeilTag)
                    {
                        em.RemoveComponent<VeilDebuffTag>(ents[i]);
                        if (em.HasComponent<BorderDebuff>(ents[i]))
                            em.RemoveComponent<BorderDebuff>(ents[i]);
                    }
                }

                if (em.HasComponent<ExposureState>(ents[i]))
                    em.SetComponentData(ents[i], new ExposureState { Seconds = seconds });
                else if (seconds > 0f)
                    em.AddComponentData(ents[i], new ExposureState { Seconds = seconds });
            }
        }

        /// <summary>Nearest completed Hall of the unit's own faction — the
        /// auto-flee rally point for exposed workers.</summary>
        private bool TryNearestHall(Faction faction, float3 from, out float3 hall)
        {
            hall = default;
            using var facs = _hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float best = float.MaxValue;
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                float dx = xfs[i].Position.x - from.x;
                float dz = xfs[i].Position.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; hall = xfs[i].Position; }
            }
            return best < float.MaxValue;
        }

        /// <summary>Completed buildings standing in DEEP crust crumble slowly.
        /// Spread stops at structures (rule G), so this only fires when later
        /// growth/enclosure engulfed the ground — reclaim it to save them.</summary>
        private void TickBuildings(EntityManager em, in VeilField field)
        {
            using var ents = _buildingQuery.ToEntityArray(Allocator.Temp);
            using var xfs = _buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = _buildingQuery.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                if (em.HasComponent<SmallNodeTag>(ents[i])) continue; // starves, never crumbles
                // The curse's own structures are HOME in the crust — wells
                // (BorderMainNode carries BuildingTag + BorderTag) sit at the
                // centre of their deep seed discs and were crumbling to death
                // by mid-game (2026-08-03 playtest). Only PLAYER buildings
                // engulfed by later growth crumble.
                if (em.HasComponent<BorderTag>(ents[i])) continue;
                // Veilworks takes no curse damage - it is a smelter FOR cursed
                // matter and is meant to stand in the crust it feeds on
                // (docs/Design/Sects.md section 4). Without this it would be
                // the only building allowed onto cursed ground and the only one
                // guaranteed to crumble there.
                if (em.HasComponent<VeilworksTag>(ents[i])) continue;
                if (field.SaturationAt(xfs[i].Position) < VeilField.DeepThreshold) continue;

                var hp = hps[i];
                hp.Value -= (int)math.ceil(CrumbleDps * TickInterval);
                if (hp.Value < 0) hp.Value = 0;
                em.SetComponentData(ents[i], hp);
            }
        }
    }
}
