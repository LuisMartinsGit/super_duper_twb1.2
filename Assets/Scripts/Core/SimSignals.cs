// SimSignals.cs
// The one-way channel from the simulation to the presentation layer.
// Location: Assets/Scripts/Core/SimSignals.cs
//
// WHY THIS EXISTS
// Simulation code wants to tell the player things: a curse pool has
// quickened, the Shardroot has surfaced, a ritual backfired. Before this, it
// said so by calling straight into the UI --
// TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(...) and
// TheWaningBorder.UI.GameUI.MinimapPings.Post(...) -- from inside systems that
// run on the lockstep tick.
//
// That is the one dependency a deterministic simulation must not have. It
// points the wrong way (the sim reaching up into presentation), it makes an
// assembly boundary between the two impossible to draw, and it puts per-machine
// state -- what is on screen, what the local player can see -- within reach of
// code whose output has to be identical on every peer.
//
// So the simulation ENQUEUES here and never looks back. Presentation drains the
// queue each frame and decides what to show. The two sides no longer share a
// type, and the compiler can enforce that once the assemblies split.
//
// DETERMINISM. Enqueuing must never change the simulation, so nothing here
// feeds anything back: no return values a system could branch on, no queue
// depth it can read, and a full queue drops the oldest rather than blocking.
// Two peers that disagree about what is on screen still agree about the match.

using System.Collections.Generic;
using Unity.Mathematics;

namespace TheWaningBorder.Core
{
    /// <summary>What a ping means; the presentation layer picks the colour.</summary>
    public enum SimPingKind : byte
    {
        Generic = 0,
        Curse = 1,
        Combat = 2,
        Discovery = 3,
    }

    /// <summary>A message for the player, already localised by the caller.</summary>
    public readonly struct SimNotice
    {
        public readonly string Text;

        /// <summary>Error-flavoured: the HUD renders these differently (it has
        /// always had a separate NotifyError path). Carried through the seam
        /// so routing a message no longer loses which kind it was.</summary>
        public readonly bool IsError;

        public SimNotice(string text, bool isError)
        {
            Text = text;
            IsError = isError;
        }
    }

    /// <summary>A point on the map worth the player's attention.</summary>
    public readonly struct SimPing
    {
        public readonly float3 Position;
        public readonly SimPingKind Kind;
        public readonly float Seconds;
        public readonly bool Big;

        public SimPing(float3 position, SimPingKind kind, float seconds, bool big)
        {
            Position = position;
            Kind = kind;
            Seconds = seconds;
            Big = big;
        }
    }

    /// <summary>How a match ended, for the end-of-match screen.</summary>
    public readonly struct SimMatchEnd
    {
        public readonly string Title;
        public readonly string Subtitle;
        public readonly bool LocalWon;

        public SimMatchEnd(string title, string subtitle, bool localWon)
        {
            Title = title;
            Subtitle = subtitle;
            LocalWon = localWon;
        }
    }

    /// <summary>
    /// Simulation-to-presentation signals. Write from the sim, drain from the
    /// presentation layer, never the other way round.
    /// </summary>
    public static class SimSignals
    {
        /// <summary>
        /// Hard cap. A match that somehow floods the queue drops the OLDEST
        /// entries: a stale notice is worth less than a fresh one, and an
        /// unbounded queue in a system that never drains (a headless host, a
        /// match that ends mid-frame) is a slow leak.
        /// </summary>
        private const int MaxQueued = 256;

        private static readonly Queue<SimNotice> _notices = new Queue<SimNotice>();
        private static readonly Queue<SimPing> _pings = new Queue<SimPing>();
        private static readonly Queue<SimMatchEnd> _matchEnd = new Queue<SimMatchEnd>();

        /// <summary>Tell the player something. Text is already localised.</summary>
        public static void Notify(string text) => Post(text, isError: false);

        /// <summary>Something the player asked for could not happen.</summary>
        public static void NotifyError(string text) => Post(text, isError: true);

        private static void Post(string text, bool isError)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_notices.Count >= MaxQueued) _notices.Dequeue();
            _notices.Enqueue(new SimNotice(text, isError));
        }

        /// <summary>Mark a spot on the minimap.</summary>
        public static void Ping(float3 position, SimPingKind kind,
                                float seconds = 4f, bool big = false)
        {
            if (_pings.Count >= MaxQueued) _pings.Dequeue();
            _pings.Enqueue(new SimPing(position, kind, seconds, big));
        }

        /// <summary>
        /// The match is over. Queued rather than shown directly, because
        /// deciding WHETHER a screen appears -- and falling back to a toast
        /// when the HUD is not up -- is a presentation question. The
        /// simulation used to branch on VictoryPanel.TryShow's return value,
        /// which meant a system's control flow depended on what was on screen.
        /// </summary>
        public static void MatchEnded(string title, string subtitle, bool localWon)
        {
            if (_matchEnd.Count > 0) return;   // first result wins; a match ends once
            _matchEnd.Enqueue(new SimMatchEnd(title, subtitle, localWon));
        }

        /// <summary>Presentation only: see TryDrainNotice.</summary>
        public static bool TryDrainMatchEnd(out SimMatchEnd end)
        {
            if (_matchEnd.Count == 0) { end = default; return false; }
            end = _matchEnd.Dequeue();
            return true;
        }

        /// <summary>Presentation only: take everything queued since last frame.
        /// Returns false when there is nothing, so the caller can skip.</summary>
        public static bool TryDrainNotice(out SimNotice notice)
        {
            if (_notices.Count == 0) { notice = default; return false; }
            notice = _notices.Dequeue();
            return true;
        }

        /// <summary>Presentation only: see TryDrainNotice.</summary>
        public static bool TryDrainPing(out SimPing ping)
        {
            if (_pings.Count == 0) { ping = default; return false; }
            ping = _pings.Dequeue();
            return true;
        }

        /// <summary>Drop anything queued. Called when a match ends so the next
        /// one does not open with the previous match's notices.</summary>
        public static void Clear()
        {
            _notices.Clear();
            _pings.Clear();
            _matchEnd.Clear();
        }
    }
}
