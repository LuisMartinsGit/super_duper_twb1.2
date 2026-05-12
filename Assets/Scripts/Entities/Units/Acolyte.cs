// File: Assets/Scripts/Entities/Units/Acolyte.cs
// Runai acolyte — performs the Conversion ritual at active crystal nodes.
// Vulnerable channeling unit (spec §5.4). Mechanical difficulty of the
// ritual is enforced by RitualDefenseSystem's RitualDefenseRunaiIntensity
// multiplier (spec §5.5: node fights enslavement hardest), not by the
// acolyte itself being weaker than a scholar.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    public static class Acolyte
    {
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = AcolyteHP;
            float speed = AcolyteSpeed;
            float los = AcolyteLoS;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Runai_Acolyte", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = AcolytePresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Magic });
            creator.AddComponent<AcolyteTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = 0 });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = AcolyteRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });

            return entity;
        }
    }
}
