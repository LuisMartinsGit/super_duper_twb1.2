// MusterMapView.cs
// The map stage of the Muster Rolls screen: a clipped viewport holding a
// content rect laid out in WORLD METRES (1 unit = 1 m, centred on the world
// bounds), scaled to fit the viewport times the zoom. Inside it, bottom to
// top: the map's lobby picture (framed on the terrain bounds, as the lobby
// bakes it), a dimming wash, the territory raster tinted by owner at the
// playhead (a point-filtered Texture2D rebuilt only when ownership changed),
// region names, and the MusterMapOverlay mesh.
//
// Mouse wheel zooms about the cursor, drag pans. Laying the content out in
// metres means nothing inside has to be re-placed when the viewport resizes
// or the zoom moves -- only the content's scale and offset change.

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TheWaningBorder.Core.Diagnostics.MatchRecording;

namespace TheWaningBorder.UI.Ingame
{
    internal sealed class MusterMapView : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
    {
        private RectTransform _viewport;
        private RectTransform _content;
        private RawImage _thumb;
        private RawImage _terrImage;
        private Texture2D _terrTex;
        private Color32[] _terrPx;
        private bool[] _terrEdge;
        private int[] _owners;
        private int _ownerStamp = -1;
        private MusterMapOverlay _overlay;
        private RectTransform _labelRoot;
        private readonly System.Collections.Generic.List<RectTransform> _labels =
            new System.Collections.Generic.List<RectTransform>();

        private MatchRecord _rec;
        private MusterRollsPanelConfig _cfg;
        private Vector2 _span = Vector2.one;
        private Vector2 _centre;
        private float _zoom = 1f;
        private float _fit = 1f;
        private float _labelZoom = -1f;

        public MusterMapOverlay Overlay => _overlay;

        public static MusterMapView Create(RectTransform parent)
        {
            var bg = GameUIKit.Image(parent, "mapStage", new Color(0.02f, 0.03f, 0.07f, 1f), raycast: true);
            bg.gameObject.AddComponent<RectMask2D>();
            var view = bg.gameObject.AddComponent<MusterMapView>();
            view._viewport = bg.rectTransform;

            view._content = GameUIKit.Rect(bg.rectTransform, "content");
            view._content.anchorMin = view._content.anchorMax = new Vector2(0.5f, 0.5f);
            view._content.pivot = new Vector2(0.5f, 0.5f);

            view._thumb = Raw(view._content, "thumbnail");
            GameUIKit.Stretch(view._thumb.rectTransform);
            var wash = GameUIKit.Image(view._content, "wash", new Color(0.05f, 0.07f, 0.13f, 0.30f));
            GameUIKit.Stretch(wash.rectTransform);

            view._terrImage = Raw(view._content, "territories");
            var tr = view._terrImage.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.pivot = Vector2.zero;

            view._labelRoot = GameUIKit.Rect(view._content, "regionNames");
            GameUIKit.Stretch(view._labelRoot);

            var ov = new GameObject("overlay", typeof(RectTransform));
            ov.transform.SetParent(view._content, false);
            GameUIKit.Stretch((RectTransform)ov.transform);
            view._overlay = ov.AddComponent<MusterMapOverlay>();
            view._overlay.raycastTarget = false;
            return view;
        }

        private static RawImage Raw(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RawImage>();
            r.raycastTarget = false;
            return r;
        }

        public void Bind(MatchRecord rec, MusterRollsPanelConfig cfg)
        {
            _rec = rec;
            _cfg = cfg;
            _zoom = 1f;
            _content.anchoredPosition = Vector2.zero;
            _ownerStamp = -1;
            _labelZoom = -1f;
            foreach (var l in _labels) if (l != null) Destroy(l.gameObject);
            _labels.Clear();
            if (rec == null) return;

            _span = new Vector2(Mathf.Max(1f, rec.WorldMax.x - rec.WorldMin.x),
                                Mathf.Max(1f, rec.WorldMax.y - rec.WorldMin.y));
            _centre = (rec.WorldMin + rec.WorldMax) * 0.5f;
            _content.sizeDelta = _span;

            _thumb.texture = rec.MapThumbnail;
            _thumb.enabled = rec.MapThumbnail != null;   // a null RawImage draws white
            _thumb.color = new Color(1f, 1f, 1f, cfg != null ? cfg.thumbnailAlpha : 0.85f);

            BuildTerritory();

            for (int i = 0; i < rec.Regions.Count; i++)
            {
                var r = rec.Regions[i];
                if (string.IsNullOrEmpty(r.Name)) continue;
                var t = GameUIKit.Text(_labelRoot, "region", r.Name.Replace('_', ' '), 22f,
                    new Color(0.92f, 0.90f, 0.84f, 0.55f), TextAlignmentOptions.Center, wrap: false);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(420f, 40f);
                rt.anchoredPosition = new Vector2(r.X - _centre.x, r.Z - _centre.y);
                _labels.Add(rt);
            }

            _overlay.Bind(rec, cfg);
        }

        private void BuildTerritory()
        {
            var rec = _rec;
            if (rec.TerritoryCells == null || rec.TerritoryW <= 0 || rec.TerritoryH <= 0)
            {
                _terrImage.enabled = false;
                return;
            }
            int w = rec.TerritoryW, h = rec.TerritoryH;
            if (_terrTex == null || _terrTex.width != w || _terrTex.height != h)
            {
                if (_terrTex != null) Destroy(_terrTex);
                _terrTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "MusterRollsTerritories",
                };
            }
            _terrPx = new Color32[w * h];
            _terrEdge = new bool[w * h];
            var cells = rec.TerritoryCells;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i, id = cells[k];
                    if (id < 0) continue;
                    bool edge = (i > 0 && cells[k - 1] != id) || (i < w - 1 && cells[k + 1] != id)
                             || (j > 0 && cells[k - w] != id) || (j < h - 1 && cells[k + w] != id);
                    _terrEdge[k] = edge;
                }
            _owners = new int[Mathf.Max(1, rec.Regions.Count)];

            _terrImage.texture = _terrTex;
            _terrImage.enabled = true;
            var tr = _terrImage.rectTransform;
            tr.anchoredPosition = new Vector2(rec.TerritoryX0 - _centre.x, rec.TerritoryZ0 - _centre.y);
            tr.sizeDelta = new Vector2(w * rec.TerritoryCell, h * rec.TerritoryCell);
        }

        private void PaintTerritory(float t)
        {
            var rec = _rec;
            if (_terrTex == null || rec.TerritoryCells == null) return;
            int stamp = rec.OwnerEventsUpTo(t);
            if (stamp == _ownerStamp) return;
            _ownerStamp = stamp;
            rec.FillOwnersAt(t, _owners);

            float fillA = _cfg != null ? _cfg.territoryFillAlpha : 0.3f;
            float edgeA = _cfg != null ? _cfg.territoryBorderAlpha : 0.9f;
            Color curse = _cfg != null ? _cfg.curseColor : new Color(0.62f, 0.3f, 0.85f);
            var cells = rec.TerritoryCells;
            var none = new Color32(0, 0, 0, 0);
            var noneEdge = new Color32(10, 12, 20, 110);
            for (int k = 0; k < cells.Length; k++)
            {
                int id = cells[k];
                if (id < 0 || id >= _owners.Length) { _terrPx[k] = none; continue; }
                int o = _owners[id];
                bool edge = _terrEdge[k];
                if (o == MatchRecord.OwnerNone) { _terrPx[k] = edge ? noneEdge : none; continue; }
                Color c = o == MatchRecord.OwnerCurse ? curse : rec.FactionColor[o];
                c.a = edge ? edgeA : fillA;
                _terrPx[k] = c;
            }
            _terrTex.SetPixels32(_terrPx);
            _terrTex.Apply(false);
        }

        /// <summary>Draw the map at sim time <paramref name="t"/>.</summary>
        public void Render(float t)
        {
            if (_rec == null) return;
            var vr = _viewport.rect;
            _fit = Mathf.Max(1e-4f, Mathf.Min(vr.width / _span.x, vr.height / _span.y) * 0.98f);
            float s = _fit * _zoom;
            _content.localScale = new Vector3(s, s, 1f);
            ClampPan();

            PaintTerritory(t);

            if (!Mathf.Approximately(_labelZoom, s))
            {
                _labelZoom = s;
                float ls = Mathf.Sqrt(_zoom) / s;
                var v = new Vector3(ls, ls, 1f);
                foreach (var l in _labels) if (l != null) l.localScale = v;
            }
            _overlay.Show(t, s, _zoom);
        }

        private void ClampPan()
        {
            var vr = _viewport.rect;
            float s = _fit * _zoom;
            float mx = Mathf.Max(0f, (_span.x * s - vr.width) * 0.5f) + vr.width * 0.25f;
            float mz = Mathf.Max(0f, (_span.y * s - vr.height) * 0.5f) + vr.height * 0.25f;
            var p = _content.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -mx, mx);
            p.y = Mathf.Clamp(p.y, -mz, mz);
            _content.anchoredPosition = p;
        }

        public void ResetView()
        {
            _zoom = 1f;
            if (_content != null) _content.anchoredPosition = Vector2.zero;
        }

        public void OnScroll(PointerEventData e)
        {
            if (_rec == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, e.position,
                    e.pressEventCamera, out Vector2 q)) return;
            // The content is anchored at the viewport's centre.
            q -= _viewport.rect.center;
            float maxZoom = _cfg != null ? Mathf.Max(1f, _cfg.maxZoom) : 16f;
            float old = _zoom;
            // One notch = one step, whatever the input module reports per notch.
            float notches = Mathf.Clamp(e.scrollDelta.y, -3f, 3f);
            _zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.2f, notches), 1f, maxZoom);
            if (Mathf.Approximately(old, _zoom)) return;
            var p = _content.anchoredPosition;
            _content.anchoredPosition = q - (q - p) * (_zoom / old);
        }

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            var canvas = GetComponentInParent<Canvas>();
            float sf = canvas != null ? Mathf.Max(1e-4f, canvas.rootCanvas.scaleFactor) : 1f;
            _content.anchoredPosition += e.delta / sf;
        }

        private void OnDestroy()
        {
            if (_terrTex != null) Destroy(_terrTex);
        }
    }
}
