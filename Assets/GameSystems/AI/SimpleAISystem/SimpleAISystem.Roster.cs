// SimpleAISystem.Roster.cs
// The faction's own combat units, read once per simulation step and shared
// by every draft in a think (2026-10-16 AI perf pass).
//
// Eleven drafts — the claim pool, the claim / curse / reconquest assaults,
// the attack wave, its reinforcements, the raid party, the reclaim squad,
// the first-RP hunt, the wall guard, the economy-defence pool and the
// Shardroot strike — each copied EVERY unit on the map (entity, UnitTag,
// FactionTag, often LocalTransform) to keep the 1/8th that is theirs, then
// asked the EntityManager eight questions per own soldier (UnderConstruction
// and the seven-tag IsVerbUnit) before reaching their own rules. With three
// thousand units and several drafts per think that was the bulk of a step.
//
// The roster keeps the SAME ROWS IN THE SAME ORDER the drafts used to see
// (the same query, filtered by faction and combat class), so every
// "first N soldiers" draft picks the same soldiers. The four
// rarely-changing facts every draft tests first are read once per unit per
// step; everything that changes with orders (TransientState, mission
// rosters, claim squads) is still read live by the draft itself.
//
// Host-only (the AI runs only where GameSettings.ShouldRunAIBrains()), a
// sim step old at most — the AI acts through lockstep commands, which land
// on a later tick anyway.

using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private const byte RosterUnderConstruction = 1;
        private const byte RosterVerb = 2;
        private const byte RosterNoControl = 4;
        private const byte RosterPlunderer = 8;

        private sealed class CombatRoster
        {
            public Entity[] E = new Entity[64];
            public float3[] P = new float3[64];
            public byte[] Flags = new byte[64];
            public int Count;

            public long Step = long.MinValue;
            public int Epoch = int.MinValue;
            public ulong World;

            public bool UnderConstruction(int i) => (Flags[i] & RosterUnderConstruction) != 0;
            public bool Verb(int i) => (Flags[i] & RosterVerb) != 0;
            public bool NoControl(int i) => (Flags[i] & RosterNoControl) != 0;
            public bool Plunderer(int i) => (Flags[i] & RosterPlunderer) != 0;

            public void Add(Entity e, float3 p, byte flags)
            {
                if (Count == E.Length)
                {
                    int cap = E.Length * 2;
                    System.Array.Resize(ref E, cap);
                    System.Array.Resize(ref P, cap);
                    System.Array.Resize(ref Flags, cap);
                }
                E[Count] = e; P[Count] = p; Flags[Count] = flags;
                Count++;
            }
        }

        /// <summary>One per player faction (Faction is a byte; 0-15 covers
        /// the eight seats, the curse and headroom).</summary>
        private static readonly CombatRoster[] _rosters = new CombatRoster[16];

        // The step's one bulk read of every unit, bucketed by nobody: each
        // faction's roster filters it on first ask.
        private static Entity[] _rAllE = System.Array.Empty<Entity>();
        private static UnitClass[] _rAllCls = System.Array.Empty<UnitClass>();
        private static Faction[] _rAllF = System.Array.Empty<Faction>();
        private static float3[] _rAllP = System.Array.Empty<float3>();
        private static int _rAllN;
        private static long _rAllStep = long.MinValue;
        private static int _rAllEpoch = int.MinValue;
        private static ulong _rAllWorld;

        private static void RefreshAllUnits(EntityManager em)
        {
            ulong world = em.WorldUnmanaged.SequenceNumber;
            if (_rAllStep == SimClock.Steps && _rAllEpoch == SimCadence.Epoch && _rAllWorld == world) return;
            _rAllStep = SimClock.Steps;
            _rAllEpoch = SimCadence.Epoch;
            _rAllWorld = world;

            var q = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int n = ents.Length;
            if (_rAllE.Length < n)
            {
                int cap = math.max(n, _rAllE.Length * 2);
                _rAllE = new Entity[cap];
                _rAllCls = new UnitClass[cap];
                _rAllF = new Faction[cap];
                _rAllP = new float3[cap];
            }
            for (int i = 0; i < n; i++)
            {
                _rAllE[i] = ents[i];
                _rAllCls[i] = tags[i].Class;
                _rAllF[i] = facs[i].Value;
                _rAllP[i] = xfs[i].Position;
            }
            _rAllN = n;
        }

        /// <summary>
        /// The faction's combat-class units this sim step, in query order,
        /// with position and the four standing flags (under construction,
        /// verb unit, not controllable, plunderer). Every draft that used to
        /// walk the whole unit query for "my soldiers" reads this instead.
        /// </summary>
        private static CombatRoster OwnCombatRoster(EntityManager em, Faction faction)
        {
            int fi = (int)faction;
            if (fi < 0 || fi >= _rosters.Length) fi = _rosters.Length - 1;   // never a player seat
            var r = _rosters[fi] ??= new CombatRoster();
            ulong world = em.WorldUnmanaged.SequenceNumber;
            if (r.Step == SimClock.Steps && r.Epoch == SimCadence.Epoch && r.World == world) return r;
            r.Step = SimClock.Steps;
            r.Epoch = SimCadence.Epoch;
            r.World = world;
            r.Count = 0;

            RefreshAllUnits(em);
            for (int i = 0; i < _rAllN; i++)
            {
                if (_rAllF[i] != faction || !IsCombatClass(_rAllCls[i])) continue;
                var e = _rAllE[i];
                byte flags = 0;
                if (em.HasComponent<UnderConstruction>(e)) flags |= RosterUnderConstruction;
                if (IsVerbUnit(em, e)) flags |= RosterVerb;
                if (em.HasComponent<NotControllableTag>(e)) flags |= RosterNoControl;
                if (em.HasComponent<PlundererTag>(e)) flags |= RosterPlunderer;
                r.Add(e, _rAllP[i], flags);
            }
            return r;
        }
    }
}
