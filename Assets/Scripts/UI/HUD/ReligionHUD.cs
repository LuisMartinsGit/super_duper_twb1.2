// File: Assets/Scripts/UI/HUD/ReligionHUD.cs
// Top-center HUD strip showing the local player's 6 sect chapel slots.
// Hidden whenever the player has anything selected — matches the spec
// behaviour ("when nothing is selected: top-center Religion HUD").
//
// Each slot shows: empty / building / adopted-with-letter (A/R/F/Re/S/J/V/W/W/A/R/W).
// Adopted slots gain a small Fire button when the sect's Active Power
// lever is bought; the cast originates at the temple position (a future
// pass adds target-cursor mode).
//
// Audit fix #3.

using System.Collections.Generic;
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
        public const float SlotWidth = 72f;
        public const float SlotHeight = 56f;
        public const float SlotSpacing = 4f;
        public const float TopMargin = 14f;
        private const int SlotCount = 6;

        private EntityWorld _world;
        private EntityManager _em;

        private GUIStyle _slotStyle;
        private GUIStyle _slotEmptyStyle;
        private GUIStyle _fireBtnStyle;
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

            // Hide whenever the player has anything selected.
            if (SelectionSystem.CurrentSelection != null
                && SelectionSystem.CurrentSelection.Count > 0)
                return;

            BuildStyles();

            var faction = GameSettings.LocalPlayerFaction;
            if (!TryGetTemple(faction, out var temple))
            {
                DrawNoTempleHint();
                return;
            }

            DrawSlots(faction, temple);
        }

        private void DrawNoTempleHint()
        {
            float total = SlotWidth * SlotCount + SlotSpacing * (SlotCount - 1);
            float x = (Screen.width - total) * 0.5f;
            var rect = new Rect(x, TopMargin, total, SlotHeight);
            GUI.Label(rect, "Religion HUD: build a Temple of Ridan to enable sect adoption.", _slotStyle);
        }

        private void DrawSlots(Faction faction, Entity temple)
        {
            if (!_em.HasBuffer<TempleChapelSlot>(temple)) return;
            var slots = _em.GetBuffer<TempleChapelSlot>(temple);

            float total = SlotWidth * SlotCount + SlotSpacing * (SlotCount - 1);
            float x0 = (Screen.width - total) * 0.5f;
            float y = TopMargin;

            for (int i = 0; i < SlotCount; i++)
            {
                var rect = new Rect(x0 + i * (SlotWidth + SlotSpacing), y, SlotWidth, SlotHeight);
                if (i >= slots.Length) { GUI.Label(rect, $"Slot {i + 1}", _slotEmptyStyle); continue; }

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

                // State 2 — chapel complete, sect adopted.
                string sectId = slot.SectId.ToString();
                GUI.Label(rect, ShortName(sectId), _slotStyle);

                // Fire button if the Active Power lever is bought.
                byte apLevel = SectQuery.LevelOf(_em, faction, sectId, SectLeverKind.ActivePower);
                if (apLevel > 0)
                {
                    var btnRect = new Rect(rect.x + 4, rect.y + 28, rect.width - 8, 22);
                    float remaining = SectActivePowerHelper.CooldownRemaining(_em, faction, sectId);
                    bool ready = remaining <= 0f;
                    GUI.enabled = ready;
                    string label = ready ? "Fire" : $"{(int)remaining}s";
                    if (GUI.Button(btnRect, label, _fireBtnStyle))
                    {
                        if (_em.HasComponent<LocalTransform>(temple))
                        {
                            var t = _em.GetComponentData<LocalTransform>(temple);
                            SectActivePowerHelper.Fire(_em, faction, sectId, t.Position);
                        }
                    }
                    GUI.enabled = true;
                }
            }
        }

        private bool TryGetTemple(Faction faction, out Entity temple)
        {
            temple = Entity.Null;
            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (_em.GetComponentData<FactionTag>(entities[i]).Value == faction)
                {
                    temple = entities[i];
                    return true;
                }
            }
            // Fallback to TempleTag (legacy alias).
            var legacyQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var legacy = legacyQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < legacy.Length; i++)
            {
                if (_em.GetComponentData<FactionTag>(legacy[i]).Value == faction)
                {
                    temple = legacy[i];
                    return true;
                }
            }
            return false;
        }

        private static string ShortName(string sectId)
        {
            // SectConfig ids look like "Sect_Antiquity" — strip the prefix.
            if (string.IsNullOrEmpty(sectId)) return string.Empty;
            const string p = "Sect_";
            return sectId.StartsWith(p) ? sectId.Substring(p.Length) : sectId;
        }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _slotStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true,
            };
            _slotEmptyStyle = new GUIStyle(_slotStyle)
            {
                fontStyle = FontStyle.Italic,
            };
            _fireBtnStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
            };
            _stylesBuilt = true;
        }
    }
}
