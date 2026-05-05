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
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.Input;
using TheWaningBorder.Systems.Sect;
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
        private GUIStyle _btnStyle;
        private GUIStyle _toggleStyle;
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

            BuildStyles();

            var faction = GameSettings.LocalPlayerFaction;
            bool hasTemple = TryGetTemple(faction, out var temple);
            int rp = FactionReligionPointsHelper.GetBalance(_em, faction);

            DrawSlotStrip(faction, hasTemple ? temple : Entity.Null, rp);
            if (_rosterOpen)
                DrawRoster(faction, hasTemple ? temple : Entity.Null, rp);
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
            string toggleLabel = _rosterOpen ? "[ Hide Sects ]" : "[ Manage Sects ]";
            if (GUI.Button(new Rect(x0 + total - 140f, headerY, 140f, 20f),
                toggleLabel, _toggleStyle))
            {
                _rosterOpen = !_rosterOpen;
            }

            DynamicBuffer<TempleChapelSlot> slots = default;
            bool hasSlots = temple != Entity.Null && _em.HasBuffer<TempleChapelSlot>(temple);
            if (hasSlots) slots = _em.GetBuffer<TempleChapelSlot>(temple);

            for (int i = 0; i < StripSlotCount; i++)
            {
                var rect = new Rect(x0 + i * (StripSlotWidth + StripSlotSpacing), y,
                    StripSlotWidth, StripSlotHeight);

                if (!hasSlots || i >= slots.Length)
                {
                    GUI.Label(rect, "Slot\n(no temple)", _slotEmptyStyle);
                    continue;
                }

                var slot = slots[i];
                if (slot.State == 0)
                {
                    GUI.Label(rect, $"Slot {i + 1}\n(empty)", _slotEmptyStyle);
                    continue;
                }
                if (slot.State == 1)
                {
                    int pct = slot.BuildTime > 0 ? (int)(100f * slot.BuildProgress / slot.BuildTime) : 0;
                    GUI.Label(rect, $"{ShortName(slot.SectId.ToString())}\n{pct}%", _slotStyle);
                    continue;
                }

                // State 2 — adopted. Show short name + Fire button if AP bought.
                string sectId = slot.SectId.ToString();
                GUI.Label(new Rect(rect.x, rect.y, rect.width, 26f),
                    ShortName(sectId), _slotStyle);

                byte apLevel = SectQuery.LevelOf(_em, faction, sectId, SectLeverKind.ActivePower);
                if (apLevel > 0)
                {
                    var btnRect = new Rect(rect.x + 4, rect.y + 28, rect.width - 8, 22);
                    float remaining = SectActivePowerHelper.CooldownRemaining(_em, faction, sectId);
                    bool ready = remaining <= 0f;
                    GUI.enabled = ready;
                    string label = ready ? "Fire" : $"{(int)remaining}s";
                    if (GUI.Button(btnRect, label, _btnStyle))
                        FireActivePower(faction, sectId, temple);
                    GUI.enabled = true;
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
            if (currentLevel >= 3)
            {
                label = $"{letter}III";
                enabled = false;
            }
            else
            {
                var check = SectAdoption.CanUpgradeLever(_em, faction, sectId, lever, out int cost);
                enabled = check == SectAdoptionResult.Ok;
                label = $"{letter}{Roman(currentLevel + 1)} ({cost})";
            }
            GUI.enabled = enabled;
            if (GUI.Button(rect, label, _btnStyle))
                SectAdoption.TryUpgradeLever(_em, faction, sectId, lever);
            GUI.enabled = true;
        }

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
            _stylesBuilt = true;
        }
    }
}
