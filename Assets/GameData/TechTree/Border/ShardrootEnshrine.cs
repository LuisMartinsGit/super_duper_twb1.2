// ShardrootEnshrine.cs
// THE PLAYER'S ENSHRINE ORDER (2026-10-20, Curse_And_Shardroot.md § 3.1d).
// Developer: "it is impossible for a human player to store the shardroot on
// the temple (should be: select the hero who has it -> right click to stow
// away the shardroot. Hero needs to be next to the building)".
//
// The AI enshrines by walking its courier to its Temple with Intent = Temple
// (SimpleAISystem.Shardroot.cs); ShardrootCarrySystem's delivery pass then
// enshrines it. A player's right-click on the Temple used to be a plain
// formation move: the bearer stopped wherever its slot fell, walked past its
// own Hall on the way (which binds the artifact there first), and a faction
// that could not pay the enshrine price was told nothing. This is the same
// sim path made explicit:
//
//   * Check      — who may give the order and on what (issue side AND every
//                  peer's executor; the refusal text the input layer shows).
//   * Execute    — every peer: stamp ShardrootEnshrineOrder + Intent Temple
//                  on the bearer and walk it to the Temple wall.
//   * Tick       — ShardrootCarrySystem, every peer: enshrine on arrival
//                  (Enshrine — the courier pass's effect), wait when
//                  the faction cannot pay, drop when re-ordered or stuck.
//
// The order travels as LockstepCommandType.EnshrineShardroot.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    public static class ShardrootEnshrine
    {
        public enum Refusal : byte
        {
            None = 0,
            /// <summary>The unit does not carry the Shardroot.</summary>
            NoBearer,
            /// <summary>The target is not a Temple of Ridan.</summary>
            NotTemple,
            /// <summary>The Temple belongs to someone else.</summary>
            NotOwn,
            /// <summary>The Temple is still a construction site / dead.</summary>
            Unfinished,
            /// <summary>The bearer is the Shardbound Hero: the Hall choice
            /// holds until he falls (§ 3.1b).</summary>
            Bound,
            /// <summary>The faction cannot pay the enshrine price (§ 3.1c).</summary>
            CannotPay,
        }

        /// <summary>Metres beyond the Temple wall the bearer is walked to
        /// (inside ShardrootDepositRadius, so arrival enshrines).</summary>
        private const float StandOff = ShardrootDepositRadius * 0.4f;

        /// <summary>A bearer idle this long short of the Temple was stopped or
        /// is stuck: the order is dropped.</summary>
        private const float GiveUpSeconds = 3f;

        /// <summary>The order is dropped when the bearer's destination lies
        /// further than this from the Temple wall — it was ordered elsewhere.</summary>
        private const float ReorderSlack = ShardrootDepositRadius + 4f;

        /// <summary>True when <paramref name="unit"/> is a living unit
        /// carrying the Shardroot.</summary>
        public static bool IsBearer(EntityManager em, Entity unit)
            => unit != Entity.Null && em.Exists(unit)
               && em.HasComponent<UnitTag>(unit) && !em.HasComponent<BuildingTag>(unit)
               && em.HasComponent<ShardrootTag>(unit) && em.HasComponent<ShardrootBearer>(unit)
               && (!em.HasComponent<Health>(unit) || em.GetComponentData<Health>(unit).Value > 0);

        /// <summary>True when <paramref name="target"/> is a Temple of Ridan.</summary>
        public static bool IsTemple(EntityManager em, Entity target)
            => target != Entity.Null && em.Exists(target) && em.HasComponent<TempleOfRidanTag>(target)
               && em.HasComponent<BuildingTag>(target);

        /// <summary>
        /// May <paramref name="unit"/> be ordered to enshrine at
        /// <paramref name="temple"/>? <paramref name="checkPrice"/> is the issue
        /// side's extra test (the executor does not refuse an order the bank
        /// cannot cover yet: the bearer waits at the Temple, § 3.1c).
        /// </summary>
        public static Refusal Check(EntityManager em, Entity unit, Entity temple, bool checkPrice)
        {
            if (!IsBearer(em, unit)) return Refusal.NoBearer;
            if (!IsTemple(em, temple)) return Refusal.NotTemple;
            var f = em.GetComponentData<FactionTag>(unit).Value;
            if (!em.HasComponent<FactionTag>(temple) || em.GetComponentData<FactionTag>(temple).Value != f)
                return Refusal.NotOwn;
            if (em.HasComponent<UnderConstruction>(temple) || !em.HasComponent<ShardrootStored>(temple)
                || (em.HasComponent<Health>(temple) && em.GetComponentData<Health>(temple).Value <= 0))
                return Refusal.Unfinished;
            if (em.HasComponent<ShardboundHeroTag>(unit)) return Refusal.Bound;
            if (checkPrice && !TheWaningBorder.Economy.FactionEconomy.CanAfford(em, f,
                    ShardrootEmpowermentConfig.I.EnshrinePrice))
                return Refusal.CannotPay;
            return Refusal.None;
        }

        /// <summary>Player-facing text for a refusal (already localised).</summary>
        public static string RefusalText(Refusal r)
        {
            switch (r)
            {
                case Refusal.NoBearer:   return Loc.T("Only the unit carrying the Shardroot can enshrine it");
                case Refusal.NotTemple:  return Loc.T("The Shardroot can only be enshrined in a Temple of Ridan");
                case Refusal.NotOwn:     return Loc.T("The Shardroot can only be enshrined in your own Temple of Ridan");
                case Refusal.Unfinished: return Loc.T("The Temple of Ridan must be finished to enshrine the Shardroot");
                case Refusal.Bound:      return Loc.T("The Shardroot is bound to its hero until he falls");
                case Refusal.CannotPay:  return string.Format(Loc.T("Enshrining the Shardroot costs {0}"), PriceText());
            }
            return null;
        }

        /// <summary>The enshrine price as "10000 supplies, 1500 veilstone, ...".</summary>
        public static string PriceText()
        {
            var c = ShardrootEmpowermentConfig.I.EnshrinePrice;
            var sb = new System.Text.StringBuilder();
            void Part(int n, string what)
            {
                if (n <= 0) return;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(n).Append(' ').Append(Loc.T(what));
            }
            Part(c.Supplies, "supplies");
            Part(c.Iron, "iron");
            Part(c.Veilstone, "veilstone");
            Part(c.Veilsteel, "veilsteel");
            return sb.Length > 0 ? sb.ToString() : Loc.T("nothing");
        }

        /// <summary>
        /// The executor — runs on every peer at the command's tick. Re-checks
        /// (the state may have moved since the click), stamps the order and
        /// Intent Temple on the bearer, and walks it to the Temple wall.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, Entity temple)
        {
            if (Check(em, unit, temple, checkPrice: false) != Refusal.None) return;

            var bearer = em.GetComponentData<ShardrootBearer>(unit);
            byte prev = bearer.Intent;
            if (em.HasComponent<ShardrootEnshrineOrder>(unit))
                prev = em.GetComponentData<ShardrootEnshrineOrder>(unit).PrevIntent;
            bearer.Intent = ShardrootIntent.Temple;
            em.SetComponentData(unit, bearer);

            var order = new ShardrootEnshrineOrder { Temple = temple, PrevIntent = prev };
            if (em.HasComponent<ShardrootEnshrineOrder>(unit)) em.SetComponentData(unit, order);
            else em.AddComponentData(unit, order);

            float3 from = em.GetComponentData<LocalTransform>(unit).Position;
            float3 dest = TargetGeometry.Extent(em, temple).ApproachPoint(from, StandOff);
            MoveCommandHelper.Execute(em, unit, dest);
        }

        /// <summary>
        /// The ordered delivery: move the artifact from <paramref name="unit"/>
        /// into <paramref name="temple"/> (already paid for). The same effect,
        /// line for line, as ShardrootCarrySystem's courier pass (the AI's
        /// road) — keep the two in step. Structural changes go through
        /// <paramref name="ecb"/>.
        /// </summary>
        public static void Enshrine(EntityManager em, EntityCommandBuffer ecb,
            Entity unit, Entity temple, Faction f)
        {
            int delivered = em.GetComponentData<ShardrootBearer>(unit).Amount;
            var stored = em.GetComponentData<ShardrootStored>(temple);
            stored.Amount += delivered;
            em.SetComponentData(temple, stored);

            // Zero it now: a later pass of the same tick must not deliver twice.
            var b = em.GetComponentData<ShardrootBearer>(unit);
            b.Amount = 0;
            em.SetComponentData(unit, b);
            ecb.RemoveComponent<ShardrootBearer>(unit);
            if (em.HasComponent<ShardrootEnshrineOrder>(unit))
                ecb.RemoveComponent<ShardrootEnshrineOrder>(unit);

            // Temple choice: enshrining the Shardroot moves the tag onto the
            // Temple (locked in — it leaves only via the Temple's detonation,
            // TempleExplodeSystem).
            if (em.HasComponent<ShardrootTag>(unit))
            {
                ecb.RemoveComponent<ShardrootTag>(unit);
                ecb.AddComponent<ShardrootTag>(temple);
                SimSignals.Notify(
                    string.Format(Loc.T("{0} enshrines the Shardroot — its army is empowered!"), f));
            }
            UnityEngine.Debug.Log($"[Shardroot] {f} deposited {delivered} Shardroot at Temple of Ridan (stored: {stored.Amount})");
        }

        /// <summary>
        /// One tick of every ordered bearer (called by ShardrootCarrySystem on
        /// every peer, before its courier pass). Arrival enshrines; a faction
        /// that cannot pay waits with the order kept; a bearer ordered
        /// elsewhere, stopped short, or whose Temple fell loses the order and
        /// gets its old Intent back.
        /// </summary>
        public static void TickOrders(EntityManager em, EntityCommandBuffer ecb,
            Unity.Collections.NativeArray<Entity> ordered, float dt)
        {
            for (int i = 0; i < ordered.Length; i++)
            {
                Entity unit = ordered[i];
                if (!em.Exists(unit) || !em.HasComponent<ShardrootEnshrineOrder>(unit)) continue;
                var order = em.GetComponentData<ShardrootEnshrineOrder>(unit);

                // Lost the artifact (dropped on death, handed over), or the
                // Temple is gone / no longer able to take it.
                if (!IsBearer(em, unit))
                {
                    ecb.RemoveComponent<ShardrootEnshrineOrder>(unit);
                    continue;
                }
                var f = em.GetComponentData<FactionTag>(unit).Value;
                if (Check(em, unit, order.Temple, checkPrice: false) != Refusal.None)
                {
                    Drop(em, ecb, unit, order);
                    if (f == GameSettings.LocalPlayerFaction)
                        SimSignals.NotifyError(Loc.T("The Shardroot can no longer be enshrined there"));
                    continue;
                }

                var pos = em.GetComponentData<LocalTransform>(unit).Position;
                var extent = TargetGeometry.Extent(em, order.Temple);
                bool moving = em.HasComponent<DesiredDestination>(unit)
                              && em.GetComponentData<DesiredDestination>(unit).Has != 0;

                // Ordered elsewhere: the destination left the Temple.
                if (moving && extent.SurfaceDistXZ(em.GetComponentData<DesiredDestination>(unit).Position) > ReorderSlack)
                {
                    Drop(em, ecb, unit, order);
                    continue;
                }

                if (extent.SurfaceDistXZ(pos) <= ShardrootDepositRadius)
                {
                    // THE PRICE (§3.1c): paid on delivery; a faction that
                    // cannot pay waits here with the artifact, hunted.
                    if (TheWaningBorder.Economy.FactionEconomy.Spend(em, f,
                            ShardrootEmpowermentConfig.I.EnshrinePrice,
                            TheWaningBorder.Economy.SpendCategory.Religion))
                    {
                        Enshrine(em, ecb, unit, order.Temple, f);
                        continue;
                    }
                    if (order.PriceNoticed == 0)
                    {
                        order.PriceNoticed = 1;
                        if (f == GameSettings.LocalPlayerFaction)
                            SimSignals.NotifyError(string.Format(Loc.T(
                                "The Shardroot waits at the Temple — enshrining costs {0}"), PriceText()));
                    }
                    order.StuckSeconds = 0f;
                    em.SetComponentData(unit, order);
                    continue;
                }

                order.StuckSeconds = moving ? 0f : order.StuckSeconds + dt;
                if (order.StuckSeconds >= GiveUpSeconds)
                {
                    Drop(em, ecb, unit, order);
                    if (f == GameSettings.LocalPlayerFaction)
                        SimSignals.NotifyError(Loc.T("The Shardroot's bearer could not reach the Temple"));
                    continue;
                }
                em.SetComponentData(unit, order);
            }
        }

        private static void Drop(EntityManager em, EntityCommandBuffer ecb, Entity unit,
            in ShardrootEnshrineOrder order)
        {
            if (em.HasComponent<ShardrootBearer>(unit))
            {
                var b = em.GetComponentData<ShardrootBearer>(unit);
                if (b.Intent == ShardrootIntent.Temple)
                {
                    b.Intent = order.PrevIntent;
                    em.SetComponentData(unit, b);
                }
            }
            ecb.RemoveComponent<ShardrootEnshrineOrder>(unit);
        }
    }
}
