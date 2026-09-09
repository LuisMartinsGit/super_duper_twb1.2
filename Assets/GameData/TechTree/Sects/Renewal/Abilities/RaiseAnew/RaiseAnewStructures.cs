// RaiseAnewStructures.cs
// The one recipe the three Raise Anew fortifications share. Each level's
// factory (RenewalTower / RenewalFortification / RenewalFortress) is a thin
// id + presentation-id + tag over this, so the three buildings can never
// drift apart in SHAPE while differing entirely in DATA: every number is
// read from that building's own BuildingDefSO in this folder.
//
// Like the Field Hospital these are conjured, not placeable: no BuildCosts
// entry, no builder-catalog row, and they spawn already finished, with no
// UnderConstruction phase, because a fortification raised mid-fight that
// then takes a minute to build would be useless in the fight it was cast
// for. Unlike the Field Hospital they are PERMANENT: no lifetime, no
// self-demolition, no upgrade ladder. They stand until destroyed.
//
// Reached through BuildingFactory.Create(em, "<id>", ...) so the dispatcher
// attaches the NetworkedEntity / DisplayName every peer agrees on, never
// through these classes direct (MP harness catch #9).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class RaiseAnewStructures
    {
        public static Entity Create<TTag>(EntityManager em, string buildingId, int presentationId,
            float3 position, Faction faction)
            where TTag : unmanaged, IComponentData
            => CreateInternal<EmCreator, TTag>(new EmCreator(em), buildingId, presentationId, position, faction);

        public static Entity Create<TTag>(EntityCommandBuffer ecb, string buildingId, int presentationId,
            float3 position, Faction faction)
            where TTag : unmanaged, IComponentData
            => CreateInternal<EcbCreator, TTag>(new EcbCreator(ecb), buildingId, presentationId, position, faction);

        private static Entity CreateInternal<TCreator, TTag>(TCreator creator, string buildingId,
            int presentationId, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
            where TTag : unmanaged, IComponentData
        {
            // Every stat comes from the SO. No constants ladder and no
            // "if (def.hp > 0)" guard: a missing stat is a data bug the
            // load-time audit reports, not a runtime branch here.
            var def = TechCatalog.Building(buildingId);
            float hp = def.hp;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = presentationId });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = def.lineOfSight });

            var gridSize = BuildingSizeConfig.GetSize(buildingId);
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });

            creator.AddComponent<TTag>(entity);

            creator.AddComponent(entity, new BuildingRangedAttack
            {
                Range = def.attack.range,
                Damage = (int)def.attack.damage,
                Cooldown = def.attack.cooldown,
                Timer = 0f,
                MaxTargets = def.attack.maxTargets,
            });

            creator.AddComponent(entity, new ArmorTypeData
            {
                Value = CombatTypeParse.Armor(def.armorType, ArmorType.StructureHuman)
            });
            creator.AddComponent(entity, new DamageTypeData
            {
                Value = CombatTypeParse.Damage(def.attack.damageType, DamageType.Ranged)
            });
            creator.AddComponent(entity, new Defense
            {
                Melee = def.defense.melee,
                Ranged = def.defense.ranged,
                Siege = def.defense.siege,
                Magic = def.defense.magic,
            });

            return entity;
        }
    }
}
