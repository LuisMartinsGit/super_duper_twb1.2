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

Left in: **Skirmish**, **Multiplayer**, **Load Game** (unhidden 2026-10-08), **Tutorial**, **Settings**, **Quit**.

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

### Saved games and replays (unhidden 2026-10-08)

Saved games and replays exist ([Replays_And_Saves.md](Replays_And_Saves.md)):
Save Game in the pause menu, Load Game on the main menu with a Saved Games
tab and a Replays tab. Every single-player skirmish records a replay into
`Replays/` beside the exe; saved games go to `Saves/`. Both play only on the
build that made them, so a tester who updates loses access to older files —
the list shows them greyed out with the build that made them.

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

Every `AI_<Faction>.log` / `Player_<Faction>.log` also gets a **SNAPSHOT
line once a minute** (banks, military, territories held), from
`MatchSnapshotLog`. It used to be written by the Display 2 board; it was split
out so it survives that board being stripped (next section).

### The Display 2 debug board does not ship

`StatsBoardHUD` — the second-monitor board with live charts, the per-faction
economy table, FPS / entity count / lockstep tick and the curse summary — is a
development tool. It compiles only under
`UNITY_EDITOR || DEVELOPMENT_BUILD || TWB_DEBUG_DISPLAY`, and never when
`TWB_NO_DEBUG_DISPLAY` is defined; `GameBootstrap` adds it under the same
condition. Release builds (`PlayerBuild`, `BuildOptions.None`) are not
development builds, so it is absent from them with no extra step.

- Force it into a non-development build: add `TWB_DEBUG_DISPLAY` to the
  player's scripting define symbols.
- Remove it everywhere, the Editor included: add `TWB_NO_DEBUG_DISPLAY`.

Nothing is uploaded. The files stay on the tester's machine until they choose
to send them, and the shipped `README - please read.txt` in the folder says so.

---

## 3. Reversing it

- Menu: delete entries from `ShipGateMenuTrim.AlphaHiddenMenuItems`.
- Logging: it is not alpha-specific and should stay. Per-match folders and
  console capture are worth having permanently; only the tester-facing README
  is alpha framing.

---

## 4. Editions (2026-10-12)

The shipped build is a **demo** and must not carry the cultures that are not
in it. One version, three builds from the same commit:

| Edition | Ships | Version / release tag | Who can download it |
|---|---|---|---|
| **A** — demo | Age 0 + Alanthor | `0.0.41` / `v0.0.41` | every key |
| **B** — developer | + Feraldis | `0.0.41B` / `v0.0.41B` | keys granted B |
| **C** — developer | + Feraldis + Runai | `0.0.41C` / `v0.0.41C` | keys granted C |

**Maps per edition (2026-10-21).** Edition A ships only **Fiendstone Pass** and
**Yiel Lymwérra** (`MapRegistry.EditionAMapScenes`, applied to the player build
by `MapSceneSync.ScenesForPlayerBuild`); B and C ship the full
`MapRegistry.ShippingMapScenes` list. The editor keeps every map either way.

**Versioning.** Every release bumps the one version (`bundleVersion`); the
letter only names the edition. `0.0.41B` always means "the 0.0.41 demo plus
Feraldis". An edition whose content did not change need not be *published*
(its launcher just stays put), but it is never numbered separately. The
edition rides in the version string, so the multiplayer handshake already
keeps editions out of each other's lobbies, and every log and replay names it.

**What the build strips** (`Editor/EditionContent.cs`, for the length of one
build, restored afterwards): every def of a stripped culture from
`Resources/TechTreeCatalog.asset` — so its SOs, prefabs, models, materials
and controllers are not referenced and Unity leaves them out — and the
culture's model branch inside the shared building prefabs (the Barracks,
Fortress, Hut… carry per-culture branches for `BuildingVariantVisual`). The
**Fiendstone Keep** counts as Feraldis (it is the Feraldis landmark).
`PlayerBuild` then checks the build report and **fails the build** if any
asset from a stripped culture's folders was packed anyway.

**What it does not strip:**
- **the C#** — `Civs/<Culture>/` code compiles into the same assembly as the
  factories that dispatch to it; it ships inert, with no data to act on;
- **the sect code and data** — but which sects can be ADOPTED follows the
  edition (2026-10-15, developer): **A offers only the House of Vaelith (the
  four Alanthor sects), B adds the four Feraldis sects, C all twelve.**
  `SectConfig.IsInEdition` (through `GameEdition.Allows`) gates adoption for
  players and AI; the religion menu, picker and tree show only the edition's
  clusters, and a set passive the edition cannot complete (Foreknown in B,
  everything but Sovereign Hour in A, Concord below C) is not shown.

**What a build offers** is decided in one place, `GameEdition.Allows`, which
`CultureConfig.IsComingSoon` reads — the age-up menu, the landmark buttons,
the AI's pick, the lobby's start age and the age-up command all go through
it. A culture an edition does not ship shows as locked, exactly as both did
in the demo before editions.

**The Editor** plays as the edition picked under **Waning Border > Edition**
(default A). Player builds ignore that pick.

**Building and releasing.** Only `PlayerBuild` may build a player
(`EditionBuildGate` refuses the Build Settings window, which would carry no
edition and no strip):
1. `tools/Launcher/publish.ps1 -Edition <X>` — each edition has its own
   launcher (own install folder, chosen on first install and suggested as
   `…\Shardroot Entertainment\The Waning Border [B|C]`, own settings file, own Start Menu entry), published to
   `tools/Launcher/publish/<X>/`. A build carries the launcher of its own
   edition.
2. Build: bridge `build <dir>` (A) / `build-edition <X> <dir>`, or batch
   `-executeMethod TheWaningBorder.EditorTools.PlayerBuild.Build -buildPath
   <dir> -twbEdition <X>`. It writes `edition.txt` into the build.
3. `tools/release.ps1 -BuildPath <dir> -Edition <X>` — refuses a build whose
   `edition.txt` disagrees.

**Downloads are key-gated** (`tools/UpdateWorker`): a key's KV value is a
bare tester name (a demo key — every key issued before editions) or a JSON
grant `{"name": "...", "editions": ["A","B","C"]}`. A key asking for an
edition it is not granted gets a 403, which the launcher treats like a bad
key and asks for another. Provisioning commands are in `wrangler.toml`.
