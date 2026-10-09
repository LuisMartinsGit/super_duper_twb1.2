// TerritoryClaimBars.cs
// TERRITORY TAKEOVERS, OVER THE TERRITORY (docs/Design/Territory_Claims.md §2).
// Ground changes hands by standing on it, over tens of seconds, so every
// takeover the local player is part of gets a large Synty-framed bar floating
// over that territory's centre — a health bar for the ground itself:
//
//   Claiming North Ford      your colour, filling      you are taking free ground
//   Taking   Red's Vale      THEIR colour, draining    your army is draining theirs
//   Locked   Red's Vale      their colour, full        raze its extractors first
//   Losing   Home Vale       your colour, draining     someone is draining yours
//   Decaying East Field      your colour, draining     nobody holds it any more
//   Contested West Ridge     dimmed, frozen            hostiles share it
//
// Replaces the minimap strip (2026-09-30): three 36 px rows above the minimap
// were unreadable, and they told you WHICH territory only by name.
//
// The bar is the meter itself (TerritoryOwnership.ValueOf), so what fills
// here is exactly what the claim system counts. Only takeovers the local
// player is part of are drawn, so it reveals nothing the fog hides.
// Presentation only.
//
// The anchor is the territory's CENTRE (TerritoryCentres — shared with the
// IncomeOverlay), not its Voronoi seed, which can sit near an edge.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.World
{
    public sealed class TerritoryClaimBars : MonoBehaviour
    {
        private const float UpdateInterval = 0.2f;
        /// <summary>Bar size at 1080p; scaled with the screen height.</summary>
        private const float BarWidth = 340f;
        private const float BarHeight = 56f;
        private const float ReferenceHeight = 1080f;
        /// <summary>Under the floating health bars (50).</summary>
        private const int CanvasSortingOrder = 45;

        /// <summary>The frame sprite is 512 x 256 with 200 px end caps; these
        /// are where its bar channel sits, in the sprite's own pixels, so the
        /// fill lands between the rails at any bar height.</summary>
        private const float FrameSpriteHeight = 256f;
        private const float ChannelInsetX = 150f;
        private const float ChannelInsetY = 72f;

        private static readonly Color CurseColor = new Color(0.62f, 0.35f, 0.85f);
        private static readonly Color TrackColor = new Color(0f, 0f, 0f, 0.6f);

        private sealed class Bar
        {
            public RectTransform Root;
            public Image Fill;
            public TMP_Text Label;
        }

        private struct Entry
        {
            public int Territory;
            public string Text;
            public float Value;
            public Color Color;
        }

        private readonly Dictionary<int, Bar> _bars = new Dictionary<int, Bar>();
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<int> _live = new HashSet<int>();
        private Sprite _frame, _fill;
        private RectTransform _canvasRoot;
        private float _next;

        public void Init(Sprite frame, Sprite fill)
        {
            _frame = frame;
            _fill = fill;

            var go = new GameObject("TerritoryClaimBars-Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            _canvasRoot = (RectTransform)go.transform;
        }

        private void LateUpdate()
        {
            if (_canvasRoot == null) return;

            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + UpdateInterval;
                Collect();
                Sync();
            }
            Place();
        }

        // ── What to show ────────────────────────────────────────────────

        /// <summary>Every takeover the local player is part of.</summary>
        private void Collect()
        {
            _entries.Clear();
            if (!RegionMap.Ready || !TerritoryOwnership.Ready || GameSettings.IsSpectating) return;

            int me = (int)GameSettings.LocalPlayerFaction;
            for (int t = 0; t < RegionMap.Count; t++)
            {
                int holder = TerritoryOwnership.HolderOf(t);
                int challenger = TerritoryOwnership.ChallengerOf(t);
                bool claimed = TerritoryOwnership.IsClaimed(t);
                bool locked = TerritoryOwnership.IsLocked(t);
                bool contested = TerritoryOwnership.IsContested(t);
                float value = TerritoryOwnership.ValueOf(t);
                string name = RegionMap.NameOf(t);
                if (string.IsNullOrEmpty(name)) name = Loc.T("Territory") + " " + t;
                int pct = Mathf.FloorToInt(value);

                if (holder == me)
                {
                    if (contested)
                        Add(t, string.Format(Loc.T("Contested {0}"), name), value, Dim(ColorOf(me)));
                    else if (claimed && challenger != TerritoryOwnership.Natural && !locked)
                        Add(t, string.Format(Loc.T("Losing {0}  {1}%"), name, pct), value, ColorOf(me));
                    else if (!claimed && value > 0f)
                        Add(t, string.Format(Loc.T("Claiming {0}  {1}%"), name, pct), value, ColorOf(me));
                    else if (claimed && value < 99.5f && !locked)
                        Add(t, string.Format(Loc.T("Decaying {0}  {1}%"), name, pct), value, ColorOf(me));
                }
                else if (challenger == me)
                {
                    if (locked)
                        Add(t, string.Format(Loc.T("{0} is locked — raze its extractors"), name),
                            100f, Dim(ColorOf(holder)));
                    else
                        Add(t, string.Format(Loc.T("Taking {0}  {1}%"), name, pct), value, ColorOf(holder));
                }
            }
        }

        private void Add(int territory, string text, float value, Color color)
            => _entries.Add(new Entry { Territory = territory, Text = text, Value = value, Color = color });

        /// <summary>One bar per listed territory: create, refresh, retire.</summary>
        private void Sync()
        {
            _live.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                _live.Add(e.Territory);
                if (!_bars.TryGetValue(e.Territory, out var bar))
                    _bars[e.Territory] = bar = BuildBar(e.Territory);
                if (!bar.Root.gameObject.activeSelf) bar.Root.gameObject.SetActive(true);
                bar.Fill.fillAmount = Mathf.Clamp01(e.Value / 100f);
                if (bar.Fill.color != e.Color) bar.Fill.color = e.Color;
                if (bar.Label.text != e.Text) bar.Label.text = e.Text;
            }
            foreach (var kv in _bars)
                if (!_live.Contains(kv.Key) && kv.Value.Root.gameObject.activeSelf)
                    kv.Value.Root.gameObject.SetActive(false);
        }

        // ── Where to show it ────────────────────────────────────────────

        /// <summary>Every frame: project each live bar's territory centre.</summary>
        private void Place()
        {
            var cam = Camera.main;
            if (cam == null || _bars.Count == 0) return;

            float scale = Mathf.Max(0.5f, Screen.height / ReferenceHeight);
            foreach (var kv in _bars)
            {
                var root = kv.Value.Root;
                if (!root.gameObject.activeSelf) continue;
                int t = kv.Key;
                if (!TerritoryCentres.TryGet(t, out var centre)) continue;

                Vector3 sp = cam.WorldToScreenPoint(centre);
                bool visible = sp.z > 0f && sp.x > -BarWidth && sp.x < Screen.width + BarWidth
                               && sp.y > -BarHeight && sp.y < Screen.height + BarHeight;
                var cg = root.GetComponent<CanvasGroup>();
                cg.alpha = visible ? 1f : 0f;
                if (!visible) continue;
                root.position = new Vector3(sp.x, sp.y, 0f);
                root.localScale = Vector3.one * scale;
            }
        }

        // ── Building a bar ──────────────────────────────────────────────

        private Bar BuildBar(int territory)
        {
            var go = new GameObject("ClaimBar_" + territory, typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_canvasRoot, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(BarWidth, BarHeight);
            var cg = go.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            float s = BarHeight / FrameSpriteHeight;
            var channelMin = new Vector2(ChannelInsetX * s, ChannelInsetY * s);
            var channelMax = new Vector2(-ChannelInsetX * s, -ChannelInsetY * s);

            // Track, then fill, both in the frame's channel; the frame on top.
            var track = MakeImage(rt, "Track", _fill, TrackColor);
            Stretch(track.rectTransform, channelMin, channelMax);

            var fill = MakeImage(rt, "Fill", _fill, Color.white);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            Stretch(fill.rectTransform, channelMin, channelMax);

            var frame = MakeImage(rt, "Frame", _frame, Color.white);
            frame.type = Image.Type.Sliced;
            // The 200 px end caps scale with the bar, not with the sprite.
            frame.pixelsPerUnitMultiplier = FrameSpriteHeight / BarHeight;
            Stretch(frame.rectTransform, Vector2.zero, Vector2.zero);

            // The name and percentage sit ABOVE the bar, large, so they read
            // at a glance over busy terrain.
            var label = GameUIKit.Text(rt, "Label", "", 22f, Color.white,
                TextAlignmentOptions.Center, wrap: false);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, -4f);
            lrt.sizeDelta = new Vector2(120f, 30f);
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(0, 0, 0, 230);
            label.raycastTarget = false;

            go.SetActive(false);
            return new Bar { Root = rt, Fill = fill, Label = label };
        }

        private static Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static Color ColorOf(int side)
        {
            if (side == TerritoryOwnership.Curse) return CurseColor;
            if (side >= 0 && side < 8) return FactionColors.Get((Faction)side);
            return Color.gray;
        }

        private static Color Dim(Color c) => Color.Lerp(c, Color.gray, 0.55f);
    }
}
