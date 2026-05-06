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
using TheWaningBorder.Core.Commands.Types;

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

            // Upgrade button — appears next to the portrait area for any
            // upgradeable building owned by the local player whose faction
            // has picked a culture. Click triggers UpgradeBuildingCommandHelper.
            DrawUpgradeButton();

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

            // Veteran rank promotion + Glow Ability (audit fix #1)
            DrawUnitRankSection();

            GUILayout.Space(10);

            // Description
            GUILayout.Label(info.Description, _descStyle);

            GUILayout.EndArea();
        }

        /// <summary>
        /// Veteran rank UI for the current selection — works for a single
        /// unit, a battalion (leader + members), or a mixed multi-select.
        /// Aggregates owned military units and exposes a single Promote
        /// button that targets the lowest-rank cohort (so subsequent clicks
        /// catch up stragglers before pushing the front-runners higher).
        /// At Lv 5 a Glow Ability button fires the burst on every ready
        /// Lv 5 unit in the selection.
        /// (Audit fix #1 / battalion-promote follow-up.)
        /// </summary>
        private void DrawUnitRankSection()
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            var selection = TWB_Input.SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            // Bucket owned military units by current rank, plus collect
            // Lv5 units for the Glow Ability button.
            var byRank = new int[TheWaningBorder.Economy.UnitRankConfig.MaxRank + 1];
            var promotable = new System.Collections.Generic.List<Entity>(selection.Count);
            var glowReady = new System.Collections.Generic.List<Entity>();
            int glowActiveCount = 0;
            int glowCooldownMin = int.MaxValue;
            int glowMaxCount = 0;

            foreach (var e in selection)
            {
                if (!em.Exists(e)) continue;
                if (!em.HasComponent<UnitTag>(e)) continue;
                var cls = em.GetComponentData<UnitTag>(e).Class;
                if (cls != UnitClass.Melee && cls != UnitClass.Ranged && cls != UnitClass.Siege) continue;
                if (!em.HasComponent<FactionTag>(e)) continue;
                if (em.GetComponentData<FactionTag>(e).Value != GameSettings.LocalPlayerFaction) continue;

                byte rank = em.HasComponent<UnitRank>(e) ? em.GetComponentData<UnitRank>(e).Value : (byte)1;
                if (rank < 1) rank = 1;
                if (rank > TheWaningBorder.Economy.UnitRankConfig.MaxRank) rank = TheWaningBorder.Economy.UnitRankConfig.MaxRank;
                byRank[rank]++;
                if (rank < TheWaningBorder.Economy.UnitRankConfig.MaxRank)
                    promotable.Add(e);

                if (rank == TheWaningBorder.Economy.UnitRankConfig.MaxRank)
                {
                    glowMaxCount++;
                    GlowAbilityState glow = default;
                    if (em.HasComponent<GlowAbilityState>(e))
                        glow = em.GetComponentData<GlowAbilityState>(e);
                    if (glow.ActiveRemaining > 0f) glowActiveCount++;
                    else if (glow.CooldownRemaining > 0f)
                        glowCooldownMin = System.Math.Min(glowCooldownMin, (int)glow.CooldownRemaining);
                    else
                        glowReady.Add(e);
                }
            }

            int totalMilitary = 0;
            for (int i = 1; i <= TheWaningBorder.Economy.UnitRankConfig.MaxRank; i++) totalMilitary += byRank[i];
            if (totalMilitary == 0) return;

            GUILayout.Space(5);

            // Composition line: "Ranks: 8×I, 4×II"
            string composition = BuildRankComposition(byRank);
            GUILayout.Label($"Ranks: {composition}", Styles.Label);

            // ── Promote (lowest-rank cohort) ──
            if (promotable.Count > 0)
            {
                byte lowestRank = 1;
                for (byte r = 1; r < TheWaningBorder.Economy.UnitRankConfig.MaxRank; r++)
                {
                    if (byRank[r] > 0) { lowestRank = r; break; }
                }
                int cohortCount = byRank[lowestRank];
                byte target = (byte)(lowestRank + 1);
                var unitCost = TheWaningBorder.Economy.UnitRankConfig.CostFor(target);
                var totalCost = MultiplyCost(unitCost, cohortCount);
                string costLabel = CostLabel(totalCost);
                bool canAffordOne = TheWaningBorder.Economy.FactionEconomy.CanAfford(
                    em, GameSettings.LocalPlayerFaction, unitCost);
                bool canAffordAll = TheWaningBorder.Economy.FactionEconomy.CanAfford(
                    em, GameSettings.LocalPlayerFaction, totalCost);
                string suffix = canAffordAll ? "" : (canAffordOne ? "  (partial)" : "  (need resources)");

                GUI.enabled = canAffordOne;
                string label = cohortCount == 1
                    ? $"Promote → Lv {target} ({costLabel}){suffix}"
                    : $"Promote {cohortCount} (Lv {lowestRank} → Lv {target}) — {costLabel}{suffix}";
                if (GUILayout.Button(label, GUILayout.Width(260)))
                {
                    // Re-collect the cohort by rank so we don't promote
                    // stale entries (selection might have changed since gather).
                    foreach (var e in promotable)
                    {
                        if (!em.Exists(e)) continue;
                        byte r = em.HasComponent<UnitRank>(e) ? em.GetComponentData<UnitRank>(e).Value : (byte)1;
                        if (r < 1) r = 1;
                        if (r != lowestRank) continue;
                        var result = TheWaningBorder.Core.Commands.Types
                            .UnitRankCommandHelper.Execute(em, e);
                        if (result == TheWaningBorder.Core.Commands.Types.UnitRankPromoteResult.CannotAfford)
                            break; // stop on first afford failure
                    }
                }
                GUI.enabled = true;
            }

            // ── Glow Ability (Lv 5 batch fire) ──
            if (glowMaxCount > 0)
            {
                bool ready = glowReady.Count > 0;
                GUI.enabled = ready;
                string label;
                if (ready)
                    label = glowReady.Count == 1
                        ? "Glow Ability"
                        : $"Glow Ability ({glowReady.Count} ready)";
                else if (glowActiveCount > 0)
                    label = $"Glow active ({glowActiveCount})";
                else if (glowCooldownMin != int.MaxValue)
                    label = $"Glow ({glowCooldownMin}s)";
                else
                    label = "Glow Ability";
                if (GUILayout.Button(label, GUILayout.Width(260)))
                {
                    foreach (var e in glowReady)
                        TheWaningBorder.Core.Commands.Types.GlowAbilityCommandHelper.Execute(em, e);
                }
                GUI.enabled = true;
            }
        }

        private static string BuildRankComposition(int[] byRank)
        {
            var sb = new System.Text.StringBuilder();
            string[] roman = { "?", "I", "II", "III", "IV", "V" };
            bool first = true;
            for (int r = 1; r < byRank.Length; r++)
            {
                if (byRank[r] == 0) continue;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(byRank[r]).Append('×').Append(roman[r]);
            }
            return first ? "—" : sb.ToString();
        }

        private static TheWaningBorder.Core.Cost MultiplyCost(in TheWaningBorder.Core.Cost c, int n)
        {
            return new TheWaningBorder.Core.Cost
            {
                Supplies  = c.Supplies  * n,
                Iron      = c.Iron      * n,
                Crystal   = c.Crystal   * n,
                Veilsteel = c.Veilsteel * n,
                Glow      = c.Glow      * n,
            };
        }

        private static string CostLabel(in TheWaningBorder.Core.Cost c)
        {
            if (c.Glow > 0)      return $"{c.Glow} Glow";
            if (c.Veilsteel > 0) return $"{c.Veilsteel} Veilsteel";
            if (c.Crystal > 0)   return $"{c.Crystal} Crystal";
            if (c.Iron > 0)      return $"{c.Iron} Iron";
            return $"{c.Supplies} Supplies";
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

            // ── Veteran rank promotion + Glow Ability (batch over selection) ──
            DrawUnitRankSection();

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

        // ──────────────────────────────────────────────────────────────────
        // BUILDING UPGRADE BUTTON
        // ──────────────────────────────────────────────────────────────────

        // Brief feedback line shown under the upgrade button — reflects the
        // last command result so the player understands why a click stuck
        // (or didn't). Clears after BuildingUpgradeFeedbackTimeout seconds.
        private string _upgradeFeedback;
        private float _upgradeFeedbackUntil;
        private const float BuildingUpgradeFeedbackTimeout = 3f;

        // Diagnostic: log the eligibility outcome ONCE per entity so we
        // can see in the console which gate (Upgradeable / Faction / etc.)
        // is rejecting an upgrade attempt without spamming every frame.
        private Entity _lastDiagEntity = Entity.Null;
        private string _lastDiagOutcome;

        private void DrawUpgradeButton()
        {
            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            var em = UnifiedUIManager.GetEntityManager();
            if (entity == Entity.Null || em.Equals(default(EntityManager))) return;
            if (!em.Exists(entity)) return;

            // Diagnostic: log once when selection changes, so the console
            // tells us whether the button is being suppressed.
            if (entity != _lastDiagEntity)
            {
                _lastDiagEntity = entity;
                bool upgradeable = em.HasComponent<BuildingUpgradeable>(entity);
                bool hasFaction  = em.HasComponent<FactionTag>(entity);
                Faction sFac = hasFaction ? em.GetComponentData<FactionTag>(entity).Value : (Faction)255;
                bool localOwned = hasFaction && sFac == GameSettings.LocalPlayerFaction;
                byte lvl = em.HasComponent<BuildingUpgradeState>(entity)
                    ? em.GetComponentData<BuildingUpgradeState>(entity).Level : (byte)0;
                Debug.Log($"[Upgrade] selected entity {entity.Index}: " +
                          $"upgradeable={upgradeable} faction={sFac} local={localOwned} level={lvl}");
            }

            if (!em.HasComponent<BuildingUpgradeable>(entity)) return;

            // Local-player only — no upgrade button on enemy buildings.
            if (!em.HasComponent<FactionTag>(entity)) return;
            var fac = em.GetComponentData<FactionTag>(entity).Value;
            if (fac != GameSettings.LocalPlayerFaction) return;

            // Already at max?
            byte currentLevel = em.HasComponent<BuildingUpgradeState>(entity)
                ? em.GetComponentData<BuildingUpgradeState>(entity).Level : (byte)0;
            bool atMax = currentLevel >= TheWaningBorder.Core.Settings.BuildingUpgradeConfig.MaxLevel;

            // In progress?
            bool upgrading = em.HasComponent<BuildingUpgrading>(entity);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();

            if (upgrading)
            {
                var ub = em.GetComponentData<BuildingUpgrading>(entity);
                float pct = ub.Total > 0f ? Mathf.Clamp01(ub.Progress / ub.Total) : 0f;
                GUILayout.Label($"Upgrading to L{ub.TargetLevel}... {(int)(pct * 100f)}%",
                    Styles.Label);
            }
            else if (atMax)
            {
                GUILayout.Label($"Lvl {currentLevel} (max)", Styles.Label);
            }
            else if (UpgradeBuildingCommandHelper.TryGetNextCost(em, entity,
                out var cost, out byte nextLevel))
            {
                string costLabel = FormatCost(cost);
                bool clicked = GUILayout.Button($"Upgrade to L{nextLevel} — {costLabel}");
                if (clicked)
                {
                    Debug.Log($"[Upgrade] click — entity {entity.Index}, target L{nextLevel}, cost {costLabel}");
                    var result = UpgradeBuildingCommandHelper.Execute(em, entity);
                    Debug.Log($"[Upgrade] result = {result}");
                    _upgradeFeedback = FeedbackFor(result);
                    _upgradeFeedbackUntil = Time.realtimeSinceStartup + BuildingUpgradeFeedbackTimeout;
                }
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_upgradeFeedback)
                && Time.realtimeSinceStartup < _upgradeFeedbackUntil)
            {
                GUILayout.Label(_upgradeFeedback, Styles.SmallLabel);
            }
        }

        private static string FormatCost(TheWaningBorder.Core.Cost c)
        {
            // Compact inline display: "100s 25i" or "200s 50i 15c". Skip
            // zeros so the button label stays short.
            var sb = new System.Text.StringBuilder(24);
            if (c.Supplies > 0)  sb.Append(c.Supplies).Append("s ");
            if (c.Iron > 0)      sb.Append(c.Iron).Append("i ");
            if (c.Crystal > 0)   sb.Append(c.Crystal).Append("c ");
            if (c.Veilsteel > 0) sb.Append(c.Veilsteel).Append("v ");
            if (c.Glow > 0)      sb.Append(c.Glow).Append("g ");
            if (sb.Length == 0)  return "free";
            return sb.ToString().TrimEnd();
        }

        private static string FeedbackFor(UpgradeBuildingResult r) => r switch
        {
            UpgradeBuildingResult.Ok                 => "Upgrade started.",
            UpgradeBuildingResult.NotUpgradeable     => "Cannot upgrade this building.",
            UpgradeBuildingResult.AlreadyMaxLevel    => "Already at max level.",
            UpgradeBuildingResult.NoCulture          => "Pick a culture (age up) first.",
            UpgradeBuildingResult.AlreadyUpgrading   => "Already upgrading.",
            UpgradeBuildingResult.UnderConstruction  => "Finish construction first.",
            UpgradeBuildingResult.CannotAfford       => "Not enough resources.",
            _                                        => string.Empty,
        };
    }
}
