// AIAlanthorEndgameSystem.Walls.cs
// Wall doctrine: plan execution, hub placement, gate and tower conversion.
// Partial of AIAlanthorEndgameSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_WallHubTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<WallHubTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            // The doctrine builds the STONE wall; a palisade the faction
            // raised in Age 0 is a different building and never part of it.
            ComponentType.Exclude<PalisadeTag>(),
        };
        static CachedEntityQuery QC_WallHubTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_WallInstanceTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<WallInstanceTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<PalisadeTag>(),
        };
        static CachedEntityQuery QC_WallInstanceTagFactionTagLocalTransform;

        #endregion

        // ──────────────────────────────────────────────────────────────────
        // 6b. WALL DOCTRINE (terrain-aware: seal chokepoints, else enclose
        //     the base in a large square-ish perimeter)
        // ──────────────────────────────────────────────────────────────────
        //
        // The doctrine follows the player thought process: "Am I sheltered
        // by terrain? Does ingress mean going through chokepoints? If yes,
        // wall off and fortify the chokepoints. If not, wall a LARGE
        // square-ish area around what's important and push from there."
        //
        // AIWallPlanner runs the terrain-only shelter scan ONCE when the
        // doctrine first ticks and freezes the resulting plan (mode + hub
        // slot list with gate/tower flags) on the brain entity. Every think
        // tick afterwards executes one action from the plan:
        //   1. place the next missing hub (linking it to in-range friendly
        //      hubs — that stitching closes lines and perimeter loops);
        //   2. convert a finished gate-flagged segment to a Gate
        //      (gates auto-open for friendlies, so the enclosure never
        //      walls in the AI's own army; perimeter gates sit at the four
        //      side midpoints — one facing each cardinal direction);
        //   3. convert the wall instance at a tower-flagged slot (corners,
        //      line ends, gate shoulders) to a Wall Tower.
        //
        // Wall placement has no lockstep command yet — the player panel also
        // places hubs/segments with direct EM calls, so the AI mirrors that
        // (parity; multiplayer wall replication is future work). Gate
        // conversion rides the replicating CommandRouter entry point; tower
        // conversion mirrors ActionsPanelBinder's direct-EM path.




        /// <summary>Longest gap the gap-closing pass will span with a single
        /// segment. Two plan spacings plus tolerance — enough to bridge one
        /// dead slot, short of stitching a wall across open map when a whole
        /// run failed.</summary>
        private static float WallMaxGapSpan => AIWallPlanner.HubSpacing * 2f + 8f;

        /// <summary>Link radius for stitching a fresh hub to its plan
        /// neighbours — covers the plan's 30 m spacing plus nudge tolerance.
        /// Segments span any length (CreateSegment tiles 3 m modules); the
        /// 16 m WallAutoSegmentSystem constant is that DISABLED system's
        /// auto-link rule, not a segment limit, so it does not bound this.
        /// Kept under 2x HubSpacing so the wall never links across a dead
        /// slot's hole (that hole is deliberate — usually a mountain).</summary>
        private static float WallLinkRadius => AIWallPlanner.HubSpacing + 3f;

        private static void TryBuildWallDefenses(Faction faction, EntityManager em,
            Entity brainEntity, float3 hallPos)
        {
            // ── WALLS YIELD TO THE ARMY (2026-10-03, docs/Design/Game_AI.md
            //    § Walls). Every wall action costs supplies and iron; while
            //    military purchases are being refused for either, nothing is
            //    spent on stone. ──
            if (AIBudget.IsMilitaryShort(faction, AIBudget.ResSupplies)
                || AIBudget.IsMilitaryShort(faction, AIBudget.ResIron))
            {
                LogWallsThrottled(faction, "Alanthor walls: holding — the army is short of supplies or iron");
                return;
            }

            // ── Plan, then execute. A BORDER plan is redrawn whenever the set
            //    of territories this faction may WALL changes — its home, and
            //    any other held territory with its own Fortress in it
            //    (AIWallPlanner.CollectWallTerritories). Claiming or losing
            //    unfortified ground moves no wall. Standing hubs stay; the
            //    executor matches slots to hubs by position. ──
            uint signature = AIWallPlanner.WallTerritorySignature(em, faction, hallPos);
            if (em.HasComponent<AIWallPlan>(brainEntity))
            {
                var held = em.GetComponentData<AIWallPlan>(brainEntity);
                if (held.Mode == AIWallPlanner.ModeBorder && held.Territories != signature)
                {
                    em.RemoveComponent<AIWallPlan>(brainEntity);
                    if (em.HasBuffer<AIWallPlanSlot>(brainEntity))
                        em.RemoveComponent<AIWallPlanSlot>(brainEntity);
                    AILogger.Log(faction, "BUILDING", "Alanthor walls: walled territories changed — redrawing");
                }
            }
            if (!em.HasComponent<AIWallPlan>(brainEntity))
            {
                var planned = new NativeList<AIWallPlanSlot>(Allocator.Temp);
                byte mode = AIWallPlanner.BuildPlan(em, faction, hallPos, planned,
                    out string why);
                int gates = 0, towers = 0;
                for (int i = 0; i < planned.Length; i++)
                {
                    if ((planned[i].Flags & AIWallPlanner.FlagGateAfter) != 0) gates++;
                    if ((planned[i].Flags & AIWallPlanner.FlagTower) != 0) towers++;
                }
                em.AddComponentData(brainEntity, new AIWallPlan { Mode = mode, Territories = signature });
                var buf = em.AddBuffer<AIWallPlanSlot>(brainEntity);
                for (int i = 0; i < planned.Length; i++) buf.Add(planned[i]);
                int slotCount = planned.Length;
                planned.Dispose();

                string modeName = mode switch
                {
                    AIWallPlanner.ModeNone => "fully sheltered, no walls needed",
                    AIWallPlanner.ModeChokepoints => "seal chokepoints",
                    AIWallPlanner.ModeBorder => "along the home (and Fortress) territory border",
                    _ => "perimeter around the base",
                };
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: plan = {modeName} " +
                    $"({slotCount} hubs, {gates} gates, {towers} towers; {why})");
                return; // build from the next tick
            }

            var plan = em.GetComponentData<AIWallPlan>(brainEntity);
            if (plan.Mode == AIWallPlanner.ModeNone) return;
            if (!em.HasBuffer<AIWallPlanSlot>(brainEntity)) return;

            // Snapshot the slots — hub placement below is structural and
            // would invalidate a live buffer handle.
            var slots = em.GetBuffer<AIWallPlanSlot>(brainEntity)
                .ToNativeArray(Allocator.Temp);

            // Own hubs, snapshotted once (occupancy checks, link targets).
            var hubEntities = new NativeList<Entity>(Allocator.Temp);
            var hubPositions = new NativeList<float3>(Allocator.Temp);
            {
                var q = QC_WallHubTagFactionTagLocalTransform.Get(em, QT_WallHubTagFactionTagLocalTransform);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    hubEntities.Add(ents[i]);
                    hubPositions.Add(xfs[i].Position);
                }
            }

            try
            {
                // A hub seen standing at its slot marks the slot BUILT, so a
                // later gap there reads as a loss (rebuild-capped), not as an
                // order the executor refused.
                MarkBuiltSlots(em, brainEntity, slots, hubPositions);

                // Which chains (one per walled territory) may grow, and which
                // may still convert gates and towers.
                ComputeChainGates(em, faction, hallPos, slots, hubPositions);

                // One action per think tick, in priority order.
                //
                // The cap counts the hubs ON THIS PLAN only. A border plan is
                // redrawn when the territory changes and standing hubs stay;
                // counting every hub let an old, smaller inner ring use up the
                // cap so the wall at the real border was never built
                // (2026-10-02).
                int planHubs = 0;
                for (int h = 0; h < hubPositions.Length; h++)
                    for (int i = 0; i < slots.Length; i++)
                        if (math.distancesq(hubPositions[h].xz, slots[i].Position.xz)
                            <= Cfg.wallSlotOccupiedRadius * Cfg.wallSlotOccupiedRadius)
                        { planHubs++; break; }
                if (planHubs < Cfg.maxWallHubs
                    && TryPlacePlannedHub(faction, em, brainEntity, slots,
                        hubEntities, hubPositions))
                    return;

                // The wall lock: while a wall level researches, standing
                // walls cannot be extended or converted, and the executors
                // would refuse every order below. Wait it out rather than
                // re-issuing refused orders each think tick.
                // docs/Design/Age_1_Alanthor.md § The four wall levels
                if (CommandRouter.WallsLockedForUpgrade(em, faction))
                    return;

                // Close any hole BEFORE spending on gates and towers — an
                // unbroken wall with no gate beats a decorated one with a
                // doorway in it.
                if (TryCloseWallGaps(faction, em, brainEntity, plan.Mode, slots,
                        hubEntities, hubPositions))
                    return;

                if (TryConvertPlannedGate(faction, em, slots, hubEntities, hubPositions))
                    return;

                TryConvertPlannedTower(faction, em, slots);
            }
            finally
            {
                slots.Dispose();
                hubEntities.Dispose();
                hubPositions.Dispose();
            }
        }

        /// <summary>Index of the first hub within <paramref name="radius"/>
        /// of <paramref name="pos"/>, or -1.</summary>
        private static int FindHubNear(NativeList<float3> hubPositions, float3 pos,
            float radius)
        {
            float r2 = radius * radius;
            for (int h = 0; h < hubPositions.Length; h++)
            {
                float dx = hubPositions[h].x - pos.x, dz = hubPositions[h].z - pos.z;
                if (dx * dx + dz * dz <= r2) return h;
            }
            return -1;
        }

        // ──────────────────────────────────────────────────────────────────
        // WHERE AND WHEN THE AI MAY WALL (2026-10-03, docs/Design/Game_AI.md
        // § Walls)
        // ──────────────────────────────────────────────────────────────────
        //
        // The 0.0.33 60-minute batch ended with 2,803 stone wall pieces on
        // Veilmarch, 822 on Sundered Reach and 702 on Twin Spans: the border
        // plan walled the union of everything held, was redrawn (and a whole
        // new ring added) whenever a territory changed hands, and re-bought
        // hubs on a contested line every half minute (Hollow Table Blue
        // raised the hub at (0,32) 94 times). Every one of those pieces was
        // paid bank-direct, outside the budget the army draws on.
        //
        // The rule now, per plan chain (one chain = one walled territory):
        //   * only the home territory, or a held territory with the
        //     faction's own Fortress in it, is walled (the planner draws
        //     nothing else; a chain whose territory stops qualifying is
        //     frozen until the plan is redrawn);
        //   * a non-home territory is only STARTED when the bank covers the
        //     Fortress price plus the whole ring it plans;
        //   * at most maxWallPiecesPerTerritory pieces stand in a territory;
        //   * a lost hub or link is rebuilt at most maxWallSlotRebuilds times;
        //   * every wall purchase comes out of the Economy wallet without
        //     borrowing from the army's, and the whole doctrine holds while
        //     the army is short of supplies or iron (TryBuildWallDefenses).
        // Once a ring is closed — every live slot hubbed, every link standing
        // or refused — the doctrine has nothing left to add.

        /// <summary>Per chain id, this think: may the doctrine add hubs and
        /// curtain? May it still convert gates and towers?</summary>
        private static readonly bool[] _chainMayGrow = new bool[256];
        private static readonly bool[] _chainMayConvert = new bool[256];
        private static readonly int[] _chainRegion = new int[256];
        private static readonly int[] _chainPieces = new int[256];
        private static readonly System.Collections.Generic.List<int> _wallRegions
            = new System.Collections.Generic.List<int>(4);
        private static readonly System.Collections.Generic.List<float3> _wallAnchors
            = new System.Collections.Generic.List<float3>(4);
        private static readonly System.Collections.Generic.Dictionary<int, float> _nextWallLog
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>A wall-doctrine note, at most once per wallLogInterval per faction.</summary>
        private static void LogWallsThrottled(Faction faction, string line)
        {
            int key = (int)faction;
            float now = SimClock.Now;
            if (_nextWallLog.TryGetValue(key, out float next) && now < next) return;
            _nextWallLog[key] = now + Cfg.wallLogInterval;
            AILogger.Log(faction, "BUILDING", line);
        }

        /// <summary>
        /// May the doctrine spend <paramref name="cost"/> on stone? Out of the
        /// ECONOMY wallet alone — CanSpend does not borrow, so a wall can
        /// never take the army's share — and the bank must cover it.
        /// </summary>
        private static bool WallSpendAllowed(EntityManager em, Faction faction, Cost cost)
            => AIBudget.CanSpend(faction, AIBudgetCategory.EconomyExpansion, cost)
               && FactionEconomy.CanAfford(em, faction, cost);

        /// <summary>Charge a wall order to the Economy wallet (the executor
        /// spends the real bank).</summary>
        private static void RecordWallSpend(Faction faction, Cost cost)
            => AIBudget.RecordSpend(faction, AIBudgetCategory.EconomyExpansion, cost);

        /// <summary>Set FlagHubBuilt on every live slot a hub stands at.</summary>
        private static void MarkBuiltSlots(EntityManager em, Entity brainEntity,
            NativeArray<AIWallPlanSlot> slots, NativeList<float3> hubPositions)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                byte f = slots[i].Flags;
                if ((f & (AIWallPlanner.FlagDead | AIWallPlanner.FlagHubBuilt)) != 0) continue;
                if (FindHubNear(hubPositions, slots[i].Position, Cfg.wallSlotOccupiedRadius) < 0) continue;
                UpdateSlot(em, brainEntity, slots, i, s => { s.Flags |= AIWallPlanner.FlagHubBuilt; return s; });
            }
        }

        /// <summary>Fill <see cref="_chainMayGrow"/> / <see cref="_chainMayConvert"/>
        /// for this think (see the rule above).</summary>
        private static void ComputeChainGates(EntityManager em, Faction faction, float3 hallPos,
            NativeArray<AIWallPlanSlot> slots, NativeList<float3> hubPositions)
        {
            System.Array.Clear(_chainMayGrow, 0, _chainMayGrow.Length);
            System.Array.Clear(_chainMayConvert, 0, _chainMayConvert.Length);
            System.Array.Clear(_chainPieces, 0, _chainPieces.Length);
            for (int c = 0; c < _chainRegion.Length; c++)
                _chainRegion[c] = TheWaningBorder.World.Regions.RegionMap.None;

            // No partition (a map without regions): the terrain-only plans
            // ring the home base and nothing else, so every chain qualifies.
            bool regions = TheWaningBorder.World.Regions.RegionMap.Ready
                        && TheWaningBorder.World.Regions.TerritoryOwnership.Ready;
            if (!regions)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    _chainMayGrow[slots[i].Chain] = true;
                    _chainMayConvert[slots[i].Chain] = true;
                }
                return;
            }

            // Each chain's territory: the region its slots stand in (a slot
            // sits borderInset inside its own territory).
            for (int i = 0; i < slots.Length; i++)
            {
                int ch = slots[i].Chain;
                if (_chainRegion[ch] != TheWaningBorder.World.Regions.RegionMap.None) continue;
                _chainRegion[ch] = TheWaningBorder.World.Regions.RegionMap.RegionAt(
                    slots[i].Position.x, slots[i].Position.z);
            }

            // Pieces standing per chain territory: hubs + curtain modules.
            for (int h = 0; h < hubPositions.Length; h++)
                CountPieceAt(hubPositions[h]);
            {
                var q = QC_WallInstanceTagFactionTagLocalTransform.Get(em, QT_WallInstanceTagFactionTagLocalTransform);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < facs.Length; i++)
                    if (facs[i].Value == faction) CountPieceAt(xfs[i].Position);
            }

            AIWallPlanner.CollectWallTerritories(em, faction, hallPos, _wallRegions, _wallAnchors);
            int home = AIWallPlanner.HomeRegion(hallPos);
            BuildCosts.TryGet("Fortress", out var fortressCost);

            for (int ch = 0; ch < _chainRegion.Length; ch++)
            {
                int r = _chainRegion[ch];
                if (r == TheWaningBorder.World.Regions.RegionMap.None) continue;
                // Home, or held with its own Fortress in it — re-checked
                // every think, not only when the plan was drawn.
                if (!_wallRegions.Contains(r)) continue;
                _chainMayConvert[ch] = true;

                if (_chainPieces[ch] >= Cfg.maxWallPiecesPerTerritory)
                {
                    LogWallsThrottled(faction,
                        $"Alanthor walls: territory {r} holds {_chainPieces[ch]} pieces " +
                        $"(cap {Cfg.maxWallPiecesPerTerritory}) — no more stone there");
                    continue;
                }

                // A non-home territory is only STARTED when the bank covers
                // the Fortress price plus everything its ring plans to buy.
                if (r != home && !ChainStarted(slots, (byte)ch))
                {
                    var ring = EstimateChainCost(slots, (byte)ch);
                    if (!FactionEconomy.CanAfford(em, faction, fortressCost + ring))
                    {
                        LogWallsThrottled(faction,
                            $"Alanthor walls: territory {r} (Fortress) waits — the bank must cover the " +
                            $"Fortress price plus its ring ({fortressCost.Supplies + ring.Supplies}s, " +
                            $"{fortressCost.Iron + ring.Iron}i)");
                        continue;
                    }
                }
                _chainMayGrow[ch] = true;
            }
        }

        private static void CountPieceAt(float3 p)
        {
            int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(p.x, p.z);
            if (r == TheWaningBorder.World.Regions.RegionMap.None) return;
            for (int ch = 0; ch < _chainRegion.Length; ch++)
            {
                if (_chainRegion[ch] == TheWaningBorder.World.Regions.RegionMap.None) continue;
                if (_chainRegion[ch] == r) _chainPieces[ch]++;
            }
        }

        /// <summary>True once any slot of the chain has had a hub stand.</summary>
        private static bool ChainStarted(NativeArray<AIWallPlanSlot> slots, byte chain)
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Chain == chain && (slots[i].Flags & AIWallPlanner.FlagHubBuilt) != 0)
                    return true;
            return false;
        }

        /// <summary>What a chain's whole ring would cost: a hub per live slot
        /// and the curtain to its next live slot.</summary>
        private static Cost EstimateChainCost(NativeArray<AIWallPlanSlot> slots, byte chain)
        {
            Cost total = default;
            if (!BuildCosts.TryGet("Alanthor_Wall", out var hubCost)) return total;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Chain != chain || (slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;
                total = total + hubCost;
                int j = NextLiveSlot(slots, i, cyclic: true);
                if (j < 0 || (slots[i].Flags & AIWallPlanner.FlagTerrainSealed) != 0) continue;
                total = total + CommandRouter.WallRunCost(false,
                    math.distance(slots[i].Position.xz, slots[j].Position.xz));
            }
            return total;
        }

        /// <summary>Unit direction along the plan chain at slot i — the
        /// nudge axis when the exact slot point is unbuildable.</summary>
        private static float3 ChainDirAt(NativeArray<AIWallPlanSlot> slots, int i)
        {
            int j = (i + 1 < slots.Length && slots[i + 1].Chain == slots[i].Chain) ? i + 1
                  : (i > 0 && slots[i - 1].Chain == slots[i].Chain) ? i - 1 : i;
            if (j == i) return new float3(1f, 0f, 0f);
            float3 d = slots[math.max(i, j)].Position - slots[math.min(i, j)].Position;
            d.y = 0f;
            float len = math.length(d);
            return len > 0.01f ? d / len : new float3(1f, 0f, 0f);
        }

        /// <summary>Place the first missing plan hub and link it to its PLAN
        /// neighbours (pre-checked, detoured round a blocker if needed).
        /// Slots that fail placement even after nudging — or whose orders
        /// never produce a hub — are marked dead. Returns true when a hub was
        /// placed this tick.</summary>
        private static bool TryPlacePlannedHub(Faction faction, EntityManager em,
            Entity brainEntity, NativeArray<AIWallPlanSlot> slots,
            NativeList<Entity> hubEntities, NativeList<float3> hubPositions)
        {
            if (!BuildCosts.TryGet("Alanthor_Wall", out var hubCost)) return false;
            int2 hubSize = BuildingSizeConfig.GetSize("Alanthor_Wall");

            int live = 0, filled = 0;
            for (int i = 0; i < slots.Length; i++)
                if ((slots[i].Flags & AIWallPlanner.FlagDead) == 0)
                {
                    live++;
                    if (FindHubNear(hubPositions, slots[i].Position,
                            Cfg.wallSlotOccupiedRadius) >= 0) filled++;
                }

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if ((slot.Flags & AIWallPlanner.FlagDead) != 0) continue;
                // Its territory may not grow (not walled ground, at the piece
                // cap, or a Fortress territory the bank cannot yet fund).
                if (!_chainMayGrow[slot.Chain]) continue;
                if (FindHubNear(hubPositions, slot.Position,
                        Cfg.wallSlotOccupiedRadius) >= 0) continue;

                // A hub STOOD here and was lost. Rebuilt a few times, then
                // left dead — neighbours span the gap if they can.
                bool rebuild = (slot.Flags & AIWallPlanner.FlagHubBuilt) != 0;
                if (rebuild && slot.HubRebuilds >= Cfg.maxWallSlotRebuilds)
                {
                    AILogger.Log(faction, "BUILDING",
                        $"Alanthor walls: hub at ({slot.Position.x:F0},{slot.Position.z:F0}) " +
                        $"lost {slot.HubRebuilds + 1}x — not rebuilt again, marked dead");
                    UpdateSlot(em, brainEntity, slots, i, s => { s.Flags |= AIWallPlanner.FlagDead; return s; });
                    continue;
                }

                // Orders issued for this slot that never produced a hub: the
                // executor refused them (under lockstep the AI never hears
                // so). Kill the slot so its neighbours span it, instead of
                // re-issuing the same refused hub forever (2026-10-02).
                if (slot.HubTries >= AIWallPlanner.MaxWallTries)
                {
                    AILogger.Log(faction, "BUILDING",
                        $"Alanthor walls: hub at ({slot.Position.x:F0},{slot.Position.z:F0}) " +
                        $"ordered {slot.HubTries}x and never raised — marked dead");
                    UpdateSlot(em, brainEntity, slots, i, s => { s.Flags |= AIWallPlanner.FlagDead; return s; });
                    continue;
                }

                // Wait for the wallet rather than skipping ahead — the wall
                // grows in chain order so partial lines stay contiguous.
                if (!WallSpendAllowed(em, faction, hubCost)) return false;

                // Nudge candidates: PERPENDICULAR slides lead (2026-08-11,
                // Green's half wall: a rock on the line killed the middle
                // slot because along-chain nudges walked straight back into
                // it; sliding sideways clears a rock while keeping the
                // neighbour spacing inside the link radius).
                float3 chainDir = ChainDirAt(slots, i);
                float3 perp = new float3(-chainDir.z, 0f, chainDir.x);
                var nudges = new float3[]
                {
                    float3.zero,
                    perp * 2.5f, perp * -2.5f,
                    chainDir * 2.5f, chainDir * -2.5f,
                    perp * 5f, perp * -5f,
                };
                for (int n = 0; n < nudges.Length; n++)
                {
                    // Hubs are buildings and snap to the build grid; the
                    // curtain segments between them stay freeform. Snap first
                    // so validation sees where the hub really lands.
                    // docs/Design/Build_Grid.md
                    float3 pos = BuildGrid.Snap(slot.Position + nudges[n], hubSize);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    if (!BuildCommandHelper.IsValidBuildPosition(em, pos, hubSize)) continue;
                    // A nudge must not carry the hub off its owner's ground —
                    // the executor would refuse it (WallLineOnOwnGround).
                    if (!CommandRouter.WallPointOnOwnGround(em, faction, pos)) continue;

                    // Affordability CHECK only — the SPEND and the hub
                    // creation live in PlaceWallHubDirect, which every peer
                    // executes. The old direct create/spend pair existed on
                    // the host alone and shifted NetworkId allocation for
                    // every later entity in the tick.
                    // docs/Multiplayer_Desync_Sweep_2026-08-16.md
                    if (!WallSpendAllowed(em, faction, hubCost)) return false;

                    // A rebuild is counted once, when ordered; the slot reads
                    // as BUILT again once the new hub is seen standing.
                    if (rebuild)
                        UpdateSlot(em, brainEntity, slots, i, s =>
                        {
                            s.HubRebuilds++;
                            s.Flags = (byte)(s.Flags & ~AIWallPlanner.FlagHubBuilt);
                            return s;
                        });

                    if (GameSettings.IsMultiplayer)
                    {
                        CommandRouter.IssuePlaceWallHub(em, pos, faction,
                            autoBuild: true, CommandSource.AI);
                        RecordWallSpend(faction, hubCost);
                        UpdateSlot(em, brainEntity, slots, i, s => { s.HubTries++; return s; });
                        // The hub entity is created inside the replicated
                        // executor two ticks from now, so the proximity links
                        // cannot be wired this call — TryCloseWallGaps links
                        // plan-adjacent hubs on later think ticks instead.
                        AILogger.Log(faction, "BUILDING",
                            $"Alanthor walls: hub {filled + 1}/{live} at ({pos.x:F0},{pos.z:F0}) (MP)");
                        return true;
                    }

                    Entity hub = CommandRouter.PlaceWallHubDirect(em, pos, faction, autoBuild: true);
                    if (hub == Entity.Null)
                    {
                        UpdateSlot(em, brainEntity, slots, i, s => { s.HubTries++; return s; });
                        return false;
                    }
                    RecordWallSpend(faction, hubCost);
                    // Link to the PLAN neighbours only (2026-10-02). Linking to
                    // every hub within reach pulled in an old ring's hubs and
                    // reached across mountain stretches the plan leaves open.
                    // Each link is pre-checked, detoured round a blocker if it
                    // must be; TryCloseWallGaps retries what is left.
                    byte mode = em.HasComponent<AIWallPlan>(brainEntity)
                        ? em.GetComponentData<AIWallPlan>(brainEntity).Mode : AIWallPlanner.ModeNone;
                    bool cyc = mode == AIWallPlanner.ModePerimeter || mode == AIWallPlanner.ModeBorder;
                    int[] nb = { PrevLiveSlot(slots, i, cyc), NextLiveSlot(slots, i, cyc) };
                    for (int q = 0; q < nb.Length; q++)
                    {
                        int k = nb[q];
                        if (k < 0) continue;
                        // The stretch from -> to is the mountain's when its
                        // FIRST slot (or a dead one inside it) is a seal.
                        int from = q == 0 ? k : i, to = q == 0 ? i : k;
                        if ((slots[from].Flags & AIWallPlanner.FlagTerrainSealed) != 0) continue;
                        if (SealedBetween(slots, from, to)) continue;
                        int h = FindHubNearest(hubPositions, slots[k].Position, Cfg.wallSlotOccupiedRadius);
                        if (h < 0 || !em.Exists(hubEntities[h])) continue;
                        if (AlanthorWall.AreHubsConnected(em, hub, hubEntities[h])) continue;
                        TryLinkHubs(em, faction, hub, hubEntities[h], pos, hubPositions[h]);
                    }
                    AILogger.Log(faction, "BUILDING",
                        $"Alanthor walls: hub {filled + 1}/{live} at ({pos.x:F0},{pos.z:F0})");
                    return true;
                }

                // Unplaceable (veil crust / a building landed there since
                // planning) — kill the slot so the doctrine moves on, and
                // SAY SO (2026-08-11, Green's silent half wall). No
                // structural change has happened this call, so the live
                // buffer fetch is safe. TryCloseWallGaps then spans the dead
                // slot from its two live neighbours, so this is a detour
                // rather than a permanent hole unless the span is too wide.
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: slot at ({slot.Position.x:F0},{slot.Position.z:F0}) " +
                    "unplaceable after nudges — marked dead (neighbours will span it)");
                var buf = em.GetBuffer<AIWallPlanSlot>(brainEntity);
                var s = buf[i];
                s.Flags |= AIWallPlanner.FlagDead;
                buf[i] = s;
                slots[i] = s;
            }
            return false;
        }

        /// <summary>
        /// Walk the plan in chain order and connect every pair of adjacent
        /// live slots whose hubs both stand but are NOT linked by a segment.
        /// One link per think tick; returns true when one was made.
        ///
        /// Why this exists: hub placement links a fresh hub to whatever sits
        /// within <see cref="WallLinkRadius"/>, which is a proximity rule, not
        /// an adjacency rule. Two plan neighbours that each dodged an obstacle
        /// in opposite directions end up 35-40 m apart, silently fall outside
        /// that radius, and never get a curtain between them — the wall looks
        /// built and has a hub-wide hole in it. Skipping a slot flagged
        /// <see cref="AIWallPlanner.FlagTerrainSealed"/> keeps the pass off
        /// stretches the mountain already closes.
        ///
        /// The perimeter is CYCLIC (the last slot's neighbour is the first),
        /// which is what actually closes the loop; chokepoint chains are open
        /// lines and terminate at their ends.
        /// </summary>
        private static bool TryCloseWallGaps(Faction faction, EntityManager em,
            Entity brainEntity, byte planMode, NativeArray<AIWallPlanSlot> slots,
            NativeList<Entity> hubEntities, NativeList<float3> hubPositions)
        {
            if (slots.Length < 2) return false;
            bool cyclic = planMode == AIWallPlanner.ModePerimeter
                       || planMode == AIWallPlanner.ModeBorder;

            for (int i = 0; i < slots.Length; i++)
            {
                if ((slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;
                // Mountain closes this stretch — no curtain wanted.
                if ((slots[i].Flags & AIWallPlanner.FlagTerrainSealed) != 0) continue;
                // Already tried every way and refused: left open, not retried.
                if ((slots[i].Flags & AIWallPlanner.FlagLinkRefused) != 0) continue;
                // Its territory may not grow (see ComputeChainGates).
                if (!_chainMayGrow[slots[i].Chain]) continue;

                int j = NextLiveSlot(slots, i, cyclic);
                if (j < 0) continue;
                // A DEAD slot that was the end of a terrain-sealed run still
                // means "the mountain closes this": never bridge across it.
                if (SealedBetween(slots, i, j)) continue;

                int ha = FindHubNearest(hubPositions, slots[i].Position, Cfg.wallSlotOccupiedRadius);
                int hb = FindHubNearest(hubPositions, slots[j].Position, Cfg.wallSlotOccupiedRadius);
                if (ha < 0 || hb < 0) continue;          // not built yet
                if (ha == hb) continue;                  // one hub fills both

                Entity hubA = hubEntities[ha], hubB = hubEntities[hb];
                if (!em.Exists(hubA) || !em.Exists(hubB)) continue;
                if (AlanthorWall.AreHubsConnected(em, hubA, hubB))
                {
                    if (slots[i].LinkTries != 0 || (slots[i].Flags & AIWallPlanner.FlagLinked) == 0)
                        UpdateSlot(em, brainEntity, slots, i, s =>
                        {
                            s.LinkTries = 0;
                            s.Flags |= AIWallPlanner.FlagLinked;
                            return s;
                        });
                    continue;
                }

                // The link STOOD and was broken. Re-laid a few times, then
                // left open.
                bool relink = (slots[i].Flags & AIWallPlanner.FlagLinked) != 0;
                if (relink && slots[i].LinkRebuilds >= Cfg.maxWallSlotRebuilds)
                {
                    MarkLinkRefused(faction, em, brainEntity, slots, i, j,
                        $"lost {slots[i].LinkRebuilds + 1}x — not rebuilt again");
                    continue;
                }

                // Ordered before and still not linked: the executor refused it
                // (blocked since, out of money at the tick, another order got
                // there first). Give up on this pair rather than re-issuing it
                // every think tick forever — which also starved every gate and
                // tower behind it (2026-10-02).
                if (slots[i].LinkTries >= AIWallPlanner.MaxWallTries)
                {
                    MarkLinkRefused(faction, em, brainEntity, slots, i, j,
                        $"ordered {slots[i].LinkTries}x and never connected");
                    continue;
                }

                var result = TryLinkHubs(em, faction, hubA, hubB, hubPositions[ha], hubPositions[hb]);
                if (result == LinkResult.Unaffordable) return false;   // wait for the bank
                if (result == LinkResult.Refused)
                {
                    MarkLinkRefused(faction, em, brainEntity, slots, i, j,
                        "blocked straight and round both sides");
                    continue;
                }
                UpdateSlot(em, brainEntity, slots, i, s =>
                {
                    s.LinkTries++;
                    if (relink)
                    {
                        s.LinkRebuilds++;
                        s.Flags = (byte)(s.Flags & ~AIWallPlanner.FlagLinked);
                    }
                    return s;
                });
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: linking ({slots[i].Position.x:F0},{slots[i].Position.z:F0}) to " +
                    $"({slots[j].Position.x:F0},{slots[j].Position.z:F0})" +
                    (result == LinkResult.Detoured ? " — detouring round a blocker" : ""));
                return true;
            }
            return false;
        }

        private enum LinkResult { Straight, Detoured, Unaffordable, Refused }

        /// <summary>
        /// Link two standing hubs with a wall that the executor will ACCEPT —
        /// checked here first with the executor's own rules (own ground the
        /// whole way, clear the whole length, affordable), so the AI never
        /// sends an order it knows will be refused. The straight run first;
        /// if something stands in it, a curved run bulging round it — left
        /// and right, wider each try — sent as one drawn-wall order between
        /// the two hubs (docs/Design/Age_1_Alanthor.md § The AI's wall).
        /// </summary>
        private static LinkResult TryLinkHubs(EntityManager em, Faction faction,
            Entity hubA, Entity hubB, float3 pa, float3 pb)
        {
            var joints = new[] { pa, pb };
            float gap = math.distance(pa.xz, pb.xz);
            if (gap < 0.5f) return LinkResult.Refused;

            if (gap <= WallMaxGapSpan
                && CommandRouter.WallLineOnOwnGround(em, faction, joints)
                && CommandRouter.WallLineClear(em, faction, joints, joints, palisade: false))
            {
                var runCost = CommandRouter.WallRunCost(false, gap);
                if (!WallSpendAllowed(em, faction, runCost))
                    return LinkResult.Unaffordable;
                CommandRouter.IssueWallExtend(em, hubA, hubB, pb, faction, CommandSource.AI);
                RecordWallSpend(faction, runCost);
                return LinkResult.Straight;
            }

            float3 dir = new float3(pb.x - pa.x, 0f, pb.z - pa.z) / gap;
            float3 perp = new float3(-dir.z, 0f, dir.x);
            float3 mid = (pa + pb) * 0.5f;
            float[] bulges = { 4f, -4f, 8f, -8f, 12f, -12f, 16f, -16f };
            var pts = new System.Collections.Generic.List<float3>(64);
            var kinds = new System.Collections.Generic.List<CommandRouter.WallPathKind>(64);
            for (int b = 0; b < bulges.Length; b++)
            {
                // Quadratic curve whose middle stands `bulge` off the chord.
                float3 ctrl = mid + perp * (bulges[b] * 2f);
                int n = math.max(4, (int)math.ceil((gap + 2f * math.abs(bulges[b])) / 1.5f));
                pts.Clear(); kinds.Clear();
                for (int s = 0; s <= n; s++)
                {
                    float t = s / (float)n;
                    float3 p = (1 - t) * (1 - t) * pa + 2 * (1 - t) * t * ctrl + t * t * pb;
                    if (s > 0 && s < n) p.y = TerrainUtility.GetHeight(p.x, p.z);
                    pts.Add(s == 0 ? pa : s == n ? pb : p);
                    kinds.Add(s == 0 || s == n ? CommandRouter.WallPathKind.ExistingHub
                                               : CommandRouter.WallPathKind.Point);
                }
                float len = CommandRouter.PolylineLength(pts);
                if (len > WallMaxGapSpan * 1.3f) continue;
                if (!CommandRouter.WallLineOnOwnGround(em, faction, pts)) continue;
                if (!CommandRouter.WallLineClear(em, faction, pts, joints, palisade: false)) continue;
                var curveCost = CommandRouter.WallRunCost(false, len);
                if (!WallSpendAllowed(em, faction, curveCost))
                    return LinkResult.Unaffordable;
                CommandRouter.IssuePlaceWallPath(em, pts, kinds, faction, CommandSource.AI, palisade: false);
                RecordWallSpend(faction, curveCost);
                return LinkResult.Detoured;
            }
            return LinkResult.Refused;
        }

        private static void MarkLinkRefused(Faction faction, EntityManager em, Entity brainEntity,
            NativeArray<AIWallPlanSlot> slots, int i, int j, string why)
        {
            AILogger.Log(faction, "BUILDING",
                $"Alanthor walls: link ({slots[i].Position.x:F0},{slots[i].Position.z:F0}) to " +
                $"({slots[j].Position.x:F0},{slots[j].Position.z:F0}) refused — {why}; left open");
            UpdateSlot(em, brainEntity, slots, i, s => { s.Flags |= AIWallPlanner.FlagLinkRefused; return s; });
        }

        /// <summary>Change slot <paramref name="i"/> in BOTH the live plan
        /// buffer and this tick's snapshot. Fetches the buffer fresh: callers
        /// may have made structural changes since the snapshot.</summary>
        private static void UpdateSlot(EntityManager em, Entity brainEntity,
            NativeArray<AIWallPlanSlot> slots, int i, System.Func<AIWallPlanSlot, AIWallPlanSlot> change)
        {
            if (!em.Exists(brainEntity) || !em.HasBuffer<AIWallPlanSlot>(brainEntity)) return;
            var buf = em.GetBuffer<AIWallPlanSlot>(brainEntity);
            if (i < 0 || i >= buf.Length) return;
            var s = change(buf[i]);
            buf[i] = s;
            slots[i] = s;
        }

        /// <summary>True when a slot strictly after <paramref name="i"/> up to
        /// (not including) <paramref name="j"/> — wrapping — is a terrain
        /// seal: the plan leaves that stretch to the mountain.</summary>
        private static bool SealedBetween(NativeArray<AIWallPlanSlot> slots, int i, int j)
        {
            int n = slots.Length;
            for (int k = (i + 1) % n, guard = 0; k != j && guard < n; k = (k + 1) % n, guard++)
                if ((slots[k].Flags & AIWallPlanner.FlagTerrainSealed) != 0) return true;
            return false;
        }

        /// <summary>The NEAREST hub within <paramref name="radius"/> of
        /// <paramref name="pos"/> (FindHubNear returns the first, which can be
        /// an old ring's hub), or -1. Ties by index, so peers agree.</summary>
        private static int FindHubNearest(NativeList<float3> hubPositions, float3 pos, float radius)
        {
            int best = -1;
            float bestD = radius * radius;
            for (int h = 0; h < hubPositions.Length; h++)
            {
                float dx = hubPositions[h].x - pos.x, dz = hubPositions[h].z - pos.z;
                float d = dx * dx + dz * dz;
                if (d <= bestD && (best < 0 || d < bestD)) { bestD = d; best = h; }
            }
            return best;
        }

        /// <summary>The previous live slot of the same chain (wrapping when
        /// <paramref name="cyclic"/>), or -1.</summary>
        private static int PrevLiveSlot(NativeArray<AIWallPlanSlot> slots, int i, bool cyclic)
        {
            byte chain = slots[i].Chain;
            for (int k = i - 1; k >= 0; k--)
            {
                if (slots[k].Chain != chain) break;
                if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                return k;
            }
            if (!cyclic) return -1;
            for (int k = slots.Length - 1; k > i; k--)
            {
                if (slots[k].Chain != chain) continue;
                if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                return k;
            }
            return -1;
        }

        /// <summary>Index of the next non-dead slot after <paramref name="i"/>
        /// in the same chain, or -1. Wraps to the chain's first slot when
        /// <paramref name="cyclic"/> (the perimeter loop closes on itself);
        /// open chokepoint lines just end.</summary>
        private static int NextLiveSlot(NativeArray<AIWallPlanSlot> slots, int i, bool cyclic)
        {
            byte chain = slots[i].Chain;
            for (int k = i + 1; k < slots.Length; k++)
            {
                if (slots[k].Chain != chain) break;
                if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                return k;
            }
            if (!cyclic) return -1;

            // Wrap: first live slot of this chain, provided it isn't i itself.
            for (int k = 0; k < i; k++)
            {
                if (slots[k].Chain != chain) continue;
                if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                return k;
            }
            return -1;
        }

        /// <summary>Convert the segment behind each gate-flagged slot to a
        /// Gate once both hubs stand and the wall pieces have finished
        /// self-building. One conversion per think tick; returns true when
        /// one was issued.</summary>
        private static bool TryConvertPlannedGate(Faction faction, EntityManager em,
            NativeArray<AIWallPlanSlot> slots,
            NativeList<Entity> hubEntities, NativeList<float3> hubPositions)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if ((slots[i].Flags & AIWallPlanner.FlagGateAfter) == 0) continue;
                if ((slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;
                if (!_chainMayConvert[slots[i].Chain]) continue;

                // Far hub = next live slot of the same chain.
                int j = -1;
                for (int k = i + 1; k < slots.Length; k++)
                {
                    if (slots[k].Chain != slots[i].Chain) break;
                    if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                    j = k;
                    break;
                }
                if (j < 0) continue;

                int ha = FindHubNear(hubPositions, slots[i].Position, Cfg.wallSlotOccupiedRadius);
                int hb = FindHubNear(hubPositions, slots[j].Position, Cfg.wallSlotOccupiedRadius);
                if (ha < 0 || hb < 0) continue;
                Entity hubA = hubEntities[ha], hubB = hubEntities[hb];
                if (!em.Exists(hubA) || !em.Exists(hubB)) continue;
                if (em.HasComponent<UnderConstruction>(hubA)) continue;
                if (em.HasComponent<UnderConstruction>(hubB)) continue;
                if (!em.HasBuffer<WallHubLink>(hubA)) continue;

                Entity segment = Entity.Null;
                var links = em.GetBuffer<WallHubLink>(hubA);
                for (int l = 0; l < links.Length; l++)
                    if (links[l].ConnectedHub == hubB) { segment = links[l].Segment; break; }
                if (segment == Entity.Null || !em.Exists(segment)) continue;
                if (em.HasComponent<WallSegmentUpgradeState>(segment)) continue; // converting
                if (SegmentHasGate(em, segment)) continue;                       // done
                if (SegmentUnderConstruction(em, segment)) continue;             // still rising

                if (!WallSpendAllowed(em, faction,
                        ConvertSegmentToGateCommandHelper.ConversionCost)) return false;
                CommandRouter.IssueConvertSegmentToGate(em, segment, Entity.Null,
                    CommandSource.AI);
                RecordWallSpend(faction, ConvertSegmentToGateCommandHelper.ConversionCost);
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: gate conversion at " +
                    $"({slots[i].Position.x:F0},{slots[i].Position.z:F0})");
                return true;
            }
            return false;
        }

        private static bool SegmentHasGate(EntityManager em, Entity segment)
        {
            if (!em.HasBuffer<WallInstanceRef>(segment)) return false;
            var insts = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < insts.Length; i++)
                if (em.Exists(insts[i].Instance)
                    && em.HasComponent<WallGateTag>(insts[i].Instance))
                    return true;
            return false;
        }

        private static bool SegmentUnderConstruction(EntityManager em, Entity segment)
        {
            if (!em.HasBuffer<WallInstanceRef>(segment)) return false;
            var insts = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < insts.Length; i++)
                if (em.Exists(insts[i].Instance)
                    && em.HasComponent<UnderConstruction>(insts[i].Instance))
                    return true;
            return false;
        }

        /// <summary>Convert the wall instance nearest each tower-flagged
        /// slot (corners, line ends, gate shoulders) to a Wall Tower —
        /// mirrors ActionsPanelBinder's player path (cost + per-instance
        /// WallUpgradeState, UpgradeType 1). One conversion per think tick.
        /// A slot whose nearest instance already carries WallTowerTag is
        /// done and skipped.</summary>
        private static void TryConvertPlannedTower(Faction faction, EntityManager em,
            NativeArray<AIWallPlanSlot> slots)
        {
            if (!BuildCosts.TryGet("Alanthor_WallTower", out var towerCost)) return;

            var instEnts = new NativeList<Entity>(Allocator.Temp);
            var instPos = new NativeList<float3>(Allocator.Temp);
            {
                var q = QC_WallInstanceTagFactionTagLocalTransform.Get(em, QT_WallInstanceTagFactionTagLocalTransform);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    instEnts.Add(ents[i]);
                    instPos.Add(xfs[i].Position);
                }
            }

            try
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if ((slots[i].Flags & AIWallPlanner.FlagTower) == 0) continue;
                    if ((slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;
                    if (!_chainMayConvert[slots[i].Chain]) continue;

                    int best = -1;
                    float bestD2 = 8f * 8f;
                    for (int k = 0; k < instEnts.Length; k++)
                    {
                        float dx = instPos[k].x - slots[i].Position.x;
                        float dz = instPos[k].z - slots[i].Position.z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < bestD2) { bestD2 = d2; best = k; }
                    }
                    if (best < 0) continue;
                    Entity inst = instEnts[best];
                    if (!em.Exists(inst)) continue;
                    if (em.HasComponent<WallTowerTag>(inst)) continue;      // done
                    if (em.HasComponent<WallUpgradeState>(inst)) continue;  // converting
                    if (em.HasComponent<WallGateTag>(inst)) continue;       // gate piece
                    if (em.HasComponent<WallGateRegionTag>(inst)) continue;
                    if (em.HasComponent<UnderConstruction>(inst)) continue; // still rising

                    if (!WallSpendAllowed(em, faction, towerCost)) return;
                    // Spend + stamp through the charged executor: it
                    // validates again and charges the same bank on every
                    // peer, replacing the local Spend + AddComponentData
                    // pair that debited the host alone
                    // (docs/Multiplayer_LAN_Readiness.md). Routed with
                    // CommandSource.AI so the host replicates the
                    // conversion instead of stamping it locally.
                    CommandRouter.IssueWallUpgradeCharged(em, inst, 1, 10f,
                        TheWaningBorder.Core.Commands.CommandSource.AI);
                    RecordWallSpend(faction, towerCost);
                    AILogger.Log(faction, "BUILDING",
                        $"Alanthor walls: tower conversion at " +
                        $"({slots[i].Position.x:F0},{slots[i].Position.z:F0})");
                    return; // one per tick
                }
            }
            finally
            {
                instEnts.Dispose();
                instPos.Dispose();
            }
        }

        // DEAD CODE REMOVED (2026-09-05): PlaceAutoBuildWallHub and
        // ConnectWallHubs — bare AlanthorWall.CreateHub/CreateSegment with
        // direct Health writes, HOST-ONLY and un-networked. Orphaned by the
        // 2026-08-16 desync sweep, which routed every live call site through
        // IssuePlaceWallHub / IssueWallExtend / PlaceWallHubDirect (spend and
        // creation execute on every peer). Deleted so nothing can call them
        // back into existence: any wall entity born outside the replicated
        // path is invisible to lockstep commands and forks the match.
    }
}
