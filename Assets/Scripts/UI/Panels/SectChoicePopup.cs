// File: Assets/Scripts/UI/Panels/SectChoicePopup.cs
// Modal popup for choosing a sect to adopt into a specific Temple chapel slot.
//
// Opened by clicking one of the six ground decals around the Temple of Ridan,
// or — as a fallback while world decals aren't shipped — by clicking an empty
// slot in the Religion HUD strip.
//
// Mirrors CultureChoicePopup's 620×340 modal pattern (dim background + bordered
// navy panel) but uses a dropdown (prev / next arrows) to browse the 12 sects
// since three side-by-side columns don't fit 12 options. The visible body shows:
//   • Lore line   — flavour from SectInfo.Lore
//   • Active     — SectInfo.ActivePowerDescription
//   • Passive    — SectInfo.PassiveDescription
//   • Building   — SectInfo.BuildingDescription (chapel aura)
//   • Unit       — SectInfo.UnitDescription
//   • Technology — SectInfo.TechnologyDescription
//
// Adopt cost = SectConfig.AdoptionCost (RP) + BuildCosts.ChapelMaterialCost
// (Supplies + Crystal + Iron). The Adopt button greys out unless all are
// affordable + the slot is free + no in-flight build for the same sect exists.

using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Data;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.Panels
{
    public class SectChoicePopup : MonoBehaviour
    {
        // ─── State ───────────────────────────────────────────────────────
        private static bool _visible;
        private static Entity _temple;
        private static int _slotIndex;
        private static Faction _faction;
        private static int _browseIndex;
        private static Rect _popupRect;

        // ─── Layout ──────────────────────────────────────────────────────
        private const float PopupWidth  = 620f;
        private const float PopupHeight = 340f;
        private const float Padding     = 16f;

        // ─── Styles ──────────────────────────────────────────────────────
        private GUIStyle _headerStyle;
        private GUIStyle _sectNameStyle;
        private GUIStyle _loreStyle;
        private GUIStyle _sectionLabelStyle;
        private GUIStyle _sectionBodyStyle;
        private GUIStyle _costAffordable;
        private GUIStyle _costUnaffordable;
        private GUIStyle _arrowStyle;
        private GUIStyle _adoptStyle;
        private bool _stylesInit;

        // ─── Background image ────────────────────────────────────────────
        private static Texture2D _bgImage;
        private static bool _bgLoaded;

        // ═══════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Open the sect picker bound to a specific chapel slot on a temple.
        /// Slot index is 0..5; the popup remembers it so Adopt writes into
        /// that slot specifically (matching the decal the player clicked).
        /// </summary>
        public static void Show(Entity temple, int slotIndex, Faction faction)
        {
            _temple = temple;
            _slotIndex = slotIndex;
            _faction = faction;
            _browseIndex = 0;
            _visible = true;
        }

        public static void Close()
        {
            _visible = false;
            _temple = Entity.Null;
            _slotIndex = -1;
        }

        public static bool IsVisible => _visible;
        public static Entity CurrentTemple => _temple;
        public static int CurrentSlot => _slotIndex;

        /// <summary>True if the mouse is over the popup, for input blocking.</summary>
        public static bool IsPointerOver()
        {
            if (!_visible) return false;
            var mousePos = UnityEngine.Input.mousePosition;
            var screenRect = new Rect(
                _popupRect.x,
                Screen.height - _popupRect.y - _popupRect.height,
                _popupRect.width,
                _popupRect.height);
            return screenRect.Contains(mousePos);
        }

        // ═══════════════════════════════════════════════════════════
        // IMGUI
        // ═══════════════════════════════════════════════════════════

        void OnGUI()
        {
            if (!_visible) return;

            Styles.Initialize();
            InitStyles();
            EnsureBackgroundLoaded();

            // Dim background. 0.6 alpha matches CultureChoicePopup.
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = (Screen.width - PopupWidth) * 0.5f;
            float y = (Screen.height - PopupHeight) * 0.5f;
            _popupRect = new Rect(x, y, PopupWidth, PopupHeight);

            // Optional background art behind the panel — falls back to the
            // bordered navy panel if no asset is present.
            if (_bgImage != null)
                GUI.DrawTexture(_popupRect, _bgImage, ScaleMode.ScaleAndCrop);

            // Bordered navy panel on top (preserves the readable theme even
            // when the art is loud or absent).
            GUI.Box(_popupRect, "", Styles.PanelBox);

            var em = EntityWorld.DefaultGameObjectInjectionWorld?.EntityManager
                     ?? default;
            bool emValid = !em.Equals(default(EntityManager));

            string sectId = SectConfig.IdAt(_browseIndex);
            if (sectId == null) sectId = SectConfig.AllSectIds[0];

            var inner = new Rect(
                _popupRect.x + Padding,
                _popupRect.y + Padding,
                _popupRect.width - Padding * 2f,
                _popupRect.height - Padding * 2f);

            GUILayout.BeginArea(inner);

            // ── Header row: title centered, slot index right-aligned ──
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sect Adoption", _headerStyle);
            GUILayout.FlexibleSpace();
            string slotLabel = _slotIndex >= 0 ? $"Chapel slot {_slotIndex + 1} / 6" : "";
            GUILayout.Label(slotLabel, _sectionLabelStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ── Dropdown row: ◀ Sect Name ▶ ──
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("◀", _arrowStyle, GUILayout.Width(36), GUILayout.Height(28)))
                _browseIndex = (_browseIndex + SectConfig.SectCount - 1) % SectConfig.SectCount;

            GUILayout.Space(8);
            GUILayout.Label(SectInfo.ShortName(sectId) + "  ·  " + SectConfig.ClusterOf(sectId),
                _sectNameStyle, GUILayout.Width(280), GUILayout.Height(28));
            GUILayout.Space(8);

            if (GUILayout.Button("▶", _arrowStyle, GUILayout.Width(36), GUILayout.Height(28)))
                _browseIndex = (_browseIndex + 1) % SectConfig.SectCount;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label(SectInfo.Lore(sectId), _loreStyle);
            GUILayout.Space(6);

            // ── Description body — two columns ──
            GUILayout.BeginHorizontal();

            // Left column: Passive / Building / Unit
            GUILayout.BeginVertical(GUILayout.Width((inner.width - 16f) * 0.5f));
            DrawSection("Passive",  SectInfo.PassiveDescription(sectId));
            DrawSection("Building", SectInfo.BuildingDescription(sectId));
            DrawSection("Unit",     SectInfo.UnitDescription(sectId));
            GUILayout.EndVertical();

            GUILayout.Space(16);

            // Right column: Active / Technology
            GUILayout.BeginVertical(GUILayout.Width((inner.width - 16f) * 0.5f));
            DrawSection("Active Power", SectInfo.ActivePowerDescription(sectId));
            DrawSection("Technology",   SectInfo.TechnologyDescription(sectId));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            // ── Cost + buttons row ──
            DrawCostAndButtons(em, emValid, sectId);

            GUILayout.EndArea();

            // Block all input behind the popup.
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
                Event.current.Use();
        }

        private void DrawSection(string label, string body)
        {
            GUILayout.Label(label, _sectionLabelStyle);
            GUILayout.Label(body, _sectionBodyStyle);
            GUILayout.Space(2);
        }

        private void DrawCostAndButtons(EntityManager em, bool emValid, string sectId)
        {
            // Cost computation
            int rpCost = -1;
            Cost matCost = default;
            bool canRp = false;
            bool canMat = false;
            bool canAdopt = false;
            string failReason = null;

            if (emValid && _temple != Entity.Null && em.Exists(_temple))
            {
                var check = SectAdoption.CanAdopt(em, _faction, sectId, out rpCost);
                matCost = BuildCosts.ChapelMaterialCost;
                canRp  = check != SectAdoptionResult.NotEnoughRP;
                canMat = FactionEconomy.CanAfford(em, _faction, matCost);
                bool slotOk = IsSlotFreeOrTargeted(em);

                // Status precedence: structural blockers (slot/adoption state)
                // shadow affordability blockers, since the affordability check
                // only matters once the structural conditions are satisfied.
                if (check == SectAdoptionResult.AlreadyAdopted) failReason = "Already adopted";
                else if (check == SectAdoptionResult.SlotsFull) failReason = "All chapel slots full";
                else if (!slotOk)                                failReason = "Slot occupied";
                else if (check == SectAdoptionResult.NotEnoughRP) failReason = $"Need {rpCost} RP";
                else if (check == SectAdoptionResult.Ok && !canMat) failReason = "Need materials";
                else if (check != SectAdoptionResult.Ok)         failReason = check.ToString();

                canAdopt = check == SectAdoptionResult.Ok && canMat && slotOk;
            }
            else
            {
                failReason = "No temple";
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(rpCost > 0 ? $"Cost: {rpCost} RP" : "Cost: —",
                canRp ? _costAffordable : _costUnaffordable, GUILayout.Width(120));
            GUILayout.Label(UIHelpers.FormatCost(matCost),
                canMat ? _costAffordable : _costUnaffordable);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUI.enabled = canAdopt;
            string adoptLabel = canAdopt ? "Adopt" : (failReason ?? "Adopt");
            if (GUILayout.Button(adoptLabel, _adoptStyle, GUILayout.Height(32), GUILayout.Width(220)))
                Adopt(em, sectId);
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", Styles.Button, GUILayout.Height(32), GUILayout.Width(120)))
                Close();
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════
        // ADOPT
        // ═══════════════════════════════════════════════════════════

        private void Adopt(EntityManager em, string sectId)
        {
            if (em.Equals(default(EntityManager))) return;
            if (!em.Exists(_temple) || !em.HasBuffer<TempleChapelSlot>(_temple)) return;

            // Targeted-slot guard: refuse if the popup's slot has been filled
            // by another path (a duplicate click on a different decal that won
            // the race for the same sect). This preserves the "decal = slot"
            // invariant so each decal builds exactly one chapel.
            if (!IsSlotFreeOrTargeted(em)) return;

            var matCost = BuildCosts.ChapelMaterialCost;
            var result = SectAdoption.TryStartAdoption(em, _faction, sectId, matCost, _temple);
            if (result != SectAdoptionResult.Ok) return;

            // Stamp the slot — same shape the Religion HUD's old roster wrote.
            var slots = em.GetBuffer<TempleChapelSlot>(_temple);
            int idx = _slotIndex;
            if (idx < 0 || idx >= slots.Length || slots[idx].State != 0)
            {
                // Fallback — first free slot.
                idx = -1;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].State == 0) { idx = i; break; }
                }
            }
            if (idx < 0) return;

            slots[idx] = new TempleChapelSlot
            {
                Chapel        = Entity.Null,
                SectId        = new Unity.Collections.FixedString64Bytes(sectId),
                State         = 1,
                BuildProgress = 0f,
                BuildTime     = 30f, // matches ReligionHUD.ChapelBuildSeconds
            };

            Close();
        }

        private bool IsSlotFreeOrTargeted(EntityManager em)
        {
            if (_slotIndex < 0) return true; // fallback path; popup will pick first free
            if (!em.HasBuffer<TempleChapelSlot>(_temple)) return false;
            var slots = em.GetBuffer<TempleChapelSlot>(_temple);
            if (_slotIndex >= slots.Length) return false;
            return slots[_slotIndex].State == 0;
        }

        // ═══════════════════════════════════════════════════════════
        // BACKGROUND ART
        // ═══════════════════════════════════════════════════════════

        private static void EnsureBackgroundLoaded()
        {
            if (_bgLoaded) return;
            _bgImage = Resources.Load<Texture2D>("Sprites/Sects/Background");
            _bgLoaded = true;
        }

        // ═══════════════════════════════════════════════════════════
        // STYLES
        // ═══════════════════════════════════════════════════════════

        private void InitStyles()
        {
            if (_stylesInit) return;

            _headerStyle = new GUIStyle(Styles.Header)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleLeft,
            };

            _sectNameStyle = new GUIStyle(Styles.SubHeader)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Styles.HighlightColor },
            };

            _loreStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.78f, 0.76f, 0.68f) },
            };

            _sectionLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor },
            };

            _sectionBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.90f, 0.88f, 0.82f) },
            };

            _costAffordable = new GUIStyle(Styles.CostStyleAffordable)
            {
                alignment = TextAnchor.MiddleLeft,
            };
            _costUnaffordable = new GUIStyle(Styles.CostStyleUnaffordable)
            {
                alignment = TextAnchor.MiddleLeft,
            };

            _arrowStyle = new GUIStyle(Styles.Button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            _adoptStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Styles.HighlightColor },
                hover  = { textColor = Color.white },
            };

            _stylesInit = true;
        }
    }
}
