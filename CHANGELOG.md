# Changelog

All notable changes to The Waning Border.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions are the Player Settings **Bundle Version**, which is also what the
main menu shows and what every match log records — so a tester's report and a
build always name the same number.

---

## [Unreleased]

Nothing yet.

---

## [0.0.13] — 2026-08-20

The last full download. From the next version on, an update fetches only
the files that changed.

### Added

- **Updates are incremental.** A build is 1.1 GB and 863 MB of that is a single
  asset file that only changes when an asset does — v0.0.10 and v0.0.11 differed
  by one line of code, their packages differed by 78 bytes, and every tester
  downloaded 473 MB to get it. The launcher now checks what it already has and
  fetches only the parts that differ, straight out of the same package by byte
  range. A code-only patch should be a few MB. Anything that goes wrong falls
  back to the download you get today, so an update can cost time but never a
  working install.
- **The game keeps the launcher up to date.** The launcher sits outside the
  folder an update replaces, so until now a fix to it could only reach you by
  asking you to download one by hand. Installing this version updates it for
  you, in the background, with nothing to click. **This is why 0.0.12 is still a
  full download** — it is the one carrying the new launcher.

---

## [0.0.12] — 2026-08-20

A one-fix patch on 0.0.11.

### Fixed

- **The skirmish menu's panel backgrounds looked stretched on 16:10 displays.**
  The menu canvas scaled to match screen HEIGHT, so on anything narrower than
  16:9 the canvas got narrower too — 3456 units wide at 16:10 instead of 3840.
  The two columns are sized as a fraction of that width and shrank with it, but
  the plate artwork inside them is a fixed width baked at 16:9, so the
  backgrounds no longer fitted the panels they belonged to. The canvas now keeps
  its full width at every aspect and puts the leftover height into empty margin
  above and below instead: the layout on a 16:10 screen is now exactly the 16:9
  one, just with more breathing room top and bottom. 16:9 and ultrawide are
  unchanged.

---

## [0.0.11] — 2026-08-20

A one-fix patch on 0.0.10.

### Fixed

- **The name prompt asked again every time you returned to the main menu.** It
  was gated on "this process created the settings file", which is true for the
  whole run — so backing out of skirmish or multiplayer asked again. It is
  gated on a saved answer now, which flips when you actually answer rather than
  when you are asked: closing the game on the prompt asks once more next
  launch, and answering it never asks again. Players who have already named
  themselves are not re-asked.

---

## [0.0.10] — 2026-08-20

The menus build. Multiplayer gets its own screen, laid out like the skirmish
one so the two lobbies read the same, and the settings that used to be typed
into a lobby every time now live in a file the player owns. Several menu
controls that had never actually worked — dead buttons, unclickable options,
text that ignored its own colour — turned out to share three causes.

### Added

- **`settings.json`, beside the executable.** The player's name and every
  persisted setting in one file, created with defaults on first run. It
  replaces PlayerPrefs, which on Windows means the registry: invisible,
  un-editable, and impossible for a tester to send back with a bug report —
  the same reason the logs already sit there. Existing installs are migrated
  on the run that creates the file, so nobody loses their video or audio
  settings.
- **First run asks the player's name**, once, and Settings can change it any
  time. Multiplayer reads it from there instead of asking again in every
  lobby.
- **`MultiplayerMenu.unity`.** The LAN screen is its own scene now, built from
  a copy of the skirmish one: same map plate, same roster plate, same footer.
  The host / join cards overlay it.
- **The multiplayer lobby is editable while you are in it** — map, starting
  resources, starting age, fog, curse nodes, teams, colours and the game name,
  none of which could be changed after the lobby opened.
- **AI personality per slot**, matching the skirmish roster, and the lobby is
  sized from the roster itself: the top free rung adds a player, the bottom
  one closes it.
- **The game browser shows what a game is** before you commit to it — host,
  map and occupancy — and you pick a row and press CONNECT rather than hunting
  for the right JOIN button. A full lobby lists but cannot be joined.
- **`tools/fetch-logs.ps1`.** Lists what testers have uploaded and unzips
  anything new into `logs-inbox/<tester>/<match>/`, skipping what is already
  there. Sorted worst-first: a match that threw an exception lists above one
  that only logged errors, above a clean one.

### Changed

- **Three clicks to a multiplayer match**: Multiplayer, HOST, START. The
  create-lobby window is gone — everything it asked for is either editable in
  the lobby or no longer worth asking.
- **No more port field.** The game port is standard and moves itself when it
  is busy, so two instances on one machine both host without being told to.
  Direct connect keeps a port box, because a joiner whose broadcast is blocked
  has no other way to name the host.
- **The lobby title reads `MULTIPLAYER — <game name>`** on both peers. A
  client used to see a generic "LOBBY" while the host looked at a named one.

### Fixed

- **Every menu label rendered dark olive yellow.** Synty ships the menu font
  with a gold `_FaceColor` on its default material, and TMP draws a glyph as
  vertex colour × face colour — so every authored colour was multiplied into
  gold, and the pass that sets the whole screen white had been a no-op on
  screen for as long as it has existed.
- **Pressing Play landed on a main menu whose buttons did nothing.** The
  editor's play-mode override pointed at Synty's *sample* menu scene, which is
  built from the same prefabs and so is indistinguishable from the real one —
  but is not named `MainMenu`, and every runtime hook that wires those buttons
  gates on that name. It now targets the real scene, announces itself in the
  console, and flags a boot scene that is not in Build Settings.
- **Decorative frames swallowed every click underneath them.** A full-rect
  border drawn last with `raycastTarget` left on absorbs the pointer for the
  whole plate. This is what made the lobby's map start-positions unclickable.
- **The map options could not be clicked at all.** They sit under a nested
  Canvas with no `GraphicRaycaster`, which takes every graphic below it out of
  the root canvas's registry and hands them to nobody.
- **Buttons and dropdowns ignored their authored widths**, growing with the
  display: `childForceExpandWidth` overrules a `LayoutElement` that asked for
  none, and the two footer buttons were rendering at nearly three times the
  size they were built for.
- **A name typed with a comma or a pipe corrupted the lobby for everyone.**
  Those are the delimiters the lobby protocol packs names into, so one of
  either shifted every field after it — a colour index read as a team, or the
  slot list dropped entirely. Names are sanitised at the source now.
- **The release script could not write its manifest at all.** The BOM fix from
  v0.0.9 reached for `Set-Content -Encoding utf8NoBOM`, which exists only in
  PowerShell 7; this machine has 5.1. The guarded fallback beside it never ran,
  because an unknown `-Encoding` value fails at parameter binding — terminating,
  and so untouched by the `-ErrorAction SilentlyContinue` it was relying on. The
  manifest is written with .NET on both editions now, which emits no BOM and has
  nothing to fall back to.
- **Release notes reached testers as mojibake.** Three separate encoding
  faults in the release script, each invisible while the notes were a
  hand-typed ASCII sentence and each fatal the moment they came from this
  file. `Get-Content` reads with the system ANSI codepage on Windows
  PowerShell 5.1, so every em-dash came back as three Latin-1 characters;
  `Invoke-RestMethod` encodes a string body as Latin-1 too, so the euro sign
  that produced could not be represented at all and GitHub rejected the
  release *after* the build had been zipped; and `Set-Content -Encoding utf8`
  writes a BOM, which every `manifest.json` so far has carried and which only
  parsed because the fetch specification strips a leading BOM. The published
  v0.0.9 notes were repaired in place without re-uploading the build.

---

## [0.0.9] — 2026-08-18

The plumbing build. Testers now get updates and send back their logs without
being asked, and the multiplayer desync hunt gets the two things it was
missing: a fork that can be localised to a single tick, and a simulation
that is not free to differ between two brands of CPU.

### Added

- **The launcher.** Testers run `TWBLauncher.exe`, which checks for a new
  build, downloads it, verifies its checksum and starts the game. Builds
  live on a private repository; the launcher holds no credential that could
  reach it, and each tester has a key that can be revoked on its own. The
  previous build is kept as `game.old`, so a bad patch is undone by renaming
  a folder rather than re-downloading.
- **Match logs send themselves.** When a match ends its log folder is
  uploaded; if the game crashes first, the launcher sends it on the next
  start — which is the case that matters, because the match that crashed is
  the one worth reading. Your copy stays in the `logs` folder either way,
  nothing is deleted, and it can be switched off per tester on request. The
  logs README no longer claims nothing is uploaded, because that is no
  longer true.
- **A build fingerprint beside the version.** The main menu now reads
  `v0.0.9` followed by eight hex characters, and `Summary.txt` records the
  same. The version is typed by hand in Player Settings, so two different
  builds can carry the same number; the fingerprint is what says whether two
  machines are running the same bytes.

### Fixed

- **The menu version label never updated.** It looked for a `Version` object
  and asked it for a text component, but in the menu prefab that object is a
  layout container and the text sits on a child. The lookup returned nothing,
  the label was never written, and the hand-typed `v0.1.0` placeholder
  shipped in every build since.
- **Launcher windows clipped their own text** on any display running above
  100% scaling. Every control had been pinned to a fixed pixel offset; they
  now size themselves to their contents.

### Changed

- **Desync forks can now be localised to one tick.** In deterministic
  multiplayer the state checksum is written to `Lockstep.log` every tick
  while still being broadcast on the usual interval — no extra network
  traffic. Diffing two logs previously narrowed a fork to a thirty-tick
  window with a handful of suspects; it now names the tick.
- **Floating-point behaviour pinned across the simulation.** All 52
  Burst-compiled entry points in the simulation and tech-tree code now
  specify `FloatMode.Deterministic` and `FloatPrecision.High`; not one of
  them pinned either before. This is belt-and-braces rather than a fix for
  a live fault: 0.0.7 already pinned Burst to a single CPU target, so every
  machine runs identical instructions and the compiler was not free to pick
  a different maths routine per processor. What it buys is that the
  guarantee no longer rests *only* on that one setting — adding a second
  target later, for the performance, would otherwise quietly re-open the
  0.0.7 desync.

---

## [0.0.8] — 2026-08-18

The second-match build. The headline fix: the first multiplayer match of a
session worked, and every one after it silently ran as two separate
single-player games — both players saw the lobby, the teams, and each
other's starting bases, but nothing either of them did reached the other,
and the AI quietly played the remote player's faction. Also in this build:
the game speaks Portuguese, the settings menu actually opens, buildings
look like their footprints again, and a review scenario shows every
building at every level.

### Added

- **A build fingerprint beside the menu version**, and the version label
  itself finally driven from Player Settings rather than a hand-typed
  string. See 0.0.9 for the full description — both landed in this build.
- **Portuguese (Português).** Complete UI translation — every menu,
  tooltip, button, notification, tutorial chapter, unit and building name,
  tech description, sect text and loading tip (~1,250 strings, European
  Portuguese). Switch language in Settings; the choice applies instantly
  and persists. English remains the default, and anything untranslated
  falls back to English rather than breaking.
- **Music volume control.** A separate slider in Settings next to Master
  Volume — changes are audible while dragging and persist across runs.
- **The Settings entry in the main menu now opens the options panel.** It
  had been wired to nothing since the July menu rework; graphics quality,
  resolution, display mode, both volume sliders and the language switcher
  live there.
- **Building Showcase scenario rebuilt as a review grid.** Every building
  at every upgrade level (including the true Age 0 state) laid out on a
  flat map over a bright-green build-grid overlay, with the Temple of
  Ridan carrying all six chapel statues and a Worker standing by for
  scale. Victory conditions off. Scenario scenes also load from the
  editor again (see Fixed).

- **Feraldis is playable.** Fire and blood joins Alanthor as a choice for
  both you and the AI. Its mechanics were audited end to end first —
  frenzy-on-blood, bleeding, the warpath, raider camps and the plunder
  ladder, batch training, the Mine and the Corruptor ritual all verified
  working, with five breakages fixed (below). Runai stays locked; its
  trade lanes are still unbuilt.
- **The AI decides whether a fight is worth having.** A new engagement
  layer weighs the army against what is actually waiting at the target —
  including BUILDINGS, which the old assessment ignored entirely, so a
  Hall with a multi-target gun counted for nothing. A wave that cannot win
  now holds and says why in the log; a wave losing a fight it already
  joined disengages instead of feeding itself in.

### Changed

- **The Temple of Ridan is half its former size.** The August 13 footprint
  doubling had given the cathedral a 16 x 16 m class of its own and it
  dwarfed everything in play; it now shares the Hall class at 8 x 8 m,
  and the chapel statues and their docking ring halve with the wall they
  sit against.
- **The Barracks is one cell larger** — 10 x 10 m instead of 8 x 8 m, so a
  unit-producing hall reads bigger than a house.

### Fixed

- **Multiplayer works again after returning to the menu.** The lockstep
  bootstrap created by a second lobby start destroyed ITSELF: the old
  bootstrap's deferred teardown still occupied the singleton slot for the
  rest of the frame, so the fresh one concluded it was the duplicate. With
  no bootstrap alive, the match booted without any networking — silently.
  The newer bootstrap now always wins the slot, the old one is torn down
  immediately, and a multiplayer boot with no lockstep logs a loud error
  instead of playing pretend.
- **Buildings render at their intended size again.** Completed buildings
  lost their footprint fit the moment construction finished (the visual
  swap kept only the art's raw scale), so huts and barracks read tiny
  while statue-heavy prefabs read huge. Every visual path — spawn, level
  swap, culture switch — now fits the model to its footprint, off-centre
  art is re-anchored so the mesh centre sits on the building's true
  position, and attack rings hug the walls as a result.
- **Freshly trained units appear beside their building.** They used to
  walk out of a point up to 17 m away on a fixed north-east diagonal;
  they now exit at the footprint edge, on the side facing the rally
  point when one is set.
- **The AI no longer starves itself into a stalemate.** Late-game AI
  factions ended matches sitting on thousands of iron and veilstone with
  under forty supplies, no research, and a single adopted sect. The cause
  was one rule: building upgrades required a supply floor, and the Guild
  upgrade is what RAISES supply income — so the moment supplies dipped, the
  one cure was locked out with everything else. A second rule made it
  worse, reserving veilsteel for Smelter levels in a way that never
  released (every new Smelter reset the condition), so Guild levels needed
  65 veilsteel banked to spend 5 and lost every contest to veilsteel-free
  tower levels, 61 to 4. The supply engine is now exempt from the floor,
  and the veilsteel reserve lifts once one Smelter is maxed. Research and
  sect adoption were starving on the same empty wallets and should recover
  with them.
- **Buildings can no longer be placed inside each other.** The placement
  check ran on the position a builder ASKED for, but buildings snap to the
  build grid on the way in, so the ground that was checked and the ground
  that was occupied could differ by up to a full cell — which is how AI
  watch towers ended up standing in their own Hall. Candidates are now
  snapped before they are checked, and the placement executor re-tests the
  final footprint against every existing building and refuses (before
  charging) if they overlap.
- **Scenario scenes load in the editor again.** The ship gate stripped
  them from the scene list, which also broke editor play mode; they now
  stay listed for the editor and are filtered out of player builds at
  build time instead.
- **A failed scene load no longer freezes the game.** The loading screen
  crashed and re-froze time forever; it now reports the error and returns
  to the main menu.
- **Match starts and building level-ups are recorded.** Skirmish and
  multiplayer host/client launches write one log line naming the flow,
  map, seed and player count, and every building level write logs its
  entity — so "which mode actually started" and "which buildings really
  upgraded" are never forensic digs again.

---

## [0.0.7] — 2026-08-16

The cross-machine determinism build. The first LAN match between two
different PCs desynced after 14 seconds with byte-identical command logs:
the two machines' CPUs were running different Burst-compiled code (the
package default ships SSE2 and AVX2 variants and picks one per machine at
runtime) and drifted apart in the low bits of every float. Multiplayer
needs this build on both machines (protocol 4).

### Fixed

- **Builds now compile Burst code for exactly one CPU target (SSE4).**
  With the default dual-target dispatch, two different CPUs simulated
  slightly different floating-point results — invisible for minutes, then
  a desync. One target means every machine runs identical instructions.
  (Any PC from roughly 2009 onward has SSE4.)
- **A determinism fork is now caught the moment it is born.** The state
  checksum quantised positions to a millimetre, so sub-millimetre drift
  hid through 13 clean checks and only surfaced after crossing a
  build-range test — the desync reported tick 420 for a divergence born
  around tick 31. Deterministic matches now also hash the raw position
  bits.
- **Both peers now agree on the input delay.** Each side adapted the
  delay to its own measured round trip (the desync dumps read 5 ticks on
  the host, 3 on the client). Peers now advertise their delay in the ping
  and everyone adopts the highest.
- **Desync dumps are locale-proof and date their entities.** The dump
  wrote decimal commas on a Portuguese Windows — diffed against a peer
  with dots, every line is a false difference — and printed spawn=0 for
  every entity. It now writes invariant numbers and the real spawn tick.

---

## [0.0.6] — 2026-08-16

The log-review build. Two full matches were read line by line and every
finding fixed: AI opponents that never aged up and stopped attacking after
one wave now play the whole game, matches end on a real victory screen, and
placement, previews, siege timing and the training queue all behave the way
they always should have. Observer mode grew a proper spectator perspective.

### Added

- **Observer perspective follows the selection.** In observer mode,
  selecting any player's unit or building shifts the whole view to that
  player: their fog of war on the terrain and minimap (including their
  team's shared vision), their resource bank and population on the top
  bar, their income popups, their training queues. Box-select and
  double-click select-all also operate on the viewed player's units.
  Deselect everything and the observer sees the whole map again, with
  the resource bar blanked.
- **A victory screen.** A match now ends on a full-screen VICTORY / DEFEAT
  panel with a Return to Main Menu button — for conquest and well victories
  alike, and for observers ("<culture> WINS"). Before, the outcome was a
  toast that faded after 2.5 seconds, the game sat there for another 7.5,
  then yanked itself back to the menu with no input from you.
- **The build grid is visible while placing.** White 2 m grid lines follow
  the cursor and a gold outline marks the exact cells the building will
  occupy, seams and all; the outline turns red while the spot is invalid.
  The lines existed before but rendered opaque-solid instead of blending —
  now they read as ground markings, gold on white.

### Changed

- **The training queue moved into the roster area and holds 16.** Select a
  production building and its queue fills the sixteen portrait slots beside
  the stats panel: the top-left slot is the unit in production and carries a
  progress bar, everything behind it is pending, and right-click cancels a
  pending item with a full refund. The production cap is now 16 (training
  and research share it; it was 5). The strip above the actions panel now
  shows research only, so nothing is displayed twice.
- **Ballistas have their own model.** The ballista had been wearing the
  Synty catapult as a placeholder since its refit. The wiring tool
  (Waning Border > Game Data > Wire Siege Visuals) now builds its prefab
  from the actual ballista art, and its bolt renders in flight.
- **Smoother frames in AI-heavy matches.** All AI brains used to think in
  the same frame (spiking up to 91 ms), and every Gatherer's Hut on the
  map recalculated its income in one frame every two seconds; brains are
  now phase-staggered and hut recalculation rotates across factions. Five
  previously unmeasured per-frame systems (presentation sync, fog
  stamping, melee combat, influence texture, unit indicators) now report
  into Perf.log, so the next slow match names its culprit.

### Fixed

- **AI opponents now age up.** In logged matches no AI ever chose a culture:
  the age-up order sat 13-30 build-order steps deep (unreachable in a real
  match), one strategy's choice building was silently dropped by a timeout —
  making that AI structurally unable to age up, ever — and the 700-supply
  cost never accumulated because the budget only protected supplies while
  the unreachable step was active. An age-up director now fires the moment
  the requirements hold (from five minutes in), builds the missing choice
  building if there is none, and tilts the budget toward the age-up bank.
- **AI armies no longer collapse after the first attack.** A chain of gates
  let every AI launch exactly one 5-unit wave and then never train again:
  military build-order steps were skipped outright when their Barracks was
  missing (one AI finished a match having never built one), and the next
  wave demanded more idle soldiers than the army director intended to
  field. Missing trainers are now built instead of skipped, waiting steps
  no longer time out while their trainer rises, wave requirements are
  clamped to the intended army size, and every step skip is logged.
- **AI clocks now start at the match, not at app launch.** All AI timing
  gates ran on a clock that included menu and loading time, so on the first
  match of a session the opening grace, the first-attack timer and the
  economy fallbacks had already expired at second zero — that is also why
  an AI could log "no trainer for Worker" before its Hall existed.
- **Runai caravans spawn correctly** — the unit had stats but no factory
  recipe, and the escort unit, whose design role was removed when caravans
  became combat-capable, is delisted instead of sitting in the catalog as
  a dead entry.
- **Building placement previews were far larger than the buildings.** The
  real building is scaled to fit its footprint at spawn; the preview ghost
  skipped that scaling and rendered up to four times the finished size. The
  ghost now goes through the exact same fit, so what you place is what you
  get.
- **Some previews showed future building levels stacked on the current
  one.** The multi-level building prefabs carry every culture and level
  variant inside them; the real spawn hides all but one, the preview showed
  them all at once, interpenetrating. The preview now runs the same variant
  setup: neutral Lv0 before a culture, your culture's Lv1 after.
- **Choosing a culture no longer transforms your base instantly.** Clicking
  the age-up button changed every building's visuals and switched on the
  Alanthor build-inside-influence rule on the spot, 60 seconds before the
  research finished. Both now wait for the research to complete; only unit
  banners preview the pick immediately, as intended.
- **The Temple's upgrade flourish played over and over.** Two systems
  permanently disagreed about an upgraded Temple's level and re-triggered
  the level-up dissolve and spark burst every half second, forever. One
  source of truth now; the flourish fires once, on the upgrade.
- **Ballista and catapult damage now lands when the projectile lands.** The
  hit was always simulated at impact, but the visuals lied: the ballista's
  shot resolved in a fraction of a second while a slow decorative stone was
  still mid-air, and the catapult's stone flew at its own speed unrelated
  to the sim. The ballista's bolt is now the actual projectile, and the
  catapult stone's launch is solved so it strikes at the exact moment the
  damage applies.
- **The Runai catapult had no model at all** — it fired from inside a
  placeholder capsule, with no visible stone. The same siege wiring tool
  builds its engine prefab, arms the throw animation and connects the shot
  effect.

---

## [0.0.5] — 2026-08-16

Multiplayer. The menu entry is in the build: host a game, it appears in the
other machine's browse list, join, play. LAN only — two machines on the same
network. Internet play is not advertised: it works only via direct IP with
the host's ports forwarded, and nothing in the UI will tell you that.

**Both machines need this exact build.** A mismatched pair is refused at the
lobby door with a message naming both versions — that message is right,
re-copy the build. And if a match ever goes out of sync, it says so on
screen and both machines write the evidence into their `logs` folder:
**both players' log folders are needed**, the diagnosis is the difference
between them.

### The desync hunt

0.0.3's multiplayer rebuild shipped untested on two machines. The first real
sessions desynced at match start, five times, for five different reasons —
each one found, fixed, and given a tripwire so it cannot return quietly:

- **The deterministic simulation was never actually running.** The biggest
  one, and the last one found: a leftover defense against a Unity networking
  package was silently removing the fixed-step driver during loading — three
  seconds after it was installed. Every "deterministic" match to date had
  actually been running on each machine's own frame clock, which is the exact
  thing the 0.0.3 rebuild existed to stop. The defense now spares our own
  driver, the game verifies the real thing every tick, and repairs and
  reports it if anything ever detaches it again.
- **The match clock started before the world finished loading.** Ticks began
  while deposits and wells were still being placed, so a faster machine had
  more of the map than a slower one at the same tick. The clock now waits for
  the world to be complete.
- **The two machines dealt out the map in different orders.** The scene's
  deposit markers came back in whatever order Unity felt like, per process,
  so the same deposits got different identities on each machine. Marker order
  is now fixed and identical everywhere.
- **The curse started its pulse at a per-machine moment.** Its schedule was
  anchored to a wall-clock readiness flag, so veilstone precipitated on
  different ticks. It now anchors to the match clock.
- **A second match in one session inherited the first match's leftovers.**
  Timers, random streams and a "world is ready" flag survived from the
  previous match — differently on each machine. Everything per-match now
  resets at match start.

### Fixed — actions that only happened on one machine

- **Every purchase now costs both machines.** Resources were deducted on the
  machine that clicked and nowhere else, and the banks are part of the desync
  check — so the first building of any match would have forked it. Spending
  now happens identically on every machine, for players and AI alike. This
  also closed a real exploit: queueing a discounted unit and cancelling it
  refunded the full price.
- **The AI now plays the same game on both machines.** Its attack waves,
  all of its miner tasking and its scouts were host-only side effects; the
  other machine's copy of the AI stood idle with a frozen economy. Every AI
  order now travels the same road as a player's.
- **Alanthor walls exist on both machines.** Placing a wall hub or extending
  a wall built it on the placing machine only — and quietly corrupted the
  identity sequence every later unit draws from, breaking things far from
  the wall. Wall placement is now a replicated order like everything else.
- **Six more actions reach the other player**: vault deposits and
  withdrawals, bazaar packing and unpacking, sect glow allocation, the
  Feraldis Corruptor's crack-the-well order, cancelling a training queue
  slot, and the orders after the first in a planning-mode batch.

### Fixed — the lobby

- **Games now show up in the browse list reliably.** The host used to
  advertise on one network interface of Windows' choosing — routinely a VPN
  or virtual adapter no other machine is on. It now advertises on all of
  them, and browsing machines also actively ask, which works through the
  firewall situations that eat one-way broadcasts.
- **One hosted game no longer appears as two or three list entries** on
  machines with several network adapters.
- **A stuck search says so.** After ten quiet seconds the browse list stops
  pretending and names the likely causes — different network, firewall —
  and points at direct IP as the fallback.

### Known issues

- **Windows Firewall must be allowed on both machines** the first time you
  host or join, or nothing sees anything.
- **Building rotation is ignored in multiplayer** — mouse-wheel rotation
  while placing applies in single player only, for now.
- **Ground-aimed unit abilities may land at a slightly different point on
  the other machine.** Being fixed; sect powers are unaffected.
- **Teams still are not applied from the map layout in the multiplayer
  lobby** — set them by hand on the team chips (carried over from 0.0.3).

---

## [0.0.4] — 2026-08-16

Map changes. Runtime-generated-terrain maps are out of the shipped build —
multiplayer cannot trust two machines to generate identical ground, and the
shipping maps all bake their terrain. **Sundered Crown** is the alpha's map.

---

## [0.0.3] — 2026-08-15

Third alpha build. Movement, mining and the tutorial — most of this cycle went
into one theme: a unit that is told to go somewhere should walk there in a
straight line, arrive, and do the job, rather than orbit, give up, or wander
off to something else.

> **This build has not been through a play session.** Everything below compiles
> and the reasoning is recorded in the code, but none of it has been watched
> running. The movement changes in particular touch every unit in the game —
> please hammer them.
>
> **Update — first session played.** The three sections marked *(from the first
> session)* below come out of its match logs.

### Fixed — units would not arrive

- **Units circled a destination forever.** Two recovery systems undid each
  other: when a unit was as close to its ordered point as the crowd allowed,
  one of them ended the order — and the other read that exact state as "idle
  unit away from its post" and re-issued the same unreachable point, about
  every two and a half seconds, indefinitely. Attack-move was worse, because it
  also overwrote the destination every frame, disabling every other recovery in
  the movement stack. Since attack-move is the only way the AI moves, its
  armies were the worst offenders.
- **Units gave up several metres short.** A unit was declared "arrived" if any
  other unit happened to be closer to its goal and nearby — including one
  merely walking past, or one standing at a different spot entirely. It now
  has to be genuinely parked on the goal, which is the only case where it can
  actually keep you out.
- **Units ran at full speed until the instant they stopped.** The steering
  vector is normalised, so near the goal — where the forces almost cancel — a
  unit still moved at full tilt in whatever direction was left over. That is
  the circling motion. They now decelerate into a destination.
- **Two units sent to the same point could never both arrive.** Arrival needed
  half a metre; unit separation holds them a metre and a half apart. Whoever
  got there second was stuck by construction. Rally points, formation slots and
  AI staging positions were all affected.

### Fixed — mining

- **Workers mined from about two squares away.** The three systems that drive
  mining each carried their own reach, measured differently, and the loosest
  one won: a worker already within two and a half squares never took a step.
  There is now one reach, measured to the node's surface, and workers walk up
  to the node.
- **Workers got lost and circled the node.** Every mining path aimed the worker
  at the node's own tile — which the node makes impassable, so the destination
  could never be reached. It also defeated the straight-line check, so the
  approach was not even direct. Workers now walk to a spot beside the node.
- **Right-clicking one node sent workers to a different one.** Clicking any
  node retargeted to the nearest node of its "patch", and the patch definition
  was wide enough to stride from one patch clean into the next. You now get the
  node you clicked; workers divert only when it genuinely cannot be mined.
- **A worker blocked by another stopped instead of stepping around it.** Stand
  positions ignored units entirely, so a second worker aimed at the exact spot
  the first occupied, pressed into it, and was then declared done. Workers now
  pick a free side of the node, and re-pick if someone beats them to it.
- **Villagers were pulled off mining onto building sites.** The rule that gives
  idle builders a nearby unfinished foundation judged "idle" purely by the
  absence of a build order — and a villager mid-mine has none. It would be
  adopted onto the site, the mining job silently dropped, and your economy
  would wander off somewhere you never sent it.

### Fixed — formations and tight spaces

- **Units reversed out of gates, alleys and wall breaches.** The wall-slide
  asked whether the whole flank was open two and a half metres out. In any
  corridor narrower than five metres both sides answered "blocked", so the code
  concluded dead end and backed the unit out — of exactly the gap you were
  sending it through.
- **Movement stuttered along walls and around corners.** Approaching an
  obstacle replaced the unit's entire heading with a sideways slide, at any
  distance — throwing away the pathing that already knew where the corner went.
  Units crabbed sideways, cleared the obstacle, lurched forward, clipped again.
  The response is now scaled by how close the obstacle actually is.
- **Formations did not hold shape while moving.** Three causes: the group's
  invisible leader paid none of the costs its members pay — no crowd, no
  obstacles, no terrain or curse slowdown — so it simply outran them; the
  catch-up speed cut out at exactly the distance where members were still
  losing ground, leaving them permanently trailing; and the steering rule that
  settles a crowd at its destination was also dissolving the formation over the
  last fifteen metres of the march.
- **Ordering a formation onto broken ground piled it into one spot.** Slot
  positions were pure geometry with no check against the terrain, so slots
  landing on cliffs or water were each snapped to the *same* nearest walkable
  tile. Slots are now placed on distinct, reachable ground.
- **Units could not walk between adjacent buildings.** Buildings tile the build
  grid exactly, so a row of them was one solid wall. Buildings now stop
  blocking their outermost metre, leaving a lane between them. Walls are
  exempt — a wall line still seals.

### Fixed — other

- **Buildings damaged during construction finished at full health.** The
  construction tick deliberately preserves damage taken mid-build; completion
  then overwrote it. A site that was nearly razed while going up popped out
  pristine. It now completes at whatever health it actually has.
- **Melee units spun instead of walking around obstacles.** Their chase point
  was recomputed every frame from their own position, so it slid sideways as
  they did — sidestepping could never close the gap, and an obstacle in the
  direct line was a wall they pressed against forever.
- **The AI never mined the veilsteel node.** Its crew was only ever staffed
  from idle workers, and AI workers are essentially never idle. On top of that,
  the veilstone rebalance treated the veilsteel crew as spare labour and poached
  them mid-walk, so they never arrived.
- **Enemy ownership markers showed through fog of war.** They were drawn
  independently of the unit, so hiding the unit left its marker hanging in the
  dark — free intel on exactly where an unseen enemy was.
- **The veilsteel node was called "Unit"** when selected. It is now the
  **Veilsteel Mine** (previously "Sharp Crystals" in older docs).
- **The special-building choice showed the wrong names.** The cluster had one
  shared caption sitting over the leftmost button: it read "Choose one" and
  then renamed itself to whichever *other* button you hovered. So the leftmost
  building never showed its own name, and the label you were reading belonged
  to a different building than the one under it. All three buttons now carry
  their own permanent name, and nothing changes on hover — the description
  still appears in the tooltip beside the cursor.

### Multiplayer — LAN play, rebuilt

Two players could already connect, sit in a lobby and load a match. What they
could not do was play *the same* match: only commands were being synchronised,
never outcomes, and the simulation that turns one into the other ran on each
machine's own frame clock. Identical orders still resolved different fights and
finished buildings at different moments, so within a minute or two the two
players were in separate games that happened to share a lobby.

> **None of this has been through a two-machine test yet.** It compiles and the
> reasoning is recorded, but multiplayer needs a real session before it can be
> called working. The full analysis, including the test order to run it in, is in
> `docs/Multiplayer_LAN_Readiness.md`.

- **The simulation now runs in lockstep for real.** Both machines advance the
  world in identical fixed steps rather than free-running on their own frame
  rates. This is the change everything else rests on.
- **The game no longer looks like it is running at ten frames a second.** The
  shared simulation ticks 30 times a second, and units are drawn smoothly
  between ticks rather than jumping from one to the next.
- **Clicks respond about three times faster.** The delay between an order and
  the world acting on it was a fixed fifth of a second no matter the
  connection — on a LAN, that was almost entirely self-inflicted. The game now
  measures the actual round trip and uses it.
- **The AI no longer plays two games at once.** Every AI decision was being made
  independently on both machines *and* sent across, so the joining player's game
  applied each one twice — from two AI brains that had already drifted apart.
  The host thinks for the AI now, and the other machine is told.
- **The joining player gets the host's match, not their own leftovers.**
  Starting age, culture, starting resources and the pathfinding grid size were
  never sent; each machine used whatever its last single-player game had left
  behind. A host who had just played an Age 1 skirmish handed the other player
  an Age 0 game.
- **Mismatched versions are refused at the door**, with a message saying so,
  instead of connecting happily and falling apart ten minutes later.
- **Six more actions now reach the other player.** Sect powers, reliquary
  abilities, wall upgrades, keep wings, unit promotions and shift-queued
  waypoints all used to happen on the clicking player's machine and nowhere
  else — so one player watched a sect power kill their army while the other saw
  nothing cast at all.
- **Orders issued in the same instant can no longer arrive in different
  orders.** Commands were being sent without their sequence number, so the two
  machines sorted a tick's orders by whatever sequence the network happened to
  deliver.
- **Big fights no longer drop orders.** A tick full of commands used to be sent
  as one oversized packet that the network split up, and losing any piece lost
  the lot — most likely exactly when the most was happening.
- **The match tells you what the network is doing.** Ping and input delay in the
  corner; a banner when the game is waiting for the other player, when the
  connection is lost, and when the two games have gone out of sync. Previously
  a quit by one player froze the other one silently, forever.
- **Matches do not start until both worlds are built.** The faster-loading
  machine used to get several seconds of a world with no water, no cliffs and no
  bridges that the other one never had.
- **Multiplayer refuses maps whose terrain is generated at runtime.** Two
  machines cannot be relied on to generate byte-identical ground, and if they do
  not, nothing else about staying in sync matters. Every shipping map bakes its
  terrain, so nothing is lost today.

### Multiplayer — logs you can actually test against

- **Two copies of the game no longer overwrite each other's logs.** Running two
  instances from one folder — Unity's virtual players, or two copies of the exe
  side by side — meant both opened the same `Console.log`, the second failed,
  and that instance recorded nothing for the rest of the session. Silently, and
  usually it was the instance you needed. Each process now claims its own file
  names, and in multiplayer the match folder says which player wrote it
  (`..._host`, `..._client1`) instead of leaving you to guess from timestamps.
- **New `Lockstep.log`, written so that two players in sync produce identical
  files.** One line per tick listing the orders that tick carried, and a state
  checksum every second. Diff the host's copy against the other player's and the
  first line that differs is the exact moment the two games parted company —
  which turns "it desynced somewhere" into a specific tick you can reproduce.
  It deliberately contains nothing machine-specific: no clock times, no frame
  counts, no ping, because two perfectly healthy games differ on all of those
  and any one of them would bury the real difference.
- **The kind of line that differs first says where to look.** Orders differ:
  something did not reach the other player. Orders match but checksums differ:
  the simulation itself is not deterministic. The header differs: the two games
  disagreed before the first tick.
- **A desync now writes down what forked**, not just that something did — every
  unit and building sorted by id, with health, position and owner, plus every
  faction's bank, from both players. `Lockstep.log` says when; that says what.
- **Waiting, stalls and connection quality are recorded as they happen** — which
  player is being waited on, how long, their last confirmed tick, and every time
  the input delay changes with measured latency.
- **The match header records the multiplayer identity**: role, instance, tick
  rate, pathfinding grid size and the build fingerprint. A log sent back on its
  own now says which player wrote it and whether the two agreed about the match
  before a single tick ran.

  *Full instructions for running the two-instance test and reading the diff are
  in `docs/Multiplayer_LAN_Readiness.md` §7.*

### Fixed — teams and allied fire *(from the first session)*

- **Twin Spans played as a six-way free-for-all.** The map is drawn as 3v3 —
  three warbands to a shore, two shared crossings between them — but it never
  told the lobby who was on whose side, and a slot with no team assigned is
  read as "allied with nobody, hostile to everyone". So the three players
  sharing a shore were enemies, and neighbours spent the match fighting each
  other instead of the far bank. Maps now carry a team layout attached to each
  start position, and the lobby applies it — including when you move a player
  to a different start, because which shore you are on is what decides your
  side. Setting a team yourself still wins and is never written over.
- **Allies damaged each other anyway, a point at a time.** Every damage
  calculation ends by flooring the result to "at least 1", and that floor sat
  *after* the no-friendly-fire check — so the check's zero was turned straight
  back into a 1. Melee swings chipped teammates and arrows chipped their
  buildings, quietly, all match. The floor now applies to genuine hits only,
  and a melee unit that somehow ends up locked onto an ally drops the target
  instead of swinging at it.

### Fixed — Twin Spans: the river and the bridges *(from the first session)*

- **Nothing on Twin Spans blocked movement — not the river, not the crags — so
  the bridges were decorative.** The terrain map that tells pathing where water
  and cliffs are was being read one step too early: it is allocated before it
  is filled, and "allocated" was being taken as "ready". Pathing built itself
  from a blank one, cached the result, and never looked again, so for the whole
  match the map had no water and no cliffs in it. The crossings went with them:
  a bridge only becomes a crossing where the ground it spans is impassable, so
  with a walkable riverbed underneath there was no deck for the planner to
  route over and units simply waded through the river. Pathing now waits for
  the terrain map to be finished, re-reads it if it is ever rebuilt, and
  refuses to cache a result that found no blocked ground at all.
- **The one number that would have caught this was meaningless.** The log
  reported "1412 of 125316 cells impassable", which reads like real terrain. It
  was in fact 1412 cells of empty margin around the edge of the map and nothing
  else — the river and the crags contributed zero. That count is now split into
  real terrain versus off-map margin, broken down by rule (water, painted
  no-go, slope) and by bridge (deck, ramp), and a map that ships water, painted
  cliffs or a bridge yet produces a terrain map blocking nothing now logs a
  loud error naming what is missing.

### Added

- **Selection rings.** Selected units get a ground ring in their owner's
  colour, matching the Gatherer's Hut circle.
- **Rally a Hall onto a resource.** Right-click a resource node with a Hall
  selected and workers trained there walk out and start gathering it with no
  further input. The rally marker turns green and sits on the node.
- **Use Celestar can be aimed.** The Scout's reveal now puts up a targeting
  ring so you choose the patch of map to uncover, like the sect powers.
  Previously it revealed around the Scout — ground it could already see.
- **Veilstone patches have their own ground.** Ore-bearing ground gets its own
  terrain texture rather than borrowing the curse's.
  *(Run **Waning Border > Game Data > Wire Influence Layers** once to author
  the layer; until then the patch simply keeps the terrain's own texture.)*

### Changed

- **The Gatherer's Hut influence bonus is +50%**, down from double.
- **Cursed ground and ground held by a rival now cut a hut's yield.** Both the
  percentage shown while placing and the income it earns. Allied ground still
  counts, so a shared border does not starve both partners. Ground you cannot
  walk on already counted and still does.
- **Resource patches are solid blocks.** Patch layout scattered nodes on a
  lattice that had nothing to do with the build grid, then snapped each to a
  tile — which both tore holes in patches and collapsed two nodes onto one
  tile, so the node count you authored was never the node count you got.
- **Resource nodes have tile-sized click boxes.** A cube filling the tile the
  node occupies, instead of a box shrink-wrapped to the art that under-filled
  the tile in some directions and overhung it in others.
- **Unit facing indicators removed.** A unit's facing is already readable from
  its model.
- **The Select Culture button is twice the size** and no longer overlaps the
  match clock at any resolution.
- **Skirmish players arrive with a start position already picked.** The lobby
  used to open with every slot unassigned and the map preview blank, so you
  either placed eight players by hand or let the spawn layout choose without
  telling you. Everyone is now seated at a random free start when the lobby
  opens, and anyone left without one — a slot you just added, or a player
  evicted when someone took their spot — picks up another. A start you chose
  yourself is never moved, and clicking your own start still releases it.
- **The executable is now `The Waning Border`**, without the version suffix.

### Tutorial

- **It no longer teaches controls that do not exist.** It taught Q/E to rotate
  the camera and R/F to tilt it; both are deliberately disabled — the camera
  holds a fixed angle and only pans and zooms.
- **The tutorial AI no longer attacks.** It still builds, researches and
  defends itself, so the combat lesson still has a real opponent that fights
  back — it just will not send a wave at a first-time player who is three
  chapters away from being told how to build soldiers.

### Known issues

- **Solid ore patches are mined from the outside in.** Nodes buried inside a
  patch have nowhere to stand next to them until the outer ring is exhausted.
  This resolves itself as the patch is worked.
- **Huts placed flush still block movement.** The gap between buildings comes
  from each one giving up its outer metre, which a one-tile building cannot
  afford without vanishing entirely.
- **Crowds still queue at a one-unit-wide gap.** Units are not written into the
  pathing grid by design, so nothing tells a unit the gap is already taken.
- **Selecting several workers and clicking one node sends them all to that
  node** rather than spreading them across the patch. Surplus workers wait
  beside it.
- **The AI still pulls its own villagers off mining to build.** The equivalent
  player-side behaviour is fixed; changing it for the AI shifts its economy
  balance and was left for a tuning pass.
- **Units clip about a metre into building edges**, the cost of the lane
  between adjacent buildings.
- **What emptied the Twin Spans terrain map has not been reproduced.** The
  guards above stop the symptom — pathing will no longer cache an empty one —
  but the reason it came out empty in that session is not yet pinned down. If
  it recurs, the new per-rule log line and the error it now raises will name
  the rule that dropped out; please send the log.
- **Only Twin Spans ships a team layout.** Every other map still opens as a
  free-for-all and needs teams set by hand on the lobby chips.
- **Teams are a skirmish feature only for now.** The multiplayer lobby has its
  own team chips but does not apply a map's layout.
- **Multiplayer has not been played on two machines since the rebuild.** It
  compiles and the reasoning is recorded, but nobody has watched it run. Expect
  to find things.
- **Three player actions still do not reach the other player**: vault
  deposit/withdraw, bazaar pack/unpack, and placing wall hubs and segments.
- **Vault and bank spending is still paid by whoever clicked**, which is the
  established model and correct as long as every purchase replicates — the
  match now checksums all eight banks, so a divergence is reported rather than
  discovered an hour later.

---

## [0.0.2] — 2026-08-13

Second alpha build. First round of fixes from Duarte's play session on 0.1.0,
plus the build grid and teams work.

> **Version numbering restarted.** The previous build reported `0.1.0`; this
> one is `0.0.2`. A higher-looking number in an older log is expected — use the
> timestamp in `Summary.txt` to tell builds apart.

### Fixed — reported by testers

- **Siege engines could not move.** Any ranged unit that acquired a target
  inside its firing band had its move order treated as already arrived and
  discarded. On a catapult that band is 5.5–30 m, so it froze the moment
  anything came near and refused to continue. An explicit move order now
  outranks auto-acquisition.
- **Units walked and shot over mountains.** From the second match of a session,
  the terrain passability mask was rebuilt empty and never refilled, so
  mountains and water stopped blocking. Introduced in this cycle; fixed.
- **Builders only built the last of several queued buildings.** Build
  assignments overwrote one another instead of queueing, so placing several
  buildings in a row silently dropped all but the last — resources spent,
  foundations never touched. It looked distance-dependent only because nearby
  sites were accidentally rescued by the auto-chain. Builds now queue in order.
- **Workers could not be box-selected when troops were in the drag.** The
  "military wins" rule is intentional but had no override, making workers
  unreachable if a single soldier stood in the rectangle. **Hold Ctrl (or Alt)
  to select everything in the box.**
- **Queued research showed nothing.** The queue existed but was never drawn,
  and the chips were hidden entirely for research-only buildings like the Hall
  — so queueing a tech removed its button and showed nothing in return.
- **Arrows flew past the wall they were aimed at.** Range is measured to a
  building's edge but shots were fired at its centre. Arrows now aim at the
  surface facing the shooter.
- **Archers out-ranged crossbowmen.** The Archer sat at the bottom of the
  ranged ladder with the weakest damage and the second-longest reach, beating
  the unit meant to counter it — and it shot 5 m beyond its own line of sight.
  The whole ladder is retuned; see *Changed*.
- **Workers gathered ore from several body-widths away.** Reach is measured to
  a node's surface and was set to 5 m, so a worker mined from about 6 m off
  centre. Now 2.5 m.
- **Right-click grabbed resource nodes from well outside them**, for two
  separate reasons: the iron deposit's click box was 3×3 m around a node
  occupying 2×2 m, and the veilsteel node rendered at roughly three times the
  ground it occupies — and its click target is fitted to the visual.
- **Unexplored ground was not fully hidden.** Fog sat at 98% opacity and the
  camera cleared to the skybox, so terrain showed faintly through unexplored
  black and the map's outline was silhouetted against the sky. Unexplored fog
  is now pure black, and so is the backdrop — the map edge is indistinguishable
  from unexplored ground.
- **The minimap revealed territory nobody had scouted.** Player and curse
  influence were drawn *over* the fog layer with no visibility test, showing
  the shape of every faction's territory and the curse's spread across
  unexplored ground. Explored territory still shows, as a remembered building
  does.
- **The main menu Quit button did nothing.** It had no handler at all.

### Fixed — stability

- **The second match of a session was unplayable.** Ending a match wiped ECS
  entities that Unity Physics and the navigation stack never rebuilt, so
  physics and pathfinding were dead from match two onward and the log filled
  with singleton errors every frame. Twelve separate sites fixed, along with
  the allocation leaks and stale cached handles they exposed.
- **Unit stats had drifted between their ScriptableObject and the JSON
  fallback** — the Archer read 8 damage / 20 sight from the SO the game
  actually uses, and 17 / 30 from the JSON it would fall back to. Realigned, so
  a failed SO load can no longer silently change the game's balance.
- **Chosen start positions could spawn the wrong player at the wrong corner.**
  The lobby's map preview and the spawner disagreed about which start was
  which. Both now share one ordering, and a start resolves by marker identity
  rather than array position. *(Requires re-baking each map's MapInfo.)*

### Added

- **The 2 m build grid.** Buildings snap to it, their outlines show the exact
  cells they occupy, and faint white grid marks appear under the cursor while
  placing. Resource nodes, curse nodes and trees each occupy one cell and block
  movement.
- **Teams.** Each lobby slot can join a team or fight alone. Allies share line
  of sight, cannot damage each other by any route, and can heal and buff each
  other — but the same effect applied twice does not stack. A match ends when
  one team remains. No team is the default, so an untouched lobby is unchanged.
- **Colour picker.** The roster swatch opens a grid of all twelve colours;
  colours already taken are locked out. It used to cycle one step per click.
- **Start-position picker.** Click a player in the roster, then click a start
  on the map preview to place them there — for arranging allies. Unassigned
  players spawn automatically as before.
- **Per-match logs.** Every match writes a timestamped folder into `logs`
  beside the executable: outcome and error counts (`Summary.txt`), all
  warnings and crashes with stack traces (`Console.log`), stutters
  (`Perf.log`), each AI's decisions, and a full economy timeline
  (`Timeline.csv`). Nothing is deleted between matches, and nothing is
  uploaded anywhere.

### Changed

- **Building footprints doubled.** Buildings read far too small against units
  and terrain. Spacing, the starting base layout and the chapel ring moved with
  them. Note this shifts effective ranged range against buildings outward by
  half the size increase.
- **Lobby roster rebuilt as eight fixed slots** with aligned columns —
  colour, name, team, difficulty, strategy. Team shows a number or `-`.
  "+ ADD PLAYER" now sits on the first empty row instead of a separate button.
  Keyboard and gamepad navigation skips hidden entries.
- **Wall hubs snap to the grid; the curtain between them stays freeform**, so
  walls still follow the ground at any angle.
- **Ranged ladder rebalanced.** Two rules now hold across it, and any new
  ranged unit must respect both: **range never exceeds line of sight**, and
  **range rises with the ladder**, so a higher tier is never a sidegrade.

  | Unit | Damage | Range | Min range | Line of sight |
  |---|---|---|---|---|
  | Archer | 8 | 10 | 2 | 10 |
  | Crossbowman | 18 | 12 | 3 | 12 |
  | Longbowman | 25 | 20 | 8 | 20 |

  Ranges are roughly halved. Combined with the doubled footprints, expect
  engagements to sit much closer to buildings than in 0.1.0.
- **Menu trimmed for the alpha.** Campaign, Multiplayer, Scenarios and Load
  Game are hidden in the shipped build — none are ready. The editor keeps the
  full menu.

### Known issues

- **First load takes about 6 seconds**, most of it in one frame. Not yet
  investigated.
- **Siege engines can wedge in narrow gaps between buildings.** Pathfinding
  ignores unit size, and doubling the footprints made the gaps tighter. Partly
  masked by the movement fix above; the real fix is a larger piece of work.
- **The Trebuchet still outranges its own sight** (38 range / 30 line of
  sight) — the last unit that does, left for a siege balance pass.
- **Ritual markers and glow pickups still show through fog on the minimap.**
  That is deliberate: the design makes waking a well "legible to everyone" and
  the Shardroot carrier visible to all. An active ritual therefore reveals the
  well it is running on.
- **Runai and Feraldis are locked.** Alanthor only.
- There is no save system; "Load Game" is hidden rather than disabled.

---

## [0.1.0] — 2026-08-13

First alpha build handed to testers.

Reported by Duarte and addressed in 0.0.2: builders abandoning queued builds,
workers unselectable in mixed drags, research queue invisible, siege engines
stuck, units crossing mountains.
