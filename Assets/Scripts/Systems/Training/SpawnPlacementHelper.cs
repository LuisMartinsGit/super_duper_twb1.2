// SpawnPlacementHelper.cs
// Helper class for finding empty spawn positions
// Location: Assets/Scripts/Systems/Training/SpawnPlacementHelper.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

namespace TheWaningBorder.Systems.Training
{
    /// <summary>
    /// Helper class for finding valid spawn positions that avoid overlap with existing entities.
    /// Used by TrainingSystem when spawning newly trained units.
    /// </summary>
    public static class SpawnPlacementHelper
    {
        /// <summary>
        /// Find an empty position near the desired spawn point.
        /// Searches in a spiral pattern to find a position without overlapping entities.
        /// </summary>
        /// <param name="desiredPosition">The ideal spawn position</param>
        /// <param name="unitRadius">The radius of the unit to spawn</param>
        /// <param name="em">EntityManager for querying existing entities</param>
        /// <param name="maxAttempts">Maximum positions to try before giving up</param>
        /// <returns>A valid spawn position, or the desired position if no better option found</returns>
        public static float3 FindEmptyPosition(float3 desiredPosition, float unitRadius, 
            EntityManager em, int maxAttempts = 16)
        {
            // First, check if the desired position is already clear
            if (IsPositionClear(desiredPosition, unitRadius, em))
                return desiredPosition;

            // Search in expanding rings
            float ringSpacing = unitRadius * 2.5f;
            int ringIndex = 1;
            int attempt = 0;
            
            while (attempt < maxAttempts)
            {
                float ringRadius = ringIndex * ringSpacing;
                int pointsOnRing = math.max(6, ringIndex * 6);  // More points on larger rings
                
                for (int i = 0; i < pointsOnRing && attempt < maxAttempts; i++)
                {
                    float angle = (i / (float)pointsOnRing) * math.PI * 2;
                    float3 testPos = desiredPosition + new float3(
                        math.cos(angle) * ringRadius,
                        0,
                        math.sin(angle) * ringRadius
                    );
                    
                    if (IsPositionClear(testPos, unitRadius, em))
                        return testPos;
                    
                    attempt++;
                }
                
                ringIndex++;
            }

            // Fallback: return original position with small random offset
            return desiredPosition + new float3(
                Unity.Mathematics.Random.CreateFromIndex((uint)attempt).NextFloat(-1f, 1f),
                0,
                Unity.Mathematics.Random.CreateFromIndex((uint)(attempt + 1)).NextFloat(-1f, 1f)
            );
        }

        /// <summary>
        /// Check if a position is clear of other entities.
        /// </summary>
        private static bool IsPositionClear(float3 position, float radius, EntityManager em)
        {
            float checkRadius = radius * 2f;  // Account for other unit radii
            float checkRadiusSq = checkRadius * checkRadius;

            // Query all entities with positions
            var query = em.CreateEntityQuery(typeof(LocalTransform), typeof(Radius));
            var entities = query.ToEntityArray(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var radii = query.ToComponentDataArray<Radius>(Allocator.Temp);

            bool isClear = true;
            
            for (int i = 0; i < entities.Length; i++)
            {
                float3 otherPos = transforms[i].Position;
                float otherRadius = radii[i].Value;
                
                float distSq = math.distancesq(position, otherPos);
                float minDist = radius + otherRadius;
                
                if (distSq < minDist * minDist)
                {
                    isClear = false;
                    break;
                }
            }

            entities.Dispose();
            transforms.Dispose();
            radii.Dispose();

            return isClear;
        }

        /// <summary>
        /// Find an empty position for a group of units (e.g., formation spawn).
        /// Returns an array of positions for the specified count.
        /// </summary>
        public static NativeArray<float3> FindFormationPositions(float3 center, float unitRadius, 
            int count, EntityManager em, Allocator allocator = Allocator.Temp)
        {
            var positions = new NativeArray<float3>(count, allocator);
            
            int cols = (int)math.ceil(math.sqrt(count));
            float spacing = unitRadius * 2.5f;
            
            float startX = center.x - ((cols - 1) * spacing * 0.5f);
            float startZ = center.z - (((count / cols) - 1) * spacing * 0.5f);

            for (int i = 0; i < count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                
                float3 desiredPos = new float3(
                    startX + col * spacing,
                    center.y,
                    startZ + row * spacing
                );
                
                positions[i] = FindEmptyPosition(desiredPos, unitRadius, em, 8);
            }

            return positions;
        }
    }
}