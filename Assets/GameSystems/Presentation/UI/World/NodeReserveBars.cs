// NodeReserveBars.cs
// WHAT IS LEFT IN EACH NODE (docs/Design/Territory_Claims.md § 11.3,
// 2026-10-18): every resource node carries a finite reserve and collapses,
// with the buildings working it, when it is spent. So every node shows what is
// left — a small bar in the resource's colour and the amount as a number —
// wherever the player can see the node.
//
// Reads NodeReserve (Remaining / Initial), the same value TerritoryIncomeSystem
// draws from, so the bar empties exactly as the node does. Visibility is the
// node's view: FogVisibilitySyncSystem deactivates the view of anything out of
// vision, so a node the fog hides shows no bar. The number is dropped when the
// camera is so far out that a node is a few pixels across (the bars stay).
// Presentation only.

using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.UI.Ingame;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.World
{
    public sealed class NodeReserveBars : MonoBehaviour
    {
        private const float CollectInterval = 0.25f;
        private const float BarWidth = 46f;
        private const float BarHeight = 5f;
        /// <summary>Metres above the node's base the bar floats.</summary>
        private const float Lift = 3.4f;
        /// <summary>Under the takeover bars (45) and the health bars (50).</summary>
        private const int CanvasSortingOrder = 40;
        /// <summary>Below this many pixels per metre at the node the number
        /// is hidden: the camera is too far out to read dozens of them.</summary>
        private const float LabelMinPixelsPerMetre = 2.2f;

        private static readonly Color SupplyColor = new Color(0.55f, 0.78f, 0.36f);
        private static readonly Color IronColor = new Color(0.70f, 0.74f, 0.80f);
        /// <summary>Cyan = veilstone (Art_Direction.md).</summary>
        private static readonly Color VeilstoneColor = new Color(0.30f, 0.86f, 0.95f);
        private static readonly Color TrackColor = new Color(0f, 0f, 0f, 0.65f);

        private static readonly ComponentType[][] QT_Nodes =
        {
            new[] { ComponentType.ReadOnly<SupplyNodeTag>(), ComponentType.ReadOnly<NodeReserve>(), ComponentType.ReadOnly<LocalTransform>() },
            new[] { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<NodeReserve>(), ComponentType.ReadOnly<LocalTransform>() },
            new[] { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<NodeReserve>(), ComponentType.ReadOnly<LocalTransform>() },
        };
        private static readonly CachedEntityQuery[] QC_Nodes = new CachedEntityQuery[3];

        private sealed class Widget
        {
            public RectTransform Root;
            public Image Fill;
            public TMP_Text Label;
            public int Shown = -1;
        }

        private struct Entry
        {
            public Entity Node;
            public Vector3 Anchor;
            public float Fill;
            public int Remaining;
            public Color Color;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Widget> _pool = new List<Widget>();
        private RectTransform _canvasRoot;
        private float _next;

        private void Awake()
        {
            var go = new GameObject("NodeReserveBars-Canvas", typeof(Canvas), typeof(CanvasScaler));
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

        /// <summary>Every visible node and what is left in it.</summary>
        private void Collect()
        {
            _entries.Clear();
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var views = TheWaningBorder.Rendering.EntityViewManager.Instance;
            if (views == null) return;
            for (int kind = 0; kind < 3; kind++)
            {
                var q = QC_Nodes[kind].Get(em, QT_Nodes[kind]);
                if (q.IsEmptyIgnoreFilter) continue;
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var res = q.ToComponentDataArray<NodeReserve>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                Color col = kind == 0 ? SupplyColor : kind == 1 ? IronColor : VeilstoneColor;
                for (int i = 0; i < ents.Length; i++)
                {
                    if (res[i].Initial <= 0f) continue;
                    if (!views.TryGetView(ents[i], out var go) || go == null || !go.activeInHierarchy) continue;
                    var p = xfs[i].Position;
                    _entries.Add(new Entry
                    {
                        Node = ents[i],
                        Anchor = new Vector3(p.x, p.y + Lift, p.z),
                        Fill = Mathf.Clamp01(res[i].Remaining / res[i].Initial),
                        Remaining = Mathf.CeilToInt(Mathf.Max(0f, res[i].Remaining)),
                        Color = col,
                    });
                }
            }
        }

        /// <summary>Every frame: project the visible nodes and lay the pool on them.</summary>
        private void Place()
        {
            var cam = Camera.main;
            int used = 0;
            if (cam != null)
            {
                float sw = Screen.width, sh = Screen.height, m = BarWidth;
                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    Vector3 sp = cam.WorldToScreenPoint(e.Anchor);
                    if (sp.z <= 0f || sp.x < -m || sp.x > sw + m || sp.y < -m || sp.y > sh + m) continue;
                    // pixels per metre at the node: is the number readable?
                    Vector3 sp2 = cam.WorldToScreenPoint(e.Anchor + cam.transform.right);
                    bool label = (sp2 - sp).magnitude >= LabelMinPixelsPerMetre;

                    var w = used < _pool.Count ? _pool[used] : Build();
                    used++;
                    if (!w.Root.gameObject.activeSelf) w.Root.gameObject.SetActive(true);
                    w.Root.anchoredPosition = new Vector2(sp.x, sp.y);
                    w.Fill.fillAmount = e.Fill;
                    if (w.Fill.color != e.Color) w.Fill.color = e.Color;
                    if (w.Label.enabled != label) w.Label.enabled = label;
                    if (label && w.Shown != e.Remaining)
                    {
                        w.Shown = e.Remaining;
                        w.Label.text = e.Remaining.ToString("N0");
                    }
                }
            }
            for (int i = used; i < _pool.Count; i++)
                if (_pool[i].Root.gameObject.activeSelf) _pool[i].Root.gameObject.SetActive(false);
        }

        private Widget Build()
        {
            var go = new GameObject("NodeReserve", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_canvasRoot, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(BarWidth + 2f, BarHeight + 2f);

            var track = Img(rt, "Track", TrackColor);
            Stretch(track.rectTransform, Vector2.zero, Vector2.zero);
            var fill = Img(rt, "Fill", Color.white);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.sprite = WhiteSprite();
            Stretch(fill.rectTransform, Vector2.one, -Vector2.one);

            var label = GameUIKit.Text(rt, "Amount", "", 13f, Color.white, TextAlignmentOptions.Center, wrap: false);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 1f);
            lrt.sizeDelta = new Vector2(30f, 16f);
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.3f;
            label.outlineColor = new Color32(0, 0, 0, 230);
            label.raycastTarget = false;

            var w = new Widget { Root = rt, Fill = fill, Label = label };
            _pool.Add(w);
            return w;
        }

        private static Sprite _white;
        /// <summary>A Filled Image needs a sprite to fill.</summary>
        private static Sprite WhiteSprite()
        {
            if (_white != null) return _white;
            var tex = Texture2D.whiteTexture;
            _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _white;
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
