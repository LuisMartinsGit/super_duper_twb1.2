// UnitStatsPanelBinder.cs
// Live data binding for the authored UnitStatsPanel prefab (Assets/GameData/
// Scenes/Menus/GameUI/SelectionUI/UnitStatsPanel.prefab). GameUIManager
// spawns the panel and adds this component to its root.
//
// Shown whenever the current selection contains at least one unit (UnitTag +
// Health); stats come from the FOCUSED unit (UnitRosterFocus): the roster
// entry the player clicked for mixed selections, else a hero (King Lexor),
// else the most-numerous selected type. Hidden via CanvasGroup alpha so this
// component keeps updating while invisible (SetActive on the root would stop
// Update and the panel could never come back).
//
// Bindings (children found by name):
// - HPSlider / HPLabel: current vs max Health, label "cur/max".
// - AttackCooldownSlider / ACDLabel: attack readiness — full bar = ready to
//   attack, an attack instantly empties it, then it refills as the cooldown
//   elapses. Label shows remaining cooldown rounded to tenths of a second
//   ("1.2s"). Ranged units track their live timer in
//   ArcherState.CooldownTimer; everything else in AttackCooldown.Timer.
//   Both elements hide for units that cannot attack.
// - Heart stat rows (Attack_Melee / Attack_Ranged / Attack_Siege /
//   Attack_Magic under the AttackDamage group, plus AttackRange / Speed /
//   LineofSight / MeleeDefense / RangedDefense / SiegeDefense /
//   MagicDefense): each row is a Synty HeartsBar of five hearts showing a
//   0-15 point score, three points per heart (partial hearts via the
//   SPR_Heart fill image). Raw stats normalize against the authored
//   TechTree ranges (see the *Full constants); armor is direct — one armor
//   point = one heart. The row Toggles are display-only and get disabled.
//   Only the attack row matching the unit's DamageTypeData shows, and
//   melee units hide the AttackRange row.
//
// BUILDINGS: when the selection holds no units but at least one building,
// the panel binds to the first selected building instead. HP works as for
// units; the cooldown slider doubles as CONSTRUCTION progress ("62%")
// while the building is UnderConstruction, then shows the
// BuildingRangedAttack cooldown for structures that can attack (hidden
// for passive buildings).
//
// The prefab carries two mode sections toggled wholesale: "UnitsSection"
// (AttackDamage type picker / Speed / LineofSight / AttackRange) and
// "BuildingsSection" (Attack_Ranged / AttackRange / LineofSight hearts +
// a "ResourceGeneration" pane of four sliders — SuppliesSlider /
// IronSlider / VeilstoneSlider / VeilsteelSlider — one per passive
// *Income component, normalized against IncomeFull, hidden for buildings
// that generate nothing). Row names repeat across the two sections, so
// heart rows bind SCOPED to their section root. The shared "Housing"
// row's "amount" TMP shows "+N" for PopulationProvider buildings and "N"
// for PopulationCost units, hiding when neither applies. Training queue,
// current production and actions belong to the actions panel, not here.
//
// Data is sampled at 10 Hz; the bars ease toward the sampled value every
// frame so changes read as smooth. Two exceptions snap instantly: an attack
// firing (bar back to empty) and the bound unit changing. Heart rows snap
// (they are discrete pips).

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class UnitStatsPanelBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.1f; // 10 Hz
        private const float BarLerpSpeed = 12f;     // exponential ease factor

        private CanvasGroup _group;
        private Slider _hpSlider, _acdSlider;
        private TMP_Text _hpLabel, _acdLabel;

        private float _timer;
        private bool _visible = true;
        private SelectionChangeDetector _selectionDetector;

        private Entity _boundUnit = Entity.Null;
        private float _hpTarget, _acdTarget;
        private float _lastRemaining;

        // ── Heart stat rows ────────────────────────────────────────────
        private const int PointsMax = 15;      // full bar
        private const int PointsPerHeart = 3;  // 5 hearts x 3

        // Raw stat value that fills the whole bar, calibrated to the
        // authored TechTree ranges (damage 0-40, range/sight 8-40, speed
        // 3-7.2, per-channel armor 0-5). Rebalance passes that widen those
        // ranges must retune these.
        private const float DamageFull = 40f;
        private const float RangeFull  = 40f;
        private const float SpeedFull  = 7.5f;
        private const float SightFull  = 40f;
        private const float ArmorFull  = 5f;

        // Full-bar rate for the per-resource generation sliders
        // (Gatherer's Hut peaks at 60 supplies/min).
        private const float IncomeFull = 120f;

        /// <summary>SPR_Heart fill images of one row, in display order.</summary>
        private sealed class HeartRow
        {
            public GameObject Root;
            public Image[] Hearts;
        }

        private HeartRow _atkMelee, _atkRanged, _atkSiege, _atkMagic,
                         _atkRange, _speedRow, _sightRow,
                         _defMelee, _defRanged, _defSiege, _defMagic;

        // Mode sections + building-scoped bindings (BuildingsSection).
        private GameObject _unitsSection, _buildingsSection;
        private HeartRow _bAtkRanged, _bAtkRange, _bSight;
        private GameObject _resourceGenRow;
        private Slider _suppliesGen, _ironGen, _veilstoneGen, _veilsteelGen;
        private GameObject _housingRow;
        private TMP_Text _housingAmount;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            foreach (var slider in GetComponentsInChildren<Slider>(true))
            {
                if (NameIs(slider, "HPSlider")) _hpSlider = slider;
                else if (NameIs(slider, "AttackCooldownSlider")) _acdSlider = slider;
            }
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
            {
                if (NameIs(label, "HPLabel")) _hpLabel = label;
                else if (NameIs(label, "ACDLabel") || NameIs(label, "HPLabel (1)"))
                    _acdLabel = label;
            }

            // Display-only bars — normalize and lock out user interaction.
            Setup(_hpSlider);
            Setup(_acdSlider);

            if (_hpSlider == null || _hpLabel == null)
                TWBLog.Log("[GameUI] UnitStatsPanel: HPSlider/HPLabel not found — renamed?");
            if (_acdSlider == null || _acdLabel == null)
                TWBLog.Log("[GameUI] UnitStatsPanel: AttackCooldownSlider/ACDLabel not found — renamed?");

            // Section roots. Row names repeat between the unit and building
            // sections, so unit rows bind scoped to UnitsSection and
            // building rows to BuildingsSection (whole-panel fallback keeps
            // older prefabs without sections working).
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (NameIs(t, "UnitsSection")) _unitsSection = t.gameObject;
                else if (NameIs(t, "BuildingsSection")) _buildingsSection = t.gameObject;
                else if (NameIs(t, "Housing")) _housingRow = t.gameObject;
            }

            Transform unitRoot = _unitsSection != null ? _unitsSection.transform : transform;
            _atkMelee  = BindHeartRow(unitRoot, "Attack_Melee", "AttackMelee", "MeleeAttack");
            _atkRanged = BindHeartRow(unitRoot, "Attack_Ranged", "AttackRanged", "RangedAttack");
            _atkSiege  = BindHeartRow(unitRoot, "Attack_Siege", "AttackSiege", "SiegeAttack");
            _atkMagic  = BindHeartRow(unitRoot, "Attack_Magic", "AttackMagic", "MagicAttack");
            _atkRange  = BindHeartRow(unitRoot, "AttackRange");
            _speedRow  = BindHeartRow(unitRoot, "Speed");
            _sightRow  = BindHeartRow(unitRoot, "LineOfSight");
            _defMelee  = BindHeartRow(transform, "MeleeDefence", "MeleeDefense"); // authored spelling
            _defRanged = BindHeartRow(transform, "RangedDefense");
            _defSiege  = BindHeartRow(transform, "SiegeDefense");
            _defMagic  = BindHeartRow(transform, "MagicDefense");
            if (_atkMelee == null && _defMelee == null)
                TWBLog.Log("[GameUI] UnitStatsPanel: no heart stat rows found — were the " +
                    "GameUI.unity scene overrides applied to the prefab?");

            if (_buildingsSection != null)
            {
                var bRoot = _buildingsSection.transform;
                _bAtkRanged = BindHeartRow(bRoot, "Attack_Ranged");
                _bAtkRange  = BindHeartRow(bRoot, "AttackRange");
                _bSight     = BindHeartRow(bRoot, "LineOfSight");
                foreach (var t in bRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (NameIs(t, "ResourceGeneration"))
                        _resourceGenRow = t.gameObject;
                }
                foreach (var slider in bRoot.GetComponentsInChildren<Slider>(true))
                {
                    if (NameIs(slider, "SuppliesSlider")) _suppliesGen = slider;
                    else if (NameIs(slider, "IronSlider")) _ironGen = slider;
                    else if (NameIs(slider, "VeilstoneSlider")) _veilstoneGen = slider;
                    else if (NameIs(slider, "VeilsteelSlider")) _veilsteelGen = slider;
                }
                Setup(_suppliesGen);
                Setup(_ironGen);
                Setup(_veilstoneGen);
                Setup(_veilsteelGen);
                _buildingsSection.SetActive(false);
            }

            if (_housingRow != null)
            {
                foreach (var label in _housingRow.GetComponentsInChildren<TMP_Text>(true))
                {
                    _housingAmount = label;
                    if (NameIs(label, "amount")) break;
                }
            }

            SetVisible(false);
        }

        private static bool NameIs(Component c, string name) =>
            string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase);

        private static void Setup(Slider slider)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
        }

        private void Update()
        {
            // Refresh on the slow cadence for live data, but INSTANTLY when
            // the selection changes — every selection panel must appear on
            // the same frame.
            _timer += Time.unscaledDeltaTime;
            if (_timer >= RefreshInterval || _selectionDetector.Poll())
            {
                _timer = 0f;
                Refresh();
            }

            if (!_visible) return;

            // Per-frame easing toward the last sampled values.
            float k = 1f - Mathf.Exp(-BarLerpSpeed * Time.unscaledDeltaTime);
            if (_hpSlider != null)
                _hpSlider.value = Mathf.Lerp(_hpSlider.value, _hpTarget, k);
            if (_acdSlider != null)
                _acdSlider.value = Mathf.Lerp(_acdSlider.value, _acdTarget, k);
        }

        private void Refresh()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { SetVisible(false); return; }
            var em = world.EntityManager;

            var unit = UnitRosterFocus.ResolveStatsUnit(em);
            if (unit == Entity.Null)
            {
                // No unit in the selection — fall back to the first selected
                // building (any selected unit wins over buildings).
                var building = ResolveStatsBuilding(em);
                if (building == Entity.Null)
                {
                    SetVisible(false);
                    _boundUnit = Entity.Null;
                    return;
                }
                RefreshBuilding(em, building);
                return;
            }
            SetVisible(true);

            bool unitChanged = unit != _boundUnit;
            _boundUnit = unit;

            var health = em.GetComponentData<Health>(unit);
            _hpTarget = health.Max > 0
                ? Mathf.Clamp01((float)health.Value / health.Max) : 0f;
            if (unitChanged && _hpSlider != null) _hpSlider.value = _hpTarget;
            if (_hpLabel != null) _hpLabel.text = health.Value + "/" + health.Max;

            bool hasAttack = em.HasComponent<AttackCooldown>(unit);
            if (hasAttack)
            {
                var cd = em.GetComponentData<AttackCooldown>(unit);
                // Ranged units run their live countdown in ArcherState;
                // AttackCooldown.Timer stays untouched for them.
                float remaining = em.HasComponent<ArcherState>(unit)
                    ? em.GetComponentData<ArcherState>(unit).CooldownTimer
                    : cd.Timer;
                remaining = Mathf.Clamp(remaining, 0f, cd.Cooldown);

                // Full bar = ready; refills as the cooldown runs out.
                _acdTarget = cd.Cooldown > 0f ? 1f - remaining / cd.Cooldown : 1f;

                // The timer jumping UP means an attack just fired — the bar
                // must drop to empty instantly, not ease down.
                bool attackFired = remaining > _lastRemaining + 0.001f;
                _lastRemaining = remaining;
                if ((unitChanged || attackFired) && _acdSlider != null)
                    _acdSlider.value = _acdTarget;

                if (_acdLabel != null)
                    _acdLabel.text = (Mathf.Round(remaining * 10f) / 10f).ToString("0.0") + "s";
            }
            if (_acdSlider != null && _acdSlider.gameObject.activeSelf != hasAttack)
                _acdSlider.gameObject.SetActive(hasAttack);
            if (_acdLabel != null && _acdLabel.gameObject.activeSelf != hasAttack)
                _acdLabel.gameObject.SetActive(hasAttack);

            RefreshHeartRows(em, unit);
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
}
