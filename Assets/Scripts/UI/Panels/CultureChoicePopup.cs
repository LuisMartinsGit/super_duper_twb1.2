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

        // ─── Cached styles (specialty centered variants — no 1:1 Styles match) ──
        private GUIStyle _headerCenteredStyle;   // derived from Styles.Header, centered, 22pt
        private GUIStyle _nameStyle;             // 16pt bold centered (derived from SubHeader)
        private GUIStyle _descStyle;             // 12pt wordWrap upper-center (bespoke)
        private GUIStyle _chooseStyle;           // 14pt bold gold-text button
        private GUIStyle _costAffordable;        // pre-cached 13pt centered green cost label
        private GUIStyle _costUnaffordable;      // pre-cached 13pt centered red cost label
        private bool _stylesInit;

        // Culture portrait textures — loaded once from Resources. Each
        // culture has an illustration at Resources/Sprites/Cultures/<Name>.
        // Falls back to colored swatches if the texture is missing.
        private static Texture2D _imgAlanthor;
        private static Texture2D _imgFeraldis;
        private static Texture2D _imgRunai;
        private static bool _imagesLoaded;
        private const float CultureImageSize = 96f;

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

        /// <summary>Hall entity currently bound to the popup (Entity.Null when hidden).</summary>
        public static Entity HallEntity => _hallEntity;

        /// <summary>Faction whose age-up the popup is offering.</summary>
        public static Faction CurrentFaction => _faction;

        /// <summary>
        /// Commit the age-up for the currently-shown culture. Same logic as the
        /// instance CommitAgeUp; exposed statically so the UI Toolkit popup
        /// region can drive it without re-instantiating the MonoBehaviour.
        /// </summary>
        public static void CommitAgeUpStatic(byte culture)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            // Hard-block locked cultures. Defense in depth — the IMGUI
            // column and web HUD card both disable the choose action,
            // but a malformed bridge payload must not slip through.
            if (CultureConfig.IsComingSoon(culture))
            {
                PlayerNotificationSystem.NotifyError($"{CultureConfig.GetName(culture)} is coming soon");
                return;
            }

            if (!FactionEconomy.Spend(em, _faction, CultureConfig.AgeUpCost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources to advance");
                return;
            }

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

            FactionColors.SetFactionCulture(_faction, culture);
            Close();
        }

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

            Styles.Initialize();
            InitStyles();

            // Dim background overlay (full screen) — 0.6 alpha is lighter than DimOverlayColor's
            // 0.7, kept inline to preserve the popup's existing modal-tint feel.
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Center the popup
            float x = (Screen.width - PopupWidth) * 0.5f;
            float y = (Screen.height - PopupHeight) * 0.5f;
            _popupRect = new Rect(x, y, PopupWidth, PopupHeight);

            // Panel background — canonical bordered navy panel.
            GUI.Box(_popupRect, "", Styles.PanelBox);

            var inner = new Rect(
                _popupRect.x + Padding,
                _popupRect.y + Padding,
                _popupRect.width - Padding * 2f,
                _popupRect.height - Padding * 2f
            );

            GUILayout.BeginArea(inner);

            // ── Header ──
            GUILayout.Label("Advance to Era 2", _headerCenteredStyle);
            GUILayout.Space(4);

            // Cost line — pre-cached affordable/unaffordable styles (no per-frame alloc).
            string costText = $"Cost: {UIHelpers.FormatCost(CultureConfig.AgeUpCost)}";
            var em = UnifiedUIManager.GetEntityManager();
            bool canAfford = !em.Equals(default(EntityManager)) && FactionEconomy.CanAfford(em, _faction, CultureConfig.AgeUpCost);
            GUILayout.Label(costText, canAfford ? _costAffordable : _costUnaffordable);

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
            if (GUILayout.Button("Cancel", Styles.Button, GUILayout.Width(100), GUILayout.Height(30)))
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
            Texture2D image = GetCultureImage(culture);
            bool locked = CultureConfig.IsComingSoon(culture);

            GUILayout.BeginVertical(GUILayout.Width(ColumnWidth));

            // Locked columns render dimmed so the eye is pulled to playable ones.
            Color prevTint = GUI.color;
            if (locked) GUI.color = new Color(1f, 1f, 1f, 0.45f);

            // Culture illustration (or colored swatches if image missing)
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (image != null)
            {
                var imageRect = GUILayoutUtility.GetRect(CultureImageSize, CultureImageSize);
                GUI.DrawTexture(imageRect, image, ScaleMode.ScaleToFit);
            }
            else
            {
                // Fallback: colored swatches keyed to the culture's primary/secondary
                // colours. Renders when Resources/Sprites/Cultures/<Name> is absent.
                var swatchRect = GUILayoutUtility.GetRect(SwatchSize, SwatchSize);
                GUI.color = locked ? new Color(primary.r, primary.g, primary.b, 0.45f) : primary;
                GUI.DrawTexture(swatchRect, Texture2D.whiteTexture);

                GUILayout.Space(4);

                var swatch2Rect = GUILayoutUtility.GetRect(SwatchSize, SwatchSize);
                GUI.color = locked ? new Color(secondary.r, secondary.g, secondary.b, 0.45f) : secondary;
                GUI.DrawTexture(swatch2Rect, Texture2D.whiteTexture);
                GUI.color = locked ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Culture name
            GUILayout.Label(name, _nameStyle);

            GUILayout.Space(4);

            // Description
            GUILayout.Label(desc, _descStyle, GUILayout.Height(50));

            GUILayout.FlexibleSpace();

            // Restore tint before drawing the button so the disabled state's
            // own greying isn't compounded with the column-wide dimming.
            GUI.color = prevTint;

            // Choose button — disabled when locked or unaffordable.
            bool wasEnabled = GUI.enabled;
            if (locked || !canAfford) GUI.enabled = false;

            string buttonLabel = locked ? "Coming Soon" : $"Choose {name}";
            if (GUILayout.Button(buttonLabel, _chooseStyle, GUILayout.Height(36)) && !locked)
            {
                CommitAgeUp(culture);
            }

            GUI.enabled = wasEnabled;

            GUILayout.EndVertical();

            // "COMING SOON" ribbon across the locked column — drawn after
            // EndVertical so it stacks above the dimmed content.
            if (locked)
            {
                var columnRect = GUILayoutUtility.GetLastRect();
                DrawComingSoonRibbon(columnRect);
            }
        }

        // Diagonal-feel ribbon centered on the locked column. Uses solid
        // bands rather than a rotated texture so it works without any
        // shader/material setup and reads well at IMGUI scale.
        private void DrawComingSoonRibbon(Rect columnRect)
        {
            const float ribbonHeight = 28f;
            var ribbonRect = new Rect(
                columnRect.x - 4f,
                columnRect.y + columnRect.height * 0.5f - ribbonHeight * 0.5f,
                columnRect.width + 8f,
                ribbonHeight
            );

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(ribbonRect, Texture2D.whiteTexture);
            GUI.color = Styles.HighlightColor;
            var topEdge = new Rect(ribbonRect.x, ribbonRect.y, ribbonRect.width, 1f);
            var botEdge = new Rect(ribbonRect.x, ribbonRect.yMax - 1f, ribbonRect.width, 1f);
            GUI.DrawTexture(topEdge, Texture2D.whiteTexture);
            GUI.DrawTexture(botEdge, Texture2D.whiteTexture);
            GUI.color = prev;

            var prevAlign = _nameStyle.alignment;
            var prevColor = _nameStyle.normal.textColor;
            _nameStyle.alignment = TextAnchor.MiddleCenter;
            _nameStyle.normal.textColor = Styles.HighlightColor;
            GUI.Label(ribbonRect, "COMING SOON", _nameStyle);
            _nameStyle.alignment = prevAlign;
            _nameStyle.normal.textColor = prevColor;
        }

        // ═══════════════════════════════════════════════════════════
        // AGE-UP LOGIC
        // ═══════════════════════════════════════════════════════════

        private void CommitAgeUp(byte culture)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            if (CultureConfig.IsComingSoon(culture))
            {
                PlayerNotificationSystem.NotifyError($"{CultureConfig.GetName(culture)} is coming soon");
                return;
            }

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


            Close();
        }

        // ═══════════════════════════════════════════════════════════
        // CULTURE IMAGES
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve the illustration texture for a culture column. Looked up
        /// once and cached statically. Falls back to null if the user
        /// hasn't dropped a texture at the expected Resources path yet —
        /// DrawCultureColumn will use coloured swatches in that case.
        ///
        /// Expected paths (Texture2D imports, .png recommended):
        ///   Assets/Resources/Sprites/Cultures/Alanthor.{png,jpg,tga}
        ///   Assets/Resources/Sprites/Cultures/Feraldis.{png,jpg,tga}
        ///   Assets/Resources/Sprites/Cultures/Runai.{png,jpg,tga}
        /// </summary>
        private static Texture2D GetCultureImage(byte culture)
        {
            if (!_imagesLoaded)
            {
                _imgAlanthor = Resources.Load<Texture2D>("Sprites/Cultures/Alanthor");
                _imgFeraldis = Resources.Load<Texture2D>("Sprites/Cultures/Feraldis");
                _imgRunai    = Resources.Load<Texture2D>("Sprites/Cultures/Runai");
                _imagesLoaded = true;
            }
            return culture switch
            {
                Cultures.Alanthor => _imgAlanthor,
                Cultures.Feraldis => _imgFeraldis,
                Cultures.Runai    => _imgRunai,
                _ => null,
            };
        }

        // ═══════════════════════════════════════════════════════════
        // STYLES
        // ═══════════════════════════════════════════════════════════

        private void InitStyles()
        {
            if (_stylesInit) return;

            // Centered 22pt header — derived from Styles.Header (20pt bold gold).
            _headerCenteredStyle = new GUIStyle(Styles.Header)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter
            };

            // Centered 16pt bold name label — derived from Styles.SubHeader.
            _nameStyle = new GUIStyle(Styles.SubHeader)
            {
                alignment = TextAnchor.MiddleCenter
            };

            // Bespoke: 12pt wordWrap upper-center description (no Styles match).
            _descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = UIHelpers.ThemeTextDim }
            };

            // Bespoke: 14pt bold gold-tinted choose button.
            _chooseStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor },
                hover = { textColor = Color.white }
            };

            // Pre-cached cost styles — derived from canonical CostStyle members with center alignment.
            _costAffordable = new GUIStyle(Styles.CostStyleAffordable)
            {
                alignment = TextAnchor.MiddleCenter
            };
            _costUnaffordable = new GUIStyle(Styles.CostStyleUnaffordable)
            {
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInit = true;
        }
    }
}
