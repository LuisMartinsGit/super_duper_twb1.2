// File: Assets/Scripts/UI/Panels/EntityActionPanel.cs
// Action buttons panel — Dark Navy + Golden theme

using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Input;
using TheWaningBorder.UI;
using TheWaningBorder.Core;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Data;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// IMGUI panel for building placement and unit training — dark navy with golden accents.
    /// </summary>
    public class EntityActionPanel : MonoBehaviour
    {
        public static bool PanelVisible { get; private set; }
        public static Rect PanelRect { get; private set; }

        private const float PanelWidth = 370f;
        private const float PanelPadding = 10f;
        private const float ButtonSize = 22f;
        private const float ButtonSpacing = 3f;

        /// <summary>Maximum number of items allowed in a training queue.</summary>
        private const int MAX_TRAIN_QUEUE = 5;
        private const float QueueSlotSize = 22f;
        private const float QueueSlotSpacing = 2f;

        // Local cached styles (no clean Styles.cs counterpart):
        // _ageUpStyle: large bold gold button. _requireStyle: small word-wrapped warning label.
        // _tooltipStyle: bordered word-wrap rich-text tooltip box with custom padding.
        // All standard panel/header/button/label/small/cost styles are sourced from Styles.cs.
        private GUIStyle _ageUpStyle;
        private GUIStyle _requireStyle;
        private RectOffset _padding;
        private GUIStyle _tooltipStyle;
        private bool _localStylesBuilt = false;

        /// <summary>Tooltip text set by DrawActionGrid, drawn as floating box in OnGUI.</summary>
        private string _hoveredTooltip;
        private Cost _hoveredCost;

        /// <summary>Currently selected chapel slot index (-1 = none, shows sect picker).</summary>
        private int _selectedChapelSlot = -1;

        /// <summary>Build time in seconds for a temple chapel (after RP spent).</summary>
        private const float ChapelBuildTime = 20f;

        void Awake()
        {
            _padding = new RectOffset(10, 10, 10, 10);
        }

        void OnGUI()
        {
            PanelVisible = false;
            _hoveredTooltip = null;
            _hoveredCost = default;

            // Observer cannot issue commands
            if (GameSettings.IsObserver) return;

            // Only show actions for own entities, not enemy
            if (!UnifiedUIManager.IsSelectionOwnedByPlayer()) return;

            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null) return;

            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            var actionInfo = EntityActionExtractor.GetActionInfo(entity, em);

            if (actionInfo.Type == ActionType.None) return;

            Styles.Initialize();
            BuildLocalStyles();

            // Guard against Layout/Repaint control count mismatches.
            // Entity state can change between IMGUI passes causing different
            // control counts which throws ArgumentException.
            try
            {

            switch (actionInfo.Type)
            {
                case ActionType.BuildingPlacement:
                    DrawBuildingPlacementPanel(entity, actionInfo);
                    break;

                case ActionType.UnitTraining:
                    DrawUnitTrainingPanel(entity, actionInfo);
                    break;

                case ActionType.UnitTrainingAndResearch:
                    DrawUnitTrainingAndResearchPanel(entity, actionInfo);
                    break;

                case ActionType.VaultManagement:
                    DrawVaultPanel(entity);
                    break;

                case ActionType.TempleUpgrade:
                    DrawTempleLevelUpPanel(entity, actionInfo);
                    break;

                case ActionType.BattalionStance:
                    DrawStancePanel(entity, em);
                    break;

                case ActionType.WallInstanceUpgrade:
                    DrawWallUpgradePanel(entity);
                    break;

                case ActionType.BazaarWagonUnpack:
                    DrawBazaarWagonPanel(entity);
                    break;
            }

            } // end try
            catch (System.ArgumentException)
            {
                // Layout/Repaint control count mismatch — entity state changed between passes.
                // Silently skip this frame; next frame will re-sync.
            }

            // Draw floating tooltip above the panel (outside any BeginArea)
            DrawFloatingTooltip();

            // Resource icon tooltips
            ResourceIcons.DrawTooltip();
        }

        // Build the truly-unique cached locals that have no clean Styles.cs counterpart.
        // Standard styles (PanelBox, Header, IconButton, Label, SmallLabel, CostStyleAffordable,
        // CostStyleUnaffordable) come directly from Styles.cs. Cost/RP styles previously
        // pre-cached locally (Fix #221) now come from Styles.CostStyleAffordable/Unaffordable
        // which are also pre-cached in Styles.CreateMiscStyles().
        private void BuildLocalStyles()
        {
            if (_localStylesBuilt) return;

            _ageUpStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor },
                hover = { textColor = Color.white }
            };

            _requireStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.5f, 0.3f) }  // dim warning amber
            };

            // Bordered word-wrap rich-text tooltip box. Border color sourced from
            // Styles.HighlightColor (with alpha 0.6 override). Background uses a slightly
            // darker navy variant kept inline since no Styles member matches exactly.
            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                richText = true,
                fontSize = 12,
                normal = {
                    textColor = new Color(0.9f, 0.85f, 0.7f),
                    background = UIHelpers.MakeBorderedTexture(64, 64,
                        new Color(0.05f, 0.06f, 0.14f, 0.97f),
                        new Color(Styles.HighlightColor.r, Styles.HighlightColor.g,
                                  Styles.HighlightColor.b, 0.6f), 1)
                },
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(8, 8, 6, 6)
            };

            _localStylesBuilt = true;
        }

        private void DrawBuildingPlacementPanel(Entity entity, EntityActionInfo actionInfo)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                EntityInfoPanel.NextPanelX,
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

            // Earlier missing braces stacked both labels during placement —
            // "Left-click to place..." appeared, then "Build Structure"
            // appeared below it as if it were a new heading. (task-060 F-4)
            if (BuilderCommandPanel.IsPlacingBuilding)
                GUILayout.Label("Left-click to place, Right/Esc to cancel", Styles.Header);
            else
                GUILayout.Label("Build Structure", Styles.Header);

            GUILayout.Space(8);

            GUI.enabled = !BuilderCommandPanel.IsPlacingBuilding;

            // Use larger buttons (3 per row) for building icons
            DrawActionGrid(entity, actionInfo.Actions.ToArray(), (button) =>
            {
                BuilderCommandPanel.TriggerBuildingPlacement(button.Id);
            }, overrideButtonSize: 55f);

            GUI.enabled = true;

            GUILayout.EndArea();
        }

        private void DrawUnitTrainingPanel(Entity entity, EntityActionInfo actionInfo)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                EntityInfoPanel.NextPanelX,
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

            GUILayout.Label("Train Units", Styles.Header);
            GUILayout.Space(8);

            DrawActionGrid(entity, actionInfo.Actions.ToArray(), (button) =>
            {
                var em = UnifiedUIManager.GetEntityManager();
                if (!em.Exists(entity)) return;

                // Bazaar Pack button — add pack command instead of training
                if (button.Id == "BazaarPack")
                {
                    if (!em.HasComponent<BazaarPackCommand>(entity))
                        em.AddComponent<BazaarPackCommand>(entity);
                    Event.current.Use();
                    return;
                }

                Faction faction = GameSettings.LocalPlayerFaction;
                if (em.HasComponent<FactionTag>(entity))
                    faction = em.GetComponentData<FactionTag>(entity).Value;

                // Enforce queue limit
                if (em.HasBuffer<TrainQueueItem>(entity))
                {
                    var q = em.GetBuffer<TrainQueueItem>(entity);
                    if (q.Length >= MAX_TRAIN_QUEUE)
                    {
                        PlayerNotificationSystem.Notify("Training queue full");
                        return;
                    }
                }

                // Check population capacity before queuing
                int popCost = PopulationHelper.GetUnitPopulationCost(button.Id.ToString());
                if (!PopulationHelper.HasPopulationCapacity(faction, popCost))
                {
                    PlayerNotificationSystem.Notify("Population cap reached");
                    return;
                }

                // Deduct cost when adding to queue
                if (!FactionEconomy.Spend(em, faction, button.Cost))
                {
                    PlayerNotificationSystem.NotifyError("Not enough resources");
                    return;
                }

                // Add to training queue (via CommandRouter for multiplayer sync)
                CommandRouter.IssueTrain(em, entity, button.Id.ToString());
                Event.current.Use();
            });

            GUILayout.Space(8);

            // Training progress bar
            if (actionInfo.TrainingState.HasValue && actionInfo.TrainingState.Value.IsTraining)
            {
                DrawProgressBar(actionInfo.TrainingState.Value);
                GUILayout.Space(6);
            }

            // Training queue with interactive cancel slots
            if (actionInfo.TrainingState.HasValue)
            {
                DrawInteractiveQueue(entity, actionInfo.TrainingState.Value);
            }

            // ── Age-Up Section (Hall only, Era 1 only) ──
            DrawAgeUpSection(entity);

            GUILayout.EndArea();
        }

        /// <summary>Scroll position for the temple panel (sect list can be long).</summary>
        private Vector2 _templePanelScroll;

        /// <summary>
        /// Draw the Temple of Ridan level-up panel with training section and upgrade button.
        /// Shows training actions, training progress/queue, and a level-up button below.
        /// </summary>
        private void DrawTempleLevelUpPanel(Entity entity, EntityActionInfo actionInfo)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                EntityInfoPanel.NextPanelX,
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
            _templePanelScroll = GUILayout.BeginScrollView(_templePanelScroll);

            // ── Training Section ──
            if (actionInfo.Actions != null && actionInfo.Actions.Count > 0)
            {
                GUILayout.Label("Train Units", Styles.Header);
                GUILayout.Space(4);

                DrawActionGrid(entity, actionInfo.Actions.ToArray(), (button) =>
                {
                    var em = UnifiedUIManager.GetEntityManager();
                    if (!em.Exists(entity)) return;

                    Faction faction = GameSettings.LocalPlayerFaction;
                    if (em.HasComponent<FactionTag>(entity))
                        faction = em.GetComponentData<FactionTag>(entity).Value;

                    // Enforce queue limit
                    if (em.HasBuffer<TrainQueueItem>(entity))
                    {
                        var q = em.GetBuffer<TrainQueueItem>(entity);
                        if (q.Length >= MAX_TRAIN_QUEUE)
                        {
                            PlayerNotificationSystem.Notify("Training queue full");
                            return;
                        }
                    }

                    int popCost = PopulationHelper.GetUnitPopulationCost(button.Id.ToString());
                    if (!PopulationHelper.HasPopulationCapacity(faction, popCost))
                    {
                        PlayerNotificationSystem.Notify("Population cap reached");
                        return;
                    }

                    if (!FactionEconomy.Spend(em, faction, button.Cost))
                    {
                        PlayerNotificationSystem.NotifyError("Not enough resources");
                        return;
                    }

                    CommandRouter.IssueTrain(em, entity, button.Id.ToString());
                    Event.current.Use();
                });

                GUILayout.Space(4);

                // Training progress bar
                if (actionInfo.TrainingState.HasValue && actionInfo.TrainingState.Value.IsTraining)
                {
                    DrawProgressBar(actionInfo.TrainingState.Value);
                    GUILayout.Space(4);
                }

                // Training queue with interactive cancel slots
                if (actionInfo.TrainingState.HasValue)
                    DrawInteractiveQueue(entity, actionInfo.TrainingState.Value);
            }

            // ── Temple Level-Up Section ──
            DrawTempleLevelUpSection(entity);

            // ── Chapel Slot Diagram ──
            DrawTempleChapelSlots(entity);

            // Sect adoption now happens through the expansion slot UI above — no separate panel needed

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Draw the temple level-up button and status.
        /// Shows current level, upgrade cost, and a button to upgrade.
        /// </summary>
        private void DrawTempleLevelUpSection(Entity entity)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;
            if (!em.Exists(entity)) return;
            if (!em.HasComponent<TempleTag>(entity)) return;
            if (!em.HasComponent<TempleLevel>(entity)) return;

            var templeLevel = em.GetComponentData<TempleLevel>(entity);

            GUILayout.Space(10);

            // Separator line
            var sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.4f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(6);

            // Current level display
            string levelText = templeLevel.Level >= TempleLevelConfig.MaxLevel
                ? $"Temple Level {templeLevel.Level} (Maximum)"
                : $"Temple Level {templeLevel.Level}";
            GUILayout.Label(levelText, Styles.Label);

            // Max level reached — no upgrade button
            if (templeLevel.Level >= TempleLevelConfig.MaxLevel)
            {
                GUILayout.Label("All eras unlocked", Styles.SmallLabel);
                return;
            }

            // Under construction — cannot upgrade
            if (em.HasComponent<UnderConstruction>(entity))
            {
                GUILayout.Label("Complete construction first", _requireStyle);
                return;
            }

            // Currently upgrading — show progress bar
            if (em.HasComponent<TempleUpgradeState>(entity))
            {
                var upgrade = em.GetComponentData<TempleUpgradeState>(entity);
                float pct = 1f - (upgrade.Remaining / upgrade.Duration);
                int secs = Mathf.CeilToInt(upgrade.Remaining);
                GUILayout.Label($"Upgrading to Level {upgrade.TargetLevel}... {(int)(pct * 100)}% ({secs}s)", Styles.Label);

                // Progress bar
                var barRect = GUILayoutUtility.GetRect(0, 12, GUILayout.ExpandWidth(true));
                GUI.color = new Color(0.2f, 0.2f, 0.2f);
                GUI.DrawTexture(barRect, Texture2D.whiteTexture);
                GUI.color = Styles.HighlightColor;
                GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * pct, barRect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                return;
            }

            // Must be Era 2 (culture chosen) before first temple upgrade
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            int currentEra = EntityInfoExtractor.GetFactionEra(em, faction);
            if (currentEra < 2)
            {
                GUILayout.Label("Advance to Era 2 first (culture choice)", _requireStyle);
                return;
            }

            int nextLevel = templeLevel.Level + 1;
            int nextEra = TempleLevelConfig.GetEraForLevel(nextLevel);
            var upgradeCost = TempleLevelConfig.GetUpgradeCost(templeLevel.Level);
            int rpGrant = TempleLevelConfig.GetRPGranted(nextLevel);
            float duration = TempleLevelConfig.GetUpgradeDuration(templeLevel.Level);

            bool canAfford = FactionEconomy.CanAfford(em, faction, upgradeCost);

            bool wasEnabled = GUI.enabled;
            if (!canAfford) GUI.enabled = false;

            string buttonText = $"Upgrade to Level {nextLevel} (Era {nextEra}) — {(int)duration}s";
            if (GUILayout.Button(buttonText, _ageUpStyle, GUILayout.Height(36)))
            {
                // Spend resources and start upgrade timer
                if (!FactionEconomy.Spend(em, faction, upgradeCost))
                {
                    PlayerNotificationSystem.NotifyError("Not enough resources");
                }
                else
                {
                    // Start timed upgrade — TempleUpgradeSystem will complete it
                    em.AddComponentData(entity, new TempleUpgradeState
                    {
                        TargetLevel = nextLevel,
                        Duration = duration,
                        Remaining = duration
                    });

                    PlayerNotificationSystem.Notify($"Temple upgrade started ({(int)duration}s)");
                }
            }

            GUI.enabled = wasEnabled;

            // Cost display — Fix #221: use pre-cached colour variants
            if (!upgradeCost.IsZero)
            {
                var costStyle = canAfford ? Styles.CostStyleAffordable : Styles.CostStyleUnaffordable;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Cost: ", costStyle, GUILayout.Width(40));
                ResourceIcons.DrawCostLayout(upgradeCost, 12f, costStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Label($"Grants: +{rpGrant} Religion Points", Styles.SmallLabel);
        }

        // ═══════════════════════════════════════════════════════════════════
        // TEMPLE CHAPEL SLOT DIAGRAM
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw chapel expansion slots as a compact vertical list (BFME2-style).
        /// Empty slots open a sect choice menu that costs RP.
        /// Complete slots show sect name but are NOT selectable.
        /// </summary>
        private void DrawTempleChapelSlots(Entity entity)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;
            if (!em.Exists(entity)) return;
            if (!em.HasComponent<TempleOfRidanTag>(entity)) return;
            if (!em.HasBuffer<TempleChapelSlot>(entity)) return;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            var slots = em.GetBuffer<TempleChapelSlot>(entity);
            if (slots.Length == 0) return;

            GUILayout.Space(10);

            // Separator line
            var sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.4f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(6);

            // Show RP count in header
            int rp = EntityInfoExtractor.GetFactionReligionPoints(em, faction);
            GUILayout.Label($"Expansion Slots  (RP: {rp})", Styles.Header);
            GUILayout.Space(4);

            // ── 8 expansion slots ──
            for (int i = 0; i < slots.Length && i < 8; i++)
            {
                var slot = slots[i];
                GUILayout.BeginHorizontal();

                // Slot number label
                GUILayout.Label($"#{i + 1}", Styles.SmallLabel, GUILayout.Width(24));

                if (slot.State == 0)
                {
                    // Empty slot — clickable to open sect choice
                    GUI.color = new Color(0.4f, 0.4f, 0.4f);
                    if (GUILayout.Button("Choose Sect", GUILayout.Height(20)))
                    {
                        _selectedChapelSlot = (_selectedChapelSlot == i) ? -1 : i;
                    }
                    GUI.color = Color.white;
                }
                else if (slot.State == 1)
                {
                    // Building — progress display
                    float pct = slot.BuildTime > 0 ? slot.BuildProgress / slot.BuildTime : 0f;
                    string sectName = SectConfig.GetDisplayName(slot.SectId.ToString());
                    GUI.color = new Color(0.9f, 0.6f, 0.1f);
                    GUILayout.Label($"{sectName} — {(int)(pct * 100)}%", Styles.SmallLabel);
                    GUI.color = Color.white;
                }
                else if (slot.State == 2)
                {
                    // Complete — green label (NOT selectable, chapel is part of temple)
                    string sectName = SectConfig.GetDisplayName(slot.SectId.ToString());
                    GUI.color = new Color(0.2f, 0.7f, 0.3f);
                    GUILayout.Label(sectName, Styles.Label);
                    GUI.color = Color.white;
                }

                GUILayout.EndHorizontal();
            }

            // ── Sect Choice Menu (when an empty slot is selected) ──
            if (_selectedChapelSlot >= 0 && _selectedChapelSlot < slots.Length)
            {
                var selectedSlot = slots[_selectedChapelSlot];
                if (selectedSlot.State == 0)
                {
                    DrawSectChoiceMenu(entity, faction, em, slots);
                }
                else
                {
                    _selectedChapelSlot = -1;
                }
            }
        }

        /// <summary>
        /// Draw the sect choice menu for a temple expansion slot.
        /// Clicking a sect spends RP (not resources) and starts building the chapel.
        /// Simultaneously adopts the sect via FactionSectState.
        /// </summary>
        private void DrawSectChoiceMenu(Entity temple, Faction faction, EntityManager em,
            DynamicBuffer<TempleChapelSlot> slots)
        {
            GUILayout.Space(6);
            GUILayout.Label($"Choose Sect for Slot #{_selectedChapelSlot + 1}", Styles.Label);

            // Determine faction culture for RP cost calculation
            byte factionCulture = Cultures.None;
            var hallQuery = em.CreateEntityQuery(typeof(HallTag), typeof(FactionTag), typeof(FactionProgress));
            var hallEntities = hallQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < hallEntities.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(hallEntities[i]).Value == faction)
                {
                    factionCulture = em.GetComponentData<FactionProgress>(hallEntities[i]).Culture;
                    break;
                }
            }
            hallEntities.Dispose();

            int currentRP = EntityInfoExtractor.GetFactionReligionPoints(em, faction);

            // Find which sects are already in a slot
            var usedSects = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].State != 0 && slots[i].SectId.ToString().Length > 0)
                    usedSects.Add(slots[i].SectId.ToString());
            }

            GUILayout.Space(4);

            // Show all 12 sects grouped by culture
            string[] cultureNames = { "Alanthor", "Runai", "Feraldis" };
            string[][] cultureSects = {
                SectConfig.AlanthorSects,
                SectConfig.RunaiSects,
                SectConfig.FeraldisSects
            };
            Color[] cultureColors = {
                new Color(0.55f, 0.65f, 0.50f),
                new Color(0.25f, 0.75f, 0.80f),
                new Color(0.70f, 0.18f, 0.15f)
            };
            byte[] cultureIds = { Cultures.Alanthor, Cultures.Runai, Cultures.Feraldis };

            bool anyAvailable = false;

            for (int c = 0; c < 3; c++)
            {
                if (cultureSects[c] == null) continue;

                GUI.color = cultureColors[c];
                GUILayout.Label(cultureNames[c], Styles.Label);
                GUI.color = Color.white;

                foreach (var sectId in cultureSects[c])
                {
                    if (usedSects.Contains(sectId)) continue; // Already built

                    anyAvailable = true;
                    string displayName = SectConfig.GetDisplayName(sectId);
                    string passive = SectConfig.GetPassiveDescription(sectId);

                    // RP cost: 1 if same culture, 3 if cross-culture
                    int rpCost = (cultureIds[c] == factionCulture) ? 1 : 3;
                    bool canAffordRP = currentRP >= rpCost;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"  {displayName}", Styles.Label, GUILayout.Width(140));

                    // RP cost label — Fix #221: use pre-cached colour variants
                    var rpStyle = canAffordRP ? Styles.CostStyleAffordable : Styles.CostStyleUnaffordable;
                    GUILayout.Label($"{rpCost} RP", rpStyle, GUILayout.Width(40));

                    bool wasEnabled = GUI.enabled;
                    if (!canAffordRP) GUI.enabled = false;

                    if (GUILayout.Button("Build", GUILayout.Width(50), GUILayout.Height(20)))
                    {
                        // Spend RP
                        var sectState = FactionSectState.Instance;
                        if (sectState != null)
                        {
                            // Adopt the sect (deducts RP)
                            if (sectState.TryAdopt(faction, sectId))
                            {
                                // Set slot to building state
                                var slot = slots[_selectedChapelSlot];
                                slot.State = 1;
                                slot.SectId = new Unity.Collections.FixedString64Bytes(sectId);
                                slot.BuildProgress = 0f;
                                slot.BuildTime = ChapelBuildTime;
                                slots[_selectedChapelSlot] = slot;

                                _selectedChapelSlot = -1;
                                PlayerNotificationSystem.Notify($"Building {displayName} chapel...");
                            }
                            else
                            {
                                PlayerNotificationSystem.NotifyError("Cannot adopt sect (insufficient RP or already adopted)");
                            }
                        }
                    }

                    GUI.enabled = wasEnabled;
                    GUILayout.EndHorizontal();

                    if (!string.IsNullOrEmpty(passive))
                    {
                        GUILayout.Label($"    {passive}", Styles.SmallLabel);
                    }
                }
            }

            if (!anyAvailable)
            {
                GUILayout.Label("All sects already assigned to slots", _requireStyle);
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Cancel", GUILayout.Height(20)))
            {
                _selectedChapelSlot = -1;
            }
        }

        /// <summary>
        /// Draw a simple line between two points using GUI.DrawTexture.
        /// </summary>
        private static void DrawLine(Vector2 from, Vector2 to, Color color)
        {
            var savedColor = GUI.color;
            GUI.color = color;

            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            float length = Vector2.Distance(from, to);

            var pivot = new Vector2(from.x, from.y + 0.5f);
            GUIUtility.RotateAroundPivot(angle, pivot);
            GUI.DrawTexture(new Rect(from.x, from.y, length, 1f), Texture2D.whiteTexture);
            GUIUtility.RotateAroundPivot(-angle, pivot);

            GUI.color = savedColor;
        }

        /// <summary>
        /// Draw the "Advance to Era 2" button on the Hall if still in Era 1,
        /// or an age-up progress bar if the timer is active.
        /// </summary>
        private void DrawAgeUpSection(Entity entity)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;
            if (!em.Exists(entity)) return;

            // Only for Hall buildings
            if (!em.HasComponent<HallTag>(entity)) return;

            // If age-up timer is active, show progress bar instead of button
            if (em.HasComponent<AgeUpState>(entity))
            {
                DrawAgeUpProgressBar(em, entity);
                return;
            }

            // Only if still Era 1 (no culture chosen yet)
            if (!em.HasComponent<FactionProgress>(entity)) return;
            var progress = em.GetComponentData<FactionProgress>(entity);
            if (progress.Culture != Cultures.None) return;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            GUILayout.Space(10);

            // Separator line
            var sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.4f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(6);

            // Check prerequisites
            string choiceBuilding = BuildingFactory.GetFactionChoiceBuilding(em, faction);
            bool hasChoiceBuilding = choiceBuilding != null;
            bool canAfford = FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost);

            bool canAgeUp = hasChoiceBuilding && canAfford;

            bool wasEnabled = GUI.enabled;
            if (!canAgeUp) GUI.enabled = false;

            if (GUILayout.Button("Advance to Era 2", _ageUpStyle, GUILayout.Height(36)))
            {
                CultureChoicePopup.Show(entity, faction);
            }

            GUI.enabled = wasEnabled;

            // Requirement status
            if (!hasChoiceBuilding)
            {
                GUILayout.Label("Requires: Choice Building (Temple / Vault / Keep)", _requireStyle);
            }
            else if (!canAfford)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Requires: ", _requireStyle, GUILayout.Width(65));
                ResourceIcons.DrawCostLayout(CultureConfig.AgeUpCost, 12f, _requireStyle);
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// Draw age-up progress bar while AgeUpState timer is active on a Hall.
        /// </summary>
        private void DrawAgeUpProgressBar(EntityManager em, Entity entity)
        {
            var ageUp = em.GetComponentData<AgeUpState>(entity);
            float elapsed = ageUp.Duration - ageUp.Remaining;
            float pct = (ageUp.Duration > 0f) ? Mathf.Clamp01(elapsed / ageUp.Duration) : 1f;
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, ageUp.Remaining));

            string cultureName = CultureConfig.GetName(ageUp.Culture);

            GUILayout.Space(10);

            // Separator line
            var sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.4f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.Space(6);

            // Label
            GUILayout.Label($"Advancing to Era 2 ({cultureName})  {seconds}s", Styles.Label);
            GUILayout.Space(4);

            // Progress bar background
            var barRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);

            // Progress bar fill (golden)
            var fillRect = new Rect(barRect.x, barRect.y, barRect.width * pct, barRect.height);
            GUI.color = UIHelpers.ThemeGold;
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

            // Percentage text centered on bar
            GUI.color = Color.white;
            GUI.Label(barRect, $"{Mathf.RoundToInt(pct * 100f)}%",
                new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                });

            GUI.color = Color.white;
        }

        /// <summary>
        /// Panel for buildings that support both unit training and technology research (e.g. Barracks, Hall).
        /// Training section on top, research section below with a separator.
        /// </summary>
        private void DrawUnitTrainingAndResearchPanel(Entity entity, EntityActionInfo actionInfo)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                EntityInfoPanel.NextPanelX,
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

            // ── Training Section ──
            if (actionInfo.Actions != null && actionInfo.Actions.Count > 0)
            {
                GUILayout.Label("Train Units", Styles.Header);
                GUILayout.Space(4);

                DrawActionGrid(entity, actionInfo.Actions.ToArray(), (button) =>
                {
                    var em = UnifiedUIManager.GetEntityManager();
                    if (!em.Exists(entity)) return;

                    Faction faction = GameSettings.LocalPlayerFaction;
                    if (em.HasComponent<FactionTag>(entity))
                        faction = em.GetComponentData<FactionTag>(entity).Value;

                    // Enforce queue limit
                    if (em.HasBuffer<TrainQueueItem>(entity))
                    {
                        var q = em.GetBuffer<TrainQueueItem>(entity);
                        if (q.Length >= MAX_TRAIN_QUEUE)
                        {
                            PlayerNotificationSystem.Notify("Training queue full");
                            return;
                        }
                    }

                    int popCost = PopulationHelper.GetUnitPopulationCost(button.Id.ToString());
                    if (!PopulationHelper.HasPopulationCapacity(faction, popCost))
                    {
                        PlayerNotificationSystem.Notify("Population cap reached");
                        return;
                    }

                    if (!FactionEconomy.Spend(em, faction, button.Cost))
                    {
                        PlayerNotificationSystem.NotifyError("Not enough resources");
                        return;
                    }

                    CommandRouter.IssueTrain(em, entity, button.Id.ToString());
                    Event.current.Use();
                });

                GUILayout.Space(4);

                // Training progress bar
                if (actionInfo.TrainingState.HasValue && actionInfo.TrainingState.Value.IsTraining)
                {
                    DrawProgressBar(actionInfo.TrainingState.Value);
                    GUILayout.Space(4);
                }

                // Training queue with interactive cancel slots
                if (actionInfo.TrainingState.HasValue)
                    DrawInteractiveQueue(entity, actionInfo.TrainingState.Value);
            }

            // ── Age-Up Section (Hall only) ──
            DrawAgeUpSection(entity);

            // ── Research Section ──
            var researchActions = EntityActionExtractor.GetResearchActions(entity, UnifiedUIManager.GetEntityManager());
            bool hasResearchProgress = actionInfo.ResearchState.HasValue && actionInfo.ResearchState.Value.IsResearching;

            if (researchActions.Count > 0 || hasResearchProgress)
            {
                GUILayout.Space(8);

                // Separator line
                var sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
                GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.4f);
                GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUILayout.Space(6);
                GUILayout.Label("Research", Styles.Header);
                GUILayout.Space(4);

                // Research buttons
                if (researchActions.Count > 0)
                {
                    DrawActionGrid(entity, researchActions.ToArray(), (button) =>
                    {
                        var em = UnifiedUIManager.GetEntityManager();
                        if (!em.Exists(entity)) return;

                        Faction faction = GameSettings.LocalPlayerFaction;
                        if (em.HasComponent<FactionTag>(entity))
                            faction = em.GetComponentData<FactionTag>(entity).Value;

                        if (!FactionEconomy.Spend(em, faction, button.Cost))
                        {
                            PlayerNotificationSystem.NotifyError("Not enough resources");
                            return;
                        }

                        if (em.HasBuffer<ResearchQueueItem>(entity))
                        {
                            var queue = em.GetBuffer<ResearchQueueItem>(entity);
                            queue.Add(new ResearchQueueItem
                            {
                                TechId = new Unity.Collections.FixedString64Bytes(button.Id)
                            });
                            Event.current.Use();
                        }
                    });
                }

                GUILayout.Space(4);

                // Research progress bar
                if (hasResearchProgress)
                {
                    var rInfo = actionInfo.ResearchState.Value;
                    DrawResearchProgressBar(rInfo);
                    GUILayout.Space(4);
                }

                // Research queue
                if (actionInfo.ResearchState.HasValue && actionInfo.ResearchState.Value.Queue != null
                    && actionInfo.ResearchState.Value.Queue.Length > 0)
                {
                    DrawResearchQueue(actionInfo.ResearchState.Value.Queue);
                }
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// Draw a progress bar for active research.
        /// </summary>
        private void DrawResearchProgressBar(ResearchInfo info)
        {
            GUILayout.Label($"Researching: {info.CurrentTechName}", Styles.Label);

            var rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));

            // Dark navy background
            GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Blue-ish fill (to distinguish from golden training bar)
            GUI.color = new Color(0.30f, 0.55f, 0.85f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * info.Progress, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;

            var timeStyle = new GUIStyle(Styles.SmallLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, $"{info.TimeRemaining:F1}s", timeStyle);
        }

        private void DrawActionGrid(Entity entity, ActionButton[] actions, System.Action<ActionButton> onClick,
            float overrideButtonSize = 0f)
        {
            if (actions == null || actions.Length == 0)
            {
                GUILayout.Label("No actions available", Styles.SmallLabel);
                return;
            }

            float btnSize = overrideButtonSize > 0f ? overrideButtonSize : ButtonSize;
            int buttonsPerRow = Mathf.FloorToInt((PanelWidth - PanelPadding * 2f) / (btnSize + ButtonSpacing));
            if (buttonsPerRow < 1) buttonsPerRow = 1;
            int row = 0;

            GUILayout.BeginHorizontal();

            for (int i = 0; i < actions.Length; i++)
            {
                var button = actions[i];

                // Disable if can't afford or requirements not met
                bool wasEnabled = GUI.enabled;
                if (!button.CanAfford || !button.Enabled) GUI.enabled = false;

                // If icon available, show empty button + icon overlay; otherwise show text label
                string label = button.Icon != null ? "" : button.Label;

                if (GUILayout.Button(label, Styles.IconButton,
                    GUILayout.Width(btnSize), GUILayout.Height(btnSize)))
                {
                    onClick?.Invoke(button);
                }

                // Check hover for tooltip (works even on disabled buttons)
                var btnRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(button.Tooltip))
                {
                    var mousePos = Event.current.mousePosition;
                    if (btnRect.Contains(mousePos))
                    {
                        _hoveredTooltip = button.Tooltip;
                        _hoveredCost = button.Cost;
                    }
                }

                // Draw icon on top of button, filling the full area
                if (button.Icon != null)
                {
                    // Slight inset so the button border is visible
                    var iconRect = new Rect(btnRect.x + 2, btnRect.y + 2, btnRect.width - 4, btnRect.height - 4);
                    GUI.DrawTexture(iconRect, button.Icon, ScaleMode.ScaleToFit);
                }

                GUI.enabled = wasEnabled;

                row++;
                if (row >= buttonsPerRow && i < actions.Length - 1)
                {
                    row = 0;
                    GUILayout.EndHorizontal();
                    GUILayout.Space(ButtonSpacing);
                    GUILayout.BeginHorizontal();
                }
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draw a floating tooltip box above the action panel.
        /// Called at the end of OnGUI, outside any BeginArea, so it isn't clipped.
        /// </summary>
        private void DrawFloatingTooltip()
        {
            if (string.IsNullOrEmpty(_hoveredTooltip)) return;
            if (!_localStylesBuilt) return;

            // Split tooltip into lines — replace "Cost: ..." line with icon rendering
            string[] lines = _hoveredTooltip.Split('\n');

            // Strip cost line from the text for measurement (we'll draw it separately with icons)
            int costLineIndex = -1;
            var textWithoutCost = new System.Text.StringBuilder(128);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Cost:"))
                {
                    costLineIndex = i;
                    textWithoutCost.Append("Cost: \n"); // placeholder line for spacing
                }
                else
                {
                    textWithoutCost.Append(lines[i]);
                    if (i < lines.Length - 1) textWithoutCost.Append('\n');
                }
            }

            float maxWidth = 280f;
            float width = maxWidth;
            var textContent = new GUIContent(textWithoutCost.ToString());
            float height = _tooltipStyle.CalcHeight(textContent, width);

            // Position above the panel
            float x = PanelRect.x;
            float y = PanelRect.y - height - 4f;

            // Clamp to screen
            if (x + width > Screen.width) x = Screen.width - width - 4f;
            if (y < 0) y = 0;

            var tooltipRect = new Rect(x, y, width, height);

            GUI.depth = -100;

            // Draw background box
            GUI.Box(tooltipRect, "", _tooltipStyle);

            // Draw each line
            float lineHeight = _tooltipStyle.lineHeight + 2f;
            float lineY = tooltipRect.y + _tooltipStyle.padding.top;
            float lineX = tooltipRect.x + _tooltipStyle.padding.left;
            float innerWidth = width - _tooltipStyle.padding.left - _tooltipStyle.padding.right;

            for (int i = 0; i < lines.Length; i++)
            {
                if (i == costLineIndex && !_hoveredCost.IsZero)
                {
                    // Draw "Cost: " label then icons
                    var costLabelStyle = new GUIStyle(_tooltipStyle)
                    {
                        padding = new RectOffset(0, 0, 0, 0),
                        alignment = TextAnchor.MiddleLeft
                    };
                    GUI.Label(new Rect(lineX, lineY, 40f, lineHeight), "Cost:", costLabelStyle);
                    float iconX = lineX + 40f;
                    float iconSize = lineHeight - 2f;

                    // Get current resources to color-code affordability
                    FactionResources available = default;
                    var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (world != null && world.IsCreated)
                    {
                        FactionEconomy.TryGetResources(world.EntityManager,
                            GameSettings.LocalPlayerFaction, out available);
                    }

                    void DrawCostEntry(string resName, int needed, int have)
                    {
                        if (needed <= 0) return;
                        ResourceIcons.DrawIcon(iconX, lineY + 1f, resName, iconSize);
                        iconX += iconSize + 1f;
                        var valStyle = new GUIStyle(costLabelStyle)
                        {
                            normal = { textColor = have >= needed
                                ? new Color(0.72f, 0.90f, 0.72f)
                                : new Color(1f, 0.33f, 0.33f) }
                        };
                        string val = needed.ToString();
                        float valW = valStyle.CalcSize(new GUIContent(val)).x;
                        GUI.Label(new Rect(iconX, lineY, valW + 4f, lineHeight), val, valStyle);
                        iconX += valW + 8f;
                    }

                    DrawCostEntry("Supplies", _hoveredCost.Supplies, available.Supplies);
                    DrawCostEntry("Iron", _hoveredCost.Iron, available.Iron);
                    DrawCostEntry("Crystal", _hoveredCost.Crystal, available.Crystal);
                    DrawCostEntry("Veilsteel", _hoveredCost.Veilsteel, available.Veilsteel);
                    DrawCostEntry("Glow", _hoveredCost.Glow, available.Glow);
                }
                else
                {
                    var lineStyle = new GUIStyle(_tooltipStyle)
                    {
                        padding = new RectOffset(0, 0, 0, 0),
                        alignment = TextAnchor.MiddleLeft
                    };
                    GUI.Label(new Rect(lineX, lineY, innerWidth, lineHeight), lines[i], lineStyle);
                }
                lineY += lineHeight;
            }

            GUI.depth = 0;
        }

        private void DrawProgressBar(TrainingInfo info)
        {
            GUILayout.Label($"Training: {info.CurrentUnitId}", Styles.Label);

            var rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));

            // Dark navy background
            GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Golden fill
            GUI.color = Styles.HighlightColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * info.Progress, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;

            // Time remaining
            var timeStyle = new GUIStyle(Styles.SmallLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, $"{info.TimeRemaining:F1}s", timeStyle);
        }

        /// <summary>
        /// Draw interactive training queue with clickable slots.
        /// Shows all queue items as visual slots (including currently training).
        /// Right-click on a queued (non-training) slot cancels it and refunds resources.
        /// </summary>
        private void DrawInteractiveQueue(Entity entity, TrainingInfo info)
        {
            int totalInQueue = info.QueueCapacity; // includes currently training item
            GUILayout.Label($"Queue: {totalInQueue}/{MAX_TRAIN_QUEUE}", Styles.SmallLabel);

            // Build the full slot list: currently training + pending queue
            // info.Queue excludes the currently training item, so reconstruct full list
            string[] allSlots = new string[totalInQueue];
            int idx = 0;
            if (info.IsTraining && info.CurrentUnitId != null)
            {
                allSlots[0] = info.CurrentUnitId;
                idx = 1;
            }
            if (info.Queue != null)
            {
                for (int i = 0; i < info.Queue.Length && idx < allSlots.Length; i++, idx++)
                    allSlots[idx] = info.Queue[i];
            }

            GUILayout.BeginHorizontal();

            for (int slot = 0; slot < MAX_TRAIN_QUEUE; slot++)
            {
                bool occupied = slot < totalInQueue && allSlots[slot] != null;
                bool isTrainingSlot = slot == 0 && info.IsTraining;

                // Reserve a rect for the slot
                var slotRect = GUILayoutUtility.GetRect(QueueSlotSize, QueueSlotSize,
                    GUILayout.Width(QueueSlotSize), GUILayout.Height(QueueSlotSize));

                if (occupied)
                {
                    // Background: golden for training slot, darker for queued
                    if (isTrainingSlot)
                    {
                        GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.6f);
                    }
                    else
                    {
                        GUI.color = new Color(0.15f, 0.18f, 0.30f, 0.9f);
                    }
                    GUI.DrawTexture(slotRect, Texture2D.whiteTexture);

                    // Border
                    GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.5f);
                    DrawSlotBorder(slotRect, 1);

                    GUI.color = Color.white;

                    // Unit name abbreviation (first 3 chars)
                    string abbrev = allSlots[slot].Length > 3
                        ? allSlots[slot].Substring(0, 3)
                        : allSlots[slot];
                    var slotLabelStyle = new GUIStyle(Styles.SmallLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 10,
                        normal = { textColor = isTrainingSlot ? Color.white : new Color(0.9f, 0.88f, 0.82f) }
                    };
                    GUI.Label(slotRect, abbrev, slotLabelStyle);

                    // Right-click to cancel (only for non-training slots)
                    if (!isTrainingSlot && Event.current.type == EventType.MouseDown
                        && Event.current.button == 1 && slotRect.Contains(Event.current.mousePosition))
                    {
                        CancelQueueItem(entity, slot);
                        Event.current.Use();
                    }

                    // Tooltip on hover for occupied slots
                    if (slotRect.Contains(Event.current.mousePosition))
                    {
                        string tip = isTrainingSlot
                            ? $"Training: {allSlots[slot]}"
                            : $"{allSlots[slot]} (right-click to cancel)";
                        GUI.Label(new Rect(Event.current.mousePosition.x + 12,
                            Event.current.mousePosition.y - 16, 180, 20), tip, Styles.SmallLabel);
                    }
                }
                else
                {
                    // Empty slot — dark navy outline
                    GUI.color = new Color(0.08f, 0.10f, 0.20f, 0.6f);
                    GUI.DrawTexture(slotRect, Texture2D.whiteTexture);
                    GUI.color = new Color(0.3f, 0.3f, 0.4f, 0.4f);
                    DrawSlotBorder(slotRect, 1);
                    GUI.color = Color.white;
                }

                GUILayout.Space(QueueSlotSpacing);
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Cancel a training queue item at the given buffer index and refund its cost.
        /// Index 0 is the currently training item (should not be cancelled via this method).
        /// </summary>
        private static void CancelQueueItem(Entity entity, int bufferIndex)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager)) || !em.Exists(entity)) return;
            if (!em.HasBuffer<TrainQueueItem>(entity)) return;

            var queue = em.GetBuffer<TrainQueueItem>(entity);
            if (bufferIndex < 0 || bufferIndex >= queue.Length) return;

            // Don't cancel the currently training item (index 0 when busy)
            if (bufferIndex == 0 && em.HasComponent<TrainingState>(entity))
            {
                var ts = em.GetComponentData<TrainingState>(entity);
                if (ts.Busy != 0) return;
            }

            string unitId = queue[bufferIndex].UnitId.ToString();

            // Refund the unit's cost
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            var cost = EntityActionExtractor.GetUnitCost(unitId);
            if (!cost.IsZero)
            {
                FactionEconomy.Add(em, faction, cost);
            }
            else
            {
            }

            queue.RemoveAt(bufferIndex);
        }

        /// <summary>
        /// Draw a 1px border around a rect.
        /// </summary>
        private static void DrawSlotBorder(Rect rect, int width)
        {
            // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, width), Texture2D.whiteTexture);
            // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - width, rect.width, width), Texture2D.whiteTexture);
            // Left
            GUI.DrawTexture(new Rect(rect.x, rect.y, width, rect.height), Texture2D.whiteTexture);
            // Right
            GUI.DrawTexture(new Rect(rect.xMax - width, rect.y, width, rect.height), Texture2D.whiteTexture);
        }

        /// <summary>
        /// Simple text-based queue display for research (non-interactive).
        /// </summary>
        private void DrawResearchQueue(string[] queue)
        {
            if (queue.Length == 0) return;

            GUILayout.Label($"Queue ({queue.Length}):", Styles.SmallLabel);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Mathf.Min(queue.Length, 8); i++)
            {
                GUILayout.Label(queue[i], Styles.SmallLabel, GUILayout.Width(60));
            }
            if (queue.Length > 8)
                GUILayout.Label($"+{queue.Length - 8}", Styles.SmallLabel);
            GUILayout.EndHorizontal();
        }

        private static readonly string[] VaultResourceNames = { "None", "Supplies", "Iron", "Crystal", "Veilsteel", "Glow" };

        /// <summary>Currently selected resource in the vault dropdown (1-5). Persists across frames.</summary>
        private static int _vaultSelectedResource = 1;

        private void DrawVaultPanel(Entity entity)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                EntityInfoPanel.NextPanelX,
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

            GUILayout.Label("Vault of Almiérra", Styles.Header);
            GUILayout.Space(4);

            var em = UnifiedUIManager.GetEntityManager();
            if (!em.Exists(entity) || !em.HasComponent<VaultStorage>(entity))
            {
                GUILayout.Label("Vault not available", Styles.SmallLabel);
                GUILayout.EndArea();
                return;
            }

            var vault = em.GetComponentData<VaultStorage>(entity);
            var faction = em.HasComponent<FactionTag>(entity)
                ? em.GetComponentData<FactionTag>(entity).Value
                : GameSettings.LocalPlayerFaction;

            // Interest rate display
            GUILayout.Label($"Interest: {vault.InterestRate * 100f:F0}% per minute (compound)", Styles.Label);
            GUILayout.Space(2);

            // Current storage
            if (vault.ResourceType > 0 && vault.ResourceType < VaultResourceNames.Length)
            {
                string resName = VaultResourceNames[vault.ResourceType];
                GUILayout.BeginHorizontal();
                GUILayout.Label("Stored: ", Styles.Label, GUILayout.Width(50));
                GUILayout.Label($"{(int)vault.StoredAmount}", Styles.Label, GUILayout.Width(50));
                ResourceIcons.DrawLayoutIcon(resName, 13f);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Stored: Empty", Styles.Label);
            }

            // Lock status
            bool isLocked = vault.LockTimer > 0f;
            if (isLocked)
            {
                int lockMin = (int)(vault.LockTimer / 60f);
                int lockSec = (int)(vault.LockTimer % 60f);
                var lockStyle = new GUIStyle(Styles.Label)
                {
                    normal = { textColor = new Color(1f, 0.4f, 0.4f) }
                };
                GUILayout.Label($"LOCKED — {lockMin}:{lockSec:D2} remaining", lockStyle);
            }
            else
            {
                var unlockStyle = new GUIStyle(Styles.Label)
                {
                    normal = { textColor = new Color(0.4f, 1f, 0.4f) }
                };
                GUILayout.Label("Unlocked", unlockStyle);
            }

            GUILayout.Space(8);

            // If vault already has a resource stored, lock the dropdown to that type
            int activeResource = vault.ResourceType > 0 ? vault.ResourceType : _vaultSelectedResource;
            if (vault.ResourceType > 0)
                _vaultSelectedResource = vault.ResourceType;

            // Resource selector — cycle button
            bool dropdownLocked = vault.ResourceType > 0;
            bool wasEnabledDd = GUI.enabled;
            if (dropdownLocked) GUI.enabled = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Resource:", Styles.Label, GUILayout.Width(80));
            if (GUILayout.Button($"◄", Styles.IconButton, GUILayout.Width(28), GUILayout.Height(26)))
            {
                _vaultSelectedResource--;
                if (_vaultSelectedResource < 1) _vaultSelectedResource = 5;
            }
            ResourceIcons.DrawLayoutIconValue(VaultResourceNames[_vaultSelectedResource],
                VaultResourceNames[_vaultSelectedResource], 13f, Styles.Label, 90f);
            if (GUILayout.Button($"►", Styles.IconButton, GUILayout.Width(28), GUILayout.Height(26)))
            {
                _vaultSelectedResource++;
                if (_vaultSelectedResource > 5) _vaultSelectedResource = 1;
            }
            GUILayout.EndHorizontal();

            GUI.enabled = wasEnabledDd;

            GUILayout.Space(6);

            // Deposit buttons — 100, 200, 500
            GUILayout.Label("Deposit:", Styles.Label);
            GUILayout.BeginHorizontal();
            int[] amounts = { 100, 200, 500 };
            for (int i = 0; i < amounts.Length; i++)
            {
                int amount = amounts[i];
                bool canDeposit = !isLocked
                    && (vault.ResourceType == 0 || vault.ResourceType == _vaultSelectedResource)
                    && FactionEconomy.CanAfford(em, faction, ResourceTypeToCost(_vaultSelectedResource, amount));

                bool wasEnabled = GUI.enabled;
                if (!canDeposit) GUI.enabled = false;

                if (GUILayout.Button($"{amount}", Styles.IconButton, GUILayout.Height(32)))
                {
                    VaultAction(em, entity, faction, _vaultSelectedResource, amount, true);
                }
                GUI.enabled = wasEnabled;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Withdraw All button
            bool canWithdraw = !isLocked && vault.ResourceType > 0 && vault.StoredAmount > 0f;
            bool wasEnabledW = GUI.enabled;
            if (!canWithdraw) GUI.enabled = false;
            if (GUILayout.Button($"Withdraw All ({(int)vault.StoredAmount})", Styles.IconButton, GUILayout.Height(32)))
            {
                VaultAction(em, entity, faction, vault.ResourceType, (int)vault.StoredAmount, false);
            }
            GUI.enabled = wasEnabledW;

            GUILayout.EndArea();
        }

        private static void VaultAction(EntityManager em, Entity entity, Faction faction,
            int resourceType, int amount, bool isDeposit)
        {
            if (!em.HasComponent<VaultStorage>(entity)) return;
            var vault = em.GetComponentData<VaultStorage>(entity);

            if (isDeposit)
            {
                // Take from faction bank
                var cost = ResourceTypeToCost(resourceType, amount);
                if (!FactionEconomy.Spend(em, faction, cost)) return;

                vault.ResourceType = resourceType;
                vault.StoredAmount += amount;
            }
            else
            {
                // Give back to faction bank
                int withdrawAmount = (int)vault.StoredAmount;
                if (withdrawAmount <= 0) return;

                FactionEconomy.Add(em, faction, ResourceTypeToCost(resourceType, withdrawAmount));
                vault.StoredAmount = 0f;
                vault.ResourceType = 0;
            }

            vault.LockTimer = vault.LockDuration;
            em.SetComponentData(entity, vault);
        }

        private static Cost ResourceTypeToCost(int type, int amount)
        {
            return type switch
            {
                1 => Cost.Of(supplies: amount),
                2 => Cost.Of(iron: amount),
                3 => Cost.Of(crystal: amount),
                4 => Cost.Of(veilsteel: amount),
                5 => Cost.Of(glow: amount),
                _ => default
            };
        }

        private string FormatShortCost(Cost cost)
        {
            if (cost.Supplies > 0) return $"{cost.Supplies}S";
            if (cost.Iron > 0) return $"{cost.Iron}Fe";
            return "";
        }

        /// <summary>
        /// Check if pointer is over this panel.
        /// </summary>
        // ═══════════════════════════════════════════════════════════════════════
        // BATTALION STANCE PANEL
        // ═══════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════
        // WALL INSTANCE UPGRADE PANEL
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Panel for the Bazaar Wagon — shows an Unpack button to convert back to building.
        /// </summary>
        private void DrawBazaarWagonPanel(Entity entity)
        {
            PanelVisible = true;

            float panelHeight = 100f;
            var panelRect = new Rect(
                Screen.width - PanelWidth - PanelPadding,
                Screen.height * 0.5f - panelHeight * 0.5f,
                PanelWidth,
                panelHeight);
            PanelRect = panelRect;

            GUI.Box(panelRect, "", Styles.PanelBox);
            GUILayout.BeginArea(new Rect(
                panelRect.x + _padding.left,
                panelRect.y + _padding.top,
                panelRect.width - _padding.horizontal,
                panelRect.height - _padding.vertical));

            GUILayout.Label("Bazaar Wagon", Styles.Header);
            GUILayout.Space(8);

            if (GUILayout.Button("Unpack Bazaar", Styles.IconButton, GUILayout.Height(28)))
            {
                var em = UnifiedUIManager.GetEntityManager();
                if (em.Exists(entity) && !em.HasComponent<BazaarUnpackCommand>(entity))
                {
                    em.AddComponent<BazaarUnpackCommand>(entity);
                }
                Event.current.Use();
            }

            GUILayout.EndArea();
        }

        private void DrawWallUpgradePanel(Entity entity)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                EntityInfoPanel.NextPanelX,
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

            var em = UnifiedUIManager.GetEntityManager();
            if (!em.Exists(entity) || !em.HasComponent<WallInstanceTag>(entity))
            {
                GUILayout.Label("Wall not available", Styles.SmallLabel);
                GUILayout.EndArea();
                return;
            }

            // Show upgrade progress if upgrading
            if (em.HasComponent<WallUpgradeState>(entity))
            {
                var state = em.GetComponentData<WallUpgradeState>(entity);
                string typeName = state.UpgradeType == 1 ? "Tower" : "Gate";
                float progress = 1f - (state.Remaining / state.Duration);
                GUILayout.Label($"Upgrading to {typeName}...", Styles.Header);
                GUILayout.Space(4);

                var barRect = GUILayoutUtility.GetRect(innerRect.width - 10, 16);
                GUI.Box(barRect, "");
                var fillRect = new Rect(barRect.x, barRect.y, barRect.width * progress, barRect.height);
                GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
                GUILayout.Label($"{progress * 100f:F0}%", Styles.SmallLabel);

                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Upgrade Wall", Styles.Header);
            GUILayout.Space(4);

            var faction = em.HasComponent<FactionTag>(entity)
                ? em.GetComponentData<FactionTag>(entity).Value
                : GameSettings.LocalPlayerFaction;

            // Upgrade to Tower button
            BuildCosts.TryGet("Alanthor_WallTower", out var towerCost);
            bool canAffordTower = FactionEconomy.CanAfford(em, faction, towerCost);
            string towerLabel = $"Tower ({towerCost.Supplies}S {towerCost.Iron}I)";

            GUI.enabled = canAffordTower;
            if (GUILayout.Button(towerLabel, GUILayout.Height(28)))
            {
                if (FactionEconomy.Spend(em, faction, towerCost))
                {
                    em.AddComponentData(entity, new WallUpgradeState
                    {
                        UpgradeType = 1,
                        Duration = 10f,
                        Remaining = 10f
                    });
                }
            }
            GUI.enabled = true;

            GUILayout.Space(2);

            // Upgrade to Gate button
            BuildCosts.TryGet("Alanthor_WallGate", out var gateCost);
            bool canAffordGate = FactionEconomy.CanAfford(em, faction, gateCost);
            string gateLabel = $"Gate ({gateCost.Supplies}S {gateCost.Iron}I)";

            GUI.enabled = canAffordGate;
            if (GUILayout.Button(gateLabel, GUILayout.Height(28)))
            {
                if (FactionEconomy.Spend(em, faction, gateCost))
                {
                    em.AddComponentData(entity, new WallUpgradeState
                    {
                        UpgradeType = 2,
                        Duration = 8f,
                        Remaining = 8f
                    });
                }
            }
            GUI.enabled = true;

            GUILayout.EndArea();
        }

        private void DrawStancePanel(Entity entity, EntityManager em)
        {
            // Resolve to leader entity
            Entity leader = Entity.Null;
            if (em.HasComponent<BattalionLeader>(entity))
            {
                leader = entity;
            }
            else if (em.HasComponent<BattalionMemberData>(entity))
            {
                leader = em.GetComponentData<BattalionMemberData>(entity).Leader;
            }

            if (leader == Entity.Null || !em.Exists(leader)) return;
            if (!em.HasComponent<BattalionStanceData>(leader)) return;

            var currentStance = em.GetComponentData<BattalionStanceData>(leader).Value;

            PanelVisible = true;

            var panelRect = new Rect(
                EntityInfoPanel.NextPanelX,
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

            GUILayout.Label("Battalion Stance", Styles.Header);
            GUILayout.Space(8);

            // Current stance display
            string stanceLabel = currentStance switch
            {
                BattalionStance.Defensive => "Defensive",
                BattalionStance.Default => "Default",
                BattalionStance.Aggressive => "Aggressive",
                _ => "Unknown"
            };
            GUILayout.Label($"Current: {stanceLabel}", Styles.Label);
            GUILayout.Space(8);

            // 3 horizontal stance buttons
            GUILayout.BeginHorizontal();

            float btnWidth = (innerRect.width - 12f) / 3f;

            // Aggressive button (D key — BFME2 layout)
            DrawStanceButton("[D] Aggressive", BattalionStance.Aggressive, currentStance, leader, em, btnWidth);
            GUILayout.Space(6);
            // Default / Standard button
            DrawStanceButton("[F] Standard", BattalionStance.Default, currentStance, leader, em, btnWidth);
            GUILayout.Space(6);
            // Defensive button (G key — BFME2 layout)
            DrawStanceButton("[G] Defensive", BattalionStance.Defensive, currentStance, leader, em, btnWidth);

            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Stance description
            string desc = currentStance switch
            {
                BattalionStance.Defensive => "Members hold position and only return fire when attacked.",
                BattalionStance.Default => "Members auto-engage enemies within range but stay near formation.",
                BattalionStance.Aggressive => "Members pursue enemies without distance limits.",
                _ => ""
            };
            GUILayout.Label(desc, Styles.SmallLabel);

            GUILayout.EndArea();
        }

        private void DrawStanceButton(string label, BattalionStance stance, BattalionStance current,
            Entity leader, EntityManager em, float width)
        {
            bool isActive = stance == current;

            // Golden highlight for active stance
            var prevBg = GUI.backgroundColor;
            if (isActive)
                GUI.backgroundColor = Styles.HighlightColor;

            if (GUILayout.Button(label, Styles.IconButton, GUILayout.Width(width), GUILayout.Height(28f)))
            {
                CommandRouter.IssueStanceChange(em, leader, stance);
                BuilderCommandPanel.SuppressClicksThisFrame = true;
            }

            GUI.backgroundColor = prevBg;
        }

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
