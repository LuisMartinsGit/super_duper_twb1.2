// File: Assets/Scripts/Entities/Sporeling.cs
// The Sporeling — the small destructible crystal growth anchoring an Age 0
// blight pocket (§2.5b). While alive it feeds its haze patch through the
// veil CA (VeilFieldSystem folds live sporelings into the feeder set); it
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
    public static class Sporeling
    {
        // Reuses the veilstone gem-cluster visual (the outcropping prefab)
        // at anchor scale — the pocket's heart looks like the material it
        // eventually pays out.
        private const int PresentationID = 301;
        // Size ladder on the shared gem prefab (x6 visual base): resource
        // nodes ~2-3 world, Sporeling ~4.8, well landmark 8 — the anchor
        // reads clearly above any deposit, clearly below a well.
        private const float VisualScale = 0.8f;
        private const float SelectRadius = 1.5f;

        public static Entity Create(EntityManager em, float3 position)
        {
            var e = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(SporelingTag),
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
                Value = (int)SporelingHealth,
                Max = (int)SporelingHealth
            });
            em.SetComponentData(e, new Radius { Value = SelectRadius });

            em.AddComponentData(e, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });
            return e;
        }
    }
}
