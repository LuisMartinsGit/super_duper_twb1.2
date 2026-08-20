// VeilFieldSystem.Grid.cs
// Grid allocation, disc seeding, the CA step and the enclosure fill.
// Partial of VeilFieldSystem.cs -- split 2026-08-12 for readability.

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
    public partial class VeilFieldSystem : SystemBase
    {
        // ─────────────────────────────────────────────────────────────
        // INITIALISATION + SEEDING
        // ─────────────────────────────────────────────────────────────

        private bool TryInitialise(EntityManager em)
        {
            // Under deterministic lockstep the terrain gate is SKIPPED: the
            // lockstep world gate (mask baked + map populated) cannot pass
            // without the terrain having been ready first, so by tick 0 it is
            // guaranteed — while the flag itself flips at a per-peer wall-clock
            // moment. Consulting it inside the tick timeline delayed this
            // system's first update by a DIFFERENT number of ticks on each
            // peer, which shifted the whole pulse schedule: precipitation then
            // spawned the same outcropping at tick 79 on one peer and 83 on
            // the other (the 2026-08-16 tick-90 desync). Single-player keeps
            // the check — it has no world gate.
            if (!TheWaningBorder.Multiplayer.LockstepFixedStep.Active
                && !TerrainUtility.IsReady()) return false;

            var wellQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            if (wellQuery.CalculateEntityCount() == 0) return false;

            // A re-initialise after the end-of-match entity wipe would
            // otherwise leak every persistent array this method allocates —
            // six system-side grids plus the field's own Saturation/Cooldown,
            // whose owning component vanished with the entity.
            DisposeGrids(em);

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

            // Start the heartbeat in a fresh dormant window — and reset EVERY
            // per-match accumulator, not just the cycle trio. The system
            // object survives across matches in one session (the world is
            // wiped, not rebuilt), so leftover _maintAcc/_swallowAcc shifted
            // the whole pulse schedule of the NEXT match by a per-peer amount.
            // The RNG is reseeded here for the same reason: OnCreate ran once
            // per session with whatever SpawnSeed the process had at boot,
            // and its stream position carried across matches.
            _cyclePhaseTime = 0f;
            _substepAcc = 0f;
            _maintAcc = 0f;
            _swallowAcc = 0f;
            _rng = (uint)GameSettings.SpawnSeed * 2246822519u + 3266489917u;
            if (_rng == 0) _rng = 0x9E3779B9u;
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
            // System-side mirrors so the next rebuild (or OnDestroy) can free
            // these even when the component that held them is already gone.
            _saturation = field.Saturation;
            _cooldown = field.Cooldown;
            TWBLog.Log($"[Veil] field initialised {w}x{h} @ {CellSize}m — the world is already sick");
            // Tripwire in the DIFFABLE log: the init tick anchors the entire
            // pulse schedule (and therefore precipitation spawn ticks), so if
            // it ever differs between peers again, the two Lockstep.log files
            // disagree on this one line instead of on a mystery spawn later.
            // The stepped flag is the key diagnostic: false here means this
            // system ran OUTSIDE the fixed-step driver — the 2026-08-16
            // 'tick 766/636' events that exposed the frame-driven-match bug.
            bool stepped = TheWaningBorder.Multiplayer.LockstepFixedStep.IsAttached;
            TheWaningBorder.Multiplayer.LockstepLog.Event(
                (int)math.round(World.Time.ElapsedTime
                    * TheWaningBorder.Core.Multiplayer.LockstepTiming.TicksPerSecond),
                $"veil field initialised {w}x{h} stepped={stepped}");
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
            // §2.5b: live SmallNodes (blight-pocket anchors) join the feeder
            // set as Active wells — each sustains and slowly grows its own
            // pocket until killed or starved (BlightPocketSystem collapses it).
            using var sXfs = _smallNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sHps = _smallNodeQuery.ToComponentDataArray<Health>(Allocator.Temp);
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
            // SmallNodes below are NOT gated: a blight pocket the players made
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
    }
}
