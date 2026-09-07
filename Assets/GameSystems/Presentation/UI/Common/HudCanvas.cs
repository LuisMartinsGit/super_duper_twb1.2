// HudCanvas.cs
// How the HUD maps onto whatever shape the player's screen actually is.
//
// Every in-match overlay is AUTHORED against a 16:9 frame — the GameUI
// staging scene is 3840x2160, StatsBoardHUD 1280x720 — and the player's
// monitor is not obliged to agree. A 32:9 ultrawide (5120x1440) is the case
// that breaks a naively configured HUD, and this file is the single place
// that stops it.
//
// Two things go wrong on a very wide screen, and there is one fix for each.
//
// 1. THE SCALER. A CanvasScaler set to MatchWidthOrHeight with match = 0
//    scales by WIDTH alone: scaleFactor = Screen.width / reference.x. At
//    5120x1440 against a 3840-wide reference that is 1.333, so the canvas
//    keeps its 3840 reference units of width but its height collapses from
//    2160 to 1440/1.333 = 1080 — HALF the frame everything was authored in.
//    Panels double in size relative to the screen (the bottom-left dock went
//    from 30% of screen height to 61%) and anything sitting high in the
//    frame falls off the bottom edge. Expand instead takes scaleFactor =
//    min(w/ref.x, h/ref.y), so the canvas is never SMALLER than the authored
//    frame on either axis: a layout that fits 3840x2160 fits every screen,
//    ultrawide and 4:3 alike. On 32:9 the extra width becomes empty middle,
//    which is what an RTS wants anyway.
//
// 2. THE ANCHORS. Expand alone is not enough: it widens the canvas, so a
//    panel anchored to the CENTRE drifts away from the edge it was authored
//    against. The minimap was authored anchored (0.5, 0.5) and nudged
//    +1896/-609 to reach the bottom-right corner of the 3840x2160 frame; on
//    a 32:9 canvas that offset leaves it stranded in open space, and before
//    the scaler fix it put the whole panel below the bottom edge — invisible.
//    AnchorToScreenEdges re-expresses each authored placement as a distance
//    from the screen edge it was authored nearest, which is what the author
//    meant by nudging it there in the first place.
//
// Panels already anchored to the right edge (Religion), to the top-left
// (Objectives) or genuinely centred come out of that pass UNCHANGED — it
// reads the authored intent rather than imposing a layout of its own.

using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Common
{
    /// <summary>
    /// Aspect-ratio safety for screen-space HUD canvases. See the file header
    /// for why each half exists.
    /// </summary>
    public static class HudCanvas
    {
        /// <summary>
        /// The frame the GameUI staging scene — and so every panel prefab it
        /// produces — is authored in.
        /// </summary>
        public static readonly Vector2 StagingReference = new Vector2(3840f, 2160f);

        /// <summary>
        /// A panel belongs to an EDGE when its centre falls in the outer third
        /// of the authored frame on that axis, and is treated as centred
        /// otherwise. Thirds rather than halves, so a panel parked near the
        /// middle is not yanked to an edge by a few pixels of drift.
        /// </summary>
        private const float EdgeBand = 1f / 3f;

        /// <summary>
        /// Point the scaler at <paramref name="reference"/> in the one mode
        /// that cannot lose authored frame on any aspect ratio.
        /// </summary>
        public static void Configure(CanvasScaler scaler, Vector2 reference)
        {
            if (scaler == null) return;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // Unused by Expand, but a stale 0 here is the first thing the next
            // reader will check when a panel looks wrong. Leave it agreeing
            // with the mode: Expand is the min of both axes, so neither one
            // wins outright.
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>
        /// The scale factor <see cref="Configure"/> produces for the current
        /// screen — canvas units to screen pixels.
        ///
        /// Derived rather than read off a live Canvas, so a caller that needs
        /// it before the canvas exists (or from a static property) gets the
        /// same answer. This is the exact rule Unity applies for Expand.
        /// </summary>
        public static float ScaleFactor(Vector2 reference)
        {
            if (reference.x <= 0f || reference.y <= 0f) return 1f;
            return Mathf.Min(Screen.width / reference.x, Screen.height / reference.y);
        }

        /// <summary>
        /// Re-express a panel's authored placement as an offset from the
        /// screen edge — or centre line — it was authored nearest, so it holds
        /// that relationship on any aspect ratio.
        ///
        /// <paramref name="panel"/> must be a direct child of the canvas whose
        /// authored size is <paramref name="authoredCanvasSize"/>. An axis the
        /// panel STRETCHES on is left alone, since a stretched edge already
        /// tracks the canvas, and so is a zero-sized rect — that is one a
        /// script drives per frame (the drag-select box).
        /// </summary>
        public static void AnchorToScreenEdges(RectTransform panel, Vector2 authoredCanvasSize)
        {
            if (panel == null) return;
            if (authoredCanvasSize.x <= 0f || authoredCanvasSize.y <= 0f) return;

            Vector2 size = panel.rect.size;
            Vector2 aMin = panel.anchorMin, aMax = panel.anchorMax, pivot = panel.pivot;
            Vector2 pos = panel.anchoredPosition;

            for (int axis = 0; axis < 2; axis++)
            {
                // Stretched: this edge already spans with the canvas.
                if (!Mathf.Approximately(aMin[axis], aMax[axis])) continue;
                // Script-driven rect (SelectionBox) — nothing authored to keep.
                if (size[axis] <= 0f) continue;

                float frame = authoredCanvasSize[axis];

                // Where the panel actually sat in the authored frame, measured
                // from that frame's lower-left corner. This drops out
                // independent of the canvas rect's own pivot, which is why it
                // is safe to compute against the STAGING size while the panel
                // is already parented to the live canvas.
                float min = aMin[axis] * frame + pos[axis] - pivot[axis] * size[axis];
                float max = min + size[axis];
                float centre = (min + max) * 0.5f;

                float anchor, authored;
                if (centre < frame * EdgeBand)
                {
                    // Left / bottom: hold the authored gap to that edge.
                    anchor = 0f;
                    authored = min + pivot[axis] * size[axis];
                }
                else if (centre > frame * (1f - EdgeBand))
                {
                    // Right / top: hold the authored gap to that edge.
                    anchor = 1f;
                    authored = (max - frame) - (1f - pivot[axis]) * size[axis];
                }
                else
                {
                    // Centred: hold the authored offset from the centre line.
                    anchor = 0.5f;
                    authored = (centre - frame * 0.5f) - size[axis] * 0.5f
                               + pivot[axis] * size[axis];
                }

                aMin[axis] = anchor;
                aMax[axis] = anchor;
                pos[axis] = authored;
            }

            panel.anchorMin = aMin;
            panel.anchorMax = aMax;
            panel.anchoredPosition = pos;
        }
    }
}
