// SimpleAISystem.Seen.cs
// THE AI KNOWS WHAT IT HAS SEEN (2026-10-25, docs/Design/Game_AI.md § 6v).
//
// Developer: "AI is targeting things it never saw." Several pickers read the
// live entity table and validated a hostile entity with nothing, or with
// FogOfWarManager.IsRevealed / AICommon.IsKnownGround — "this GROUND was
// explored once", which says nothing about a building raised there later.
// Every wave/raid/chain/claim/Sanctum destination and every board read that
// chooses a victim now goes through these helpers:
//
//   * SEEN   — the entity is in this faction's EnemySightingRecord buffer
//              (IntelSystem upserts only what stands inside the faction's
//              fog VISIBLE grid; buildings stay remembered until they die,
//              mobile units age out after IntelSystem.mobileSightingMaxAge).
//   * VISIBLE NOW — FogOfWarManager.IsVisible(faction, pos): the per-faction
//              (team-merged) visibility grid the simulation stamps — never
//              the local player's fog presentation.
//
// Public knowledge needs neither: start positions (the lobby shows them),
// territory ownership (every border is drawn), the Shardroot beacon.
// Host-side AI state; the buffer is walked in index order (deterministic).

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Data.AI;
using TheWaningBorder.World.FogOfWar;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        /// <summary>Host-only scratch: the entities the brain filled it for
        /// has seen. Filled by <see cref="FillSeen"/>, never held across a call.</summary>
        private static readonly HashSet<Entity> _seenScratch = new HashSet<Entity>();

        /// <summary>Is <paramref name="pos"/> inside <paramref name="faction"/>'s
        /// sim-side visibility grid right now? (No fog = everything visible.)</summary>
        private static bool VisibleNow(Faction faction, float3 pos)
        {
            var fog = FogOfWarManager.Instance;
            return fog == null || fog.IsVisible(faction, new UnityEngine.Vector3(pos.x, 0f, pos.z));
        }

        /// <summary>Fill <see cref="_seenScratch"/> with every entity the
        /// faction's brain has a sighting of. False when it has no memory.</summary>
        private static bool FillSeen(EntityManager em, Entity brain)
        {
            _seenScratch.Clear();
            if (brain == Entity.Null || !em.HasBuffer<EnemySightingRecord>(brain)) return false;
            var buf = em.GetBuffer<EnemySightingRecord>(brain);
            for (int i = 0; i < buf.Length; i++) _seenScratch.Add(buf[i].Enemy);
            return true;
        }

        /// <summary>Has the faction seen <paramref name="e"/> (sighting memory)
        /// or does it stand in vision now? Valid after <see cref="FillSeen"/>.</summary>
        private static bool SeenOrVisible(Faction faction, Entity e, float3 pos)
            => _seenScratch.Contains(e) || VisibleNow(faction, pos);

        /// <summary>The known military of <paramref name="of"/>: head count and
        /// summed strength of the MilitaryUnit sightings no older than
        /// <paramref name="maxAge"/> seconds (0 = every remembered one).</summary>
        private static void KnownArmyOf(EntityManager em, Entity brain, Faction of, float maxAge,
            out int count, out int strength)
        {
            count = 0; strength = 0;
            if (brain == Entity.Null || !em.HasBuffer<EnemySightingRecord>(brain)) return;
            var buf = em.GetBuffer<EnemySightingRecord>(brain);
            float simNow = (float)TheWaningBorder.Core.SimClock.Now;
            for (int i = 0; i < buf.Length; i++)
            {
                var s = buf[i];
                if (s.OwnerFaction != of || s.Category != IntelCategory.MilitaryUnit) continue;
                if (maxAge > 0f && simNow - s.LastSeenTime > maxAge) continue;
                count++;
                strength += math.max(0, s.EstStrength);
            }
        }

        /// <summary>Has the faction ever seen anything of <paramref name="of"/>?</summary>
        private static bool KnowsOf(EntityManager em, Entity brain, Faction of)
        {
            if (brain == Entity.Null || !em.HasBuffer<EnemySightingRecord>(brain)) return false;
            var buf = em.GetBuffer<EnemySightingRecord>(brain);
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].OwnerFaction == of) return true;
            return false;
        }

        /// <summary>
        /// The board as <paramref name="observer"/> knows it: territories
        /// (public) x 8 plus the military it has SEEN of <paramref name="f"/>
        /// (its own army it knows exactly). Negative when the faction has no
        /// Hall — elimination is announced to everyone. Replaces the live
        /// CountAliveMilitary read the victim pickers used.
        /// </summary>
        private static int KnownBoardScore(EntityManager em, Faction observer, Faction f)
        {
            if (FindFactionBuilding<HallTag>(em, f) == Entity.Null) return -1;
            int land = TheWaningBorder.World.Regions.TerritoryOwnership.CountOf(f) * 8;
            if (f == observer) return land + CountAliveMilitary(em, f);
            KnownArmyOf(em, FindBrainEntity(em, observer), f, 0f, out int seen, out _);
            return land + seen;
        }
    }
}
