// CurseKillReligionSystem.cs
// RELIGION FROM THE CURSE (docs/Design/Religion.md §1, 2026-09-29).
//
// Every curse unit that dies pays kill POINTS to the faction that landed the
// last hit — whatever dealt it: a soldier, a tower, a sect power, a hero, an
// ally's hit paying that ally. Points convert to Religion Points at an
// escalating rate (FactionReligionPointsHelper.AddKillPoints).
//
//   crystalling  3   veilstinger  8   godsplinter  20   (FactionReligionPoints.asset)
//
// The last hit is LastDamagedByFaction, which every damage path stamps
// (melee, ranged, projectiles incl. towers, spells, damage over time). A
// curse unit with no hostile hit on record — killed by exposure or by
// nothing anyone owns — pays no one.
//
// Fires once per death, in the same slot and with the same filter as
// BorderDeathDropSystem: after the damage systems, before DeathSystem, on
// bodies whose death has not yet been registered (DeathAnimationState still
// disabled). DeathSystem registers it the same tick.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// THE TEMPLE PRODUCES RELIGION SLOWLY (docs/Design/Religion.md §2,
    /// 2026-09-29): a finished, standing Temple of Ridan pays its faction one
    /// point every templeSecondsPerPoint seconds — the same points curse kills
    /// pay, so they fill the same ring and convert at the same escalating
    /// rate. On the lockstep clock, whole seconds, integer counter: every
    /// peer pays the same point on the same tick.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class TempleReligionSystem : SystemBase
    {
        private SimCadence.Periodic _acc;
        private EntityQuery _temples;

        protected override void OnCreate()
        {
            _temples = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TempleOfRidanTag, FactionTag, Health>()
                .WithNone<UnderConstruction>()
                .Build(this);
            RequireForUpdate(_temples);
        }

        protected override void OnUpdate()
        {
            var lockstep = TheWaningBorder.Multiplayer.LockstepManager.Instance;
            if (lockstep != null && !lockstep.IsSimulationRunning) return;
            if (!_acc.Due(SystemAPI.Time.DeltaTime, 1f)) return;

            var em = EntityManager;
            int period = System.Math.Max(1, FactionReligionPointsHelper.Cfg.templeSecondsPerPoint);
            using var facs = _temples.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = _temples.ToComponentDataArray<Health>(Allocator.Temp);

            // One Temple per faction; a faction is paid once per second
            // however its Temple count came out.
            uint paid = 0;
            for (int i = 0; i < facs.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                int f = (int)facs[i].Value;
                if (f < 0 || f > 7 || (paid & (1u << f)) != 0) continue;
                paid |= 1u << f;

                var faction = facs[i].Value;
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)
                    || !em.HasComponent<FactionReligionPoints>(bank)) continue;
                var rp = em.GetComponentData<FactionReligionPoints>(bank);
                rp.TempleSeconds++;
                bool due = rp.TempleSeconds >= period;
                if (due) rp.TempleSeconds = 0;
                em.SetComponentData(bank, rp);
                if (!due) continue;

                int gained = FactionReligionPointsHelper.AddKillPoints(em, faction, 1);
                if (gained > 0 && faction == GameSettings.LocalPlayerFaction)
                    SimSignals.Notify(string.Format(
                        TheWaningBorder.Core.Localization.Loc.T("+{0} Religion Point from the Temple"), gained));
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class CurseKillReligionSystem : SystemBase
    {
        private EntityQuery _curseUnits;
        private EntityQuery _curseNodes;

        protected override void OnCreate()
        {
            _curseUnits = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BorderUnitTag, Health, FactionTag>()
                .WithNone<DeathAnimationState>()
                .Build(this);
            // Curse nodes about to collapse: registered once — DeathSystem
            // gives a dying building BuildingCollapseState the same tick.
            _curseNodes = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SmallNodeTag, Health, FactionTag>()
                .WithNone<BuildingCollapseState>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            PayForCurseNodes(em);
            if (_curseUnits.IsEmpty) return;
            using var ents = _curseUnits.ToEntityArray(Allocator.Temp);
            using var hps = _curseUnits.ToComponentDataArray<Health>(Allocator.Temp);
            using var facs = _curseUnits.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var cfg = FactionReligionPointsHelper.Cfg;
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value > 0) continue;
                if (facs[i].Value != Faction.Border) continue;   // a converted body is no curse kill
                var e = ents[i];

                if (!TransientState.Active<LastDamagedByFaction>(em, e)) continue;
                var killer = em.GetComponentData<LastDamagedByFaction>(e).Value;
                if (killer == Faction.Border || (int)killer < 0 || (int)killer > 7) continue;

                int pts = em.HasComponent<GodsplinterState>(e) ? cfg.ptsGodsplinter
                        : em.HasComponent<VeilstingerState>(e) ? cfg.ptsVeilstinger
                        : cfg.ptsCrystalling;

                // A STANDING TEMPLE BOOSTS THE HARVEST (Religion.md §2): a
                // faction whose Temple is finished and alive earns
                // templeKillBonusPct more per kill. Integer maths — every
                // lockstep peer pays the same.
                if (SectQuery.HasStandingTemple(em, killer))
                    pts = pts * (100 + cfg.templeKillBonusPct) / 100;

                int gained = FactionReligionPointsHelper.AddKillPoints(em, killer, pts);
                if (gained > 0 && killer == GameSettings.LocalPlayerFaction)
                    SimSignals.Notify(string.Format(
                        TheWaningBorder.Core.Localization.Loc.T("+{0} Religion Point from the curse"), gained));
            }
        }

        /// <summary>
        /// DESTROYING A CURSE NODE PAYS A FULL RELIGION POINT (Religion.md §1,
        /// 2026-09-29) to the faction that landed the last hit — straight to
        /// the balance, not through the points ladder.
        /// </summary>
        private void PayForCurseNodes(EntityManager em)
        {
            if (_curseNodes.IsEmpty) return;
            using var ents = _curseNodes.ToEntityArray(Allocator.Temp);
            using var hps = _curseNodes.ToComponentDataArray<Health>(Allocator.Temp);
            using var facs = _curseNodes.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value > 0 || facs[i].Value != Faction.Border) continue;
                var e = ents[i];
                if (!TransientState.Active<LastDamagedByFaction>(em, e)) continue;
                var killer = em.GetComponentData<LastDamagedByFaction>(e).Value;
                if (killer == Faction.Border || (int)killer < 0 || (int)killer > 7) continue;

                FactionReligionPointsHelper.Refund(em, killer, 1);
                if (killer == GameSettings.LocalPlayerFaction)
                    SimSignals.Notify(TheWaningBorder.Core.Localization.Loc.T(
                        "+1 Religion Point — a curse node destroyed"));
            }
        }
    }
}
