// ResourceGainPopups.cs
// Floating "+N [icon]" wherever a faction gains resources on the map — a
// Waystation's road tax as a wagon rolls by, a market sale, the cargo of a
// wagon its killers take, Feraldis plunder and raze bounties. The simulation
// posts SimSignals.Gain; this drains it each frame and draws the numbers.
//
// Shown for the local player's gains; a spectator sees everyone's. Text uses
// the resource panel's own icons inline (ResourceIcons), falling back to
// letters before the HUD has registered them. Tunables are on
// ResourceGainPopups.asset beside this file.

using System.Collections.Generic;
using System.Text;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class ResourceGainPopups : MonoBehaviour
    {
        private static ResourceGainPopupsConfig _cfg;
        private static ResourceGainPopupsConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<ResourceGainPopupsConfig>());

        private struct Popup
        {
            public Vector3 World;
            public string Text;
            public float Spawn;
        }

        private readonly List<Popup> _popups = new List<Popup>();
        private readonly List<TextMeshProUGUI> _pool = new List<TextMeshProUGUI>();
        private readonly StringBuilder _sb = new StringBuilder();
        private Canvas _canvas;
        private Camera _cam;

        private void Awake() => EnsureCanvas();

        private void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("ResourceGains-Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = Cfg.canvasSortingOrder;
            go.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        }

        private void Update()
        {
            var local = GameSettings.LocalPlayerFaction;
            float now = Time.time;
            while (SimSignals.TryDrainGain(out var g))
            {
                if (!GameSettings.IsSpectating && g.Faction != local) continue;
                if (_popups.Count >= Cfg.maxPopups) _popups.RemoveAt(0);
                _popups.Add(new Popup
                {
                    World = new Vector3(g.Position.x, g.Position.y, g.Position.z),
                    Text = Format(g.Amount),
                    Spawn = now,
                });
            }

            for (int i = _popups.Count - 1; i >= 0; i--)
                if (now - _popups[i].Spawn > Cfg.lifetimeSeconds) _popups.RemoveAt(i);

            var cam = _cam != null ? _cam : (_cam = Camera.main);
            int shown = 0;
            if (cam != null)
                for (int i = 0; i < _popups.Count; i++)
                {
                    var p = _popups[i];
                    float t = (now - p.Spawn) / Mathf.Max(0.01f, Cfg.lifetimeSeconds);
                    var world = p.World + Vector3.up * (Cfg.yOffset + Cfg.riseWorldUnits * t);
                    var screen = cam.WorldToScreenPoint(world);
                    if (screen.z <= 0f) continue;   // behind the camera
                    var label = Label(shown++);
                    label.text = p.Text;
                    label.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
                    var c = Cfg.color;
                    c.a = t < 0.6f ? 1f : Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);
                    label.color = c;
                }
            for (int i = shown; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        private string Format(Cost a)
        {
            _sb.Clear();
            Part(a.Supplies, ResourceIcons.Supplies, "S");
            Part(a.Iron, ResourceIcons.Iron, "I");
            Part(a.Veilstone, ResourceIcons.Veilstone, "V");
            Part(a.Veilsteel, ResourceIcons.Veilsteel, "Vs");
            return _sb.ToString();
        }

        private void Part(int n, string resource, string letter)
        {
            if (n <= 0) return;
            if (_sb.Length > 0) _sb.Append("  ");
            _sb.Append('+').Append(n).Append(' ').Append(ResourceIcons.Tag(resource) ?? letter);
        }

        private TextMeshProUGUI Label(int i)
        {
            while (_pool.Count <= i)
            {
                var go = new GameObject("Gain", typeof(RectTransform));
                go.transform.SetParent(_canvas.transform, false);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.fontSize = Cfg.fontSize;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.enableWordWrapping = false;
                t.raycastTarget = false;
                t.outlineWidth = 0.2f;
                t.outlineColor = new Color32(0, 0, 0, 200);
                ((RectTransform)go.transform).sizeDelta = new Vector2(320f, 40f);
                _pool.Add(t);
            }
            var label = _pool[i];
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            return label;
        }
    }
}
