// GallopTestDriver.cs
// Runs the Gallop Test scenario (ScenarioType.GallopTest). Two lanes, Blue
// (Full Gallop researched) and Green (not), each a cavalry squad and a Red
// sword block standing across the middle of the lane:
//
//   * PASSES. Every pass orders both squads, with plain MOVE orders, to the far
//     end of their lane, straight through their Red block. A pass ends when
//     every rider has arrived or after PassTimeout; the next one rides back.
//   * RE-FORMING. Between passes a lane whose Red block has fallen below
//     ReformBelow gets a fresh block, and a lane whose squad is gone gets a
//     fresh squad, so the comparison can run as long as you watch.
//   * THE LOG. Once a second: how many riders in each lane are galloping. At
//     the end of each pass, per lane: riders alive, rider HP lost, Red units
//     killed and Red HP removed, i.e. what galloping changed. Lines start
//     "[GallopTest]" (TWBLog), and each pass also posts an on-screen notice.
//
// Untick Automate in the Inspector (or select the riders and give orders) to
// drive the Blue squad by hand; the driver then only logs.
// docs/Design/Age_1_Alanthor.md § Galloping.

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    public sealed class GallopTestDriver : MonoBehaviour
    {
        /// <summary>Squads start at -this and ride to +this, and back.</summary>
        public const float LaneHalfLength = 70f;

        public bool Automate = true;
        public float FirstPassAt = 6f;
        public float PassTimeout = 40f;
        public float PauseBetweenPasses = 3f;
        public int ReformBelow = 8;

        private const float LaneWidth = 10f;      // a unit is "in the lane" within this of its centre line
        private const float ArriveDistance = 5f;

        private static readonly string[] RiderIds =
            { "Alanthor_Cataphract", "Alanthor_Cataphract", "Alanthor_Cataphract", "Alanthor_Cataphract",
              "Alanthor_Outrider", "Alanthor_Outrider", "Alanthor_Outrider", "Alanthor_Outrider" };
        private const int SquadCols = 4;
        private const float SquadSpacing = 3f;

        private float2 _origin;          // RecenterScenario's offset
        private float _laneZ;
        private int _pass;
        private int _direction = 1;      // +1 = riding east
        private bool _passOpen;
        private float _passStarted;
        private float _nextPassAt;
        private float _nextLogAt;
        private readonly LaneStats[] _lanes = { new LaneStats(Faction.Blue), new LaneStats(Faction.Green) };

        private sealed class LaneStats
        {
            public readonly Faction Rider;
            public float Z;
            public int RidersAtStart, RedAtStart;
            public long RiderHpAtStart, RedHpAtStart;
            public LaneStats(Faction rider) { Rider = rider; }
        }

        public void Configure(float laneZ)
        {
            _laneZ = laneZ;
            _lanes[0].Z = laneZ;
            _lanes[1].Z = -laneZ;
            _nextPassAt = FirstPassAt;
            TWBLog.Log("[GallopTest] Blue lane: Full Gallop researched. Green lane: control (no research). "
                + "Red sword blocks stand in the middle of both lanes.");
        }

        /// <summary>RecenterScenario moves the entities; this moves the lanes.</summary>
        public void Shift(float dx, float dz)
        {
            _origin.x += dx;
            _origin.y += dz;
        }

        private float3 Point(float x, float z)
            => TerrainUtility.SnapToTerrain(new float3(_origin.x + x, 0f, _origin.y + z));

        void Update()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            float now = SimClock.Now;

            if (now >= _nextLogAt)
            {
                _nextLogAt = now + 1f;
                if (_passOpen)
                {
                    using var blue = Riders(em, _lanes[0]);
                    using var green = Riders(em, _lanes[1]);
                    TWBLog.Log($"[GallopTest] t={now:F0}s pass {_pass}: galloping Blue {Galloping(em, Faction.Blue)}/{blue.Length}"
                        + $", Green {Galloping(em, Faction.Green)}/{green.Length}");
                }
            }

            if (!Automate) return;

            if (_passOpen)
            {
                bool allIn = AllArrived(em, _lanes[0]) && AllArrived(em, _lanes[1]);
                if (!allIn && now - _passStarted < PassTimeout) return;
                ClosePass(em, now, allIn);
                return;
            }

            if (now < _nextPassAt) return;
            OpenPass(em, now);
        }

        private void OpenPass(EntityManager em, float now)
        {
            // Re-form anything worn out, at the end the squad now stands on.
            float startX = -_direction * LaneHalfLength;
            foreach (var lane in _lanes)
            {
                int ridersLeft, redLeft;
                using (var r = Riders(em, lane)) ridersLeft = r.Length;
                using (var b = RedIn(em, lane)) redLeft = b.Length;
                if (ridersLeft == 0)
                {
                    SpawnSquad(em, lane.Rider, Point(startX, lane.Z));
                    TWBLog.Log($"[GallopTest] {lane.Rider} squad re-formed");
                }
                if (redLeft < ReformBelow)
                {
                    SpawnBlock(em, Point(0f, lane.Z));
                    TWBLog.Log($"[GallopTest] Red block re-formed in the {lane.Rider} lane");
                }
            }

            _pass++;
            foreach (var lane in _lanes)
            {
                using var riders = Riders(em, lane);
                lane.RidersAtStart = riders.Length;
                lane.RiderHpAtStart = HpOf(em, riders);
                using var red = RedIn(em, lane);
                lane.RedAtStart = red.Length;
                lane.RedHpAtStart = HpOf(em, red);

                float endX = _direction * LaneHalfLength;
                for (int i = 0; i < riders.Length; i++)
                    MoveCommandHelper.Execute(em, riders[i], Point(endX, lane.Z) + SlotOffset(i, _direction));
            }
            _passOpen = true;
            _passStarted = now;
            SimSignals.Notify($"Gallop test: pass {_pass} rides {(_direction > 0 ? "east" : "west")}.");
        }

        private void ClosePass(EntityManager em, float now, bool allArrived)
        {
            _passOpen = false;
            _nextPassAt = now + PauseBetweenPasses;
            string report = $"[GallopTest] pass {_pass} done in {now - _passStarted:F0}s{(allArrived ? "" : " (timed out)")}";
            string notice = $"Pass {_pass}:";
            foreach (var lane in _lanes)
            {
                using var riders = Riders(em, lane);
                using var red = RedIn(em, lane);
                long riderHpLost = lane.RiderHpAtStart - HpOf(em, riders);
                int redKilled = lane.RedAtStart - red.Length;
                long redHpRemoved = lane.RedHpAtStart - HpOf(em, red);
                string tag = lane.Rider == Faction.Blue ? "Blue (gallop)" : "Green (control)";
                report += $" | {tag}: riders {riders.Length}/{lane.RidersAtStart}, rider HP lost {riderHpLost}, "
                        + $"Red killed {redKilled}, Red HP removed {redHpRemoved}";
                notice += $" {tag} killed {redKilled}, lost {lane.RidersAtStart - riders.Length} riders.";
            }
            TWBLog.Log(report);
            SimSignals.Notify(notice);
            _direction = -_direction;
        }

        // ── Spawning (also used by ScenarioSetup.SpawnGallopTest) ─────────

        /// <summary>Eight riders in two ranks centred on <paramref name="center"/>.</summary>
        internal static void SpawnSquad(EntityManager em, Faction faction, float3 center)
        {
            for (int i = 0; i < RiderIds.Length; i++)
            {
                var p = TerrainUtility.SnapToTerrain(center + SlotOffset(i, 1));
                UnitFactory.Create(em, RiderIds[i], p, faction);
            }
        }

        /// <summary>A 5 x 4 Red sword block across the lane, standing its ground
        /// (Defensive: it fights what reaches it and never chases).</summary>
        internal static void SpawnBlock(EntityManager em, float3 center)
        {
            const int cols = 4, rows = 5;
            const float spacing = 1.6f;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var p = center + new float3((c - (cols - 1) * 0.5f) * spacing, 0f, (r - (rows - 1) * 0.5f) * spacing);
                var e = UnitFactory.Create(em, "Alanthor_Swordsman", TerrainUtility.SnapToTerrain(p), Faction.Red);
                if (e != Entity.Null) StanceCommandHelper.Apply(em, e, UnitStanceMode.Defensive);
            }
        }

        /// <summary>A rider's place in the squad: ranks across the lane, so two
        /// riders never share a destination.</summary>
        private static float3 SlotOffset(int i, int direction)
        {
            int col = i % SquadCols, row = i / SquadCols;
            return new float3(-direction * row * SquadSpacing, 0f, (col - (SquadCols - 1) * 0.5f) * SquadSpacing);
        }

        // ── Reads ──────────────────────────────────────────────────────────

        private static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        private static CachedEntityQuery QC_Units;

        private NativeList<Entity> Riders(EntityManager em, LaneStats lane)
            => UnitsIn(em, lane.Rider, lane.Z);

        private NativeList<Entity> RedIn(EntityManager em, LaneStats lane)
            => UnitsIn(em, Faction.Red, lane.Z);

        private NativeList<Entity> UnitsIn(EntityManager em, Faction faction, float laneZ)
        {
            var list = new NativeList<Entity>(Allocator.Temp);
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            float z = _origin.y + laneZ;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction || hps[i].Value <= 0) continue;
                if (math.abs(xfs[i].Position.z - z) > LaneWidth) continue;
                list.Add(ents[i]);
            }
            return list;
        }

        private static long HpOf(EntityManager em, NativeList<Entity> units)
        {
            long hp = 0;
            for (int i = 0; i < units.Length; i++)
                if (em.Exists(units[i])) hp += math.max(0, em.GetComponentData<Health>(units[i]).Value);
            return hp;
        }

        private bool AllArrived(EntityManager em, LaneStats lane)
        {
            using var riders = Riders(em, lane);
            float endX = _origin.x + _direction * LaneHalfLength;
            for (int i = 0; i < riders.Length; i++)
                if (math.abs(em.GetComponentData<LocalTransform>(riders[i]).Position.x - endX) > ArriveDistance + SquadSpacing)
                    return false;
            return true;
        }

        private static int Galloping(EntityManager em, Faction faction)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction && AlanthorGallop.IsGalloping(em, ents[i])) n++;
            return n;
        }
    }
}
