// ActionsPanelPrefabBinder.Render.cs
// What the grid shows for the current selection: the worker palette and
// every building menu on the 4 x 4 grid (building actions, research rows,
// paging, the level-up in the bottom-right cell), and the unit formations on
// the authored 3 x 5.

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        /// <summary>Build palette: only current-age buildings are VISIBLE
        /// (the extractor marks future-era ones Enabled=false — those are
        /// hidden here, not greyed); missing resources grey a visible one.</summary>
        private int RenderBuilder(EntityManager em, List<ActionButton> actions)
        {
            bool placing = WorkerCommandPanel.IsPlacingBuilding;
            SetGrid(GridMode.Palette);
            // The palette is 4 x 4; the cells past it belong to other modes.
            int limit = Mathf.Min(_slots.Length, PaletteSlotCount);

            // FOUR FIXED ROWS (BuildOrder; developer, 2026-11-18): Economy,
            // Military, Defense, and the reserved fourth row - each category
            // on its own grid row whatever the others hold, so a building
            // never moves when another unlocks. The religion buildings go to
            // the second panel (ReligionPanel.cs).
            bool aged = CultureConfig.GetCompletedCulture(em, OwnFaction(em)) != Cultures.None;
            var rows = new List<ActionButton>[BuildOrder.Length];
            for (int r = 0; r < rows.Length; r++) rows[r] = new List<ActionButton>();
            var religion = new List<ActionButton>();
            for (int i = 0; i < actions.Count; i++)
            {
                var b = actions[i];
                if (!b.Enabled && !b.ShownWhenLocked) continue;   // era-locked → hidden in this panel
                // Extractors are built from the button over their node
                // (NodeButton.cs), never from the palette.
                if (TheWaningBorder.World.Regions.TerritoryOwnership.IsExtractor(b.Id)) continue;
                // ONE FORTRESS, EVER (decision 69): it is never placed from
                // the palette. Any other keystone whose rubble lies (Temple,
                // sect building) stays in its cell, greyed — it is rebuilt from
                // the button over its rubble (NodeButton.cs) — so the grid
                // does not reflow.
                if (b.Id == TheWaningBorder.Entities.KeystoneRubbles.FortressId) continue;
                if (TheWaningBorder.Entities.KeystoneRubbles.IsKeystone(b.Id)
                    && TheWaningBorder.Entities.KeystoneRubbles.TryFind(em, OwnFaction(em), b.Id, out _, out _))
                {
                    b.Enabled = false;
                    b.CanAfford = false;
                    b.ShownWhenLocked = true;
                    b.Tooltip = (b.Label ?? b.Id) + "\n<color=#C08040>"
                        + Loc.T("Fallen - rebuild it from the button over its rubble (select Workers).") + "</color>";
                }
                if (IsReligionPanelBuilding(b.Id))
                {
                    // After the age-up the Temple stays in the second panel
                    // only while religion is still locked: the panel then
                    // shows the Temple alone (developer, 2026-11-18: "panel
                    // 2 shows only a temple until it is built and religion
                    // is unlocked"). RenderReligionPanel decides.
                    religion.Add(b);
                    continue;
                }
                rows[(int)BuildRowOf(b.Id)].Add(b);
            }
            for (int r = 0; r < rows.Length; r++)
            {
                int row = r;
                // Stable: ties (unnamed ids) keep catalog order.
                var list = rows[r];
                var ranked = new List<(int rank, int idx, ActionButton b)>(list.Count);
                for (int i = 0; i < list.Count; i++)
                    ranked.Add((BuildRankOf(list[i].Id, row), i, list[i]));
                ranked.Sort((x, y) => x.rank != y.rank ? x.rank.CompareTo(y.rank)
                                                       : x.idx.CompareTo(y.idx));
                for (int i = 0; i < list.Count; i++) list[i] = ranked[i].b;
            }

            // Row r is grid row r, always: its cells first, blanks after. A
            // row with more than four entries is a data/layout bug - logged,
            // the extra dropped - never wrapped into the next category's row.
            int used = 0;
            for (int r = 0; r < rows.Length; r++)
            {
                int cell = r * PaletteCols, end = Mathf.Min(cell + PaletteCols, limit);
                var list = rows[r];
                for (int i = 0; i < list.Count; i++)
                {
                    if (cell >= end)
                    {
                        if (!_paletteOverflowLogged)
                        {
                            _paletteOverflowLogged = true;
                            TWBLog.Log($"[GameUI] ActionsPanel: build palette row {r} has {list.Count} " +
                                $"entries, {PaletteCols} fit — overflow dropped.");
                        }
                        break;
                    }
                    FillBuildCell(_slots[cell++], list[i], placing, em);
                    used++;
                }
                while (cell < end) BlankSlot(_slots[cell++]);
            }
            for (int i = limit; i < _slots.Length; i++) ClearSlot(_slots[i]);

            used += RenderReligionPanel(em, religion, aged, placing);
            _religionWanted = true;
            return used;
        }

        private bool _paletteOverflowLogged;

        /// <summary>One build-palette cell (either panel): the building's
        /// button, a click that starts its placement unless it is locked or a
        /// placement is already running.</summary>
        private void FillBuildCell(Slot slot, ActionButton b, bool placing, EntityManager em)
        {
            string id = b.Id;
            FillSlot(slot, b, BuildingCategory(id), null,
                placing || !b.Enabled ? null
                        : (System.Action)(() => WorkerCommandPanel.TriggerBuildingPlacement(id)),
                em);
        }

        /// <summary>
        /// THE UNITS' ACTIONS PANEL (2026-08-31): a movable selection gets
        /// the four formation buttons in the grid, replacing the floating
        /// bottom-centre strip the old FormationsPanelBinder built. Clicking
        /// mirrors the X-key cycle (RequestFormationShape re-slots the whole
        /// selection immediately); the current shape is tinted gold, and the
        /// highlight follows shape changes from EITHER path on the panel's
        /// normal refresh.
        /// </summary>
        private int RenderUnitFormations(EntityManager em)
        {
            bool movable = false;
            var sel = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (sel != null)
                for (int i = 0; i < sel.Count && !movable; i++)
                    movable = em.Exists(sel[i]) && em.HasComponent<MoveSpeed>(sel[i]);
            if (!movable) return -1;

            SetGrid(GridMode.Authored);
            HideWorkerOnlyRow();
            int current = (int)TheWaningBorder.Input.FormationInput.CurrentShape;
            int count = Mathf.Min(4, _slots.Length);
            for (int i = 0; i < count; i++)
            {
                var shape = (FormationShape)i;
                var b = new ActionButton
                {
                    Id = "Formation_" + FormationLabels[i],
                    Label = Loc.T(FormationLabels[i]),
                    Tooltip = Loc.T(FormationTips[i]),
                    Enabled = true,
                    CanAfford = true,
                };
                FillSlot(_slots[i], b, Category.Military, null, () =>
                {
                    TheWaningBorder.Input.FormationInput.RequestShape(shape);
                    _timer = RefreshInterval;   // repaint the highlight now
                }, em);

                if (i == current)
                    foreach (var img in _slots[i].TintBg) img.color = GameUIKit.BarGold;
            }
            // BlankSlot, not ClearSlot: a cleared cell is inactive and the
            // GridLayoutGroup would slide the stances up beside the
            // formations; blanks keep formations on the top row and the
            // stances on the bottom one.
            for (int i = count; i < _slots.Length; i++) BlankSlot(_slots[i]);

            RenderStances(em, sel);
            RenderEnshrine(em, sel);
            return count;
        }

        // ── Unit mode: enshrine the Shardroot (Curse_And_Shardroot.md § 3.1d) ──

        /// <summary>First slot of the MIDDLE row (row 1): the bearer's Enshrine
        /// button, Drop beside it. The top row is the formations and the
        /// bottom row the stances (developer, 2026-11-18: "top row should be
        /// for Formation, bottom row for stance"), so the Shardroot moved up
        /// out of the bottom row.</summary>
        private const int EnshrineSlot = Cols;

        /// <summary>With the Shardroot's bearer selected, one button sends it
        /// to the nearest own finished Temple of Ridan to enshrine the
        /// artifact — the same order a right-click on the Temple gives.</summary>
        private void RenderEnshrine(EntityManager em, List<Entity> sel)
        {
            if (_slots.Length <= EnshrineSlot) return;
            Entity bearer = Entity.Null;
            var local = GameSettings.LocalPlayerFaction;
            for (int i = 0; sel != null && i < sel.Count && bearer == Entity.Null; i++)
            {
                var e = sel[i];
                if (!em.Exists(e) || !em.HasComponent<FactionTag>(e)
                    || em.GetComponentData<FactionTag>(e).Value != local) continue;
                if (TheWaningBorder.Entities.ShardrootEnshrine.IsBearer(em, e)) bearer = e;
            }
            if (bearer == Entity.Null) return;

            var price = TheWaningBorder.Entities.ShardrootEmpowermentConfig.I.EnshrinePrice;
            var b = new ActionButton
            {
                Id = "EnshrineShardroot",
                Label = Loc.T("Enshrine"),
                Tooltip = "<b>" + Loc.T("Enshrine the Shardroot") + "</b>\n"
                          + Loc.T("Carry it to your nearest Temple of Ridan and enshrine it there: "
                                  + "every unit of yours is empowered and the ascension countdown begins. "
                                  + "Or right-click your Temple with the bearer selected.")
                          + "\n" + Loc.T("Cost: "),
                Cost = price,
                Enabled = true,
                CanAfford = FactionEconomy.CanAfford(em, local, price),
            };
            Entity who = bearer;
            FillSlot(_slots[EnshrineSlot], b, Category.Religion, null, () =>
            {
                string refusal = TheWaningBorder.Core.Commands.Issuing.SelectionOrders
                    .EnshrineAtNearestTemple(em, who);
                if (refusal != null) PlayerNotificationSystem.NotifyError(refusal);
            }, em);

            // ... or set it down, with no explosion (docs/Design/Heroes.md §4).
            if (_slots.Length <= EnshrineSlot + 1) return;
            var drop = new ActionButton
            {
                Id = "DropShardroot",
                Label = Loc.T("Drop"),
                Tooltip = "<b>" + Loc.T("Drop the Shardroot") + "</b>\n"
                          + Loc.T("Set it down here, safely. The hero loses its power and any hero may take it."),
                Enabled = true,
                CanAfford = true,
            };
            FillSlot(_slots[EnshrineSlot + 1], drop, Category.Religion, null, () =>
                TheWaningBorder.Core.Commands.CommandRouter.IssueDropShardroot(em, who), em);
        }

        // ── Unit mode: stances (docs/Design/Stances.md §6) ─────────────────

        /// <summary>First slot of the stance row: the BOTTOM row of the
        /// authored 3 x 5 (developer, 2026-11-18: "top row should be for
        /// Formation, bottom row for stance").</summary>
        private const int StanceFirstSlot = 2 * Cols;

        // DISPLAY NAMES ONLY (developer, 2026-11-18; Stances.md § 9): the
        // enum's Defensive shows as "Standard" and its Hold as "Defensive".
        // The enum, the command ids, the save / lockstep format and the
        // hotkeys (G / D / H) are unchanged. The ids below keep the enum's
        // names so nothing that keys on them moves.
        private static readonly UnitStanceMode[] StanceOrder =
            { UnitStanceMode.Aggressive, UnitStanceMode.Defensive, UnitStanceMode.Hold };
        private static readonly string[] StanceIds = { "Aggressive", "Defensive", "Hold" };
        private static readonly string[] StanceLabels = { "Aggressive", "Standard", "Defensive" };
        private static readonly string[] StanceTips =
        {
            "<b>Aggressive</b>  [G]\nEngages any enemy it can see and pursues it, "
                + "then walks back to its post.",
            "<b>Standard</b>  [D]\nReturns fire: fights back against whatever attacks it, "
                + "if it can reach it from where it stands. Never pursues.",
            "<b>Defensive</b>  [H]\nCompletely passive: does not return fire and never "
                + "moves on its own. Your orders still work.",
        };

        /// <summary>Dimmer gold for a stance only PART of a mixed selection holds.</summary>
        private static readonly Color MixedStanceTint = new Color(0.55f, 0.45f, 0.20f, 1f);

        private void RenderStances(EntityManager em, List<Entity> sel)
        {
            if (_slots.Length < StanceFirstSlot + StanceOrder.Length) return;

            // Census of the owned units the stance order would reach. An
            // emplaced engine always holds and is left out, so an all-engine
            // selection shows no stance row at all.
            var counts = new int[StanceOrder.Length];
            int total = 0;
            var local = GameSettings.LocalPlayerFaction;
            for (int i = 0; sel != null && i < sel.Count; i++)
            {
                var e = sel[i];
                if (!em.Exists(e) || !em.HasComponent<UnitTag>(e) || em.HasComponent<BuildingTag>(e)) continue;
                if (!em.HasComponent<FactionTag>(e) || em.GetComponentData<FactionTag>(e).Value != local) continue;
                if (em.HasComponent<EmplacedEngineTag>(e)) continue;
                counts[(int)StanceCommandHelper.Effective(em, e)]++;
                total++;
            }
            if (total == 0) return;

            for (int k = 0; k < StanceOrder.Length; k++)
            {
                var stance = StanceOrder[k];
                string tip = Loc.T(StanceTips[k]);
                if (counts[k] > 0 && counts[k] < total)
                    tip += "\n<i>" + counts[k] + " / " + total + "</i>";
                var b = new ActionButton
                {
                    Id = "Stance_" + StanceIds[k],
                    Label = Loc.T(StanceLabels[k]),
                    Tooltip = tip,
                    Enabled = true,
                    CanAfford = true,
                };
                var slot = _slots[StanceFirstSlot + k];
                FillSlot(slot, b, Category.Military, null, () =>
                {
                    TheWaningBorder.Core.Commands.Issuing.SelectionOrders.IssueStanceToOwnedUnits(
                        em, TheWaningBorder.Input.SelectionSystem.CurrentSelection, stance,
                        GameSettings.LocalPlayerFaction);
                    _timer = RefreshInterval;   // repaint the highlight now
                }, em);

                // Gold: the whole selection is in this stance. Dim gold: part
                // of a mixed selection is.
                if (counts[k] == total)
                    foreach (var img in slot.TintBg) img.color = GameUIKit.BarGold;
                else if (counts[k] > 0)
                    foreach (var img in slot.TintBg) img.color = MixedStanceTint;
            }
        }

        /// <summary>The cloned cells past the authored fifteen belong to the
        /// 4 x 4 modes (palette, building menus); the unit mode hides them
        /// (its own render never reaches past SlotCount).</summary>
        private void HideWorkerOnlyRow()
        {
            for (int i = SlotCount; i < _slots.Length; i++) ClearSlot(_slots[i]);
        }

        // ── Building mode: the 4 x 4 grid (2026-11-18) ────────────────────

        /// <summary>One cell of a building menu, before it is placed. A null
        /// entry in a plan is a cell held blank (row padding, a spent chain).</summary>
        private struct PlanCell
        {
            public ActionButton Button;
            public Category Cat;
            public string[] ChainIds;
            public System.Action Click;
        }

        /// <summary>
        /// EVERY BUILDING'S MENU IS THE SAME 4 x 4 GRID (developer, 2026-11-18:
        /// "all buildings should preserve the 4x4 action grid"). Each path
        /// below lays its cells into the sixteen; SealGrid then holds every
        /// cell left empty in place, so the grid never shrinks, grows or
        /// slides. Returns -1 when this selection is not one the panel
        /// describes as a building.
        /// </summary>
        private int RenderBuilding(EntityManager em)
        {
            int used = RenderBuildingCells(em);
            if (used >= 0) SealGrid();
            return used;
        }

        private int RenderBuildingCells(EntityManager em)
        {
            var info = EntityActionExtractor.GetActionInfo(_entity, em);
            bool hasLayout = BuildingActionLayouts.TryResolve(_entity, em, out var layoutSlots);
            // Walls, gates, manned towers, the hut's age-up choice, the
            // Bazaar wagon and the Vault (ActionsPanelPrefabBinder.Special.cs).
            bool special = !hasLayout && IsSpecialType(info.Type);
            // The Market's trade buttons (ActionsPanelPrefabBinder.Market.cs).
            bool market = !special && em.HasComponent<MarketTag>(_entity);
            bool roster = hasLayout
                || info.Type == ActionType.UnitTraining
                || info.Type == ActionType.UnitTrainingAndResearch
                || info.Type == ActionType.TempleTraining;
            // A building whose ONLY action is its level-up (the Watch Tower
            // trains and researches nothing) gets the grid with just that.
            bool upgradeOnly = !special && !market && !roster
                && info.Type == ActionType.None
                && BuildingUpgradeAction.Describe(em, _entity).Show;
            if (!special && !market && !roster && !upgradeOnly)
                return -1;   // not a building this panel describes

            // A building's menu is the builder's grid (developer, 2026-11-13:
            // "building menu should use the same menu as the builder"): the
            // same square cells filling the frame, no separator.
            SetGrid(GridMode.Palette);
            BlankGrid();

            if (special) return RenderSpecial(em, info);
            if (market) return RenderMarket(em, info);
            if (upgradeOnly)
            {
                int only = RenderUpgradeSlot(em);
                RenderProgress(em, info);
                return only;
            }

            int used = 0;
            Entity entity = _entity;
            if (hasLayout)
            {
                // An authored layout places every cell itself (4 x 4,
                // BuildingActionLayouts); its empty cells stay blank.
                for (int i = 0; i < layoutSlots.Length && i < BuildingCells && i < _slots.Length; i++)
                {
                    if (layoutSlots[i].Empty) continue;
                    var resolved = layoutSlots[i];
                    var b = resolved.Button;
                    var cat = resolved.IsTrain ? UnitCategory(b.Id) : Category.Research;
                    bool isTrain = resolved.IsTrain;
                    FillSlot(_slots[i], b, cat, resolved.ChainIds,
                        b.Enabled ? (System.Action)(() => Execute(entity, b, isTrain)) : null, em);
                    used++;
                }
            }
            else
            {
                // Row 0: trainable units (and the building's own cells -
                // Bazaar pack, Reliquary powers, Forge bays, chapel levers -
                // which ride the training list). Research from the next row.
                var top = new List<PlanCell?>();
                var trains = info.Actions;
                for (int i = 0; trains != null && i < trains.Count; i++)
                {
                    var b = trains[i];
                    top.Add(new PlanCell
                    {
                        Button = b,
                        Cat = UnitCategory(b.Id),
                        Click = b.Enabled ? (System.Action)(() => Execute(entity, b, isTrain: true)) : null,
                    });
                }
                used += PaintSections(em, top, BuildResearchPlan(em));
            }

            used += RenderUpgradeSlot(em);
            RenderProgress(em, info);
            return used;
        }

        /// <summary>Every cell of the 4 x 4 held blank (in place, invisible),
        /// every cloned cell past it hidden: the start of a building render.
        /// Never deactivates a cell of the sixteen, so a cell that is filled
        /// again this refresh keeps its button state.</summary>
        private void BlankGrid()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (i < BuildingCells) BlankSlot(_slots[i]);
                else ClearSlot(_slots[i]);
            }
        }

        /// <summary>The end of a building render: any of the sixteen cells a
        /// path cleared is held blank again, and nothing past them shows, so
        /// every building menu is the full 4 x 4.</summary>
        private void SealGrid()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (i >= BuildingCells) { ClearSlot(_slots[i]); continue; }
                if (!_slots[i].Root.activeSelf) BlankSlot(_slots[i]);
            }
        }

        /// <summary>
        /// The generic building layout: <paramref name="top"/> from row 0 (at
        /// least one row, blank when empty; more than four cells wrap), then
        /// <paramref name="research"/> from the next whole row. Up to fifteen
        /// cells fit beside the level-up; more are paged (PaintPaged).
        /// Returns the cells filled.
        /// </summary>
        private int PaintSections(EntityManager em, List<PlanCell?> top, List<PlanCell?> research)
        {
            var flat = new List<PlanCell?>(BuildingCells);
            if (top != null) flat.AddRange(top);
            int rowEnd = Mathf.Max(BuildingCols,
                (flat.Count + BuildingCols - 1) / BuildingCols * BuildingCols);
            while (flat.Count < rowEnd) flat.Add(null);
            if (research != null) flat.AddRange(research);
            // Trailing blanks take no room (they would only force a page).
            while (flat.Count > 0 && !flat[flat.Count - 1].HasValue) flat.RemoveAt(flat.Count - 1);
            return PaintPaged(em, flat);
        }

        /// <summary>
        /// Lays a row-padded plan onto the grid. Fifteen cells or fewer: one
        /// page, cells 0-14, the level-up in 15. More: twelve cells a page
        /// (rows 0-2, so a page keeps the plan's rows), the page button in
        /// cell 12, the level-up still in 15. Every action stays reachable.
        /// </summary>
        private int PaintPaged(EntityManager em, List<PlanCell?> flat)
        {
            bool paged = flat.Count > UpgradeCell;
            int perPage = paged ? PageCells : UpgradeCell;
            int pages = paged ? (flat.Count + PageCells - 1) / PageCells : 1;
            if (_pageEntity != _entity) { _pageEntity = _entity; _page = 0; }
            if (_page >= pages || _page < 0) _page = 0;

            int used = 0, start = _page * perPage;
            for (int i = 0; i < perPage && i < _slots.Length; i++)
            {
                int k = start + i;
                if (k >= flat.Count || !flat[k].HasValue) continue;   // held blank (BlankGrid)
                var c = flat[k].Value;
                FillSlot(_slots[i], c.Button, c.Cat, c.ChainIds, c.Click, em);
                used++;
            }
            if (paged && PagerCell < _slots.Length) RenderPager(em, pages);
            return used;
        }

        /// <summary>The page button: shows which page is up, a click turns to
        /// the next (and from the last back to the first).</summary>
        private void RenderPager(EntityManager em, int pages)
        {
            var b = new ActionButton
            {
                Id = "PanelPage",
                Label = string.Format(Loc.T("Page {0}/{1}"), _page + 1, pages),
                Tooltip = "<b>" + string.Format(Loc.T("Page {0} of {1}"), _page + 1, pages) + "</b>\n"
                          + Loc.T("This building has more actions than one grid holds. Click for the next page."),
                Enabled = true,
                CanAfford = true,
            };
            int count = pages;
            FillSlot(_slots[PagerCell], b, Category.Research, null, () =>
            {
                _page = (_page + 1) % count;
                _timer = RefreshInterval;   // repaint now
            }, em);
        }

        /// <summary>Lays <paramref name="cells"/> one after another from
        /// <paramref name="first"/>, never past the level-up cell (the Vault's
        /// research under its own two rows). Returns the cells filled.</summary>
        private int PaintRun(EntityManager em, List<PlanCell?> cells, int first)
        {
            int used = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int index = first + i;
                if (index >= UpgradeCell || index >= _slots.Length)
                {
                    TWBLog.Log($"[GameUI] ActionsPanel: {cells.Count} cells from cell {first}, " +
                        $"only {UpgradeCell - first} before the level-up — overflow dropped.");
                    break;
                }
                if (!cells[i].HasValue) continue;
                var c = cells[i].Value;
                FillSlot(_slots[index], c.Button, c.Cat, c.ChainIds, c.Click, em);
                used++;
            }
            return used;
        }

        /// <summary>
        /// The building's level-up, ALWAYS in the bottom-right cell of the
        /// 4 x 4 (cell 15) - one place for every building, so it is never
        /// hunted for and never displaces a unit or a research chain. (It
        /// used to take the last free cell, and the Garrison's top-left; the
        /// level-up was a pill off the selection header before that.)
        /// </summary>
        private static int TrailingNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int end = text.Length;
            while (end > 0 && !char.IsDigit(text[end - 1])) end--;
            int start = end;
            while (start > 0 && char.IsDigit(text[start - 1])) start--;
            return start < end && int.TryParse(text.Substring(start, end - start), out int n) ? n : 0;
        }

        private int RenderUpgradeSlot(EntityManager em)
        {
            var upgrade = BuildingUpgradeAction.Describe(em, _entity);
            if (!upgrade.Show) return 0;
            if (UpgradeCell >= _slots.Length) return 0;

            var slot = _slots[UpgradeCell];
            if (slot.ActionId != null)
            {
                // A layout put something in the level-up's cell.
                if (!_upgradeOverflowLogged)
                {
                    _upgradeOverflowLogged = true;
                    TWBLog.Log($"[GameUI] ActionsPanel: cell {UpgradeCell} is taken by " +
                        $"'{slot.ActionId}' — it belongs to the building level-up.");
                }
                return 0;
            }

            Entity entity = _entity;
            var button = new ActionButton
            {
                Id = UpgradeActionId,
                Label = upgrade.Label,
                Tooltip = upgrade.Tooltip,
                Cost = upgrade.Cost,
                Enabled = upgrade.Enabled,
                CanAfford = upgrade.CanAfford,
                // An arrow pointing up, with the level it leads to (the label
                // ends in that number).
                Icon = ActionIcons.Upgrade(TrailingNumber(upgrade.Label)),
            };
            FillSlot(slot, button, Category.Research, null,
                upgrade.Enabled
                    ? (System.Action)(() => UpgradeClicked(entity))
                    : null,
                em);

            // FillSlot zeroes the radial sweep; a running upgrade drives it.
            if (upgrade.Progress >= 0f && slot.CooldownFill != null)
                slot.CooldownFill.fillAmount = 1f - upgrade.Progress;
            return 1;
        }

        /// <summary>
        /// The building's research as a plan with STABLE positions: each tech
        /// chain (linked by prerequisites within this building's research
        /// list) owns one cell and shows its first not-yet-started tier;
        /// exhausted chains keep their cell as a blank (null) so nothing
        /// shifts around. A chain no tier of which this faction's culture may
        /// research takes no cell at all (the Barracks lists every culture's
        /// ladders; they used to hold blank cells for the others). Extra
        /// actions that are not catalog research (Keep wings, chapel levers,
        /// the Temple's Petriarchy buttons) follow, one cell each.
        /// </summary>
        private List<PlanCell?> BuildResearchPlan(EntityManager em)
        {
            var plan = new List<PlanCell?>();
            var visible = EntityActionExtractor.GetResearchActions(_entity, em);
            var byId = new Dictionary<string, ActionButton>(visible.Count);
            foreach (var a in visible) byId[a.Id] = a;

            var claimed = new HashSet<string>();
            Entity entity = _entity;
            byte culture = CultureConfig.GetCompletedCulture(em, OwnFaction(em));

            string buildingId = EntityActionExtractor.GetBuildingIdPublic(_entity, em);
            if (buildingId != null && TechCatalog.IsReady
                && TechCatalog.TryGetBuilding(buildingId, out var def) && def.research != null)
            {
                var ids = new List<string>();
                ids.AddRange(def.research);

                // parent = first prerequisite that is itself in this list.
                var parent = new Dictionary<string, string>();
                foreach (var id in ids)
                {
                    parent[id] = null;
                    if (!TechCatalog.TryGetTechnology(id, out var tech)
                        || tech.prerequisites == null) continue;
                    foreach (var pre in tech.prerequisites)
                        if (ids.Contains(pre)) { parent[id] = pre; break; }
                }

                foreach (var rootId in ids)
                {
                    if (parent[rootId] != null) continue;   // not a chain root
                    // Walk the chain root -> tier2 -> ... and show the first
                    // tier still offered by the extractor (started/finished
                    // tiers are absent from its list).
                    ActionButton? display = null;
                    var chain = new List<string>();
                    bool cultureMay = false;
                    string cursor = rootId;
                    while (cursor != null)
                    {
                        claimed.Add(cursor);
                        chain.Add(cursor);
                        if (!TechCatalog.TryGetTechnology(cursor, out var t)
                            || TechCatalog.CultureAllows(t, culture))
                            cultureMay = true;
                        if (display == null && byId.TryGetValue(cursor, out var b))
                            display = b;
                        string next = null;
                        foreach (var id in ids)
                            if (parent[id] == cursor) { next = id; break; }
                        cursor = next;
                    }
                    if (!cultureMay) continue;   // another culture's ladder

                    if (display == null) { plan.Add(null); continue; }
                    var shown = display.Value;
                    plan.Add(new PlanCell
                    {
                        Button = shown,
                        Cat = Category.Research,
                        ChainIds = chain.ToArray(),
                        Click = shown.Enabled
                            ? (System.Action)(() => Execute(entity, shown, isTrain: false)) : null,
                    });
                }
            }

            foreach (var a in visible)
            {
                if (claimed.Contains(a.Id)) continue;
                var extra = a;
                plan.Add(new PlanCell
                {
                    Button = extra,
                    Cat = Category.Research,
                    ChainIds = new[] { extra.Id },
                    Click = extra.Enabled
                        ? (System.Action)(() => Execute(entity, extra, isTrain: false)) : null,
                });
            }
            return plan;
        }

        /// <summary>In-progress training/research shown as a radial cooldown
        /// sweep on the matching button (for chains: on the slot the active
        /// tier's chain occupies, i.e. under the successor tier).</summary>
        private void RenderProgress(EntityManager em, in EntityActionInfo info)
        {
            // The head item sweeps the button that queued it: a unit its
            // roster button, a tech its research button. A running LEVEL-UP
            // sweeps its own slot, which RenderUpgradeSlot already does from
            // BuildingUpgrading.
            if (info.ProductionState.HasValue
                && info.ProductionState.Value.IsBusy
                && info.ProductionState.Value.CurrentKind == ProductionKind.Train)
            {
                var t = info.ProductionState.Value;
                // Any cell of the grid: a unit is not always in row 0 (the
                // Garrison authors its units in rows 1 and 2).
                for (int i = 0; i < _slots.Length && i < BuildingCells; i++)
                    if (_slots[i].ActionId == t.CurrentId && _slots[i].ChainIds == null
                        && _slots[i].CooldownFill != null)
                        _slots[i].CooldownFill.fillAmount = 1f - Mathf.Clamp01(t.Progress);
            }
            if (info.ProductionState.HasValue
                && info.ProductionState.Value.IsBusy
                && info.ProductionState.Value.CurrentKind == ProductionKind.Research)
            {
                var r = info.ProductionState.Value;
                for (int i = 0; i < _slots.Length; i++)
                {
                    var s = _slots[i];
                    if (s.CooldownFill == null || s.ChainIds == null) continue;
                    foreach (var id in s.ChainIds)
                        if (id == r.CurrentId)
                        { s.CooldownFill.fillAmount = 1f - Mathf.Clamp01(r.Progress); break; }
                }
            }
        }
    }
}
