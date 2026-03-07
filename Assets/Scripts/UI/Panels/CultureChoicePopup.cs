// File: Assets/Scripts/UI/Panels/CultureChoicePopup.cs
// Modal popup for Era 2 culture selection — Dark Navy + Golden theme

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Core;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// IMGUI modal popup for choosing a culture when advancing to Era 2.
    /// Shows three culture options with color swatches, descriptions, and choose buttons.
    /// </summary>
    public class CultureChoicePopup : MonoBehaviour
    {
        // ─── State ───────────────────────────────────────────────────
        private static bool _visible;
        private static Entity _hallEntity;
        private static Faction _faction;
        private static Rect _popupRect;

        // ─── Layout constants ────────────────────────────────────────
        private const float PopupWidth = 620f;
        private const float PopupHeight = 340f;
        private const float ColumnWidth = 180f;
        private const float SwatchSize = 40f;
        private const float ColumnSpacing = 12f;
        private const float Padding = 16f;

        // ─── Cached styles ───────────────────────────────────────────
        private GUIStyle _bgStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _descStyle;
        private GUIStyle _chooseStyle;
        private GUIStyle _cancelStyle;
        private GUIStyle _costStyle;
        private bool _stylesInit;

        // ═══════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Open the culture choice popup for a given Hall entity.
        /// </summary>
        public static void Show(Entity hall, Faction faction)
        {
            _hallEntity = hall;
            _faction = faction;
            _visible = true;
        }

        /// <summary>
        /// Close the popup without committing.
        /// </summary>
        public static void Close()
        {
            _visible = false;
            _hallEntity = Entity.Null;
        }

        /// <summary>
        /// Is the popup currently visible?
        /// </summary>
        public static bool IsVisible => _visible;

        /// <summary>
        /// Check if mouse is over the popup for input blocking.
        /// </summary>
        public static bool IsPointerOver()
        {
            if (!_visible) return false;
            var mousePos = UnityEngine.Input.mousePosition;
            var screenRect = new Rect(
                _popupRect.x,
                Screen.height - _popupRect.y - _popupRect.height,
                _popupRect.width,
                _popupRect.height
            );
            return screenRect.Contains(mousePos);
        }

        // ═══════════════════════════════════════════════════════════
        // IMGUI
        // ═══════════════════════════════════════════════════════════

        void OnGUI()
        {
            if (!_visible) return;

            InitStyles();

            // Dim background overlay (full screen)
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Center the popup
            float x = (Screen.width - PopupWidth) * 0.5f;
            float y = (Screen.height - PopupHeight) * 0.5f;
            _popupRect = new Rect(x, y, PopupWidth, PopupHeight);

            // Panel background
            GUI.Box(_popupRect, "", _bgStyle);

            var inner = new Rect(
                _popupRect.x + Padding,
                _popupRect.y + Padding,
                _popupRect.width - Padding * 2f,
                _popupRect.height - Padding * 2f
            );

            GUILayout.BeginArea(inner);

            // ── Header ──
            GUILayout.Label("Advance to Era 2", _headerStyle);
            GUILayout.Space(4);

            // Cost line
            string costText = $"Cost: {UIHelpers.FormatCost(CultureConfig.AgeUpCost)}";
            var em = UnifiedUIManager.GetEntityManager();
            bool canAfford = !em.Equals(default(EntityManager)) && FactionEconomy.CanAfford(em, _faction, CultureConfig.AgeUpCost);
            var costColor = canAfford ? new Color(0.3f, 0.9f, 0.3f) : new Color(1f, 0.3f, 0.3f);
            var costStyleInst = new GUIStyle(_costStyle) { normal = { textColor = costColor } };
            GUILayout.Label(costText, costStyleInst);

            GUILayout.Space(8);
            GUILayout.Label("Choose your cultural specialization:", _descStyle);
            GUILayout.Space(12);

            // ── Three columns ──
            GUILayout.BeginHorizontal();

            DrawCultureColumn(Cultures.Alanthor, canAfford);
            GUILayout.Space(ColumnSpacing);
            DrawCultureColumn(Cultures.Feraldis, canAfford);
            GUILayout.Space(ColumnSpacing);
            DrawCultureColumn(Cultures.Runai, canAfford);

            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            // ── Cancel button ──
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", _cancelStyle, GUILayout.Width(100), GUILayout.Height(30)))
            {
                Close();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.EndArea();

            // Block all input behind the popup
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
            {
                Event.current.Use();
            }
        }

        private void DrawCultureColumn(byte culture, bool canAfford)
        {
            var primary = CultureConfig.GetPrimary(culture);
            var secondary = CultureConfig.GetSecondary(culture);
            string name = CultureConfig.GetName(culture);
            string desc = CultureConfig.GetDescription(culture);

            GUILayout.BeginVertical(GUILayout.Width(ColumnWidth));

            // Color swatches — primary + secondary side by side
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Primary swatch
            var swatchRect = GUILayoutUtility.GetRect(SwatchSize, SwatchSize);
            GUI.color = primary;
            GUI.DrawTexture(swatchRect, Texture2D.whiteTexture);

            GUILayout.Space(4);

            // Secondary swatch
            var swatch2Rect = GUILayoutUtility.GetRect(SwatchSize, SwatchSize);
            GUI.color = secondary;
            GUI.DrawTexture(swatch2Rect, Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Culture name
            GUILayout.Label(name, _nameStyle);

            GUILayout.Space(4);

            // Description
            GUILayout.Label(desc, _descStyle, GUILayout.Height(50));

            GUILayout.FlexibleSpace();

            // Choose button
            bool wasEnabled = GUI.enabled;
            if (!canAfford) GUI.enabled = false;

            if (GUILayout.Button($"Choose {name}", _chooseStyle, GUILayout.Height(36)))
            {
                CommitAgeUp(culture);
            }

            GUI.enabled = wasEnabled;

            GUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════
        // AGE-UP LOGIC
        // ═══════════════════════════════════════════════════════════

        private void CommitAgeUp(byte culture)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            // 1. Spend resources
            if (!FactionEconomy.Spend(em, _faction, CultureConfig.AgeUpCost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources to advance");
                return;
            }

            // 2. Add AgeUpState timer to the Hall — completion handled by AgeUpSystem
            if (em.Exists(_hallEntity))
            {
                float duration = CultureConfig.AgeUpDuration;
                if (!em.HasComponent<AgeUpState>(_hallEntity))
                {
                    em.AddComponentData(_hallEntity, new AgeUpState
                    {
                        Culture = culture,
                        Duration = duration,
                        Remaining = duration
                    });
                }
            }

            // 3. Register culture with FactionColors so UI/rendering picks it up immediately
            FactionColors.SetFactionCulture(_faction, culture);

            Debug.Log($"[CultureChoicePopup] {_faction} started age-up to Era 2 — culture: {CultureConfig.GetName(culture)} ({CultureConfig.AgeUpDuration}s)");

            Close();
        }

        // ═══════════════════════════════════════════════════════════
        // STYLES
        // ═══════════════════════════════════════════════════════════

        private void InitStyles()
        {
            if (_stylesInit) return;

            _bgStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = UIHelpers.MakeBorderedTexture(64, 64,
                    UIHelpers.ThemePanelBg,
                    UIHelpers.ThemeGoldBorder, 2) }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UIHelpers.ThemeGold }
            };

            _nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UIHelpers.ThemeText }
            };

            _descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = UIHelpers.ThemeTextDim }
            };

            _chooseStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UIHelpers.ThemeGold },
                hover = { textColor = Color.white }
            };

            _cancelStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                normal = { textColor = UIHelpers.ThemeTextDim }
            };

            _costStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInit = true;
        }
    }
}
