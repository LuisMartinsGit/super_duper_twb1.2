// InquisitorCleanseSystem.cs
// Justice's Inquisitor (unit lever, Lv I): every CleansePeriod seconds
// the Inquisitor strips one debuff from the nearest afflicted ally in
// range. The only cleansable debuff today is CodexFrozen (Antiquity's
// Recall the Codex / the Reliquary Lockout); new debuffs opt in here as
// they are implemented.
//
// SystemBase on a 0.5 s cadence, collect-then-apply (no structural
// changes mid-query) — same pattern as LorekeeperDetectionSystem.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class InquisitorCleanseSystem : SystemBase
    {
        private const float TickInterval = 0.5f;
        private const float CleanseRange = 10f;
        private const float CleansePeriod = 10f;

        private float _accum;

        protected override void OnCreate()
        {
            RequireForUpdate<InquisitorTag>();
        }

        protected override void OnUpdate()
        {
            _accum += SystemAPI.Time.DeltaTime;
            if (_accum < TickInterval) return;
            float elapsed = _accum;
            _accum = 0f;

            var em = EntityManager;

            // Snapshot afflicted units (currently: CodexFrozen carriers).
            var frozenQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CodexFrozen>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var frozenEnts = frozenQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var frozenPos = frozenQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            using var frozenFac = frozenQuery.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);

            var cleansed = new List<Entity>(4);

            foreach (var (state, transform, factionTag) in SystemAPI
                .Query<RefRW<InquisitorState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<InquisitorTag>())
            {
                if (state.ValueRO.CleanseCooldown > 0f)
                {
                    state.ValueRW.CleanseCooldown -= elapsed;
                    continue;
                }
                if (frozenEnts.Length == 0) continue;

                // Nearest afflicted ALLY in range, not already claimed by
                // another Inquisitor this tick (fixed iteration order keeps
                // this deterministic for lockstep).
                float bestDistSq = CleanseRange * CleanseRange;
                int best = -1;
                for (int i = 0; i < frozenEnts.Length; i++)
                {
                    // Inquisitors cleanse allies too. docs/Design/Teams.md
                    if (!Alliances.AreAllied(factionTag.ValueRO.Value, frozenFac[i].Value)) continue;
                    if (cleansed.Contains(frozenEnts[i])) continue;
                    float3 d = frozenPos[i].Position - transform.ValueRO.Position;
                    float distSq = d.x * d.x + d.z * d.z;
                    if (distSq > bestDistSq) continue;
                    bestDistSq = distSq;
                    best = i;
                }
                if (best < 0) continue;

                cleansed.Add(frozenEnts[best]);
                state.ValueRW.CleanseCooldown = CleansePeriod;
            }

            for (int i = 0; i < cleansed.Count; i++)
            {
                if (em.Exists(cleansed[i]) && em.HasComponent<CodexFrozen>(cleansed[i]))
                    em.RemoveComponent<CodexFrozen>(cleansed[i]);
            }
        }
    }
}
