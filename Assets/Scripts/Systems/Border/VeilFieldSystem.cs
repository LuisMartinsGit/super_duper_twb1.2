// VeilFieldSystem.cs
// THE VEIL — simulation + presentation of the curse as a continuous sheet
// (Curse & Shardroot canon §2.3). Owns the VeilField saturation grid:
//
//   * SEED    — matches start with established crust discs around every
//               well (decision: "the world is already sick").
//   * FEED    — Active wells saturate their surroundings.
//   * GROW    — cellular spread: cells adjacent to solid crust gain
//               saturation; tuned so a fully neglected map reaches the
//               bases in roughly 20–30 minutes (the collective clock).
//   * DECAY   — cells whose nearest well is NOT Active lose saturation:
//               every verb visibly starves the sheet, and a Purified
//               (Cleansed) well clamps a sanctified circle to zero.
//   * MINING   — the Veil is an INFINITE veilstone source, dug DIRECTLY
//               (no deposit entities of any kind): VeilMiningSystem walks
//               miners to crusted vertices of this grid, credits veilstone
//               per pick-swing, and drains saturation under the pick — the
//               sheet RECEDES exactly where the villager is digging.
//               The Veil's only visual body is the terrain overlay
//               (InfluenceMaskTexture + TWB/Terrain/Lit) — the instanced
//               crystal-mesh renderer was removed 2026-07-25.
//   * DEBUFF  — units standing on crust get BorderDebuff (reduced speed +
//               attack/defense; deep crust is worse). No damage over time.
//   * SWALLOW — iron deposits under deep veil read as depleted until the
//               ground is reclaimed.
//   * PAINT   — a runtime TerrainLayer alphamap shows the crust in the
//               world (snapshotted + restored on teardown so hand-authored
//               terrain assets are never permanently modified in-editor).
//
// Determinism: fixed-cadence integer cellular pass over a double-buffered
// byte grid, seeded crystallization, no wall-clock — lockstep-safe. The
// terrain painter is presentation-only (reads the grid, never writes).

using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Influence;
using TheWaningBorder.Systems.Border.Jobs;
using TheWaningBorder.World.Terrain;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VeilFieldSystem : SystemBase
    {
        // ── Simulation tuning ──────────────────────────────────────────
        // The CA rates (grow/feed/decay/threshold), field geometry (CellSize,
        // MaxCellsPerAxis), pulse cadence, noise and break/cooldown values now
        // live in VeilCrustConstants so the Burst job (VeilSpreadJob) and this
        // system share one set of numbers — pulled in via `using static` above.
        // Only the SEED values (how sick the world starts) stay local.
        private const byte SeedCoreSaturation = 220; // starting crust at wells
        private const float SeedRadius = 26f;

        // ── Crust debuffs (reduced speed + stats; NO damage over time) ─
        private const float DebuffInterval = 1f;
        private static readonly BorderDebuff CrustDebuff =
            new BorderDebuff { DefPenalty = 0.15f, AttPenalty = 0.15f, SpeedPenalty = 0.2f };
        private static readonly BorderDebuff DeepDebuff =
            new BorderDebuff { DefPenalty = 0.3f, AttPenalty = 0.3f, SpeedPenalty = 0.35f };

        private float _maintAcc;          // 1 s maintenance-pulse accumulator
        private float _swallowAcc;
        private uint _rng;
        private Entity _fieldEntity = Entity.Null;
        private NativeArray<byte> _back;      // CA ping-pong scratch
        private NativeArray<byte> _visited;   // enclosure flood-fill scratch
        private NativeArray<byte> _influence; // §2.6 per-cell influence effect (sampled from PlayerInfluenceMap)
        private NativeArray<byte> _blocked;    // 1 = cell is nav-impassable for a NON-crust reason (terrain/building) — the CA won't grow into it
        private NativeArray<byte> _workerWard; // 1 = cell is near a worker — growth/enclosure never crystallize it (diggers can't be sealed in)
        private readonly int[] _cultureChannels = new int[PlayerInfluenceMap.PlayerChannels];

        // ── Tendril heartbeat state (deterministic: seeded RNG + fixed dt) ──
        private float _cyclePhaseTime;   // seconds elapsed in the current cycle
        private float _dormantDuration;  // this cycle's random "still" time
        private float _substepAcc;       // burst-substep accumulator
        private int _cycleSeed;          // reseeds tendril-site noise per cycle
        private EntityQuery _wellQuery;  // cached — was created per CA substep (query leak)

        // ── §2.5b hostile-ground state (2026-08-03 rev.2) ──────────────
        private float _escalation = 1f;       // dormant-window multiplier (shrinks over match time)
        private float _escalationT = 0f;      // raw 0..1 escalation ramp progress
        private NativeArray<byte> _wasCrust;  // precipitation: crust state at last pulse
        // Mirrors of the VeilField component's own arrays, so they can still be
        // disposed after the entity holding them has been wiped.
        private NativeArray<byte> _saturation;
        private NativeArray<byte> _cooldown;
        private byte _precipSeeded;           // first pulse only records, never spawns
        private float _precipTokens;          // precipitation spawn budget (token bucket)
        private EntityQuery _smallNodeQuery;  // blight-pocket anchors join the feeder set
        private EntityQuery _hallQuery;       // Age 0 hearth suppression sources
        private EntityQuery _cleanseHeroQuery;    // cached — ran per 1 s pulse (query leak)
        private EntityQuery _cleanseScholarQuery; // cached — ran per 1 s pulse (query leak)

        protected override void OnCreate()
        {
            if (!VeilFieldEnabled)
            {
                // Crystal growth deactivated (VeilCrustConstants.VeilFieldEnabled):
                // no field entity is ever created, so every veil consumer
                // (crystal renderer, nav stamp, classify, mining, debuffs)
                // no-ops. The curse lives on through the influence map.
                Enabled = false;
                return;
            }
            RequireForUpdate<BorderNodeState>(); // no wells → no veil
            // FEEDER query — dormant wells are excluded outright (canon §2.8).
            // Filtering in the query rather than branching per-substep keeps
            // the quiet phase genuinely free: an all-dormant map builds a
            // zero-length feeder array and every cell starves at DecayPerTick.
            _wellQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<BorderMainNodeTag>(),
                    ComponentType.ReadOnly<BorderNodeState>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                },
                None = new[] { ComponentType.ReadOnly<WellDormant>() },
            });
            _smallNodeQuery = GetEntityQuery(
                ComponentType.ReadOnly<SmallNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            _hallQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HallTag, LocalTransform>()
                .WithNone<UnderConstruction>()
                .Build(this);
            // Cached like _wellQuery: ApplyCleanseAuras runs on the 1 s
            // maintenance pulse, so building these per call leaked two
            // undisposed queries per second for the whole match.
            _cleanseHeroQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LocalTransform>() },
                Any = new[]
                {
                    ComponentType.ReadOnly<LitharchTag>(),
                    ComponentType.ReadOnly<TheWaningBorder.Abilities.UniqueUnitTag>(),
                    ComponentType.ReadOnly<ShardboundHeroTag>(),
                },
            });
            _cleanseScholarQuery = GetEntityQuery(
                ComponentType.ReadOnly<ScholarTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _rng = (uint)GameSettings.SpawnSeed * 2246822519u + 3266489917u;
            if (_rng == 0) _rng = 0x9E3779B9u;
        }

        /// <summary>
        /// Free every persistent grid this system owns, including the field's
        /// Saturation/Cooldown via the system-side mirrors. Safe to call when
        /// the field entity has already been destroyed — which is exactly the
        /// case after an end-of-match wipe.
        /// </summary>
        private void DisposeGrids(EntityManager em)
        {
            if (_back.IsCreated) _back.Dispose();
            if (_visited.IsCreated) _visited.Dispose();
            if (_influence.IsCreated) _influence.Dispose();
            if (_blocked.IsCreated) _blocked.Dispose();
            if (_workerWard.IsCreated) _workerWard.Dispose();
            if (_wasCrust.IsCreated) _wasCrust.Dispose();
            if (_saturation.IsCreated) _saturation.Dispose();
            if (_cooldown.IsCreated) _cooldown.Dispose();
        }

        protected override void OnDestroy()
        {
            DisposeGrids(EntityManager);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // Gate on the field entity EXISTING, not merely on the cached
            // handle being non-null. GameBootstrap's end-of-match wipe destroys
            // this ordinary gameplay entity while the system survives, so the
            // stale handle sailed past a null check and GetComponentData threw
            // "The entity does not exist" every frame from match 2 onward.
            if ((_fieldEntity == Entity.Null
                 || !em.Exists(_fieldEntity)
                 || !em.HasComponent<VeilField>(_fieldEntity))
                && !TryInitialise(em)) return;

            var field = em.GetComponentData<VeilField>(_fieldEntity);
            if (field.Initialised == 0) return;

            float dt = (float)SystemAPI.Time.DeltaTime;

            // §2.5b escalation: the heartbeat's dormant windows shrink over
            // match time (linear ramp to EscalationFloor at EscalationRamp-
            // Seconds) — an Age 0 nuisance becomes a late-game terrain force.
            _escalationT = math.saturate(
                (float)(SystemAPI.Time.ElapsedTime / EscalationRampSeconds));
            _escalation = math.lerp(1f, EscalationFloor, _escalationT);

            // Tracks whether the saturation grid actually changed this frame; if
            // so we bump field.Generation so VeilNavStampSystem re-mirrors the
            // crust into the nav cost field (and skips work on quiet frames).
            bool mutated = false;

            // Apply any queued breaks first, so a hole appears the instant it's
            // requested and the CA sees the cleared cells on the next tick.
            if (DrainBreaks(em, ref field)) mutated = true;

            // ── TENDRIL HEARTBEAT ──────────────────────────────────────
            // Advance the cycle clock and pick this frame's growth mode:
            //   dormant → (early window) → main burst → dormant …
            // Early tendrils begin EarlyLeadSeconds before the main burst so the
            // front ramps up rather than snapping out all at once (hybrid).
            _cyclePhaseTime += dt;
            float earlyStart = math.max(0f, _dormantDuration - EarlyLeadSeconds);
            float mainStart = _dormantDuration;
            float cycleEnd = _dormantDuration + BurstDurationSeconds;

            VeilGrowthMode growth = VeilGrowthMode.None;
            if (_cyclePhaseTime >= mainStart) growth = VeilGrowthMode.All;
            else if (_cyclePhaseTime >= earlyStart) growth = VeilGrowthMode.Early;

            // Growth substeps fire only while a burst is active — each extends
            // eligible tendrils one cell, so ~5 substeps across 1.5 s = ~5-cell fingers.
            if (growth != VeilGrowthMode.None)
            {
                _substepAcc += dt;
                if (_substepAcc >= BurstSubstepSeconds)
                {
                    _substepAcc -= BurstSubstepSeconds;
                    StepCA(em, ref field, maintenance: false, growth);
                    mutated = true;
                }
            }

            // Maintenance (feed/decay/sanctify/cooldown) + enclosure snap-fill,
            // once per second regardless of the heartbeat.
            _maintAcc += dt;
            if (_maintAcc >= PulseInterval)
            {
                _maintAcc -= PulseInterval;
                var pulseSw = System.Diagnostics.Stopwatch.StartNew();
                SampleInfluence(in field); // refresh §2.6 influence for this pulse
                SampleHearths(in field);   // §2.5b Age 0 hearths merge in as suppression
                ApplyCleanseAuras(em, in field); // heroes/Litharchs burn the curse away (2026-08-04)
                SampleBlocked(in field);   // rule G — growth stops at cliffs/buildings
                StepCA(em, ref field, maintenance: true, VeilGrowthMode.None);
                RunEnclosureFill(field);
                DepositCurseInfluence(in field); // curse influence tracks the crust footprint (B)
                ProcessPrecipitation(em, in field); // §2.5b — the Veil precipitates veilstone
                mutated = true;
                pulseSw.Stop();
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "VeilPulse", pulseSw.Elapsed.TotalMilliseconds);
            }

            // Roll the cycle over and reseed the tendril pattern for variety.
            if (_cyclePhaseTime >= cycleEnd)
            {
                _cyclePhaseTime -= cycleEnd;
                _substepAcc = 0f;
                _cycleSeed++;
                _dormantDuration = NextDormantDuration();
            }

            _swallowAcc += dt;
            if (_swallowAcc >= DebuffInterval)
            {
                _swallowAcc -= DebuffInterval;
                if (CrustPhysical)
                {
                    ApplyCrustDebuffs(em, in field, SystemAPI.Time.ElapsedTime);
                    SwallowIron(em, in field);
                    ProcessInfection(em, in field, SystemAPI.Time.ElapsedTime);
                }
            }

            if (mutated) field.Generation++;
            em.SetComponentData(_fieldEntity, field);
        }

        /// <summary>Random still time before the next tendril burst, scaled by
        /// the §2.5b escalation factor (windows shrink over match time). Uses
        /// the seeded RNG (no wall-clock) so it stays lockstep-deterministic.</summary>
        /// <summary>
        /// Tutorial-only speed-up for the tendril heartbeat, applied as a
        /// divisor on the dormant window. 1 = shipped pacing.
        ///
        /// The heartbeat rests 190-320 s between bursts, which is correct for
        /// a match and useless for a lesson: the tutorial's curse chapter asks
        /// the player to WATCH the crust advance, and up to five minutes of
        /// still ground teaches nothing. TutorialDirector raises this while
        /// that chapter is live and restores it after.
        ///
        /// Deliberately not a VeilCrustConstants entry — those are const and
        /// describe the game's balance. This is a scripted override with one
        /// caller, and it must be visible as such.
        /// </summary>
        public static float TutorialCreepMultiplier = 1f;

        private float NextDormantDuration()
            => (DormantMinSeconds + NextRand01() * (DormantMaxSeconds - DormantMinSeconds))
                * _escalation
                / math.max(1f, TutorialCreepMultiplier);

        private float NextRand01()
        {
            // xorshift32 — deterministic.
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFFu) / (float)0x1000000;
        }

    }
}
