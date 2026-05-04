// File: Assets/Scripts/UI/HUD/ResourceHUD.cs
// Resource Display — Dark Navy + Golden theme, bottom-left panel (height matches minimap)

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.Input;
using TheWaningBorder.UI;
using TheWaningBorder.UI.Common;
using EntityWorld = Unity.Entities.World;



namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// IMGUI-based resource HUD — dark navy panel with golden accents.
    /// Displays resources in the bottom-left corner, height matching the minimap (256px).
    /// Shows: Population, Religion Points, Supplies, Iron, Crystal, Veilsteel, Glow.
    /// </summary>
    public class ResourceHUD : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private Faction humanFaction = GameSettings.LocalPlayerFaction;
        [SerializeField] private float refreshInterval = 0.25f;

        // ── HUD bar constants (shared with EntityInfoPanel / EntityActionPanel) ──
        // Height and bottom margin match the minimap (256px, 20px offset).
        public const float HudBarHeight = 256f;
        public const float HudBottomMargin = 20f;
        public const float HudLeftMargin = 20f;
        public const float PanelGap = 6f;

        // Panel layout constants
        public const float PanelWidth = 200f;
        private const float PanelPadding = 8f;
        private const float RowHeight = 22f;
        private const float RowSpacing = 2f;
        private const float HeaderHeight = 26f;
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

        // Alanthor wall income — per-faction so observer mode can switch
        // between AI economies and still show the right value.
        private EntityQuery _wallIncomeQuery;
        private readonly Dictionary<Faction, float> _wallIncomePerFaction = new();

        // Local cached styles (no clean Styles.cs counterpart):
        // _valueStyle: bold-white right-aligned. _menuButtonStyle: bordered button with hover textures.
        private GUIStyle _valueStyle;
        private GUIStyle _menuButtonStyle;
        private Texture2D _texMenuBtn, _texMenuBtnHover;
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
                ComponentType.ReadOnly<FactionReligionPoints>());

            // Menu button textures use the canonical navy panel bg + a brighter hover variant.
            // Source the base color from Styles.PanelBgColor so the palette stays canonical.
            var menuBtnBg = new Color(Styles.PanelBgColor.r, Styles.PanelBgColor.g, Styles.PanelBgColor.b, 0.85f);
            _texMenuBtn = Styles.MakeSolid(menuBtnBg);
            _texMenuBtnHover = Styles.MakeSolid(new Color(0.12f, 0.14f, 0.28f, 0.9f));

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

            // Get religion points (task-063: FactionReligionPoints.Balance is the new source).
            using var rpEntities = _rpQuery.ToEntityArray(Allocator.Temp);
            using var rpTags = _rpQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var rpData = _rpQuery.ToComponentDataArray<FactionReligionPoints>(Allocator.Temp);

            for (int i = 0; i < rpEntities.Length; i++)
            {
                _rpCache[rpTags[i].Value] = rpData[i].Balance;
            }

            // Cache Alanthor wall enclosure income per-faction so the panel can
            // show the correct income for whoever the observer is currently
            // watching (DrawResourcePanel reads from _wallIncomePerFaction).
            _wallIncomePerFaction.Clear();
            using var wallEntities = _wallIncomeQuery.ToEntityArray(Allocator.Temp);
            using var wallTags = _wallIncomeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var wallIncomes = _wallIncomeQuery.ToComponentDataArray<SuppliesIncome>(Allocator.Temp);
            for (int i = 0; i < wallEntities.Length; i++)
            {
                var f = wallTags[i].Value;
                _wallIncomePerFaction.TryGetValue(f, out float prev);
                _wallIncomePerFaction[f] = prev + wallIncomes[i].PerMinute;
            }
        }

        private void OnGUI()
        {
            Styles.Initialize();
            if (!_stylesBuilt) BuildLocalStyles();

            // Always draw Menu button in top-left
            DrawMenuButton();

            if (_cache.Count == 0) return;

            DrawResourcePanel();

            // Show tooltip for resource icons on hover
            ResourceIcons.DrawTooltip();
        }

        // Build the truly-unique cached locals that have no Styles.cs counterpart.
        // Colors are sourced from Styles.HighlightColor where applicable so the palette
        // stays canonical (no inline navy/gold literals here except the brighter hover
        // tint which is a hover-state accent, not a palette base color).
        private void BuildLocalStyles()
        {
            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _menuButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _texMenuBtn, textColor = Styles.HighlightColor },
                hover = { background = _texMenuBtnHover, textColor = new Color(0.93f, 0.76f, 0.36f) },
                active = { background = _texMenuBtnHover, textColor = Color.white },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(6, 6, 4, 4)
            };

            _stylesBuilt = true;
        }

        /// <summary>
        /// In observer mode, follow the player's selection: if any selected
        /// entity has a FactionTag, show that faction's resources. Otherwise
        /// (and in non-observer modes) show the local player's. Lets the
        /// observer flip between AI economies just by clicking on their stuff.
        /// </summary>
        private Faction GetDisplayedFaction()
        {
            if (GameSettings.IsObserver)
            {
                var sel = SelectionSystem.CurrentSelection;
                if (sel != null && sel.Count > 0 && _world != null && _world.IsCreated)
                {
                    var em = _world.EntityManager;
                    for (int i = 0; i < sel.Count; i++)
                    {
                        var e = sel[i];
                        if (em.Exists(e) && em.HasComponent<FactionTag>(e))
                            return em.GetComponentData<FactionTag>(e).Value;
                    }
                }
            }
            return GameSettings.LocalPlayerFaction;
        }

        private void DrawResourcePanel()
        {
            var displayedFaction = GetDisplayedFaction();
            if (!_cache.ContainsKey(displayedFaction)) return;
            if (!_cache.TryGetValue(displayedFaction, out var res)) return;

            int curPop = 0, maxPop = 0;
            if (_popCache.TryGetValue(displayedFaction, out var pop))
            {
                curPop = pop.current;
                maxPop = pop.max;
            }

            int rp = 0;
            if (_rpCache.TryGetValue(displayedFaction, out var rpVal))
            {
                rp = rpVal;
            }

            // Panel dimensions — height matches minimap
            float panelX = HudLeftMargin;
            float panelY = Screen.height - HudBarHeight - HudBottomMargin;

            _panelRect = new Rect(panelX, panelY, PanelWidth, HudBarHeight);

            // Draw panel background
            GUI.color = Color.white;
            GUI.Box(_panelRect, "", Styles.PanelBox);

            // Golden border (top, bottom, left, right)
            // Kept inline: alpha 0.7 differs from Styles.HighlightColor's alpha 1.0.
            Color borderColor = new Color(0.83f, 0.66f, 0.26f, 0.7f);
            GUI.color = borderColor;
            GUI.DrawTexture(new Rect(panelX, panelY, PanelWidth, 2f), Texture2D.whiteTexture);                      // top
            GUI.DrawTexture(new Rect(panelX, panelY + HudBarHeight - 2f, PanelWidth, 2f), Texture2D.whiteTexture);  // bottom
            GUI.DrawTexture(new Rect(panelX, panelY, 2f, HudBarHeight), Texture2D.whiteTexture);                     // left
            GUI.DrawTexture(new Rect(panelX + PanelWidth - 2f, panelY, 2f, HudBarHeight), Texture2D.whiteTexture);  // right
            GUI.color = Color.white;

            // Header — in observer mode, show whose resources we're viewing
            // (tinted with their faction color) so the watcher can tell when a
            // selection click switched the panel to a different AI.
            float yPos = panelY + PanelPadding;
            string headerText = "RESOURCES";
            if (GameSettings.IsObserver)
                headerText = displayedFaction.ToString().ToUpperInvariant();
            var prevColor = GUI.color;
            if (GameSettings.IsObserver)
                GUI.color = FactionColors.Get(displayedFaction);
            GUI.Label(new Rect(panelX + PanelPadding, yPos, PanelWidth - PanelPadding * 2, HeaderHeight),
                      headerText, Styles.Header);
            GUI.color = prevColor;
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

            // Alanthor wall income — only meaningful for the displayed faction.
            float wallIncome = 0f;
            if (FactionColors.GetFactionCulture(displayedFaction) == Cultures.Alanthor)
                _wallIncomePerFaction.TryGetValue(displayedFaction, out wallIncome);
            if (wallIncome > 0f)
            {
                yPos += RowSpacing * 2;
                Color alanthorGreen = CultureConfig.AlanthorPrimary;
                DrawResourceRow(panelX, ref yPos, "Wall Income", $"+{wallIncome:F0}/min", alanthorGreen);
            }

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
            GUI.Box(rowRect, "", Styles.InnerRowBox);

            // Icon or text label (left-aligned)
            var icon = ResourceIcons.Get(label);
            if (icon != null)
            {
                float iconSize = RowHeight - 4f;
                float iconY = yPos + 2f;
                GUI.DrawTexture(new Rect(rowX + 4f, iconY, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
            }
            else
            {
                // Per-row text-color override on top of the canonical SmallLabel style.
                // The original code allocated a per-row GUIStyle here too; not introducing
                // new per-frame allocs.
                var styledLabel = new GUIStyle(Styles.SmallLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = labelColor }
                };
                GUI.Label(new Rect(rowX + 6f, yPos, rowWidth * 0.5f, RowHeight), label, styledLabel);
            }

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

        /// <summary>
        /// Returns the X coordinate where the next HUD panel should start (right edge of resources + gap).
        /// Used by EntityInfoPanel to know its left position.
        /// </summary>
        public static float NextPanelX => HudLeftMargin + PanelWidth + PanelGap;
    }
}
