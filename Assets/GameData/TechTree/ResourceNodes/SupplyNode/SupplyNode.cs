// SupplyNode.cs
// The supply node entity. docs/Design/Regions.md §4.
//
// Deliberately the thinnest node in the game: a tag, a transform, a footprint.
// It carries no amount and no depletion state because it is not a resource —
// it is a PLACE. A Gatherer's Hut may only be raised on one, and the hut is
// what pays (50/min of supplies into its territory).
//
// It is NOT registered in ResourceNodeQuery.IsGatherable: nothing is ever sent
// to gather from it, and answering true there would silently enrol it in the
// training rally hand-off, the click router and the rally overlay.
//
// No ObstacleTag either, unlike iron / veilstone / veilsteel. Those block their
// cell so units route around them; a supply node is ground you are meant to
// BUILD ON, so blocking it would make the one thing it exists for impossible.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    public static class SupplyNode
    {
        /// <summary>Presentation id — must match PresentationSpawnSystem's
        /// dispatch table. 404 was the one gap in the 400-block.</summary>
        public const int PresentationID = 404;

        /// <summary>
        /// Footprint radius. A Gatherer's Hut is 2x2 build cells (4 m), and it
        /// is placed ON the node, so the node has to read as a piece of ground
        /// you put a building on rather than as a prop you build beside it.
        /// </summary>
        public const float NodeRadius = 3f;

        public static Entity Create(EntityManager em, float3 position)
        {
            position = BuildGrid.SnapToCellCentre(position);

            var entity = em.CreateEntity(
                typeof(SupplyNodeTag),
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            em.SetComponentData(entity, new Radius { Value = NodeRadius });
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }
    }
}
