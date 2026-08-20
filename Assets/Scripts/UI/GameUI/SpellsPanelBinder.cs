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
using TheWaningBorder.Core.Localization;

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

        // Rebuilt each poll and read by the hover callbacks.
        private string _activeTooltip;
        private string _passiveTooltip;

        void Start()
        {
            // Canvas units on a 3840x2160 reference — halve them for screen
            // pixels at 1080p. The bar shipped at 332x68 with 11-13pt text,
            // i.e. a 166px strip of ~6px lettering.
            _root = GameUIKit.Rect(transform, "GameUI_SpellsPanel");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, 144f);
            _root.sizeDelta = new Vector2(700f, 152f);

            var bg = _root.gameObject.AddComponent<Image>();
            bg.color = GameUIKit.PanelBg;
            GameUIKit.PanelChrome(_root);

            var title = GameUIKit.Text(_root, "Title", Loc.T("ABILITY"), 20f, GameUIKit.TextDim,
                TextAlignmentOptions.Center);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -6f);
            titleRect.sizeDelta = new Vector2(0f, 24f);

            var castRect = GameUIKit.Rect(_root, "CastButton");
            castRect.anchorMin = new Vector2(0f, 1f);
            castRect.anchorMax = new Vector2(1f, 1f);
            castRect.pivot = new Vector2(0.5f, 1f);
            castRect.anchoredPosition = new Vector2(0f, -38f);
            castRect.sizeDelta = new Vector2(-28f, 62f);
            _castBg = castRect.gameObject.AddComponent<Image>();
            _castBg.color = GameUIKit.ButtonBg;
            _castButton = castRect.gameObject.AddComponent<Button>();
            _castButton.targetGraphic = _castBg;
            _castButton.onClick.AddListener(Cast);
            _castLabel = GameUIKit.Text(castRect, "Label", "", 26f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch((RectTransform)_castLabel.transform);
            UITooltip.Bind(castRect.gameObject, () => _activeTooltip);

            _passiveLabel = GameUIKit.Text(_root, "Passive", "", 20f, GameUIKit.TextDim,
                TextAlignmentOptions.Center, wrap: false);
            var passiveRect = (RectTransform)_passiveLabel.transform;
            passiveRect.anchorMin = new Vector2(0f, 0f);
            passiveRect.anchorMax = new Vector2(1f, 0f);
            passiveRect.pivot = new Vector2(0.5f, 0f);
            passiveRect.anchoredPosition = new Vector2(0f, 10f);
            passiveRect.sizeDelta = new Vector2(-28f, 28f);
            // A label, not a button — but a passive still has to explain
            // itself, so it gets a raycast target and a hover of its own.
            _passiveLabel.raycastTarget = true;
            UITooltip.Bind(_passiveLabel.gameObject, () => _passiveTooltip);

            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Prose for an ability card. AbilityCard is pure data — there is no
        /// authored description field — so the tooltip is composed from the
        /// same structured effects the ability engine executes. That means it
        /// can never drift from what the ability actually does, and a new
        /// ability that reuses existing effect kinds documents itself.
        /// </summary>
        private static string Describe(AbilityCard card)
        {
            var sb = new System.Text.StringBuilder();

            sb.Append(card.Targeting switch
            {
                AbilityTargeting.SelfCast     => Loc.T("Affects the caster."),
                AbilityTargeting.SingleTarget => Loc.T("Targets one unit."),
                AbilityTargeting.Area         => Loc.T("Targets an area."),
                AbilityTargeting.Aura         => Loc.T("Continuous aura around the caster."),
                AbilityTargeting.Global       => Loc.T("Affects the whole faction."),
                _                             => "",
            });

            if (card.Targeting != AbilityTargeting.SelfCast
                && card.Targeting != AbilityTargeting.Global)
            {
                sb.Append(card.Affects switch
                {
                    AbilityAffects.AlliedCulture      => " " + Loc.T("Allies of your culture."),
                    AbilityAffects.AlliedAll          => " " + Loc.T("All allies."),
                    AbilityAffects.AlliedCavalry      => " " + Loc.T("Allied cavalry."),
                    AbilityAffects.Enemies            => " " + Loc.T("Enemies."),
                    AbilityAffects.EconomicBuildings  => " " + Loc.T("Allied economy buildings."),
                    _                                 => "",
                });
            }

            if (card.Effects != null)
                foreach (var effect in card.Effects)
                {
                    string line = DescribeEffect(effect);
                    if (line != null) sb.Append("\n• ").Append(line);
                }

            if (card.Radius > 0f) sb.Append('\n').Append(Loc.T("Radius")).Append(' ')
                                    .Append(card.Radius.ToString("0.#"));
            if (card.Range > 0f) sb.Append("   ").Append(Loc.T("Range")).Append(' ')
                                   .Append(card.Range.ToString("0.#"));
            if (card.Duration > 0f)
                sb.Append('\n').Append(Loc.T("Lasts")).Append(' ')
                  .Append(card.Duration.ToString("0.#")).Append('s');
            else if (card.IsPermanent) sb.Append('\n').Append(Loc.T("Always on"));
            if (card.Cooldown > 0f)
                sb.Append("   ").Append(Loc.T("Cooldown")).Append(' ')
                  .Append(Mathf.RoundToInt(card.Cooldown)).Append('s');

            return sb.ToString();
        }

        private static string DescribeEffect(AbilityEffect e) => e.Kind switch
        {
            AbilityEffectKind.AttackPct        => Signed(e.Value) + Loc.T("% damage dealt"),
            AbilityEffectKind.ArmorPct         => Signed(e.Value) + Loc.T("% armour"),
            AbilityEffectKind.ArmorFlat        => Signed(e.Value) + Loc.T(" armour"),
            AbilityEffectKind.DamageTakenPct   => Signed(e.Value) + Loc.T("% damage taken"),
            AbilityEffectKind.MoveSpeedPct     => Signed(e.Value) + Loc.T("% move speed"),
            AbilityEffectKind.SelfDoTPctOverDuration
                => string.Format(Loc.T("costs {0:0.#}% of max HP over the duration"), e.Value),
            AbilityEffectKind.HpFloor
                => string.Format(Loc.T("cannot drop below {0:0.#} HP"), e.Value),
            AbilityEffectKind.ChargeBonusFlat  => Signed(e.Value) + Loc.T(" charge damage"),
            AbilityEffectKind.RevealFog        => Loc.T("reveals fog of war"),
            AbilityEffectKind.ResourceYieldPct => Signed(e.Value) + Loc.T("% resource yield"),
            AbilityEffectKind.NoAutomation     => Loc.T("blocks further automation"),
            AbilityEffectKind.LosRampWhileStill=> Loc.T("sight grows while standing still"),
            AbilityEffectKind.ChargeDamagePct  => Signed(e.Value) + Loc.T("% damage on the next charge"),
            AbilityEffectKind.DisarmWhileBuffed=> Loc.T("cannot attack while it lasts"),
            AbilityEffectKind.DeployFieldHospital => Loc.T("deploys a temporary field hospital"),
            _ => null,
        };

        private static string Signed(float v) => (v >= 0f ? "+" : "") + v.ToString("0.#");

        /// <summary>Ring colour while aiming a unit's area ability. Distinct
        /// from the sect powers' blue so the two read apart.</summary>
        private static readonly Color AimRingColor = new Color(0.55f, 0.9f, 0.55f, 0.35f);

        private void Cast()
        {
            if (!_emReady || _primary == Entity.Null || !_em.Exists(_primary)) return;

            // AREA abilities are AIMED — put up the same ground-targeting ring
            // the sect powers and the Reliquary use, so the player can see and
            // choose the patch of map the ability will cover. Casting one
            // straight away centred it on the caster, which for Use Celestar
            // meant revealing fog the scout could already see.
            var card = ActiveCard();
            if (card != null && card.Targeting == AbilityTargeting.Area && card.Radius > 0f)
            {
                Entity caster = _primary;
                float radius = card.Radius;
                TheWaningBorder.UI.HUD.GroundTargeting.Begin(radius, AimRingColor, point =>
                {
                    var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (world == null || !world.IsCreated) return;
                    var em = world.EntityManager;
                    if (!em.Exists(caster)) return;
                    TheWaningBorder.Core.Commands.CommandRouter.IssueUnitAbility(
                        em, caster, default, point);
                });
                return;
            }

            TheWaningBorder.Core.Commands.CommandRouter.IssueUnitAbility(_em, _primary);
        }

        /// <summary>The selected unit's ready ACTIVE card, or null.</summary>
        private AbilityCard ActiveCard()
        {
            if (!_em.HasComponent<UnitAbilities>(_primary)) return null;
            var ua = _em.GetComponentData<UnitAbilities>(_primary);
            for (int s = 0; s < 4; s++) // UnitAbilities carries four slots (S0..S3)
            {
                var card = AbilityCatalog.Get(ua.Get(s));
                if (card != null && card.Activation == AbilityActivation.Active) return card;
            }
            return null;
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

            _passiveLabel.text = passive != null
                ? string.Format(Loc.T("Passive: {0}"), Loc.T(passive.Name)) : "";
            _passiveTooltip = passive != null
                ? $"<b>{Loc.T(passive.Name)}</b>  <color=#8FA8C0>{Loc.T("passive")}</color>\n{Describe(passive)}"
                : null;

            if (active == null)
            {
                _activeTooltip = null;
                _castButton.gameObject.SetActive(false);
                return;
            }
            _castButton.gameObject.SetActive(true);

            bool autonomous = _em.HasComponent<NotControllableTag>(_primary);
            float cd = AbilityQuery.CooldownRemaining(_em, _primary, activeSlot);
            bool ready = !autonomous && cd <= 0f;

            _castLabel.text = autonomous
                ? $"{Loc.T(active.Name)}  <color=#a8a294>{Loc.T("auto")}</color>"
                : cd <= 0f
                    ? Loc.T(active.Name)
                    : $"{Loc.T(active.Name)}  <color=#b88452>{Mathf.CeilToInt(cd)}s</color>";

            _castButton.interactable = ready;
            _castBg.color = ready ? GameUIKit.ButtonBg : GameUIKit.ButtonBgLocked;
            _castLabel.color = ready ? GameUIKit.Gold : GameUIKit.TextLocked;

            _activeTooltip = $"<b>{Loc.T(active.Name)}</b>  <color=#8FA8C0>{Loc.T("active")}</color>\n"
                + Describe(active)
                + (autonomous
                    ? "\n" + Loc.T("<i>This unit casts it by itself — you cannot trigger it.</i>")
                    : cd > 0f
                        ? "\n" + string.Format(
                            Loc.T("<color=#C08040>Recharging — {0}s.</color>"),
                            Mathf.CeilToInt(cd))
                        : "\n" + Loc.T("<color=#7FB069>Ready.</color>"));
        }
    }
}
