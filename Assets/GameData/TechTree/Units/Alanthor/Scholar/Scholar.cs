// Alanthor scholar — performs the Purification ritual at active veilstone
// nodes. Vulnerable channeling unit (spec §11 item 1). Survival depends on
// escort: defending border waves spawn during the ritual.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Spawns Alanthor scholars. Speed and HP intentionally low — the
    /// scholar is a "soft" caster that needs escorts during rituals.
    /// </summary>
    public static class Scholar
    {
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = ScholarHP;
            float speed = ScholarSpeed;
            float los = ScholarLoS;

            if (TechCatalog.TryGetUnit("Alanthor_Scholar", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = ScholarPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Magic });
            creator.AddComponent<ScholarTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            // Scholars don't attack — Damage = 0. They rely on escorts.
            creator.AddComponent(entity, new Damage { Value = 0 });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = ScholarRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            // Bake DesiredDestination so PurificationRitualSystem can SetComponent
            // a move target without a structural change inside its query loop.
            // Mirrors Miner.cs / Builder.cs.
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Combat type tags — Scholars take Ranged-class hits poorly so the
            // defending border-wave Veilstingers are an effective interrupt.
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });

            return entity;
        }
    }
}
