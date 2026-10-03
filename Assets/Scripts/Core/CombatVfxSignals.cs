// CombatVfxSignals.cs
// The sim → presentation bridge for hit effects. CombatDamageHelper posts one
// entry per landed melee / projectile hit; UnitCombatVfx drains it each frame.
// Same shape as SimSignals: a static queue the sim writes and never reads, so
// it cannot feed back into the lockstep simulation.

using System.Collections.Generic;
using Unity.Entities;

namespace TheWaningBorder.Core
{
    /// <summary>What delivered a hit — decides which effect it gets
    /// (docs/Design/Vfx_Assignments.md §1).</summary>
    public enum HitSource : byte
    {
        Melee = 0,
        /// <summary>A plain arrow: its effect follows the shooter's arrow-tip tier.</summary>
        Arrow = 1,
        /// <summary>A ballista bolt or any special projectile (curse lasers,
        /// Veilstinger, Godsplinter): the generic hit.</summary>
        Bolt = 2,
    }

    public static class CombatVfxSignals
    {
        public struct Hit
        {
            public Entity Target;
            public HitSource Source;
            /// <summary>The attacker was charging (cavalry or infantry charge).</summary>
            public bool Charge;
            /// <summary>The attacker's faction (its arrow-tip tier for arrows).</summary>
            public Faction AttackerFaction;
        }

        private static readonly Queue<Hit> _queue = new Queue<Hit>();
        private const int Cap = 512;   // nobody draining (headless / tests): drop, never grow

        public static void Post(Entity target, HitSource source, bool charge, Faction attackerFaction)
        {
            if (_queue.Count >= Cap) _queue.Dequeue();
            _queue.Enqueue(new Hit { Target = target, Source = source, Charge = charge, AttackerFaction = attackerFaction });
        }

        public static bool TryTake(out Hit hit)
        {
            if (_queue.Count > 0) { hit = _queue.Dequeue(); return true; }
            hit = default;
            return false;
        }

        public static void Clear() => _queue.Clear();
    }
}
