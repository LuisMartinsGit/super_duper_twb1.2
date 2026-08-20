// GameClockHUD.cs
// Small match-time readout, top-centre of the screen (user request
// 2026-08-04: track AI pacing/wave timing accurately while spectating).
// Shows SIMULATION time (the ECS world's ElapsedTime — the clock every AI
// gate, wave interval and escalation curve runs on), not wall time, so what
// you read is exactly what the systems see. Same pool-on-private-canvas
// pattern as the other runtime HUD pieces; mounted by GameBootstrap.

using UnityEngine;
using UnityEngine.UI;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    public class GameClockHUD : MonoBehaviour
    {
        private const float UpdateInterval = 0.25f;
        private const int CanvasSortingOrder = 60; // above bars (50), below web HUD (100)

        /// <summary>Gap from the top screen edge to the clock label.</summary>
        private const float TopGapPx = 6f;
        /// <summary>Clock label height.</summary>
        private const float LabelHeightPx = 26f;

        /// <summary>
        /// SCREEN PIXELS the clock reserves at top-centre, top edge down.
        ///
        /// Raw pixels, not canvas units: the clock lives on its own
        /// constant-pixel canvas, so it is the same size on screen at every
        /// resolution. Anything else that pins top-centre — TopChoiceBar's
        /// "SELECT CULTURE" pill — must clear this, and if that widget's canvas
        /// scales with screen size it has to convert through its own
        /// scaleFactor rather than assume the two agree.
        /// </summary>
        public const float ReservedScreenHeight = TopGapPx + LabelHeightPx;

        private Text _label;
        private float _nextUpdate;

        private void Start()
        {
            var canvasGo = new GameObject("[Game Clock Canvas]");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            canvasGo.AddComponent<CanvasScaler>();

            var textGo = new GameObject("ClockText");
            textGo.transform.SetParent(canvasGo.transform, false);
            _label = textGo.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 18;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.UpperCenter;
            _label.color = new Color(1f, 1f, 1f, 0.9f);

            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1f, -1f);

            var rt = _label.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -TopGapPx);
            rt.sizeDelta = new Vector2(160f, LabelHeightPx);
        }

        private void Update()
        {
            if (_label == null || Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + UpdateInterval;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { _label.text = string.Empty; return; }

            double t = world.Time.ElapsedTime;
            int hours = (int)(t / 3600.0);
            int minutes = (int)(t / 60.0) % 60;
            int seconds = (int)t % 60;
            _label.text = hours > 0
                ? $"{hours}:{minutes:00}:{seconds:00}"
                : $"{minutes:00}:{seconds:00}";
        }
    }
}
