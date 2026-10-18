// MenuBackgroundFit.cs
// Makes a menu scene's full-screen art COVER the window at any aspect ratio
// (2026-10-16). The Skirmish and Multiplayer scenes authored SPR_Screenshot as
// a fixed-width, height-stretched Image with preserveAspect, so the art was
// always fitted by HEIGHT: bars down both sides on anything wider than ~16:9,
// and at 4:3 the panels sat over a cropped strip. Envelope fitting crops the
// overflow instead, the way a background should behave.

using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus
{
    public static class MenuBackgroundFit
    {
        /// <summary>Fit the scene's background art (by name) to cover its
        /// parent. Safe to call more than once.</summary>
        public static void Apply(string objectName = "SPR_Screenshot")
        {
            var go = GameObject.Find(objectName);
            if (go == null) return;
            var img = go.GetComponent<Image>();
            if (img == null || img.sprite == null) return;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            img.preserveAspect = false;   // the fitter keeps the aspect now

            var fitter = go.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = go.AddComponent<AspectRatioFitter>();
            var r = img.sprite.rect;
            fitter.aspectRatio = r.width / Mathf.Max(1f, r.height);
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        }

        /// <summary>
        /// The lobby plates' dark fills (RosterPlate, MapPreview, TheatreBar —
        /// each a child named "Background") were authored a FIXED 1728 wide,
        /// pinned top-left, while the plates themselves stretch with the
        /// window. Off 16:9 the fill stopped short of the gold frame, leaving
        /// a bare strip on the right of every plate. Each one is made to fill
        /// its plate exactly.
        /// </summary>
        public static void StretchPlateBackgrounds(Transform root, params string[] plates)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (t.name != "Background" || t.parent == null) continue;
                if (System.Array.IndexOf(plates, t.parent.name) < 0) continue;
                t.anchorMin = Vector2.zero;
                t.anchorMax = Vector2.one;
                t.pivot = new Vector2(0.5f, 0.5f);
                t.offsetMin = Vector2.zero;
                t.offsetMax = Vector2.zero;
            }
        }

        /// <summary>The lobby plates every lobby screen shares.</summary>
        public static readonly string[] LobbyPlates = { "RosterPlate", "MapPreview", "TheatreBar" };
    }
}
