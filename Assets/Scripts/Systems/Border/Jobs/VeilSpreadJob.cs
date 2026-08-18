// File: Assets/Scripts/Systems/Border/Jobs/VeilSpreadJob.cs
// The Veil's cellular automaton, as a Burst job — now a TENDRIL HEARTBEAT.
//
// The front does NOT creep continuously. Two kinds of tick drive it, and the
// system (VeilFieldSystem) decides which each call is:
//
//   * GROWTH substep (during a burst): eligible frontier TIP cells extend one
//     cell outward as a fast crystal tendril. Runs several times across the
//     1.5 s burst → ~5-cell fingers. Fingers, not a wall, because only thin
//     protrusion tips selected by per-cycle noise grow (see TipMaxSolidNeighbors
//     + TendrilThreshold). Nothing else changes on these ticks.
//
//   * MAINTENANCE tick (once/second): well cores feed, starved crust decays,
//     Cleansed ground clamps to zero, and break cooldowns tick down. NO frontier
//     creep here — advancing the front is exclusively the tendrils' job.
//
// PING-PONG: every cell reads neighbours from the stable Src snapshot and writes
// only its own Dst slot, so a tick is order-independent and deterministic (same
// Src + same CycleSeed + same flags → same Dst; no wall-clock). VeilFieldSystem
// blits Dst back after each tick.
//
// Location: Assets/Scripts/Systems/Border/Jobs/

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border.Jobs
{
    /// <summary>Which tendrils may extend this growth substep.</summary>
    public enum VeilGrowthMode : int
    {
        None = 0,   // dormant — no tendril growth
        Early = 1,  // hybrid ramp — only the "early" (top-noise) tendrils
        All = 2,    // main burst — every tendril
    }

    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    public struct VeilSpreadJob : IJobParallelFor
    {
        // ── Field state (double buffer) ────────────────────────────────
        [ReadOnly] public NativeArray<byte> Src;   // this tick's snapshot
        [WriteOnly] public NativeArray<byte> Dst;  // next tick's state
        public NativeArray<byte> Cooldown;         // per-cell regrow lockout (read+write own slot)
        [ReadOnly] public NativeArray<byte> Influence; // §2.6 per-cell influence effect (InfluenceSuppress = curse-immune + decay)
        [ReadOnly] public NativeArray<byte> Blocked;   // rule G: 1 = cell is nav-impassable (terrain/building) — the front can't grow into it
        [ReadOnly] public NativeArray<byte> WorkerWard; // 1 = cell is near a worker — growth never crystallizes a digger's ground

        // ── Wells (few — main nodes only) ──────────────────────────────
        [ReadOnly] public NativeArray<float2> WellPos;     // world XZ
        [ReadOnly] public NativeArray<NodeState> WellState;
        public int WellCount;

        // ── Grid dims ──────────────────────────────────────────────────
        public int Width;
        public int Height;
        public float Cell;
        public float2 Origin;

        // ── Tick control (set by the system) ───────────────────────────
        public int GrowthMode;   // VeilGrowthMode; drives tendril extension this substep
        public byte Maintenance; // 1 = also run feed/decay/sanctify/cooldown this tick
        public int CycleSeed;    // reseeds tendril-site noise each heartbeat cycle
        public float SustainR2;  // squared sustain-tether radius (escalation-scaled)

        public void Execute(int idx)
        {
            int x = idx % Width;
            int z = idx / Width;
            int v = Src[idx];
            byte cd = Cooldown[idx];

            // Cell centre → nearest well → regime (feed vs decay vs clamp).
            float wx = Origin.x + (x + 0.5f) * Cell;
            float wz = Origin.y + (z + 0.5f) * Cell;
            int nearest = -1;
            float nearestD2 = float.MaxValue;
            for (int wi = 0; wi < WellCount; wi++)
            {
                float dx = WellPos[wi].x - wx;
                float dz = WellPos[wi].y - wz;
                float d2 = dx * dx + dz * dz;
                if (d2 < nearestD2) { nearestD2 = d2; nearest = wi; }
            }
            // No feeder left anywhere → everything collapses (Destroyed
            // regime), never "frozen forever" (2026-08-04 recession fix).
            NodeState regime = nearest >= 0 ? WellState[nearest] : NodeState.Destroyed;
            float feedR2 = FeedRadius * FeedRadius;
            float sanctR2 = SanctifyRadius * SanctifyRadius;

            // §2.6: a suppressing influence field (Alanthor/Runai) makes the cell
            // curse-immune — the curse can't grow here and existing crust decays.
            bool suppress = Influence[idx] == InfluenceSuppress;

            // Rule G: impassable terrain / structures stop the front cold. The
            // crust never spreads onto a cliff, deep water, or a building
            // footprint — the front routes around them as fingers instead.
            // The worker ward folds in here: ground under/near a digger is
            // equally unclaimable, so a burst can't seal a worker inside.
            bool blocked = (Blocked.IsCreated && Blocked[idx] != 0)
                || (WorkerWard.IsCreated && WorkerWard[idx] != 0);

            // ── TENDRIL GROWTH (burst substep) ─────────────────────────
            // Only Active ground, not on cooldown, not already solid, not
            // warded, and inside the sustain tether — a tendril never grows
            // ground its feeder couldn't hold.
            if (GrowthMode != (int)VeilGrowthMode.None && !suppress && !blocked
                && regime == NodeState.Active && cd == 0 && v < SolidThreshold
                && nearestD2 <= SustainR2)
            {
                int solid = 0;
                if (x > 0 && Src[idx - 1] >= SolidThreshold) solid++;
                if (x < Width - 1 && Src[idx + 1] >= SolidThreshold) solid++;
                if (z > 0 && Src[idx - Width] >= SolidThreshold) solid++;
                if (z < Height - 1 && Src[idx + Width] >= SolidThreshold) solid++;

                // A thin protrusion tip (1..Tip solid neighbours) — buried cells
                // never grow, so the mass advances as fingers not a slab.
                if (solid >= 1 && solid <= TipMaxSolidNeighbors)
                {
                    float tn = 0.5f + 0.5f * noise.cnoise(new float2(
                        x * TendrilNoiseFrequency + CycleSeed * 7.13f,
                        z * TendrilNoiseFrequency - CycleSeed * 3.71f));

                    bool isTendril = tn > TendrilThreshold;
                    bool isEarly = tn > EarlyTendrilThreshold;
                    // Early window grows only the top slice; the main burst all.
                    bool grow = isTendril
                        && (GrowthMode == (int)VeilGrowthMode.All || isEarly);

                    if (grow)
                    {
                        // Jump to solid so the tendril seeds its next cell on the
                        // following substep — that 1-cell-per-substep march IS the
                        // finger extending over the 1.5 s burst.
                        Dst[idx] = (byte)math.min(255, math.max(v, SolidThreshold + 8));
                        return;
                    }
                }
            }

            // ── MAINTENANCE (feed / decay / sanctify / cooldown) ───────
            if (Maintenance != 0)
            {
                if (cd > 0)
                {
                    // Broken + locked: hold the hole open; only clearing regimes
                    // touch it. Tick the lockout down (once/second).
                    if (regime == NodeState.Cleansed && nearestD2 <= sanctR2) v = 0;
                    else if (regime == NodeState.Destroyed) v -= DestroyedDecayPerTick;
                    else if (regime != NodeState.Active) v -= DecayPerTick;
                    Cooldown[idx] = (byte)(cd - 1);
                }
                else if (suppress)
                {
                    // Warded ground: the culture's influence starves the crust
                    // (reclaim), overriding any Active well's feed. Faster
                    // than plain starvation (2026-08-04) — pushback must be
                    // VISIBLE.
                    v -= SuppressDecayPerTick;
                }
                else if (regime == NodeState.Active)
                {
                    // Feed the well core; let thin haze firm up. NO frontier
                    // creep — the front only advances via tendrils. Beyond
                    // the sustain tether even a live feeder can't hold ground:
                    // distant crust starves (2026-08-04, "still not receding" —
                    // before this, Active-regime crust anywhere on the map was
                    // simply permanent).
                    if (nearestD2 <= feedR2) v += FeedPerTick;
                    else if (nearestD2 > SustainR2) v -= DecayPerTick;
                    else if (v > 0 && v < VeilField.CrustThreshold) v += 1;
                }
                else if (regime == NodeState.Cleansed && nearestD2 <= sanctR2)
                {
                    v = 0; // sanctified ground — the Font holds it clear
                }
                else if (regime == NodeState.Destroyed)
                {
                    v -= DestroyedDecayPerTick; // slow-fading loot field
                }
                else
                {
                    v -= DecayPerTick; // starved: the verb pushes the veil back
                }

                Dst[idx] = (byte)math.clamp(v, 0, 255);
                return;
            }

            // Growth-only tick and this cell didn't extend: carry it over.
            Dst[idx] = (byte)v;
        }
    }
}
