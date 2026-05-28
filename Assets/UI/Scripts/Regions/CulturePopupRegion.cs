// CulturePopupRegion — UI Toolkit replacement for the IMGUI
// Assets/Scripts/UI/Panels/CultureChoicePopup.cs modal. Watches the static
// `CultureChoicePopup.IsVisible` flag (the IMGUI MonoBehaviour is suspended;
// its statics keep the game-state API stable so AI, save/load, etc. don't
// need touching) and renders the jade modal whenever something calls
// CultureChoicePopup.Show(hall, faction).
//
// The three "choose" buttons commit via CultureChoicePopup.CommitAgeUpStatic
// — the same spend / AgeUpState / FactionColors path the IMGUI version used.

using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.Regions
{
    public sealed class CulturePopupRegion
    {
        private readonly VisualElement _root;
        private readonly Label _costLabel;

        // Per-culture refs — name, desc, two swatches, choose button.
        private readonly Label _alanthorName, _alanthorDesc;
        private readonly VisualElement _alanthorPrimary, _alanthorSecondary, _alanthorChoose;
        private readonly Label _feraldisName, _feraldisDesc;
        private readonly VisualElement _feraldisPrimary, _feraldisSecondary, _feraldisChoose;
        private readonly Label _runaiName, _runaiDesc;
        private readonly VisualElement _runaiPrimary, _runaiSecondary, _runaiChoose;
        private readonly VisualElement _cancelBtn;

        private bool _populated;
        private bool _wasVisible;

        public CulturePopupRegion(VisualElement root)
        {
            _root = root.Q<VisualElement>("culture-popup");
            _costLabel = root.Q<Label>("culture-cost");

            _alanthorName      = root.Q<Label>("culture-alanthor-name");
            _alanthorDesc      = root.Q<Label>("culture-alanthor-desc");
            _alanthorPrimary   = root.Q<VisualElement>("culture-alanthor-primary");
            _alanthorSecondary = root.Q<VisualElement>("culture-alanthor-secondary");
            _alanthorChoose    = root.Q<VisualElement>("culture-alanthor-choose");

            _feraldisName      = root.Q<Label>("culture-feraldis-name");
            _feraldisDesc      = root.Q<Label>("culture-feraldis-desc");
            _feraldisPrimary   = root.Q<VisualElement>("culture-feraldis-primary");
            _feraldisSecondary = root.Q<VisualElement>("culture-feraldis-secondary");
            _feraldisChoose    = root.Q<VisualElement>("culture-feraldis-choose");

            _runaiName      = root.Q<Label>("culture-runai-name");
            _runaiDesc      = root.Q<Label>("culture-runai-desc");
            _runaiPrimary   = root.Q<VisualElement>("culture-runai-primary");
            _runaiSecondary = root.Q<VisualElement>("culture-runai-secondary");
            _runaiChoose    = root.Q<VisualElement>("culture-runai-choose");

            _cancelBtn = root.Q<VisualElement>("culture-cancel");

            WireClicks();
            PopulateOnce();
        }

        private void WireClicks()
        {
            _alanthorChoose?.RegisterCallback<ClickEvent>(_ => Commit(Cultures.Alanthor));
            _feraldisChoose?.RegisterCallback<ClickEvent>(_ => Commit(Cultures.Feraldis));
            _runaiChoose?.RegisterCallback<ClickEvent>(_ => Commit(Cultures.Runai));
            _cancelBtn?.RegisterCallback<ClickEvent>(_ => CultureChoicePopup.Close());
        }

        // Names / descriptions / swatch colors don't change at runtime — bind once.
        private void PopulateOnce()
        {
            if (_populated) return;
            BindCulture(Cultures.Alanthor, _alanthorName, _alanthorDesc, _alanthorPrimary, _alanthorSecondary);
            BindCulture(Cultures.Feraldis, _feraldisName, _feraldisDesc, _feraldisPrimary, _feraldisSecondary);
            BindCulture(Cultures.Runai,    _runaiName,    _runaiDesc,    _runaiPrimary,    _runaiSecondary);
            _populated = true;
        }

        private static void BindCulture(byte culture, Label name, Label desc,
                                        VisualElement primary, VisualElement secondary)
        {
            if (name != null) name.text = CultureConfig.GetName(culture).ToUpperInvariant();
            if (desc != null) desc.text = CultureConfig.GetDescription(culture);
            if (primary   != null) primary.style.backgroundColor   = new StyleColor(CultureConfig.GetPrimary(culture));
            if (secondary != null) secondary.style.backgroundColor = new StyleColor(CultureConfig.GetSecondary(culture));
        }

        public void Refresh()
        {
            if (_root == null) return;

            bool visible = CultureChoicePopup.IsVisible;
            if (visible != _wasVisible)
            {
                _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                _wasVisible = visible;
            }
            if (!visible) return;

            // Cost text + affordability tint refreshes while visible.
            var em = UnifiedUIManager.GetEntityManager();
            bool canAfford = !em.Equals(default(EntityManager)) &&
                             TheWaningBorder.Economy.FactionEconomy.CanAfford(
                                 em, CultureChoicePopup.CurrentFaction, CultureConfig.AgeUpCost);

            if (_costLabel != null)
            {
                _costLabel.text = "Cost: " + UIHelpers.FormatCost(CultureConfig.AgeUpCost);
                _costLabel.style.color = canAfford
                    ? new StyleColor(new Color(0.3f, 0.9f, 0.3f))
                    : new StyleColor(new Color(1f, 0.3f, 0.3f));
            }

            SetChooseEnabled(_alanthorChoose, canAfford);
            SetChooseEnabled(_feraldisChoose, canAfford);
            SetChooseEnabled(_runaiChoose,    canAfford);
        }

        private static void SetChooseEnabled(VisualElement btn, bool enabled)
        {
            if (btn == null) return;
            btn.SetEnabled(enabled);
            btn.style.opacity = enabled ? 1f : 0.5f;
        }

        private static void Commit(byte culture)
        {
            CultureChoicePopup.CommitAgeUpStatic(culture);
        }
    }
}
