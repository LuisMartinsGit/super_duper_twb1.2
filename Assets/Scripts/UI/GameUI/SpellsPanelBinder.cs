// SpellsPanelBinder.cs
// Code-built spells bar (no authored prefab yet). Design rule (2026-08-02):
// non-hero units carry AT MOST one Active and one Passive ability — heroes
// may exceed this later. So the bar is exactly: one big cast button named
// after the Active skill (with live READY/cooldown state), and a dim line
// for the Passive. Casting goes through CommandRouter.IssueUnitAbility —
// which is precise under the one-active rule (it fires the first ready
// Active). Not-controllable automatons (Ledger) show their active as "auto".

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.UI.GameUI
{
    public class SpellsPanelBinder : MonoBehaviour
    {
        private const float PollInterval = 0.2f;
        private const int MaxSlots = 4; // storage allows 4; design uses 1+1 for non-heroes

        private RectTransform _root;
        private Button _castButton;
        private Image _castBg;
        private TMP_Text _castLabel;
        private TMP_Text _passiveLabel;
        private float _nextPoll;
        private EntityManager _em;
        private bool _emReady;
        private Entity _primary = Entity.Null;

        void Start()
        {
            _root = GameUIKit.Rect(transform, "GameUI_SpellsPanel");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, 64f);
            _root.sizeDelta = new Vector2(332f, 68f);

            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = GameUIKit.PanelBg;
            GameUIKit.PanelChrome(_root);

            var title = GameUIKit.Text(_root, "Title", "SPELLS", 11f, GameUIKit.TextDim,
                TextAlignmentOptions.Center);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -2f);
            titleRect.sizeDelta = new Vector2(0f, 12f);

            var castRect = GameUIKit.Rect(_root, "CastButton");
            castRect.anchorMin = new Vector2(0f, 1f);
            castRect.anchorMax = new Vector2(1f, 1f);
            castRect.pivot = new Vector2(0.5f, 1f);
            castRect.anchoredPosition = new Vector2(0f, -16f);
            castRect.sizeDelta = new Vector2(-12f, 30f);
            _castBg = castRect.gameObject.AddComponent<Image>();
            _castBg.color = GameUIKit.ButtonBg;
            _castButton = castRect.gameObject.AddComponent<Button>();
            _castButton.targetGraphic = _castBg;
            _castButton.onClick.AddListener(Cast);
            _castLabel = GameUIKit.Text(castRect, "Label", "", 13f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch((RectTransform)_castLabel.transform);

            _passiveLabel = GameUIKit.Text(_root, "Passive", "", 11f, GameUIKit.TextDim,
                TextAlignmentOptions.Center, wrap: false);
            var passiveRect = (RectTransform)_passiveLabel.transform;
            passiveRect.anchorMin = new Vector2(0f, 0f);
            passiveRect.anchorMax = new Vector2(1f, 0f);
            passiveRect.pivot = new Vector2(0.5f, 0f);
            passiveRect.anchoredPosition = new Vector2(0f, 4f);
            passiveRect.sizeDelta = new Vector2(-12f, 13f);

            _root.gameObject.SetActive(false);
        }

        private void Cast()
        {
            if (!_emReady || _primary == Entity.Null || !_em.Exists(_primary)) return;
            TheWaningBorder.Core.Commands.CommandRouter.IssueUnitAbility(_em, _primary);
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll || _root == null) return;
            _nextPoll = Time.unscaledTime + PollInterval;

            if (!_emReady)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                _em = world.EntityManager;
                _emReady = true;
            }

            // Primary = first selected entity carrying catalog abilities.
            _primary = Entity.Null;
            var sel = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (sel != null)
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    if (_em.Exists(sel[i]) && _em.HasComponent<UnitAbilities>(sel[i]))
                    { _primary = sel[i]; break; }
                }
            }

            bool show = _primary != Entity.Null;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            // One Active + one Passive by design — find each.
            var ua = _em.GetComponentData<UnitAbilities>(_primary);
            AbilityCard active = null, passive = null;
            int activeSlot = -1;
            for (int s = 0; s < MaxSlots; s++)
            {
                var card = AbilityCatalog.Get(ua.Get(s));
                if (card == null) continue;
                if (card.Activation == AbilityActivation.Active && active == null)
                { active = card; activeSlot = s; }
                else if (card.Activation == AbilityActivation.Passive && passive == null)
                    passive = card;
            }

            _passiveLabel.text = passive != null ? $"Passive: {passive.Name}" : "";

            if (active == null)
            {
                _castButton.gameObject.SetActive(false);
                return;
            }
            _castButton.gameObject.SetActive(true);

            bool autonomous = _em.HasComponent<NotControllableTag>(_primary);
            float cd = AbilityQuery.CooldownRemaining(_em, _primary, activeSlot);
            bool ready = !autonomous && cd <= 0f;

            _castLabel.text = autonomous
                ? $"{active.Name}  <color=#a8a294>auto</color>"
                : cd <= 0f
                    ? active.Name
                    : $"{active.Name}  <color=#b88452>{Mathf.CeilToInt(cd)}s</color>";

            _castButton.interactable = ready;
            _castBg.color = ready ? GameUIKit.ButtonBg : GameUIKit.ButtonBgLocked;
            _castLabel.color = ready ? GameUIKit.Gold : GameUIKit.TextLocked;
        }
    }
}
