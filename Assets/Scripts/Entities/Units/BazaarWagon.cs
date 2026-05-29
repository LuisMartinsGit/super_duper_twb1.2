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
            => CreateInternal(new EmCreator(em), position, faction, currentHP, originalBazaarMaxHP);

        /// <summary>
        /// Create a Bazaar Wagon using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction,
            int currentHP, int originalBazaarMaxHP)
            => CreateInternal(new EcbCreator(ecb), position, faction, currentHP, originalBazaarMaxHP);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction,
            int currentHP, int originalBazaarMaxHP)
            where TCreator : struct, IEntityCreator
        {
            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            creator.AddComponent(entity, new Health { Value = currentHP, Max = MaxHP });
            creator.AddComponent(entity, new MoveSpeed { Value = DefaultSpeed });
            creator.AddComponent(entity, new LineOfSight { Radius = DefaultLoS });
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 0 });
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Wagon-specific components
            creator.AddComponent<BazaarWagonTag>(entity);
            creator.AddComponent(entity, new BazaarWagonState
            {
                OriginalMaxHP = originalBazaarMaxHP
            });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
