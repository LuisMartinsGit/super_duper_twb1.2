// AIAlanthorEndgameSystem.cs
// Culture-specific endgame AI for Alanthor factions. Picks up after the
// SimpleAISystem build order finishes (Age 2+) and drives the late-game
// behaviour Alanthor should ship with: defensive tower clusters, sect
// adoption (Fortitude / Renewal cluster), and siege production.
//
// Scope: ADDITIVE. Doesn't replace SimpleAISystem, AIBuildingManager,
// or AIMilitaryManager — sits alongside and queues into the same
// BuildRequest / RecruitmentRequest buffers. Only fires for AIs whose
// faction has Cultures.Alanthor on its Hall.
//
// Tick rate: 5 seconds (slow loop — strategic decisions, not micro).
//
// Phases:
//   1. Mark HasAgedUp on AIStrategyState the first frame era >= 2 is
//      observed on the Hall. Lets future systems (e.g. evaluators) gate
//      on it without each one scanning the Hall.
//   2. Sect adoption: when a Temple of Ridan is built and RP / supplies /
//      crystal can afford a chapel, queue an adoption via SectAdoption
//      .TryStartAdoption. Picks Alanthor-cluster sects in priority order
//      (Fortitude first — wall HP fits the defensive playstyle).
//   3. Defensive tower spam: late-game (>5 min) build extra Alanthor_Towers
//      around the Hall up to a tunable cap.
//   4. Siege production: when an Alanthor_SiegeYard exists, push Ballista
//      RecruitmentRequests. Strategy assumption: Alanthor's late-game
//      pressure comes from siege + defensive cavalry, not infantry waves.
//
// Location: Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        // Tick interval — slow strategic loop.
        private const float ThinkInterval = 5f;

        // Late-game tower cap — beyond this, the AI stops spamming towers.
        private const int LateGameTowerCap = 4;

        // When elapsed gameTime exceeds this, late-game behaviours kick in.
        private const float LateGameStart = 300f; // 5 min

        // Material cost the AI keeps in reserve before queuing chapel
        // adoption (so adoption doesn't bankrupt the economy).
        private const int ChapelReserveSupplies = 100;
        private const int ChapelReserveCrystal  = 40;

        // Alanthor-cluster sect priority (best-first). Fortitude buffs walls
        // and towers — fits defensive theme. Renewal auto-repairs buildings.
        // Antiquity gives per-class kill bonuses (works well with mixed army).
        // Reclamation is the last pick; it shines vs Crystal-Curse PvE which
        // the AI doesn't deeply engage.
        private static readonly string[] AlanthorSectPriority =
        {
            SectConfig.Fortitude,
            SectConfig.Renewal,
            SectConfig.Antiquity,
            SectConfig.Reclamation,
        };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            foreach (var (brain, entity) in SystemAPI
                .Query<RefRO<AIBrain>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                Faction faction = brain.ValueRO.Owner;

                // Throttle: 5 s tick. Use a per-brain tick timer if available;
                // fall back to a global modulo on time.
                if (em.HasComponent<AIAlanthorTickState>(entity))
                {
                    var tick = em.GetComponentData<AIAlanthorTickState>(entity);
                    if (time < tick.NextThinkTime) continue;
                    tick.NextThinkTime = time + ThinkInterval;
                    em.SetComponentData(entity, tick);
                }
                else
                {
                    em.AddComponentData(entity, new AIAlanthorTickState
                    {
                        NextThinkTime = time + ThinkInterval,
                    });
                    continue; // skip first tick after stamp
                }

                // Find this faction's Hall and read culture/era.
                bool hasHall = false;
                byte culture = Cultures.None;
                int era = 1;
                float3 hallPos = float3.zero;
                foreach (var (fTag, progress, lt) in SystemAPI
                    .Query<RefRO<FactionTag>, RefRO<FactionProgress>, RefRO<LocalTransform>>()
                    .WithAll<HallTag>())
                {
                    if (fTag.ValueRO.Value != faction) continue;
                    culture = progress.ValueRO.Culture;
                    hallPos = lt.ValueRO.Position;
                    hasHall = true;
                    break;
                }
                if (!hasHall) continue;
                if (FactionEconomy.TryGetBank(em, faction, out var bank)
                    && em.HasComponent<FactionEra>(bank))
                    era = em.GetComponentData<FactionEra>(bank).Value;

                if (culture != Cultures.Alanthor) continue;
                if (era < 2) continue;

                // ─── 1. HasAgedUp latch ────────────────────────────────
                if (em.HasComponent<AIStrategyState>(entity))
                {
                    var ss = em.GetComponentData<AIStrategyState>(entity);
                    if (ss.HasAgedUp == 0)
                    {
                        ss.HasAgedUp = 1;
                        em.SetComponentData(entity, ss);
                        AILogger.Log(faction, "STRATEGY",
                            "Alanthor: aged up to era 2+ — endgame system engaged");
                    }
                }

                // ─── 2. Sect adoption ─────────────────────────────────
                TryAdoptNextSect(ref state, faction, em);

                // ─── 3. Late-game defensive towers ─────────────────────
                if (time > LateGameStart && em.HasBuffer<BuildRequest>(entity))
                {
                    var buildReqs = em.GetBuffer<BuildRequest>(entity);
                    TryQueueDefensiveTower(ref state, faction, buildReqs, hallPos);
                }

                // ─── 4. Siege production ───────────────────────────────
                if (em.HasBuffer<RecruitmentRequest>(entity))
                {
                    var recruitReqs = em.GetBuffer<RecruitmentRequest>(entity);
                    TryQueueSiege(ref state, faction, recruitReqs, entity);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // 2. SECT ADOPTION
        // ──────────────────────────────────────────────────────────────────

        private static void TryAdoptNextSect(ref SystemState state, Faction faction, EntityManager em)
        {
            // Need a Temple of Ridan to host chapels.
            Entity temple = Entity.Null;
            foreach (var (fTag, _, e) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<TempleOfRidanTag>>()
                .WithEntityAccess())
            {
                if (fTag.ValueRO.Value != faction) continue;
                temple = e;
                break;
            }
            if (temple == Entity.Null) return;

            // Find the next un-adopted priority sect we can afford.
            for (int i = 0; i < AlanthorSectPriority.Length; i++)
            {
                string sectId = AlanthorSectPriority[i];
                if (SectQuery.IsAdopted(em, faction, sectId)) continue;

                if (!TheWaningBorder.Data.BuildCosts.TryGet(
                    SectConfig.ChapelIdFor(sectId), out var chapelCost)) continue;

                // Reserve buffer — don't bankrupt the economy.
                if (FactionEconomy.TryGetResources(em, faction, out var res))
                {
                    if (res.Supplies < chapelCost.Supplies + ChapelReserveSupplies) return;
                    if (res.Crystal  < chapelCost.Crystal  + ChapelReserveCrystal)  return;
                }
                else return;

                var result = SectAdoption.TryStartAdoption(em, faction, sectId, chapelCost, temple);
                if (result == SectAdoptionResult.Ok)
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: adopting sect {sectId.Substring(5)}");
                    // Find the first free slot and queue the chapel build —
                    // mirrors what ReligionHUD does on the player side.
                    if (em.HasBuffer<TempleChapelSlot>(temple))
                    {
                        var slots = em.GetBuffer<TempleChapelSlot>(temple);
                        for (int s = 0; s < slots.Length; s++)
                        {
                            if (slots[s].State != 0) continue;
                            slots[s] = new TempleChapelSlot
                            {
                                Chapel        = Entity.Null,
                                SectId        = new FixedString64Bytes(sectId),
                                State         = 1,
                                BuildProgress = 0f,
                                BuildTime     = 30f,
                            };
                            break;
                        }
                    }
                    return; // one adoption per tick
                }
                if (result == SectAdoptionResult.NotEnoughRP) return; // wait for RP
                // For other failure modes (slot full, already adopted), try next priority.
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // 3. DEFENSIVE TOWER SPAM
        // ──────────────────────────────────────────────────────────────────

        private static void TryQueueDefensiveTower(ref SystemState state, Faction faction,
            DynamicBuffer<BuildRequest> buildReqs, float3 hallPos)
        {
            const string towerId = "Alanthor_Tower";
            int existing = CountBuildings(ref state, faction, towerId);
            if (existing >= LateGameTowerCap) return;

            // Already pending?
            for (int i = 0; i < buildReqs.Length; i++)
            {
                if (buildReqs[i].BuildingType.Equals(towerId) && buildReqs[i].Assigned == 0) return;
            }

            // Affordability via TechTreeDB.
            var typeFixed = new FixedString64Bytes(towerId);
            if (!TheWaningBorder.Data.BuildCosts.TryGet(towerId, out var cost)) return;
            if (!FactionEconomy.CanAfford(state.EntityManager, faction, cost)) return;

            // Place ~25-35 m from Hall in a random direction.
            uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000 + (int)faction * 71 + existing * 23);
            if (seed == 0) seed = 1;
            var random = new Unity.Mathematics.Random(seed);
            float angle = random.NextFloat(0, math.PI * 2);
            float distance = random.NextFloat(25f, 35f);
            float3 buildPos = hallPos + new float3(
                math.cos(angle) * distance, 0, math.sin(angle) * distance);

            buildReqs.Add(new BuildRequest
            {
                BuildingType = typeFixed,
                DesiredPosition = buildPos,
                Priority = 4, // below culture rebuilds (5/7), above nothing
                Assigned = 0,
                AssignedBuilder = Entity.Null,
            });
            AILogger.Log(faction, "BUILDING",
                $"Alanthor late-game: queued defensive tower {existing + 1}/{LateGameTowerCap}");
        }

        // ──────────────────────────────────────────────────────────────────
        // 4. SIEGE PRODUCTION
        // ──────────────────────────────────────────────────────────────────

        private static void TryQueueSiege(ref SystemState state, Faction faction,
            DynamicBuffer<RecruitmentRequest> recruitReqs, Entity brainEntity)
        {
            // Need a SiegeYard built.
            int siegeYards = CountBuildings(ref state, faction, "Alanthor_SiegeYard");
            if (siegeYards == 0) return;

            // Don't double-queue siege requests.
            for (int i = 0; i < recruitReqs.Length; i++)
            {
                if (recruitReqs[i].UnitType == UnitClass.Siege) return;
            }

            recruitReqs.Add(new RecruitmentRequest
            {
                UnitType = UnitClass.Siege,
                Quantity = 1,
                Priority = 6, // above default infantry (5)
                RequestingManager = brainEntity,
            });
            AILogger.Log(faction, "MILITARY",
                "Alanthor late-game: requested siege unit");
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static int CountBuildings(ref SystemState state, Faction faction, string buildingId)
        {
            var em = state.EntityManager;
            int count = 0;
            int pid = TheWaningBorder.Entities.BuildingFactory.GetPresentationId(buildingId);
            foreach (var (fTag, presId) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<PresentationId>>()
                .WithAll<BuildingTag>())
            {
                if (fTag.ValueRO.Value == faction && presId.ValueRO.Id == pid) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Per-AIBrain tick state for the Alanthor endgame loop. Lazy-stamped
    /// the first time AIAlanthorEndgameSystem inspects a brain.
    /// </summary>
    public struct AIAlanthorTickState : IComponentData
    {
        public float NextThinkTime;
    }
}
