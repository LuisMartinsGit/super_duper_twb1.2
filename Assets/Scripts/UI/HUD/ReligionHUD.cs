// File: Assets/Scripts/UI/HUD/ReligionHUD.cs
// Top-center Religion HUD. The single home for the player's sect adoption
// and management UI — adoption was previously embedded inside the temple
// panel, but the spec calls for it to live entirely on this top menu so
// the Religion HUD becomes the only place to:
//   - choose a sect (adopt),
//   - read its passive description,
//   - upgrade its 4 levers (P / B / U / A),
//   - activate / manage the active power (Fire button + cooldown).
//
// Hidden when the player has anything selected (matches the spec rule
// "when nothing is selected" for the various top-level HUD strips).
//
// Adoption flow: clicking Adopt deducts both RP and the chapel material
// cost atomically via SectAdoption.TryStartAdoption, then queues a chapel
// build into the first free temple slot. Duplicate clicks are rejected
// because TryStartAdoption checks for both already-adopted state and
// any in-flight chapel build for the same sect.
//
// Audit fix: spec items #1 / #2 / #3.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.Input;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.UI.Panels;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    public class ReligionHUD : MonoBehaviour
    {
        // ── Layout constants ───────────────────────────────────────────────
        public const float TopMargin = 14f;
        public const float StripSlotWidth = 72f;
        public const float StripSlotHeight = 56f;
        public const float StripSlotSpacing = 4f;
        private const int  StripSlotCount = 6;

        public const float RosterCellWidth = 168f;
        public const float RosterCellHeight = 96f;
        public const float RosterCellSpacing = 4f;
        private const int  RosterCols = 6;
        private const int  RosterRows = 2;
        private const float ChapelBuildSeconds = 30f;

        private EntityWorld _world;
        private EntityManager _em;
        private bool _rosterOpen;

        // Cached IMGUI styles, built on first OnGUI tick.
        private GUIStyle _slotStyle;
        private GUIStyle _slotEmptyStyle;
        private GUIStyle _slotEmptyButtonStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _tooltipStyle;
        private Texture2D _tooltipBgTex;
        private bool _stylesBuilt;

        private void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;
        }

        private void OnGUI()
        {
            if (_world == null || !_world.IsCreated) return;
            if (_em.Equals(default(EntityManager))) _em = _world.EntityManager;

            if (SelectionSystem.CurrentSelection != null
                && SelectionSystem.CurrentSelection.Count > 0)
                return;

            var faction = GameSettings.LocalPlayerFaction;

            // Religion UI is gated on owning a completed Temple of Ridan.
            // A temple entity gets TempleOfRidanTag at spawn but carries
            // UnderConstruction until the build finishes, so the HUD stays
            // hidden while the foundation is being built. Closes the roster
            // too — otherwise toggling Manage Sects, then losing/canceling
            // the temple would leave the panel orphaned next frame.
            if (!TryGetTemple(faction, out var temple)
                || _em.HasComponent<UnderConstruction>(temple))
            {
                _rosterOpen = false;
                return;
            }

            BuildStyles();

            int rp = FactionReligionPointsHelper.GetBalance(_em, faction);

            DrawSlotStrip(faction, temple, rp);
            if (_rosterOpen)
                DrawRoster(faction, temple, rp);

            // Tooltip rendered last so it sits above the strip + roster. IMGUI
            // sets GUI.tooltip whenever the mouse hovers a control whose
            // GUIContent has a tooltip — DrawTooltip wraps the value in a
            // small navy box if non-empty.
            DrawTooltip();
        }

        // ──────────────────────────────────────────────────────────────────
        // SLOT STRIP — 6 tiles always visible, plus header w/ Manage toggle.
        // ──────────────────────────────────────────────────────────────────

        private void DrawSlotStrip(Faction faction, Entity temple, int rp)
        {
            float total = StripSlotWidth * StripSlotCount + StripSlotSpacing * (StripSlotCount - 1);
            float x0 = (Screen.width - total) * 0.5f;
            float y = TopMargin;

            // RP / toggle bar above the strip.
            var headerY = y - 22f;
            string headerText = $"Religion · RP: {rp}";
            GUI.Label(new Rect(x0, headerY, total - 140f, 20f), headerText, _slotStyle);
            // Renamed from "Manage Sects" — the toggle now only opens the
            // existing roster for lever upgrades + ability comparison.
            // Adoption is owned by SectChoicePopup (triggered by clicking an
            // empty strip slot, or — once shipped — a ground decal).
            string toggleLabel = _rosterOpen ? "[ Hide Upgrades ]" : "[ Sect Upgrades ]";
            if (GUI.Button(new Rect(x0 + total - 140f, headerY, 140f, 20f),
                new GUIContent(toggleLabel, "Browse adopted sects and upgrade their levers (P / B / U / A)."),
                _toggleStyle))
            {
                _rosterOpen = !_rosterOpen;
            }

            // Snapshot slot data into local arrays — DynamicBuffer references
            // are invalidated by any structural change in the entity world,
            // and OnGUI fires multiple times per frame (Layout/Repaint events)
            // interleaved with ECS work. Holding a buffer reference across
            // GUI controls trips the safety system with ObjectDisposedException.
            int slotCount = 0;
            var slotStates = new byte[StripSlotCount];
            var slotSectIds = new string[StripSlotCount];
            var slotProgress = new int[StripSlotCount];
            if (temple != Entity.Null && _em.HasBuffer<TempleChapelSlot>(temple))
            {
                var slots = _em.GetBuffer<TempleChapelSlot>(temple);
                slotCount = math.min(StripSlotCount, slots.Length);
                for (int i = 0; i < slotCount; i++)
                {
                    var s = slots[i];
                    slotStates[i] = s.State;
                    slotSectIds[i] = s.SectId.ToString();
                    slotProgress[i] = s.BuildTime > 0 ? (int)(100f * s.BuildProgress / s.BuildTime) : 0;
                }
            }

            for (int i = 0; i < StripSlotCount; i++)
            {
                var rect = new Rect(x0 + i * (StripSlotWidth + StripSlotSpacing), y,
                    StripSlotWidth, StripSlotHeight);

                if (i >= slotCount)
                {
                    GUI.Label(rect, "Slot\n(no temple)", _slotEmptyStyle);
                    continue;
                }

                var state = slotStates[i];
                if (state == 0)
                {
                    // Empty slot — clicking opens the Sect Choice popup for
                    // this specific slot index (so it matches the ground decal
                    // the player will eventually click). Tooltip nudges the
                    // first-time player toward the picker.
                    var emptyContent = new GUIContent(
                        $"Slot {i + 1}\n+ Sect",
                        "Click to choose a sect to adopt into this chapel slot.");
                    if (GUI.Button(rect, emptyContent, _slotEmptyButtonStyle))
                        SectChoicePopup.Show(temple, i, faction);
                    continue;
                }
                if (state == 1)
                {
                    var buildingContent = new GUIContent(
                        $"{ShortName(slotSectIds[i])}\n{slotProgress[i]}%",
                        $"Chapel of {ShortName(slotSectIds[i])} is under construction ({slotProgress[i]}%).");
                    GUI.Label(rect, buildingContent, _slotStyle);
                    continue;
                }

                // State 2 — adopted. Every adopted sect has a god power
                // (refinement: religion is opt-in, each chosen sect gets a power).
                string sectId = slotSectIds[i];
                var nameContent = new GUIContent(
                    ShortName(sectId),
                    $"{SectInfo.Lore(sectId)}\n\nPassive: {SectInfo.PassiveDescription(sectId)}");
                GUI.Label(new Rect(rect.x, rect.y, rect.width, 18f),
                    nameContent, _slotStyle);

                bool glowAllocated = SectActivePowerHelper.HasGlowAllocated(_em, faction, sectId);

                // Fire button — shrinks vertically so a glow toggle fits below.
                var btnRect = new Rect(rect.x + 4, rect.y + 18, rect.width - 8, 18);
                float remaining = SectActivePowerHelper.CooldownRemaining(_em, faction, sectId);
                bool ready = remaining <= 0f;
                GUI.enabled = ready;
                string fireLabel = ready ? "Fire" : $"{(int)remaining}s";
                var fireContent = new GUIContent(fireLabel,
                    "Active Power — " + SectInfo.ActivePowerDescription(sectId));
                if (GUI.Button(btnRect, fireContent, _btnStyle))
                    FireActivePower(faction, sectId, temple);
                GUI.enabled = true;

                // Glow allocation toggle. Filled = 1 Glow locked here (halves
                // cooldown on each cast). Click to toggle allocate/deallocate.
                var glowRect = new Rect(rect.x + 4, rect.y + 38, rect.width - 8, 16);
                string glowText = glowAllocated ? "◆ Glow ◆" : "+ Allocate Glow";
                var glowContent = new GUIContent(glowText, glowAllocated
                    ? "1 Glow allocated here — halves this sect's active-power cooldown. Click to release."
                    : "Allocate 1 Glow to this chapel — halves this sect's active-power cooldown.");
                if (GUI.Button(glowRect, glowContent, _btnStyle))
                {
                    if (glowAllocated) SectActivePowerHelper.DeallocateGlow(_em, faction, sectId);
                    else SectActivePowerHelper.AllocateGlow(_em, faction, sectId);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // 12-SECT ROSTER — opens below the strip when "Manage Sects" toggled.
        // ──────────────────────────────────────────────────────────────────

        private void DrawRoster(Faction faction, Entity temple, int rp)
        {
            float total = RosterCellWidth * RosterCols + RosterCellSpacing * (RosterCols - 1);
            float x0 = (Screen.width - total) * 0.5f;
            float y0 = TopMargin + StripSlotHeight + 16f;

            var panelRect = new Rect(x0 - 8, y0 - 8, total + 16,
                RosterCellHeight * RosterRows + RosterCellSpacing * (RosterRows - 1) + 16);
            GUI.Box(panelRect, GUIContent.none);

            byte culture = LookupCulture(faction);

            for (int i = 0; i < SectConfig.SectCount; i++)
            {
                int row = i / RosterCols;
                int col = i % RosterCols;
                var rect = new Rect(
                    x0 + col * (RosterCellWidth + RosterCellSpacing),
                    y0 + row * (RosterCellHeight + RosterCellSpacing),
                    RosterCellWidth, RosterCellHeight);
                DrawSectCell(rect, faction, temple, SectConfig.IdAt(i), culture, rp);
            }
        }

        private void DrawSectCell(Rect rect, Faction faction, Entity temple,
            string sectId, byte culture, int rp)
        {
            GUI.Box(rect, GUIContent.none);

            var nameRect = new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 16);
            GUI.Label(nameRect, $"{ShortName(sectId)}  ·  {SectConfig.ClusterOf(sectId)}", _slotStyle);

            var descRect = new Rect(rect.x + 4, rect.y + 18, rect.width - 8, 28);
            GUI.Label(descRect, SectInfo.PassiveDescription(sectId), _slotEmptyStyle);

            PerSectState sect = default;
            if (FactionEconomy.TryGetBank(_em, faction, out var bank)
                && _em.HasComponent<SectAdoptionState>(bank))
                sect = _em.GetComponentData<SectAdoptionState>(bank).Get(sectId);

            float btnY = rect.y + rect.height - 24f;
            float btnX = rect.x + 4f;
            float btnW = rect.width - 8f;

            if (!sect.IsAdopted)
            {
                int adoptCost = SectConfig.AdoptionCost(sectId, culture);
                bool slotFree = HasFreeSlot(temple);
                bool inFlight = HasInFlightBuildForSect(temple, sectId);
                bool canMaterial = TheWaningBorder.Data.BuildCosts.TryGet(
                    SectConfig.ChapelIdFor(sectId), out var chapelCost)
                    && FactionEconomy.CanAfford(_em, faction, chapelCost);
                bool canAfford = rp >= adoptCost;
                bool enabled = canAfford && slotFree && canMaterial && !inFlight;

                string label = inFlight ? "Building…"
                    : !slotFree ? "no slot"
                    : !canMaterial ? "need materials"
                    : !canAfford ? $"need {adoptCost} RP"
                    : $"Adopt — {adoptCost} RP";

                GUI.enabled = enabled;
                if (GUI.Button(new Rect(btnX, btnY, btnW, 20f), label, _btnStyle))
                    StartAdoption(faction, temple, sectId, chapelCost);
                GUI.enabled = true;
                return;
            }

            // Adopted — show 4 lever buttons.
            float btnW4 = (btnW - 12f) / 4f;
            DrawLever(new Rect(btnX,                       btnY, btnW4, 20f), faction, sectId, SectLeverKind.Passive,     "P", sect.PassiveLevel);
            DrawLever(new Rect(btnX + btnW4 + 4f,          btnY, btnW4, 20f), faction, sectId, SectLeverKind.Building,    "B", sect.BuildingLevel);
            DrawLever(new Rect(btnX + (btnW4 + 4f) * 2,    btnY, btnW4, 20f), faction, sectId, SectLeverKind.Unit,        "U", sect.UnitLevel);
            DrawLever(new Rect(btnX + (btnW4 + 4f) * 3,    btnY, btnW4, 20f), faction, sectId, SectLeverKind.ActivePower, "A", sect.ActivePowerLevel);

            // Fire button — top-right of cell.
            if (sect.ActivePowerLevel > 0)
            {
                var fireRect = new Rect(rect.x + rect.width - 52f, rect.y + 2f, 48f, 18f);
                float remaining = SectActivePowerHelper.CooldownRemaining(_em, faction, sectId);
                bool ready = remaining <= 0f;
                GUI.enabled = ready;
                string label = ready ? "Fire" : $"{(int)remaining}s";
                if (GUI.Button(fireRect, label, _btnStyle))
                    FireActivePower(faction, sectId, temple);
                GUI.enabled = true;
            }
        }

        private void DrawLever(Rect rect, Faction faction, string sectId,
            SectLeverKind lever, string letter, byte currentLevel)
        {
            string label;
            bool enabled;
            string tooltip;
            if (currentLevel >= 3)
            {
                label = $"{letter}III";
                enabled = false;
                tooltip = $"{LeverLabel(lever)} — fully upgraded.\n\n{LeverDescription(sectId, lever)}";
            }
            else
            {
                var check = SectAdoption.CanUpgradeLever(_em, faction, sectId, lever,
                    out int cost, out var matCost);
                enabled = check == SectAdoptionResult.Ok;
                label = $"{letter}{Roman(currentLevel + 1)} ({cost})";
                string matLine = matCost.IsZero ? "" : $"  +  {TheWaningBorder.UI.Common.UIHelpers.FormatCost(matCost)}";
                tooltip = $"{LeverLabel(lever)} → Lv {Roman(currentLevel + 1)}\nCost: {cost} RP{matLine}\n\n{LeverDescription(sectId, lever)}";
            }
            GUI.enabled = enabled;
            if (GUI.Button(rect, new GUIContent(label, tooltip), _btnStyle))
                SectAdoption.TryUpgradeLever(_em, faction, sectId, lever);
            GUI.enabled = true;
        }

        private static string LeverLabel(SectLeverKind lever) => lever switch
        {
            SectLeverKind.Passive     => "Passive",
            SectLeverKind.Building    => "Building aura",
            SectLeverKind.Unit        => "Unit bonus",
            SectLeverKind.ActivePower => "Active power",
            _                         => "Lever",
        };

        private static string LeverDescription(string sectId, SectLeverKind lever) => lever switch
        {
            SectLeverKind.Passive     => SectInfo.PassiveDescription(sectId),
            SectLeverKind.Building    => SectInfo.BuildingDescription(sectId),
            SectLeverKind.Unit        => SectInfo.UnitDescription(sectId),
            SectLeverKind.ActivePower => SectInfo.ActivePowerDescription(sectId),
            _                         => "",
        };

        // ──────────────────────────────────────────────────────────────────
        // ACTIONS
        // ──────────────────────────────────────────────────────────────────

        private void StartAdoption(Faction faction, Entity temple, string sectId,
            TheWaningBorder.Core.Cost chapelCost)
        {
            if (!_em.Exists(temple) || !_em.HasBuffer<TempleChapelSlot>(temple)) return;

            var result = SectAdoption.TryStartAdoption(_em, faction, sectId, chapelCost, temple);
            if (result != SectAdoptionResult.Ok) return;

            var slots = _em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].State != 0) continue;
                slots[i] = new TempleChapelSlot
                {
                    Chapel        = Entity.Null,
                    SectId        = new Unity.Collections.FixedString64Bytes(sectId),
                    State         = 1,
                    BuildProgress = 0f,
                    BuildTime     = ChapelBuildSeconds,
                };
                return;
            }
        }

        private void FireActivePower(Faction faction, string sectId, Entity temple)
        {
            if (!_em.Exists(temple) || !_em.HasComponent<LocalTransform>(temple)) return;
            var t = _em.GetComponentData<LocalTransform>(temple);
            SectActivePowerHelper.Fire(_em, faction, sectId, t.Position);
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        private bool HasFreeSlot(Entity temple)
        {
            if (temple == Entity.Null) return false;
            if (!_em.HasBuffer<TempleChapelSlot>(temple)) return false;
            var slots = _em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].State == 0) return true;
            return false;
        }

        private bool HasInFlightBuildForSect(Entity temple, string sectId)
        {
            if (temple == Entity.Null) return false;
            if (!_em.HasBuffer<TempleChapelSlot>(temple)) return false;
            var slots = _em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].State == 1 && slots[i].SectId == sectId) return true;
            return false;
        }

        private bool TryGetTemple(Faction faction, out Entity temple)
        {
            temple = Entity.Null;
            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (_em.GetComponentData<FactionTag>(entities[i]).Value == faction)
                    {
                        temple = entities[i]; return true;
                    }
                }
            }
            var legacyQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var legacy = legacyQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < legacy.Length; i++)
                {
                    if (_em.GetComponentData<FactionTag>(legacy[i]).Value == faction)
                    {
                        temple = legacy[i]; return true;
                    }
                }
            }
            return false;
        }

        private byte LookupCulture(Faction faction)
        {
            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var progress = query.ToComponentDataArray<FactionProgress>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                if (factions[i].Value == faction) return progress[i].Culture;
            return Cultures.None;
        }

        private static string ShortName(string sectId)
        {
            if (string.IsNullOrEmpty(sectId)) return "?";
            const string p = "Sect_";
            return sectId.StartsWith(p) ? sectId.Substring(p.Length) : sectId;
        }

        private static string Roman(int level) => level switch
        {
            1 => "I", 2 => "II", 3 => "III", _ => level.ToString(),
        };

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _slotStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true,
            };
            _slotEmptyStyle = new GUIStyle(_slotStyle)
            {
                fontStyle = FontStyle.Italic,
                fontSize = 10,
            };
            // Empty-slot button — same italic small-text look as the label,
            // but clickable + gold hover so it reads as an action surface.
            _slotEmptyButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Italic,
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.68f, 0.60f) },
                hover  = { textColor = TheWaningBorder.UI.Common.Styles.HighlightColor },
            };
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
            };
            _toggleStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
            };
            // Tooltip — dark navy box with golden text, word-wrapped at ~320 px.
            _tooltipBgTex = new Texture2D(1, 1);
            _tooltipBgTex.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.12f, 0.95f));
            _tooltipBgTex.Apply();
            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 11,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6),
                normal = { textColor = new Color(0.95f, 0.92f, 0.82f), background = _tooltipBgTex },
            };
            _stylesBuilt = true;
        }

        // ──────────────────────────────────────────────────────────────────
        // TOOLTIP — IMGUI exposes the last-hovered GUIContent's tooltip via
        // GUI.tooltip. Render it next to the cursor so the strip's gold-on-
        // navy slot buttons and lever upgrade buttons all share the same
        // hover behaviour without each caller drawing its own panel.
        // ──────────────────────────────────────────────────────────────────
        private void DrawTooltip()
        {
            string text = GUI.tooltip;
            if (string.IsNullOrEmpty(text)) return;
            if (_tooltipStyle == null) return;

            const float maxWidth = 320f;
            var content = new GUIContent(text);
            // CalcHeight reads the wrapped height for the given fixed width.
            float h = _tooltipStyle.CalcHeight(content, maxWidth);
            Vector2 mouse = Event.current.mousePosition;

            // Anchor below-and-right of the cursor; clamp to screen so we
            // never spill off the edge.
            float x = Mathf.Min(mouse.x + 16f, Screen.width  - maxWidth - 4f);
            float y = Mathf.Min(mouse.y + 18f, Screen.height - h        - 4f);
            GUI.Box(new Rect(x, y, maxWidth, h), content, _tooltipStyle);
        }
    }
}
