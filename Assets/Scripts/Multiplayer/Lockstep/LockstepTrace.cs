// LockstepTrace.cs
// A rolling per-entity, per-tick record of the simulation, dumped when a
// desync fires.
// Location: Assets/Scripts/Multiplayer/Lockstep/LockstepTrace.cs
//
// THE PROBLEM THIS SOLVES
// Checksums are exchanged every SYNC_CHECK_INTERVAL ticks, so a desync is
// always DETECTED later than it HAPPENS. The three 2026-08-21 matches forked
// at ticks 369, 757 and 1721 and were detected at 390, 780 and 1740 -- and the
// only per-entity evidence written was a single snapshot taken at detection
// time, twenty ticks of consequence after the cause.
//
// So the dump showed units 0.9 m apart and could not say whether they had been
// given different destinations, had different flow directions, or had simply
// integrated differently. Three matched log pairs and that question was still
// open.
//
// This keeps the last TraceTicks ticks of FULL per-entity state in memory and
// writes them all out when a desync fires. The fork tick is inside that window
// by construction, and so are the ticks before it -- which is where the cause
// is, since the fork tick itself already shows the effect.
//
// WHY RAW FLOAT BITS
// The deterministic-mode checksum hashes math.asuint(position), so it fires on
// a ONE-ULP difference. A trace printed to three decimals would show two
// identical lines for exactly the divergence that tripped the alarm. Every
// float here is its exact bit pattern in hex; position also carries a decimal
// rendering because it is the field a human actually reads.
//
// WHY EVERY ENTITY EVERY TICK
// Emitting only changed rows would shrink the file a lot, and would also mean
// the two peers stop emitting the same ROWS the moment they diverge -- which
// is precisely when the files need to still line up. Alignment beats size: a
// thirty-second match is a few hundred thousand lines of near-identical text
// and compresses to almost nothing in the log zip.

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheWaningBorder.Multiplayer
{
    public static class LockstepTrace
    {
        /// <summary>
        /// How many ticks of history to keep. Must comfortably exceed the sync
        /// interval, since that is the worst-case gap between a fork and its
        /// detection -- 4 s at 30 Hz against a 1 s sync interval leaves room
        /// for a fork that only trips the check on the second comparison.
        /// </summary>
        public const int TraceTicks = 120;

        private sealed class Frame
        {
            public int Tick = -1;
            public SimStateHash Hash;
            public EntitySnapshot[] Entities = new EntitySnapshot[512];
            public int Count;
        }

        private static Frame[] _ring;
        private static int _next;      // next slot to overwrite
        private static bool _enabled;

        /// <summary>Reusable capture buffer, so the per-tick path does not
        /// allocate a list per tick.</summary>
        private static readonly List<EntitySnapshot> _scratch = new List<EntitySnapshot>(1024);

        /// <summary>The buffer the manager fills each tick. Never null while
        /// tracing is on; null when it is off, which is the signal to
        /// LockstepStateHash to skip snapshot capture entirely.</summary>
        public static List<EntitySnapshot> CaptureBuffer => _enabled ? _scratch : null;

        public static bool Enabled => _enabled;

        public static void Begin()
        {
            // Only in deterministic lockstep. Outside it, sub-millimetre drift
            // is expected and tolerated, so a bit-exact trace would be a large
            // file full of differences that mean nothing.
            _enabled = GameSettings.DeterministicLockstep;
            if (!_enabled) { _ring = null; return; }

            _ring = new Frame[TraceTicks];
            for (int i = 0; i < TraceTicks; i++) _ring[i] = new Frame();
            _next = 0;
        }

        public static void Close()
        {
            _ring = null;
            _enabled = false;
            _scratch.Clear();
        }

        /// <summary>Store this tick's snapshot, overwriting the oldest.</summary>
        public static void Record(int tick, SimStateHash hash, List<EntitySnapshot> snapshots)
        {
            if (!_enabled || _ring == null || snapshots == null) return;

            var frame = _ring[_next];
            _next = (_next + 1) % TraceTicks;

            frame.Tick = tick;
            frame.Hash = hash;
            frame.Count = snapshots.Count;

            if (frame.Entities.Length < snapshots.Count)
                frame.Entities = new EntitySnapshot[snapshots.Count * 2];

            for (int i = 0; i < snapshots.Count; i++) frame.Entities[i] = snapshots[i];
        }

        /// <summary>
        /// Write every retained tick, oldest first. Returns the number of ticks
        /// written, or -1 if tracing was off.
        /// </summary>
        public static int Flush(string path, int desyncTick, int localPlayerIndex, bool isHost)
        {
            if (!_enabled || _ring == null) return -1;

            var sb = new StringBuilder(4 * 1024 * 1024);

            sb.AppendLine($"=== DESYNC TRACE — detected at tick {desyncTick} ===");
            sb.AppendLine($"player {localPlayerIndex} ({(isHost ? "host" : "client")})");
            sb.AppendLine();
            sb.AppendLine("The last " + TraceTicks + " ticks of per-entity state, oldest first.");
            sb.AppendLine("Diff this against the other peer's copy. The first differing line is");
            sb.AppendLine("the fork, and unlike the checksum it names the entity AND the field.");
            sb.AppendLine();
            sb.AppendLine("Floats are raw IEEE-754 bits in hex, because the checksum fires on a");
            sb.AppendLine("one-ULP difference that no decimal rendering would show.");
            sb.AppendLine();
            sb.AppendLine("  sum   per-tick checksum, broken down by subsystem and faction");
            sb.AppendLine("  e     one entity: id, faction, hp, position, rotation,");
            sb.AppendLine("        destination / flow / steering / smoothed direction,");
            sb.AppendLine("        speed, stuck counters, combat target, work progress");
            sb.AppendLine();
            sb.AppendLine("  dest/flow/steer are 'has:xbits,zbits' — has=0 means the unit has no");
            sb.AppendLine("  such input this tick, which is itself often the divergence.");
            sb.AppendLine();

            int written = 0;

            // Oldest first: _next points at the slot due to be overwritten,
            // which is the oldest one still holding data.
            for (int k = 0; k < TraceTicks; k++)
            {
                var frame = _ring[(_next + k) % TraceTicks];
                if (frame.Tick < 0) continue;   // ring not full yet

                AppendFrame(sb, frame);
                written++;
            }

            try
            {
                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch
            {
                return -1;
            }
            return written;
        }

        private static void AppendFrame(StringBuilder sb, Frame frame)
        {
            var h = frame.Hash;
            sb.Append("sum   tick=").Append(D6(frame.Tick))
              .Append(" total=").Append(Hex(h.Total))
              .Append(" ents=").Append(frame.Count.ToString(CultureInfo.InvariantCulture))
              .Append(" pos=").Append(Hex(h.Pos))
              .Append(" rot=").Append(Hex(h.Rot))
              .Append(" hp=").Append(Hex(h.Health))
              .Append(" nav=").Append(Hex(h.Nav))
              .Append(" cbt=").Append(Hex(h.Combat))
              .Append(" wrk=").Append(Hex(h.Work))
              .Append(" bank=").Append(Hex(h.Bank))
              .Append(" tech=").Append(Hex(h.Tech))
              .Append(" rng=").Append(Hex(h.Rng))
              .Append(" veil=").Append(Hex(h.Veil))
              .Append(" cost=").Append(Hex(h.Cost))
              .AppendLine();

            for (int i = 0; i < frame.Count; i++)
                AppendEntityLine(sb, frame.Tick, ref frame.Entities[i]);
        }

        /// <summary>
        /// One entity's full row. Public so the desync dump emits the SAME
        /// format as the trace — the two files are meant to be read together,
        /// and a row that reads differently in each is a row you have to
        /// translate by hand at exactly the wrong moment.
        /// </summary>
        public static void AppendEntityLine(StringBuilder sb, int tick, ref EntitySnapshot s)
        {
            {
                sb.Append("e     tick=").Append(D6(tick))
                  .Append(" id=").Append(D5(s.Id))
                  .Append(" fac=").Append(s.Faction == 255 ? "-" : s.Faction.ToString(CultureInfo.InvariantCulture))
                  .Append(" hp=").Append(s.Hp.ToString(CultureInfo.InvariantCulture))
                  .Append('/').Append(s.HpMax.ToString(CultureInfo.InvariantCulture))
                  .Append(" pos=").Append(Hex(s.Px)).Append(',').Append(Hex(s.Py)).Append(',').Append(Hex(s.Pz))
                  .Append(" (").Append(F3(s.Px)).Append(',').Append(F3(s.Py)).Append(',').Append(F3(s.Pz)).Append(')')
                  .Append(" rot=").Append(Hex(s.Rx)).Append(',').Append(Hex(s.Ry)).Append(',')
                                  .Append(Hex(s.Rz)).Append(',').Append(Hex(s.Rw))
                  .Append(" dest=").Append(s.HasDest).Append(':').Append(Hex(s.Dx)).Append(',').Append(Hex(s.Dz))
                  .Append(" flow=").Append(s.HasFlow).Append(':').Append(Hex(s.Fx)).Append(',').Append(Hex(s.Fz))
                  .Append(" steer=").Append(s.HasSteer).Append(':').Append(Hex(s.Sx)).Append(',').Append(Hex(s.Sz))
                  .Append(" sm=").Append(Hex(s.SmX)).Append(',').Append(Hex(s.SmZ))
                  .Append(" spd=").Append(Hex(s.Speed))
                  .Append(" stuck=").Append(s.Stuck).Append('/').Append(s.StuckAttempt)
                  .Append(" tgt=").Append(s.TargetId.ToString(CultureInfo.InvariantCulture))
                  .Append(" atk=").Append(Hex(s.AtkTimer))
                  .Append(" work=").Append(s.WorkKind)
                  .Append(':').Append(Hex(s.WorkA)).Append(',').Append(Hex(s.WorkB))
                  .Append('@').Append(s.WorkTarget.ToString(CultureInfo.InvariantCulture))
                  .AppendLine();
            }
        }

        // Invariant culture throughout: desync #6's dumps came off a
        // Portuguese Windows with decimal COMMAS, diffed against a dot-locale
        // peer, and every single line was a false difference.
        private static string Hex(uint v) => v.ToString("X8", CultureInfo.InvariantCulture);
        private static string D6(int v) => v.ToString("D6", CultureInfo.InvariantCulture);
        private static string D5(int v) => v.ToString("D5", CultureInfo.InvariantCulture);

        private static string F3(uint bits)
            => Unity.Mathematics.math.asfloat(bits).ToString("F3", CultureInfo.InvariantCulture);
    }
}
