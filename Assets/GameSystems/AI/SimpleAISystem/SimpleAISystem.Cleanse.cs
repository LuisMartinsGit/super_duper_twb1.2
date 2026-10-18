// SimpleAISystem.Cleanse.cs
// LITHARCHS CLEANSE THE GROUND (2026-10-18, docs/Design/Game_AI.md § 6n,
// Territory_Claims.md §6.9.7).
//
// Cursed ground locks a territory: nobody can claim it while any of it is
// cursed (§6.9.4), and the curse creeps into held ground too. A Litharch's
// Cleanse clears a growing circle while it channels. On the support cadence,
// each free Litharch of the faction:
//   * takes the nearest cursed cell within cleanseSearchRadius that lies in
//     ground the faction OWNS, or in ground it WANTS — a territory bordering
//     its Fortress-linked ground with no living curse node in it (a node's
//     own ground grows back; that is the army's job, SimpleAISystem.CurseClear),
//     once claims are open (aged up);
//   * skips it if an armed hostile (the curse included) stands within
//     cleanseThreatRadius of the spot — a Litharch does not walk into a fight;
//   * walks into range, then casts Cleanse at the spot and channels.
// A Litharch on that errand is left out of the army-follow step for
// cleanseErrandSeconds, and one channelling is never moved (a move order
// would end the channel). Once the ground is clean the claim logic sees the
// territory as a candidate again on its own.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        /// <summary>Healer → match time its cleanse errand lapses.</summary>
        private readonly Dictionary<Entity, float> _cleanseErrand = new Dictionary<Entity, float>();
        private readonly HashSet<int> _cleanseTargets = new HashSet<int>();

        static readonly ComponentType[] QT_Armed =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<Damage>(),
        };
        static CachedEntityQuery QC_Armed;

        /// <summary>Is this healer busy with a cleanse (on its way, or
        /// channelling)? The follow step leaves it alone.</summary>
        private bool OnCleanseErrand(EntityManager em, Entity h, float now)
        {
            if (LitharchCleanse.IsChanneling(em, h)) return true;
            return _cleanseErrand.TryGetValue(h, out float until) && now < until;
        }

        private void TickCleanse(EntityManager em, Faction faction, List<Entity> healers, float now)
        {
            var cfg = AISupport.Cfg;
            if (!cfg.cleanseEnabled || healers.Count == 0) return;
            if (!TheWaningBorder.Systems.Border.CurseGroundSystem.Ready) return;
            int cleanse = AbilityCatalog.IndexOf("Cleanse");
            var card = AbilityCatalog.Get(cleanse);
            if (card == null) return;

            // The ground this faction may cleanse: what it holds, and — once
            // claims are open — what it would claim but for the curse.
            _cleanseTargets.Clear();
            var mine = TerritoryOwnership.TerritoriesOf(faction);
            for (int i = 0; i < mine.Count; i++)
                if (TheWaningBorder.Systems.Border.CurseGroundSystem.TerritoryHasCurse(mine[i]))
                    _cleanseTargets.Add(mine[i]);
            if (CultureConfig.GetCompletedCulture(em, faction) != Cultures.None)
            {
                for (int r = 0; r < RegionMap.Count; r++)
                {
                    if (TerritoryOwnership.OwnerOf(r) == (int)faction) continue;
                    if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                    if (!TheWaningBorder.Systems.Border.CurseGroundSystem.TerritoryHasCurse(r)) continue;
                    if (TerritoryOwnership.IsCurseHeld(r)) continue;          // a living node: the army's job
                    if (!BordersFortressGround(faction, r, mine)) continue;
                    _cleanseTargets.Add(r);
                }
            }
            if (_cleanseTargets.Count == 0) return;

            foreach (var h in healers)
            {
                if (!em.HasComponent<LocalTransform>(h) || !em.HasComponent<UnitAbilities>(h)) continue;
                if (LitharchCleanse.IsChanneling(em, h)) continue;
                int slot = SlotOf(em.GetComponentData<UnitAbilities>(h), cleanse);
                if (slot < 0 || !Ready(em, h, slot)) continue;

                float3 p = em.GetComponentData<LocalTransform>(h).Position;
                if (!TheWaningBorder.Systems.Border.CurseGroundSystem.TryNearestCursed(
                        p, cfg.cleanseSearchRadius, t => _cleanseTargets.Contains(t), out float3 spot))
                    continue;
                if (ArmedHostileNear(em, faction, spot, cfg.cleanseThreatRadius)) continue;

                float dist = math.distance(p.xz, spot.xz);
                if (card.Range > 0f && dist > card.Range)
                {
                    // Walk to just inside the reach, then cast on a later think.
                    float3 toward = spot + (p - spot) / math.max(0.01f, dist) * (card.Range - 2f);
                    toward.y = spot.y;
                    CommandRouter.IssueMove(em, h, toward, CommandSource.AI);
                    _cleanseErrand[h] = now + cfg.cleanseErrandSeconds;
                    AILogger.Log(faction, "SUPPORT",
                        $"Litharch walks to cleanse cursed ground at ({spot.x:0},{spot.z:0}) in {RegionMap.NameOf(RegionMap.RegionAt(spot.x, spot.z))}");
                    continue;
                }
                if (CommandRouter.IssueUnitAbilitySlot(em, h, slot, Entity.Null, spot, CommandSource.AI))
                {
                    _cleanseErrand[h] = now + cfg.cleanseErrandSeconds;
                    AILogger.Log(faction, "SUPPORT",
                        $"Litharch cleanses at ({spot.x:0},{spot.z:0}) in {RegionMap.NameOf(RegionMap.RegionAt(spot.x, spot.z))}");
                }
            }
        }

        private static int SlotOf(UnitAbilities ua, int idx)
        {
            for (int s = 0; s < 4; s++) if (ua.Get(s) == idx) return s;
            return -1;
        }

        private static bool Ready(EntityManager em, Entity e, int slot)
        {
            if (!em.HasComponent<AbilityCooldowns>(e)) return true;
            var c = em.GetComponentData<AbilityCooldowns>(e);
            float cd = slot == 0 ? c.C0 : slot == 1 ? c.C1 : slot == 2 ? c.C2 : c.C3;
            return cd <= 0f;
        }

        private static bool ArmedHostileNear(EntityManager em, Faction faction, float3 at, float radius)
        {
            if (radius <= 0f) return false;
            var q = QC_Armed.Get(em, QT_Armed);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var dmg = q.ToComponentDataArray<Damage>(Allocator.Temp);
            float r2 = radius * radius;
            for (int i = 0; i < facs.Length; i++)
            {
                if (hps[i].Value <= 0 || dmg[i].Value <= 0) continue;
                var f = facs[i].Value;
                if (f != Faction.Border && !Alliances.AreHostile(faction, f)) continue;
                if (math.distancesq(xfs[i].Position.xz, at.xz) <= r2) return true;
            }
            return false;
        }
    }
}
