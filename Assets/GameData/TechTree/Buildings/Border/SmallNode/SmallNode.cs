// File: Assets/GameData/TechTree/Buildings/Border/SmallNode/SmallNode.cs
// The curse's small node (formerly "Sporeling") — the small destructible
// crystal growth anchoring an Age 0 blight pocket (§2.5b), also raised when
// a corrupted veilstone crop depletes (VeilstoneMiningSystem). While alive it feeds its haze patch through the
// veil CA (VeilFieldSystem folds live small nodes into the feeder set); it
// starves under hearth/ward/influence suppression (BlightPocketSystem) and
// dies to ordinary attack damage. Death — any cause — collapses the pocket:
// field break + residue veilstone payout.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Entities
{
    public static class SmallNode
    {
        // Reuses the veilstone gem-cluster visual (the outcropping prefab)
        // at anchor scale — the pocket's heart looks like the material it
        // eventually pays out.
        private const int PresentationID = 301;
        // Build-grid rework: the anchor occupies one 2 m cell like every other
        // node, so it is scaled to fill exactly that cell on the shared gem
        // prefab (x6 visual base) rather than sitting on the old size ladder
        // at ~4.8 world units — more than twice the ground it blocks.
        // docs/Design/Build_Grid.md
        private const float VisualScale =
            BuildGrid.CellSize / PresentationSpawnSystem.VeilstoneOutcroppingVisualBaseScale;

        public static Entity Create(EntityManager em, float3 position)
        {
            // One pocket anchor, one build cell, snapped to its centre.
            // docs/Design/Build_Grid.md
            position = BuildGrid.SnapToCellCentre(position);

            var e = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(SmallNodeTag),
                // Single-cell footprint. Without BuildingSize the anchor fell
                // into the DEFAULT 3x3 building stamp and blocked a metre of
                // ground on every side that it does not own.
                typeof(BuildingSize),
                // BuildingTag + FactionTag(Border) + Health make it a normal
                // attackable hostile structure to targeting/combat/UI. It is
                // explicitly excluded from crumble (its own pocket deepens
                // past DeepThreshold by design) and Border never passes the
                // age-up gate, so it grants no influence.
                typeof(BuildingTag),
                typeof(FactionTag),
                typeof(Health),
                typeof(Radius));

            em.SetComponentData(e, new PresentationId { Id = PresentationID });
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, VisualScale));
            em.SetComponentData(e, new FactionTag { Value = Faction.Border });
            em.SetComponentData(e, new Health
            {
                Value = (int)SmallNodeHealth,
                Max = (int)SmallNodeHealth
            });
            em.SetComponentData(e, new Radius { Value = BuildGrid.HalfCell });
            em.SetComponentData(e, new BuildingSize
            {
                Width = BuildingSizeConfig.SingleCellSize.x,
                Height = BuildingSizeConfig.SingleCellSize.y
            });

            em.AddComponentData(e, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });
            return e;
        }
    }
}
