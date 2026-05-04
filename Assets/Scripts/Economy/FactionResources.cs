// FactionResources.cs
// ECS components for faction resource and income tracking
// Part of: Economy/

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Economy
{
    // ═══════════════════════════════════════════════════════════════════════
    // FACTION RESOURCES COMPONENT
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Stores the current resource amounts for a faction.
    /// Attached to a "bank" entity alongside FactionTag.
    /// </summary>
    public struct FactionResources : IComponentData
    {
        /// <summary>Hard cap for every resource type</summary>
        public const int ResourceCap = 100_000;

        /// <summary>Basic resource - used for most construction and training</summary>
        public int Supplies;

        /// <summary>Industrial resource - used for military and advanced structures</summary>
        public int Iron;

        /// <summary>Magical resource - used for temples and magical units</summary>
        public int Crystal;

        /// <summary>Rare resource - used for elite units and advanced technologies</summary>
        public int Veilsteel;

        /// <summary>Energy resource - used for special abilities and ultimate powers</summary>
        public int Glow;

        // ==================== Helpers ====================

        /// <summary>
        /// Clamp every resource to the range [0, ResourceCap]. Burst-safe.
        ///
        /// Fix #216: previously this only capped at ResourceCap and allowed
        /// negative values to slip through when direct component mutations
        /// (MiningSystem, CrystalMiningSystem, etc.) bypassed FactionEconomy.
        /// Spend's balance check. A negative bank could cascade into integer
        /// underflow and nonsensical UI. The lower bound is now enforced here
        /// as a defensive safety net.
        /// </summary>
        public void Clamp()
        {
            if (Supplies  > ResourceCap) Supplies  = ResourceCap;
            if (Iron      > ResourceCap) Iron      = ResourceCap;
            if (Crystal   > ResourceCap) Crystal   = ResourceCap;
            if (Veilsteel > ResourceCap) Veilsteel = ResourceCap;
            if (Glow      > ResourceCap) Glow      = ResourceCap;

            if (Supplies  < 0) Supplies  = 0;
            if (Iron      < 0) Iron      = 0;
            if (Crystal   < 0) Crystal   = 0;
            if (Veilsteel < 0) Veilsteel = 0;
            if (Glow      < 0) Glow      = 0;
        }
        
        /// <summary>
        /// Create resources with specified values.
        /// </summary>
        public static FactionResources Of(int supplies = 0, int iron = 0, int crystal = 0,
                                          int veilsteel = 0, int glow = 0)
        {
            return new FactionResources
            {
                Supplies = supplies,
                Iron = iron,
                Crystal = crystal,
                Veilsteel = veilsteel,
                Glow = glow
            };
        }
        
        /// <summary>
        /// Check if faction has at least the specified resources.
        /// </summary>
        public bool HasAtLeast(int supplies = 0, int iron = 0, int crystal = 0,
                               int veilsteel = 0, int glow = 0)
        {
            return Supplies >= supplies &&
                   Iron >= iron &&
                   Crystal >= crystal &&
                   Veilsteel >= veilsteel &&
                   Glow >= glow;
        }
        
        /// <summary>
        /// Get total resource value (simple weighted sum).
        /// </summary>
        public int TotalValue => Supplies + (Iron * 2) + (Crystal * 3) + 
                                 (Veilsteel * 5) + (Glow * 4);
        
        public override string ToString()
        {
            return $"S:{Supplies} Fe:{Iron} Cr:{Crystal} Vs:{Veilsteel} Gl:{Glow}";
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // FACTION ERA & RELIGION POINTS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks the current era of a faction (1-5).
    /// Era 1 = start, Era 2 = culture chosen, Era 3-5 = temple level-ups.
    /// Attached to faction bank entity alongside FactionResources.
    /// </summary>
    public struct FactionEra : IComponentData
    {
        /// <summary>Current era (1-5)</summary>
        public int Value;
    }

    // The old `ReligionPoints { int Value }` component was removed in
    // task-063 phase 1. Religion Points now live on FactionReligionPoints
    // (see Economy/FactionReligionPoints.cs) which carries the Shrine-bonus
    // latch and CurrentAge needed for the age-gated upgrade rules. All
    // callers were migrated; readers of RP balance go through
    // FactionReligionPointsHelper.GetBalance.

    // ═══════════════════════════════════════════════════════════════════════
    // RESOURCE TICK STATE
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Tracks the last game-time second when resource income was applied.
    /// Used by ApplySuppliesIncomeSystem to prevent duplicate income ticks.
    /// </summary>
    public struct ResourceTickState : IComponentData
    {
        /// <summary>The floor(ElapsedTime) value when income was last applied</summary>
        public int LastWholeSecond;
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // INCOME PROVIDERS
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Attach to any building that provides passive Supplies income.
    /// Uses discrete ticks: delivers PerTick supplies every Interval seconds.
    /// Example: Hall delivers 50 supplies every 15 seconds.
    /// </summary>
    public struct SuppliesIncome : IComponentData
    {
        /// <summary>Supplies delivered per tick (may be modified by area ratio for GathererHuts)</summary>
        public float PerTick;

        /// <summary>Seconds between each tick</summary>
        public float Interval;

        /// <summary>Timer accumulator (advanced each frame, resets on tick)</summary>
        public float Elapsed;

        /// <summary>Equivalent per-minute rate (for display purposes)</summary>
        public float PerMinute => Interval > 0 ? (PerTick / Interval * 60f) : 0f;
    }
    
    // Fix #217: Iron/Crystal/Veilsteel/Glow income used to expose
    // `int PerSecond => PerMinute / 60;` which truncated to zero for any
    // PerMinute below 60. A building producing 30 iron/min reported 0/sec
    // and never actually delivered anything. The fractional accumulator
    // lets the ResourceTickSystem ticker integrate the true rate across
    // multiple 1-second ticks and deliver whole units as they build up.

    /// <summary>
    /// Attach to any building that provides passive Iron income.
    /// Example: Foundry provides iron from nearby deposits.
    /// </summary>
    public struct IronIncome : IComponentData
    {
        /// <summary>Iron generated per minute</summary>
        public int PerMinute;

        /// <summary>Fractional accumulator (see Fix #217) — integrates sub-unit income across ticks.</summary>
        public float FractionalAccumulator;
    }

    /// <summary>
    /// Attach to any building that provides passive Crystal income.
    /// Example: Crystal Shrine generates crystal over time.
    /// </summary>
    public struct CrystalIncome : IComponentData
    {
        /// <summary>Crystal generated per minute</summary>
        public int PerMinute;

        /// <summary>Fractional accumulator (see Fix #217).</summary>
        public float FractionalAccumulator;
    }

    /// <summary>
    /// Attach to any building that provides passive Veilsteel income.
    /// Example: Advanced smeltery with veilsteel processing.
    /// </summary>
    public struct VeilsteelIncome : IComponentData
    {
        /// <summary>Veilsteel generated per minute</summary>
        public int PerMinute;

        /// <summary>Fractional accumulator (see Fix #217).</summary>
        public float FractionalAccumulator;
    }

    /// <summary>
    /// Attach to any building that provides passive Glow income.
    /// Example: Ley line nexus building.
    /// </summary>
    public struct GlowIncome : IComponentData
    {
        /// <summary>Glow generated per minute</summary>
        public int PerMinute;

        /// <summary>Fractional accumulator (see Fix #217).</summary>
        public float FractionalAccumulator;
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // RESOURCE HELPERS
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Static helper methods for working with faction resources.
    /// </summary>
    public static class FactionResourcesHelper
    {
        // Fix #206: cache the query so repeated UI calls don't allocate
        // native memory every time. Invalidated by TryGetFactionResources
        // when the world changes out from under it (world recreation).
        private static EntityQuery _cachedQuery;
        private static EntityManager _queryOwner;

        /// <summary>
        /// Clear the cached query. Call on world reset so a stale query
        /// bound to a destroyed world is not reused.
        /// </summary>
        public static void ClearCache()
        {
            _cachedQuery = default;
            _queryOwner = default;
        }

        /// <summary>
        /// Get resources for a specific faction.
        /// </summary>
        public static bool TryGetFactionResources(Faction faction, out FactionResources resources)
        {
            resources = default;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return false;

            var em = world.EntityManager;

            // Rebuild the query if this is the first call or the world changed.
            if (!_queryOwner.Equals(em))
            {
                _cachedQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<FactionResources>()
                );
                _queryOwner = em;
            }

            using var entities = _cachedQuery.ToEntityArray(Allocator.Temp);
            using var tags = _cachedQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var resourceData = _cachedQuery.ToComponentDataArray<FactionResources>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                {
                    resources = resourceData[i];
                    return true;
                }
            }

            return false;
        }
        
        /// <summary>
        /// Get a formatted display string for resources.
        /// </summary>
        public static string GetDisplayString(FactionResources res)
        {
            return $"📦{res.Supplies} ⚙️{res.Iron} 💎{res.Crystal} ⚫{res.Veilsteel} ✨{res.Glow}";
        }
    }
}