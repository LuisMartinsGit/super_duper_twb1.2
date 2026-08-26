// MatchSettingsSync.cs
// The one place that decides which GameSettings shape the simulated world, and
// the only code allowed to put them on the wire.
//
// WHY THIS EXISTS
//   GameSettings is a bag of process-local statics. A host that had just played
//   a skirmish carried that skirmish's StartAge / StartCulture /
//   MaxStartingResources / PathfindingCellSize into the multiplayer match; the
//   client, whose statics still held menu defaults, built a DIFFERENT WORLD from
//   tick 0 and no amount of command replication could reconcile it. The old
//   TWB_START payload carried five fields and none of these were among them.
//
//   Anything that changes what the simulation computes belongs in Capture() and
//   Apply(). Cosmetic or per-peer settings (camera, audio, debug overlays) do
//   NOT — they are deliberately excluded so peers stay free to differ there.
//
// FORMAT
//   A single key=value blob, comma-separated, carried as ONE field of TWB_START.
//   Self-describing and order-independent, so adding a setting is a one-line
//   change here rather than a positional-parsing change on both sides. Unknown
//   keys are ignored (forward compatible); missing keys keep their default.
//
// docs/Multiplayer_LAN_Readiness.md

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheWaningBorder.Core.Multiplayer
{
    public static class MatchSettingsSync
    {
        /// <summary>
        /// Bumped whenever the lockstep protocol, the command-type table, or the
        /// meaning of any synced setting changes. Two peers with different
        /// protocol versions cannot agree on a simulation, so the lobby refuses
        /// the join rather than letting them desync ten minutes in.
        /// </summary>
        // 3 (2026-08-16): spends moved from the issue site into the lockstep
        // executors, wall placement gained opcodes 36/37 — a build from either
        // side of that migration computes different bank states from the same
        // command stream, so mixed pairs must refuse at the lobby door.
        // 4 (2026-08-16, desync #6): deterministic-mode checksums now mix raw
        // position bits (old and new builds compute different sums from the
        // same state), PING grew a 4th field (advertised input delay), and
        // Burst AOT is pinned to one CPU target — a mixed pair would pair an
        // SSE4-only sim against an AVX2 one, which is the very bug this fixes.
        public const int ProtocolVersion = 4;

        // ═══════════════════════════════════════════════════════════════
        // BUILD FINGERPRINT
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Identifies the simulation binary + its data. Two peers whose
        /// fingerprints differ are running different games — a host on 0.0.3 and
        /// a client on 0.0.2 would previously connect happily and diverge on the
        /// first cost lookup, which reads as a mysterious desync rather than the
        /// version mismatch it is.
        ///
        /// Deliberately cheap: the build version and the protocol version catch
        /// the case that actually happens (someone did not re-copy the exe).
        /// </summary>
        public static string Fingerprint
        {
            get
            {
                if (_fingerprint != null) return _fingerprint;
                unchecked
                {
                    uint h = 2166136261u;
                    void Mix(string s)
                    {
                        if (s == null) return;
                        for (int i = 0; i < s.Length; i++)
                        {
                            h ^= s[i];
                            h *= 16777619u;
                        }
                        h ^= 0x9E3779B9u;
                    }

                    Mix(UnityEngine.Application.version);
                    Mix(ProtocolVersion.ToString(CultureInfo.InvariantCulture));
                    // The command table is part of the contract: a peer that
                    // does not know a command type silently drops it.
                    Mix(((int)LockstepCommandType.SectAdopt).ToString(CultureInfo.InvariantCulture));

                    _fingerprint = h.ToString("X8", CultureInfo.InvariantCulture);
                }
                return _fingerprint;
            }
        }
        private static string _fingerprint;

        /// <summary>Human-readable build id for mismatch messages.</summary>
        public static string BuildLabel =>
            $"{UnityEngine.Application.version} (protocol {ProtocolVersion})";

        // ═══════════════════════════════════════════════════════════════
        // CAPTURE / APPLY
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Snapshot every world-shaping setting into a wire blob. Host side.
        /// </summary>
        public static string Capture()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(256);

            void Put(string key, string value)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(key).Append('=').Append(value);
            }

            Put("v", ProtocolVersion.ToString(c));
            Put("fp", Fingerprint);

            // ── World shape ────────────────────────────────────────────
            Put("seed", GameSettings.SpawnSeed.ToString(c));
            Put("map", GameSettings.SelectedMapScene ?? "");
            Put("half", GameSettings.MapHalfSize.ToString(c));
            Put("layout", ((int)GameSettings.SpawnLayout).ToString(c));
            Put("sides", ((int)GameSettings.TwoSides).ToString(c));
            Put("edgemin", GameSettings.SpawnEdgeBufferMin.ToString(c));
            Put("edgemax", GameSettings.SpawnEdgeBufferMax.ToString(c));
            Put("minsep", GameSettings.SpawnMinSeparation.ToString(c));

            // ── Rules ──────────────────────────────────────────────────
            Put("mode", ((int)GameSettings.Mode).ToString(c));
            Put("fog", GameSettings.FogOfWarEnabled ? "1" : "0");
            Put("border", GameSettings.BorderEnabled ? "1" : "0");
            Put("age", ((int)GameSettings.StartAge).ToString(c));
            Put("culture", GameSettings.StartCulture.ToString(c));
            Put("maxres", GameSettings.MaxStartingResources ? "1" : "0");

            // ── Simulation shape ───────────────────────────────────────
            // PathfindingCellSize decides the nav grid's resolution. Two peers
            // with different cell sizes have different worlds in the most total
            // sense available: every path, every footprint, every reachability
            // test.
            Put("cell", GameSettings.PathfindingCellSize.ToString("R", c));
            Put("det", GameSettings.DeterministicLockstep ? "1" : "0");
            Put("tickhz", LockstepTiming.TicksPerSecond.ToString(c));

            return sb.ToString();
        }

        /// <summary>
        /// Apply a host's blob to this peer's GameSettings. Client side.
        /// Returns false (with a player-facing reason) when the peers cannot
        /// possibly agree — the lobby should refuse the match rather than start
        /// one that is already broken.
        /// </summary>
        public static bool Apply(string blob, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(blob))
            {
                error = "The host sent no match settings — it is running an older build.";
                return false;
            }

            var c = CultureInfo.InvariantCulture;
            var kv = new Dictionary<string, string>(24);
            foreach (var pair in blob.Split(','))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                kv[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }

            // ── Refuse before touching anything ────────────────────────
            if (!kv.TryGetValue("v", out var vs) || !int.TryParse(vs, NumberStyles.Integer, c, out int v)
                || v != ProtocolVersion)
            {
                error = $"Version mismatch: the host speaks protocol {vs ?? "?"}, this build speaks " +
                        $"{ProtocolVersion}. Both players need the same build ({BuildLabel}).";
                return false;
            }

            if (!kv.TryGetValue("fp", out var fp) || fp != Fingerprint)
            {
                error = $"Build mismatch: the host's game is {fp ?? "unknown"}, yours is {Fingerprint}. " +
                        "Both players need the same build.";
                return false;
            }

            // ── Adopt the host's world ─────────────────────────────────
            int I(string key, int fallback)
                => kv.TryGetValue(key, out var s) && int.TryParse(s, NumberStyles.Integer, c, out int r)
                    ? r : fallback;
            bool B(string key, bool fallback)
                => kv.TryGetValue(key, out var s) ? s == "1" : fallback;
            float F(string key, float fallback)
                => kv.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, c, out float r)
                    ? r : fallback;

            GameSettings.SpawnSeed = I("seed", GameSettings.SpawnSeed);
            if (kv.TryGetValue("map", out var map) && !string.IsNullOrEmpty(map))
                GameSettings.SelectedMapScene = map;
            GameSettings.MapHalfSize = I("half", GameSettings.MapHalfSize);
            GameSettings.SpawnLayout = (SpawnLayout)I("layout", (int)GameSettings.SpawnLayout);
            GameSettings.TwoSides = (TwoSidesPreset)I("sides", (int)GameSettings.TwoSides);
            GameSettings.SpawnEdgeBufferMin = I("edgemin", GameSettings.SpawnEdgeBufferMin);
            GameSettings.SpawnEdgeBufferMax = I("edgemax", GameSettings.SpawnEdgeBufferMax);
            GameSettings.SpawnMinSeparation = I("minsep", GameSettings.SpawnMinSeparation);

            GameSettings.Mode = (GameMode)I("mode", (int)GameSettings.Mode);
            GameSettings.FogOfWarEnabled = B("fog", GameSettings.FogOfWarEnabled);
            GameSettings.BorderEnabled = B("border", GameSettings.BorderEnabled);
            GameSettings.StartAge = (SkirmishStartAge)I("age", (int)GameSettings.StartAge);
            GameSettings.StartCulture = (byte)I("culture", GameSettings.StartCulture);
            GameSettings.MaxStartingResources = B("maxres", GameSettings.MaxStartingResources);

            GameSettings.PathfindingCellSize = F("cell", GameSettings.PathfindingCellSize);
            GameSettings.DeterministicLockstep = B("det", true);
            LockstepTiming.TicksPerSecond = I("tickhz", LockstepTiming.TicksPerSecond);

            return true;
        }
    }
}
