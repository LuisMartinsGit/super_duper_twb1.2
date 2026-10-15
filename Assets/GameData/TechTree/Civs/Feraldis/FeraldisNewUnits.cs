// FeraldisNewUnits.cs
// Creators for the units the 2026-10-12 redesign added
// (docs/Design/Age_1_Feraldis.md §7-8): the Overseer, Harkon Fiend and the
// four Engine Works rigs. Every stat is the unit SO's; the art is borrowed
// from the closest existing unit (presentation id) until Feraldis art exists.

using TheWaningBorder.Abilities;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>The shared body of a Feraldis unit built from its SO.</summary>
    internal static class FeraldisUnitBody
    {
        public static Entity Base<TCreator>(TCreator creator, UnitDef def, int pid, UnitClass cls,
            float3 position, Faction faction, DamageType dmgType, ArmorType armor)
            where TCreator : struct, IEntityCreator
        {
            var e = creator.CreateEntity();
            creator.AddComponent(e, new PresentationId { Id = pid });
            creator.AddComponent(e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(e, new FactionTag { Value = faction });
            creator.AddComponent(e, new UnitTag { Class = cls });
            creator.AddComponent<FeraldisUnitTag>(e);
            creator.AddComponent(e, new Health { Value = (int)def.hp, Max = (int)def.hp });
            creator.AddComponent(e, new MoveSpeed { Value = def.speed });
            creator.AddComponent(e, new Damage { Value = (int)def.damage });
            creator.AddComponent(e, new AttackCooldown { Cooldown = def.attackCooldown, Timer = 0f });
            creator.AddComponent(e, new LineOfSight { Radius = def.lineOfSight });
            creator.AddComponent(e, new Target { Value = Entity.Null });
            creator.AddComponent(e, new Radius { Value = def.radius });
            creator.AddComponent(e, new PopulationCost { Amount = def.populationCost });
            creator.AddComponent(e, new DamageTypeData { Value = dmgType });
            creator.AddComponent(e, new ArmorTypeData { Value = armor });
            var d = def.defense;
            creator.AddComponent(e, d == null ? new Defense()
                : new Defense { Melee = d.melee, Ranged = d.ranged, Siege = d.siege, Magic = d.magic });
            return e;
        }

        public static void Ranged<TCreator>(TCreator creator, Entity e, UnitDef def, byte defaultTrajectory)
            where TCreator : struct, IEntityCreator
        {
            byte traj = string.IsNullOrEmpty(def.trajectory) ? defaultTrajectory : ShotTrajectory.Parse(def.trajectory);
            creator.AddComponent<ArcherTag>(e);
            creator.AddComponent(e, new ArcherState
            {
                AimTimeRequired = def.aimTime, MinRange = def.minAttackRange, MaxRange = def.attackRange,
                Trajectory = traj, ProjectileSpeed = def.projectileSpeed,
            });
        }
    }

    /// <summary>Feraldis_Overseer — slaves near it work faster (§3.3). One per faction.</summary>
    public static class FeraldisOverseer
    {
        public const int PresentationID = 427;   // Overseer.prefab (Knight variant) on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_Overseer");
            var e = FeraldisUnitBody.Base(c, def, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryLight);
            c.AddComponent<OverseerTag>(e);
            return e;
        }
    }

    /// <summary>Feraldis_HarkonFiend — the Feraldis hero (§8). Levels 1-10 from kills.</summary>
    public static class HarkonFiend
    {
        public const int PresentationID = 425;   // HarkonFiend.prefab (2-handed variant) on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_HarkonFiend");
            var e = FeraldisUnitBody.Base(c, def, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryHeavy);
            c.AddComponent(e, new UniqueUnitTag { Kind = UniqueUnitKind.HarkonFiend });
            c.AddComponent(e, AbilityAssignment.Build(def.abilities ?? System.Array.Empty<string>()));
            c.AddComponent(e, default(AbilityCooldowns));
            c.AddComponent(e, new HeroLevel { Value = HeroProgressionConfig.MinLevel });
            c.AddComponent(e, new HeroExperience { Xp = 0 });
            return e;
        }
    }

    /// <summary>Feraldis_RamRig — the explosive on wheels (§7.5).</summary>
    public static class FeraldisRamRig
    {
        public const int PresentationID = 422;   // RamRig.prefab on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_RamRig");
            var e = FeraldisUnitBody.Base(c, def, PresentationID, UnitClass.Siege, p, f, DamageType.Siege, ArmorType.InfantryHeavy);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<RamRigTag>(e);
            c.AddComponent<BuildingsOnlyAttacker>(e);
            return e;
        }
    }

    /// <summary>Feraldis_HarpoonRig — harpoons leave barbed wire (§7.5).</summary>
    public static class FeraldisHarpoonRig
    {
        public const int PresentationID = 423;   // HarpoonRig.prefab on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_HarpoonRig");
            var e = FeraldisUnitBody.Base(c, def, PresentationID, UnitClass.Siege, p, f, DamageType.Siege, ArmorType.Ranged);
            FeraldisUnitBody.Ranged(c, e, def, ShotTrajectory.Flat);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<HarpoonRigTag>(e);
            return e;
        }
    }

    /// <summary>Feraldis_CorpseEngine — lobbed splash that leaves blood (§7.5).</summary>
    public static class FeraldisCorpseEngine
    {
        public const int PresentationID = 421;   // CorpseEngine.prefab on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_CorpseEngine");
            var e = FeraldisUnitBody.Base(c, def, PresentationID, UnitClass.Siege, p, f, DamageType.Siege, ArmorType.Ranged);
            FeraldisUnitBody.Ranged(c, e, def, ShotTrajectory.High);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<CatapultTag>(e);
            c.AddComponent<CorpseEngineTag>(e);
            c.AddComponent(e, new AOEShooterData { Radius = def.aoeRadius });
            return e;
        }
    }

    /// <summary>Feraldis_WarRig — the flagship: carries infantry, fires on the move (§7.5).</summary>
    public static class FeraldisWarRig
    {
        public const int PresentationID = 424;   // WarRig.prefab on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f)
        {
            var e = EntityBuild.Done(Make(new EmCreator(em), p, f));
            em.AddBuffer<WarRigPassenger>(e);
            return e;
        }
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f)
        {
            var e = EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
            ecb.AddBuffer<WarRigPassenger>(e);
            return e;
        }
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_WarRig");
            var e = FeraldisUnitBody.Base(c, def, PresentationID, UnitClass.Siege, p, f, DamageType.Siege, ArmorType.InfantryHeavy);
            FeraldisUnitBody.Ranged(c, e, def, ShotTrajectory.High);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<WarRigTag>(e);
            c.AddComponent<FiresOnMove>(e);
            c.AddComponent(e, AbilityAssignment.Build(def.abilities ?? System.Array.Empty<string>()));
            c.AddComponent(e, default(AbilityCooldowns));
            if (def.aoeRadius > 0f) c.AddComponent(e, new AOEShooterData { Radius = def.aoeRadius });
            return e;
        }
    }
}
