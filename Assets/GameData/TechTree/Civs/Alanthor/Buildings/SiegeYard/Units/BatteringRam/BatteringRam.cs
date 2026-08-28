using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Battering Ram unit - Alanthor culture melee siege engine (calculator
    /// 2026-08: Siege Yard Lv 2). Melee range — it must physically touch the
    /// wall — with the highest anti-building damage per hit (+80 vs Building),
    /// and it CANNOT attack units: BuildingsOnlyAttacker (declared in the
    /// co-located BatteringRamComponents.cs) makes TargetingSystem skip every
    /// non-building candidate and MeleeCombatSystem drop a force-ordered
    /// non-building target instead of swinging.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class BatteringRam
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "alanthor_siegeyard_battering_ram" — 340 HP / 36 dmg / 3.0 cd /
        // range 1 / LoS 18 / speed 3.0 / 36 s train / pop 2 /
        // 220 S + 120 I + 40 V / defense 0-1-2-0).
        public const int PresentationID = 347;

        /// <summary>Create Battering Ram using EntityManager.</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create Battering Ram using EntityCommandBuffer for deferred creation.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Alanthor_BatteringRam");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float cooldown = def.attackCooldown;
        

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            creator.AddComponent<SiegeTag>(entity);
            // Anti-structure ONLY — see class doc + BatteringRamComponents.cs.
            creator.AddComponent<BuildingsOnlyAttacker>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 2 });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 2, Magic = 0 });

            // SO bonus-vs-tags (+80 vs Building). Design canon for the ram, so
            // it must survive a missing / ungenerated TechTreeCatalog entry or
            // an unparseable SO list: fall back to a hard +80 vs Building when
            // the parsed component comes back empty (Ballista pattern).
            var bonusVsTags = UnitTagParse.Bonus(def != null ? def.bonusVsTags : null);
            if (bonusVsTags.IsEmpty)
                bonusVsTags = new BonusVsTags
                {
                    Mask0 = (uint)UnitTagBits.Building,
                    Amount0 = 80,
                };
            creator.AddComponent(entity, bonusVsTags);

            return entity;
        }
    }
}
