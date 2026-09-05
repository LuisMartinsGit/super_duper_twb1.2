// MapPreviewWidget.cs
// uGUI map preview shared by the Skirmish and Multiplayer panels: map name/tag
// header, the thumbnail with marker dots (player starts / resources / curse
// nodes), description, and a legend.
// All visuals are scene GameObjects (built once by MenuPanelsBuilder, then
// hand-editable); this component only fills them per selected map.
//
// Marker placement is rotation-agnostic by construction: a dot's anchorMin ==
// anchorMax == the map's normalized (x, y), inside a MarkerLayer stretched over
// the very same rect the thumbnail fills. Dots therefore track the thumbnail
// whatever the preview's transform does. The Skirmish preview is an upright
// square as of 2026-08-18; the Multiplayer one is still the 45° diamond, and
// both place their markers correctly off the same code.
//
// The node names still say "Diamond" — the scenes bind the fields below to
// those objects, and renaming them would be churn for nothing.

using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class MapPreviewWidget : MonoBehaviour
    {
        [Header("Header")]
        public TMP_Text MapName;
        public TMP_Text MapTag;

        [Header("Preview")]
        public RawImage Diamond;          // the thumbnail itself
        public GameObject DiamondGem;     // placeholder shown when no thumbnail
        public RectTransform MarkerLayer; // stretched over the thumbnail's rect
        public GameObject MarkerTemplate; // inactive dot (Image), cloned per marker

        [Header("Text")]
        public TMP_Text Description;
        public RectTransform LegendContainer;
        public GameObject LegendTemplate; // inactive: "Swatch" Image + TMP label

        public static readonly Color StartsColor    = new Color(0.910f, 0.722f, 0.290f);
        public static readonly Color VeilstoneColor = new Color(0.247f, 0.749f, 0.604f);
        public static readonly Color VeilsteelColor = new Color(0.620f, 0.470f, 0.860f);
        public static readonly Color IronColor      = new Color(0.720f, 0.750f, 0.780f);
        public static readonly Color CurseColor     = new Color(0.850f, 0.300f, 0.250f);

        /// <summary>Fill every part of the widget for the given MapRegistry index.</summary>
        public void Show(int mapIndex)
        {
            var maps = MapRegistry.Maps;
            if (maps.Count == 0) return;
            mapIndex = Mathf.Clamp(mapIndex, 0, maps.Count - 1);
            var info = MapInfoIndex.For(maps[mapIndex].SceneName);

            if (MapName != null)
                MapName.text = DisplayName(mapIndex).ToUpperInvariant();
            if (MapTag != null)
                MapTag.text = info == null ? "2–8P"
                    : string.IsNullOrEmpty(info.SizeTag)
                        ? $"{info.PlayerCount}P"
                        : $"{info.PlayerCount}P · {Loc.T(info.SizeTag)}";
            if (Description != null)
                Description.text = info != null && !string.IsNullOrEmpty(info.Description)
                    ? Loc.T(info.Description)
                    : Loc.T("A hand-authored theatre. Warband starts, resources, and " +
                            "border sites are placed by the map's own markers.");

            bool hasThumb = info != null && info.Thumbnail != null;
            if (Diamond != null)
            {
                Diamond.texture = hasThumb ? info.Thumbnail : null;
                Diamond.enabled = hasThumb; // a null RawImage texture draws white
            }
            if (DiamondGem != null) DiamondGem.SetActive(!hasThumb);

            WarnOnMissingWiring();

            ClearClones(MarkerLayer, MarkerTemplate);
            ClearClones(LegendContainer, LegendTemplate);
            if (info != null)
            {
                AddStartMarkers(info.PlayerStarts);
                AddMarkerSet(info.VeilstoneNodes, VeilstoneColor, "VEILSTONE");
                AddMarkerSet(info.VeilsteelNodes, VeilsteelColor, "VEILSTEEL");
                AddMarkerSet(info.IronDeposits, IronColor, "IRON");
                AddMarkerSet(info.CurseNodes, CurseColor, "CURSE");
            }
        }

        private bool _warned;

        /// <summary>
        /// Say so when a slot is unassigned, once per widget.
        ///
        /// Every consumer below is null-guarded, which means a reference
        /// cleared by a scene edit does not throw - that part of the preview
        /// simply stops appearing, with nothing in the console to say why. The
        /// legend went missing exactly like that and took a scene diff to find.
        /// </summary>
        private void WarnOnMissingWiring()
        {
            if (_warned) return;
            _warned = true;

            string missing = "";
            if (MapName == null) missing += " MapName";
            if (MapTag == null) missing += " MapTag";
            if (Diamond == null) missing += " Diamond";
            if (MarkerLayer == null) missing += " MarkerLayer";
            if (MarkerTemplate == null) missing += " MarkerTemplate";
            if (Description == null) missing += " Description";
            if (LegendContainer == null) missing += " LegendContainer";
            if (LegendTemplate == null) missing += " LegendTemplate";

            if (missing.Length == 0) return;
            Debug.LogWarning($"[MapPreviewWidget] '{name}' has unassigned field(s):{missing}. " +
                             "Those parts of the map preview will silently not render - " +
                             "re-assign them in the Inspector.", this);
        }

        public static string DisplayName(int mapIndex)
        {
            var maps = MapRegistry.Maps;
            var info = MapInfoIndex.For(maps[mapIndex].SceneName);
            return info != null && !string.IsNullOrEmpty(info.DisplayName)
                ? info.DisplayName
                : maps[mapIndex].DisplayName;
        }

        public static int MaxPlayers(int mapIndex)
        {
            var maps = MapRegistry.Maps;
            var info = MapInfoIndex.For(maps[Mathf.Clamp(mapIndex, 0, maps.Count - 1)].SceneName);
            return info != null ? Mathf.Clamp(info.PlayerCount, 2, 8) : 8;
        }

        // ── Start-position assignment (docs/Design/Lobby_Setup.md) ──────────
        //
        // Start dots become clickable so the lobby can place players on
        // specific spawns, BFME2 / Supreme Commander style. The widget stays
        // dumb: it renders what the owner tells it and reports clicks back.
        // The panel owns the roster and decides what a click means.

        /// <summary>
        /// Per-start display state supplied by the owning panel.
        /// <paramref name="holderLabel"/> is empty when the start is free.
        /// </summary>
        public delegate void StartStateProvider(int startIndex, out Color tint, out string holderLabel);

        /// <summary>Set to enable click-to-assign on the start dots.</summary>
        public StartStateProvider StartState;

        /// <summary>Raised with the start index the player clicked.</summary>
        public System.Action<int> OnStartClicked;

        private readonly System.Collections.Generic.List<GameObject> _startDots = new();

        /// <summary>
        /// Re-tint and re-label the start dots without rebuilding the preview.
        /// Called whenever a slot's colour or start assignment changes.
        /// </summary>
        public void RefreshStartMarkers()
        {
            for (int i = 0; i < _startDots.Count; i++)
                ApplyStartState(_startDots[i], i);
        }

        private void AddStartMarkers(Vector2[] points)
        {
            _startDots.Clear();

            if (points == null || points.Length == 0) return;

            if (MarkerLayer != null && MarkerTemplate != null)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    var dot = Instantiate(MarkerTemplate, MarkerLayer);
                    dot.SetActive(true);
                    var rt = (RectTransform)dot.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(points[i].x, points[i].y);
                    rt.anchoredPosition = Vector2.zero;

                    // Start dots read larger than resource dots — they are the
                    // interactive element and need a comfortable click target.
                    rt.sizeDelta = MarkerTemplate.GetComponent<RectTransform>().sizeDelta * 2.2f;

                    _startDots.Add(dot);

                    if (StartState != null)
                    {
                        var img = dot.GetComponent<Image>();
                        if (img != null) img.raycastTarget = true;

                        var btn = dot.GetComponent<Button>() ?? dot.AddComponent<Button>();
                        btn.targetGraphic = img;
                        int captured = i;
                        btn.onClick.AddListener(() => OnStartClicked?.Invoke(captured));
                    }

                    ApplyStartState(dot, i);
                }
            }

            AddLegendEntry(StartsColor, "STARTS", points.Length);
        }

        /// <summary>Tint + number one start dot from the owner's state provider.</summary>
        private void ApplyStartState(GameObject dot, int startIndex)
        {
            if (dot == null) return;

            Color tint = StartsColor;
            string holder = string.Empty;
            StartState?.Invoke(startIndex, out tint, out holder);

            var img = dot.GetComponent<Image>();
            if (img != null) img.color = tint;

            // Number label lives as a runtime child — the scene's marker
            // template is a bare dot with no text.
            var labelTf = dot.transform.Find("StartLabel");
            TMP_Text label;
            if (labelTf == null)
            {
                var go = new GameObject("StartLabel", typeof(RectTransform));
                go.transform.SetParent(dot.transform, false);
                var lrt = (RectTransform)go.transform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                label = go.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                label.enableAutoSizing = true;
                label.fontSizeMin = 6f;
                label.fontSizeMax = 40f;
                // Keep the number upright whatever the preview does. The
                // Skirmish preview is an un-rotated square now, but the
                // Multiplayer one is still the 45-degree diamond, and a start
                // number tipped onto its corner is unreadable.
                //
                // Setting WORLD rotation, not local. The old line inverted
                // MarkerLayer.localRotation, which is identity - the 45 degrees
                // live on MarkerLayer's PARENT - so it corrected nothing.
                lrt.rotation = Quaternion.identity;
            }
            else label = labelTf.GetComponent<TMP_Text>();

            if (label != null)
            {
                label.text = string.IsNullOrEmpty(holder) ? (startIndex + 1).ToString() : holder;
                // White, like every other label on this screen. These numbers
                // sit ON a coloured dot rather than on the panel, so they used
                // to pick black or white per dot for contrast (ReadableOn).
                // Watch the pale player colours - white on the White faction's
                // dot has nothing to read against; put ReadableOn(tint) back
                // here if that turns out to matter in a real lobby.
                label.color = Color.white;
            }
        }

        /// <summary>
        /// Black or white, whichever stays legible on the given fill.
        /// Currently unused - the start numbers are plain white with the rest
        /// of the screen. Kept deliberately: it is the one-line fix if a pale
        /// player colour turns out to swallow its number.
        /// </summary>
        private static Color ReadableOn(Color fill)
        {
            float luma = 0.299f * fill.r + 0.587f * fill.g + 0.114f * fill.b;
            return luma > 0.55f ? new Color(0.08f, 0.09f, 0.10f) : Color.white;
        }

        private void AddLegendEntry(Color color, string label, int count)
        {
            if (LegendContainer == null || LegendTemplate == null) return;
            var item = Instantiate(LegendTemplate, LegendContainer);
            item.SetActive(true);
            var swatch = item.transform.Find("Swatch");
            if (swatch != null && swatch.TryGetComponent(out Image swatchImg))
                swatchImg.color = color;
            var text = item.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = $"{Loc.T(label)} ({count})";
        }

        private void AddMarkerSet(Vector2[] points, Color color, string label)
        {
            if (points == null || points.Length == 0) return;

            if (MarkerLayer != null && MarkerTemplate != null)
            {
                foreach (var p in points)
                {
                    var dot = Instantiate(MarkerTemplate, MarkerLayer);
                    dot.SetActive(true);
                    var img = dot.GetComponent<Image>();
                    if (img != null) img.color = color;
                    var rt = (RectTransform)dot.transform;
                    // Normalized map coords: x = west->east, y = south->north.
                    // uGUI anchors are bottom-up, so y maps directly.
                    rt.anchorMin = rt.anchorMax = new Vector2(p.x, p.y);
                    rt.anchoredPosition = Vector2.zero;
                }
            }

            if (LegendContainer != null && LegendTemplate != null)
            {
                var item = Instantiate(LegendTemplate, LegendContainer);
                item.SetActive(true);
                var swatch = item.transform.Find("Swatch");
                if (swatch != null && swatch.TryGetComponent(out Image swatchImg))
                    swatchImg.color = color;
                var text = item.GetComponentInChildren<TMP_Text>(true);
                if (text != null) text.text = $"{Loc.T(label)} ({points.Length})";
            }
        }

        // Remove every previously-cloned child, leaving the inactive template.
        private static void ClearClones(RectTransform container, GameObject template)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
                if (child != template) Destroy(child);
            }
        }
    }
}
