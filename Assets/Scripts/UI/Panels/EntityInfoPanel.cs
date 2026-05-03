// File: Assets/Scripts/UI/Panels/EntityInfoPanel.cs
// Entity info panel — Dark Navy + Golden theme
// Left-aligned after resources, height matches minimap. Multi-selection army breakdown.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using TWB_Input = TheWaningBorder.Input;
using TheWaningBorder.UI;
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
        private const float UnitSquareSize = 38f;        // Size of each clickable unit square
        private const float UnitSquareSpacing = 4f;       // Gap between squares

        // Local cached style (no clean Styles.cs counterpart — wordWrap + dimmed off-white).
        private GUIStyle _descStyle;
        private RectOffset _padding;
        private bool _localStylesBuilt = false;

        // Multi-select cache
        private Vector2 _multiScrollPos;
        private int _highlightedIndex = 0;          // Which square in the grid is currently highlighted
        private int _pendingHighlightIndex = -1;    // Click sets this; applied next frame to keep Layout/Repaint consistent
        private Entity _highlightedEntity = Entity.Null;
        private List<Entity> _orderedSelection = new(); // Stable order for grid layout

        void Awake()
        {
            _padding = new RectOffset(10, 10, 10, 10);
        }

        void OnGUI()
        {
            PanelVisible = false;

            // Apply any pending grid click from the previous frame BEFORE any
            // event-type-specific drawing, so Layout and Repaint within this
            // frame use the same _highlightedIndex (Unity calls OnGUI multiple
            // times per frame and conditional GUILayout calls must match).
            if (Event.current.type == EventType.Layout && _pendingHighlightIndex >= 0)
            {
                _highlightedIndex = _pendingHighlightIndex;
                _pendingHighlightIndex = -1;
            }

            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null) return;

            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            Styles.Initialize();
            BuildLocalStyles();

            if (UnifiedUIManager.IsMultiSelection())
            {
                DrawMultiPanel(em);
            }
            else
            {
                var info = EntityInfoExtractor.GetDisplayInfo(entity, em);
                DrawSinglePanel(info);
            }

            ResourceIcons.DrawTooltip();
        }

        // Build the truly-unique cached local (description label — wordWrap + custom dim color).
        // All other panel styles (PanelBox, Header, Label, SmallLabel) come from Styles.cs.
        private void BuildLocalStyles()
        {
            if (_localStylesBuilt) return;

            _descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.8f, 0.78f, 0.72f) }
            };

            _localStylesBuilt = true;
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

            GUI.Box(panelRect, "", Styles.PanelBox);

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
            GUILayout.Label(info.Name, Styles.Header);
            bool isEnemy = info.Faction != null &&
                info.Faction != GameSettings.LocalPlayerFaction.ToString();
            string typeLabel = isEnemy ? $"{info.Faction} {info.Type}" : info.Type;
            GUILayout.Label(typeLabel, Styles.SmallLabel);

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
                    GUILayout.Label($"Attack: {info.Attack.Value}", Styles.Label, GUILayout.Width(120));
                if (info.Defense.HasValue)
                    GUILayout.Label($"Defense: {info.Defense.Value}", Styles.Label, GUILayout.Width(120));
                GUILayout.EndHorizontal();

                if (info.Speed.HasValue)
                    GUILayout.Label($"Speed: {info.Speed.Value:F1}", Styles.Label);
            }

            // Battalion stance display
            {
                var stanceEntity = UnifiedUIManager.GetFirstSelectedEntity();
                var stanceEm = UnifiedUIManager.GetEntityManager();
                if (stanceEntity != Entity.Null && !stanceEm.Equals(default(EntityManager)))
                {
                    Entity stanceLeader = Entity.Null;
                    if (stanceEm.HasComponent<BattalionLeader>(stanceEntity))
                        stanceLeader = stanceEntity;
                    else if (stanceEm.HasComponent<BattalionMemberData>(stanceEntity))
                        stanceLeader = stanceEm.GetComponentData<BattalionMemberData>(stanceEntity).Leader;

                    if (stanceLeader != Entity.Null && stanceEm.Exists(stanceLeader)
                        && stanceEm.HasComponent<BattalionStanceData>(stanceLeader))
                    {
                        var stance = stanceEm.GetComponentData<BattalionStanceData>(stanceLeader).Value;
                        string stanceLabel = stance switch
                        {
                            BattalionStance.Defensive => "Defensive",
                            BattalionStance.Default => "Default",
                            BattalionStance.Aggressive => "Aggressive",
                            _ => "Unknown"
                        };
                        GUILayout.Label($"Stance: {stanceLabel}", Styles.Label);
                    }
                }
            }

            // Resource generation (buildings)
            if (info.HasResourceGeneration)
            {
                GUILayout.Space(5);
                GUILayout.Label("Resource Generation:", Styles.SmallLabel);
                GUILayout.BeginHorizontal();
                if (info.SuppliesPerMinute.HasValue && info.SuppliesPerMinute.Value > 0)
                    ResourceIcons.DrawLayoutIconValue("Supplies", $"{info.SuppliesPerMinute.Value:F0}/min", 12f, Styles.Label, 130f);
                if (info.IronPerMinute.HasValue && info.IronPerMinute.Value > 0)
                    ResourceIcons.DrawLayoutIconValue("Iron", $"{info.IronPerMinute}/min", 12f, Styles.Label, 120f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (info.CrystalPerMinute.HasValue && info.CrystalPerMinute.Value > 0)
                    ResourceIcons.DrawLayoutIconValue("Crystal", $"{info.CrystalPerMinute}/min", 12f, Styles.Label, 130f);
                if (info.VeilsteelPerMinute.HasValue && info.VeilsteelPerMinute.Value > 0)
                    ResourceIcons.DrawLayoutIconValue("Veilsteel", $"{info.VeilsteelPerMinute}/min", 12f, Styles.Label, 130f);
                if (info.GlowPerMinute.HasValue && info.GlowPerMinute.Value > 0)
                    ResourceIcons.DrawLayoutIconValue("Glow", $"{info.GlowPerMinute}/min", 12f, Styles.Label, 120f);
                GUILayout.EndHorizontal();
            }

            // Miner info (no carry bar -- just rate and status)
            if (info.HasMinerInfo)
            {
                GUILayout.Space(5);
                GUILayout.Label($"Rate: {info.MinerExtractionRate}", Styles.Label);
                GUILayout.Label($"Status: {info.MinerState}", Styles.SmallLabel);
            }

            // Resource deposit info
            if (info.HasResourceInfo)
            {
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                ResourceIcons.DrawLayoutIcon(info.ResourceTypeName, 11f);
                GUILayout.Label(" Remaining:", Styles.SmallLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                DrawResourceBar(info.ResourceRemaining, info.ResourceMax);
                GUILayout.Label($"{info.ResourceRemaining} / {info.ResourceMax}", Styles.Label);
            }

            // Trade lane caravan countdown (Runai Trading Posts)
            DrawTradeLaneCountdown();

            GUILayout.Space(10);

            // Description
            GUILayout.Label(info.Description, _descStyle);

            GUILayout.EndArea();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TRADE LANE CARAVAN COUNTDOWN (Runai Trading Posts)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// If the selected entity is a TradeHub with a TradeHubSpawner waiting
        /// for a trader spawn, draw a countdown progress bar.
        /// </summary>
        private void DrawTradeLaneCountdown()
        {
            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null) return;
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            // Only show for trade hubs with a spawner
            if (!em.HasComponent<TradeHubTag>(entity)) return;
            if (!em.HasComponent<TradeHubSpawner>(entity)) return;

            var spawner = em.GetComponentData<TradeHubSpawner>(entity);

            const float SpawnInterval = 30f;
            const int MaxTradersPerFaction = 30;

            float remaining = Mathf.Max(0f, spawner.TraderTimer);
            float elapsed = SpawnInterval - remaining;
            float pct = Mathf.Clamp01(elapsed / SpawnInterval);
            int seconds = Mathf.CeilToInt(remaining);

            GUILayout.Space(6);

            // Separator line
            var sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.25f, 0.75f, 0.80f, 0.4f); // Runai cyan tint
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(4);

            // Label
            GUILayout.Label($"Next Trader  {seconds}s", Styles.Label);
            GUILayout.Space(2);

            // Progress bar background
            var barRect = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);

            // Progress bar fill (Runai cyan)
            var fillRect = new Rect(barRect.x, barRect.y, barRect.width * pct, barRect.height);
            GUI.color = new Color(0.25f, 0.75f, 0.80f, 1f);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

            // Percentage text centered on bar
            GUI.color = Color.white;
            GUI.Label(barRect, $"{Mathf.RoundToInt(pct * 100f)}%",
                new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                });

            GUI.color = Color.white;

            // Spawned traders info
            GUILayout.Label($"Traders Spawned: {spawner.TradersSpawned} (max {MaxTradersPerFaction}/faction)", Styles.SmallLabel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MULTI-SELECTION PANEL (bottom-center, army breakdown)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawMultiPanel(EntityManager em)
        {
            var allEntities = UnifiedUIManager.GetAllSelectedEntities();
            if (allEntities.Count == 0) return;

            PanelVisible = true;

            // Build stable ordered list (battalion leaders first, then individual units, then buildings)
            // and skip battalion members (their leader represents them).
            _orderedSelection.Clear();
            foreach (var e in allEntities)
            {
                if (!em.Exists(e)) continue;
                if (em.HasComponent<BattalionMemberData>(e)) continue;
                _orderedSelection.Add(e);
            }
            if (_orderedSelection.Count == 0) return;

            // Clamp highlight index to valid range
            if (_highlightedIndex >= _orderedSelection.Count) _highlightedIndex = 0;
            _highlightedEntity = _orderedSelection[_highlightedIndex];

            var panelRect = new Rect(
                ResourceHUD.NextPanelX,
                Screen.height - ResourceHUD.HudBarHeight - ResourceHUD.HudBottomMargin,
                PanelWidth,
                ResourceHUD.HudBarHeight
            );
            PanelRect = panelRect;

            GUI.Box(panelRect, "", Styles.PanelBox);

            var innerRect = new Rect(
                panelRect.x + _padding.left,
                panelRect.y + _padding.top,
                panelRect.width - _padding.horizontal,
                panelRect.height - _padding.vertical
            );

            GUILayout.BeginArea(innerRect);

            // Header
            GUILayout.Label($"Selection ({_orderedSelection.Count})", Styles.Header);

            // Golden separator
            var sepRect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.5f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.Space(6f);

            // ── Unit grid: clickable squares ──
            DrawUnitSquareGrid(em, innerRect.width);

            GUILayout.Space(8f);

            // ── Stats panel for highlighted unit ──
            DrawHighlightedStats(em, _highlightedEntity);

            GUILayout.EndArea();
        }

        /// <summary>
        /// Renders a grid of clickable unit-type squares. Each square shows the unit's
        /// faction color, type icon (text fallback), and an HP bar above. Clicking a
        /// square highlights that unit so its full stats show below the grid.
        /// </summary>
        private void DrawUnitSquareGrid(EntityManager em, float availableWidth)
        {
            int squaresPerRow = Mathf.Max(1, (int)((availableWidth + UnitSquareSpacing) / (UnitSquareSize + UnitSquareSpacing)));

            // Reserve a flex-height area; grid grows by row count.
            int rowCount = (_orderedSelection.Count + squaresPerRow - 1) / squaresPerRow;
            float gridHeight = rowCount * (UnitSquareSize + UnitSquareSpacing + 6f); // +6 for HP bar
            var gridRect = GUILayoutUtility.GetRect(availableWidth, Mathf.Min(gridHeight, 120f));

            // Scrollable if grid is too tall
            var scrollViewRect = new Rect(gridRect.x, gridRect.y, gridRect.width, gridRect.height);
            var contentRect = new Rect(0, 0, gridRect.width - 16f, gridHeight);
            _multiScrollPos = GUI.BeginScrollView(scrollViewRect, _multiScrollPos, contentRect);

            for (int i = 0; i < _orderedSelection.Count; i++)
            {
                var entity = _orderedSelection[i];
                if (!em.Exists(entity)) continue;

                int col = i % squaresPerRow;
                int row = i / squaresPerRow;
                float x = col * (UnitSquareSize + UnitSquareSpacing);
                float y = row * (UnitSquareSize + UnitSquareSpacing + 6f);

                // HP bar above the square
                float hpRatio = 1f;
                if (em.HasComponent<Health>(entity))
                {
                    var hp = em.GetComponentData<Health>(entity);
                    hpRatio = hp.Max > 0 ? Mathf.Clamp01(hp.Value / (float)hp.Max) : 0f;
                }
                var hpRect = new Rect(x, y, UnitSquareSize, 4f);
                GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
                GUI.DrawTexture(hpRect, Texture2D.whiteTexture);
                Color hpColor = hpRatio > 0.5f ? new Color(0.3f, 0.9f, 0.3f)
                              : (hpRatio > 0.25f ? new Color(0.9f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f));
                GUI.color = hpColor;
                GUI.DrawTexture(new Rect(x, y, UnitSquareSize * hpRatio, 4f), Texture2D.whiteTexture);
                GUI.color = Color.white;

                // Square button
                var squareRect = new Rect(x, y + 6f, UnitSquareSize, UnitSquareSize);

                // Faction color background
                Color fillColor = new Color(0.2f, 0.2f, 0.3f);
                if (em.HasComponent<FactionTag>(entity))
                    fillColor = FactionColors.Get(em.GetComponentData<FactionTag>(entity).Value) * 0.5f;
                GUI.color = fillColor;
                GUI.DrawTexture(squareRect, Texture2D.whiteTexture);

                // Border (highlighted if selected)
                Color borderColor = (i == _highlightedIndex)
                    ? new Color(1.0f, 0.85f, 0.3f)   // gold for highlighted
                    : new Color(0.83f, 0.66f, 0.26f, 0.5f);
                GUI.color = borderColor;
                int borderThickness = (i == _highlightedIndex) ? 2 : 1;
                GUI.DrawTexture(new Rect(squareRect.x, squareRect.y, squareRect.width, borderThickness), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(squareRect.x, squareRect.yMax - borderThickness, squareRect.width, borderThickness), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(squareRect.x, squareRect.y, borderThickness, squareRect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(squareRect.xMax - borderThickness, squareRect.y, borderThickness, squareRect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                // Unit type abbreviation (text fallback for icon)
                string abbrev = GetUnitAbbreviation(em, entity);
                var labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
                GUI.Label(squareRect, abbrev, labelStyle);

                // Click detection: store the new index for next frame so Layout
                // and Repaint within this frame stay consistent.
                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 0
                    && squareRect.Contains(Event.current.mousePosition))
                {
                    _pendingHighlightIndex = i;
                    Event.current.Use();
                }
            }

            GUI.EndScrollView();
        }

        /// <summary>
        /// Returns a 1-3 character abbreviation for a unit's type, used in grid squares.
        /// </summary>
        private static string GetUnitAbbreviation(EntityManager em, Entity entity)
        {
            if (em.HasComponent<ArcherTag>(entity)) return "A";
            if (em.HasComponent<MinerTag>(entity)) return "M";
            if (em.HasComponent<CanBuild>(entity)) return "B";
            if (em.HasComponent<BattalionLeader>(entity))
            {
                if (em.HasBuffer<BattalionMember>(entity))
                {
                    var members = em.GetBuffer<BattalionMember>(entity);
                    for (int i = 0; i < members.Length; i++)
                    {
                        var m = members[i].Value;
                        if (m == Entity.Null || !em.Exists(m)) continue;
                        if (em.HasComponent<ArcherTag>(m)) return "A";
                        if (em.HasComponent<UnitTag>(m))
                        {
                            var ut = em.GetComponentData<UnitTag>(m);
                            if (ut.Class == UnitClass.Siege) return "S";
                            return "Sw"; // Swordsman/melee
                        }
                    }
                }
                return "B";
            }
            if (em.HasComponent<UnitTag>(entity))
            {
                var ut = em.GetComponentData<UnitTag>(entity);
                if (ut.Class == UnitClass.Siege) return "S";
                if (ut.Class == UnitClass.Ranged) return "A";
                if (ut.Class == UnitClass.Melee) return "Sw";
            }
            if (em.HasComponent<BuildingTag>(entity)) return "Bd";
            return "?";
        }

        /// <summary>
        /// Renders the stats panel below the unit grid for the currently highlighted entity.
        /// Shows: name, unit count (for battalions), HP, attack/defense/speed, stance.
        /// </summary>
        private void DrawHighlightedStats(EntityManager em, Entity entity)
        {
            if (entity == Entity.Null || !em.Exists(entity)) return;

            var info = EntityInfoExtractor.GetDisplayInfo(entity, em);

            // Name + battalion size (alive/total)
            string headerLine = info.Name;
            if (em.HasComponent<BattalionLeader>(entity) && em.HasBuffer<BattalionMember>(entity))
            {
                var members = em.GetBuffer<BattalionMember>(entity);
                int alive = 0;
                int total = members.Length;
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i].Value;
                    if (m == Entity.Null || !em.Exists(m)) continue;
                    if (!em.HasComponent<Health>(m)) continue;
                    if (em.GetComponentData<Health>(m).Value > 0) alive++;
                }
                headerLine = $"{info.Name}  ({alive}/{total} units)";
            }

            var nameStyle = new GUIStyle(Styles.Label) { fontStyle = FontStyle.Bold };
            GUILayout.Label(headerLine, nameStyle);

            // HP bar (sum of members for battalions, else single entity HP)
            int curHp = 0, maxHp = 0;
            if (em.HasComponent<BattalionLeader>(entity) && em.HasBuffer<BattalionMember>(entity))
            {
                var members = em.GetBuffer<BattalionMember>(entity);
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i].Value;
                    if (m == Entity.Null || !em.Exists(m)) continue;
                    if (!em.HasComponent<Health>(m)) continue;
                    var h = em.GetComponentData<Health>(m);
                    curHp += (int)h.Value;
                    maxHp += (int)h.Max;
                }
            }
            else if (em.HasComponent<Health>(entity))
            {
                var h = em.GetComponentData<Health>(entity);
                curHp = (int)h.Value; maxHp = (int)h.Max;
            }
            if (maxHp > 0)
                DrawHealthBar(curHp, maxHp);

            GUILayout.Space(4f);

            // Combat stats — gather from leader's first alive member if battalion
            int? attack = null, defense = null;
            float? speed = null;
            Entity statSrc = entity;
            if (em.HasComponent<BattalionLeader>(entity) && em.HasBuffer<BattalionMember>(entity))
            {
                var members = em.GetBuffer<BattalionMember>(entity);
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i].Value;
                    if (m != Entity.Null && em.Exists(m)) { statSrc = m; break; }
                }
            }
            if (em.HasComponent<Damage>(statSrc)) attack = em.GetComponentData<Damage>(statSrc).Value;
            if (em.HasComponent<Defense>(statSrc))
            {
                var d = em.GetComponentData<Defense>(statSrc);
                defense = Mathf.Max(d.Melee, Mathf.Max(d.Ranged, d.Magic));
            }
            if (em.HasComponent<MoveSpeed>(statSrc)) speed = em.GetComponentData<MoveSpeed>(statSrc).Value;

            if (attack.HasValue || defense.HasValue || speed.HasValue)
            {
                GUILayout.BeginHorizontal();
                if (attack.HasValue) GUILayout.Label($"Atk {attack.Value}", Styles.Label, GUILayout.Width(70));
                if (defense.HasValue) GUILayout.Label($"Def {defense.Value}", Styles.Label, GUILayout.Width(70));
                if (speed.HasValue) GUILayout.Label($"Spd {speed.Value:F1}", Styles.Label, GUILayout.Width(70));
                GUILayout.EndHorizontal();
            }

            // Attack range / cooldown for ranged
            if (em.HasComponent<ArcherState>(statSrc))
            {
                var a = em.GetComponentData<ArcherState>(statSrc);
                GUILayout.BeginHorizontal();
                if (a.MaxRange > 0) GUILayout.Label($"Range {a.MaxRange:F0}", Styles.Label, GUILayout.Width(85));
                if (a.AimTimeRequired > 0) GUILayout.Label($"Aim {a.AimTimeRequired:F1}s", Styles.Label, GUILayout.Width(85));
                GUILayout.EndHorizontal();
            }

            // Battalion stance
            if (em.HasComponent<BattalionStanceData>(entity))
            {
                var stance = em.GetComponentData<BattalionStanceData>(entity).Value;
                string stanceLabel = stance switch
                {
                    BattalionStance.Defensive => "Defensive",
                    BattalionStance.Default => "Default",
                    BattalionStance.Aggressive => "Aggressive",
                    _ => "Unknown"
                };
                GUILayout.Label($"Stance: {stanceLabel}", Styles.SmallLabel);
            }
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
