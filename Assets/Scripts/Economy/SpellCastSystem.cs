// SpellCastSystem.cs
// Handles spell casting, targeting, and effect application for 12 BFME2-style strategic spells
// Location: Assets/Scripts/Economy/SpellCastSystem.cs

using System.Collections;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;

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
        // EFFECT DISPATCHER
        // ═══════════════════════════════════════════════════════════════════

        private bool ApplySpellEffect(SpellDefinition spell, Faction faction, float3 targetPosition)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            var em = world.EntityManager;

            switch (spell.Id)
            {
                case "Spell_RestorationWave":
                    return ApplyRestorationWave(em, faction, spell);
                case "Spell_ArcaneBombardment":
                    return ApplyArcaneBombardment(em, faction, targetPosition, spell);
                case "Spell_Earthquake":
                    return ApplyEarthquake(em, faction, targetPosition, spell);
                case "Spell_VeilOfShadows":
                    return ApplyVeilOfShadows(em, faction, spell);
                case "Spell_SummonCaravanGuard":
                    return ApplySummonCaravanGuard(em, faction, targetPosition, spell);
                case "Spell_GoldenTribute":
                    return ApplyGoldenTribute(em, faction, spell);
                case "Spell_ArcaneStorm":
                    return ApplyArcaneStorm(em, faction, targetPosition, spell);
                case "Spell_Dominate":
                    return ApplyDominate(em, faction, targetPosition, spell);
                case "Spell_Firestorm":
                    return ApplyFirestorm(em, faction, targetPosition, spell);
                case "Spell_SummonWarHost":
                    return ApplySummonWarHost(em, faction, targetPosition, spell);
                case "Spell_ChainLightning":
                    return ApplyChainLightning(em, faction, targetPosition, spell);
                case "Spell_Annihilation":
                    return ApplyAnnihilation(em, faction, targetPosition, spell);
                default:
                    Debug.LogWarning($"[SpellCastSystem] Unknown spell ID: {spell.Id}");
                    return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_RESTORATIONWAVE - Global Mass Heal
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Heal ALL friendly units by 100 HP and ALL friendly buildings by 200 HP globally.
        /// </summary>
        private static bool ApplyRestorationWave(EntityManager em, Faction faction, SpellDefinition spell)
        {
            int healed = 0;

            // Heal friendly units
            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadWrite<Health>()
            );

            using var unitEntities = unitQuery.ToEntityArray(Allocator.Temp);
            using var unitFactions = unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitHealths = unitQuery.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < unitEntities.Length; i++)
            {
                if (unitFactions[i].Value != faction) continue;
                var hp = unitHealths[i];
                if (hp.Value >= hp.Max) continue;

                hp.Value = math.min(hp.Value + 100, hp.Max);
                em.SetComponentData(unitEntities[i], hp);
                healed++;
            }

            // Heal friendly buildings
            var buildingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadWrite<Health>()
            );

            using var buildingEntities = buildingQuery.ToEntityArray(Allocator.Temp);
            using var buildingFactions = buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var buildingHealths = buildingQuery.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < buildingEntities.Length; i++)
            {
                if (buildingFactions[i].Value != faction) continue;
                var hp = buildingHealths[i];
                if (hp.Value >= hp.Max) continue;

                hp.Value = math.min(hp.Value + 200, hp.Max);
                em.SetComponentData(buildingEntities[i], hp);
                healed++;
            }

            Debug.Log($"[SpellCastSystem] Restoration Wave: healed {healed} entities globally");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_ARCANEBOMBARDMENT - Artillery Strike (Coroutine)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Over 8 seconds, spawn 8 projectile entities at random positions within 20u of target.
        /// Each deals 50 magic damage in a 3u AOE.
        /// </summary>
        private bool ApplyArcaneBombardment(EntityManager em, Faction faction, float3 target, SpellDefinition spell)
        {
            StartCoroutine(ArcaneBombardmentCoroutine(faction, target, spell));
            Debug.Log($"[SpellCastSystem] Arcane Bombardment: 8 bolts over 8s at {target}");
            return true;
        }

        private IEnumerator ArcaneBombardmentCoroutine(Faction faction, float3 target, SpellDefinition spell)
        {
            int boltCount = (int)spell.SecondaryValue; // 8
            float interval = spell.Duration / boltCount; // 1s per bolt
            var rng = new Unity.Mathematics.Random((uint)(Time.time * 1000 + 1));

            for (int b = 0; b < boltCount; b++)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) yield break;

                var em = world.EntityManager;

                // Random offset within radius
                float2 offset = rng.NextFloat2Direction() * rng.NextFloat(0f, spell.AreaRadius);
                float3 impactPos = target + new float3(offset.x, 0f, offset.y);
                float3 spawnPos = impactPos + new float3(0f, 30f, 0f); // Spawn high above

                // Create projectile entity
                var projectile = em.CreateEntity();
                em.AddComponentData(projectile, new LocalTransform
                {
                    Position = spawnPos,
                    Rotation = quaternion.identity,
                    Scale = 1f
                });
                em.AddComponentData(projectile, new Projectile
                {
                    Start = spawnPos,
                    End = impactPos,
                    StartTime = world.Time.ElapsedTime,
                    FlightTime = 0.5f,
                    Damage = (int)spell.EffectValue, // 50
                    Target = Entity.Null,
                    Faction = faction,
                    DmgType = DamageType.Magic
                });
                em.AddComponentData(projectile, new AOEProjectile { Radius = 3f });
                em.AddComponent<LaserProjectileTag>(projectile);

                yield return new WaitForSeconds(interval);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_EARTHQUAKE - Building Destroyer + Stun
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Deal 300 siege damage to enemy buildings (600 to walls) within 25u,
        /// and stun enemy ground units for 2 seconds.
        /// </summary>
        private static bool ApplyEarthquake(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            int damaged = 0;

            // Damage enemy buildings
            var buildingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>()
            );

            using var bEntities = buildingQuery.ToEntityArray(Allocator.Temp);
            using var bFactions = buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bTransforms = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var bHealths = buildingQuery.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < bEntities.Length; i++)
            {
                if (bFactions[i].Value == faction) continue; // enemy only

                float distSq = math.distancesq(bTransforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var hp = bHealths[i];
                // Walls take double damage (600)
                bool isWall = em.HasComponent<WallTag>(bEntities[i]);
                int damage = isWall ? 600 : (int)spell.EffectValue; // 300 or 600

                hp.Value = math.max(0, hp.Value - damage);
                em.SetComponentData(bEntities[i], hp);
                damaged++;
            }

            // Stun enemy ground units (speed reduction = 1.0 = 100% = frozen)
            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var uEntities = unitQuery.ToEntityArray(Allocator.Temp);
            using var uFactions = unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var uTransforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int stunned = 0;
            for (int i = 0; i < uEntities.Length; i++)
            {
                if (uFactions[i].Value == faction) continue;

                float distSq = math.distancesq(uTransforms[i].Position, center);
                if (distSq > radiusSq) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = 1.0f, // 100% = stun
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = spell.SecondaryValue // 2s
                };

                if (em.HasComponent<SpellDebuff>(uEntities[i]))
                    em.SetComponentData(uEntities[i], debuff);
                else
                    em.AddComponentData(uEntities[i], debuff);

                stunned++;
            }

            Debug.Log($"[SpellCastSystem] Earthquake: damaged {damaged} buildings, stunned {stunned} units");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_VEILOFSHADOWS - Global Mass Stealth
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Add StealthTag to ALL friendly units on the map for 15 seconds.
        /// </summary>
        private static bool ApplyVeilOfShadows(EntityManager em, Faction faction, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int stealthed = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                var stealth = new StealthTag { TimeRemaining = spell.Duration }; // 15s

                if (em.HasComponent<StealthTag>(entities[i]))
                    em.SetComponentData(entities[i], stealth);
                else
                    em.AddComponentData(entities[i], stealth);

                stealthed++;
            }

            Debug.Log($"[SpellCastSystem] Veil of Shadows: stealthed {stealthed} units for {spell.Duration}s");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_SUMMONCARAVANGUARD - Summon 5 Flame Wardens
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Create 5 FlameWarden units at target position. Each despawns after 60s.
        /// </summary>
        private static bool ApplySummonCaravanGuard(EntityManager em, Faction faction, float3 target, SpellDefinition spell)
        {
            int count = (int)spell.EffectValue; // 5
            float lifetime = spell.SecondaryValue; // 60s

            for (int i = 0; i < count; i++)
            {
                // Offset units slightly so they don't stack
                float angle = (float)i / count * math.PI * 2f;
                float3 offset = new float3(math.cos(angle) * 2f, 0f, math.sin(angle) * 2f);
                float3 spawnPos = target + offset;

                Entity unit = UnitFactory.Create(em, "Sect_FlameWarden", spawnPos, faction);
                em.AddComponentData(unit, new SummonedUnit { DespawnTimer = lifetime });
            }

            Debug.Log($"[SpellCastSystem] Summon Caravan Guard: spawned {count} Flame Wardens for {lifetime}s");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_GOLDENTRIBUTE - Economy Boost
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Instantly add 500 Supplies + 200 Iron + 100 Crystal to caster's faction.
        /// Apply a SpellBuff marker to the faction bank entity for the 30s production boost window.
        /// </summary>
        private static bool ApplyGoldenTribute(EntityManager em, Faction faction, SpellDefinition spell)
        {
            // Add resources
            var resources = Cost.Of(supplies: 500, iron: 200, crystal: 100);
            FactionEconomy.Add(em, faction, in resources);

            // Find faction bank entity and apply production boost marker
            var bankQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionResources>()
            );

            using var bankEntities = bankQuery.ToEntityArray(Allocator.Temp);
            using var bankFactions = bankQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < bankEntities.Length; i++)
            {
                if (bankFactions[i].Value != faction) continue;

                var buff = new SpellBuff
                {
                    ArmorBonus = 0f,
                    DamageMultiplier = 1.5f, // Marks production boost
                    SpeedMultiplier = 1.0f,
                    DamageReflect = 0f,
                    TimeRemaining = spell.Duration // 30s
                };

                if (em.HasComponent<SpellBuff>(bankEntities[i]))
                    em.SetComponentData(bankEntities[i], buff);
                else
                    em.AddComponentData(bankEntities[i], buff);

                break;
            }

            Debug.Log($"[SpellCastSystem] Golden Tribute: +500 Supplies, +200 Iron, +100 Crystal for {faction}");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_ARCANESTORM - Lightning Storm (Coroutine)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Over 8 seconds, every 0.5s strike a random enemy within 20u of target.
        /// Each strike: 30 magic damage to primary + 10 splash to enemies within 3u.
        /// ~16 bolts total.
        /// </summary>
        private bool ApplyArcaneStorm(EntityManager em, Faction faction, float3 target, SpellDefinition spell)
        {
            StartCoroutine(ArcaneStormCoroutine(faction, target, spell));
            Debug.Log($"[SpellCastSystem] Arcane Storm: 16 bolts over 8s at {target}");
            return true;
        }

        private IEnumerator ArcaneStormCoroutine(Faction faction, float3 target, SpellDefinition spell)
        {
            int boltCount = (int)spell.SecondaryValue; // 16
            float interval = spell.Duration / boltCount; // 0.5s per bolt
            float radiusSq = spell.AreaRadius * spell.AreaRadius;
            var rng = new Unity.Mathematics.Random((uint)(Time.time * 1000 + 2));

            for (int b = 0; b < boltCount; b++)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) yield break;

                var em = world.EntityManager;

                // Find random enemy within radius
                var query = em.CreateEntityQuery(
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadWrite<Health>()
                );

                using var entities = query.ToEntityArray(Allocator.Temp);
                using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

                // Collect eligible targets
                var candidates = new NativeList<int>(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    if (factions[i].Value == faction) continue;
                    float distSq = math.distancesq(transforms[i].Position, target);
                    if (distSq <= radiusSq)
                        candidates.Add(i);
                }

                if (candidates.Length > 0)
                {
                    int idx = candidates[rng.NextInt(candidates.Length)];
                    float3 strikePos = transforms[idx].Position;

                    // Primary damage
                    var hp = healths[idx];
                    hp.Value = math.max(0, hp.Value - (int)spell.EffectValue); // 30
                    em.SetComponentData(entities[idx], hp);

                    // Splash damage to nearby enemies within 3u
                    float splashRadiusSq = 3f * 3f;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (i == idx) continue;
                        if (factions[i].Value == faction) continue;

                        float dSq = math.distancesq(transforms[i].Position, strikePos);
                        if (dSq <= splashRadiusSq)
                        {
                            var splashHp = healths[i];
                            splashHp.Value = math.max(0, splashHp.Value - 10);
                            em.SetComponentData(entities[i], splashHp);
                        }
                    }
                }

                candidates.Dispose();
                yield return new WaitForSeconds(interval);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_DOMINATE - Mind Control
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Find nearest enemy unit within 5u of target click.
        /// Add MindControlled component, change faction to caster's.
        /// MindControlSystem reverts on expiry.
        /// </summary>
        private static bool ApplyDominate(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float closestDist = float.MaxValue;
            int closestIdx = -1;

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction) continue; // enemy only

                float dist = math.distance(transforms[i].Position, center);
                if (dist < closestDist && dist < 5f)
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            if (closestIdx < 0)
            {
                Debug.Log("[SpellCastSystem] Dominate: no valid enemy unit found within range");
                return false;
            }

            Faction originalFaction = factions[closestIdx].Value;
            float duration = spell.SecondaryValue; // 30s

            // Add MindControlled component
            var mc = new MindControlled
            {
                OriginalFaction = originalFaction,
                TimeRemaining = duration
            };

            if (em.HasComponent<MindControlled>(entities[closestIdx]))
                em.SetComponentData(entities[closestIdx], mc);
            else
                em.AddComponentData(entities[closestIdx], mc);

            // Change faction to caster's
            em.SetComponentData(entities[closestIdx], new FactionTag { Value = faction });

            Debug.Log($"[SpellCastSystem] Dominate: took control of enemy unit from {originalFaction} for {duration}s");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_FIRESTORM - Area Denial (Burning Ground)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Spawn BurningGround entities in a grid covering the 15u radius area.
        /// Each tile: 10 DPS for 10s, 3u individual radius.
        /// After DPS phase ends, BurningGroundSystem destroys the tiles.
        /// </summary>
        private static bool ApplyFirestorm(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            float areaRadius = spell.AreaRadius; // 15
            float tileSpacing = 5f; // Space between burning ground tiles
            float tileDamageRadius = 3f;
            int tilesCreated = 0;

            // Create a grid of burning ground tiles covering the area
            for (float x = -areaRadius; x <= areaRadius; x += tileSpacing)
            {
                for (float z = -areaRadius; z <= areaRadius; z += tileSpacing)
                {
                    float3 tilePos = center + new float3(x, 0f, z);

                    // Only create tiles within the circular area
                    if (math.distance(new float2(tilePos.x, tilePos.z), new float2(center.x, center.z)) > areaRadius)
                        continue;

                    var tile = em.CreateEntity();
                    em.AddComponentData(tile, new LocalTransform
                    {
                        Position = tilePos,
                        Rotation = quaternion.identity,
                        Scale = 1f
                    });
                    em.AddComponentData(tile, new BurningGround
                    {
                        DPS = spell.EffectValue, // 10
                        TimeRemaining = spell.Duration, // 10s
                        Radius = tileDamageRadius
                    });
                    em.AddComponentData(tile, new FactionTag { Value = faction });

                    tilesCreated++;
                }
            }

            Debug.Log($"[SpellCastSystem] Firestorm: created {tilesCreated} burning ground tiles for {spell.Duration}s");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_SUMMONWARHOST - Summon 3 Brandbreaker + 2 Ashblade
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Create 3 Brandbreaker + 2 Ashblade units at target. Each despawns after 60s.
        /// </summary>
        private static bool ApplySummonWarHost(EntityManager em, Faction faction, float3 target, SpellDefinition spell)
        {
            float lifetime = spell.SecondaryValue; // 60s

            string[] unitTypes = { "Sect_Brandbreaker", "Sect_Brandbreaker", "Sect_Brandbreaker",
                                   "Sect_Ashblade", "Sect_Ashblade" };

            for (int i = 0; i < unitTypes.Length; i++)
            {
                float angle = (float)i / unitTypes.Length * math.PI * 2f;
                float3 offset = new float3(math.cos(angle) * 2f, 0f, math.sin(angle) * 2f);
                float3 spawnPos = target + offset;

                Entity unit = UnitFactory.Create(em, unitTypes[i], spawnPos, faction);
                em.AddComponentData(unit, new SummonedUnit { DespawnTimer = lifetime });
            }

            Debug.Log($"[SpellCastSystem] Summon War Host: spawned 3 Brandbreakers + 2 Ashblades for {lifetime}s");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_CHAINLIGHTNING - Chain Damage
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Hit primary target for 80 damage, then chain to 5 nearby enemies for 40 each.
        /// Each chain target becomes the new center for the next chain (10u search radius).
        /// </summary>
        private static bool ApplyChainLightning(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

            // Track which entities have been hit to avoid re-chaining
            var hitSet = new NativeHashSet<int>(8, Allocator.Temp);

            // Find primary target (nearest enemy within 5u)
            float closestDist = float.MaxValue;
            int closestIdx = -1;

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction) continue;
                float dist = math.distance(transforms[i].Position, center);
                if (dist < closestDist && dist < 5f)
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            if (closestIdx < 0)
            {
                hitSet.Dispose();
                Debug.Log("[SpellCastSystem] Chain Lightning: no valid target found");
                return false;
            }

            // Primary hit - 80 damage
            var primaryHp = healths[closestIdx];
            primaryHp.Value = math.max(0, primaryHp.Value - (int)spell.EffectValue);
            em.SetComponentData(entities[closestIdx], primaryHp);
            hitSet.Add(closestIdx);

            int totalHits = 1;
            float3 chainCenter = transforms[closestIdx].Position;
            float chainSearchRadius = 10f;
            int chainDamage = (int)spell.SecondaryValue; // 40

            // Chain 5 more times
            for (int chain = 0; chain < 5; chain++)
            {
                float bestDist = float.MaxValue;
                int bestIdx = -1;

                for (int i = 0; i < entities.Length; i++)
                {
                    if (factions[i].Value == faction) continue;
                    if (hitSet.Contains(i)) continue;

                    float dist = math.distance(transforms[i].Position, chainCenter);
                    if (dist < bestDist && dist < chainSearchRadius)
                    {
                        bestDist = dist;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0) break; // No more targets in range

                var chainHp = healths[bestIdx];
                chainHp.Value = math.max(0, chainHp.Value - chainDamage);
                em.SetComponentData(entities[bestIdx], chainHp);
                hitSet.Add(bestIdx);

                chainCenter = transforms[bestIdx].Position;
                totalHits++;
            }

            hitSet.Dispose();
            Debug.Log($"[SpellCastSystem] Chain Lightning: hit {totalHits} enemies");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SPELL_ANNIHILATION - Ultimate Nuke
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Find nearest entity within 5u of target.
        /// Crystal sub-nodes: instant destroy. Crystal main nodes: 500 true damage.
        /// Anything else: 500 true damage.
        /// </summary>
        private static bool ApplyAnnihilation(EntityManager em, Faction faction, float3 center, SpellDefinition spell)
        {
            // Find nearest entity with Health within 5u
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
                if (dist < closestDist && dist < 5f)
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            if (closestIdx < 0)
            {
                Debug.Log("[SpellCastSystem] Annihilation: no valid target found");
                return false;
            }

            Entity target = entities[closestIdx];

            // Crystal sub-node: instant kill
            if (em.HasComponent<CrystalSubNodeTag>(target))
            {
                var hp = healths[closestIdx];
                hp.Value = 0;
                em.SetComponentData(target, hp);
                Debug.Log("[SpellCastSystem] Annihilation: destroyed Crystal sub-node instantly");
                return true;
            }

            // Crystal main node or anything else: 500 true damage
            var targetHp = healths[closestIdx];
            targetHp.Value = math.max(0, targetHp.Value - (int)spell.EffectValue); // 500
            em.SetComponentData(target, targetHp);

            if (em.HasComponent<CrystalMainNodeTag>(target))
                Debug.Log($"[SpellCastSystem] Annihilation: dealt {spell.EffectValue} true damage to Crystal main node");
            else
                Debug.Log($"[SpellCastSystem] Annihilation: dealt {spell.EffectValue} true damage to target");

            return true;
        }
    }
}
