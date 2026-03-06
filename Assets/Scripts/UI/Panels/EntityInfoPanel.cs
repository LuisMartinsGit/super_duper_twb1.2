// File: Assets/Scripts/UI/Panels/EntityInfoPanel.cs
// Entity info panel — Dark Navy + Golden theme

using UnityEngine;
using Unity.Entities;
using TWB_Input = TheWaningBorder.Input;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// IMGUI panel showing selected entity info — dark navy with golden accents.
    /// </summary>
    public class EntityInfoPanel : MonoBehaviour
    {
        public static bool PanelVisible { get; private set; }
        public static Rect PanelRect { get; private set; }

        private const float PanelWidth = 300f;
        private const float PanelHeight = 310f;
        private const float PanelPadding = 10f;
        private const float PortraitSize = 80f;

        private GUIStyle _boxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _descStyle;
        private RectOffset _padding;
        private bool _stylesInit = false;

        void Awake()
        {
            _padding = new RectOffset(10, 10, 10, 10);
        }

        void OnGUI()
        {
            PanelVisible = false;

            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null) return;

            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) return;

            var info = EntityInfoExtractor.GetDisplayInfo(entity, em);

            InitStyles();
            DrawPanel(info);
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = UIHelpers.MakeBorderedTexture(64, 64,
                    new Color(0.06f, 0.08f, 0.18f, 0.95f),
                    new Color(0.83f, 0.66f, 0.26f, 0.8f), 2) }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.83f, 0.66f, 0.26f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.7f, 0.68f, 0.60f) }
            };

            _descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.8f, 0.78f, 0.72f) }
            };

            _stylesInit = true;
        }

        private void DrawPanel(EntityDisplayInfo info)
        {
            PanelVisible = true;

            var panelRect = new Rect(
                PanelPadding,
                Screen.height - PanelHeight - PanelPadding,
                PanelWidth,
                PanelHeight
            );
            PanelRect = panelRect;

            GUI.Box(panelRect, "", _boxStyle);

            var innerRect = new Rect(
                panelRect.x + _padding.left,
                panelRect.y + _padding.top,
                panelRect.width - _padding.horizontal,
                panelRect.height - _padding.vertical
            );

            GUILayout.BeginArea(innerRect);

            // Top section: Portrait + Name/Type
            GUILayout.BeginHorizontal();

            // Portrait
            if (info.Portrait != null)
            {
                var portraitRect = GUILayoutUtility.GetRect(PortraitSize, PortraitSize,
                    GUILayout.Width(PortraitSize), GUILayout.Height(PortraitSize));
                GUI.DrawTexture(portraitRect, info.Portrait, ScaleMode.ScaleToFit);
            }
            else
            {
                var portraitRect = GUILayoutUtility.GetRect(PortraitSize, PortraitSize,
                    GUILayout.Width(PortraitSize), GUILayout.Height(PortraitSize));
                GUI.Box(portraitRect, "?");
            }

            GUILayout.Space(10);

            // Name and type
            GUILayout.BeginVertical();
            GUILayout.Label(info.Name, _headerStyle);
            bool isEnemy = info.Faction != null &&
                info.Faction != GameSettings.LocalPlayerFaction.ToString();
            string typeLabel = isEnemy ? $"{info.Faction} {info.Type}" : info.Type;
            GUILayout.Label(typeLabel, _smallStyle);

            // Health bar
            if (info.CurrentHealth.HasValue && info.MaxHealth.HasValue)
            {
                GUILayout.Space(5);
                DrawHealthBar(info.CurrentHealth.Value, info.MaxHealth.Value);
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Stats section
            if (info.HasCombatStats)
            {
                GUILayout.BeginHorizontal();
                if (info.Attack.HasValue)
                    GUILayout.Label($"Attack: {info.Attack.Value}", _labelStyle, GUILayout.Width(120));
                if (info.Defense.HasValue)
                    GUILayout.Label($"Defense: {info.Defense.Value}", _labelStyle, GUILayout.Width(120));
                GUILayout.EndHorizontal();

                if (info.Speed.HasValue)
                    GUILayout.Label($"Speed: {info.Speed.Value:F1}", _labelStyle);
            }

            // Resource generation (buildings)
            if (info.HasResourceGeneration)
            {
                GUILayout.Space(5);
                GUILayout.Label("Resource Generation:", _smallStyle);
                GUILayout.BeginHorizontal();
                if (info.SuppliesPerMinute.HasValue && info.SuppliesPerMinute.Value > 0)
                    GUILayout.Label($"Supplies: {info.SuppliesPerMinute.Value:F0}/min", _labelStyle, GUILayout.Width(130));
                if (info.IronPerMinute.HasValue && info.IronPerMinute.Value > 0)
                    GUILayout.Label($"Iron: {info.IronPerMinute}/min", _labelStyle, GUILayout.Width(120));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (info.CrystalPerMinute.HasValue && info.CrystalPerMinute.Value > 0)
                    GUILayout.Label($"Crystal: {info.CrystalPerMinute}/min", _labelStyle, GUILayout.Width(130));
                if (info.VeilsteelPerMinute.HasValue && info.VeilsteelPerMinute.Value > 0)
                    GUILayout.Label($"Veilsteel: {info.VeilsteelPerMinute}/min", _labelStyle, GUILayout.Width(130));
                if (info.GlowPerMinute.HasValue && info.GlowPerMinute.Value > 0)
                    GUILayout.Label($"Glow: {info.GlowPerMinute}/min", _labelStyle, GUILayout.Width(120));
                GUILayout.EndHorizontal();
            }

            // Miner info (no carry bar — just rate and status)
            if (info.HasMinerInfo)
            {
                GUILayout.Space(5);
                GUILayout.Label($"Rate: {info.MinerExtractionRate}", _labelStyle);
                GUILayout.Label($"Status: {info.MinerState}", _smallStyle);
            }

            // Resource deposit info
            if (info.HasResourceInfo)
            {
                GUILayout.Space(5);
                GUILayout.Label($"{info.ResourceTypeName} Remaining:", _smallStyle);
                DrawResourceBar(info.ResourceRemaining, info.ResourceMax);
                GUILayout.Label($"{info.ResourceRemaining} / {info.ResourceMax}", _labelStyle);
            }

            GUILayout.Space(10);

            // Description
            GUILayout.Label(info.Description, _descStyle);

            GUILayout.EndArea();
        }

        private void DrawResourceBar(int current, int max)
        {
            var rect = GUILayoutUtility.GetRect(200f, 12);

            // Background
            GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Fill (teal/blue for resources)
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
            Color fillColor = ratio > 0.5f ? new Color(0.3f, 0.7f, 0.9f) :
                              (ratio > 0.2f ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.8f, 0.3f, 0.3f));
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        private void DrawHealthBar(int current, int max)
        {
            var rect = GUILayoutUtility.GetRect(PortraitSize, 10);

            // Background
            GUI.color = new Color(0.04f, 0.05f, 0.12f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Fill
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
            Color fillColor = ratio > 0.5f ? new Color(0.3f, 0.9f, 0.3f) :
                              (ratio > 0.25f ? new Color(0.9f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f));
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;

            // Text
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, $"{current}/{max}", labelStyle);
        }

        /// <summary>
        /// Check if pointer is over this panel.
        /// </summary>
        public static bool IsPointerOver()
        {
            if (!PanelVisible) return false;
            var mousePos = UnityEngine.Input.mousePosition;
            var screenRect = new Rect(
                PanelRect.x,
                Screen.height - PanelRect.y - PanelRect.height,
                PanelRect.width,
                PanelRect.height
            );
            return screenRect.Contains(mousePos);
        }
    }
}
