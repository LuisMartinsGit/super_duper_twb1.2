// NodeStateDeathInterceptSystem.cs
// Veilstone main nodes don't die — they go dormant. When a node's HP hits 0
// we transition it to State=Destroyed, tag it NodeDormant (DeathSystem skips
// dormant entities), and stamp the destroyer's faction + culture onto both
// the node and the global NodeVictoryState so the victory checker can fire
// the Feraldis instant-win.
//
// Spec §9 (state machine), §8 (Feraldis instant victory on killing blow).
//
// Runs UpdateBefore(DeathSystem) so the intercept happens before destruction.
//
// Location: Assets/GameData/TechTree/Buildings/Border/LargeNode/NodeStateDeathInterceptSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Intercepts Veilstone main node deaths and converts them into the
    /// Destroyed state instead of letting DeathSystem delete the entity.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class NodeStateDeathInterceptSystem : SystemBase
    {
        private EntityQuery _victoryQuery;
        private EntityQuery _factionProgressQuery;

        protected override void OnCreate()
        {
            _victoryQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<NodeVictoryState>()
            );
            _factionProgressQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>()
            );
        }

        protected override void OnUpdate()
        {
            // Collect dying main nodes — defer structural changes until after
            // the query loop, same pattern as NodeStateReversionSystem.
            var dyingNodes = new NativeList<Entity>(2, Allocator.Temp);
            var dyingPositions = new NativeList<float3>(2, Allocator.Temp);
            var killers = new NativeList<Faction>(2, Allocator.Temp);

            foreach (var (health, state, lastDamager, transform, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<BorderNodeState>, RefRO<LastDamagedByFaction>, RefRO<LocalTransform>>()
                .WithAll<BorderMainNodeTag>()
                .WithNone<NodeDormant>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;
                if (state.ValueRO.State == NodeState.Destroyed) continue;

                dyingNodes.Add(entity);
                dyingPositions.Add(transform.ValueRO.Position);
                killers.Add(lastDamager.ValueRO.Value);
            }

            if (dyingNodes.Length == 0)
            {
                dyingNodes.Dispose();
                dyingPositions.Dispose();
                killers.Dispose();
                return;
            }

            // Build a faction -> culture lookup so we can stamp the killer's
            // culture on the node + victory state.
            var cultureOf = BuildFactionCultureLookup();

            // Pull victory singleton up-front (mutated below if any kill landed).
            bool hasVictoryState = !_victoryQuery.IsEmpty;
            NodeVictoryState victory = default;
            Entity victoryEntity = Entity.Null;
            if (hasVictoryState)
            {
                using var ve = _victoryQuery.ToEntityArray(Allocator.Temp);
                victoryEntity = ve[0];
                victory = EntityManager.GetComponentData<NodeVictoryState>(victoryEntity);
            }

            for (int i = 0; i < dyingNodes.Length; i++)
            {
                Entity node = dyingNodes[i];
                Faction killer = killers[i];

                // Fall back to Border if killer attribution missing — the node
                // still transitions to Destroyed (regrowth applies), it just
                // can't credit a Feraldis killing blow for the victory check.
                byte killerCulture = cultureOf.TryGetValue((byte)killer, out byte c)
                    ? c
                    : Cultures.None;

                // CORRUPTION CREDIT (Feraldis Corruptor). A cracked-open well
                // is auto-acquirable for its window, so anyone standing there
                // can land hits on it — including a rival army rushing in to
                // smash the well purely to DENY the Feraldis win. Credit
                // follows the faction whose Corruptor opened it: they did the
                // work, and the kill is the payoff for a 40 s channel plus a
                // fought-off defence wave.
                if (EntityManager.HasComponent<WellCorrupted>(node))
                {
                    var wc = EntityManager.GetComponentData<WellCorrupted>(node);
                    killer = wc.Corruptor;
                    killerCulture = cultureOf.TryGetValue((byte)killer, out byte cc)
                        ? cc
                        : Cultures.Feraldis;
                }

                BorderNodeStateHelper.SetState(
                    EntityManager,
                    node,
                    NodeState.Destroyed,
                    killerCulture,
                    killer);

                // VEILSTONE LOOT BURST (2026-08-04): the collapsing crust
                // precipitates its substance — a ring of mineable residue
                // around the dead well, claimable by whoever holds the
                // ground while the crust violently recedes
                // (DestroyedDecayPerTick). Deterministic scatter from the
                // node's entity index.
                {
                    var lootRng = new Unity.Mathematics.Random(
                        (uint)(node.Index * 2654435761u + 97) | 1u);
                    const int LootNodes = 8;
                    const int LootPerNode = 50;
                    const float LootRadius = 18f;
                    float3 wellPos = dyingPositions[i];
                    for (int n = 0; n < LootNodes; n++)
                    {
                        float angle = lootRng.NextFloat(0f, math.PI * 2f);
                        float dist = lootRng.NextFloat(4f, LootRadius);
                        float x = wellPos.x + math.cos(angle) * dist;
                        float z = wellPos.z + math.sin(angle) * dist;
                        float y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(x, z);
                        TheWaningBorder.Entities.VeilstoneOutcropping.CreateOrMerge(
                            EntityManager, new float3(x, y, z), LootPerNode);
                    }
                }

                if (hasVictoryState)
                {
                    victory.LastDestroyerFaction = killer;
                    victory.LastDestroyerCulture = killerCulture;
                }

                // Secondary border nodes (born from over-grown resource patches)
                // don't trigger the Feraldis Violent Extraction reward — their
                // payout is a flat +1 RP to the killer, granted by
                // BorderDeathDropSystem when the entity actually dies.
                bool isSecondary = EntityManager.HasComponent<SecondaryBorderLocationTag>(node);

                // Feraldis Violent Extraction (spec §5.3): when the killing
                // blow came from a Feraldis-aligned faction, the node erupts
                // in a final massive border wave + drops a Glow pickup. Other
                // factions that bring a node to 0 HP still cause Destroyed
                // (state machine is symmetric) but do NOT trigger the
                // extraction reward — they didn't perform the ritual.
                if (killerCulture == Cultures.Feraldis && !isSecondary)
                {
                    SpawnFinalBorderWave(dyingPositions[i], killer, node);

                    // Curse & Shardroot canon §2.3 (THE VEIL): no discrete
                    // shard-field drop — the dead well's crust now lingers
                    // (slow decay in VeilFieldSystem) as an UNDEFENDED
                    // minable loot field, Feraldis' burst income. Plus the
                    // Shardroot if this was the seeded host well.
                    ShardrootSystem.TryAward(EntityManager, node,
                        dyingPositions[i], RitualKind.ViolentExtraction);
                    TWBLog.Log($"[ViolentExtraction] well destroyed by {killer} (Feraldis) — final wave; its crust lingers as an undefended loot field");
                }
            }

            if (hasVictoryState)
                EntityManager.SetComponentData(victoryEntity, victory);

            cultureOf.Dispose();
            dyingNodes.Dispose();
            dyingPositions.Dispose();
            killers.Dispose();
        }

        /// <summary>
        /// Spawn the spec §5.3 final massive border wave around a node that
        /// was just destroyed by Feraldis. Units charge the killer's nearest
        /// hall (or, failing that, the killer's last known position).
        /// </summary>
        private void SpawnFinalBorderWave(float3 nodePos, Faction killer, Entity node)
        {
            var em = EntityManager;

            // Find a target — prefer the killer's hall.
            float3 target = nodePos; // fallback: charge outward from the corpse
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var hallEnts = hallQuery.ToEntityArray(Allocator.Temp);
            using var hallTags = hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < hallEnts.Length; i++)
            {
                if (hallTags[i].Value != killer) continue;
                if (em.HasComponent<Health>(hallEnts[i])
                    && em.GetComponentData<Health>(hallEnts[i]).Value <= 0) continue;
                target = hallTransforms[i].Position;
                break;
            }

            // Deterministic per-event seed so multiplayer replays match.
            // Quantized to whole millimetres BEFORE the multiply: seeding from
            // raw float truncation meant a single ULP of position drift flips
            // the entire wave layout — the loot burst already seeds from the
            // node index for the same reason.
            int qx = (int)math.round(nodePos.x * 1000f);
            int qz = (int)math.round(nodePos.z * 1000f);
            uint seed = (uint)(math.abs(qx * 1009 + qz * 7919) + (int)killer * 31 + 1);
            var rng = new Unity.Mathematics.Random(math.max(1u, seed));

            for (int i = 0; i < ViolentExtractionFinalWaveSize; i++)
            {
                float angle = rng.NextFloat(0f, math.PI * 2f);
                float r = ViolentExtractionFinalWaveRadius * rng.NextFloat(0.5f, 1.0f);
                var spawnPos = new float3(
                    nodePos.x + math.cos(angle) * r,
                    nodePos.y,
                    nodePos.z + math.sin(angle) * r);

                // Two Veilstingers, the rest Crystallings — gives the wave a
                // ranged backbone so it feels like a climax, not just a melee
                // mob.
                Entity unit = (i < 2)
                    ? Veilstinger.Create(em, spawnPos, Faction.Border)
                    : Crystalling.Create(em, spawnPos, Faction.Border);

                if (em.HasComponent<BorderWaveOrder>(unit))
                    em.SetComponentData(unit, new BorderWaveOrder { Target = target, WaveNumber = -2 });
                else
                    em.AddComponentData(unit, new BorderWaveOrder { Target = target, WaveNumber = -2 });

                // Group this death-throes wave with its (now-dying) node + the
                // ATTACK slot so BorderHordeSystem charges it at the nearest enemy
                // (the node is Destroyed, so BorderArmyAISystem leaves it alone).
                if (em.HasComponent<OwnerNode>(unit))
                    em.SetComponentData(unit, new OwnerNode { Value = node });
                else
                    em.AddComponentData(unit, new OwnerNode { Value = node });
                if (em.HasComponent<BorderArmyRole>(unit))
                    em.SetComponentData(unit, new BorderArmyRole { Role = BorderArmyRoleType.Attack });
                else
                    em.AddComponentData(unit, new BorderArmyRole { Role = BorderArmyRoleType.Attack });
            }
        }

        /// <summary>
        /// Faction byte → Cultures.* byte lookup, built from every faction bank
        /// entity that carries a FactionProgress component. Used to translate
        /// LastDamagedByFaction into a culture id without scanning each frame.
        /// </summary>
        private NativeHashMap<byte, byte> BuildFactionCultureLookup()
        {
            var map = new NativeHashMap<byte, byte>(8, Allocator.Temp);
            if (_factionProgressQuery.IsEmpty) return map;

            using var factions = _factionProgressQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var progress = _factionProgressQuery.ToComponentDataArray<FactionProgress>(Allocator.Temp);

            for (int i = 0; i < factions.Length; i++)
            {
                byte key = (byte)factions[i].Value;
                if (!map.ContainsKey(key))
                    map.Add(key, progress[i].Culture);
            }
            return map;
        }
    }
}
