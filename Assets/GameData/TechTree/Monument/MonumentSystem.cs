// MonumentSystem.cs
// Conquering monuments and their religion trickle
// (docs/Design/Monuments_And_Petriarchy.md §1). Any one unit inside the ring for
// captureSeconds takes it; the curse neither blocks nor takes it. Lockstep:
// SimCadence clocks, and nothing depends on the order units are walked in.

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
        private const int Sides = 8;   // the eight factions; the curse does not count

        private static MonumentSystemConfig Cfg => MonumentSystemConfig.I;

        private SimCadence.Periodic _tick;
        private SimCadence.Periodic _points;
        private SimCadence.Periodic _secretLog;
        private const float SecretLogSeconds = 60f;

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
            float dt = SystemAPI.Time.DeltaTime;
            bool tick = _tick.Due(dt, TickSeconds);
            bool pay = _points.Due(dt, Cfg.secondsPerPoint);
            bool secretLog = _secretLog.Due(dt, SecretLogSeconds);
            if (!tick && !pay && !secretLog) return;

            using var monuments = Monument.All(em, Allocator.Temp);
            if (monuments.Length == 0) return;

            if (tick) Monument.TryPlaceShardroot(em);
            if (tick) Monument.TickHiddenShardroot(em);
            if (secretLog)
            {
                UnityEngine.Debug.Log($"[Clock] sim {SimClock.Now:F0}");
                Monument.LogHiddenShardroot(em);
            }
            if (tick) TickRings(em, monuments);
            if (pay) PayPoints(em, monuments);
        }

        private void TickRings(EntityManager em, NativeArray<Entity> monuments)
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
                System.Array.Clear(_inside, 0, Sides);
                for (int i = 0; i < ents.Length; i++)
                {
                    int side = (int)facs[i].Value;
                    if (side < 0 || side >= Sides || hps[i].Value <= 0) continue;
                    if (math.distancesq(xfs[i].Position.xz, at.xz) > r2) continue;
                    if (TransientState.Active<DeathAnimationState>(em, ents[i])) continue;
                    _inside[side]++;
                }
                Advance(em, monuments[m]);
            }
        }

        private static bool SameSide(int a, int b)
            => a == b || !Alliances.AreHostile((Faction)a, (Faction)b);

        private void Advance(EntityManager em, Entity monument)
        {
            var s = em.GetComponentData<MonumentState>(monument);

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
            if (claimant < 0 || SameSide(claimant, (int)s.Holder))
            {
                if (s.CaptureSeconds == 0 && s.Capturer == Faction.Border) return;
                s.CaptureSeconds = 0;
                s.Capturer = Faction.Border;
                em.SetComponentData(monument, s);
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
                em.SetComponentData(monument, new FactionTag { Value = s.Holder });
                UnityEngine.Debug.Log($"[Monument] {s.Holder} took the monument in territory {s.Territory} from {was}");
                if (s.Holder == GameSettings.LocalPlayerFaction || was == GameSettings.LocalPlayerFaction) SimSignals.Alert(SimAlertKind.Monument);
                if (s.Holder == GameSettings.LocalPlayerFaction)
                    SimSignals.Notify(Loc.T("You hold a monument. Explore its ruins, and it pays religion."));
                else if (was == GameSettings.LocalPlayerFaction)
                    SimSignals.NotifyError(Loc.T("You lost a monument."));
            }
            em.SetComponentData(monument, s);
        }

        private static void PayPoints(EntityManager em, NativeArray<Entity> monuments)
        {
            for (int m = 0; m < monuments.Length; m++)
            {
                var s = em.GetComponentData<MonumentState>(monuments[m]);
                if ((int)s.Holder >= Sides) continue;   // nobody holds it yet
                FactionReligionPointsHelper.AddKillPoints(em, s.Holder, 1);
            }
        }
    }
}
