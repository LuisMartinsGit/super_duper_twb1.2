// ObjectivesPanelBinder.cs
// Binds the authored ObjectivesPanel prefab (GameUICatalog.objectivesPanel,
// top-left) to live match state. Four fixed step rows, found by node name:
//   Step_Special  — build a choice building (Shrine / Vault / Keep)
//   Step_Culture  — select a culture and age up (Era 2)
//   Step_Temple   — 3A: build the Temple, upgrade it through the ages,
//                   then claim every curse node with the culture's verb
//                   (Alanthor purify / Runai pacify / Feraldis destroy)
//   Step_Military — 3B: destroy all other players
// Step states: pending = dim, active = white, done = dim + strikethrough.

using TMPro;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class ObjectivesPanelBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        private TMP_Text _stepSpecial, _stepCulture, _stepTemple, _stepMilitary;
        private float _timer;
        private bool _visible = true;

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
            bool ok = world != null && world.IsCreated && !GameSettings.IsObserver;
            // Toggle the rendered children, never this GameObject — a
            // self-deactivate would stop Update and the panel could never
            // come back once the world finishes loading.
            if (_visible != ok)
            {
                _visible = ok;
                foreach (Transform child in transform)
                    if (child.gameObject.activeSelf != ok) child.gameObject.SetActive(ok);
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
                Set(_stepSpecial, Loc.T("1. Build a special building (Shrine / Vault / Keep)"), Active);
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

            // Phase 2: upgrade the Temple through the ages (L1..L4 = Era 2..5).
            int level = em.HasComponent<TempleLevel>(temple)
                ? em.GetComponentData<TempleLevel>(temple).Level : 1;
            if (level < TempleLevelConfig.MaxLevel)
            {
                if (em.HasComponent<TempleUpgradeState>(temple))
                {
                    var up = em.GetComponentData<TempleUpgradeState>(temple);
                    float pct = up.Duration > 0f
                        ? Mathf.Clamp01((up.Duration - up.Remaining) / up.Duration) : 0f;
                    sb.Append(string.Format(
                        Loc.T("     Upgrade the Temple (Lv {0}, upgrading {1}%)"),
                        level, (int)(pct * 100f)));
                }
                else
                {
                    sb.Append(string.Format(
                        Loc.T("     Upgrade the Temple to age up (Lv {0} of {1})"),
                        level, TempleLevelConfig.MaxLevel));
                }
                Set(_stepTemple, sb.ToString(), Active);
                return;
            }
            sb.Append(Loc.T("<s>     Upgrade the Temple to age up</s>")).Append('\n');

            // Phase 3: claim every curse node with the culture verb.
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
