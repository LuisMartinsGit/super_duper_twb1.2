// FieldHospital.cs
// The temporary building a Litharch deploys once the Field Hospital
// tech is researched. It heals allied units around it and then tears itself
// down after two minutes.
//
// Deliberately NOT a placeable building: it has no BuildCosts entry and no
// worker-catalog row. It is spawned by the ability (see AbilityEffectExecutor
// -> DeployFieldHospital) at the caster's position, already finished — no
// UnderConstruction phase, because a field hospital that takes 30 s to raise
// would be useless in the fight it was cast for.

using TheWaningBorder.Core.Multiplayer;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class FieldHospital
    {
        public const int PresentationID = 358;

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
