// NodeStateDeathInterceptSystem.cs
// Crystal main nodes don't die — they go dormant. When a node's HP hits 0
// we transition it to State=Destroyed, tag it NodeDormant (DeathSystem skips
// dormant entities), and stamp the destroyer's faction + culture onto both
// the node and the global NodeVictoryState so the victory checker can fire
// the Feraldis instant-win.
//
// Spec §9 (state machine), §8 (Feraldis instant victory on killing blow).
//
// Runs UpdateBefore(DeathSystem) so the intercept happens before destruction.
//
// Location: Assets/Scripts/Systems/Crystal/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Intercepts Crystal main node deaths and converts them into the
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
                .Query<RefRO<Health>, RefRO<CrystalNodeState>, RefRO<LastDamagedByFaction>, RefRO<LocalTransform>>()
                .WithAll<CrystalMainNodeTag>()
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

                // Fall back to Curse if killer attribution missing — the node
                // still transitions to Destroyed (regrowth applies), it just
                // can't credit a Feraldis killing blow for the victory check.
                byte killerCulture = cultureOf.TryGetValue((byte)killer, out byte c)
                    ? c
                    : Cultures.None;

                CrystalNodeStateHelper.SetState(
                    EntityManager,
                    node,
                    NodeState.Destroyed,
                    killerCulture,
                    killer);

                if (hasVictoryState)
                {
                    victory.LastDestroyerFaction = killer;
                    victory.LastDestroyerCulture = killerCulture;
                }

                // Secondary curse nodes (born from over-grown resource patches)
                // don't trigger the Feraldis Violent Extraction reward — their
                // payout is a flat +1 RP to the killer, granted by
                // CrystalDeathDropSystem when the entity actually dies.
                bool isSecondary = EntityManager.HasComponent<SecondaryCurseLocationTag>(node);

                // Feraldis Violent Extraction (spec §5.3): when the killing
                // blow came from a Feraldis-aligned faction, the node erupts
                // in a final massive curse wave + drops a Glow pickup. Other
                // factions that bring a node to 0 HP still cause Destroyed
                // (state machine is symmetric) but do NOT trigger the
                // extraction reward — they didn't perform the ritual.
                if (killerCulture == Cultures.Feraldis && !isSecondary)
                {
                    SpawnFinalCurseWave(dyingPositions[i], killer);
                    GlowPickup.Create(EntityManager, dyingPositions[i],
                        RitualKind.ViolentExtraction,
                        ViolentExtractionGlowYield);
                    TWBLog.Log($"[ViolentExtraction] node destroyed by {killer} (Feraldis) — final wave + Glow pickup spawned");
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
        /// Spawn the spec §5.3 final massive curse wave around a node that
        /// was just destroyed by Feraldis. Units charge the killer's nearest
        /// hall (or, failing that, the killer's last known position).
        /// </summary>
        private void SpawnFinalCurseWave(float3 nodePos, Faction killer)
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
            uint seed = (uint)(math.abs((int)(nodePos.x * 1009 + nodePos.z * 7919)) + (int)killer * 31 + 1);
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
                    ? Veilstinger.Create(em, spawnPos, Faction.Curse)
                    : Crystalling.Create(em, spawnPos, Faction.Curse);

                if (em.HasComponent<CrystalWaveOrder>(unit))
                    em.SetComponentData(unit, new CrystalWaveOrder { Target = target, WaveNumber = -2 });
                else
                    em.AddComponentData(unit, new CrystalWaveOrder { Target = target, WaveNumber = -2 });
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
