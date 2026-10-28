// SkirmishPanel.cs
// uGUI controller for the Skirmish vs AI panel (scene GameObjects under
// UI_Canvas, scaffolded once by MenuPanelsBuilder and then hand-editable).
// Layout: map preview + 2x2 map options on the left, warband roster on the
// right, CANCEL / START in the footer (BeginButton keeps its field name — the
// scene serialises the reference by name, so renaming it would unwire it).
//
// Lives in its own scene (SkirmishMenu.unity) since 2026-08-18 — it used to be
// a panel inside MainMenu.unity that the blue menu switched on with SetActive.
// The blue menu's Skirmish entry now loads that scene through a MenuSceneLink,
// and CANCEL loads MainMenu back. OnEnable still does all the setup, so the
// panel behaves the same whether it is switched on or scene-loaded. All slot
// state lives in LobbyConfig (static, so it survives the scene swap); the start
// flow writes GameSettings and calls LoadingScreen.Show — identical to the
// previous lobbies.

using TheWaningBorder.Bootstrap;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class SkirmishPanel : MonoBehaviour
    {
        private static readonly string[] StrategyNames = LobbyOptions.StrategyNames;
        private static readonly string[] DifficultyNames =
            { "EASY", "STANDARD", "HARD", "EXPERT" };
        // The STARTING AGE dropdown was replaced by START WITH CULTURE
        // (StartCultureOption, 2026-10-14): AgeDropdown stays wired in the
        // scene only as the cell the toggle is built into.
        private static readonly string[] ResourceNames = LobbyOptions.ResourceNames;

        /// <summary>
        /// Team dropdown entries, index-parallel with the team index itself:
        /// entry 0 is <see cref="Alliances.NoTeam"/>, entry N is team N. Built
        /// from <see cref="Alliances.MaxTeams"/> rather than written out, so
        /// raising the team cap cannot leave the lobby offering fewer teams
        /// than the game supports.
        /// </summary>
        private static readonly string[] TeamNames = BuildTeamNames();

        private static string[] BuildTeamNames()
        {
            var names = new string[Alliances.MaxTeams + 1];
            names[Alliances.NoTeam] = "NO TEAM";
            for (int i = 1; i < names.Length; i++) names[i] = $"TEAM {i}";
            return names;
        }

        public MapPreviewWidget MapPreview;
        public Button PrevMapButton;
        public Button NextMapButton;

        public TMP_Dropdown ResourcesDropdown;
        public TMP_Dropdown AgeDropdown;
        public Button FogToggle;
        public TMP_Text FogState;
        public Button CurseToggle;
        public TMP_Text CurseState;
        /// <summary>The Synty toggle prefab every ON/OFF option is drawn with
        /// (MenuToggleSwitch.Replace). Unwired, the authored tracks stay.</summary>
        public GameObject TogglePrefab;

        public RectTransform RosterContent;
        public GameObject RosterRowTemplate; // inactive; cloned per slot
        public Button AddOpponentButton;

        public Button BackButton;
        public Button BeginButton;
        public TMP_Text ErrorText;

        private int _mapIndex;
        private int _spawnSeed;
        private bool _fogOfWar, _curseNodes;
        private StartingResourcesLevel _startResources;
        private bool _wired;

        // START WITH CULTURE (StartCultureOption) — replaced the STARTING AGE
        // dropdown. The static remembers the choice for the session.
        private static bool _startWithCultureDefault;
        private bool _startWithCulture;
        private MenuToggleSwitch _cultureToggle;

        // The authored fog / curse options, dressed (MenuToggleSwitch.Replace).
        private MenuToggleSwitch _fogSwitch, _curseSwitch;

        // Observer mode (AI vs AI): the local player spectates and every
        // slot — including slot 0 — is AI-controlled. Its option cell is a
        // runtime clone of the fog cell on a third options row, so it sits
        // in the same grid as the authored options. OFF by default; the
        // static remembers the player's choice for the session.
        private static bool _observerDefault = false;
        private bool _observer;
        private MenuToggleSwitch _observerToggle;

        private void OnEnable()
        {
            MenuBackgroundFit.Apply();
            MenuBackgroundFit.StretchPlateBackgrounds(transform.root, MenuBackgroundFit.LobbyPlates);
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
            _startResources = GameSettings.StartingResources;
            _startWithCulture = _startWithCultureDefault;
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

            // CANCEL returns to the blue menu by loading it. This screen used to
            // be a panel inside MainMenu.unity and closed itself with
            // SetActive(false); it is its own scene now, so there is nothing
            // behind it to uncover.
            if (BackButton != null)
                BackButton.onClick.AddListener(
                    () => SceneManager.LoadScene(TheWaningBorder.Core.SceneNames.Menu));
            if (BeginButton != null) BeginButton.onClick.AddListener(StartGame);
            if (PrevMapButton != null) PrevMapButton.onClick.AddListener(() => CycleMap(-1));
            if (NextMapButton != null) NextMapButton.onClick.AddListener(() => CycleMap(+1));

            if (ResourcesDropdown != null)
            {
                SetOptions(ResourcesDropdown, ResourceNames);
                ResourcesDropdown.onValueChanged.AddListener(v => _startResources = (StartingResourcesLevel)v);
            }
            // Clones are taken off the fog cell BEFORE it is dressed, so every
            // option starts from the same authored pill.
            CreateObserverCell();
            _cultureToggle = StartCultureOption.BuildToggle(AgeDropdown, FogToggle, TogglePrefab);
            if (_cultureToggle != null)
                _cultureToggle.Changed += on =>
                {
                    _startWithCulture = on;
                    _startWithCultureDefault = on;
                    SyncOptions();
                    RebuildRoster();
                };
            _fogSwitch = MenuToggleSwitch.Replace(FogToggle, FogState, TogglePrefab);
            if (_fogSwitch != null) _fogSwitch.Changed += on => { _fogOfWar = on; SyncOptions(); };
            _curseSwitch = MenuToggleSwitch.Replace(CurseToggle, CurseState, TogglePrefab);
            if (_curseSwitch != null) _curseSwitch.Changed += on => { _curseNodes = on; SyncOptions(); };
            if (_observerToggle != null)
                _observerToggle.Changed += on =>
                {
                    _observer = on;
                    _observerDefault = on;
                    SyncOptions();
                    RebuildRoster();
                    SetError(null);
                };
            if (AddOpponentButton != null) AddOpponentButton.onClick.AddListener(AddOpponent);
        }

        /// <summary>
        /// The OBSERVER option: a clone of the whole fog cell (label, caption
        /// and pill), placed on the MAP OPTIONS title line in the right
        /// column - its rect copied off the live Curse cell for the column
        /// and off the title for the line - so it reads as the top-right
        /// cell of the grid. The two authored rows are hand-placed (their
        /// layout elements ignore the box's layout group) and fill the box,
        /// so a third row has nowhere to go: a cloned row landed on top of
        /// the second (2026-10-28). Cloning the cell is what keeps it aligned
        /// with the others - the hand-built version picked its own fonts,
        /// sizes and spacing. The clone is checked, not trusted: a missing
        /// label or track is a logged error and no option, never a silent
        /// dead toggle.
        /// </summary>
        private void CreateObserverCell()
        {
            if (_observerToggle != null || FogToggle == null) return;

            // FogToggle sits in its cell's "Pill" row -> cell -> options row
            // -> the options box, whose first child is the title.
            Transform pillRow = FogToggle.transform.parent;
            Transform cell = pillRow != null ? pillRow.parent : null;
            Transform row = cell != null ? cell.parent : null;
            var box = row != null ? row.parent as RectTransform : null;
            var rightCell = row != null && row.childCount > 1 ? row.GetChild(row.childCount - 1) as RectTransform : null;
            var title = box != null && box.childCount > 0 ? box.GetChild(0) as RectTransform : null;
            if (box == null || rightCell == null || title == null)
            {
                Debug.LogError("[SkirmishPanel] fog option cell / options box not found - no OBSERVER option.", this);
                return;
            }

            // The row's cells are sized by its layout group; make sure that
            // has run before their rects are read.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)row);

            var cellGo = Instantiate(cell.gameObject, box, false);
            cellGo.name = "OptObserver";
            var rt = (RectTransform)cellGo.transform;
            var le = cellGo.GetComponent<LayoutElement>() ?? cellGo.AddComponent<LayoutElement>();
            le.ignoreLayout = true;   // hand-placed, like the rows
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            Vector2 column = box.InverseTransformPoint(rightCell.TransformPoint(rightCell.rect.center));
            Vector2 line = box.InverseTransformPoint(title.TransformPoint(title.rect.center));
            rt.sizeDelta = new Vector2(rightCell.rect.width, rightCell.rect.height);
            rt.anchoredPosition = new Vector2(column.x, line.y);

            TMP_Text label = null, caption = null;
            foreach (var t in cellGo.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.name == "Label") label = t;
                else if (t.name == "Caption") caption = t;
            }
            Button track = null;
            TMP_Text state = null;
            foreach (var b in cellGo.GetComponentsInChildren<Button>(true))
                if (b.GetComponent<Toggle>() == null) { track = b; break; }
            foreach (var t in cellGo.GetComponentsInChildren<TMP_Text>(true))
                if (t.name == "State") { state = t; break; }
            if (label == null || track == null)
            {
                Debug.LogError("[SkirmishPanel] the fog option cell has no Label / Track to clone - " +
                               "its structure drifted; no OBSERVER option.", this);
                Destroy(cellGo);
                return;
            }
            label.text = Loc.T("OBSERVER");
            if (caption != null) caption.text = Loc.T("AI against AI; you watch.");
            track.onClick.RemoveAllListeners();
            _observerToggle = MenuToggleSwitch.Replace(track, state, TogglePrefab);
        }

        private static void SetOptions(TMP_Dropdown dd, string[] names)
        {
            // The arrays stay English (selection travels by index); the
            // visible option list is translated here, at the render point.
            dd.ClearOptions();
            var list = new System.Collections.Generic.List<string>(names.Length);
            for (int i = 0; i < names.Length; i++) list.Add(Loc.T(names[i]));
            dd.AddOptions(list);
        }

        private void SyncOptions()
        {
            if (ResourcesDropdown != null) ResourcesDropdown.SetValueWithoutNotify((int)_startResources);
            _cultureToggle?.SetOn(_startWithCulture);
            _fogSwitch?.SetOn(_fogOfWar);
            _curseSwitch?.SetOn(_curseNodes);
            _observerToggle?.SetOn(_observer);
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
            if (MapPreview == null) return;

            // Start positions belong to the map, so a map change invalidates
            // every assignment — a start index on Sundered Crown means nothing
            // on Twin Spans. docs/Design/Lobby_Setup.md
            if (_mapIndex != _startsMapIndex)
            {
                ClearAllStartAssignments();
                _startsMapIndex = _mapIndex;
                // A different map is a different question about sides, so the
                // player's team edits on the previous one stop applying.
                _teamsOverridden = false;
            }

            // Fill in anyone without a start — on first show, after a map
            // change wiped them, or after a slot was added.
            AssignMissingStarts();

            // …then hand out the map's authored teams for those starts. Order
            // matters: the preset is keyed by start, so it has to run after
            // every occupied slot has one.
            ApplyMapTeamPreset();

            MapPreview.StartState = GetStartState;
            MapPreview.OnStartClicked = OnStartPositionClicked;
            MapPreview.Show(_mapIndex);
        }

        // ── Start positions ─────────────────────────────────────────────
        //
        // Click a roster row to select a player, then click a start dot on the
        // map preview to place them there. docs/Design/Lobby_Setup.md

        private int _selectedSlot = -1;
        private int _startsMapIndex = -1;

        /// <summary>Which slot currently holds a given start, or -1.</summary>
        private static int SlotHolding(int startIndex)
        {
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var s = LobbyConfig.Slots[i];
                if (s != null && s.StartIndex == startIndex) return i;
            }
            return -1;
        }

        private static void ClearAllStartAssignments()
        {
            for (int i = 0; i < LobbyConfig.Slots.Length; i++)
                if (LobbyConfig.Slots[i] != null)
                    LobbyConfig.Slots[i].StartIndex = PlayerSlot.AutoStart;
        }

        /// <summary>MapInfo for the map currently shown, or null.</summary>
        private MapInfo CurrentMapInfo()
        {
            var maps = MapRegistry.Maps;
            if (maps.Count == 0) return null;
            int idx = Mathf.Clamp(_mapIndex, 0, maps.Count - 1);
            return MapInfoIndex.For(maps[idx].SceneName);
        }

        /// <summary>How many authored start positions the shown map has.</summary>
        private int StartCountForCurrentMap()
        {
            var info = CurrentMapInfo();
            return info?.PlayerStarts?.Length ?? 0;
        }

        // ── Team preset ─────────────────────────────────────────────────
        //
        // A map drawn as 3v3 has to arrive as 3v3. Twin Spans shipped with
        // every slot on Alliances.NoTeam, which the hostility table reads as
        // "allied with nobody" — so the three warbands sharing a shore were
        // enemies, and the map played as a six-way free-for-all on a layout
        // built around two shared crossings. docs/Design/Teams.md

        /// <summary>
        /// True once the player has touched a team chip. From then on the map's
        /// preset stops writing over their choice — picking FFA on a team map
        /// is a legitimate thing to want.
        /// </summary>
        private bool _teamsOverridden;

        /// <summary>
        /// Copy the current map's authored team layout onto the roster, keyed
        /// by each slot's START (not its slot index — starts are handed out
        /// randomly, so slot order says nothing about which shore you are on).
        /// Returns true when anything changed, so the caller can refresh the
        /// chips.
        /// </summary>
        private bool ApplyMapTeamPreset()
        {
            if (_teamsOverridden) return false;

            var info = CurrentMapInfo();
            bool hasPreset = info != null && info.HasTeamPreset;

            // Resolve the whole roster FIRST, then commit. Two rules need the
            // full picture before anything is written:
            //
            //  1. A map with no preset must CLEAR teams, not leave them alone.
            //     The presets are per-map, but LobbyConfig.Slots is static and
            //     outlives the lobby: selecting Twin Spans (3v3) and then
            //     switching to any other map used to leave both players sitting
            //     on Team 1. Allied players are not hostile, so
            //     VictoryConditionSystem saw one side remaining and ended the
            //     match with a VICTORY banner ~10 s in, before anyone had met.
            //
            //  2. A preset that puts EVERY occupied slot on one team is not a
            //     team layout, it is the same bug wearing the map's clothes —
            //     starts are handed out randomly, so a 2-player lobby on a 3v3
            //     map can easily draw two starts from the same shore. Fall back
            //     to a free-for-all rather than shipping a match with no enemy.
            var wanted = new byte[LobbyConfig.Slots.Length];
            int occupied = 0;
            byte firstTeam = Alliances.NoTeam;
            bool allSameTeam = true;

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot == null || slot.Type == SlotType.Empty) continue;

                // No start yet means no shore, so no side either.
                wanted[i] = hasPreset && slot.StartIndex != PlayerSlot.AutoStart
                    ? info.TeamForStart(slot.StartIndex)
                    : Alliances.NoTeam;

                if (occupied == 0) firstTeam = wanted[i];
                else if (wanted[i] != firstTeam) allSameTeam = false;
                occupied++;
            }

            if (occupied > 1 && allSameTeam && firstTeam != Alliances.NoTeam)
                for (int i = 0; i < wanted.Length; i++) wanted[i] = Alliances.NoTeam;

            bool changed = false;
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot == null || slot.Type == SlotType.Empty) continue;
                if (slot.TeamIndex == wanted[i]) continue;
                slot.TeamIndex = wanted[i];
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Give every occupied slot a start position, picking RANDOMLY from
        /// the ones still free.
        ///
        /// The lobby used to open with every slot on AutoStart, so the map
        /// preview showed no assignments at all and the player either clicked
        /// eight dots by hand or let the spawn layout decide silently. Slots
        /// now come pre-assigned, and anything still unassigned — a slot just
        /// added, or one whose start was released by re-clicking it — picks up
        /// a free start rather than staying blank.
        ///
        /// Random rather than in slot order so repeated skirmishes on the same
        /// map are not always the same seating. Existing assignments are never
        /// disturbed: a start the player deliberately chose is theirs.
        /// </summary>
        private void AssignMissingStarts()
        {
            int startCount = StartCountForCurrentMap();
            if (startCount <= 0) return;

            // Free = not already held by an occupied slot.
            var free = new System.Collections.Generic.List<int>(startCount);
            for (int s = 0; s < startCount; s++)
                if (SlotHolding(s) < 0) free.Add(s);

            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                if (free.Count == 0) break;

                var slot = LobbyConfig.Slots[i];
                if (slot == null || slot.Type == SlotType.Empty) continue;
                if (slot.StartIndex != PlayerSlot.AutoStart) continue;

                int pick = UnityEngine.Random.Range(0, free.Count);
                slot.StartIndex = free[pick];
                free.RemoveAt(pick);
            }
        }

        /// <summary>Colour + label for one start dot, read by MapPreviewWidget.</summary>
        private void GetStartState(int startIndex, out Color tint, out string holderLabel)
        {
            int holder = SlotHolding(startIndex);
            if (holder < 0)
            {
                tint = MapPreviewWidget.StartsColor;
                holderLabel = string.Empty;
                return;
            }
            tint = LobbyConfig.Slots[holder].GetFactionColor();
            holderLabel = (holder + 1).ToString();
        }

        private void OnStartPositionClicked(int startIndex)
        {
            int holder = SlotHolding(startIndex);

            // No player selected: clicking a claimed start releases it, so the
            // map is usable without touching the roster first.
            if (_selectedSlot < 0 || _selectedSlot >= LobbyConfig.ActiveSlotCount)
            {
                if (holder >= 0)
                {
                    LobbyConfig.Slots[holder].StartIndex = PlayerSlot.AutoStart;
                    RefreshStartMarkers();
                }
                else if (ErrorText != null)
                {
                    ErrorText.text = Loc.T("Pick a player on the left, then choose a start position.");
                }
                return;
            }

            var sel = LobbyConfig.Slots[_selectedSlot];

            // Clicking the start you already hold releases it.
            if (sel.StartIndex == startIndex)
            {
                sel.StartIndex = PlayerSlot.AutoStart;
                RefreshStartMarkers();
                return;
            }

            // A start holds exactly one player: whoever was there is evicted
            // back to automatic placement.
            if (holder >= 0) LobbyConfig.Slots[holder].StartIndex = PlayerSlot.AutoStart;

            sel.StartIndex = startIndex;
            if (ErrorText != null) ErrorText.text = string.Empty;
            RefreshStartMarkers();

            // Moving to the far shore changes which side you are on, so the
            // preset follows. Only rebuild the roster when a chip actually
            // moved.
            if (ApplyMapTeamPreset()) RebuildRoster();
        }

        private void RefreshStartMarkers()
        {
            if (MapPreview != null) MapPreview.RefreshStartMarkers();
        }

        /// <summary>
        /// Highlight the selected roster row and sink the unoccupied ones, so
        /// the ladder reads as "these seats are taken, these are free".
        /// </summary>
        private void ApplyRowSelection()
        {
            for (int i = 0; i < _rowRoots.Count; i++)
            {
                if (_rowRoots[i] == null) continue;
                var img = _rowRoots[i].GetComponent<Image>();
                if (img == null) continue;

                bool occupied = i < _rowKinds.Count && _rowKinds[i] == RowKind.Occupied;
                if (i == _selectedSlot && occupied)
                    img.color = new Color(0.16f, 0.24f, 0.30f, 1f);
                else if (occupied)
                    img.color = _rowBaseColor;
                else
                    img.color = new Color(_rowBaseColor.r, _rowBaseColor.g, _rowBaseColor.b,
                                          _rowBaseColor.a * 0.45f);
            }
        }

        private readonly System.Collections.Generic.List<GameObject> _rowRoots = new();
        private readonly System.Collections.Generic.List<RowKind> _rowKinds = new();
        private Color _rowBaseColor = new Color(0.08f, 0.11f, 0.13f, 1f);

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

            // The template is a stencil, never a row. It is left ACTIVE in the
            // scene so it can be styled in place, and the destroy loop below
            // deliberately spares it - so without this it would render as a
            // permanent extra row showing its placeholder content, which no
            // rebuild ever updates. Switching it off here rather than in the
            // scene keeps it editable and keeps the panel right either way.
            if (RosterRowTemplate.activeSelf) RosterRowTemplate.SetActive(false);

            // Adding or removing an opponent changes who needs a start, so top
            // the assignments up before the rows (and the map dots) are drawn.
            AssignMissingStarts();

            for (int i = RosterContent.childCount - 1; i >= 0; i--)
            {
                var child = RosterContent.GetChild(i).gameObject;
                if (child != RosterRowTemplate &&
                    (AddOpponentButton == null || child != AddOpponentButton.gameObject))
                    Destroy(child);
            }

            // The roster is a FIXED ladder of 8 slots, occupied or not, rather
            // than a list that grows and shrinks. A stable set of rows is what
            // makes the columns line up and stops the panel reflowing every
            // time an opponent is added. docs/Design/Lobby_Setup.md
            _rowRoots.Clear();
            _rowKinds.Clear();
            int active = LobbyConfig.ActiveSlotCount;
            int cap = MaxPlayers();
            for (int i = 0; i < TotalSlotRows; i++)
            {
                RowKind kind =
                    i < active ? RowKind.Occupied
                    : i == active && active < cap ? RowKind.AddHere
                    : RowKind.Empty;
                BuildRosterRow(i, kind);
            }

            // Keep the selection valid across rebuilds (add/remove opponent).
            if (_selectedSlot >= active) _selectedSlot = -1;
            ApplyRowSelection();
            RefreshStartMarkers();

            // The standalone "+ ADD OPPONENT" button is retired: the add
            // affordance now lives on the top-most empty row, so the roster
            // reads as one ladder instead of a list plus a stray button.
            if (AddOpponentButton != null)
                AddOpponentButton.gameObject.SetActive(false);
        }

        /// <summary>What a roster row is showing.</summary>
        private enum RowKind
        {
            /// <summary>A player (human or AI) sits here.</summary>
            Occupied,
            /// <summary>Top-most free slot — carries the add-player affordance.</summary>
            AddHere,
            /// <summary>Free, but not the next one to fill (or past the map's cap).</summary>
            Empty
        }

        // Always eight slots, matching the engine's faction range (Blue..White).
        private const int TotalSlotRows = 8;

        private void BuildRosterRow(int index, RowKind kind)
        {
            var slot = LobbyConfig.Slots[index];
            int idx = index;
            bool occupied = kind == RowKind.Occupied;

            var row = Instantiate(RosterRowTemplate, RosterContent);
            row.SetActive(true);
            var t = row.transform;

            // A row is a straight COPY of the authored template. Nothing below
            // reorders its children or overrides their widths - the template
            // owns the row's look, and every row is the same clone, so the
            // columns line up by construction. What varies per row is the DATA
            // and which optional widgets are shown for that slot's state.
            //
            // This used to re-pin every column here (LobbyRowLayout.PrepareRow
            // + a Column call per widget) because the template had no widths of
            // its own and its layout group force-expanded children. It carries
            // a LayoutElement on every column now, so the pinning only fought
            // the authored design. MultiplayerPanel still clones a template
            // that has not been through that pass, so LobbyRowLayout stays.

            // The row itself selects the player for start-position placement.
            // A Button on the row root works because the row already carries a
            // background Image; child widgets (colour swatch, team chip,
            // dropdowns) sit above it and consume their own clicks first.
            // docs/Design/Lobby_Setup.md
            _rowRoots.Add(row);
            _rowKinds.Add(kind);
            if (row.TryGetComponent(out Image rowImg))
            {
                if (_rowRoots.Count == 1) _rowBaseColor = rowImg.color;
                rowImg.raycastTarget = true;
                var rowBtn = row.GetComponent<Button>() ?? row.AddComponent<Button>();
                rowBtn.transition = Selectable.Transition.None;
                rowBtn.targetGraphic = rowImg;

                if (occupied)
                    rowBtn.onClick.AddListener(() =>
                    {
                        _selectedSlot = _selectedSlot == idx ? -1 : idx;
                        ApplyRowSelection();
                    });
                else if (kind == RowKind.AddHere)
                    rowBtn.onClick.AddListener(() => { AddOpponent(); });
                else
                    rowBtn.interactable = false;
            }

            var strip = t.Find("ColorStrip");
            if (strip != null && !occupied)
            {
                // Empty rows carry no colour — a coloured swatch on a slot with
                // nobody in it reads as an occupied slot.
                if (strip.TryGetComponent(out Image emptyStrip))
                {
                    emptyStrip.color = new Color(1f, 1f, 1f, 0.07f);
                    emptyStrip.raycastTarget = false;
                }
                if (strip.TryGetComponent(out Button emptyStripBtn))
                    emptyStripBtn.interactable = false;
            }
            else if (strip != null)
            {
                if (strip.TryGetComponent(out Image stripImg))
                {
                    stripImg.color = slot.GetFactionColor();
                    // The builder creates swatches with raycastTarget off; the
                    // Button needs a raycastable graphic to be clickable.
                    stripImg.raycastTarget = true;
                }
                if (strip.TryGetComponent(out Button stripBtn))
                    stripBtn.onClick.AddListener(() =>
                    {
                        // Opens the 12-swatch picker. This used to cycle to the
                        // next free colour, which took up to eleven clicks to
                        // reach a specific one. docs/Design/Lobby_Setup.md
                        ColorPickerPopup.Open(
                            this,
                            strip as RectTransform,
                            LobbyConfig.Slots[idx].ColorIndex,
                            c => IsColorInUse(c, idx),
                            chosen =>
                            {
                                LobbyConfig.Slots[idx].ColorIndex = chosen;
                                if (stripImg != null)
                                    stripImg.color = LobbyConfig.Slots[idx].GetFactionColor();
                                // Start markers are tinted by slot colour.
                                RefreshStartMarkers();
                            });
                    });
            }

            var team = t.Find("TeamDropdown")?.GetComponent<TMP_Dropdown>();
            var chipRoot = t.Find("TeamChip");
            var chip = chipRoot?.GetComponentInChildren<TMP_Text>(true);
            var badge = t.Find("HostBadge");
            var name = t.Find("NameLabel")?.GetComponent<TMP_Text>();
            var strat = t.Find("StrategyDropdown")?.GetComponent<TMP_Dropdown>();
            var diff = t.Find("DifficultyDropdown")?.GetComponent<TMP_Dropdown>();
            var remove = t.Find("RemoveButton")?.GetComponent<Button>();

            // Observer mode: the host slot is AI-controlled too — it gets the
            // same strategy/difficulty dropdowns as every other AI warband.
            bool aiRow = index != 0 || _observer;

            // HOST is a multiplayer concept — there is no host in a single
            // player skirmish, so the badge never shows here.
            if (badge != null) badge.gameObject.SetActive(false);

            // Team: a dropdown reading NO TEAM / TEAM 1 .. TEAM MaxTeams,
            // styled and behaving like the personality and difficulty columns
            // beside it. The dropdown VALUE is the team index, so no mapping is
            // needed either way. docs/Design/Teams.md
            if (team != null)
            {
                team.gameObject.SetActive(occupied);
                if (occupied)
                {
                    SetOptions(team, TeamNames);
                    team.SetValueWithoutNotify(
                        Mathf.Clamp(slot.TeamIndex, 0, Alliances.MaxTeams));
                    team.onValueChanged.AddListener(v =>
                    {
                        LobbyConfig.Slots[idx].TeamIndex = (byte)v;
                        // The player has spoken: stop applying the map's preset
                        // over the top of their choice for this map.
                        _teamsOverridden = true;
                    });
                }
                // The chip it replaced, if the row still carries one.
                if (chipRoot != null) chipRoot.gameObject.SetActive(false);
            }
            // Fallback for a row template that has not been converted: the
            // original chip, clicked to cycle "-" -> 1 .. MaxTeams -> "-".
            else if (chipRoot != null)
            {
                chipRoot.gameObject.SetActive(occupied);
                if (occupied)
                {
                    if (chip != null) chip.text = TeamLabel(slot.TeamIndex);
                    var chipImg = chipRoot.GetComponent<Image>();
                    if (chipImg != null) chipImg.color = TeamChipColor(slot.TeamIndex);

                    var chipBtn = chipRoot.GetComponent<Button>()
                                  ?? chipRoot.gameObject.AddComponent<Button>();
                    chipBtn.onClick.AddListener(() =>
                    {
                        var s = LobbyConfig.Slots[idx];
                        s.TeamIndex = (byte)((s.TeamIndex + 1) % (Alliances.MaxTeams + 1));
                        _teamsOverridden = true;
                        if (chip != null) chip.text = TeamLabel(s.TeamIndex);
                        if (chipImg != null) chipImg.color = TeamChipColor(s.TeamIndex);
                    });
                }
            }

            if (name != null)
            {
                name.gameObject.SetActive(true);
                name.alignment = TextAlignmentOptions.MidlineLeft;
                switch (kind)
                {
                    case RowKind.Occupied:
                        name.color = Color.white;
                        name.text = aiRow
                            ? string.Format(Loc.T("AI ({0})"),
                                            Loc.T(DifficultyNames[(int)slot.AIDifficulty]))
                            : (string.IsNullOrEmpty(slot.PlayerName) ? Loc.T("PLAYER")
                                                                     : slot.PlayerName.ToUpperInvariant());
                        break;
                    case RowKind.AddHere:
                        name.color = Color.white; // gold once; the screen is all-white text now
                        name.text = Loc.T("+ ADD PLAYER");
                        break;
                    default:
                        name.color = new Color(1f, 1f, 1f, 0.22f);
                        name.text = index < MaxPlayers() ? Loc.T("EMPTY") : "—";
                        break;
                }
            }

            if (strat != null)
            {
                strat.gameObject.SetActive(occupied && aiRow);
                if (occupied && aiRow)
                {
                    SetOptions(strat, StrategyNames);
                    strat.SetValueWithoutNotify((int)slot.AIStrategy);
                    strat.onValueChanged.AddListener(v => LobbyConfig.Slots[idx].AIStrategy = (LobbyAIStrategy)v);
                }
            }
            if (diff != null)
            {
                diff.gameObject.SetActive(occupied && aiRow);
                if (occupied && aiRow)
                {
                    SetOptions(diff, DifficultyNames);
                    diff.SetValueWithoutNotify((int)slot.AIDifficulty);
                    diff.onValueChanged.AddListener(v =>
                    {
                        LobbyConfig.Slots[idx].AIDifficulty = (LobbyAIDifficulty)v;
                        // The name column carries the difficulty, so keep it
                        // in step rather than letting the two disagree.
                        if (name != null)
                            name.text = string.Format(Loc.T("AI ({0})"), Loc.T(DifficultyNames[v]));
                    });
                }
            }
            // START WITH CULTURE: every occupied row — the player's too — picks
            // the culture it starts in, beside difficulty and personality.
            var culture = StartCultureOption.RowDropdown(t, diff != null ? diff : strat);
            if (culture != null)
            {
                bool show = occupied && _startWithCulture;
                culture.gameObject.SetActive(show);
                if (show)
                    StartCultureOption.Fill(culture, slot.StartCulture,
                        c => LobbyConfig.Slots[idx].StartCulture = c);
            }
            if (remove != null)
            {
                remove.gameObject.SetActive(occupied && index != 0 && LobbyConfig.ActiveSlotCount > 2);
                if (occupied && index != 0)
                    remove.onClick.AddListener(() => { RemoveSlot(idx); RebuildRoster(); });
            }

            // No column pass here on purpose - see the note where the row is
            // cloned. Order and width come from the template.
        }

        /// <summary>Team column text: a number, or "-" when unteamed.</summary>
        private static string TeamLabel(byte team)
            => team == Alliances.NoTeam ? "-" : team.ToString();

        /// <summary>
        /// Tint for the team chip. Deliberately NOT a player colour — team
        /// membership has to read as a separate axis from who is who, and
        /// player colour never changes for any reason. docs/Design/Teams.md
        /// </summary>
        private static Color TeamChipColor(byte team) => team switch
        {
            // No team reads as an empty outlined chip holding "-", not a solid
            // block — an unteamed slot is the default and should be quiet.
            Alliances.NoTeam => new Color(1f, 1f, 1f, 0.06f),
            1 => new Color(0.36f, 0.62f, 0.94f),
            2 => new Color(0.94f, 0.48f, 0.36f),
            3 => new Color(0.50f, 0.84f, 0.48f),
            4 => new Color(0.84f, 0.74f, 0.38f),
            _ => new Color(0.32f, 0.32f, 0.36f),
        };

        /// <summary>
        /// True when two or more occupied slots exist and every one of them
        /// sits on the same real team — a roster with no hostility in it.
        /// Unteamed slots (Alliances.NoTeam) are hostile to everyone including
        /// each other, so an all-unteamed roster is a normal free-for-all and
        /// never trips this.
        /// </summary>
        private static bool AllSlotsOnOneTeam()
        {
            int occupied = 0;
            byte team = Alliances.NoTeam;
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var s = LobbyConfig.Slots[i];
                if (s == null || s.Type == SlotType.Empty) continue;
                if (occupied == 0) team = s.TeamIndex;
                else if (s.TeamIndex != team) return false;
                occupied++;
            }
            return occupied > 1 && team != Alliances.NoTeam;
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
            var savedTeam = new byte[8];
            var savedStart = new int[8];
            for (int i = 0; i < 8; i++)
            {
                var s = LobbyConfig.Slots[i];
                savedColor[i] = s.ColorIndex;
                savedDiff[i] = s.AIDifficulty;
                savedStrat[i] = s.AIStrategy;
                savedTeam[i] = s.TeamIndex;
                savedStart[i] = s.StartIndex;
            }

            LobbyConfig.SetupSinglePlayer(count);

            for (int i = 0; i < 8; i++)
            {
                var s = LobbyConfig.Slots[i];
                s.ColorIndex = savedColor[i];
                s.AIDifficulty = savedDiff[i];
                s.AIStrategy = savedStrat[i];
                s.TeamIndex = savedTeam[i];
                s.StartIndex = savedStart[i];
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
                a.TeamIndex = b.TeamIndex;
                a.StartIndex = b.StartIndex;
            }
            // The removed slot's start assignment must not linger on the tail
            // slot the shift left behind, or a start stays claimed by nobody.
            int tail = LobbyConfig.ActiveSlotCount - 1;
            if (tail >= 0 && tail < LobbyConfig.Slots.Length && LobbyConfig.Slots[tail] != null)
                LobbyConfig.Slots[tail].StartIndex = PlayerSlot.AutoStart;
            SetSlotCount(LobbyConfig.ActiveSlotCount - 1);
        }

        // CycleSlotColor removed: the colour swatch opens ColorPickerPopup now,
        // so nothing cycles. ResolveColorConflicts still handles automatic
        // de-duplication when slot counts change. docs/Design/Lobby_Setup.md

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
            GameSettings.StartingResources = _startResources;
            GameSettings.BorderEnabled = _curseNodes;
            GameSettings.IsObserver = _observer;
            StartCultureOption.Apply(_startWithCulture);
            GameSettings.Mode = GameMode.FreeForAll;

            // Last call before the table is published: make sure the map's team
            // layout reached the roster even if the panel never refreshed after
            // the last roster edit. ApplyColorSelections is what pushes teams
            // into Alliances, so this has to land first. docs/Design/Teams.md
            ApplyMapTeamPreset();

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

            // A match needs two SIDES. Teams are hostility, so a roster where
            // every slot shares one team has no enemies in it at all — the
            // victory check sees one side remaining and ends the match with a
            // VICTORY banner about ten seconds in. That reads as "I instantly
            // win", not as "my lobby was wrong", so refuse it here where the
            // player can still fix it. docs/Design/Teams.md
            if (AllSlotsOnOneTeam())
            {
                SetError("Everyone is on the same team — someone has to be the enemy!");
                return;
            }

            // Last word on start positions: nobody launches unassigned. The
            // lobby pre-assigns and tops up as the roster changes, but a slot
            // can still be blank here — its start was taken by someone else on
            // the map preview (the evicted player is released), or the player
            // clicked their own dot to let it go. Give them a random free one
            // rather than falling through to silent automatic placement.
            AssignMissingStarts();

            GameSettings.TotalPlayers = humanCount + aiCount;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            // Static and sticky across scene loads: without this, "tutorial →
            // main menu → skirmish" keeps the coach overlay running.
            GameSettings.TutorialActive = false;

            SetError(null);

            // Launch-path forensics: which flow started a match is the FIRST
            // question of every "multiplayer didn't work" report (2026-08-17:
            // a match believed to be MP turned out to be this path — the log
            // folder had no role suffix and no lockstep lines, but nothing
            // recorded what was clicked). Tagged, so console capture keeps it.
            Debug.Log($"[SkirmishPanel] Launching SINGLE-PLAYER skirmish — map " +
                      $"{GameSettings.SelectedMapScene}, seed {GameSettings.SpawnSeed}, " +
                      $"players {GameSettings.TotalPlayers}, multiplayer=OFF.");

            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        private void SetError(string message)
        {
            // Render chokepoint — callers pass the English source string.
            if (ErrorText != null) ErrorText.text = message == null ? string.Empty : Loc.T(message);
        }
    }
}
