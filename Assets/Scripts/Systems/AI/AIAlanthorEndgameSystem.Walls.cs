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

        /// <summary>Hard cap on doctrine-built wall hubs per faction —
        /// sized for a full max-extent perimeter (4 x 124 m / 12.5 m).</summary>
        private const int MaxWallHubs = 40;

        /// <summary>Hub / instance self-build time — mirrors
        /// BuilderCommandPanel.WallExtendBuildSeconds (30 s, AutoConstructTag,
        /// no builder dispatched).</summary>
        private const float WallHubBuildSeconds = 30f;

        /// <summary>A plan slot with a friendly hub within this range counts
        /// as filled. Must exceed the largest placement nudge (perp * 5 m) or
        /// a hub that dodged a rock stops counting as its own slot's
        /// occupant: the doctrine then re-places it forever and the
        /// gap-closing pass below never sees the slot as filled. Plan
        /// spacing is 30 m, so 7 m cannot claim a neighbour's slot.</summary>
        private const float WallSlotOccupiedRadius = 7f;

        /// <summary>Longest gap the gap-closing pass will span with a single
        /// segment. Two plan spacings plus tolerance — enough to bridge one
        /// dead slot, short of stitching a wall across open map when a whole
        /// run failed.</summary>
        private const float WallMaxGapSpan = AIWallPlanner.HubSpacing * 2f + 8f;

        /// <summary>Link radius for stitching a fresh hub to its plan
        /// neighbours — covers the plan's 30 m spacing plus nudge tolerance.
        /// Segments span any length (CreateSegment tiles 3 m modules); the
        /// 16 m WallAutoSegmentSystem constant is that DISABLED system's
        /// auto-link rule, not a segment limit, so it does not bound this.
        /// Kept under 2x HubSpacing so the wall never links across a dead
        /// slot's hole (that hole is deliberate — usually a mountain).</summary>
        private const float WallLinkRadius = AIWallPlanner.HubSpacing + 3f;

        private static void TryBuildWallDefenses(Faction faction, EntityManager em,
            Entity brainEntity, float3 hallPos)
        {
            // ── Plan once, then execute forever. ──
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
                em.AddComponentData(brainEntity, new AIWallPlan { Mode = mode });
                var buf = em.AddBuffer<AIWallPlanSlot>(brainEntity);
                for (int i = 0; i < planned.Length; i++) buf.Add(planned[i]);
                int slotCount = planned.Length;
                planned.Dispose();

                string modeName = mode switch
                {
                    AIWallPlanner.ModeNone => "fully sheltered, no walls needed",
                    AIWallPlanner.ModeChokepoints => "seal chokepoints",
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
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<WallHubTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
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
                // One action per think tick, in priority order.
                if (hubEntities.Length < MaxWallHubs
                    && TryPlacePlannedHub(faction, em, brainEntity, slots,
                        hubEntities, hubPositions))
                    return;

                // Close any hole BEFORE spending on gates and towers — an
                // unbroken wall with no gate beats a decorated one with a
                // doorway in it.
                if (TryCloseWallGaps(faction, em, plan.Mode, slots,
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

        /// <summary>Place the first missing plan hub and link it to every
        /// friendly hub within <see cref="WallLinkRadius"/> (plan neighbours
        /// sit at HubSpacing, so the chain stitches itself and the perimeter
        /// loop closes on the last slot). Slots that fail placement even
        /// after nudging are marked dead. Returns true when a hub was placed
        /// this tick.</summary>
        private static bool TryPlacePlannedHub(Faction faction, EntityManager em,
            Entity brainEntity, NativeArray<AIWallPlanSlot> slots,
            NativeList<Entity> hubEntities, NativeList<float3> hubPositions)
        {
            if (!BuildCosts.TryGet("Alanthor_Wall", out var hubCost)) return false;
            int2 hubSize = BuildingSizeConfig.GetSize("Alanthor_Wall");
            const float maxLink = WallLinkRadius;

            int live = 0, filled = 0;
            for (int i = 0; i < slots.Length; i++)
                if ((slots[i].Flags & AIWallPlanner.FlagDead) == 0)
                {
                    live++;
                    if (FindHubNear(hubPositions, slots[i].Position,
                            WallSlotOccupiedRadius) >= 0) filled++;
                }

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if ((slot.Flags & AIWallPlanner.FlagDead) != 0) continue;
                if (FindHubNear(hubPositions, slot.Position,
                        WallSlotOccupiedRadius) >= 0) continue;

                // Wait for the bank rather than skipping ahead — the wall
                // grows in chain order so partial lines stay contiguous.
                if (!FactionEconomy.CanAfford(em, faction, hubCost)) return false;

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

                    // Affordability CHECK only — the SPEND and the hub
                    // creation live in PlaceWallHubDirect, which every peer
                    // executes. The old direct create/spend pair existed on
                    // the host alone and shifted NetworkId allocation for
                    // every later entity in the tick.
                    // docs/Multiplayer_Desync_Sweep_2026-08-16.md
                    if (!FactionEconomy.CanAfford(em, faction, hubCost)) return false;

                    if (GameSettings.IsMultiplayer)
                    {
                        CommandRouter.IssuePlaceWallHub(em, pos, faction,
                            autoBuild: true, CommandSource.AI);
                        // The hub entity is created inside the replicated
                        // executor two ticks from now, so the proximity links
                        // cannot be wired this call — TryCloseWallGaps links
                        // plan-adjacent hubs on later think ticks instead.
                        AILogger.Log(faction, "BUILDING",
                            $"Alanthor walls: hub {filled + 1}/{live} at ({pos.x:F0},{pos.z:F0}) (MP)");
                        return true;
                    }

                    Entity hub = CommandRouter.PlaceWallHubDirect(em, pos, faction, autoBuild: true);
                    if (hub == Entity.Null) return false;
                    for (int h = 0; h < hubEntities.Length; h++)
                    {
                        float dx = hubPositions[h].x - pos.x;
                        float dz = hubPositions[h].z - pos.z;
                        if (dx * dx + dz * dz > maxLink * maxLink) continue;
                        if (!em.Exists(hubEntities[h])) continue;
                        if (AlanthorWall.AreHubsConnected(em, hub, hubEntities[h])) continue;
                        CommandRouter.IssueWallExtend(em, hub, hubEntities[h], pos, faction,
                            CommandSource.AI);
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
            byte planMode, NativeArray<AIWallPlanSlot> slots,
            NativeList<Entity> hubEntities, NativeList<float3> hubPositions)
        {
            if (slots.Length < 2) return false;
            bool cyclic = planMode == AIWallPlanner.ModePerimeter;

            for (int i = 0; i < slots.Length; i++)
            {
                if ((slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;
                // Mountain closes this stretch — no curtain wanted.
                if ((slots[i].Flags & AIWallPlanner.FlagTerrainSealed) != 0) continue;

                int j = NextLiveSlot(slots, i, cyclic);
                if (j < 0) continue;

                int ha = FindHubNear(hubPositions, slots[i].Position, WallSlotOccupiedRadius);
                int hb = FindHubNear(hubPositions, slots[j].Position, WallSlotOccupiedRadius);
                if (ha < 0 || hb < 0) continue;          // not built yet
                if (ha == hb) continue;                  // one hub fills both

                Entity hubA = hubEntities[ha], hubB = hubEntities[hb];
                if (!em.Exists(hubA) || !em.Exists(hubB)) continue;
                if (AlanthorWall.AreHubsConnected(em, hubA, hubB)) continue;

                float dx = hubPositions[ha].x - hubPositions[hb].x;
                float dz = hubPositions[ha].z - hubPositions[hb].z;
                float gap = math.sqrt(dx * dx + dz * dz);
                if (gap > WallMaxGapSpan)
                {
                    // Too wide to be one curtain run. Say so once per pair
                    // rather than silently leaving the enclosure open — a
                    // silent hole is the exact failure this pass exists for.
                    AILogger.Log(faction, "BUILDING",
                        $"Alanthor walls: {gap:F0} m gap between " +
                        $"({slots[i].Position.x:F0},{slots[i].Position.z:F0}) and " +
                        $"({slots[j].Position.x:F0},{slots[j].Position.z:F0}) " +
                        "exceeds the single-span limit — enclosure still OPEN here");
                    continue;
                }

                // Routed: segment creation replicates to every peer (the old
                // direct call built it on the host alone).
                CommandRouter.IssueWallExtend(em, hubA, hubB,
                    hubPositions[hb], faction, CommandSource.AI);
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: closed a {gap:F0} m gap between " +
                    $"({slots[i].Position.x:F0},{slots[i].Position.z:F0}) and " +
                    $"({slots[j].Position.x:F0},{slots[j].Position.z:F0})");
                return true;
            }
            return false;
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

                int ha = FindHubNear(hubPositions, slots[i].Position, WallSlotOccupiedRadius);
                int hb = FindHubNear(hubPositions, slots[j].Position, WallSlotOccupiedRadius);
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

                if (!FactionEconomy.CanAfford(em, faction,
                        ConvertSegmentToGateCommandHelper.ConversionCost)) return false;
                CommandRouter.IssueConvertSegmentToGate(em, segment, Entity.Null,
                    CommandSource.AI);
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
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<WallInstanceTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
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

                    if (!FactionEconomy.CanAfford(em, faction, towerCost)) return;
                    // Spend + stamp through the charged executor: it
                    // validates again and charges the same bank on every
                    // peer, replacing the local Spend + AddComponentData
                    // pair that debited the host alone
                    // (docs/Multiplayer_LAN_Readiness.md). Routed with
                    // CommandSource.AI so the host replicates the
                    // conversion instead of stamping it locally.
                    CommandRouter.IssueWallUpgradeCharged(em, inst, 1, 10f,
                        TheWaningBorder.Core.Commands.CommandSource.AI);
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

        /// <summary>Place a self-building wall hub (30 s AutoConstruct, no
        /// builder) — mirrors BuilderCommandPanel.SpawnExtendedWallHub.
        /// Every hub SEALS to adjacent impassable terrain (curtain modules
        /// across the hub-to-rock gap) so chokepoint lines cannot be
        /// squeezed around at their ends.</summary>
        private static Entity PlaceAutoBuildWallHub(EntityManager em, float3 pos, Faction faction)
        {
            Entity hub = AlanthorWall.CreateHub(em, pos, faction);
            em.AddComponentData(hub, new UnderConstruction
            {
                Progress = 0f,
                Total = WallHubBuildSeconds,
            });
            em.AddComponent<AutoConstructTag>(hub);
            if (em.HasComponent<Health>(hub))
            {
                var hp = em.GetComponentData<Health>(hub);
                em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
            }
            AlanthorWall.SealToTerrain(em, hub, autoConstruct: true);
            return hub;
        }

        /// <summary>Create the segment between two hubs and tag every spawned
        /// wall instance for auto-construction. The instance buffer is
        /// snapshotted first — the AddComponentData calls below are
        /// structural and would invalidate a live buffer handle (same
        /// pattern as the player's chain-placement code).</summary>
        private static void ConnectWallHubs(EntityManager em, Entity hubA, Entity hubB,
            Faction faction)
        {
            Entity segment = AlanthorWall.CreateSegment(em, hubA, hubB, faction);
            if (!em.HasBuffer<WallInstanceRef>(segment)) return;

            var instances = em.GetBuffer<WallInstanceRef>(segment);
            int count = instances.Length;
            var snapshot = new NativeArray<Entity>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) snapshot[i] = instances[i].Instance;

            for (int i = 0; i < count; i++)
            {
                var inst = snapshot[i];
                if (!em.Exists(inst)) continue;
                if (!em.HasComponent<UnderConstruction>(inst))
                    em.AddComponentData(inst, new UnderConstruction
                    {
                        Progress = 0f,
                        Total = WallHubBuildSeconds,
                    });
                if (!em.HasComponent<AutoConstructTag>(inst))
                    em.AddComponent<AutoConstructTag>(inst);
                if (em.HasComponent<Health>(inst))
                {
                    var hp = em.GetComponentData<Health>(inst);
                    em.SetComponentData(inst, new Health { Value = 1, Max = hp.Max });
                }
            }
            snapshot.Dispose();
        }
    }
}
