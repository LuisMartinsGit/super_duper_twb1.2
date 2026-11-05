// MonumentSystem.cs
// The capture meter and the religion trickle of every monument
// (docs/Design/Monuments_And_Petriarchy.md §1). Lockstep: SimCadence clocks and
// sums only, so no result depends on the order the units are walked in.

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
    public partial class MonumentSystem : SystemBase
    {
        private const float TickSeconds = 1f;
        private const int Sides = 9;   // eight factions + the curse

        private static MonumentSystemConfig Cfg => MonumentSystemConfig.I;

        private SimCadence.Periodic _tick;
        private SimCadence.Periodic _points;

        private readonly int[] _weight = new int[Sides];
        private readonly int[] _sideTotal = new int[Sides];

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
            float dt = SystemAPI.Time.DeltaTime;
            bool tick = _tick.Due(dt, TickSeconds);
            bool pay = _points.Due(dt, Cfg.secondsPerPoint);
            if (!tick && !pay) return;

            using var monuments = Monument.All(em, Allocator.Temp);
            if (monuments.Length == 0) return;

            if (tick) TickMeters(em, monuments);
            if (pay) PayPoints(em, monuments);
        }

        private void TickMeters(EntityManager em, NativeArray<Entity> monuments)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            float r2 = Cfg.captureRadius * Cfg.captureRadius;

            for (int m = 0; m < monuments.Length; m++)
            {
                var at = em.GetComponentData<LocalTransform>(monuments[m]).Position;
                System.Array.Clear(_weight, 0, Sides);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (hps[i].Value <= 0) continue;
                    if (math.distancesq(xfs[i].Position.xz, at.xz) > r2) continue;
                    if (!Counts(em, ents[i])) continue;
                    int side = (int)facs[i].Value;
                    if (side < 0 || side >= Sides) continue;
                    _weight[side] += TheWaningBorder.Systems.World.TerritoryClaimSystem.PopOf(em, ents[i], side);
                }
                Push(em, monuments[m]);
            }
        }

        /// <summary>A soldier: the territory meter's test, at one point.</summary>
        private static bool Counts(EntityManager em, Entity e)
        {
            var cls = em.GetComponentData<UnitTag>(e).Class;
            if (cls == UnitClass.Worker || cls == UnitClass.Economy) return false;
            return !em.HasComponent<CaravanTag>(e)
                && !em.HasComponent<ConscriptedTag>(e)
                && !em.HasComponent<TemporarySummon>(e)
                && !em.HasComponent<DeathAnimationState>(e);
        }

        private static bool SameSide(int a, int b)
            => a == b || !Alliances.AreHostile((Faction)a, (Faction)b);

        /// <summary>The tug of war: the stronger side, allies summed, moves the
        /// meter by the difference. Ties and an empty circle leave it alone.</summary>
        private void Push(EntityManager em, Entity monument)
        {
            int lead = -1, leadTotal = 0;
            for (int a = 0; a < Sides; a++)
            {
                _sideTotal[a] = 0;
                if (_weight[a] <= 0) continue;
                for (int b = 0; b < Sides; b++)
                    if (_weight[b] > 0 && SameSide(a, b)) _sideTotal[a] += _weight[b];
                if (_sideTotal[a] > leadTotal
                    || (_sideTotal[a] == leadTotal && lead >= 0 && _weight[a] > _weight[lead]))
                {
                    lead = a;
                    leadTotal = _sideTotal[a];
                }
            }
            if (lead < 0) return;

            int opposed = 0;
            for (int a = 0; a < Sides; a++)
                if (_sideTotal[a] > opposed && !SameSide(a, lead)) opposed = _sideTotal[a];
            int net = leadTotal - opposed;
            if (net <= 0) return;

            var s = em.GetComponentData<MonumentState>(monument);
            int capacity = Cfg.capturePopSeconds;
            if (SameSide(lead, (int)s.Holder))
            {
                s.Meter = math.min(capacity, s.Meter + net);
            }
            else
            {
                s.Meter -= net;
                if (s.Meter <= 0)
                {
                    var was = s.Holder;
                    s.Holder = (Faction)lead;
                    s.Meter = 0;
                    em.SetComponentData(monument, new FactionTag { Value = s.Holder });
                    UnityEngine.Debug.Log($"[Monument] {s.Holder} took the monument in territory {s.Territory} from {was}");
                    if (s.Holder == GameSettings.LocalPlayerFaction)
                        SimSignals.Notify(Loc.T("You hold a monument. Explore its ruins, and it pays religion."));
                    else if (was == GameSettings.LocalPlayerFaction)
                        SimSignals.NotifyError(Loc.T("You lost a monument."));
                }
            }
            em.SetComponentData(monument, s);
        }

        private static void PayPoints(EntityManager em, NativeArray<Entity> monuments)
        {
            for (int m = 0; m < monuments.Length; m++)
            {
                var s = em.GetComponentData<MonumentState>(monuments[m]);
                if ((int)s.Holder >= 8) continue;   // the curse is paid nothing
                FactionReligionPointsHelper.AddKillPoints(em, s.Holder, 1);
            }
        }
    }
}
