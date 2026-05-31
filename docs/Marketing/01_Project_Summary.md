# The Waning Border — Project Summary

> One-page pitch and reference doc. The substantive truth source for
> mechanics is still [docs/Design/Overview.md](../Design/Overview.md);
> this file exists to be **copied, pasted, and shown to other people**
> (artists, press, collaborators, Steam page reviewers).

**Studio:** Shardroot — `shardroot.com` · `info@shardroot.com`
**Engine:** Unity 6 (6000.0.37f1) · DOTS / ECS (Entities 1.3.14)
**Genre:** Real-time strategy — base-building, 1v1 / FFA, hybrid PvE
**Status (May 2026):** Pre-announce; playable internal build with placeholder art
**Stage flagship:** Solo / small-team development

---

## The pitch (one sentence)

> *The Waning Border is an asymmetric RTS where three cultures share one
> starting age and then diverge into three opposite relationships with
> movement on the map — walls that deny it, raids that prey on it, and
> caravans that embody it.*

## The pitch (one paragraph)

Every match begins on **equal footing**: no faction picked, the same
buildings, the same units, the same map to read. Halfway in, you commit
to one of three cultures — **Alanthor** (medieval stoneworkers who deny
movement with hub-and-segment walls and earn income only from closed
compartments), **Runai** (desert nomads whose economy *is* their army
*is* their territory — caravans on trade lanes generate supplies in
transit and auto-spawn patrolling trader-warriors), or **Feraldis**
(wood-and-bone Norse-coded warbands whose damage *is* their income —
every kill drips supplies, every house spawns autonomous raiders). Every
faction's identity is built around its relationship to the movement axis,
and a **Crystal Curse** PvE layer threatens all three. Twelve religious
sects (pick six) layer further depth on top of the cultural choice. No
imperial age, no fourth tech tier — the depth lives in **culture × sect
× per-battalion upgrades** rather than in further ages.

## Why this is interesting

1. **A genuinely new triangle.** Most RTS asymmetry pivots on
   *what units you train*. The Waning Border pivots on *how each faction
   relates to motion on the map* — denial, predation, embodiment. The
   three pairwise matchups (siege economy, chase economy, perimeter
   economy) fall out of this for free. *Source:*
   [Overview.md § Movement axis](../Design/Overview.md#north-star-the-movement-axis).
2. **No second age-up — depth comes from culture × sect × upgrades.**
   Two ages, hard stop. The 12-sect Petriarchy (pick 6) gives ~924
   six-sect combinations, layered on top of three cultures. Each
   battalion upgrade is paid per-battalion, so an army's tier ladder is a
   resource sink, not a free power-spike.
3. **A PvE layer that gates the late game.** The Crystal Curse spreads
   across every map and is the *only* source of **Glow** (the T4 super-
   resource). Each culture interacts with it differently: Alanthor
   cleanses, Runai converts, Feraldis destroys — and the Glow drop is
   one-shot per node, so the late game is fought on a finite resource
   gradient.
4. **Identity-defining absences.** Runai has *no walls* and *no Houses*.
   Feraldis has *no House-pop* (Houses become pure raider-spawn
   buildings). Alanthor has *no instant-pop*. These absences do real
   design work — they're not cosmetic.

## Factions at a glance

| | Alanthor | Runai | Feraldis |
|---|---|---|---|
| **Focus** | Defense | Economy / movement | Military |
| **Style** | Medieval European castle | Arabic-coded desert nomads | Celtic / Viking warband |
| **Palette** | Sage green + warm grey limestone | Cyan + sandstone, copper domes | Crimson + dark grey, obsidian |
| **Magic** | Arcane-machine | Light-blue, flowing | Dark blood magic |
| **Movement relationship** | Denies (walls, towers) | Embodies (caravans, lanes) | Preys on (raiders, pillage) |
| **Income floor** | Closed wall compartments | Caravans-in-transit + trader-warrior patrols | Damage on enemies *or* Crystal Curse |
| **Population** | Standard house ladder | Instant 200 at age-up (no houses) | Instant 200 at age-up (houses spawn raiders) |
| **Religious unit** | Scholar (cleanses curse → Glow) | Acolyte (converts → Glow) | Iconoclast (unblocks → Glow) |
| **Wall system** | BFME2 hub-and-segment, gates by conversion | None — lanes are defense | None — pressure is defense |

## Standout mechanics

- **Per-hut age-up choice** *(Alanthor)* — each Gatherer's Hut from
  Age 0 prompts the player at age-up to convert it into a **Wall Hub** or
  **Watch Tower**. Mixed conversions are the norm.
- **Caravan transit-spike** *(Runai)* — wagons output full supplies *while
  moving*, decaying linearly over 4 minutes. Drive far for skill ceiling,
  plant fast for safety, re-route mid-transit for adaptive play.
- **Pillage + Veilsteel Frenzy** *(Feraldis)* — killing non-military
  units drips supplies and iron; carrying Veilsteel shavings into battle
  buffs attack like Norse berserker mushroom-rage (a flavor / lore beat).
- **BFME2 hub-and-segment walls** *(Alanthor)* — only Wall Hubs are
  placed; segments and gates auto-form between hubs. Kill a hub, the
  segments cascade-destroy.
- **Caravan kills feed Feraldis** *(cross-faction synergy)* — a Runai
  caravan killed by Feraldis drops 50% of its cargo as Feraldis supplies;
  Alanthor or Runai killers destroy the cargo. Asymmetry has teeth.
- **Glow as one-shot finite resource** *(cross-faction)* — each Crystal
  Curse node yields exactly one Glow pickup, ever. Late-game T4 upgrades
  are bottlenecked by a fixed map budget.

## Current build state (honest snapshot)

What works in the playable build:
- Age 0 economy loop (workers, mining, building, depositing)
- Combat (melee + ranged), targeting, line-of-sight, damage formula
- Building construction, upgrades, training queues
- The three culture identities exist as data; the cultured renames and
  per-faction tech are in the JSON tech tree
- Alanthor BFME2 wall system (hub-and-segment, auto-formed segments,
  hub-death cascade) — newest major system, just landed
- Internal AI with multiple personalities; 4-agent dev pipeline for
  internal task tracking

What is **placeholder** today (and what the art search is for):
- Building visuals — Synty kit with faction-tint shader as stand-in
- Unit models / animations — placeholder
- UI / HUD — IMGUI panels (functional, not pretty)
- Logo, capsule art, key art, Shardroot studio identity — none yet
- Religious-unit (Scholar / Acolyte / Iconoclast) visuals
- Crystal-Curse / Glow visual identity
- Battalion size finalization for per-battalion upgrades
- Petriarchy 12-sect art and balance

## Art prompts already written

The project ships with **per-faction building art prompts** with exact
color codes:
- Alanthor — warm grey limestone `#737068`, sage green `#8CA680`
- Runai — cyan + sandstone (see `Assets/Art/Prompts/Runai_Buildings.md`)
- Feraldis — crimson + dark grey, obsidian veining

These are ready to hand to a 3D artist for direct reference. See
[Assets/Art/Prompts/](../../Assets/Art/Prompts/).

## Why now

The RTS revival is real (Stormgate, Tempest Rising, Battle Aces,
Fractured Alliance hitting 50k+ wishlists in 2025-26) and **none of the
revival entries lean into asymmetric movement-philosophy** the way The
Waning Border does. The genre's audience is hungry, the comparable
indies are mostly Starcraft-clones, and the "one culture choice"
structure makes the game **legibly different in 30 seconds** — a
critical property for Steam-page conversion.

## What we need (the asks)

1. **3D art partner** — a stylized low-poly RTS-isometric artist who
   can take the art prompts and produce buildings, units, and unit
   animations. Rev-share / credit-only / portfolio-piece terms.
2. **Concept / key art** — splashes for the three factions, Steam
   capsule art, and the studio logo for Shardroot.
3. **UI art** — building icons, unit portraits, panel chrome for the
   IMGUI HUD.
4. **A small press / community reveal moment** at the right time —
   first devlog goes live when Steam page + Shardroot site are
   simultaneously ready.

## One-line tagline candidates

These are scaffolding for the marketing copy — workshop, don't ship as-is:
- *Three cultures. One moment to choose. A border that's only ever waning.*
- *Wall it. Raid it. Walk it. Three answers to the same map.*
- *An RTS where every faction is a different argument about what a border is for.*

---

*Last updated: 2026-05-26. Owner: Luis Martins, Shardroot.*
