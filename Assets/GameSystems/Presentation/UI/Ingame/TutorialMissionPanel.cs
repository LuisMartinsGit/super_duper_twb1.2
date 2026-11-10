// TutorialMissionPanel.cs
// The tutorial mission's screen: the objectives list, one coach line for what to
// do next, and keeping every panel the mission has not reached yet off screen.
// docs/Design/Tutorial_Missions.md
//
// It only reads: TutorialMissionSystem owns the mission and publishes its
// progress in TutorialMissionState. Like the old coach, nothing here is a
// raycast target, so it cannot eat a click meant for the map.
//
// Panels are hidden through a CanvasGroup, not SetActive: several of them switch
// themselves on and off by selection and would undo it.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Input;
using TheWaningBorder.Tutorial;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class TutorialMissionPanel : MonoBehaviour
    {
        private const float PanelWidth = 760f;
        private const float RefreshInterval = 0.25f;

        /// <summary>Never part of the Basic mission.</summary>
        private static readonly string[] AlwaysHidden =
        {
            "GameUI_TopChoiceBar", "GameUI_SpecialBuildingMenu", "GameUI_CultureSelectionButton",
            "GameUI_ReligionPanel", "GameUI_ScoreBoard", "GameUI_ObjectivesPanel",
            "GameUI_SpellsPanel",
        };

        /// <summary>Off screen until the camp is found: no build screen and no
        /// resources while the Litharch is still searching.</summary>
        private static readonly string[] HiddenBeforeCamp =
        {
            "GameUI_ResourcePanel", "GameUI_IdleBuilders",
        };

        /// <summary>Drives a CanvasGroup of its own every frame, so a group here
        /// would be overwritten: it is switched off whole instead.</summary>
        private const string ActionsPanel = "GameUI_ActionsPanel";

        /// <summary>Rows of the resource panel the Basic mission never uses.</summary>
        private static readonly string[] HiddenResourceRows = { "Religion" };

        private sealed class Row
        {
            public TMP_Text Text;
            public System.Func<bool> Visible;
            public System.Func<string> Label;
            public System.Func<bool> Done;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<string, CanvasGroup> _groups = new Dictionary<string, CanvasGroup>();
        private TMP_Text _title, _body;
        private Transform _uiRoot;
        private float _timer;
        private Vector3 _cameraStart;
        private bool _cameraStartKnown, _cameraMoved, _selectedOnce;

        private void Start()
        {
            _uiRoot = transform.root;
            Build();
            Refresh();
        }

        private void Build()
        {
            var cfg = TutorialMissionSystemConfig.I;
            var root = GameUIKit.Rect(transform, "TutorialMission");
            root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
            root.pivot = new Vector2(0f, 0.5f);
            root.anchoredPosition = new Vector2(40f, 220f);
            root.sizeDelta = new Vector2(PanelWidth, 0f);

            GameUIKit.PanelChrome(root);
            var stack = GameUIKit.VStack(root, 24f, 8f);
            stack.childForceExpandHeight = false;
            root.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            _title = GameUIKit.Text(root, "title", "", 32f, GameUIKit.Gold);
            _title.fontStyle = FontStyles.Bold;
            _body = GameUIKit.Text(root, "body", "", 24f, GameUIKit.TextMain);

            bool Open(TutorialStage s) => TutorialMissionState.Stage >= s;

            if (GameSettings.TutorialAdvancedActive)
            {
                AddAdvancedRows(root);
                GameUIKit.DisableRaycasts(root.gameObject);
                return;
            }

            AddRow(root, () => true,
                () => string.Format(Loc.T("1 - Rescue all the workers ({0}/{1})"),
                    TutorialMissionState.Workers, cfg.workerSites.Length * cfg.workersPerSite),
                () => TutorialMissionState.Stage > TutorialStage.Rescue);
            AddRow(root, () => Open(TutorialStage.Camp),
                () => string.Format(Loc.T("2 - Build the Shelter ({0}/{1})"),
                    Mathf.Min(1, TutorialMissionState.Shelters), 1),
                () => TutorialMissionState.Stage > TutorialStage.Camp);
            AddRow(root, () => Open(TutorialStage.Settle),
                () => string.Format(Loc.T("3a - Build {1} Huts ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.Huts, cfg.hutsNeeded), cfg.hutsNeeded),
                () => TutorialMissionState.Huts >= cfg.hutsNeeded);
            AddRow(root, () => Open(TutorialStage.Settle),
                () => string.Format(Loc.T("3b - Build {1} Gatherer's Huts ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.GatherersHuts, cfg.gatherersHutsNeeded), cfg.gatherersHutsNeeded),
                () => TutorialMissionState.GatherersHuts >= cfg.gatherersHutsNeeded);
            AddRow(root, () => Open(TutorialStage.Settle),
                () => string.Format(Loc.T("3c - Build {0} Veilstone Mine and {1} Iron Mines ({2}/{0}, {3}/{1})"),
                    cfg.veilstoneMinesNeeded, cfg.ironMinesNeeded,
                    Mathf.Min(TutorialMissionState.VeilstoneMines, cfg.veilstoneMinesNeeded),
                    Mathf.Min(TutorialMissionState.IronMines, cfg.ironMinesNeeded)),
                () => TutorialMissionState.VeilstoneMines >= cfg.veilstoneMinesNeeded
                   && TutorialMissionState.IronMines >= cfg.ironMinesNeeded);
            AddRow(root, () => Open(TutorialStage.Settle),
                () => string.Format(Loc.T("3d - Build a Barracks ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.Barracks, cfg.barracksNeeded), cfg.barracksNeeded),
                () => TutorialMissionState.Barracks >= cfg.barracksNeeded);
            int lost = cfg.spearmanSites.Length * cfg.spearmenPerSite;
            AddRow(root, () => Open(TutorialStage.Muster),
                () => string.Format(Loc.T("4a - Train Scouts ({0}/{1}) and find the lost Spearmen ({2}/{3})"),
                    Mathf.Min(TutorialMissionState.ScoutsTrained, cfg.scoutsNeeded), cfg.scoutsNeeded,
                    TutorialMissionState.SpearmenFound, lost),
                () => TutorialMissionState.ScoutsTrained >= cfg.scoutsNeeded
                   && TutorialMissionState.SpearmenFound >= lost);
            AddRow(root, () => Open(TutorialStage.Muster),
                () => string.Format(Loc.T("4b - Train {1} Spearmen ({0}/{1})"),
                    Mathf.Min(TutorialMissionState.SpearmenTrained, cfg.spearmenToTrain), cfg.spearmenToTrain),
                () => TutorialMissionState.SpearmenTrained >= cfg.spearmenToTrain);
            AddRow(root, () => TutorialMissionState.CurseOpen,
                () => TutorialMissionState.Over
                    ? Loc.T("5 - Survive the curse long enough to vanquish the node")
                    : string.Format(Loc.T("5 - Survive the curse long enough to vanquish the node (next wave {0})"),
                        FormatSeconds(TutorialMissionState.SecondsToNextWave)),
                () => TutorialMissionState.Over && TutorialMissionState.Won);

            GameUIKit.DisableRaycasts(root.gameObject);
        }

        private void AddRow(RectTransform parent, System.Func<bool> visible,
            System.Func<string> label, System.Func<bool> done)
        {
            _rows.Add(new Row
            {
                Text = GameUIKit.Text(parent, "row" + _rows.Count, "", 24f, GameUIKit.TextMain),
                Visible = visible,
                Label = label,
                Done = done,
            });
        }

        private static string FormatSeconds(float s)
        {
            int total = Mathf.CeilToInt(s);
            return $"{total / 60}:{total % 60:00}";
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < RefreshInterval) return;
            _timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            ObservePlayer();

            foreach (var r in _rows)
            {
                bool show = r.Visible();
                if (r.Text.gameObject.activeSelf != show) r.Text.gameObject.SetActive(show);
                if (!show) continue;
                bool done = r.Done();
                r.Text.text = (done ? "<s>" : "") + r.Label() + (done ? "</s>" : "");
                r.Text.color = done ? GameUIKit.TextDim : GameUIKit.TextMain;
            }

            WriteCoach();
            HidePanels();
        }

        private void ObservePlayer()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                if (!_cameraStartKnown) { _cameraStart = cam.transform.position; _cameraStartKnown = true; }
                else if ((cam.transform.position - _cameraStart).sqrMagnitude > 25f) _cameraMoved = true;
            }
            var selection = SelectionSystem.CurrentSelection;
            if (selection != null && selection.Count > 0) _selectedOnce = true;
        }

        // ── The coach line ────────────────────────────────────────────────

        private void WriteCoach()
        {
            string title, body;
            var cfg = TutorialMissionSystemConfig.I;

            if (GameSettings.TutorialAdvancedActive)
            {
                WriteAdvancedCoach(out title, out body);
            }
            else if (TutorialMissionState.CurseOpen)
            {
                title = Loc.T("The curse");
                body = string.Format(Loc.T("A curse node stands in the east, and it sends a wave every {0} minutes. "
                    + "Keep soldiers near the camp, and train more than you lose. When your army is strong enough, "
                    + "select it, press <b>A</b> and right-click the node. The mission ends when the node falls."),
                    Mathf.RoundToInt(cfg.waveIntervalSeconds / 60f));
            }
            else
            {
                switch (TutorialMissionState.Stage)
                {
                    case TutorialStage.Rescue: Rescue(out title, out body); break;
                    case TutorialStage.Camp: Camp(out title, out body); break;
                    case TutorialStage.Settle: Settle(out title, out body); break;
                    default:
                        title = Loc.T("Muster");
                        body = Loc.T("Select the <b>Shelter</b> and train Scouts, then send them down the side paths "
                            + "off the clearing to find the lost Spearmen. Select the <b>Barracks</b> to train more. "
                            + "Research there too: <b>Stone Weapons</b> makes every soldier hit harder.");
                        break;
                }
            }
            _title.text = title;
            _body.text = body;
        }

        private void Rescue(out string title, out string body)
        {
            if (!_cameraMoved)
            {
                title = Loc.T("After the calamity");
                body = Loc.T("Your Litharch is searching the wilderness for anything that survived. "
                    + "Look around first: use the <b>arrow keys</b>, hold the <b>middle mouse button</b> and drag, "
                    + "or push the cursor to the edge of the screen. The <b>mouse wheel</b> zooms.");
            }
            else if (!_selectedOnce)
            {
                title = Loc.T("Select your Litharch");
                body = Loc.T("<b>Left-click</b> the Litharch to select them.");
            }
            else if (!TutorialMissionState.LitharchMoved)
            {
                title = Loc.T("Give an order");
                body = Loc.T("<b>Right-click</b> the ground to walk there. The only way on is the path north through the trees.");
            }
            else if (!TutorialMissionState.ChainedOrders)
            {
                title = Loc.T("Chain your orders");
                body = Loc.T("Hold <b>Shift</b> while you right-click several points along the path. "
                    + "The Litharch walks to each one in turn.");
            }
            else
            {
                title = Loc.T("Find the workers");
                body = Loc.T("People are lost in these woods. Bring the Litharch close and they will join you.");
            }
        }

        private static void Camp(out string title, out string body)
        {
            if (!TutorialMissionState.CampFound)
            {
                title = Loc.T("A place to camp");
                body = Loc.T("Everyone is together. Drag a box to select them all, then lead them to the clearing "
                    + "marked on the minimap.");
            }
            else
            {
                title = Loc.T("Build the Shelter");
                body = Loc.T("These are your <b>resources</b>, bottom left. This ground is yours now. Select a Worker, "
                    + "choose the <b>Shelter</b> from the build menu and click a spot in the clearing. "
                    + "The more Workers you send, the faster it rises.");
            }
        }

        private static void Settle(out string title, out string body)
        {
            title = Loc.T("Build up the camp");
            body = Loc.T("Nobody gathers by hand: a building on a resource node pays you by itself. "
                + "<b>Gatherer's Huts</b> go on supply nodes. The <b>Mine</b> button goes on iron, and on the crystal "
                + "outcrop it raises a Veilstone Mine. <b>Huts</b> house your people, and the <b>Barracks</b> trains soldiers.");
        }

        // ── What is on screen ─────────────────────────────────────────────

        private void HidePanels()
        {
            if (GameSettings.TutorialAdvancedActive)
            {
                HideAdvancedPanels();
                return;
            }
            foreach (var name in AlwaysHidden) SetHidden(name, true);
            bool beforeCamp = !TutorialMissionState.CampFound;
            foreach (var name in HiddenBeforeCamp) SetHidden(name, beforeCamp);
            var actions = GameUIKit.FindDeep(_uiRoot, ActionsPanel);
            if (actions != null && actions.gameObject.activeSelf == beforeCamp)
                actions.gameObject.SetActive(!beforeCamp);

            var resources = GameUIKit.FindDeep(_uiRoot, "GameUI_ResourcePanel");
            if (resources == null) return;
            foreach (var row in HiddenResourceRows)
            {
                var t = GameUIKit.FindDeep(resources, row);
                if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
            }
        }

        /// <summary>The Alanthor mission has the whole interface, but for the score
        /// and religion until religion is taught.</summary>
        private void HideAdvancedPanels()
        {
            SetHidden("GameUI_ScoreBoard", true);
            SetHidden("GameUI_ObjectivesPanel", true);
            bool before = TutorialMissionState.AdvancedStage < AdvancedStage.Points;
            SetHidden("GameUI_ReligionPanel", TutorialMissionState.AdvancedStage < AdvancedStage.Religion);
            var resources = GameUIKit.FindDeep(_uiRoot, "GameUI_ResourcePanel");
            var row = resources != null ? GameUIKit.FindDeep(resources, "Religion") : null;
            if (row != null && row.gameObject.activeSelf == before) row.gameObject.SetActive(!before);
        }

        private void SetHidden(string panel, bool hidden)
        {
            if (!_groups.TryGetValue(panel, out var group) || group == null)
            {
                var t = GameUIKit.FindDeep(_uiRoot, panel);
                if (t == null) return;   // not spawned yet, or not in this match
                group = t.GetComponent<CanvasGroup>();
                if (group == null) group = t.gameObject.AddComponent<CanvasGroup>();
                _groups[panel] = group;
            }
            group.alpha = hidden ? 0f : 1f;
            group.interactable = !hidden;
            group.blocksRaycasts = !hidden;
        }
    }
}
