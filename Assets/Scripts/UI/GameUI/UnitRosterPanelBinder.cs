// UnitRosterPanelBinder.cs
// Live binding for the authored UnitRoster panel (Assets/GameData/Scenes/
// Menus/GameUI/SelectionUI/UnitRoster.prefab). GameUIManager spawns the
// panel and adds this component to its root.
//
// Shown whenever the selection contains at least one unit. The prefab
// carries a FIXED grid of portrait slots — every child of the authored
// "SelectionList" node is one slot, consumed in sibling (display) order.
// Slot i shows the i-th selected unit: its "SPR_Screenshot" Image gets the
// unit type's symbol sprite (catalog entitySymbols). Selections larger
// than the authored slot count show only the first N units.
//
// Clicking a slot focuses that unit's TYPE via UnitRosterFocus — the stats
// panel then describes that type. While nothing has been clicked, focus is
// automatic: a hero (UniqueUnitTag, e.g. King Lexor) wins, otherwise the
// most-numerous selected type. Slots of the focused type show a tint
// overlay; hovered slots show a lighter one. The "Selected"/"Highlighted"
// overlay nodes are created at runtime when the slot doesn't author them,
// and any authored Graphic inside them suppresses the fallback tint.
// Location: Assets/Scripts/UI/GameUI/UnitRosterPanelBinder.cs

using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheWaningBorder.UI.GameUI
{
    /// <summary>
    /// Which unit of the current selection the stats UI describes.
    /// FocusedName is the roster type the player clicked (null = automatic:
    /// hero first, then the most-numerous type). Reset whenever the
    /// selection itself changes.
    /// </summary>
    public static class UnitRosterFocus
    {
        /// <summary>Display name of the roster entry the player clicked;
        /// null = automatic (hero, else majority type).</summary>
        public static string FocusedName;

        public struct Group
        {
            public string Name;
            public Entity First;   // representative (first selected of the type)
            public Entity Hero;    // first UniqueUnitTag member, or Null
            public int Count;
        }

        /// <summary>
        /// Distinct unit types of the current selection in first-appearance
        /// order (units only: UnitTag + Health). Returns the shared scratch
        /// list — consume it before the next call, do not store it.
        /// </summary>
        public static List<Group> BuildGroups(EntityManager em)
        {
            _groups.Clear();
            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (selection == null) return _groups;

            for (int i = 0; i < selection.Count; i++)
            {
                var e = selection[i];
                if (!em.Exists(e) || !em.HasComponent<UnitTag>(e) || !em.HasComponent<Health>(e))
                    continue;

                string name = EntityInfoExtractor.GetSelectionDisplayName(e, em);
                bool isHero = em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(e);

                int idx = -1;
                for (int g = 0; g < _groups.Count; g++)
                    if (_groups[g].Name == name) { idx = g; break; }

                if (idx < 0)
                {
                    _groups.Add(new Group
                    {
                        Name = name,
                        First = e,
                        Hero = isHero ? e : Entity.Null,
                        Count = 1
                    });
                }
                else
                {
                    var grp = _groups[idx];
                    grp.Count++;
                    if (grp.Hero == Entity.Null && isHero) grp.Hero = e;
                    _groups[idx] = grp;
                }
            }
            return _groups;
        }

        /// <summary>Name of the group the stats UI should describe: the
        /// clicked roster entry if still selected, else a hero's type, else
        /// the most-numerous type.</summary>
        public static string ResolveFocusName(List<Group> groups)
        {
            if (groups.Count == 0) return null;

            if (FocusedName != null)
            {
                for (int i = 0; i < groups.Count; i++)
                    if (groups[i].Name == FocusedName) return FocusedName;
                FocusedName = null; // focused type left the selection
            }

            // Hero overrides the majority rule (e.g. King Lexor).
            for (int i = 0; i < groups.Count; i++)
                if (groups[i].Hero != Entity.Null) return groups[i].Name;

            int best = 0;
            for (int i = 1; i < groups.Count; i++)
                if (groups[i].Count > groups[best].Count) best = i;
            return groups[best].Name;
        }

        /// <summary>The unit whose stats the UI shows for the current
        /// selection (Entity.Null when no unit is selected).</summary>
        public static Entity ResolveStatsUnit(EntityManager em)
        {
            var groups = BuildGroups(em);
            string name = ResolveFocusName(groups);
            if (name == null) return Entity.Null;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Name != name) continue;
                return groups[i].Hero != Entity.Null ? groups[i].Hero : groups[i].First;
            }
            return Entity.Null;
        }

        private static readonly List<Group> _groups = new List<Group>();
    }

    /// <summary>
    /// Cheap per-frame "did the selection change?" poll shared by every
    /// selection-driven panel. Each panel refreshes on its own slow cadence
    /// for live data (HP, cooldowns), but ALL of them must react on the very
    /// frame the selection changes — otherwise the panels pop in staggered
    /// by up to their individual refresh intervals.
    /// </summary>
    internal struct SelectionChangeDetector
    {
        private int _signature;

        /// <summary>True exactly once per selection change. Call every frame.</summary>
        public bool Poll()
        {
            int sig = 17;
            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    sig = sig * 31 + selection[i].Index;
                    sig = sig * 31 + selection[i].Version;
                }
            }
            if (sig == _signature) return false;
            _signature = sig;
            return true;
        }
    }

    public sealed class UnitRosterPanelBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.15f;

        private static readonly Color SelectedTint    = new Color(0.98f, 0.85f, 0.35f, 0.35f);
        private static readonly Color HighlightedTint = new Color(1f, 1f, 1f, 0.20f);

        private CanvasGroup _group;
        private readonly List<UnitRosterEntry> _slots = new List<UnitRosterEntry>();
        private Dictionary<string, Sprite> _symbols;

        private float _timer;
        private bool _visible = true;
        private SelectionChangeDetector _selectionDetector;

        /// <summary>Symbol sprites keyed by display name (shared with
        /// GameUIManager's catalog lookup). Call right after AddComponent.</summary>
        public void Init(Dictionary<string, Sprite> symbols) => _symbols = symbols;

        private void Awake()
        {
            _group = gameObject.AddComponent<CanvasGroup>();

            // The authored slot grid: every child of "SelectionList" is one
            // portrait slot, in sibling (display) order.
            Transform list = null;
            foreach (var t in GetComponentsInChildren<RectTransform>(true))
            {
                if (string.Equals(t.name, "SelectionList", System.StringComparison.OrdinalIgnoreCase))
                {
                    list = t;
                    break;
                }
            }
            if (list == null)
            {
                TWBLog.Log("[GameUI] UnitRoster: no 'SelectionList' slot container found — renamed?");
                enabled = false;
                return;
            }
            for (int i = 0; i < list.childCount; i++)
            {
                var entry = list.GetChild(i).gameObject.AddComponent<UnitRosterEntry>();
                entry.Setup(this, SelectedTint, HighlightedTint);
                _slots.Add(entry);
            }

            SetVisible(false);
        }

        private void Update()
        {
            // A new selection drops any explicit focus back to automatic and
            // refreshes IMMEDIATELY — all selection panels must appear on the
            // same frame, not staggered by their individual poll cadences.
            bool selectionChanged = _selectionDetector.Poll();
            if (selectionChanged) UnitRosterFocus.FocusedName = null;

            _timer += Time.unscaledDeltaTime;
            if (_timer < RefreshInterval && !selectionChanged) return;
            _timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { SetVisible(false); return; }
            var em = world.EntityManager;

            // The focused type (clicked slot, else hero, else majority) —
            // its slots get the Selected tint.
            var groups = UnitRosterFocus.BuildGroups(em);
            string focusName = UnitRosterFocus.ResolveFocusName(groups);

            // One slot per selected unit, in selection order; the authored
            // grid caps how many show.
            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            int shown = 0;
            if (selection != null)
            {
                for (int i = 0; i < selection.Count && shown < _slots.Count; i++)
                {
                    var e = selection[i];
                    if (!em.Exists(e) || !em.HasComponent<UnitTag>(e)
                        || !em.HasComponent<Health>(e))
                        continue;

                    string name = EntityInfoExtractor.GetSelectionDisplayName(e, em);
                    Sprite sprite = null;
                    if (_symbols != null && !_symbols.TryGetValue(name, out sprite))
                        _symbols.TryGetValue("Unit", out sprite);

                    var slot = _slots[shown];
                    if (!slot.gameObject.activeSelf) slot.gameObject.SetActive(true);
                    slot.Bind(name, sprite, name == focusName);
                    shown++;
                }
            }
            for (int i = shown; i < _slots.Count; i++)
                if (_slots[i].gameObject.activeSelf) _slots[i].gameObject.SetActive(false);

            SetVisible(shown > 0);
        }

        /// <summary>Entry click: pin the stats UI to this type.</summary>
        internal void OnEntryClicked(string typeName)
        {
            UnitRosterFocus.FocusedName = typeName;
            _timer = RefreshInterval; // apply overlays on the next frame
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            _group.alpha = visible ? 1f : 0f;
            _group.interactable = visible;
            _group.blocksRaycasts = visible;
        }
    }

    /// <summary>
    /// One roster slot (an authored "Portrait" child of SelectionList).
    /// Toggles the "Selected" overlay for the focused type and the
    /// "Highlighted" overlay while hovered; clicking focuses the slot's
    /// type. Overlay nodes are created at runtime when not authored.
    /// </summary>
    public sealed class UnitRosterEntry : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private UnitRosterPanelBinder _owner;
        private Image _icon;
        private GameObject _selected, _highlighted;
        private string _typeName;

        public void Setup(UnitRosterPanelBinder owner, Color selectedTint, Color highlightedTint)
        {
            _owner = owner;

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(t.name, "SPR_Screenshot", System.StringComparison.OrdinalIgnoreCase))
                {
                    _icon = t.GetComponent<Image>();
                    if (_icon != null) _icon.preserveAspect = true;
                }
                else if (string.Equals(t.name, "Selected", System.StringComparison.OrdinalIgnoreCase))
                    _selected = t.gameObject;
                else if (string.Equals(t.name, "Highlighted", System.StringComparison.OrdinalIgnoreCase))
                    _highlighted = t.gameObject;
            }

            if (_selected == null) _selected = CreateOverlayNode("Selected");
            if (_highlighted == null) _highlighted = CreateOverlayNode("Highlighted");

            EnsureOverlayVisual(_selected, selectedTint);
            EnsureOverlayVisual(_highlighted, highlightedTint);
            _selected.SetActive(false);
            _highlighted.SetActive(false);
        }

        private GameObject CreateOverlayNode(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            return go;
        }

        public void Bind(string typeName, Sprite sprite, bool focused)
        {
            _typeName = typeName;
            if (_icon != null && sprite != null && _icon.sprite != sprite)
                _icon.sprite = sprite;
            if (_selected != null && _selected.activeSelf != focused)
                _selected.SetActive(focused);
        }

        /// <summary>The authored Selected/Highlighted nodes are empty
        /// placeholders. If (and only if) no Graphic has been authored
        /// inside one, stretch a translucent tint Image over the entry so
        /// the state is visible today; authored art suppresses this.</summary>
        private static void EnsureOverlayVisual(GameObject overlay, Color tint)
        {
            if (overlay == null) return;
            if (overlay.GetComponentInChildren<Graphic>(true) != null) return;

            var rect = (RectTransform)overlay.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var img = overlay.AddComponent<Image>();
            img.color = tint;
            img.raycastTarget = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_highlighted != null) _highlighted.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_highlighted != null) _highlighted.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_owner != null && _typeName != null) _owner.OnEntryClicked(_typeName);
        }
    }
}
