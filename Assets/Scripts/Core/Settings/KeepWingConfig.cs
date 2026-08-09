// KeepWingConfig.cs
// Static tables for the Fiendstone Keep wing system: costs, build times,
// display strings, and the wing-derived trainable roster. Costs are
// PLAYTEST PLACEHOLDERS (design 2026-07-04 gave behaviours, not numbers).

using System.Collections.Generic;
using Unity.Entities;

namespace TheWaningBorder.Core.Settings
{
    public static class KeepWingConfig
    {
        public const int MaxWings = 3;
        public const float BuildDuration = 30f;

        /// <summary>Engineers wing HP bonus (applied once on completion).</summary>
        public const float EngineersHpMultiplier = 1.25f;
        /// <summary>Extra ballista bolts per auto-fire volley from the Engineers wing.</summary>
        public const int EngineersBallistaBolts = 3;
        /// <summary>Librarians' wing: global research-time divisor (+20% speed).</summary>
        public const float LibrariansResearchSpeed = 1.2f;
        /// <summary>Civic wing Supplies trickle (per second; 30/min).</summary>
        public const float CivicSuppliesPerSecond = 0.5f;
        /// <summary>Economic wing Supplies trickle (per second; 60/min — hut-like, larger area).</summary>
        public const float EconomicSuppliesPerSecond = 1.0f;

        public static readonly KeepWingType[] AllWings =
        {
            KeepWingType.War, KeepWingType.Civic, KeepWingType.Engineers,
            KeepWingType.Economic, KeepWingType.Librarians, KeepWingType.Temple,
        };

        public static string NameOf(KeepWingType t) => t switch
        {
            KeepWingType.War        => "War Wing",
            KeepWingType.Civic      => "Civic Wing",
            KeepWingType.Engineers  => "Engineers' Wing",
            KeepWingType.Economic   => "Economic Wing",
            KeepWingType.Librarians => "Librarians' Wing",
            KeepWingType.Temple     => "Temple Wing",
            _ => "Wing",
        };

        public static string DescriptionOf(KeepWingType t) => t switch
        {
            KeepWingType.War        => "Train Barracks, Archery Range and Stable units at the Keep.",
            KeepWingType.Civic      => "The Keep generates Supplies and trains Workers.",
            KeepWingType.Engineers  => "Three ballista emplacements (extra bolts each volley) and +25% Keep HP.",
            KeepWingType.Economic   => "Gathers like a Gatherer's Hut with a larger area (Supplies income).",
            KeepWingType.Librarians => "Hall economy techs researchable at the Keep; all research 20% faster.",
            KeepWingType.Temple     => "Trains sect units (Litharchs for now); grants +1 Religion Point when built.",
            _ => "",
        };

        public static Cost CostOf(KeepWingType t) => t switch
        {
            KeepWingType.War        => new Cost { Supplies = 300, Iron = 100 },
            KeepWingType.Civic      => new Cost { Supplies = 250, Iron = 50 },
            KeepWingType.Engineers  => new Cost { Supplies = 350, Iron = 150 },
            KeepWingType.Economic   => new Cost { Supplies = 300, Iron = 80 },
            KeepWingType.Librarians => new Cost { Supplies = 280, Iron = 60, Veilstone = 20 },
            KeepWingType.Temple     => new Cost { Supplies = 250, Veilstone = 80 },
            _ => default,
        };

        /// <summary>
        /// The Keep's trainable roster derived from its wings. War = melee /
        /// ranged / cavalry trainer rosters; Civic adds Workers; Temple adds
        /// Litharchs (sect units follow with the task-063 phase 2 Unit lever).
        /// </summary>
        public static string[] BuildTrainList(KeepWings wings)
        {
            var list = new List<string>();
            if (wings.Has(KeepWingType.War))
            {
                AddTrains(list, "Barracks");
                AddTrains(list, "ArcheryRange");
                AddTrains(list, "Alanthor_RoyalStable");
            }
            if (wings.Has(KeepWingType.Civic))
            {
                if (!list.Contains("Worker")) list.Add("Worker");
            }
            if (wings.Has(KeepWingType.Temple))
            {
                if (!list.Contains("Litharch")) list.Add("Litharch");
            }
            return list.ToArray();
        }

        private static void AddTrains(List<string> list, string buildingId)
        {
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def.trains == null) return;
            for (int i = 0; i < def.trains.Length; i++)
                if (!list.Contains(def.trains[i])) list.Add(def.trains[i]);
        }
    }
}
