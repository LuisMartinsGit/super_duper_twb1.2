// SimpleAISystem.VictoryPlan.cs
// THE THREE ROADS TO VICTORY (docs/Design/Game_AI.md § 6aa, 2026-11-05):
// Shardroot ascension, Sanctum dominion, military conquest.
//
//   Easy, Normal   one road at a time, chosen by personality, by opportunity
//                  and in reaction to the other players.
//   Hard, Expert   every road at once (Mixed), until one is clearly the best
//                  chance, then that road comes first.
//
// The plan decides nothing about HOW a road is walked. It only opens and closes
// what already exists: the waves, the Sanctum armies, the monument squads, the
// towers. Each personality weighs the roads differently (AISettings.asset).
//
// Lockstep: only sim state and the known board are read, on the sim clock.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Economy;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private enum VictoryPath : byte { Mixed = 0, Conquest = 1, Sanctums = 2, Shardroot = 3 }

        private sealed class VictoryPlanState
        {
            public VictoryPath Path;
            public float ChosenAt = float.MinValue;
            public float NextThink;
            /// <summary>This faction holds the artifact (carried or enshrined).</summary>
            public bool HoldsArtifact;
            /// <summary>The enemy's soldiers stand on its Sanctums: its base is the target.</summary>
            public bool StrikeHome;
            public Faction StrikeVictim;
        }

        private readonly Dictionary<int, VictoryPlanState> _victoryPlans = new Dictionary<int, VictoryPlanState>();
        private int _victoryPlanEpoch = -1;

        private static readonly ComponentType[] QT_VictoryShardroot = { ComponentType.ReadOnly<ShardrootState>() };
        private static CachedEntityQuery QC_VictoryShardroot;

        private VictoryPlanState VictoryPlanOf(Faction faction)
        {
            if (_victoryPlanEpoch != SimCadence.Epoch)
            {
                _victoryPlanEpoch = SimCadence.Epoch;
                _victoryPlans.Clear();
            }
            if (!_victoryPlans.TryGetValue((int)faction, out var p))
            {
                p = new VictoryPlanState();
                _victoryPlans[(int)faction] = p;
            }
            return p;
        }

        // ── What the rest of the AI asks ──────────────────────────────────

        /// <summary>No new wave and no reinforcement: this road does not go
        /// through the enemy's army.</summary>
        private bool VictoryHoldsWaves(Faction faction)
        {
            if (!Cfg.victoryPlanEnabled) return false;
            var p = VictoryPlanOf(faction);
            if (p.Path == VictoryPath.Sanctums) return true;
            if (p.Path != VictoryPath.Shardroot) return false;
            return p.HoldsArtifact || !ProfileOf(faction).VictoryMultiFocus;
        }

        /// <summary>May a NEW Sanctum army be raised? (Those standing stay.)</summary>
        private bool VictoryAllowsSanctumArmy(Faction faction)
        {
            if (!Cfg.victoryPlanEnabled) return true;
            var p = VictoryPlanOf(faction);
            return p.Path == VictoryPath.Sanctums || p.Path == VictoryPath.Mixed;
        }

        /// <summary>May a NEW monument squad march? Exploring is always allowed.</summary>
        private bool VictoryAllowsMonumentSquad(Faction faction)
        {
            if (!Cfg.victoryPlanEnabled) return true;
            var p = VictoryPlanOf(faction);
            return p.Path == VictoryPath.Shardroot || p.Path == VictoryPath.Mixed;
        }

        /// <summary>Fortify and ascend: the tower budget while it holds the artifact.</summary>
        private float VictoryTowerScale(Faction faction)
        {
            if (!Cfg.victoryPlanEnabled) return 1f;
            var p = VictoryPlanOf(faction);
            return p.Path == VictoryPath.Shardroot && p.HoldsArtifact ? Cfg.victoryFortifyTowerScale : 1f;
        }

        /// <summary>The enemy's soldiers stand on its Sanctums, so the next wave
        /// goes for its capital.</summary>
        private bool VictoryStrikeHome(Faction faction, out Faction victim)
        {
            victim = faction;
            if (!Cfg.victoryPlanEnabled) return false;
            var p = VictoryPlanOf(faction);
            if (!p.StrikeHome || p.Path != VictoryPath.Conquest) return false;
            victim = p.StrikeVictim;
            return true;
        }

        // ── The decision ──────────────────────────────────────────────────

        private void TickVictoryPlan(EntityManager em, Entity brain, Faction faction, float now)
        {
            if (!Cfg.victoryPlanEnabled) return;
            var plan = VictoryPlanOf(faction);
            if (now < plan.NextThink) return;
            plan.NextThink = now + math.max(1f, Cfg.victoryThinkInterval);

            bool multi = ProfileOf(faction).VictoryMultiFocus;
            var row = PersonalityOf(faction);

            ReadShardroot(em, faction, out bool mine, out bool rivalHolds, out bool found);
            plan.HoldsArtifact = mine;

            int sanctums = TerritoryResources.TerritoriesOf(RegionSeedMarker.ResourceType.Sanctum).Count;
            int held = SanctumSystem.HeldBy(faction);
            bool rivalDominion = false;
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var f = (Faction)i;
                if (f == faction || !Alliances.AreHostile(faction, f)) continue;
                if (sanctums > 0 && SanctumSystem.HeldBy(f) >= sanctums) rivalDominion = true;
            }

            // ── Forced: these leave no choice. ──
            VictoryPath forced = VictoryPath.Mixed;
            string why = null;
            if (mine)
            {
                bool bound = AIShardroot.Locate(em, faction, out Entity holder, out _) == AIShardroot.Where.Mine
                    && em.HasComponent<ShardboundHeroTag>(holder);
                forced = bound ? VictoryPath.Conquest : VictoryPath.Shardroot;
                why = bound ? "the king carries the Shardroot: he leads the war" : "we hold the Shardroot: fortify and ascend";
            }
            else if (rivalHolds) { forced = VictoryPath.Conquest; why = "a rival holds the Shardroot: everyone against it"; }
            else if (rivalDominion) { forced = VictoryPath.Sanctums; why = "a rival holds every Sanctum: take one back"; }
            else if (FinishingVictim(faction, out _)) { forced = VictoryPath.Conquest; why = "a rival is beaten: finish it"; }

            plan.StrikeHome = false;
            float conquest = ConquestChance(em, brain, faction, plan);
            float sanctum = sanctums > 0 ? Cfg.victorySanctumBase + (float)held / sanctums : 0f;
            float shardroot = found && !mine ? 0f : ShardrootChance(em, faction);

            float wc = conquest * row.victoryConquestWeight;
            float ws = sanctum * row.victorySanctumWeight;
            float wa = shardroot * row.victoryShardrootWeight;

            VictoryPath best = VictoryPath.Conquest;
            float bestScore = wc;
            if (ws > bestScore) { best = VictoryPath.Sanctums; bestScore = ws; }
            if (wa > bestScore) { best = VictoryPath.Shardroot; bestScore = wa; }
            if (multi && bestScore < Cfg.victoryPrioritizeScore) best = VictoryPath.Mixed;

            VictoryPath next;
            if (forced != VictoryPath.Mixed)
            {
                next = forced;
            }
            else
            {
                next = best;
                float current = plan.Path == VictoryPath.Conquest ? wc
                    : plan.Path == VictoryPath.Sanctums ? ws
                    : plan.Path == VictoryPath.Shardroot ? wa : 0f;
                bool settled = plan.ChosenAt > float.MinValue && now - plan.ChosenAt < Cfg.victoryMinHoldSeconds;
                bool keep = plan.ChosenAt > float.MinValue && plan.Path != VictoryPath.Mixed
                    && next != VictoryPath.Mixed && bestScore < current * Cfg.victorySwitchMargin;
                if (settled || keep) next = plan.Path;
                if (next == VictoryPath.Conquest && plan.StrikeHome) why = "its soldiers stand on the Sanctums: strike its base";
            }

            if (next != plan.Path || plan.ChosenAt == float.MinValue)
            {
                var was = plan.Path;
                plan.Path = next;
                plan.ChosenAt = now;
                AILogger.Log(faction, "PLAN",
                    $"{was} -> {next} ({(multi ? "all roads" : "one road")}): {why ?? "best chance"}; " +
                    $"conquest {wc:0.00} sanctums {ws:0.00} ({held}/{sanctums}) shardroot {wa:0.00}");
                if (next == VictoryPath.Sanctums || (next == VictoryPath.Shardroot && (mine || !multi)))
                    CallOffAttacks(faction, now);
            }
        }

        private static void ReadShardroot(EntityManager em, Faction faction, out bool mine, out bool rivalHolds, out bool found)
        {
            mine = false; rivalHolds = false; found = false;
            var q = QC_VictoryShardroot.Get(em, QT_VictoryShardroot);
            if (q.IsEmptyIgnoreFilter) return;
            using var states = q.ToComponentDataArray<ShardrootState>(Allocator.Temp);
            var s = states[0];
            found = s.Found != 0 && !TheWaningBorder.Entities.ShardrootPickup.AnyHiddenFrom(em, faction);
            if (!found) return;
            var holder = s.AscensionFaction != Faction.Border ? s.AscensionFaction : s.HolderFaction;
            if (holder == Faction.Border) return;
            mine = holder == faction;
            rivalHolds = !mine && Alliances.AreHostile(faction, holder);
        }

        /// <summary>The chance of this road: how we stand against the strongest
        /// known rival, plus the opening when its soldiers stand on its Sanctums.</summary>
        private float ConquestChance(EntityManager em, Entity brain, Faction faction, VictoryPlanState plan)
        {
            int mine = BoardScore(em, faction);
            int rival = 0;
            Faction strongest = faction;
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var f = (Faction)i;
                if (f == faction || !Alliances.AreHostile(faction, f)) continue;
                int known = KnownBoardScore(em, faction, f);
                if (known > rival) { rival = known; strongest = f; }
            }
            float ratio = rival > 0 ? (float)mine / rival : 1f;
            float chance = math.clamp(Cfg.victoryConquestBase + (ratio - 1f) * Cfg.victoryConquestPerRatio, 0f, 1f);

            if (strongest != faction && SanctumGarrisonAway(em, brain, faction, strongest))
            {
                plan.StrikeHome = true;
                plan.StrikeVictim = strongest;
                chance += Cfg.victoryStrikeHomeBonus;
            }
            return chance;
        }

        /// <summary>Most of the victim's soldiers we can see stand on its Sanctums,
        /// and our army outnumbers what is left at its capital.</summary>
        private bool SanctumGarrisonAway(EntityManager em, Entity brain, Faction faction, Faction victim)
        {
            // Only what it has seen (§ 6v): a Sanctum in vision, a capital on record.
            if (!FillSeen(em, brain)) return false;
            var cq = QC_SanctumCrystals.Get(em, QT_SanctumCrystals);
            using var crystals = cq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int garrison = 0;
            for (int i = 0; i < crystals.Length; i++)
            {
                float3 p = crystals[i].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None || TerritoryOwnership.OwnerOf(t) != (int)victim) continue;
                if (!VisibleNow(faction, p)) continue;
                garrison += TacticalQuery.FactionStrengthInRadius(em, victim, p, Cfg.sanctumThreatRadius);
            }
            if (garrison <= 0) return false;

            var hall = FindFactionBuilding<HallTag>(em, victim);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall)) return false;
            float3 at = em.GetComponentData<LocalTransform>(hall).Position;
            if (!SeenOrVisible(faction, hall, at)) return false;
            int home = TacticalQuery.FactionStrengthInRadius(em, victim, at, Cfg.victoryBaseRadius);
            if (garrison < (garrison + home) * Cfg.victoryGarrisonShare) return false;
            return ArmyPowerOf(em, faction) >= math.max(1, home) * Cfg.victoryStrikeSuperiority;
        }

        /// <summary>The artifact not yet out: what we hold of the monuments, and
        /// the artifact lying where a squad can reach it.</summary>
        private float ShardrootChance(EntityManager em, Faction faction)
        {
            using var monuments = Monument.All(em, Allocator.Temp);
            if (monuments.Length == 0) return 0f;
            int mine = 0;
            for (int i = 0; i < monuments.Length; i++)
                if (em.GetComponentData<MonumentState>(monuments[i]).Holder == faction) mine++;
            float chance = Cfg.victoryShardrootBase + Cfg.victoryShardrootPerMonument * mine / monuments.Length;
            // Reacting to failure: every squad lost on a monument makes this road look worse.
            _monumentFailedSquads.TryGetValue((int)faction, out int failed);
            chance = math.max(0f, chance - failed * Cfg.victoryShardrootPerFailedSquad);
            if (AIShardroot.Locate(em, faction, out _, out _) == AIShardroot.Where.Ground)
                chance += Cfg.victoryShardrootOnGround;
            return chance;
        }

        /// <summary>The attacks under way end at their next think; the soldiers
        /// come home. The home defence stays.</summary>
        private void CallOffAttacks(Faction faction, float now)
        {
            if (!_missions.TryGetValue((int)faction, out var list)) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Phase != MissionPhase.Direct) list[i].Deadline = now;
        }
    }
}
