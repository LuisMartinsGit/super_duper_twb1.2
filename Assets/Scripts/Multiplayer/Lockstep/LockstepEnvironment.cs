// LockstepEnvironment.cs
// The per-machine facts that a desync investigation needs and the sim state
// cannot show.
//
// WHY THIS EXISTS
// A lockstep desync is "the same code produced different numbers". Once the
// command streams are proven identical (LockstepLog does that), the next
// question is always "what is different about the two MACHINES" — and every
// answer to it lives outside the ECS world, so nothing in the diffable part of
// the log can carry it.
//
// The 2026-08-21 investigation stalled exactly here: three matched log pairs
// proved the fork lands on the tick a movement order is applied and that only
// float positions of moving units differ, but there was no record of the two
// machines' CPU, worker-thread count or Burst state to test the obvious next
// hypothesis against. This is that record.
//
// Every field below is chosen because it can plausibly change a float result
// or the ORDER floats are combined in:
//   * processor / core count -> how many job workers, hence batch boundaries
//   * job worker count       -> the same, directly
//   * Burst enabled + target -> which machine code actually runs the math
//   * culture                -> a decimal comma already produced a whole file
//                               of false diffs once (desync #6)
//   * fixed-step state       -> whether the sim is actually tick-driven here
//
// Written to the per-peer HEADER of Lockstep.log and to every desync dump. It
// deliberately never touches the diffable body: these values are SUPPOSED to
// differ between peers, and putting them in the body would drown the signal.

using System.Globalization;
using System.Text;
using UnityEngine;

namespace TheWaningBorder.Multiplayer
{
    public static class LockstepEnvironment
    {
        /// <summary>
        /// One "key = value" line per fact, each prefixed with <paramref name="prefix"/>
        /// so the caller can make them log comments ("# ") or plain dump lines ("").
        /// </summary>
        public static string Describe(string prefix = "")
        {
            var sb = new StringBuilder(1024);

            Line(sb, prefix, "machine", $"{SystemInfo.processorType} " +
                                        $"({SystemInfo.processorCount} cores @ {SystemInfo.processorFrequency} MHz)");
            Line(sb, prefix, "memory", $"{SystemInfo.systemMemorySize} MB");
            Line(sb, prefix, "os", SystemInfo.operatingSystem);
            Line(sb, prefix, "unity", Application.unityVersion);

            // The single most likely source of a cross-machine float difference
            // that survives FloatMode.Deterministic: a different number of
            // worker threads means different parallel batch boundaries, and a
            // different batch boundary can mean a different summation order.
            Line(sb, prefix, "jobs", $"workers={JobWorkerCount()} maxThreads={MaxJobThreads()}");
            Line(sb, prefix, "burst", BurstState());

            // A comma decimal separator once made every line of a dump a false
            // difference. Recorded so that is a five-second check, not an hour.
            var ci = CultureInfo.CurrentCulture;
            Line(sb, prefix, "culture", $"{ci.Name} decimal='{ci.NumberFormat.NumberDecimalSeparator}'");

            // NOT LockstepFixedStep.Active. This block is written from
            // LockstepLog.Begin, which runs inside StartSimulation() — and
            // LockstepBootstrap installs the fixed step AFTER that call, so
            // Active is always false here and the line always read "NOT
            // INSTALLED" even on a perfectly healthy match. Report the
            // intent; LockstepLog.NoteFixedStep records the outcome once it
            // is actually known.
            Line(sb, prefix, "fixedstep", GameSettings.DeterministicLockstep
                ? "requested (installed later in bootstrap — see the fixedstep line below)"
                : "OFF — the sim runs per-frame, not per-tick");

            Line(sb, prefix, "frame", $"targetFrameRate={Application.targetFrameRate} " +
                                      $"vSync={QualitySettings.vSyncCount}");

            return sb.ToString();
        }

        /// <summary>
        /// Who plays each faction, as THIS peer believes it.
        ///
        /// Worth its own block because <see cref="GameSettings.IsFactionHumanControlled"/>
        /// reads <c>FactionToPlayerMapping</c>, and that dictionary is built
        /// locally on each peer. If it disagrees, each machine spawns an AI
        /// brain for a different set of factions — two simulations playing
        /// different games while every command replicates perfectly. The
        /// 2026-08-21 logs showed each peer opening an AI log for the OTHER
        /// human's faction, which is exactly what that failure looks like from
        /// the outside.
        /// </summary>
        public static string DescribeFactionControl(string prefix = "")
        {
            var sb = new StringBuilder(512);
            Line(sb, prefix, "local faction", GameSettings.LocalPlayerFaction.ToString());
            Line(sb, prefix, "observer", GameSettings.IsObserver.ToString());

            for (int i = 0; i < 8; i++)
            {
                var f = (Faction)i;
                bool mapped = GameSettings.FactionToPlayerMapping.TryGetValue(f, out ulong clientId);
                bool human = GameSettings.IsFactionHumanControlled(f);
                bool local = GameSettings.IsFactionLocallyControlled(f);

                // Every faction, unconditionally. The first version skipped
                // any faction that was neither mapped nor human — which is
                // precisely the AI factions, the ones whose brains are the
                // asymmetry worth seeing. Eight fixed lines also means the two
                // peers' headers diff cleanly against each other.
                Line(sb, prefix, $"faction {f}",
                     $"human={human} local={local} mapped={mapped}" +
                     (mapped ? $" client={clientId}" : "") +
                     (human ? "" : "  <- AI, brain runs only where ShouldRunAIBrains()"));
            }
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string prefix, string key, string value)
            => sb.Append(prefix).Append(key.PadRight(14)).Append(' ').AppendLine(value);

        private static string Fmt(float v) => v.ToString("F6", CultureInfo.InvariantCulture);

        private static int JobWorkerCount()
        {
            try { return Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount; }
            catch { return -1; }
        }

        private static int MaxJobThreads()
        {
            try { return Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount; }
            catch { return -1; }
        }

        private static string BurstState()
        {
#if UNITY_EDITOR
            return $"editor enabled={Unity.Burst.BurstCompiler.IsEnabled}";
#else
            // In a player the AOT target is fixed at build time; what matters
            // at runtime is only whether Burst is actually running the jobs.
            return $"player enabled={Unity.Burst.BurstCompiler.IsEnabled}";
#endif
        }
    }
}
