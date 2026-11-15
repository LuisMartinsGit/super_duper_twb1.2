// CountingHouseSystem.cs
// Capturing Counting Houses and their trickle (docs/Design/Sects.md § Sect of
// Prosperity, Plenty). The capture rules are the monument's
// (MonumentSystem, Monuments_And_Petriarchy.md §1): any unit inside the ring for
// captureSeconds takes it; hostile sides inside at once freeze it; leaving
// resets it; allies count together; the curse neither blocks nor takes it.
// Income is banked each second in sixtieths and paid in whole units. Lockstep:
// SimCadence clock, integer arithmetic, and nothing depends on unit order.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CountingHouseSystem : SystemBase
    {
        /// <summary>The capture and pay clock: whole seconds (engine-side, as in MonumentSystem).</summary>
        private const float TickSeconds = 1f;
        private const int Sides = 8;   // the eight factions; the curse does not count

        private static CountingHouseConfig Cfg => CountingHouseConfig.I;

        private SimCadence.Periodic _tick;
        private readonly int[] _inside = new int[Sides];

        private static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        private static CachedEntityQuery QC_Units;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!_tick.Due(SystemAPI.Time.DeltaTime, TickSeconds)) return;

            using var houses = CountingHouse.All(em, Allocator.Temp);
            if (houses.Length == 0) return;

            TickRings(em, houses);
            for (int h = 0; h < houses.Length; h++) Pay(em, houses[h]);
        }

        private void TickRings(EntityManager em, NativeArray<Entity> houses)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            float r2 = Cfg.captureRadius * Cfg.captureRadius;

            for (int h = 0; h < houses.Length; h++)
            {
                var at = em.GetComponentData<LocalTransform>(houses[h]).Position;
                System.Array.Clear(_inside, 0, Sides);
                for (int i = 0; i < ents.Length; i++)
                {
                    int side = (int)facs[i].Value;
                    if (side < 0 || side >= Sides || hps[i].Value <= 0) continue;
                    if (math.distancesq(xfs[i].Position.xz, at.xz) > r2) continue;
                    if (TransientState.Active<DeathAnimationState>(em, ents[i])) continue;
                    _inside[side]++;
                }
                Advance(em, houses[h]);
            }
        }

        private static bool SameSide(int a, int b)
            => a == b || !Alliances.AreHostile((Faction)a, (Faction)b);

        private void Advance(EntityManager em, Entity house)
        {
            var s = em.GetComponentData<CountingHouseState>(house);

            int claimant = -1;
            bool contested = false;
            for (int a = 0; a < Sides; a++)
            {
                if (_inside[a] == 0) continue;
                if (claimant >= 0 && !SameSide(a, claimant)) contested = true;
                if (claimant < 0 || _inside[a] > _inside[claimant]) claimant = a;
            }

            // Hostile sides both inside: the timer waits, and the holder keeps it.
            if (contested) return;

            // Empty, or only the holder's side inside: nothing is being taken.
            if (claimant < 0 || ((int)s.Holder < Sides && SameSide(claimant, (int)s.Holder)))
            {
                if (s.CaptureSeconds == 0 && s.Capturer == Faction.Border) return;
                s.CaptureSeconds = 0;
                s.Capturer = Faction.Border;
                em.SetComponentData(house, s);
                return;
            }

            if ((int)s.Capturer >= Sides || !SameSide(claimant, (int)s.Capturer))
                s.CaptureSeconds = 0;
            s.Capturer = (Faction)claimant;
            s.CaptureSeconds++;

            if (s.CaptureSeconds >= Cfg.captureSeconds)
            {
                var was = s.Holder;
                s.Holder = (Faction)claimant;
                s.Capturer = Faction.Border;
                s.CaptureSeconds = 0;
                em.SetComponentData(house, new FactionTag { Value = s.Holder });
                UnityEngine.Debug.Log($"[CountingHouse] {s.Holder} took a Counting House from {was}");
                if (s.Holder == GameSettings.LocalPlayerFaction || was == GameSettings.LocalPlayerFaction)
                    SimSignals.Alert(SimAlertKind.Monument);
                if (s.Holder == GameSettings.LocalPlayerFaction)
                    SimSignals.Notify(Loc.T("You took a Counting House. It pays you now."));
                else if (was == GameSettings.LocalPlayerFaction)
                    SimSignals.NotifyError(Loc.T("You lost a Counting House."));
            }
            em.SetComponentData(house, s);
        }

        private static void Pay(EntityManager em, Entity house)
        {
            var s = em.GetComponentData<CountingHouseState>(house);
            if ((int)s.Holder >= Sides) return;   // nobody holds it

            var rate = CountingHouse.PerMinute(s.Level);
            s.SuppliesAcc += rate.Supplies;
            s.IronAcc += rate.Iron;
            s.VeilstoneAcc += rate.Veilstone;
            var pay = Cost.Of(
                supplies: s.SuppliesAcc / 60,
                iron: s.IronAcc / 60,
                veilstone: s.VeilstoneAcc / 60);
            s.SuppliesAcc %= 60;
            s.IronAcc %= 60;
            s.VeilstoneAcc %= 60;
            if (!pay.IsZero) FactionEconomy.Add(em, s.Holder, pay, IncomeSource.BuildingPassive);

            float every = CountingHouse.SecondsPerReligionPoint(s.Level);
            if (every > 0f)
            {
                s.ReligionSeconds++;
                if (s.ReligionSeconds >= every)
                {
                    s.ReligionSeconds = 0;
                    // Religion POINTS (RP), not kill points: the Refund helper
                    // credits the RP balance directly.
                    FactionReligionPointsHelper.Grant(em, s.Holder, 1);
                }
            }
            em.SetComponentData(house, s);
        }
    }
}
