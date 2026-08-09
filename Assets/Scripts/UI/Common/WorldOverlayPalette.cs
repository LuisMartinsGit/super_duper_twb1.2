// WorldOverlayPalette — centralized color tokens for the floating IMGUI/world
// overlays during the UI Toolkit migration.
//
// Why this file exists (Phase 5 of the UI Toolkit migration plan):
//   The new in-match HUD is jade & silver with gold accents (see Assets/UI/Styles/_tokens.uss).
//   The world-space and floating overlays (health bars, income numbers, rally
//   points, movement lines, unit indicators, formation previews, planning-mode
//   overlay, player notifications) stay in IMGUI / Gizmos / LineRenderer for now
//   but want to share the same palette so the diegetic widgets read as part of
//   the same visual family.
//
// Pattern: each overlay file replaces its inline `new Color(0.83f, 0.66f, 0.26f, ...)`
// literals with references here, e.g.
//
//     GUI.color = WorldOverlayPalette.Accent;
//     var bar    = WorldOverlayPalette.PanelDeep;
//
// The actual edits to the overlay files (FloatingHealthBars, FloatingIncomeDisplay,
// MovementLineDisplay, RallyPointDisplay, UnitIndicatorSystem, FormationPreview,
// FormationDragPreview, PlanningModeOverlay, PlayerNotificationSystem) land as
// follow-up commits — each one needs a per-file pass to swap colors without
// changing layout.
//
// Token names mirror _tokens.uss --tw-* names; values are baked from the jade
// theme entry in Game HUD/themes.jsx so the IMGUI side matches the UI Toolkit
// side pixel-for-pixel.

using UnityEngine;

namespace TheWaningBorder.UI.Common
{
    public static class WorldOverlayPalette
    {
        // ─── Surface / chrome ─────────────────────────────────────────────
        /// <summary>Panel deep — matches --tw-base (#0b1f1a).</summary>
        public static readonly Color PanelDeep = new Color(0.043f, 0.122f, 0.102f, 0.95f);

        /// <summary>Panel mid radial highlight — matches --tw-base-mid (#143228).</summary>
        public static readonly Color PanelMid  = new Color(0.078f, 0.196f, 0.157f, 1.0f);

        /// <summary>Panel outer edge — matches --tw-base-edge (#04100b).</summary>
        public static readonly Color PanelEdge = new Color(0.016f, 0.063f, 0.043f, 1.0f);

        // ─── Inlay / filigree ─────────────────────────────────────────────
        /// <summary>Filigree silver — matches --tw-inlay (#cfd6d3).</summary>
        public static readonly Color Inlay       = new Color(0.812f, 0.839f, 0.827f, 1.0f);

        /// <summary>Dim filigree — matches --tw-inlay-dim (#7f8e8a).</summary>
        public static readonly Color InlayDim    = new Color(0.498f, 0.557f, 0.541f, 1.0f);

        /// <summary>Deep shadow — matches --tw-inlay-shadow (#020806).</summary>
        public static readonly Color InlayShadow = new Color(0.008f, 0.031f, 0.024f, 1.0f);

        // ─── Accent (gold) ────────────────────────────────────────────────
        /// <summary>Default accent — matches --tw-accent (#e8b84a).</summary>
        public static readonly Color Accent     = new Color(0.910f, 0.722f, 0.290f, 1.0f);

        /// <summary>Soft accent — matches --tw-accent-soft (#8a6a1f).</summary>
        public static readonly Color AccentSoft = new Color(0.541f, 0.416f, 0.122f, 1.0f);

        // ─── Text ─────────────────────────────────────────────────────────
        /// <summary>Body text — matches --tw-text (#e6efea).</summary>
        public static readonly Color Text    = new Color(0.902f, 0.937f, 0.918f, 1.0f);

        /// <summary>Dim text — matches --tw-text-dim (rgba 207/214/211 @ 60%).</summary>
        public static readonly Color TextDim = new Color(0.812f, 0.839f, 0.827f, 0.6f);

        // ─── Gem / veilstone ────────────────────────────────────────────────
        /// <summary>Veilstone facet — matches --tw-gem (#1d6a55).</summary>
        public static readonly Color Gem      = new Color(0.114f, 0.416f, 0.333f, 1.0f);

        /// <summary>Bright veilstone — matches --tw-gem-hi (#3fbf9a).</summary>
        public static readonly Color GemBright = new Color(0.247f, 0.749f, 0.604f, 1.0f);

        // ─── Status colors (kept from the navy theme so semantics don't shift) ─
        /// <summary>Healthy bar — used by FloatingHealthBars on units at full HP.</summary>
        public static readonly Color HealthFull   = new Color(0.247f, 0.749f, 0.604f, 1.0f); // gem-bright
        /// <summary>Wounded bar — used at &gt;30% &lt;100% HP.</summary>
        public static readonly Color HealthMid    = new Color(0.910f, 0.722f, 0.290f, 1.0f); // accent gold
        /// <summary>Critical bar — used at &lt;30% HP.</summary>
        public static readonly Color HealthLow    = new Color(0.910f, 0.290f, 0.220f, 1.0f); // red

        /// <summary>
        /// Resource depletion bar — amber, used by FloatingHealthBars for iron
        /// deposits and veilstone outcroppings in place of the green health bar
        /// (task-108 Phase 5). Matches the panel-side `.sel-bar-resource__fill`
        /// gradient anchor (#d97a2e) so the world-space bar and the selection
        /// panel bar read as the same channel of information.
        /// </summary>
        public static readonly Color32 ResourceDepletion = new Color32(0xd9, 0x7a, 0x2e, 0xff);

        // ─── Friend / foe color tints for overlay markers ─────────────────
        /// <summary>Allied tint for unit indicator rings, rally points, etc.</summary>
        public static readonly Color AlliedTint = new Color(0.247f, 0.749f, 0.604f, 1.0f); // gem-bright
        /// <summary>Hostile tint.</summary>
        public static readonly Color HostileTint = new Color(0.910f, 0.290f, 0.220f, 1.0f);
        /// <summary>Neutral tint.</summary>
        public static readonly Color NeutralTint = new Color(0.812f, 0.839f, 0.827f, 1.0f); // inlay silver
    }
}
