// LockstepTypes.cs
// All lockstep-related types for multiplayer synchronization
// Location: Assets/Scripts/Core/Multiplayer/LockstepTypes.cs

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
        Ability = 15
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
        /// Format: Type,EntityId,PosX,PosY,PosZ,TargetId,SecondaryId,BuildingId
        ///
        /// Floats use the round-trip ("R") format specifier to preserve full
        /// IEEE 754 precision. The previous "F2" format truncated positions to
        /// two decimal places, causing building placements to desync between
        /// peers whose source float values differed in the third+ decimal.
        /// </summary>
        public string Serialize()
        {
            // Use InvariantCulture to ensure '.' decimal separator on all locales
            var c = CultureInfo.InvariantCulture;
            return string.Format(c, "{0},{1},{2:R},{3:R},{4:R},{5},{6},{7}",
                (int)Type, EntityNetworkId, TargetPosition.x, TargetPosition.y, TargetPosition.z,
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
                if (parts.Length < 7) return null;

                // Use InvariantCulture to parse '.' decimal separator on all locales
                var c = CultureInfo.InvariantCulture;
                return new LockstepCommand
                {
                    Type = (LockstepCommandType)int.Parse(parts[0], c),
                    EntityNetworkId = int.Parse(parts[1], c),
                    TargetPosition = new float3(
                        float.Parse(parts[2], c),
                        float.Parse(parts[3], c),
                        float.Parse(parts[4], c)),
                    TargetEntityId = int.Parse(parts[5], c),
                    SecondaryTargetId = int.Parse(parts[6], c),
                    BuildingId = parts.Length > 7 ? parts[7] : ""
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
    ///     bases, iron deposits, crystal nodes, etc.). Both peers MUST run
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
                }
                return _bootstrapNextId++;
            }

            // Tick-aligned mode
            int id = _currentTickBase + _nextIdInTick;
            _nextIdInTick++;
            if (_nextIdInTick >= SLOTS_PER_TICK)
            {
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