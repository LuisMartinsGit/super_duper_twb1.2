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
        // The dropdown picks an age AND the culture every faction starts in —
        // the labels have always carried the culture suffix, so the list is
        // age/culture combinations rather than two separate controls.
        //
        // The tables themselves moved to LobbyOptions when the multiplayer
        // lobby started offering the same two options: one source, so the two
        // screens cannot drift apart on what an age label promises.
        private static readonly string[] AgeNames = LobbyOptions.AgeNames;
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
            label.text = Loc.T("OBSERVER");
            label.fontSize = 13f;
            label.fontStyle = TMPro.FontStyles.Bold;
            if (FogState != null) { label.font = FogState.font; label.color = FogState.color; }
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.minHeight = 18f;

            // Sizes are READ off the fog option rather than hard-coded. The
            // constants that used to sit here (13pt, 26x96) were written for a
            // 1080p canvas and rendered this option visibly smaller than its
            // neighbours on the 3840x2160 one. Borrowing values is not the
            // structural coupling the summary above warns about - nothing here
            // reaches into another cell's child objects.
            float stateSize = FogState != null ? FogState.fontSize : 13f;
            var fogLe = FogToggle.GetComponent<LayoutElement>();
            float trackH = fogLe != null && fogLe.minHeight > 0f ? fogLe.minHeight : 26f;
            float trackW = fogLe != null && fogLe.minWidth > 0f ? fogLe.minWidth : 96f;

            // Row: ON/OFF caption beside the switch, same shape as the authored
            // fog and curse cells.
            var pillGo = new GameObject("Pill", typeof(RectTransform));
            pillGo.transform.SetParent(cellGo.transform, false);
            var pillH = pillGo.AddComponent<HorizontalLayoutGroup>();
            pillH.childControlWidth = true;
            pillH.childControlHeight = true;
            pillH.childForceExpandWidth = false;
            pillH.childForceExpandHeight = false;
            pillH.childAlignment = TextAnchor.MiddleLeft;
            pillH.spacing = 10f;

            var stateGo = new GameObject("State", typeof(RectTransform));
            stateGo.transform.SetParent(pillGo.transform, false);
            var stateText = stateGo.AddComponent<TMPro.TextMeshProUGUI>();
            stateText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            stateText.fontSize = stateSize;
            stateText.text = Loc.T("OFF");
            stateText.raycastTarget = false;
            if (FogState != null) { stateText.font = FogState.font; stateText.color = FogState.color; }
            _observerState = stateText;

            var trackGo = new GameObject("Track", typeof(RectTransform));
            trackGo.transform.SetParent(pillGo.transform, false);
            var img = trackGo.AddComponent<Image>();
            img.color = PillOff;
            _observerToggle = trackGo.AddComponent<Button>();
            _observerToggle.targetGraphic = img;
            var trackLe = trackGo.AddComponent<LayoutElement>();
            trackLe.minHeight = trackLe.preferredHeight = trackH;
            trackLe.minWidth = trackLe.preferredWidth = trackW;

            // Same Synty art as the authored switches, taken off the fog one so
            // this stays a plain pill when the dressing pass has not been run.
            var fogSwitch = FogToggle.GetComponent<MenuToggleSwitch>();
            if (fogSwitch != null)
                MenuToggleSwitch.Attach((RectTransform)trackGo.transform,
                    fogSwitch.TrackSprite, fogSwitch.OutlineSprite, fogSwitch.KnobSprite,
                    fogSwitch.KnobWidth, fogSwitch.KnobInset, fogSwitch.KnobPadding);

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
            // The arrays stay English (selection travels by index); the
            // visible option list is translated here, at the render point.
            dd.ClearOptions();
            var list = new System.Collections.Generic.List<string>(names.Length);
            for (int i = 0; i < names.Length; i++) list.Add(Loc.T(names[i]));
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
            if (state != null) state.text = Loc.T(on ? "ON" : "OFF");
            if (toggle == null) return;

            // Dressed as a Synty sliding switch by MapOptionsChrome; the flat
            // colour swap below is the fallback for an undressed toggle, so the
            // option still reads as on or off before that pass has been run.
            var sw = toggle.GetComponent<MenuToggleSwitch>();
            if (sw != null) { sw.SetOn(on); return; }

            if (toggle.targetGraphic is Image img)
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
            if (info == null || !info.HasTeamPreset) return false;

            bool changed = false;
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot == null || slot.Type == SlotType.Empty) continue;
                if (slot.StartIndex == PlayerSlot.AutoStart) continue;

                byte team = info.TeamForStart(slot.StartIndex);
                if (slot.TeamIndex == team) continue;
                slot.TeamIndex = team;
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
            GameSettings.MaxStartingResources = _maxResources;
            GameSettings.BorderEnabled = _curseNodes;
            GameSettings.IsObserver = _observer;
            GameSettings.StartAge = _startAge;
            GameSettings.StartCulture = LobbyOptions.CultureForAge(_startAge);
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
