// TorchThrows.cs
// The sim -> presentation hand-off for a thrown torch (decision 82,
// 2026-11-19; docs/Design/Combat_Pacing.md § Buildings).
//
// A torch thrower's hit on a building lands on the swing, in the simulation,
// exactly as a blade's did (MeleeCombatSystem.Swing). Nothing in the
// simulation flies: the torch the player sees is drawn afterwards by
// TheWaningBorder.Rendering.TorchThrowVisual, which drains this queue every
// frame. Nothing is queued unless that visual is listening, so a headless run
// or a replay verification never fills it; the cap bounds a frame in which the
// presentation stalls.
//
// Presentation only: never read by the simulation, not part of a save
// (SnapshotState captures an explicit list of static owners).

using System.Collections.Generic;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Combat
{
    public static class TorchThrows
    {
        public struct Throw
        {
            /// <summary>The thrower's feet.</summary>
            public float3 From;
            /// <summary>The point of the building's edge nearest the thrower,
            /// at the building's ground height.</summary>
            public float3 To;
        }

        /// <summary>Set by the presentation while it draws torches.</summary>
        public static bool Listening;

        /// <summary>Throws held between two presentation frames, at most.
        /// Sizes a buffer, it does not tune play.</summary>
        private const int Cap = 512;

        private static readonly List<Throw> _pending = new List<Throw>(64);

        public static void Post(float3 from, float3 to)
        {
            if (!Listening || _pending.Count >= Cap) return;
            _pending.Add(new Throw { From = from, To = to });
        }

        /// <summary>Moves every queued throw into <paramref name="into"/>.</summary>
        public static void Drain(List<Throw> into)
        {
            if (_pending.Count == 0) return;
            into.AddRange(_pending);
            _pending.Clear();
        }

        public static void Clear() => _pending.Clear();
    }
}
