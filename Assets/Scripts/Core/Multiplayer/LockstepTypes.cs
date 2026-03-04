// LockstepTypes.cs
// All lockstep-related types for multiplayer synchronization
// Location: Assets/Scripts/Core/Multiplayer/LockstepTypes.cs

using System;
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
        Heal = 8
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
        /// </summary>
        public string Serialize()
        {
            return $"{(int)Type},{EntityNetworkId},{TargetPosition.x:F2},{TargetPosition.y:F2},{TargetPosition.z:F2},{TargetEntityId},{SecondaryTargetId},{BuildingId ?? ""}";
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

                return new LockstepCommand
                {
                    Type = (LockstepCommandType)int.Parse(parts[0]),
                    EntityNetworkId = int.Parse(parts[1]),
                    TargetPosition = new float3(
                        float.Parse(parts[2]),
                        float.Parse(parts[3]),
                        float.Parse(parts[4])),
                    TargetEntityId = int.Parse(parts[5]),
                    SecondaryTargetId = int.Parse(parts[6]),
                    BuildingId = parts.Length > 7 ? parts[7] : ""
                };
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[LockstepCommand] Deserialize failed: {e.Message}");
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
    /// Helper for generating unique network IDs.
    /// </summary>
    public static class NetworkIdGenerator
    {
        private static int _nextId = 1;
        private static readonly object _lock = new object();

        /// <summary>
        /// Get the next available network ID.
        /// Thread-safe for burst-compiled systems.
        /// </summary>
        public static int GetNextId()
        {
            lock (_lock)
            {
                return _nextId++;
            }
        }

        /// <summary>
        /// Reset the ID counter (call when starting new game).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _nextId = 1;
            }
        }

        /// <summary>
        /// Synchronize the counter to a specific value (for clients).
        /// </summary>
        public static void SyncTo(int value)
        {
            lock (_lock)
            {
                _nextId = Math.Max(_nextId, value + 1);
            }
        }
    }
}