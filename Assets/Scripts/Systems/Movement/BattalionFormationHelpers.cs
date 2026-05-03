// BattalionFormationHelpers.cs
// Greedy slot reassignment logic extracted from BattalionSyncSystem.
// Location: Assets/Scripts/Systems/Movement/BattalionFormationHelpers.cs
//
// Fix #218: the 795-line BattalionSyncSystem held formation, combat, and
// movement logic in one OnUpdate. The formation-specific slot reassignment
// is broken out here so the main system file can stay under 500 lines and
// each concern is testable in isolation.
//
// This is NOT a separate ISystem — reassignment runs synchronously as a
// helper call from BattalionSyncSystem.OnUpdate, using the system's
// persistent scratch arrays. Splitting into a second ISystem would force
// sharing state via components and introduce frame-boundary latency.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Movement
{
    internal static class BattalionFormationHelpers
    {
        /// <summary>
        /// Greedy front-to-back nearest-slot assignment. For each slot,
        /// finds the closest alive unassigned member and writes the new
        /// Column/Row onto that member's BattalionMemberData.
        ///
        /// The caller is responsible for:
        ///   - Deciding whether reassignment is needed (angle change,
        ///     NeedsReassignment flag, uninitialized LastAssignmentRot).
        ///   - Storing the new LastAssignmentRot on the leader after the call.
        /// </summary>
        /// <param name="em">Entity manager used to write member data.</param>
        /// <param name="leaderPos">Leader world position (formation origin).</param>
        /// <param name="formationRot">Rotation applied to slot offsets.</param>
        /// <param name="cols">Formation column count.</param>
        /// <param name="rows">Formation row count.</param>
        /// <param name="spacing">World-space spacing between slots.</param>
        /// <param name="memberCount">Number of entries in <paramref name="members"/> to consider.</param>
        /// <param name="members">Cached member entities (size >= memberCount).</param>
        /// <param name="memberPos">Cached member world positions (size >= memberCount).</param>
        /// <param name="memberAlive">Cached alive flags (size >= memberCount).</param>
        /// <param name="slotWorldPositions">Scratch array for computed slot positions (size >= cols*rows).</param>
        /// <param name="slotAssignment">Scratch array for slot->member index mapping (size >= cols*rows).</param>
        /// <param name="memberUsed">Scratch array for per-member used flags (size >= memberCount).</param>
        public static void ReassignSlots(
            EntityManager em,
            float3 leaderPos,
            quaternion formationRot,
            int cols, int rows, float spacing,
            int memberCount,
            NativeArray<Entity> members,
            NativeArray<float3> memberPos,
            NativeArray<bool> memberAlive,
            NativeArray<float3> slotWorldPositions,
            NativeArray<int> slotAssignment,
            NativeArray<bool> memberUsed)
        {
            int slotCount = cols * rows;

            // Compute slot world positions for assignment
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int idx = row * cols + col;
                    float3 localOffset = BattalionFormation.ComputeSlotOffset(col, row, cols, rows, spacing);
                    slotWorldPositions[idx] = leaderPos + math.mul(formationRot, localOffset);
                }
            }

            for (int s = 0; s < slotCount; s++)
                slotAssignment[s] = -1;
            for (int m = 0; m < memberCount; m++)
                memberUsed[m] = false;

            // Greedy front-to-back: for each slot, find closest alive unassigned member
            for (int s = 0; s < slotCount; s++)
            {
                float3 slotPos = slotWorldPositions[s];
                float bestDist = float.MaxValue;
                int bestMember = -1;

                for (int m = 0; m < memberCount; m++)
                {
                    if (!memberAlive[m] || memberUsed[m]) continue;

                    float3 diff = slotPos - memberPos[m];
                    diff.y = 0f;
                    float d = math.lengthsq(diff);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestMember = m;
                    }
                }

                if (bestMember >= 0)
                {
                    slotAssignment[s] = bestMember;
                    memberUsed[bestMember] = true;
                }
            }

            // Write new assignments onto member components
            for (int s = 0; s < slotCount; s++)
            {
                int mi = slotAssignment[s];
                if (mi < 0) continue;

                int newCol = s % cols;
                int newRow = s / cols;

                var md = em.GetComponentData<BattalionMemberData>(members[mi]);
                if (md.Column != newCol || md.Row != newRow)
                {
                    md.Column = newCol;
                    md.Row = newRow;
                    em.SetComponentData(members[mi], md);
                }
            }
        }
    }
}
