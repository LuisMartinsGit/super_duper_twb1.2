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
// Location: Assets/Scripts/UI/GameUI/ReligionPanelBinder.cs

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
using TheWaningBorder.UI.HUD;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class ReligionPanelBinder : MonoBehaviour
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

        private void RefreshSlots(EntityManager em, Faction faction, Entity temple)
        {
            // Snapshot the buffer — holding a DynamicBuffer across UI work is
            // unsafe against structural changes.
            int count = 0;
            var states = new byte[_slots.Count];
            var sects = new string[_slots.Count];
            var progress = new int[_slots.Count];
            if (em.HasBuffer<TempleChapelSlot>(temple))
            {
                var buffer = em.GetBuffer<TempleChapelSlot>(temple);
                count = Mathf.Min(_slots.Count, buffer.Length);
                for (int i = 0; i < count; i++)
                {
                    var s = buffer[i];
                    states[i] = s.State;
                    sects[i] = s.SectId.ToString();
                    progress[i] = s.BuildTime > 0f
                        ? (int)(100f * s.BuildProgress / s.BuildTime) : 0;
                }
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                var view = _slots[i];
                if (view.Label == null) continue;

                if (i >= count)
                {
                    view.SectId = null;
                    view.State = 255;
                    SetSlot(view, Loc.T("No slot"), false);
                    continue;
                }

                byte state = states[i];
                if (state == 0)
                {
                    view.SectId = null;
                    view.State = 0;
                    SetSlot(view, Loc.T("Adopt a sect"), true);
                }
                else if (state == 1)
                {
                    view.SectId = sects[i];
                    view.State = 1;
                    SetSlot(view, string.Format(Loc.T("{0} chapel - {1}%"),
                        SectInfo.ShortName(sects[i]), progress[i]), false);
                }
                else
                {
                    string sectId = sects[i];
                    view.SectId = sectId;
                    view.State = 2;
                    // ALL unlocked tiers stay usable (2026-08-04 — upgrading
                    // used to hide the previous powers): the slot casts the
                    // highest tier that is READY, and each tier cools
                    // independently, so tier 1 remains available while
                    // tier 3 recharges.
                    int unlocked = SectActivePowerHelper.UnlockedTier(em, faction, sectId);
                    int readyTier = 0;
                    float soonest = float.MaxValue;
                    for (int t = unlocked; t >= 1; t--)
                    {
                        float rem = SectActivePowerHelper.CooldownRemaining(em, faction, sectId, t);
                        if (rem <= 0f && readyTier == 0) readyTier = t;
                        if (rem < soonest) soonest = rem;
                    }
                    bool glow = SectActivePowerHelper.HasGlowAllocated(em, faction, sectId);
                    bool ready = readyTier > 0;

                    string line1 = string.Format(Loc.T("{0}  Tier {1}"),
                            SectInfo.ShortName(sectId), unlocked)
                        + (glow ? "  [" + Loc.T("Glow") + "]" : "");
                    string line2 = ready
                        ? Loc.T("Cast: ") + SectInfo.ActiveName(sectId, readyTier)
                            + (readyTier < unlocked ? $" (T{readyTier})" : "")
                        : string.Format(Loc.T("Ready in {0}s"), Mathf.CeilToInt(soonest));
                    SetSlot(view, line1 + "\n" + line2, ready);
                }

                RefreshTierControls(view, em, faction);
            }
        }

        private static void SetSlot(SlotView view, string text, bool interactable)
        {
            if (view.Label.text != text) view.Label.text = text;
            if (view.Button != null && view.Button.interactable != interactable)
                view.Button.interactable = interactable;
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

        private void BuildTierControls(SlotView view, int slotIndex)
        {
            var parent = view.Root.transform as RectTransform;
            if (parent == null) return;

            // Keep the sect name clear of the strip.
            if (view.Label != null)
            {
                var lrt = view.Label.rectTransform;
                lrt.offsetMax = new Vector2(-StripWidth, lrt.offsetMax.y);
                view.Label.alignment = TextAlignmentOptions.MidlineLeft;
            }

            view.Tier = new Button[3];
            view.TierLabel = new TMP_Text[3];

            // Cell 0 is the passive badge, cells 1-3 the actives, laid out
            // left to right from the strip's left edge.
            view.PassiveIcon = BuildCell(parent, "PassiveIcon", 0, PassiveLive,
                out view.PassiveGlyph, out _);
            UITooltip.Bind(view.PassiveIcon, () => PassiveTooltip(slotIndex));

            for (int t = 0; t < 3; t++)
            {
                int tier = t + 1;
                var cell = BuildCell(parent, $"Ability{tier}", tier, AbilityLocked,
                    out var label, out var image);

                var btn = cell.AddComponent<Button>();
                btn.targetGraphic = image;
                btn.onClick.AddListener(() => ClickTier(slotIndex, tier));
                label.text = tier.ToString();

                UITooltip.Bind(cell, () => TierTooltip(slotIndex, tier));

                view.Tier[t] = btn;
                view.TierLabel[t] = label;
                cell.SetActive(false);
            }
            view.PassiveIcon.SetActive(false);
        }

        /// <summary>One square cell of the four-wide ability strip.</summary>
        private static GameObject BuildCell(RectTransform parent, string name, int index,
            Color background, out TMP_Text glyph, out Image image)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(AbilitySize, AbilitySize);
            rt.anchoredPosition = new Vector2(
                -(5f + (3 - index) * (AbilitySize + AbilityGap)), 0f);

            image = go.GetComponent<Image>();
            image.color = background;

            glyph = GameUIKit.Text(rt, "glyph", index == 0 ? "P" : "", 26f, GlyphLive,
                TextAlignmentOptions.Center, wrap: false);
            var grt = glyph.rectTransform;
            grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
            grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
            return go;
        }

        private void ClickTier(int slotIndex, int tier)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;
            var view = _slots[slotIndex];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return;
            BeginCast(view.SectId, tier);
        }


        // ── Tooltips ───────────────────────────────────────────────────────
        // Every ability cell explains itself on hover through the shared
        // UITooltip. These used to be pushed through the notification line,
        // which meant a hover printed a transient one-liner in the corner of
        // the screen and then expired — impossible to read while comparing
        // three powers.

        private string TierTooltip(int slotIndex, int tier)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return null;
            var view = _slots[slotIndex];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return null;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(SectInfo.ActiveName(view.SectId, tier))
              .Append("</b>  <color=#8FA8C0>")
              .Append(string.Format(Loc.T("Active {0}/3"), tier)).Append("</color>\n")
              .Append(SectInfo.ActivePowerDescription(view.SectId, tier));

            var spec = SectLeverEffects.ActiveOf(view.SectId, tier);
            if (spec.Radius > 0f) sb.Append('\n').Append(Loc.T("Radius")).Append(' ')
                                    .Append(spec.Radius.ToString("0.#"));
            if (spec.Cooldown > 0f) sb.Append("   ").Append(Loc.T("Cooldown")).Append(' ')
                                      .Append(Mathf.RoundToInt(spec.Cooldown)).Append('s');

            int unlocked = SectActivePowerHelper.UnlockedTier(em, faction, view.SectId);
            if (tier > unlocked)
            {
                sb.Append("\n<color=#C08040>")
                  .Append(string.Format(
                      Loc.T("Locked — raise the sect's Active lever to Lv {0}."), tier))
                  .Append("</color>");
            }
            else
            {
                float rem = SectActivePowerHelper.CooldownRemaining(em, faction, view.SectId, tier);
                sb.Append(rem > 0f
                    ? "\n" + string.Format(
                        Loc.T("<color=#C08040>Recharging — {0}s.</color>"),
                        Mathf.CeilToInt(rem))
                    : "\n" + Loc.T("<color=#7FB069>Ready — click, then pick a target on the map.</color>"));
            }

            if (SectActivePowerHelper.HasGlowAllocated(em, faction, view.SectId))
                sb.Append('\n').Append(Loc.T("<i>Glow allocated: cooldowns halved.</i>"));
            else
                sb.Append('\n').Append(
                    Loc.T("<i>Right-click the slot to allocate Glow (halves cooldowns).</i>"));
            return sb.ToString();
        }

        /// <summary>The slot body itself: what it is and what clicking does.</summary>
        private string SlotTooltip(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return null;
            var view = _slots[slotIndex];

            switch (view.State)
            {
                case 0:
                    return Loc.T("<b>Empty chapel slot</b>") + "\n"
                        + Loc.T("Click to open the sect roster. Adopting a sect spends Religion Points plus the chapel's materials and is permanent for the match.");
                case 1:
                    return string.Format(Loc.T("<b>{0} chapel</b>"),
                            SectInfo.ShortName(view.SectId)) + "\n"
                        + Loc.T("Under construction. Its powers come online when the chapel finishes.");
                case 2:
                    return $"<b>{SectInfo.ShortName(view.SectId)}</b>\n{SectInfo.Lore(view.SectId)}"
                        + "\n" + Loc.T("<i>Left-click: cast the highest ready active. Right-click: toggle Glow allocation (halves cooldowns).</i>");
                default:
                    return Loc.T("<b>No slot</b>") + "\n"
                        + Loc.T("Upgrade the Temple of Ridan to open more chapel slots.");
            }
        }

        /// <summary>Roster row: the whole sect, before you commit to it.</summary>
        private static string AdoptTooltip(string sectId)
        {
            if (!SectConfig.IsImplemented(sectId))
                return $"<b>{SectInfo.ShortName(sectId)}</b>\n" + Loc.T("<i>Coming soon.</i>");

            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(SectInfo.ShortName(sectId)).Append("</b>\n")
              .Append(SectInfo.Lore(sectId))
              .Append("\n\n<b>").Append(Loc.T("Passive")).Append("</b>  ")
              .Append(SectInfo.PassiveDescription(sectId));
            for (int tier = 1; tier <= 3; tier++)
                sb.Append("\n<b>").Append(string.Format(Loc.T("Active {0}"), tier))
                  .Append("</b>  ")
                  .Append(SectInfo.ActiveName(sectId, tier)).Append(" — ")
                  .Append(SectInfo.ActivePowerDescription(sectId, tier));
            sb.Append("\n<b>").Append(Loc.T("Unit")).Append("</b>  ")
              .Append(SectInfo.UnitDescription(sectId));
            sb.Append("\n<b>").Append(Loc.T("Research")).Append("</b>  ")
              .Append(SectInfo.TechnologyDescription(sectId));
            return sb.ToString();
        }

        private string PassiveTooltip(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return null;
            var view = _slots[slotIndex];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return null;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            bool live = world != null && world.IsCreated
                && SectQuery.HasStandingTemple(world.EntityManager, GameSettings.LocalPlayerFaction);

            return string.Format(Loc.T("<b>{0} — passive</b>"),
                    SectInfo.ShortName(view.SectId)) + "\n"
                + SectInfo.PassiveDescription(view.SectId)
                + (live
                    ? "\n" + Loc.T("<color=#7FB069>Active — always on, no cooldown.</color>")
                    : "\n" + Loc.T("<color=#C08040>Dormant — your Temple is down.</color>"));
        }

        /// <summary>Refresh the three tier buttons + the passive icon for a slot.</summary>
        private static void RefreshTierControls(SlotView view, EntityManager em, Faction faction)
        {
            bool adopted = view.State == 2 && !string.IsNullOrEmpty(view.SectId);

            if (view.PassiveIcon != null)
            {
                if (view.PassiveIcon.activeSelf != adopted) view.PassiveIcon.SetActive(adopted);
                if (adopted && view.PassiveGlyph != null)
                {
                    // Dimmed while the Temple is down: the passive is asleep and
                    // that should be readable at a glance, not only on hover.
                    bool live = SectQuery.HasStandingTemple(em, faction);
                    var c = live ? GlyphLive : GlyphDormant;
                    if (view.PassiveGlyph.color != c) view.PassiveGlyph.color = c;
                }
            }

            if (view.Tier == null) return;
            int unlocked = adopted ? SectActivePowerHelper.UnlockedTier(em, faction, view.SectId) : 0;

            for (int t = 0; t < 3; t++)
            {
                var btn = view.Tier[t];
                if (btn == null) continue;
                if (btn.gameObject.activeSelf != adopted) btn.gameObject.SetActive(adopted);
                if (!adopted) continue;

                int tier = t + 1;
                bool owned = tier <= unlocked;
                bool ready = owned && SectActivePowerHelper.CanFire(em, faction, view.SectId, tier);
                if (btn.interactable != ready) btn.interactable = ready;

                if (btn.targetGraphic is Image img)
                {
                    Color c = !owned ? AbilityLocked : ready ? AbilityReady : AbilityCool;
                    if (img.color != c) img.color = c;
                }

                var lbl = view.TierLabel[t];
                if (lbl == null) continue;
                string txt;
                if (!owned) txt = "-";
                else
                {
                    float rem = SectActivePowerHelper.CooldownRemaining(em, faction, view.SectId, tier);
                    txt = rem > 0f ? Mathf.CeilToInt(rem).ToString() : tier.ToString();
                }
                if (lbl.text != txt) lbl.text = txt;
            }
        }

        private void ClickSlot(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            var view = _slots[index];

            if (view.State == 0)
            {
                _pickerSlotIndex = index;
                if (_picker != null)
                {
                    _picker.SetActive(true);
                    var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (world != null && world.IsCreated
                        && TryGetTemple(world.EntityManager, GameSettings.LocalPlayerFaction, out var temple))
                        RefreshPicker(world.EntityManager, GameSettings.LocalPlayerFaction, temple,
                            FactionReligionPointsHelper.GetBalance(world.EntityManager, GameSettings.LocalPlayerFaction));
                }
                return;
            }

            if (view.State == 2 && !string.IsNullOrEmpty(view.SectId))
                BeginCast(view.SectId);
        }

        private void RightClickSlot(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            var view = _slots[index];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            // MP: allocation must replicate — it halves the sect's power
            // cooldown, and a local-only toggle makes a later SectPower fire
            // on one peer and drop on the other. SP keeps the direct call for
            // the instant error message.
            bool has = SectActivePowerHelper.HasGlowAllocated(em, faction, view.SectId);
            if (GameSettings.IsMultiplayer)
            {
                TheWaningBorder.Core.Commands.CommandRouter.IssueSectGlowAlloc(
                    em, faction, view.SectId, allocate: !has);
            }
            else if (has)
                SectActivePowerHelper.DeallocateGlow(em, faction, view.SectId);
            else if (!SectActivePowerHelper.AllocateGlow(em, faction, view.SectId))
                PlayerNotificationSystem.NotifyError(Loc.T("No Glow stored in the Temple"));
        }

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

        private void RefreshPicker(EntityManager em, Faction faction, Entity temple, int rp)
        {
            byte culture = LookupCulture(em, faction);
            foreach (var row in _pickerRows)
            {
                if (row.Label == null) continue;

                string name = SectInfo.ShortName(row.SectId);
                string text;
                bool enabled = false;

                if (!SectConfig.IsImplemented(row.SectId))
                {
                    text = string.Format(Loc.T("{0} - coming soon"), name);
                }
                else
                {
                    var check = SectAdoption.CanAdopt(em, faction, row.SectId, out int cost);
                    bool materials = BuildCosts.TryGet(
                            SectConfig.ChapelIdFor(row.SectId), out var chapelCost)
                        && FactionEconomy.CanAfford(em, faction, chapelCost);

                    switch (check)
                    {
                        case SectAdoptionResult.Ok when materials:
                            text = string.Format(Loc.T("{0} - {1} RP"), name, cost);
                            enabled = true;
                            break;
                        case SectAdoptionResult.Ok:
                            text = string.Format(Loc.T("{0} - need materials"), name);
                            break;
                        case SectAdoptionResult.AlreadyAdopted:
                            text = string.Format(Loc.T("{0} - adopted"), name);
                            break;
                        case SectAdoptionResult.NotEnoughRP:
                            text = string.Format(Loc.T("{0} - need {1} RP (have {2})"),
                                name, cost, rp);
                            break;
                        case SectAdoptionResult.SlotsFull:
                            text = string.Format(Loc.T("{0} - no free slot"), name);
                            break;
                        default:
                            text = string.Format(Loc.T("{0} - unavailable"), name);
                            break;
                    }
                }

                if (row.Label.text != text) row.Label.text = text;
                if (row.Button != null && row.Button.interactable != enabled)
                    row.Button.interactable = enabled;
            }
        }

        private void ClickAdopt(string sectId)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            if (!TryGetTemple(em, faction, out var temple)) { ClosePicker(); return; }
            if (!BuildCosts.TryGet(SectConfig.ChapelIdFor(sectId), out var chapelCost)) return;

            // Validate only — the RP + material SPEND happens inside
            // SectAdoptionCommandDirect on every peer, alongside the slot
            // stamp (docs/Multiplayer_LAN_Readiness.md).
            var result = SectAdoption.ValidateAdoption(em, faction, sectId, chapelCost, temple);
            if (result == SectAdoptionResult.Ok)
            {
                CommandRouter.IssueSectAdoption(em, temple, sectId, _pickerSlotIndex,
                    ChapelBuildSeconds);
                ClosePicker();
                return;
            }

            PlayerNotificationSystem.NotifyError(result switch
            {
                SectAdoptionResult.NotEnoughRP => Loc.T("Not enough Religion Points"),
                SectAdoptionResult.SlotsFull => Loc.T("All chapel slots are in use"),
                SectAdoptionResult.AlreadyAdopted => Loc.T("Sect already adopted"),
                SectAdoptionResult.NotYetImplemented => Loc.T("This sect is coming soon"),
                _ => Loc.T("Cannot adopt this sect"),
            });
        }

        private void ClosePicker()
        {
            _pickerSlotIndex = -1;
            if (_picker != null && _picker.activeSelf) _picker.SetActive(false);
        }

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
