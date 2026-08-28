// TerritoryCorruptionSystem.cs
// The mid-game curse escalation, re-triggered for the territory economy.
//
// WHY THIS FILE EXISTS
// --------------------
// The "a veilstone patch wakes a blight pocket" loop used to live inside
// VeilstoneMiningSystem: draining the LAST bud of a patch pushed a
// PendingCorruption, which BlightPocketSystem raised into a curse node a few
// seconds later. That system was the ONLY in-match producer of
// PendingCorruption in the whole game.
//
// docs/Design/Regions.md §4 deletes worker gathering, so nothing depletes a
// patch any more and that trigger can never fire again. Deleting the mining
// system without this would have taken the entire mid-game curse escalation
// with it — the purple telegraph ping, the announcement and the pocket — and
// produced no compile error anywhere. It would simply have gone quiet.
//
// WHAT IS PRESERVED
// -----------------
// The old trigger's design intent, quoted from the code it replaces:
//
//   * "the mid game [is] a curse players CHOOSE to create"
//   * suppressed ground stays immune — "the opening should not punish you"
//   * "it puts the guaranteed pocket out on the CONTESTED patches you have to
//     leave home for"
//
// The new trigger keeps all three by reading TENURE instead of depletion:
// holding a veilstone-bearing territory that is NOT your home ground wakes its
// pocket after a while. You still choose it (by claiming contested veilstone),
// home is still immune (a territory with your Hall in it never wakes), and the
// risk still lives on the ground you had to leave home to take.
//
// The telegraph is unchanged, so the player-facing experience — ping, notice,
// CorruptionTelegraphSeconds of warning — is exactly what it was.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class TerritoryCorruptionSystem : SystemBase
    {
        /// <summary>Seconds a contested veilstone territory must be held before
        /// its pocket wakes. Long enough that taking the ground is a commitment
        /// rather than a coin flip.</summary>
        private const float TenureSeconds = 120f;

        private const float CheckInterval = 5f;

        private float _timer;

        /// <summary>Territory -> (owner, seconds held). Reset when the owner
        /// changes, so losing and retaking ground restarts the clock.</summary>
        private readonly System.Collections.Generic.Dictionary<int, (int owner, float held)> _tenure = new();

        /// <summary>Territories whose pocket has already been woken — once per
        /// territory per match, like the old once-per-patch rule.</summary>
        private readonly System.Collections.Generic.HashSet<int> _fired = new();

        protected override void OnUpdate()
        {
            if (!RegionMap.Ready) return;

            _timer -= SystemAPI.Time.DeltaTime;
            if (_timer > 0f) return;
            _timer = CheckInterval;

            var em = EntityManager;

            var registryQ = em.CreateEntityQuery(ComponentType.ReadWrite<BlightPocket>());
            if (registryQ.IsEmptyIgnoreFilter) { registryQ.Dispose(); return; }
            using var registries = registryQ.ToEntityArray(Allocator.Temp);
            registryQ.Dispose();
            if (registries.Length == 0) return;

            double now = SystemAPI.Time.ElapsedTime;
            var pending = em.GetBuffer<PendingCorruption>(registries[0]);

            // Where the veilstone is. Positions, not just territory ids: the
            // pocket rises AT the node, the way it used to rise at the drained
            // bud.
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var nodes = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            q.Dispose();

            for (int i = 0; i < nodes.Length; i++)
            {
                float3 pos = nodes[i].Position;
                int t = RegionMap.RegionAt(pos.x, pos.z);
                if (t == RegionMap.None || _fired.Contains(t)) continue;

                int owner = TerritoryOwnership.OwnerOf(t);
                if (owner < 0) { _tenure.Remove(t); continue; }   // unheld ground never wakes

                // Home ground is immune — the same rule the old trigger had via
                // the Hall hearth ring, expressed in territory terms now.
                if (IsHomeTerritory(em, t, (Faction)owner)) { _tenure.Remove(t); continue; }

                if (!_tenure.TryGetValue(t, out var rec) || rec.owner != owner)
                    rec = (owner, 0f);

                rec.held += CheckInterval;
                _tenure[t] = rec;

                if (rec.held < TenureSeconds) continue;

                _fired.Add(t);
                pending.Add(new PendingCorruption
                {
                    Pos = pos,
                    At = now + TheWaningBorder.Core.Config.VeilCrustConstants.CorruptionTelegraphSeconds,
                });
                SimSignals.Ping(pos, SimPingKind.Curse, 15f);
                SimSignals.Notify(string.Format(
                    Loc.T("A veilstone node is corrupting — the curse rises in {0}s!"),
                    (int)TheWaningBorder.Core.Config.VeilCrustConstants.CorruptionTelegraphSeconds));
                TWBLog.Log($"[TerritoryCorruption] territory {t} held {TenureSeconds:0}s — " +
                           $"pocket waking at {pos}.");
            }

            nodes.Dispose();
        }

        /// <summary>A territory containing one of this faction's Halls is home.</summary>
        private bool IsHomeTerritory(EntityManager em, int territory, Faction f)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Allocator.Temp);
            bool home = false;
            for (int i = 0; i < ents.Length && !home; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != f) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                home = RegionMap.RegionAt(p.x, p.z) == territory;
            }
            ents.Dispose();
            q.Dispose();
            return home;
        }
    }
}
