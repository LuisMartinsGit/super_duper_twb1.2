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
//   * walks into range, then casts Cleanse and channels — aimed
//     cleanseAimInset x the starting radius past the spot, into the patch,
//     because the starting circle clears at once (2026-10-19);
//   * lets go of a channel whose circle is fully grown or has no cursed
//     ground left near it (cleanseDoneMargin), so the Litharch heals again.
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
            return _cleanseErrand.TryGetValue(h, out float until) && now < until;
        }

        private void TickCleanse(EntityManager em, Faction faction, List<Entity> healers, float now)
        {
            var cfg = AISupport.Cfg;
            if (!cfg.cleanseEnabled || healers.Count == 0) return;
            if (!TheWaningBorder.Systems.Border.CurseGroundSystem.Ready) return;
            int cleanse = AbilityCatalog.IndexOf("Cleanse");
            var wardCard = AbilityCatalog.Get(cleanse);
            if (wardCard == null) return;
            int pulse = AbilityCatalog.IndexOf("Cleanse Pulse");

            // A channel never ends by itself: let go of one that has nothing
            // left to do, so the Litharch returns to healing and its cooldown
            // runs (2026-10-19). Before the target scan, which returns early
            // once no cursed ground is left — exactly when a channel is done.
            ReleaseFinishedCleanses(em, healers, cfg.cleanseDoneMargin);

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
                var ua = em.GetComponentData<UnitAbilities>(h);
                int wardSlot = SlotOf(ua, cleanse);
                int pulseSlot = pulse >= 0 ? SlotOf(ua, pulse) : -1;
                bool wardReady = wardSlot >= 0 && Ready(em, h, wardSlot);
                bool pulseReady = pulseSlot >= 0 && Ready(em, h, pulseSlot);
                if (!wardReady && !pulseReady) continue;

                float3 p = em.GetComponentData<LocalTransform>(h).Position;
                if (!TheWaningBorder.Systems.Border.CurseGroundSystem.TryNearestCursed(
                        p, cfg.cleanseSearchRadius, t => _cleanseTargets.Contains(t), out float3 spot))
                    continue;
                // A ward already covers that ground, or the team is at its
                // cap (CleanseWardSystem.asset): the engine would refuse the
                // ward, so this ground gets the pulse, which has neither rule.
                if (wardReady && !TheWaningBorder.Entities.CleanseWard.CanPlant(em, faction, spot, out _))
                    wardReady = false;
                if (!wardReady && !pulseReady) continue;
                bool castWard = wardReady;
                int slot = castWard ? wardSlot : pulseSlot;
                var card = castWard ? wardCard : AbilityCatalog.Get(pulse);
                float dist = math.distance(p.xz, spot.xz);

                // Aim INTO the patch: the starting circle clears at once, so
                // centring it on the nearest (edge) cell would spend half of it
                // on clean ground. Pushed along the line from the Litharch, by
                // less than the radius, so the cell stays inside the circle.
                float3 aim = spot;
                if (dist > 0.01f && card.Radius > 0f)
                {
                    float inset = math.clamp(cfg.cleanseAimInset, 0f, 0.95f) * card.Radius;
                    aim = spot + (spot - p) / dist * inset;
                    aim.y = spot.y;
                }
                if (ArmedHostileNear(em, faction, spot, cfg.cleanseThreatRadius)
                    || ArmedHostileNear(em, faction, aim, cfg.cleanseThreatRadius)) continue;

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
                // A ward stands on the build grid, inside the reach, on ground
                // that can take it (LitharchCleanse.Begin refuses anything else):
                // the nearest such site that still covers the cell. If there is
                // none, the pulse instead, or a later think.
                if (castWard && !TryWardSite(em, h, wardCard, p, spot, ref aim))
                {
                    if (!pulseReady) continue;
                    castWard = false;
                    slot = pulseSlot;
                    card = AbilityCatalog.Get(pulse);
                }
                // The aim must be in sight (Spells.md §6.1), or the engine
                // refuses the cast: hold it for a later think instead.
                if (!AbilityTargetRules.AimInSight(em, card, h, aim)) continue;
                if (CommandRouter.IssueUnitAbilitySlot(em, h, slot, Entity.Null, aim, CommandSource.AI))
                {
                    _cleanseErrand[h] = now + cfg.cleanseErrandSeconds;
                    AILogger.Log(faction, "SUPPORT",
                        $"Litharch {(castWard ? "plants a ward" : "pulses")} at ({spot.x:0},{spot.z:0}) in {RegionMap.NameOf(RegionMap.RegionAt(spot.x, spot.z))}");
                }
            }
        }

        /// <summary>
        /// The ward site nearest <paramref name="aim"/> the engine will accept
        /// and whose starting circle still covers <paramref name="spot"/>:
        /// the aim, drawn back inside the reach, then rings around it. The
        /// card's own test answers (CleanseWard.SiteRefusal), so nothing the
        /// engine would refuse is ever cast.
        /// </summary>
        private static bool TryWardSite(EntityManager em, Entity h, AbilityCard card, float3 from, float3 spot, ref float3 aim)
        {
            float2 d = aim.xz - from.xz;
            float len = math.length(d);
            float reach = card.Range > 0f ? math.max(0f, card.Range - BuildGrid.CellSize) : float.MaxValue;
            float3 centre = len > reach
                ? new float3(from.x + d.x / len * reach, aim.y, from.z + d.y / len * reach)
                : aim;

            const int Rings = 3, Samples = 8;
            for (int r = 0; r <= Rings; r++)
                for (int k = 0; k < (r == 0 ? 1 : Samples); k++)
                {
                    float a = k / (float)Samples * 2f * math.PI;
                    float metres = r * BuildGrid.CellSize;
                    float3 site = TheWaningBorder.Entities.CleanseWard.SiteFor(
                        centre + new float3(math.cos(a) * metres, 0f, math.sin(a) * metres));
                    if (card.Radius > 0f && math.distance(site.xz, spot.xz) >= card.Radius) continue;
                    if (TheWaningBorder.Entities.CleanseWard.SiteRefusal(em, h, card, site) != null) continue;
                    aim = site;
                    return true;
                }
            return false;
        }

        /// <summary>Nothing to release since 2026-10-22: a Cleanse plants a
        /// ward that pulses on its own (Territory_Claims.md §6.9.7); the
        /// Litharch is free the moment it lands.</summary>
        private void ReleaseFinishedCleanses(EntityManager em, List<Entity> healers, float margin) { }

        private static int SlotOf(UnitAbilities ua, int idx)
        {
            for (int s = 0; s < UnitAbilities.Count; s++) if (ua.Get(s) == idx) return s;
            return -1;
        }

        private static bool Ready(EntityManager em, Entity e, int slot)
        {
            if (!em.HasComponent<AbilityCooldowns>(e)) return true;
            var c = em.GetComponentData<AbilityCooldowns>(e);
            return c.Get(slot) <= 0f;
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
