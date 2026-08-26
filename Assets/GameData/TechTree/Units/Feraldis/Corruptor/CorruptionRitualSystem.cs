// File: Assets/GameData/TechTree/Units/Feraldis/Corruptor/CorruptionRitualSystem.cs
// The Feraldis verb. Canon: docs/Design/Age_1_Feraldis.md § Corruptor.
//
// Three phases, deliberately mirroring PurificationRitualSystem so the two
// verbs feel like siblings:
//   1. APPROACH  — a Corruptor with CorruptCommand walks to the well.
//   2. CHANNEL   — CorruptionChannelTime seconds, interruptible by death,
//                  by being dragged out of range, or by the well changing
//                  state under it.
//   3. CRACK     — the well gains WellCorrupted for a fixed window. It can
//                  now be damaged and auto-acquired, and the curse spawns
//                  defenders at it (CorruptionDefenseSystem). If the army
//                  fails to kill it in time the well seals again.
//
// Unlike purify/pacify this claims NOTHING. Feraldis does not hold wells; it
// breaks them. The kill itself is handled by the existing
// NodeStateDeathInterceptSystem, which already flips a well to Destroyed
// with the killer's culture — and NodeVictorySystem already awards an
// instant win to a Feraldis faction that has all wells Destroyed at once.
//
// Phase 4 is an orphan sweep. PurificationRitualSystem has one and
// ConversionRitualSystem does not, which is a known bug there — a node whose
// ritualist dies mid-channel is locked out forever. This system has one.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.FeraldisConstants;

using TheWaningBorder.Core;
namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NodeStateReversionSystem))]
    public partial class CorruptionRitualSystem : SystemBase
    {
        // ── Diagnostics ─────────────────────────────────────────────────
        // Every ritual message in this stack went through TWBLog, which is
        // [Conditional("TWB_VERBOSE")] — so unless that symbol is defined the
        // calls are COMPILED OUT and a failing verb is completely silent. That
        // is why three separate matches showed hundreds of dispatches with no
        // way to tell why none of them landed.
        //
        // AILogger is unconditional and already per-faction, so the reason
        // lands in AI_<Faction>.log next to the dispatch that caused it.
        // Throttled: identical messages repeat at most every 10 s, so a
        // per-frame failure produces a readable trail instead of megabytes.
        private static readonly Dictionary<Faction, (string Msg, double At)> _lastDiag = new();
        private double _now;

        /// <summary>Ground-plane distance. Ritual adjacency is a question about
        /// footing, not elevation.</summary>
        private static float XZDistance(float3 a, float3 b)
            => math.distance(new float2(a.x, a.z), new float2(b.x, b.z));

        private void Diag(EntityManager em, Entity corruptor, string message)
        {
            Faction f = em.HasComponent<FactionTag>(corruptor)
                ? em.GetComponentData<FactionTag>(corruptor).Value
                : Faction.Border;

            if (_lastDiag.TryGetValue(f, out var prev)
                && prev.Msg == message && _now - prev.At < 10.0) return;

            _lastDiag[f] = (message, _now);
            TheWaningBorder.AI.AILogger.Log(f, "RITUAL", message);
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;
            _now = SystemAPI.Time.ElapsedTime;

            ApproachAndStart(em);
            Channel(em, dt);
            TickVulnerability(em, dt);
            SweepOrphans(em);
        }

        // ---------------------------------------------------------------
        // 1 + 2. Walk to the well, then start channelling.
        // ---------------------------------------------------------------
        private void ApproachAndStart(EntityManager em)
        {
            var starting = new NativeList<Entity>(Allocator.Temp);
            var startNode = new NativeList<Entity>(Allocator.Temp);
            var cancelling = new NativeList<Entity>(Allocator.Temp);

            foreach (var (cmd, xf, health, entity) in SystemAPI
                .Query<RefRO<CorruptCommand>, RefRO<LocalTransform>, RefRO<Health>>()
                .WithAll<CorruptorTag>()
                .WithNone<RitualState>()
                .WithEntityAccess())
            {
                var node = cmd.ValueRO.TargetNode;
                if (node == Entity.Null || !em.Exists(node)
                    || !em.HasComponent<BorderNodeState>(node)
                    || health.ValueRO.Value <= 0)
                {
                    Diag(em, entity, "approach cancelled: target gone or Corruptor dead");
                    cancelling.Add(entity);
                    continue;
                }

                var ns = em.GetComponentData<BorderNodeState>(node);
                // A well already broken needs no cracking.
                if (ns.State == NodeState.Destroyed)
                { Diag(em, entity, "approach cancelled: well already Destroyed"); cancelling.Add(entity); continue; }
                if (em.HasComponent<UnderConstruction>(node))
                { Diag(em, entity, "approach cancelled: well still UnderConstruction"); cancelling.Add(entity); continue; }
                // Already cracked, or someone else is mid-channel here.
                if (em.HasComponent<WellCorrupted>(node))
                { Diag(em, entity, "approach cancelled: well already WellCorrupted"); cancelling.Add(entity); continue; }
                if (em.HasComponent<ActiveRitualOnNode>(node))
                {
                    var act = em.GetComponentData<ActiveRitualOnNode>(node);
                    if (act.Ritualist != entity && em.Exists(act.Ritualist))
                    {
                        Diag(em, entity, $"approach cancelled: well claimed by another {act.Kind} " +
                                         $"ritual ({act.RitualistFaction})");
                        cancelling.Add(entity);
                        continue;
                    }
                }

                var nodePos = em.GetComponentData<LocalTransform>(node).Position;
                // XZ, not 3D. PurificationRitualSystem has always measured on
                // the ground plane and this system did not — an inconsistency
                // that bites hardest exactly where this map puts the well: on
                // a raised plinth, where a unit standing at the node's foot is
                // horizontally on top of it but metres below its origin. "Am I
                // next to the well" is a ground question; height is noise.
                float dist = XZDistance(xf.ValueRO.Position, nodePos);
                if (dist > CorruptRange)
                {
                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        // Without this component the approach is a silent
                        // no-op: the Corruptor keeps its order and stands
                        // still forever, which reads in-game as "the ritual
                        // does nothing".
                        Diag(em, entity, "approach STALLED: Corruptor has no DesiredDestination " +
                                         "component — it cannot walk to the well");
                        continue;
                    }
                    // Beside the node, not on it — the node's own footprint is
                    // impassable and a goal there yields an empty flow field,
                    // which is why Corruptors only ever channelled when
                    // something else shoved them into range (see
                    // RitualApproach).
                    em.SetComponentData(entity, new DesiredDestination
                    {
                        Position = RitualApproach.StandPoint(nodePos, xf.ValueRO.Position),
                        Has = 1,
                    });
                    continue;
                }

                Diag(em, entity, $"channel STARTING at {dist:0.0}m " +
                                 $"(range {CorruptRange}, cancel {CorruptCancelRange}, " +
                                 $"dy {math.abs(xf.ValueRO.Position.y - nodePos.y):0.0}m)");
                starting.Add(entity);
                startNode.Add(node);
            }

            for (int i = 0; i < cancelling.Length; i++)
                em.RemoveComponent<CorruptCommand>(cancelling[i]);

            for (int i = 0; i < starting.Length; i++)
            {
                var c = starting[i];
                var node = startNode[i];

                var faction = em.GetComponentData<FactionTag>(c).Value;
                em.AddComponentData(c, new RitualState
                {
                    Kind = RitualKind.ViolentExtraction,
                    TargetNode = node,
                    Progress = 0f,
                    TotalDuration = CorruptionChannelTime,
                });
                var claim = new ActiveRitualOnNode
                {
                    Ritualist = c,
                    Kind = RitualKind.ViolentExtraction,
                    RitualistFaction = faction,
                    RitualistCulture = Cultures.Feraldis,
                    DefenseSpawnTimer = 0f,
                    DefendersSpawned = 0,
                };
                if (em.HasComponent<ActiveRitualOnNode>(node)) em.SetComponentData(node, claim);
                else em.AddComponentData(node, claim);

                if (em.HasComponent<DesiredDestination>(c))
                    em.SetComponentData(c, new DesiredDestination { Has = 0 });

                // THE WAKING (canon §2.8): touching a well wakes THAT well —
                // it starts feeding the veil and never sleeps again. Fires on
                // channel START, not completion, so an interrupted attempt has
                // still armed the region: no safe probe, no take-backs.
                CurseAwakeningHelper.Wake(em, node, faction, SystemAPI.Time.ElapsedTime);

                SimSignals.Notify(
                    Loc.T("A Corruptor is defiling a well!"));
            }

            starting.Dispose();
            startNode.Dispose();
            cancelling.Dispose();
        }

        // ---------------------------------------------------------------
        // 2. Channel, and crack the well on completion.
        // ---------------------------------------------------------------
        private void Channel(EntityManager em, float dt)
        {
            var done = new NativeList<Entity>(Allocator.Temp);
            var doneNode = new NativeList<Entity>(Allocator.Temp);
            var doneFaction = new NativeList<Faction>(Allocator.Temp);
            var broken = new NativeList<Entity>(Allocator.Temp);

            foreach (var (ritual, xf, health, faction, entity) in SystemAPI
                .Query<RefRW<RitualState>, RefRO<LocalTransform>, RefRO<Health>, RefRO<FactionTag>>()
                .WithAll<CorruptorTag>()
                .WithEntityAccess())
            {
                if (ritual.ValueRO.Kind != RitualKind.ViolentExtraction) continue;

                var node = ritual.ValueRO.TargetNode;
                if (node == Entity.Null || !em.Exists(node) || health.ValueRO.Value <= 0)
                {
                    Diag(em, entity, $"channel BROKEN at {ritual.ValueRO.Progress:0.0}s: " +
                                     $"target gone or Corruptor dead (hp {health.ValueRO.Value})");
                    broken.Add(entity); continue;
                }

                var ns = em.GetComponentData<BorderNodeState>(node);
                if (ns.State == NodeState.Destroyed)
                {
                    Diag(em, entity, $"channel BROKEN at {ritual.ValueRO.Progress:0.0}s: " +
                                     "well went Destroyed mid-channel");
                    broken.Add(entity); continue;
                }

                var nodePos = em.GetComponentData<LocalTransform>(node).Position;
                float d = XZDistance(xf.ValueRO.Position, nodePos);
                if (d > CorruptCancelRange)
                {
                    // The commonest failure by far, and the hardest to see:
                    // the Corruptor gets nudged off the node by anything that
                    // touches its transform while it stands still. Log the
                    // vertical component separately — the distance test is 3D,
                    // so a well whose entity sits at a different height from
                    // the ground the unit stands on can break a channel that
                    // is horizontally on top of it.
                    Diag(em, entity, $"channel BROKEN at {ritual.ValueRO.Progress:0.0}s: " +
                                     $"drifted to {d:0.0}m (cancel {CorruptCancelRange}m, " +
                                     $"dy {math.abs(xf.ValueRO.Position.y - nodePos.y):0.0}m)");
                    broken.Add(entity); continue;
                }

                ritual.ValueRW.Progress += dt;
                if (ritual.ValueRO.Progress < ritual.ValueRO.TotalDuration) continue;

                done.Add(entity);
                doneNode.Add(node);
                doneFaction.Add(faction.ValueRO.Value);
            }

            for (int i = 0; i < broken.Length; i++)
            {
                var c = broken[i];
                var node = em.GetComponentData<RitualState>(c).TargetNode;
                Faction provoker = em.HasComponent<FactionTag>(c)
                    ? em.GetComponentData<FactionTag>(c).Value : Faction.Border;

                em.RemoveComponent<RitualState>(c);
                if (em.HasComponent<CorruptCommand>(c)) em.RemoveComponent<CorruptCommand>(c);
                if (em.Exists(node) && em.HasComponent<ActiveRitualOnNode>(node)
                    && em.GetComponentData<ActiveRitualOnNode>(node).Ritualist == c)
                    em.RemoveComponent<ActiveRitualOnNode>(node);

                // THE BACKLASH (canon §2.9). A channel that began and did not
                // finish wakes the well's fury. Armed here and NOT in the
                // approach-cancel path: you are punished for a rite you
                // started, not for walking up to a well someone else claimed.
                RitualBacklashSystem.Arm(em, node, provoker);
            }

            for (int i = 0; i < done.Length; i++)
            {
                var c = done[i];
                var node = doneNode[i];

                em.RemoveComponent<RitualState>(c);
                if (em.HasComponent<CorruptCommand>(c)) em.RemoveComponent<CorruptCommand>(c);
                if (em.HasComponent<ActiveRitualOnNode>(node))
                    em.RemoveComponent<ActiveRitualOnNode>(node);

                em.AddComponentData(node, new WellCorrupted
                {
                    Remaining = CorruptionVulnerableSeconds,
                    Corruptor = doneFaction[i],
                    WaveTimer = 0f,
                    DefendersSpawned = 0,
                    TotalSeconds = CorruptionVulnerableSeconds,
                    LastHealth = em.HasComponent<Health>(node)
                        ? em.GetComponentData<Health>(node).Value : 0,
                    HeldSeconds = 0f,
                });

                // Open it to ordinary target acquisition for the window.
                if (em.HasComponent<NodeNoAutoAcquire>(node))
                    em.RemoveComponent<NodeNoAutoAcquire>(node);
                if (em.HasComponent<NodeUntargetable>(node))
                    em.RemoveComponent<NodeUntargetable>(node);

                var p = em.GetComponentData<LocalTransform>(node).Position;
                SimSignals.Ping(p,
                    SimPingKind.Curse,
                    CorruptionVulnerableSeconds, big: true);
                SimSignals.Notify(
                    string.Format(Loc.T("A well lies open — {0}s to break it!"), (int)CorruptionVulnerableSeconds));
                TWBLog.Log($"[Corruption] well corrupted by {doneFaction[i]} " +
                           $"at ({p.x:0},{p.z:0}); vulnerable {CorruptionVulnerableSeconds}s.");
            }

            done.Dispose();
            doneNode.Dispose();
            doneFaction.Dispose();
            broken.Dispose();
        }

        // ---------------------------------------------------------------
        // 3. Run the vulnerability window down; reseal on expiry.
        // ---------------------------------------------------------------
        private void TickVulnerability(EntityManager em, float dt)
        {
            var expired = new NativeList<Entity>(Allocator.Temp);

            foreach (var (corrupt, entity) in SystemAPI
                .Query<RefRW<WellCorrupted>>()
                .WithEntityAccess())
            {
                ref var wc = ref corrupt.ValueRW;

                // HOLD THE WINDOW WHILE THE ASSAULT IS LANDING. A well is
                // 4000 HP; a flat 60 s meant it resealed mid-fight every
                // single time and the Feraldis victory condition was
                // unreachable in practice. So the clock only runs when the
                // well is NOT losing health — an army that is actually
                // breaking it gets to finish, an abandoned corruption still
                // times out. Capped by CorruptionMaxHeldSeconds so a single
                // unit chipping at it cannot hold a well open forever.
                int hp = em.HasComponent<Health>(entity)
                    ? em.GetComponentData<Health>(entity).Value : 0;
                bool underAssault = wc.LastHealth > 0 && hp < wc.LastHealth;
                wc.LastHealth = hp;

                if (underAssault && wc.HeldSeconds < CorruptionMaxHeldSeconds)
                    wc.HeldSeconds += dt;          // clock paused
                else
                    wc.Remaining -= dt;

                if (wc.Remaining <= 0f) expired.Add(entity);
            }

            for (int i = 0; i < expired.Length; i++)
            {
                var node = expired[i];
                em.RemoveComponent<WellCorrupted>(node);
                // NodeTargetabilitySystem re-seals it on its next pass; put
                // the auto-acquire block back immediately so nothing keeps
                // hitting it in the gap.
                if (em.Exists(node) && !em.HasComponent<NodeNoAutoAcquire>(node)
                    && em.HasComponent<BorderMainNodeTag>(node))
                    em.AddComponent<NodeNoAutoAcquire>(node);

                SimSignals.Notify(
                    Loc.T("The well seals itself — the corruption failed."));
            }
            expired.Dispose();
        }

        // ---------------------------------------------------------------
        // 4. Orphan sweep — a node whose Corruptor died mid-channel must not
        //    stay claimed forever (the bug ConversionRitualSystem still has).
        // ---------------------------------------------------------------
        private void SweepOrphans(EntityManager em)
        {
            var orphans = new NativeList<Entity>(Allocator.Temp);

            foreach (var (claim, entity) in SystemAPI
                .Query<RefRO<ActiveRitualOnNode>>()
                .WithEntityAccess())
            {
                // NOT Kind-filtered, deliberately. ApproachAndStart cancels on
                // a foreign claim of ANY kind, so a stale Purification or
                // Conversion claim locks Corruption out of that well forever —
                // and a Kind-filtered sweep would refuse to clear it. On a map
                // with a single well (Sundered Crown) that is the whole verb
                // path gone. Clearing a dead claim is safe regardless of which
                // verb left it: the owning system re-adds its own on the next
                // successful start.
                var r = claim.ValueRO.Ritualist;
                bool dead = r == Entity.Null || !em.Exists(r)
                            || !em.HasComponent<RitualState>(r)
                            || (em.HasComponent<Health>(r)
                                && em.GetComponentData<Health>(r).Value <= 0);
                if (dead) orphans.Add(entity);
            }

            for (int i = 0; i < orphans.Length; i++)
                em.RemoveComponent<ActiveRitualOnNode>(orphans[i]);
            orphans.Dispose();
        }
    }
}
