// SelectionRegion — binds the bottom-center selection panel to the currently
// selected ECS entity. Replaces the visible surface of the IMGUI
// Assets/Scripts/UI/Panels/EntityInfoPanel.cs (action buttons stay IMGUI
// until Phase 3b).
//
// State machine:
//   no selection                → "sel-empty"  visible
//   single valid entity         → "sel-single" visible, populated from
//                                  EntityInfoExtractor.GetDisplayInfo
//   multi-select                → "sel-multi"  visible, just shows count
//
// Training widget (in sel-single): for training buildings (Hall, Barracks,
// Shrine, Temple, etc.) we additionally pull EntityActionInfo.TrainingState
// from EntityActionExtractor and render:
//   - Current unit being trained + time remaining
//   - Progress bar  (sel-train-fill)
//   - Five queue slots — slot 0 is the currently-training unit; slots 1-4 are
//     the next 4 queued (TrainingInfo.Queue[]). Empty slots get the dim border.

using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
using TheWaningBorder.UI.Common;     // UnifiedUIManager
using TheWaningBorder.UI.Panels;     // EntityInfoExtractor, EntityDisplayInfo,
                                     // EntityActionExtractor, EntityActionInfo
using TheWaningBorder.Input;         // SelectionSystem

namespace TheWaningBorder.UI.Regions
{
    public sealed class SelectionRegion
    {
        private const int QueueSlotCount = 5;

        private readonly VisualElement _empty;
        private readonly VisualElement _single;
        private readonly VisualElement _multi;

        private readonly Label _name;
        private readonly Label _type;
        private readonly Label _hpText;
        private readonly VisualElement _hpFill;
        private readonly VisualElement _hpRow;
        private readonly VisualElement _statsRow;
        private readonly Label _atk;
        private readonly Label _def;
        private readonly Label _spd;
        private readonly Label _desc;
        private readonly Label _multiCount;

        // Training widget
        private readonly VisualElement _trainRoot;
        private readonly Label _trainCurrent;
        private readonly Label _trainTime;
        private readonly VisualElement _trainFill;
        private readonly VisualElement[] _trainSlots = new VisualElement[QueueSlotCount];
        private readonly Label[] _trainSlotLabels = new Label[QueueSlotCount];

        public SelectionRegion(VisualElement root)
        {
            _empty  = root.Q<VisualElement>("sel-empty");
            _single = root.Q<VisualElement>("sel-single");
            _multi  = root.Q<VisualElement>("sel-multi");

            _name     = root.Q<Label>("sel-name");
            _type     = root.Q<Label>("sel-type");
            _hpText   = root.Q<Label>("sel-hp-text");
            _hpFill   = root.Q<VisualElement>("sel-hp-fill");
            _hpRow    = root.Q<VisualElement>("sel-hp-row");
            _statsRow = root.Q<VisualElement>("sel-stats-row");
            _atk      = root.Q<Label>("sel-atk");
            _def      = root.Q<Label>("sel-def");
            _spd      = root.Q<Label>("sel-spd");
            _desc     = root.Q<Label>("sel-desc");
            _multiCount = root.Q<Label>("sel-multi-count");

            _trainRoot    = root.Q<VisualElement>("sel-training");
            _trainCurrent = root.Q<Label>("sel-train-current");
            _trainTime    = root.Q<Label>("sel-train-time");
            _trainFill    = root.Q<VisualElement>("sel-train-fill");
            for (int i = 0; i < QueueSlotCount; i++)
            {
                _trainSlots[i] = root.Q<VisualElement>("sel-train-slot-" + i);
                _trainSlotLabels[i] = _trainSlots[i]?.Q<Label>(null, "tw-train-slot-label");
            }
        }

        public void Refresh()
        {
            var sel = SelectionSystem.CurrentSelection;
            int selCount = sel?.Count ?? 0;

            if (selCount == 0)
            {
                ShowOnly(_empty);
                return;
            }

            if (UnifiedUIManager.IsMultiSelection())
            {
                ShowOnly(_multi);
                if (_multiCount != null)
                    _multiCount.text = selCount + (selCount == 1 ? " unit" : " units");
                return;
            }

            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null)
            {
                ShowOnly(_empty);
                return;
            }

            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager)))
            {
                ShowOnly(_empty);
                return;
            }

            var info = EntityInfoExtractor.GetDisplayInfo(entity, em);
            ShowOnly(_single);
            ApplySingle(info);
            ApplyTraining(entity, em);
        }

        private void ShowOnly(VisualElement target)
        {
            SetDisplay(_empty,  target == _empty);
            SetDisplay(_single, target == _single);
            SetDisplay(_multi,  target == _multi);
        }

        private static void SetDisplay(VisualElement e, bool visible)
        {
            if (e == null) return;
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ApplySingle(EntityDisplayInfo info)
        {
            if (_name != null) _name.text = info.Name ?? "--";

            if (_type != null)
            {
                var t = info.Type ?? string.Empty;
                var f = info.Faction ?? string.Empty;
                _type.text = string.IsNullOrEmpty(f) ? t : (t + " · " + f);
            }

            bool hasHp = info.CurrentHealth.HasValue && info.MaxHealth.HasValue && info.MaxHealth.Value > 0;
            SetDisplay(_hpRow, hasHp);
            if (hasHp)
            {
                int cur = info.CurrentHealth.Value;
                int max = info.MaxHealth.Value;
                if (_hpText != null) _hpText.text = cur + "/" + max;
                if (_hpFill != null)
                {
                    float pct = (float)cur / max;
                    _hpFill.style.width = Length.Percent(Mathf.Clamp01(pct) * 100f);
                }
            }

            SetDisplay(_statsRow, info.HasCombatStats);
            if (info.HasCombatStats)
            {
                if (_atk != null) _atk.text = info.Attack.HasValue  ? info.Attack.Value.ToString()  : "--";
                if (_def != null) _def.text = info.Defense.HasValue ? info.Defense.Value.ToString() : "--";
                if (_spd != null) _spd.text = info.Speed.HasValue   ? info.Speed.Value.ToString("0.0") : "--";
            }

            if (_desc != null) _desc.text = info.Description ?? string.Empty;
        }

        // ─── Training widget ──────────────────────────────────────────────
        private void ApplyTraining(Entity entity, EntityManager em)
        {
            if (_trainRoot == null) return;

            // EntityActionInfo.TrainingState is nullable — populated only when
            // the selected entity actually has a TrainingState component.
            var action = EntityActionExtractor.GetActionInfo(entity, em);
            if (!action.TrainingState.HasValue || !action.TrainingState.Value.IsTraining)
            {
                SetDisplay(_trainRoot, false);
                return;
            }
            var ts = action.TrainingState.Value;
            SetDisplay(_trainRoot, true);

            // Top line — "Builder    5.3s"
            string current = string.IsNullOrEmpty(ts.CurrentUnitId) ? "--" : ts.CurrentUnitId;
            if (_trainCurrent != null) _trainCurrent.text = current;
            if (_trainTime != null)
                _trainTime.text = ts.TimeRemaining > 0f ? ts.TimeRemaining.ToString("0.0") + "s" : "--";

            // Progress bar — TrainingInfo.Progress is ALREADY a 0..1 ratio
            // (matches DrawProgressBar in EntityActionPanel.cs:1135). Earlier
            // code mistakenly divided by Total, producing a near-zero fill.
            if (_trainFill != null)
            {
                float pct = Mathf.Clamp01(ts.Progress);
                _trainFill.style.width = Length.Percent(pct * 100f);
            }

            // Queue slots — slot 0 = currently training; slots 1-4 = ts.Queue[0..3].
            for (int i = 0; i < QueueSlotCount; i++)
            {
                string id = null;
                if (i == 0)
                {
                    id = ts.CurrentUnitId;
                }
                else if (ts.Queue != null && (i - 1) < ts.Queue.Length)
                {
                    id = ts.Queue[i - 1];
                }

                bool filled = !string.IsNullOrEmpty(id);
                var slot = _trainSlots[i];
                if (slot != null)
                {
                    if (filled) slot.AddToClassList("tw-train-slot-filled");
                    else        slot.RemoveFromClassList("tw-train-slot-filled");
                }
                if (_trainSlotLabels[i] != null)
                {
                    _trainSlotLabels[i].text = filled ? ShortenUnitId(id) : string.Empty;
                }
            }
        }

        // Unit IDs are typically PascalCase ("Builder", "Spearman"). Slots are
        // 28px wide so 3-letter UPPER reads cleanly.
        private static string ShortenUnitId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            return id.Length <= 3 ? id.ToUpperInvariant() : id.Substring(0, 3).ToUpperInvariant();
        }
    }
}
