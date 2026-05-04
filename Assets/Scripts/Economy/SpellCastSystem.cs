// SpellCastSystem.cs
// Targeting + cast-pipeline shell for active spells.
// Location: Assets/Scripts/Economy/SpellCastSystem.cs
//
// task-063 phase 1: the 12 old sect-spell effect implementations were deleted
// because every spell was bound to a removed sect ID (Renewal / Antiquity /
// LivingStone / VeiledMemory / StillFlame / QuietVault / MirrorRite /
// ShardJudgment / EmberAsh / HollowBrand / FlamewroughtChains / UnmakersGrasp).
//
// What's preserved:
//  - Targeting state (IsTargeting / ActiveSpell / CastingFaction).
//  - BeginTargeting / CancelTargeting / CastAtPosition / CastSpell entry points.
//  - SpellState cooldown wiring.
//
// What's stubbed:
//  - ApplySpellEffect always returns false. Phase 2 will reintroduce the
//    per-sect Active Power lever via a dispatcher keyed on SectAdoptionState.

using UnityEngine;
using Unity.Mathematics;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// MonoBehaviour singleton that drives spell-targeting input and dispatches
    /// effects. Phase 2 will repopulate the dispatcher with per-sect actives.
    /// </summary>
    public class SpellCastSystem : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════

        public static SpellCastSystem Instance { get; private set; }

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

        // ═══════════════════════════════════════════════════════════════════
        // TARGETING STATE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>True when waiting for player to click a target location.</summary>
        public bool IsTargeting { get; private set; }

        /// <summary>The spell being targeted (null if not targeting).</summary>
        public SpellDefinition ActiveSpell { get; private set; }

        /// <summary>The faction casting the spell.</summary>
        public Faction CastingFaction { get; private set; }

        /// <summary>
        /// Enter spell targeting mode. Player must click a world position to cast.
        /// </summary>
        public void BeginTargeting(Faction faction, SpellDefinition spell)
        {
            IsTargeting = true;
            ActiveSpell = spell;
            CastingFaction = faction;
        }

        /// <summary>
        /// Cancel targeting mode without casting.
        /// </summary>
        public void CancelTargeting()
        {
            IsTargeting = false;
            ActiveSpell = null;
        }

        // ═══════════════════════════════════════════════════════════════════
        // CAST SPELL
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cast the active spell at the given world position.
        /// Phase 1 stub: returns false (no spells registered in SpellDatabase).
        /// </summary>
        public bool CastAtPosition(float3 targetPosition)
        {
            if (!IsTargeting || ActiveSpell == null) return false;

            var spell = ActiveSpell;
            var faction = CastingFaction;

            // Check cooldown
            var spellState = SpellState.Instance;
            if (spellState != null && spellState.IsOnCooldown(faction, spell.Id))
            {
                CancelTargeting();
                return false;
            }

            bool success = ApplySpellEffect(spell, faction, targetPosition);

            if (success)
                spellState?.StartCooldown(faction, spell.Id, spell.Cooldown);

            IsTargeting = false;
            ActiveSpell = null;
            return success;
        }

        /// <summary>
        /// Direct cast without targeting mode (for AI use).
        /// Phase 1 stub: returns false.
        /// </summary>
        public bool CastSpell(Faction faction, string spellId, float3 targetPosition)
        {
            var spell = SpellDatabase.GetSpell(spellId);
            if (spell == null) return false;

            var spellState = SpellState.Instance;
            if (spellState != null && spellState.IsOnCooldown(faction, spellId))
                return false;

            bool success = ApplySpellEffect(spell, faction, targetPosition);
            if (success)
                spellState?.StartCooldown(faction, spellId, spell.Cooldown);
            return success;
        }

        // ═══════════════════════════════════════════════════════════════════
        // EFFECT DISPATCHER (Phase 1 stub)
        // ═══════════════════════════════════════════════════════════════════

        // task-063 phase 1: the 12 ApplyXxx methods (RestorationWave,
        // ArcaneBombardment, Earthquake, VeilOfShadows, SummonCaravanGuard,
        // GoldenTribute, ArcaneStorm, Dominate, Firestorm, SummonWarHost,
        // ChainLightning, Annihilation) were deleted because every spell ID
        // they handled was bound to a removed sect. Phase 2 will reintroduce
        // a dispatcher keyed on the new sect roster + each sect's
        // Active Power lever level (SectAdoptionState.PerSectState.ActivePowerLevel).

        // Suppress unused-parameter warnings on the stub.
        private static bool ApplySpellEffect(SpellDefinition spell, Faction faction, float3 targetPosition)
        {
            _ = spell; _ = faction; _ = targetPosition;
            return false;
        }
    }
}
