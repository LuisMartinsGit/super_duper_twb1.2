// MenuToggleSwitch.cs
// Sliding on/off switch built from Synty "Interface Fantasy Menus" parts:
// a pill track, a gold pill outline, and a gem knob that slides between the
// two ends. Location: Assets/Scripts/UI/Menus/MenuToggleSwitch.cs

using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// The visual half of a boolean menu option. The CLICK still belongs to a
    /// plain <see cref="Button"/> on the same GameObject - the skirmish screen
    /// binds to that Button and owns the state - so this component only paints
    /// what the owner tells it via <see cref="SetOn"/>.
    ///
    /// Keeping the Button means the scene's existing FogToggle / CurseToggle
    /// references stay valid; swapping in a uGUI Toggle would have rebound
    /// every field for no gain, since a menu option that lives in a static
    /// config has no use for Toggle's own isOn state.
    ///
    /// The sprites are stored on the component as well as applied to the child
    /// images. That is what lets the OBSERVER option - which is constructed in
    /// code, not authored - build a switch with the same art without depending
    /// on the authored cell's structure, and without a Resources folder (the
    /// Synty art is editor-only asset-path content).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuToggleSwitch : MonoBehaviour
    {
        public const string FillNode = "TrackFill";
        public const string OutlineNode = "TrackOutline";
        public const string KnobNode = "Knob";

        [Header("Parts")]
        public Image Fill;
        public Image Outline;
        public RectTransform Knob;
        public Image KnobImage;

        [Header("Art (borrowed when a switch is built at runtime)")]
        public Sprite TrackSprite;
        public Sprite OutlineSprite;
        public Sprite KnobSprite;

        [Header("Shape")]
        [Tooltip("Knob width in canvas pixels; its height follows the track.")]
        public float KnobWidth = 34f;
        [Tooltip("Gap between the knob and the end of the track.")]
        public float KnobInset = 5f;
        [Tooltip("Gap above and below the knob.")]
        public float KnobPadding = 5f;

        // Palette - the menu's gold, dimmed to a cold steel when off.
        public static readonly Color FillOn     = new Color(0.282f, 0.204f, 0.075f, 0.95f);
        public static readonly Color FillOff    = new Color(0.075f, 0.110f, 0.137f, 0.95f);
        public static readonly Color OutlineOn  = new Color(0.910f, 0.722f, 0.290f);
        public static readonly Color OutlineOff = new Color(0.420f, 0.345f, 0.180f);
        public static readonly Color KnobOn     = new Color(0.980f, 0.800f, 0.360f);
        public static readonly Color KnobOff    = new Color(0.400f, 0.455f, 0.510f);

        /// <summary>Paint the switch for the given state and park the knob.</summary>
        public void SetOn(bool on)
        {
            if (Fill != null) Fill.color = on ? FillOn : FillOff;
            if (Outline != null) Outline.color = on ? OutlineOn : OutlineOff;
            if (KnobImage != null) KnobImage.color = on ? KnobOn : KnobOff;
            if (Knob == null) return;

            // Anchored to whichever END the knob belongs at, and stretched
            // vertically, so the knob keeps its place and its height no matter
            // what the layout does to the track.
            float x = on ? 1f : 0f;
            Knob.anchorMin = new Vector2(x, 0f);
            Knob.anchorMax = new Vector2(x, 1f);
            Knob.pivot = new Vector2(0.5f, 0.5f);
            Knob.sizeDelta = new Vector2(KnobWidth, -2f * KnobPadding);

            float offset = KnobWidth * 0.5f + KnobInset;
            Knob.anchoredPosition = new Vector2(on ? -offset : offset, 0f);
        }

        /// <summary>
        /// Create-or-update the switch on <paramref name="track"/>. Idempotent:
        /// the three child images are found by name and reused, so running this
        /// over an already-built switch only refreshes it.
        ///
        /// Used by both the editor dressing pass (sprites from AssetDatabase)
        /// and the runtime OBSERVER option (sprites borrowed off another
        /// switch), which is what keeps the two looking the same.
        /// </summary>
        public static MenuToggleSwitch Attach(RectTransform track,
            Sprite trackSprite, Sprite outlineSprite, Sprite knobSprite,
            float knobWidth, float knobInset, float knobPadding)
        {
            if (track == null) return null;

            var sw = track.GetComponent<MenuToggleSwitch>();
            if (sw == null) sw = track.gameObject.AddComponent<MenuToggleSwitch>();

            sw.TrackSprite = trackSprite;
            sw.OutlineSprite = outlineSprite;
            sw.KnobSprite = knobSprite;
            sw.KnobWidth = knobWidth;
            sw.KnobInset = knobInset;
            sw.KnobPadding = knobPadding;

            // The track's own Image is the Button's targetGraphic and takes the
            // Button's hover tint, so it must not carry the art - it goes fully
            // transparent and the fill rides underneath as a child instead.
            var own = track.GetComponent<Image>();
            if (own != null) own.color = new Color(1f, 1f, 1f, 0f);

            sw.Fill = Stretched(track, FillNode, trackSprite, 0);
            sw.Outline = Stretched(track, OutlineNode, outlineSprite, 1);

            var knob = track.Find(KnobNode) as RectTransform;
            if (knob == null)
            {
                var go = new GameObject(KnobNode, typeof(RectTransform), typeof(Image));
                knob = (RectTransform)go.transform;
                knob.SetParent(track, false);
            }
            knob.SetSiblingIndex(2);
            sw.Knob = knob;
            sw.KnobImage = knob.GetComponent<Image>();
            if (sw.KnobImage == null) sw.KnobImage = knob.gameObject.AddComponent<Image>();
            sw.KnobImage.sprite = knobSprite;
            sw.KnobImage.type = Image.Type.Simple;
            sw.KnobImage.preserveAspect = true;
            sw.KnobImage.raycastTarget = false;

            sw.SetOn(false);
            return sw;
        }

        /// <summary>A raycast-transparent sliced image filling the track.</summary>
        private static Image Stretched(RectTransform parent, string name, Sprite sprite, int index)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
            }
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetSiblingIndex(index);

            var img = rt.GetComponent<Image>();
            if (img == null) img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            // Menu_Button_17 carries a full four-way slice border, so the pill
            // keeps its corners at any size the layout gives it.
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }
    }
}
