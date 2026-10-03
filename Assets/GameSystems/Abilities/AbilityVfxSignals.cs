// AbilityVfxSignals.cs
// The sim → presentation bridge for ability visuals. AbilityEffectExecutor
// posts one entry each time an ability lands; AbilityVfxPlayer drains the
// queue each frame and spawns the card's effect. Same shape as SimSignals:
// a plain static queue the sim writes and never reads, so nothing about it can
// feed back into the lockstep simulation.

using System.Collections.Generic;
using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class AbilityVfxSignals
    {
        public struct Landed
        {
            public AbilityCard Card;
            public Entity Caster;
            /// <summary>The entity the effects applied to (the caster for a
            /// self-cast or an area formed around him).</summary>
            public Entity Target;
            /// <summary>Seconds the effect lasts; 0 = instant.</summary>
            public float Duration;
        }

        private static readonly Queue<Landed> _queue = new Queue<Landed>();
        private const int Cap = 256;   // nobody draining (headless / tests): drop, never grow

        public static void Post(AbilityCard card, Entity caster, Entity target, float duration)
        {
            if (card == null || card.Vfx == null) return;
            if (_queue.Count >= Cap) _queue.Dequeue();
            _queue.Enqueue(new Landed { Card = card, Caster = caster, Target = target, Duration = duration });
        }

        public static bool TryTake(out Landed landed)
        {
            if (_queue.Count > 0) { landed = _queue.Dequeue(); return true; }
            landed = default;
            return false;
        }

        public static void Clear() => _queue.Clear();
    }
}
