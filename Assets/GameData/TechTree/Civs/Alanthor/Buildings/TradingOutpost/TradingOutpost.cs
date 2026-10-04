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
    /// Alanthor Trading Outpost — raised BESIDE a veilstone outcrop, it runs one
    /// of three trades: buy veilstone with supplies and iron (default), forge
    /// veilstone into veilsteel, or sell veilsteel for iron and supplies — the
    /// last two unlocked by its own research. docs/Design/Veilstone_Economy.md §3.1.
    ///
    /// Alanthor do not mine the curse's crystal; they trade in it. The Outpost
    /// is that trade made physical without caravans (Runai's identity) or a
    /// menu nobody can contest: a building in the ground beside the crystal,
    /// which locks its territory and which anyone can burn.
    ///
    /// FOUR SIDES PER OUTCROP (2026-10-04, Veilstone_Economy.md §3.1 and
    /// Build_Grid.md § The Trading Outpost's side slots). An Outpost snaps to
    /// one of the four orthogonal side slots of an uncursed outcrop — north,
    /// east, south, west — its footprint flush against the outcrop's square,
    /// one post per side. The outcrop itself stays an impassable node nobody
    /// builds on. Each further post around the same outcrop costs more to
    /// build and to level (<see cref="RampMultiplier"/>); the first post on a
    /// new outcrop is base price again.
    ///
    /// Placement (<see cref="TrySnapToSide"/> / <see cref="SnapToSideAmong"/>,
    /// reached through TerritoryOwnership.TrySnapToNode and enforced in
    /// CommandRouter.CheckPlaceBuilding).
    /// </summary>
    public static class TradingOutpost
    {
        /// <summary>
        /// Reuses the shared Runai Trading Post / Practice Range visual (355).
        /// ART PASS: give the Outpost its own Alanthor mesh.
        /// </summary>
        public const int PresentationID = 355;

        public const string BuildingId = "Alanthor_TradingOutpost";

        /// <summary>How many posts one outcrop takes: one per side.</summary>
        public const int SideCount = 4;

        /// <summary><see cref="TradingOutpostSite.Side"/> of a post standing on
        /// the outcrop itself (an age-up conversion with no free side).</summary>
        public const byte OnOutcrop = 255;

        /// <summary>Metres within which a position counts as a given slot.
        /// Slots are grid-snapped, so a real match is exact; this only absorbs
        /// float noise.</summary>
        private const float SlotTolerance = 0.5f;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform),
                typeof(FactionTag), typeof(BuildingTag), typeof(Health),
                typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            em.SetComponentData(entity, new LineOfSight { Radius = def.lineOfSight });

            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<TradingOutpostTag>(entity);
            em.AddComponentData(entity, new TradingOutpostMode { Recipe = TradeRecipe.BuyVeilstone });
            em.AddComponent<TradingOutpostCarry>(entity);
            // Hosts its own research: the two trade unlocks and the discount
            // ladder (Research/ beside this file) — Barracks pattern.
            em.AddComponentData(entity, new ProductionState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ProductionQueueItem>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, Defence(def));
            // Levels 1-3 (Civs/Alanthor/Buildings/TradingOutpost/TradingOutpost_Lvl1..3):
            // L1 is granted by BuildingCultureAutoLevelSystem, L2/L3 are bought.
            em.AddComponent<BuildingUpgradeable>(entity);
            em.AddComponentData(entity, SiteFor(em, entity, position));
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = def.lineOfSight });

            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<TradingOutpostTag>(entity);
            ecb.AddComponent(entity, new TradingOutpostMode { Recipe = TradeRecipe.BuyVeilstone });
            ecb.AddComponent<TradingOutpostCarry>(entity);
            ecb.AddComponent(entity, new ProductionState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ProductionQueueItem>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, Defence(def));
            ecb.AddComponent<BuildingUpgradeable>(entity);
            // TradingOutpostSite needs the world to resolve; TradingOutpostSystem
            // stamps it on its next cycle (the ECB path has no EntityManager).
            return entity;
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

        // ── Geometry: the four side slots of an outcrop ─────────────────

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
        static readonly ComponentType[] QT_Capitals =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Outcrops, QC_Outposts, QC_Plans, QC_Capitals;

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

        /// <summary>Is (x, z) one of this outcrop's side slots (or the outcrop
        /// itself, for an age-up conversion that found no free side)?</summary>
        private static bool AtOutcrop(float3 outcrop, int2 size, float x, float z, out byte side)
        {
            for (int s = 0; s < SideCount; s++)
            {
                var slot = SideSlot(outcrop, s, size);
                if (Near(slot.x, slot.z, x, z)) { side = (byte)s; return true; }
            }
            side = OnOutcrop;
            return Near(outcrop.x, outcrop.z, x, z);
        }

        /// <summary>
        /// The outcrop a post at (<paramref name="x"/>, <paramref name="z"/>)
        /// trades at: the one whose side slot it stands on (or, for an age-up
        /// conversion with no free side, the one it stands on). A slot two
        /// outcrops share (they stand two posts apart on one axis) resolves to
        /// the outcrop with the lower coordinates, so every peer names the
        /// same one.
        /// </summary>
        public static bool TryGetOutcropOf(EntityManager em, float x, float z,
            out Entity outcrop, out float3 outcropPos, out byte side)
        {
            outcrop = Entity.Null;
            outcropPos = default;
            side = OnOutcrop;
            var size = BuildingSizeConfig.GetSize(BuildingId);
            var nq = QC_Outcrops.Get(em, QT_Outcrops);
            using var nodes = nq.ToEntityArray(Allocator.Temp);
            using var xfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < nodes.Length; i++)
            {
                var np = xfs[i].Position;
                if (!AtOutcrop(np, size, x, z, out byte s)) continue;
                if (outcrop != Entity.Null
                    && (np.x > outcropPos.x || (np.x == outcropPos.x && np.z >= outcropPos.z))) continue;
                outcrop = nodes[i];
                outcropPos = np;
                side = s;
            }
            return outcrop != Entity.Null;
        }

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
                if (AtOutcrop(outcropPos, size, xfs[i].Position.x, xfs[i].Position.z, out _)) n++;
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
                if (AtOutcrop(outcropPos, size, xfs[i].Position.x, xfs[i].Position.z, out _)) n++;
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
                veilstone: (int)System.Math.Round(c.Veilstone * (double)m),
                veilsteel: (int)System.Math.Round(c.Veilsteel * (double)m));
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
        /// the post's ramp multiplier. Refunds re-derive the same figure.</summary>
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

            var nq = QC_Outcrops.Get(em, QT_Outcrops);
            using var nodes = nq.ToEntityArray(Allocator.Temp);
            using var nodeXfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var oq = QC_Outposts.Get(em, QT_Outposts);
            using var postXfs = oq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            _cands.Clear();
            for (int i = 0; i < nodes.Length; i++)
            {
                var np = nodeXfs[i].Position;
                float dx = np.x - pos.x, dz = np.z - pos.z;
                if (dx * dx + dz * dz > r2) continue;
                if (VeilstoneNodeStateSystem.KindOf(em, nodes[i]) == VeilstoneNodeKind.Cursed) continue;
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

        // ── Is the post's outcrop still a trading partner? ───────────────

        /// <summary>
        /// True when an Outpost standing at (<paramref name="x"/>,
        /// <paramref name="z"/>) still has a trading partner: the outcrop it
        /// stands beside exists and is not Cursed. A cursed outcrop idles
        /// EVERY post around it until it is pacified (Veilstone_Economy.md
        /// §3.1); a Depleted one trades as well as a fresh one.
        /// </summary>
        public static bool HasLiveOutcrop(EntityManager em, float x, float z)
        {
            if (!TryGetOutcropOf(em, x, z, out var outcrop, out _, out _)) return false;
            return VeilstoneNodeStateSystem.KindOf(em, outcrop) != VeilstoneNodeKind.Cursed;
        }

        // ── Age-up: every Alanthor Veilstone Mine becomes a Trading Outpost ──

        static readonly ComponentType[] QT_VeilstoneMines =
        {
            ComponentType.ReadOnly<VeilstoneMineTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_VeilstoneMines;

        /// <summary>
        /// ALANTHOR DO NOT MINE VEILSTONE, so at age-up every Veilstone Mine the
        /// faction owns becomes a Trading Outpost (Veilstone_Economy.md §3.1).
        /// Iron Mines stay as they are.
        ///
        /// A TRANSFORM, not a replacement — the same entity, health fraction,
        /// owner and territory lock — the rule the Hall and the House already
        /// follow. What changes is WHERE it stands: the mine stood ON its
        /// outcrop, the Outpost stands BESIDE it, on the free side slot nearest
        /// the faction's capital (ties: north, east, south, west). With no free
        /// side the Outpost stays on the outcrop. The mine's level ladder does
        /// not carry over; the Outpost starts its own at L1 (granted by
        /// BuildingCultureAutoLevelSystem).
        ///
        /// Idempotent and deterministic (query order, sim state only), so it is
        /// safe from both the age-up and StartAgePromoter on every peer.
        /// </summary>
        public static void ConvertMinesForCulture(EntityManager em, Faction faction, byte culture)
        {
            if (culture != Cultures.Alanthor) return;
            var targets = new NativeList<Entity>(Allocator.Temp);
            Collect(em, QC_VeilstoneMines.Get(em, QT_VeilstoneMines), faction, targets);
            if (targets.Length == 0) { targets.Dispose(); return; }

            var def = TechCatalog.Building(BuildingId);
            var size = BuildingSizeConfig.GetSize(BuildingId);
            bool haveCapital = TryGetCapital(em, faction, out float3 capital);
            int moved = 0, stayed = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                var e = targets[i];
                if (em.HasComponent<VeilstoneMineTag>(e)) em.RemoveComponent<VeilstoneMineTag>(e);
                if (em.HasComponent<BuildingUpgradeState>(e)) em.RemoveComponent<BuildingUpgradeState>(e);
                // A level-up queued on the mine has nothing left to level.
                if (em.HasBuffer<ProductionQueueItem>(e)) em.GetBuffer<ProductionQueueItem>(e).Clear();

                em.AddComponent<TradingOutpostTag>(e);
                em.AddComponentData(e, new TradingOutpostMode { Recipe = TradeRecipe.BuyVeilstone });
                if (!em.HasComponent<TradingOutpostCarry>(e)) em.AddComponent<TradingOutpostCarry>(e);
                if (!em.HasComponent<ProductionState>(e))
                    em.AddComponentData(e, new ProductionState { Busy = 0, Remaining = 0 });
                if (!em.HasBuffer<ProductionQueueItem>(e)) em.AddBuffer<ProductionQueueItem>(e);
                if (!em.HasComponent<BuildingUpgradeable>(e)) em.AddComponent<BuildingUpgradeable>(e);
                em.SetComponentData(e, new PresentationId { Id = PresentationID });
                if (em.HasComponent<BuildingSize>(e))
                    em.SetComponentData(e, new BuildingSize { Width = size.x, Height = size.y });
                if (em.HasComponent<DisplayName>(e))
                    em.SetComponentData(e, new DisplayName
                    {
                        Value = TheWaningBorder.Core.DisplayNames.ForBuildingFixed(BuildingId),
                    });
                if (em.HasComponent<Health>(e) && def.hp > 0)
                {
                    var h = em.GetComponentData<Health>(e);
                    float frac = h.Max > 0 ? (float)h.Value / h.Max : 1f;
                    int max = (int)def.hp;
                    em.SetComponentData(e, new Health { Value = math.max(1, (int)math.round(max * frac)), Max = max });
                }

                // Off the outcrop, onto a side.
                var xf = em.GetComponentData<LocalTransform>(e);
                float3 outcrop = xf.Position;
                if (VeilstoneNodeStateSystem.TryGetOutcropAt(em, xf.Position.x, xf.Position.z,
                        BuildGrid.ResourceNodeMeters, out var node, out _))
                    outcrop = em.GetComponentData<LocalTransform>(node).Position;

                // Pass 0 keeps the post in the outcrop's own territory (a side
                // across a border would lock and pay the wrong ground); pass 1
                // takes any legal side when no same-territory one is free.
                byte side = OnOutcrop;
                float bestD2 = float.MaxValue;
                float3 bestSlot = default;
                int home = TheWaningBorder.World.Regions.RegionMap.Ready
                    ? TheWaningBorder.World.Regions.RegionMap.RegionAt(outcrop.x, outcrop.z) : -1;
                for (int pass = 0; pass < 2 && side == OnOutcrop; pass++)
                {
                    for (int s = 0; s < SideCount; s++)
                    {
                        var slot = SideSlot(outcrop, s, size);
                        if (pass == 0 && home >= 0
                            && TheWaningBorder.World.Regions.RegionMap.RegionAt(slot.x, slot.z) != home) continue;
                        if (SlotTaken(em, slot)) continue;
                        if (TheWaningBorder.Core.Commands.Types.BuildCommandHelper.CheckBuildPosition(
                                em, slot, size, BuildingId) != TheWaningBorder.World.Regions.PlacementRefusal.None)
                            continue;
                        float d2 = 0f;
                        if (haveCapital)
                        {
                            float dx = slot.x - capital.x, dz = slot.z - capital.z;
                            d2 = dx * dx + dz * dz;
                        }
                        if (side == OnOutcrop || d2 < bestD2) { side = (byte)s; bestD2 = d2; bestSlot = slot; }
                    }
                }

                if (side != OnOutcrop)
                {
                    float y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(bestSlot.x, bestSlot.z);
                    xf.Position = new float3(bestSlot.x, y, bestSlot.z);
                    em.SetComponentData(e, xf);
                    moved++;
                }
                else
                {
                    stayed++;
                    UnityEngine.Debug.LogWarning(
                        $"[AgeUp] {faction}: the Veilstone Mine at ({outcrop.x:0},{outcrop.z:0}) has no free side " +
                        "— its Trading Outpost stays on the outcrop.");
                }

                var site = new TradingOutpostSite
                {
                    OutcropX = outcrop.x,
                    OutcropZ = outcrop.z,
                    Side = side,
                    RampIndex = (byte)math.min(255, CountPostsAt(em, outcrop, e)),
                };
                if (em.HasComponent<TradingOutpostSite>(e)) em.SetComponentData(e, site);
                else em.AddComponentData(e, site);
            }
            // The nav cost field restamps on a change in the SET of buildings,
            // not on a move — ask for one so the posts block their new cells.
            if (moved > 0) TheWaningBorder.Systems.Navigation.CostFieldStampSystem.RequestRestamp();
            UnityEngine.Debug.Log($"[AgeUp] {faction}: {targets.Length} Veilstone Mine(s) became Trading Outposts " +
                                  $"(Alanthor) — {moved} moved beside their outcrop, {stayed} left on it.");
            targets.Dispose();
        }

        private static bool TryGetCapital(EntityManager em, Faction faction, out float3 pos)
        {
            pos = default;
            var q = QC_Capitals.Get(em, QT_Capitals);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            bool found = false;
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var p = xfs[i].Position;
                // Deterministic among several Fortresses: the lowest coordinates.
                if (!found || p.x < pos.x || (p.x == pos.x && p.z < pos.z)) { pos = p; found = true; }
            }
            return found;
        }

        private static void Collect(EntityManager em, EntityQuery q, Faction faction, NativeList<Entity> into)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction) into.Add(ents[i]);
        }

        /// <summary>The trade this Outpost runs (Buy Veilstone if unset).</summary>
        public static TradeRecipe RecipeOf(EntityManager em, Entity outpost)
            => em.HasComponent<TradingOutpostMode>(outpost)
               ? em.GetComponentData<TradingOutpostMode>(outpost).Recipe
               : TradeRecipe.BuyVeilstone;
    }
}
