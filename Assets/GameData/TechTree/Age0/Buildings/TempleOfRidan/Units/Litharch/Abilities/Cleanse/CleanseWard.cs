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
// Not in the worker's build palette: no BuildCosts entry, no worker row. The
// Litharch places it from a distance, on the build grid, like a building
// (2026-11-06): GroundPlacement shows it, SiteRefusal judges it, and
// AbilityEffectExecutor -> CleanseGround -> LitharchCleanse.Begin raises it
// already finished — no UnderConstruction phase.
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

        /// <summary>The ward's footprint in metres (footprintCells, in build cells).</summary>
        public static int2 FootprintMetres
        {
            get
            {
                var cfg = TheWaningBorder.Systems.Border.CleanseWardSystemConfig.I;
                int cells = cfg != null ? math.max(1, cfg.footprintCells) : 1;
                return BuildingSizeConfig.ToMeters(new int2(cells, cells));
            }
        }

        /// <summary>Where a ward aimed at <paramref name="aim"/> stands: on the
        /// build grid, at the ground's height there.</summary>
        public static float3 SiteFor(float3 aim)
        {
            float3 site = BuildGrid.Snap(aim, FootprintMetres);
            site.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(site.x, site.z);
            return site;
        }

        /// <summary>
        /// The one test of a ward's site, with its reason (null = it may stand
        /// here): the ghost, the AI and the executor all ask this, so red is
        /// exactly what the simulation refuses. Within the card's reach of the
        /// caster (plus <paramref name="reachSlack"/>), the building geometry
        /// rules, not on one of the owner's plans, and the team's cap and
        /// spacing. Territory and cursed ground are deliberately not asked: a
        /// ward is planted on the curse, on ground no one holds yet.
        /// </summary>
        public static string SiteRefusal(EntityManager em, Entity caster, TheWaningBorder.Abilities.AbilityCard card,
            float3 site, float reachSlack = 0f)
        {
            if (!em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster))
                return TheWaningBorder.Core.Localization.Loc.T("The caster is gone");
            float3 here = em.GetComponentData<LocalTransform>(caster).Position;
            if (card.Range > 0f && math.distance(here.xz, site.xz) > card.Range + reachSlack)
                return TheWaningBorder.Core.Localization.Loc.T("Out of the Litharch's reach");

            var size = FootprintMetres;
            var r = TheWaningBorder.Core.Commands.Types.BuildCommandHelper.CheckSiteGeometry(em, site, size, null);
            if (r != TheWaningBorder.World.Regions.PlacementRefusal.None)
                return TheWaningBorder.World.Regions.PlacementRefusalText.Of(r);

            var faction = em.GetComponentData<FactionTag>(caster).Value;
            if (PlannedBuildings.OverlapsOwnPlan(em, faction, site, size))
                return TheWaningBorder.World.Regions.PlacementRefusalText.Of(
                    TheWaningBorder.World.Regions.PlacementRefusal.Overlap);
            return CanPlant(em, faction, site, out string why) ? null : why;
        }

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
            // Its exact footprint, for the nav stamp and every later overlap test.
            var size = FootprintMetres;
            em.AddComponentData(e, new BuildingSize { Width = size.x, Height = size.y });
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
