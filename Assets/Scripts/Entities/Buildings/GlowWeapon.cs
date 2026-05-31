// File: Assets/Scripts/Entities/Buildings/GlowWeapon.cs
// Dropped Glow weapon — appears at the death position of a Glow-tier unit.
// Spec §4.5: only Glow-tier equipment drops. Attunement to claim:
// stand within GlowWeaponClaimRadius for GlowWeaponAttunementTime
// uninterrupted, qualifier must be Veilsteel-tier or higher.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    public static class GlowWeapon
    {
        public static Entity Create(EntityManager em, float3 position, UnitClass cls)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(GlowWeaponTag),
                typeof(GlowWeaponState),
                typeof(Radius)
            );
            em.SetComponentData(entity, new PresentationId { Id = GlowWeaponPresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = Faction.Curse }); // neutral; no faction owns it
            em.SetComponentData(entity, new GlowWeaponState
            {
                Class = cls,
                TimeRemaining = GlowWeaponPickupTimeout,
                Attuner = Entity.Null,
                AttunementProgress = 0f,
            });
            em.SetComponentData(entity, new Radius { Value = 0.5f });
            return entity;
        }
    }
}
