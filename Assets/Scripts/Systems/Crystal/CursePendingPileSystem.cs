// File: Assets/Scripts/Systems/Crystal/CursePendingPileSystem.cs
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Drives the curse-unit-death crystal payout. CursePendingPile entities are
    /// created/updated by CrystalDeathDropSystem on each curse-unit death; this
    /// system ticks their 30 s timers and, on expiry, distributes the accumulated
    /// crystal across every existing crystal patch on the map.
    ///
    /// Distribution rules (cadaver = node, patch = spatial cluster of nodes):
    ///   1. Cluster all CadaverTag entities into patches using PatchClusterRadius.
    ///   2. With N patches found, each patch receives floor(Amount/N), and the
    ///      modulo remainder is given one extra crystal each to the first
    ///      (Amount mod N) patches so every crystal lands in a patch.
    ///   3. Within each patch, top up existing non-full nodes up to
    ///      MaxCrystalPerNode (60). If any of the patch's share is left over,
    ///      spawn new nodes within the patch (each up to 60) until exhausted.
    ///   4. If no patches exist (N == 0), spawn a brand-new patch near the
    ///      nearest active CrystalNode (curse spread source); falls back to the
    ///      pile's own death position if no curse nodes are alive.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CrystalDeathDropSystem))]
    public partial class CursePendingPileSystem : SystemBase
    {
        /// <summary>Per-node crystal cap once a patch is being topped up by a payout.</summary>
        public const int MaxCrystalPerNode = 60;

        /// <summary>
        /// Two cadavers belong to the same patch if reachable by a chain of
        /// node-to-node hops each ≤ this distance. Sized to cluster all nodes
        /// inside a scattered patch (5 m spread, max pairwise ≈ 10 m) while
        /// leaving room (≥ 14 m gap between distinct patches given the 24 m
        /// MinDistBetweenPatchCenters) so adjacent patches stay separate.
        /// </summary>
        public const float PatchClusterRadius = 12f;

        /// <summary>Max nodes a single payout can spawn — guards against runaway pile values.</summary>
        public const int MaxNodesPerPayout = 32;

        /// <summary>
        /// Resource-patch size at which the patch is consumed and replaced by a
        /// secondary curse location (1 main node + pylons, tagged
        /// <see cref="SecondaryCurseLocationTag"/>). Set so a 44-node seed patch
        /// converts on the first additional node spawn.
        /// </summary>
        public const int PatchConvertNodeThreshold = 45;

        /// <summary>Radius around the converted patch's centroid where pylons are placed.</summary>
        private const float PylonRingRadius = 4f;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // 1. Tick down all pile timers and collect the ones that expired.
            var expiredPiles = new List<Entity>();
            foreach (var (pile, entity) in SystemAPI
                .Query<RefRW<CursePendingPile>>()
                .WithEntityAccess())
            {
                pile.ValueRW.TimerRemaining -= dt;
                if (pile.ValueRW.TimerRemaining <= 0f)
                {
                    expiredPiles.Add(entity);
                }
            }

            if (expiredPiles.Count == 0) return;

            // 2. Snapshot every existing cadaver (node) once — distribution may
            //    mutate them and add new ones, but we cluster against the
            //    pre-payout state so each pile sees a consistent map.
            var cadaverQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CadaverTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<CadaverState>());
            using var cadaverEntities = cadaverQuery.ToEntityArray(Allocator.Temp);
            using var cadaverTransforms = cadaverQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // 3. Cluster cadavers into patches via BFS on the PatchClusterRadius graph.
            var patches = BuildPatches(cadaverEntities, cadaverTransforms);

            foreach (var pileEntity in expiredPiles)
            {
                var pile = em.GetComponentData<CursePendingPile>(pileEntity);
                var pileTransform = em.GetComponentData<LocalTransform>(pileEntity);

                if (pile.Amount > 0)
                {
                    DistributePile(em, pile.Amount, pileTransform.Position, patches);
                }

                em.DestroyEntity(pileEntity);
            }

            // After all piles for this frame have paid out, any patch that has
            // grown to PatchConvertNodeThreshold or more is consumed and replaced
            // by a secondary curse location.
            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i].Count >= PatchConvertNodeThreshold)
                {
                    ConvertPatchToCurseLocation(em, patches[i]);
                }
            }
        }

        /// <summary>BFS-cluster every cadaver by PatchClusterRadius.</summary>
        private static List<List<Entity>> BuildPatches(
            NativeArray<Entity> cadaverEntities,
            NativeArray<LocalTransform> cadaverTransforms)
        {
            var patches = new List<List<Entity>>();
            int n = cadaverEntities.Length;
            if (n == 0) return patches;

            var visited = new bool[n];
            float clusterSqr = PatchClusterRadius * PatchClusterRadius;
            var stack = new Stack<int>();

            for (int seed = 0; seed < n; seed++)
            {
                if (visited[seed]) continue;

                var patch = new List<Entity>();
                stack.Push(seed);
                visited[seed] = true;

                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    patch.Add(cadaverEntities[current]);

                    float2 a = new float2(cadaverTransforms[current].Position.x, cadaverTransforms[current].Position.z);
                    for (int j = 0; j < n; j++)
                    {
                        if (visited[j]) continue;
                        float2 b = new float2(cadaverTransforms[j].Position.x, cadaverTransforms[j].Position.z);
                        if (math.distancesq(a, b) <= clusterSqr)
                        {
                            visited[j] = true;
                            stack.Push(j);
                        }
                    }
                }

                patches.Add(patch);
            }

            return patches;
        }

        private static void DistributePile(
            EntityManager em,
            int totalAmount,
            float3 pilePosition,
            List<List<Entity>> patches)
        {
            if (patches.Count == 0)
            {
                SpawnFreshPatch(em, pilePosition, totalAmount);
                return;
            }

            int n = patches.Count;
            int baseShare = totalAmount / n;
            int remainder = totalAmount - baseShare * n;

            int spawnBudget = MaxNodesPerPayout;
            for (int i = 0; i < n; i++)
            {
                int share = baseShare + (i < remainder ? 1 : 0);
                if (share <= 0) continue;
                spawnBudget = ApplyShareToPatch(em, patches[i], share, spawnBudget);
            }
        }

        /// <summary>
        /// Top up the patch's existing non-full nodes, then spawn new nodes within
        /// the patch until the share is exhausted or we hit the per-payout cap.
        /// Returns the remaining spawn budget.
        /// </summary>
        private static int ApplyShareToPatch(
            EntityManager em,
            List<Entity> patchNodes,
            int share,
            int spawnBudget)
        {
            // Top up existing non-full nodes first.
            for (int i = 0; i < patchNodes.Count && share > 0; i++)
            {
                var nodeEntity = patchNodes[i];
                if (!em.HasComponent<CadaverState>(nodeEntity)) continue;

                var nodeState = em.GetComponentData<CadaverState>(nodeEntity);
                if (nodeState.Depleted != 0) continue;
                if (nodeState.RemainingCrystal >= MaxCrystalPerNode) continue;

                int room = MaxCrystalPerNode - nodeState.RemainingCrystal;
                int add = math.min(room, share);
                nodeState.RemainingCrystal += add;
                if (nodeState.MaxCrystal < nodeState.RemainingCrystal)
                    nodeState.MaxCrystal = nodeState.RemainingCrystal;
                em.SetComponentData(nodeEntity, nodeState);

                // Refresh visual scale + selection radius to track the new amount.
                if (em.HasComponent<LocalTransform>(nodeEntity))
                {
                    var lt = em.GetComponentData<LocalTransform>(nodeEntity);
                    lt.Scale = Cadaver.ComputeScale(nodeState.RemainingCrystal);
                    em.SetComponentData(nodeEntity, lt);
                }
                if (em.HasComponent<Radius>(nodeEntity))
                {
                    em.SetComponentData(nodeEntity, new Radius
                    {
                        Value = Cadaver.ComputeRadius(nodeState.RemainingCrystal)
                    });
                }

                share -= add;
            }

            if (share <= 0 || spawnBudget <= 0) return spawnBudget;

            // Patch share still has leftover — spawn new nodes inside the patch.
            ComputePatchBounds(em, patchNodes, out float3 centroid, out float patchRadius);
            float spawnRadius = math.max(2f, patchRadius * 0.9f);
            uint seed = (uint)((int)(centroid.x * 73.1f + centroid.z * 19.7f) ^ share ^ 0x51A3CD);
            if (seed == 0) seed = 1;
            var rng = new Unity.Mathematics.Random(seed);

            while (share > 0 && spawnBudget > 0)
            {
                int chunk = math.min(share, MaxCrystalPerNode);
                float angle = rng.NextFloat(0f, math.PI * 2f);
                float dist  = rng.NextFloat(0f, spawnRadius);
                float x = centroid.x + math.cos(angle) * dist;
                float z = centroid.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                var newNode = Cadaver.Create(em, new float3(x, y, z), chunk);
                patchNodes.Add(newNode);  // keep patch list in sync so the 45-node check sees freshly-spawned nodes
                share -= chunk;
                spawnBudget--;
            }

            return spawnBudget;
        }

        private static void ComputePatchBounds(
            EntityManager em,
            List<Entity> patchNodes,
            out float3 centroid,
            out float patchRadius)
        {
            double sumX = 0, sumY = 0, sumZ = 0;
            int count = 0;
            for (int i = 0; i < patchNodes.Count; i++)
            {
                if (!em.HasComponent<LocalTransform>(patchNodes[i])) continue;
                var p = em.GetComponentData<LocalTransform>(patchNodes[i]).Position;
                sumX += p.x; sumY += p.y; sumZ += p.z;
                count++;
            }

            if (count == 0)
            {
                centroid = float3.zero;
                patchRadius = 1f;
                return;
            }

            centroid = new float3((float)(sumX / count), (float)(sumY / count), (float)(sumZ / count));

            float maxSqr = 0f;
            for (int i = 0; i < patchNodes.Count; i++)
            {
                if (!em.HasComponent<LocalTransform>(patchNodes[i])) continue;
                var p = em.GetComponentData<LocalTransform>(patchNodes[i]).Position;
                float2 a = new float2(p.x, p.z);
                float2 b = new float2(centroid.x, centroid.z);
                float dsq = math.distancesq(a, b);
                if (dsq > maxSqr) maxSqr = dsq;
            }
            patchRadius = math.max(2f, math.sqrt(maxSqr));
        }

        /// <summary>
        /// Spawn a brand-new patch (one or more cadavers, each ≤ MaxCrystalPerNode)
        /// near an active CrystalNode. Falls back to the pile's death position if
        /// no curse spread source is alive.
        /// </summary>
        private static void SpawnFreshPatch(EntityManager em, float3 fallbackPosition, int amount)
        {
            float3 center = PickFreshPatchCenter(em, fallbackPosition);
            uint seed = (uint)((int)(center.x * 91.3f + center.z * 41.1f) ^ amount ^ 0x7FCAFE);
            if (seed == 0) seed = 1;
            var rng = new Unity.Mathematics.Random(seed);

            int remaining = amount;
            int spawned = 0;
            while (remaining > 0 && spawned < MaxNodesPerPayout)
            {
                int chunk = math.min(remaining, MaxCrystalPerNode);
                float angle = rng.NextFloat(0f, math.PI * 2f);
                float dist  = spawned == 0 ? 0f : rng.NextFloat(1.5f, 4f);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                Cadaver.Create(em, new float3(x, y, z), chunk);
                remaining -= chunk;
                spawned++;
            }
        }

        /// <summary>
        /// Consume every resource node in the patch and replace them with a
        /// secondary curse location: 1 CrystalMainNode at the centroid + 3
        /// pylons (Turret, Enforcement, Suppression) on a small ring around it.
        /// All four entities are tagged <see cref="SecondaryCurseLocationTag"/>
        /// so their RP-yield and no-regrow behaviours kick in.
        /// </summary>
        private static void ConvertPatchToCurseLocation(EntityManager em, List<Entity> patchNodes)
        {
            ComputePatchBounds(em, patchNodes, out float3 centroid, out _);

            // Destroy the cadavers — the resource patch is "consumed" by the curse.
            for (int i = 0; i < patchNodes.Count; i++)
            {
                if (em.Exists(patchNodes[i]))
                    em.DestroyEntity(patchNodes[i]);
            }

            // Spawn the main node at the centroid + 3 pylons on a ring around it.
            // CrystalMainNode / Crystal*Node factories live in TheWaningBorder.Entities (imported).
            float3 mainPos = new float3(centroid.x, TerrainUtility.GetHeight(centroid.x, centroid.z), centroid.z);
            var main = CrystalMainNode.Create(em, mainPos, Faction.Curse);
            em.AddComponent<SecondaryCurseLocationTag>(main);

            float step = math.PI * 2f / 3f;

            float3 turretPos = OffsetOnRing(centroid, step * 0, PylonRingRadius);
            var turret = CrystalTurretNode.Create(em, turretPos, Faction.Curse);
            em.AddComponent<SecondaryCurseLocationTag>(turret);
            em.AddComponentData(turret, new LastDamagedByFaction { Value = Faction.Curse });

            float3 enforcePos = OffsetOnRing(centroid, step * 1, PylonRingRadius);
            var enforce = CrystalEnforcementNode.Create(em, enforcePos, Faction.Curse);
            em.AddComponent<SecondaryCurseLocationTag>(enforce);
            em.AddComponentData(enforce, new LastDamagedByFaction { Value = Faction.Curse });

            float3 suppressPos = OffsetOnRing(centroid, step * 2, PylonRingRadius);
            var suppress = CrystalSuppressionNode.Create(em, suppressPos, Faction.Curse);
            em.AddComponent<SecondaryCurseLocationTag>(suppress);
            em.AddComponentData(suppress, new LastDamagedByFaction { Value = Faction.Curse });

            TWBLog.Log($"[CursePendingPileSystem] resource patch grew to {patchNodes.Count} nodes — converted to secondary curse location at ({centroid.x:F1}, {centroid.z:F1})");
        }

        private static float3 OffsetOnRing(float3 centroid, float angle, float radius)
        {
            float x = centroid.x + math.cos(angle) * radius;
            float z = centroid.z + math.sin(angle) * radius;
            float y = TerrainUtility.GetHeight(x, z);
            return new float3(x, y, z);
        }

        private static float3 PickFreshPatchCenter(EntityManager em, float3 fallback)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalNode>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var nodeTransforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            if (nodeTransforms.Length == 0)
            {
                return fallback;
            }

            // Pick the nearest active curse node to the fallback (death) position
            // — keeps the new patch in the same theatre as the deaths.
            int best = 0;
            float bestSqr = float.MaxValue;
            float2 f = new float2(fallback.x, fallback.z);
            for (int i = 0; i < nodeTransforms.Length; i++)
            {
                float2 n = new float2(nodeTransforms[i].Position.x, nodeTransforms[i].Position.z);
                float d = math.distancesq(f, n);
                if (d < bestSqr) { bestSqr = d; best = i; }
            }

            float3 nodePos = nodeTransforms[best].Position;
            const float offset = 6f;
            float3 candidate = new float3(nodePos.x + offset, nodePos.y, nodePos.z);
            candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);
            return candidate;
        }
    }
}
