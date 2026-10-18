// SanctumSystem.cs
// SANCTUMS (docs/Design/Territory_Claims.md § 11, § 11.4 and § 7, 2026-10-18).
// A Sanctum territory carries no resource nodes. Each one its holder has
// CLAIMED pays, and the bonuses stack per Sanctum:
//
//   * one Religion Point every `secondsPerPoint` (straight to the balance,
//     not through the points ladder)
//   * a veilsteel trickle, `veilsteelPerMinute` (a source nobody forges)
//   * sect powers recharge faster: CooldownScale, read by
//     SectActivePowerSystem.EffectiveCooldown — the cast and the tooltip
//
// DOMINION, the second exception to elimination (§ 7): a faction — or a team,
// allies' Sanctums counting together (Teams.md) — that holds EVERY Sanctum on
// the map starts a `dominionSeconds` countdown; losing any Sanctum (to a
// player or the curse) cancels it, and holding them all again starts afresh.
// When it runs out, SanctumDominion.Done is set and EliminationSystem retires
// every faction hostile to the winner — the same path as the Shardroot
// ascension. A map with one Sanctum is allowed (developer: "keep as is").
//
// Lockstep: SimCadence clocks, territories walked in index order, replicated
// ownership only; the countdown lives on an ECS singleton (saved with the
// world). Notices and minimap pings are presentation.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;

/// <summary>The dominion countdown (Territory_Claims.md § 11.4). One per
/// match, created by SanctumSystem. Holder = Faction.Border when nobody holds
/// every Sanctum.</summary>
public struct SanctumDominion : IComponentData
{
    public Faction Holder;
    public float Elapsed;
    public byte Done;
    public int NoticeMinutes;
}

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SanctumSystem : SystemBase
    {
        private static SanctumSystemConfig Cfg => SanctumSystemConfig.I;

        private SimCadence.Periodic _points;
        private SimCadence.Periodic _tick;
        private const float TickSeconds = 1f;

        /// <summary>Fractional veilsteel carry per faction; match state, reset
        /// per match (see TerritoryIncomeSystem's carry for why).</summary>
        private readonly float[] _carryVeilsteel = new float[9];
        private int _epoch = -1;

        private static readonly ComponentType[] QT_Dominion = { ComponentType.ReadWrite<SanctumDominion>() };
        private static CachedEntityQuery QC_Dominion;

        protected override void OnUpdate()
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                System.Array.Clear(_carryVeilsteel, 0, _carryVeilsteel.Length);
            }

            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            if (_tick.Due(dt, TickSeconds))
            {
                PayVeilsteel(em, TickSeconds);
                TickDominion(em, TickSeconds);
            }
            if (_points.Due(dt, Cfg.secondsPerPoint)) PayPoints(em);
        }

        // ── what a held Sanctum pays ────────────────────────────────────

        /// <summary>How many Sanctums this faction holds (claimed).</summary>
        public static int HeldBy(Faction faction)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return 0;
            int n = 0;
            for (int t = 0; t < RegionMap.Count; t++)
                if (TerritoryResources.IsSanctum(t) && TerritoryOwnership.OwnerOf(t) == (int)faction) n++;
            return n;
        }

        /// <summary>
        /// The factor on this faction's sect power cooldowns: the cut per
        /// Sanctum applied once per Sanctum held, multiplicatively, never past
        /// the cap. 1 with none.
        /// </summary>
        public static float CooldownScale(Faction faction)
        {
            int n = HeldBy(faction);
            if (n <= 0) return 1f;
            float scale = math.pow(1f - Cfg.cooldownCutPerSanctum, n);
            return math.max(1f - Cfg.maxCooldownCut, scale);
        }

        private static void PayPoints(EntityManager em)
        {
            for (int t = 0; t < RegionMap.Count; t++)
            {
                if (!TerritoryResources.IsSanctum(t)) continue;
                int owner = TerritoryOwnership.OwnerOf(t);
                if (owner < 0 || owner >= 8) continue;   // natural ground or the curse
                FactionReligionPointsHelper.Refund(em, (Faction)owner, 1);
                if ((Faction)owner == GameSettings.LocalPlayerFaction)
                    SimSignals.Notify(string.Format(Loc.T("+1 Religion Point — {0} (Sanctum)"), RegionMap.NameOf(t)));
            }
        }

        private void PayVeilsteel(EntityManager em, float seconds)
        {
            float rate = Cfg.veilsteelPerMinute;
            if (rate <= 0f) return;
            for (int f = 0; f < 8; f++)
            {
                int n = HeldBy((Faction)f);
                if (n <= 0) continue;
                _carryVeilsteel[f] += n * rate * seconds / 60f;
                int whole = Mathf.FloorToInt(_carryVeilsteel[f]);
                if (whole <= 0) continue;
                _carryVeilsteel[f] -= whole;
                FactionEconomy.Add(em, (Faction)f, new Cost { Veilsteel = whole }, IncomeSource.Other);
            }
        }

        // ── dominion ────────────────────────────────────────────────────

        /// <summary>The faction whose team has won by dominion, if any
        /// (read by EliminationSystem).</summary>
        public static bool TryWinner(EntityManager em, out Faction winner)
        {
            winner = Faction.Border;
            var q = QC_Dominion.Get(em, QT_Dominion);
            if (q.IsEmptyIgnoreFilter) return false;
            using var s = q.ToComponentDataArray<SanctumDominion>(Allocator.Temp);
            if (s[0].Done == 0 || s[0].Holder == Faction.Border) return false;
            winner = s[0].Holder;
            return true;
        }

        /// <summary>The running countdown, for the HUD: false when nobody holds
        /// every Sanctum. Presentation reads it; nothing in the sim does.</summary>
        public static bool TryCountdown(EntityManager em, out Faction holder, out float secondsLeft)
        {
            holder = Faction.Border; secondsLeft = 0f;
            var q = QC_Dominion.Get(em, QT_Dominion);
            if (q.IsEmptyIgnoreFilter) return false;
            using var s = q.ToComponentDataArray<SanctumDominion>(Allocator.Temp);
            if (s[0].Holder == Faction.Border) return false;
            holder = s[0].Holder;
            secondsLeft = math.max(0f, Cfg.dominionSeconds - s[0].Elapsed);
            return true;
        }

        private static void TickDominion(EntityManager em, float dt)
        {
            var q = QC_Dominion.Get(em, QT_Dominion);
            if (q.IsEmptyIgnoreFilter)
            {
                var e = em.CreateEntity(typeof(SanctumDominion));
                em.SetComponentData(e, new SanctumDominion { Holder = Faction.Border, NoticeMinutes = -1 });
                return;
            }
            var ent = q.GetSingletonEntity();
            var st = em.GetComponentData<SanctumDominion>(ent);
            if (st.Done != 0) return;           // decided: final

            // Who holds every Sanctum? One faction, or allies together; the
            // lowest-numbered holder stands for the team.
            int rep = -1;
            bool all = true, any = false;
            for (int t = 0; t < RegionMap.Count && all; t++)
            {
                if (!TerritoryResources.IsSanctum(t)) continue;
                any = true;
                int owner = TerritoryOwnership.OwnerOf(t);
                if (owner < 0 || owner >= 8) { all = false; break; }
                if (rep < 0) rep = owner;
                else if (owner != rep && Alliances.AreHostile((Faction)owner, (Faction)rep)) { all = false; break; }
                else if (owner < rep) rep = owner;
            }

            if (!any || !all)
            {
                if (st.Holder != Faction.Border)
                {
                    SimSignals.Announce(string.Format(Loc.T("{0}'s dominion is broken — a Sanctum was lost"),
                        FactionColors.RichName(st.Holder)));
                    st.Holder = Faction.Border; st.Elapsed = 0f; st.NoticeMinutes = -1;
                    em.SetComponentData(ent, st);
                }
                return;
            }

            var holder = (Faction)rep;
            if (st.Holder == Faction.Border || Alliances.AreHostile(st.Holder, holder))
            {
                st.Holder = holder; st.Elapsed = 0f; st.NoticeMinutes = -1;
                UnityEngine.Debug.Log($"[Sanctum] DOMINION -- {holder} (and allies) hold every Sanctum; " +
                                      $"{Cfg.dominionSeconds:0}s to victory");
            }
            else st.Holder = holder;            // the same team; its stand-in may change

            st.Elapsed += dt;
            float left = math.max(0f, Cfg.dominionSeconds - st.Elapsed);
            int minutes = (int)math.ceil(left / 60f);
            if (minutes != st.NoticeMinutes && left > 0f
                && (st.NoticeMinutes < 0 || minutes == 5 || minutes == 3 || minutes <= 2))
            {
                st.NoticeMinutes = minutes;
                SimSignals.Announce(string.Format(Loc.T("{0} holds every Sanctum — {1}:00 to dominion!"),
                    FactionColors.RichName(holder), minutes));
                for (int t = 0; t < RegionMap.Count; t++)
                {
                    if (!TerritoryResources.IsSanctum(t)) continue;
                    var p = RegionMap.SeedOf(t);
                    SimSignals.Ping(new float3(p.x, 0f, p.y), SimPingKind.Discovery, 12f, big: true);
                }
            }
            if (left <= 0f)
            {
                st.Done = 1;
                SimSignals.Announce(string.Format(Loc.T("{0} wins by dominion — every Sanctum held!"),
                    FactionColors.RichName(holder)));
                UnityEngine.Debug.Log($"[Sanctum] DOMINION COMPLETE -- {holder} wins after {st.Elapsed:0}s");
            }
            em.SetComponentData(ent, st);
        }
    }
}
