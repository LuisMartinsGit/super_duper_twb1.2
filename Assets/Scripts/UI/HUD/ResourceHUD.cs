// File: Assets/Scripts/UI/HUD/ResourceHUD.cs
// Resource Display — Dark Navy + Golden theme, bottom-left vertical panel

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Settings;
using EntityWorld = Unity.Entities.World;


namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// IMGUI-based resource HUD — dark navy panel with golden accents.
    /// Displays resources in a compact vertical panel in the bottom-left corner.
    /// Shows: Population, Religion Points, Supplies, Iron, Crystal, Veilsteel, Glow.
    /// </summary>
    public class ResourceHUD : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private Faction humanFaction = GameSettings.LocalPlayerFaction;
        [SerializeField] private float refreshInterval = 0.25f;

        // Panel layout constants
        private const float PanelWidth = 200f;
        private const float PanelPadding = 8f;
        private const float RowHeight = 22f;
        private const float RowSpacing = 2f;
        private const float HeaderHeight = 26f;
        private const float BottomMargin = 10f;
        private const float LeftMargin = 10f;
        private const int ResourceRowCount = 7; // Pop, RP, Supplies, Iron, Crystal, Veilsteel, Glow

        /// <summary>Returns true if the mouse is over the resource panel.</summary>
        public static bool IsPointerOverResourcePanel { get; private set; }

        // Keep the old name as well so any future references still compile
        public static bool IsPointerOverTopBar => IsPointerOverResourcePanel;

        private EntityWorld _world;
        private EntityManager _em;
        private EntityQuery _banksQuery;
        private EntityQuery _populationQuery;
        private EntityQuery _rpQuery;

        private readonly Dictionary<Faction, FactionResources> _cache = new();
        private readonly Dictionary<Faction, (int current, int max)> _popCache = new();
        private readonly Dictionary<Faction, int> _rpCache = new();
        private float _timer;

        // Menu button constants
        private const float MenuBtnWidth = 80f;
        private const float MenuBtnHeight = 30f;
        private const float MenuBtnMargin = 10f;

        // Alanthor wall income
        private EntityQuery _wallIncomeQuery;
        private float _wallIncome;

        // Styles
        private GUIStyle _panelBg;
        private GUIStyle _rowBg;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _menuButtonStyle;
        private Texture2D _texPanel, _texRow, _texMenuBtn, _texMenuBtnHover;
        private bool _stylesBuilt = false;

        // Cached panel rect for pointer detection
        private Rect _panelRect;

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

            _rpQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<ReligionPoints>());

            _texPanel = MakeTex(2, 2, new Color(0.06f, 0.08f, 0.18f, 0.92f));
            _texRow = MakeTex(2, 2, new Color(0.08f, 0.10f, 0.22f, 0.4f));
            _texMenuBtn = MakeTex(2, 2, new Color(0.06f, 0.08f, 0.18f, 0.85f));
            _texMenuBtnHover = MakeTex(2, 2, new Color(0.12f, 0.14f, 0.28f, 0.9f));

            // Alanthor wall income query
            _wallIncomeQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<WallEnclosureIncomeTag>(),
                ComponentType.ReadOnly<SuppliesIncome>(),
                ComponentType.ReadOnly<FactionTag>());

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
            _rpCache.Clear();

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

            // Get religion points
            using var rpEntities = _rpQuery.ToEntityArray(Allocator.Temp);
            using var rpTags = _rpQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var rpData = _rpQuery.ToComponentDataArray<ReligionPoints>(Allocator.Temp);

            for (int i = 0; i < rpEntities.Length; i++)
            {
                _rpCache[rpTags[i].Value] = rpData[i].Value;
            }

            // Get Alanthor wall enclosure income
            _wallIncome = 0f;
            var localFaction = GameSettings.LocalPlayerFaction;
            if (CultureConfig.GetFactionCulture(localFaction) == Cultures.Alanthor)
            {
                using var wallEntities = _wallIncomeQuery.ToEntityArray(Allocator.Temp);
                using var wallTags = _wallIncomeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var wallIncomes = _wallIncomeQuery.ToComponentDataArray<SuppliesIncome>(Allocator.Temp);
                for (int i = 0; i < wallEntities.Length; i++)
                {
                    if (wallTags[i].Value == localFaction)
                        _wallIncome += wallIncomes[i].PerMinute;
                }
            }
        }

        private void OnGUI()
        {
            if (!_stylesBuilt) BuildStyles();

            // Always draw Menu button in top-left
            DrawMenuButton();

            if (_cache.Count == 0) return;

            DrawResourcePanel();

            // Alanthor wall income sub-bar
            if (_wallIncome > 0f)
                DrawWallIncomeBar();
        }

        private void BuildStyles()
        {
            _panelBg = new GUIStyle { normal = { background = _texPanel } };
            _rowBg = new GUIStyle { normal = { background = _texRow } };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.83f, 0.66f, 0.26f) }
            };

            _menuButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texMenuBtn, textColor = new Color(0.83f, 0.66f, 0.26f) },
                hover = { background = _texMenuBtnHover, textColor = new Color(0.93f, 0.76f, 0.36f) },
                active = { background = _texMenuBtnHover, textColor = Color.white }
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

        private void DrawResourcePanel()
        {
            var localFaction = GameSettings.LocalPlayerFaction;
            if (!_cache.ContainsKey(localFaction)) return;
            if (!_cache.TryGetValue(localFaction, out var res)) return;

            int curPop = 0, maxPop = 0;
            if (_popCache.TryGetValue(localFaction, out var pop))
            {
                curPop = pop.current;
                maxPop = pop.max;
            }

            int rp = 0;
            if (_rpCache.TryGetValue(localFaction, out var rpVal))
            {
                rp = rpVal;
            }

            // Calculate panel dimensions
            float panelHeight = PanelPadding + HeaderHeight +
                                (ResourceRowCount * (RowHeight + RowSpacing)) +
                                PanelPadding;

            float panelX = LeftMargin;
            float panelY = Screen.height - panelHeight - BottomMargin;

            _panelRect = new Rect(panelX, panelY, PanelWidth, panelHeight);

            // Draw panel background
            GUI.color = Color.white;
            GUI.Box(_panelRect, "", _panelBg);

            // Golden border (top, bottom, left, right)
            Color borderColor = new Color(0.83f, 0.66f, 0.26f, 0.7f);
            GUI.color = borderColor;
            GUI.DrawTexture(new Rect(panelX, panelY, PanelWidth, 2f), Texture2D.whiteTexture);                      // top
            GUI.DrawTexture(new Rect(panelX, panelY + panelHeight - 2f, PanelWidth, 2f), Texture2D.whiteTexture);   // bottom
            GUI.DrawTexture(new Rect(panelX, panelY, 2f, panelHeight), Texture2D.whiteTexture);                      // left
            GUI.DrawTexture(new Rect(panelX + PanelWidth - 2f, panelY, 2f, panelHeight), Texture2D.whiteTexture);   // right
            GUI.color = Color.white;

            // Header
            float yPos = panelY + PanelPadding;
            GUI.Label(new Rect(panelX + PanelPadding, yPos, PanelWidth - PanelPadding * 2, HeaderHeight),
                      "RESOURCES", _headerStyle);
            yPos += HeaderHeight;

            // Resource rows: Pop, RP, Supplies, Iron, Crystal, Veilsteel, Glow
            string popText = $"{curPop}/{maxPop}";
            Color popColor = curPop >= maxPop ? new Color(1f, 0.3f, 0.3f) : new Color(0.6f, 1f, 0.6f);

            DrawResourceRow(panelX, ref yPos, "Pop", popText, popColor);
            DrawResourceRow(panelX, ref yPos, "RP", rp.ToString(), new Color(1f, 0.75f, 0.3f));
            DrawResourceRow(panelX, ref yPos, "Supplies", res.Supplies.ToString(), new Color(1f, 0.85f, 0.4f));
            DrawResourceRow(panelX, ref yPos, "Iron", res.Iron.ToString(), new Color(0.7f, 0.7f, 0.8f));
            DrawResourceRow(panelX, ref yPos, "Crystal", res.Crystal.ToString(), new Color(0.6f, 0.8f, 1f));
            DrawResourceRow(panelX, ref yPos, "Veilsteel", res.Veilsteel.ToString(), new Color(0.8f, 0.5f, 1f));
            DrawResourceRow(panelX, ref yPos, "Glow", res.Glow.ToString(), new Color(1f, 1f, 0.6f));

            // Pointer detection — convert mouse position from bottom-left origin to GUI top-left origin
            Vector2 mousePos = UnityEngine.Input.mousePosition;
            float guiMouseY = Screen.height - mousePos.y;
            IsPointerOverResourcePanel = _panelRect.Contains(new Vector2(mousePos.x, guiMouseY));
        }

        private void DrawResourceRow(float panelX, ref float yPos, string label, string value, Color labelColor)
        {
            float rowX = panelX + PanelPadding;
            float rowWidth = PanelWidth - PanelPadding * 2;

            // Row background
            var rowRect = new Rect(rowX, yPos, rowWidth, RowHeight);
            GUI.Box(rowRect, "", _rowBg);

            // Label (left-aligned, colored)
            var styledLabel = new GUIStyle(_labelStyle)
            {
                normal = { textColor = labelColor }
            };
            GUI.Label(new Rect(rowX + 6f, yPos, rowWidth * 0.5f, RowHeight), label, styledLabel);

            // Value (right-aligned, white)
            GUI.Label(new Rect(rowX + rowWidth * 0.4f, yPos, rowWidth * 0.55f, RowHeight), value, _valueStyle);

            yPos += RowHeight + RowSpacing;
        }

        private void DrawMenuButton()
        {
            var btnRect = new Rect(MenuBtnMargin, MenuBtnMargin, MenuBtnWidth, MenuBtnHeight);
            if (GUI.Button(btnRect, "Menu", _menuButtonStyle))
            {
                InGameMenuPanel.Toggle();
            }
        }

        private void DrawWallIncomeBar()
        {
            // Small sub-bar below the resource panel
            float barX = LeftMargin;
            float barY = _panelRect.yMax + 4f;
            float barW = PanelWidth;
            float barH = 24f;

            GUI.Box(new Rect(barX, barY, barW, barH), "", _panelBg);
            Color alanthorGreen = CultureConfig.AlanthorPrimary;
            var wallStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 11,
                normal = { textColor = alanthorGreen }
            };
            GUI.Label(new Rect(barX + PanelPadding, barY, barW * 0.6f, barH),
                      "Wall Income", wallStyle);
            var wallValStyle = new GUIStyle(_valueStyle) { fontSize = 11 };
            GUI.Label(new Rect(barX + barW * 0.4f, barY, barW * 0.55f, barH),
                      $"+{_wallIncome:F0}/min", wallValStyle);
        }
    }
}
