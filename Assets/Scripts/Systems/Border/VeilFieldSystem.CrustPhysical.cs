// VeilFieldSystem.CrustPhysical.cs
// PHYSICAL-CRUST MODEL -- gated off by VeilCrustConstants.CrustPhysical (false).
// Unreachable today and kept deliberately as the flip-back design: debuffs,
// worker ward, miner infection and iron swallowing. Quarantined here so the
// live veil code stays readable. Do not delete without retiring the flag.
// Partial of VeilFieldSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Influence;
using TheWaningBorder.Systems.Border.Jobs;
using TheWaningBorder.World.Terrain;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border
{
    public partial class VeilFieldSystem : SystemBase
    {
        // ─────────────────────────────────────────────────────────────
        // CRUST DEBUFFS (speed + stats; NO damage over time)
        // ─────────────────────────────────────────────────────────────

        private void ApplyCrustDebuffs(EntityManager em, in VeilField field, double matchTime)
        {
            var uq = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var ents = uq.ToEntityArray(Allocator.Temp);
            using var tags = uq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = uq.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == Faction.Border) continue; // the curse is home here
                if (hps[i].Value <= 0) continue; // dying — DeathSystem owns it now

                byte sat = field.SaturationAt(xfs[i].Position);
                bool onCrust = sat >= VeilField.CrustThreshold;
                bool hasVeilTag = em.HasComponent<VeilDebuffTag>(ents[i]);

                // CATCH-CONVERSION (2026-07-12): the crust is impassable, so a
                // unit standing ON crust means the wall grew over it. Workers
                // are warded (growth can't reach them) and stay on the debuff
                // path below as a fallback; every OTHER unit caught by the
                // wall is consumed and erupts as a curse creature — the tier
                // scaling with the match clock, same ladder as infection.
                //
                // The kill goes through the NORMAL death pipeline: Health -> 0,
                // DeathSystem destroys next update. NEVER DestroyEntity here —
                // other systems (integrator / targeting / death) queue ECB ops
                // against this entity in the same frame, and a synchronous
                // destroy makes the EndSimulation playback throw "entity does
                // not exist" and corrupts the whole world (2026-07-12 crash).
                var cls = tags[i].Class;
                if (onCrust && cls != UnitClass.Economy && cls != UnitClass.Miner)
                {
                    var hp = hps[i];
                    hp.Value = 0;
                    em.SetComponentData(ents[i], hp);
                    SpawnCurseCreature(em, xfs[i].Position, matchTime);
                    continue;
                }

                if (onCrust)
                {
                    var debuff = sat >= VeilField.DeepThreshold ? DeepDebuff : CrustDebuff;
                    if (em.HasComponent<BorderDebuff>(ents[i]))
                        em.SetComponentData(ents[i], debuff);
                    else
                        em.AddComponentData(ents[i], debuff);
                    if (!hasVeilTag) em.AddComponent<VeilDebuffTag>(ents[i]);
                }
                else if (hasVeilTag)
                {
                    em.RemoveComponent<VeilDebuffTag>(ents[i]);
                    if (em.HasComponent<BorderDebuff>(ents[i]))
                        em.RemoveComponent<BorderDebuff>(ents[i]);
                }
            }
        }

        /// <summary>Stamp a small "no growth" disc around every worker
        /// (MinerTag — the unified Worker carries it from both factories) so a
        /// burst can never crystallize the ground under a digger and seal it
        /// inside the wall. Cleared and re-stamped from live positions before
        /// every CA step. Military units get NO ward — the wall catches them
        /// (catch-conversion in ApplyCrustDebuffs).</summary>
        private void SampleWorkerWard(EntityManager em, in VeilField field)
        {
            for (int i = 0; i < _workerWard.Length; i++) _workerWard[i] = 0;

            var wq = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = wq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int r = (int)math.ceil(WorkerWardRadius / field.CellSize);
            float r2 = WorkerWardRadius * WorkerWardRadius;
            for (int i = 0; i < xfs.Length; i++)
            {
                field.TryWorldToCell(xfs[i].Position, out int cx, out int cz);
                for (int z = math.max(0, cz - r); z <= math.min(field.Height - 1, cz + r); z++)
                    for (int x = math.max(0, cx - r); x <= math.min(field.Width - 1, cx + r); x++)
                    {
                        float dx = (x - cx) * field.CellSize;
                        float dz = (z - cz) * field.CellSize;
                        if (dx * dx + dz * dz > r2) continue;
                        _workerWard[field.Index(x, z)] = 1;
                    }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // MINER INFECTION  (neglected miners near the veil turn to curse)
        // ─────────────────────────────────────────────────────────────

        /// <summary>Accrue veil exposure on miners standing in haze; when a miner
        /// crosses <see cref="InfectionSeconds"/> it is consumed and a hostile
        /// curse creature erupts in its place. The creature tier scales with how
        /// late the eruption is (Crystalling → Veilstinger → Godsplinter), so a
        /// map left to rot spawns progressively worse things. This is the ONLY
        /// source of curse creatures now the curse is a force, not a faction —
        /// so it is NOT gated by BorderConstants.CurseFieldsArmies.</summary>
        private void ProcessInfection(EntityManager em, in VeilField field, double matchTime)
        {
            var mq = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = mq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = mq.ToComponentDataArray<Health>(Allocator.Temp);

            // Eruptions collected during the scan, spawned after it. The miner
            // itself is killed through the NORMAL death pipeline (Health -> 0,
            // DeathSystem destroys next update) — NEVER DestroyEntity here:
            // other systems queue ECB ops against the entity in the same
            // frame, and a synchronous destroy makes the EndSimulation
            // playback throw "entity does not exist" and corrupts the world
            // (2026-07-12 crash).
            var erupt = new NativeList<float3>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == Faction.Border) continue; // curse things don't infect
                if (hps[i].Value <= 0) continue; // dying — DeathSystem owns it now

                byte sat = field.SaturationAt(xfs[i].Position);
                bool near = sat >= InfectionNearThreshold;

                float prog = em.HasComponent<InfectionState>(ents[i])
                    ? em.GetComponentData<InfectionState>(ents[i]).Progress : 0f;
                prog += near ? DebuffInterval : -DebuffInterval * InfectionRecoverMul;
                prog = math.clamp(prog, 0f, InfectionSeconds);

                if (prog >= InfectionSeconds)
                {
                    erupt.Add(xfs[i].Position);
                    var hp = hps[i];
                    hp.Value = 0;
                    em.SetComponentData(ents[i], hp);
                    continue;
                }

                if (em.HasComponent<InfectionState>(ents[i]))
                    em.SetComponentData(ents[i], new InfectionState { Progress = prog });
                else if (prog > 0f)
                    em.AddComponentData(ents[i], new InfectionState { Progress = prog });
            }

            for (int i = 0; i < erupt.Length; i++)
                SpawnCurseCreature(em, erupt[i], matchTime);
            erupt.Dispose();
        }

        /// <summary>Erupt the tier appropriate to the match clock.</summary>
        private static void SpawnCurseCreature(EntityManager em, float3 pos, double matchTime)
        {
            if (matchTime < InfectionEarlyMaxSeconds)
                Crystalling.Create(em, pos, Faction.Border);
            else if (matchTime < InfectionMidMaxSeconds)
                Veilstinger.Create(em, pos, Faction.Border);
            else
                Godsplinter.Create(em, pos, Faction.Border);
        }

        // ─────────────────────────────────────────────────────────────
        // IRON SWALLOWING
        // ─────────────────────────────────────────────────────────────

        private void SwallowIron(EntityManager em, in VeilField field)
        {
            foreach (var (state, xf) in SystemAPI
                .Query<RefRW<IronDepositState>, RefRO<LocalTransform>>()
                .WithAll<IronMineTag>())
            {
                if (state.ValueRO.RemainingIron <= 0) continue; // truly empty stays depleted
                bool swallowed = field.SaturationAt(xf.ValueRO.Position) >= VeilField.DeepThreshold;
                if (swallowed && state.ValueRO.Depleted == 0)
                    state.ValueRW.Depleted = 1;
                else if (!swallowed && state.ValueRO.Depleted == 1)
                    state.ValueRW.Depleted = 0; // ground reclaimed — the mine breathes again
            }
        }
    }
}
