// EntityViewManager.cs
// Manages the link between ECS entities and their visual GameObjects
// Location: Assets/Scripts/Presentation/EntityViewManager.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Manages the mapping between ECS entities and their visual GameObject representations.
    /// Used by systems like FogVisibilitySyncSystem to show/hide visuals.
    /// </summary>
    public class EntityViewManager : MonoBehaviour
    {
        public static EntityViewManager Instance { get; private set; }

        private readonly Dictionary<Entity, GameObject> _entityToView = new();

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
        }
    }
}