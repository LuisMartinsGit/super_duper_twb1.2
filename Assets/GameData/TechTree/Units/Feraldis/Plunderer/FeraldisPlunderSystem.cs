// The Feraldis economy: Plunderers steal from the bank of whoever they are
// attacking. Canon: docs/Design/Age_1_Feraldis.md § Raider Camp.
//
// A Plunderer earns ONLY while it is genuinely raiding, which means all
// three of:
//   1. it is engaging an enemy target,
//   2. it is OUTSIDE its owner's own influence, and
//   3. it is OUTSIDE the curse's influence.
// Loitering at home pays nothing; standing in cursed ground pays nothing.
// Feraldis has to be in someone's face to be paid.
//
// FLOOR RULE: if the victim is not a player faction (the curse, or a
// neutral), the take is GENERATED rather than stolen. Without it, a Feraldis
// player who is boxed in or has no reachable enemy would have no economy at
// all — the original design flagged that softlock hazard explicitly.
//
// Income accrues into a per-raider float purse and is banked as whole units,
// so fractional rates are not lost to integer resource writes.
//
// PlayerInfluenceMap / FactionResearchState are managed statics, so this is
// a SystemBase on a 1 s tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Influence;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisPlunderSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<PlundererTag>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (!PlayerInfluenceMap.Ready) return;

            var em = EntityManager;

            foreach (var (purse, transform, faction, target, entity) in SystemAPI
                // EVERY FERALDIS WARRIOR PLUNDERS (2026-08-07), not just the
                // Plunderer. This is the compensation for cutting raider
                // throughput: raiding stops being a unit type you build and
                // becomes what the Feraldis army does. The three raiding
                // conditions below are unchanged, so Feraldis is still paid
                // only for being in someone's face.
                //
                // Gated on PlunderPurse rather than a tag, so the purse is the
                // single switch for "this unit can plunder" —
                // FeraldisCultureRetrofitSystem stamps it on the army.
                .Query<RefRW<PlunderPurse>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<Target>>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                ref var pp = ref purse.ValueRW;
                pp.TickTimer -= dt;
                if (pp.TickTimer > 0f) continue;
                float slice = PlunderTickInterval;
                pp.TickTimer = PlunderTickInterval;

                var owner = faction.ValueRO.Value;
                var pos = transform.ValueRO.Position;

                // (1) engaging something, AND actually on top of it. The
                // target check alone was not enough: the patrol driver
                // assigns targets out to 200 m, so without a proximity test a
                // Plunderer earned full rate loitering just past its own
                // border with no risk at all.
                var victim = target.ValueRO.Value;
                if (victim == Entity.Null || !em.Exists(victim)) continue;
                if (!em.HasComponent<Health>(victim)) continue;
                if (em.GetComponentData<Health>(victim).Value <= 0) continue;
                if (!em.HasComponent<LocalTransform>(victim)) continue;

                var vPos = em.GetComponentData<LocalTransform>(victim).Position;
                float vdx = vPos.x - pos.x, vdz = vPos.z - pos.z;
                if (vdx * vdx + vdz * vdz > PlunderEngageRadius * PlunderEngageRadius)
                    continue;

                // (2) outside own influence, (3) outside curse influence
                if (PlayerInfluenceMap.ChannelStrengthWorld((int)owner, pos.x, pos.z)
                    >= PlunderInfluenceBlock) continue;
                if (PlayerInfluenceMap.ChannelStrengthWorld(
                        PlayerInfluenceMap.CurseChannel, pos.x, pos.z)
                    >= PlunderInfluenceBlock) continue;

                var victimFaction = em.HasComponent<FactionTag>(victim)
                    ? em.GetComponentData<FactionTag>(victim).Value
                    : Faction.Border;

                bool victimIsPlayer = (int)victimFaction >= 0
                    && (int)victimFaction < PlayerInfluenceMap.PlayerChannels
                    && victimFaction != owner;

                // Take, scaled by the Raiding survey ladder. Against a
                // non-player victim the take is GENERATED rather than stolen,
                // so it is cut hard — the floor rule exists to stop a
                // boxed-in Feraldis softlocking, not to be a farm.
                float mult = RaidMultiplier(owner, out bool iron, out bool veilstone, out bool veilsteel);
                if (!victimIsPlayer) mult *= PlunderFloorFraction;
                // A line soldier earns a fraction of the specialist's take —
                // the Plunderer keeps its job as the dedicated raider.
                if (!em.HasComponent<PlundererTag>(entity)) mult *= PlunderWarriorFraction;
                float supplies = PlunderSuppliesPerSecond * mult * slice;

                var take = new Cost
                {
                    Supplies  = (int)supplies,
                    Iron      = iron      ? (int)(supplies * PlunderIronFraction)      : 0,
                    Veilstone = veilstone ? (int)(supplies * PlunderVeilstoneFraction) : 0,
                    Veilsteel = veilsteel ? (int)(supplies * PlunderVeilsteelFraction) : 0,
                };

                // Carry the fractional remainder so slow rates still pay out.
                pp.Supplies  += supplies - take.Supplies;
                pp.Iron      += iron      ? supplies * PlunderIronFraction      - take.Iron      : 0f;
                pp.Veilstone += veilstone ? supplies * PlunderVeilstoneFraction - take.Veilstone : 0f;
                pp.Veilsteel += veilsteel ? supplies * PlunderVeilsteelFraction - take.Veilsteel : 0f;
                FlushPurse(ref pp, ref take);

                if (take.Supplies + take.Iron + take.Veilstone + take.Veilsteel <= 0) continue;

                // STEAL from a player victim; the non-player case was already
                // rate-cut above and is simply generated.
                if (victimIsPlayer)
                    take = DrainFromVictim(em, victimFaction, take);

                if (take.Supplies + take.Iron + take.Veilstone + take.Veilsteel > 0)
                    FactionEconomy.Add(em, owner, take);
            }
        }

        /// <summary>Move whole units out of the float purse into this tick's take.</summary>
        private static void FlushPurse(ref PlunderPurse pp, ref Cost take)
        {
            int s = (int)pp.Supplies;  if (s > 0) { pp.Supplies  -= s; take.Supplies  += s; }
            int i = (int)pp.Iron;      if (i > 0) { pp.Iron      -= i; take.Iron      += i; }
            int v = (int)pp.Veilstone; if (v > 0) { pp.Veilstone -= v; take.Veilstone += v; }
            int t = (int)pp.Veilsteel; if (t > 0) { pp.Veilsteel -= t; take.Veilsteel += t; }
        }

        /// <summary>
        /// Deduct what we can from the victim's bank and return what was
        /// ACTUALLY taken — a broke victim yields nothing, so raiding a
        /// bankrupt player is not free money.
        ///
        /// Each field is clamped to the victim's balance before spending
        /// because FactionEconomy.Spend is all-or-nothing across every
        /// resource type: an unclamped request (say, enough Supplies but not
        /// enough Iron) would deduct NOTHING and silently return false. The
        /// return value is then checked so a failed Spend can never be paired
        /// with a successful Add — that pairing would mint resources out of
        /// thin air, which is the one bug this system must never have.
        /// </summary>
        private static Cost DrainFromVictim(EntityManager em, Faction victim, in Cost want)
        {
            if (!FactionEconomy.TryGetResources(em, victim, out var res)) return default;

            // Max(0, ...) is not paranoia: a bank can legitimately sit
            // negative (FactionResources.Clamp exists precisely because some
            // systems mutate balances directly). Without the floor, Min()
            // against a negative balance yields a NEGATIVE take, which would
            // Spend a negative into the victim (crediting them) and Add a
            // negative to the raider's owner — theft in reverse.
            var got = new Cost
            {
                Supplies  = System.Math.Max(0, System.Math.Min(want.Supplies,  res.Supplies)),
                Iron      = System.Math.Max(0, System.Math.Min(want.Iron,      res.Iron)),
                Veilstone = System.Math.Max(0, System.Math.Min(want.Veilstone, res.Veilstone)),
                Veilsteel = System.Math.Max(0, System.Math.Min(want.Veilsteel, res.Veilsteel)),
            };
            if (got.Supplies + got.Iron + got.Veilstone + got.Veilsteel <= 0) return default;

            return FactionEconomy.Spend(em, victim, got) ? got : default;
        }

        /// <summary>
        /// Raiding survey ladder — take multiplier plus which secondary
        /// resources the faction's Plunderers can carry off. Mirrors the
        /// Alanthor Guild Survey pattern (tiered HasResearched checks).
        /// </summary>
        private static float RaidMultiplier(Faction f,
            out bool iron, out bool veilstone, out bool veilsteel)
        {
            iron = veilstone = veilsteel = false;
            var research = FactionResearchState.Instance;
            if (research == null) return 1f;

            iron      = research.HasResearched(f, "IronPlunder");
            veilstone = research.HasResearched(f, "VeilstonePlunder");
            veilsteel = research.HasResearched(f, "VeilsteelPlunder");

            if (research.HasResearched(f, "Raiding3")) return RaidingTier3Mult;
            if (research.HasResearched(f, "Raiding2")) return RaidingTier2Mult;
            if (research.HasResearched(f, "Raiding1")) return RaidingTier1Mult;
            return 1f;
        }
    }
}
