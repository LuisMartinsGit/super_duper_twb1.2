// FactionResearchState.cs
// Managed singleton tracking completed research per faction
// Part of: Economy/

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Tracks which technologies each faction has researched.
    /// MonoBehaviour singleton - lives on the RuntimeManagers GameObject.
    ///
    /// Used by:
    /// - ResearchSystem: to mark techs as complete
    /// - UI: to grey out already-researched techs and check prerequisites
    /// - Future systems: to apply research effects (stat bonuses, unlocks)
    /// </summary>
    public class FactionResearchState : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════════

        public static FactionResearchState Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DATA
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Completed technologies per faction. Key = (int)Faction, Value = set of tech IDs.
        /// </summary>
        private readonly Dictionary<int, HashSet<string>> _completedByFaction = new();

        /// <summary>
        /// Fired when a technology is completed. Parameters: (faction, techId).
        /// Subscribed to by TechEffectSystem to apply stat modifiers.
        /// </summary>
        public event System.Action<Faction, string> OnTechCompleted;

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if a faction has completed a specific technology.
        /// </summary>
        public bool HasResearched(Faction faction, string techId)
        {
            int key = (int)faction;
            return _completedByFaction.TryGetValue(key, out var set) && set.Contains(techId);
        }

        /// <summary>
        /// Mark a technology as completed for a faction.
        /// </summary>
        public void CompleteResearch(Faction faction, string techId)
        {
            int key = (int)faction;
            if (!_completedByFaction.TryGetValue(key, out var set))
            {
                set = new HashSet<string>();
                _completedByFaction[key] = set;
            }

            if (set.Add(techId))
            {
                Debug.Log($"[FactionResearchState] {faction} completed research: {techId}");
                OnTechCompleted?.Invoke(faction, techId);
            }
        }

        /// <summary>
        /// Check if a faction meets all prerequisites for a technology.
        /// Returns true if all prerequisite techs are researched, or if there are no prerequisites.
        /// </summary>
        public bool MeetsPrerequisites(Faction faction, string[] prerequisites)
        {
            if (prerequisites == null || prerequisites.Length == 0)
                return true;

            foreach (var prereq in prerequisites)
            {
                if (!HasResearched(faction, prereq))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Get all completed technology IDs for a faction.
        /// </summary>
        public IReadOnlyCollection<string> GetCompletedTechs(Faction faction)
        {
            int key = (int)faction;
            if (_completedByFaction.TryGetValue(key, out var set))
                return set;
            return System.Array.Empty<string>();
        }

        /// <summary>
        /// Get the count of completed technologies for a faction.
        /// </summary>
        public int GetCompletedCount(Faction faction)
        {
            int key = (int)faction;
            return _completedByFaction.TryGetValue(key, out var set) ? set.Count : 0;
        }

        /// <summary>
        /// Reset all research state (for new game).
        /// </summary>
        public void ResetAll()
        {
            _completedByFaction.Clear();
            Debug.Log("[FactionResearchState] Reset all research state");
        }
    }
}
