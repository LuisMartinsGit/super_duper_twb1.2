// SkirmishPanel.cs
// uGUI controller for the Skirmish vs AI panel (scene GameObjects under
// UI_Canvas, scaffolded once by MenuPanelsBuilder and then hand-editable).
// Layout: map preview + 2x2 map options on the left, warband roster on the
// right, BACK / BEGIN SKIRMISH in the footer.
//
// The panel opens by SetActive(true) (the blue menu's Skirmish button is
// wired to that in the Inspector) and closes by SetActive(false). All slot
// state lives in LobbyConfig; the start flow writes GameSettings and calls
// LoadingScreen.Show — identical to the previous lobbies.

using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class SkirmishPanel : MonoBehaviour
    {
        private static readonly string[] StrategyNames =
            { "RANDOM", "ECONOMIST", "BALANCED", "TECHNOLOGIST", "AGGRESSOR", "TURTLE", "DEFENDER" };
        private static readonly string[] DifficultyNames =
            { "EASY", "STANDARD", "HARD", "EXPERT" };
        // The dropdown picks an age AND the culture every faction starts in —
        // the labels have always carried the culture suffix, so the list is
        // age/culture combinations rather than two separate controls.

        /// <summary>Culture each age entry starts every faction in, BEFORE the
        /// ship gate. Index-parallel with <see cref="AgeBaseNames"/>.</summary>
        private static readonly byte[] AgeCultures =
        {
            Cultures.None,      // AGE 0 — no promotion, culture chosen in play
            Cultures.Alanthor,
            Cultures.Alanthor,
            Cultures.Alanthor,
            Cultures.Feraldis,  // AGE 4 — top of the ladder, both verb gates open
        };

        private static readonly string[] AgeBaseNames =
            { "AGE 0", "AGE 1", "AGE 2", "AGE 3", "AGE 4" };

        // Declared AFTER the two arrays it reads — static field initializers
        // run in declaration order.
        //
        // The culture suffix is DERIVED from the gated culture, never written
        // by hand. This panel already shipped one bug where an option read
        // "(FER)" and started four Alanthor players; hard-coding the suffix
        // while the ship gate silently rewrites the culture would reintroduce
        // exactly that, so label and behaviour come from one call.
        private static readonly string[] AgeNames = BuildAgeNames();

        private static readonly string[] ResourceNames = { "NORMAL", "MAX" };

        private static string[] BuildAgeNames()
        {
            var names = new string[AgeBaseNames.Length];
            for (int i = 0; i < names.Length; i++)
            {
                byte culture = i < AgeCultures.Length
                    ? CultureConfig.Playable(AgeCultures[i])
                    : Cultures.None;
                string suffix = CultureSuffix(culture);
                names[i] = suffix.Length == 0
                    ? AgeBaseNames[i]
                    : $"{AgeBaseNames[i]} ({suffix})";
            }
            return names;
        }

        private static string CultureSuffix(byte culture) => culture switch
        {
            Cultures.Alanthor => "AL",
            Cultures.Feraldis => "FER",
            Cultures.Runai    => "RU",
            _                 => string.Empty,
        };

        /// <summary>
        /// Culture for a start age, read straight from AgeCultures.
        ///
        /// DERIVED, never stored: an earlier version kept a _startCulture
        /// field updated from the dropdown's onValueChanged. SyncOptions
        /// restores the saved age with SetValueWithoutNotify, which by design
        /// does NOT fire that listener — so reopening the lobby on a saved
        /// "AGE 4 (FER)" left the age at 4 and the culture at its Alanthor
        /// default. That shipped exactly once and produced a match of four
        /// Alanthor players from an option labelled (FER).
        /// </summary>
        private static byte CultureForAge(SkirmishStartAge age)
        {
            int i = (int)age;
            byte culture = i >= 0 && i < AgeCultures.Length
                ? AgeCultures[i]
                : Cultures.Alanthor;
            // Ship gate. AgeNames is built from this same call, so what the
            // dropdown promises is what the match actually starts in.
            return CultureConfig.Playable(culture);
        }

        private static readonly Color PillOn  = new Color(0.690f, 0.525f, 0.173f);
        private static readonly Color PillOff = new Color(0.086f, 0.118f, 0.141f);

        [Header("Map")]
        public MapPreviewWidget MapPreview;
        public Button PrevMapButton;
        public Button NextMapButton;

        [Header("Map options")]
        public TMP_Dropdown ResourcesDropdown;
        public TMP_Dropdown AgeDropdown;
        public Button FogToggle;
        public TMP_Text FogState;
        public Button CurseToggle;
        public TMP_Text CurseState;

        [Header("Roster")]
        public RectTransform RosterContent;
        public GameObject RosterRowTemplate; // inactive; cloned per slot
        public Button AddOpponentButton;

        [Header("Footer")]
        public Button BackButton;
        public Button BeginButton;
        public TMP_Text ErrorText;

        private int _mapIndex;
        private int _spawnSeed;
        private bool _fogOfWar, _curseNodes, _maxResources;
        private SkirmishStartAge _startAge;
        private bool _wired;

        // Observer mode (AI vs AI): the local player spectates and every
        // slot — including slot 0 — is AI-controlled. The pill is built
        // programmatically at runtime (no scene edit, no structural
        // assumptions about the built option cells). OFF by default; the
        // static remembers the player's choice for the session.
        private static bool _observerDefault = false;
        private bool _observer;
        private Button _observerToggle;
        private TMP_Text _observerState;

        private void OnEnable()
        {
            // Same lobby reset the previous skirmish screens performed.
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            LobbyConfig.SetupSinglePlayer(GameSettings.TotalPlayers);
            FactionColors.ResetToDefaults();

            _mapIndex = Mathf.Max(0, MapRegistry.IndexOf(GameSettings.SelectedMapScene));
            if (_mapIndex >= MapRegistry.Maps.Count) _mapIndex = 0;
            _spawnSeed = Random.Range(1, 99999); // fresh spawns every visit
            _fogOfWar = GameSettings.FogOfWarEnabled;
            _curseNodes = GameSettings.BorderEnabled;
            _maxResources = GameSettings.MaxStartingResources;
            _startAge = GameSettings.StartAge;
            _observer = _observerDefault;

            Wire();
            SyncOptions();
            RefreshMap();
            ClampRoster();
            RebuildRoster();
            SetError(null);
        }

        private void Wire()
        {
            if (_wired) return;
            _wired = true;

            if (BackButton != null) BackButton.onClick.AddListener(() => gameObject.SetActive(false));
            if (BeginButton != null) BeginButton.onClick.AddListener(StartGame);
            if (PrevMapButton != null) PrevMapButton.onClick.AddListener(() => CycleMap(-1));
            if (NextMapButton != null) NextMapButton.onClick.AddListener(() => CycleMap(+1));

            if (ResourcesDropdown != null)
            {
                SetOptions(ResourcesDropdown, ResourceNames);
                ResourcesDropdown.onValueChanged.AddListener(v => _maxResources = v == 1);
            }
            if (AgeDropdown != null)
            {
                SetOptions(AgeDropdown, AgeNames);
                AgeDropdown.onValueChanged.AddListener(v => _startAge = (SkirmishStartAge)v);
            }
            if (FogToggle != null) FogToggle.onClick.AddListener(() => { _fogOfWar = !_fogOfWar; SyncOptions(); });
            if (CurseToggle != null) CurseToggle.onClick.AddListener(() => { _curseNodes = !_curseNodes; SyncOptions(); });
            if (AddOpponentButton != null) AddOpponentButton.onClick.AddListener(AddOpponent);

            CreateObserverPill();
        }

        /// <summary>
        /// Build the OBSERVER option pill fully programmatically (label +
        /// ON/OFF button) and append it next to the fog option. The previous
        /// clone-the-fog-cell approach depended on the built cell's internal
        /// structure and silently produced a dead toggle when the structure
        /// drifted — this version constructs every element itself.
        /// </summary>
        private void CreateObserverPill()
        {
            if (_observerToggle != null || FogToggle == null) return;

            // The options row that holds the fog option cell: FogToggle sits
            // inside its cell's "Pill" sub-row → cell → row. Fall back one
            // level at a time if the hierarchy is shallower than expected.
            Transform pillRow = FogToggle.transform.parent;
            Transform cell = pillRow != null ? pillRow.parent : null;
            Transform row = cell != null ? cell.parent : pillRow;
            if (row == null) return;

            var cellGo = new GameObject("OptObserver", typeof(RectTransform));
            cellGo.transform.SetParent(row, false);
            var v = cellGo.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;
            v.spacing = 4f;
            var cellLe = cellGo.AddComponent<LayoutElement>();
            cellLe.flexibleWidth = 1f;

            // Caption.
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(cellGo.transform, false);
            var label = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
            label.text = "OBSERVER";
            label.fontSize = 13f;
            label.fontStyle = TMPro.FontStyles.Bold;
            if (FogState != null) { label.font = FogState.font; label.color = FogState.color; }
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.minHeight = 18f;

            // Pill button with ON/OFF state text.
            var btnGo = new GameObject("Pill", typeof(RectTransform));
            btnGo.transform.SetParent(cellGo.transform, false);
            var img = btnGo.AddComponent<Image>();
            img.color = PillOff;
            _observerToggle = btnGo.AddComponent<Button>();
            _observerToggle.targetGraphic = img;
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.minHeight = 26f;
            btnLe.minWidth = 96f;

            var stateGo = new GameObject("State", typeof(RectTransform));
            stateGo.transform.SetParent(btnGo.transform, false);
            var srt = (RectTransform)stateGo.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var stateText = stateGo.AddComponent<TMPro.TextMeshProUGUI>();
            stateText.alignment = TMPro.TextAlignmentOptions.Center;
            stateText.fontSize = 13f;
            stateText.text = "OFF";
            if (FogState != null) { stateText.font = FogState.font; stateText.color = FogState.color; }
            _observerState = stateText;

            _observerToggle.onClick.AddListener(() =>
            {
                _observer = !_observer;
                _observerDefault = _observer;
                SyncOptions();
                RebuildRoster();
                SetError(null);
            });
        }

        private static void SetOptions(TMP_Dropdown dd, string[] names)
        {
            dd.ClearOptions();
            var list = new System.Collections.Generic.List<string>(names);
            dd.AddOptions(list);
        }

        private void SyncOptions()
        {
            if (ResourcesDropdown != null) ResourcesDropdown.SetValueWithoutNotify(_maxResources ? 1 : 0);
            if (AgeDropdown != null) AgeDropdown.SetValueWithoutNotify((int)_startAge);
            SyncPill(FogToggle, FogState, _fogOfWar);
            SyncPill(CurseToggle, CurseState, _curseNodes);
            SyncPill(_observerToggle, _observerState, _observer);
        }

        private static void SyncPill(Button toggle, TMP_Text state, bool on)
        {
            if (state != null) state.text = on ? "ON" : "OFF";
            if (toggle != null && toggle.targetGraphic is Image img)
                img.color = on ? PillOn : PillOff;
        }

        // ── Map ─────────────────────────────────────────────────────────

        private void CycleMap(int delta)
        {
            int count = MapRegistry.Maps.Count;
            if (count == 0) return;
            _mapIndex = ((_mapIndex + delta) % count + count) % count;
            RefreshMap();
            ClampRoster();
            RebuildRoster();
        }

        private void RefreshMap()
        {
            if (MapPreview != null) MapPreview.Show(_mapIndex);
        }

        private int MaxPlayers() => MapPreviewWidget.MaxPlayers(_mapIndex);

        private void ClampRoster()
        {
            if (LobbyConfig.ActiveSlotCount > MaxPlayers())
                SetSlotCount(MaxPlayers());
        }

        // ── Roster ──────────────────────────────────────────────────────

        private void RebuildRoster()
        {
            if (RosterContent == null || RosterRowTemplate == null) return;

            for (int i = RosterContent.childCount - 1; i >= 0; i--)
            {
                var child = RosterContent.GetChild(i).gameObject;
                if (child != RosterRowTemplate &&
                    (AddOpponentButton == null || child != AddOpponentButton.gameObject))
                    Destroy(child);
            }

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                BuildRosterRow(i);

            if (AddOpponentButton != null)
                AddOpponentButton.gameObject.SetActive(LobbyConfig.ActiveSlotCount < MaxPlayers());
        }

        private void BuildRosterRow(int index)
        {
            var slot = LobbyConfig.Slots[index];
            bool isHost = index == 0;
            int idx = index;

            var row = Instantiate(RosterRowTemplate, RosterContent);
            row.SetActive(true);
            var t = row.transform;

            var strip = t.Find("ColorStrip");
            if (strip != null)
            {
                if (strip.TryGetComponent(out Image stripImg))
                    stripImg.color = slot.GetFactionColor();
                if (strip.TryGetComponent(out Button stripBtn))
                    stripBtn.onClick.AddListener(() =>
                    {
                        CycleSlotColor(idx, +1);
                        if (stripImg != null)
                            stripImg.color = LobbyConfig.Slots[idx].GetFactionColor();
                    });
            }

            var chip = t.Find("TeamChip")?.GetComponentInChildren<TMP_Text>(true);
            if (chip != null) chip.text = $"TEAM {index + 1}";

            var badge = t.Find("HostBadge");
            var name = t.Find("NameLabel")?.GetComponent<TMP_Text>();
            var strat = t.Find("StrategyDropdown")?.GetComponent<TMP_Dropdown>();
            var diff = t.Find("DifficultyDropdown")?.GetComponent<TMP_Dropdown>();
            var remove = t.Find("RemoveButton")?.GetComponent<Button>();

            // Observer mode: the host slot is AI-controlled too — it gets the
            // same strategy/difficulty dropdowns as every other AI warband.
            bool aiRow = !isHost || _observer;

            if (badge != null) badge.gameObject.SetActive(isHost);
            if (name != null)
            {
                name.gameObject.SetActive(isHost && !_observer);
                if (isHost && !_observer)
                {
                    string playerName = string.IsNullOrEmpty(slot.PlayerName) ? "Player" : slot.PlayerName;
                    name.text = playerName.ToUpperInvariant();
                }
            }

            if (strat != null)
            {
                strat.gameObject.SetActive(aiRow);
                if (aiRow)
                {
                    SetOptions(strat, StrategyNames);
                    strat.SetValueWithoutNotify((int)slot.AIStrategy);
                    strat.onValueChanged.AddListener(v => LobbyConfig.Slots[idx].AIStrategy = (LobbyAIStrategy)v);
                }
            }
            if (diff != null)
            {
                diff.gameObject.SetActive(aiRow);
                if (aiRow)
                {
                    SetOptions(diff, DifficultyNames);
                    diff.SetValueWithoutNotify((int)slot.AIDifficulty);
                    diff.onValueChanged.AddListener(v => LobbyConfig.Slots[idx].AIDifficulty = (LobbyAIDifficulty)v);
                }
            }
            if (remove != null)
            {
                remove.gameObject.SetActive(!isHost && LobbyConfig.ActiveSlotCount > 2);
                if (!isHost)
                    remove.onClick.AddListener(() => { RemoveSlot(idx); RebuildRoster(); });
            }
        }

        private void AddOpponent()
        {
            int idx = LobbyConfig.ActiveSlotCount; // the slot being added
            SetSlotCount(LobbyConfig.ActiveSlotCount + 1);
            if (idx < LobbyConfig.ActiveSlotCount)
            {
                var slot = LobbyConfig.Slots[idx];
                slot.AIDifficulty = LobbyAIDifficulty.Normal;
                slot.AIStrategy = LobbyAIStrategy.Random;
            }
            RebuildRoster();
        }

        // SetupSinglePlayer resets every slot's type/difficulty, so save +
        // restore the per-slot personality across count changes.
        private void SetSlotCount(int count)
        {
            count = Mathf.Clamp(count, 2, MaxPlayers());

            var savedColor = new int[8];
            var savedDiff = new LobbyAIDifficulty[8];
            var savedStrat = new LobbyAIStrategy[8];
            for (int i = 0; i < 8; i++)
            {
                var s = LobbyConfig.Slots[i];
                savedColor[i] = s.ColorIndex;
                savedDiff[i] = s.AIDifficulty;
                savedStrat[i] = s.AIStrategy;
            }

            LobbyConfig.SetupSinglePlayer(count);

            for (int i = 0; i < 8; i++)
            {
                var s = LobbyConfig.Slots[i];
                s.ColorIndex = savedColor[i];
                s.AIDifficulty = savedDiff[i];
                s.AIStrategy = savedStrat[i];
            }

            ResolveColorConflicts();
        }

        private void RemoveSlot(int index)
        {
            for (int i = index; i < LobbyConfig.ActiveSlotCount - 1; i++)
            {
                var a = LobbyConfig.Slots[i];
                var b = LobbyConfig.Slots[i + 1];
                a.ColorIndex = b.ColorIndex;
                a.AIDifficulty = b.AIDifficulty;
                a.AIStrategy = b.AIStrategy;
            }
            SetSlotCount(LobbyConfig.ActiveSlotCount - 1);
        }

        private void CycleSlotColor(int slotIndex, int direction)
        {
            var slot = LobbyConfig.Slots[slotIndex];
            int current = slot.ColorIndex;
            int count = FactionColors.ColorCount;
            int dir = direction >= 0 ? 1 : -1;

            for (int attempt = 1; attempt <= count; attempt++)
            {
                int next = ((current + dir * attempt) % count + count) % count;
                if (!IsColorInUse(next, slotIndex))
                {
                    slot.ColorIndex = next;
                    return;
                }
            }
            slot.ColorIndex = ((current + dir) % count + count) % count;
        }

        private bool IsColorInUse(int colorIndex, int excludeSlot)
        {
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (i == excludeSlot) continue;
                if (LobbyConfig.Slots[i].ColorIndex == colorIndex)
                    return true;
            }
            return false;
        }

        private void ResolveColorConflicts()
        {
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                while (IsColorInUse(LobbyConfig.Slots[i].ColorIndex, i))
                {
                    LobbyConfig.Slots[i].ColorIndex =
                        (LobbyConfig.Slots[i].ColorIndex + 1) % FactionColors.ColorCount;
                }
            }
        }

        // ── Start (same flow as the previous lobbies) ───────────────────

        private void StartGame()
        {
            var maps = MapRegistry.Maps;
            GameSettings.SelectedMapScene = maps[Mathf.Clamp(_mapIndex, 0, maps.Count - 1)].SceneName;
            GameSettings.SpawnSeed = _spawnSeed;
            GameSettings.FogOfWarEnabled = _fogOfWar;
            GameSettings.MaxStartingResources = _maxResources;
            GameSettings.BorderEnabled = _curseNodes;
            GameSettings.IsObserver = _observer;
            GameSettings.StartAge = _startAge;
            GameSettings.StartCulture = CultureForAge(_startAge);
            GameSettings.Mode = GameMode.FreeForAll;

            LobbyConfig.ApplyColorSelections();

            // Observer mode: every active slot becomes an AI (slot 0
            // included) — the local player spectates. SlotType.AI (not
            // Observer) so the AI bootstrap reads the slot's configured
            // difficulty/strategy and PlayerSpawnSystem spawns its base.
            if (_observer)
            {
                for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                    if (LobbyConfig.Slots[i].Type == SlotType.Human)
                        LobbyConfig.Slots[i].Type = SlotType.AI;
            }

            int humanCount = 0;
            int aiCount = 0;
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot.Type == SlotType.Human) humanCount++;
                else if (slot.Type == SlotType.AI) aiCount++;
            }

            if (_observer)
            {
                if (aiCount < 2)
                {
                    SetError("Need at least 2 AI warbands to observe!");
                    return;
                }
            }
            else if (humanCount == 0)
            {
                SetError("Need at least 1 human player!");
                return;
            }

            GameSettings.TotalPlayers = humanCount + aiCount;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.LocalPlayerFaction = Faction.Blue;

            SetError(null);
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        private void SetError(string message)
        {
            if (ErrorText != null) ErrorText.text = message ?? string.Empty;
        }
    }
}
