// File: Assets/Scripts/UI/Panels/EntityInfoPanel.cs
// Entity info panel — Dark Navy + Golden theme
// Left-aligned after resources, height matches minimap. Multi-selection army breakdown.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using TWB_Input = TheWaningBorder.Input;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// IMGUI panel showing selected entity info — dark navy with golden accents.
    /// Left-aligned after ResourceHUD, height matches minimap (256px).
    /// Shows army breakdown for multi-select.
    /// </summary>
    public class EntityInfoPanel : MonoBehaviour
    {
        public static bool PanelVisible { get; private set; }
        public static Rect PanelRect { get; private set; }

        public const float PanelWidth = 320f;
        private const float PanelPadding = 10f;
        private const float PortraitSize = 64f;
        private const float MultiRowHeight = 24f;
        private const float MultiBaseHeight = 80f;

        private GUIStyle _boxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _descStyle;
        private GUIStyle _rowBg;
        private Texture2D _rowTex;
        private RectOffset _padding;
        private bool _stylesInit = false;

        // Multi-select cache
        private Vector2 _multiScrollPos;

        void Awake()
        {
            _padding = new RectOffset(10, 10, 10, 10);
        }

        void OnGUI()
        {
            PanelVisible = false;

            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null) return;

            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            InitStyles();

            if (UnifiedUIManager.IsMultiSelection())
            {
                DrawMultiPanel(em);
            }
            else
            {
                var info = EntityInfoExtractor.GetDisplayInfo(entity, em);
                DrawSinglePanel(info);
            }
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = UIHelpers.MakeBorderedTexture(64, 64,
                    new Color(0.06f, 0.08f, 0.18f, 0.95f),
                    new Color(0.83f, 0.66f, 0.26f, 0.8f), 2) }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.83f, 0.66f, 0.26f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.7f, 0.68f, 0.60f) }
            };

            _descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.8f, 0.78f, 0.72f) }
            };

            _rowTex = UIHelpers.MakeTexture(2, 2, new Color(0.08f, 0.10f, 0.22f, 0.4f));
            _rowBg = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _rowTex },
                margin = new RectOffset(0, 0, 1, 1)
            };

            _stylesInit = true;
        }

        private void DrawSinglePanel(EntityDisplayInfo info)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                ResourceHUD.NextPanelX,
                Screen.height - ResourceHUD.HudBarHeight - ResourceHUD.HudBottomMargin,
                PanelWidth,
                ResourceHUD.HudBarHeight
            );
            PanelRect = panelRect;

            GUI.Box(panelRect, "", _boxStyle);

            var innerRect = new Rect(
                panelRect.x + _padding.left,
                panelRect.y + _padding.top,
                panelRect.width - _padding.horizontal,
                panelRect.height - _padding.vertical
            );

            GUILayout.BeginArea(innerRect);

            // Top section: Portrait + Name/Type
            GUILayout.BeginHorizontal();

            // Portrait
            if (info.Portrait != null)
            {
                var portraitRect = GUILayoutUtility.GetRect(PortraitSize, PortraitSize,
                    GUILayout.Width(PortraitSize), GUILayout.Height(PortraitSize));
                GUI.DrawTexture(portraitRect, info.Portrait, ScaleMode.ScaleToFit);
            }
            else
            {
                var portraitRect = GUILayoutUtility.GetRect(PortraitSize, PortraitSize,
                    GUILayout.Width(PortraitSize), GUILayout.Height(PortraitSize));
                GUI.Box(portraitRect, "?");
            }

            GUILayout.Space(10);

            // Name and type
            GUILayout.BeginVertical();
            GUILayout.Label(info.Name, _headerStyle);
            bool isEnemy = info.Faction != null &&
                info.Faction != GameSettings.LocalPlayerFaction.ToString();
            string typeLabel = isEnemy ? $"{info.Faction} {info.Type}" : info.Type;
            GUILayout.Label(typeLabel, _smallStyle);

            // Health bar
            if (info.CurrentHealth.HasValue && info.MaxHealth.HasValue)
            {
                GUILayout.Space(5);
                DrawHealthBar(info.CurrentHealth.Value, info.MaxHealth.Value);
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Stats section
            if (info.HasCombatStats)
            {
                GUILayout.BeginHorizontal();
                if (info.Attack.HasValue)
                    GUILayout.Label($"Attack: {info.Attack.Value}", _labelStyle, GUILayout.Width(120));
                if (info.Defense.HasValue)
                    GUILayout.Label($"Defense: {info.Defense.Value}", _labelStyle, GUILayout.Width(120));
                GUILayout.EndHorizontal();

                if (info.Speed.HasValue)
                    GUILayout.Label($"Speed: {info.Speed.Value:F1}", _labelStyle);
            }

            // Resource generation (buildings)
            if (info.HasResourceGeneration)
            {
                GUILayout.Space(5);
                GUILayout.Label("Resource Generation:", _smallStyle);
                GUILayout.BeginHorizontal();
                if (info.SuppliesPerMinute.HasValue && info.SuppliesPerMinute.Value > 0)
                    GUILayout.Label($"Supplies: {info.SuppliesPerMinute.Value:F0}/min", _labelStyle, GUILayout.Width(130));
                if (info.IronPerMinute.HasValue && info.IronPerMinute.Value > 0)
                    GUILayout.Label($"Iron: {info.IronPerMinute}/min", _labelStyle, GUILayout.Width(120));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (info.CrystalPerMinute.HasValue && info.CrystalPerMinute.Value > 0)
                    GUILayout.Label($"Crystal: {info.CrystalPerMinute}/min", _labelStyle, GUILayout.Width(130));
                if (info.VeilsteelPerMinute.HasValue && info.VeilsteelPerMinute.Value > 0)
                    GUILayout.Label($"Veilsteel: {info.VeilsteelPerMinute}/min", _labelStyle, GUILayout.Width(130));
                if (info.GlowPerMinute.HasValue && info.GlowPerMinute.Value > 0)
                    GUILayout.Label($"Glow: {info.GlowPerMinute}/min", _labelStyle, GUILayout.Width(120));
                GUILayout.EndHorizontal();
            }

            // Miner info (no carry bar — just rate and status)
            if (info.HasMinerInfo)
            {
                GUILayout.Space(5);
                GUILayout.Label($"Rate: {info.MinerExtractionRate}", _labelStyle);
                GUILayout.Label($"Status: {info.MinerState}", _smallStyle);
            }

            // Resource deposit info
            if (info.HasResourceInfo)
            {
                GUILayout.Space(5);
                GUILayout.Label($"{info.ResourceTypeName} Remaining:", _smallStyle);
                DrawResourceBar(info.ResourceRemaining, info.ResourceMax);
                GUILayout.Label($"{info.ResourceRemaining} / {info.ResourceMax}", _labelStyle);
            }

            GUILayout.Space(10);

            // Description
            GUILayout.Label(info.Description, _descStyle);

            GUILayout.EndArea();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MULTI-SELECTION PANEL (bottom-center, army breakdown)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawMultiPanel(EntityManager em)
        {
            var allEntities = UnifiedUIManager.GetAllSelectedEntities();
            if (allEntities.Count == 0) return;

            PanelVisible = true;

            // Group entities by type name
            var groups = new Dictionary<string, UnitGroupInfo>();
            int totalHP = 0, totalMaxHP = 0;
            bool hasUnits = false;
            bool hasBuildings = false;

            foreach (var e in allEntities)
            {
                if (!em.Exists(e)) continue;

                string name;
                bool isBuilding = em.HasComponent<BuildingTag>(e);
                bool isUnit = em.HasComponent<UnitTag>(e);

                if (isBuilding)
                {
                    hasBuildings = true;
                    name = EntityInfoExtractor.GetDisplayInfo(e, em).Name;
                }
                else if (isUnit)
                {
                    hasUnits = true;
                    name = EntityInfoExtractor.GetDisplayInfo(e, em).Name;
                }
                else
                {
                    name = "Other";
                }

                if (!groups.TryGetValue(name, out var grp))
                {
                    grp = new UnitGroupInfo { Name = name };
                    groups[name] = grp;
                }
                grp.Count++;

                if (em.HasComponent<Health>(e))
                {
                    var hp = em.GetComponentData<Health>(e);
                    grp.TotalHP += (int)hp.Value;
                    grp.TotalMaxHP += (int)hp.Max;
                    totalHP += (int)hp.Value;
                    totalMaxHP += (int)hp.Max;
                }
            }

            var panelRect = new Rect(
                ResourceHUD.NextPanelX,
                Screen.height - ResourceHUD.HudBarHeight - ResourceHUD.HudBottomMargin,
                PanelWidth,
                ResourceHUD.HudBarHeight
            );
            PanelRect = panelRect;

            GUI.Box(panelRect, "", _boxStyle);

            var innerRect = new Rect(
                panelRect.x + _padding.left,
                panelRect.y + _padding.top,
                panelRect.width - _padding.horizontal,
                panelRect.height - _padding.vertical
            );

            GUILayout.BeginArea(innerRect);

            // Header
            string headerText;
            if (hasUnits && !hasBuildings)
                headerText = $"Army ({allEntities.Count} units)";
            else if (hasBuildings && !hasUnits)
                headerText = $"Buildings ({allEntities.Count} selected)";
            else
                headerText = $"Selection ({allEntities.Count} entities)";

            GUILayout.Label(headerText, _headerStyle);

            // Total HP summary
            if (totalMaxHP > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Total HP: {totalHP}/{totalMaxHP}", _smallStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4f);

            // Golden separator
            var sepRect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.5f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(4f);

            // Scrollable unit type list
            float scrollHeight = innerRect.height - 80f;
            _multiScrollPos = GUILayout.BeginScrollView(_multiScrollPos,
                GUILayout.Height(Mathf.Max(scrollHeight, 60f)));

            foreach (var kvp in groups)
            {
                var grp = kvp.Value;
                var rowRect = GUILayoutUtility.GetRect(PanelWidth - 40f, MultiRowHeight);
                GUI.Box(rowRect, "", _rowBg);

                // Name x Count
                var nameStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(rowRect.x + 6f, rowRect.y + 2f, 180f, 20f),
                    $"{grp.Name} x{grp.Count}", nameStyle);

                // HP bar for group
                if (grp.TotalMaxHP > 0)
                {
                    float hpBarWidth = 80f;
                    float hpBarX = rowRect.x + rowRect.width - hpBarWidth - 6f;
                    float hpBarY = rowRect.y + 4f;
                    float ratio = Mathf.Clamp01((float)grp.TotalHP / grp.TotalMaxHP);

                    // Background
                    GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
                    GUI.DrawTexture(new Rect(hpBarX, hpBarY, hpBarWidth, 14f), Texture2D.whiteTexture);

                    // Fill
                    Color fillColor = ratio > 0.5f ? new Color(0.3f, 0.9f, 0.3f) :
                                      (ratio > 0.25f ? new Color(0.9f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f));
                    GUI.color = fillColor;
                    GUI.DrawTexture(new Rect(hpBarX, hpBarY, hpBarWidth * ratio, 14f), Texture2D.whiteTexture);
                    GUI.color = Color.white;

                    // HP text
                    var hpStyle = new GUIStyle(_smallStyle)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 10,
                        normal = { textColor = Color.white }
                    };
                    GUI.Label(new Rect(hpBarX, hpBarY, hpBarWidth, 14f),
                        $"{grp.TotalHP}/{grp.TotalMaxHP}", hpStyle);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private class UnitGroupInfo
        {
            public string Name;
            public int Count;
            public int TotalHP;
            public int TotalMaxHP;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HEALTH / RESOURCE BARS
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawResourceBar(int current, int max)
        {
            var rect = GUILayoutUtility.GetRect(200f, 12);

            // Background
            GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Fill (teal/blue for resources)
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
            Color fillColor = ratio > 0.5f ? new Color(0.3f, 0.7f, 0.9f) :
                              (ratio > 0.2f ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.8f, 0.3f, 0.3f));
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        private void DrawHealthBar(int current, int max)
        {
            var rect = GUILayoutUtility.GetRect(PortraitSize, 10);

            // Background
            GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Fill
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
            Color fillColor = ratio > 0.5f ? new Color(0.3f, 0.9f, 0.3f) :
                              (ratio > 0.25f ? new Color(0.9f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f));
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;

            // Text
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, $"{current}/{max}", labelStyle);
        }

        /// <summary>
        /// Returns the X coordinate where the next HUD panel should start (right edge of info + gap).
        /// Used by EntityActionPanel.
        /// </summary>
        public static float NextPanelX => ResourceHUD.NextPanelX + PanelWidth + ResourceHUD.PanelGap;

        /// <summary>
        /// Check if pointer is over this panel.
        /// </summary>
        public static bool IsPointerOver()
        {
            if (!PanelVisible) return false;
            var mousePos = UnityEngine.Input.mousePosition;
            var screenRect = new Rect(
                PanelRect.x,
                Screen.height - PanelRect.y - PanelRect.height,
                PanelRect.width,
                PanelRect.height
            );
            return screenRect.Contains(mousePos);
        }
    }
}
