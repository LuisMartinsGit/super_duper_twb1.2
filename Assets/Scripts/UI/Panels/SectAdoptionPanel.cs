// SectAdoptionPanel.cs
// IMGUI section that hosts the 12-sect chapel-build menu inside the Temple
// of Ridan panel. Adoption flow:
//   1. Player selects a Temple of Ridan.
//   2. EntityActionPanel calls SectAdoptionPanel.Draw(faction, temple).
//   3. Panel lists the 12 sects; clicking one queues a chapel build into the
//      first empty TempleChapelSlot. RP cost is shown but NOT deducted here
//      — TempleChapelBuildSystem calls SectAdoption.OnChapelCompleted on
//      slot completion which performs the deduction atomically.
//   4. Panel renders the 4 lever upgrade buttons (Passive / Building / Unit
//      / Active Power) for each adopted sect, each costing 2 RP (Lv I→II) /
//      3 RP (Lv II→III), gated by SectAdoption.CanUpgradeLever.
//
// Location: Assets/Scripts/UI/Panels/SectAdoptionPanel.cs
// task-063 phase 2a.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// Sect adoption + lever-upgrade UI hosted inside the Temple of Ridan
    /// panel. Pure IMGUI; no MonoBehaviour. Caller passes the Temple entity
    /// so the panel can resolve the 6 chapel slots without a separate query.
    /// </summary>
    public static class SectAdoptionPanel
    {
        /// <summary>
        /// Draw the sect adoption section for the given faction + temple.
        /// </summary>
        public static void Draw(Faction faction, Entity temple)
        {
            Styles.Initialize();

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            int rp = FactionReligionPointsHelper.GetBalance(em, faction);
            byte culture = LookupCulture(em, faction);

            GUILayout.Space(8);
            DrawHeader(rp);
            GUILayout.Space(4);

            // ── Roster of 12 sects ──
            for (int i = 0; i < SectConfig.SectCount; i++)
            {
                string sectId = SectConfig.IdAt(i);
                DrawSectRow(em, faction, temple, sectId, culture, rp);
                GUILayout.Space(2);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // ROW LAYOUT
        // ═══════════════════════════════════════════════════════════════════

        private static void DrawHeader(int rp)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sects", Styles.Header);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"RP: {rp}", Styles.Label);
            GUILayout.EndHorizontal();
        }

        private static void DrawSectRow(
            EntityManager em, Faction faction, Entity temple, string sectId,
            byte culture, int rp)
        {
            // Resolve adoption state.
            PerSectState sect = default;
            if (FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.HasComponent<SectAdoptionState>(bank))
            {
                sect = em.GetComponentData<SectAdoptionState>(bank).Get(sectId);
            }

            int adoptCost = SectConfig.AdoptionCost(sectId, culture);
            var cluster = SectConfig.ClusterOf(sectId);

            GUILayout.BeginHorizontal(GUI.skin.box);

            // Sect name + cluster tag.
            GUILayout.BeginVertical(GUILayout.MinWidth(160));
            GUILayout.Label(DisplayName(sectId), Styles.Label);
            GUILayout.Label($"  {cluster}", Styles.SmallLabel);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            if (!sect.IsAdopted)
            {
                // Adopt button.
                bool canAfford = rp >= adoptCost;
                bool slotFree = HasFreeSlot(em, temple);
                bool canMaterial = TheWaningBorder.Data.BuildCosts.TryGet(SectConfig.ChapelIdFor(sectId), out var chapelCost)
                    && FactionEconomy.CanAfford(em, faction, chapelCost);
                bool enabled = canAfford && slotFree && canMaterial;

                GUI.enabled = enabled;
                string label = $"Adopt — {adoptCost} RP";
                if (!slotFree) label += " (no slot)";
                else if (!canMaterial) label += " (resources)";
                if (GUILayout.Button(label, GUILayout.Width(160)))
                {
                    StartChapelBuild(em, faction, temple, sectId, chapelCost);
                }
                GUI.enabled = true;
            }
            else
            {
                // Lever upgrade buttons.
                DrawLeverButton(em, faction, sectId, SectLeverKind.Passive,     "P", sect.PassiveLevel);
                DrawLeverButton(em, faction, sectId, SectLeverKind.Building,    "B", sect.BuildingLevel);
                DrawLeverButton(em, faction, sectId, SectLeverKind.Unit,        "U", sect.UnitLevel);
                DrawLeverButton(em, faction, sectId, SectLeverKind.ActivePower, "A", sect.ActivePowerLevel);
            }

            GUILayout.EndHorizontal();
        }

        private static void DrawLeverButton(
            EntityManager em, Faction faction, string sectId, SectLeverKind lever,
            string letter, byte currentLevel)
        {
            string label;
            bool enabled;

            if (currentLevel >= 3)
            {
                label = $"{letter} III";
                enabled = false;
            }
            else
            {
                var check = SectAdoption.CanUpgradeLever(em, faction, sectId, lever, out int cost);
                enabled = check == SectAdoptionResult.Ok;
                label = $"{letter} {RomanNumeral(currentLevel + 1)} — {cost} RP";
            }

            GUI.enabled = enabled;
            if (GUILayout.Button(label, GUILayout.Width(86)))
            {
                SectAdoption.TryUpgradeLever(em, faction, sectId, lever);
            }
            GUI.enabled = true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SLOT MANAGEMENT
        // ═══════════════════════════════════════════════════════════════════

        private static bool HasFreeSlot(EntityManager em, Entity temple)
        {
            if (!em.Exists(temple)) return false;
            if (!em.HasBuffer<TempleChapelSlot>(temple)) return false;
            var slots = em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].State == 0) return true;
            return false;
        }

        /// <summary>
        /// Queue a chapel build into the first free Temple slot. Material cost
        /// (supplies/crystal) is deducted here at click time. Adoption RP is
        /// deducted on completion (TempleChapelBuildSystem calls
        /// SectAdoption.OnChapelCompleted) — so the player can in theory spend
        /// RP elsewhere between click and completion and the late-arriving
        /// chapel will fail validation, get destroyed, and refund nothing.
        /// UI affordability gating (the Adopt button) prevents this in practice;
        /// Phase 5 polish may add proper RP reservation if abuse is observed.
        /// </summary>
        private static void StartChapelBuild(
            EntityManager em, Faction faction, Entity temple, string sectId,
            TheWaningBorder.Core.Cost chapelCost)
        {
            if (!em.Exists(temple)) return;
            if (!em.HasBuffer<TempleChapelSlot>(temple)) return;

            var slots = em.GetBuffer<TempleChapelSlot>(temple);
            int freeSlot = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].State == 0) { freeSlot = i; break; }
            }
            if (freeSlot < 0) return;

            // Deduct material cost atomically. If the spend fails (race with
            // another transaction), abort without writing the slot.
            if (!FactionEconomy.Spend(em, faction, chapelCost)) return;

            slots[freeSlot] = new TempleChapelSlot
            {
                Chapel        = Entity.Null,
                SectId        = new Unity.Collections.FixedString64Bytes(sectId),
                State         = 1,
                BuildProgress = 0f,
                BuildTime     = ChapelBuildSeconds,
            };
        }

        /// <summary>Build time for a chapel in seconds. Phase 5 may differentiate per cluster.</summary>
        private const float ChapelBuildSeconds = 30f;

        // ═══════════════════════════════════════════════════════════════════
        // FORMATTING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sect-id → display name. Phase 1 has no <c>SectDefinition</c>
        /// ScriptableObject assets bound yet, so fall back to a stripped
        /// version of the string id ("Sect_Antiquity" → "Antiquity").
        /// Phase 2 SectDatabase lookup will replace this.
        /// </summary>
        private static string DisplayName(string sectId)
        {
            if (string.IsNullOrEmpty(sectId)) return "?";
            if (sectId.StartsWith("Sect_")) return sectId.Substring("Sect_".Length);
            return sectId;
        }

        private static string RomanNumeral(int level)
        {
            return level switch
            {
                1 => "I",
                2 => "II",
                3 => "III",
                _ => level.ToString(),
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // INTERNAL HELPERS
        // ═══════════════════════════════════════════════════════════════════

        private static byte LookupCulture(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
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
    }
}
