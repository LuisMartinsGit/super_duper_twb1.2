// VeilFieldSystem.Influence.cs
// Suppression sampling (player influence, hearths, cleanse auras), curse-influence deposit, nav blocking.
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
        // INFLUENCE  (§2.6 — cultures act on the crust through their field)
        // ─────────────────────────────────────────────────────────────

        /// <summary>Sample PlayerInfluenceMap into the per-cell effect array the
        /// CA reads. An Alanthor player's influence (≥ threshold) marks a cell
        /// InfluenceSuppress: the curse can't grow there and existing crust
        /// decays (curse-immune reclaim). Runs once per pulse — influence moves
        /// slowly. Extends to Runai (decay) / Feraldis (corrupt) later.</summary>
        private void SampleInfluence(in VeilField field)
        {
            // CACHED (2026-09-27): the influence map is the territory
            // rasterize, static between ownership changes, and every write to
            // it bumps PlayerInfluenceMap.DataVersion. The sampled effect is a
            // pure function of that content and this grid's geometry, so the
            // full-grid bilinear resample (36,864 cells x (1 + players)
            // samples, the bulk of the 5-12 ms VeilPulse) now runs only when
            // one of those moves. Every pulse still starts from a fresh copy,
            // because SampleHearths stamps on top of it. Content-keyed, not
            // time-keyed: every peer reads the same array on the same tick.
            int total = field.Width * field.Height;
            bool fresh = !_influenceBase.IsCreated || _influenceBase.Length != total
                || _influenceBaseVersion != PlayerInfluenceMap.DataVersion
                || _influenceBaseReady != PlayerInfluenceMap.Ready
                || _influenceBaseEpoch != SimCadence.Epoch
                || !_influenceBaseValid;
            if (fresh)
            {
                if (!_influenceBase.IsCreated || _influenceBase.Length != total)
                {
                    if (_influenceBase.IsCreated) _influenceBase.Dispose();
                    _influenceBase = new NativeArray<byte>(total, Allocator.Persistent,
                        NativeArrayOptions.ClearMemory);
                }
                ComputeInfluence(in field);
                _influenceBase.CopyFrom(_influence);
                _influenceBaseVersion = PlayerInfluenceMap.DataVersion;
                _influenceBaseReady = PlayerInfluenceMap.Ready;
                _influenceBaseEpoch = SimCadence.Epoch;
                _influenceBaseValid = true;
            }
            else
            {
                _influence.CopyFrom(_influenceBase);
            }
        }

        private NativeArray<byte> _influenceBase;
        private int _influenceBaseVersion;
        private bool _influenceBaseReady;
        private int _influenceBaseEpoch;
        /// <summary>Cleared by TryInitialise so a re-initialised field (new
        /// match, same grid size) never adopts the previous match's sample.</summary>
        private bool _influenceBaseValid;

        private void ComputeInfluence(in VeilField field)
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
            using (var xfs = _cleanseHeroQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < xfs.Length; i++)
                    CleanseCircle(in field, xfs[i].Position, CleanseAuraRadius);
            }

            using (var xfs = _cleanseScholarQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
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
        // DepositCurseInfluence is DELETED (Regions.md §3b, 2026-08-31). The
        // crust used to write the curse channel every pulse — thousands of
        // disc stamps — so its influence footprint tracked the crystals. The
        // curse channel is now the territory-ownership rasterize (curse-held
        // regions at full strength), rebuilt only when ownership changes; a
        // per-pulse writer would both fight that and wake every version-gated
        // renderer every pulse. The crust CA itself is untouched — it is
        // ground state (§2.5b), not influence.

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

            // Burst job (2026-09-27) — was a 36,864-cell managed loop on the
            // main thread every pulse. Same per-cell arithmetic; each index
            // writes only its own slot, so the result is order-independent.
            bool hasTerrain = nav.TerrainCost.IsCreated;
            new VeilBlockedSampleJob
            {
                // An unassigned container cannot be scheduled; Flags stands in
                // and is never read through this field when hasTerrain is false.
                NavTerrainCost = hasTerrain ? nav.TerrainCost : nav.Flags,
                NavFlags = nav.Flags,
                HasTerrainCost = hasTerrain,
                NavWidth = nav.Width,
                NavHeight = nav.Height,
                NavCell = navCell,
                NavOrigin = navOrigin,
                StructuralMask = structural,
                CostImpassable = NavCostField.CostImpassable,
                Width = field.Width,
                CellSize = field.CellSize,
                Origin = field.Origin,
                Blocked = _blocked,
            }.Schedule(field.Width * field.Height, 1024).Complete();
        }
    }
}
