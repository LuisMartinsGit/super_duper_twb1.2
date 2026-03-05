// File: Assets/Scripts/UI/HUD/PlayerNotificationSystem.cs
// Displays floating notification messages (errors, warnings) at the top-center of the screen.
// Messages auto-fade after a configurable duration with duplicate suppression.

using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Singleton MonoBehaviour that queues and displays player feedback messages.
    /// Uses IMGUI to match the existing Dark Navy + Golden theme.
    /// Call <see cref="Notify"/> or <see cref="NotifyError"/> from anywhere to show a message.
    /// </summary>
    public class PlayerNotificationSystem : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>How long a notification stays visible (seconds).</summary>
        private const float DefaultDuration = 2.5f;

        /// <summary>During the last N seconds of life the notification fades out.</summary>
        private const float FadeDuration = 0.5f;

        /// <summary>Maximum number of notifications visible at once.</summary>
        private const int MaxVisible = 5;

        /// <summary>Y offset from top of screen (below the resource bar).</summary>
        private const float TopOffset = 50f;

        /// <summary>Vertical spacing between stacked notifications.</summary>
        private const float StackSpacing = 6f;

        /// <summary>Notification pill dimensions.</summary>
        private const float PillMinWidth = 220f;
        private const float PillMaxWidth = 500f;
        private const float PillHeight = 32f;
        private const float PillPaddingH = 16f;

        // ═══════════════════════════════════════════════════════════════════
        // INTERNAL TYPES
        // ═══════════════════════════════════════════════════════════════════

        private struct Notification
        {
            public string Message;
            public float TimeRemaining;
            public float Duration;
            public Color TextColor;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════

        private static PlayerNotificationSystem _instance;

        private readonly List<Notification> _notifications = new();

        // Cached styles
        private GUIStyle _pillTextStyle;
        private Texture2D _pillBgTex;
        private Texture2D _pillBorderTex;
        private bool _stylesInit;

        // ═══════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Show a warning notification with golden text.
        /// </summary>
        public static void Notify(string message)
        {
            if (_instance == null) return;
            _instance.AddNotification(message, UIHelpers.ThemeGold);
        }

        /// <summary>
        /// Show an error notification with red text.
        /// </summary>
        public static void NotifyError(string message)
        {
            if (_instance == null) return;
            _instance.AddNotification(message, new Color(1f, 0.35f, 0.35f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        void Update()
        {
            // Tick down notification timers
            float dt = Time.unscaledDeltaTime;
            for (int i = _notifications.Count - 1; i >= 0; i--)
            {
                var n = _notifications[i];
                n.TimeRemaining -= dt;
                if (n.TimeRemaining <= 0f)
                {
                    _notifications.RemoveAt(i);
                }
                else
                {
                    _notifications[i] = n;
                }
            }
        }

        void OnGUI()
        {
            if (_notifications.Count == 0) return;

            InitStyles();

            // Draw notifications from top (newest first — newest is at end of list)
            float y = TopOffset;
            int drawn = 0;

            for (int i = _notifications.Count - 1; i >= 0 && drawn < MaxVisible; i--, drawn++)
            {
                var n = _notifications[i];
                DrawNotification(n, y);
                y += PillHeight + StackSpacing;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // INTERNALS
        // ═══════════════════════════════════════════════════════════════════

        private void AddNotification(string message, Color color)
        {
            // Duplicate suppression: skip if same message is already active
            for (int i = 0; i < _notifications.Count; i++)
            {
                if (_notifications[i].Message == message)
                {
                    // Reset the timer on the existing notification instead
                    var existing = _notifications[i];
                    existing.TimeRemaining = DefaultDuration;
                    _notifications[i] = existing;
                    return;
                }
            }

            // Enforce max visible — remove oldest if at capacity
            while (_notifications.Count >= MaxVisible)
            {
                _notifications.RemoveAt(0);
            }

            _notifications.Add(new Notification
            {
                Message = message,
                TimeRemaining = DefaultDuration,
                Duration = DefaultDuration,
                TextColor = color
            });
        }

        private void DrawNotification(Notification n, float y)
        {
            // Calculate alpha based on fade
            float alpha = 1f;
            if (n.TimeRemaining < FadeDuration)
            {
                alpha = n.TimeRemaining / FadeDuration;
            }

            // Measure text width
            var content = new GUIContent(n.Message);
            float textWidth = _pillTextStyle.CalcSize(content).x;
            float pillWidth = Mathf.Clamp(textWidth + PillPaddingH * 2f, PillMinWidth, PillMaxWidth);

            // Center horizontally
            float x = (Screen.width - pillWidth) * 0.5f;
            var pillRect = new Rect(x, y, pillWidth, PillHeight);

            // Background pill
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.9f);
            GUI.DrawTexture(pillRect, _pillBgTex);

            // Border
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.6f);
            DrawPillBorder(pillRect, 1);

            // Text
            var textColor = n.TextColor;
            textColor.a = alpha;
            var style = new GUIStyle(_pillTextStyle)
            {
                normal = { textColor = textColor }
            };
            GUI.Label(pillRect, n.Message, style);

            // Reset GUI color
            GUI.color = Color.white;
        }

        private static void DrawPillBorder(Rect rect, int width)
        {
            // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, width), Texture2D.whiteTexture);
            // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - width, rect.width, width), Texture2D.whiteTexture);
            // Left
            GUI.DrawTexture(new Rect(rect.x, rect.y, width, rect.height), Texture2D.whiteTexture);
            // Right
            GUI.DrawTexture(new Rect(rect.xMax - width, rect.y, width, rect.height), Texture2D.whiteTexture);
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            // Dark navy background for the pill
            _pillBgTex = UIHelpers.MakeTexture(2, 2, new Color(0.06f, 0.08f, 0.18f, 0.92f));

            _pillTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UIHelpers.ThemeGold }
            };

            _stylesInit = true;
        }
    }
}
