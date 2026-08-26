// File: Assets/GameData/TechTree/Units/Feraldis/Eagle/Eagle.cs
// The Feraldis Scout's eagle. Canon: docs/Design/Age_1_Feraldis.md.
//
// Feraldis scouts trade the huge static scout-sight circle for a bird that
// circles them and carries its OWN line of sight. Same total vision budget,
// completely different shape: a wide sweeping arc that keeps re-revealing
// its surroundings instead of one fixed bubble. It also means a Feraldis
// scout sees things BESIDE its path, not just ahead of it.
//
// Deliberately NOT a unit: no UnitTag, no Health, no PopulationCost. It
// cannot be selected, ordered, attacked or killed — kill the scout and the
// eagle goes with it (EagleOrbitSystem). FogOfWarSystem only needs
// LineOfSight + LocalTransform + FactionTag to reveal, so that is all it has.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class Eagle
    {
        public const int PresentationID = 345;

        public static Entity Create(EntityManager em, float3 position, Faction faction, Entity owner)
        {
            var e = em.CreateEntity(
                typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(LineOfSight), typeof(EagleCompanion));

            em.SetComponentData(e, new PresentationId { Id = PresentationID });
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 0.45f));
            em.SetComponentData(e, new FactionTag { Value = faction });
            em.SetComponentData(e, new LineOfSight
            {
                Radius = TheWaningBorder.Core.Config.FeraldisConstants.EagleLos
            });

            // Deterministic per-eagle phase from the owner's index — no RNG,
            // so lockstep clients fly identical birds.
            float seed = (owner.Index % 360) * math.PI / 180f;
            em.SetComponentData(e, new EagleCompanion
            {
                Owner = owner,
                Angle = seed,
                WobblePhase = seed * 1.7f,
            });

            return e;
        }
    }
}
