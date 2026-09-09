// SectAntiquityTallySystem.cs
// Implements Antiquity's Lv I "Tally of the Lost" passive: each kill the
// attacker makes is logged into a per-UnitClass counter on the attacker
// (the AntiquityKills component). CombatDamageHelper reads those counters
// on every hit and grants +1% per logged kill of the *target's* class,
// capped at +10% (Lv I). Phase 4 raises both the per-kill bonus and the
// cap.
//
// Hook: same death-event pattern as SectVenerationFervorSystem. Runs
// before DeathSystem, scans entities at Health <= 0 with no death-marker,
// reads LastAttackerEntity, increments the killer's class counter for
// the dead unit's UnitClass.
//
// task-063 phase 2e.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct SectAntiquityTallySystem : ISystem
    {
        // Spec (task-063): the cap is a PERCENTAGE ceiling per class —
        // +5% / +10% / +15% at Lv I/II/III. With the per-kill bonus at
        // 0.5% / 1% / 1.5% (CombatDamageHelper), a flat 10-kill tally per
        // class hits the ceiling exactly at every level.
        private static byte KillCapFor(byte level) => 10;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        private struct PendingTally
        {
            public Entity Killer;
            public UnitClass VictimClass;
            public byte Cap;

            /// <summary>
            /// Set when the VICTIM's faction has Antiquity — the killer owes
            /// that faction one more unit, which is what Writ of Attainder
            /// bills for. Independent of <see cref="Cap"/>'s side of the
            /// event: the same kill can feed both books, one book, or neither.
            /// </summary>
            public bool Attainder;
            public Faction Victim;
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Collect kill events first, apply after the iteration — the lazy
            // AddComponentData below is a STRUCTURAL change, which throws
            // InvalidOperationException inside a SystemAPI.Query foreach.
            var pending = new NativeList<PendingTally>(Allocator.Temp);

            foreach (var (health, lastAttacker, victimUnit, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LastAttackerEntity>, RefRO<UnitTag>>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                Entity killer = lastAttacker.ValueRO.Value;
                if (killer == Entity.Null || !em.Exists(killer)) continue;
                if (!em.HasComponent<FactionTag>(killer)) continue;
                if (!em.HasComponent<UnitTag>(killer)) continue;

                Faction killerFaction = em.GetComponentData<FactionTag>(killer).Value;
                if (!em.HasComponent<FactionTag>(entity)) continue;
                Faction victimFaction = em.GetComponentData<FactionTag>(entity).Value;
                if (victimFaction == killerFaction) continue;

                // TWO books, from one pass over the same kill events.
                //
                //   The PASSIVE's book belongs to the killer: an Antiquity
                //   player's units get better at killing what they have killed
                //   before.
                //   The WRIT's book belongs to the killer too, but it is kept
                //   on behalf of the VICTIM's side — Writ of Attainder asks an
                //   enemy how many of MY units it has killed, so the count has
                //   to exist on units that belong to no Antiquity player at all.
                //
                // Reading both here is what lets the design claim the sect's
                // two halves are one idea rather than two systems.
                byte level = SectQuery.LevelOf(em, killerFaction,
                    SectConfig.Antiquity, SectLeverKind.Passive);
                bool attainder = SectQuery.IsAdopted(em, victimFaction, SectConfig.Antiquity);
                if (level == 0 && !attainder) continue;

                pending.Add(new PendingTally
                {
                    Killer = killer,
                    VictimClass = victimUnit.ValueRO.Class,
                    Cap = level == 0 ? (byte)0 : KillCapFor(level),
                    Attainder = attainder,
                    Victim = victimFaction,
                });
            }

            for (int i = 0; i < pending.Length; i++)
            {
                var p = pending[i];
                if (!em.Exists(p.Killer)) continue;

                if (p.Cap > 0)
                {
                    // Stamp lazily on first relevant kill (legal here — the
                    // query iteration is over).
                    if (!em.HasComponent<AntiquityKills>(p.Killer))
                        em.AddComponentData(p.Killer, new AntiquityKills());

                    var kills = em.GetComponentData<AntiquityKills>(p.Killer);
                    Increment(ref kills, p.VictimClass, p.Cap);
                    em.SetComponentData(p.Killer, kills);
                }

                if (p.Attainder)
                {
                    if (!em.HasComponent<AttainderLedger>(p.Killer))
                        em.AddComponentData(p.Killer, new AttainderLedger());

                    var ledger = em.GetComponentData<AttainderLedger>(p.Killer);
                    ledger.Record(p.Victim);
                    em.SetComponentData(p.Killer, ledger);
                }
            }

            pending.Dispose();
        }

        private static void Increment(ref AntiquityKills k, UnitClass cls, byte cap)
        {
            switch (cls)
            {
                case UnitClass.Melee:   if (k.Melee   < cap) k.Melee++;   break;
                case UnitClass.Ranged:  if (k.Ranged  < cap) k.Ranged++;  break;
                case UnitClass.Siege:   if (k.Siege   < cap) k.Siege++;   break;
                case UnitClass.Support: if (k.Support < cap) k.Support++; break;
                case UnitClass.Magic:   if (k.Magic   < cap) k.Magic++;   break;
                case UnitClass.Economy: if (k.Economy < cap) k.Economy++; break;
                case UnitClass.Miner:   if (k.Miner   < cap) k.Miner++;   break;
                case UnitClass.Scout:   if (k.Scout   < cap) k.Scout++;   break;
            }
        }

        /// <summary>
        /// Read the per-class kill count for a unit. Returns 0 if the attacker
        /// has no AntiquityKills component yet. Public so CombatDamageHelper
        /// can fold the bonus in without duplicating the switch.
        /// </summary>
        public static byte KillsAgainst(in AntiquityKills k, UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Melee:   return k.Melee;
                case UnitClass.Ranged:  return k.Ranged;
                case UnitClass.Siege:   return k.Siege;
                case UnitClass.Support: return k.Support;
                case UnitClass.Magic:   return k.Magic;
                case UnitClass.Economy: return k.Economy;
                case UnitClass.Miner:   return k.Miner;
                case UnitClass.Scout:   return k.Scout;
                default: return 0;
            }
        }
    }
}
