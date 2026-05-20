# Game Design Document — *The Waning Border*

> **Game-design truth lives in [docs/Design/](docs/Design/Overview.md).** This file
> is a one-page summary; for any decision about mechanics, units, buildings,
> tech, costs, factions, or sects, read the Design folder and treat it as
> authoritative. Where this file and Design/ disagree, Design/ wins.

---

## 1. Game Summary

- **Genre:** RTS / Base-Building (Unity 6 + DOTS/ECS)
- **Core loop:** Gather → Build → Fight → Expand
- **Player fantasy:** Lead a no-culture starting civilization through one
  decisive culture pick, then drive depth via building upgrades, sect
  adoption, and the Crystal-Curse layer.
- **USP:**
  - Hybrid ECS RTS with 1k+ units
  - **Faction-agnostic Age 0** — every player starts on equal footing; the
    culture choice is the major irreversible mid-game decision
  - Three asymmetric cultures (Runai / Alanthor / Feraldis) with distinct
    relationships to **movement** — see [docs/Design/Overview.md § Movement axis](docs/Design/Overview.md#north-star-the-movement-axis)
  - Survival RTS layer (the Crystal Curse of Ahridan) that every culture
    interacts with differently
  - **Petriarchy sect system** — 6 of 12 religious sects per player, each
    granting a map power + passive + unit + building + technology

## 2. Design folder map

| Doc | Scope |
|-----|-------|
| [docs/Design/Overview.md](docs/Design/Overview.md) | Two-age structure, movement axis, age-up transformations, per-battalion upgrades, Glow economy, religious-unit tier, population model, caravan-death rule, Petriarchy framing |
| [docs/Design/Tech_Tree.md](docs/Design/Tech_Tree.md) | At-a-glance Mermaid charts — buildings, units, techs across Age 0 and all three cultures |
| [docs/Design/Age_0.md](docs/Design/Age_0.md) | Pre-culture Age 0 — every building / unit / tech / cost |
| [docs/Design/Age_1_Alanthor.md](docs/Design/Age_1_Alanthor.md) | Alanthor (defense focus) full Age 1 tree |
| [docs/Design/Age_1_Runai.md](docs/Design/Age_1_Runai.md) | Runai (economy / movement focus) full Age 1 tree |
| [docs/Design/Age_1_Feraldis.md](docs/Design/Age_1_Feraldis.md) | Feraldis (military focus) full Age 1 tree |

## 3. Aesthetics

The only design content not duplicated in `docs/Design/`. Each culture has
a tightly-controlled visual identity; per-building art prompts live in
[Assets/Art/Prompts/](Assets/Art/Prompts/).

| Culture | Palette | Style | Magic |
|---------|---------|-------|-------|
| **Runai** | Cyan / sandstone | Arabic-influenced palace + tent kit, flowing robes, horseshoe arches, copper domes | Light-blue magic |
| **Alanthor** | Sage green / warm grey | Medieval European castle, thick stone masonry, iron, slate | Arcane-machine magic |
| **Feraldis** | Crimson / dark grey | Celtic / Viking / dwarven-underdark, angular stone, obsidian, spires | Dark blood magic |

Visual rendering rules (Synty kit + faction-tint shader) are in
[docs/Alanthor_Visual_Systems_Spec.md](docs/Alanthor_Visual_Systems_Spec.md).
Engine: Unity 6 (6000.0.37f1), Entities 1.3.14 (DOTS/ECS).

## 4. Where everything else lives

- **Player-facing controls / hotkeys / UI:** [GAME_MANUAL.md](GAME_MANUAL.md)
- **Code-level reference (what's currently implemented):** [docs/Technical_Reference.md](docs/Technical_Reference.md)
- **Crystal-Curse implementation & test checklist:** [docs/Crystal_Curse_Sweep_And_Checklist_v2.md](docs/Crystal_Curse_Sweep_And_Checklist_v2.md)
- **Code conventions, namespaces, branching:** [CLAUDE.md](CLAUDE.md) and [.deft/project.md](.deft/project.md)
- **Implementation backlog:** [.deft/tasks/](.deft/tasks/)
