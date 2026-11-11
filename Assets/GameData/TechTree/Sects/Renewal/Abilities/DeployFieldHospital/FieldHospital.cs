// FieldHospital.cs
// The temporary building a Litharch deploys once the Field Hospital
// tech is researched. It heals allied units around it and then tears itself
// down after two minutes.
//
// Deliberately NOT in the worker's build palette: it has no BuildCosts entry
// and no worker-catalog row. The Litharch PLACES it like a building, within the
// card's reach (2026-11-11, Build_Grid.md § Buildings raised by abilities):
// GroundPlacement shows the ghost on the grid, SiteRefusal judges the snapped
// site, and AbilityEffectExecutor -> DeployFieldHospital raises it there,
// already finished — no UnderConstruction phase, because a field hospital that
// takes 30 s to raise would be useless in the fight it was cast for.

using TheWaningBorder.Core.Multiplayer;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class FieldHospital
    {
        public const int PresentationID = 358;

        /// <summary>Its BuildingDefSO id (footprintCells, the art).</summary>
        public const string Id = "Renewal_FieldHospital";

        public const float LifetimeSeconds = 120f;
        /// <summary>Heal radius: the canon Medium (docs/Design/Spells.md 8.5),
        /// and the Deploy Field Hospital card's Radius, which must match.</summary>
        public const float HealRadius = 15f;
        public const float HealPerSecond = 3f;
        public const int MaxHealth = 400;
        public const float BuildingRadius = 1.2f;
        public const float Sight = 10f;

        static readonly ComponentType[] QT_Standing =
        {
            ComponentType.ReadOnly<FieldHospitalTag>(),
            ComponentType.ReadOnly<FieldHospitalState>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_Standing;

        /// <summary>
        /// May <paramref name="faction"/> raise a hospital at <paramref name="position"/>?
        /// No past the TEAM's cap of standing hospitals, and no within the
        /// spacing of one of the team's (docs/Design/Sects.md, Renewal;
        /// FieldHospitalSystem.asset). The reason names the rule broken.
        /// </summary>
        public static bool CanDeploy(EntityManager em, Faction faction, float3 position, out string reason)
        {
            reason = null;
            var cfg = TheWaningBorder.Systems.Buildings.FieldHospitalSystemConfig.I;
            if (cfg == null) return true;
            int cap = cfg.capPerTeam;
            float minD2 = cfg.minSpacingMetres * cfg.minSpacingMetres;
            var q = QC_Standing.Get(em, QT_Standing);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Unity.Collections.Allocator.Temp);
            using var sts = q.ToComponentDataArray<FieldHospitalState>(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            int standing = 0;
            for (int i = 0; i < facs.Length; i++)
            {
                if (hps[i].Value <= 0 || sts[i].TimeToLive <= 0f) continue;
                if (!Alliances.AreAllied(faction, facs[i].Value)) continue;
                standing++;
                float2 d = xfs[i].Position.xz - position.xz;
                if (minD2 > 0f && math.dot(d, d) < minD2)
                {
                    reason = string.Format(TheWaningBorder.Core.Localization.Loc.T(
                        "Too close to another field hospital (keep {0} m apart)."), (int)cfg.minSpacingMetres);
                    return false;
                }
            }
            if (cap > 0 && standing >= cap)
            {
                reason = string.Format(TheWaningBorder.Core.Localization.Loc.T(
                    "Your team already has {0} field hospital(s) standing."), cap);
                return false;
            }
            return true;
        }

        /// <summary>The footprint in metres: the SO's footprintCells.</summary>
        public static int2 SizeMeters => BuildingSizeConfig.GetSize(Id);

        /// <summary>Where a hospital aimed at <paramref name="aim"/> stands: on
        /// the build grid by its footprint, at the ground's height.</summary>
        public static float3 SiteFor(float3 aim) => AbilityBuildingSite.Snap(aim, SizeMeters);

        /// <summary>
        /// The one test of a hospital's SNAPPED site, with its reason (null = it
        /// may stand here): the ghost, the AI and the executor all ask it.
        /// Within the card's reach of the caster (plus <paramref name="reachSlack"/>),
        /// on ground a building could stand on (crust, geometry, the owner's
        /// plans) — no territory gate, a field hospital is raised where the
        /// fight is — and the team's cap and spacing.
        /// </summary>
        public static string SiteRefusal(EntityManager em, Entity caster, TheWaningBorder.Abilities.AbilityCard card,
            float3 site, float reachSlack = 0f)
        {
            if (!em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster))
                return TheWaningBorder.Core.Localization.Loc.T("The caster is gone");
            float3 here = em.GetComponentData<LocalTransform>(caster).Position;
            if (card != null && card.Range > 0f && math.distance(here.xz, site.xz) > card.Range + reachSlack)
                return TheWaningBorder.Core.Localization.Loc.T("Out of the Litharch's reach");
            var faction = em.GetComponentData<FactionTag>(caster).Value;
            string why = AbilityBuildingSite.Refusal(em, faction, site, SizeMeters, Id,
                AbilityBuildingSite.Ground.Buildable);
            if (why != null) return why;
            return CanDeploy(em, faction, site, out why) ? null : why;
        }

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
            // Its footprint, for the nav stamp and every later overlap test.
            var size = SizeMeters;
            em.AddComponentData(e, new BuildingSize { Width = size.x, Height = size.y });
            em.AddComponentData(e, new LineOfSight { Radius = Sight });
            em.AddComponentData(e, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(e, new Defense { Melee = 0, Ranged = 1, Siege = 0, Magic = 2 });

            // Conjured by an ability, so it never passes BuildingFactory.Create
            // and used to ship without a NetworkedEntity: under lockstep every
            // order aimed at it (the AI's repair loop, every ~30 s) was dropped
            // by the CommandRouter guard (Hollow Table 16-23-16, 2026-09-13).
            // The cast is a replicated command, so this runs in the same order
            // on every peer and the id it draws is the same everywhere.
            em.AddComponentData(e, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick
            });
            em.AddComponentData(e, new DisplayName
            {
                Value = TheWaningBorder.Core.DisplayNames.ForBuildingFixed("FieldHospital")
            });

            return e;
        }
    }
}
