// BootstrapQueries.cs
// Entity queries more than one bootstrap step shares.

using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// One cached query each (Core/CachedEntityQuery.cs — CreateEntityQuery
    /// registers a new query with the world on every call), instead of the
    /// same declaration copied into every file that needs it.
    /// </summary>
    internal static class BootstrapQueries
    {
        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        /// <summary>Every capital (HallTag) with its owner and transform.</summary>
        public static EntityQuery Halls(EntityManager em)
            => QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);
    }
}
