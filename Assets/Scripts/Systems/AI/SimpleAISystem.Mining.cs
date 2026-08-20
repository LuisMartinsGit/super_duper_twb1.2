// SimpleAISystem.Mining.cs
// Miner tasking and deposit selection (iron / veilstone / veilsteel).
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
        /// <summary>Nearest live Sharp Crystals node — deliberately NOT
        /// home-tethered: the single 1500-unit deposit sits wherever the map
        /// put it, and a long walk beats zero mined veilsteel.</summary>
        private static Entity PickNearestVeilsteel(float3 from,
            NativeArray<Entity> ents, NativeArray<IronDepositState> states,
            NativeArray<LocalTransform> xfs)
        {
            Entity best = Entity.Null;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted != 0) continue;
                float dx = xfs[i].Position.x - from.x;
                float dz = xfs[i].Position.z - from.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; best = ents[i]; }
            }
            return best;
        }
        // ─────────────────────────────────────────────────────────────────
        // MINER TASKING
        // ─────────────────────────────────────────────────────────────────

        // Hard upper bound on the per-strategy SetVeilstoneTarget value. Bumped
        // to 16 so the runtime 50/50 floor (totalMiners / 2) isn't crushed by
        // an old clamp from when veilstone miners were treated as a niche.
        private const int MaxVeilstoneMiners = 16;

        /// <summary>Fixed crew on the veilsteel Sharp Crystals node once the
        /// faction has aged up — two miners saturate the single finite
        /// deposit (2026-08-11: it was never tasked at all).</summary>
        private const int VeilsteelMinerFlowTarget = 2;

        // Mining stays anchored to the base: deposits within this range of
        // the home building (Hall) are "home" deposits (miners pick the
        // nearest to themselves among them, so they spread). Only when NO home deposit
        // exists do miners take the deposit nearest to the HALL — never the
        // one nearest to the worker, which on lopsided maps marched the
        // whole workforce toward the enemy base on the first think tick.
        private const float HomeMiningRadius = 70f;

        // (Research ladder lives in EconomyResearchLadder — walked by the
        // always-on TickEconomy with continue-past-failure semantics.)

        /// <summary>Extended crust-dig reach used only when NO crust exists
        /// inside <see cref="HomeMiningRadius"/> — the veil seeds around the
        /// wells, which usually sit far from the AI base, and the old hard
        /// tether meant the AI never dug veilstone at all.</summary>
        private const float VeilReachFallback = 170f;

        /// <summary>
        /// Issue explicit GatherCommands to every idle AI miner. Iron and veilstone
        /// are separate flows: the AI counts current veilstone miners and, while
        /// under the effective target, sends new idle miners to outcroppings; the
        /// rest go to iron.
        ///
        /// Default effective target = <c>max(buildOrderTarget, totalMiners / 2)</c>.
        /// The build-order SetVeilstoneTarget normally acts as a FLOOR — strategies
        /// can front-load veilstone demand (e.g. TechBoom asking for 2 with only
        /// 4 miners) and the steady-state allocation is 50/50 because veilstone
        /// is just as important as iron for age-up + tech.
        ///
        /// EXCEPTION: military-rush strategies (Rush) treat their SetVeilstoneTarget
        /// as an explicit CAP, not a floor. The 50/50 floor would otherwise
        /// override Rush's `SetVeilstoneTarget(1)` (only "enough veilstone for
        /// Shrine + age-up") and starve early military production. (task-062 G-1)
        ///
        /// Auto-find is fully removed from MiningSystem and VeilstoneMiningSystem
        /// for AI factions — every miner movement is the result of a command
        /// issued here (or the LOS-based after-depletion routing inside the
        /// mining systems, which is intentional player UX).
        /// </summary>
        private static void AssignIdleMiners(EntityManager em, Faction faction, int targetVeilstone, AIStrategy strategy, double now)
        {
            // FERALDIS WORKERS CANNOT GATHER — the retrofit strips MinerTag
            // and MinerState at age-up, and their ore comes from Mines. This
            // pass was still handing them veilstone gather orders every tick,
            // which fought the endgame system's own orders: a worker would be
            // told to walk to an outcropping, then told to hold or conscript,
            // then re-issued the gather. The player-visible symptom was
            // workers twitching between "go mine veilstone" and "stay put".
            if (CultureConfig.GetCompletedCulture(em, faction) == Cultures.Feraldis) return;

            // Defensive clamp: SetVeilstoneTarget already clamps writes, but a
            // bootstrap that left VeilstoneMinerTarget at default still produces
            // a sane non-negative value here.
            targetVeilstone = math.clamp(targetVeilstone, 0, MaxVeilstoneMiners);
            // Find this faction's home anchor (Hall first, then any
            // GathererHut) — mining stays tethered to the base even though
            // resources are credited directly to the bank.
            Entity home = FindFactionBuilding<HallTag>(em, faction);
            if (home == Entity.Null)
                home = FindFactionBuilding<GathererHutTag>(em, faction);
            if (home == Entity.Null) return; // no base to anchor mining to
            if (!em.HasComponent<LocalTransform>(home)) return;
            float3 homePos = em.GetComponentData<LocalTransform>(home).Position;

            // Snapshot all non-depleted iron deposits and outcroppings. We do per-
            // miner nearest selection below so miners spread across multiple
            // deposits instead of all converging on one.
            var ironQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ironEnts = ironQuery.ToEntityArray(Allocator.Temp);
            using var ironStates = ironQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var ironTransforms = ironQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var outcroppingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<VeilstoneOutcroppingState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var outcroppingEnts = outcroppingQuery.ToEntityArray(Allocator.Temp);
            using var outcroppingStates = outcroppingQuery.ToComponentDataArray<VeilstoneOutcroppingState>(Allocator.Temp);
            using var outcroppingTransforms = outcroppingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // VEILSTEEL (2026-08-11): the Sharp Crystals node was never
            // tasked — the Smelter fleet was the only veilsteel source while
            // a 1500-unit deposit sat untouched on the map. Deposits reuse
            // IronDepositState (fixed amount, mined until gone).
            var veilsteelQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilsteelDepositTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var veilsteelEnts = veilsteelQuery.ToEntityArray(Allocator.Temp);
            using var veilsteelStates = veilsteelQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var veilsteelTransforms = veilsteelQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            bool anyVeilsteel = false;
            for (int i = 0; i < veilsteelStates.Length; i++)
                if (veilsteelStates[i].Depleted == 0) { anyVeilsteel = true; break; }

            // Veilsteel mining is aged-up behaviour — the node usually sits
            // far from the base, and an Age-0 economy has no veilsteel sink.
            bool agedUp = false;
            if (FactionEconomy.TryGetBank(em, faction, out var eraBank)
                && em.HasComponent<FactionEra>(eraBank))
                agedUp = em.GetComponentData<FactionEra>(eraBank).Value >= 2;

            bool anyIron = HasAnyIron(ironStates);
            bool anyVeilstoneOutcropping = HasAnyVeilstoneOutcropping(outcroppingStates);

            // THE VEIL (canon §2.3): on cursed maps there are no veilstone
            // deposit entities at all — the crust sheet is dug directly.
            // Veilstone is "available" when there is crust within the home
            // mining radius; diggers are tasked with GatherVeilCommand.
            bool hasVeilField = false;
            VeilField veilField = default;
            var veilFieldQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VeilField>());
            if (!veilFieldQuery.IsEmpty)
            {
                veilField = veilFieldQuery.GetSingleton<VeilField>();
                hasVeilField = veilField.Initialised == 1 && veilField.Saturation.IsCreated;
            }
            // Dig-the-sheet is retired with the wall model (§2.5b): the
            // walkable veil cannot be mined, and tasking GatherVeilCommand
            // marched AI workers into the crust where exposure killed them
            // (2026-08-03 playtest: 3-4 workers lost at game start). The
            // field stays sampled above — the outcropping pickers use it to
            // avoid hazed nodes.
            bool anyVeilCrust = TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                && hasVeilField && VeilMiningUtil.TryFindCrustVertexNear(
                in veilField, homePos, homePos, HomeMiningRadius, out _);

            // FAR-CRUST FALLBACK (2026-07-12): crust seeds around the WELLS,
            // which on most maps sit well beyond the 70 m home tether — the
            // probe above always failed and the AI never dug a single cell
            // ("AI is not pursuing veilstone"). When nothing is in home range,
            // extend the reach: the veil is the ONLY veilstone source on
            // cursed maps, so a longer walk beats zero income.
            float veilReach = HomeMiningRadius;
            if (TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                && hasVeilField && !anyVeilCrust && VeilMiningUtil.TryFindCrustVertexNear(
                    in veilField, homePos, homePos, VeilReachFallback, out _))
            {
                anyVeilCrust = true;
                veilReach = VeilReachFallback;
            }

            bool anyVeilSource = anyVeilstoneOutcropping || anyVeilCrust;
            if (!anyIron && !anyVeilSource && !anyVeilsteel) return;

            // Snapshot this faction's miners.
            var minerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<MinerState>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var minerEntities = minerQuery.ToEntityArray(Allocator.Temp);
            using var minerStates = minerQuery.ToComponentDataArray<MinerState>(Allocator.Temp);
            using var minerFactions = minerQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var minerTransforms = minerQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int totalMiners = 0;
            int crystalMiners = 0;
            int veilsteelMiners = 0;
            var idleMiners = new System.Collections.Generic.List<(Entity ent, float3 pos)>();

            for (int i = 0; i < minerEntities.Length; i++)
            {
                if (minerFactions[i].Value != faction) continue;
                totalMiners++;
                var ms = minerStates[i];
                if (ms.GatheringResource == 1) crystalMiners++;
                if (ms.GatheringResource == 2) veilsteelMiners++;
                // Idle = not currently moving/mining/returning AND not already
                // commanded (no GatherCommand pending). Skipping miners that
                // already hold a GatherCommand prevents reissuing every tick.
                // COMMAND FOLLOW-THROUGH: workers committed to construction or
                // repair (BuildCommand / BuildOrder / RepairOrder) are off
                // limits — re-tasking them to mine was clearing their build
                // command mid-walk, so AI foundations never got built.
                if (ms.State == MinerWorkState.Idle
                    && !em.HasComponent<GatherCommand>(minerEntities[i])
                    && !em.HasComponent<TheWaningBorder.Core.Commands.Types.GatherVeilCommand>(minerEntities[i])
                    && !em.HasComponent<UserMoveOrder>(minerEntities[i])
                    && !IsCommittedWorker(em, minerEntities[i]))
                    idleMiners.Add((minerEntities[i], minerTransforms[i].Position));
            }

            // 50/50 floor: at minimum, half the workforce should be on veilstone
            // when outcroppings are reachable. The build-order target only wins if
            // it asks for MORE veilstone than 50/50 (early front-loading). This
            // replaces the previous cap-driven allocation where the AI sat at
            // 1-3 veilstone miners regardless of army size and starved on veilstone.
            //
            // Rush keeps a LIGHTER floor, never a zero one (2026-08-04,
            // supersedes the task-062 G-1 full opt-out): progression
            // hard-gates on veilstone (70-veilstone choice building), and a
            // Rush AI observed with ZERO veilstone miners froze mid-order
            // with banked iron. 1 miner at 4+, 2 at 8+ — military-first
            // stays intact, starvation does not.
            if (anyVeilSource)
                targetVeilstone = strategy == AIStrategy.Rush
                    ? math.max(targetVeilstone, math.min(2, totalMiners / 4))
                    : math.max(targetVeilstone, totalMiners / 2);

            for (int i = 0; i < idleMiners.Count; i++)
            {
                var (miner, minerPos) = idleMiners[i];

                // VEILSTEEL first: keep the Sharp Crystals node worked with a
                // small fixed crew once aged up (the walk is long and the
                // node is finite — two miners saturate it).
                if (agedUp && anyVeilsteel && veilsteelMiners < VeilsteelMinerFlowTarget)
                {
                    Entity vsTarget = PickNearestVeilsteel(minerPos,
                        veilsteelEnts, veilsteelStates, veilsteelTransforms);
                    if (vsTarget != Entity.Null)
                    {
                        CommandRouter.IssueGather(em, miner, vsTarget, CommandSource.AI);
                        veilsteelMiners++;
                        continue;
                    }
                }

                // Prefer veilstone until the AI hits its target count, but only
                // if a source is actually available. Otherwise send to iron.
                bool wantVeilstone = crystalMiners < targetVeilstone && anyVeilSource;

                // Veilstone on cursed maps = dig the sheet directly: closest
                // crusted vertex to this worker (home-tethered).
                if (wantVeilstone && !anyVeilstoneOutcropping)
                {
                    if (anyVeilCrust && VeilMiningUtil.TryFindCrustVertexNear(
                            in veilField, minerPos, homePos, veilReach, out float3 digSite))
                    {
                        CommandRouter.IssueGatherVeil(em, miner, digSite, CommandSource.AI);
                        crystalMiners++;
                        continue;
                    }
                    wantVeilstone = false; // no crust reachable — fall to iron
                }

                Entity target = wantVeilstone
                    ? PickNearestVeilstoneOutcropping(minerPos, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now)
                    : PickNearestIron(minerPos, homePos, ironEnts, ironStates, ironTransforms, em, now);

                if (target == Entity.Null)
                {
                    // First-choice resource is gone (e.g. last outcropping depleted
                    // mid-loop). Try the other side once before giving up.
                    target = wantVeilstone
                        ? PickNearestIron(minerPos, homePos, ironEnts, ironStates, ironTransforms, em, now)
                        : PickNearestVeilstoneOutcropping(minerPos, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now);
                    if (target == Entity.Null) continue;
                    wantVeilstone = !wantVeilstone;
                }

                CommandRouter.IssueGather(em, miner, target, CommandSource.AI);
                if (wantVeilstone) crystalMiners++;
            }

            // VEILSTEEL ANTI-STAGNATION: the crew above is only ever staffed
            // from IDLE miners — and AI miners essentially never go idle, because
            // MiningSystem auto-chains them onto the next deposit the moment one
            // runs dry. So on any map where the workforce was already tasked by
            // the time the faction aged up, the Sharp Crystals node stayed at
            // ZERO miners for the rest of the match and the AI banked no mined
            // veilsteel at all. Same failure the veilstone block below exists to
            // fix, and the same one-worker-per-think-tick remedy.
            //
            // Drawn from IRON miners only: veilstone has its own target to hit,
            // and trading between those two pools would just churn both.
            if (agedUp && anyVeilsteel && veilsteelMiners < VeilsteelMinerFlowTarget)
            {
                Entity vsCandidate = Entity.Null;
                float3 vsCandidatePos = default;
                float vsBestDistSq = float.MaxValue;
                for (int i = 0; i < minerEntities.Length; i++)
                {
                    if (minerFactions[i].Value != faction) continue;
                    var ms = minerStates[i];
                    if (ms.GatheringResource != 0) continue;          // iron only
                    if (ms.State == MinerWorkState.Idle) continue;    // handled above
                    if (em.HasComponent<GatherCommand>(minerEntities[i])) continue;
                    if (em.HasComponent<UserMoveOrder>(minerEntities[i])) continue;
                    if (IsCommittedWorker(em, minerEntities[i])) continue;

                    Entity nearest = PickNearestVeilsteel(minerTransforms[i].Position,
                        veilsteelEnts, veilsteelStates, veilsteelTransforms);
                    if (nearest == Entity.Null) continue;

                    float3 np = em.GetComponentData<LocalTransform>(nearest).Position;
                    float dx = np.x - minerTransforms[i].Position.x;
                    float dz = np.z - minerTransforms[i].Position.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < vsBestDistSq)
                    {
                        vsBestDistSq = d2;
                        vsCandidate = minerEntities[i];
                        vsCandidatePos = minerTransforms[i].Position;
                    }
                }
                if (vsCandidate != Entity.Null)
                {
                    Entity vsTarget = PickNearestVeilsteel(vsCandidatePos,
                        veilsteelEnts, veilsteelStates, veilsteelTransforms);
                    if (vsTarget != Entity.Null)
                    {
                        CommandRouter.IssueGather(em, vsCandidate, vsTarget, CommandSource.AI);
                        veilsteelMiners++;
                    }
                }
            }

            // ANTI-STAGNATION rebalance: miners locked on iron never go idle
            // (they auto-chain to the next deposit), so a faction whose whole
            // workforce started on iron could NEVER reach its veilstone target —
            // it sat veilstone-starved forever and couldn't afford age-up.
            // Re-task ONE actively-iron-mining worker per think tick toward
            // veilstone until the target is met (one per tick = no churn).
            if (anyVeilSource && crystalMiners < targetVeilstone)
            {
                Entity candidate = Entity.Null;
                float3 candidatePos = default;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < minerEntities.Length; i++)
                {
                    if (minerFactions[i].Value != faction) continue;
                    var ms = minerStates[i];
                    // IRON only. This used to skip just GatheringResource == 1,
                    // so the veilsteel crew (2) counted as fair game and the
                    // rebalance poached the very miners the block above had
                    // just sent on the long walk to the Sharp Crystals node —
                    // they never arrived, veilsteelMiners fell back to 0, and
                    // the cycle repeated. Half of "the AI gets no veilsteel".
                    if (ms.GatheringResource != 0) continue;
                    if (ms.State == MinerWorkState.Idle) continue;    // idle ones were handled above
                    if (em.HasComponent<UserMoveOrder>(minerEntities[i])) continue;
                    if (IsCommittedWorker(em, minerEntities[i])) continue;

                    // Distance to the nearest veilstone source: deposit
                    // entity (curse-free maps) or diggable crust vertex.
                    float d2;
                    if (anyVeilstoneOutcropping)
                    {
                        Entity nearest = PickNearestVeilstoneOutcropping(minerTransforms[i].Position, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now);
                        if (nearest == Entity.Null) continue;
                        float dx = em.GetComponentData<LocalTransform>(nearest).Position.x - minerTransforms[i].Position.x;
                        float dz = em.GetComponentData<LocalTransform>(nearest).Position.z - minerTransforms[i].Position.z;
                        d2 = dx * dx + dz * dz;
                    }
                    else
                    {
                        if (!VeilMiningUtil.TryFindCrustVertexNear(in veilField,
                                minerTransforms[i].Position, homePos, HomeMiningRadius, out float3 v))
                            continue;
                        float dx = v.x - minerTransforms[i].Position.x;
                        float dz = v.z - minerTransforms[i].Position.z;
                        d2 = dx * dx + dz * dz;
                    }
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        candidate = minerEntities[i];
                        candidatePos = minerTransforms[i].Position;
                    }
                }
                if (candidate != Entity.Null)
                {
                    if (anyVeilstoneOutcropping)
                    {
                        Entity outcropping = PickNearestVeilstoneOutcropping(candidatePos, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now);
                        if (outcropping != Entity.Null)
                            CommandRouter.IssueGather(em, candidate, outcropping, CommandSource.AI);
                    }
                    // Dig-the-sheet only exists in the wall model (§2.5b) —
                    // never send workers into the walkable crust.
                    else if (TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                             && VeilMiningUtil.TryFindCrustVertexNear(in veilField,
                                 candidatePos, homePos, HomeMiningRadius, out float3 digSite))
                    {
                        CommandRouter.IssueGatherVeil(em, candidate, digSite, CommandSource.AI);
                    }
                }
            }
        }

        private static bool HasAnyIron(Unity.Collections.NativeArray<IronDepositState> states)
        {
            for (int i = 0; i < states.Length; i++)
                if (states[i].Depleted == 0 && states[i].RemainingIron > 0) return true;
            return false;
        }

        private static bool HasAnyVeilstoneOutcropping(Unity.Collections.NativeArray<VeilstoneOutcroppingState> states)
        {
            for (int i = 0; i < states.Length; i++)
                if (states[i].Depleted == 0 && states[i].RemainingVeilstone > 0) return true;
            return false;
        }

        // Home-anchored deposit picks (two passes):
        //   1. Nearest to the WORKER among deposits within HomeMiningRadius
        //      of the Hall — home deposits, workers spread across them.
        //   2. No home deposit at all → nearest to the HALL, so the whole
        //      workforce migrates to the closest outside cluster as a group
        //      instead of scattering toward whatever is nearest to each
        //      worker (which pointed straight at the enemy base on maps with
        //      lopsided resources).

        private static Entity PickNearestIron(float3 from, float3 home,
            Unity.Collections.NativeArray<Entity> ents,
            Unity.Collections.NativeArray<IronDepositState> states,
            Unity.Collections.NativeArray<LocalTransform> transforms,
            EntityManager em, double now)
        {
            float homeSq = HomeMiningRadius * HomeMiningRadius;
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingIron <= 0) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                if (hx * hx + hz * hz > homeSq) continue;
                float dx = transforms[i].Position.x - from.x;
                float dz = transforms[i].Position.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            if (best != Entity.Null) return best;

            // Fallback: nearest to home.
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingIron <= 0) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                float d = hx * hx + hz * hz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }

        /// <summary>True when the deposit was recently proven unreachable
        /// (StuckRedirectSystem's UnreachableMark still unexpired) — pickers
        /// must skip it or workers orbit the same blocked node forever.</summary>
        private static bool IsMarkedUnreachable(EntityManager em, Entity deposit, double now)
            => em.HasComponent<UnreachableMark>(deposit)
               && em.GetComponentData<UnreachableMark>(deposit).Until > now;

        private static Entity PickNearestVeilstoneOutcropping(float3 from, float3 home,
            Unity.Collections.NativeArray<Entity> ents,
            Unity.Collections.NativeArray<VeilstoneOutcroppingState> states,
            Unity.Collections.NativeArray<LocalTransform> transforms,
            in VeilField veilField, bool hasVeilField,
            EntityManager em, double now)
        {
            float homeSq = HomeMiningRadius * HomeMiningRadius;
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingVeilstone <= 0) continue;
                // Never auto-assign onto cursed ground (§2.5b loop damping) —
                // a hazed node costs exposure; only an explicit player order
                // may take that trade.
                if (hasVeilField && veilField.SaturationAt(transforms[i].Position)
                    >= VeilField.CrustThreshold) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                if (hx * hx + hz * hz > homeSq) continue;
                float dx = transforms[i].Position.x - from.x;
                float dz = transforms[i].Position.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            if (best != Entity.Null) return best;

            // Fallback: nearest to home.
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingVeilstone <= 0) continue;
                if (hasVeilField && veilField.SaturationAt(transforms[i].Position)
                    >= VeilField.CrustThreshold) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                float d = hx * hx + hz * hz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }
    }
}
