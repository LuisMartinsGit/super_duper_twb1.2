// EntityViewManager.cs
// Manages the link between ECS entities and their visual GameObjects
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Manages the mapping between ECS entities and their visual GameObject representations.
    /// Used by systems like FogVisibilitySyncSystem to show/hide visuals.
    /// </summary>
    public class EntityViewManager : MonoBehaviour
    {
        public static EntityViewManager Instance { get; private set; }

        private readonly Dictionary<Entity, GameObject> _entityToView = new();

        /// <summary>How many entities currently hold a view. Read by
        /// WorldCensus to catch views that are registered and never released
        /// — the managed half of a match that gets heavier as it runs.</summary>
        public int ViewCount => _entityToView.Count;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Register a GameObject as the visual representation of an entity.
        /// </summary>
        public void RegisterView(Entity entity, GameObject view)
        {
            if (entity == Entity.Null || view == null) return;
            _entityToView[entity] = view;
            if (_registered.Count < RegisteredJournalCap) _registered.Add(entity);
            else _registeredOverflow = true;
        }

        // Registration journal (2026-09-25). PresentationSpawnSystem skips
        // whole chunks whose LocalTransform did not change; a view that was
        // (re)registered on an unchanged entity must still get its first
        // write, and this is how it learns which ones. Drained every frame by
        // that one consumer; the cap only guards a consumer-less scene.
        private const int RegisteredJournalCap = 65536;
        private readonly List<Entity> _registered = new();
        private bool _registeredOverflow;

        /// <summary>Moves every entity registered since the last drain into
        /// <paramref name="into"/>. <paramref name="overflow"/> = the journal
        /// hit its cap and the consumer must fall back to a full sweep.</summary>
        public void DrainRegistered(List<Entity> into, out bool overflow)
        {
            into.AddRange(_registered);
            _registered.Clear();
            overflow = _registeredOverflow;
            _registeredOverflow = false;
        }

        /// <summary>
        /// Unregister an entity's view.
        /// </summary>
        public void UnregisterView(Entity entity)
        {
            _entityToView.Remove(entity);
        }

        /// <summary>
        /// Try to get the GameObject for an entity.
        /// </summary>
        public bool TryGetView(Entity entity, out GameObject view)
        {
            return _entityToView.TryGetValue(entity, out view);
        }

        /// <summary>
        /// Get the GameObject for an entity, or null if not found.
        /// </summary>
        public GameObject GetView(Entity entity)
        {
            return _entityToView.TryGetValue(entity, out var view) ? view : null;
        }

        /// <summary>
        /// Clear all registered views.
        /// </summary>
        public void ClearAll()
        {
            _entityToView.Clear();
            _registered.Clear();
            _registeredOverflow = true;   // consumer re-sweeps everything
        }
    }
}