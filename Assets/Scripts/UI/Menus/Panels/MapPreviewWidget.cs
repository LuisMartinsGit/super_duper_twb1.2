// MapPreviewWidget.cs
// uGUI diamond map preview shared by the Skirmish and Multiplayer panels:
// map name/tag header, the 45°-rotated thumbnail diamond with marker dots
// (player starts / resources / curse nodes), description, and a legend.
// All visuals are scene GameObjects (built once by MenuPanelsBuilder, then
// hand-editable); this component only fills them per selected map.

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
        public RawImage Diamond;          // thumbnail, inside the rotated square
        public GameObject DiamondGem;     // placeholder shown when no thumbnail
        public RectTransform MarkerLayer; // rotates with the diamond
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
                        : $"{info.PlayerCount}P · {info.SizeTag}";
            if (Description != null)
                Description.text = info != null && !string.IsNullOrEmpty(info.Description)
                    ? info.Description
                    : "A hand-authored theatre. Warband starts, resources, and " +
                      "border sites are placed by the map's own markers.";

            bool hasThumb = info != null && info.Thumbnail != null;
            if (Diamond != null)
            {
                Diamond.texture = hasThumb ? info.Thumbnail : null;
                Diamond.enabled = hasThumb; // a null RawImage texture draws white
            }
            if (DiamondGem != null) DiamondGem.SetActive(!hasThumb);

            ClearClones(MarkerLayer, MarkerTemplate);
            ClearClones(LegendContainer, LegendTemplate);
            if (info != null)
            {
                AddMarkerSet(info.PlayerStarts, StartsColor, "STARTS");
                AddMarkerSet(info.VeilstoneNodes, VeilstoneColor, "VEILSTONE");
                AddMarkerSet(info.VeilsteelNodes, VeilsteelColor, "VEILSTEEL");
                AddMarkerSet(info.IronDeposits, IronColor, "IRON");
                AddMarkerSet(info.CurseNodes, CurseColor, "CURSE");
            }
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
                if (text != null) text.text = $"{label} ({points.Length})";
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
