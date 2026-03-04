// File: Assets/Scripts/UI/Menus/LoadingScreen.cs
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Full-screen loading overlay shown during scene transitions.
    /// Persists across scene loads via DontDestroyOnLoad.
    /// Fades out once the Game scene is fully initialized.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        private static LoadingScreen _instance;

        private float _alpha = 1f;
        private float _fadeSpeed = 1.5f;
        private bool _fadingOut;
        private bool _sceneLoaded;
        private string _statusText = "Loading...";
        private float _progress;
        private Texture2D _bgTex;
        private GUIStyle _titleStyle;
        private GUIStyle _statusStyle;
        private bool _stylesInit;

        /// <summary>
        /// Show the loading screen and begin async scene load.
        /// </summary>
        public static void Show(string sceneName)
        {
            if (_instance != null) return;

            var go = new GameObject("LoadingScreen");
            _instance = go.AddComponent<LoadingScreen>();
            DontDestroyOnLoad(go);

            _instance.StartCoroutine(_instance.LoadSceneRoutine(sceneName));
        }

        /// <summary>
        /// Called by game systems when initialization is complete.
        /// Triggers the fade-out.
        /// </summary>
        public static void NotifyReady()
        {
            if (_instance != null)
            {
                _instance._statusText = "Ready";
                _instance._fadingOut = true;
            }
        }

        /// <summary>
        /// True if a loading screen is currently active.
        /// </summary>
        public static bool IsActive => _instance != null && !_instance._fadingOut;

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            _statusText = "Loading world...";
            _progress = 0f;

            var op = SceneManager.LoadSceneAsync(sceneName);
            op.allowSceneActivation = true;

            while (!op.isDone)
            {
                _progress = Mathf.Clamp01(op.progress / 0.9f);
                _statusText = $"Loading world... {(_progress * 100f):F0}%";
                yield return null;
            }

            _sceneLoaded = true;
            _statusText = "Initializing...";
            _progress = 1f;

            // Wait for game bootstrap to finish (terrain + spawning)
            // NotifyReady() will be called by SpawnDelayHelper when done
        }

        void Update()
        {
            if (_fadingOut)
            {
                _alpha -= Time.deltaTime * _fadeSpeed;
                if (_alpha <= 0f)
                {
                    _instance = null;
                    Destroy(gameObject);
                }
            }
        }

        void OnGUI()
        {
            if (_alpha <= 0f) return;

            InitStyles();

            // Full-screen black overlay
            GUI.color = new Color(0, 0, 0, _alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bgTex);
            GUI.color = new Color(1, 1, 1, _alpha);

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            // Title
            var titleRect = new Rect(cx - 200, cy - 60, 400, 50);
            GUI.Label(titleRect, "The Waning Border", _titleStyle);

            // Status text
            var statusRect = new Rect(cx - 200, cy, 400, 30);
            GUI.Label(statusRect, _statusText, _statusStyle);

            // Progress bar
            float barWidth = 300f;
            float barHeight = 8f;
            var barBgRect = new Rect(cx - barWidth * 0.5f, cy + 40, barWidth, barHeight);

            GUI.color = new Color(0.2f, 0.2f, 0.2f, _alpha);
            GUI.DrawTexture(barBgRect, Texture2D.whiteTexture);

            GUI.color = new Color(0.6f, 0.8f, 1f, _alpha);
            GUI.DrawTexture(new Rect(barBgRect.x, barBgRect.y, barWidth * _progress, barHeight), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, Color.black);
            _bgTex.Apply();

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.8f, 0.85f, 1f) }
            };

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _stylesInit = true;
        }
    }
}
