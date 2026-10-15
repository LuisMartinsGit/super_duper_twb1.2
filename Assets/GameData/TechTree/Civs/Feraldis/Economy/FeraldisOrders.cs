// FeraldisOrders.cs
// The executor of every Feraldis order (CommandRouter.IssueFeraldisOrder ->
// LockstepCommandType.FeraldisOrder). Runs on every peer at the same tick, so
// everything here must be deterministic: no UnityEngine.Random, no wall clock.
// docs/Design/Age_1_Feraldis.md (2026-10-12).

using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Feraldis
{
    public static class FeraldisOrders
    {
        /// <summary>Cheap pre-check on the issuing side; the executor re-checks everything.</summary>
        public static bool IsValid(EntityManager em, Entity actor, Entity target, FeraldisOp op, int arg)
        {
            if (!em.Exists(actor) || !em.HasComponent<FactionTag>(actor)) return false;
            var faction = em.GetComponentData<FactionTag>(actor).Value;
            // The rig orders are not Feraldis-only: the Runai War Wagon rides the
            // same boarding machinery (docs/Design/Age_1_Runai.md §6.4).
            // So is the Sanctuary trade, which is Runai's alone.
            if (op == FeraldisOp.SanctuaryTrade)
                return TheWaningBorder.Runai.RunaiSanctuarySystem.CanTrade(em, actor);
            bool rigOp = op == FeraldisOp.RigLoad || op == FeraldisOp.RigUnload;
            if (!rigOp && !FeraldisEconomy.IsFeraldis(em, faction)) return false;
            switch (op)
            {
                case FeraldisOp.Staff:
                    return em.HasComponent<SlaveTag>(actor) && SameFaction(em, target, faction)
                        && FeraldisEconomy.CrewSlots(em, target) > 0;
                case FeraldisOp.MeltDown:
                    return CanMelt(em, actor) && SameFaction(em, target, faction)
                        && em.HasComponent<GreatForgeTag>(target);
                case FeraldisOp.Sacrifice:
                    return CanSacrifice(em, actor) && SameFaction(em, target, faction)
                        && em.HasComponent<TempleOfRidanTag>(target);
                case FeraldisOp.FieldSacrifice:
                    return em.HasComponent<SlaveTag>(actor);
                case FeraldisOp.ForgeAddBay:
                case FeraldisOp.ForgeSetRecipe:
                case FeraldisOp.ForgeUpgradeBay:
                    return em.HasComponent<GreatForgeTag>(actor);
                case FeraldisOp.TotemRite:
                    return em.HasComponent<WarTotemTag>(actor);
                case FeraldisOp.RigLoad:
                    return SameFaction(em, target, faction) && WarRig.CanLoad(em, actor, target);
                case FeraldisOp.RigUnload:
                    return em.HasComponent<WarRigTag>(actor);
                case FeraldisOp.Overcharge:
                    return em.HasComponent<WarRigTag>(actor) && FeraldisCombat.Researched(em, actor, "Feraldis_Overcharge");
            }
            return false;
        }

        public static void Execute(EntityManager em, Entity actor, Entity target, FeraldisOp op, int arg, float3 point)
        {
            if (!IsValid(em, actor, target, op, arg)) return;
            switch (op)
            {
                case FeraldisOp.Staff:     StartErrand(em, actor, target, FeraldisErrandKind.Staff); break;
                case FeraldisOp.MeltDown:  StartErrand(em, actor, target, FeraldisErrandKind.MeltDown); break;
                case FeraldisOp.Sacrifice: StartErrand(em, actor, target, FeraldisErrandKind.Sacrifice); break;
                case FeraldisOp.FieldSacrifice: FeraldisSacrifice.FieldSacrifice(em, actor); break;
                case FeraldisOp.ForgeAddBay:     GreatForge.AddBay(em, actor); break;
                case FeraldisOp.ForgeSetRecipe:  GreatForge.SetRecipe(em, actor, arg / 8, (ForgeRecipe)(arg % 8)); break;
                case FeraldisOp.ForgeUpgradeBay: GreatForge.UpgradeBay(em, actor, arg); break;
                case FeraldisOp.TotemRite:       WarTotemRites.Begin(em, actor, (TotemRite)arg); break;
                case FeraldisOp.RigLoad:         WarRig.Load(em, actor, target); break;
                case FeraldisOp.RigUnload:       WarRig.Unload(em, actor); break;
                case FeraldisOp.Overcharge:      FeraldisCombatSystems.Overcharge(em, actor); break;
                case FeraldisOp.SanctuaryTrade:  TheWaningBorder.Runai.RunaiSanctuarySystem.Trade(em, actor); break;
            }
        }

        // ── errands ──────────────────────────────────────────────────────

        private static void StartErrand(EntityManager em, Entity unit, Entity building, FeraldisErrandKind kind)
        {
            var errand = new FeraldisErrand { Building = building, Kind = kind, StuckSeconds = 0f };
            if (em.HasComponent<FeraldisErrand>(unit)) em.SetComponentData(unit, errand);
            else em.AddComponentData(unit, errand);
            if (kind != FeraldisErrandKind.Staff && em.HasComponent<StaffAssignment>(unit))
                em.RemoveComponent<StaffAssignment>(unit);

            float3 dest = em.GetComponentData<LocalTransform>(building).Position;
            MoveCommandHelper.Execute(em, unit, dest);
        }

        public static bool CanMelt(EntityManager em, Entity unit)
            => em.HasComponent<UnitTag>(unit) && !em.HasComponent<SlaveTag>(unit)
               && !em.HasComponent<UniqueUnitTag>(unit) && !em.HasComponent<CanBuild>(unit)
               && !em.HasComponent<WarRigTag>(unit);

        public static bool CanSacrifice(EntityManager em, Entity unit)
            => em.HasComponent<UnitTag>(unit) && !em.HasComponent<UniqueUnitTag>(unit)
               && !em.HasComponent<SectUniqueUnitTag>(unit);

        private static bool SameFaction(EntityManager em, Entity e, Faction faction)
            => e != Entity.Null && em.Exists(e) && em.HasComponent<FactionTag>(e)
               && em.GetComponentData<FactionTag>(e).Value == faction
               && !em.HasComponent<UnderConstruction>(e);
    }
}
