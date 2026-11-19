// HealingWard.cs
// THE HEALING WARD (docs/Design/Sects.md, Renewal, 2026-11-18): the one-cell
// building a Litharch plants once the Sect of Renewal's research is bought.
// It works like the Litharch's Cleansing Ward (Territory_Claims.md §6.9.7):
// it stands until destroyed (it is fragile: 1 HP on its SO) and PULSES every HealingWardSystem.asset
// pulseSeconds, each pulse healing every wounded allied unit inside the
// card's Radius (HealingWardSystem). It replaced the Field Hospital, a 2 x 2
// tent that tore itself down after two minutes.
//
// Each Litharch has its own cooldown (the card's); the shared one went with
// the permanent ward (developer 2026-11-18: "the cooldown can be per
// litharch").
//
// Not in the worker's build palette: no BuildCosts entry, no worker row. The
// Litharch PLACES it like a building, within the card's reach (Build_Grid.md
// § Buildings raised by abilities): GroundPlacement shows the ghost on the
// grid, SiteRefusal judges the snapped site, and AbilityEffectExecutor ->
// DeployHealingWard raises it there, already finished — no UnderConstruction
// phase. Every stat is its building SO's (RenewalHealingWard.asset), every
// rule number HealingWardSystem.asset's or the card's.

using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Economy;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    public static class HealingWard
    {
        public const int PresentationID = 358;

        /// <summary>Its BuildingDefSO id (stats, footprintCells, the art).</summary>
        public const string Id = "Renewal_HealingWard";

        /// <summary>The ability card's name (HealingWard.asset).</summary>
        public const string CardName = "Healing Ward";

        static readonly ComponentType[] QT_Standing =
        {
            ComponentType.ReadOnly<HealingWardTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_Standing;

        /// <summary>
        /// May <paramref name="faction"/> plant a ward at <paramref name="position"/>?
        /// No past the TEAM's cap of standing wards, and no within the spacing
        /// of one of the team's (docs/Design/Sects.md, Renewal;
        /// HealingWardSystem.asset). The reason names the rule broken.
        /// </summary>
        public static bool CanDeploy(EntityManager em, Faction faction, float3 position, out string reason)
        {
            reason = null;
            var cfg = TheWaningBorder.Systems.Buildings.HealingWardSystemConfig.I;
            if (cfg == null) return true;
            int cap = cfg.capPerTeam;
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
                        "Too close to another Healing Ward (keep {0} m apart)."), (int)cfg.minSpacingMetres);
                    return false;
                }
            }
            if (cap > 0 && standing >= cap)
            {
                reason = string.Format(TheWaningBorder.Core.Localization.Loc.T(
                    "Your team already has {0} Healing Ward(s) standing."), cap);
                return false;
            }
            return true;
        }

        /// <summary>The footprint in metres: the SO's footprintCells (one cell).</summary>
        public static int2 SizeMeters => BuildingSizeConfig.GetSize(Id);

        /// <summary>Where a ward aimed at <paramref name="aim"/> stands: on
        /// the build grid by its footprint, at the ground's height.</summary>
        public static float3 SiteFor(float3 aim) => AbilityBuildingSite.Snap(aim, SizeMeters);

        /// <summary>
        /// The one test of a ward's SNAPPED site, with its reason (null = it
        /// may stand here): the ghost, the AI and the executor all ask it.
        /// Within the card's reach of the caster (plus <paramref name="reachSlack"/>),
        /// on ground a building could stand on (crust, geometry, the owner's
        /// plans) — no territory gate, a ward is planted where the fight is —
        /// and the team's cap and spacing.
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

        // ── The building ────────────────────────────────────────────────────

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(Id);
            int hp = (int)math.max(1f, def.hp);

            Entity e = em.CreateEntity();

            em.AddComponentData(e, new PresentationId { Id = PresentationID });
            em.AddComponentData(e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.AddComponentData(e, new FactionTag { Value = faction });
            em.AddComponentData(e, new BuildingTag { IsBase = 0 });
            em.AddComponent<HealingWardTag>(e);
            em.AddComponentData(e, new Health { Value = hp, Max = hp });
            em.AddComponentData(e, new Radius { Value = def.radius });
            // Its footprint, for the nav stamp and every later overlap test.
            var size = SizeMeters;
            em.AddComponentData(e, new BuildingSize { Width = size.x, Height = size.y });
            em.AddComponentData(e, new LineOfSight { Radius = def.lineOfSight });
            em.AddComponentData(e, new ArmorTypeData
            {
                Value = CombatTypeParse.Armor(def.armorType, ArmorType.StructureHuman)
            });
            em.AddComponentData(e, new Defense
            {
                Melee = def.defense.melee,
                Ranged = def.defense.ranged,
                Siege = def.defense.siege,
                Magic = def.defense.magic,
            });

            // Conjured by an ability, so it never passes BuildingFactory.Create
            // and needs its own NetworkedEntity: without it every order aimed
            // at it under lockstep (the AI's repair loop) was dropped by the
            // CommandRouter guard (Hollow Table 16-23-16, 2026-09-13). The cast
            // is a replicated command, so this runs in the same order on every
            // peer and the id it draws is the same everywhere.
            em.AddComponentData(e, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick
            });
            em.AddComponentData(e, new DisplayName
            {
                Value = TheWaningBorder.Core.DisplayNames.ForBuildingFixed(Id)
            });

            return e;
        }
    }
}
