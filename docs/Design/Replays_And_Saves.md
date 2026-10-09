# Replays and saved games

*2026-10-08.* Built from [Save_And_Replay_Plan.md](../Save_And_Replay_Plan.md)
(steps R1–R3, R5–R7 and saved-game design A, S1–S2 and S6). This page is the
rule; that one is the history of how it was chosen.

## 1. Single-player runs through lockstep (solo lockstep)

Every single-player **skirmish** now runs on the same driver as multiplayer:
a `LockstepManager` with no sockets and no peers. Every order — the player's
and the AI's — becomes a `LockstepCommand` stamped **one tick ahead**, and the
world steps at the fixed tick rate, exactly once per tick.

- **What the player feels:** what multiplayer already feels like — a fixed
  30 Hz simulation and one tick (≈33 ms) of input delay. Game speed and the
  pause menu work as before (the tick clock runs on `Time.deltaTime`).
- **What it buys:** a single-player match is now a reproducible stream of
  commands. That is what replays and saves are made of, and it removes the
  "works in single-player, desyncs in multiplayer" class of bug: there is no
  longer a separate single-player path.
- **Not in solo lockstep:** scenarios, the unit sandbox and the tutorial.
  Their tools write the world directly, so a recording of them could never
  reproduce the match; they stay frame-driven, record nothing and cannot be
  saved. `-twbNoSoloLockstep` turns solo lockstep off for diagnosis.

`GameSettings.UsesLockstep` is the test for "must this order travel as a
command?". `IsMultiplayer` is only for genuinely network things.

## 2. Replays

**Every lockstep match records its replay.** A replay is the match's set-up
plus every command it executed, tick by tick, plus the world's state hash once
a second. The AI's commands are in it too: the AI runs outside the simulation
and is not deterministic, so it does not think during playback — its recorded
decisions are replayed instead. The curse needs nothing; it is in the
simulation and seeded.

- **Where:** `Replays/` beside the executable (falls back to the user data
  folder if that is read-only). The newest replays are kept and older ones
  deleted; how many is `replaysKept` on `ReplayRecorder.asset`. Headless batch
  runs write `Replay.twbr` into the match's log folder instead.
- **Watching:** Load Game → **Replays** → Watch, or **Watch Replay** on the
  end-of-match screen. The view is a spectator's — no command panels, full
  vision, or the selected faction's vision — and no order can be given.
- **Playback bar:** clock, a progress bar (click to jump), pause (Space),
  speeds and a skip-ahead button (both on `ReplayControlsPanel.asset`), and
  restart. Jumping **forward** fast-forwards; jumping **back** restarts the map
  and fast-forwards to the moment, because the simulation only runs forward.
- **Self-verifying:** playback compares its own state hash to the recorded
  one every second. The bar says *In sync* or *Out of sync from m:ss* — the
  first second the replay stopped being the match that was played (something
  changed the world outside the command stream).
- **End:** a replay of a decided match shows the same victory screen; one that
  was quit shows *REPLAY ENDED*. Both offer **Muster Rolls** and **Watch
  Again**.
- **Build-bound:** a replay only plays on the build that recorded it (the
  file carries the build fingerprint). Others are listed but cannot be played.

## 3. Saved games

**A saved game is a snapshot of the match, and loading comes only from the
snapshot.** Nothing is re-simulated. (Revised 2026-10-08: the first version
re-simulated the replay up to a marker, so load time grew with the match.)

A `.twbsave` holds:

- **The world**: every simulation entity and component (`WorldSnapshot`,
  Unity's `SerializeUtility`) — every unit, building, projectile, node, bank
  and order.
- **The state the world does not hold** (`SnapshotState`): the territory
  meters, curse wrath and the curse brain, the blood map, research, hero
  ledgers, cultures, the clocks, every simulation system's timers, fractional
  carries and RNG streams (found by reflection, so a new system is covered
  without a code change), the explored-map fog, and the start positions.
- **The commands already ordered** for ticks after the save (one tick of input
  delay).
- **The replay so far**: the resumed match keeps writing it, so a loaded game
  still produces one whole-match replay.

Saving takes a fraction of a second. Loading boots the map, restores the
snapshot in place of the starting spawn, rebuilds what is derived from the map
(navigation cost field and flow caches, start clearing, passability of the
resource nodes, territory types), and starts the clock at the saved tick.

- **Saving:** pause menu (Esc) → **Save Game**. Shown only in a single-player
  lockstep match that is being played — not in multiplayer, not while watching
  a replay, not after the match is decided.
- **Loading:** main menu → **Load Game** → **Saved Games** → Load. Saves live
  in `Saves/` beside the executable and are never deleted automatically.
- **Exact for gameplay, warm-up for pathing.** Every gameplay value comes back
  exactly — positions, health, banks, research, territory, the curse, every
  RNG stream. What is rebuilt rather than restored are the navigation caches
  (which flow field a unit was following), so units' paths in the first
  moments after a load can differ by a step from what they would have been.
  Loading the same save twice and giving the same orders gives the same match:
  the load itself is deterministic.
- **What does not survive a load:** the AI's private plans. It re-plans from
  the restored board.
- **A loaded game's replay embeds the snapshot** (a `snap` line). Watching it
  re-simulates up to the load, then restores that snapshot exactly as the
  player's load did and carries on — so the replay shows what the player
  actually played, and still verifies every recorded hash on both sides of the
  load. A save made after a load chains: each load adds its snapshot.
- **Build-bound**, like replays.

Multiplayer saves are out of scope (each peer records its own replay; a
resumed match would need every peer to load the same snapshot and reconnect).

## 4. The files

**Replay** (`.twbr`): UTF-8 text, tab-separated, documented in the header of
`Assets/Scripts/Core/Replay/ReplayFile.cs` — header (build fingerprint, map,
`MatchSettingsSync` settings blob, players, the lobby slots, tick rate, input
delay), then `c` (command, lossless wire form), `h` (state hash), `snap`
(tick + base64 saved game, for a resumed match) and `end`.

**Saved game** (`.twbsave`): a zip, documented in the header of
`Assets/Scripts/Core/Save/SaveGameFile.cs` — `save.txt` (tick, match time,
label, world clock), `replay.twbr`, `world.bin`, `state.bin`, `pending.txt`.

## 5. Code

| Piece | Where |
|---|---|
| Format, header capture/apply | `Scripts/Core/Replay/ReplayFile.cs` |
| Recorder (write-through, continuation after a load) | `Scripts/Core/Replay/ReplayRecorder.cs` + `ReplayRecorder.asset` |
| World snapshot | `Scripts/Core/Save/WorldSnapshot.cs` |
| Non-ECS state (statics, system fields) | `Scripts/Core/Save/SnapshotState.cs`, `SnapshotCodec.cs`; Bootstrap's own section in `Bootstrap/SnapshotSections.cs` |
| Save file, Save Game | `Scripts/Core/Save/SaveGameFile.cs`, `SaveGameWriter.cs` |
| Session carried across the scene load | `Scripts/Core/Replay/ReplaySession.cs` |
| Folders, listing | `Scripts/Core/Replay/SavedGames.cs` |
| Solo init, replay feed, hash verify | `LockstepManager.InitializeSolo` / `UpdateReplayFeed`, `LockstepBootstrap.InitializeSoloNow` |
| Snapshot load in place of the spawn | `SpawnDelayHelper.RestoreSnapshot` (from `GameBootstrap.InitializeFactions`) |
| Playback bar | `UI/Ingame/Panels/ReplayControlsPanel.cs` + `.asset` |
| End-screen buttons | `UI/Ingame/Panels/ReplayEndButtons.cs` (through `VictoryPanel.ExtraButtons`) |
| Pause-menu Save Game | `PauseMenuPanel.SaveGame` |
| Load Game screen | `Scenes/Menus/MainMenu/SavedGamesScreen.cs`, `LoadGameMenuButton.cs` |
| Headless verifier | `HeadlessBatch`: `-twbReplay <file>` (exit 0 identical, 42 diverged, 3 unreadable); `-twbSaveAt <s>` (save mid-match); `-twbResume <save> -twbLimit N`; `-twbOracle <save> -twbOracleReplay <replay>` (restore the snapshot, feed the uninterrupted match's commands, compare hashes — measures what a load does not carry) |

The post-game Muster Rolls screen records every match and every replay on sim
time: [Muster_Rolls_PostGame.md](Muster_Rolls_PostGame.md).
