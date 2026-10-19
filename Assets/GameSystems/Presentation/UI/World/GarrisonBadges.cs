// GarrisonBadges.cs
// WHERE THE ARCHERS ARE (docs/Design/Age_1_Alanthor.md § Watch Tower,
// 2026-10-20). A garrisoned unit has no body on the map — it is inside the
// building — so every garrisonable building that holds anyone shows a small
// "n/max" badge over it, always, at any zoom.
//
// Which buildings: anything with garrison slots (WallGarrisonSlot — the Watch
// Tower today). Whose: your own and your allies' whenever one has a man in it
// (and your own when selected, even empty, so you can read its capacity); an
// ENEMY building only while you can see it (its view is active — the fog
// already decided), and only when it is manned. Occupancy is read from the
// slots, the same source WallGarrison.OccupantCount uses.
//
// Presentation only: reads the world, writes nothing.

using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Ingame;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.World
{
    public sealed class GarrisonBadges : MonoBehaviour
    {
        private const float CollectInterval = 0.2f;
        private const float BadgeWidth = 38f;
        private const float BadgeHeight = 17f;
        /// <summary>Metres above the building's base the badge is anchored —
        /// the anchor the building health bar uses.</summary>
        private const float Lift = 3.2f;
        /// <summary>Pixels the badge sits above that anchor, clear of the bar.</summary>
        private const float PixelRaise = 16f;
        /// <summary>Above the node bars (40) and the takeover bars (45),
        /// under the health bars (50).</summary>
        private const int CanvasSortingOrder = 48;

        private static readonly ComponentType[] QT_Garrisons =
        {
            ComponentType.ReadOnly<WallGarrisonSlot>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        private static CachedEntityQuery QC_Garrisons;

        private sealed class Widget
        {
            public RectTransform Root;
            public Image Back;
            public TMP_Text Label;
            public int ShownCount = -1;
            public int ShownMax = -1;
        }

        private struct Entry
        {
            public Vector3 Anchor;
            public int Count;
            public int Max;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Widget> _pool = new List<Widget>();
        private readonly HashSet<Entity> _selected = new HashSet<Entity>();
        private RectTransform _canvasRoot;
        private float _next;

        private void Awake()
        {
            var go = new GameObject("GarrisonBadges-Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            go.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _canvasRoot = (RectTransform)go.transform;
        }

        private void LateUpdate()
        {
            if (_canvasRoot == null) return;
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + CollectInterval;
                Collect();
            }
            Place();
        }

        /// <summary>Every garrisonable building whose badge the viewer may see.</summary>
        private void Collect()
        {
            _entries.Clear();
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var q = QC_Garrisons.Get(em, QT_Garrisons);
            if (q.IsEmptyIgnoreFilter) return;

            _selected.Clear();
            var sel = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (sel != null) for (int i = 0; i < sel.Count; i++) _selected.Add(sel[i]);

            var views = TheWaningBorder.Rendering.EntityViewManager.Instance;
            bool spectating = GameSettings.IsSpectating;
            var viewer = GameSettings.ViewFactionOrLocal;

            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var slots = em.GetBuffer<WallGarrisonSlot>(e, true);
                int max = slots.Length;
                if (max <= 0) continue;
                int count = 0;
                for (int s = 0; s < slots.Length; s++)
                    if (slots[s].Occupant != Entity.Null && em.Exists(slots[s].Occupant)) count++;

                bool friendly = !spectating && !Alliances.AreHostile(viewer, facs[i].Value);
                bool own = !spectating && facs[i].Value == viewer;
                bool want = count > 0 || (own && _selected.Contains(e));
                if (!want) continue;
                // An enemy's (or, spectating, anyone's) garrison is intel:
                // only while its building is in sight.
                if (!friendly)
                {
                    if (views == null || !views.TryGetView(e, out var go) || go == null || !go.activeInHierarchy)
                        continue;
                }

                var p = xfs[i].Position;
                _entries.Add(new Entry
                {
                    Anchor = new Vector3(p.x, p.y + Lift, p.z),
                    Count = count,
                    Max = max,
                });
            }
        }

        /// <summary>Every frame: project the badges and lay the pool on them.</summary>
        private void Place()
        {
            var cam = Camera.main;
            int used = 0;
            if (cam != null)
            {
                float sw = Screen.width, sh = Screen.height, m = BadgeWidth;
                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    Vector3 sp = cam.WorldToScreenPoint(e.Anchor);
                    if (sp.z <= 0f || sp.x < -m || sp.x > sw + m || sp.y < -m || sp.y > sh + m) continue;

                    var w = used < _pool.Count ? _pool[used] : Build();
                    used++;
                    if (!w.Root.gameObject.activeSelf) w.Root.gameObject.SetActive(true);
                    w.Root.anchoredPosition = new Vector2(sp.x, sp.y + PixelRaise);
                    if (w.ShownCount != e.Count || w.ShownMax != e.Max)
                    {
                        w.ShownCount = e.Count;
                        w.ShownMax = e.Max;
                        w.Label.text = e.Count + "/" + e.Max;
                        // A full building reads in the accent gold.
                        w.Label.color = e.Count >= e.Max ? WorldOverlayPalette.Accent : WorldOverlayPalette.Inlay;
                    }
                }
            }
            for (int i = used; i < _pool.Count; i++)
                if (_pool[i].Root.gameObject.activeSelf) _pool[i].Root.gameObject.SetActive(false);
        }

        private Widget Build()
        {
            var go = new GameObject("GarrisonBadge", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_canvasRoot, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(BadgeWidth, BadgeHeight);

            var edge = Img(rt, "Edge", WorldOverlayPalette.AccentSoft);
            Stretch(edge.rectTransform, Vector2.zero, Vector2.zero);
            var back = Img(rt, "Back", WorldOverlayPalette.PanelDeep);
            Stretch(back.rectTransform, Vector2.one, -Vector2.one);

            var label = GameUIKit.Text(rt, "Count", "", 13f, WorldOverlayPalette.Inlay,
                TextAlignmentOptions.Center, wrap: false);
            Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;

            var w = new Widget { Root = rt, Back = back, Label = label };
            _pool.Add(w);
            return w;
        }

        private static Image Img(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
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
    }
}
