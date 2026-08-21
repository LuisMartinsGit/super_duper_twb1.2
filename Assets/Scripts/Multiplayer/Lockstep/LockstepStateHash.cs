// LockstepStateHash.cs
// The simulation checksum, broken out by SUBSYSTEM and by FACTION.
// Location: Assets/Scripts/Multiplayer/Lockstep/LockstepStateHash.cs
//
// WHY A BREAKDOWN AND NOT ONE NUMBER
// A single checksum answers "did the two worlds diverge". It cannot answer
// "at what", and that is the whole of the work. The 2026-08-21 investigation
// had three matched log pairs, knew the exact fork tick in each, and still had
// to reason from a 34-line position diff taken twenty ticks late to guess which
// subsystem was at fault.
//
// Every column below turns one of those guesses into a lookup. Diff two logs
// and the first differing line now says WHICH subsystem forked (positions? the
// nav intent that produced them? the veil grid under them?) and WHICH faction
// owns it, before any dump is opened.
//
// THE TOTAL IS BIT-FOR-BIT THE OLD FORMULA. It is the number peers exchange
// over the wire, so changing it would make this build unable to sync with
// itself mid-investigation. The categories are computed alongside, never from,
// the total.
//
// COST. The detailed pass does roughly a dozen component lookups per entity
// per tick. That is real work, and it is bought deliberately: these matches
// die inside thirty seconds, so a few hundred microseconds a tick costs
// nothing anyone will notice and buys the only evidence that matters. The
// detailed pass is skipped entirely when DeterministicLockstep is off.

using System.Collections.Generic;
using TheWaningBorder.Core.Multiplayer;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Multiplayer
{
    /// <summary>One tick's simulation state, hashed per subsystem.</summary>
    public struct SimStateHash
    {
        /// <summary>The wire checksum. Identical formula to the original
        /// ComputeGameStateChecksum -- do not change it.</summary>
        public uint Total;

        public int Entities;

        public uint Pos;      // LocalTransform.Position (quantised + raw bits)
        public uint Rot;      // LocalTransform.Rotation
        public uint Health;   // Health.Value / Max
        public uint Nav;      // destination, steering, flow, stuck, speed
        public uint Combat;   // target, attack cooldown
        public uint Work;     // construction, training, mining
        public uint Bank;     // faction resources
        public uint Tech;     // research state + queues, sect adoption
        public uint Rng;      // seeded RNG stream states
        public uint Veil;     // veil saturation grid + generation
        public uint Cost;     // nav cost field + generation

        /// <summary>Per-faction roll-up of the same per-entity hash that feeds
        /// <see cref="Total"/>. Names the guilty side before anything is
        /// opened -- in all three 2026-08-21 desyncs only one faction's units
        /// had moved, and this would have said so on the first line.</summary>
        public uint Faction0, Faction1, Faction2, Faction3,
                    Faction4, Faction5, Faction6, Faction7;

        public uint FactionAt(int i)
        {
            switch (i)
            {
                case 0: return Faction0;
                case 1: return Faction1;
                case 2: return Faction2;
                case 3: return Faction3;
                case 4: return Faction4;
                case 5: return Faction5;
                case 6: return Faction6;
                default: return Faction7;
            }
        }
    }

    /// <summary>
    /// Per-entity state, captured once per tick for the rolling trace.
    /// Raw float bits rather than formatted text: the checksum forks on a
    /// single ULP, so a dump that prints three decimals can show two identical
    /// lines for a genuine divergence.
    /// </summary>
    public struct EntitySnapshot
    {
        public int Id;
        public int SpawnTick;
        public byte Faction;
        public int Hp, HpMax;

        public uint Px, Py, Pz;          // position bits
        public uint Rx, Ry, Rz, Rw;      // rotation bits

        public byte HasDest; public uint Dx, Dz;
        public byte HasFlow; public uint Fx, Fz;
        public byte HasSteer; public uint Sx, Sz;
        public uint SmX, SmZ;            // smoothed direction bits
        public uint Speed;
        public byte Stuck, StuckAttempt;

        public int TargetId;             // network id, -1 = none
        public uint AtkTimer;

        public int WorkTarget;           // build site / assigned deposit, -1 = none
        public uint WorkA, WorkB;        // progress pair (see LockstepTrace.Format)
        public byte WorkKind;            // 0 none, 1 construction, 2 training, 3 mining
    }

    public static class LockstepStateHash
    {
        // Hoisted so the per-tick path allocates nothing, and cached so this
        // never creates a query per call -- an EntityQuery built every frame
        // from managed code is a documented leak in this codebase.
        private static readonly ComponentType[] VeilTypes = { typeof(VeilField) };
        private static readonly ComponentType[] CostTypes = { typeof(NavCostField) };
        private static TheWaningBorder.Core.CachedEntityQuery _veilQuery;
        private static TheWaningBorder.Core.CachedEntityQuery _costQuery;

        /// <summary>
        /// Hash the world. When <paramref name="detailed"/> is false only
        /// <see cref="SimStateHash.Total"/> and Entities are meaningful -- that
        /// is the original cheap path, kept for non-deterministic matches.
        ///
        /// <paramref name="snapshots"/>, when non-null, is filled with one row
        /// per networked entity in the same pass, sorted by network id.
        /// </summary>
        public static SimStateHash Compute(EntityManager em, EntityQuery networked,
                                           bool detailed, List<EntitySnapshot> snapshots)
        {
            var result = default(SimStateHash);
            if (snapshots != null) snapshots.Clear();

            var entities = networked.ToEntityArray(Allocator.Temp);
            var ids = networked.ToComponentDataArray<NetworkedEntity>(Allocator.Temp);

            result.Entities = entities.Length;

            unchecked
            {
                uint total = (uint)entities.Length * 2654435761u;

                uint hPos = 2166136261u, hRot = 2166136261u, hHealth = 2166136261u,
                     hNav = 2166136261u, hCombat = 2166136261u, hWork = 2166136261u,
                     hTech = 2166136261u;
                var perFaction = new uint[8];
                for (int i = 0; i < 8; i++) perFaction[i] = 2166136261u;

                for (int i = 0; i < entities.Length; i++)
                {
                    uint h = 2166136261u;
                    Mix(ref h, (uint)ids[i].NetworkId);

                    var e = entities[i];
                    int factionIndex = -1;

                    var snap = default(EntitySnapshot);
                    snap.Id = ids[i].NetworkId;
                    snap.SpawnTick = ids[i].SpawnTick;
                    snap.Faction = 255;
                    snap.Hp = -1; snap.HpMax = -1;
                    snap.TargetId = -1; snap.WorkTarget = -1;

                    if (em.HasComponent<Health>(e))
                    {
                        var hp = em.GetComponentData<Health>(e);
                        Mix(ref h, (uint)hp.Value);
                        Mix(ref h, (uint)hp.Max);
                        snap.Hp = hp.Value; snap.HpMax = hp.Max;

                        if (detailed)
                        {
                            Mix(ref hHealth, (uint)ids[i].NetworkId);
                            Mix(ref hHealth, (uint)hp.Value);
                            Mix(ref hHealth, (uint)hp.Max);
                        }
                    }

                    if (em.HasComponent<FactionTag>(e))
                    {
                        factionIndex = (int)em.GetComponentData<FactionTag>(e).Value;
                        Mix(ref h, (uint)factionIndex);
                        snap.Faction = (byte)factionIndex;
                    }

                    if (em.HasComponent<LocalTransform>(e))
                    {
                        var lt = em.GetComponentData<LocalTransform>(e);
                        float3 p = lt.Position;

                        // Millimetre quantisation, then -- in deterministic
                        // mode -- the exact bits. See the note on the original
                        // checksum: sub-millimetre drift once hid below the
                        // quantised mixes for 13 clean checks.
                        Mix(ref h, (uint)(int)math.round(p.x * 1000f));
                        Mix(ref h, (uint)(int)math.round(p.y * 1000f));
                        Mix(ref h, (uint)(int)math.round(p.z * 1000f));

                        if (GameSettings.DeterministicLockstep)
                        {
                            Mix(ref h, math.asuint(p.x));
                            Mix(ref h, math.asuint(p.y));
                            Mix(ref h, math.asuint(p.z));
                        }

                        snap.Px = math.asuint(p.x);
                        snap.Py = math.asuint(p.y);
                        snap.Pz = math.asuint(p.z);
                        var r = lt.Rotation.value;
                        snap.Rx = math.asuint(r.x); snap.Ry = math.asuint(r.y);
                        snap.Rz = math.asuint(r.z); snap.Rw = math.asuint(r.w);

                        if (detailed)
                        {
                            Mix(ref hPos, (uint)ids[i].NetworkId);
                            Mix(ref hPos, snap.Px); Mix(ref hPos, snap.Py); Mix(ref hPos, snap.Pz);

                            // Rotation is separate on purpose: a facing that
                            // forks without a position forking points at
                            // targeting, not at movement.
                            Mix(ref hRot, (uint)ids[i].NetworkId);
                            Mix(ref hRot, snap.Rx); Mix(ref hRot, snap.Ry);
                            Mix(ref hRot, snap.Rz); Mix(ref hRot, snap.Rw);
                        }
                    }

                    if (detailed)
                    {
                        CaptureNavCombatWork(em, e, ids[i].NetworkId, ref snap,
                                             ref hNav, ref hCombat, ref hWork);
                        CaptureTech(em, e, ids[i].NetworkId, ref hTech);
                    }

                    total += h;
                    if (factionIndex >= 0 && factionIndex < 8)
                    {
                        uint pf = perFaction[factionIndex];
                        Mix(ref pf, h);
                        perFaction[factionIndex] = pf;
                    }

                    if (snapshots != null) snapshots.Add(snap);
                }

                // Banks live on entities with no NetworkedEntity, so the scan
                // above never sees them -- and a quietly diverged economy only
                // surfaces much later as "how can they afford that".
                uint hBank = 2166136261u;
                for (int f = 0; f < 8; f++)
                {
                    if (!TheWaningBorder.Economy.FactionEconomy.TryGetResources(
                            em, (Faction)f, out var bank)) continue;
                    uint bh = 2166136261u;
                    Mix(ref bh, (uint)f);
                    Mix(ref bh, (uint)bank.Supplies);
                    Mix(ref bh, (uint)bank.Iron);
                    Mix(ref bh, (uint)bank.Veilstone);
                    Mix(ref bh, (uint)bank.Veilsteel);
                    total += bh;
                    Mix(ref hBank, bh);

                    // Sect adoption rides on the same bank entity. Twelve
                    // sects x five lever levels decide what a faction's units
                    // and powers DO, and none of it was hashed -- a divergence
                    // here would surface much later as an ability behaving
                    // differently on the two peers.
                    if (TheWaningBorder.Economy.FactionEconomy.TryGetBank(em, (Faction)f, out var bankEntity)
                        && em.HasComponent<TheWaningBorder.Economy.SectAdoptionState>(bankEntity))
                    {
                        var sects = em.GetComponentData<TheWaningBorder.Economy.SectAdoptionState>(bankEntity);
                        Mix(ref hTech, (uint)f);
                        for (int si = 0; si < 12; si++)
                        {
                            var ps = sects.Get(si);
                            Mix(ref hTech, ps.AdoptedAtAge);
                            Mix(ref hTech, ps.PowerLevel);
                            Mix(ref hTech, ps.PassiveLevel);
                            Mix(ref hTech, ps.BuildingLevel);
                            Mix(ref hTech, ps.UnitLevel);
                            Mix(ref hTech, ps.ActivePowerLevel);
                        }
                    }
                }

                result.Total = total;
                result.Pos = hPos; result.Rot = hRot; result.Health = hHealth;
                result.Nav = hNav; result.Combat = hCombat; result.Work = hWork;
                result.Bank = hBank;
                result.Tech = hTech;
                result.Faction0 = perFaction[0]; result.Faction1 = perFaction[1];
                result.Faction2 = perFaction[2]; result.Faction3 = perFaction[3];
                result.Faction4 = perFaction[4]; result.Faction5 = perFaction[5];
                result.Faction6 = perFaction[6]; result.Faction7 = perFaction[7];

                if (detailed)
                {
                    result.Veil = HashVeil(em);
                    result.Cost = HashCostField(em);
                    result.Rng = HashRngStreams(em);
                }
            }

            entities.Dispose();
            ids.Dispose();

            if (snapshots != null) snapshots.Sort(CompareById);
            return result;
        }

        private static int CompareById(EntitySnapshot a, EntitySnapshot b)
            => a.Id.CompareTo(b.Id);

        /// <summary>
        /// The INTENT behind a position, hashed separately from the position.
        ///
        /// This is the column the 2026-08-21 investigation was missing. Units
        /// diverged in position with identical health and identical commands;
        /// whether their destination, flow direction and steering vector
        /// agreed at that moment is the difference between "the integrator is
        /// not deterministic" and "they were told to go somewhere else".
        /// </summary>
        private static void CaptureNavCombatWork(EntityManager em, Entity e, int networkId,
                                                 ref EntitySnapshot snap,
                                                 ref uint hNav, ref uint hCombat, ref uint hWork)
        {
            bool anyNav = false;

            if (em.HasComponent<DesiredDestination>(e))
            {
                var d = em.GetComponentData<DesiredDestination>(e);
                snap.HasDest = d.Has;
                snap.Dx = math.asuint(d.Position.x);
                snap.Dz = math.asuint(d.Position.z);
                Mix(ref hNav, d.Has); Mix(ref hNav, snap.Dx); Mix(ref hNav, snap.Dz);
                anyNav = true;
            }

            if (em.HasComponent<FlowDesiredDir>(e))
            {
                var f = em.GetComponentData<FlowDesiredDir>(e);
                snap.HasFlow = f.HasValue;
                snap.Fx = math.asuint(f.Value.x);
                snap.Fz = math.asuint(f.Value.z);
                Mix(ref hNav, f.HasValue); Mix(ref hNav, snap.Fx); Mix(ref hNav, snap.Fz);
                anyNav = true;
            }

            if (em.HasComponent<SteeringDesiredDir>(e))
            {
                var s = em.GetComponentData<SteeringDesiredDir>(e);
                snap.HasSteer = s.HasValue;
                snap.Sx = math.asuint(s.Value.x);
                snap.Sz = math.asuint(s.Value.z);
                Mix(ref hNav, s.HasValue); Mix(ref hNav, snap.Sx); Mix(ref hNav, snap.Sz);
                anyNav = true;
            }

            if (em.HasComponent<SmoothedDirection>(e))
            {
                var sm = em.GetComponentData<SmoothedDirection>(e);
                snap.SmX = math.asuint(sm.Value.x);
                snap.SmZ = math.asuint(sm.Value.z);
                Mix(ref hNav, snap.SmX); Mix(ref hNav, snap.SmZ);
                anyNav = true;
            }

            if (em.HasComponent<StuckState>(e))
            {
                var st = em.GetComponentData<StuckState>(e);
                snap.Stuck = st.Counter; snap.StuckAttempt = st.LastAttempt;
                Mix(ref hNav, st.Counter); Mix(ref hNav, st.LastAttempt);
                anyNav = true;
            }

            if (em.HasComponent<MoveSpeed>(e))
            {
                snap.Speed = math.asuint(em.GetComponentData<MoveSpeed>(e).Value);
                Mix(ref hNav, snap.Speed);
                anyNav = true;
            }

            if (em.HasComponent<FormationSpeedOverride>(e))
                Mix(ref hNav, math.asuint(em.GetComponentData<FormationSpeedOverride>(e).Value));

            if (em.HasComponent<GuardPoint>(e))
            {
                var g = em.GetComponentData<GuardPoint>(e);
                Mix(ref hNav, g.Has);
                Mix(ref hNav, math.asuint(g.Position.x));
                Mix(ref hNav, math.asuint(g.Position.z));
            }

            if (anyNav) Mix(ref hNav, (uint)networkId);

            // -- Combat -------------------------------------------------
            if (em.HasComponent<Target>(e))
            {
                var t = em.GetComponentData<Target>(e);
                // The target's NETWORK id, never its Entity index: entity
                // indices are an allocator detail and can legitimately differ
                // between peers without anything being wrong.
                snap.TargetId = (t.Value != Entity.Null && em.Exists(t.Value)
                                 && em.HasComponent<NetworkedEntity>(t.Value))
                    ? em.GetComponentData<NetworkedEntity>(t.Value).NetworkId
                    : -1;
                Mix(ref hCombat, (uint)networkId);
                Mix(ref hCombat, (uint)snap.TargetId);
            }

            if (em.HasComponent<AttackCooldown>(e))
            {
                var ac = em.GetComponentData<AttackCooldown>(e);
                snap.AtkTimer = math.asuint(ac.Timer);
                Mix(ref hCombat, snap.AtkTimer);
            }

            // -- Work ---------------------------------------------------
            if (em.HasComponent<UnderConstruction>(e))
            {
                var uc = em.GetComponentData<UnderConstruction>(e);
                snap.WorkKind = 1;
                snap.WorkA = math.asuint(uc.Progress);
                snap.WorkB = math.asuint(uc.Total);
                Mix(ref hWork, (uint)networkId);
                Mix(ref hWork, snap.WorkA); Mix(ref hWork, snap.WorkB);
                Mix(ref hWork, (uint)uc.LastProgressHp);
            }
            else if (em.HasComponent<TrainingState>(e))
            {
                var ts = em.GetComponentData<TrainingState>(e);
                snap.WorkKind = 2;
                snap.WorkA = math.asuint(ts.Remaining);
                snap.WorkB = math.asuint(ts.Total);
                Mix(ref hWork, (uint)networkId);
                Mix(ref hWork, ts.Busy);
                Mix(ref hWork, snap.WorkA); Mix(ref hWork, snap.WorkB);
            }
            else if (em.HasComponent<MinerState>(e))
            {
                var ms = em.GetComponentData<MinerState>(e);
                snap.WorkKind = 3;
                snap.WorkA = math.asuint(ms.GatherTimer);
                snap.WorkB = ms.GatheringResource;
                snap.WorkTarget = (ms.AssignedDeposit != Entity.Null && em.Exists(ms.AssignedDeposit)
                                   && em.HasComponent<NetworkedEntity>(ms.AssignedDeposit))
                    ? em.GetComponentData<NetworkedEntity>(ms.AssignedDeposit).NetworkId
                    : -1;
                Mix(ref hWork, (uint)networkId);
                Mix(ref hWork, snap.WorkA); Mix(ref hWork, snap.WorkB);
                Mix(ref hWork, (uint)snap.WorkTarget);
            }

            if (em.HasComponent<BuildOrder>(e))
            {
                var bo = em.GetComponentData<BuildOrder>(e);
                snap.WorkTarget = (bo.Site != Entity.Null && em.Exists(bo.Site)
                                   && em.HasComponent<NetworkedEntity>(bo.Site))
                    ? em.GetComponentData<NetworkedEntity>(bo.Site).NetworkId
                    : -1;
                Mix(ref hWork, (uint)networkId);
                Mix(ref hWork, (uint)snap.WorkTarget);
            }
        }

        /// <summary>
        /// Research and the two QUEUES.
        ///
        /// TrainingState and ResearchState say what is in progress; neither
        /// says what is queued BEHIND it. A queue that diverges is invisible
        /// until the queue drains, at which point the two peers train or
        /// research different things and the fork looks like it started
        /// minutes after it did.
        /// </summary>
        private static void CaptureTech(EntityManager em, Entity e, int networkId, ref uint hTech)
        {
            if (em.HasComponent<ResearchState>(e))
            {
                var rs = em.GetComponentData<ResearchState>(e);
                Mix(ref hTech, (uint)networkId);
                Mix(ref hTech, rs.Busy);
                Mix(ref hTech, math.asuint(rs.Remaining));
            }

            if (em.HasBuffer<ResearchQueueItem>(e))
            {
                var q = em.GetBuffer<ResearchQueueItem>(e);
                Mix(ref hTech, (uint)networkId);
                Mix(ref hTech, (uint)q.Length);
                for (int i = 0; i < q.Length; i++)
                    Mix(ref hTech, (uint)q[i].TechId.GetHashCode());
            }

            if (em.HasBuffer<TrainQueueItem>(e))
            {
                var q = em.GetBuffer<TrainQueueItem>(e);
                Mix(ref hTech, (uint)networkId);
                Mix(ref hTech, (uint)q.Length);
                for (int i = 0; i < q.Length; i++)
                    Mix(ref hTech, (uint)q[i].UnitId.GetHashCode());
            }
        }

        /// <summary>
        /// The veil saturation grid. It is a shared cellular automaton feeding
        /// passability and unit debuffs, it is thousands of cells wide, and
        /// nothing in the old checksum touched it -- a fork here would have
        /// surfaced only once it pushed a unit somewhere different, ticks or
        /// minutes later and looking like a movement bug.
        /// </summary>
        private static uint HashVeil(EntityManager em)
        {
            var q = _veilQuery.Get(em, VeilTypes);
            if (q.CalculateEntityCount() == 0) return 0u;

            var vf = q.GetSingleton<VeilField>();
            if (vf.Initialised == 0 || !vf.Saturation.IsCreated) return 0u;

            unchecked
            {
                uint h = 2166136261u;
                Mix(ref h, (uint)vf.Generation);
                Mix(ref h, (uint)vf.Width);
                Mix(ref h, (uint)vf.Height);

                for (int i = 0; i < vf.Saturation.Length; i++) { h ^= vf.Saturation[i]; h *= 16777619u; }
                if (vf.Cooldown.IsCreated)
                    for (int i = 0; i < vf.Cooldown.Length; i++) { h ^= vf.Cooldown[i]; h *= 16777619u; }
                return h;
            }
        }

        /// <summary>
        /// The nav cost field -- what every path is computed against. A stamp
        /// landing on one peer and not the other reroutes units without ever
        /// touching a component the old checksum could see. All three
        /// 2026-08-21 forks landed on a tick that stamped or re-stamped this.
        /// </summary>
        private static uint HashCostField(EntityManager em)
        {
            var q = _costQuery.Get(em, CostTypes);
            if (q.CalculateEntityCount() == 0) return 0u;

            var cf = q.GetSingleton<NavCostField>();
            if (!cf.Cost.IsCreated) return 0u;

            unchecked
            {
                uint h = 2166136261u;
                Mix(ref h, (uint)cf.Generation);
                Mix(ref h, (uint)cf.Width);
                Mix(ref h, (uint)cf.Height);
                Mix(ref h, (uint)cf.LayerCount);
                for (int i = 0; i < cf.Cost.Length; i++) { h ^= cf.Cost[i]; h *= 16777619u; }
                if (cf.Flags.IsCreated)
                    for (int i = 0; i < cf.Flags.Length; i++) { h ^= cf.Flags[i]; h *= 16777619u; }
                return h;
            }
        }

        /// <summary>
        /// Every seeded RNG stream in the simulation.
        ///
        /// These are the quietest forks there are. Both peers seed from
        /// SpawnSeed, so the streams start identical and stay identical only
        /// while both consume the same number of values. One extra draw on one
        /// peer -- a rejected spawn position, a branch taken once -- silently
        /// shifts every later roll, and nothing observable changes at the
        /// moment it happens. Hashing the STATE catches it on the tick it
        /// occurs instead of whenever it first produces a visible difference.
        /// </summary>
        private static uint HashRngStreams(EntityManager em)
        {
            var world = em.World;
            if (world == null || !world.IsCreated) return 0u;

            unchecked
            {
                uint h = 2166136261u;

                var blood = world.GetExistingSystemManaged<TheWaningBorder.Systems.Border.BloodCurseSpawnSystem>();
                if (blood != null) Mix(ref h, blood.RngState);

                var veilstone = world.Unmanaged.GetExistingUnmanagedSystem<TheWaningBorder.Systems.Work.VeilstoneMiningSystem>();
                if (veilstone != SystemHandle.Null)
                    Mix(ref h, world.Unmanaged
                        .GetUnsafeSystemRef<TheWaningBorder.Systems.Work.VeilstoneMiningSystem>(veilstone).RngState);

                return h;
            }
        }

        /// <summary>FNV-1a round over a 32-bit word.</summary>
        public static void Mix(ref uint h, uint value)
        {
            unchecked
            {
                h ^= value & 0xFF; h *= 16777619u;
                h ^= (value >> 8) & 0xFF; h *= 16777619u;
                h ^= (value >> 16) & 0xFF; h *= 16777619u;
                h ^= (value >> 24) & 0xFF; h *= 16777619u;
            }
        }
    }
}
