using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIAlanthorEndgameSystem"/>. The asset is AIAlanthorEndgameSystem.asset,
    /// beside AIAlanthorEndgameSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIAlanthorEndgameSystem",
                     fileName = "AIAlanthorEndgameSystem")]
    public sealed class AIAlanthorEndgameSystemConfig : ScriptableObject, IComponentConfig
    {
        static AIAlanthorEndgameSystemConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIAlanthorEndgameSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIAlanthorEndgameSystemConfig>());

        /// <summary>Bodyguards dispatched alongside a well ritualist.
        /// HEAVY (2026-08-04, was 5): the node births defenders at the
        /// channeling Scholar — a token screen kept losing the ritual.</summary>
        public int escortSize;

        /// <summary>Endgame Smelter fleet target — matches
        /// CommandRouter.MaxSmeltersPerFaction. Five L3 Forges = 15
        /// veilsteel / 10 s, the ceiling of the veilsteel economy.</summary>
        public int smelterTarget;

        /// <summary>Endgame housing target: 8 Huts. They auto-level to House
        /// L1 under culture (BuildingCultureAutoLevelSystem); the
        /// AIBuildingUpgradeSystem rotation takes them on to L3.</summary>
        public int houseTarget;

        // Worker flee tuning. Miners and builders run home if any enemy
        // unit is within FleeRadius. Throttled per worker so we don't
        // spam MoveCommands every tick once a threat is committed.
        public float fleeRadius;

        /// <summary>Alive-or-queued cap per sect for the chapel unit
        /// (docs/Design/Sect_Units.md) — elite specialists, not a line.</summary>
        public int sectUnitCap;

        /// <summary>
        /// How many of one sect's buildings the AI aims for. The hard cap is 5
        /// (SectBuilding.CapPerFaction); two is what the AI actually wants --
        /// one is a single point of failure for the sect's unit and research,
        /// five is an economy the AI cannot afford alongside its army.
        /// </summary>
        public int sectBuildingTarget;

        // Own towers may never stand closer than this — 1.6× the 15 m
        // influence radius, so their build-space circles TILE new ground
        // instead of stacking (the old ring placement produced 4-in-a-row).
        public float minTowerSpacing;

        // A corridor narrower than this along the enemy approach counts as
        // a chokepoint worth fortifying.
        public float chokeWidthThreshold;

        /// <summary>Nearest resource node (veilstone or iron) within
        /// <see cref="UnprotectedNodeRadius"/> of the hall whose ground this
        /// faction's influence does NOT yet cover — the next tower anchor.</summary>
        public float unprotectedNodeRadius;

        /// <summary>A Gatherer's Hut counts as tower-covered when a friendly
        /// Watch Tower stands within this range.</summary>
        public float hutCoverageRadius;

        /// <summary>Hard cap on doctrine-built wall hubs per faction —
        /// sized for a full max-extent perimeter (4 x 124 m / 12.5 m).</summary>
        public int maxWallHubs;

        /// <summary>Hub / instance self-build time — mirrors
        /// BuilderCommandPanel.WallExtendBuildSeconds (30 s, AutoConstructTag,
        /// no builder dispatched).</summary>
        public float wallHubBuildSeconds;

        /// <summary>A plan slot with a friendly hub within this range counts
        /// as filled. Must exceed the largest placement nudge (perp * 5 m) or
        /// a hub that dodged a rock stops counting as its own slot's
        /// occupant: the doctrine then re-places it forever and the
        /// gap-closing pass below never sees the slot as filled. Plan
        /// spacing is 30 m, so 7 m cannot claim a neighbour's slot.</summary>
        public float wallSlotOccupiedRadius;

        // Tick interval — slow strategic loop.
        public float thinkInterval;

        // Train-queue cap per Stable / SiegeYard. Mirrors SimpleAISystem.
        public int maxTrainQueue;

        // Strategy switch threshold: number of armies lost without dealing
        // significant damage since the last strategy switch before we flip
        // to Defensive. Cheap signal — armies-lost is bumped by combat
        // bookkeeping elsewhere; we just react to it.
        public int lossesBeforeDefensiveFlip;
    }
}
