// ActionsPanelPrefabBinder.ReligionPanel.cs
// THE WORKER'S SECOND BUILD PANEL (developer, 2026-11-18: "draw another panel
// to the side. the worker now has two build panels, one for normal buildings
// and one for religion buildings"; docs/Design/Overview.md § The actions
// panel). A frame of its own, drawn beside the build palette, 3 columns x 4
// rows of the palette's own cells, shown whenever the palette is:
//
//   Age 0      row 0: the three age-up landmarks, each in its culture's
//              column (Vault - Alanthor, Keep - Feraldis, Crossing - Runai),
//              greyed where the edition does not ship it; row 1, centre:
//              the Temple of Ridan. No column frames.
//   Age 1      one column per culture's sect cluster - Alanthor, Feraldis,
//              Runai - each framed in its culture's colours
//              (ActionsPanelPrefabBinder.asset), one row per sect in
//              SectConfig.AllSectIds order. A sect building the faction
//              cannot build yet is shown locked with its reason; a sect with
//              no building yet, or one the edition strips, is a greyed
//              placeholder - the 3 x 4 always reads whole. UNTIL religion
//              is unlocked (a standing Temple, or a sect adopted - the
//              Religion menu's rule) the panel shows ONLY the Temple, in the
//              Age 0 Temple cell (developer, 2026-11-18: "panel 2 shows only
//              a temple until it is built and religion is unlocked").
//
// The cells are clones of the authored button made at bind time, exactly as
// the palette's fourth row is, and are a separate slot array: nothing in the
// building / unit / Vault / Market paths ever touches them. They carry no
// hotkeys (the palette has none either).

using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        /// <summary>The religion panel's grid: 3 columns (one per culture)
        /// x 4 rows (one per sect of a cluster).</summary>
        private const int ReligionCols = 3;
        private const int ReligionRows = 4;
        private const int ReligionCells = ReligionCols * ReligionRows;

        private const string TempleId = "TempleOfRidan";

        /// <summary>The column of each cluster, left to right (developer,
        /// 2026-11-18): Alanthor, Feraldis, Runai.</summary>
        private static readonly SectCluster[] ReligionColumnClusters =
            { SectCluster.Alanthor, SectCluster.Feraldis, SectCluster.Runaii };

        /// <summary>Age 0: each landmark sits in its culture's column of row 0.</summary>
        private static readonly string[] LandmarkColumn =
            { "VaultOfAlmierra", "FiendstoneKeep", "ThessarasCrossing" };

        private ActionsPanelPrefabBinderConfig _cfg;
        private ActionsPanelPrefabBinderConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<ActionsPanelPrefabBinderConfig>());

        private RectTransform _religionRoot;
        private RectTransform _religionGrid;
        private GridLayoutGroup _religionLayout;
        private Slot[] _religionSlots;
        private RectTransform[] _columnFrames;
        private Image[] _columnFills;
        private float _religionCellLaidOut = -1f;
        private bool _religionWanted;

        /// <summary>An id the second panel draws, never the palette.</summary>
        private static bool IsReligionPanelBuilding(string id) => ReligionBuildings.Contains(id);

        /// <summary>Builds the panel's frame, column frames and twelve cells
        /// (pristine copies of <paramref name="template"/>; bound later by
        /// BindReligionSlots). Hidden until a worker is selected.</summary>
        private void CreateReligionPanel(GameObject template)
        {
            var go = new GameObject("ReligionBuildPanel", typeof(RectTransform));
            _religionRoot = (RectTransform)go.transform;
            _religionRoot.SetParent(transform, false);
            // Beside the palette's right edge, vertically centred on it.
            _religionRoot.anchorMin = _religionRoot.anchorMax = new Vector2(1f, 0.5f);
            _religionRoot.pivot = new Vector2(0f, 0.5f);

            // The palette's own frame, copied (Background = SPR_Background,
            // SPR_Frame, SPR_Frame_Lip, stretched over the root).
            // A direct child only: the Synty buttons carry their own
            // background nodes deeper down.
            var frame = transform.Find("Background");
            if (frame != null)
            {
                var copy = (RectTransform)Instantiate(frame.gameObject, _religionRoot).transform;
                copy.name = "Background";
                GameUIKit.Stretch(copy);
            }
            else
                TWBLog.Log("[GameUI] ActionsPanel prefab: no \"Background\" node - the religion panel has no frame.");

            // One frame per culture column, behind the cells.
            _columnFrames = new RectTransform[ReligionCols];
            _columnFills = new Image[ReligionCols];
            for (int c = 0; c < ReligionCols; c++)
            {
                var edge = GameUIKit.Image(_religionRoot, "ColumnFrame_" + c, Color.clear);
                edge.raycastTarget = false;
                var er = edge.rectTransform;
                er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f);
                er.pivot = new Vector2(0.5f, 0.5f);
                var fill = GameUIKit.Image(er, "Fill", Color.clear);
                fill.raycastTarget = false;
                GameUIKit.Stretch(fill.rectTransform);
                _columnFrames[c] = er;
                _columnFills[c] = fill;
            }

            var gridGo = new GameObject("Actions", typeof(RectTransform), typeof(GridLayoutGroup));
            _religionGrid = (RectTransform)gridGo.transform;
            _religionGrid.SetParent(_religionRoot, false);
            _religionGrid.anchorMin = _religionGrid.anchorMax = new Vector2(0.5f, 0.5f);
            _religionGrid.pivot = new Vector2(0.5f, 0.5f);
            _religionLayout = gridGo.GetComponent<GridLayoutGroup>();
            _religionLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _religionLayout.constraintCount = ReligionCols;
            _religionLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _religionLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _religionLayout.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < ReligionCells; i++)
            {
                var clone = Instantiate(template, _religionGrid);
                clone.name = template.name + "_religion" + i;
            }
            go.SetActive(false);
        }

        private void BindReligionSlots()
        {
            if (_religionGrid == null) return;
            _religionSlots = new Slot[_religionGrid.childCount];
            for (int i = 0; i < _religionSlots.Length; i++)
            {
                _religionSlots[i] = BindSlot(_religionGrid.GetChild(i));
                BlankSlot(_religionSlots[i]);
            }
        }

        private void ShowReligionPanel(bool shown)
        {
            if (_religionRoot != null && _religionRoot.gameObject.activeSelf != shown)
                _religionRoot.gameObject.SetActive(shown);
        }

        /// <summary>Sizes the panel to the palette's current cell: three of
        /// them wide, four tall (the palette's height, so the rows line up),
        /// inside the same inset the palette keeps from its frame.</summary>
        private void LayoutReligionPanel()
        {
            if (_religionRoot == null || _gridLayout == null) return;
            float cell = _gridLayout.cellSize.x;
            if (Mathf.Approximately(cell, _religionCellLaidOut)) return;
            _religionCellLaidOut = cell;
            float sp = WorkerCellSpacing;
            float w = ReligionCols * cell + (ReligionCols - 1) * sp;
            float h = ReligionRows * cell + (ReligionRows - 1) * sp;
            var root = (RectTransform)transform;
            float rootH = root.rect.height > 0f ? root.rect.height : root.sizeDelta.y;

            _religionRoot.sizeDelta = new Vector2(w + 2f * WorkerGridInset, rootH);
            _religionRoot.anchoredPosition = new Vector2(Cfg.religionPanelGap, 0f);

            _religionLayout.cellSize = new Vector2(cell, cell);
            _religionLayout.spacing = new Vector2(sp, sp);
            _religionGrid.sizeDelta = new Vector2(w, h);
            // The palette's authored x offset keeps it centred inside the
            // frame's slightly asymmetric border; the copied frame has it too.
            float gx = _gridPosAuthored.x;
            _religionGrid.anchoredPosition = new Vector2(gx, 0f);

            // Column frames: the column's four cells, grown by half the
            // spacing all round, so neighbouring frames meet in the gap.
            float edge = Cfg.sectColumnEdgeWidth;
            for (int c = 0; c < ReligionCols; c++)
            {
                float left = gx - w * 0.5f + c * (cell + sp);
                _columnFrames[c].anchoredPosition = new Vector2(left + cell * 0.5f, 0f);
                _columnFrames[c].sizeDelta = new Vector2(cell + sp, h + sp);
                var fr = _columnFills[c].rectTransform;
                fr.offsetMin = new Vector2(edge, edge);
                fr.offsetMax = new Vector2(-edge, -edge);
            }
        }

        private void SetColumnFrames(bool on)
        {
            for (int c = 0; c < ReligionCols; c++)
            {
                var edgeImg = _columnFrames[c].GetComponent<Image>();
                if (!on)
                {
                    edgeImg.color = Color.clear;
                    _columnFills[c].color = Color.clear;
                    continue;
                }
                switch (ReligionColumnClusters[c])
                {
                    case SectCluster.Alanthor:
                        edgeImg.color = Cfg.alanthorColumnEdge;
                        _columnFills[c].color = Cfg.alanthorColumnFill;
                        break;
                    case SectCluster.Feraldis:
                        edgeImg.color = Cfg.feraldisColumnEdge;
                        _columnFills[c].color = Cfg.feraldisColumnFill;
                        break;
                    default:
                        edgeImg.color = Cfg.runaiColumnEdge;
                        _columnFills[c].color = Cfg.runaiColumnFill;
                        break;
                }
            }
        }

        /// <summary>Fills the second panel from the builder's religion
        /// buildings. Every one of the twelve cells is held (blank, filled or
        /// a placeholder), so the grid never reflows. Returns the cells filled.</summary>
        private int RenderReligionPanel(EntityManager em, List<ActionButton> religion, bool aged, bool placing)
        {
            if (_religionSlots == null || _religionSlots.Length < ReligionCells) return 0;
            LayoutReligionPanel();
            for (int i = 0; i < _religionSlots.Length; i++) BlankSlot(_religionSlots[i]);

            var byId = new Dictionary<string, ActionButton>(religion.Count);
            foreach (var b in religion) byId[b.Id] = b;

            int used = 0;
            if (aged && !ReligionUnlocked(em, OwnFaction(em)))
            {
                SetColumnFrames(false);
                if (byId.TryGetValue(TempleId, out var t1))
                { FillBuildCell(_religionSlots[ReligionCols + 1], t1, placing, em); used++; }
                return used;
            }
            if (!aged)
            {
                SetColumnFrames(false);
                for (int c = 0; c < LandmarkColumn.Length && c < ReligionCols; c++)
                    if (byId.TryGetValue(LandmarkColumn[c], out var lm))
                    { FillBuildCell(_religionSlots[c], lm, placing, em); used++; }
                // The Temple: row 1, the middle column.
                if (byId.TryGetValue(TempleId, out var temple))
                { FillBuildCell(_religionSlots[ReligionCols + 1], temple, placing, em); used++; }
                return used;
            }

            SetColumnFrames(true);
            for (int c = 0; c < ReligionCols; c++)
            {
                var cluster = ReligionColumnClusters[c];
                int row = 0;
                for (int s = 0; s < SectConfig.AllSectIds.Length && row < ReligionRows; s++)
                {
                    string sectId = SectConfig.AllSectIds[s];
                    if (SectConfig.ClusterOf(sectId) != cluster) continue;
                    var slot = _religionSlots[row * ReligionCols + c];
                    row++;
                    string buildingId = SectConfig.BuildingIdFor(sectId);
                    if (buildingId != null && byId.TryGetValue(buildingId, out var sb))
                    {
                        FillBuildCell(slot, sb, placing, em);
                        used++;
                        continue;
                    }
                    FillSlot(slot, SectPlaceholder(sectId, buildingId), Category.Religion, null, null, em);
                }
            }
            return used;
        }

        /// <summary>Religion is unlocked once a Temple stands or a sect is
        /// adopted — the Religion menu's own rule (Religion_Menu.md).</summary>
        private static bool ReligionUnlocked(EntityManager em, Faction faction)
        {
            if (SectQuery.HasStandingTemple(em, faction)) return true;
            return SectQuery.TryGetAdoptionState(em, faction, out var st) && st.AdoptedCount() > 0;
        }

        /// <summary>A greyed cell for a sect whose building the faction is
        /// not offered: the edition strips its cluster, or the sect has no
        /// building yet.</summary>
        private static ActionButton SectPlaceholder(string sectId, string buildingId)
        {
            string name = string.Format(Loc.T("Sect of {0}"), SectInfo.ShortName(sectId));
            string reason = !SectConfig.IsInEdition(sectId)
                ? Loc.T("Unavailable in this edition.")
                : buildingId == null
                    ? Loc.T("This sect has no building yet.")
                    : Loc.T("Not available.");
            return new ActionButton
            {
                Id = "SectPlaceholder_" + sectId,
                Label = name,
                Tooltip = "<b>" + name + "</b>\n<color=#C08040>" + reason + "</color>",
                Enabled = false,
                CanAfford = false,
                ShownWhenLocked = true,
            };
        }
    }
}
