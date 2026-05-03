// File: Assets/Scripts/UI/HUD/FloatingHealthBars.cs
// Renders floating health bars above hovered and selected entities

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.Systems.Visibility;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Draws floating health bars above entities that are hovered or selected.
    /// Uses IMGUI to project world positions to screen-space bars.
    /// </summary>
    [DefaultExecutionOrder(910)]
    public class FloatingHealthBars : MonoBehaviour
    {
        [Header("Bar Dimensions")]
        [SerializeField] private float barWidth = 60f;
        [SerializeField] private float barHeight = 6f;
        [SerializeField] private float barBorder = 1f;
        [SerializeField] private float yOffsetAboveEntity = 1.8f;
        [SerializeField] private float buildingYOffset = 3.2f;

        private EntityWorld _world;
        private EntityManager _em;
        private Camera _cachedCamera;  // Fix #222

        private static readonly Color BgColor = new Color(0.04f, 0.05f, 0.12f, 0.85f);
        private static readonly Color BorderColor = new Color(0f, 0f, 0f, 0.6f);

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;
        }

        void OnGUI()
        {
            if (_world == null || !_world.IsCreated) return;
            if (_em.Equals(default(EntityManager)))
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world != null && _world.IsCreated) _em = _world.EntityManager;
            }
            if (_em.Equals(default(EntityManager))) return;

            // Fix #222: cache Camera.main (FindGameObjectWithTag on every call).
            var cam = _cachedCamera != null ? _cachedCamera : (_cachedCamera = Camera.main);
            if (cam == null) return;

            // Track which entities already have bars drawn (avoid duplicates for hovered+selected)
            var drawn = new HashSet<Entity>();

            // Draw bar for hovered entity (any visible entity, including battalion members)
            var hovered = RTSInput.HoveredEntity;
            if (hovered != Entity.Null && _em.Exists(hovered) && _em.HasComponent<Health>(hovered))
            {
                if (ShouldShowBar(hovered))
                {
                    DrawBarForEntity(cam, hovered, isHovered: true);
                    drawn.Add(hovered);
                }
            }

            // Draw bars for selected entities
            var selection = RTSInput.CurrentSelection;
            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    var e = selection[i];
                    if (drawn.Contains(e)) continue;
                    if (!_em.Exists(e)) continue;
                    if (!_em.HasComponent<Health>(e)) continue;

                    DrawBarForEntity(cam, e, isSelected: true);
                    drawn.Add(e);
                }
            }
        }

        private bool ShouldShowBar(Entity e)
        {
            // If the entity exists and has Health, show the bar.
            // FogVisibilitySyncSystem already hides invisible entities by deactivating
            // their GameObjects, so if the hover raycast hit this entity, it is visible.
            return _em.Exists(e);
        }

        private void DrawBarForEntity(Camera cam, Entity e, bool isSelected = false, bool isHovered = false)
        {
            if (!_em.HasComponent<LocalTransform>(e)) return;

            // Skip battalion leaders (invisible, dummy HP)
            if (_em.HasComponent<BattalionLeader>(e)) return;

            // Battalion members: show when hovered OR when their battalion is selected
            if (_em.HasComponent<BattalionMemberData>(e) && !isSelected && !isHovered) return;

            var hp = _em.GetComponentData<Health>(e);

            if (hp.Max <= 0) return;

            var pos = _em.GetComponentData<LocalTransform>(e).Position;
            bool isBuilding = _em.HasComponent<BuildingTag>(e);
            float yOff = isBuilding ? buildingYOffset : yOffsetAboveEntity;

            Vector3 worldPos = new Vector3(pos.x, pos.y + yOff, pos.z);
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            // Behind camera check
            if (screenPos.z < 0) return;

            // Convert to GUI coordinates (origin top-left)
            float guiX = screenPos.x - barWidth * 0.5f;
            float guiY = Screen.height - screenPos.y - barHeight * 0.5f;

            float ratio = Mathf.Clamp01((float)hp.Value / hp.Max);

            // Border
            GUI.color = BorderColor;
            GUI.DrawTexture(new Rect(guiX - barBorder, guiY - barBorder,
                barWidth + barBorder * 2, barHeight + barBorder * 2), Texture2D.whiteTexture);

            // Background
            GUI.color = BgColor;
            GUI.DrawTexture(new Rect(guiX, guiY, barWidth, barHeight), Texture2D.whiteTexture);

            // Fill
            Color fillColor = ratio > 0.5f ? new Color(0.3f, 0.9f, 0.3f) :
                              (ratio > 0.25f ? new Color(0.9f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f));
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(guiX, guiY, barWidth * ratio, barHeight), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }
    }
}
