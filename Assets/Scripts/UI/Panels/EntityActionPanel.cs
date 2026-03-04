// File: Assets/Scripts/UI/Panels/EntityActionPanel.cs
// Action buttons panel — Dark Navy + Golden theme

using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.UI;
using TheWaningBorder.Core;
using TheWaningBorder.UI.Common;

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
        private const float PanelHeight = 260f;
        private const float PanelPadding = 10f;
        private const float ButtonSize = 64f;
        private const float ButtonSpacing = 8f;

        private GUIStyle _boxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _ageUpStyle;
        private GUIStyle _requireStyle;
        private RectOffset _padding;
        private bool _stylesInit = false;

        void Awake()
        {
            _padding = new RectOffset(10, 10, 10, 10);
        }

        void OnGUI()
        {
            PanelVisible = false;

            // Only show actions for own entities, not enemy
            if (!UnifiedUIManager.IsSelectionOwnedByPlayer()) return;

            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null) return;

            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            var actionInfo = EntityActionExtractor.GetActionInfo(entity, em);

            if (actionInfo.Type == ActionType.None) return;

            InitStyles();

            switch (actionInfo.Type)
            {
                case ActionType.BuildingPlacement:
                    DrawBuildingPlacementPanel(entity, actionInfo);
                    break;

                case ActionType.UnitTraining:
                    DrawUnitTrainingPanel(entity, actionInfo);
                    break;

                case ActionType.VaultManagement:
                    DrawVaultPanel(entity);
                    break;
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

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.68f, 0.60f) }
            };

            _ageUpStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.83f, 0.66f, 0.26f) },
                hover = { textColor = Color.white }
            };

            _requireStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.5f, 0.3f) }
            };

            _stylesInit = true;
        }

        private void DrawBuildingPlacementPanel(Entity entity, EntityActionInfo actionInfo)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                PanelPadding + 300f + PanelPadding,
                Screen.height - PanelHeight - PanelPadding,
                PanelWidth,
                PanelHeight
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

            if (BuilderCommandPanel.IsPlacingBuilding)
                GUILayout.Label("Left-click to place, Right/Esc to cancel", _headerStyle);
            else
                GUILayout.Label("Build Structure", _headerStyle);

            GUILayout.Space(8);

            GUI.enabled = !BuilderCommandPanel.IsPlacingBuilding;

            DrawActionGrid(entity, actionInfo.Actions.ToArray(), (button) =>
            {
                BuilderCommandPanel.TriggerBuildingPlacement(button.Id);
            });

            GUI.enabled = true;

            GUILayout.EndArea();
        }

        private void DrawUnitTrainingPanel(Entity entity, EntityActionInfo actionInfo)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                PanelPadding + 300f + PanelPadding,
                Screen.height - PanelHeight - PanelPadding,
                PanelWidth,
                PanelHeight
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

            GUILayout.Label("Train Units", _headerStyle);
            GUILayout.Space(8);

            DrawActionGrid(entity, actionInfo.Actions.ToArray(), (button) =>
            {
                var em = UnifiedUIManager.GetEntityManager();
                if (!em.Exists(entity)) return;

                Faction faction = GameSettings.LocalPlayerFaction;
                if (em.HasComponent<FactionTag>(entity))
                    faction = em.GetComponentData<FactionTag>(entity).Value;

                // Check population capacity before queuing
                int popCost = PopulationHelper.GetUnitPopulationCost(button.Id.ToString());
                if (!PopulationHelper.HasPopulationCapacity(faction, popCost))
                {
                    Debug.LogWarning("Population limit reached — cannot train more units");
                    return;
                }

                // Deduct cost when adding to queue
                if (!FactionEconomy.Spend(em, faction, button.Cost))
                {
                    Debug.LogWarning($"Cannot afford to train {button.Id}");
                    return;
                }

                // Add to training queue
                if (em.HasBuffer<TrainQueueItem>(entity))
                {
                    var queue = em.GetBuffer<TrainQueueItem>(entity);
                    queue.Add(new TrainQueueItem { UnitId = button.Id });
                    Debug.Log($"Queued {button.Id} for training");
                    Event.current.Use();
                }
            });

            GUILayout.Space(8);

            // Training progress bar
            if (actionInfo.TrainingState.HasValue && actionInfo.TrainingState.Value.IsTraining)
            {
                DrawProgressBar(actionInfo.TrainingState.Value);
                GUILayout.Space(6);
            }

            // Training queue
            if (actionInfo.TrainingState.HasValue && actionInfo.TrainingState.Value.Queue != null)
            {
                DrawQueue(actionInfo.TrainingState.Value.Queue);
            }

            // ── Age-Up Section (Hall only, Era 1 only) ──
            DrawAgeUpSection(entity);

            GUILayout.EndArea();
        }

        /// <summary>
        /// Draw the "Advance to Era 2" button on the Hall if still in Era 1.
        /// </summary>
        private void DrawAgeUpSection(Entity entity)
        {
            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;
            if (!em.Exists(entity)) return;

            // Only for Hall buildings
            if (!em.HasComponent<HallTag>(entity)) return;

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
                GUILayout.Label($"Requires: {UIHelpers.FormatCost(CultureConfig.AgeUpCost)}", _requireStyle);
            }
        }

        private void DrawActionGrid(Entity entity, ActionButton[] actions, System.Action<ActionButton> onClick)
        {
            if (actions == null || actions.Length == 0)
            {
                GUILayout.Label("No actions available", _smallStyle);
                return;
            }

            int buttonsPerRow = 4;
            int row = 0;

            GUILayout.BeginHorizontal();

            for (int i = 0; i < actions.Length; i++)
            {
                var button = actions[i];

                // Disable if can't afford
                bool wasEnabled = GUI.enabled;
                if (!button.CanAfford) GUI.enabled = false;

                // Button content
                string label = button.Icon != null ? "" : button.Label;
                var content = new GUIContent(label, button.Tooltip);

                if (GUILayout.Button(content, _buttonStyle,
                    GUILayout.Width(ButtonSize), GUILayout.Height(ButtonSize)))
                {
                    onClick?.Invoke(button);
                }

                // Draw icon on top
                if (button.Icon != null)
                {
                    var rect = GUILayoutUtility.GetLastRect();
                    var iconRect = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 20);
                    GUI.DrawTexture(iconRect, button.Icon, ScaleMode.ScaleToFit);

                    // Draw label below icon
                    var labelRect = new Rect(rect.x, rect.y + rect.height - 18, rect.width, 16);
                    GUI.Label(labelRect, button.Label, _smallStyle);
                }

                // Cost indicator
                if (!button.Cost.IsZero)
                {
                    var rect = GUILayoutUtility.GetLastRect();
                    var costStr = FormatShortCost(button.Cost);
                    var costStyle = new GUIStyle(_smallStyle)
                    {
                        normal = { textColor = button.CanAfford ? new Color(0.3f, 0.9f, 0.3f) : new Color(1f, 0.3f, 0.3f) },
                        alignment = TextAnchor.LowerRight
                    };
                    GUI.Label(rect, costStr, costStyle);
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

        private void DrawProgressBar(TrainingInfo info)
        {
            GUILayout.Label($"Training: {info.CurrentUnitId}", _labelStyle);

            var rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));

            // Dark navy background
            GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Golden fill
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * info.Progress, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;

            // Time remaining
            var timeStyle = new GUIStyle(_smallStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, $"{info.TimeRemaining:F1}s", timeStyle);
        }

        private void DrawQueue(string[] queue)
        {
            if (queue.Length == 0) return;

            GUILayout.Label($"Queue ({queue.Length}):", _smallStyle);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Mathf.Min(queue.Length, 8); i++)
            {
                GUILayout.Label(queue[i], _smallStyle, GUILayout.Width(60));
            }
            if (queue.Length > 8)
                GUILayout.Label($"+{queue.Length - 8}", _smallStyle);
            GUILayout.EndHorizontal();
        }

        private static readonly string[] VaultResourceNames = { "None", "Supplies", "Iron", "Crystal", "Veilsteel", "Glow" };

        /// <summary>Currently selected resource in the vault dropdown (1-5). Persists across frames.</summary>
        private static int _vaultSelectedResource = 1;

        private void DrawVaultPanel(Entity entity)
        {
            PanelVisible = true;

            float vaultPanelHeight = 320f;
            var panelRect = new Rect(
                PanelPadding + 300f + PanelPadding,
                Screen.height - vaultPanelHeight - PanelPadding,
                PanelWidth,
                vaultPanelHeight
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

            GUILayout.Label("Vault of Almiérra", _headerStyle);
            GUILayout.Space(4);

            var em = UnifiedUIManager.GetEntityManager();
            if (!em.Exists(entity) || !em.HasComponent<VaultStorage>(entity))
            {
                GUILayout.Label("Vault not available", _smallStyle);
                GUILayout.EndArea();
                return;
            }

            var vault = em.GetComponentData<VaultStorage>(entity);
            var faction = em.HasComponent<FactionTag>(entity)
                ? em.GetComponentData<FactionTag>(entity).Value
                : GameSettings.LocalPlayerFaction;

            // Interest rate display
            GUILayout.Label($"Interest: {vault.InterestRate * 100f:F0}% per minute (compound)", _labelStyle);
            GUILayout.Space(2);

            // Current storage
            if (vault.ResourceType > 0 && vault.ResourceType < VaultResourceNames.Length)
            {
                string resName = VaultResourceNames[vault.ResourceType];
                GUILayout.Label($"Stored: {(int)vault.StoredAmount} {resName}", _labelStyle);
            }
            else
            {
                GUILayout.Label("Stored: Empty", _labelStyle);
            }

            // Lock status
            bool isLocked = vault.LockTimer > 0f;
            if (isLocked)
            {
                int lockMin = (int)(vault.LockTimer / 60f);
                int lockSec = (int)(vault.LockTimer % 60f);
                var lockStyle = new GUIStyle(_labelStyle)
                {
                    normal = { textColor = new Color(1f, 0.4f, 0.4f) }
                };
                GUILayout.Label($"LOCKED — {lockMin}:{lockSec:D2} remaining", lockStyle);
            }
            else
            {
                var unlockStyle = new GUIStyle(_labelStyle)
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
            GUILayout.Label("Resource:", _labelStyle, GUILayout.Width(80));
            if (GUILayout.Button($"◄", _buttonStyle, GUILayout.Width(28), GUILayout.Height(26)))
            {
                _vaultSelectedResource--;
                if (_vaultSelectedResource < 1) _vaultSelectedResource = 5;
            }
            GUILayout.Label(VaultResourceNames[_vaultSelectedResource], _labelStyle, GUILayout.Width(90));
            if (GUILayout.Button($"►", _buttonStyle, GUILayout.Width(28), GUILayout.Height(26)))
            {
                _vaultSelectedResource++;
                if (_vaultSelectedResource > 5) _vaultSelectedResource = 1;
            }
            GUILayout.EndHorizontal();

            GUI.enabled = wasEnabledDd;

            GUILayout.Space(6);

            // Deposit buttons — 100, 200, 500
            GUILayout.Label("Deposit:", _labelStyle);
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

                if (GUILayout.Button($"{amount}", _buttonStyle, GUILayout.Height(32)))
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
            if (GUILayout.Button($"Withdraw All ({(int)vault.StoredAmount})", _buttonStyle, GUILayout.Height(32)))
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
