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
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Ingame
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

        /// <summary>Clock label width.</summary>
        private const float ClockWidth = 160f;
        /// <summary>Width of each countdown label flanking the clock.</summary>
        private const float CountdownWidth = 340f;
        /// <summary>Half the clock text's width ("00:00" at 18 pt bold) plus a
        /// gap: where a countdown starts, either side of the centre line.</summary>
        private const float CountdownInsetPx = 34f;

        private Text _label;
        /// <summary>TIMED VICTORIES (2026-10-19): up to two countdowns -- the
        /// Sanctum dominion and the Shardroot ascension -- one either side of
        /// the clock, on the clock's own row, so they never push into the
        /// culture pill / special-building cluster pinned below it.</summary>
        private Text _right, _left;
        private float _nextUpdate;
        private readonly System.Collections.Generic.List<string> _countdowns =
            new System.Collections.Generic.List<string>(2);

        private void Start()
        {
            var canvasGo = new GameObject("[Game Clock Canvas]");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            canvasGo.AddComponent<CanvasScaler>();

            _label = MakeLabel(canvasGo.transform, "ClockText", TextAnchor.UpperCenter,
                pivotX: 0.5f, x: 0f, width: ClockWidth);
            _right = MakeLabel(canvasGo.transform, "CountdownRight", TextAnchor.UpperLeft,
                pivotX: 0f, x: CountdownInsetPx, width: CountdownWidth);
            _left = MakeLabel(canvasGo.transform, "CountdownLeft", TextAnchor.UpperRight,
                pivotX: 1f, x: -CountdownInsetPx, width: CountdownWidth);
        }

        private static Text MakeLabel(Transform parent, string name, TextAnchor align,
                                      float pivotX, float x, float width)
        {
            var textGo = new GameObject(name);
            textGo.transform.SetParent(parent, false);
            var label = textGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 18;
            label.fontStyle = FontStyle.Bold;
            label.alignment = align;
            label.color = new Color(1f, 1f, 1f, 0.9f);
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1f, -1f);

            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(pivotX, 1f);
            rt.anchoredPosition = new Vector2(x, -TopGapPx);
            rt.sizeDelta = new Vector2(width, LabelHeightPx);
            return label;
        }

        private void Update()
        {
            if (_label == null || Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + UpdateInterval;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                _label.text = string.Empty; _right.text = string.Empty; _left.text = string.Empty;
                return;
            }

            // SimClock, NOT world.Time.ElapsedTime. The ECS world is created
            // at process start and keeps running through the whole main menu,
            // so its elapsed time is "how long the game has been open" — a
            // player who browsed the menus for six minutes started their match
            // at 06:00 on the clock.
            //
            // SimClock is reset when the match spawns and only advances inside
            // SimulationSystemGroup, which is held until the loading screen is
            // gone (timeScale in single-player, the world-ready gate under
            // lockstep). So it counts exactly the time the player has been
            // playing, and reads identically on every peer.
            double t = TheWaningBorder.Core.SimClock.Elapsed;
            int hours = (int)(t / 3600.0);
            int minutes = (int)(t / 60.0) % 60;
            int seconds = (int)t % 60;
            string clock = hours > 0
                ? $"{hours}:{minutes:00}:{seconds:00}"
                : $"{minutes:00}:{seconds:00}";
            // TIMED VICTORIES (2026-10-19): while one side holds every
            // Sanctum (Territory_Claims.md § 11.4) or a Temple enshrines the
            // Shardroot (Curse_And_Shardroot.md § 3.1c), every player and
            // spectator sees whose countdown it is and the time left. Both
            // are read from the sim's own ECS state (sim time), so replays,
            // spectators and loaded saves show exactly what the sim holds.
            _countdowns.Clear();
            var em = world.EntityManager;
            if (TheWaningBorder.Systems.Economy.SanctumSystem.TryCountdown(em, out var holder, out float left))
                _countdowns.Add(CountdownLine(holder, left, sanctum: true));
            if (TheWaningBorder.Systems.Border.ShardrootSystem.TryAscensionCountdown(em, out var temple, out float aLeft))
                _countdowns.Add(CountdownLine(temple, aLeft, sanctum: false));
            _right.text = _countdowns.Count > 0 ? _countdowns[0] : string.Empty;
            _left.text = _countdowns.Count > 1 ? _countdowns[1] : string.Empty;
            _label.text = clock;
        }

        /// <summary>
        /// One countdown: "Your dominion: 07:32" in the allied tint for the
        /// local player's side, "Blue — Sanctum dominion 07:32" with the
        /// winner's name in its colour and the rest in the hostile tint for an
        /// opponent, the coloured name and plain text for a spectator.
        /// </summary>
        private static string CountdownLine(Faction winner, float secondsLeft, bool sanctum)
        {
            string time = CountdownWarnings.Clock(secondsLeft);
            var side = CountdownWarnings.SideOf(winner);
            if (side == CountdownSide.Own)
            {
                string own = sanctum ? Loc.T("Your dominion: {0}") : Loc.T("Your ascension: {0}");
                return Tint(string.Format(own, time), WorldOverlayPalette.AlliedTint);
            }
            string name = Tint(Loc.T(FactionColors.GetColorName(winner)), FactionColors.Get(winner));
            string kind = sanctum ? Loc.T("Sanctum dominion {0}") : Loc.T("Shardroot ascension {0}");
            string rest = " \u2014 " + string.Format(kind, time);
            return side == CountdownSide.Hostile
                ? name + Tint(rest, WorldOverlayPalette.HostileTint)
                : name + rest;
        }

        private static string Tint(string text, Color c) =>
            "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + text + "</color>";
    }
}
