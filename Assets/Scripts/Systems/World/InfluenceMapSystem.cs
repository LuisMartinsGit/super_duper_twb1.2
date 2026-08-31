// InfluenceMapSystem.cs
// THE INFLUENCE SIMULATION IS GONE (docs/Design/Regions.md §3b, 2026-08-31).
//
// This system used to be a 0.1 s tick of deposits (curse nodes, walls,
// towers, civic buildings, Halls, trade lanes, war totems) against a
// decaying field — hundreds of disc stamps per second, forever, and the
// single biggest measured per-frame territory cost. Territories have fixed
// shapes now and ownership is the only variable, so the whole ladder is
// replaced by ONE rasterize of TerritoryOwnership into the (renamed in
// spirit, not in name) PlayerInfluenceMap grid, re-run ONLY when
// TerritoryOwnership.Version moves — a claim, a loss, a curse conquest.
// Between ownership changes this system's per-frame cost is two integer
// compares.
//
// What deliberately remains on a timer is BLOOD: the Feraldis stain map is
// its own mechanic (docs/Design/Age_1_Feraldis.md), and its decay-on-tended-
// ground rule now reads "tended" from the rasterized ownership grid — your
// territory cleans itself, wild ground stays stained forever.

using TheWaningBorder.Influence;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Systems.World
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct InfluenceMapSystem : ISystem
    {
        /// <summary>Blood decay cadence. Fixed dt so the fade is frame-rate
        /// independent, exactly as the old tick was.</summary>
        private const float BloodTickInterval = 0.5f;

        // Blood fades over ~3 minutes (design 2026-07-06): a saturated
        // stain (100) drops below the terrain-paint threshold (15) after
        // ~2 min and reaches zero at ~4½ min.
        private const float BloodDecayFractionPerSecond = 0.015f;
        private const float BloodDecayLinearPerSecond = 0.03f;

        /// <summary>Ownership strength at/above which ground counts as
        /// "tended" — blood fades there; below it blood is eternal (§2.5b
        /// rev.3). The rasterized grid is full-strength-or-nothing, so any
        /// owned cell clears comfortably.</summary>
        private const float BloodCleanInfluenceThreshold = 0.2f;

        private double _lastBloodTick;
        private int _lastOwnershipVersion;

        public void OnCreate(ref SystemState state)
        {
            // Fresh match world → fresh maps (the stores are static and would
            // otherwise leak the previous match's territory / blood).
            PlayerInfluenceMap.Reset();
            BloodMap.Reset();
            _lastBloodTick = double.MinValue;
            _lastOwnershipVersion = int.MinValue;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!PlayerInfluenceMap.Ready && !TryConfigure()) return;

            // Ownership rasterize — EVENT-DRIVEN, never per frame.
            int version = TheWaningBorder.World.Regions.TerritoryOwnership.Version;
            if (version != _lastOwnershipVersion
                && TheWaningBorder.World.Regions.RegionMap.Ready)
            {
                _lastOwnershipVersion = version;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                PlayerInfluenceMap.RebuildFromTerritories();
                sw.Stop();
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "TerritoryRasterize", sw.Elapsed.TotalMilliseconds);
            }

            // Blood decay keeps its fixed tick — but only while there IS
            // blood: a clean map must not keep bumping BloodMap.DataVersion,
            // or the version-gated ground mask would re-run its passes on a
            // map where nothing is happening.
            double now = SystemAPI.Time.ElapsedTime;
            if (now - _lastBloodTick < BloodTickInterval) return;
            _lastBloodTick = now;
            if (BloodMap.HasPresence(0.001f))
                BloodMap.DecayInsideInfluence(
                    BloodDecayFractionPerSecond * BloodTickInterval,
                    BloodDecayLinearPerSecond * BloodTickInterval,
                    BloodCleanInfluenceThreshold);
        }

        private static bool TryConfigure()
        {
            // Map bounds come from the baked terrain (every playable map is
            // hand-authored with one). Until it exists, stay dormant.
            var terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return false;

            Vector3 pos = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            PlayerInfluenceMap.Configure(
                new Vector2(pos.x, pos.z),
                new Vector2(size.x, size.z));
            BloodMap.Configure(
                new Vector2(pos.x, pos.z),
                new Vector2(size.x, size.z));
            return true;
        }
    }
}
