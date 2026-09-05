// WorldCensus.cs
// Periodic world size readout (2026-09-03).
//
// WHY: the 0.0.19 Veilmarch match lost 85% of its frame rate over 13 minutes
// (46.7 -> 6.9 FPS) with ZERO garbage collections on the hitching frames and
// every instrumented system reading flat from the first minute to the last.
// A cost that grows while the per-system costs do not is a cost paid PER
// SOMETHING, and this prints the somethings: entities, archetypes, chunks,
// chunk occupancy and live views, once every interval, so the growth curve is
// in the log next to the frame times instead of being inferred from them.
//
// Archetype and chunk counts matter as much as the entity count. Every
// EntityQuery must be matched against every archetype, and a project that
// adds and removes single tag components entity-by-entity fragments its
// chunks: 4,000 units spread over 900 near-empty chunks iterate far slower
// than the same 4,000 in 40 full ones, with no change in entity count at all.
//
// Presentation/diagnostic only — never read by the sim.

using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    public static class WorldCensus
    {
        /// <summary>Seconds between readouts. Long enough that the census
        /// itself is not part of what it measures.</summary>
        private const float Interval = 15f;

        private static float _next;
        private static int _prevEntities, _prevArchetypes, _prevChunks;

        /// <summary>Take a census if the interval has elapsed. Call once a
        /// frame; it returns immediately on every frame but one in ~900.</summary>
        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + Interval;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            int entities = em.UniversalQuery.CalculateEntityCount();

            var archetypes = new NativeList<EntityArchetype>(256, Allocator.Temp);
            em.GetAllArchetypes(archetypes);

            int chunks = 0, capacity = 0, emptyArchetypes = 0;
            for (int i = 0; i < archetypes.Length; i++)
            {
                var a = archetypes[i];
                int c = a.ChunkCount;
                if (c == 0) { emptyArchetypes++; continue; }
                chunks += c;
                capacity += c * a.ChunkCapacity;
            }

            int archetypeCount = archetypes.Length;
            archetypes.Dispose();

            // Occupancy is the fragmentation readout: entities per chunk slot.
            // A healthy world sits high; a world that churns single components
            // per entity sinks, and every iterating system slows with it.
            float fill = capacity > 0 ? 100f * entities / capacity : 0f;

            int views = Rendering.EntityViewManager.Instance != null
                ? Rendering.EntityViewManager.Instance.ViewCount : -1;

            PerfSpikeLog.Report("CENSUS", 0,
                $"entities={entities}(+{entities - _prevEntities}) "
                + $"archetypes={archetypeCount}(+{archetypeCount - _prevArchetypes}) "
                + $"empty={emptyArchetypes} "
                + $"chunks={chunks}(+{chunks - _prevChunks}) "
                + $"fill={fill:F0}% views={views} "
                + $"sysprobes={EcsSystemProfiler.ProbeCount}",
                0.0);

            // Name the combinations that appeared since the last census.
            // GetAllArchetypes returns them in CREATION order (the store's
            // archetype list is append-only), so everything at or past the
            // previous count is new. An archetype is one exact set of
            // component types, so this prints the component churn by name
            // instead of leaving it as a number that only goes up.
            if (_prevArchetypes > 0 && archetypeCount > _prevArchetypes)
                DescribeNewArchetypes(em, _prevArchetypes);

            _prevEntities = entities;
            _prevArchetypes = archetypeCount;
            _prevChunks = chunks;
        }

        /// <summary>How many of the new archetypes to spell out. Enough to
        /// see the pattern, few enough that a census stays one screen.</summary>
        private const int MaxDescribed = 4;

        private static void DescribeNewArchetypes(EntityManager em, int from)
        {
            var all = new NativeList<EntityArchetype>(64, Allocator.Temp);
            em.GetAllArchetypes(all);

            int described = 0;
            for (int i = from; i < all.Length && described < MaxDescribed; i++)
            {
                var types = all[i].GetComponentTypes(Allocator.Temp);
                var sb = new System.Text.StringBuilder(160);
                for (int t = 0; t < types.Length; t++)
                {
                    // Entity is on every archetype and Simulate is added by
                    // the netcode bootstrap; neither distinguishes anything.
                    string name = types[t].GetManagedType()?.Name;
                    if (string.IsNullOrEmpty(name) || name == "Entity") continue;
                    if (sb.Length > 0) sb.Append('+');
                    sb.Append(name);
                }
                types.Dispose();

                PerfSpikeLog.Report("NEWARCH", 0,
                    $"#{i} chunks={all[i].ChunkCount} {sb}", 0.0);
                described++;
            }
            all.Dispose();
        }
    }
}
