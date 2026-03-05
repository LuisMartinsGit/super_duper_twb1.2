// SectAdoptionPanel.cs
// IMGUI panel for sect adoption — drawn inline within the temple panel
// Location: Assets/Scripts/UI/Panels/SectAdoptionPanel.cs

using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using TheWaningBorder.Economy;
using TheWaningBorder.UI;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// Draws the sect adoption section inside the temple panel.
    /// Shows 12 sects grouped by culture (Alanthor, Runai, Feraldis), 4 each.
    /// Each entry displays name, affinity icon, cost (1 or 3 RP), and an Adopt button
    /// or "Adopted" label. Culture-colored names use Alanthor=sage, Runai=cyan, Feraldis=red.
    ///
    /// Accessed from EntityActionPanel.DrawTempleLevelUpPanel() after the level-up section.
    /// </summary>
    public static class SectAdoptionPanel
    {
        // ═══════════════════════════════════════════════════════════════════
        // STYLES (lazily initialized)
        // ═══════════════════════════════════════════════════════════════════

        private static GUIStyle _sectionHeaderStyle;
        private static GUIStyle _cultureHeaderStyle;
        private static GUIStyle _sectNameStyle;
        private static GUIStyle _sectDescStyle;
        private static GUIStyle _adoptButtonStyle;
        private static GUIStyle _adoptedLabelStyle;
        private static GUIStyle _costStyle;
        private static GUIStyle _rpDisplayStyle;
        private static GUIStyle _synergyStyle;
        private static bool _stylesInit;

        // ═══════════════════════════════════════════════════════════════════
        // CULTURE COLORS
        // ═══════════════════════════════════════════════════════════════════

        private static readonly Color AlanthorColor = new Color(0.55f, 0.65f, 0.50f);  // sage green
        private static readonly Color RunaiColor = new Color(0.25f, 0.75f, 0.80f);     // cyan
        private static readonly Color FeraldisColor = new Color(0.70f, 0.18f, 0.15f);  // crimson red
        private static readonly Color GoldAccent = new Color(0.83f, 0.66f, 0.26f);
        private static readonly Color DimText = new Color(0.7f, 0.68f, 0.60f);
        private static readonly Color BrightText = new Color(0.9f, 0.88f, 0.82f);

        // ═══════════════════════════════════════════════════════════════════
        // SECT GROUPS
        // ═══════════════════════════════════════════════════════════════════

        private static readonly string[] AlanthorSects =
        {
            SectConfig.Renewal, SectConfig.Antiquity,
            SectConfig.LivingStone, SectConfig.VeiledMemory
        };

        private static readonly string[] RunaiSects =
        {
            SectConfig.StillFlame, SectConfig.QuietVault,
            SectConfig.MirrorRite, SectConfig.ShardJudgment
        };

        private static readonly string[] FeraldisSects =
        {
            SectConfig.EmberAsh, SectConfig.HollowBrand,
            SectConfig.FlamewroughtChains, SectConfig.UnmakersGrasp
        };

        // ═══════════════════════════════════════════════════════════════════
        // PUBLIC DRAW METHOD
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw the sect adoption section. Called from within a GUILayout.BeginArea.
        /// Requires a valid faction with a culture (Era 2+).
        /// </summary>
        public static void Draw(Faction faction)
        {
            InitStyles();

            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            byte culture = FactionColors.GetFactionCulture(faction);
            if (culture == Cultures.None)
            {
                GUILayout.Label("Choose a culture to unlock sects", _sectDescStyle);
                return;
            }

            // Get current RP
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            int currentRP = EntityInfoExtractor.GetFactionReligionPoints(em, faction);

            // ── Separator ──
            GUILayout.Space(8);
            var sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.4f);
            GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.Space(6);

            // ── Header with RP display ──
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sect Adoption", _sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"RP: {currentRP}", _rpDisplayStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // ── Draw sects grouped by culture ──
            // Player's affinity culture first, then others
            if (culture == Cultures.Alanthor)
            {
                DrawCultureGroup("Alanthor", AlanthorSects, AlanthorColor, faction, sectState, currentRP, true);
                DrawCultureGroup("Runai", RunaiSects, RunaiColor, faction, sectState, currentRP, false);
                DrawCultureGroup("Feraldis", FeraldisSects, FeraldisColor, faction, sectState, currentRP, false);
            }
            else if (culture == Cultures.Runai)
            {
                DrawCultureGroup("Runai", RunaiSects, RunaiColor, faction, sectState, currentRP, true);
                DrawCultureGroup("Alanthor", AlanthorSects, AlanthorColor, faction, sectState, currentRP, false);
                DrawCultureGroup("Feraldis", FeraldisSects, FeraldisColor, faction, sectState, currentRP, false);
            }
            else if (culture == Cultures.Feraldis)
            {
                DrawCultureGroup("Feraldis", FeraldisSects, FeraldisColor, faction, sectState, currentRP, true);
                DrawCultureGroup("Alanthor", AlanthorSects, AlanthorColor, faction, sectState, currentRP, false);
                DrawCultureGroup("Runai", RunaiSects, RunaiColor, faction, sectState, currentRP, false);
            }

            // ── Active synergies ──
            DrawActiveSynergies(faction, sectState);
        }

        // ═══════════════════════════════════════════════════════════════════
        // CULTURE GROUP DRAWING
        // ═══════════════════════════════════════════════════════════════════

        private static void DrawCultureGroup(string cultureName, string[] sectIds, Color cultureColor,
            Faction faction, FactionSectState sectState, int currentRP, bool isAffinity)
        {
            GUILayout.Space(4);

            // Culture header
            var savedColor = _cultureHeaderStyle.normal.textColor;
            _cultureHeaderStyle.normal.textColor = cultureColor;
            string headerText = isAffinity ? $"{cultureName} (Affinity)" : cultureName;
            GUILayout.Label(headerText, _cultureHeaderStyle);
            _cultureHeaderStyle.normal.textColor = savedColor;

            // Draw each sect in this culture
            foreach (var sectId in sectIds)
            {
                DrawSectEntry(sectId, cultureColor, faction, sectState, currentRP);
            }
        }

        private static void DrawSectEntry(string sectId, Color cultureColor,
            Faction faction, FactionSectState sectState, int currentRP)
        {
            bool isAdopted = sectState.HasAdopted(faction, sectId);
            int cost = sectState.GetAdoptionCost(faction, sectId);
            bool canAfford = currentRP >= cost;
            string displayName = SectConfig.GetDisplayName(sectId);
            string passiveDesc = SectConfig.GetPassiveDescription(sectId);

            GUILayout.BeginHorizontal();

            // Sect name (culture-colored) + passive description
            GUILayout.BeginVertical(GUILayout.Width(200));

            var savedNameColor = _sectNameStyle.normal.textColor;
            _sectNameStyle.normal.textColor = cultureColor;
            GUILayout.Label(displayName, _sectNameStyle);
            _sectNameStyle.normal.textColor = savedNameColor;

            GUILayout.Label(passiveDesc, _sectDescStyle);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Adopt button or Adopted label
            if (isAdopted)
            {
                GUILayout.Label("Adopted", _adoptedLabelStyle, GUILayout.Width(70), GUILayout.Height(24));
            }
            else
            {
                // Cost label
                var costColor = canAfford ? new Color(0.3f, 0.9f, 0.3f) : new Color(1f, 0.3f, 0.3f);
                var savedCostColor = _costStyle.normal.textColor;
                _costStyle.normal.textColor = costColor;
                GUILayout.Label($"{cost} RP", _costStyle, GUILayout.Width(30));
                _costStyle.normal.textColor = savedCostColor;

                bool wasEnabled = GUI.enabled;
                if (!canAfford) GUI.enabled = false;

                if (GUILayout.Button("Adopt", _adoptButtonStyle, GUILayout.Width(56), GUILayout.Height(24)))
                {
                    if (sectState.TryAdopt(faction, sectId))
                    {
                        PlayerNotificationSystem.Notify($"Adopted {displayName}!");
                    }
                    else
                    {
                        PlayerNotificationSystem.NotifyError("Cannot adopt sect");
                    }
                    Event.current.Use();
                }

                GUI.enabled = wasEnabled;
            }

            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════════
        // ACTIVE SYNERGIES
        // ═══════════════════════════════════════════════════════════════════

        private static void DrawActiveSynergies(Faction faction, FactionSectState sectState)
        {
            bool hasSynergy = false;

            foreach (var pair in SectConfig.SynergyPairs)
            {
                if (sectState.HasAdopted(faction, pair.SectA) &&
                    sectState.HasAdopted(faction, pair.SectB))
                {
                    if (!hasSynergy)
                    {
                        GUILayout.Space(6);
                        var sepRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                        GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.3f);
                        GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
                        GUI.color = Color.white;
                        GUILayout.Space(4);
                        hasSynergy = true;
                    }

                    GUILayout.Label($"Synergy: {pair.Name} — {pair.Description}", _synergyStyle);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // STYLE INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════

        private static void InitStyles()
        {
            if (_stylesInit) return;

            _sectionHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = GoldAccent }
            };

            _cultureHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = BrightText }
            };

            _sectNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = BrightText }
            };

            _sectDescStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = DimText }
            };

            _adoptButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = GoldAccent },
                hover = { textColor = Color.white }
            };

            _adoptedLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.4f, 0.8f, 0.4f) }
            };

            _costStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = BrightText }
            };

            _rpDisplayStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.9f, 0.8f, 0.3f) }
            };

            _synergyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Italic,
                normal = { textColor = GoldAccent }
            };

            _stylesInit = true;
        }
    }
}
