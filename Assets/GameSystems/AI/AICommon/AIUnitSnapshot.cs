// AIUnitSnapshot.cs
// One read of every unit's (entity, faction, health, position) per simulation
// step, shared by every AI scan that wants "the units near X" (2026-10-15 AI
// perf pass).
//
// The endgame power pickers, the base-threat count and the worker flee each
// walked the whole unit query with three EntityManager calls per unit —
// GetComponentData<FactionTag> / <Health> / <LocalTransform> — and a single
// think ran that walk up to a dozen times (once per sect power slot). On a
// development player that is ~8k EntityManager calls per walk. The snapshot
// is four bulk column copies, taken at most once per sim step and read as
// plain arrays.
//
// Same rows in the same order as ToEntityArray on the same query (the
// columns are copied from one query), so every scan that used to break ties
// by "first encountered" still picks the same unit. Host-only reader (the AI
// runs only where GameSettings.ShouldRunAIBrains()): nothing simulated reads
// it. A snapshot is a step old at most; AI systems act through lockstep
// commands, which land on a later tick anyway.

using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    public static class AIUnitSnapshot
    {
        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Units;

        public static Entity[] Ents = System.Array.Empty<Entity>();
        public static Faction[] Fac = System.Array.Empty<Faction>();
        public static int[] Hp = System.Array.Empty<int>();
        public static int[] HpMax = System.Array.Empty<int>();
        public static float3[] Pos = System.Array.Empty<float3>();
        public static int Count;

        private static long _step = long.MinValue;
        private static int _epoch = int.MinValue;
        private static ulong _world;

        /// <summary>Make the snapshot current for this sim step (no-op when
        /// it already is). Call before reading the arrays.</summary>
        public static void Refresh(EntityManager em)
        {
            ulong world = em.WorldUnmanaged.SequenceNumber;
            if (_step == SimClock.Steps && _epoch == SimCadence.Epoch && _world == world) return;
            _step = SimClock.Steps;
            _epoch = SimCadence.Epoch;
            _world = world;

            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int n = ents.Length;
            if (Ents.Length < n)
            {
                int cap = math.max(n, Ents.Length * 2);
                Ents = new Entity[cap];
                Fac = new Faction[cap];
                Hp = new int[cap];
                HpMax = new int[cap];
                Pos = new float3[cap];
            }
            for (int i = 0; i < n; i++)
            {
                Ents[i] = ents[i];
                Fac[i] = facs[i].Value;
                Hp[i] = hps[i].Value;
                HpMax[i] = hps[i].Max;
                Pos[i] = xfs[i].Position;
            }
            Count = n;
        }

        /// <summary>Force the next <see cref="Refresh"/> to re-read (after a
        /// change the caller itself made inside the step).</summary>
        public static void Invalidate() => _step = long.MinValue;
    }
}
