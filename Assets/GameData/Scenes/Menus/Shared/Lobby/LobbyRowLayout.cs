// LobbyRowLayout.cs
// Shared column geometry for the lobby roster rows (Skirmish + Multiplayer).
// Canonical spec: docs/Design/Lobby_Setup.md

using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    /// <summary>
    /// MULTIPLAYER ONLY as of 2026-08-19. The skirmish roster no longer calls
    /// any of this - its template in SkirmishMenu.unity now carries a
    /// LayoutElement on every column and has childForceExpandWidth off, so it
    /// sizes its own row and each clone is a faithful copy. Pinning columns
    /// over the top of that only fought the authored design.
    ///
    /// Both lobbies used to clone the SAME row template
    /// (MenuPanelsBuilder.RosterRowTemplate) and let its children size
    /// themselves. Two things broke the alignment:
    ///
    ///   1. the row's HorizontalLayoutGroup force-expands children, so every
    ///      widget grew to share the row equally and its own preferred width
    ///      was ignored;
    ///   2. each widget kept whatever width it was authored with, so the same
    ///      column landed at a different x on each row.
    ///
    /// The multiplayer template in MainMenu.unity has not been through that
    /// pass, so it still needs this.
    ///
    /// Widths are in the builder's UNSCALED units and are multiplied by the
    /// row's own scale, because the menu scene was rescaled 2x after it was
    /// authored — raw pixel constants render at half the size of everything
    /// around them.
    /// </summary>
    public static class LobbyRowLayout
    {
        /// <summary>Row height MenuPanelsBuilder authored, before any rescale.</summary>
        public const float BuilderRowHeight = 54f;

        public const float ColColor  = 16f;
        public const float ColName   = 150f;   // minimum; absorbs the row's slack
        public const float ColTeam   = 56f;
        public const float ColDiff   = 130f;
        public const float ColStrat  = 150f;
        public const float ColRemove = 34f;
        public const float ColBadge  = 58f;
        public const float ColButton = 90f;

        /// <summary>
        /// How much this row was scaled relative to the builder's authored
        /// size. Derived from the row itself so it survives any future rescale.
        /// </summary>
        public static float RowScale(GameObject row)
        {
            if (row == null) return 1f;

            float h = BuilderRowHeight;
            if (row.TryGetComponent(out LayoutElement le) && le.preferredHeight > 1f)
                h = le.preferredHeight;
            else if (row.TryGetComponent(out RectTransform rt) && rt.rect.height > 1f)
                h = rt.rect.height;

            return h / BuilderRowHeight;
        }

        /// <summary>
        /// Stop the row's layout group from overriding the column widths.
        /// Must be called before any <see cref="Column"/> on that row.
        /// </summary>
        public static void PrepareRow(GameObject row)
        {
            if (row == null) return;
            if (!row.TryGetComponent(out HorizontalLayoutGroup hlg)) return;

            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
        }

        /// <summary>
        /// Place <paramref name="child"/> at the next column position and pin
        /// its width. <paramref name="order"/> advances only for widgets that
        /// exist, so a missing one does not leave a gap.
        /// </summary>
        public static void Column(Transform child, float widthUnits, float scale,
                                  ref int order, float flexible = 0f)
        {
            if (child == null) return;

            child.SetSiblingIndex(order++);

            var le = child.GetComponent<LayoutElement>()
                     ?? child.gameObject.AddComponent<LayoutElement>();
            float w = widthUnits * scale;
            le.minWidth = w;
            le.preferredWidth = w;
            le.flexibleWidth = flexible;
        }
    }
}
