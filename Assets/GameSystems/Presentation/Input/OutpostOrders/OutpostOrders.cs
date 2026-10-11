// OutpostOrders.cs
// The local player's Trading Outposts as a group (2026-10-11):
//   * SetModeForSelection — a trade button on the action panel sets EVERY
//     selected outpost of ours, not just the one whose panel is showing.
//     Levels rename the building, so the same-name fan-out the training
//     buttons use would miss an outpost of another level; this goes by
//     TradingOutpostTag.
//   * Collect / Focus — what the Trade menu (UI/Ingame/Panels/TradeMenu)
//     lists, and selecting one of them with the camera on it.
//
// Every order still travels through CommandRouter.IssueSetOutpostMode, one
// command per outpost, so lockstep sees exactly what a click on each would
// have sent.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;

namespace TheWaningBorder.Input
{
    public static class OutpostOrders
    {
        private static readonly ComponentType[] QT_Outposts =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        private static CachedEntityQuery QC_Outposts;

        private static bool IsOwnFinishedOutpost(EntityManager em, Entity e, Faction own)
            => em.Exists(e)
            && em.HasComponent<TradingOutpostTag>(e)
            && !em.HasComponent<UnderConstruction>(e)
            && em.HasComponent<FactionTag>(e)
            && em.GetComponentData<FactionTag>(e).Value == own;

        /// <summary>
        /// Set <paramref name="recipe"/> on <paramref name="primary"/> and on
        /// every other finished outpost of the local player in the selection.
        /// </summary>
        public static void SetModeForSelection(EntityManager em, Entity primary, TradeRecipe recipe)
        {
            CommandRouter.IssueSetOutpostMode(em, primary, recipe);
            var sel = SelectionSystem.CurrentSelection;
            if (sel == null || sel.Count <= 1) return;
            var own = GameSettings.LocalPlayerFaction;
            for (int i = 0; i < sel.Count; i++)
            {
                var e = sel[i];
                if (e == primary || !IsOwnFinishedOutpost(em, e, own)) continue;
                CommandRouter.IssueSetOutpostMode(em, e, recipe);
            }
        }

        /// <summary>The local player's finished, living Trading Outposts,
        /// by entity index (a stable order for the menu's rows).</summary>
        public static void Collect(EntityManager em, List<Entity> into)
        {
            into.Clear();
            var q = QC_Outposts.Get(em, QT_Outposts);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var own = GameSettings.LocalPlayerFaction;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != own) continue;
                if (em.HasComponent<Health>(ents[i]) && em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                into.Add(ents[i]);
            }
            into.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        /// <summary>Select <paramref name="outpost"/> alone and centre the
        /// camera on it.</summary>
        public static void Focus(EntityManager em, Entity outpost)
        {
            if (!em.Exists(outpost) || !em.HasComponent<LocalTransform>(outpost)) return;
            SelectionSystem.ClearSelection();
            SelectionSystem.AddToSelection(outpost);
            var pos = em.GetComponentData<LocalTransform>(outpost).Position;
            CameraController.FocusOn(new Vector3(pos.x, pos.y, pos.z));
        }
    }
}
