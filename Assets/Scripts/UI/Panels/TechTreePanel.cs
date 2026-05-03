// File: Assets/Scripts/UI/Panels/TechTreePanel.cs
// Read-only tech tree viewer accessible from the HUD top-right area.
// Shows all technologies grouped by category with research status.

using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// IMGUI panel that displays a read-only tech tree overview.
    /// Toggled via a button in the top-right HUD area.
    /// Groups technologies into Era 1, culture-specific, and sect categories.
    /// </summary>
    public class TechTreePanel : MonoBehaviour
    {
        public static bool IsVisible { get; private set; }

        // Layout
        private const float PanelWidth = 700f;
        private const float PanelHeight = 520f;
        private const float ButtonWidth = 90f;
        private const float ButtonHeight = 28f;
        private const float ButtonMargin = 120f; // Offset from right edge (past End Game button)
        private const float RowHeight = 44f;
        private const float SectionSpacing = 12f;

        // State
        private bool _visible;
        private Vector2 _scrollPos;
        private float _prevTimeScale = 1f;

        // Styles — bespoke wordWrap/bold variants cached locally (no Styles match);
        // panel, header, subheader, label all sourced from Styles.cs.
        private GUIStyle _smallStyle;     // 11pt wordWrap (TinyLabel doesn't wrap)
        private GUIStyle _buttonStyle;    // 12pt bold gold-tinted
        private bool _stylesInit;

        // Tech grouping
        private static readonly string[] Era1Techs =
        {
            "ImprovedTools", "StorageCarts", "BasicDrills", "WoodenArmor", "Research_Era2"
        };

        private static readonly string[] RunaiTechs =
        {
            "Runai_LongHaulTariffs", "Runai_PackBazaar", "Runai_EscortedCaravans"
        };

        void OnGUI()
        {
            Styles.Initialize();
            InitStyles();

            // Draw toggle button in top-right (next to End Game)
            DrawToggleButton();

            if (!_visible) return;

            DrawPanel();

            // Escape to close
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
            }
        }

        private void DrawToggleButton()
        {
            // Don't show button if post-game stats are visible
            if (TheWaningBorder.UI.HUD.PostGameStatsUI.IsVisible) return;

            float x = Screen.width - ButtonWidth - ButtonMargin;
            float y = 10f;

            if (GUI.Button(new Rect(x, y, ButtonWidth, ButtonHeight), "Tech Tree", _buttonStyle))
            {
                if (_visible) Close();
                else Open();
            }
        }

        private void Open()
        {
            _visible = true;
            IsVisible = true;
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        private void Close()
        {
            _visible = false;
            IsVisible = false;
            Time.timeScale = _prevTimeScale;
        }

        private void DrawPanel()
        {
            // Centered overlay
            float x = (Screen.width - PanelWidth) * 0.5f;
            float y = (Screen.height - PanelHeight) * 0.5f;
            var panelRect = new Rect(x, y, PanelWidth, PanelHeight);

            GUI.Box(panelRect, "", Styles.PanelBox);

            // Inner area
            var innerRect = new Rect(x + 12f, y + 12f, PanelWidth - 24f, PanelHeight - 24f);
            GUILayout.BeginArea(innerRect);

            // Header row
            GUILayout.BeginHorizontal();
            GUILayout.Label("Technology Overview", Styles.Header);
            GUILayout.FlexibleSpace();

            var faction = GameSettings.LocalPlayerFaction;
            var researchState = FactionResearchState.Instance;
            int completedCount = researchState != null ? researchState.GetCompletedCount(faction) : 0;
            GUILayout.Label($"Researched: {completedCount}", Styles.Label);

            GUILayout.Space(12f);
            if (GUILayout.Button("X", GUILayout.Width(28f), GUILayout.Height(24f)))
            {
                Close();
            }
            GUILayout.EndHorizontal();

            // Golden separator
            var sepRect = GUILayoutUtility.GetRect(1, 2, GUILayout.ExpandWidth(true));
            GUI.color = new Color(Styles.HighlightColor.r, Styles.HighlightColor.g, Styles.HighlightColor.b, 0.6f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(6f);

            // Scrollable content
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, true);

            DrawSection("Era 1 Technologies", Era1Techs, faction, researchState);
            DrawSection("Runai Technologies", RunaiTechs, faction, researchState);
            DrawSectTechSection(faction, researchState);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSection(string title, string[] techIds, Faction faction,
            FactionResearchState researchState)
        {
            var db = TechTreeDB.Instance;
            if (db == null) return;

            GUILayout.Label(title, Styles.SubHeader);
            GUILayout.Space(4f);

            foreach (var techId in techIds)
            {
                if (!db.TryGetTechnology(techId, out var tech)) continue;

                bool researched = researchState != null && researchState.HasResearched(faction, techId);
                bool meetsPrereqs = researchState != null &&
                                    researchState.MeetsPrerequisites(faction, tech.prerequisites);

                DrawTechRow(tech.name, tech.effect, tech.cost, researched, meetsPrereqs);
            }

            GUILayout.Space(SectionSpacing);
        }

        private void DrawSectTechSection(Faction faction, FactionResearchState researchState)
        {
            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            GUILayout.Label("Sect Technologies", Styles.SubHeader);
            GUILayout.Space(4f);

            // Group by culture
            DrawSectCultureGroup("Alanthor Sects", new[]
            {
                SectConfig.Renewal, SectConfig.Antiquity,
                SectConfig.LivingStone, SectConfig.VeiledMemory
            }, faction, sectState, CultureConfig.AlanthorPrimary);

            DrawSectCultureGroup("Runai Sects", new[]
            {
                SectConfig.StillFlame, SectConfig.QuietVault,
                SectConfig.MirrorRite, SectConfig.ShardJudgment
            }, faction, sectState, CultureConfig.RunaiPrimary);

            DrawSectCultureGroup("Feraldis Sects", new[]
            {
                SectConfig.EmberAsh, SectConfig.HollowBrand,
                SectConfig.FlamewroughtChains, SectConfig.UnmakersGrasp
            }, faction, sectState, CultureConfig.FeraldisPrimary);
        }

        private void DrawSectCultureGroup(string groupTitle, string[] sectIds,
            Faction faction, FactionSectState sectState, Color cultureColor)
        {
            var cultureLabel = new GUIStyle(_smallStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = cultureColor }
            };
            GUILayout.Label(groupTitle, cultureLabel);
            GUILayout.Space(2f);

            foreach (var sectId in sectIds)
            {
                bool adopted = sectState.HasAdopted(faction, sectId);
                bool techResearched = sectState.HasTechFlag(faction, sectId);

                string name = SectConfig.GetTechDisplayName(sectId);
                string desc = SectConfig.GetTechDescription(sectId);
                string passive = SectConfig.GetPassiveDescription(sectId);

                // Status: researched > adopted (can research) > not adopted (locked)
                string status;
                Color statusColor;
                if (techResearched)
                {
                    status = "RESEARCHED";
                    statusColor = Styles.SuccessColor;
                }
                else if (adopted)
                {
                    status = "AVAILABLE";
                    statusColor = Styles.HighlightColor;
                }
                else
                {
                    status = "LOCKED";
                    statusColor = Styles.DisabledColor;
                }

                // Row background
                var rowRect = GUILayoutUtility.GetRect(PanelWidth - 60f, RowHeight);
                GUI.Box(rowRect, "", Styles.InnerRowBox);

                // Sect display name + passive
                string sectName = SectConfig.GetDisplayName(sectId);
                var nameStyle = new GUIStyle(Styles.Label) { fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 2f, 180f, 20f), sectName, nameStyle);
                GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 20f, 280f, 18f), passive, _smallStyle);

                // Tech description
                GUI.Label(new Rect(rowRect.x + 300f, rowRect.y + 4f, 240f, 36f), desc, _smallStyle);

                // Status label
                var statusStyle = new GUIStyle(Styles.Label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = statusColor }
                };
                GUI.Label(new Rect(rowRect.x + rowRect.width - 110f, rowRect.y + 10f, 100f, 22f),
                    status, statusStyle);
            }

            GUILayout.Space(8f);
        }

        private void DrawTechRow(string name, string effect, TheWaningBorder.Data.CostBlock cost,
            bool researched, bool meetsPrereqs)
        {
            var rowRect = GUILayoutUtility.GetRect(PanelWidth - 60f, RowHeight);
            GUI.Box(rowRect, "", Styles.InnerRowBox);

            // Status indicator
            Color statusColor;
            string statusText;
            if (researched)
            {
                statusColor = Styles.SuccessColor;
                statusText = "DONE";
            }
            else if (meetsPrereqs)
            {
                statusColor = Styles.HighlightColor;
                statusText = "READY";
            }
            else
            {
                statusColor = Styles.DisabledColor;
                statusText = "LOCKED";
            }

            // Name
            var nameStyle = new GUIStyle(Styles.Label) { fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 2f, 160f, 20f), name, nameStyle);

            // Effect
            GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 22f, 300f, 18f), effect, _smallStyle);

            // Cost
            if (cost != null && !cost.IsZero)
            {
                string costStr = cost.ToString();
                GUI.Label(new Rect(rowRect.x + 320f, rowRect.y + 10f, 180f, 22f), costStr, _smallStyle);
            }

            // Status
            var sStyle = new GUIStyle(Styles.Label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = statusColor }
            };
            GUI.Label(new Rect(rowRect.x + rowRect.width - 80f, rowRect.y + 10f, 70f, 22f),
                statusText, sStyle);
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            // 11pt wordWrap small text (TinyLabel is 11pt but doesn't wrap).
            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.68f, 0.60f) }
            };

            // 12pt bold gold-tinted toggle button.
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor }
            };

            _stylesInit = true;
        }

        /// <summary>
        /// Check if pointer is over this panel (for input blocking).
        /// </summary>
        public static bool IsPointerOver()
        {
            if (!IsVisible) return false;

            float x = (Screen.width - PanelWidth) * 0.5f;
            float y = (Screen.height - PanelHeight) * 0.5f;
            var panelRect = new Rect(x, y, PanelWidth, PanelHeight);

            return UIHelpers.IsMouseOverRect(panelRect);
        }
    }
}
