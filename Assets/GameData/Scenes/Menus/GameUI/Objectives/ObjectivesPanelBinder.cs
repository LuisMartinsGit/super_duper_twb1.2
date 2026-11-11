// ObjectivesPanelBinder.cs
// Binds the authored ObjectivesPanel prefab (GameUICatalog.objectivesPanel,
// top-left) to live match state. Four fixed step rows, found by node name:
//   Step_Special  — build a choice building (Vault / Keep)
//   Step_Culture  — select a culture and age up (Era 2)
//   Step_Temple   — 3: the ways to win (decision 54): conquest (rivals
//                   left) and Sanctum dominion (held / on the map, and the
//                   countdown while it runs)
//   Step_Military — 3, cont.: the Shardroot (ruins explored, who carries
//                   it, the ascension countdown) — never its hidden spot
// The old "cleanse the curse nodes" step is gone with that victory.
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
using TheWaningBorder.Core;
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
        private static readonly ComponentType[] BuildingQueryTypes =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] MonumentQueryTypes =
        {
            ComponentType.ReadOnly<MonumentTag>(),
            ComponentType.ReadOnly<MonumentState>(),
        };
        private static readonly ComponentType[] ShardrootQueryTypes =
        {
            ComponentType.ReadOnly<ShardrootState>(),
        };
        private static readonly ComponentType[] HiddenQueryTypes =
        {
            ComponentType.ReadOnly<ShardrootHidden>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _hallQuery;
        private TheWaningBorder.Core.CachedEntityQuery _buildingQuery;
        private TheWaningBorder.Core.CachedEntityQuery _monumentQuery;
        private TheWaningBorder.Core.CachedEntityQuery _shardrootQuery;
        private TheWaningBorder.Core.CachedEntityQuery _hiddenQuery;

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

        // ── Step 3: the ways to win (decision 54) ──────────────────────────
        //
        // Every live victory with its progress (Territory_Claims.md § 7):
        // conquest (last side standing, Teams.md), Sanctum dominion (§ 11.4)
        // and the Shardroot ascension (Monuments_And_Petriarchy.md § 3,
        // Heroes.md § 4). The retired curse-node victory is gone. Dim until
        // the faction has aged up, as the two opening steps lead there.
        //   Step_Temple   — the header, conquest and Sanctum dominion
        //   Step_Military — the Shardroot (two lines)

        private void RefreshTemple(EntityManager em, Faction faction)
        {
            if (_stepTemple == null) return;
            bool aged = FactionColors.GetFactionCulture(faction) != Cultures.None;

            var sb = new System.Text.StringBuilder(200);
            sb.Append(Loc.T("3. Win the match, any one way:"));
            sb.Append('\n').Append(ConquestLine(em, faction));
            string sanctum = SanctumLine(em, faction);
            if (sanctum != null) sb.Append('\n').Append(sanctum);
            Set(_stepTemple, sb.ToString(), aged ? Active : Dim);
        }

        /// <summary>Conquest: hostile sides still standing, of all hostile
        /// sides (last side standing, Teams.md). A side stands while it owns a
        /// finished building — the rule VictoryConditionSystem used, close
        /// enough for a progress line.</summary>
        private string ConquestLine(EntityManager em, Faction faction)
        {
            int players = Mathf.Clamp(GameSettings.TotalPlayers, 0, (int)Faction.Border);
            int rivals = 0;
            for (int f = 0; f < players; f++)
                if ((Faction)f != faction && Alliances.AreHostile(faction, (Faction)f)) rivals++;

            var alive = new System.Collections.Generic.HashSet<Faction>();
            var q = _buildingQuery.Get(em, BuildingQueryTypes);
            using (var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var fac = facs[i].Value;
                    if (fac == faction || fac >= Faction.Border || (int)fac >= players) continue;
                    if (alive.Contains(fac) || !Alliances.AreHostile(faction, fac)) continue;
                    if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                    alive.Add(fac);
                }
            }
            int left = Mathf.Min(alive.Count, rivals);
            return rivals > 0 && left == 0
                ? string.Format(Loc.T("<s>   Conquest - rivals left ({0}/{1})</s>"), left, rivals)
                : string.Format(Loc.T("   Conquest - rivals left ({0}/{1})"), left, rivals);
        }

        /// <summary>Sanctum dominion: Sanctums your side holds (allies count
        /// together, as the dominion does) of all on the map, and the running
        /// countdown — public to every player (§ 11.4). Null on a map with no
        /// Sanctum.</summary>
        private static string SanctumLine(EntityManager em, Faction faction)
        {
            if (!TheWaningBorder.World.Regions.RegionMap.Ready
                || !TheWaningBorder.World.Regions.TerritoryOwnership.Ready) return null;
            int total = 0, held = 0;
            for (int t = 0; t < TheWaningBorder.World.Regions.RegionMap.Count; t++)
            {
                if (!TheWaningBorder.World.Regions.TerritoryResources.IsSanctum(t)) continue;
                total++;
                int owner = TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t);
                if (owner >= 0 && owner < (int)Faction.Border && Alliances.AreAllied(faction, (Faction)owner))
                    held++;
            }
            if (total == 0) return null;

            if (TheWaningBorder.Systems.Economy.SanctumSystem.TryCountdown(em, out var holder, out float left))
            {
                string time = CountdownWarnings.Clock(left);
                return CountdownWarnings.SideOf(holder) == CountdownSide.Own
                    ? string.Format(Loc.T("   Sanctum dominion - held {0}/{1}, you win in {2}"), held, total, time)
                    : string.Format(Loc.T("   Sanctum dominion - held {0}/{1}, {2} wins in {3}"),
                        held, total, FactionColors.RichName(holder), time);
            }
            return string.Format(Loc.T("   Sanctum dominion - held {0}/{1}"), held, total);
        }

        /// <summary>The Shardroot: ruins you explored of all monuments, then
        /// where it stands — only what this player may know. Its spot is
        /// private until someone picks it up (Monuments_And_Petriarchy.md
        /// § 3): before that the line says only whether YOU know it.</summary>
        private void RefreshMilitary(EntityManager em, Faction faction)
        {
            if (_stepMilitary == null) return;
            bool aged = FactionColors.GetFactionCulture(faction) != Cultures.None;

            int monuments, explored = 0;
            var mq = _monumentQuery.Get(em, MonumentQueryTypes);
            using (var states = mq.ToComponentDataArray<MonumentState>(Unity.Collections.Allocator.Temp))
            {
                monuments = states.Length;
                for (int i = 0; i < states.Length; i++)
                    if (Monument.HasExplored(states[i], faction)) explored++;
            }

            var sb = new System.Text.StringBuilder(160);
            sb.Append(monuments > 0
                ? string.Format(Loc.T("   Shardroot - ruins explored ({0}/{1})"), explored, monuments)
                : Loc.T("   Shardroot"));
            sb.Append('\n').Append(ShardrootStatus(em, faction));
            Set(_stepMilitary, sb.ToString(), aged ? Active : Dim);
        }

        private string ShardrootStatus(EntityManager em, Faction faction)
        {
            var sq = _shardrootQuery.Get(em, ShardrootQueryTypes);
            ShardrootState st = default;
            bool hasState = !sq.IsEmptyIgnoreFilter;
            if (hasState)
            {
                using var arr = sq.ToComponentDataArray<ShardrootState>(Unity.Collections.Allocator.Temp);
                if (arr.Length > 0) st = arr[0];
            }

            // Enshrined: the ascension countdown is public (§ 7).
            if (TheWaningBorder.Systems.Border.ShardrootSystem.TryAscensionCountdown(em, out var temple, out float left))
            {
                string time = CountdownWarnings.Clock(left);
                return CountdownWarnings.SideOf(temple) == CountdownSide.Own
                    ? string.Format(Loc.T("     enshrined in your Temple - you ascend in {0}"), time)
                    : string.Format(Loc.T("     enshrined by {0} - ascends in {1}"), FactionColors.RichName(temple), time);
            }
            // Carried: public once picked up (the minimap beacon shows it).
            if (hasState && st.HolderFaction != Faction.Border)
                return st.HolderFaction == faction
                    ? Loc.T("     carried by your hero - enshrine it in the Temple")
                    : string.Format(Loc.T("     carried by {0}"), FactionColors.RichName(st.HolderFaction));

            // Still hidden (or not laid yet): say only whether this player has
            // learned the spot by exploring the right ruins.
            var hq = _hiddenQuery.Get(em, HiddenQueryTypes);
            if (!hq.IsEmptyIgnoreFilter || !hasState || st.Found == 0)
            {
                bool known = false;
                using (var hidden = hq.ToComponentDataArray<ShardrootHidden>(Unity.Collections.Allocator.Temp))
                    for (int i = 0; i < hidden.Length; i++)
                        if ((hidden[i].RevealedTo & (1u << (int)faction)) != 0) known = true;
                return known
                    ? Loc.T("     found - send a hero to take it")
                    : Loc.T("     hidden - explore ruins to find it");
            }
            // Picked up once and set down: public, nobody holds it.
            return Loc.T("     loose - no one holds it");
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static void Set(TMP_Text label, string text, Color color)
        {
            if (label.text != text) label.text = text;
            if (label.color != color) label.color = color;
        }
    }
}
