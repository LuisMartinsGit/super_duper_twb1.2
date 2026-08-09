// FieldHospital.cs
// The temporary building a Litharch deploys once the Shrine's Field Hospital
// tech is researched. It heals allied units around it and then tears itself
// down after two minutes.
//
// Deliberately NOT a placeable building: it has no BuildCosts entry and no
// builder-catalog row. It is spawned by the ability (see AbilityEffectExecutor
// -> DeployFieldHospital) at the caster's position, already finished — no
// UnderConstruction phase, because a field hospital that takes 30 s to raise
// would be useless in the fight it was cast for.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class FieldHospital
    {
        public const int PresentationID = 358;

        public const float LifetimeSeconds = 120f;
        public const float HealRadius = 12f;
        public const float HealPerSecond = 3f;
        public const int MaxHealth = 400;
        public const float BuildingRadius = 1.2f;
        public const float Sight = 10f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            Entity e = em.CreateEntity();

            em.AddComponentData(e, new PresentationId { Id = PresentationID });
            em.AddComponentData(e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.AddComponentData(e, new FactionTag { Value = faction });
            em.AddComponent<BuildingTag>(e);
            em.AddComponent<FieldHospitalTag>(e);
            em.AddComponentData(e, new FieldHospitalState { TimeToLive = LifetimeSeconds });
            em.AddComponentData(e, new Health { Value = MaxHealth, Max = MaxHealth });
            em.AddComponentData(e, new Radius { Value = BuildingRadius });
            em.AddComponentData(e, new LineOfSight { Radius = Sight });
            em.AddComponentData(e, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(e, new Defense { Melee = 0, Ranged = 1, Siege = 0, Magic = 2 });

            return e;
        }
    }
}
