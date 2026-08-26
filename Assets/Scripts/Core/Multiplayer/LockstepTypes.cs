// LockstepTypes.cs
// All lockstep-related types for multiplayer synchronization

using System;
using System.Globalization;
using System.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Core.Multiplayer
{
    // ═══════════════════════════════════════════════════════════════════════════
    // COMMAND TYPES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Types of commands that can be sent through lockstep.
    /// </summary>
    public enum LockstepCommandType : byte
    {
        None = 0,
        Move = 1,
        Attack = 2,
        Stop = 3,
        Build = 4,
        Train = 5,
        Gather = 6,
        SetRally = 7,
        Heal = 8,
        AttackMove = 9,
        Repair = 10,
        Convert = 11,
        Patrol = 12,
        HoldPosition = 13,
        PlaceBuilding = 14,
        Ability = 15,
        Purify = 16,             // CommandRouter.IssuePurify  (scholar + node)
        ConvertNode = 17,        // CommandRouter.IssueConvertNode (acolyte + node)
        EquipmentUpgrade = 18,   // CommandRouter.IssueEquipmentUpgrade (faction + class + tier)
        GodPower = 19,           // CommandRouter.IssueGodPower (faction + targetPosition)
        CancelTrain = 20,        // CommandRouter.IssueCancelTrain (building + slotIndex in TargetEntityId)
        ConvertHut = 21,         // CommandRouter.IssueConvertHut (hut + HutConversionTarget byte in TargetEntityId)
        ConvertSegmentToGate = 22, // CommandRouter.IssueConvertSegmentToGate (segment + focus-instance network id in TargetEntityId)
        GatherVeil = 23,         // CommandRouter.IssueGatherVeil (miner + dig site in TargetPosition)
        LayeredMove = 24,        // CommandRouter.IssueLayeredMove (unit + dest + target layer byte in TargetEntityId)
        Research = 25,           // CommandRouter.IssueResearch (building + tech id in BuildingId)
        BuildingUpgrade = 26,    // CommandRouter.IssueBuildingUpgrade (building level-up; target level recomputed per peer)
        AgeUp = 27,              // CommandRouter.IssueAgeUp (hall + culture byte in TargetEntityId)
        TempleUpgrade = 28,      // CommandRouter.IssueTempleUpgrade (temple; level/duration recomputed per peer)
        SectAdopt = 29,          // CommandRouter.IssueSectAdoption (temple + sect id in BuildingId + slot in TargetEntityId + build time in TargetPosition.x)

        // ── Added 2026-08-15 (docs/Multiplayer_LAN_Readiness.md) ─────────
        // Every one of these was a UI button writing ECS directly, so the
        // effect existed on the clicking peer alone. They follow the
        // established shape: the ISSUER validates and pays, every peer applies
        // the mutation.
        SectPower = 30,          // CommandRouter.IssueSectPower (faction in EntityNetworkId, sect id in BuildingId, tier in TargetEntityId, target in TargetPosition)
        ReliquaryAbility = 31,   // CommandRouter.IssueReliquaryAbility (reliquary + ability index in TargetEntityId + target in TargetPosition)
        WallUpgrade = 32,        // CommandRouter.IssueWallUpgrade (wall instance + upgrade type in TargetEntityId)
        KeepWing = 33,           // CommandRouter.IssueKeepWing (keep + KeepWingType byte in TargetEntityId)
        UnitPromote = 34,        // CommandRouter.IssueUnitPromote (unit)
        QueueWaypoint = 35,      // CommandRouter.IssueQueuedWaypoint (unit + QueuedCommandType byte in TargetEntityId + point in TargetPosition)

        // ── Added 2026-08-16 (docs/Multiplayer_Desync_Sweep_2026-08-16.md) ──
        // Alanthor wall placement was created OUTSIDE the command stream on
        // both the player and AI paths — worse than a missing feature, the
        // off-tick entity creation consumed NetworkId slots on one peer only
        // and shifted every later id assigned that tick.
        PlaceWallHub = 36,       // CommandRouter.IssuePlaceWallHub (faction in EntityNetworkId, autoBuild flag in TargetEntityId, position in TargetPosition)
        WallExtend = 37,         // CommandRouter.IssueWallExtend (source hub; snap hub network id in TargetEntityId or 0 for a new hub at TargetPosition; faction in SecondaryTargetId)
        Corrupt = 38,            // CommandRouter.IssueCorrupt (corruptor + node in TargetEntityId — the Feraldis verb; mirrors Purify)
        SectGlowAlloc = 39,      // CommandRouter.IssueSectGlowAlloc (faction in EntityNetworkId, sect id in BuildingId, allocate flag in TargetEntityId — halves that sect's power cooldown, so peers must agree)
        BazaarPack = 40,         // CommandRouter.IssueBazaarPack (bazaar + pack flag in TargetEntityId; BazaarPackSystem destroys the building and spawns the wagon, so it must run on every peer)
        VaultTransfer = 41,      // CommandRouter.IssueVaultTransfer (vault; resource type + deposit flag packed in TargetEntityId, amount in SecondaryTargetId — bank + VaultStorage move on every peer)
    }

    /// <summary>
    /// A command to be executed at a specific tick.
    /// Serializable for network transmission.
    /// </summary>
    [Serializable]
    public class LockstepCommand
    {
        /// <summary>Type of command</summary>
        public LockstepCommandType Type;
        
        /// <summary>Player who issued the command</summary>
        public int PlayerIndex;
        
        /// <summary>Tick when command should execute</summary>
        public int Tick;
        
        /// <summary>Sequence number for ordering</summary>
        public int CommandIndex;
        
        /// <summary>Network ID of the entity performing the action</summary>
        public int EntityNetworkId;
        
        /// <summary>Target position for move/build/rally commands</summary>
        public float3 TargetPosition;
        
        /// <summary>Network ID of the target entity (for attack/heal/gather)</summary>
        public int TargetEntityId;
        
        /// <summary>Network ID of secondary target (e.g., deposit for gather)</summary>
        public int SecondaryTargetId;
        
        /// <summary>Building type ID for build commands</summary>
        public string BuildingId;

        // ═══════════════════════════════════════════════════════════════════════
        // SERIALIZATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Serialize command to string for network transmission.
        /// Format: Index,Type,EntityId,PosX,PosY,PosZ,TargetId,SecondaryId,BuildingId
        ///
        /// Floats use the round-trip ("R") format specifier to preserve full
        /// IEEE 754 precision. The previous "F2" format truncated positions to
        /// two decimal places, causing building placements to desync between
        /// peers whose source float values differed in the third+ decimal.
        ///
        /// CommandIndex leads the payload because EXECUTION ORDER DEPENDS ON IT.
        /// It used not to be sent at all: every received command arrived with
        /// CommandIndex 0, so LockstepManager's "sort by (PlayerIndex,
        /// CommandIndex)" had nothing to order a single player's commands by,
        /// and fell back on the order the datagrams happened to be parsed in.
        /// Two commands issued in one tick — select-all then attack, or two
        /// buildings placed on the same frame — could therefore execute in
        /// opposite orders on the two peers. Chunked datagrams make that
        /// certain rather than merely possible.
        /// </summary>
        public string Serialize()
        {
            // Use InvariantCulture to ensure '.' decimal separator on all locales
            var c = CultureInfo.InvariantCulture;
            return string.Format(c, "{0},{1},{2},{3:R},{4:R},{5:R},{6},{7},{8}",
                CommandIndex, (int)Type, EntityNetworkId,
                TargetPosition.x, TargetPosition.y, TargetPosition.z,
                TargetEntityId, SecondaryTargetId, BuildingId ?? "");
        }

        /// <summary>
        /// Deserialize command from network string.
        /// </summary>
        public static LockstepCommand Deserialize(string data)
        {
            try
            {
                string[] parts = data.Split(',');
                if (parts.Length < 8) return null;

                // Use InvariantCulture to parse '.' decimal separator on all locales
                var c = CultureInfo.InvariantCulture;
                return new LockstepCommand
                {
                    CommandIndex = int.Parse(parts[0], c),
                    Type = (LockstepCommandType)int.Parse(parts[1], c),
                    EntityNetworkId = int.Parse(parts[2], c),
                    TargetPosition = new float3(
                        float.Parse(parts[3], c),
                        float.Parse(parts[4], c),
                        float.Parse(parts[5], c)),
                    TargetEntityId = int.Parse(parts[6], c),
                    SecondaryTargetId = int.Parse(parts[7], c),
                    BuildingId = parts.Length > 8 ? parts[8] : ""
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PLAYER INFO TYPES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Information about a remote player for lockstep connections.
    /// Used during lobby-to-game transition.
    /// </summary>
    public class RemotePlayerInfo
    {
        public string IP;
        public int Port;
        public Faction Faction;
        public string PlayerName;
        /// <summary>Lockstep player index = LOBBY SLOT INDEX. Every peer
        /// derives its own index from its slot, so the host must register
        /// remotes under the same number — a sequential 1..N assignment
        /// diverges as soon as an AI slot sits between two humans (host
        /// waits for P1 ticks while the client broadcasts as P2, and the
        /// simulation never advances past the input delay).</summary>
        public int PlayerIndex;
    }

    /// <summary>
    /// Runtime data for a connected remote player.
    /// Maintained by LockstepManager during gameplay.
    /// </summary>
    public class RemotePlayer
    {
        public int PlayerIndex;
        public Faction Faction;
        public IPEndPoint EndPoint;
        public int LastConfirmedTick;
        public int Latency; // ms
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ECS COMPONENTS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ECS component that gives an entity a network-synchronized ID.
    /// Required for lockstep command routing.
    /// </summary>
    public struct NetworkedEntity : IComponentData
    {
        /// <summary>Unique network ID (positive, assigned at spawn)</summary>
        public int NetworkId;
        
        /// <summary>Tick when entity was created (for sync validation)</summary>
        public int SpawnTick;
    }

    /// <summary>
    /// Helper for generating unique, lockstep-deterministic network IDs.
    ///
    /// Determinism model:
    ///   - Pre-lockstep (bootstrap) spawns use a sequential counter in the
    ///     reserved range [1 .. BOOTSTRAP_RESERVE-1]. This range is for
    ///     entities that exist before the first tick fires (initial player
    ///     bases, iron deposits, veilstone nodes, etc.). Both peers MUST run
    ///     bootstrap in the same order — this is a pre-condition.
    ///   - Each lockstep tick gets a reserved slot range of SLOTS_PER_TICK
    ///     IDs, starting at BOOTSTRAP_RESERVE + tick * SLOTS_PER_TICK. Any
    ///     entity spawned during ProcessTick(N) on either peer falls inside
    ///     that tick's slot range.
    ///   - Because each tick's slot range is disjoint, a minor difference in
    ///     spawn order inside the same tick will cause a hard checksum desync
    ///     (via LockstepManager.ComputeGameStateChecksum) rather than silent
    ///     ID drift that persists for the rest of the match.
    ///
    /// Call sites (LockstepManager):
    ///   - Reset() at game start
    ///   - BeginTick(tick) at the top of each ProcessTick
    ///
    /// Call sites (bootstrap / factories):
    ///   - GetNextId() for entity spawns — works in both modes
    ///
    /// THREAD SAFETY: main-thread only. The previous lock-based implementation
    /// did NOT provide lockstep determinism (order still depends on scheduling)
    /// and gave a false sense of safety. Removed deliberately — if this method
    /// is ever called from a worker thread, we want the race to surface.
    /// </summary>
    public static class NetworkIdGenerator
    {
        /// <summary>ID range reserved for pre-lockstep bootstrap spawns.</summary>
        public const int BOOTSTRAP_RESERVE = 1_000_000;

        /// <summary>Max entities that can be spawned in a single tick without ID collision.</summary>
        public const int SLOTS_PER_TICK = 10_000;

        private static int _bootstrapNextId = 1;
        private static int _currentTickBase = -1; // -1 = bootstrap mode (pre-lockstep)
        private static int _nextIdInTick;

        /// <summary>
        /// The tick the ids handed out right now belong to: 0 during
        /// bootstrap, the executing lockstep tick after BeginTick. Factories
        /// stamp it into NetworkedEntity.SpawnTick so the desync dump's
        /// spawn column says when an entity appeared — it printed 0 for
        /// every entity in the desync #6 dumps, leaving the spawn tick to be
        /// reverse-engineered from the id's slot range.
        /// </summary>
        public static int CurrentTick { get; private set; }

        /// <summary>
        /// Get the next available network ID.
        /// Must be called on the main thread. Returns bootstrap-range IDs
        /// before the first BeginTick() call, and tick-aligned IDs after.
        /// </summary>
        public static int GetNextId()
        {
            if (_currentTickBase < 0)
            {
                // Pre-lockstep bootstrap mode
                if (_bootstrapNextId >= BOOTSTRAP_RESERVE)
                {
                    UnityEngine.Debug.LogError(
                        $"[NetworkIdGenerator] Bootstrap ID range exhausted (>= {BOOTSTRAP_RESERVE}); " +
                        "further IDs collide with tick-aligned IDs and will desync multiplayer.");
                }
                return _bootstrapNextId++;
            }

            // Tick-aligned mode
            int id = _currentTickBase + _nextIdInTick;
            _nextIdInTick++;
            if (_nextIdInTick >= SLOTS_PER_TICK)
            {
                UnityEngine.Debug.LogError(
                    $"[NetworkIdGenerator] More than {SLOTS_PER_TICK} spawns in one tick; " +
                    "IDs now collide with the next tick's range and will desync multiplayer.");
            }
            return id;
        }

        /// <summary>
        /// Begin a new lockstep tick. Called by LockstepManager.ProcessTick.
        /// Subsequent GetNextId() calls will return IDs in the tick's reserved slot range.
        /// </summary>
        public static void BeginTick(int tick)
        {
            if (tick < 0)
            {
                tick = 0;
            }
            _currentTickBase = BOOTSTRAP_RESERVE + tick * SLOTS_PER_TICK;
            _nextIdInTick = 0;
            CurrentTick = tick;
        }

        /// <summary>
        /// Reset the ID counters to the initial bootstrap state.
        /// Call when starting a new game — BOTH peers must call this at
        /// the same logical moment to stay in sync.
        /// </summary>
        public static void Reset()
        {
            _bootstrapNextId = 1;
            _currentTickBase = -1;
            _nextIdInTick = 0;
            CurrentTick = 0;
        }

        /// <summary>
        /// Defensive sync: bump counters so the next generated ID exceeds `value`.
        /// Kept as a safety net for legacy flows, but new code should rely on the
        /// tick-aligned determinism model rather than mid-game re-syncing.
        /// </summary>
        public static void SyncTo(int value)
        {
            if (_currentTickBase < 0)
            {
                if (_bootstrapNextId <= value) _bootstrapNextId = value + 1;
            }
            else
            {
                int nextAbsolute = _currentTickBase + _nextIdInTick;
                if (nextAbsolute <= value)
                {
                    // Only advance within the current tick's slot range
                    _nextIdInTick = Math.Max(_nextIdInTick, value - _currentTickBase + 1);
                }
            }
        }
    }
}