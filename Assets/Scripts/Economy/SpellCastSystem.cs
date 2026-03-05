// SpellCastSystem.cs
// Handles spell casting, targeting, and effect application
// Location: Assets/Scripts/Economy/SpellCastSystem.cs

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// MonoBehaviour singleton that executes spell effects on the game world.
    ///
    /// Workflow:
    /// 1. SpellPanel sets SpellCastSystem into targeting mode (BeginTargeting)
    /// 2. Player clicks a world position
    /// 3. CastSpell is called with the target position
    /// 4. System finds entities in area and applies the spell effect
    /// 5. Cooldown starts via SpellState
    ///
    /// Pattern: same as SectEffectSystem (MonoBehaviour singleton with ECS queries).
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
            Debug.Log($"[SpellCastSystem] Targeting mode: {spell.Name} for {faction}");
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
        /// Applies effects to entities in area, starts cooldown, exits targeting mode.
        /// </summary>
        /// <returns>True if cast was successful.</returns>
        public bool CastAtPosition(float3 targetPosition)
        {
            if (!IsTargeting || ActiveSpell == null) return false;

            var spell = ActiveSpell;
            var faction = CastingFaction;

            // Check cooldown
            var spellState = SpellState.Instance;
            if (spellState != null && spellState.IsOnCooldown(faction, spell.Id))
            {
                Debug.Log($"[SpellCastSystem] {spell.Name} is on cooldown");
                CancelTargeting();
                return false;
            }

            // Execute spell effect
            bool success = ApplySpellEffect(spell, faction, targetPosition);

            if (success)
            {
                // Start cooldown
                spellState?.StartCooldown(faction, spell.Id, spell.Cooldown);
                Debug.Log($"[SpellCastSystem] {faction} cast {spell.Name} at {targetPosition}");
            }

            // Exit targeting mode
            IsTargeting = false;
            ActiveSpell = null;

            return success;
        }

        /// <summary>
        /// Direct cast without targeting mode (for AI use).
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
        // EFFECT APPLICATION
        // ═══════════════════════════════════════════════════════════════════

        private bool ApplySpellEffect(SpellDefinition spell, Faction faction, float3 targetPosition)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            var em = world.EntityManager;

            switch (spell.EffectType)
            {
                case "Heal":
                    return ApplyHeal(em, faction, targetPosition, spell);
                case "Buff":
                    return ApplyBuff(em, faction, targetPosition, spell);
                case "Debuff":
                    return ApplyDebuff(em, faction, targetPosition, spell);
                case "Damage":
                    return ApplyDamage(em, faction, targetPosition, spell);
                case "Invulnerable":
                    return ApplyInvulnerable(em, faction, targetPosition, spell);
                case "Vision":
                    // Vision spells are flag-based, handled elsewhere
                    Debug.Log($"[SpellCastSystem] Vision spell '{spell.Name}' activated for {spell.Duration}s");
                    return true;
                case "Disable":
                    // Disable spells are flag-based, handled elsewhere
                    Debug.Log($"[SpellCastSystem] Disable spell '{spell.Name}' activated for {spell.Duration}s");
                    return true;
                default:
                    Debug.LogWarning($"[SpellCastSystem] Unknown effect type: {spell.EffectType}");
                    return false;
            }
        }

        /// <summary>
        /// Heal friendly buildings in area.
        /// </summary>
        private static bool ApplyHeal(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            int healed = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                float distSq = math.distancesq(transforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var hp = healths[i];
                if (hp.Value >= hp.Max) continue;

                hp.Value = math.min(hp.Value + (int)spell.EffectValue, hp.Max);
                em.SetComponentData(entities[i], hp);
                healed++;
            }

            Debug.Log($"[SpellCastSystem] Healed {healed} buildings by {spell.EffectValue} HP");
            return true;
        }

        /// <summary>
        /// Apply buff to friendly entities in area.
        /// </summary>
        private static bool ApplyBuff(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            int buffed = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                float distSq = math.distancesq(transforms[i].Position, center);
                if (distSq > radiusSq) continue;

                // Determine buff values based on spell
                var buff = new SpellBuff
                {
                    ArmorBonus = spell.EffectValue > 0f && spell.SecondaryValue == 0f ? spell.EffectValue : 0f,
                    DamageMultiplier = spell.Id == "Spell_BattleFervor" ? spell.EffectValue : 1.0f,
                    SpeedMultiplier = spell.Id == "Spell_BattleFervor" ? spell.SecondaryValue : 1.0f,
                    DamageReflect = spell.Id == "Spell_ReflectiveWard" ? spell.SecondaryValue : 0f,
                    TimeRemaining = spell.Duration
                };

                // Add or replace buff
                if (em.HasComponent<SpellBuff>(entities[i]))
                    em.SetComponentData(entities[i], buff);
                else
                    em.AddComponentData(entities[i], buff);

                buffed++;
            }

            Debug.Log($"[SpellCastSystem] Buffed {buffed} entities with {spell.Name}");
            return true;
        }

        /// <summary>
        /// Apply debuff to enemy entities in area.
        /// </summary>
        private static bool ApplyDebuff(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            int debuffed = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                // Target enemies only
                if (factions[i].Value == faction) continue;

                float distSq = math.distancesq(transforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = spell.Id == "Spell_ProfaneRally" ? spell.EffectValue : 0f,
                    SuppliesDrainPerSecond = spell.Id == "Spell_EdictOfSeizure" ?
                        spell.EffectValue / spell.Duration : 0f,
                    TimeRemaining = spell.Duration
                };

                if (em.HasComponent<SpellDebuff>(entities[i]))
                    em.SetComponentData(entities[i], debuff);
                else
                    em.AddComponentData(entities[i], debuff);

                debuffed++;
            }

            Debug.Log($"[SpellCastSystem] Debuffed {debuffed} enemy entities with {spell.Name}");
            return true;
        }

        /// <summary>
        /// Deal damage to target entity (Unravel - single target true damage).
        /// </summary>
        private static bool ApplyDamage(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            // Find nearest Crystal entity at target position
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

            float closestDist = float.MaxValue;
            int closestIdx = -1;

            for (int i = 0; i < entities.Length; i++)
            {
                float dist = math.distance(transforms[i].Position, center);
                if (dist < closestDist && dist < 5f) // Must click within 5 units
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            if (closestIdx < 0)
            {
                Debug.Log("[SpellCastSystem] No valid target found for damage spell");
                return false;
            }

            var hp = healths[closestIdx];
            hp.Value -= (int)spell.EffectValue;
            if (hp.Value < 0) hp.Value = 0;
            em.SetComponentData(entities[closestIdx], hp);

            Debug.Log($"[SpellCastSystem] Dealt {spell.EffectValue} true damage to entity");
            return true;
        }

        /// <summary>
        /// Make friendly buildings invulnerable in area.
        /// </summary>
        private static bool ApplyInvulnerable(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            int count = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                float distSq = math.distancesq(transforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var invuln = new Invulnerable { TimeRemaining = spell.Duration };

                if (em.HasComponent<Invulnerable>(entities[i]))
                    em.SetComponentData(entities[i], invuln);
                else
                    em.AddComponentData(entities[i], invuln);

                count++;
            }

            Debug.Log($"[SpellCastSystem] Made {count} buildings invulnerable for {spell.Duration}s");
            return true;
        }
    }
}
