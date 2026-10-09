// IdleWorkerCycler.cs
// The local player's INACTIVE builders, one at a time (2026-10-09). Shared by
// the idle-worker hotkey (HotkeyInput) and the HUD's idle-builder button
// (UI/Ingame/Panels/IdleBuilderButton), so both cycle the same list the same
// way.
//
// Inactive = a living Worker (CanBuild) of the local faction with no build or
// repair order and not walking anywhere. The list is ordered by entity index,
// so repeated presses advance through the same order instead of the chunk
// order of the moment.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Input
{
    public static class IdleWorkerCycler
    {
        private static readonly ComponentType[] QT_Workers =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<CanBuild>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Workers;

        private static readonly List<Entity> _idle = new List<Entity>();

        /// <summary>The worker selected last; the next press picks the first
        /// idle worker after it in entity-index order.</summary>
        private static int _lastIndex = -1;

        /// <summary>Fill <see cref="_idle"/> with the local player's inactive
        /// builders, by entity index.</summary>
        private static void Collect(EntityManager em)
        {
            _idle.Clear();
            var q = QC_Workers.Get(em, QT_Workers);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var local = GameSettings.LocalPlayerFaction;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (facs[i].Value != local) continue;
                if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;
                if (em.HasComponent<BuildOrder>(e) || em.HasComponent<RepairOrder>(e)) continue;
                if (TransientState.Active<MoveCommand>(em, e)) continue;
                _idle.Add(e);
            }
            _idle.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        /// <summary>How many builders stand idle right now.</summary>
        public static int CountIdle(EntityManager em)
        {
            Collect(em);
            return _idle.Count;
        }

        /// <summary>
        /// Select the next idle builder (alone) and centre the camera on it.
        /// False when there is none.
        /// </summary>
        public static bool SelectNext(EntityManager em)
        {
            Collect(em);
            if (_idle.Count == 0) return false;

            Entity pick = _idle[0];
            for (int i = 0; i < _idle.Count; i++)
                if (_idle[i].Index > _lastIndex) { pick = _idle[i]; break; }
            _lastIndex = pick.Index;

            SelectionSystem.ClearSelection();
            SelectionSystem.AddToSelection(pick);
            var pos = em.GetComponentData<LocalTransform>(pick).Position;
            CameraController.FocusOn(new Vector3(pos.x, pos.y, pos.z));
            return true;
        }
    }
}
