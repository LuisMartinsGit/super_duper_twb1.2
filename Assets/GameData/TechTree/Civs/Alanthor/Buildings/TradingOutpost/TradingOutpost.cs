using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Systems.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Alanthor Trading Outpost — raised BESIDE a resource node (supply, iron
    /// or veilstone) after the Trade Charters research, it trickles that
    /// node's resource without ever drawing on it, for as long as the node
    /// lives (docs/Design/Veilstone_Economy.md §3.1, rewritten 2026-11-09,
    /// decision 42). The pay is TradingOutpostSystem's.
    ///
    /// FOUR SIDES PER NODE (Build_Grid.md § The Trading Outpost's side slots).
    /// An Outpost snaps to one of the four orthogonal side slots of a living
    /// node — north, east, south, west — its footprint flush against the
    /// node's square, one post per side. The node itself stays an impassable
    /// cell nobody builds on (its extractor stands ON it). Each further post
    /// around the same node costs more to build (<see cref="RampMultiplier"/>);
    /// the first post on a new node is base price again.
    ///
    /// Placement (<see cref="TrySnapToSide"/> / <see cref="SnapToSideAmong"/>,
    /// reached through TerritoryOwnership.TrySnapToNode and enforced in
    /// CommandRouter.CheckPlaceBuilding).
    /// </summary>
    public static class TradingOutpost
    {
        /// <summary>
        /// The Outpost's own presentation id. It used to share 355 with the
        /// Runai Trading Post, which made the SO prefab unassignable: the
        /// catalog maps prefabs BY presentation id, so art on one would have
        /// drawn on both. Must equal presentationId on TradingOutpost.asset,
        /// whose prefab is the visual (a placeholder House model until the
        /// ART PASS gives the Outpost its own Alanthor mesh).
        /// </summary>
        public const int PresentationID = 523;

        public const string BuildingId = "Alanthor_TradingOutpost";

        /// <summary>How many posts one outcrop takes: one per side.</summary>
        public const int SideCount = 4;

        /// <summary><see cref="TradingOutpostSite.Side"/> of a post that stands
        /// on no node's side slot.</summary>
        public const byte OnOutcrop = 255;

        /// <summary>Metres within which a position counts as a given slot.
        /// Slots are grid-snapped, so a real match is exact; this only absorbs
        /// float noise.</summary>
        private const float SlotTolerance = 0.5f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = def.lineOfSight });

            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(TradingOutpostTag));
            EntityBuild.Add(em, entity, default(TradingOutpostCarry));
            // The production queue carries its level-ups (the unified queue).
            EntityBuild.Add(em, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(em, entity);
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(em, entity, Defence(def));
            // Levels 1-3 (Civs/Alanthor/Buildings/TradingOutpost/TradingOutpost_Lvl1..3):
            // L1 is granted by BuildingCultureAutoLevelSystem, L2/L3 are bought.
            EntityBuild.Add(em, entity, default(BuildingUpgradeable));
            EntityBuild.Add(em, entity, SiteFor(em, entity, position));
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = def.lineOfSight });

            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(TradingOutpostTag));
            EntityBuild.Add(ecb, entity, default(TradingOutpostCarry));
            EntityBuild.Add(ecb, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, entity);
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(ecb, entity, Defence(def));
            EntityBuild.Add(ecb, entity, default(BuildingUpgradeable));
            // TradingOutpostSite needs the world to resolve; TradingOutpostSystem
            // stamps it on its next cycle (the ECB path has no EntityManager).
            return EntityBuild.Done(entity);
        }

        private static Defense Defence(TheWaningBorder.Data.BuildingDef def)
        {
            if (def?.defense == null)
                return new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 };
            return new Defense
            {
                Melee = def.defense.melee,
                Ranged = def.defense.ranged,
                Siege = def.defense.siege,
                Magic = def.defense.magic,
            };
        }

        // ── Geometry: the four side slots of a node ────────────────────

        /// <summary>The kind of resource node a post stands beside — which
        /// resource it trickles.</summary>
        public enum NodeKind : byte { None = 0, Supply = 1, Iron = 2, Veilstone = 3 }

        static readonly ComponentType[] QT_SupplyNodes =
        {
            ComponentType.ReadOnly<SupplyNodeTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static readonly ComponentType[] QT_IronNodes =
        {
            ComponentType.ReadOnly<IronMineTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static readonly ComponentType[] QT_Outcrops =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static readonly ComponentType[] QT_Outposts =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static readonly ComponentType[] QT_Plans =
        {
            ComponentType.ReadOnly<PlannedBuilding>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_SupplyNodes, QC_IronNodes, QC_Outcrops, QC_Outposts, QC_Plans;

        /// <summary>One resource node a post may stand beside.</summary>
        public struct NodeRef
        {
            public Entity Entity;
            public float3 Position;
            public NodeKind Kind;
        }

        // Main-thread scratch (the cycle, the ghost, the router and the AI).
        private static readonly System.Collections.Generic.List<NodeRef> _nodes =
            new System.Collections.Generic.List<NodeRef>(64);

        /// <summary>
        /// Every resource node, in a FIXED order (supply, iron, veilstone; each
        /// in query order) — so every lockstep peer walks them identically.
        /// The returned list is shared scratch: read it before the next call.
        /// </summary>
        public static System.Collections.Generic.List<NodeRef> AllNodes(EntityManager em)
        {
            _nodes.Clear();
            AddNodes(em, QC_SupplyNodes.Get(em, QT_SupplyNodes), NodeKind.Supply);
            AddNodes(em, QC_IronNodes.Get(em, QT_IronNodes), NodeKind.Iron);
            AddNodes(em, QC_Outcrops.Get(em, QT_Outcrops), NodeKind.Veilstone);
            return _nodes;
        }

        private static void AddNodes(EntityManager em, EntityQuery q, NodeKind kind)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                _nodes.Add(new NodeRef { Entity = ents[i], Position = xfs[i].Position, Kind = kind });
        }

        /// <summary>The kind of a node entity (None for anything else).</summary>
        public static NodeKind KindOf(EntityManager em, Entity node)
        {
            if (node == Entity.Null || !em.Exists(node)) return NodeKind.None;
            if (em.HasComponent<SupplyNodeTag>(node)) return NodeKind.Supply;
            if (em.HasComponent<IronMineTag>(node)) return NodeKind.Iron;
            if (em.HasComponent<VeilstoneOutcroppingTag>(node)) return NodeKind.Veilstone;
            return NodeKind.None;
        }

        /// <summary>A node a post may stand beside right now: not spent, and —
        /// for an outcrop — not Cursed.</summary>
        public static bool IsLiveNode(EntityManager em, Entity node, NodeKind kind)
        {
            if (node == Entity.Null || !em.Exists(node)) return false;
            if (em.HasComponent<NodeReserve>(node) && em.GetComponentData<NodeReserve>(node).Remaining <= 0f)
                return false;
            if (kind == NodeKind.Veilstone
                && VeilstoneNodeStateSystem.KindOf(em, node) == VeilstoneNodeKind.Cursed) return false;
            return true;
        }

        /// <summary>Placement snap reach: metres from the cursor (or a
        /// candidate site) to an outcrop's centre (TradingOutpostSystem.asset).</summary>
        public static float Reach => TradingOutpostSystem.Cfg.nodeReach;

        /// <summary>
        /// Where a post on <paramref name="side"/> (0 N, 1 E, 2 S, 3 W) of the
        /// outcrop at <paramref name="outcrop"/> stands: centred on the
        /// outcrop's axis, its footprint flush against the outcrop's square.
        /// The offset is half the node plus half the post, from the post's SO
        /// footprint — with the node's even parity it always lands on the
        /// post's own grid parity, so the snap below never moves it.
        /// </summary>
        public static float3 SideSlot(float3 outcrop, int side)
            => SideSlot(outcrop, side, BuildingSizeConfig.GetSize(BuildingId));

        private static float3 SideSlot(float3 outcrop, int side, int2 size)
        {
            float ox = BuildGrid.ResourceNodeHalf + size.x * 0.5f;
            float oz = BuildGrid.ResourceNodeHalf + size.y * 0.5f;
            float3 p = side switch
            {
                0 => new float3(outcrop.x, outcrop.y, outcrop.z + oz),
                1 => new float3(outcrop.x + ox, outcrop.y, outcrop.z),
                2 => new float3(outcrop.x, outcrop.y, outcrop.z - oz),
                _ => new float3(outcrop.x - ox, outcrop.y, outcrop.z),
            };
            return BuildGrid.Snap(p, size);
        }

        private static bool Near(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz <= SlotTolerance * SlotTolerance;
        }

        /// <summary>Is (x, z) one of this node's four side slots?</summary>
        private static bool AtSide(float3 node, int2 size, float x, float z, out byte side)
        {
            for (int s = 0; s < SideCount; s++)
            {
                var slot = SideSlot(node, s, size);
                if (Near(slot.x, slot.z, x, z)) { side = (byte)s; return true; }
            }
            side = OnOutcrop;
            return false;
        }

        /// <summary>
        /// The node a post at (<paramref name="x"/>, <paramref name="z"/>)
        /// stands beside: the one whose side slot it stands on. A slot two
        /// nodes share resolves to the node with the lower coordinates, so
        /// every peer names the same one.
        /// </summary>
        public static bool TryGetNodeOf(EntityManager em, float x, float z,
            out Entity node, out float3 nodePos, out byte side, out NodeKind kind)
        {
            node = Entity.Null;
            nodePos = default;
            side = OnOutcrop;
            kind = NodeKind.None;
            var size = BuildingSizeConfig.GetSize(BuildingId);
            var all = AllNodes(em);
            for (int i = 0; i < all.Count; i++)
            {
                var np = all[i].Position;
                if (!AtSide(np, size, x, z, out byte s)) continue;
                if (node != Entity.Null
                    && (np.x > nodePos.x || (np.x == nodePos.x && np.z >= nodePos.z))) continue;
                node = all[i].Entity;
                nodePos = np;
                side = s;
                kind = all[i].Kind;
            }
            return node != Entity.Null;
        }

        /// <summary><see cref="TryGetNodeOf"/> without the kind.</summary>
        public static bool TryGetOutcropOf(EntityManager em, float x, float z,
            out Entity outcrop, out float3 outcropPos, out byte side)
            => TryGetNodeOf(em, x, z, out outcrop, out outcropPos, out side, out _);

        /// <summary>How many Outposts (any owner, finished or not) stand beside
        /// the outcrop at <paramref name="outcropPos"/>.</summary>
        public static int CountPostsAt(EntityManager em, float3 outcropPos, Entity ignore = default)
        {
            var size = BuildingSizeConfig.GetSize(BuildingId);
            var oq = QC_Outposts.Get(em, QT_Outposts);
            using var posts = oq.ToEntityArray(Allocator.Temp);
            using var xfs = oq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < posts.Length; i++)
            {
                if (posts[i] == ignore) continue;
                if (AtSide(outcropPos, size, xfs[i].Position.x, xfs[i].Position.z, out _)) n++;
            }
            return n;
        }

        /// <summary>The faction's own Outpost PLANS beside that outcrop — paid
        /// for already, so they count toward its ramp.</summary>
        private static int CountOwnPlansAt(EntityManager em, Faction faction, float3 outcropPos)
        {
            var pq = QC_Plans.Get(em, QT_Plans);
            if (pq.IsEmptyIgnoreFilter) return 0;
            var size = BuildingSizeConfig.GetSize(BuildingId);
            using var plans = pq.ToEntityArray(Allocator.Temp);
            using var facs = pq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = pq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < plans.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.GetComponentData<PlannedBuilding>(plans[i]).BuildingId != BuildingId) continue;
                if (AtSide(outcropPos, size, xfs[i].Position.x, xfs[i].Position.z, out _)) n++;
            }
            return n;
        }

        /// <summary>Is a side slot already taken by an Outpost?</summary>
        public static bool SlotTaken(EntityManager em, float3 slot)
        {
            var oq = QC_Outposts.Get(em, QT_Outposts);
            using var xfs = oq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
                if (Near(xfs[i].Position.x, xfs[i].Position.z, slot.x, slot.z)) return true;
            return false;
        }

        // ── The per-outcrop cost ramp ─────────────────────────────────────

        /// <summary>
        /// The ramp index a NEW post at <paramref name="slot"/> would take: how
        /// many posts already stand beside its outcrop, plus the faction's own
        /// plans there. 0 when the slot belongs to no outcrop.
        /// </summary>
        public static int RampIndexForNewPost(EntityManager em, Faction faction, float3 slot)
        {
            if (!TryGetOutcropOf(em, slot.x, slot.z, out _, out var op, out _)) return 0;
            return CountPostsAt(em, op) + CountOwnPlansAt(em, faction, op);
        }

        /// <summary>The multiplier at a ramp index, from
        /// TradingOutpostSystem.asset (`outcropRampMultipliers`); past its end
        /// the last entry holds. 1 with no table.</summary>
        public static float RampMultiplier(int index)
        {
            var m = TradingOutpostSystem.Cfg?.outcropRampMultipliers;
            if (m == null || m.Length == 0) return 1f;
            return math.max(0f, m[math.clamp(index, 0, m.Length - 1)]);
        }

        /// <summary>A price scaled by a ramp multiplier, rounded per resource
        /// (BuildCosts' own rounding rule).</summary>
        public static Cost Ramp(Cost c, float m)
        {
            if (m == 1f) return c;
            return Cost.Of(
                supplies:  (int)System.Math.Round(c.Supplies  * (double)m),
                iron:      (int)System.Math.Round(c.Iron      * (double)m),
                veilstone: (int)System.Math.Round(c.Veilstone * (double)m));
        }

        /// <summary>The build price of a new post at <paramref name="slot"/>.</summary>
        public static Cost RampedBuildCost(EntityManager em, Faction faction, float3 slot, Cost baseCost)
            => Ramp(baseCost, RampMultiplier(RampIndexForNewPost(em, faction, slot)));

        /// <summary>A post's own place in its outcrop's ramp — fixed when its
        /// ground was broken (<see cref="TradingOutpostSite.RampIndex"/>).</summary>
        public static int RampIndexOf(EntityManager em, Entity post)
        {
            if (em.HasComponent<TradingOutpostSite>(post))
                return em.GetComponentData<TradingOutpostSite>(post).RampIndex;
            if (!em.HasComponent<LocalTransform>(post)) return 0;
            var p = em.GetComponentData<LocalTransform>(post).Position;
            return TryGetOutcropOf(em, p.x, p.z, out _, out var op, out _)
                ? CountPostsAt(em, op, post) : 0;
        }

        /// <summary>A level-up price on this post: the level SO's price times
        /// the post's ramp multiplier. NOT USED since 2026-10-19: building
        /// levels are faction-wide per type, so a level's price cannot depend
        /// on which post its clock runs on — the ramp prices building a post
        /// only (UpgradeBuildingCommandHelper.TryGetLevelCost).</summary>
        public static Cost RampedUpgradeCost(EntityManager em, Entity post, Cost baseCost)
            => Ramp(baseCost, RampMultiplier(RampIndexOf(em, post)));

        /// <summary>The site record for a post standing at <paramref name="at"/>.</summary>
        public static TradingOutpostSite SiteFor(EntityManager em, Entity post, float3 at)
        {
            if (!TryGetOutcropOf(em, at.x, at.z, out _, out var op, out byte side))
                return new TradingOutpostSite { OutcropX = at.x, OutcropZ = at.z, Side = OnOutcrop, RampIndex = 0 };
            return new TradingOutpostSite
            {
                OutcropX = op.x,
                OutcropZ = op.z,
                Side = side,
                RampIndex = (byte)math.min(255, CountPostsAt(em, op, post)),
            };
        }

        // ── Placement: snap to the nearest free side ─────────────────────

        /// <summary>
        /// The pure side-snap over caller-supplied outcrop and post lists (the
        /// AI's placement snapshot; TerritoryOwnership.SnapAmong forwards
        /// here). The nearest free side slot of any outcrop whose centre is
        /// within <see cref="Reach"/> of <paramref name="pos"/>; ties broken on
        /// the slot's coordinates, never on list order.
        /// </summary>
        internal static bool SnapToSideAmong(float3 pos,
            LocalTransform[] outcrops, int outcropCount,
            LocalTransform[] taken, int takenCount, out float3 snapped)
        {
            snapped = pos;
            float reach = Reach;
            float r2 = reach * reach;
            var size = BuildingSizeConfig.GetSize(BuildingId);
            bool found = false;
            float bestD2 = float.MaxValue;
            float3 best = default;
            for (int i = 0; i < outcropCount; i++)
            {
                var np = outcrops[i].Position;
                float dx = np.x - pos.x, dz = np.z - pos.z;
                if (dx * dx + dz * dz > r2) continue;
                for (int s = 0; s < SideCount; s++)
                {
                    var slot = SideSlot(np, s, size);
                    bool occupied = false;
                    for (int k = 0; k < takenCount && !occupied; k++)
                        occupied = Near(taken[k].Position.x, taken[k].Position.z, slot.x, slot.z);
                    if (occupied) continue;
                    float sx = slot.x - pos.x, sz = slot.z - pos.z;
                    float d2 = sx * sx + sz * sz;
                    if (!found || d2 < bestD2
                        || (d2 == bestD2 && (slot.x < best.x || (slot.x == best.x && slot.z < best.z))))
                    {
                        found = true;
                        bestD2 = d2;
                        best = slot;
                    }
                }
            }
            if (!found) return false;
            snapped = new float3(best.x, pos.y, best.z);
            return true;
        }

        // Live-path scratch (main thread only: the ghost and the router).
        private static readonly System.Collections.Generic.List<float4> _cands =
            new System.Collections.Generic.List<float4>(16);

        /// <summary>
        /// The live side-snap (the placement ghost and the router's issue
        /// check): the nearest side slot, within <see cref="Reach"/>, of an
        /// UNCURSED outcrop that no post takes and the ground accepts
        /// (BuildCommandHelper.CheckBuildPosition — terrain, another node,
        /// another building). Nearest first, so the ghost moves to the next
        /// side only when the nearer one is refused. The snapped point is what
        /// the lockstep command carries, so no peer re-derives it.
        /// </summary>
        public static bool TrySnapToSide(EntityManager em, float3 pos, out float3 snapped)
        {
            snapped = pos;
            float reach = Reach;
            float r2 = reach * reach;
            var size = BuildingSizeConfig.GetSize(BuildingId);

            var all = AllNodes(em);
            var oq = QC_Outposts.Get(em, QT_Outposts);
            using var postXfs = oq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            _cands.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                var np = all[i].Position;
                float dx = np.x - pos.x, dz = np.z - pos.z;
                if (dx * dx + dz * dz > r2) continue;
                if (!IsLiveNode(em, all[i].Entity, all[i].Kind)) continue;
                for (int s = 0; s < SideCount; s++)
                {
                    var slot = SideSlot(np, s, size);
                    bool occupied = false;
                    for (int k = 0; k < postXfs.Length && !occupied; k++)
                        occupied = Near(postXfs[k].Position.x, postXfs[k].Position.z, slot.x, slot.z);
                    if (occupied) continue;
                    float sx = slot.x - pos.x, sz = slot.z - pos.z;
                    _cands.Add(new float4(slot.x, slot.z, sx * sx + sz * sz, 0f));
                }
            }
            // Nearest first, coordinate tie-break.
            _cands.Sort((a, b) =>
            {
                int c = a.z.CompareTo(b.z);
                if (c != 0) return c;
                c = a.x.CompareTo(b.x);
                return c != 0 ? c : a.y.CompareTo(b.y);
            });
            for (int i = 0; i < _cands.Count; i++)
            {
                var slot = new float3(_cands[i].x, pos.y, _cands[i].y);
                if (TheWaningBorder.Core.Commands.Types.BuildCommandHelper.CheckBuildPosition(
                        em, slot, size, BuildingId) != TheWaningBorder.World.Regions.PlacementRefusal.None)
                    continue;
                snapped = slot;
                return true;
            }
            return false;
        }

        // ── Does the post's node still pay? ──────────────────────────────

        /// <summary>
        /// True when a post of <paramref name="faction"/> standing at
        /// (<paramref name="x"/>, <paramref name="z"/>) still has a paying
        /// node: the node it stands beside exists, is not spent, is not Cursed
        /// (an outcrop), and lies in a territory <paramref name="faction"/>
        /// holds (Veilstone_Economy.md §3.1).
        /// </summary>
        public static bool TryGetLiveNode(EntityManager em, float x, float z, Faction faction,
            out Entity node, out NodeKind kind)
        {
            if (!TryGetNodeOf(em, x, z, out node, out var np, out _, out kind)) return false;
            if (!IsLiveNode(em, node, kind)) return false;
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return false;
            int t = TheWaningBorder.World.Regions.RegionMap.RegionAt(np.x, np.z);
            return t >= 0 && TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t) == (int)faction;
        }
    }
}
