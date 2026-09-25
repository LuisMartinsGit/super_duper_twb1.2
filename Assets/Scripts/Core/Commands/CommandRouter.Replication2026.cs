// CommandRouter.Replication2026.cs
// The six player actions that still wrote ECS directly, brought under lockstep.
//
// Each of these was a UI button reaching into the EntityManager itself, so the
// effect existed on the clicking peer and nowhere else: one player saw a sect
// power land, the other saw their units die to nothing; one saw a wall become a
// tower, the other kept shooting at a wall. Because none of them had a command
// type, there was no replication path to fix — only a bypass to close.
//
// They all follow the shape the earlier conversions established:
//   * the ISSUING peer validates and pays (affordability, cooldown, slot rules)
//   * every peer applies the MUTATION, recomputing anything derivable
//   * the lockstep payload carries only what cannot be recomputed
//
// docs/Multiplayer_LAN_Readiness.md, docs/Multiplayer_Audit.md

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        // ═══════════════════════════════════════════════════════════════
        // SECT ACTIVE POWERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cast a sect's active power at a ground target. The most
        /// consequential of the unreplicated actions: these do AoE damage and
        /// spawn strikes, so a cast that existed on one peer only made the two
        /// worlds disagree about who was alive.
        /// </summary>
        public static void IssueSectPower(EntityManager em, Faction faction, string sectId,
            int tier, float3 targetPos, CommandSource source = CommandSource.LocalPlayer)
        {
            if (string.IsNullOrEmpty(sectId)) return;

            if (ShouldQueueForLockstep(source))
            {
                // Cooldown is checked on BOTH sides: here so the caster gets
                // immediate feedback, and again inside Fire on every peer so a
                // replayed cast cannot fire a power that is not ready.
                if (!SectActivePowerHelper.CanFire(em, faction, sectId, tier)) return;

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SectPower,
                    EntityNetworkId = (int)faction,   // no entity — the faction IS the caster
                    BuildingId = sectId,
                    TargetEntityId = tier,
                    TargetPosition = targetPos
                });
            }
            else
            {
                SectActivePowerHelper.Fire(em, faction, sectId, tier, targetPos);
            }
        }

        /// <summary>Post-lockstep application. Every peer runs this.</summary>
        public static void SectPowerDirect(EntityManager em, Faction faction, string sectId,
            int tier, float3 targetPos)
            => SectActivePowerHelper.Fire(em, faction, sectId, tier, targetPos);

        // ═══════════════════════════════════════════════════════════════
        // RELIQUARY ABILITIES (Antiquity)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Scry (0) / Lockout (1) target the ground; Vision (2) is self.</summary>
        public static void IssueReliquaryAbility(EntityManager em, Entity reliquary, int ability,
            float3 targetPos, CommandSource source = CommandSource.LocalPlayer)
        {
            if (reliquary == Entity.Null || !em.Exists(reliquary)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, reliquary);
                if (networkId <= 0)
                {
                    ReliquaryAbilityDirect(em, reliquary, ability, targetPos);
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.ReliquaryAbility,
                    EntityNetworkId = networkId,
                    TargetEntityId = ability,
                    TargetPosition = targetPos
                });
            }
            else
            {
                ReliquaryAbilityDirect(em, reliquary, ability, targetPos);
            }
        }

        public static void ReliquaryAbilityDirect(EntityManager em, Entity reliquary,
            int ability, float3 targetPos)
            => TheWaningBorder.Systems.Sect.ReliquaryHelper.Fire(em, reliquary, ability, targetPos);

        // ═══════════════════════════════════════════════════════════════
        // WALL PLACEMENT (hub + extend)
        //
        // Wall hubs/segments used to be created straight from the click
        // handler (and from the AI's wall doctrine), outside the command
        // stream. Beyond the walls existing on one peer only, the off-tick
        // entity creation consumed NetworkId slots on that peer alone and
        // shifted every later id assigned in the same tick — corrupting
        // UNRELATED commands. Both paths now ride these two opcodes; the
        // spend happens in the executor on every peer.
        // docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Wall hub. Builder-driven 5 s construction by default;
        /// <paramref name="autoBuild"/> = the AI/extend flavour that
        /// self-builds in 30 s with no builder.</summary>
        public static void IssuePlaceWallHub(EntityManager em, float3 pos, Faction faction,
            bool autoBuild = false, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;

            if (ShouldQueueForLockstep(source))
            {
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.PlaceWallHub,
                    EntityNetworkId = (int)faction,
                    TargetEntityId = autoBuild ? 1 : 0,
                    TargetPosition = pos
                });
            }
            else
            {
                PlaceWallHubDirect(em, pos, faction, autoBuild);
            }
        }

        /// <summary>Executor — every peer. Validates + spends, then creates
        /// the hub exactly as the old click handler did.</summary>
        public static Entity PlaceWallHubDirect(EntityManager em, float3 pos, Faction faction,
            bool autoBuild = false)
        {
            if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost))
                return Entity.Null;

            // NO GRID SNAP (2026-09-24). The wall hub is the one building
            // exempt from the 2 m build grid: a wall is DRAWN, the run cap
            // inserts hubs along the stroke, and while the hub snapped and the
            // curtain did not, every inserted hub landed up to ~1.4 m off the
            // drawn line and the wall visibly kinked at each one. The hub keeps
            // its 2 x 2 footprint for placement, passability and selection --
            // it simply stands where it was put.
            // docs/Design/Build_Grid.md § 5
            pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);

            // Dispatcher, not AlanthorWall.CreateHub direct -- see the same
            // note in the WallExtend executor below (2026-09-13).
            Entity hub = TheWaningBorder.Entities.BuildingFactory.Create(em, "Alanthor_Wall", pos, faction);

            float total = autoBuild ? 30f : 5f;
            if (!em.HasComponent<UnderConstruction>(hub))
                em.AddComponentData(hub, new UnderConstruction { Progress = 0f, Total = total });
            if (autoBuild && !em.HasComponent<AutoConstructTag>(hub))
                em.AddComponent<AutoConstructTag>(hub);
            if (em.HasComponent<Health>(hub))
            {
                var hp = em.GetComponentData<Health>(hub);
                em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
            }

            // Terrain anchor: seal the hub-to-rock gap with curtain modules.
            TheWaningBorder.Entities.AlanthorWall.SealToTerrain(em, hub, autoConstruct: true);
            return hub;
        }

        /// <summary>
        /// Per-hub "Build Wall": a connecting segment from <paramref name="sourceHub"/>
        /// to either an existing hub (<paramref name="snapHub"/>) or a NEW
        /// self-building hub at <paramref name="pos"/>.
        /// </summary>
        public static void IssueWallExtend(EntityManager em, Entity sourceHub, Entity snapHub,
            float3 pos, Faction faction, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (sourceHub == Entity.Null || !em.Exists(sourceHub)) return;

            if (ShouldQueueForLockstep(source))
            {
                int sourceId = GetNetworkId(em, sourceHub);
                if (sourceId <= 0)
                {
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] WallExtend dropped: source hub has no NetworkId in MP.");
                    return;
                }
                int snapId = snapHub != Entity.Null ? GetNetworkId(em, snapHub) : 0;

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.WallExtend,
                    EntityNetworkId = sourceId,
                    TargetEntityId = snapId > 0 ? snapId : 0,
                    SecondaryTargetId = (int)faction,
                    TargetPosition = pos
                });
            }
            else
            {
                WallExtendDirect(em, sourceHub, snapHub, pos, faction);
            }
        }

        /// <summary>Executor — every peer. Mirrors the old SpawnExtendedWallHub
        /// body: snap-to-hub builds only the segment (free); a new hub pays the
        /// standard hub cost and self-builds, as do the segment instances.</summary>
        public static Entity WallExtendDirect(EntityManager em, Entity sourceHub, Entity snapHub,
            float3 pos, Faction faction)
        {
            const float BuildSeconds = 30f;
            if (sourceHub == Entity.Null || !em.Exists(sourceHub)) return Entity.Null;

            Entity hub = snapHub;
            if (hub != Entity.Null && em.Exists(hub))
            {
                if (TheWaningBorder.Entities.AlanthorWall.AreHubsConnected(em, sourceHub, hub))
                    return hub; // identical no-op on every peer
            }
            else
            {
                if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;
                if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost))
                    return Entity.Null;

                // Through the dispatcher, never AlanthorWall.CreateHub direct:
                // the dispatcher is what stamps NetworkedEntity + DisplayName.
                // A hub made here without them (2026-09-13, Hollow Table)
                // could never be repaired or extended from under lockstep --
                // every order aimed at it was dropped by the router guard.
                // Unsnapped, as above: the hub is grid-exempt.
                pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);
                hub = TheWaningBorder.Entities.BuildingFactory.Create(em, "Alanthor_Wall", pos, faction);
                em.AddComponentData(hub,
                    new UnderConstruction { Progress = 0f, Total = BuildSeconds });
                em.AddComponent<AutoConstructTag>(hub);
                if (em.HasComponent<Health>(hub))
                {
                    var hp = em.GetComponentData<Health>(hub);
                    em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
                }
                TheWaningBorder.Entities.AlanthorWall.SealToTerrain(em, hub, autoConstruct: true);
            }

            Entity segment = TheWaningBorder.Entities.AlanthorWall.CreateSegment(
                em, sourceHub, hub, faction);
            TagSegmentAutoConstruct(em, segment, BuildSeconds);
            return hub;
        }

        /// <summary>Tag every instance of a fresh segment for self-construction.
        /// Snapshot first — the AddComponentData calls are structural.</summary>
        static void TagSegmentAutoConstruct(EntityManager em, Entity segment, float buildSeconds)
        {
            if (segment == Entity.Null || !em.HasBuffer<WallInstanceRef>(segment)) return;
            var instances = em.GetBuffer<WallInstanceRef>(segment);
            int count = instances.Length;
            var snapshot = new Unity.Collections.NativeArray<Entity>(
                count, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < count; i++)
                snapshot[i] = instances[i].Instance;

            for (int i = 0; i < count; i++)
            {
                var inst = snapshot[i];
                if (!em.Exists(inst)) continue;
                if (!em.HasComponent<UnderConstruction>(inst))
                    em.AddComponentData(inst,
                        new UnderConstruction { Progress = 0f, Total = buildSeconds });
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


        // ═══════════════════════════════════════════════════════════════
        // VAULT DEPOSIT / WITHDRAW
        //
        // Moves resources between the faction bank (checksummed) and the
        // vault's VaultStorage — both sides of the move must land on every
        // peer, and interest compounding starts from the stored amount.
        // docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        public static void IssueVaultTransfer(EntityManager em, Entity vaultEntity,
            int resourceType, int amount, bool deposit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (vaultEntity == Entity.Null || !em.Exists(vaultEntity)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, vaultEntity);
                if (networkId <= 0)
                {
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] VaultTransfer dropped: vault has no NetworkId in MP.");
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.VaultTransfer,
                    EntityNetworkId = networkId,
                    TargetEntityId = (resourceType & 0xFF) | (deposit ? 0x100 : 0),
                    SecondaryTargetId = amount,
                });
            }
            else
            {
                VaultTransferDirect(em, vaultEntity, resourceType, amount, deposit);
            }
        }


        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        // A DRAWN wall (docs/Design/Age_1_Alanthor.md § Drawing walls):
        // every hub of the path and the segments between them as ONE order,
        // executed hub -> segment -> hub through the two executors above.
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Per-point role in a drawn wall path.</summary>
        public enum WallPathKind : byte
        {
            Point = 0,       // a sample of the curve; modules are laid along these
            NewHub = 1,      // a hub is built here
            ExistingHub = 2, // the friendly hub nearest this point (within snap range) is used
            CellHub = 3      // the friendly wall CELL nearest this point becomes a hub
                             // (its segment splits there) and the path attaches to it —
                             // how a wall branches off a standing wall (T / X)
        }

        /// <summary>
        /// Encode a drawn wall for the command's string field: "x:z" per
        /// point, prefixed "H" for a new hub, "E" for an existing hub and
        /// "C" for a wall cell to convert into one, joined by ';' — two
        /// decimals, invariant culture. Y is re-sampled from the terrain by
        /// the executor.
        ///
        /// NEVER a '|' or a ',' in here: the lockstep datagram is
        /// "TICK|player|tick|count|cmd|cmd|…" and each cmd is comma-joined
        /// (LockstepTypes.LockstepCommand.Serialize), with no escaping. The
        /// first version of this encoder wrote "x|z" and caused the
        /// 2026-09-22 tester desync — the receiver split the path point into
        /// two commands, dropped the tail as unparseable, and the LAST real
        /// command of the tick fell past the count and was lost, so the
        /// client neither placed the wall nor sent worker 4 to build it.
        /// The issuer never notices, because its own canonicalisation round
        /// trip only runs the inner comma split. LockstepManager.QueueCommand
        /// now refuses any command whose text carries the outer delimiter.
        /// </summary>
        public static string EncodeWallPath(System.Collections.Generic.IReadOnlyList<float3> pts,
            System.Collections.Generic.IReadOnlyList<WallPathKind> kinds)
        {
            var sb = new System.Text.StringBuilder(pts.Count * 16);
            var c = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sb.Append(';');
                var k = kinds != null && i < kinds.Count ? kinds[i] : WallPathKind.Point;
                if (k == WallPathKind.NewHub) sb.Append('H');
                else if (k == WallPathKind.ExistingHub) sb.Append('E');
                else if (k == WallPathKind.CellHub) sb.Append('C');
                sb.Append(pts[i].x.ToString("F2", c)).Append(':').Append(pts[i].z.ToString("F2", c));
            }
            return sb.ToString();
        }

        public static bool DecodeWallPath(string encoded,
            System.Collections.Generic.List<float3> pts, System.Collections.Generic.List<WallPathKind> kinds)
        {
            pts.Clear(); kinds.Clear();
            if (string.IsNullOrEmpty(encoded)) return false;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var raw in encoded.Split(';'))
            {
                string pair = raw;
                var kind = WallPathKind.Point;
                if (pair.Length > 0 && pair[0] == 'H') { kind = WallPathKind.NewHub; pair = pair.Substring(1); }
                else if (pair.Length > 0 && pair[0] == 'E') { kind = WallPathKind.ExistingHub; pair = pair.Substring(1); }
                else if (pair.Length > 0 && pair[0] == 'C') { kind = WallPathKind.CellHub; pair = pair.Substring(1); }
                int bar = pair.IndexOf(':');
                if (bar <= 0) return false;
                if (!float.TryParse(pair.Substring(0, bar), System.Globalization.NumberStyles.Float, c, out float x)) return false;
                if (!float.TryParse(pair.Substring(bar + 1), System.Globalization.NumberStyles.Float, c, out float z)) return false;
                pts.Add(new float3(x, TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(x, z), z));
                kinds.Add(kind);
            }
            return pts.Count > 0;
        }

        /// <summary>
        /// Place a drawn wall (docs/Design/Age_1_Alanthor.md § Drawing walls):
        /// the curve's samples with their kinds. Hubs go up at the NewHub /
        /// ExistingHub points; between consecutive hubs the modules follow
        /// the curve. In MP this is one lockstep command; every peer runs
        /// <see cref="PlaceWallPathDirect"/> on it.
        /// </summary>
        public static void IssuePlaceWallPath(EntityManager em,
            System.Collections.Generic.IReadOnlyList<float3> pts,
            System.Collections.Generic.IReadOnlyList<WallPathKind> kinds,
            Faction faction, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (pts == null || pts.Count == 0) return;

            if (ShouldQueueForLockstep(source))
            {
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.PlaceWallPath,
                    EntityNetworkId = (int)faction,
                    BuildingId = EncodeWallPath(pts, kinds),
                });
            }
            else
            {
                PlaceWallPathDirect(em, pts, kinds, faction, null);
            }
        }

        /// <summary>How near a friendly hub must be to an ExistingHub point.
        /// Mirrors the input side's snap radius (BuildCommandPannel's
        /// HubSnapRadius) — both derive from the hub's own radius, so neither
        /// goes stale when the tower is resized.</summary>
        static float WallPathHubSnap
            => TheWaningBorder.Entities.AlanthorWall.HubRadius * 2f;

        static readonly ComponentType[] QT_WallHubs =
        {
            ComponentType.ReadOnly<WallHubTag>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Unity.Transforms.LocalTransform>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_WallHubs;

        static Entity FindWallHubNear(EntityManager em, float3 pos, Faction faction)
        {
            var q = QC_WallHubs.Get(em, QT_WallHubs);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            float bestSq = WallPathHubSnap * WallPathHubSnap;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                var hp = em.GetComponentData<Unity.Transforms.LocalTransform>(ents[i]).Position;
                float dx = pos.x - hp.x, dz = pos.z - hp.z;
                float d = dx * dx + dz * dz;
                // Ties broken by index so every peer picks the same hub.
                if (d < bestSq || (d == bestSq && best != Entity.Null && ents[i].Index < best.Index))
                { bestSq = d; best = ents[i]; }
            }
            return best;
        }

        /// <summary>How near a friendly wall cell must be to a CellHub point.
        /// Half a module: the input side snaps the point onto the cell's
        /// own position, so this is tolerance, not a search radius.</summary>
        const float WallPathCellSnap = 2f;

        static readonly ComponentType[] QT_WallCells =
        {
            ComponentType.ReadOnly<WallInstanceTag>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Unity.Transforms.LocalTransform>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_WallCells;

        /// <summary>Nearest friendly wall cell to <paramref name="pos"/> that
        /// can become a hub (plain, finished, not a gate or tower). Ties
        /// broken by index so every peer picks the same cell.</summary>
        public static Entity FindWallCellNear(EntityManager em, float3 pos, Faction faction,
            float radius = WallPathCellSnap)
        {
            var q = QC_WallCells.Get(em, QT_WallCells);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            float bestSq = radius * radius;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                var cp = em.GetComponentData<Unity.Transforms.LocalTransform>(ents[i]).Position;
                float dx = pos.x - cp.x, dz = pos.z - cp.z;
                float d = dx * dx + dz * dz;
                if (d > bestSq) continue;
                if (d == bestSq && best != Entity.Null && ents[i].Index >= best.Index) continue;
                if (!TheWaningBorder.Entities.AlanthorWall.CanConvertInstanceToHub(em, ents[i])) continue;
                bestSq = d; best = ents[i];
            }
            return best;
        }

        /// <summary>
        /// Executor — every peer. A standing wall cell becomes a hub (its
        /// segment splits there) for a hub's price. Instant: the wall it
        /// joins is already built. Entity.Null when there is no such cell
        /// or the bank is short.
        /// </summary>
        public static Entity ConvertWallCellToHubDirect(EntityManager em, float3 pos, Faction faction)
        {
            Entity cell = FindWallCellNear(em, pos, faction);
            if (cell == Entity.Null) return Entity.Null;
            if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost))
                return Entity.Null;
            return TheWaningBorder.Entities.AlanthorWall.ConvertInstanceToHub(em, cell);
        }

        /// <summary>
        /// Where a run's curve ENDS at <paramref name="hub"/>.
        ///
        /// A hub this order raised (NewHub) ends at the point the player drew:
        /// the hub snapped to the build grid, the wall did not, and bending the
        /// wall onto the snapped centre is what made inserted run-cap hubs kink
        /// the line. An EXISTING hub (or a wall cell converted into one) is a
        /// thing already standing on the ground, so the wall has to actually
        /// reach its centre — there is nothing to keep straight.
        /// docs/Design/Age_1_Alanthor.md § Drawing walls
        /// </summary>
        static float3 CurveEndFor(EntityManager em, Entity hub, WallPathKind kind, float3 drawn)
        {
            if (kind != WallPathKind.NewHub)
                return em.GetComponentData<Unity.Transforms.LocalTransform>(hub).Position;
            // The drawn point carries the ground height sampled at its own XZ,
            // which is what the swept mesh wants to follow.
            return drawn;
        }

        /// <summary>
        /// Executor — every peer. Walks the samples; at each hub point it
        /// raises (or finds, or converts a wall cell into) the hub and, if
        /// there is a previous hub, lays a curved segment along the samples
        /// since it. A lone new hub with no path is the old builder-built
        /// click; everything else self-builds. Stops, identically on every
        /// peer, when the bank runs dry.
        /// <paramref name="created"/> receives every hub made; may be null.
        /// </summary>
        public static void PlaceWallPathDirect(EntityManager em,
            System.Collections.Generic.IReadOnlyList<float3> pts,
            System.Collections.Generic.IReadOnlyList<WallPathKind> kinds,
            Faction faction, System.Collections.Generic.List<Entity> created)
        {
            const float BuildSeconds = 30f;
            int hubCount = 0;
            for (int i = 0; i < kinds.Count; i++) if (kinds[i] != WallPathKind.Point) hubCount++;

            Entity prev = Entity.Null;
            float3 prevEnd = default;
            var sub = new System.Collections.Generic.List<float3>();
            for (int i = 0; i < pts.Count; i++)
            {
                var k = i < kinds.Count ? kinds[i] : WallPathKind.Point;
                if (k == WallPathKind.Point) { sub.Add(pts[i]); continue; }

                Entity hub;
                if (k == WallPathKind.ExistingHub)
                {
                    hub = FindWallHubNear(em, pts[i], faction);
                    if (hub == Entity.Null) return;      // the hub is gone: the order dies here, everywhere
                }
                else if (k == WallPathKind.CellHub)
                {
                    hub = ConvertWallCellToHubDirect(em, pts[i], faction);
                    if (hub == Entity.Null) return;      // the cell is gone (or a hub already stands there)
                    created?.Add(hub);
                }
                else if (prev == Entity.Null && hubCount == 1 && pts.Count == 1)
                {
                    hub = PlaceWallHubDirect(em, pts[i], faction, autoBuild: false);   // the old single click
                }
                else
                {
                    hub = PlaceWallHubDirect(em, pts[i], faction, autoBuild: true);
                }
                if (hub == Entity.Null) return;
                if (k == WallPathKind.NewHub) created?.Add(hub);

                if (prev != Entity.Null && prev != hub
                    && !TheWaningBorder.Entities.AlanthorWall.AreHubsConnected(em, prev, hub))
                {
                    // The sub-path runs END to END, and for a hub the ORDER
                    // raised itself that end is the point the player DREW, not
                    // the hub's grid-snapped centre (2026-09-24).
                    //
                    // A hub is a building and snaps to the 2 m grid; the drawn
                    // curve does not. Pulling the curve onto the snapped centre
                    // bent both runs meeting at an inserted run-cap hub toward
                    // it by up to ~1.4 m, and the wall visibly kinked at every
                    // one. Meeting at the drawn point instead keeps the two
                    // runs collinear and lets the drum CLIP the curtain, which
                    // is the cheaper error by a wide margin.
                    // docs/Design/Age_1_Alanthor.md § Drawing walls
                    var curve = new System.Collections.Generic.List<float3>(sub.Count + 2);
                    curve.Add(prevEnd);
                    curve.AddRange(sub);
                    curve.Add(CurveEndFor(em, hub, k, pts[i]));
                    var segment = TheWaningBorder.Entities.AlanthorWall.CreateSegmentAlong(em, prev, hub, curve, faction);
                    TagSegmentAutoConstruct(em, segment, BuildSeconds);
                }
                prev = hub;
                prevEnd = CurveEndFor(em, hub, k, pts[i]);
                sub.Clear();
            }
        }

        /// <summary>Executor — every peer. Faction comes from the vault's own
        /// FactionTag; a short bank rejects the whole deposit identically
        /// everywhere.</summary>
        public static void VaultTransferDirect(EntityManager em, Entity vaultEntity,
            int resourceType, int amount, bool deposit)
        {
            if (vaultEntity == Entity.Null || !em.Exists(vaultEntity)) return;
            if (!em.HasComponent<VaultStorage>(vaultEntity)) return;
            if (!em.HasComponent<FactionTag>(vaultEntity)) return;

            var vault = em.GetComponentData<VaultStorage>(vaultEntity);
            var faction = em.GetComponentData<FactionTag>(vaultEntity).Value;

            if (deposit)
            {
                if (amount <= 0) return;
                if (!TheWaningBorder.Economy.FactionEconomy.Spend(
                        em, faction, VaultTransferCost(resourceType, amount))) return;
                vault.ResourceType = resourceType;
                vault.StoredAmount += amount;
            }
            else
            {
                int withdraw = (int)vault.StoredAmount;
                if (withdraw <= 0) return;
                TheWaningBorder.Economy.FactionEconomy.Add(
                    em, faction, VaultTransferCost(vault.ResourceType, withdraw));
                vault.StoredAmount = 0f;
                vault.ResourceType = 0;
            }
            vault.LockTimer = vault.LockDuration;
            em.SetComponentData(vaultEntity, vault);
        }

        private static Cost VaultTransferCost(int type, int amount) => type switch
        {
            1 => Cost.Of(supplies: amount),
            2 => Cost.Of(iron: amount),
            3 => Cost.Of(veilstone: amount),
            4 => Cost.Of(veilsteel: amount),
            _ => default,
        };

        // ═══════════════════════════════════════════════════════════════
        // BAZAAR PACK / UNPACK
        //
        // The pack tag triggers BazaarPackSystem to DESTROY the building and
        // spawn a wagon — a structural change plus fresh NetworkIds, so a
        // local-only tag forked both the entity set and the id sequence.
        // docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        public static void IssueBazaarPack(EntityManager em, Entity bazaar, bool pack,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (bazaar == Entity.Null || !em.Exists(bazaar)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, bazaar);
                if (networkId <= 0)
                {
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] BazaarPack dropped: bazaar has no NetworkId in MP.");
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.BazaarPack,
                    EntityNetworkId = networkId,
                    TargetEntityId = pack ? 1 : 0,
                });
            }
            else
            {
                BazaarPackDirect(em, bazaar, pack);
            }
        }

        /// <summary>Executor — every peer. Idempotent: the tag add is skipped
        /// when already present, so a replay cannot double-trigger.</summary>
        public static void BazaarPackDirect(EntityManager em, Entity bazaar, bool pack)
        {
            if (bazaar == Entity.Null || !em.Exists(bazaar)) return;
            if (pack)
            {
                if (!em.HasComponent<BazaarPackCommand>(bazaar))
                    em.AddComponent<BazaarPackCommand>(bazaar);
            }
            else
            {
                if (!em.HasComponent<BazaarUnpackCommand>(bazaar))
                    em.AddComponent<BazaarUnpackCommand>(bazaar);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SECT GLOW ALLOCATION
        //
        // Allocated glow HALVES the sect's active-power cooldown, so a local
        // allocation makes CanFire disagree between peers — and a replicated
        // SectPower command then fires on one peer and is dropped on the
        // other. docs/Multiplayer_Desync_Sweep_2026-08-16.md
        // ═══════════════════════════════════════════════════════════════

        public static void IssueSectShardrootAlloc(EntityManager em, Faction faction, string sectId,
            bool allocate, CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (string.IsNullOrEmpty(sectId)) return;

            if (ShouldQueueForLockstep(source))
            {
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SectShardrootAlloc,
                    EntityNetworkId = (int)faction,
                    BuildingId = sectId,
                    TargetEntityId = allocate ? 1 : 0,
                });
            }
            else
            {
                SectShardrootAllocDirect(em, faction, sectId, allocate);
            }
        }

        /// <summary>Executor — every peer. The helper is a no-op when the
        /// state already matches, so replays cannot double-apply.</summary>
        public static void SectShardrootAllocDirect(EntityManager em, Faction faction, string sectId,
            bool allocate)
        {
            if (allocate)
                SectActivePowerHelper.AllocateShardroot(em, faction, sectId);
            else
                SectActivePowerHelper.DeallocateShardroot(em, faction, sectId);
        }

        // ═══════════════════════════════════════════════════════════════
        // WALL INSTANCE UPGRADE (tower / gate)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Start a wall piece's upgrade. The COST is paid by the issuer; the
        /// timer component is stamped on every peer, so the upgrade completes at
        /// the same tick everywhere.
        /// </summary>
        public static void IssueWallUpgrade(EntityManager em, Entity wall, int upgradeType,
            float duration, CommandSource source = CommandSource.LocalPlayer)
        {
            if (wall == Entity.Null || !em.Exists(wall)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, wall);
                if (networkId <= 0)
                {
                    // DROP, never stamp locally: a local-only upgrade timer
                    // completes on one peer alone, and the old fallback also
                    // skipped the charge. A wall without a NetworkId in MP is
                    // a spawn-path bug — surface it.
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] WallUpgrade dropped: wall has no NetworkId in MP.");
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.WallUpgrade,
                    EntityNetworkId = networkId,
                    TargetEntityId = upgradeType,
                    // Duration rides the spare position field rather than being
                    // recomputed, so a future balance change to one peer's
                    // constants cannot desync an upgrade already in flight.
                    TargetPosition = new float3(duration, 0f, 0f)
                });
            }
            else
            {
                WallUpgradeDirect(em, wall, upgradeType, duration);
            }
        }

        public static void WallUpgradeDirect(EntityManager em, Entity wall, int upgradeType, float duration)
        {
            if (wall == Entity.Null || !em.Exists(wall)) return;
            if (em.HasComponent<WallUpgradeState>(wall)) return;   // already upgrading
            if (duration <= 0f) duration = 10f;
            em.AddComponentData(wall, new WallUpgradeState
            {
                UpgradeType = (byte)upgradeType,
                Duration = duration,
                Remaining = duration,
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // FIENDSTONE KEEP WINGS
        // ═══════════════════════════════════════════════════════════════

        public static void IssueKeepWing(EntityManager em, Entity keep, byte wing, float duration,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (keep == Entity.Null || !em.Exists(keep)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, keep);
                if (networkId <= 0)
                {
                    // DROP — same rationale as the WallUpgrade fallback above.
                    UnityEngine.Debug.LogError(
                        "[CommandRouter] KeepWing dropped: keep has no NetworkId in MP.");
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.KeepWing,
                    EntityNetworkId = networkId,
                    TargetEntityId = wing,
                    TargetPosition = new float3(duration, 0f, 0f)
                });
            }
            else
            {
                KeepWingDirect(em, keep, wing, duration);
            }
        }

        public static void KeepWingDirect(EntityManager em, Entity keep, byte wing, float duration)
        {
            if (keep == Entity.Null || !em.Exists(keep)) return;
            if (!em.HasComponent<KeepWings>(keep)) return;
            if (em.HasComponent<KeepWingConstruction>(keep)) return;   // one at a time
            if (duration <= 0f) duration = 30f;
            em.AddComponentData(keep, new KeepWingConstruction
            {
                Wing = wing, Remaining = duration, Total = duration,
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // UNIT PROMOTION
        // ═══════════════════════════════════════════════════════════════

        public static void IssueUnitPromote(EntityManager em, Entity unit,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, unit);
                if (networkId <= 0)
                {
                    UnitRankCommandHelper.Execute(em, unit);
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.UnitPromote,
                    EntityNetworkId = networkId
                });
            }
            else
            {
                UnitRankCommandHelper.Execute(em, unit);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SHIFT-QUEUED WAYPOINTS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Append one waypoint to a unit's command queue. Ordinary moves were
        /// replicated long ago; the shift-queued variant never was, so a queued
        /// march existed only on the machine that drew it and the other peer's
        /// copy of those units stood still.
        /// </summary>
        public static void IssueQueuedWaypoint(EntityManager em, Entity unit,
            QueuedCommandType type, float3 targetPos, Entity targetEntity,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;

            if (ShouldQueueForLockstep(source))
            {
                int networkId = GetNetworkId(em, unit);
                if (networkId <= 0)
                {
                    QueuedWaypointDirect(em, unit, type, targetPos, targetEntity);
                    return;
                }

                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.QueueWaypoint,
                    EntityNetworkId = networkId,
                    TargetEntityId = (int)type,
                    SecondaryTargetId = GetNetworkId(em, targetEntity),
                    TargetPosition = targetPos
                });
            }
            else
            {
                QueuedWaypointDirect(em, unit, type, targetPos, targetEntity);
            }
        }

        public static void QueuedWaypointDirect(EntityManager em, Entity unit,
            QueuedCommandType type, float3 targetPos, Entity targetEntity)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (!em.HasBuffer<QueuedCommand>(unit)) em.AddBuffer<QueuedCommand>(unit);
            em.GetBuffer<QueuedCommand>(unit).Add(new QueuedCommand
            {
                Type = type,
                TargetPosition = targetPos,
                TargetEntity = targetEntity,
            });

            // The activation must travel WITH the payload: CommandQueueSystem
            // only drains units that carry CommandQueueActive, and the input
            // manager used to add that tag locally — so a remote peer received
            // the waypoints but its copy of the unit never moved (2026-08-16
            // sweep, B3). Adding it here makes the direct path (SP) and the
            // replicated path (every MP peer) behave identically.
            if (!em.HasComponent<CommandQueueActive>(unit))
                em.AddComponent<CommandQueueActive>(unit);
        }
    }
}
