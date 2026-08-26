// SimpleAISystem.Targeting.cs
// Attack-target selection and curse-corridor pathing checks.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using UnityEngine;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        // ── Curse-aware corridor + placement checks (2026-08-04) ──
        /// <summary>Fraction of corridor samples that must be deep crust
        /// before a wave counts the route as blocked.</summary>
        private const float CurseCorridorHeavyFraction = 0.35f;
        private const float CurseCorridorSampleStep = 8f;

        private static bool TryGetVeilField(EntityManager em, out VeilField field)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<VeilField>());
            if (q.CalculateEntityCount() == 0) { field = default; return false; }
            field = q.GetSingleton<VeilField>();
            return field.Initialised != 0;
        }

        /// <summary>True when a substantial share of the straight line
        /// between the two points crosses deep crust — marching an army
        /// through it costs more HP than the fight at the end.</summary>
        private static bool CurseBlocksCorridor(EntityManager em, float3 a, float3 b)
        {
            if (!TryGetVeilField(em, out var field)) return false;
            float dist = math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
            int samples = math.max(2, (int)(dist / CurseCorridorSampleStep));
            int crusted = 0;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float x = math.lerp(a.x, b.x, t);
                float z = math.lerp(a.z, b.z, t);
                int cx = (int)math.floor((x - field.Origin.x) / field.CellSize);
                int cz = (int)math.floor((z - field.Origin.y) / field.CellSize);
                if (cx < 0 || cx >= field.Width || cz < 0 || cz >= field.Height) continue;
                if (field.Saturation[field.Index(cx, cz)] >= VeilField.CrustThreshold)
                    crusted++;
            }
            return crusted >= (samples + 1) * CurseCorridorHeavyFraction;
        }

        /// <summary>Nearest well still feeding the curse (Active, awake).</summary>
        private static Entity FindNearestActiveWell(EntityManager em, float3 origin, out float3 wellPos)
        {
            wellPos = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<BorderNodeState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var states = q.ToComponentDataArray<BorderNodeState>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].State != NodeState.Active) continue;
                if (em.HasComponent<NodeDormant>(ents[i])) continue;
                // WellDormant ≠ NodeDormant. NodeDormant is a DESTROYED well
                // lying inert; WellDormant is an unwoken one (canon §2.8) —
                // still Active, still full HP, but feeding nothing. This
                // method exists to find what is driving the crust, so an
                // unwoken well is the wrong answer: marching on it wastes the
                // squad and does not even wake it (only a verb channel does).
                // Skipping it lets the caller fall through to a SmallNode,
                // which in the early/mid game is the real source anyway.
                if (em.HasComponent<WellDormant>(ents[i])) continue;
                var p = xfs[i].Position;
                float dx = p.x - origin.x, dz = p.z - origin.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = ents[i]; wellPos = p; }
            }
            return best;
        }

        /// <summary>Whether this faction has completed the Feraldis age-up —
        /// the only culture allowed to attack wells.</summary>
        private static bool IsFeraldisCulture(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var progs = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction)
                    return progs[i].Culture == Cultures.Feraldis;
            return false;
        }

        /// <summary>Nearest live SmallNode — the curse anchor a non-Feraldis
        /// army CAN kill (wells are Feraldis-only targets).</summary>
        private static Entity FindNearestSmallNode(EntityManager em, float3 origin, out float3 pos)
        {
            pos = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SmallNodeTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                var p = xfs[i].Position;
                float dx = p.x - origin.x, dz = p.z - origin.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = ents[i]; pos = p; }
            }
            return best;
        }
        /// <summary>
        /// M2 target value assessment: pick the highest-scored candidate from
        /// the brain's EnemySightingRecord buffer. Honors the same fog rules
        /// as the legacy ladder (mobile targets need CURRENT visibility,
        /// statics only need revealed). Returns Entity.Null when the AI has
        /// no usable intel (caller falls back to the legacy ladder).
        /// </summary>
        private static Entity ChooseAttackTargetScored(
            EntityManager em, Entity brainEntity, Faction myFaction, float3 originPos,
            AISettingsSO settings, AISettingsSO.PersonalityBlock personality, float now,
            out float intelAge, out IntelCategory category, bool ecoOnly = false)
        {
            intelAge = 0f;
            category = IntelCategory.MilitaryUnit;
            if (!em.HasBuffer<EnemySightingRecord>(brainEntity)) return Entity.Null;
            var buffer = em.GetBuffer<EnemySightingRecord>(brainEntity);
            if (buffer.Length == 0) return Entity.Null;

            var fogMgr = FogOfWarManager.Instance;
            Entity best = Entity.Null;
            float bestScore = float.MinValue;
            for (int i = 0; i < buffer.Length; i++)
            {
                var rec = buffer[i];
                if (!em.Exists(rec.Enemy)) continue;
                if (em.HasComponent<UnderConstruction>(rec.Enemy)) continue;
                // Raid mode: economy targets only (miners + eco buildings).
                if (ecoOnly && rec.Category != IntelCategory.Miner
                            && rec.Category != IntelCategory.EcoBuilding) continue;

                bool mobile = rec.Category == IntelCategory.MilitaryUnit
                           || rec.Category == IntelCategory.Miner;
                if (fogMgr != null)
                {
                    Vector3 p = (Vector3)rec.Position;
                    bool seen = mobile
                        ? fogMgr.IsVisible(myFaction, p)
                        : fogMgr.IsRevealed(myFaction, p);
                    if (!seen) continue;
                }

                float score = TargetScorer.Score(em, settings, personality.riskMultiplier, originPos, rec, now);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = rec.Enemy;
                    intelAge = now - rec.LastSeenTime;
                    category = rec.Category;
                }
            }
            return best;
        }
        // (M6 retreat moved into UpdateMissions: per-mission centroid strength
        // checks replace the old global CheckRetreat, which yanked every army
        // home whenever any single one was outmatched.)

        // Scout movement moved to ScoutDirectorSystem (AI plan M3): zone-based
        // exploration with staleness/enemy-base priorities, recon requests
        // (scout-then-strike), threat-aware routing, and flee-when-hurt.

        /// <summary>
        /// Pick the closest enemy target by priority:
        /// Miners → GathererHuts → Veilstone hives → Veilstone sub-nodes → Halls.
        /// Distance is measured from <paramref name="originPos"/> (the AI's
        /// Hall) so the army marches toward the nearest enemy first.
        ///
        /// Border targets (BorderMainNodeTag, SmallNodeTag) live under
        /// Faction.Border — they pass the !=myFaction filter automatically and
        /// give the AI something to chew on even when no enemy player base
        /// has been scouted yet. Main nodes are higher priority than sub-nodes
        /// (killing a hive rolls back the border spread).
        ///
        /// Fog of war: AI must respect the same visibility rules the human
        /// player has. Miners are mobile and require *current* visibility
        /// (the AI can chase what its scouts / military see right now).
        /// Static targets (GHuts, Halls, Veilstone nodes) only need *revealed*
        /// visibility — once seen they're known targets (matches the "explored
        /// ghost" rule for buildings), so the AI can march toward a last-seen
        /// hive even after the scout moves on.
        /// </summary>
        private static Entity ChooseAttackTarget(EntityManager em, Faction myFaction, float3 originPos)
        {
            // 1. Visible enemy miners — most actionable raid target.
            Entity t = FindClosestEnemyOf<MinerTag>(em, myFaction, originPos, requireCurrentVisibility: true);
            if (t != Entity.Null) return t;
            // 2. Revealed enemy economy buildings.
            t = FindClosestEnemyOf<GathererHutTag>(em, myFaction, originPos, requireCurrentVisibility: false);
            if (t != Entity.Null) return t;
            // (Border wells REMOVED from the plain-army ladder, 2026-07-12.
            //  Wells are VERB objectives — the culture's ritualist (Scholar /
            //  Acolyte / Iconoclast) works them with the army as ESCORT,
            //  dispatched by the per-culture endgame system. Sending raw
            //  waves at a well just fed armies to the crystal spread.)
            // 3. Enemy Halls — finisher.
            return FindClosestEnemyOf<HallTag>(em, myFaction, originPos, requireCurrentVisibility: false);
        }

        private static Entity FindClosestEnemyOf<TTag>(
            EntityManager em, Faction myFaction, float3 originPos, bool requireCurrentVisibility)
            where TTag : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // FogOfWarManager may be null when fog is disabled (Observer mode,
            // or future modes). In that case treat everything as visible —
            // matches the human player's behaviour with fog off.
            var fogMgr = FogOfWarManager.Instance;

            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                // The AI never picks an ally as a target. docs/Design/Teams.md
                if (!Alliances.AreHostile(myFaction, facs[i].Value)) continue;
                // Skip targets still under construction (Halls only — others
                // wouldn't have UnderConstruction). Easier to detect by checking
                // the component than to add a separate query exclusion.
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;

                if (fogMgr != null)
                {
                    Vector3 pos = (Vector3)xfs[i].Position;
                    bool seen = requireCurrentVisibility
                        ? fogMgr.IsVisible(myFaction, pos)
                        : fogMgr.IsRevealed(myFaction, pos);
                    if (!seen) continue;
                }

                float dx = xfs[i].Position.x - originPos.x;
                float dz = xfs[i].Position.z - originPos.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }
    }
}
