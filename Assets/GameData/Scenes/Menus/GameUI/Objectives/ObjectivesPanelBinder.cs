// ObjectivesPanelBinder.cs
// Binds the authored ObjectivesPanel prefab (GameUICatalog.objectivesPanel,
// top-left) to live match state. Four fixed step rows, found by node name:
//   Step_Special  — build a choice building (Vault / Keep)
//   Step_Culture  — select a culture and age up (Era 2)
//   Step_Temple   — 3A: build the Temple, upgrade it through the ages,
//                   then claim every curse node with the culture's verb
//                   (Alanthor purify / Runai pacify / Feraldis destroy)
//   Step_Military — 3B: destroy all other players
// Step states: pending = dim, active = white, done = dim + strikethrough.
//
// COLLAPSIBLE AND OUT OF THE WAY (2026-11-10, developer: it sat in front of
// the income breakdown). Clicking the title bar folds the panel to a small
// tab — the title alone — and back; the choice holds for the rest of the
// session (a static, not a save). Collapsed or not, the panel lines itself
// up to the RIGHT of the economy panel's header (EconomyPanel, the
// per-territory income breakdown on the left edge), every frame, from that
// header's on-screen rect: the two live on canvases that scale differently,
// so an authored offset could not keep them apart at every HUD scale.

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class ObjectivesPanelBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        private TMP_Text _stepSpecial, _stepCulture, _stepTemple, _stepMilitary;
        private float _timer;
        private bool _visible = true;

        // ── Collapse (session state) and placement ────────────────────────

        /// <summary>Folded to its title tab. Static: holds for the session.</summary>
        private static bool _collapsed;
        /// <summary>Collapsed tab: the title band's height (panel units), and its width.</summary>
        private const float CollapsedHeight = 68f;
        private const float CollapsedWidth = 330f;
        /// <summary>Gap to the economy header, as a share of that header's height.</summary>
        private const float EconomyGapShare = 0.35f;

        private static readonly string[] StepNodes =
            { "Step_Special", "Step_Culture", "Step_Temple", "Step_Military" };

        private RectTransform _rect;
        private Vector2 _authoredSize;
        private TMP_Text _arrow;
        private GameObject[] _steps;
        private Vector3 _placedAt = new Vector3(float.NaN, 0f, 0f);

        private static readonly Color Active = new Color(0.92f, 0.90f, 0.84f);
        private static readonly Color Done   = new Color(0.55f, 0.62f, 0.55f);
        private static readonly Color Dim    = new Color(0.55f, 0.55f, 0.55f, 0.85f);

        // Cached queries — CreateEntityQuery per frame leaks into the world's
        // query registry (see reference_managed_query_leak).
        private static readonly ComponentType[] HallQueryTypes =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] TempleQueryTypes =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] WellQueryTypes =
        {
            ComponentType.ReadOnly<BorderMainNodeTag>(),
            ComponentType.ReadOnly<BorderNodeState>(),
        };
        private static readonly ComponentType[] BuildingQueryTypes =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] VictoryQueryTypes =
        {
            ComponentType.ReadOnly<NodeVictoryState>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _hallQuery;
        private TheWaningBorder.Core.CachedEntityQuery _templeQuery;
        private TheWaningBorder.Core.CachedEntityQuery _wellQuery;
        private TheWaningBorder.Core.CachedEntityQuery _buildingQuery;
        private TheWaningBorder.Core.CachedEntityQuery _victoryQuery;

        private void Awake()
        {
            _stepSpecial  = FindLabel("Step_Special");
            _stepCulture  = FindLabel("Step_Culture");
            _stepTemple   = FindLabel("Step_Temple");
            _stepMilitary = FindLabel("Step_Military");
            if (_stepSpecial == null)
                TWBLog.Log("[GameUI] ObjectivesPanel: step nodes not found — check prefab names.");

            _rect = (RectTransform)transform;
            _authoredSize = _rect.sizeDelta;
            _steps = new GameObject[StepNodes.Length];
            for (int i = 0; i < StepNodes.Length; i++)
            {
                var node = TheWaningBorder.UI.Ingame.GameUIKit.FindDeep(transform, StepNodes[i]);
                _steps[i] = node != null ? node.gameObject : null;
            }
            BuildToggle();
            ApplyCollapse();
        }

        /// <summary>The title bar becomes the fold toggle: a transparent hit
        /// area over it and a ▾ / ▸ marker at its left end.</summary>
        private void BuildToggle()
        {
            var title = TheWaningBorder.UI.Ingame.GameUIKit.FindDeep(transform, "Title") as RectTransform;
            if (title == null)
            {
                TWBLog.Log("[GameUI] ObjectivesPanel: no \"Title\" node — the panel cannot fold.");
                return;
            }
            var hit = TheWaningBorder.UI.Ingame.GameUIKit.Image(title, "FoldToggle",
                new Color(0f, 0f, 0f, 0f), raycast: true);
            TheWaningBorder.UI.Ingame.GameUIKit.Stretch(hit.rectTransform);
            var relay = TheWaningBorder.UI.Ingame.UITooltip.Relay(hit.gameObject);
            relay.OnLeftClick = () => { _collapsed = !_collapsed; ApplyCollapse(); };
            TheWaningBorder.UI.Ingame.UITooltip.Bind(hit.gameObject,
                () => _collapsed ? Loc.T("Show the objectives") : Loc.T("Hide the objectives"));

            _arrow = TheWaningBorder.UI.Ingame.GameUIKit.Text(title, "FoldArrow", "", 30f,
                new Color(1f, 0.86f, 0.55f), TextAlignmentOptions.Left, wrap: false);
            var ar = _arrow.rectTransform;
            ar.anchorMin = new Vector2(0f, 0f);
            ar.anchorMax = new Vector2(0f, 1f);
            ar.pivot = new Vector2(0f, 0.5f);
            ar.anchoredPosition = new Vector2(10f, 0f);
            ar.sizeDelta = new Vector2(40f, 0f);
        }

        /// <summary>Folded: the title tab only. Open: the authored panel.</summary>
        private void ApplyCollapse()
        {
            bool open = !_collapsed && _visible;
            if (_steps != null)
                foreach (var s in _steps)
                    if (s != null && s.activeSelf != open) s.SetActive(open);
            if (_rect != null)
                _rect.sizeDelta = _collapsed
                    ? new Vector2(Mathf.Min(CollapsedWidth, _authoredSize.x), Mathf.Min(CollapsedHeight, _authoredSize.y))
                    : _authoredSize;
            if (_arrow != null) _arrow.text = _collapsed ? "▸" : "▾";
        }

        /// <summary>Beside the economy header, top edges level, every frame
        /// (only moved when the spot changes).</summary>
        private void LateUpdate()
        {
            if (_rect == null || !EconomyPanel.TryGetHeaderScreenRect(out var header)) return;
            var at = new Vector3(header.xMax + header.height * EconomyGapShare, header.yMax, 0f);
            if (at == _placedAt) return;
            _placedAt = at;
            // Overlay canvas: world space is screen pixels, and the panel's
            // pivot is its top-left corner.
            _rect.position = at;
        }

        private TMP_Text FindLabel(string node)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == node)
                    return t.GetComponentInChildren<TMP_Text>(true);
            return null;
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
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            bool ok = world != null && world.IsCreated && !GameSettings.IsSpectating;
            // Toggle the rendered children, never this GameObject — a
            // self-deactivate would stop Update and the panel could never
            // come back once the world finishes loading.
            if (_visible != ok)
            {
                _visible = ok;
                foreach (Transform child in transform)
                    if (child.gameObject.activeSelf != ok) child.gameObject.SetActive(ok);
                ApplyCollapse();
            }
            if (!ok) return;

            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            RefreshSpecial(em, faction);
            RefreshCulture(em, faction);
            RefreshTemple(em, faction);
            RefreshMilitary(em, faction);
        }

        // ── Step 1: special building ───────────────────────────────────────

        private void RefreshSpecial(EntityManager em, Faction faction)
        {
            if (_stepSpecial == null) return;
            bool done = BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction) != null;
            bool started = !done && BuildingFactory.GetFactionChoiceBuilding(em, faction) != null;

            if (done)
                Set(_stepSpecial, Loc.T("<s>1. Build a special building</s>"), Done);
            else if (started)
                Set(_stepSpecial, Loc.T("1. Build a special building - under construction"), Active);
            else
                Set(_stepSpecial, Loc.T("1. Build a special building (Vault / Keep)"), Active);
        }

        // ── Step 2: culture + age up ───────────────────────────────────────

        private void RefreshCulture(EntityManager em, Faction faction)
        {
            if (_stepCulture == null) return;

            byte culture = FactionColors.GetFactionCulture(faction);
            if (culture != Cultures.None)
            {
                Set(_stepCulture, Loc.T("<s>2. Select a culture and age up</s>"), Done);
                return;
            }

            // Age-up timer running on the local Hall?
            var q = _hallQuery.Get(em, HallQueryTypes);
            using var halls = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < halls.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<AgeUpState>(halls[i]))
                {
                    var s = em.GetComponentData<AgeUpState>(halls[i]);
                    float pct = s.Duration > 0f
                        ? Mathf.Clamp01((s.Duration - s.Remaining) / s.Duration) : 0f;
                    Set(_stepCulture,
                        string.Format(Loc.T("2. Advancing to Era 2 - {0}%"), (int)(pct * 100f)),
                        Active);
                    return;
                }
                break;
            }

            bool gate = BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction) != null;
            Set(_stepCulture, Loc.T("2. Select a culture and age up"), gate ? Active : Dim);
        }

        // ── Step 3A: temple path ───────────────────────────────────────────

        private void RefreshTemple(EntityManager em, Faction faction)
        {
            if (_stepTemple == null) return;

            byte culture = FactionColors.GetFactionCulture(faction);
            if (culture == Cultures.None)
            {
                Set(_stepTemple,
                    Loc.T("3A. Build the Temple, ascend the ages,\n     then cleanse the curse nodes"),
                    Dim);
                return;
            }

            // Temple presence + level.
            Entity temple = Entity.Null;
            bool building = false;
            var q = _templeQuery.Get(em, TempleQueryTypes);
            using (var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                    temple = ents[i];
                    building = em.HasComponent<UnderConstruction>(ents[i]);
                    break;
                }
            }

            // The verb variable stays English; Loc.T translates it at each
            // composition site below.
            string verb = culture == Cultures.Runai ? "Pacify"
                : culture == Cultures.Feraldis ? "Destroy" : "Purify";
            var sb = new System.Text.StringBuilder(160);

            // Phase 1: build the Temple.
            if (temple == Entity.Null)
            {
                Set(_stepTemple,
                    string.Format(
                        Loc.T("3A. Build the Temple of Ridan,\n     then {0} the curse nodes"),
                        Loc.T(verb).ToLowerInvariant()),
                    Active);
                return;
            }
            if (building)
            {
                Set(_stepTemple, Loc.T("3A. Temple of Ridan - under construction"), Active);
                return;
            }
            sb.Append(Loc.T("<s>3A. Build the Temple of Ridan</s>")).Append('\n');

            // Phase 2: claim every curse node with the culture verb.
            CountWells(em, faction, culture, out int claimed, out int total);
            if (total > 0 && claimed >= total)
            {
                float hold = HoldRemaining(em, culture);
                if (hold > 0f)
                    sb.Append(string.Format(
                        Loc.T("     {0} the curse nodes ({1}/{2}) - hold {3:0.0}s"),
                        Loc.T(verb), claimed, total, hold));
                else
                    sb.Append(string.Format(
                        Loc.T("<s>     {0} the curse nodes ({1}/{2})</s>"),
                        Loc.T(verb), claimed, total));
            }
            else
            {
                sb.Append(string.Format(
                    Loc.T("     {0} the curse nodes ({1}/{2})"),
                    Loc.T(verb), claimed, total));
            }
            Set(_stepTemple, sb.ToString(), Active);
        }

        private void CountWells(EntityManager em, Faction faction, byte culture,
            out int claimed, out int total)
        {
            claimed = 0;
            total = 0;
            NodeState want = culture == Cultures.Runai ? NodeState.Converted
                : culture == Cultures.Feraldis ? NodeState.Destroyed : NodeState.Cleansed;

            var q = _wellQuery.Get(em, WellQueryTypes);
            using var states = q.ToComponentDataArray<BorderNodeState>(Unity.Collections.Allocator.Temp);
            total = states.Length;
            for (int i = 0; i < states.Length; i++)
                if (states[i].State == want && states[i].OwnerFaction == faction)
                    claimed++;
        }

        private float HoldRemaining(EntityManager em, byte culture)
        {
            // Feraldis destruction wins instantly — no hold timer.
            if (culture == Cultures.Feraldis) return 0f;
            var q = _victoryQuery.Get(em, VictoryQueryTypes);
            if (q.IsEmptyIgnoreFilter) return 0f;
            var v = q.GetSingleton<NodeVictoryState>();
            float timer = culture == Cultures.Runai ? v.RunaiHoldTimer : v.AlanthorHoldTimer;
            return Mathf.Max(0f, BorderConstants.NodeVictoryHoldTime - timer);
        }

        // ── Step 3B: destroy the other players ─────────────────────────────

        private void RefreshMilitary(EntityManager em, Faction faction)
        {
            if (_stepMilitary == null) return;

            byte culture = FactionColors.GetFactionCulture(faction);
            int opponents = Mathf.Max(0, GameSettings.TotalPlayers - 1);

            // A faction is alive while it owns at least one completed
            // building — same rule as VictoryConditionSystem.
            var alive = new System.Collections.Generic.HashSet<Faction>();
            var q = _buildingQuery.Get(em, BuildingQueryTypes);
            using (var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var fac = facs[i].Value;
                    if (fac == faction || fac >= Faction.Border) continue;
                    if (alive.Contains(fac)) continue;
                    if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                    alive.Add(fac);
                }
            }
            int aliveOpponents = Mathf.Min(alive.Count, opponents);
            int destroyed = opponents - aliveOpponents;

            if (opponents > 0 && aliveOpponents == 0)
                Set(_stepMilitary,
                    string.Format(Loc.T("<s>3B. Destroy all other players ({0}/{1})</s>"),
                        destroyed, opponents), Done);
            else
                Set(_stepMilitary,
                    string.Format(Loc.T("3B. Destroy all other players ({0}/{1})"),
                        destroyed, opponents),
                    culture == Cultures.None ? Dim : Active);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static void Set(TMP_Text label, string text, Color color)
        {
            if (label.text != text) label.text = text;
            if (label.color != color) label.color = color;
        }
    }
}
