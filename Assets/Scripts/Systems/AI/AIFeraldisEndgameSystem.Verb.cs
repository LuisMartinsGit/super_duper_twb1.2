// AIFeraldisEndgameSystem.Verb.cs
// The Corruptor verb: well selection, escort commit, patience timeout.
// Partial of AIFeraldisEndgameSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIFeraldisEndgameSystem : ISystem
    {
        // ---------------------------------------------------------------
        // THE VERB — crack a well, then smash it.
        // ---------------------------------------------------------------
        private static void TryRunTheVerb(EntityManager em, Entity brainEntity,
            Faction faction, float3 hallPos, float now)
        {
            // 1. A well already cracked open? Everything goes at it NOW —
            //    the window is short and the curse is spawning defenders.
            if (TryFindCorruptedWell(em, out Entity cracked, out float3 crackedPos))
            {
                CommitArmy(em, faction, cracked, crackedPos, attackTheWell: true);
                return;
            }

            // 2. Otherwise: find an idle Corruptor and send it at a well.
            Entity corruptor = Entity.Null;
            bool anyCorruptor = false;
            var cq = em.CreateEntityQuery(
                ComponentType.ReadOnly<CorruptorTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var ents = cq.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                    anyCorruptor = true;
                    if (em.HasComponent<RitualState>(ents[i])) continue;
                    if (em.HasComponent<CorruptCommand>(ents[i])) continue;
                    corruptor = ents[i];
                    break;
                }
            }

            // 3. No Corruptor at all -> train one (once; the queue check is
            //    the anti-money-furnace guard the Alanthor sibling uses too).
            if (!anyCorruptor)
            {
                if (!IsUnitQueued(em, faction, "Feraldis_Iconoclast"))
                    TryQueueAtTemple(em, faction, "Feraldis_Iconoclast");
                return;
            }
            if (corruptor == Entity.Null) return;   // busy channelling

            if (!TryPickWell(em, faction, hallPos, out Entity well, out float3 wellPos)) return;

            // ESCORT FIRST, THEN THE RITUALIST. The 2026-08-06 match trained
            // NINE Corruptors and dispatched them 34 times over 19 minutes
            // without landing a single corruption — they were walking alone
            // at 3.2 speed across a map that was 61 % curse, and dying before
            // arrival. The army now moves out first so the lane is contested
            // by the time the 300-supply ritualist follows it.
            int escort = CommitArmy(em, faction, well, wellPos, attackTheWell: false);

            // Prefer an escort — but never wait forever for one. See
            // MaxEscortWaitSeconds: an AI that cannot field four spare units
            // used to sit on "escort 0/4" until the match ended, which is a
            // guaranteed loss on a map whose only victory path is the verb.
            var tick = em.GetComponentData<AIFeraldisTickState>(brainEntity);
            bool escortReady = escort >= MinEscortBeforeDispatch;

            if (!escortReady)
            {
                if (tick.CorruptorHeldSince <= 0f)
                {
                    tick.CorruptorHeldSince = now;
                    em.SetComponentData(brainEntity, tick);
                }

                float waited = now - tick.CorruptorHeldSince;
                if (waited < MaxEscortWaitSeconds)
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Corruptor held: escort {escort}/{MinEscortBeforeDispatch} " +
                        $"({waited:0}s of {MaxEscortWaitSeconds:0}s)");
                    return;
                }

                AILogger.Log(faction, "STRATEGY",
                    $"Corruptor dispatched UNESCORTED after {waited:0}s waiting on an escort " +
                    $"that never came (escort {escort}) — an unescorted try beats never trying");
            }

            // Reset the patience clock: either we have an escort now, or we
            // just spent it. The next hold starts fresh.
            if (tick.CorruptorHeldSince > 0f)
            {
                tick.CorruptorHeldSince = 0f;
                em.SetComponentData(brainEntity, tick);
            }

            CommandRouter.IssueCorrupt(em, corruptor, well, CommandSource.AI);
            if (escortReady)
                AILogger.Log(faction, "STRATEGY",
                    $"Corruptor dispatched to well at ({wellPos.x:0},{wellPos.z:0}) with escort {escort}");
        }

        private static bool TryFindCorruptedWell(EntityManager em, out Entity well, out float3 pos)
        {
            well = Entity.Null; pos = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<WellCorrupted>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            if (ents.Length == 0) return false;
            well = ents[0];
            pos = em.GetComponentData<LocalTransform>(well).Position;
            return true;
        }

        /// <summary>Nearest living well this faction has actually revealed.</summary>
        private static bool TryPickWell(EntityManager em, Faction faction, float3 hallPos,
            out Entity best, out float3 bestPos)
        {
            best = Entity.Null; bestPos = default;
            float bestD = float.MaxValue;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<BorderNodeState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var s = em.GetComponentData<BorderNodeState>(e).State;
                // A dead well needs no corrupting; it is already counted.
                if (s == NodeState.Destroyed) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<ActiveRitualOnNode>(e)) continue;
                if (em.HasComponent<WellCorrupted>(e)) continue;

                var p = em.GetComponentData<LocalTransform>(e).Position;
                if (fog != null && !fog.IsRevealed(faction, new UnityEngine.Vector3(p.x, 0f, p.z)))
                    continue;

                float d = math.distancesq(p, hallPos);
                if (d < bestD) { bestD = d; best = e; bestPos = p; }
            }
            return best != Entity.Null;
        }

        /// <summary>
        /// Send the standing army to the well. When the well is already
        /// cracked they get an explicit ATTACK order on it — wells are never
        /// auto-acquired unless corrupted, and an explicit order is also the
        /// only path CommandRouter allows against a well.
        /// </summary>
        private static int CommitArmy(EntityManager em, Faction faction,
            Entity well, float3 wellPos, bool attackTheWell)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);

            int sent = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                var u = ents[i];
                if (em.GetComponentData<FactionTag>(u).Value != faction) continue;

                var cls = em.GetComponentData<UnitTag>(u).Class;
                if (cls != UnitClass.Melee && cls != UnitClass.Ranged && cls != UnitClass.Siege)
                    continue;
                // Never drag the Corruptor itself, or uncontrollable raiders.
                if (em.HasComponent<CorruptorTag>(u)) continue;
                if (em.HasComponent<NotControllableTag>(u)) continue;
                if (em.HasComponent<UserMoveOrder>(u)) continue;

                var p = em.GetComponentData<LocalTransform>(u).Position;
                if (math.distance(p, wellPos) > AssaultRange) continue;

                if (attackTheWell)
                {
                    CommandRouter.IssueAttack(em, u, well, CommandSource.AI);
                }
                else
                {
                    // STAND OFF — do not pile onto the well itself.
                    //
                    // Sending every escort to the exact wellPos is what has
                    // been killing this AI's own verb. A channelling ritualist
                    // sits with DesiredDestination.Has = 0, and SteeringSystem
                    // keeps separation "at full strength so units still push
                    // apart inside the cluster" — so a dozen escorts
                    // converging on the ritualist's tile shove it radially
                    // outward, every 5 s re-commit ratcheting it further, with
                    // nothing pulling it back. Past CorruptCancelRange (10 m)
                    // the channel breaks and the whole approach restarts.
                    //
                    // The 2026-08-07 8-player match measured it cleanly across
                    // 73 dispatches: mean gap between re-dispatches was 18.5 s
                    // at escort 12+ (63 samples, channel never survived its
                    // 40 s), 35.2 s at escort 8-11, and 123 s at escort < 8 —
                    // i.e. the verb only ever landed once the bodyguard got
                    // thin enough to stop trampling it.
                    //
                    // A ring at EscortStandoffRadius is also just the correct
                    // bodyguard shape: a screen intercepts what comes at the
                    // ritualist instead of standing on top of it.
                    float3 slot = AIEndgameCommon.EscortSlot(
                        wellPos, sent, AssaultArmySize, AIEndgameCommon.EscortStandoffRadius);
                    CommandRouter.IssueAttackMove(em, u, slot, CommandSource.AI);
                }

                if (++sent >= AssaultArmySize) break;
            }
            return sent;
        }
    }
}
