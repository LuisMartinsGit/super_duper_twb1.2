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
//
// Location: Assets/Scripts/Systems/Border/

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
        private byte _precipSeeded;           // first pulse only records, never spawns
        private float _precipTokens;          // precipitation spawn budget (token bucket)
        private EntityQuery _sporelingQuery;  // blight-pocket anchors join the feeder set
        private EntityQuery _hallQuery;       // Age 0 hearth suppression sources

        // ── Terrain painter state ──────────────────────────────────────
        private UnityEngine.Terrain _terrain;
        private TerrainLayer _veilLayer;
        private float[,,] _alphaSnapshot;
        private TerrainLayer[] _layerSnapshot;
        private NativeArray<float> _paintWeight;  // eased on-screen weight per cell
        private NativeArray<float> _paintApplied; // weight last written to the alphamap
        private NativeArray<byte> _paintDirty;    // cell (or a neighbour) moved since its last write
        private int _paintScanCursor;
        private const int PaintCellsPerTick = 96;
        // Presentation-only smoothing: the sim lurches once per pulse, the
        // paint glides. 0.15/s ≈ a fresh crust cell fades in over ~2 s.
        private const float PaintFadePerSecond = 0.15f;
        private const float PaintEpsilon = 0.005f;

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
            _sporelingQuery = GetEntityQuery(
                ComponentType.ReadOnly<SporelingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            _hallQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HallTag, LocalTransform>()
                .WithNone<UnderConstruction>()
                .Build(this);
            _rng = (uint)GameSettings.SpawnSeed * 2246822519u + 3266489917u;
            if (_rng == 0) _rng = 0x9E3779B9u;
        }

        protected override void OnDestroy()
        {
            RestoreTerrain();
            if (_back.IsCreated) _back.Dispose();
            if (_visited.IsCreated) _visited.Dispose();
            if (_influence.IsCreated) _influence.Dispose();
            if (_blocked.IsCreated) _blocked.Dispose();
            if (_workerWard.IsCreated) _workerWard.Dispose();
            if (_wasCrust.IsCreated) _wasCrust.Dispose();
            if (_paintWeight.IsCreated) _paintWeight.Dispose();
            if (_paintApplied.IsCreated) _paintApplied.Dispose();
            if (_paintDirty.IsCreated) _paintDirty.Dispose();
            if (_fieldEntity != Entity.Null && EntityManager.Exists(_fieldEntity))
            {
                var f = EntityManager.GetComponentData<VeilField>(_fieldEntity);
                if (f.Saturation.IsCreated) f.Saturation.Dispose();
                if (f.Cooldown.IsCreated) f.Cooldown.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            if (_fieldEntity == Entity.Null && !TryInitialise(em)) return;
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

            // Terrain paint RETIRED (2026-07-24): the crust's ground look is
            // now the curse overlay in TWB/Terrain/Lit (crystallized grass),
            // fed by DepositCurseInfluence above → InfluenceMaskTexture.
            // Runtime SetAlphamaps painting was unfixably slow and stepped.
            // PaintDirtyCells is kept (unused) for reference.

            if (mutated) field.Generation++;
            em.SetComponentData(_fieldEntity, field);
        }

        // ─────────────────────────────────────────────────────────────
        // INITIALISATION + SEEDING
        // ─────────────────────────────────────────────────────────────

        private bool TryInitialise(EntityManager em)
        {
            if (!TerrainUtility.IsReady()) return false;

            var wellQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            if (wellQuery.CalculateEntityCount() == 0) return false;

            TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
            int w = math.clamp((int)math.ceil((bMax.x - bMin.x) / CellSize), 8, MaxCellsPerAxis);
            int h = math.clamp((int)math.ceil((bMax.y - bMin.y) / CellSize), 8, MaxCellsPerAxis);

            var field = new VeilField
            {
                Saturation = new NativeArray<byte>(w * h, Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                Cooldown = new NativeArray<byte>(w * h, Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                Width = w,
                Height = h,
                CellSize = CellSize,
                Origin = new float2(bMin.x, bMin.y),
                Initialised = 1,
            };
            _back = new NativeArray<byte>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _visited = new NativeArray<byte>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _influence = new NativeArray<byte>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _blocked = new NativeArray<byte>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _workerWard = new NativeArray<byte>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _paintWeight = new NativeArray<float>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _paintApplied = new NativeArray<float>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _paintDirty = new NativeArray<byte>(w * h, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            // Start the heartbeat in a fresh dormant window.
            _cyclePhaseTime = 0f;
            _substepAcc = 0f;
            _dormantDuration = NextDormantDuration();

            // Decision #3: the world is ALREADY sick — established crust
            // discs around every well, saturation falling off with distance.
            using (var xfs = wellQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < xfs.Length; i++)
                    SeedDisc(ref field, xfs[i].Position, SeedRadius, SeedCoreSaturation);
            }

            _fieldEntity = em.CreateEntity(typeof(VeilField));
            em.AddBuffer<VeilBreakRequest>(_fieldEntity); // break-write funnel
            em.SetComponentData(_fieldEntity, field);
            TWBLog.Log($"[Veil] field initialised {w}x{h} @ {CellSize}m — the world is already sick");
            return true;
        }

        private static void SeedDisc(ref VeilField field, float3 center, float radius, byte core)
        {
            int r = (int)math.ceil(radius / field.CellSize);
            field.TryWorldToCell(center, out int cx, out int cz);
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (z < 0 || z >= field.Height) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= field.Width) continue;
                    float dx = (x - cx) * field.CellSize;
                    float dz = (z - cz) * field.CellSize;
                    float d = math.sqrt(dx * dx + dz * dz);
                    if (d > radius) continue;
                    float t = 1f - d / radius;
                    byte v = (byte)math.min(255f, core * (0.35f + 0.65f * t));
                    int idx = field.Index(x, z);
                    if (v > field.Saturation[idx]) field.Saturation[idx] = v;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CA STEP  (tendril growth substep OR maintenance pulse)
        // ─────────────────────────────────────────────────────────────

        private void StepCA(EntityManager em, ref VeilField field,
            bool maintenance, VeilGrowthMode growth)
        {
            // Snapshot wells (few — main nodes only) into flat arrays for the job.
            var wellQuery = _wellQuery; // cached in OnCreate (no per-substep query leak)
            using var wStates = wellQuery.ToComponentDataArray<BorderNodeState>(Allocator.Temp);
            using var wXfs = wellQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            // §2.5b: live Sporelings (blight-pocket anchors) join the feeder
            // set as Active wells — each sustains and slowly grows its own
            // pocket until killed or starved (BlightPocketSystem collapses it).
            using var sXfs = _sporelingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sHps = _sporelingQuery.ToComponentDataArray<Health>(Allocator.Temp);
            int wells = wStates.Length;
            // Zero feeders is NOT an early-out (2026-08-04): the job must
            // still run so ownerless crust collapses (Destroyed regime)
            // instead of freezing at whatever state it died in.

            // NOTE (canon §2.8): _wellQuery already excludes WellDormant, so
            // `wells` counts only WOKEN wells. Absent means absent — a dormant
            // well must not appear here even with a non-Active state, or it
            // would still be the "nearest well" for its neighbourhood and pin
            // those cells into a decay regime owned by a well that has not
            // entered play yet.
            //
            // Sporelings below are NOT gated: a blight pocket the players made
            // by mining a patch dry is exactly the curse the early/mid game is
            // supposed to have, and it must keep feeding its own patch.
            var wellPos = new NativeArray<float2>(wells + sXfs.Length, Allocator.TempJob);
            var wellState = new NativeArray<NodeState>(wells + sXfs.Length, Allocator.TempJob);
            for (int i = 0; i < wells; i++)
            {
                wellPos[i] = new float2(wXfs[i].Position.x, wXfs[i].Position.z);
                wellState[i] = wStates[i].State;
            }
            int feeders = wells;
            for (int i = 0; i < sXfs.Length; i++)
            {
                if (sHps[i].Value <= 0) continue; // dying anchor feeds nothing
                wellPos[feeders] = new float2(sXfs[i].Position.x, sXfs[i].Position.z);
                wellState[feeders] = NodeState.Active;
                feeders++;
            }

            // Worker ward retired with the wall (§2.5b): the walkable veil
            // can't seal anyone in. Only the CrustPhysical wall model stamps
            // it; otherwise the array stays all-zero (no ward).
            if (CrustPhysical) SampleWorkerWard(em, in field);

            // PING-PONG: read field.Saturation (Src), write _back (Dst), blit
            // back. The job reads neighbours from the stable Src snapshot and
            // writes only its own Dst slot — no in-place races, deterministic.
            var job = new VeilSpreadJob
            {
                Src = field.Saturation,
                Dst = _back,
                Cooldown = field.Cooldown,
                Influence = _influence,
                Blocked = _blocked,
                WorkerWard = _workerWard,
                WellPos = wellPos,
                WellState = wellState,
                WellCount = feeders,
                Width = field.Width,
                Height = field.Height,
                Cell = field.CellSize,
                Origin = field.Origin,
                GrowthMode = (int)growth,
                Maintenance = (byte)(maintenance ? 1 : 0),
                CycleSeed = _cycleSeed,
                SustainR2 = SustainSquared(),
            };

            int total = field.Width * field.Height;
            // Complete immediately: ticks are small/infrequent and the
            // main-thread debuff/paint passes below read Saturation this frame.
            job.Schedule(total, 256).Complete();
            _back.CopyTo(field.Saturation);

            wellPos.Dispose();
            wellState.Dispose();
        }

        /// <summary>Squared sustain-tether radius for this frame — escalation
        /// widens an Active feeder's reach from SustainRadiusBase to
        /// SustainRadiusEscalated across the ramp.</summary>
        private float SustainSquared()
        {
            float r = math.lerp(SustainRadiusBase, SustainRadiusEscalated, _escalationT);
            return r * r;
        }

        // ─────────────────────────────────────────────────────────────
        // ENCLOSURE  (pockets sealed by crust snap to full crystal)
        // ─────────────────────────────────────────────────────────────

        private void RunEnclosureFill(VeilField field)
        {
            int total = field.Width * field.Height;
            var stack = new NativeList<int>(total, Allocator.TempJob);
            var job = new VeilEnclosureJob
            {
                Saturation = field.Saturation,
                Cooldown = field.Cooldown,
                Influence = _influence,
                WorkerWard = _workerWard,
                Visited = _visited,
                Stack = stack,
                Width = field.Width,
                Height = field.Height,
                OpenBelow = VeilField.CrustThreshold,
            };
            job.Schedule().Complete();
            stack.Dispose();
        }

        // ─────────────────────────────────────────────────────────────
        // INFLUENCE  (§2.6 — cultures act on the crust through their field)
        // ─────────────────────────────────────────────────────────────

        /// <summary>Sample PlayerInfluenceMap into the per-cell effect array the
        /// CA reads. An Alanthor player's influence (≥ threshold) marks a cell
        /// InfluenceSuppress: the curse can't grow there and existing crust
        /// decays (curse-immune reclaim). Runs once per pulse — influence moves
        /// slowly. Extends to Runai (decay) / Feraldis (corrupt) later.</summary>
        private void SampleInfluence(in VeilField field)
        {
            if (!PlayerInfluenceMap.Ready) { ClearInfluence(); return; }

            // ANY player's influence reverts the curse (per the "player
            // influence reverts crystal growth" rule) — regardless of culture.
            // Only players 0..7; the curse channel (8) is never included.
            int n = 0;
            for (int f = 0; f < PlayerInfluenceMap.PlayerChannels; f++)
            {
                if (!PlayerInfluenceMap.ChannelHasPresence(f, InfluenceThreshold)) continue;
                _cultureChannels[n++] = f;
            }
            if (n == 0) { ClearInfluence(); return; }

            for (int z = 0; z < field.Height; z++)
            {
                float wz = field.Origin.y + (z + 0.5f) * field.CellSize;
                int row = z * field.Width;
                for (int x = 0; x < field.Width; x++)
                {
                    float wx = field.Origin.x + (x + 0.5f) * field.CellSize;
                    byte eff = InfluenceNone;
                    // CONTESTED suppression (2026-08-04): a cell is
                    // curse-immune only while a player's influence both
                    // clears the threshold AND matches the curse's own
                    // influence there. With curse influence slowly growing
                    // over the match, thin "just enough" rims are eventually
                    // overrun while anchored cores (towers, dense bases)
                    // keep winning — influence is the war.
                    float curse = PlayerInfluenceMap.ChannelStrengthWorld(
                        PlayerInfluenceMap.CurseChannel, wx, wz);
                    for (int k = 0; k < n; k++)
                    {
                        float s = PlayerInfluenceMap.ChannelStrengthWorld(_cultureChannels[k], wx, wz);
                        if (s >= InfluenceThreshold && s >= curse)
                        { eff = InfluenceSuppress; break; }
                    }
                    _influence[row + x] = eff;
                }
            }
        }

        private void ClearInfluence()
        {
            for (int i = 0; i < _influence.Length; i++) _influence[i] = InfluenceNone;
        }

        /// <summary>§2.5b Age 0 hearth: every completed Hall suppresses the
        /// veil within HallHearthRadius — the curse cannot grow there and
        /// existing haze decays, exactly like influence, but veil-only (no
        /// territory claim, no combat aura). Age 0 projects no influence, so
        /// this is the pre-culture securing tool; culture influence supersedes
        /// it at age-up. Must run AFTER SampleInfluence each pulse (that pass
        /// rewrites the whole effect array).</summary>
        private void SampleHearths(in VeilField field)
        {
            using var xfs = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            if (xfs.Length == 0) return;

            int r = (int)math.ceil(HallHearthRadius / field.CellSize);
            float r2 = HallHearthRadius * HallHearthRadius;
            for (int i = 0; i < xfs.Length; i++)
            {
                int cx = (int)math.floor((xfs[i].Position.x - field.Origin.x) / field.CellSize);
                int cz = (int)math.floor((xfs[i].Position.z - field.Origin.y) / field.CellSize);
                for (int z = math.max(0, cz - r); z <= math.min(field.Height - 1, cz + r); z++)
                    for (int x = math.max(0, cx - r); x <= math.min(field.Width - 1, cx + r); x++)
                    {
                        float dx = (x - cx) * field.CellSize;
                        float dz = (z - cz) * field.CellSize;
                        if (dx * dx + dz * dz > r2) continue;
                        _influence[field.Index(x, z)] = InfluenceSuppress;
                    }
            }
        }

        /// <summary>Cleanse auras (2026-08-04 readability pass): heroes
        /// (King Lexor / Shardbound) and Litharchs burn saturation down
        /// around themselves every pulse — walking consecration, ~3 s from
        /// solid crust to clean under the aura. The march of a hero through
        /// cursed ground is now the game's most READABLE push-back verb.
        /// The HOLY SCHOLAR (ScholarTag, the purify ritualist) is a walking
        /// FONT: a much larger cleanse circle that also drains blood pools —
        /// its escorting army fights on ground the Scholar keeps clean.</summary>
        private void ApplyCleanseAuras(EntityManager em, in VeilField field)
        {
            var heroQuery = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LocalTransform>() },
                Any = new[]
                {
                    ComponentType.ReadOnly<LitharchTag>(),
                    ComponentType.ReadOnly<TheWaningBorder.Abilities.UniqueUnitTag>(),
                    ComponentType.ReadOnly<ShardboundHeroTag>(),
                },
            });
            using (var xfs = heroQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < xfs.Length; i++)
                    CleanseCircle(in field, xfs[i].Position, CleanseAuraRadius);
            }

            var scholarQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<ScholarTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = scholarQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < xfs.Length; i++)
                {
                    var p = xfs[i].Position;
                    CleanseCircle(in field, p, HolyScholarCleanseRadius);
                    TheWaningBorder.Influence.BloodMap.Drain(p.x, p.z, HolyScholarCleanseRadius);
                }
            }
        }

        private static void CleanseCircle(in VeilField field, float3 pos, float radius)
        {
            int r = (int)math.ceil(radius / field.CellSize);
            float r2 = radius * radius;
            var sat = field.Saturation; // NativeArray view — writable copy of the handle
            int cx = (int)math.floor((pos.x - field.Origin.x) / field.CellSize);
            int cz = (int)math.floor((pos.z - field.Origin.y) / field.CellSize);
            for (int z = math.max(0, cz - r); z <= math.min(field.Height - 1, cz + r); z++)
                for (int x = math.max(0, cx - r); x <= math.min(field.Width - 1, cx + r); x++)
                {
                    float dx = (x - cx) * field.CellSize;
                    float dz = (z - cz) * field.CellSize;
                    if (dx * dx + dz * dz > r2) continue;
                    int idx = field.Index(x, z);
                    byte v = sat[idx];
                    if (v == 0) continue;
                    sat[idx] = v > CleanseAuraPerPulse
                        ? (byte)(v - CleanseAuraPerPulse) : (byte)0;
                }
        }

        // ─────────────────────────────────────────────────────────────
        // §2.5b PRECIPITATION  (the Veil precipitates veilstone)
        // ─────────────────────────────────────────────────────────────

        /// <summary>Track per-cell crust transitions once per pulse and spawn
        /// outcropping nodes on a token budget: a cell that RECEDES organically
        /// (suppression / verb starvation — never a break, whose reward is
        /// explicit) may leave a small residue node on the now-clean ground; a
        /// cell that NEWLY CRUSTS may erupt a richer node in the haze (the
        /// §2.5b greed tier). Budget + chance + CreateOrMerge's 4 m merge keep
        /// the map tidy; seeded RNG keeps peers in lockstep.</summary>
        private void ProcessPrecipitation(EntityManager em, in VeilField field)
        {
            int total = field.Width * field.Height;
            if (!_wasCrust.IsCreated || _wasCrust.Length != total)
            {
                if (_wasCrust.IsCreated) _wasCrust.Dispose();
                _wasCrust = new NativeArray<byte>(total, Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
                _precipSeeded = 0;
            }
            if (_precipSeeded == 0)
            {
                // First pulse only RECORDS the match-start state — the seeded
                // well discs must not read as one giant eruption field.
                for (int i = 0; i < total; i++)
                    _wasCrust[i] = field.Saturation[i] >= VeilField.CrustThreshold
                        ? (byte)1 : (byte)0;
                _precipSeeded = 1;
                _precipTokens = PrecipitationBudget;
                return;
            }

            _precipTokens = math.min(PrecipitationBudget,
                _precipTokens + PrecipitationBudget * (PulseInterval / PrecipitationInterval));

            for (int z = 0; z < field.Height; z++)
            {
                int row = z * field.Width;
                for (int x = 0; x < field.Width; x++)
                {
                    int idx = row + x;
                    bool now = field.Saturation[idx] >= VeilField.CrustThreshold;
                    if (now == (_wasCrust[idx] != 0)) continue;
                    _wasCrust[idx] = now ? (byte)1 : (byte)0;

                    if (!now)
                    {
                        // RECEDED. Break-cleared cells (cooldown ticking) never
                        // pay — pocket collapses and player breaks carry their
                        // own reward.
                        if (field.Cooldown[idx] != 0) continue;
                        if (NextRand01() >= ResidueChance) continue;
                        if (_precipTokens < 1f) continue; // budget-starved: lost, not deferred
                        SpawnPrecipitate(em, in field, x, z, ResidueVeilstone);
                        _precipTokens -= 1f;
                    }
                    else
                    {
                        // NEWLY CRUSTED — frontier eruption, richer with the
                        // depth of the front behind it (3x3 average).
                        if (NextRand01() >= EruptionChance) continue;
                        if (_precipTokens < 1f) continue;
                        int sum = 0, cnt = 0;
                        for (int nz = math.max(0, z - 1); nz <= math.min(field.Height - 1, z + 1); nz++)
                            for (int nx = math.max(0, x - 1); nx <= math.min(field.Width - 1, x + 1); nx++)
                            { sum += field.Saturation[field.Index(nx, nz)]; cnt++; }
                        float t = math.saturate((sum / (float)cnt - VeilField.CrustThreshold)
                            / (float)(255 - VeilField.CrustThreshold));
                        int amount = (int)math.round(math.lerp(
                            EruptionVeilstoneMin, EruptionVeilstoneMax, t));
                        SpawnPrecipitate(em, in field, x, z, amount);
                        _precipTokens -= 1f;
                    }
                }
            }
        }

        private void SpawnPrecipitate(EntityManager em, in VeilField field, int x, int z, int amount)
        {
            float wx = field.Origin.x + (x + 0.5f) * field.CellSize
                + (NextRand01() - 0.5f) * field.CellSize;
            float wz = field.Origin.y + (z + 0.5f) * field.CellSize
                + (NextRand01() - 0.5f) * field.CellSize;
            float wy = TerrainUtility.GetHeight(wx, wz);
            VeilstoneOutcropping.CreateOrMerge(em, new float3(wx, wy, wz), amount);
        }

        // Curse influence (PlayerInfluenceMap.CurseChannel) is deposited from the
        // CRUST itself so the curse's influence footprint tracks the crystal
        // growth (rule B), not just the fixed discs around the wells. The
        // influence map self-decays (InfluenceMapSystem), so cells that lose
        // their crust fade back to neutral on their own — that decay gap between
        // the receding crust and the still-warded player influence is what forms
        // the required neutral corridor (rule D). Deposited on a coarse stride
        // (every other cell each axis) so a fully-crusted map stays a few
        // thousand deposits per pulse, not tens of thousands.
        private const int CurseDepositStride = 2;
        private const float CurseCrustRate = 4f;   // per pulse; must outpace the map's ~0.05/s+0.1 decay
        private const float CurseCrustRadiusMul = 2f; // deposit radius = CellSize * this

        private void DepositCurseInfluence(in VeilField field)
        {
            if (!PlayerInfluenceMap.Ready) return;
            float radius = field.CellSize * CurseCrustRadiusMul;
            // §2.5b escalation: the crust's influence deposit strengthens
            // very slowly over the match — see CurseInfluenceGrowthPerMinute.
            float growth = 1f + CurseInfluenceGrowthPerMinute
                * (float)(SystemAPI.Time.ElapsedTime / 60.0);
            for (int z = 0; z < field.Height; z += CurseDepositStride)
            {
                float wz = field.Origin.y + (z + 0.5f) * field.CellSize;
                int row = z * field.Width;
                for (int x = 0; x < field.Width; x += CurseDepositStride)
                {
                    if (field.Saturation[row + x] < VeilField.CrustThreshold) continue;
                    float wx = field.Origin.x + (x + 0.5f) * field.CellSize;
                    PlayerInfluenceMap.Deposit(wx, wz, radius,
                        PlayerInfluenceMap.CurseChannel, CurseCrustRate * growth);
                }
            }
        }

        /// <summary>Sample the nav cost field into the per-cell <see cref="_blocked"/>
        /// ward the CA reads (rule G — "impassable terrain must stop curse
        /// growth"). A veil cell is blocked when the nav cell under its centre is
        /// impassable for a NON-crust reason — baked terrain (cliffs / deep
        /// water) or a structural footprint (building / wall / gate). Crust's own
        /// stamp (<see cref="NavCostField.FlagCrust"/>) is deliberately excluded,
        /// or the curse would freeze itself the instant it stamped a cell. Runs
        /// once per pulse; if there is no nav field (nav-less test scenes) every
        /// cell stays unblocked, so behaviour is unchanged.</summary>
        private void SampleBlocked(in VeilField field)
        {
            if (!SystemAPI.HasSingleton<NavCostField>())
            {
                for (int i = 0; i < _blocked.Length; i++) _blocked[i] = 0;
                return;
            }
            var nav = SystemAPI.GetSingleton<NavCostField>();
            float navCell = SystemAPI.HasSingleton<NavGridSingleton>()
                ? SystemAPI.GetSingleton<NavGridSingleton>().CellSize : 1f;
            float3 navOrigin = SystemAPI.HasSingleton<NavGridSingleton>()
                ? SystemAPI.GetSingleton<NavGridSingleton>().Origin : float3.zero;
            const byte structural = (byte)(NavCostField.FlagBuildingFootprint
                | NavCostField.FlagStaticWall | NavCostField.FlagGate);

            for (int z = 0; z < field.Height; z++)
            {
                float wz = field.Origin.y + (z + 0.5f) * field.CellSize;
                int nz = (int)math.floor((wz - navOrigin.z) / navCell);
                int row = z * field.Width;
                for (int x = 0; x < field.Width; x++)
                {
                    float wx = field.Origin.x + (x + 0.5f) * field.CellSize;
                    int nx = (int)math.floor((wx - navOrigin.x) / navCell);
                    byte b = 0;
                    if (nx >= 0 && nx < nav.Width && nz >= 0 && nz < nav.Height)
                    {
                        int nidx = nz * nav.Width + nx;
                        bool terrainBlock = nav.TerrainCost.IsCreated
                            && nav.TerrainCost[nidx] == NavCostField.CostImpassable;
                        bool structBlock = (nav.Flags[nidx] & structural) != 0;
                        if (terrainBlock || structBlock) b = 1;
                    }
                    _blocked[row + x] = b;
                }
            }
        }

        /// <summary>Random still time before the next tendril burst, scaled by
        /// the §2.5b escalation factor (windows shrink over match time). Uses
        /// the seeded RNG (no wall-clock) so it stays lockstep-deterministic.</summary>
        private float NextDormantDuration()
            => (DormantMinSeconds + NextRand01() * (DormantMaxSeconds - DormantMinSeconds))
                * _escalation;

        private float NextRand01()
        {
            // xorshift32 — deterministic.
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFFu) / (float)0x1000000;
        }

        // ─────────────────────────────────────────────────────────────
        // BREAK  (a frontier chunk is knocked off → field write + regrow lock)
        // ─────────────────────────────────────────────────────────────

        /// <returns>True if at least one break was drained (the grid changed).</returns>
        private bool DrainBreaks(EntityManager em, ref VeilField field)
        {
            if (!em.HasBuffer<VeilBreakRequest>(_fieldEntity)) return false;
            var buf = em.GetBuffer<VeilBreakRequest>(_fieldEntity);
            if (buf.Length == 0) return false;
            for (int i = 0; i < buf.Length; i++)
                StampBreak(ref field, buf[i].Position, buf[i].Radius);
            buf.Clear();
            return true;
        }

        /// <summary>Clear coverage to 0 in a world radius and stamp the regrow
        /// cooldown. Crystals vanish because they only ever mirrored the field;
        /// once the cooldown ticks out, ordinary spread refills the hole.</summary>
        private static void StampBreak(ref VeilField field, float2 centerXZ, float radius)
        {
            if (radius <= 0f) return;
            int r = (int)math.ceil(radius / field.CellSize);
            int cx = (int)math.floor((centerXZ.x - field.Origin.x) / field.CellSize);
            int cz = (int)math.floor((centerXZ.y - field.Origin.y) / field.CellSize);
            float r2 = radius * radius;

            for (int z = cz - r; z <= cz + r; z++)
            {
                if (z < 0 || z >= field.Height) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= field.Width) continue;
                    float dx = (x - cx) * field.CellSize;
                    float dz = (z - cz) * field.CellSize;
                    if (dx * dx + dz * dz > r2) continue;
                    int idx = field.Index(x, z);
                    field.Saturation[idx] = 0;
                    field.Cooldown[idx] = BreakCooldownPulses;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CRUST DEBUFFS (speed + stats; NO damage over time)
        // ─────────────────────────────────────────────────────────────

        private void ApplyCrustDebuffs(EntityManager em, in VeilField field, double matchTime)
        {
            var uq = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var ents = uq.ToEntityArray(Allocator.Temp);
            using var tags = uq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = uq.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == Faction.Border) continue; // the curse is home here
                if (hps[i].Value <= 0) continue; // dying — DeathSystem owns it now

                byte sat = field.SaturationAt(xfs[i].Position);
                bool onCrust = sat >= VeilField.CrustThreshold;
                bool hasVeilTag = em.HasComponent<VeilDebuffTag>(ents[i]);

                // CATCH-CONVERSION (2026-07-12): the crust is impassable, so a
                // unit standing ON crust means the wall grew over it. Workers
                // are warded (growth can't reach them) and stay on the debuff
                // path below as a fallback; every OTHER unit caught by the
                // wall is consumed and erupts as a curse creature — the tier
                // scaling with the match clock, same ladder as infection.
                //
                // The kill goes through the NORMAL death pipeline: Health -> 0,
                // DeathSystem destroys next update. NEVER DestroyEntity here —
                // other systems (integrator / targeting / death) queue ECB ops
                // against this entity in the same frame, and a synchronous
                // destroy makes the EndSimulation playback throw "entity does
                // not exist" and corrupts the whole world (2026-07-12 crash).
                var cls = tags[i].Class;
                if (onCrust && cls != UnitClass.Economy && cls != UnitClass.Miner)
                {
                    var hp = hps[i];
                    hp.Value = 0;
                    em.SetComponentData(ents[i], hp);
                    SpawnCurseCreature(em, xfs[i].Position, matchTime);
                    continue;
                }

                if (onCrust)
                {
                    var debuff = sat >= VeilField.DeepThreshold ? DeepDebuff : CrustDebuff;
                    if (em.HasComponent<BorderDebuff>(ents[i]))
                        em.SetComponentData(ents[i], debuff);
                    else
                        em.AddComponentData(ents[i], debuff);
                    if (!hasVeilTag) em.AddComponent<VeilDebuffTag>(ents[i]);
                }
                else if (hasVeilTag)
                {
                    em.RemoveComponent<VeilDebuffTag>(ents[i]);
                    if (em.HasComponent<BorderDebuff>(ents[i]))
                        em.RemoveComponent<BorderDebuff>(ents[i]);
                }
            }
        }

        /// <summary>Stamp a small "no growth" disc around every worker
        /// (MinerTag — the unified Worker carries it from both factories) so a
        /// burst can never crystallize the ground under a digger and seal it
        /// inside the wall. Cleared and re-stamped from live positions before
        /// every CA step. Military units get NO ward — the wall catches them
        /// (catch-conversion in ApplyCrustDebuffs).</summary>
        private void SampleWorkerWard(EntityManager em, in VeilField field)
        {
            for (int i = 0; i < _workerWard.Length; i++) _workerWard[i] = 0;

            var wq = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = wq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int r = (int)math.ceil(WorkerWardRadius / field.CellSize);
            float r2 = WorkerWardRadius * WorkerWardRadius;
            for (int i = 0; i < xfs.Length; i++)
            {
                field.TryWorldToCell(xfs[i].Position, out int cx, out int cz);
                for (int z = math.max(0, cz - r); z <= math.min(field.Height - 1, cz + r); z++)
                    for (int x = math.max(0, cx - r); x <= math.min(field.Width - 1, cx + r); x++)
                    {
                        float dx = (x - cx) * field.CellSize;
                        float dz = (z - cz) * field.CellSize;
                        if (dx * dx + dz * dz > r2) continue;
                        _workerWard[field.Index(x, z)] = 1;
                    }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // MINER INFECTION  (neglected miners near the veil turn to curse)
        // ─────────────────────────────────────────────────────────────

        /// <summary>Accrue veil exposure on miners standing in haze; when a miner
        /// crosses <see cref="InfectionSeconds"/> it is consumed and a hostile
        /// curse creature erupts in its place. The creature tier scales with how
        /// late the eruption is (Crystalling → Veilstinger → Godsplinter), so a
        /// map left to rot spawns progressively worse things. This is the ONLY
        /// source of curse creatures now the curse is a force, not a faction —
        /// so it is NOT gated by BorderConstants.CurseFieldsArmies.</summary>
        private void ProcessInfection(EntityManager em, in VeilField field, double matchTime)
        {
            var mq = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = mq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = mq.ToComponentDataArray<Health>(Allocator.Temp);

            // Eruptions collected during the scan, spawned after it. The miner
            // itself is killed through the NORMAL death pipeline (Health -> 0,
            // DeathSystem destroys next update) — NEVER DestroyEntity here:
            // other systems queue ECB ops against the entity in the same
            // frame, and a synchronous destroy makes the EndSimulation
            // playback throw "entity does not exist" and corrupts the world
            // (2026-07-12 crash).
            var erupt = new NativeList<float3>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == Faction.Border) continue; // curse things don't infect
                if (hps[i].Value <= 0) continue; // dying — DeathSystem owns it now

                byte sat = field.SaturationAt(xfs[i].Position);
                bool near = sat >= InfectionNearThreshold;

                float prog = em.HasComponent<InfectionState>(ents[i])
                    ? em.GetComponentData<InfectionState>(ents[i]).Progress : 0f;
                prog += near ? DebuffInterval : -DebuffInterval * InfectionRecoverMul;
                prog = math.clamp(prog, 0f, InfectionSeconds);

                if (prog >= InfectionSeconds)
                {
                    erupt.Add(xfs[i].Position);
                    var hp = hps[i];
                    hp.Value = 0;
                    em.SetComponentData(ents[i], hp);
                    continue;
                }

                if (em.HasComponent<InfectionState>(ents[i]))
                    em.SetComponentData(ents[i], new InfectionState { Progress = prog });
                else if (prog > 0f)
                    em.AddComponentData(ents[i], new InfectionState { Progress = prog });
            }

            for (int i = 0; i < erupt.Length; i++)
                SpawnCurseCreature(em, erupt[i], matchTime);
            erupt.Dispose();
        }

        /// <summary>Erupt the tier appropriate to the match clock.</summary>
        private static void SpawnCurseCreature(EntityManager em, float3 pos, double matchTime)
        {
            if (matchTime < InfectionEarlyMaxSeconds)
                Crystalling.Create(em, pos, Faction.Border);
            else if (matchTime < InfectionMidMaxSeconds)
                Veilstinger.Create(em, pos, Faction.Border);
            else
                Godsplinter.Create(em, pos, Faction.Border);
        }

        // ─────────────────────────────────────────────────────────────
        // IRON SWALLOWING
        // ─────────────────────────────────────────────────────────────

        private void SwallowIron(EntityManager em, in VeilField field)
        {
            foreach (var (state, xf) in SystemAPI
                .Query<RefRW<IronDepositState>, RefRO<LocalTransform>>()
                .WithAll<IronMineTag>())
            {
                if (state.ValueRO.RemainingIron <= 0) continue; // truly empty stays depleted
                bool swallowed = field.SaturationAt(xf.ValueRO.Position) >= VeilField.DeepThreshold;
                if (swallowed && state.ValueRO.Depleted == 0)
                    state.ValueRW.Depleted = 1;
                else if (!swallowed && state.ValueRO.Depleted == 1)
                    state.ValueRW.Depleted = 0; // ground reclaimed — the mine breathes again
            }
        }

        // ─────────────────────────────────────────────────────────────
        // TERRAIN PAINTING (presentation only)
        // ─────────────────────────────────────────────────────────────

        // The sim only changes on discrete pulses; painting its raw state made
        // the crust visibly lurch a cell-ring at a time, once per second. The
        // painter therefore smooths in three presentation-only ways:
        //   * CONTINUOUS — paint weight is a ramp over saturation, not three
        //     hard buckets, so a saturating cell brightens by ~0.01 per pulse
        //     instead of popping a whole bucket at once;
        //   * EASED     — the on-screen weight glides toward that target at
        //     PaintFadePerSecond, turning any remaining jump into a fade;
        //   * BILINEAR  — each alphamap texel interpolates the four nearest
        //     cell weights, so the front is a gradient, not 4 m blocks.
        private void PaintDirtyCells(in VeilField field, float dt)
        {
            if (_terrain == null && !TryInitPainter(in field)) return;

            var tData = _terrain.terrainData;
            int layerIndex = System.Array.IndexOf(tData.terrainLayers, _veilLayer);
            if (layerIndex < 0) return;

            AdvancePaintWeights(in field, dt);

            int res = tData.alphamapResolution;
            Vector3 tPos = _terrain.transform.position;
            Vector3 tSize = tData.size;

            int updated = 0;
            int total = field.Width * field.Height;
            for (int scanned = 0; scanned < total && updated < PaintCellsPerTick; scanned++)
            {
                int idx = _paintScanCursor;
                _paintScanCursor = (_paintScanCursor + 1) % total;

                if (_paintDirty[idx] == 0) continue;
                _paintDirty[idx] = 0;
                _paintApplied[idx] = _paintWeight[idx];
                updated++;

                int cx = idx % field.Width;
                int cz = idx / field.Width;
                float wx = field.Origin.x + cx * field.CellSize;
                float wz = field.Origin.y + cz * field.CellSize;

                int ax = (int)((wx - tPos.x) / tSize.x * res);
                int az = (int)((wz - tPos.z) / tSize.z * res);
                int aw = math.max(1, (int)(field.CellSize / tSize.x * res));
                int ah = math.max(1, (int)(field.CellSize / tSize.z * res));
                if (ax < 0 || az < 0 || ax + aw > res || az + ah > res) continue;

                // Muted underlay — the 3D crystal lattice is the Veil's real
                // visual body (VeilCrystalPresentation); the paint just reads
                // as corrupted soil beneath the shards, not a blot.
                var block = tData.GetAlphamaps(ax, az, aw, ah);
                int layers = block.GetLength(2);
                for (int by = 0; by < ah; by++)
                    for (int bx = 0; bx < aw; bx++)
                    {
                        float twx = tPos.x + (ax + bx + 0.5f) / res * tSize.x;
                        float twz = tPos.z + (az + by + 0.5f) / res * tSize.z;
                        float weight = SampleWeightBilinear(in field, twx, twz);
                        float rest = 1f - weight;
                        float sum = 0f;
                        for (int l = 0; l < layers; l++)
                            if (l != layerIndex) sum += block[by, bx, l];
                        for (int l = 0; l < layers; l++)
                        {
                            if (l == layerIndex) block[by, bx, l] = weight;
                            else block[by, bx, l] = sum > 0f
                                ? block[by, bx, l] / sum * rest
                                : rest / math.max(1, layers - 1);
                        }
                    }
                tData.SetAlphamaps(ax, az, block);
            }
        }

        /// <summary>Continuous paint weight for a saturation value: 0 at
        /// PaintThreshold ramping to the old shallow bucket's 0.3 at
        /// DeepThreshold, then on to 0.5 at full saturation.</summary>
        private static float TargetPaintWeight(byte sat)
        {
            if (sat < VeilField.PaintThreshold) return 0f;
            if (sat < VeilField.DeepThreshold)
                return 0.3f * (sat - VeilField.PaintThreshold)
                    / (float)(VeilField.DeepThreshold - VeilField.PaintThreshold);
            return 0.3f + 0.2f * (sat - VeilField.DeepThreshold)
                / (float)(255 - VeilField.DeepThreshold);
        }

        /// <summary>Glide every cell's on-screen weight toward its target and
        /// flag cells whose applied paint is stale. A moving cell dirties its
        /// neighbours too — their texels interpolate this cell's weight.</summary>
        private void AdvancePaintWeights(in VeilField field, float dt)
        {
            float step = PaintFadePerSecond * dt;
            int w = field.Width, h = field.Height;
            for (int z = 0; z < h; z++)
            {
                int row = z * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    float target = TargetPaintWeight(field.Saturation[i]);
                    float cur = _paintWeight[i];
                    if (cur != target)
                    {
                        float d = target - cur;
                        cur = math.abs(d) <= step ? target : cur + math.sign(d) * step;
                        _paintWeight[i] = cur;
                    }
                    if (math.abs(cur - _paintApplied[i]) <= PaintEpsilon) continue;

                    for (int nz = math.max(0, z - 1); nz <= math.min(h - 1, z + 1); nz++)
                        for (int nx = math.max(0, x - 1); nx <= math.min(w - 1, x + 1); nx++)
                            _paintDirty[nz * w + nx] = 1;
                }
            }
        }

        /// <summary>Bilinearly interpolated on-screen weight at a world
        /// position (cell weights sit at cell centres).</summary>
        private float SampleWeightBilinear(in VeilField field, float wx, float wz)
        {
            float u = (wx - field.Origin.x) / field.CellSize - 0.5f;
            float v = (wz - field.Origin.y) / field.CellSize - 0.5f;
            int x0 = (int)math.floor(u), z0 = (int)math.floor(v);
            float tx = u - x0, tz = v - z0;
            int x1 = math.min(x0 + 1, field.Width - 1);
            int z1 = math.min(z0 + 1, field.Height - 1);
            x0 = math.clamp(x0, 0, field.Width - 1);
            z0 = math.clamp(z0, 0, field.Height - 1);

            float w00 = _paintWeight[z0 * field.Width + x0];
            float w10 = _paintWeight[z0 * field.Width + x1];
            float w01 = _paintWeight[z1 * field.Width + x0];
            float w11 = _paintWeight[z1 * field.Width + x1];
            float top = w00 + (w10 - w00) * tx;
            float bot = w01 + (w11 - w01) * tx;
            return top + (bot - top) * tz;
        }

        private bool TryInitPainter(in VeilField field)
        {
            _terrain = UnityEngine.Terrain.activeTerrain;
            if (_terrain == null || _terrain.terrainData == null) return false;
            var tData = _terrain.terrainData;

            // Snapshot for teardown restore — the terrain asset must NEVER be
            // permanently modified by a play session in the editor.
            _layerSnapshot = tData.terrainLayers;
            _alphaSnapshot = tData.GetAlphamaps(0, 0,
                tData.alphamapResolution, tData.alphamapResolution);

            // Runtime veilstone layer: a flat crystalline purple.
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var cols = new Color32[16];
            for (int i = 0; i < 16; i++) cols[i] = new Color32(96, 40, 140, 255);
            tex.SetPixels32(cols);
            tex.Apply();
            _veilLayer = new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(8f, 8f),
                name = "VeilCrust(Runtime)",
            };

            var layers = new TerrainLayer[_layerSnapshot.Length + 1];
            _layerSnapshot.CopyTo(layers, 0);
            layers[_layerSnapshot.Length] = _veilLayer;
            tData.terrainLayers = layers;

            // The seeded crust is ESTABLISHED, not growing — snap its weight
            // to target so match start doesn't open on a slow fade-in. (The
            // fade applies only to changes from here on.)
            for (int i = 0; i < _paintWeight.Length; i++)
                _paintWeight[i] = TargetPaintWeight(field.Saturation[i]);
            return true;
        }

        private void RestoreTerrain()
        {
            if (_terrain == null || _terrain.terrainData == null) return;
            var tData = _terrain.terrainData;
            if (_layerSnapshot != null) tData.terrainLayers = _layerSnapshot;
            if (_alphaSnapshot != null) tData.SetAlphamaps(0, 0, _alphaSnapshot);
            _terrain = null;
        }
    }
}
