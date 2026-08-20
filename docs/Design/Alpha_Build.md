# The alpha test build

**Status:** canonical for what a shipped alpha build shows and records.

The alpha goes to a handful of friends who are not developers. Two things
follow from that: they must only be able to reach things that work, and when
something breaks they must be able to hand back everything needed to diagnose
it without being walked through it.

---

## 1. What the menu shows

Hidden **in the built player only** — the editor keeps the full menu, so
development is unaffected:

| Entry | Why it is hidden |
|---|---|
| Campaign | not implemented |
| Scenarios | a dev harness; its scenes are not in the build anyway |
| Load Game | there is no save system, so the button does nothing |

Left in: **Skirmish**, **Multiplayer**, **Tutorial**, **Settings**, **Quit**.

### Multiplayer (unhidden 2026-08-16)

LAN play shipped after the lockstep pair survived a two-editor match
following the determinism sweep
([Multiplayer_Desync_Sweep_2026-08-16.md](../Multiplayer_Desync_Sweep_2026-08-16.md)).
What testers should know:

- **Same build on both machines.** The lobby refuses a mismatched pair at
  the door with a message naming both versions — that message is correct,
  re-copy the build.
- **Same network.** Discovery is LAN broadcast + probe; games appear in the
  browse list within a couple of seconds. Nothing appearing after ~10 s
  means a network split or a firewall — the browse pane says so and offers
  direct IP as the fallback. Internet play is possible only via direct IP
  with the host's ports forwarded, and is not advertised to testers.
- **Windows Firewall asks once.** The first host or join triggers the
  firewall prompt; it must be ALLOWED (private networks) on both machines,
  or neither discovery nor joining works.
- **A desync ends the diagnosis, not the report.** Both machines write
  `Lockstep.log` and, on a desync, `Desync_tickN_pP.log` into the match's
  log folder. Both players' log folders are needed — the diff between the
  two files is the diagnosis, one side alone is not enough.

The list lives in `ShipGateMenuTrim.AlphaHiddenMenuItems`, which already
existed to hide Scenarios when its scenes are excluded from the build. Removing
an entry from that array puts the menu item back.

### Saved games

There is nothing to disable. No save system exists in the codebase — no
`SaveSystem`, no autosave, no save entry in the pause menu. "Load Game" was a
dead button, and hiding it is the entire change.

---

## 2. What every match records

Logs are the whole point of this build. They go in a **`logs` folder beside the
executable**, not in Unity's `%USERPROFILE%\AppData\LocalLow\…` location, so a
tester can find them without being told where to look. The folder ships with
the build and is recreated at runtime if it is missing.

Each match writes its own timestamped folder, e.g.
`2026-08-13_21-04-11_SunderedCrown/`:

| File | Contents |
|---|---|
| `Summary.txt` | outcome, duration, and counts of exceptions / errors / warnings |
| `Console.log` | every warning, error and exception, with stack traces |
| `Perf.log` | frame hitches and their causes |
| `AI_<Faction>.log` | each AI's decisions |
| `Player_<Faction>.log` | the human player's economy over time |
| `Timeline.csv` | every faction's economy sampled through the match, plus elimination times — opens in a spreadsheet |

The match header and `Summary.txt` carry the **spawn seed**, map, mode, player
count and settings. The seed is what makes a tester's report reproducible, so
it is recorded rather than left in memory.

**Nothing is deleted.** This is the load-bearing change: `AILogger.Initialize`
used to delete every previous `AI_*.log` and `Player_*.log` at the start of each
match. For a developer running one match that is tidy; for a tester it means
only the last match survives, and a crash-then-relaunch destroys exactly the
logs that would have explained the crash. Old match folders are pruned only
past a generous cap (30).

**Console capture is installed before the first scene loads**, so a failure in
the menu or during loading is recorded too — not just in-match ones. It writes
through on every line rather than buffering, because the messages that matter
most are the ones immediately before a hard crash.

Nothing is uploaded. The files stay on the tester's machine until they choose
to send them, and the shipped `README - please read.txt` in the folder says so.

---

## 3. Reversing it

- Menu: delete entries from `ShipGateMenuTrim.AlphaHiddenMenuItems`.
- Logging: it is not alpha-specific and should stay. Per-match folders and
  console capture are worth having permanently; only the tester-facing README
  is alpha framing.
