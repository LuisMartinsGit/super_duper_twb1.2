# Demo Scope — The Waning Border (Alanthor Demo)

> Single source of truth for what ships in the free Steam demo. When
> design, art, code, or marketing disagree about whether something is
> "in scope," this file wins. Update here first, propagate to
> sub-domains.

**Demo type:** Free permanent demo on Steam (wishlist funnel)
**Build target:** Single playable culture (Alanthor) + The Border + 6 of 12 sects
**Status (2026-05-26):** Scope decision committed. Cut list and content checklist below.
**Owner:** Luis Martins / Shardroot

---

## North-star pitch (one sentence)

> *Play the demo as Alanthor — defensive wall-builders with BFME2-style
> hub-and-segment fortifications — survive the The Border, and
> wishlist to play the other two cultures (Runai, Feraldis) on launch.*

This frames the demo as a **complete experience of one culture**, not
"a third of a game with the other two stripped out." Tone matters: every
locked-faction touchpoint must read "preview of what's coming," never
"missing content."

---

## What's IN the demo

### Cultures
- **Alanthor only** — player-playable
- **AI also plays Alanthor** — mirror matches. Cleanest scope; the
  The Border provides PvE variety, so matches don't feel
  homogeneous.

### Mechanics (all of these ship)
- Age 0 (full — workers, scouts, basic Hall, Barracks, Archery Range,
  Houses, Gatherer's Huts, the three choice buildings)
- Alanthor age-up (Hall→Town Hall, Barracks→Garrison,
  Archery→Practice Range, Hut→Wall Hub OR Watch Tower choice per hut)
- **BFME2 hub-and-segment wall system** (full — hubs, auto-segments,
  gates by conversion, towers by conversion, cascade-on-hub-death)
- **Alanthor-unique buildings** — Royal Stable, Siege Yard, Smelter,
  Crucible
- **All three Age 0 choice buildings** (Vault of Almiérra, Shrine of
  Ridan, Fiendstone Keep) — these are pre-culture, so they ship
  regardless
- **The Border PvE layer** (full — border nodes, border creatures,
  border-ground spread, Glow drops on cleanse)
- **Per-battalion upgrade system** for Alanthor units (Stone → Iron →
  Veilstone → Glow weapons + Stone-tipped → Glow-tipped arrows)
- **Petriarchy / sect system** — but only 6 of 12 sects (see roster below)
- Combat, damage formula, line of sight, fog of war
- Saving / loading single-player matches
- Tutorial / in-game tooltips (minimum viable, see UI section)
- Win conditions: standard RTS — eliminate opponent OR survive N
  minutes against border waves (optional secondary win condition adds
  PvE flavor)

### Sects in the demo — 6 of 12 (balanced across clusters)

The full game ships with 12 sects across 3 clusters. The demo includes
**2 sects per cluster**, chosen for **archetype clarity in mirror
matches**:

| # | Sect | Cluster | RP cost for Alanthor* | Archetype |
|---|------|---------|------------------------|-----------|
| 1 | **Fortitude** | Alanthor | 2 RP | The turtle — wall/tower HP, Bulwark active |
| 2 | **Renewal** | Alanthor | 2 RP | The healer — auto-repair buildings, Heal Circle |
| 3 | **Witness** | Runaii | 3 RP | The seer — scout vision, Reveal Circle |
| 4 | **Veneration** | Runaii | 3 RP | The snowball — kill-stack damage buff, Litany |
| 5 | **War** | Feraldis | 3 RP | The rush — cheaper/faster military, Bloodfury |
| 6 | **Ruin** | Feraldis | 3 RP | The wall-breaker — anti-building damage, Profane Strike |

*Same-cluster sects cost 2 RP, cross-cluster cost 3 RP, per
[SectConfig.cs](../../Assets/Scripts/Economy/SectConfig.cs).

**Why these 6 specifically:**

- **Fortitude + Renewal** showcase Alanthor's defensive identity at full
  power — the player gets a "double down on what you are" build.
- **Witness + Veneration** are the cross-cluster "what if you went the
  other direction" picks — Runai-flavored vision/snowball plays.
- **War + Ruin** are the **anti-Alanthor** picks. **Ruin in particular
  is critical**: it's the wall-breaker sect, and a mirror match where
  one player picks Ruin against another's Fortitude/wall stack creates
  the most legibly-asymmetric demo experience. *This is the
  marketing-screenshot match.*

**Sects cut from the demo** (6, to bring back at launch): Antiquity,
Reclamation, Silence, Justice, Ash, Wrath. These all have valid
mechanical effects but either require additional VFX work (Ash's
burning ground), reward harder-to-read playstyles (Silence's
stand-still buff), or feel niche for a first-impression demo.

### Maps in the demo
- **2 maps minimum** for replay value:
  - 1 small (1v1, ~5-10 min matches) — quick play
  - 1 medium (1v1, ~15-25 min matches) — full experience including
    age-up and Petriarchy
- Both maps should feature **at least one The Border node** for the
  PvE/Glow layer to be experienced. Medium map: 2-3 border nodes.
- Map art reuses the existing terrain system; no new biome art required.

### UI / UX
- Build / unit / tech panels (existing IMGUI, stylized — see art brief
  for chrome)
- Sect adoption UI (6 sect chapels visible inside the Temple)
- **Culture choice screen at age-up** — Alanthor selectable, Runai and
  Feraldis shown as "Coming soon" — see
  [06_Locked_Culture_UI_Spec.md](06_Locked_Culture_UI_Spec.md)
- Main menu with: Single-player vs AI, Tutorial, Settings, Wishlist
  Full Game (link out to Steam page), Join Discord
- Settings: graphics, audio, hotkey remapping (basic — full hotkey
  config can come later)

---

## What's OUT of the demo

| Cut | Why | When does it return? |
|-----|-----|----------------------|
| **Runai playable culture** | Bottom of the design completeness ladder (most spec gaps), wagon/lane/trader-warrior system not built | Full release |
| **Feraldis playable culture** | Same — Pillage economy, House-raider spawn, hut-raider transformation not built | Full release |
| **6 of 12 sects** | Reduces art/VFX work, UI complexity | Full release |
| **Multiplayer** | Not scoped — solo demo only | TBD, post-launch likely |
| **Map editor / mods** | Out of scope for first demo | TBD |
| **Skirmish customization** | Beyond basic difficulty + map pick | TBD |
| **Achievements** | Optional polish | Possibly at launch, not in demo |
| **Cloud saves** | Steam handles automatically; just enable in Steamworks |  |
| **Localization** | English only | EU language pass before launch |
| **Linux/Mac builds** | Windows only for demo | Decide at launch |
| **Religious unit (Scholar) full kit** | Ships if VFX for Purification ritual is ready; otherwise lock behind a tech that's "coming soon" | Demo if possible, else launch |

---

## Cut list — what to actively *remove or hide* in the demo build

This is the engineering task list. Each item is a one-line code/data
operation:

### Hide from UI / data
- [ ] **Age-up screen** — Runai and Feraldis culture cards greyed out,
      "Coming soon" overlay. Click → wishlist CTA + lore tease.
- [ ] **Sect adoption screen** — show only 6 chapels. Other 6 chapel
      slots: locked-icon + tooltip "Unlocked in full release."
- [ ] **Tech tree UI** — Runai/Feraldis branches hidden or shown as
      "Coming soon"
- [ ] **Main menu** — remove "Multiplayer" option if it exists today

### Strip from build
- [ ] **AI personality profiles** — remove Runai/Feraldis-specific AI
      logic from the build (or keep code, gate behind `DemoMode = true`
      flag in a single boot-time setting)
- [ ] **Runai/Feraldis assets** — exclude their building prefabs,
      texture variants, faction-tint variants from the Addressables /
      build pipeline. Keeps download size small.
- [ ] **Sect chapels for excluded 6 sects** — exclude their chapel
      prefabs and ScriptableObjects
- [ ] **Unused unit prefabs** — Runai Spearman, Skirmisher, Raider,
      Caravan, Escort, Acolyte; Feraldis Berserker, Hunter, Warboar
      Rider, Iconoclast, SiegeRam — all out

### Keep but disable in demo
- [ ] **Shared Age 0 systems** — keep all active; Age 0 IS the first
      ~5 minutes of the demo
- [ ] **All choice buildings** (Vault, Shrine, Fiendstone Keep) — keep,
      since they pre-date the culture choice
- [ ] **Codebase for Runai/Feraldis** — keep in source, exclude at build
      level. No need to delete code; full game brings it back.

### Single feature flag

Add a `DemoMode` constant somewhere central (e.g.,
`Core/Settings/DemoConfig.cs`) that gates:
- Culture choice screen (hide non-Alanthor)
- Sect adoption (hide non-demo-6)
- Tech tree (hide non-Alanthor branches)
- Main menu (hide non-demo modes)
- Wishlist CTA visibility

When `DemoMode = false`, the build is the full game. Avoids `#if DEMO`
preprocessor pollution.

---

## Content checklist by domain

### Code / engineering (existing build → demo build)

| Item | Status today | Demo-ready needs |
|------|--------------|------------------|
| Alanthor Hall→Town Hall age-up | ✓ working | Verify cultured rename triggers cleanly |
| Hub-and-segment walls | ✓ landed | Polish: SFX on auto-segment formation, hub-death cascade VFX |
| Wall conversion to gate/tower | ✓ landed | UI clarity pass |
| The Border spread | ✓ working | Verify deterministic seeding for demo maps |
| Glow drop on cleanse | ✓ working | VFX pass — Glow needs to feel valuable |
| Per-battalion upgrades | ⚠ partial | Verify Alanthor full ladder; numbers playtest |
| Religious unit (Scholar) | ⚠ partial | Purification VFX critical to ship in demo |
| Sect adoption UI | ⚠ partial | Lock the 6-sect set; gate UI on demo flag |
| Save/load | ⚠ unknown | Verify; required for Steam demo |
| Main menu | ⚠ unknown | Build to demo's needs |
| Tutorial | ✗ missing | Required — see UX section |
| `DemoMode` feature flag | ✗ missing | New: single source-of-truth boolean |
| AI personality (Alanthor only) | ⚠ unknown | Tune at least 2 difficulty levels |
| The Border difficulty scaling | ⚠ unknown | Important for solo demo pacing |

### Art (the priority list for hiring)

See updated [03_Art_Hiring_Brief.md](03_Art_Hiring_Brief.md). Summary:

**Tier 1 — Demo-critical** (needed before Steam demo ships)
- Studio logo (Shardroot)
- Game logo (The Waning Border)
- Steam capsule art set
- Key art / splash — but **single-faction-focused**: Alanthor key art
  with the "coming soon" silhouettes of Runai/Feraldis fading in the
  background ("the other cultures are coming")
- Alanthor building set (full — ~14 buildings)
- Alanthor unit roster — Worker, Scout, Spearman, Sentinel, Crossbowman,
  Cataphract, Scholar, Ballista (8 units, plus animations)
- UI chrome / panel frames (stylized)
- The Border VFX (creatures, border ground, Glow particles)

**Tier 2 — Polish for demo**
- Building icons (Alanthor set)
- Unit portraits (Alanthor set)
- Sect chapel art (only the 6 demo sects)

**Tier 2 deferred (for full release, not demo)**
- Runai building set
- Feraldis building set
- Runai unit roster
- Feraldis unit roster
- Other 6 sect chapels
- Faction insignia (Runai, Feraldis)

This dramatically reduces the immediate art workload. The full-game art
need doesn't shrink; it just shifts later in the timeline.

### Sound / music
- Faction-specific OST track for Alanthor (medieval / orchestral)
- The Border ambient theme (eerie / dissonant)
- Main menu theme
- ~30 SFX tags (combat, build, unit voice, UI clicks)
- Lock Runai/Feraldis music to "coming soon" — single placeholder
  ambient track for cross-culture menu transitions

### Design / balance
- Mirror-match Alanthor playtest pass — different sect builds need to
  feel like real choices (Fortitude vs Veneration vs War-rush vs
  Ruin-breaker is the matrix)
- The Border pacing for solo demo — survive-N-minutes mode tuning
- Tutorial scripting (see UX section)

### UX
- **Tutorial / first match** — guided objectives:
  1. Build worker → gather supplies
  2. Build Barracks → train Spearman
  3. Age up (pick a choice building → research Era II)
  4. Choose Alanthor culture (the OTHER two are visibly locked — first
     marketing hook!)
  5. Convert your first Gatherer's Hut to Wall Hub
  6. Adopt your first sect
  7. Defeat first The Border creature
  8. Defeat AI Alanthor opponent
- Win/lose screens with wishlist CTA on win, "try again" on loss
- **Wishlist Full Game button** in main menu, prominent
- **Join Discord button** in main menu
- **Demo end screen** — when the demo's match ends (or after N hours),
  show a "thanks for playing — here's what's coming" screen with the
  other two cultures teased

---

## The marketing-story arc

The demo is the **first chapter** of a three-chapter culture reveal,
which becomes the spine of marketing for the next year:

| Chapter | Public moment | Devlog beats |
|---------|---------------|---------------|
| **1. Alanthor demo** | Steam page + demo go live together | "Building the wall system," "The Petriarchy explained," "Surviving the The Border" |
| **2. Runai reveal** | ~3-6 months post-demo, when Runai is playable | "Caravans on trade lanes," "The trader-warrior network," "Identity through absence" |
| **3. Feraldis reveal** | ~6-12 months post-demo, full game launch lead-up | "Damage-as-income," "Veilsteel Frenzy," "Houses that spawn raiders" |

Each chapter is its own marketing push. The full game launch is the
fourth and biggest beat. **The constraint of one-culture-at-demo
becomes a year-long content engine.**

---

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Players judge "the whole game" by the Alanthor demo and decide it's not for them | Locked-culture UI tells the story explicitly — the other two are *very* different. Press kit explains the triangle. Demo length is short enough that even a "not for me" player tries it. |
| Mirror matches feel monotonous | 6 distinct sect archetypes (especially Ruin vs Fortitude) provide variety. The Border adds PvE flavor. 2 maps with different border-node counts. |
| "Coming soon" reads as vaporware | Tease with **dated content beats** (devlog posts revealing each culture on a schedule), not vague promises. |
| The 6 cut sects feel arbitrary | Don't apologize for it. Frame as "6 sects to start, 12 in the full game." Most players won't notice; the ones who do are engaged enough to wishlist. |
| Engineering scope creep — "while we're here, let's also fix X" | This file is the line. Defer X to post-demo unless it's a demo-blocker. |
| Steam refund window (2 hours) — the demo needs to deliver an experience in <2 hours per match | Two maps × 25 min × replayability = well within range. Tutorial < 15 min ideal. |

---

## Cross-references

- [01_Project_Summary.md](01_Project_Summary.md) — overall pitch
- [02_Media_Marketing_Plan.md](02_Media_Marketing_Plan.md) — phasing + channel mix
- [03_Art_Hiring_Brief.md](03_Art_Hiring_Brief.md) — updated priorities reflecting demo cuts
- [06_Locked_Culture_UI_Spec.md](06_Locked_Culture_UI_Spec.md) — how locked Runai/Feraldis appear
- [docs/Design/Age_1_Alanthor.md](../Design/Age_1_Alanthor.md) — full Alanthor spec
- [docs/Design/Overview.md](../Design/Overview.md) — game-wide framing
- [Assets/Scripts/Economy/SectConfig.cs](../../Assets/Scripts/Economy/SectConfig.cs) — sect roster + cluster mapping

---

*Last updated: 2026-05-26. Owner: Luis Martins, Shardroot.*
