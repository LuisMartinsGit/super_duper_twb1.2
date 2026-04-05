// File: Assets/Scripts/Entities/Units/BazaarWagon.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Bazaar Wagon — the packed (mobile) form of Thessara's Bazaar.
    /// Player-controllable Economy unit that can be moved and unpacked
    /// back into a Bazaar building at any valid location.
    /// HP transfers proportionally between building and wagon forms.
    /// </summary>
    public static class BazaarWagon
    {
        public const int MaxHP = 600;
        private const float DefaultSpeed = 3.0f;
        private const float DefaultLoS = 8f;
        private const float DefaultRadius = 1.0f;
        private const int PresentationID = 410;

        /// <summary>
        /// Create a Bazaar Wagon using EntityManager.
        /// </summary>
        /// <param name="em">EntityManager</param>
        /// <param name="position">World position</param>
        /// <param name="faction">Owner faction</param>
        /// <param name="currentHP">Proportional HP from the packed Bazaar</param>
        /// <param name="originalBazaarMaxHP">The Bazaar's max HP (for unpacking)</param>
        public static Entity Create(EntityManager em, float3 position, Faction faction,
            int currentHP, int originalBazaarMaxHP)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(PopulationCost),
                typeof(DesiredDestination)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Economy });
            em.SetComponentData(entity, new Health { Value = currentHP, Max = MaxHP });
            em.SetComponentData(entity, new MoveSpeed { Value = DefaultSpeed });
            em.SetComponentData(entity, new LineOfSight { Radius = DefaultLoS });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new PopulationCost { Amount = 0 });
            em.SetComponentData(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Wagon-specific components
            em.AddComponent<BazaarWagonTag>(entity);
            em.AddComponentData(entity, new BazaarWagonState
            {
                OriginalMaxHP = originalBazaarMaxHP
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create a Bazaar Wagon using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction,
            int currentHP, int originalBazaarMaxHP)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            ecb.AddComponent(entity, new Health { Value = currentHP, Max = MaxHP });
            ecb.AddComponent(entity, new MoveSpeed { Value = DefaultSpeed });
            ecb.AddComponent(entity, new LineOfSight { Radius = DefaultLoS });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });
            ecb.AddComponent(entity, new PopulationCost { Amount = 0 });
            ecb.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            ecb.AddComponent<BazaarWagonTag>(entity);
            ecb.AddComponent(entity, new BazaarWagonState
            {
                OriginalMaxHP = originalBazaarMaxHP
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
