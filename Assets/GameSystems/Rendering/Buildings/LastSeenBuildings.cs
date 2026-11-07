// LastSeenBuildings.cs
// The local player's memory of hostile buildings: what they looked like when
// last directly seen. docs/Design/Last_Seen_Buildings.md
//
// A hostile building appears only once it has been seen, and out of vision it
// stays as it was: FogVisibilitySyncSystem decides what shows, and every system
// that would change a building's look asks IsFrozen first. One destroyed out of
// sight leaves its model behind as a ghost until the spot is seen again.
//
// Presentation only: nothing here touches the simulation or the AI, so it cannot
// fork a lockstep match. Spectators never use it; they see their current view.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.World.FogOfWar;

namespace TheWaningBorder.Rendering
{
    public static class LastSeenBuildings
    {
        /// <summary>A building as it was when last directly seen.</summary>
        public struct Seen
        {
            public bool VisibleNow;
            public Faction Owner;
            public int Level;
            public byte Culture;
            public bool Depleted;
        }

        /// <summary>The model left behind by a building destroyed out of sight.</summary>
        public struct Ghost
        {
            public GameObject View;
            public float3 Position;
            public Faction Owner;
        }

        private static readonly Dictionary<Entity, Seen> _seen = new Dictionary<Entity, Seen>();
        private static readonly HashSet<Entity> _unseen = new HashSet<Entity>();
        private static readonly List<Ghost> _ghosts = new List<Ghost>();
        private static readonly byte?[] _culture = new byte?[Alliances.MaxFactions];
        private static int _epoch = -1;

        public static IReadOnlyList<Ghost> Ghosts => _ghosts;

        /// <summary>True when what the player sees of this building must not
        /// change: never seen, or out of vision now.</summary>
        public static bool IsFrozen(Entity e)
        {
            if (_seen.Count == 0 && _unseen.Count == 0) return false;
            if (_unseen.Contains(e)) return true;
            return _seen.TryGetValue(e, out var s) && !s.VisibleNow;
        }

        /// <summary>The record to show instead of live data, when frozen.</summary>
        public static bool TryGetFrozen(Entity e, out Seen seen)
            => _seen.TryGetValue(e, out seen) && !seen.VisibleNow;

        /// <summary>True once the player has directly seen this building.</summary>
        public static bool IsKnown(Entity e) => _seen.ContainsKey(e);

        /// <summary>Forgets a finished match. The system outlives the scene.</summary>
        public static void ResetIfNewMatch(int epoch)
        {
            if (_epoch == epoch) return;
            _epoch = epoch;
            Clear();
        }

        /// <summary>Drops everything, ghosts included.</summary>
        public static void Clear()
        {
            _seen.Clear();
            _unseen.Clear();
            foreach (var g in _ghosts)
                if (g.View != null) Object.Destroy(g.View);
            _ghosts.Clear();
        }

        /// <summary>Called once per fog pass, before any Observe.</summary>
        public static void BeginPass()
        {
            for (int i = 0; i < _culture.Length; i++) _culture[i] = null;
        }

        /// <summary>
        /// Takes one hostile building's state from the fog pass and answers
        /// whether its view shows. <paramref name="seedKnown"/> counts it as seen
        /// without vision: a loaded save has no memory, so it starts from the
        /// ground the player had explored.
        /// </summary>
        public static bool Observe(EntityManager em, Entity e, Faction owner, bool seesNow, bool seedKnown)
        {
            if (seesNow || seedKnown)
            {
                _unseen.Remove(e);
                _seen[e] = Snapshot(em, e, owner, seesNow);
                return true;
            }
            if (_seen.TryGetValue(e, out var s))
            {
                s.VisibleNow = false;
                _seen[e] = s;
                return true;
            }
            _unseen.Add(e);
            return false;
        }

        private static Seen Snapshot(EntityManager em, Entity e, Faction owner, bool visibleNow)
        {
            int level = 0;
            if (em.HasComponent<BuildingUpgradeState>(e))
                level = em.GetComponentData<BuildingUpgradeState>(e).Level;
            else if (em.HasComponent<WallTier>(e) && !em.HasComponent<PalisadeTag>(e))
                level = em.GetComponentData<WallTier>(e).Level;

            int f = (int)owner;
            byte culture = Cultures.None;
            if (f >= 0 && f < _culture.Length)
            {
                if (!_culture[f].HasValue) _culture[f] = CultureConfig.GetCompletedCulture(em, owner);
                culture = _culture[f].Value;
            }
            return new Seen
            {
                VisibleNow = visibleNow,
                Owner = owner,
                Level = level,
                Culture = culture,
                Depleted = em.HasComponent<DepletedHutTag>(e),
            };
        }

        /// <summary>
        /// The building's entity is gone. True when its view has been kept as a
        /// ghost, so the caller must not destroy it.
        /// </summary>
        public static bool TryKeepGhost(Entity e, GameObject view)
        {
            _unseen.Remove(e);
            if (!_seen.TryGetValue(e, out var s)) return false;
            _seen.Remove(e);
            if (s.VisibleNow || view == null) return false;

            // Only the look survives: nothing on it may read a dead entity, and
            // it is no longer anything to click on.
            foreach (var c in view.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var b in view.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            _ghosts.Add(new Ghost { View = view, Position = view.transform.position, Owner = s.Owner });
            return true;
        }

        /// <summary>Ghosts whose spot is in view again are gone.</summary>
        public static void TickGhosts(FogOfWarManager mgr, Faction viewer)
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                var g = _ghosts[i];
                if (g.View != null && !mgr.IsVisible(viewer, (Vector3)g.Position)) continue;
                if (g.View != null) Object.Destroy(g.View);
                _ghosts.RemoveAt(i);
            }
        }
    }
}
