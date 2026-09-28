// CommandRouter.Formation.cs
// Partial class extension: AoE4-style formation group orders.
//
// A formation order fans one clicked destination out into per-unit slot
// destinations (FormationMoveCommandHelper.BuildPlan) and creates the
// persistent formation group whose virtual leader FormationGroupSystem
// advances every tick.
//
// Lockstep multiplayer: a formation order is ONE replicated command,
// FormationOrder (docs/Design/Navigation_And_Formations.md §2.12). It carries
// the unit list, destination, shape and attack-move flag; every peer runs the
// same FormationMoveCommandHelper.Execute on identical state, so every peer
// builds the same FormationGroup. It used to degrade to per-unit slot moves,
// which arrived in shape but never held it en route.
//
// Wire encoding (the tick datagram splits on '|' and ',', so neither may
// appear): the unit network ids sorted ascending, as base-36 deltas joined by
// ';'. Selections above MaxIdsPerCommand go out as several commands in the
// same tick — all but the last flagged "more follows" — which the executor
// accumulates (FormationOrderAccumulator) and runs once on the last.

using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>
        /// Issue a formation move to a group of units: type-ranked slots
        /// around the destination, slowest-member group speed, cohesion
        /// gate, virtual-leader travel.
        /// </summary>
        public static void IssueFormationMove(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, FormationShape shape,
            CommandSource source = CommandSource.LocalPlayer)
        {
            IssueFormationOrder(em, units, destination, shape, attackMove: false, source);
        }

        /// <summary>
        /// Formation attack-move: same layout and travel rules; members
        /// auto-engage enemies encountered (and step out of rank while they
        /// fight — see FormationGroupSystem).
        /// </summary>
        public static void IssueFormationAttackMove(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, FormationShape shape,
            CommandSource source = CommandSource.LocalPlayer)
        {
            IssueFormationOrder(em, units, destination, shape, attackMove: true, source);
        }

        private static void IssueFormationOrder(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, FormationShape shape, bool attackMove, CommandSource source)
        {
            if (units == null || units.Count == 0) return;
            if (ShouldDropCommand(source)) return;

            // Per-unit controllability filter (same rule as IssueMove).
            var eligible = new List<Entity>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == Entity.Null || !em.Exists(u)) continue;
                if (IsBlockedByNotControllable(em, u, source)) continue;
                if (eligible.Contains(u)) continue;
                eligible.Add(u);
            }

            // AI AUDIT TRAIL (2026-08-31): every group order, one line — so
            // "why did that army march there" is always answerable from the
            // log rather than from watching the replay.
            if (source == CommandSource.AI && eligible.Count > 0
                && em.HasComponent<FactionTag>(eligible[0]))
                TheWaningBorder.AI.AILogger.Log(
                    em.GetComponentData<FactionTag>(eligible[0]).Value, "CMD",
                    $"formation {(attackMove ? "attack-move" : "move")} x{eligible.Count} " +
                    $"-> ({destination.x:0},{destination.z:0})");
            if (eligible.Count == 0) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueFormationOrderForLockstep(em, eligible, destination, shape, attackMove);
                return;
            }

            FormationMoveCommandHelper.Execute(em, eligible, destination, shape, attackMove);
        }

        // ═══════════════════════════════════════════════════════════════
        // LOCKSTEP: FormationOrder
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Unit ids per FormationOrder command. ~5-7 base-36 chars
        /// each, so 60 keeps one command near 400 bytes — comfortably inside
        /// the 1200-byte datagram budget, so a big army never fragments.</summary>
        private const int MaxIdsPerCommand = 60;

        private const int FormationFlagAttackMove = 1 << 8;
        private const int FormationFlagMoreFollows = 1 << 9;

        private static void QueueFormationOrderForLockstep(EntityManager em, List<Entity> eligible,
            float3 destination, FormationShape shape, bool attackMove)
        {
            // Every member must be networked — a unit the other peers cannot
            // resolve would silently change the census, and with it the
            // whole layout, on their side only.
            var ids = new List<int>(eligible.Count);
            for (int i = 0; i < eligible.Count; i++)
            {
                int id = GetNetworkId(em, eligible[i]);
                if (id <= 0)
                {
                    MayExecuteLocally(em, eligible[i], "FormationOrder member");
                    continue;
                }
                ids.Add(id);
            }
            if (ids.Count == 0) return;
            ids.Sort();

            int faction = em.HasComponent<FactionTag>(eligible[0])
                ? (int)em.GetComponentData<FactionTag>(eligible[0]).Value : 0;

            int flags = (int)(byte)shape | (attackMove ? FormationFlagAttackMove : 0);
            for (int start = 0; start < ids.Count; start += MaxIdsPerCommand)
            {
                int end = math.min(ids.Count, start + MaxIdsPerCommand);
                bool more = end < ids.Count;
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.FormationOrder,
                    EntityNetworkId = faction,
                    TargetPosition = destination,
                    TargetEntityId = flags | (more ? FormationFlagMoreFollows : 0),
                    SecondaryTargetId = ids.Count,
                    // The delta base restarts per command, so each one decodes
                    // on its own.
                    BuildingId = EncodeIds(ids, start, end),
                });
            }
        }

        /// <summary>Base-36 deltas of an ascending id run, ';'-separated.
        /// Contains only [0-9a-z;] — never the '|' / ',' the tick datagram
        /// splits on.</summary>
        internal static string EncodeIds(List<int> sortedIds, int start, int end)
        {
            var sb = new StringBuilder((end - start) * 6);
            int prev = 0;
            for (int i = start; i < end; i++)
            {
                if (i > start) sb.Append(';');
                AppendBase36(sb, sortedIds[i] - prev);
                prev = sortedIds[i];
            }
            return sb.ToString();
        }

        internal static bool DecodeIds(string s, List<int> into)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int prev = 0, cur = 0;
            bool any = false;
            for (int i = 0; i <= s.Length; i++)
            {
                if (i == s.Length || s[i] == ';')
                {
                    if (!any) return false;
                    prev += cur;
                    into.Add(prev);
                    cur = 0; any = false;
                    continue;
                }
                char c = s[i];
                int d = c >= '0' && c <= '9' ? c - '0'
                      : c >= 'a' && c <= 'z' ? c - 'a' + 10 : -1;
                if (d < 0) return false;
                cur = cur * 36 + d;
                any = true;
            }
            return true;
        }

        private static void AppendBase36(StringBuilder sb, int v)
        {
            if (v <= 0) { sb.Append('0'); return; }
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            int startLen = sb.Length;
            while (v > 0) { sb.Insert(startLen, digits[v % 36]); v /= 36; }
        }

        /// <summary>
        /// Executor side of FormationOrder, called by LockstepManager for each
        /// command in tick order. Continuation commands (more-follows) only
        /// accumulate; the last one runs the order. <paramref name="resolve"/>
        /// maps a network id to the entity on this peer.
        /// </summary>
        public static void ExecuteFormationOrder(EntityManager em, LockstepCommand cmd,
            System.Func<int, Entity> resolve)
        {
            var acc = FormationOrderAccumulator.For(cmd.PlayerIndex, cmd.Tick);
            if (!DecodeIds(cmd.BuildingId, acc.Ids))
            {
                UnityEngine.Debug.LogWarning("[CommandRouter] FormationOrder: malformed unit list — dropped.");
                FormationOrderAccumulator.Reset(cmd.PlayerIndex);
                return;
            }
            if ((cmd.TargetEntityId & FormationFlagMoreFollows) != 0) return;

            var ids = new List<int>(acc.Ids);
            FormationOrderAccumulator.Reset(cmd.PlayerIndex);

            // Ascending network id on every peer, so BuildPlan's claim order
            // and tie-breaks agree everywhere.
            ids.Sort();
            var units = new List<Entity>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0 && ids[i] == ids[i - 1]) continue;
                var e = resolve(ids[i]);
                if (e != Entity.Null && em.Exists(e)) units.Add(e);
            }
            if (units.Count == 0) return;

            var shape = (FormationShape)(byte)(cmd.TargetEntityId & 0xFF);
            bool attackMove = (cmd.TargetEntityId & FormationFlagAttackMove) != 0;
            FormationMoveCommandHelper.Execute(em, units, cmd.TargetPosition, shape, attackMove);
        }

        /// <summary>Per-player buffer for a formation order split over several
        /// commands of ONE tick. Keyed by tick too, so a lost tail can never
        /// leak ids into a later order.</summary>
        private static class FormationOrderAccumulator
        {
            internal sealed class Pending
            {
                public int Tick;
                public readonly List<int> Ids = new List<int>();
            }

            private static readonly Dictionary<int, Pending> _byPlayer = new Dictionary<int, Pending>();

            public static Pending For(int player, int tick)
            {
                if (!_byPlayer.TryGetValue(player, out var p))
                    _byPlayer[player] = p = new Pending { Tick = tick };
                if (p.Tick != tick) { p.Ids.Clear(); p.Tick = tick; }
                return p;
            }

            public static void Reset(int player)
            {
                if (_byPlayer.TryGetValue(player, out var p)) p.Ids.Clear();
                _byPlayer.Remove(player);
            }
        }
    }
}
