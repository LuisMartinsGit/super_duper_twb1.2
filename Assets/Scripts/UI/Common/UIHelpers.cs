// File: Assets/Scripts/UI/Common/UIHelpers.cs
// Shared UI utility functions and data structures

using UnityEngine;
using Unity.Entities;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.Input;
using TheWaningBorder.UI.HUD;

namespace TheWaningBorder.UI.Common
{
    /// <summary>
    /// Shared utility functions for UI systems.
    /// </summary>
    public static class UIHelpers
    {
        /// <summary>
        /// Create a solid color texture.
        /// </summary>
        public static Texture2D MakeTexture(int width, int height, Color color)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = color;
            var tex = new Texture2D(width, height);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Create a gradient texture (vertical).
        /// </summary>
        public static Texture2D MakeGradientTexture(int width, int height, Color top, Color bottom)
        {
            var tex = new Texture2D(width, height);
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                Color c = Color.Lerp(bottom, top, t);
                for (int x = 0; x < width; x++)
                {
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Create a texture with a solid fill and a colored border.
        /// Used for golden-contour panel backgrounds.
        /// </summary>
        public static Texture2D MakeBorderedTexture(int width, int height, Color fillColor, Color borderColor, int borderWidth = 2)
        {
            var tex = new Texture2D(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = x < borderWidth || x >= width - borderWidth ||
                                    y < borderWidth || y >= height - borderWidth;
                    tex.SetPixel(x, y, isBorder ? borderColor : fillColor);
                }
            }
            tex.Apply();
            return tex;
        }

        // ═══════════════════════════════════════════════════════════════
        // THEME COLORS — Dark Navy + Golden
        // ═══════════════════════════════════════════════════════════════
        public static readonly Color ThemePanelBg    = new Color(0.06f, 0.08f, 0.18f, 0.95f);
        public static readonly Color ThemeInnerBg    = new Color(0.08f, 0.10f, 0.22f, 0.95f);
        public static readonly Color ThemeGold       = new Color(0.83f, 0.66f, 0.26f, 1f);
        public static readonly Color ThemeGoldDim    = new Color(0.6f, 0.48f, 0.18f, 1f);
        public static readonly Color ThemeGoldBorder = new Color(0.83f, 0.66f, 0.26f, 0.8f);
        public static readonly Color ThemeText       = new Color(0.9f, 0.88f, 0.82f, 1f);
        public static readonly Color ThemeTextDim    = new Color(0.7f, 0.68f, 0.60f, 1f);

        /// <summary>
        /// Check if mouse position is inside a GUI rect (GUI coordinates).
        /// </summary>
        public static bool IsMouseOverRect(Rect guiRect)
        {
            var mousePos = UnityEngine.Input.mousePosition;
            var screenRect = new Rect(
                guiRect.x,
                Screen.height - guiRect.y - guiRect.height,
                guiRect.width,
                guiRect.height
            );
            return screenRect.Contains(mousePos);
        }

        /// <summary>
        /// Convert GUI rect to screen rect (bottom-left origin).
        /// </summary>
        public static Rect GuiToScreenRect(Rect guiRect)
        {
            return new Rect(
                guiRect.x,
                Screen.height - guiRect.y - guiRect.height,
                guiRect.width,
                guiRect.height
            );
        }

        /// <summary>
        /// Format a cost as a compact string.
        /// </summary>
        public static string FormatCost(TheWaningBorder.Core.Cost cost)
        {
            var sb = new System.Text.StringBuilder(64);
            
            void Add(string name, int value)
            {
                if (value > 0)
                {
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append(name).Append(' ').Append(value);
                }
            }

            Add("S", cost.Supplies);
            Add("Fe", cost.Iron);
            Add("Cr", cost.Crystal);
            Add("Vs", cost.Veilsteel);
            Add("Gl", cost.Glow);

            return sb.Length == 0 ? "Free" : sb.ToString();
        }

        /// <summary>
        /// Draw a progress bar.
        /// </summary>
        public static void DrawProgressBar(Rect rect, float progress, Color fillColor, Color bgColor)
        {
            progress = Mathf.Clamp01(progress);

            // Background
            GUI.color = bgColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Fill
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * progress, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw a health bar with automatic coloring.
        /// </summary>
        public static void DrawHealthBar(Rect rect, int current, int max, string label = null)
        {
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
            Color fillColor = ratio > 0.5f ? Color.green : (ratio > 0.25f ? Color.yellow : Color.red);
            
            DrawProgressBar(rect, ratio, fillColor, new Color(0.3f, 0.3f, 0.3f, 1f));

            // Optional label
            if (!string.IsNullOrEmpty(label))
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                GUI.Label(rect, label, style);
            }
        }

        /// <summary>
        /// Get faction display color.
        /// </summary>
        public static Color GetFactionColor(Faction faction)
        {
            return faction switch
            {
                Faction.Blue => new Color(0.3f, 0.5f, 1f),
                Faction.Red => new Color(1f, 0.3f, 0.3f),
                Faction.Green => new Color(0.3f, 1f, 0.3f),
                Faction.Yellow => new Color(1f, 1f, 0.3f),
                Faction.Purple => new Color(0.8f, 0.3f, 1f),
                Faction.Orange => new Color(1f, 0.6f, 0.2f),
                Faction.Teal => new Color(0.2f, 0.8f, 0.8f),
                Faction.White => new Color(0.9f, 0.9f, 0.9f),
                _ => Color.gray
            };
        }

        /// <summary>
        /// Get faction display name.
        /// </summary>
        public static string GetFactionName(Faction faction, bool includePlayerLabel = false)
        {
            if (includePlayerLabel && faction == GameSettings.LocalPlayerFaction)
                return "PLAYER";

            return faction.ToString();
        }
    }

    /// <summary>
    /// Unified UI manager that coordinates panels.
    /// </summary>
    public class UnifiedUIManager : MonoBehaviour
    {
        private static UnifiedUIManager _instance;

        private EntityWorld _world;
        private EntityManager _em;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;

            // Add panel components
            gameObject.AddComponent<Panels.EntityInfoPanel>();
            gameObject.AddComponent<Panels.EntityActionPanel>();
            gameObject.AddComponent<Panels.CultureChoicePopup>();
            gameObject.AddComponent<HUD.FloatingHealthBars>();
            gameObject.AddComponent<HUD.PlayerNotificationSystem>();
        }

        void Update()
        {
            if (_em.Equals(default(EntityManager)))
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world != null && _world.IsCreated)
                    _em = _world.EntityManager;
            }
        }

        /// <summary>
        /// Get the first valid selected entity from RTSInput.
        /// Returns own entities first, then enemy entities if visible.
        /// </summary>
        public static Entity GetFirstSelectedEntity()
        {
            var sel = SelectionSystem.CurrentSelection;
            if (sel == null || sel.Count == 0) return Entity.Null;

            var manager = GetEntityManager();
            if (manager.Equals(default(EntityManager))) return Entity.Null;

            for (int i = 0; i < sel.Count; i++)
            {
                var e = sel[i];
                if (!manager.Exists(e)) continue;
                if (!manager.HasComponent<FactionTag>(e)) continue;

                return e;
            }

            return Entity.Null;
        }

        /// <summary>
        /// Check if the first selected entity belongs to the local player.
        /// </summary>
        public static bool IsSelectionOwnedByPlayer()
        {
            var e = GetFirstSelectedEntity();
            if (e == Entity.Null) return false;

            var manager = GetEntityManager();
            if (manager.Equals(default(EntityManager))) return false;
            if (!manager.HasComponent<FactionTag>(e)) return false;

            return manager.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction;
        }

        /// <summary>
        /// Check if mouse pointer is over any UI panel.
        /// </summary>
        public static bool IsPointerOverAnyPanel()
        {
            return Panels.EntityInfoPanel.IsPointerOver()
                || Panels.EntityActionPanel.IsPointerOver()
                || Panels.CultureChoicePopup.IsPointerOver();
        }

        /// <summary>
        /// Get the EntityManager.
        /// </summary>
        public static EntityManager GetEntityManager()
        {
            if (_instance != null && !_instance._em.Equals(default(EntityManager)))
                return _instance._em;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
                return world.EntityManager;

            return default;
        }
    }
}