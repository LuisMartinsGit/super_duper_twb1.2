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
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;

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
            var killers = new NativeList<Faction>(2, Allocator.Temp);

            foreach (var (health, state, lastDamager, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<CrystalNodeState>, RefRO<LastDamagedByFaction>>()
                .WithAll<CrystalMainNodeTag>()
                .WithNone<NodeDormant>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;
                if (state.ValueRO.State == NodeState.Destroyed) continue;

                dyingNodes.Add(entity);
                killers.Add(lastDamager.ValueRO.Value);
            }

            if (dyingNodes.Length == 0)
            {
                dyingNodes.Dispose();
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
            }

            if (hasVictoryState)
                EntityManager.SetComponentData(victoryEntity, victory);

            cultureOf.Dispose();
            dyingNodes.Dispose();
            killers.Dispose();
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
