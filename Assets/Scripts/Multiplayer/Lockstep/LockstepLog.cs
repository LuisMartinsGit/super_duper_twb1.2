// LockstepLog.cs
// The multiplayer test instrument: one line per tick, written by every peer,
// designed so that TWO PEERS IN SYNC PRODUCE IDENTICAL FILES.
//
// HOW TO USE IT
//   Run two instances (ParrelSync clone, Unity's Multiplayer Play Mode virtual
//   player, or two copies of the exe), play a match, then diff the two
//   Lockstep.log files:
//
//       fc  logs\..._host\Lockstep.log  logs\..._client1\Lockstep.log
//       diff logs/..._host/Lockstep.log logs/..._client1/Lockstep.log
//
//   The FIRST differing line is the fork. Everything after it is consequence,
//   not cause. That one number — the tick — is what turns "it desynced" into a
//   bug you can reproduce and bisect.
//
// WHY IT CAN BE DIFFED
//   Every line is derived from state the two peers are supposed to agree on and
//   NOTHING ELSE. No wall-clock times, no frame counts, no local latency, no
//   peer addresses, no "I sent" versus "I received" — all of those legitimately
//   differ between two machines that are perfectly in sync, and any one of them
//   in the file would drown the real signal.
//
//   The per-tick line carries the commands the tick EXECUTED, in execution
//   order, after the sort — i.e. exactly the input the simulation consumed. The
//   checksum lines carry the state that came out. Between them, a divergence is
//   attributable: commands differ first = a replication bug; checksums differ
//   first with identical commands = a determinism bug in the simulation.
//
// Everything that is genuinely per-peer (latency, stalls, who we are waiting
// for, socket errors) goes to Console.log instead, where it belongs.
//
// Cheap by design: one string per tick, buffered, flushed every half second and
// on anything that matters. A 30-minute match at 30 Hz is ~54k lines of mostly
// "tick=N cmds=0", which compresses to nothing and costs nothing to skim.
//
// docs/Multiplayer_LAN_Readiness.md
// Location: Assets/Scripts/Multiplayer/Lockstep/LockstepLog.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Multiplayer
{
    public static class LockstepLog
    {
        private const string FileName = "Lockstep.log";

        /// <summary>
        /// Ticks with no commands are the overwhelming majority and say nothing
        /// on their own — but dropping them entirely would let two peers'
        /// files line up while their TICK COUNTS differed, which is itself a
        /// desync. Compromise: log an empty tick only every this many, so the
        /// files stay aligned on a coarse grid without the bulk.
        /// </summary>
        private const int EmptyTickStride = 30;

        private static StreamWriter _writer;
        private static bool _opened;
        private static float _nextFlush;
        private static readonly StringBuilder _line = new StringBuilder(256);

        /// <summary>Open the log in the current match folder. Idempotent.</summary>
        public static void Begin(int localPlayerIndex, bool isHost)
        {
            Close();

            try
            {
                string path = TheWaningBorder.Core.Diagnostics.MatchLogSession.File(FileName);
                _writer = new StreamWriter(path, append: false);
                _opened = true;
            }
            catch
            {
                _writer = null;
                _opened = false;
                return;
            }

            // The header is the ONE part that differs between peers on purpose:
            // it says who wrote the file. Everything below it must match.
            WriteRaw($"# Lockstep log — player {localPlayerIndex} ({(isHost ? "host" : "client")})");
            WriteRaw($"# build      {MatchSettingsSync.BuildLabel}  fingerprint {MatchSettingsSync.Fingerprint}");
            WriteRaw($"# map        {GameSettings.SelectedMapScene}  seed {GameSettings.SpawnSeed}");
            WriteRaw($"# rules      age={GameSettings.StartAge} culture={GameSettings.StartCulture} " +
                     $"maxres={GameSettings.MaxStartingResources} fog={GameSettings.FogOfWarEnabled} " +
                     $"curse={GameSettings.BorderEnabled}");
            WriteRaw($"# sim        {LockstepTiming.TicksPerSecond} Hz  cell={GameSettings.PathfindingCellSize}  " +
                     $"deterministic={GameSettings.DeterministicLockstep}");
            WriteRaw("#");

            // The machine, and who plays whom. Both are SUPPOSED to differ
            // between peers, which is why they live up here in the header and
            // never in the diffable body -- and both are the first thing an
            // investigation asks for once the command streams are proven
            // identical. See LockstepEnvironment for why each field is here.
            WriteRaw("# ---- this machine ----");
            WriteRawBlock(LockstepEnvironment.Describe("# "));
            WriteRaw("# ---- faction control (as THIS peer sees it) ----");
            WriteRawBlock(LockstepEnvironment.DescribeFactionControl("# "));

            WriteRaw("#");
            WriteRaw("# Everything below this line must be IDENTICAL on both peers.");
            WriteRaw("# Diff two of these files; the first differing line is the fork.");
            WriteRaw("#   cmds differ first      -> a command did not replicate");
            WriteRaw("#   checksums differ first -> the simulation is not deterministic");
            WriteRaw("#");
            WriteRaw("# The sum line breaks the checksum down, so the first differing line");
            WriteRaw("# already says WHAT forked and WHOSE it is:");
            WriteRaw("#   pos rot        transforms");
            WriteRaw("#   nav            destination / flow / steering / stuck / speed");
            WriteRaw("#   cbt wrk        combat target+cooldown / construction+training+mining");
            WriteRaw("#   hp bank        health / faction resources");
            WriteRaw("#   tech           research state + QUEUES, sect adoption");
            WriteRaw("#   rng            seeded RNG stream states — the quietest fork there is");
            WriteRaw("#   veil cost      the veil grid and the nav cost field under everything");
            WriteRaw("#   f0..f7         per-faction roll-up — names the guilty side");
            WriteRaw("#");
            WriteRaw("# nav differing while pos still matches = they were told different things.");
            WriteRaw("# pos differing while nav matches       = same orders, different arithmetic.");
            WriteRaw("#");
            Flush();
        }

        public static void Close()
        {
            if (_writer == null) { _opened = false; return; }
            try { _writer.Flush(); _writer.Dispose(); } catch { }
            _writer = null;
            _opened = false;
        }

        // ═══════════════════════════════════════════════════════════════
        // PER-TICK
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// One line for the tick about to execute, listing its commands in the
        /// order the simulation will consume them.
        /// </summary>
        public static void Tick(int tick, List<LockstepCommand> executed)
        {
            if (!_opened) return;

            int count = executed?.Count ?? 0;
            if (count == 0 && (tick % EmptyTickStride) != 0) return;

            _line.Clear();
            _line.Append("tick=").Append(tick.ToString("D6", CultureInfo.InvariantCulture))
                 .Append(" cmds=").Append(count);

            for (int i = 0; i < count; i++)
            {
                var c = executed[i];
                _line.Append(" | p").Append(c.PlayerIndex)
                     .Append('#').Append(c.CommandIndex)
                     .Append(' ').Append(c.Type)
                     .Append(" e=").Append(c.EntityNetworkId);

                if (c.TargetEntityId != 0) _line.Append(" t=").Append(c.TargetEntityId);
                if (c.SecondaryTargetId != 0) _line.Append(" s=").Append(c.SecondaryTargetId);
                if (!string.IsNullOrEmpty(c.BuildingId)) _line.Append(" id=").Append(c.BuildingId);

                // Positions to a millimetre. The raw float is what actually
                // crossed the wire, but printing all 9 digits makes every line
                // unreadable and adds nothing: two peers that disagree about a
                // command position disagree by metres, not by ulps.
                if (!c.TargetPosition.Equals(default(Unity.Mathematics.float3)))
                {
                    _line.Append(" @(")
                         .Append(c.TargetPosition.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                         .Append(c.TargetPosition.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                         .Append(c.TargetPosition.z.ToString("F3", CultureInfo.InvariantCulture)).Append(')');
                }
            }

            WriteRaw(_line.ToString());
            if (count > 0) Flush();   // commands are the interesting part; don't lose them to a crash
        }

        /// <summary>The state the tick produced.</summary>
        public static void Checksum(int tick, uint checksum, int entityCount)
        {
            if (!_opened) return;
            WriteRaw($"sum  tick={tick.ToString("D6", CultureInfo.InvariantCulture)} " +
                     $"= 0x{checksum:X8}  entities={entityCount}");
            Flush();
        }

        /// <summary>
        /// The state the tick produced, broken down by subsystem and faction.
        ///
        /// The aggregate is printed first and in the same position as the old
        /// one-number form, so older logs and newer logs still line up by eye.
        /// Everything after it narrows the search: with three matched log pairs
        /// in the 2026-08-21 investigation the fork tick was known within
        /// minutes and the guilty SUBSYSTEM was still a guess a day later.
        /// </summary>
        public static void Checksum(int tick, in SimStateHash h)
        {
            if (!_opened) return;

            _line.Clear();
            _line.Append("sum  tick=").Append(tick.ToString("D6", CultureInfo.InvariantCulture))
                 .Append(" = 0x").Append(h.Total.ToString("X8", CultureInfo.InvariantCulture))
                 .Append("  entities=").Append(h.Entities.ToString(CultureInfo.InvariantCulture))
                 .Append("  pos=").Append(Hex(h.Pos))
                 .Append(" rot=").Append(Hex(h.Rot))
                 .Append(" hp=").Append(Hex(h.Health))
                 .Append(" nav=").Append(Hex(h.Nav))
                 .Append(" cbt=").Append(Hex(h.Combat))
                 .Append(" wrk=").Append(Hex(h.Work))
                 .Append(" bank=").Append(Hex(h.Bank))
                 .Append(" tech=").Append(Hex(h.Tech))
                 .Append(" rng=").Append(Hex(h.Rng))
                 .Append(" veil=").Append(Hex(h.Veil))
                 .Append(" cost=").Append(Hex(h.Cost));

            // Only factions that actually have entities. A faction wiped out
            // stops appearing on BOTH peers at the same tick if they agree --
            // and if they do not, that line differing IS the finding.
            const uint EmptyFaction = 2166136261u;
            for (int f = 0; f < 8; f++)
            {
                uint fh = h.FactionAt(f);
                if (fh == EmptyFaction) continue;
                _line.Append(" f").Append(f.ToString(CultureInfo.InvariantCulture))
                     .Append('=').Append(Hex(fh));
            }

            WriteRaw(_line.ToString());
            Flush();
        }

        private static string Hex(uint v) => "0x" + v.ToString("X8", CultureInfo.InvariantCulture);

        // ═══════════════════════════════════════════════════════════════
        // MILESTONES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// A match-shaping event both peers should reach at the same tick:
        /// world ready, simulation released, desync. Written with the tick so
        /// it lines up in a diff.
        ///
        /// Do NOT use this for anything per-peer (latency, stalls, who we are
        /// waiting for) — that belongs in Console.log, and putting it here
        /// would make two healthy peers' files differ.
        /// </summary>
        public static void Event(int tick, string what)
        {
            if (!_opened) return;
            WriteRaw($"evt  tick={tick.ToString("D6", CultureInfo.InvariantCulture)} {what}");
            Flush();
        }

        // ═══════════════════════════════════════════════════════════════

        private static void WriteRaw(string line)
        {
            if (_writer == null) return;
            try { _writer.WriteLine(line); } catch { _opened = false; }
        }

        /// <summary>Write an already-prefixed multi-line block, one line at a
        /// time, so the writer's own newline convention is used throughout.</summary>
        private static void WriteRawBlock(string block)
        {
            if (_writer == null || string.IsNullOrEmpty(block)) return;
            foreach (var line in block.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                if (trimmed.Length > 0) WriteRaw(trimmed);
            }
        }

        private static void Flush()
        {
            if (_writer == null) return;
            try { _writer.Flush(); } catch { }
        }

        /// <summary>Periodic flush so a hard crash loses at most half a second.</summary>
        public static void Pump(float realtimeNow)
        {
            if (!_opened || realtimeNow < _nextFlush) return;
            _nextFlush = realtimeNow + 0.5f;
            Flush();
        }
    }
}
