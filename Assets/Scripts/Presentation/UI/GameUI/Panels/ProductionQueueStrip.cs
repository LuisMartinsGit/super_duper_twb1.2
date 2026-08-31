// ProductionQueueStrip.cs
// The training / research queue readout for the selected building.
//
// The code-built ActionsPanelBinder always had one, but it retires itself the
// moment the AUTHORED 3x5 actions grid is bound (ActionsPanelPrefabBinder.
// Active) — which is every ordinary barracks, range, stable and workshop. The
// result was a HUD where you could queue five units and get no confirmation
// that anything was happening: no progress, no queue, no way to cancel a
// misclick. This strip is the missing half, mounted above the authored grid.
//
// Shows, top to bottom:
//   "Training Archer — 4.2s"  + a filling bar
//   six queue chips (icon or 3-letter fallback); chip 0 is the item in
//   production and is tinted gold. Right-click any PENDING chip to cancel it
//   and refund (CancelTrainCommandHelper); the in-production item is not
//   cancellable, matching the code-built panel.
//   "Researching Fletching — 12s" + its own bar
// Every chip carries a hover tooltip.

using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.GameUI
{
    internal sealed class ProductionQueueStrip
    {
        private const int Chips = 6;
        private const float ChipSize = 96f;
        private const float ChipGap = 8f;
        private const float StripHeight = 236f;

        private static readonly Color ChipEmpty    = new Color(0.05f, 0.06f, 0.13f, 0.85f);
        private static readonly Color ChipPending  = new Color(0.10f, 0.13f, 0.24f, 1f);
        private static readonly Color ChipProducing= new Color(0.83f, 0.66f, 0.26f, 0.55f);

        private sealed class Chip
        {
            public GameObject Root;
            public Image Bg;
            public Image Icon;
            public TMP_Text Label;
            public int Index;
            public bool Cancellable;
            public string UnitId;
        }

        private sealed class Bar
        {
            public GameObject Root;
            public RectTransform Fill;
            public TMP_Text Label;

            public void Set(bool on, string text, float pct, Color color)
            {
                if (Root.activeSelf != on) Root.SetActive(on);
                if (!on) return;
                Fill.anchorMax = new Vector2(Mathf.Clamp01(pct), 1f);
                if (Fill.TryGetComponent(out Image img)) img.color = color;
                if (Label.text != text) Label.text = text;
            }
        }

        private readonly RectTransform _root;
        private readonly Chip[] _chips = new Chip[Chips];
        private readonly Bar _trainBar;
        private readonly Bar _researchBar;
        private readonly System.Func<string, Sprite> _iconFor;

        private Entity _building;

        public ProductionQueueStrip(Transform parent, System.Func<string, Sprite> iconFor)
        {
            _iconFor = iconFor;

            // Sits flush on top of the actions panel, growing upward.
            _root = GameUIKit.Rect(parent, "productionQueue");
            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, 10f);
            _root.sizeDelta = new Vector2(0f, StripHeight);
            GameUIKit.PanelChrome(_root);

            var stack = GameUIKit.Rect(_root, "content");
            GameUIKit.Stretch(stack);
            var v = GameUIKit.VStack(stack, 12f, 8f);
            v.childForceExpandHeight = false;

            _trainBar = MakeBar(stack, "trainBar");

            var row = GameUIKit.Rect(stack, "chips");
            GameUIKit.FixHeight(row.gameObject, ChipSize);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ChipGap;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            for (int i = 0; i < Chips; i++) _chips[i] = MakeChip(row, i);

            _researchBar = MakeBar(stack, "researchBar");

            _root.gameObject.SetActive(false);
        }

        // ── Construction ───────────────────────────────────────────────────

        private Bar MakeBar(Transform parent, string name)
        {
            var bar = new Bar();
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, 34f);

            var bg = GameUIKit.Image(rt, "bg", GameUIKit.BarBg);
            GameUIKit.Stretch(bg.rectTransform);
            var fill = GameUIKit.Image(rt, "fill", GameUIKit.BarGold);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            var label = GameUIKit.Text(rt, "label", "", 22f, Color.white,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(label.rectTransform);

            bar.Root = rt.gameObject;
            bar.Fill = fill.rectTransform;
            bar.Label = label;
            rt.gameObject.SetActive(false);
            return bar;
        }

        private Chip MakeChip(Transform parent, int index)
        {
            var chip = new Chip { Index = index };
            var rt = GameUIKit.Rect(parent, "chip" + index);
            rt.sizeDelta = new Vector2(ChipSize, ChipSize);

            chip.Bg = GameUIKit.Image(rt, "bg", ChipEmpty, raycast: true);
            GameUIKit.Stretch(chip.Bg.rectTransform);

            chip.Icon = GameUIKit.Image(rt, "icon", Color.white);
            chip.Icon.preserveAspect = true;
            var irt = chip.Icon.rectTransform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(8f, 8f);
            irt.offsetMax = new Vector2(-8f, -8f);
            chip.Icon.enabled = false;

            chip.Label = GameUIKit.Text(rt, "label", "", 22f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(chip.Label.rectTransform);

            var relay = UITooltip.Relay(chip.Bg.gameObject);
            relay.OnRightClick = () => Cancel(chip);
            UITooltip.Bind(chip.Bg.gameObject, () => Tooltip(chip));

            chip.Root = rt.gameObject;
            return chip;
        }

        private static string Tooltip(Chip chip)
        {
            if (string.IsNullOrEmpty(chip.UnitId)) return null;
            string name = EntityInfoExtractor.GetUnitDisplayName(chip.UnitId);
            return chip.Cancellable
                ? $"<b>{name}</b>\n" + Loc.T("Queued — right-click to cancel and refund")
                : $"<b>{name}</b>\n" + Loc.T("In production");
        }

        private void Cancel(Chip chip)
        {
            if (!chip.Cancellable) return;
            // Fully qualified: bare "World" binds to the TheWaningBorder.World
            // namespace, not Unity.Entities.World.
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (_building == Entity.Null || !em.Exists(_building)) return;
            // Through the router, not the helper: the refund must land on
            // every peer via the CancelTrain lockstep opcode, mirroring the
            // spend that now lives in the train executor
            // (docs/Multiplayer_LAN_Readiness.md).
            TheWaningBorder.Core.Commands.CommandRouter.IssueCancelTrain(
                em, _building, chip.Index,
                TheWaningBorder.Core.Commands.CommandSource.LocalPlayer);
        }

        // ── Render ─────────────────────────────────────────────────────────

        public void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Paint the strip for <paramref name="building"/>. Hides itself
        /// whenever there is NOTHING researching and nothing queued — the
        /// old behaviour ("an idle building still shows its empty chips")
        /// parked a dead six-slot panel over the actions grid on every
        /// research-capable selection, and read as leftover chrome
        /// (2026-08-31 screenshot). The strip now exists only while it has
        /// something to say; queueing a tech brings it up instantly.
        /// </summary>
        public void Render(EntityManager em, Entity building, in EntityActionInfo info)
        {
            _building = building;

            // Training display moved to the roster-slot area
            // (UnitRosterPanelBinder shows all 16 queue slots with the
            // in-production progress bar) — this strip is research-only now.
            _trainBar.Set(false, "", 0f, GameUIKit.BarGold);

            bool hasResearch = info.ResearchState.HasValue;
            if (!hasResearch) { Hide(); return; }

            // Research queue chips. Queueing a tech REMOVES its button from
            // the actions grid, so without these chips the player paid, the
            // button vanished and nothing anywhere showed the item.
            var rq = info.ResearchState.Value;

            var names = new string[Chips];
            int idx = 0;
            if (rq.IsResearching && !string.IsNullOrEmpty(rq.CurrentTechName))
                names[idx++] = rq.CurrentTechName;
            if (rq.Queue != null)
                for (int i = 0; i < rq.Queue.Length && idx < Chips; i++, idx++)
                    names[idx] = rq.Queue[i];

            // Empty queue, nothing researching — no ghost panel.
            if (idx == 0 && !rq.IsResearching) { Hide(); return; }

            for (int i = 0; i < Chips; i++)
            {
                var chip = _chips[i];
                bool researching = i == 0 && rq.IsResearching;
                bool occupied = names[i] != null;

                // Techs are not units: no unit id, no unit icon, and no
                // cancel path wired — a cancellable chip here would offer
                // an action that does nothing.
                chip.UnitId = null;
                chip.Cancellable = false;
                chip.Bg.color = !occupied ? ChipEmpty
                    : researching ? ChipProducing : ChipPending;
                chip.Icon.enabled = false;

                string caption = occupied ? Short(names[i]) : "";
                if (chip.Label.text != caption) chip.Label.text = caption;
            }

            if (!_chips[0].Root.transform.parent.gameObject.activeSelf)
                _chips[0].Root.transform.parent.gameObject.SetActive(true);

            _researchBar.Set(rq.IsResearching,
                rq.IsResearching
                    ? string.Format(Loc.T("Researching {0}   {1:F1}s"),
                        rq.CurrentTechName, rq.TimeRemaining)
                    : "",
                rq.Progress, GameUIKit.BarBlue);

            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        private static string Short(string s) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length > 3 ? s.Substring(0, 3) : s);
    }
}
