// AIStrengthMap.cs
// The AI's "who is standing where, and how strong" picture, refreshed
// INCREMENTALLY instead of re-read per question.
//
// WHY (2026-09-25 AI perf pass)
//
// TacticalQuery.StrengthInRadius copied every unit's entity, faction,
// transform and health out of the world — four full arrays — on EVERY call,
// and it was called per intel sighting (target scoring), per visible enemy
// building (IntelSystem garrison tally), per damaged building (posture), three
// times per army mission (retreat check), per engaged army per second (focus
// fire) and per idle cluster (IdleFormUpSystem). A mid-game think made
// hundreds of those calls.
//
// Operator direction: "Strength scan can live with being done once per 5 sec.
// Do it incrementally. It's not a problem if info is 5 s out of date." So:
//
//   * a BACK buffer is filled a slice per frame — refreshSeconds worth of
//     frames to walk every unit and building once;
//   * when the walk completes it is bucketed into a coarse spatial hash and
//     SWAPPED with the front buffer, so a reader always sees one complete
//     picture, never a half-built one;
//   * every strength read (TacticalQuery.*StrengthInRadius,
//     AIEngagement.StaticDefencePower / PickPriorityTarget) scans only the
//     hash cells its radius touches, against the snapshot positions.
//
// PickPriorityTarget re-reads the LIVE state of each candidate (position,
// health, damage) so a focus-fire order never names a body that died or
// walked off since the snapshot; only WHO is a candidate can be up to one
// refresh stale.
//
// HOST-ONLY STATE. The AI brains and the other readers (IdleFormUpSystem)
// run only where GameSettings.ShouldRunAIBrains() is true, and everything
// they decide leaves as lockstep commands, so this picture never has to be
// identical on two machines. Match-scoped: a new SimCadence epoch or a new
// world drops both buffers.

using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AIStrengthMap : SystemBase
    {
        #region Cached queries

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Buildings;

        #endregion

        #region Tuning

        /// <summary>The numbers live in AIStrengthMap.asset.</summary>
        static AIStrengthMapConfig Cfg => AIStrengthMapConfig.I;

        #endregion

        // ─────────────────────────────────────────────────────────────────
        // SNAPSHOT
        // ─────────────────────────────────────────────────────────────────

        private const byte KindUnit = 1, KindBuilding = 2;
        private const byte FlagPlunderer = 1;

        private struct Rec
        {
            public Entity E;
            public float X, Z;
            public Faction F;
            public byte Kind;
            public byte Flags;
            /// <summary>Unit: damage x2 + hp/10 (TacticalQuery's scale).</summary>
            public int Strength;
            /// <summary>Building: static defence power, or -1 when it does
            /// not count (walls, foundations, Border, dead).</summary>
            public int StaticPower;
        }

        private sealed class Snapshot
        {
            public Rec[] Recs = new Rec[256];
            public int Count;
            public float2 Origin;
            public float Cell;
            public int W, H;
            public int[] CellStart = new int[1];
            public int[] CellItems = new int[256];
            public bool Complete;
            /// <summary>Sim time the walk behind this picture finished.</summary>
            public double BuiltAt;

            public void Clear() { Count = 0; Complete = false; }

            public void Add(in Rec r)
            {
                if (Count == Recs.Length) System.Array.Resize(ref Recs, Recs.Length * 2);
                Recs[Count++] = r;
            }

            public void Bucket(float cell)
            {
                Cell = cell;
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                for (int i = 0; i < Count; i++)
                {
                    var p = new float2(Recs[i].X, Recs[i].Z);
                    lo = math.min(lo, p); hi = math.max(hi, p);
                }
                if (lo.x > hi.x) { lo = float2.zero; hi = float2.zero; }
                Origin = lo;
                W = math.max(1, (int)((hi.x - lo.x) / cell) + 1);
                H = math.max(1, (int)((hi.y - lo.y) / cell) + 1);
                int cells = W * H;
                if (CellStart.Length < cells + 1) CellStart = new int[cells + 1];
                else System.Array.Clear(CellStart, 0, cells + 1);
                if (CellItems.Length < Count) CellItems = new int[math.max(Count, CellItems.Length * 2)];

                for (int i = 0; i < Count; i++) CellStart[CellOf(Recs[i].X, Recs[i].Z) + 1]++;
                for (int c = 0; c < cells; c++) CellStart[c + 1] += CellStart[c];
                if (_cursor.Length < cells) _cursor = new int[cells];
                System.Array.Copy(CellStart, _cursor, cells);
                for (int i = 0; i < Count; i++)
                    CellItems[_cursor[CellOf(Recs[i].X, Recs[i].Z)]++] = i;
                Complete = true;
            }
            private int[] _cursor = new int[1];

            public int CellOf(float x, float z)
            {
                int cx = math.clamp((int)((x - Origin.x) / Cell), 0, W - 1);
                int cz = math.clamp((int)((z - Origin.y) / Cell), 0, H - 1);
                return cz * W + cx;
            }

            public void Range(float3 pos, float r, out int x0, out int z0, out int x1, out int z1)
            {
                x0 = math.clamp((int)math.floor((pos.x - r - Origin.x) / Cell), 0, W - 1);
                z0 = math.clamp((int)math.floor((pos.z - r - Origin.y) / Cell), 0, H - 1);
                x1 = math.clamp((int)math.floor((pos.x + r - Origin.x) / Cell), 0, W - 1);
                z1 = math.clamp((int)math.floor((pos.z + r - Origin.y) / Cell), 0, H - 1);
            }
        }

        private static Snapshot _front = new Snapshot();
        private static Snapshot _back = new Snapshot();

        // The walk in progress: entities to visit, captured once per refresh.
        private static Entity[] _pending = new Entity[512];
        private static int _pendingCount, _pendingNext;
        private static bool _walking;

        private static Unity.Entities.World _world;
        private static int _epoch = -1;
        private static int _lastReadFrame = -100000;

        // ─────────────────────────────────────────────────────────────────
        // SYSTEM: advance the walk one slice per frame
        // ─────────────────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            var em = EntityManager;
            ResetIfStale(em);

            // Nobody asking — do not pay for a picture no one reads. The next
            // reader rebuilds synchronously (EnsureReadable).
            float dt = SystemAPI.Time.DeltaTime;
            float idleFrames = Cfg.idleStopSeconds / math.max(dt, 1e-3f);
            if (UnityEngine.Time.frameCount - _lastReadFrame > idleFrames)
            {
                _walking = false;
                return;
            }

            if (!_walking) BeginWalk(em);

            // Slice: enough entities per frame that one walk takes about
            // refreshSeconds, never fewer than the floor.
            float frames = math.max(1f, Cfg.refreshSeconds / math.max(dt, 1e-3f));
            int slice = math.max(Cfg.minEntitiesPerFrame,
                (int)math.ceil(_pendingCount / frames));
            StepWalk(em, slice);
        }

        private static void ResetIfStale(EntityManager em)
        {
            if (ReferenceEquals(_world, em.World) && _epoch == SimCadence.Epoch) return;
            _world = em.World;
            _epoch = SimCadence.Epoch;
            _front.Clear();
            _back.Clear();
            _walking = false;
        }

        private static void BeginWalk(EntityManager em)
        {
            _back.Clear();
            _pendingCount = 0;
            _pendingNext = 0;
            Append(QC_Units.Get(em, QT_Units));
            Append(QC_Buildings.Get(em, QT_Buildings));
            _walking = true;
        }

        private static void Append(EntityQuery q)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            if (_pending.Length < _pendingCount + ents.Length)
                System.Array.Resize(ref _pending, math.max(_pendingCount + ents.Length, _pending.Length * 2));
            for (int i = 0; i < ents.Length; i++) _pending[_pendingCount++] = ents[i];
        }

        private static void StepWalk(EntityManager em, int budget)
        {
            int end = math.min(_pendingCount, _pendingNext + budget);
            for (; _pendingNext < end; _pendingNext++)
                Record(em, _pending[_pendingNext]);

            if (_pendingNext < _pendingCount) return;

            // Walk complete: bucket and publish.
            _back.Bucket(math.max(4f, Cfg.cellSize));
            _back.BuiltAt = TheWaningBorder.Core.SimClock.Elapsed;
            (_front, _back) = (_back, _front);
            _walking = false;
        }

        private static void Record(EntityManager em, Entity e)
        {
            if (!em.Exists(e) || !em.HasComponent<Health>(e)
                || !em.HasComponent<FactionTag>(e) || !em.HasComponent<LocalTransform>(e)) return;
            var hp = em.GetComponentData<Health>(e);
            if (hp.Value <= 0) return;
            var pos = em.GetComponentData<LocalTransform>(e).Position;
            var fac = em.GetComponentData<FactionTag>(e).Value;

            var r = new Rec { E = e, X = pos.x, Z = pos.z, F = fac, StaticPower = -1 };
            if (em.HasComponent<UnitTag>(e))
            {
                r.Kind = KindUnit;
                if (em.HasComponent<PlundererTag>(e)) r.Flags |= FlagPlunderer;
                int dmg = em.HasComponent<Damage>(e) ? em.GetComponentData<Damage>(e).Value : 0;
                r.Strength = math.max(0, dmg * 2 + hp.Value / 10);
            }
            else if (em.HasComponent<BuildingTag>(e))
            {
                r.Kind = KindBuilding;
                // AIEngagement.StaticDefencePower's rules, evaluated once.
                if (fac != Faction.Border && !em.HasComponent<WallTag>(e)
                    && !em.HasComponent<UnderConstruction>(e))
                {
                    int power = hp.Value / 40;
                    if (em.HasComponent<BuildingRangedAttack>(e))
                    {
                        var atk = em.GetComponentData<BuildingRangedAttack>(e);
                        power += atk.Damage * 2 * math.max(1, atk.MaxTargets);
                    }
                    r.StaticPower = math.max(0, power);
                }
            }
            else return;
            _back.Add(r);
        }

        /// <summary>
        /// Make sure a complete picture exists before a read. The first read
        /// of a match (or after an idle stretch) finishes the walk in one go —
        /// one full pass, the same cost a single old-style query paid.
        /// </summary>
        private static Snapshot EnsureReadable(EntityManager em)
        {
            ResetIfStale(em);
            _lastReadFrame = UnityEngine.Time.frameCount;
            // A picture older than two refresh windows means the walk was
            // parked (nobody read for a while) — do not hand it out.
            if (_front.Complete
                && TheWaningBorder.Core.SimClock.Elapsed - _front.BuiltAt <= 2.0 * Cfg.refreshSeconds)
                return _front;
            if (!_walking) BeginWalk(em);
            StepWalk(em, int.MaxValue);
            return _front;
        }

        // ─────────────────────────────────────────────────────────────────
        // READS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Summed unit strength inside the radius. <paramref name="matchFaction"/>
        /// true = <paramref name="faction"/>'s side (own + allies); false =
        /// everyone else (Border included). Plunderers never count — they are
        /// economy, not army (IntelSystem.Classify).
        /// </summary>
        public static int StrengthInRadius(EntityManager em, float3 pos, float radius,
            Faction faction, bool matchFaction)
        {
            var s = EnsureReadable(em);
            float r2 = radius * radius;
            int sum = 0;
            s.Range(pos, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * s.W + x;
                    for (int k = s.CellStart[c]; k < s.CellStart[c + 1]; k++)
                    {
                        ref readonly var r = ref s.Recs[s.CellItems[k]];
                        if (r.Kind != KindUnit || (r.Flags & FlagPlunderer) != 0) continue;
                        // "same" means "on my side" — own faction or an ally.
                        // docs/Design/Teams.md
                        if (matchFaction != Alliances.AreAllied(faction, r.F)) continue;
                        float dx = r.X - pos.x, dz = r.Z - pos.z;
                        if (dx * dx + dz * dz > r2) continue;
                        sum += r.Strength;
                    }
                }
            return sum;
        }

        /// <summary>
        /// Units of exactly <paramref name="faction"/> inside the radius, and
        /// their summed strength (same scale as <see cref="StrengthInRadius"/>).
        /// The curse-node estimate needs the COUNT, because a Crystalling
        /// pack's damage grows with its size (BorderSettingsSO
        /// crystallingPack*) and the per-unit strength cannot show that.
        /// </summary>
        public static int UnitsOfFactionInRadius(EntityManager em, float3 pos, float radius,
            Faction faction, out int strength)
        {
            var s = EnsureReadable(em);
            float r2 = radius * radius;
            int count = 0;
            strength = 0;
            s.Range(pos, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * s.W + x;
                    for (int k = s.CellStart[c]; k < s.CellStart[c + 1]; k++)
                    {
                        ref readonly var r = ref s.Recs[s.CellItems[k]];
                        if (r.Kind != KindUnit || r.F != faction) continue;
                        float dx = r.X - pos.x, dz = r.Z - pos.z;
                        if (dx * dx + dz * dz > r2) continue;
                        count++;
                        strength += r.Strength;
                    }
                }
            return count;
        }

        /// <summary>Hostile static defence power inside the radius —
        /// AIEngagement.StaticDefencePower's number.</summary>
        public static int StaticPowerInRadius(EntityManager em, Faction faction, float3 pos, float radius)
        {
            var s = EnsureReadable(em);
            float r2 = radius * radius;
            int sum = 0;
            s.Range(pos, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * s.W + x;
                    for (int k = s.CellStart[c]; k < s.CellStart[c + 1]; k++)
                    {
                        ref readonly var r = ref s.Recs[s.CellItems[k]];
                        if (r.Kind != KindBuilding || r.StaticPower < 0) continue;
                        if (!Alliances.AreHostile(faction, r.F)) continue;
                        float dx = r.X - pos.x, dz = r.Z - pos.z;
                        if (dx * dx + dz * dz > r2) continue;
                        sum += r.StaticPower;
                    }
                }
            return sum;
        }

        /// <summary>
        /// Hostile UNITS the snapshot placed within <paramref name="radius"/>
        /// plus the configured movement margin — candidates only; the caller
        /// re-reads live state. Filled into <paramref name="into"/> (cleared
        /// first).
        /// </summary>
        public static void HostileUnitCandidates(EntityManager em, Faction faction,
            float3 pos, float radius, System.Collections.Generic.List<Entity> into)
        {
            into.Clear();
            var s = EnsureReadable(em);
            float reach = radius + math.max(0f, Cfg.candidateMargin);
            float r2 = reach * reach;
            s.Range(pos, reach, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * s.W + x;
                    for (int k = s.CellStart[c]; k < s.CellStart[c + 1]; k++)
                    {
                        ref readonly var r = ref s.Recs[s.CellItems[k]];
                        if (r.Kind != KindUnit) continue;
                        if (!Alliances.AreHostile(faction, r.F)) continue;
                        float dx = r.X - pos.x, dz = r.Z - pos.z;
                        if (dx * dx + dz * dz > r2) continue;
                        into.Add(r.E);
                    }
                }
        }

        /// <summary>
        /// <see cref="HostileUnitCandidates"/> for the OTHER side: units of
        /// <paramref name="faction"/> and its allies. Candidates only, same
        /// margin and staleness — the caller re-reads live state. Used by the
        /// tactics layer's ability value checks (AITactics).
        /// </summary>
        public static void FriendlyUnitCandidates(EntityManager em, Faction faction,
            float3 pos, float radius, System.Collections.Generic.List<Entity> into)
        {
            into.Clear();
            var s = EnsureReadable(em);
            float reach = radius + math.max(0f, Cfg.candidateMargin);
            float r2 = reach * reach;
            s.Range(pos, reach, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * s.W + x;
                    for (int k = s.CellStart[c]; k < s.CellStart[c + 1]; k++)
                    {
                        ref readonly var r = ref s.Recs[s.CellItems[k]];
                        if (r.Kind != KindUnit) continue;
                        if (!Alliances.AreAllied(faction, r.F)) continue;
                        float dx = r.X - pos.x, dz = r.Z - pos.z;
                        if (dx * dx + dz * dz > r2) continue;
                        into.Add(r.E);
                    }
                }
        }

        /// <summary>
        /// The faction's own (or an ally's) building that SHOOTS — static
        /// power above zero — nearest to <paramref name="pos"/> within
        /// <paramref name="radius"/>, as a fall-back anchor for an army that
        /// is losing (AITactics). Ties go to the lower entity index.
        /// </summary>
        public static bool NearestFriendlyDefence(EntityManager em, Faction faction,
            float3 pos, float radius, out float3 at, out int power)
        {
            at = default; power = 0;
            var s = EnsureReadable(em);
            float r2 = radius * radius;
            float best = float.MaxValue;
            Entity bestE = Entity.Null;
            s.Range(pos, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * s.W + x;
                    for (int k = s.CellStart[c]; k < s.CellStart[c + 1]; k++)
                    {
                        ref readonly var r = ref s.Recs[s.CellItems[k]];
                        if (r.Kind != KindBuilding || r.StaticPower <= 0) continue;
                        if (!Alliances.AreAllied(faction, r.F)) continue;
                        float dx = r.X - pos.x, dz = r.Z - pos.z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 > r2) continue;
                        if (d2 < best || (d2 == best && r.E.Index < bestE.Index))
                        {
                            best = d2; bestE = r.E;
                            at = new float3(r.X, pos.y, r.Z);
                            power = r.StaticPower;
                        }
                    }
                }
            return bestE != Entity.Null;
        }

        /// <summary>Own-side (own + allies) static defence power inside the
        /// radius — the friendly mirror of <see cref="StaticPowerInRadius"/>.</summary>
        public static int FriendlyStaticPowerInRadius(EntityManager em, Faction faction,
            float3 pos, float radius)
        {
            var s = EnsureReadable(em);
            float r2 = radius * radius;
            int sum = 0;
            s.Range(pos, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * s.W + x;
                    for (int k = s.CellStart[c]; k < s.CellStart[c + 1]; k++)
                    {
                        ref readonly var r = ref s.Recs[s.CellItems[k]];
                        if (r.Kind != KindBuilding || r.StaticPower < 0) continue;
                        if (!Alliances.AreAllied(faction, r.F)) continue;
                        float dx = r.X - pos.x, dz = r.Z - pos.z;
                        if (dx * dx + dz * dz > r2) continue;
                        sum += r.StaticPower;
                    }
                }
            return sum;
        }
    }
}
