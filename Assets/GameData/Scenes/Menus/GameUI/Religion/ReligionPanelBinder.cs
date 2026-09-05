// ReligionPanelBinder.cs
// Binds the authored ReligionPanel prefab (GameUICatalog.religionPanel,
// mid-right) to the sect system. Successor of the removed IMGUI ReligionHUD.
//
// Layout contract (node names in the prefab):
//   RP          — TMP: religion point balance
//   TempleInfo  — TMP: temple level / upgrade status
//   Slot1..6    — chapel slot buttons (Button + child "label" TMP):
//                   empty    -> opens the sect picker
//                   building -> chapel build progress (disabled)
//                   adopted  -> left-click casts the sect's active power
//                               (ground-targeted), right-click toggles the
//                               Glow allocation (halves the cooldown)
//   Picker      — hidden roster: Sect1..12 adopt buttons + PickerClose
//
// Panel is hidden until the faction owns a COMPLETED Temple of Ridan.
// Adoption VALIDATES at click time (SectAdoption.ValidateAdoption) and
// routes CommandRouter.IssueSectAdoption; the RP + chapel material spend
// happens inside SectAdoptionCommandDirect on every peer, alongside the
// slot stamp (docs/Multiplayer_LAN_Readiness.md).

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.25f;
        private const float ChapelBuildSeconds = 30f;

        private sealed class SlotView
        {
            public GameObject Root;
            public Button Button;
            public TMP_Text Label;
            public string SectId;   // adopted sect currently shown (null otherwise)
            public byte State;      // mirrored TempleChapelSlot.State (255 = none)

            // One button per active TIER (early / mid / late) plus an
            // always-visible passive icon, built at runtime under the
            // authored slot — the prefab ships a single Button per slot, and
            // one button that auto-picked "highest ready tier" made the three
            // powers impossible to choose between.
            public Button[] Tier;      // [0]=T1 [1]=T2 [2]=T3
            public TMP_Text[] TierLabel;
            public GameObject PassiveIcon;
            public TMP_Text PassiveGlyph;
        }

        private sealed class PickerRow
        {
            public GameObject Root;
            public Button Button;
            public TMP_Text Label;
            public string SectId;
        }

        private TMP_Text _rp;
        private TMP_Text _templeInfo;
        private readonly List<SlotView> _slots = new();
        private GameObject _picker;
        private readonly List<PickerRow> _pickerRows = new();
        private int _pickerSlotIndex = -1;

        private float _timer;
        private bool _visible = true;

        private static readonly ComponentType[] TempleQueryTypes =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] LegacyTempleQueryTypes =
        {
            ComponentType.ReadOnly<TempleTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static readonly ComponentType[] HallQueryTypes =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _templeQuery;
        private TheWaningBorder.Core.CachedEntityQuery _legacyTempleQuery;
        private TheWaningBorder.Core.CachedEntityQuery _hallQuery;

        // ── Binding ────────────────────────────────────────────────────────

        /// <summary>
        /// The prefab authors the panel 460 wide. The four-cell ability strip
        /// claims ~250 of that, which would leave the sect name a sliver, so
        /// the root is widened here. The background and frame are stretched to
        /// the root and the slots are anchored 0..1 horizontally, so they all
        /// follow; the panel is right-anchored, so it grows leftwards.
        /// </summary>
        private const float MinPanelWidth = 680f;

        private void Awake()
        {
            if (transform is RectTransform root && root.sizeDelta.x < MinPanelWidth)
                root.sizeDelta = new Vector2(MinPanelWidth, root.sizeDelta.y);

            _rp = FindLabel(transform, "RP");
            _templeInfo = FindLabel(transform, "TempleInfo");

            for (int i = 0; i < 6; i++)
            {
                var node = GameUIKit.FindDeep(transform, "Slot" + (i + 1));
                if (node == null) continue;
                var view = new SlotView
                {
                    Root = node.gameObject,
                    Button = node.GetComponent<Button>(),
                    Label = node.GetComponentInChildren<TMP_Text>(true),
                    State = 255,
                };
                int slotIndex = i;
                if (view.Button != null)
                    view.Button.onClick.AddListener(() => ClickSlot(slotIndex));
                var relay = UITooltip.Relay(node.gameObject);
                relay.OnRightClick = () => RightClickSlot(slotIndex);
                UITooltip.Bind(node.gameObject, () => SlotTooltip(slotIndex));
                BuildTierControls(view, slotIndex);
                _slots.Add(view);
            }

            var picker = GameUIKit.FindDeep(transform, "Picker");
            _picker = picker != null ? picker.gameObject : null;
            if (_picker != null)
            {
                for (int i = 0; i < SectConfig.SectCount; i++)
                {
                    var node = GameUIKit.FindDeep(picker, "Sect" + (i + 1));
                    if (node == null) continue;
                    var row = new PickerRow
                    {
                        Root = node.gameObject,
                        Button = node.GetComponent<Button>(),
                        Label = node.GetComponentInChildren<TMP_Text>(true),
                        SectId = SectConfig.IdAt(i),
                    };
                    string sectId = row.SectId;
                    if (row.Button != null)
                        row.Button.onClick.AddListener(() => ClickAdopt(sectId));
                    // Adoption is permanent and costs a chapel; the roster row
                    // has to say what the sect actually does before you commit.
                    UITooltip.Bind(node.gameObject, () => AdoptTooltip(sectId));
                    _pickerRows.Add(row);
                }
                var close = GameUIKit.FindDeep(picker, "PickerClose");
                if (close != null)
                {
                    var closeButton = close.GetComponent<Button>();
                    if (closeButton != null)
                        closeButton.onClick.AddListener(ClosePicker);
                }
                _picker.SetActive(false);
            }

            if (_slots.Count == 0)
                TWBLog.Log("[GameUI] ReligionPanel: no Slot nodes found — check prefab names.");
        }

        // ── Refresh ────────────────────────────────────────────────────────

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
            EntityManager em = default;
            Entity temple = Entity.Null;
            if (ok)
            {
                em = world.EntityManager;
                ok = TryGetTemple(em, GameSettings.LocalPlayerFaction, out temple)
                    && !em.HasComponent<UnderConstruction>(temple);
            }

            SetVisible(ok);
            if (!ok) return;

            var faction = GameSettings.LocalPlayerFaction;
            int rp = FactionReligionPointsHelper.GetBalance(em, faction);
            if (_rp != null)
            {
                string text = Loc.T("Religion Points: ") + rp;
                if (_rp.text != text) _rp.text = text;
            }

            RefreshTempleInfo(em, temple);
            RefreshSlots(em, faction, temple);
            if (_picker != null && _picker.activeSelf)
                RefreshPicker(em, faction, temple, rp);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            // Hide only the rendered children, not this GameObject — the
            // binder must keep polling for the temple to reappear.
            foreach (Transform child in transform)
                if (child.gameObject.activeSelf != visible
                    && !(child.gameObject == _picker))    // picker stays closed
                    child.gameObject.SetActive(visible);
            if (!visible && _picker != null && _picker.activeSelf)
                _picker.SetActive(false);
        }

        private void RefreshTempleInfo(EntityManager em, Entity temple)
        {
            if (_templeInfo == null) return;
            int level = em.HasComponent<TempleLevel>(temple)
                ? em.GetComponentData<TempleLevel>(temple).Level : 1;
            string text;
            if (em.HasComponent<TempleUpgradeState>(temple))
            {
                var up = em.GetComponentData<TempleUpgradeState>(temple);
                float pct = up.Duration > 0f
                    ? Mathf.Clamp01((up.Duration - up.Remaining) / up.Duration) : 0f;
                text = string.Format(Loc.T("Temple Lv {0} - upgrading {1}%"),
                    level, (int)(pct * 100f));
            }
            else
            {
                text = string.Format(Loc.T("Temple Lv {0} - power tier {1}"),
                    level, Mathf.Clamp(level, 1, 3));
            }
            if (_templeInfo.text != text) _templeInfo.text = text;
        }

        // ── Slot interaction ───────────────────────────────────────────────

        // ── Runtime ability strip ──────────────────────────────────────────
        // The authored ReligionPanel prefab gives each chapel slot ONE button.
        // A sect has FOUR things to show once it is adopted — three tiered
        // actives (early / mid / ultimate) and one always-on passive — so the
        // extra controls are generated here as children of the authored slot
        // rather than by re-authoring the prefab. Swap this for prefab nodes
        // named "Ability1..3" / "PassiveIcon" if the panel ever ships them;
        // the binder resolves nodes by name via GameUIKit.FindDeep.
        //
        // Layout: [P][1][2][3] right-aligned inside the slot, sect name and
        // status on the left. The previous version stacked 34px buttons into
        // the slot's bottom corners where they overlapped the label and were
        // about 17 screen pixels across at 1080p — present, but unusable and
        // easy to miss entirely.

        private const float AbilitySize = 56f;
        private const float AbilityGap  = 6f;
        /// <summary>Room the strip needs on the right of every slot.</summary>
        private const float StripWidth = 4f * AbilitySize + 3f * AbilityGap + 10f;

        private static readonly Color AbilityLocked = new Color(0.10f, 0.10f, 0.12f, 0.55f);
        private static readonly Color AbilityReady  = new Color(0.16f, 0.34f, 0.52f, 0.95f);
        private static readonly Color AbilityCool   = new Color(0.20f, 0.18f, 0.12f, 0.90f);
        private static readonly Color PassiveLive   = new Color(0.16f, 0.13f, 0.05f, 0.92f);
        private static readonly Color GlyphLive     = new Color(1f, 0.86f, 0.45f);
        private static readonly Color GlyphDormant  = new Color(0.45f, 0.42f, 0.35f);

        // ── Tooltips ───────────────────────────────────────────────────────
        // Every ability cell explains itself on hover through the shared
        // UITooltip. These used to be pushed through the notification line,
        // which meant a hover printed a transient one-liner in the corner of
        // the screen and then expired — impossible to read while comparing
        // three powers.

        /// <summary>Cast a SPECIFIC tier — used by the three per-tier buttons.
        /// Silently does nothing if that tier is locked or still cooling.</summary>
        private void BeginCast(string sectId, int tier)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;
            if (!SectActivePowerHelper.CanFire(em, faction, sectId, tier)) return;

            var tierSpec = SectLeverEffects.ActiveOf(sectId, tier);
            float tierRadius = tierSpec.Radius > 0f ? tierSpec.Radius : 6f;
            GroundTargeting.Begin(tierRadius, new Color(0.35f, 0.6f, 0.9f, 0.85f), target =>
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w == null || !w.IsCreated) return;
                // Through the router: a sect power does AoE damage and spawns
                // strikes, so a cast that lands on one peer only makes the two
                // worlds disagree about who is alive.
                // docs/Multiplayer_LAN_Readiness.md
                TheWaningBorder.Core.Commands.CommandRouter.IssueSectPower(
                    w.EntityManager, faction, sectId, tier, target);
            });
        }

        private void BeginCast(string sectId)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            // Highest READY tier — lower tiers stay castable while higher
            // ones recharge (2026-08-04: previous powers remain available).
            int unlocked = SectActivePowerHelper.UnlockedTier(em, faction, sectId);
            int tier = 0;
            for (int t = unlocked; t >= 1; t--)
                if (SectActivePowerHelper.CanFire(em, faction, sectId, t)) { tier = t; break; }
            if (tier == 0) return;

            var spec = SectLeverEffects.ActiveOf(sectId, tier);
            float radius = spec.Radius > 0f ? spec.Radius : 6f;
            GroundTargeting.Begin(radius, new Color(0.35f, 0.6f, 0.9f, 0.85f), target =>
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w == null || !w.IsCreated) return;
                // Through the router: a sect power does AoE damage and spawns
                // strikes, so a cast that lands on one peer only makes the two
                // worlds disagree about who is alive.
                // docs/Multiplayer_LAN_Readiness.md
                TheWaningBorder.Core.Commands.CommandRouter.IssueSectPower(
                    w.EntityManager, faction, sectId, tier, target);
            });
        }

        // ── Picker ─────────────────────────────────────────────────────────

        // ── Lookups ────────────────────────────────────────────────────────

        private bool TryGetTemple(EntityManager em, Faction faction, out Entity temple)
        {
            temple = Entity.Null;
            var q = _templeQuery.Get(em, TempleQueryTypes);
            using (var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                    if (em.GetComponentData<FactionTag>(ents[i]).Value == faction)
                    { temple = ents[i]; return true; }
            }
            var lq = _legacyTempleQuery.Get(em, LegacyTempleQueryTypes);
            using (var ents = lq.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                    if (em.GetComponentData<FactionTag>(ents[i]).Value == faction)
                    { temple = ents[i]; return true; }
            }
            return false;
        }

        private byte LookupCulture(EntityManager em, Faction faction)
        {
            var q = _hallQuery.Get(em, HallQueryTypes);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var prog = q.ToComponentDataArray<FactionProgress>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) return prog[i].Culture;
            return Cultures.None;
        }

        private static TMP_Text FindLabel(Transform root, string node)
        {
            var t = GameUIKit.FindDeep(root, node);
            return t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
        }

    }
}
