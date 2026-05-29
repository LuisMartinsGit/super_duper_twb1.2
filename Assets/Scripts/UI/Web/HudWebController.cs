// HudWebController — hosts a CEF-backed web overlay on a fullscreen Canvas
// and points it at the bundled HUD frontend in StreamingAssets/HUD/.
//
// Spawned at runtime by GameBootstrap. Owns the UWB WebBrowserUIBasic and
// exposes a thin Push() API used by HudBridge to ship game state to JS.

using System.IO;
using UnityEngine;
using UnityEngine.UI;
using VoltstroStudios.UnityWebBrowser;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Input;
using VoltstroStudios.UnityWebBrowser.Shared.Core;
using VoltstroStudios.UnityWebBrowser.Shared.Popups;

namespace TheWaningBorder.UI.Web
{
    public sealed class HudWebController : MonoBehaviour
    {
        // Singleton for the rest of the C# side (HudBridge) to find the live
        // browser. Set in Awake, cleared in OnDestroy.
        public static HudWebController Instance { get; private set; }

        // True while the player's cursor is over an interactive HUD region.
        // CEF runs in a separate process and Unity's input system fires
        // independently of CEF's DOM events — so without this flag, clicking
        // an HTML button (e.g. an Action cell) would also be processed as a
        // game-world click, deselecting units / placing the wrong thing. JS
        // sets this via the `hud:capture` bridge topic from onMouseEnter /
        // onMouseLeave on the panel wrappers.
        public static bool IsPointerOverWebHud { get; set; }

        [Tooltip("Sort order for the HUD canvas. Should sit above gameplay-world canvases.")]
        public int canvasSortOrder = 100;

        [Tooltip("CEF render resolution. Higher = sharper text but more GPU/CPU. " +
                 "Set to your typical play resolution; 1920×1080 is a safe default.")]
        public Vector2Int browserResolution = new(1920, 1080);

        [Tooltip("Windowless frame rate cap. 60 gives the HUD one CEF frame per " +
                 "snapshot at 30Hz push and matches typical 60Hz monitor refresh " +
                 "so React updates appear without judder.")]
        public int browserFps = 60;

        WebBrowserClient _client;
        WebBrowserUIBasic _ui;

        /// <summary>The live UWB client, once the browser has booted.</summary>
        public WebBrowserClient Client => _client;

        /// <summary>True once UWB reports ready (CEF process up, page loaded).</summary>
        public bool IsReady => _client != null && _client.ReadySignalReceived;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            BuildCanvas();
            BuildBrowser();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _client?.Dispose();
        }

        void BuildCanvas()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = browserResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            gameObject.AddComponent<GraphicRaycaster>();
        }

        void BuildBrowser()
        {
            // The RawImage that displays CEF's pixel output sits as a child of
            // the canvas, stretched to fill the screen.
            var imageGo = new GameObject("HudWebView",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageGo.transform.SetParent(transform, false);

            var rt = (RectTransform)imageGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Build the engine config that points at the CEF binaries shipped
            // by the dev.voltstro.unitywebbrowser.engine.cef.win.x64 package.
            // We hard-code Windows x64 — that's the only platform we ship to
            // and the only engine package we depend on.
            var engineConfig = ScriptableObject.CreateInstance<EngineConfiguration>();
            engineConfig.engineAppName = "UnityWebBrowser.Engine.Cef";
            engineConfig.engineFiles = new[]
            {
                new Engine.EnginePlatformFiles
                {
                    platform = Platform.Windows64,
                    engineBaseAppLocation = string.Empty,
                    engineEditorLocation = "Packages/dev.voltstro.unitywebbrowser.engine.cef.win.x64/Engine~/",
                    engineRuntimeLocation = "UWB/",
                },
            };

            var coms = ScriptableObject.CreateInstance<TCPCommunicationLayer>();
            var inputHandler = ScriptableObject.CreateInstance<WebBrowserOldInputHandler>();

            _ui = imageGo.AddComponent<WebBrowserUIBasic>();
            _client = _ui.browserClient;
            _client.engine = engineConfig;
            _client.communicationLayer = coms;
            _ui.inputHandler = inputHandler;

            // Transparent background so the 3D game shows through the gaps
            // between HUD panels. The HUD CSS already sets `body{background:
            // transparent}` and disables the fake vignette.
            _client.backgroundColor = new Color32(0, 0, 0, 0);

            _client.javascript = true;
            _client.localStorage = true;

            // Enable JS Methods so the page can call uwb.ExecuteJsMethod(...)
            // back into C# (used by bridge.js → HudBridge.OnHudMessage).
            _client.jsMethodManager.jsMethodsEnable = true;

            // NOTE: `Resolution` setter calls `Resize()` which throws
            // UwbIsNotReadyException when the browser hasn't connected yet.
            // We defer the resize to OnBrowserConnected below. The default
            // (1920×1080, set as a field initializer on WebBrowserClient) is
            // applied through the CLI args.
            _client.windowlessFrameRate = browserFps;

            _client.popupAction = PopupAction.Ignore;

            // Boot to about:blank, then load the real HUD via LoadUrl() once the
            // browser process has connected. UWB passes initialUrl via the CEF
            // command line — paths containing spaces (like "The Waning Border 1.2")
            // can break that parser, so we sidestep it entirely.
            _client.initialUrl = "about:blank";
            _hudUrl = ResolveHudUrl();
            _client.OnClientConnected += OnBrowserConnected;
            TWBLog.Log($"[HudWebController] HUD URL will be: {_hudUrl}");
        }

        string _hudUrl;
        bool _loadedHud;

        void OnBrowserConnected()
        {
            if (_loadedHud || _client == null) return;
            _loadedHud = true;

            // Apply the inspector-configured resolution now that Resize() is safe.
            uint wantW = (uint)browserResolution.x;
            uint wantH = (uint)browserResolution.y;
            if (_client.Resolution.Width != wantW || _client.Resolution.Height != wantH)
            {
                _client.Resolution = new VoltstroStudios.UnityWebBrowser.Shared.Resolution(wantW, wantH);
            }

            TWBLog.Log($"[HudWebController] LoadUrl → {_hudUrl}");
            _client.LoadUrl(_hudUrl);
        }

        // file:// URL pointing at the bundled HUD inside StreamingAssets. We
        // URL-encode each path segment so spaces ("The Waning Border 1.2")
        // and other CLI-sensitive characters survive.
        static string ResolveHudUrl()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "HUD", "index.html")
                .Replace('\\', '/');
            // Encode segments individually so we keep "/" between them.
            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                // Preserve a Windows drive letter ("D:") at index 0 — Uri.EscapeDataString
                // would mangle the colon.
                if (i == 0 && parts[i].Length == 2 && parts[i][1] == ':') continue;
                parts[i] = System.Uri.EscapeDataString(parts[i]);
            }
            return "file:///" + string.Join("/", parts).TrimStart('/');
        }

        /// <summary>Push a state update to the JS bridge.</summary>
        public void Push(string topic, string payloadJson)
        {
            if (_client == null || !_client.IsConnected) return;
            // JSON-escape the topic to be safe; payloadJson is already JSON.
            var safeTopic = topic.Replace("\\", "\\\\").Replace("'", "\\'");
            _client.ExecuteJs(
                $"window.unityHUD && window.unityHUD.recv('{safeTopic}', {payloadJson});");
        }
    }
}
