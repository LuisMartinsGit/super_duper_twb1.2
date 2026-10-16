// EquipmentTierSystem.cs
// Applies the per-faction equipment tier multiplier to unit stats. Mirrors
// UnitRankSystem's stamp-and-apply pattern so the two layers stack cleanly:
// UnitRank covers per-unit veterancy, EquipmentTier covers faction-wide
// research. A Lv3 (Veilstone-rank) unit with Veilsteel equipment gets BOTH
// multipliers applied to Damage / Defense.
//
// Stamp pattern: UnitEquipmentApplied tracks the last-applied tier per
// unit. Units whose faction tier for their class differs from the stamp
// get the (new/old) diff applied and the stamp bumped.
//
// CHANGE-DRIVEN (2026-10-15 perf). This used to walk EVERY unit EVERY tick
// with three EntityManager calls each — 6 ms a frame late game, for a pass
// that is a no-op on all but a handful of ticks a match. A unit's target
// tier is a pure function of (its faction's FactionEquipmentTier, its
// UnitTag class, its FactionTag, its UnitTierOverride), and the stamp is
// written only here. So a unit can need work only when one of those inputs
// moved since this system last ran:
//   * any faction's tier changed (a research completed)  -> every chunk
//   * the unit's chunk changed order (spawned / archetype move)
//   * the chunk's UnitTag / FactionTag / UnitTierOverride was written
// Every other chunk is provably target == applied for all its units, so
// skipping it is RESULT-IDENTICAL, not merely deterministic: the processed
// units are visited in the same chunk order as before and get the same
// writes. Over-detection (a system taking RW on FactionTag without changing
// it) only costs a no-op revisit. Spec §4.1.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UnitRankSystem))]
    public partial struct EquipmentTierSystem : ISystem
    {
        // Faction enum ranges 0..7 (player) and 8 (Border) — size 9.
        private const int FactionCount = 9;

        private EntityQuery _units;
        private EntityQuery _tiers;
        private EntityTypeHandle _entityH;
        private ComponentTypeHandle<UnitTag> _unitTagH;
        private ComponentTypeHandle<FactionTag> _factionH;
        private ComponentTypeHandle<UnitTierOverride> _overrideH;
        private ComponentTypeHandle<UnitEquipmentApplied> _appliedH;
        private ComponentTypeHandle<FactionEquipmentTier> _tierH;

        private struct Work
        {
            public Entity E;
            public EquipmentTier Applied;
            public EquipmentTier Target;
            public bool HasStamp;
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
            state.RequireForUpdate<FactionEquipmentTier>();
            _units = SystemAPI.QueryBuilder().WithAll<UnitTag, FactionTag>().Build();
            _tiers = SystemAPI.QueryBuilder().WithAll<FactionTag, FactionEquipmentTier>().Build();
            _entityH = state.GetEntityTypeHandle();
            _unitTagH = state.GetComponentTypeHandle<UnitTag>(true);
            _factionH = state.GetComponentTypeHandle<FactionTag>(true);
            _overrideH = state.GetComponentTypeHandle<UnitTierOverride>(true);
            _appliedH = state.GetComponentTypeHandle<UnitEquipmentApplied>(true);
            _tierH = state.GetComponentTypeHandle<FactionEquipmentTier>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            uint last = state.LastSystemVersion;

            _entityH.Update(ref state);
            _unitTagH.Update(ref state);
            _factionH.Update(ref state);
            _overrideH.Update(ref state);
            _appliedH.Update(ref state);
            _tierH.Update(ref state);

            // Snapshot per-faction tiers (all zero-initialised → Base), and
            // note whether any of them moved since the last run.
            var byFaction = new NativeArray<FactionEquipmentTier>(FactionCount, Allocator.Temp);
            bool tiersChanged = false;
            _tiers.CompleteDependency();
            using (var tierChunks = _tiers.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < tierChunks.Length; c++)
                {
                    var chunk = tierChunks[c];
                    if (chunk.DidOrderChange(last) || chunk.DidChange(ref _tierH, last)
                        || chunk.DidChange(ref _factionH, last))
                        tiersChanged = true;
                    var facs = chunk.GetNativeArray(ref _factionH);
                    var tiers = chunk.GetNativeArray(ref _tierH);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        int idx = (int)facs[i].Value;
                        if (idx >= 0 && idx < FactionCount) byFaction[idx] = tiers[i];
                    }
                }
            }

            // Collect first, write after: the chunk arrays below are read-only
            // views and the writes go through the EntityManager.
            var work = new NativeList<Work>(Allocator.Temp);
            _units.CompleteDependency();
            using (var chunks = _units.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    bool hasOverride = chunk.Has(ref _overrideH);
                    if (!tiersChanged
                        && !chunk.DidOrderChange(last)
                        && !chunk.DidChange(ref _unitTagH, last)
                        && !chunk.DidChange(ref _factionH, last)
                        && !(hasOverride && chunk.DidChange(ref _overrideH, last)))
                        continue;

                    var ents = chunk.GetNativeArray(_entityH);
                    var tags = chunk.GetNativeArray(ref _unitTagH);
                    var facs = chunk.GetNativeArray(ref _factionH);
                    bool hasStamp = chunk.Has(ref _appliedH);
                    var stamps = hasStamp ? chunk.GetNativeArray(ref _appliedH) : default;
                    var overrides = hasOverride ? chunk.GetNativeArray(ref _overrideH) : default;

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        int facIdx = (int)facs[i].Value;
                        if (facIdx < 0 || facIdx >= FactionCount) continue;

                        // Per-unit override (spec §4.5: Glow weapon claim) wins
                        // over the faction-wide tier, but never downgrades: a
                        // higher researched tier still applies.
                        EquipmentTier facTier = byFaction[facIdx].Get(tags[i].Class);
                        EquipmentTier target = facTier;
                        if (hasOverride)
                        {
                            target = overrides[i].Value;
                            if ((byte)facTier > (byte)target) target = facTier;
                        }

                        EquipmentTier applied = hasStamp ? stamps[i].Value : EquipmentTier.Base;
                        if (target == applied) continue;
                        work.Add(new Work { E = ents[i], Applied = applied, Target = target, HasStamp = hasStamp });
                    }
                }
            }

            if (work.Length > 0)
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                for (int w = 0; w < work.Length; w++)
                {
                    var job = work[w];
                    ApplyTierDiff(em, job.E, job.Applied, job.Target);
                    if (job.HasStamp)
                        em.SetComponentData(job.E, new UnitEquipmentApplied { Value = job.Target });
                    else
                        ecb.AddComponent(job.E, new UnitEquipmentApplied { Value = job.Target });
                }
                ecb.Playback(em);
                ecb.Dispose();
            }

            work.Dispose();
            byFaction.Dispose();
        }

        private static void ApplyTierDiff(EntityManager em, Entity entity,
            EquipmentTier oldTier, EquipmentTier newTier)
        {
            float diff = EquipmentTierConfig.StatMultiplier(newTier)
                       / EquipmentTierConfig.StatMultiplier(oldTier);
            if (math.abs(diff - 1f) < 0.001f) return;

            if (em.HasComponent<Damage>(entity))
            {
                var d = em.GetComponentData<Damage>(entity);
                d.Value = (int)(d.Value * diff);
                em.SetComponentData(entity, d);
            }
            if (em.HasComponent<Defense>(entity))
            {
                var def = em.GetComponentData<Defense>(entity);
                def.Melee  = (int)(def.Melee  * diff);
                def.Ranged = (int)(def.Ranged * diff);
                def.Siege  = (int)(def.Siege  * diff);
                def.Magic  = (int)(def.Magic  * diff);
                em.SetComponentData(entity, def);
            }
        }
    }
}
