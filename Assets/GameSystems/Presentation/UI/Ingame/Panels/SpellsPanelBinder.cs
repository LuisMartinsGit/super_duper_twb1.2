// SpellsPanelBinder.cs
// Code-built spells bar (no authored prefab yet).
//
// The 2026-08-02 rule — at most one Active and one Passive — was always a
// NON-HERO rule, and 2026-09-08 is where the exception arrives: King Lexor
// carries King's Call (passive), Liquid Courage and Honour thy Pledge
// (docs/Design/Heroes.md §2). The bar therefore draws ONE CAST BUTTON PER
// usable Active, stacked, and grows to fit; the Passive keeps its dim line
// along the bottom.
//
// Each button casts ITS OWN SLOT through CommandRouter.IssueUnitAbilitySlot.
// It used to call IssueUnitAbility, which fires "the first ready active" —
// precise while a unit had only one, and a coin toss between two different
// abilities the moment it had two.
//
// An ability the unit has not unlocked yet is not drawn at all rather than
// drawn greyed out: a button the player cannot explain is worse than no
// button. Not-controllable automatons (Ledger) show their active as "auto".
//
// A channelled ability (cast time > 0) fills its own button from left to
// right while it channels -- the cast bar. A new order, a stun or death
// breaks the channel and the fill simply disappears (docs/Design/Spells.md).

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.Ingame
{
    public class SpellsPanelBinder : MonoBehaviour
    {
        private const float PollInterval = 0.2f;
        private const int MaxSlots = 4; // storage allows 4; design uses 1+1 for non-heroes

        /// <summary>Height of one cast row, and the gap below it.</summary>
        private const float RowHeight = 62f;
        private const float RowStride = 66f;
        /// <summary>Panel height with a single cast row.</summary>
        private const float BaseHeight = 152f;

        private RectTransform _root;
        private readonly Button[] _castButtons = new Button[MaxSlots];
        private readonly Image[] _castBgs = new Image[MaxSlots];
        private readonly TMP_Text[] _castLabels = new TMP_Text[MaxSlots];
        /// <summary>Per-row cast bar: a fill stretched to the channel's progress.</summary>
        private readonly RectTransform[] _castFills = new RectTransform[MaxSlots];
        /// <summary>UnitAbilities slot each drawn row casts, -1 when unused.</summary>
        private readonly int[] _rowSlot = new int[MaxSlots];
        /// <summary>Scratch for AbilityQuery.ActiveSlots, reused each poll.</summary>
        private readonly int[] _activeSlotScratch = new int[MaxSlots];
        private TMP_Text _passiveLabel;
        private float _nextPoll;
        private EntityManager _em;
        private bool _emReady;
        private Entity _primary = Entity.Null;

        // Rebuilt each poll and read by the hover callbacks.
        private readonly string[] _activeTooltips = new string[MaxSlots];
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

            for (int r = 0; r < MaxSlots; r++)
            {
                int row = r;   // captured per row, not per loop variable
                var castRect = GameUIKit.Rect(_root, "CastButton" + row);
                castRect.anchorMin = new Vector2(0f, 1f);
                castRect.anchorMax = new Vector2(1f, 1f);
                castRect.pivot = new Vector2(0.5f, 1f);
                castRect.anchoredPosition = new Vector2(0f, -38f - row * RowStride);
                castRect.sizeDelta = new Vector2(-28f, RowHeight);

                _castBgs[row] = castRect.gameObject.AddComponent<Image>();
                _castBgs[row].color = GameUIKit.ButtonBg;
                GameUIKit.ButtonChrome(castRect, raycast: true, existing: _castBgs[row]);
                _castButtons[row] = castRect.gameObject.AddComponent<Button>();
                _castButtons[row].targetGraphic = _castBgs[row];
                _castButtons[row].onClick.AddListener(() => Cast(row));

                // The cast bar sits UNDER the label, inside the button, and is
                // hidden until this row's ability is channelling.
                var fill = GameUIKit.Rect(castRect, "CastFill");
                fill.anchorMin = new Vector2(0f, 0f);
                fill.anchorMax = new Vector2(0f, 1f);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;
                var fillImg = fill.gameObject.AddComponent<Image>();
                fillImg.color = CastFillColor;
                fillImg.raycastTarget = false;
                fill.gameObject.SetActive(false);
                _castFills[row] = fill;

                _castLabels[row] = GameUIKit.Text(castRect, "Label", "", 26f, GameUIKit.Gold,
                    TextAlignmentOptions.Center, wrap: false);
                GameUIKit.Stretch((RectTransform)_castLabels[row].transform);
                UITooltip.Bind(castRect.gameObject, () => _activeTooltips[row]);
                castRect.gameObject.SetActive(false);
                _rowSlot[row] = -1;
            }

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
                    AbilityAffects.AlliedRanged       => " " + Loc.T("Allied ranged units."),
                    _                                 => "",
                });
            }

            if (card.Damage > 0f)
                sb.Append("\n• ").Append(string.Format(Loc.T("deals {0:0} {1} damage"),
                    card.Damage, DamageTypeName(card.DamageType)));

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
            // Every ACTIVE shows the cooldown the engine really charges --
            // including "auto" cards (Cooldown 0), which used to show none.
            if (!card.IsPassive)
                sb.Append("   ").Append(Loc.T("Cooldown")).Append(' ')
                  .Append(Mathf.RoundToInt(card.EffectiveCooldown)).Append('s');
            if (card.CastTime > 0f)
                sb.Append('\n').Append(string.Format(
                    Loc.T("Channel {0:0.#}s -- a new order, a stun or death interrupts it."),
                    card.CastTime));

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
            AbilityEffectKind.FireRatePct      => Signed(e.Value) + Loc.T("% fire rate"),
            AbilityEffectKind.SummonPledgeArmy => Loc.T("calls in a temporary army that grows with the hero's level"),
            AbilityEffectKind.ShardboundFury   => Loc.T("hurls every enemy in the radius into the air; enemy buildings take heavy damage"),
            _ => null,
        };

        private static string DamageTypeName(DamageType t) => t switch
        {
            DamageType.Melee  => Loc.T("melee"),
            DamageType.Ranged => Loc.T("ranged"),
            DamageType.Siege  => Loc.T("siege"),
            DamageType.True   => Loc.T("true"),
            _                 => Loc.T("magic"),
        };

        /// <summary>Cast-bar fill colour: the gold of the UI, translucent so the
        /// label on top stays readable.</summary>
        private static readonly Color CastFillColor = new Color(0.83f, 0.66f, 0.26f, 0.45f);

        private static string Signed(float v) => (v >= 0f ? "+" : "") + v.ToString("0.#");

        /// <summary>Ring colour while aiming a unit's area ability. Distinct
        /// from the sect powers' blue so the two read apart.</summary>
        private static readonly Color AimRingColor = new Color(0.55f, 0.9f, 0.55f, 0.35f);

        private void Cast(int row)
        {
            if (!_emReady || _primary == Entity.Null || !_em.Exists(_primary)) return;
            if (row < 0 || row >= MaxSlots) return;
            int slot = _rowSlot[row];
            if (slot < 0) return;

            // AREA abilities are AIMED — put up the same ground-targeting ring
            // the sect powers and the Reliquary use, so the player can see and
            // choose the patch of map the ability will cover. Casting one
            // straight away centred it on the caster, which for Use Celestar
            // meant revealing fog the scout could already see.
            var ua = _em.GetComponentData<UnitAbilities>(_primary);
            var card = AbilityCatalog.Get(ua.Get(slot));

            // An Area ability CENTRED ON SELF takes no aim: Honour thy Pledge
            // forms its ring around the king wherever he stands, so putting up
            // a targeting ring for it would ask the player a question with only
            // one answer. The card's AimedAtPoint flag is what marks the
            // difference -- it used to be "Range > 0", which also refused the
            // ring to Use Celestar, whose Range 0 means UNLIMITED.
            bool aimed = card != null && card.IsAimed;
            if (aimed)
            {
                Entity caster = _primary;
                float radius = card.Radius;
                TheWaningBorder.UI.World.GroundTargeting.Begin(radius, AimRingColor, point =>
                {
                    var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (world == null || !world.IsCreated) return;
                    var em = world.EntityManager;
                    if (!em.Exists(caster)) return;
                    TheWaningBorder.Core.Commands.CommandRouter.IssueUnitAbilitySlot(
                        em, caster, slot, default, point);
                });
                return;
            }

            TheWaningBorder.Core.Commands.CommandRouter.IssueUnitAbilitySlot(
                _em, _primary, slot);
        }

        void Update()
        {
            // The cast bar moves every frame; everything else polls.
            UpdateCastBars();
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

            var ua = _em.GetComponentData<UnitAbilities>(_primary);

            // The passive line first — one passive, unchanged.
            AbilityCard passive = null;
            for (int sl = 0; sl < MaxSlots; sl++)
            {
                var c = AbilityCatalog.Get(ua.Get(sl));
                if (c != null && c.Activation == AbilityActivation.Passive) { passive = c; break; }
            }

            _passiveLabel.text = passive != null
                ? string.Format(Loc.T("Passive: {0}"), Loc.T(passive.Name)) : "";
            _passiveTooltip = passive != null
                ? $"<b>{Loc.T(passive.Name)}</b>  <color=#8FA8C0>{Loc.T("passive")}</color>\n{Describe(passive)}"
                : null;

            // Every UNLOCKED active gets its own row. AbilityQuery does the
            // level gating, so a hero below the unlock simply has fewer rows.
            int count = AbilityQuery.ActiveSlots(_em, _primary, _activeSlotScratch);
            bool autonomous = _em.HasComponent<NotControllableTag>(_primary);

            for (int row = 0; row < MaxSlots; row++)
            {
                bool used = row < count;
                _rowSlot[row] = used ? _activeSlotScratch[row] : -1;

                var go = _castButtons[row].gameObject;
                if (go.activeSelf != used) go.SetActive(used);
                if (!used) { _activeTooltips[row] = null; continue; }

                int slot = _rowSlot[row];
                var active = AbilityCatalog.Get(ua.Get(slot));
                if (active == null) { go.SetActive(false); _rowSlot[row] = -1; continue; }

                float cd = AbilityQuery.CooldownRemaining(_em, _primary, slot);
                bool channelling = AbilityQuery.TryGetCast(_em, _primary, out int castSlot,
                    out _, out float castLeft) && castSlot == slot;
                bool ready = !autonomous && cd <= 0f && !channelling;

                _castLabels[row].text = autonomous
                    ? $"{Loc.T(active.Name)}  <color=#a8a294>{Loc.T("auto")}</color>"
                    : channelling
                        ? $"{Loc.T(active.Name)}  <color=#e8d5a0>{string.Format(Loc.T("casting {0:0.0}s"), castLeft)}</color>"
                    : cd <= 0f
                        ? Loc.T(active.Name)
                        : $"{Loc.T(active.Name)}  <color=#b88452>{Mathf.CeilToInt(cd)}s</color>";

                _castButtons[row].interactable = ready;
                _castBgs[row].color = ready ? GameUIKit.ButtonBg : GameUIKit.ButtonBgLocked;
                _castLabels[row].color = ready ? GameUIKit.Gold : GameUIKit.TextLocked;

                _activeTooltips[row] =
                    $"<b>{Loc.T(active.Name)}</b>  <color=#8FA8C0>{Loc.T("active")}</color>\n"
                    + Describe(active)
                    + (autonomous
                        ? "\n" + Loc.T("<i>This unit casts it by itself — you cannot trigger it.</i>")
                        : cd > 0f
                            ? "\n" + string.Format(
                                Loc.T("<color=#C08040>Recharging — {0}s.</color>"),
                                Mathf.CeilToInt(cd))
                            : "\n" + Loc.T("<color=#7FB069>Ready.</color>"));
            }

            UpdateCastBars();

            // Grow the panel to fit however many rows are drawn, so the
            // passive line along the bottom never rides up over a cast button.
            float h = BaseHeight + RowStride * Mathf.Max(0, count - 1);
            if (!Mathf.Approximately(_root.sizeDelta.y, h))
                _root.sizeDelta = new Vector2(_root.sizeDelta.x, h);
        }

        /// <summary>
        /// Stretch each row's fill to its channel's progress. Only the row whose
        /// slot is channelling shows a fill; every other row hides its own.
        /// Presentation only -- reads AbilityCastState, never writes the sim.
        /// </summary>
        private void UpdateCastBars()
        {
            if (!_emReady || _root == null || !_root.gameObject.activeSelf) return;
            bool casting = _primary != Entity.Null && _em.Exists(_primary)
                && AbilityQuery.TryGetCast(_em, _primary, out _castSlotScratch,
                    out _castProgressScratch, out _);
            for (int row = 0; row < MaxSlots; row++)
            {
                var fill = _castFills[row];
                if (fill == null) continue;
                bool show = casting && _rowSlot[row] >= 0 && _rowSlot[row] == _castSlotScratch;
                if (fill.gameObject.activeSelf != show) fill.gameObject.SetActive(show);
                if (show) fill.anchorMax = new Vector2(_castProgressScratch, 1f);
            }
        }

        private int _castSlotScratch;
        private float _castProgressScratch;
    }
}
