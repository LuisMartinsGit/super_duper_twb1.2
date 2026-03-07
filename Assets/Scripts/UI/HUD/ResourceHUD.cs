// File: Assets/Scripts/UI/HUD/ResourceHUD.cs
// Resource Display — Dark Navy + Golden theme

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;


namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// IMGUI-based resource HUD — dark navy bar with golden accents.
    /// </summary>
    public class ResourceHUD : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private Faction humanFaction = GameSettings.LocalPlayerFaction;
        [SerializeField] private float refreshInterval = 0.25f;
        [SerializeField] private float topBarHeight = 38f;
        [SerializeField] private float leftPadding = 10f;
        [SerializeField] private float pillSpacing = 8f;

        /// <summary>Returns true if the mouse is over the top resource bar.</summary>
        public static bool IsPointerOverTopBar { get; private set; }

        private EntityWorld _world;
        private EntityManager _em;
        private EntityQuery _banksQuery;
        private EntityQuery _populationQuery;

        private readonly Dictionary<Faction, FactionResources> _cache = new();
        private readonly Dictionary<Faction, (int current, int max)> _popCache = new();
        private float _timer;

        // Styles
        private GUIStyle _topBarBg;
        private GUIStyle _pillBg;
        private GUIStyle _pillText;
        private GUIStyle _menuButtonStyle;
        private Texture2D _texTopBar, _texPill, _texMenuBtn, _texMenuBtnHover;
        private bool _stylesBuilt = false;

        private void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world == null) return;

            _em = _world.EntityManager;
            _banksQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionResources>());

            _populationQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionPopulation>());

            _texTopBar = MakeTex(2, 2, new Color(0.06f, 0.08f, 0.18f, 0.92f));
            _texPill = MakeTex(2, 2, new Color(0.08f, 0.10f, 0.22f, 0.4f));

            RefreshNow();
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer >= refreshInterval)
            {
                _timer = 0f;
                RefreshNow();
            }
        }

        private void RefreshNow()
        {
            _cache.Clear();
            _popCache.Clear();

            var world = _world ?? EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            _em = world.EntityManager;

            // Get resources
            using var entities = _banksQuery.ToEntityArray(Allocator.Temp);
            using var tags = _banksQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var banks = _banksQuery.ToComponentDataArray<FactionResources>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                _cache[tags[i].Value] = banks[i];
            }

            // Get population
            using var popEntities = _populationQuery.ToEntityArray(Allocator.Temp);
            using var popTags = _populationQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var pops = _populationQuery.ToComponentDataArray<FactionPopulation>(Allocator.Temp);

            for (int i = 0; i < popEntities.Length; i++)
            {
                _popCache[popTags[i].Value] = (pops[i].Current, pops[i].Max);
            }
        }

        private void OnGUI()
        {
            if (!_stylesBuilt) BuildStyles();
            if (_cache.Count == 0) return;

            DrawAllFactionsTopBar();
        }

        private void BuildStyles()
        {
            _topBarBg = new GUIStyle { normal = { background = _texTopBar } };
            _pillBg = new GUIStyle { normal = { background = _texPill } };
            _pillText = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            // Menu button style (golden on dark, matching HUD theme)
            _texMenuBtn = MakeTex(2, 2, new Color(0.10f, 0.12f, 0.28f, 0.9f));
            _texMenuBtnHover = MakeTex(2, 2, new Color(0.15f, 0.18f, 0.38f, 0.95f));
            _menuButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.83f, 0.66f, 0.26f), background = _texMenuBtn },
                hover = { textColor = new Color(1f, 0.85f, 0.4f), background = _texMenuBtnHover },
                active = { textColor = Color.white, background = _texMenuBtnHover },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(6, 6, 4, 4)
            };

            _stylesBuilt = true;
        }

        private static Texture2D MakeTex(int w, int h, Color c)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = c;
            var t = new Texture2D(w, h);
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        private void DrawAllFactionsTopBar()
        {
            // Only show the local player's resources (not enemy factions)
            var localFaction = GameSettings.LocalPlayerFaction;
            if (!_cache.ContainsKey(localFaction)) return;

            DrawFactionBar(localFaction, 0f);

            IsPointerOverTopBar = UnityEngine.Input.mousePosition.y >= Screen.height - (topBarHeight + 4f);
        }

        private void DrawFactionBar(Faction faction, float yOffset)
        {
            if (!_cache.TryGetValue(faction, out var res)) return;

            int curPop = 0, maxPop = 0;
            if (_popCache.TryGetValue(faction, out var pop))
            {
                curPop = pop.current;
                maxPop = pop.max;
            }

            // Dark navy top bar
            var topBarRect = new Rect(0, yOffset, Screen.width, topBarHeight);
            GUI.color = Color.white;
            GUI.Box(topBarRect, "", _topBarBg);

            // Golden bottom border line
            var borderRect = new Rect(0, yOffset + topBarHeight - 2f, Screen.width, 2f);
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.7f);
            GUI.DrawTexture(borderRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Menu button (replaces the old "PLAYER" label)
            float menuBtnWidth = 80f;
            float menuBtnHeight = topBarHeight - 8f;
            if (GUI.Button(new Rect(leftPadding, yOffset + 4f, menuBtnWidth, menuBtnHeight),
                           "Menu", _menuButtonStyle))
            {
                InGameMenuPanel.Toggle();
            }

            // Resource pills
            float xPos = leftPadding + menuBtnWidth + pillSpacing;

            DrawResourcePill(xPos, yOffset, "Supplies", res.Supplies.ToString(), new Color(1f, 0.85f, 0.4f));
            xPos += 120f + pillSpacing;

            DrawResourcePill(xPos, yOffset, "Iron", res.Iron.ToString(), new Color(0.7f, 0.7f, 0.8f));
            xPos += 100f + pillSpacing;

            DrawResourcePill(xPos, yOffset, "Crystal", res.Crystal.ToString(), new Color(0.6f, 0.8f, 1f));
            xPos += 110f + pillSpacing;

            DrawResourcePill(xPos, yOffset, "Veilsteel", res.Veilsteel.ToString(), new Color(0.8f, 0.5f, 1f));
            xPos += 120f + pillSpacing;

            DrawResourcePill(xPos, yOffset, "Glow", res.Glow.ToString(), new Color(1f, 1f, 0.6f));
            xPos += 100f + pillSpacing;

            // Population
            string popText = $"{curPop}/{maxPop}";
            Color popColor = curPop >= maxPop ? new Color(1f, 0.3f, 0.3f) : new Color(0.6f, 1f, 0.6f);
            DrawResourcePill(xPos, yOffset, "Pop", popText, popColor);
        }

        private void DrawResourcePill(float x, float y, string label, string value, Color color)
        {
            var pillRect = new Rect(x, y + 4f, 110f, topBarHeight - 8f);
            GUI.Box(pillRect, "", _pillBg);

            var labelStyle = new GUIStyle(_pillText)
            {
                normal = { textColor = color },
                fontSize = 12
            };

            var valueStyle = new GUIStyle(_pillText)
            {
                normal = { textColor = Color.white },
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            var labelRect = new Rect(x + 6f, y + 3f, 100f, 14f);
            var valueRect = new Rect(x + 6f, y + 16f, 100f, 18f);

            GUI.Label(labelRect, label, labelStyle);
            GUI.Label(valueRect, value, valueStyle);
        }

    }
}
