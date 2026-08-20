# UI Review and Redesign Plan

Date: 2026-07-05. Requested after the verdict "not readable at all and it
seems cumbersome". This document is the full audit of every UI surface in
the game plus the consolidation/redesign plan.

---

## Part 1 — Review: why the UI feels cumbersome

### 1.1 The root cause: four UI technologies render in parallel

| Stack | What it draws today | Files |
|-------|--------------------|-------|
| IMGUI (`OnGUI`, ~30 components) | Main menu, skirmish/MP lobbies, options, loading screen, pause menu, culture popup, sect popup, tech tree, religion strip, entity info/action panels, build panel, spell panel, notifications, post-game stats, floating texts | `UI/Menus/*`, `UI/Panels/*`, `UI/HUD/*` |
| UI Toolkit ("jade" UXML regions) | Resources, objectives, selection, actions, culture popup — auto-mounts in every match and *suspends* 12 IMGUI panels by type-name reflection | `UI/GameplayUIController.cs`, `UI/Regions/*`, `Assets/UI/Resources/GameplayHUD.uxml` |
| Web HUD (CEF/React, separate OS process) | Resources, objectives, selection, action grid, sect rail, pause menu, minimap frame, culture picker | `UI/Web/HudBridge.cs` (~2400 lines), `HudFrontend/src/*` |
| uGUI | Minimap canvas (RawImage), web HUD host canvas | `World/Minimap/MinimapRenderer.cs` |

Consequences:

- **Three implementations of the same panels.** The resource bar, selection
  panel, action grid, and culture popup each exist three times. Which one the
  player sees depends on mount order, a reflection-based suspend list, and
  canvas sort orders — not on design intent.
- **Three visual languages at once.** Gold-on-navy IMGUI, jade UXML, and
  jade-with-filigree React (Cinzel serif) can all be on screen together: an
  IMGUI sect popup floats over the React HUD next to a uGUI minimap.
- **Three "is the pointer over UI?" flags** (`EntityInfoPanel.IsPointerOver`,
  `GameplayUIController.IsPointerOverHUD`,
  `HudWebController.IsPointerOverWebHud`) that input code must consult; each
  new panel must remember to hook one of them or clicks leak into the world.
- **Duplicated flows.** The pause menu exists in React (`Menu.jsx`) *and*
  IMGUI (`InGameMenuPanel`), and the web menu routes some items back into the
  IMGUI implementation.

### 1.2 Readability problems (specific, per surface)

- **Type is too small everywhere in IMGUI.** Body text is 10–11 px
  (`SectChoicePopup` body = 11 px, lever buttons = 11 px, keybind rows =
  12 px). At 1440p+ these are unreadable. There is no shared type scale.
- **Information crammed into tiles.** The ReligionHUD strip packs sect name,
  passive icon, three tier buttons, and the Glow toggle into a 72×56 px tile;
  the real information lives in hover tooltips, so nothing is scannable.
- **SectChoicePopup** shows lore + 5 description sections + costs + buttons in
  a fixed 620×340 modal; sections get 2 lines each at 11 px.
- **The sect rail hexes** are 28×25 px with the skill name in a hover flyout;
  cooldown countdowns are appended to labels that don't fit.
- **Cost display is inconsistent:** sometimes inline text ("120s 40i"),
  sometimes colored green/red, sometimes tooltip-only, with icons only in
  some panels.
- **Feedback is scattered:** IMGUI toast notifications top-center, React
  panel state changes, floating damage numbers — no shared visual language
  for "something happened".
- **Menus:** a single 360 px column of 56 px buttons with 2 px spacing and
  italic hint lines; options and lobbies are raw IMGUI forms with default
  Unity skin controls.

### 1.3 Interaction/cumbersomeness problems

- Sect management is split across three places: adopt at the temple strip /
  popup, cast from the rail, units from the temple panel. (Recent redesigns
  reduced this, but the surfaces still live in different stacks.)
- Modal input blocking is ad-hoc (`Event.current.Use()` on mouse events,
  full-screen invisible boxes).
- The web HUD holds a machine-wide mutex — only one game instance per
  machine gets a HUD, which breaks local multiplayer testing.
- CEF adds a separate process (memory + startup time) and a Node/esbuild
  build step for any UI change.

---

## Part 2 — The Synty UI pack: NOT in the project

Searched (2026-07-05): the entire project tree, the Unity Asset Store cache
(`%APPDATA%/Unity/Asset Store-5.x`), and `Downloads`. **No Synty UI/INTERFACE
package is present anywhere.** The only Synty content is character-model
support code (`SyntyTeamColorRecolor.cs`). Synty sells its INTERFACE packs on
syntystore.com (not the Unity Asset Store), so the purchase is likely a
`.unitypackage` on another machine or account download page.

**Action required (owner):** locate the pack (Synty account → Downloads) and
import it, or drop the `.unitypackage` path in chat. The plan below is
skin-agnostic — Synty INTERFACE packs ship PNG sprite atlases + uGUI demo
prefabs, and the sprites slot directly into the design system in Phase 0.

Already available as interim/skin candidates:

- `Assets/Layer Lab/GUI Pro-MinimalGame` — ~3,700 sprites/prefabs, dark
  theme, imported and unused.
- SlimUI "3D Modern Menu UI" — in the Asset Store cache, not imported.

---

## Part 3 — Redesign plan

### 3.1 Target: ONE stack — Unity UI Toolkit

Recommendation: consolidate everything on **UI Toolkit** and retire the other
three stacks. Rationale:

- The migration already exists in embryo (GameplayUIController phases 2a–3a
  are live) — this finishes a started journey rather than starting a fourth.
- Native: no CEF process, no machine mutex, no Node build step, one input
  system (`pickingMode`/`focusable` replace all three pointer-over flags).
- USS styling gives web-grade control over typography and theming, which is
  exactly the readability lever needed, and Synty sprites drop in as
  `background-image` 9-slices.
- Dynamic RTS panels (action grids rebuilt per selection) are straightforward
  in code-driven UXML.

The considered alternative — consolidating on the React web HUD — was
rejected: it is the most feature-complete surface today, but it costs a
separate browser process per client, cannot run two instances per machine
(mutex), needs the esbuild pipeline for every tweak, and cannot use Synty's
uGUI prefabs at all. It stays live during the migration so the game is
always playable, and is deleted last.

### 3.2 Design system (Phase 0 output, enforced everywhere)

- **Type scale (px at 1080p, scaled by PanelSettings):** 12 caption / 14 body
  / 16 button / 20 panel header / 28 title. Nothing below 12. One display
  font (titles), one text font (everything else).
- **Palette:** derived from the Synty skin once imported (fallback: current
  jade + parchment set, formalized as USS variables). All colors as USS
  custom properties in one `theme.uss`.
- **Spacing:** 4 px base grid; panels padded 12/16; button min-height 32.
- **Tooltip standard:** every actionable element gets the same tooltip card —
  name, cost row (icons), one-sentence effect, hotkey. One implementation.
- **Cost row standard:** icon + number, red when unaffordable — same widget
  in shop, training, adoption, upgrade contexts.
- **HUD layout (classic RTS grammar):** top bar = resources, age, menu;
  bottom-left = minimap; bottom-center = selection card(s); bottom-right =
  command/action grid; right rail = sects; top-center = notifications and
  objectives. No overlap with world interaction space.

### 3.3 Phases

**Phase 0 — Import + tokens (small).** Import Synty pack; slice sprites;
author `theme.uss` with tokens above; build the shared Tooltip and CostRow
controls.

**Phase 1 — Stop the overlap (small, immediate relief).** Add a single
`UIStackAuthority` that guarantees exactly one in-game HUD stack is active:
while the web HUD is the shipped HUD, GameplayUIController does not mount its
duplicate regions, and every IMGUI panel that has a web equivalent stays
suspended (today both can appear depending on mount order). One flag flips
the authority to UI Toolkit per scene as later phases land.

**Phase 2 — Menus in UI Toolkit (medium, biggest visible win).** Rebuild
MainMenu, Skirmish lobby, Multiplayer lobby, Options, Loading screen with the
Synty skin. These are IMGUI-only today (worst readability), self-contained,
and touch no gameplay code.

**Phase 3 — In-game HUD parity in UI Toolkit (large).** Port region by
region, reusing the extractor layer (`EntityExtractors`, the same data source
HudBridge uses): resource bar → selection card → action grid (tooltips,
hotkeys, queue) → build placement panel → sect rail (tiered actives, adopt
flow) → minimap frame. Each region ships behind the Phase 1 authority flag so
the web HUD keeps covering anything not yet ported.

**Phase 4 — Modals (medium).** Culture picker, sect picker, tech tree, pause
menu (incl. keybinds), post-game stats — one modal framework (backdrop, focus
trap, Escape handling) instead of five hand-rolled ones.

**Phase 5 — Feedback layer (small).** Notifications, damage numbers, floating
health bars, rally/telegraph visuals restyled to the same tokens.

**Phase 6 — Demolition (medium, high-risk, last).** Delete the IMGUI panels,
the React frontend + UWB/CEF dependency + HudBridge, and the uGUI minimap
canvas. Collapse the three pointer-over flags into UI Toolkit picking. Update
GAME_MANUAL.md screenshots.

### 3.4 Implementation status (2026-07-05, same day)

Executed in one pass (skin = Layer Lab GUI Pro-MinimalGame per owner's
correction — the pack referred to as "Synty" is this one):

- **Phase 0 DONE** — `Assets/UI/Resources/TwbUiStyles.uss` (tokens, 12px
  floor, Layer Lab white-base sprites tinted jade/gold) +
  `Assets/Scripts/UI/Toolkit/TwbUi.cs` (document setup, Panel, Btn, MenuRow,
  Cycle, Toggle, Modal, shared Tooltip, CostRow).
- **Phase 2 DONE** — `UI/Menus/Toolkit/`: MenuToolkit shell (+ auto-mount
  bootstrap that disables the IMGUI menu), MainMenuScreen, ScenariosScreen,
  OptionsScreen (same PlayerPrefs keys), SkirmishLobbyScreen (all 3 tabs),
  MultiplayerLobbyScreen (full UDP lobby protocol port).
  *Pending: LoadingScreen still IMGUI.*
- **Phase 3 DONE (regions)** — existing Resources/Objectives/Selection/
  Actions/CulturePopup regions kept; new `SectRailRegion` (slots, adoption,
  tiered actives + ground-ring casting, Glow toggle) and uGUI minimap left
  live for this stack.
- **Phase 4 PARTIAL** — `PauseMenuModal` (mirrors InGameMenuPanel.IsOpen so
  ESC keeps working) and `SectPickerModal` (full adopt flow) shipped.
  *Pending: TechTreePanel (research) and PostGameStatsUI remain IMGUI (the
  stats screen is intentionally un-suspended so match end still works).*
- **Phase 5 DONE** — `NotificationsRegion` (Layer Lab toasts fed by a new
  `PlayerNotificationSystem.Emitted` event).
- **Phase 1/6** — `GameSettings.UseWebHud` now defaults **false**: the UI
  Toolkit stack is the in-game HUD; GameplayUIController suspends the
  migrated IMGUI panels (and no longer hides the minimap or the post-game
  stats). The web HUD and IMGUI code remain in the tree as fallback until
  the owner playtests the new stack — **file deletion (Phase 6 demolition)
  is deliberately deferred to a follow-up after that playtest.**

### 3.4 Risks / notes

- The web HUD must stay functional until Phase 3 parity — never remove a
  surface before its replacement ships.
- The Synty pack's art direction (bold, flat) will read differently from the
  current filigree-jade React theme; Phase 0 should produce one mocked panel
  (selection card) for approval before mass-porting.
- Lockstep multiplayer: UI consolidation removes the CEF mutex issue, which
  currently blocks two-instance local testing.
- IMGUI deletions must respect the "Do Not Modify" note on
  `BuildCommandPannel.cs` naming until its logic is fully ported.
