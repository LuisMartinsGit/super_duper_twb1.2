// File: Assets/Scripts/Entities/Buildings/GlowPickup.cs
// Free-floating Glow pickup spawned at the end of a ritual. Carry / deposit /
// intercept mechanics are a follow-up slice — for now the pickup just sits
// at the spawn position and despawns after GlowPickupTimeout (spec §4.5).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Glow pickup entity. Spec §5.1: "Drop a physical Glow pickup on
    /// completion that must be carried back to a deposit building. Glow
    /// pickup can be intercepted in transit by any faction."
    ///
    /// FactionTag is left unset (Faction.Curse as a neutral default) so
    /// any unit can attempt to claim it. The pickup is owned by no one
    /// until carried.
    /// </summary>
    public static class GlowPickup
    {
        public static Entity Create(EntityManager em, float3 position, RitualKind source, int amount = -1)
        {
            int yield = amount > 0
                ? amount
                : source switch
                {
                    RitualKind.Purification => PurificationGlowYield,
                    _ => PurificationGlowYield,  // Conversion / Violent Extraction yields TBD by follow-up
                };

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(GlowPickupTag),
                typeof(GlowPickupState),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = GlowPickupPresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = Faction.Curse }); // neutral; reassigned on pickup
            em.SetComponentData(entity, new GlowPickupState
            {
                Amount = yield,
                TimeRemaining = GlowPickupTimeout,
                Source = source,
            });
            em.SetComponentData(entity, new Radius { Value = 0.6f });

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, RitualKind source, int amount = -1)
        {
            int yield = amount > 0
                ? amount
                : source switch
                {
                    RitualKind.Purification => PurificationGlowYield,
                    _ => PurificationGlowYield,
                };

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = GlowPickupPresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = Faction.Curse });
            ecb.AddComponent<GlowPickupTag>(entity);
            ecb.AddComponent(entity, new GlowPickupState
            {
                Amount = yield,
                TimeRemaining = GlowPickupTimeout,
                Source = source,
            });
            ecb.AddComponent(entity, new Radius { Value = 0.6f });

            return entity;
        }
    }
}
