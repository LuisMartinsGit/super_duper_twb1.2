// VeilFieldSystem.Precipitation.cs
// Veilstone precipitation from crust transitions, and player-driven veil breaks.
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
    }
}
