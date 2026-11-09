// CommandRouter.StopExtracting.cs
// STOP EXTRACTING (docs/Design/Veilstone_Economy.md §3.5, 2026-11-09, decision 42).
//
//   SetExtractorStopped — tell an extractor (the Gatherer's Hut line, the Mine,
//                         the Veilstone Mine) to stop drawing on its node, or
//                         to start again. The building stands, holds, locks
//                         and keeps paying its surveys; its node pays as an
//                         unworked node (or nothing, with a post beside it).
//
// It replicates: whether a node is drawn on decides what the faction earns and
// when the node collapses, so a peer that missed it diverges on the next
// income tick. The issuing side never mutates the world itself. It reuses the
// lockstep slot of the retired SetOutpostMode order (53).

using Unity.Entities;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>True for a building that works a resource node and so can
        /// be told to stop: the Gatherer's Hut line (not a Raider Camp), the
        /// Mine, the Veilstone Mine.</summary>
        public static bool IsStoppableExtractor(EntityManager em, Entity e)
        {
            if (e == Entity.Null || !em.Exists(e)) return false;
            if (em.HasComponent<GathererHutTag>(e)) return !em.HasComponent<RaiderCampTag>(e);
            return em.HasComponent<MineTag>(e) || em.HasComponent<VeilstoneMineTag>(e);
        }

        /// <summary>Is this extractor stopped?</summary>
        public static bool IsExtractorStopped(EntityManager em, Entity e)
            => e != Entity.Null && em.Exists(e) && em.HasComponent<ExtractorStopped>(e);

        /// <summary>Stop (<paramref name="stopped"/> true) or resume an extractor.</summary>
        public static void IssueSetExtractorStopped(EntityManager em, Entity extractor, bool stopped,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (!IsStoppableExtractor(em, extractor)) return;
            if (IsBlockedByNotControllable(em, extractor, source)) return;

            int payload = stopped ? 1 : 0;
            if (ShouldQueueForLockstep(source))
            {
                int id = GetNetworkId(em, extractor);
                if (id <= 0)
                {
                    if (!MayExecuteLocally(em, extractor, "SetExtractorStopped")) return;
                    SetExtractorStoppedDirect(em, extractor, payload);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SetExtractorStopped,
                    EntityNetworkId = id,
                    TargetEntityId = payload,   // 1 stop, 0 resume
                });
            }
            else
            {
                SetExtractorStoppedDirect(em, extractor, payload);
            }
        }

        /// <summary>The one place an extractor is stopped or resumed (every
        /// peer runs it from the same payload: 1 stop, 0 resume).</summary>
        public static void SetExtractorStoppedDirect(EntityManager em, Entity extractor, int payload)
        {
            if (!IsStoppableExtractor(em, extractor)) return;
            bool has = em.HasComponent<ExtractorStopped>(extractor);
            if (payload != 0 && !has) em.AddComponent<ExtractorStopped>(extractor);
            else if (payload == 0 && has) em.RemoveComponent<ExtractorStopped>(extractor);
        }
    }
}
