// SpellCastSystem.cs
// Handles spell casting, targeting, and effect application
// Location: Assets/Scripts/Economy/SpellCastSystem.cs

using System.Collections;
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
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
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
                    return ApplyVision(em, faction, targetPosition, spell);
                case "Disable":
                    return ApplyDisable(em, faction, targetPosition, spell);
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

        // ═══════════════════════════════════════════════════════════════════
        // VISION SPELLS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply vision spell effects.
        /// CrystalSurvey: reveals all Crystal entities on the minimap for the duration.
        /// Shroud: applies speed debuff to enemies in the target area.
        /// </summary>
        private bool ApplyVision(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            if (spell.Id == "Spell_CrystalSurvey")
                return ApplyCrystalSurvey(em, faction, spell);

            if (spell.Id == "Spell_Shroud")
                return ApplyShroud(em, faction, center, spell);

            Debug.LogWarning($"[SpellCastSystem] Unknown Vision spell: {spell.Id}");
            return false;
        }

        /// <summary>
        /// Reveal all Crystal entities on the minimap for the casting faction.
        /// Stamps fog of war at each crystal position every second for Duration seconds.
        /// </summary>
        private bool ApplyCrystalSurvey(EntityManager em, Faction faction, SpellDefinition spell)
        {
            StartCoroutine(CrystalSurveyCoroutine(faction, spell.Duration));
            Debug.Log($"[SpellCastSystem] Crystal Survey: revealing crystal nodes for {faction} for {spell.Duration}s");
            return true;
        }

        private IEnumerator CrystalSurveyCoroutine(Faction faction, float duration)
        {
            var fogMgr = FogOfWarManager.Instance;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (fogMgr != null)
                {
                    var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (world != null && world.IsCreated)
                    {
                        var em = world.EntityManager;
                        var query = em.CreateEntityQuery(
                            ComponentType.ReadOnly<CrystalTag>(),
                            ComponentType.ReadOnly<LocalTransform>()
                        );

                        using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                        for (int i = 0; i < transforms.Length; i++)
                        {
                            fogMgr.Stamp(faction, transforms[i].Position, 10f);
                        }
                    }
                }

                yield return new WaitForSeconds(1f);
                elapsed += 1f;
            }
        }

        /// <summary>
        /// Apply Shroud: speed debuff to enemy units in the target area.
        /// The fog system doesn't support area denial, so the primary gameplay
        /// effect is a 20% speed reduction to enemies caught in the shroud.
        /// </summary>
        private static bool ApplyShroud(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            int affected = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                // Target enemies only
                if (factions[i].Value == faction) continue;

                float distSq = math.distancesq(transforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = 0.20f, // -20% speed
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = spell.Duration
                };

                if (em.HasComponent<SpellDebuff>(entities[i]))
                    em.SetComponentData(entities[i], debuff);
                else
                    em.AddComponentData(entities[i], debuff);

                affected++;
            }

            Debug.Log($"[SpellCastSystem] Shroud: slowed {affected} enemy entities for {spell.Duration}s");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // DISABLE SPELLS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply disable spell effects.
        /// Embargo: disables enemy trading posts and stops caravans in area.
        /// BindTheCore: pacifies a Crystal sub-node.
        /// </summary>
        private static bool ApplyDisable(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            if (spell.Id == "Spell_Embargo")
                return ApplyEmbargo(em, faction, center, spell);

            if (spell.Id == "Spell_BindTheCore")
                return ApplyBindTheCore(em, faction, center, spell);

            Debug.LogWarning($"[SpellCastSystem] Unknown Disable spell: {spell.Id}");
            return false;
        }

        /// <summary>
        /// Disable enemy trading posts and stop caravans in the target area.
        /// Trading posts receive a SpellDebuff so TradingPostSystem skips trader spawning.
        /// Caravans receive a SpellDebuff with 100% speed reduction (stopped).
        /// </summary>
        private static bool ApplyEmbargo(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            int affected = 0;

            // --- Debuff enemy trading posts in area ---
            var postQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<TradingPostTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var postEntities = postQuery.ToEntityArray(Allocator.Temp);
            using var postFactions = postQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var postTransforms = postQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < postEntities.Length; i++)
            {
                if (postFactions[i].Value == faction) continue; // enemy only

                float distSq = math.distancesq(postTransforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = 0f,
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = spell.Duration
                };

                if (em.HasComponent<SpellDebuff>(postEntities[i]))
                    em.SetComponentData(postEntities[i], debuff);
                else
                    em.AddComponentData(postEntities[i], debuff);

                affected++;
            }

            // --- Stop enemy caravans in area (100% speed reduction) ---
            var caravanQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CaravanTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var caravanEntities = caravanQuery.ToEntityArray(Allocator.Temp);
            using var caravanFactions = caravanQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var caravanTransforms = caravanQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < caravanEntities.Length; i++)
            {
                if (caravanFactions[i].Value == faction) continue;

                float distSq = math.distancesq(caravanTransforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = 1.0f, // 100% slow = stopped
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = spell.Duration
                };

                if (em.HasComponent<SpellDebuff>(caravanEntities[i]))
                    em.SetComponentData(caravanEntities[i], debuff);
                else
                    em.AddComponentData(caravanEntities[i], debuff);

                affected++;
            }

            Debug.Log($"[SpellCastSystem] Embargo: disabled {affected} trade entities for {spell.Duration}s");
            return true;
        }

        /// <summary>
        /// Pacify a Crystal sub-node near the target position.
        /// Finds the nearest CrystalSubNodeTag entity within 5 units and applies SpellDebuff.
        /// Falls back to CrystalTag + BuildingTag if no sub-node is found.
        /// </summary>
        private static bool ApplyBindTheCore(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            // --- Try CrystalSubNodeTag first ---
            var subNodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalSubNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var subNodeEntities = subNodeQuery.ToEntityArray(Allocator.Temp);
            using var subNodeTransforms = subNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float closestDist = float.MaxValue;
            int closestIdx = -1;

            for (int i = 0; i < subNodeEntities.Length; i++)
            {
                float dist = math.distance(subNodeTransforms[i].Position, center);
                if (dist < closestDist && dist < 5f)
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            if (closestIdx >= 0)
            {
                var debuff = new SpellDebuff
                {
                    SpeedReduction = 0f,
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = spell.Duration
                };

                if (em.HasComponent<SpellDebuff>(subNodeEntities[closestIdx]))
                    em.SetComponentData(subNodeEntities[closestIdx], debuff);
                else
                    em.AddComponentData(subNodeEntities[closestIdx], debuff);

                Debug.Log($"[SpellCastSystem] Bind the Core: pacified crystal sub-node for {spell.Duration}s");
                return true;
            }

            // --- Fallback: CrystalTag + BuildingTag ---
            var crystalQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalTag>(),
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var crystalEntities = crystalQuery.ToEntityArray(Allocator.Temp);
            using var crystalTransforms = crystalQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            closestDist = float.MaxValue;
            closestIdx = -1;

            for (int i = 0; i < crystalEntities.Length; i++)
            {
                float dist = math.distance(crystalTransforms[i].Position, center);
                if (dist < closestDist && dist < 5f)
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            if (closestIdx >= 0)
            {
                var debuff = new SpellDebuff
                {
                    SpeedReduction = 0f,
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = spell.Duration
                };

                if (em.HasComponent<SpellDebuff>(crystalEntities[closestIdx]))
                    em.SetComponentData(crystalEntities[closestIdx], debuff);
                else
                    em.AddComponentData(crystalEntities[closestIdx], debuff);

                Debug.Log($"[SpellCastSystem] Bind the Core: pacified crystal building for {spell.Duration}s");
                return true;
            }

            Debug.Log("[SpellCastSystem] Bind the Core: no valid crystal target found within range");
            return false;
        }
    }
}
