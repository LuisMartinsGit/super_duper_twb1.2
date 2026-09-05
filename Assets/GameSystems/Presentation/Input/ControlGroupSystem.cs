// ControlGroupSystem.cs
// Stores 9 control groups (Ctrl+1-9 to save, 1-9 to recall, double-tap to center camera)
// Part of: Input/

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Manages control groups for the local player.
    /// Ctrl+N saves current selection to group N.
    /// Shift+N adds current selection to group N.
    /// N recalls group N. Double-tap N centers camera on group.
    /// </summary>
    public class ControlGroupSystem : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════════

        private static ControlGroupSystem _instance;

        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════

        private const float DoubleTapThreshold = 0.3f;

        // ═══════════════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════════════

        private readonly List<Entity>[] _groups = new List<Entity>[9];
        private readonly float[] _lastPressTime = new float[9];

        private EntityWorld _world;
        private EntityManager _em;

        // ═══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════

        void Awake()
        {
            _instance = this;

            for (int i = 0; i < 9; i++)
            {
                _groups[i] = new List<Entity>();
                _lastPressTime[i] = -1f;
            }

            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated) return;

            if (_em.Equals(default(EntityManager)))
                _em = _world.EntityManager;

            CleanAllGroups();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC STATIC API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ctrl+N: Replace group with current selection.
        /// </summary>
        public static void AssignGroup(int groupIndex)
        {
            if (_instance == null) return;
            _instance.AssignGroupInternal(groupIndex);
        }

        /// <summary>
        /// Shift+N: Add current selection to group without duplicates.
        /// </summary>
        public static void AddToGroup(int groupIndex)
        {
            if (_instance == null) return;
            _instance.AddToGroupInternal(groupIndex);
        }

        /// <summary>
        /// N press: Recall group (replace selection) and optionally center camera on double-tap.
        /// Returns true if this was a double-tap.
        /// </summary>
        public static bool HandleRecallOrCenter(int groupIndex)
        {
            if (_instance == null) return false;
            return _instance.HandleRecallOrCenterInternal(groupIndex);
        }

        /// <summary>
        /// Returns entity count for the given group (for future HUD use).
        /// </summary>
        public static int GetGroupCount(int groupIndex)
        {
            if (_instance == null) return 0;
            if (groupIndex < 0 || groupIndex >= 9) return 0;
            return _instance._groups[groupIndex].Count;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // INTERNAL METHODS
        // ═══════════════════════════════════════════════════════════════════════

        private void AssignGroupInternal(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= 9) return;

            var selection = SelectionSystem.CurrentSelection;
            var group = _groups[groupIndex];
            group.Clear();

            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                    group.Add(selection[i]);
            }
        }

        private void AddToGroupInternal(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= 9) return;

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null) return;

            var group = _groups[groupIndex];
            for (int i = 0; i < selection.Count; i++)
            {
                if (!group.Contains(selection[i]))
                    group.Add(selection[i]);
            }
        }

        private void RecallGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= 9) return;

            SelectionSystem.ClearSelection();

            var group = _groups[groupIndex];
            for (int i = 0; i < group.Count; i++)
                SelectionSystem.AddToSelection(group[i]);
        }

        private void CenterCameraOnGroup(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= 9) return;

            var group = _groups[groupIndex];
            if (group.Count == 0) return;

            float3 sum = float3.zero;
            int count = 0;

            for (int i = 0; i < group.Count; i++)
            {
                var e = group[i];
                if (!_em.Exists(e)) continue;
                if (!_em.HasComponent<LocalTransform>(e)) continue;

                sum += _em.GetComponentData<LocalTransform>(e).Position;
                count++;
            }

            if (count == 0) return;

            float3 center = sum / count;
            GameCamera.FocusOn(new Vector3(center.x, center.y, center.z));
        }

        private bool HandleRecallOrCenterInternal(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= 9) return false;

            float now = Time.time;
            bool isDoubleTap = (now - _lastPressTime[groupIndex]) < DoubleTapThreshold;
            _lastPressTime[groupIndex] = now;

            RecallGroup(groupIndex);

            if (isDoubleTap)
            {
                CenterCameraOnGroup(groupIndex);
                return true;
            }

            return false;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CLEANUP
        // ═══════════════════════════════════════════════════════════════════════

        private void CleanAllGroups()
        {
            for (int g = 0; g < 9; g++)
            {
                var group = _groups[g];
                for (int i = group.Count - 1; i >= 0; i--)
                {
                    if (!_em.Exists(group[i]))
                        group.RemoveAt(i);
                }
            }
        }
    }
}
