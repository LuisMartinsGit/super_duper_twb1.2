// RunaiUnits.cs
// Creators for every Runai unit of the 2026-10-13 redesign
// (docs/Design/Age_1_Runai.md §6). Every stat is the unit SO's; the art is
// borrowed from an existing unit (the SO's prefab + presentation id) until
// Runai art exists. UnitFactory.Create stamps the network id, the display
// name, the UnitTypeId and the counter tags on top of what is built here.

using TheWaningBorder.Abilities;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>The shared body of a Runai unit built from its SO.</summary>
    internal static class RunaiUnitBody
    {
        public static Entity Base<TCreator>(TCreator c, string unitId, int pid, UnitClass cls, float3 p, Faction f,
            DamageType dmgType, ArmorType armor, out UnitDef def)
            where TCreator : struct, IEntityCreator
        {
            def = TechCatalog.Unit(unitId);
            var e = c.CreateEntity();
            c.AddComponent(e, new PresentationId { Id = pid });
            c.AddComponent(e, LocalTransform.FromPositionRotationScale(p, quaternion.identity, 1f));
            c.AddComponent(e, new FactionTag { Value = f });
            c.AddComponent(e, new UnitTag { Class = cls });
            c.AddComponent<RunaiUnitTag>(e);
            c.AddComponent(e, new Health { Value = (int)def.hp, Max = (int)def.hp });
            c.AddComponent(e, new MoveSpeed { Value = def.speed });
            c.AddComponent(e, new Damage { Value = (int)def.damage });
            c.AddComponent(e, new AttackCooldown { Cooldown = def.attackCooldown, Timer = 0f });
            c.AddComponent(e, new LineOfSight { Radius = def.lineOfSight });
            c.AddComponent(e, new Target { Value = Entity.Null });
            c.AddComponent(e, new Radius { Value = def.radius });
            c.AddComponent(e, new PopulationCost { Amount = def.populationCost });
            c.AddComponent(e, new DamageTypeData { Value = dmgType });
            c.AddComponent(e, new ArmorTypeData { Value = armor });
            c.AddComponent(e, new RunaiUnitClock { LastPos = p, LastHp = (int)def.hp });
            var d = def.defense;
            c.AddComponent(e, d == null ? new Defense()
                : new Defense { Melee = d.melee, Ranged = d.ranged, Siege = d.siege, Magic = d.magic });
            return e;
        }

        public static void Ranged<TCreator>(TCreator c, Entity e, UnitDef def, byte defaultTrajectory)
            where TCreator : struct, IEntityCreator
        {
            byte traj = string.IsNullOrEmpty(def.trajectory) ? defaultTrajectory : ShotTrajectory.Parse(def.trajectory);
            c.AddComponent<ArcherTag>(e);
            c.AddComponent(e, new ArcherState
            {
                AimTimeRequired = def.aimTime, MinRange = def.minAttackRange, MaxRange = def.attackRange,
                Trajectory = traj, ProjectileSpeed = def.projectileSpeed,
            });
        }

        public static void Abilities<TCreator>(TCreator c, Entity e, UnitDef def)
            where TCreator : struct, IEntityCreator
        {
            if (def.abilities == null || def.abilities.Length == 0) return;
            c.AddComponent(e, AbilityAssignment.Build(def.abilities));
            c.AddComponent(e, default(AbilityCooldowns));
        }
    }

    // ── Fortress ────────────────────────────────────────────────────────

    /// <summary>Runai_CaravanMaster — caravans near him move faster and carry more (§6.1).</summary>
    public static class RunaiCaravanMaster
    {
        public const string Id = "Runai_CaravanMaster";
        public const int PresentationID = 440;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryLight, out var def);
            c.AddComponent<CaravanMasterTag>(e);
            RunaiUnitBody.Abilities(c, e, def);
            return e;
        }
    }

    /// <summary>Runai_Wayfinder — the Runai hero (§8). Levels 1-10 from kills.</summary>
    public static class RunaiWayfinder
    {
        public const string Id = "Runai_Wayfinder";
        public const int PresentationID = 441;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.Cavalry, out var def);
            c.AddComponent<CavalryTag>(e);
            c.AddComponent(e, new UniqueUnitTag { Kind = UniqueUnitKind.RunaiWayfinder });
            RunaiUnitBody.Abilities(c, e, def);
            c.AddComponent(e, new HeroLevel { Value = HeroProgressionConfig.MinLevel });
            c.AddComponent(e, new HeroExperience { Xp = 0 });
            return e;
        }
    }

    // ── Route Guard ─────────────────────────────────────────────────────

    /// <summary>Runai_Skirmisher — javelins, short range (§6.2).</summary>
    public static class RunaiSkirmisher
    {
        public const string Id = "Runai_Skirmisher";
        public const int PresentationID = 331;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Ranged, p, f, DamageType.Ranged, ArmorType.Ranged, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.Flat);
            return e;
        }
    }

    /// <summary>Runai_RoadWarden — the escort (§6.2).</summary>
    public static class RunaiRoadWarden
    {
        public const string Id = "Runai_RoadWarden";
        public const int PresentationID = 442;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
            => RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryHeavy, out _);
    }

    /// <summary>Runai_Sandstrider — elite light infantry (§6.2).</summary>
    public static class RunaiSandstrider
    {
        public const string Id = "Runai_Sandstrider";
        public const int PresentationID = 443;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
            => RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryLight, out _);
    }

    // ── Grazing Grounds ─────────────────────────────────────────────────

    /// <summary>Runai_Raider — the fastest cavalry (§6.3).</summary>
    public static class RunaiRaider
    {
        public const string Id = "Runai_Raider";
        public const int PresentationID = 332;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.Cavalry, out _);
            c.AddComponent<CavalryTag>(e);
            return e;
        }
    }

    /// <summary>Runai_HorseArcher — mounted bow; fires on the move after Parthian Shot (§6.3).</summary>
    public static class RunaiHorseArcher
    {
        public const string Id = "Runai_HorseArcher";
        public const int PresentationID = 444;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Ranged, p, f, DamageType.Ranged, ArmorType.Cavalry, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.Flat);
            c.AddComponent<CavalryTag>(e);
            c.AddComponent<HorseArcherTag>(e);
            return e;
        }
    }

    /// <summary>Runai_CamelLancer — heavy anti-cavalry cavalry (§6.3).</summary>
    public static class RunaiCamelLancer
    {
        public const string Id = "Runai_CamelLancer";
        public const int PresentationID = 445;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.Cavalry, out _);
            c.AddComponent<CavalryTag>(e);
            return e;
        }
    }

    // ── Wagonworks ──────────────────────────────────────────────────────

    /// <summary>Runai_Catapult — lobbed splash (§6.4).</summary>
    public static class RunaiCatapult
    {
        public const string Id = "Runai_Catapult";
        public const int PresentationID = 337;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Siege, p, f, DamageType.Siege, ArmorType.Ranged, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.High);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<CatapultTag>(e);
            if (def.aoeRadius > 0f) c.AddComponent(e, new AOEShooterData { Radius = def.aoeRadius });
            return e;
        }
    }

    /// <summary>Runai_WarWagon — a moving fort that carries a squad and
    /// shoots on the move (§6.4). Rides the War Rig's boarding machinery.</summary>
    public static class RunaiWarWagon
    {
        public const string Id = "Runai_WarWagon";
        public const int PresentationID = 446;   // on its SO
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
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Siege, p, f, DamageType.Ranged, ArmorType.InfantryHeavy, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.Flat);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<WarRigTag>(e);
            c.AddComponent<WarWagonTag>(e);
            c.AddComponent<FiresOnMove>(e);
            RunaiUnitBody.Abilities(c, e, def);
            return e;
        }
    }

    /// <summary>Runai_SiegeCaravan — long-range building killer (§6.4).</summary>
    public static class RunaiSiegeCaravan
    {
        public const string Id = "Runai_SiegeCaravan";
        public const int PresentationID = 447;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Siege, p, f, DamageType.Siege, ArmorType.Ranged, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.High);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<CatapultTag>(e);
            if (def.aoeRadius > 0f) c.AddComponent(e, new AOEShooterData { Radius = def.aoeRadius });
            return e;
        }
    }

    // ── Autonomous ──────────────────────────────────────────────────────

    /// <summary>Runai_Caravan — carries a depot's stock to a market (§3.2).
    /// No orders, no claim weight, no population. Spawned by RunaiTradeSystem.</summary>
    public static class RunaiCaravanUnit
    {
        public const string Id = "Runai_Caravan";
        public const int PresentationID = 432;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Economy, p, f, DamageType.Melee, ArmorType.InfantryLight, out var def);
            c.AddComponent<CaravanTag>(e);
            c.AddComponent<NotControllableTag>(e);
            c.AddComponent(e, new DesiredDestination { Position = p, Has = 0 });
            c.AddComponent(e, new RunaiCaravan { Leg = CaravanLeg.Loading, BaseSpeed = def.speed });
            return e;
        }
    }

    /// <summary>Runai_TraderWarrior — escorts one caravan (§3.4). No orders,
    /// no claim weight, no population. Spawned by RunaiTradeSystem.</summary>
    public static class RunaiTraderWarriorUnit
    {
        public const string Id = "Runai_TraderWarrior";
        public const int PresentationID = 448;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryLight, out _);
            c.AddComponent<NotControllableTag>(e);
            c.AddComponent(e, new DesiredDestination { Position = p, Has = 0 });
            c.AddComponent(e, new RunaiTraderWarrior());
            return e;
        }
    }

    // ── Route Guard additions (2026-10-14) ──────────────────────────────

    /// <summary>Runai_DuneGuard — the line infantry (§6.2).</summary>
    public static class RunaiDuneGuard
    {
        public const string Id = "Runai_DuneGuard";
        public const int PresentationID = 450;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
            => RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryLight, out _);
    }

    // ── Arrowyard (§6.2) ────────────────────────────────────────────────

    /// <summary>Runai_Slinger — cheap ranged, anti-cavalry (§6.2).</summary>
    public static class RunaiSlinger
    {
        public const string Id = "Runai_Slinger";
        public const int PresentationID = 451;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Ranged, p, f, DamageType.Ranged, ArmorType.Ranged, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.Flat);
            return e;
        }
    }

    /// <summary>Runai_SunArcher — the long bow (§6.2).</summary>
    public static class RunaiSunArcher
    {
        public const string Id = "Runai_SunArcher";
        public const int PresentationID = 452;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Ranged, p, f, DamageType.Ranged, ArmorType.Ranged, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.High);
            return e;
        }
    }

    /// <summary>Runai_SkyBarge — flying siege (§6.4): ignores pathfinding,
    /// only ranged units and ballistas can hit it.</summary>
    public static class RunaiSkyBarge
    {
        public const string Id = "Runai_SkyBarge";
        public const int PresentationID = 453;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Siege, p, f, DamageType.Siege, ArmorType.Ranged, out var def);
            RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.High);
            c.AddComponent<SiegeTag>(e);
            c.AddComponent<FlyingTag>(e);
            if (def.aoeRadius > 0f) c.AddComponent(e, new AOEShooterData { Radius = def.aoeRadius });
            return e;
        }
    }

    // ── Escorts (§3.4) ──────────────────────────────────────────────────

    /// <summary>The three caravan escort units. Spawned by RunaiTradeSystem;
    /// no orders until The Call makes them an army (§8.1).</summary>
    public static class RunaiEscortUnits
    {
        public const string GuardId = "Runai_EscortGuard";
        public const string ArcherId = "Runai_EscortArcher";
        public const string RiderId = "Runai_EscortRider";
        public const int GuardPID = 454, ArcherPID = 455, RiderPID = 456;   // on their SOs
        public const string ReturnAbility = "Return to Wagons";

        public static Entity CreateGuard(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), EscortKind.Guard, p, f));
        public static Entity CreateGuard(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), EscortKind.Guard, p, f));
        public static Entity CreateArcher(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), EscortKind.Archer, p, f));
        public static Entity CreateArcher(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), EscortKind.Archer, p, f));
        public static Entity CreateRider(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), EscortKind.Rider, p, f));
        public static Entity CreateRider(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), EscortKind.Rider, p, f));

        private static Entity Make<T>(T c, EscortKind kind, float3 p, Faction f) where T : struct, IEntityCreator
        {
            Entity e;
            UnitDef def;
            switch (kind)
            {
                case EscortKind.Archer:
                    e = RunaiUnitBody.Base(c, ArcherId, ArcherPID, UnitClass.Ranged, p, f, DamageType.Ranged, ArmorType.Ranged, out def);
                    RunaiUnitBody.Ranged(c, e, def, ShotTrajectory.Flat);
                    break;
                case EscortKind.Rider:
                    e = RunaiUnitBody.Base(c, RiderId, RiderPID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.Cavalry, out def);
                    c.AddComponent<CavalryTag>(e);
                    break;
                default:
                    e = RunaiUnitBody.Base(c, GuardId, GuardPID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.InfantryLight, out def);
                    break;
            }
            c.AddComponent<NotControllableTag>(e);
            c.AddComponent(e, new DesiredDestination { Position = p, Has = 0 });
            c.AddComponent(e, new RunaiEscort { Kind = kind });
            c.AddComponent(e, AbilityAssignment.Build(ReturnAbility));
            c.AddComponent(e, default(AbilityCooldowns));
            return e;
        }
    }

    /// <summary>Runai_MirageRider — a Mirage decoy: no attack, fades (§8).</summary>
    public static class RunaiMirageRider
    {
        public const string Id = "Runai_MirageRider";
        public const int PresentationID = 449;   // on its SO
        public static Entity Create(EntityManager em, float3 p, Faction f) => EntityBuild.Done(Make(new EmCreator(em), p, f));
        public static Entity Create(EntityCommandBuffer ecb, float3 p, Faction f) => EntityBuild.Done(Make(new EcbCreator(ecb), p, f));
        private static Entity Make<T>(T c, float3 p, Faction f) where T : struct, IEntityCreator
        {
            var e = RunaiUnitBody.Base(c, Id, PresentationID, UnitClass.Melee, p, f, DamageType.Melee, ArmorType.Cavalry, out _);
            c.AddComponent<CavalryTag>(e);
            c.AddComponent<MirageRiderTag>(e);
            return e;
        }
    }
}
