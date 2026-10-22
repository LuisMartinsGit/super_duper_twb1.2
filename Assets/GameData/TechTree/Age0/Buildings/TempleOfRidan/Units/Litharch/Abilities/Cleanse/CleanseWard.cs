// CleanseWard.cs
// THE CLEANSING WARD (Territory_Claims.md §6.9.7, 2026-10-22): the building a
// Litharch's Cleanse plants at its aim point. It stands until destroyed and
// PULSES every CleanseWardSystem.asset pulseSeconds, each pulse clearing the
// cursed ground in a circle that starts at the ability's radius and grows by
// a fixed area a pulse up to the ability's maximum radius (CleanseWardSystem).
// Curse units prefer it as a target (TargetingSystem: FlagWard) and the
// curse's waves strike it first (CurseTerritorySystem.Waves), so a player
// must defend it. It never decays on cursed ground (CursedGroundDecaySystem).
//
// Deliberately NOT a placeable building: no BuildCosts entry, no worker row.
// Conjured by the ability (AbilityEffectExecutor -> CleanseGround ->
// LitharchCleanse.Begin), already finished — no UnderConstruction phase.
// Every number is CleanseWardSystem.asset's or the Cleanse card's.

using TheWaningBorder.Core.Multiplayer;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class CleanseWard
    {
        public const int PresentationID = 596;

        static readonly ComponentType[] QT_Standing =
        {
            ComponentType.ReadOnly<CleanseWardTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_Standing;

        /// <summary>
        /// May <paramref name="faction"/> plant a ward at <paramref name="position"/>?
        /// No past the TEAM's cap of standing wards, and no within the spacing
        /// of one of the team's (CleanseWardSystem.asset — the Field Hospital's
        /// rule, 2026-10-22). The reason names the rule broken.
        /// </summary>
        public static bool CanPlant(EntityManager em, Faction faction, float3 position, out string reason)
        {
            reason = null;
            var cfg = TheWaningBorder.Systems.Border.CleanseWardSystemConfig.I;
            if (cfg == null) return true;
            float minD2 = cfg.minSpacingMetres * cfg.minSpacingMetres;
            var q = QC_Standing.Get(em, QT_Standing);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            int standing = 0;
            for (int i = 0; i < facs.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                if (!Alliances.AreAllied(faction, facs[i].Value)) continue;
                standing++;
                float2 d = xfs[i].Position.xz - position.xz;
                if (minD2 > 0f && math.dot(d, d) < minD2)
                {
                    reason = string.Format(TheWaningBorder.Core.Localization.Loc.T(
                        "Too close to another Cleansing Ward (keep {0} m apart)."), (int)cfg.minSpacingMetres);
                    return false;
                }
            }
            if (cfg.capPerTeam > 0 && standing >= cfg.capPerTeam)
            {
                reason = string.Format(TheWaningBorder.Core.Localization.Loc.T(
                    "Your team already has {0} Cleansing Ward(s) standing."), cfg.capPerTeam);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Plant a ward at <paramref name="position"/> for <paramref name="faction"/>.
        /// <paramref name="startRadius"/> is cleared at once; the circle then grows
        /// by <paramref name="areaPerPulse"/> m² a pulse to <paramref name="maxRadius"/>.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction,
            float startRadius, float maxRadius, float areaPerPulse, float falloff)
        {
            var cfg = TheWaningBorder.Systems.Border.CleanseWardSystemConfig.I;
            int hp = cfg != null ? math.max(1, cfg.maxHealth) : 1;

            Entity e = em.CreateEntity();
            em.AddComponentData(e, new PresentationId { Id = PresentationID });
            em.AddComponentData(e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.AddComponentData(e, new FactionTag { Value = faction });
            em.AddComponentData(e, new BuildingTag { IsBase = 0 });
            em.AddComponent<CleanseWardTag>(e);
            em.AddComponentData(e, new CleanseWardState
            {
                Radius = math.max(0f, startRadius),
                MaxRadius = math.max(startRadius, maxRadius),
                AreaPerPulse = math.max(0f, areaPerPulse),
                Falloff = math.max(0f, falloff),
                PulseTimer = 0f,
                PulseCount = 0,
            });
            em.AddComponentData(e, new Health { Value = hp, Max = hp });
            em.AddComponentData(e, new Radius { Value = cfg != null ? math.max(0.5f, cfg.bodyRadius) : 1f });
            em.AddComponentData(e, new LineOfSight { Radius = cfg != null ? math.max(1f, cfg.sight) : 10f });
            em.AddComponentData(e, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(e, new Defense
            {
                Melee = cfg != null ? cfg.defenseMelee : 0,
                Ranged = cfg != null ? cfg.defenseRanged : 0,
                Siege = cfg != null ? cfg.defenseSiege : 0,
                Magic = cfg != null ? cfg.defenseMagic : 0,
            });

            // Conjured by a replicated command, so this runs in the same order
            // on every peer and the id it draws is the same everywhere (the
            // Field Hospital's lesson: without it every order aimed at it under
            // lockstep was dropped by the CommandRouter guard).
            em.AddComponentData(e, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick
            });
            em.AddComponentData(e, new DisplayName
            {
                Value = new Unity.Collections.FixedString64Bytes("Cleansing Ward")
            });

            // The starting circle, and its soft edge, is clean the moment the
            // ward lands (the 2026-10-19 "immediate" rule kept).
            TheWaningBorder.Systems.Border.CurseGroundSystem.ClearDisk(position, startRadius, falloff);
            return e;
        }
    }
}
