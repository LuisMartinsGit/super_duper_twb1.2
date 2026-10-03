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
    /// Placement (<see cref="TryFindOutcrop"/>, enforced in
    /// CommandRouter.CheckPlaceBuilding): centre within the config's reach of
    /// an Inactive or Depleted outcrop that no other Outpost already serves.
    /// The footprint may not cover the outcrop itself — the ordinary
    /// OnResourceNode rule refuses that.
    /// </summary>
    public static class TradingOutpost
    {
        /// <summary>
        /// Reuses the shared Runai Trading Post / Practice Range visual (355).
        /// ART PASS: give the Outpost its own Alanthor mesh.
        /// </summary>
        public const int PresentationID = 355;

        public const string BuildingId = "Alanthor_TradingOutpost";

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

        // ── Placement: which outcrop an Outpost here would serve ─────────

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
        static CachedEntityQuery QC_Outcrops, QC_Outposts;

        /// <summary>Metres from an Outpost's centre to the outcrop it serves.</summary>
        public static float Reach => TradingOutpostSystem.Cfg.nodeReach;

        /// <summary>
        /// The outcrop an Outpost centred at (<paramref name="x"/>,
        /// <paramref name="z"/>) would serve: the nearest Inactive or Depleted
        /// outcrop within <see cref="Reach"/> that no other Outpost already
        /// serves. <paramref name="ignore"/> is an Outpost to leave out of the
        /// "already served" test (the Outpost asking about itself).
        ///
        /// Deterministic: nearest wins, ties broken on the outcrop's own
        /// coordinates, never on entity order.
        /// </summary>
        public static bool TryFindOutcrop(EntityManager em, float x, float z,
            out Entity outcrop, out float3 outcropPos, Entity ignore = default)
        {
            outcrop = Entity.Null;
            outcropPos = default;
            float reach = Reach;
            float r2 = reach * reach;

            var nq = QC_Outcrops.Get(em, QT_Outcrops);
            using var nodes = nq.ToEntityArray(Allocator.Temp);
            using var nodeXfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var oq = QC_Outposts.Get(em, QT_Outposts);
            using var posts = oq.ToEntityArray(Allocator.Temp);
            using var postXfs = oq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float best = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                var np = nodeXfs[i].Position;
                float dx = np.x - x, dz = np.z - z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                if (VeilstoneNodeStateSystem.KindOf(em, nodes[i]) == VeilstoneNodeKind.Cursed) continue;

                bool served = false;
                for (int k = 0; k < posts.Length && !served; k++)
                {
                    if (posts[k] == ignore) continue;
                    float ex = postXfs[k].Position.x - np.x, ez = postXfs[k].Position.z - np.z;
                    served = ex * ex + ez * ez <= r2;
                }
                if (served) continue;

                if (outcrop == Entity.Null || d2 < best
                    || (d2 == best && (np.x < outcropPos.x || (np.x == outcropPos.x && np.z < outcropPos.z))))
                {
                    best = d2;
                    outcrop = nodes[i];
                    outcropPos = np;
                }
            }
            return outcrop != Entity.Null;
        }

        /// <summary>
        /// True when an Outpost standing at (<paramref name="x"/>,
        /// <paramref name="z"/>) still has a trading partner: an uncursed
        /// veilstone outcrop within reach (one converted from a Veilstone Mine
        /// stands on its own). A cursed outcrop idles its Outpost until it is
        /// pacified (Veilstone_Economy.md §3.1).
        /// </summary>
        public static bool HasLiveOutcrop(EntityManager em, float x, float z)
        {
            float reach = Reach;
            float r2 = reach * reach;
            var nq = QC_Outcrops.Get(em, QT_Outcrops);
            using var nodes = nq.ToEntityArray(Allocator.Temp);
            using var nodeXfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < nodes.Length; i++)
            {
                var np = nodeXfs[i].Position;
                float dx = np.x - x, dz = np.z - z;
                if (dx * dx + dz * dz > r2) continue;
                if (VeilstoneNodeStateSystem.KindOf(em, nodes[i]) != VeilstoneNodeKind.Cursed)
                    return true;
            }

            return false;
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
        /// A TRANSFORM, not a replacement — the same entity, position, health
        /// fraction and territory lock — the rule the Hall and the House already
        /// follow. The mine's level ladder goes with it: the Outpost has none.
        ///
        /// Idempotent and deterministic (query order, sim state only), so it is
        /// safe from both the age-up and StartAgePromoter on every peer.
        /// </summary>
        public static void ConvertMinesForCulture(EntityManager em, Faction faction, byte culture)
        {
            if (culture != Cultures.Alanthor) return;
            var targets = new NativeList<Entity>(Allocator.Temp);
            Collect(em, QC_VeilstoneMines.Get(em, QT_VeilstoneMines), faction, targets);

            var def = TechCatalog.Building(BuildingId);
            for (int i = 0; i < targets.Length; i++)
            {
                var e = targets[i];
                if (em.HasComponent<VeilstoneMineTag>(e)) em.RemoveComponent<VeilstoneMineTag>(e);
                if (em.HasComponent<BuildingUpgradeable>(e)) em.RemoveComponent<BuildingUpgradeable>(e);
                if (em.HasComponent<BuildingUpgradeState>(e)) em.RemoveComponent<BuildingUpgradeState>(e);
                // A level-up queued on the mine has nothing left to level.
                if (em.HasBuffer<ProductionQueueItem>(e)) em.GetBuffer<ProductionQueueItem>(e).Clear();

                em.AddComponent<TradingOutpostTag>(e);
                em.AddComponentData(e, new TradingOutpostMode { Recipe = TradeRecipe.BuyVeilstone });
                if (!em.HasComponent<TradingOutpostCarry>(e)) em.AddComponent<TradingOutpostCarry>(e);
                if (!em.HasComponent<ProductionState>(e))
                    em.AddComponentData(e, new ProductionState { Busy = 0, Remaining = 0 });
                if (!em.HasBuffer<ProductionQueueItem>(e)) em.AddBuffer<ProductionQueueItem>(e);
                em.SetComponentData(e, new PresentationId { Id = PresentationID });
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
            }
            if (targets.Length > 0)
                UnityEngine.Debug.Log($"[AgeUp] {faction}: {targets.Length} Veilstone Mine(s) became Trading Outposts (Alanthor).");
            targets.Dispose();
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
