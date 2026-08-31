// IntelSystem.cs
// Perception backbone of the full-scale AI (docs/AI_Assessment_and_Plan.md M1).
//
// Every tick (1 s), for each AIBrain faction:
//   1. Prune sightings whose entity no longer exists.
//   2. For every enemy entity currently inside this faction's fog-of-war
//      VISIBLE grid, upsert an EnemySightingRecord (last-known position, time,
//      category, heuristic strength) on the brain entity.
//   3. Stamp the faction's ThreatMap from (a) known enemy military presence and
//      (b) own units that are damaged and carry a live LastAttackerEntity —
//      a combat signal that needs no combat-system edits.
//   4. Decay all threat grids once.
//
// Managed dictionaries are LOOKUP-ONLY (see BorderArmyAISystem determinism note).
// Runs only where AI brains exist (host in multiplayer), so its managed state
// never has to replicate; everything it influences flows out as commands.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.World.FogOfWar;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class IntelSystem : SystemBase
    {
        private const float TickInterval = 1f;
        private SimCadence.Periodic _acc;

        // Damage signal stamped per damaged own-unit per tick.
        private const int DamageThreatStamp = 40;

        protected override void OnCreate()
        {
            RequireForUpdate<AIBrain>();
            ThreatMaps.ResetAll();
        }

        protected override void OnUpdate()
        {
            // Host-only: this feeds the AI brains and nothing else, and those
            // run on the host alone in multiplayer. docs/Multiplayer_LAN_Readiness.md
            if (!GameSettings.ShouldRunAIBrains()) return;

            if (!_acc.Due(SystemAPI.Time.DeltaTime, TickInterval)) return;

            var em = EntityManager;
            float now = (float)SystemAPI.Time.ElapsedTime;
            var fog = FogOfWarManager.Instance;

            ThreatMaps.DecayAll();

            // World snapshot: everything that could be seen or stamped.
            var worldQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var ents = worldQuery.ToEntityArray(Allocator.Temp);
            using var facs = worldQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = worldQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = worldQuery.ToComponentDataArray<Health>(Allocator.Temp);

            var brainsQuery = SystemAPI.QueryBuilder().WithAll<AIBrain>().Build();
            using var brains = brainsQuery.ToEntityArray(Allocator.Temp);

            foreach (var brainEntity in brains)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;
                Faction owner = brain.Owner;

                if (!em.HasBuffer<EnemySightingRecord>(brainEntity))
                    em.AddBuffer<EnemySightingRecord>(brainEntity);
                var buffer = em.GetBuffer<EnemySightingRecord>(brainEntity);

                // 1. Prune dead entities (preserve order for determinism).
                for (int i = buffer.Length - 1; i >= 0; i--)
                    if (!em.Exists(buffer[i].Enemy))
                        buffer.RemoveAt(i);

                // Lookup-only index of existing records by enemy entity.
                var index = new Dictionary<Entity, int>(buffer.Length);
                for (int i = 0; i < buffer.Length; i++)
                    index[buffer[i].Enemy] = i;

                // 2 + 3a. Upsert visible enemies; stamp military presence.
                for (int j = 0; j < ents.Length; j++)
                {
                    if (hps[j].Value <= 0) continue;
                    var pos = xfs[j].Position;

                    // Allies are not intel targets. Treated exactly like own
                    // units: an ally under attack still marks a threat at its
                    // position (the fight is on your side of the line), but it
                    // is never recorded as an enemy sighting and never stamped
                    // as enemy strength. docs/Design/Teams.md
                    if (!Alliances.AreHostile(owner, facs[j].Value))
                    {
                        // 3b. Own/allied damaged unit under attack -> threat at its position.
                        if (hps[j].Value < hps[j].Max
                            && em.HasComponent<LastAttackerEntity>(ents[j])
                            && em.GetComponentData<LastAttackerEntity>(ents[j]).Value != Entity.Null)
                        {
                            ThreatMaps.Stamp(owner, pos, DamageThreatStamp);
                        }
                        continue;
                    }

                    if (fog != null && !fog.IsVisible(owner, (UnityEngine.Vector3)pos)) continue;

                    Classify(em, ents[j], out IntelCategory cat, out bool isMilitary);
                    int strength = TacticalQuery.UnitStrength(em, ents[j]);
                    // A BUILDING'S TALLY IS ITS GARRISON (2026-08-31: scouts
                    // register value AND strength). The building's own HP is
                    // not the question a raid asks — "is it stray" is, so the
                    // record carries the sighted faction's combat strength
                    // standing around it at the moment it was seen.
                    if (cat == IntelCategory.Hall
                        || cat == IntelCategory.MilitaryBuilding
                        || cat == IntelCategory.EcoBuilding)
                        strength = TacticalQuery.FactionStrengthInRadius(
                            em, facs[j].Value, pos, 24f);

                    var rec = new EnemySightingRecord
                    {
                        Enemy = ents[j],
                        OwnerFaction = facs[j].Value,
                        Position = pos,
                        LastSeenTime = now,
                        EstStrength = strength,
                        Category = cat,
                    };
                    if (index.TryGetValue(ents[j], out int at)) buffer[at] = rec;
                    else { index[ents[j]] = buffer.Length; buffer.Add(rec); }

                    if (isMilitary)
                        ThreatMaps.Stamp(owner, pos, strength);
                }

                // 4. Aggregate shared knowledge from the sighting buffer —
                // consumed by the Alanthor endgame Reveal targeting and any
                // future strategic reads. Fog-honest by construction (the
                // buffer only ever holds what this faction has seen). This
                // write was missing since the legacy manager systems were
                // deleted, leaving EnemyLastKnownPosition permanently stale.
                if (em.HasComponent<AISharedKnowledge>(brainEntity))
                {
                    var shared = em.GetComponentData<AISharedKnowledge>(brainEntity);
                    float latest = float.MinValue;
                    Unity.Mathematics.float3 latestPos = shared.EnemyLastKnownPosition;
                    int enemyStr = 0, bases = 0;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        var rec = buffer[i];
                        if (rec.LastSeenTime > latest)
                        {
                            latest = rec.LastSeenTime;
                            latestPos = rec.Position;
                        }
                        if (rec.Category == IntelCategory.Hall) bases++;
                        if (rec.Category == IntelCategory.MilitaryUnit
                            && now - rec.LastSeenTime < 90f)
                            enemyStr += rec.EstStrength;
                    }
                    if (latest > float.MinValue)
                    {
                        shared.EnemyLastKnownPosition = latestPos;
                        shared.EnemyLastSeenTime = latest;
                    }
                    shared.EnemyEstimatedStrength = enemyStr;
                    shared.KnownEnemyBases = bases;
                    em.SetComponentData(brainEntity, shared);
                }
            }
        }

        private static void Classify(EntityManager em, Entity e, out IntelCategory cat, out bool isMilitary)
        {
            // Feraldis Plunderers are NOT an army. They are free, 45 HP,
            // 3-damage tax collectors that stream out of Raider Camps
            // forever. Counting them as military made a raiding Feraldis
            // look like a doom-stack to every opponent's threat assessment,
            // and made its OWN army-strength reads meaningless.
            if (em.HasComponent<PlundererTag>(e)) { cat = IntelCategory.Miner; isMilitary = false; return; }
            if (em.HasComponent<HallTag>(e)) { cat = IntelCategory.Hall; isMilitary = false; return; }
            if (em.HasComponent<BorderMainNodeTag>(e) || em.HasComponent<SmallNodeTag>(e))
            { cat = IntelCategory.BorderNode; isMilitary = false; return; }
            if (em.HasComponent<MinerTag>(e)) { cat = IntelCategory.Miner; isMilitary = false; return; }
            if (em.HasComponent<BuildingTag>(e))
            {
                bool mil = em.HasComponent<BarracksTag>(e) || em.HasComponent<ArcheryRangeTag>(e);
                cat = mil ? IntelCategory.MilitaryBuilding : IntelCategory.EcoBuilding;
                isMilitary = mil;
                return;
            }
            cat = IntelCategory.MilitaryUnit;
            // Veilstone border units count as military pressure too.
            isMilitary = true;
        }
    }
}
