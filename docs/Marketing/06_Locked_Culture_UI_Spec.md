# Locked-Culture UI Spec — Turning the Demo Constraint into a Marketing Hook

> How the age-up culture-choice screen presents the **two locked
> cultures** (Runai, Feraldis) in the demo build. The design treats
> these locked slots as marketing surface, not as missing UI — every
> player who reaches the age-up screen sees a 30-second pitch for the
> full game.

**Linked spec:** [05_Demo_Scope.md](05_Demo_Scope.md)
**Owner:** Luis Martins / Shardroot

---

## The big idea

When the demo player reaches age-up (~3-5 minutes into a match), they
see **three culture cards**. Alanthor is selectable. Runai and Feraldis
are **shown in full** — art, identity, mechanic teaser — but cannot be
clicked. The locked state is presented as **"coming in the full release,"
not as "missing."**

This is the demo's single highest-conversion marketing moment:
- The player is **already engaged** (they got to age-up)
- They are **already curious** (they just learned the game has three cultures)
- They are **two seconds away** from a wishlist click

The UI must make that click effortless.

---

## Visual mockup (ASCII layout)

```
+---------------------------------------------------------------+
|                                                               |
|             CHOOSE YOUR CULTURE — AGE OF DIVERGENCE           |
|                                                               |
|  +-----------------+ +-----------------+ +-----------------+  |
|  |                 | |                 | |                 |  |
|  |   [ALANTHOR ART]| | [RUNAI SILHOU.] | | [FERAL SILHOU.] |  |
|  |                 | |    (greyscale,  | |    (greyscale,  |  |
|  |   Defense       | |     dimmed)     | |     dimmed)     |  |
|  |                 | |                 | |                 |  |
|  |   "Deny the     | |  RUNAI          | |  FERALDIS       |  |
|  |    border with  | |  Economy        | |  Military       |  |
|  |    stone."      | |                 | |                 |  |
|  |                 | |  "Embody the    | |  "Prey on the   |  |
|  |   Walls.        | |   border."      | |   border."      |  |
|  |   Towers.       | |                 | |                 |  |
|  |   Discipline.   | |   COMING SOON   | |   COMING SOON   |  |
|  |                 | |                 | |                 |  |
|  |  [  CHOOSE  ]   | |  [ WISHLIST ]   | |  [ WISHLIST ]   |  |
|  |                 | |                 | |                 |  |
|  +-----------------+ +-----------------+ +-----------------+  |
|                                                               |
|     The full game features three cultures and twelve sects.   |
|         [    WISHLIST ON STEAM    ]    [   JOIN DISCORD   ]   |
|                                                               |
+---------------------------------------------------------------+
```

---

## Card states — visual treatment

### Alanthor card (selectable)

- **Full color** — warm grey limestone + sage green accents
- Hero illustration of an Alanthor Town Hall behind walls
- Hover state: subtle stone-grit dust particle effect, sage banner
  ripples
- Tagline: *"Deny the border with stone."*
- Three bullet points:
  - **Walls.** Hub-and-segment fortifications that auto-form between
    hubs.
  - **Towers.** Convert any wall instance or place stand-alone.
  - **Discipline.** Closed compartments generate supplies. Stand still,
    grow strong.
- CTA button: **CHOOSE ALANTHOR** (large, primary)

### Runai card (locked)

- **Desaturated to grey-blue** with cyan accent kept visible at low
  saturation — so the cyan identity reads, but the card clearly differs
  from Alanthor
- Hero illustration **as silhouette only** — a caravan in transit
  against a desert horizon, the wagon silhouette dimly visible
- Hover state: gentle wind-and-sand particle drift across the card
  (subtle, ~30% opacity)
- Tagline: *"Embody the border."*
- Three teaser bullets:
  - **No walls.** The lane network IS the defense.
  - **Caravans in transit.** Wagons earn supplies *while moving*.
  - **Trader-warriors.** Lanes auto-spawn patrolling defenders.
- Locked overlay: subtle banner across the bottom-third:
  > **COMING IN THE FULL RELEASE**
- CTA button: **WISHLIST TO PLAY** (secondary style, links to Steam page)

### Feraldis card (locked)

- **Desaturated to grey-red** with crimson accent kept visible at low
  saturation
- Hero illustration **as silhouette only** — a Feraldis raider
  silhouetted against a burning hut at dusk
- Hover state: faint ember flicker, distant howl ambient SFX
- Tagline: *"Prey on the border."*
- Three teaser bullets:
  - **Damage is income.** Kills drip supplies; raid pressure pays out.
  - **Houses spawn raiders.** Every house build sends free aggressors.
  - **Veilsteel Frenzy.** Berserker-stim mechanic for late-game power.
- Locked overlay: **COMING IN THE FULL RELEASE**
- CTA button: **WISHLIST TO PLAY**

---

## Footer / tray

Below the three cards, a discreet tray:

```
The full game features three cultures and twelve sects.
[ WISHLIST ON STEAM ] [ JOIN DISCORD ]
```

This is also visible on **win, loss, and end-of-demo screens** — every
exit from the demo passes through this tray once.

---

## Click interactions

| Click target | Behavior |
|--------------|----------|
| Alanthor "CHOOSE" | Standard culture commit — game continues with Alanthor |
| Alanthor card body | Selects (highlights) but doesn't commit; reveals more bullet details |
| Runai/Feraldis card body | Selects (highlights) but doesn't commit; reveals more bullet details + plays a 5-second culture-tease video clip if one exists |
| Runai/Feraldis "WISHLIST" | Opens Steam overlay → Steam page → wishlist add (NOT browser; Steam overlay is in-app) |
| Footer "WISHLIST" | Same as above |
| Footer "JOIN DISCORD" | Opens Discord invite via Steam overlay or default browser |

**Steam overlay note:** Steam's `steam://` URL scheme handles
`steam://store/<appid>` cleanly. From a Unity game, the Steamworks .NET
SDK has `SteamFriends.ActivateGameOverlayToStore()`. Use that — not
`Application.OpenURL()` to a Steam URL, which kicks players to the
browser unnecessarily.

---

## Copywriting principles

1. **Never apologize.** Words like "unfortunately," "limited demo,"
   "not available yet" are banned from this screen. The demo is the
   *gift*, not the *constraint*.
2. **Concrete teasers, not vague promises.** "Caravans earn supplies
   while moving" reads more compelling than "unique economy mechanics."
3. **Same tone across all three cards.** Alanthor's "Deny the border
   with stone," Runai's "Embody the border," Feraldis's "Prey on the
   border" — the parallel construction makes the triangle legible in
   one read.
4. **No defensive language.** "Coming in the full release" is the
   commitment, not "we're working on it" or "if we have time."
5. **The wishlist button is a positive action**, not a consolation. The
   button text is "WISHLIST TO PLAY" — they wishlist *because* they
   want to play, not *because* they can't.

---

## Animation / motion

- **Card entrance:** cards slide in from below over ~400ms, staggered
  by 80ms. Alanthor first, then Runai, then Feraldis. The stagger
  emphasizes the triangle is meant to be read as three distinct
  identities.
- **Locked-card pulse:** subtle 4-second sine pulse on the
  COMING SOON banner — slow enough to not be irritating, present
  enough to draw the eye when the player's looking around.
- **Hover state:** card lifts ~4px with subtle drop shadow, plus the
  particle/ambient effect per faction.
- **Wishlist button:** breathes very faintly (95-100% brightness, 3s
  cycle) on the locked cards to signal it's an active CTA.
- **Click feedback:** standard button-press visual.

---

## Audio cues

- **Screen open:** a single low brass-and-strings stinger (3 seconds)
  evoking a "moment of decision."
- **Card hover:** faction-specific 1-second ambient bed:
  - Alanthor: distant bell, stone-on-stone clink
  - Runai: wind, fluttering canvas, a single oud note
  - Feraldis: distant horn, raven caw, ember crackle
- **Wishlist click:** a satisfying *thunk* (vault closing /
  parchment-stamp metaphor). Player should feel the action *landed*.

---

## Accessibility

- All click targets ≥48×48px (mobile-friendly even though this is
  desktop)
- Colorblind-safe: cards distinguished by silhouette and label as well
  as color
- Screen-reader: ARIA labels for the locked cards explain "Runai
  culture, coming in the full release, click to wishlist on Steam"
- Keyboard navigation: Tab cycles cards, Enter to commit (Alanthor) or
  open Steam page (locked)
- "Reduce motion" setting from OS disables the breathing/pulse
  animations

---

## Localization

Strings to externalize from day 1 (using Unity's Localization package
or a simple JSON/CSV approach):

```
LOC.AGE_UP_TITLE = "Choose your culture — Age of divergence"
LOC.CULTURE_ALANTHOR_TAGLINE = "Deny the border with stone."
LOC.CULTURE_RUNAI_TAGLINE = "Embody the border."
LOC.CULTURE_FERALDIS_TAGLINE = "Prey on the border."
LOC.COMING_SOON_BANNER = "Coming in the full release"
LOC.WISHLIST_TO_PLAY = "Wishlist to play"
LOC.CHOOSE_ALANTHOR = "Choose Alanthor"
LOC.FOOTER_TRAY = "The full game features three cultures and twelve sects."
LOC.WISHLIST_ON_STEAM = "Wishlist on Steam"
LOC.JOIN_DISCORD = "Join Discord"
```

English-only for demo; structure them as keys now so EU localization
later is a translation pass, not a refactor.

---

## Code touchpoints

### New / modified

- `Assets/Scripts/Core/Settings/DemoConfig.cs` — new file. Single bool
  `DemoMode` plus serialized strings/IDs for "locked" culture state.
- `Assets/Scripts/UI/Panels/CultureChoicePanel.cs` (or wherever the
  current age-up screen lives) — branches on `DemoConfig.DemoMode`:
  - Hide / disable non-Alanthor culture buttons
  - Show the locked-card variant for Runai / Feraldis
  - Render the wishlist tray footer
- Wishlist action: integrate Steamworks .NET SDK call. Steam App ID
  required — get one via Steam Direct ($100 one-time).

### No-touch (preserved for full-game restore)

- All Runai / Feraldis culture data structures, ScriptableObjects,
  prefabs — left in source. The `DemoMode` flag is the only behavioral
  difference. Setting `DemoMode = false` restores the full
  three-culture screen. This is the **single most important code
  discipline** for the demo build — no ripping out, only hiding.

---

## Variants for testing (post-launch decisions, not demo blockers)

Things to A/B if you ever do a Steam page A/B test (Valve permits this):

1. **Tagline punchiness** — "Deny / Embody / Prey on the border" vs
   simpler nouns ("Walls / Lanes / Raids")
2. **CTA copy** — "WISHLIST TO PLAY" vs "WISHLIST FOR LAUNCH" vs
   "ADD TO WISHLIST"
3. **Locked-card hover video presence** — does the 5-second tease
   actually help conversion, or does it interrupt the click decision?

Don't do these until you have 5k+ demo plays as a baseline.

---

## When to revisit this spec

- **When Runai is playable** — the Runai card becomes selectable;
  Feraldis stays locked with the same treatment. This screen probably
  becomes the centerpiece of "Chapter 2" marketing.
- **When Feraldis is playable** — all three cards selectable, locked
  treatment removed entirely. This screen becomes its own moment
  ("now choose freely").
- **After 1000 demo wishlist conversions** — review actual conversion
  metrics on the wishlist clicks; iterate on copy.

---

*Last updated: 2026-05-26. Owner: Luis Martins, Shardroot.*
