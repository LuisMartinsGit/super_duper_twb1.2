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
        /// <summary>Max HP of the packed form. Mirrors the Runai_BazaarWagon SO's hp —
/// BazaarPackSystem needs it as a compile-time constant for the pack/unpack HP
/// ratio, which is why this one number is duplicated in code.</summary>
        public const int MaxHP = 600;
        public const int PresentationID = 410;

        /// <summary>
        /// UnitFactory recipe entry — a wagon spawned outside the pack flow
        /// (scenario fixture, cheat, future trainer) starts at full HP and
        /// unpacks into a Bazaar at its authored max HP. The pack flow
        /// (BazaarPackSystem) keeps calling the proportional overloads below,
        /// so the HP-transfer rule is untouched.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => Create(em, position, faction, MaxHP,
                      (int)TechCatalog.Building("ThessarasBazaar").hp);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => Create(ecb, position, faction, MaxHP,
                      (int)TechCatalog.Building("ThessarasBazaar").hp);

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
            var def = TechCatalog.Unit("Runai_BazaarWagon");

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            creator.AddComponent(entity, new Health { Value = currentHP, Max = MaxHP });
            creator.AddComponent(entity, new MoveSpeed { Value = def.speed });
            creator.AddComponent(entity, new LineOfSight { Radius = def.lineOfSight });
            creator.AddComponent(entity, new Radius { Value = def.radius });
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
