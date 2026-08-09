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
// Adoption spends RP + chapel materials at click time
// (SectAdoption.TryStartAdoption) and replicates the slot stamp via
// CommandRouter.IssueSectAdoption so the chapel rises on every peer.
// Location: Assets/Scripts/UI/GameUI/ReligionPanelBinder.cs

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
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

        private void Awake()
        {
            _rp = FindLabel(transform, "RP");
            _templeInfo = FindLabel(transform, "TempleInfo");

            for (int i = 0; i < 6; i++)
            {
                var node = FindDeep(transform, "Slot" + (i + 1));
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
                var relay = node.gameObject.AddComponent<UiClickRelay>();
                relay.OnRightClick = () => RightClickSlot(slotIndex);
                _slots.Add(view);
            }

            var picker = FindDeep(transform, "Picker");
            _picker = picker != null ? picker.gameObject : null;
            if (_picker != null)
            {
                for (int i = 0; i < SectConfig.SectCount; i++)
                {
                    var node = FindDeep(picker, "Sect" + (i + 1));
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
                    _pickerRows.Add(row);
                }
                var close = FindDeep(picker, "PickerClose");
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
                string text = "Religion Points: " + rp;
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
                text = $"Temple Lv {level} - upgrading {(int)(pct * 100f)}%";
            }
            else
            {
                text = $"Temple Lv {level} - power tier {Mathf.Clamp(level, 1, 3)}";
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
                    SetSlot(view, "No slot", false);
                    continue;
                }

                byte state = states[i];
                if (state == 0)
                {
                    view.SectId = null;
                    view.State = 0;
                    SetSlot(view, "Adopt a sect", true);
                }
                else if (state == 1)
                {
                    view.SectId = sects[i];
                    view.State = 1;
                    SetSlot(view, $"{SectInfo.ShortName(sects[i])} chapel - {progress[i]}%", false);
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

                    string line1 = SectInfo.ShortName(sectId) + "  Tier " + unlocked
                        + (glow ? "  [Glow]" : "");
                    string line2 = ready
                        ? "Cast: " + SectInfo.ActiveName(sectId, readyTier)
                            + (readyTier < unlocked ? $" (T{readyTier})" : "")
                        : $"Ready in {Mathf.CeilToInt(soonest)}s";
                    SetSlot(view, line1 + "\n" + line2, ready);
                }
            }
        }

        private static void SetSlot(SlotView view, string text, bool interactable)
        {
            if (view.Label.text != text) view.Label.text = text;
            if (view.Button != null && view.Button.interactable != interactable)
                view.Button.interactable = interactable;
        }

        // ── Slot interaction ───────────────────────────────────────────────

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

            if (SectActivePowerHelper.HasGlowAllocated(em, faction, view.SectId))
                SectActivePowerHelper.DeallocateGlow(em, faction, view.SectId);
            else if (!SectActivePowerHelper.AllocateGlow(em, faction, view.SectId))
                PlayerNotificationSystem.NotifyError("No Glow stored in the Temple");
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
                SectActivePowerHelper.Fire(w.EntityManager, faction, sectId, tier, target);
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
                    text = name + " - coming soon";
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
                            text = $"{name} - {cost} RP";
                            enabled = true;
                            break;
                        case SectAdoptionResult.Ok:
                            text = $"{name} - need materials";
                            break;
                        case SectAdoptionResult.AlreadyAdopted:
                            text = name + " - adopted";
                            break;
                        case SectAdoptionResult.NotEnoughRP:
                            text = $"{name} - need {cost} RP (have {rp})";
                            break;
                        case SectAdoptionResult.SlotsFull:
                            text = name + " - no free slot";
                            break;
                        default:
                            text = name + " - unavailable";
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

            var result = SectAdoption.TryStartAdoption(em, faction, sectId, chapelCost, temple);
            if (result == SectAdoptionResult.Ok)
            {
                // RP + materials are spent; replicate the slot stamp so the
                // chapel rises on every peer (lockstep).
                CommandRouter.IssueSectAdoption(em, temple, sectId, _pickerSlotIndex,
                    ChapelBuildSeconds);
                ClosePicker();
                return;
            }

            PlayerNotificationSystem.NotifyError(result switch
            {
                SectAdoptionResult.NotEnoughRP => "Not enough Religion Points",
                SectAdoptionResult.SlotsFull => "All chapel slots are in use",
                SectAdoptionResult.AlreadyAdopted => "Sect already adopted",
                SectAdoptionResult.NotYetImplemented => "This sect is coming soon",
                _ => "Cannot adopt this sect",
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
            var t = FindDeep(root, node);
            return t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
