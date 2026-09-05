// AIQueryCache.cs
// Cached EntityQuery shapes for the AI helpers that cannot hold their own.
//
// EntityManager.CreateEntityQuery permanently registers a NEW query with the
// world on every call (see Core/CachedEntityQuery.cs). The AI tree called it
// 104 times from per-tick code, so every think tick leaked queries into the
// world registry, and a bloated registry slows every later query AND every
// structural change - the documented "skirmish starts smooth, sinks to 15 FPS"
// curve. Most call sites now hold a static CachedEntityQuery of their own.
//
// Two kinds could not:
//   - a GENERIC helper (FindFactionBuilding<T>, CountFactionBuildingsByTag<T>)
//     needs one query PER T, so the cache has to be generic too. A static
//     field inside a generic class is per-constructed-type, which is exactly
//     the right lifetime.
//   - a helper whose component type is decided at RUNTIME (the extractor's
//     required node type) needs one query per value, so it is keyed.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.AI
{
    /// <summary>Shared cached query shapes for the generic and runtime-typed
    /// AI helpers.</summary>
    public static class AIQueryCache
    {
        // One set of statics per constructed T — the point of the generic class.
        static class Shapes<T> where T : unmanaged, IComponentData
        {
            public static readonly ComponentType[] TagFaction =
                { ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<FactionTag>() };
            public static readonly ComponentType[] TagXf =
                { ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<LocalTransform>() };
            public static readonly ComponentType[] TagFactionXf =
            {
                ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
            };
            public static readonly ComponentType[] TagFactionUnderConstruction =
            {
                ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnderConstruction>(),
            };
            public static readonly ComponentType[] TagFactionResearchQueue =
            {
                ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<ResearchQueueItem>(),
            };

            public static CachedEntityQuery QTagFaction;
            public static CachedEntityQuery QTagXf;
            public static CachedEntityQuery QTagFactionXf;
            public static CachedEntityQuery QTagFactionUnderConstruction;
            public static CachedEntityQuery QTagFactionResearchQueue;
        }

        /// <summary>Entities tagged T that carry a faction.</summary>
        public static EntityQuery TagFaction<T>(EntityManager em) where T : unmanaged, IComponentData
            => Shapes<T>.QTagFaction.Get(em, Shapes<T>.TagFaction);

        /// <summary>Entities tagged T that sit somewhere.</summary>
        public static EntityQuery TagXf<T>(EntityManager em) where T : unmanaged, IComponentData
            => Shapes<T>.QTagXf.Get(em, Shapes<T>.TagXf);

        /// <summary>Entities tagged T with a faction and a position.</summary>
        public static EntityQuery TagFactionXf<T>(EntityManager em) where T : unmanaged, IComponentData
            => Shapes<T>.QTagFactionXf.Get(em, Shapes<T>.TagFactionXf);

        /// <summary>Tagged, owned, and still under construction.</summary>
        public static EntityQuery TagFactionUnderConstruction<T>(EntityManager em)
            where T : unmanaged, IComponentData
            => Shapes<T>.QTagFactionUnderConstruction.Get(
                   em, Shapes<T>.TagFactionUnderConstruction);

        /// <summary>Tagged, owned, and able to take a research order.</summary>
        public static EntityQuery TagFactionResearchQueue<T>(EntityManager em)
            where T : unmanaged, IComponentData
            => Shapes<T>.QTagFactionResearchQueue.Get(
                   em, Shapes<T>.TagFactionResearchQueue);

        // ── runtime-typed ──────────────────────────────────────────────────

        sealed class NodeShape
        {
            public ComponentType[] Types;
            public CachedEntityQuery Query;
        }

        static readonly Dictionary<ComponentType, NodeShape> _byNodeType = new();

        /// <summary>
        /// Positions of every entity carrying <paramref name="node"/>.
        ///
        /// Keyed rather than generic: the extractor asks for whichever node
        /// type the building it wants to place needs, and that is a value, not
        /// a type argument. The key set is the handful of resource-node tags,
        /// so the dictionary stays tiny; a class holds each entry so the types
        /// array is built once rather than per call.
        /// </summary>
        public static EntityQuery NodeAt(EntityManager em, ComponentType node)
        {
            if (!_byNodeType.TryGetValue(node, out var shape))
            {
                shape = new NodeShape
                {
                    Types = new[] { node, ComponentType.ReadOnly<LocalTransform>() },
                };
                _byNodeType[node] = shape;
            }
            return shape.Query.Get(em, shape.Types);
        }
    }
}
