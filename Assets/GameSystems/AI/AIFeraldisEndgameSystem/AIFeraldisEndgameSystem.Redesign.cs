// AIFeraldisEndgameSystem.Redesign.cs
// The Feraldis AI for the 2026-10-12 redesign (docs/Design/Age_1_Feraldis.md):
// slaves staff the extractors and the Forge, the slave cap is kept full (and
// the Pens grow with it), the Great Forge's bays run the loop, the uniques are
// trained, Harkon Fiend fights with his abilities, totems run rites when the
// enemy is near, and spare slaves are sacrificed at the Blood Altar for
// religion. Every order goes through CommandRouter with CommandSource.AI.
// Partial of AIFeraldisEndgameSystem.cs.

using System.Collections.Generic;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Feraldis;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    public partial struct AIFeraldisEndgameSystem : ISystem
    {
        static readonly ComponentType[] QT_Slaves =
        {
            ComponentType.ReadOnly<SlaveTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Slaves;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Buildings;

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Units;

        /// <summary>Slaves kept free of crews so the AI can still build.</summary>
        private const int FreeBuilders = 2;

        // ── slaves ──────────────────────────────────────────────────────────

        /// <summary>Put idle slaves to work on the extractors and the Forge that are short of crew.</summary>
        private static void StaffSlaves(EntityManager em, Faction faction)
        {
            var sq = QC_Slaves.Get(em, QT_Slaves);
            using var slaves = sq.ToEntityArray(Allocator.Temp);
            using var sfac = sq.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var idle = new List<Entity>();
            int free = 0;
            for (int i = 0; i < slaves.Length; i++)
            {
                if (sfac[i].Value != faction) continue;
                var s = slaves[i];
                if (em.HasComponent<StaffAssignment>(s) || em.HasComponent<FeraldisErrand>(s)) continue;
                if (TransientState.Active<BuildCommand>(em, s)) { free++; continue; }
                idle.Add(s);
            }
            // Leave a building crew behind.
            int spare = idle.Count - math.max(0, FreeBuilders - free);
            if (spare <= 0) return;

            // Crew already walking to or standing at each building.
            var claimed = new Dictionary<Entity, int>();
            for (int i = 0; i < slaves.Length; i++)
            {
                if (sfac[i].Value != faction) continue;
                Entity b = Entity.Null;
                if (em.HasComponent<StaffAssignment>(slaves[i])) b = em.GetComponentData<StaffAssignment>(slaves[i]).Building;
                else if (em.HasComponent<FeraldisErrand>(slaves[i])) b = em.GetComponentData<FeraldisErrand>(slaves[i]).Building;
                if (b == Entity.Null) continue;
                claimed.TryGetValue(b, out int n); claimed[b] = n + 1;
            }

            var bq = QC_Buildings.Get(em, QT_Buildings);
            using var blds = bq.ToEntityArray(Allocator.Temp);
            using var bfac = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bxf = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int next = 0, sent = 0;
            // The Forge first (its crew is what keeps the loop running), then the extractors.
            for (int pass = 0; pass < 2 && next < spare; pass++)
                for (int i = 0; i < blds.Length && next < spare; i++)
                {
                    if (bfac[i].Value != faction || em.HasComponent<UnderConstruction>(blds[i])) continue;
                    bool forge = em.HasComponent<GreatForgeTag>(blds[i]);
                    if ((pass == 0) != forge) continue;
                    int slots = FeraldisEconomy.CrewSlots(em, blds[i]);
                    if (slots <= 0) continue;
                    claimed.TryGetValue(blds[i], out int have);
                    while (have < slots && next < spare)
                    {
                        CommandRouter.IssueFeraldisOrder(em, idle[next++], blds[i], FeraldisOp.Staff, 0, default, CommandSource.AI);
                        have++; sent++;
                    }
                    claimed[blds[i]] = have;
                }
            if (sent > 0) AILogger.Log(faction, "ECONOMY", $"staffed {sent} slave(s) onto extractors / the Forge");
        }

        /// <summary>Keep the slave cap full, grow the Pens when it is reached, and raise the uniques.</summary>
        private static void TrainSlavesAndUniques(EntityManager em, Faction faction, float3 hallPos)
        {
            Entity fortress = FindOwn<HallTag>(em, faction);
            if (fortress == Entity.Null) return;

            // The Great Forge comes first once a working crew exists: in the
            // 2026-10-12 probe the AI kept spending every supply on slaves and
            // Pens and never had the price of the Forge in the bank.
            bool saveForForge = FindOwn<GreatForgeTag>(em, faction) == Entity.Null
                                && CountLiveSlaves(em, faction) >= 8;

            if (saveForForge) { }
            else if (!FeraldisEconomy.SlaveCapReached(em, faction))
            {
                if (TechCatalog.TryGetUnit(FeraldisEconomy.SlaveId, out var sd) && sd.cost != null
                    && FactionEconomy.CanAfford(em, faction, Cost.Of(sd.cost.Supplies, sd.cost.Iron, sd.cost.Veilstone))
                    && CommandRouter.CanTrainAtBuilding(em, fortress, FeraldisEconomy.SlaveId, out _, out _))
                    CommandRouter.IssueTrain(em, fortress, FeraldisEconomy.SlaveId, CommandSource.AI);
            }
            else if (CountFactionWith<HutTag>(em, faction) < 12)
            {
                // The cap is reached: another Slave Pen.
                TryPlace(em, faction, "Hut", hallPos, 10f, 40f, AIBudgetCategory.EconomyExpansion);
            }

            foreach (var id in new[] { FeraldisEconomy.OverseerId, FeraldisEconomy.HarkonId })
            {
                if (FeraldisEconomy.CountLiveOrQueued(em, faction, id) > 0) continue;
                if (!CommandRouter.CanTrainAtBuilding(em, fortress, id, out _, out _)) continue;
                if (!TechCatalog.TryGetUnit(id, out var ud) || ud.cost == null) continue;
                if (!FactionEconomy.CanAfford(em, faction, Cost.Of(ud.cost.Supplies, ud.cost.Iron, ud.cost.Veilstone, ud.cost.Veilsteel))) continue;
                CommandRouter.IssueTrain(em, fortress, id, CommandSource.AI);
                AILogger.Log(faction, "MILITARY", $"{id} queued at the Fortress");
            }
        }

        // ── the Great Forge ─────────────────────────────────────────────────

        /// <summary>
        /// A fixed bay plan that runs the loop (§4.1): Veilsteel first, then
        /// Soulforge, Arms Export, a second Veilsteel. A bay whose recipe the
        /// bank cannot feed simply idles a cycle (the system skips it), so the
        /// plan never needs to Hold. More bays are bought while the bank is rich.
        /// </summary>
        private static readonly ForgeRecipe[] BayPlan =
            { ForgeRecipe.Veilsteel, ForgeRecipe.Soulforge, ForgeRecipe.ArmsExport, ForgeRecipe.Veilsteel };

        private static void ManageForge(EntityManager em, Faction faction)
        {
            Entity forge = FindOwn<GreatForgeTag>(em, faction);
            if (forge == Entity.Null || em.HasComponent<UnderConstruction>(forge) || !em.HasBuffer<ForgeBay>(forge)) return;
            if (!FactionEconomy.TryGetResources(em, faction, out var bank)) return;

            var bays = em.GetBuffer<ForgeBay>(forge, true);
            int count = bays.Length;
            for (int i = 0; i < count; i++)
            {
                var want = BayPlan[math.min(i, BayPlan.Length - 1)];
                // Arms Export only when there is veilsteel to spare.
                if (want == ForgeRecipe.ArmsExport && bank.Veilsteel < 150) want = ForgeRecipe.Veilsteel;
                if (bays[i].Recipe != want)
                {
                    CommandRouter.IssueFeraldisOrder(em, forge, Entity.Null, FeraldisOp.ForgeSetRecipe, i * 8 + (int)want, default, CommandSource.AI);
                    bays = em.GetBuffer<ForgeBay>(forge, true);
                }
            }

            if (GreatForge.CanAddBay(em, forge))
            {
                var cost = GreatForge.NextBayCost(em, forge);
                if (bank.Supplies > cost.Supplies * 3 && bank.Iron > cost.Iron * 2)
                    CommandRouter.IssueFeraldisOrder(em, forge, Entity.Null, FeraldisOp.ForgeAddBay, 0, default, CommandSource.AI);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    if (GreatForge.TryUpgradeCost(em, forge, i, out var up)
                        && bank.Supplies > up.Supplies * 3 && bank.Iron > up.Iron * 2 && bank.Veilstone >= up.Veilstone)
                    {
                        CommandRouter.IssueFeraldisOrder(em, forge, Entity.Null, FeraldisOp.ForgeUpgradeBay, i, default, CommandSource.AI);
                        break;
                    }
            }
        }

        // ── Harkon Fiend ───────────────────────────────────────────────────

        private static void UseHarkon(EntityManager em, Faction faction)
        {
            Entity harkon = Entity.Null;
            foreach (var (u, f, e) in QueryUniques(em))
                if (f == faction && u == UniqueUnitKind.HarkonFiend) { harkon = e; break; }
            if (harkon == Entity.Null || !em.HasComponent<UnitAbilities>(harkon)) return;
            float3 at = em.GetComponentData<LocalTransform>(harkon).Position;

            // Raze the hostile building nearest him (curse nodes included).
            Entity bld = NearestHostile(em, faction, at, 12f, buildings: true, out float3 bpos);
            // Raze is cast ON the building (docs/Design/Spells.md §6).
            if (bld != Entity.Null && TryCast(em, harkon, "Raze", null, bld)) return;

            // In a fight: Warcry, and Blood Oath when he is badly hurt.
            Entity foe = NearestHostile(em, faction, at, 14f, buildings: false, out _);
            if (foe == Entity.Null) return;
            var hp = em.GetComponentData<Health>(harkon);
            if (hp.Max > 0 && hp.Value < hp.Max / 2 && TryCast(em, harkon, "Blood Oath", null)) return;
            TryCast(em, harkon, "Warcry", null);
        }

        private static bool TryCast(EntityManager em, Entity unit, string ability, float3? aim,
            Entity target = default)
        {
            int idx = AbilityCatalog.IndexOf(ability);
            if (idx < 0) return false;
            var ua = em.GetComponentData<UnitAbilities>(unit);
            int slot = ua.S0 == idx ? 0 : ua.S1 == idx ? 1 : ua.S2 == idx ? 2 : ua.S3 == idx ? 3 : -1;
            if (slot < 0) return false;
            var card = AbilityCatalog.Get(idx);
            if (card == null || !AbilityQuery.IsUnlocked(em, unit, card)) return false;
            if (em.HasComponent<AbilityCooldowns>(unit))
            {
                var cd = em.GetComponentData<AbilityCooldowns>(unit);
                float left = slot == 0 ? cd.C0 : slot == 1 ? cd.C1 : slot == 2 ? cd.C2 : cd.C3;
                if (left > 0f) return false;
            }
            // A single-target card refuses a target it would not accept, so a
            // building out of reach is held here instead of being sent.
            // An unseen target or aim point is held too (Spells.md §6.1).
            if (card.IsEntityTargeted && !AbilityTargetRules.CheckInSight(em, card, unit, target, out _)) return false;
            if (aim.HasValue && !AbilityTargetRules.AimInSight(em, card, unit, aim.Value)) return false;
            return CommandRouter.IssueUnitAbility(em, unit, target, aim, CommandSource.AI, slot);
        }

        // ── totems ─────────────────────────────────────────────────────────

        private static void UseTotemRites(EntityManager em, Faction faction)
        {
            var bq = QC_Buildings.Get(em, QT_Buildings);
            using var blds = bq.ToEntityArray(Allocator.Temp);
            using var bfac = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bxf = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < blds.Length; i++)
            {
                if (bfac[i].Value != faction || !em.HasComponent<WarTotemTag>(blds[i])) continue;
                if (NearestHostile(em, faction, bxf[i].Position, WarTotemRites.Cfg.radius, buildings: false, out _) == Entity.Null) continue;
                foreach (var rite in new[] { TotemRite.BloodMoon, TotemRite.RedMist, TotemRite.Bloodlust, TotemRite.Hunger })
                    if (WarTotemRites.CanBegin(em, blds[i], rite))
                    {
                        CommandRouter.IssueFeraldisOrder(em, blds[i], Entity.Null, FeraldisOp.TotemRite, (int)rite, default, CommandSource.AI);
                        break;
                    }
            }
        }

        // ── religion: sacrifice ────────────────────────────────────────────

        /// <summary>With the slave cap full and religion points short, one spare slave goes to the Blood Altar —
        /// or, with no finished Altar, is sacrificed in the field (both pay points).</summary>
        private static readonly Dictionary<Faction, double> _lastSacrifice = new Dictionary<Faction, double>();
        private const double SacrificeEverySeconds = 120;

        private static void SacrificeForReligion(EntityManager em, Faction faction)
        {
            // Only from a full crew of LIVE slaves: SlaveCapReached counts the
            // queue too, and sacrificing against it bled the economy dry (the
            // 2026-10-12 probe: ten slaves sent to the Altar in five minutes).
            if (CountLiveSlaves(em, faction) < FeraldisEconomy.SlaveCap(em, faction)) return;
            if (FactionReligionPointsHelper.GetBalance(em, faction) >= 3) return;
            double now = UnityEngine.Time.timeAsDouble;
            if (_lastSacrifice.TryGetValue(faction, out double last) && now - last < SacrificeEverySeconds && now >= last) return;
            // NO ALTAR, NO SOFT LOCK (Religion.md §7, 2026-10-13): a field
            // sacrifice pays points too, so a faction with no finished Altar
            // gives one slave to the field instead (same rate limit). At the
            // Altar the same slave is worth altarSacrificeMultiplier times as
            // much, so the Altar is preferred whenever it stands.
            Entity altar = FindOwn<TempleOfRidanTag>(em, faction);
            bool haveAltar = altar != Entity.Null && !em.HasComponent<UnderConstruction>(altar);

            var sq = QC_Slaves.Get(em, QT_Slaves);
            using var slaves = sq.ToEntityArray(Allocator.Temp);
            using var sfac = sq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < slaves.Length; i++)
                if (sfac[i].Value == faction && em.HasComponent<FeraldisErrand>(slaves[i])
                    && em.GetComponentData<FeraldisErrand>(slaves[i]).Kind == FeraldisErrandKind.Sacrifice) return;   // one at a time
            for (int i = 0; i < slaves.Length; i++)
            {
                if (sfac[i].Value != faction) continue;
                var s = slaves[i];
                if (em.HasComponent<StaffAssignment>(s) || em.HasComponent<FeraldisErrand>(s) || TransientState.Active<BuildCommand>(em, s)) continue;
                if (haveAltar)
                {
                    CommandRouter.IssueFeraldisOrder(em, s, altar, FeraldisOp.Sacrifice, 0, default, CommandSource.AI);
                    AILogger.Log(faction, "RELIGION", "a slave sent to the Blood Altar");
                }
                else
                {
                    CommandRouter.IssueFeraldisOrder(em, s, Entity.Null, FeraldisOp.FieldSacrifice, 0, default, CommandSource.AI);
                    AILogger.Log(faction, "RELIGION", "a slave sacrificed in the field (no Blood Altar)");
                }
                _lastSacrifice[faction] = now;
                return;
            }
        }

        // ── helpers ────────────────────────────────────────────────────────

        private static int CountLiveSlaves(EntityManager em, Faction faction)
        {
            var q = QC_Slaves.Get(em, QT_Slaves);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++) if (facs[i].Value == faction) n++;
            return n;
        }

        private static Entity FindOwn<T>(EntityManager em, Faction faction) where T : unmanaged, IComponentData
        {
            var q = AIQueryCache.TagFaction<T>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<FactionTag>(ents[i]).Value == faction) return ents[i];
            return Entity.Null;
        }

        private static Entity NearestHostile(EntityManager em, Faction faction, float3 at, float range, bool buildings, out float3 pos)
        {
            pos = default;
            var q = buildings ? QC_Buildings.Get(em, QT_Buildings) : QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null; float bd = range * range;
            for (int i = 0; i < ents.Length; i++)
            {
                if (!Alliances.AreHostile(faction, facs[i].Value)) continue;
                float d = math.distancesq(at.xz, xfs[i].Position.xz);
                if (d > bd) continue;
                if (em.HasComponent<Health>(ents[i]) && em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                bd = d; best = ents[i]; pos = xfs[i].Position;
            }
            return best;
        }

        static readonly ComponentType[] QT_Uniques =
        {
            ComponentType.ReadOnly<UniqueUnitTag>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Uniques;

        private static List<(int kind, Faction faction, Entity e)> QueryUniques(EntityManager em)
        {
            var list = new List<(int, Faction, Entity)>();
            var q = QC_Uniques.Get(em, QT_Uniques);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UniqueUnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++) list.Add((tags[i].Kind, facs[i].Value, ents[i]));
            return list;
        }
    }
}
