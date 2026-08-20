# Art Hiring Brief — The Waning Border

> Copy-paste-ready brief for sourcing artists. The first half is a
> recruitment doc designed to be sent to candidates **as-is**; the second
> half is collaborator-terms scaffolding for the conversation when
> someone replies.
>
> **Scope update (2026-05-26):** The first public release is a free
> Steam demo featuring **only the Alanthor culture** (Runai and Feraldis
> are locked as "coming in the full release"). This dramatically
> tightens the immediate art priorities — see [§ Demo-first art
> priorities](#demo-first-art-priorities-2026-05-26-update). The full
> roster below covers the *eventual* game; the demo cut is a subset.
> The companion doc [05_Demo_Scope.md](05_Demo_Scope.md) is the truth
> source for what ships in the demo.

---

# PART 1 — The brief (send this to artists)

## About the project

**The Waning Border** is a real-time strategy game in development by
**Shardroot**, a small indie studio (`shardroot.com`). The game is built
in Unity 6 with DOTS/ECS and is in pre-announce stage with a playable
internal build.

It's an asymmetric RTS where three cultures share one starting age and
then commit to one of three opposite philosophies:
- **Alanthor** — medieval European castle culture. Defensive. Walls,
  towers, stone, sage green banners.
- **Runai** — desert nomad traders, Arabic-influenced architecture.
  Mobile economy. Cyan and sandstone, copper domes, flowing robes.
- **Feraldis** — Celtic / Viking / dwarven-underdark warband culture.
  Military. Crimson and dark grey, obsidian veining, angular stone.

Each culture has its own buildings, units, magic style, and color
palette. Color and style discipline is critical — these factions read
distinct at a 100-unit-zoom-out.

## Art style target

**Stylized low-poly RTS-isometric.** Reference comps (not exact targets,
direction-only):
- *Battle Aces* (Uncapped Games) — clean low-poly RTS readability at zoom
- *Northgard* — stylized Norse buildings, painterly textures
- *They Are Billions* — strong silhouette discipline at small scale
- *Age of Empires IV* — aesthetic ambition, scaled down
- *Bad North* — minimal but strong silhouettes
- Synty Studios kit aesthetic (we're using this as placeholder; final
  art is one step richer than Synty default)

**Hard constraints:**
- Faction silhouette must read in a 64×64 minimap-tile
- Color palette per faction is locked (codes below)
- Buildings must read from the standard RTS isometric camera angle
- Units must remain identifiable at ~150 units on screen
- Hand-painted textures preferred over PBR for performance and stylistic
  consistency; we'll discuss texture budgets per asset

## Demo-first art priorities (2026-05-26 update)

We are shipping a **free Steam demo featuring only Alanthor** first.
The other two cultures are deferred to the full release. This rewrites
the priority order — Tier 1 art is everything needed for the demo
launch; Tier 2 expands to the full release.

If you only have appetite for the Tier 1 demo block, that is a
**complete and valuable collaboration on its own** — you'd be the
artist behind the entire visual identity at launch. The Runai +
Feraldis work below is a future expansion we'd love a continuing
partner for, but not a precondition.

## What we need (in priority order)

### Tier 1 — Demo-critical (needed before Steam demo ships)

1. **Studio logo + brand mark** for Shardroot (`shardroot.com`)
2. **Game logo / wordmark** for The Waning Border
3. **Steam capsule art set** — main capsule, header, small, vertical;
   plus the page-background banner. **This is the highest-priority single
   asset** — it determines whether Steam users click through.
4. **Key art / splash piece** — *Alanthor-led composition* with Runai
   and Feraldis as **silhouettes in the misty background** (the "coming
   chapters" tease). The demo's hero image.
5. **Full Alanthor building set** — Hall/Town Hall, Hut/House, Barracks/
   Garrison, Archery Range/Practice Range, Wall Hub, Watch Tower, Vault
   of Almiérra, Shrine of Ridan, Fiendstone Keep, Royal Stable, Siege
   Yard, Smelter, Crucible (~14 buildings — the demo's entire build
   menu)
6. **Alanthor unit roster (8 units)** — Worker, Scout, Spearman,
   Sentinel, Crossbowman (or Longbowman), Cataphract, Scholar,
   Ballista. With animations: walk, run, idle, attack, death, build
   (workers only), gather (workers only).
7. **The Border VFX kit** — border-creature models (2-3 variants),
   border-ground shader, Glow pickup particle, border-node visual state
   transitions (Active → Cleansed). The PvE layer is half the demo's
   appeal; VFX must sell it.
8. **UI / panel chrome** — stylized IMGUI panel backgrounds in Alanthor
   sage-green-and-stone, button frames, hover/active states
9. **Locked-culture card art** — silhouette-style placeholders for
   Runai and Feraldis on the age-up screen (see
   [06_Locked_Culture_UI_Spec.md](06_Locked_Culture_UI_Spec.md))
10. **Sect chapel art for the 6 demo sects** — Fortitude, Renewal,
    Witness, Veneration, War, Ruin (each is one building model, distinct
    silhouette per sect)
11. **Religious unit (Scholar) VFX** — Purification ritual on border
    nodes; high importance because the Scholar is the demo's "Glow
    unlock" moment

### Tier 2 — Deferred to full release (Runai + Feraldis chapters)

These are described in detail in the original brief below but are
**not blocking the demo**:

- Full Runai building set
- Full Feraldis building set
- Runai unit roster
- Feraldis unit roster
- Remaining 6 sect chapels (Antiquity, Reclamation, Silence, Justice,
  Ash, Wrath)
- Runai religious unit (Acolyte) + VFX
- Feraldis religious unit (Iconoclast) + VFX
- Faction insignia for Runai and Feraldis

### Tier 3 — Polish across both tiers

- Building icons for the action panel (128×128 per building)
- Unit portraits for the selection panel
- Glow effect refinement / Border VFX polish
- Main menu / loading screen art
- Particle work for combat hits, building destruction, magic effects

---

## Original priorities (kept for reference — represents full-game scope)

The original Tier 1-3 list below is the **full eventual scope**. Use it
to understand the project's total art ceiling; the demo cut above is the
immediate ask.

### Full-game asset inventory (reference — Runai + Feraldis = full release art)

- **Building set, Runai** — Trader's Hall, Route Guard, Arrowyard,
  Grazing Grounds, Trade Hub, Outpost, Veilsteel Foundry, Siege
  Workshop, Thessara's Bazaar
- **Building set, Feraldis** — War Hall, Longhouse, Thrower Camp,
  House (raider-spawn), Gatherer's Hut → Hunting Lodge / Logging
  Station, Fiend Foundry, Totem Tower, Siege Yard
- **Unit models (low-poly, ~500-2000 tris each)** for both factions —
  line infantry tier 1/2/3, ranged tier 1/2/3, cavalry, siege engine,
  religious unit
- **Animations** for each new unit — walk, run, idle, attack-melee,
  attack-ranged, death
- **Faction insignia** — Runai (cyan crescent/sun), Feraldis (crimson
  raider's-fist) — Alanthor (sage green sword/shield) is in the
  demo Tier 1 above

## Color & style discipline (locked)

### Alanthor (medieval European stoneworkers)
- **Stone:** Warm grey limestone `#737068` (primary), darker grey
  `#555550` (foundations / accents)
- **Fabric / banners:** Sage green `#8CA680`
- **Metal:** Dark iron and steel, occasional brass / gold trim
- **Roofs:** Dark slate grey
- **Shared motifs:** Crenellated battlements, arched doorways, iron
  reinforcements, square footprints, heraldic banners
- **Magic style:** Arcane-machine (think clockwork, runic inscriptions)
- **Reference:** European castles, Northgard's "stronghold" feel,
  Tolkien's Gondor minus the white

### Runai (desert nomad traders, Arabic-influenced)
- **Architecture:** Tents, palace + tent kit, horseshoe arches, copper
  domes, lattice-work shaded windows
- **Color:** Cyan / sandstone (primary), accents of deep indigo and
  warm gold
- **Magic style:** Light-blue, flowing — think water-magic visuals
- **Shared motifs:** Curved silhouettes, fluttering fabric, no walls
  (identity-defining absence), caravan-wagons
- **Reference:** Persian / Moroccan architecture, *Prince of Persia:
  Sands of Time*, *Sands of Salzaar*

### Feraldis (Celtic / Viking / dwarven-underdark warband)
- **Material:** Wood, dark stone, obsidian veining, bone-on-spike
  embellishments
- **Color:** Crimson `#9E2A2B` (banners), dark grey `#3A3A3A` (stone),
  weathered wood `#5C3A21`
- **Magic style:** Dark blood magic, eldritch
- **Shared motifs:** Angular silhouettes, runestones, raider-pyres,
  inhospitable
- **Reference:** Norse mythology, *Northgard*'s Wolf clan,
  *Hellblade*'s Norse aesthetic, *Total War: Attila*'s Saxons

## What we already have

- **Per-building art prompts** for all three factions are written and
  ready to hand over. See `Assets/Art/Prompts/Alanthor_Buildings.md`,
  `Runai_Buildings.md`, `Feraldis_Buildings.md` — each building has a
  detailed paragraph describing intent, silhouette, colors, motifs.
- **Synty Studios kit** as the current placeholder — gives the artist a
  reference for in-engine scale and the camera angle.
- **Faction-tint shader** for color discipline at runtime.
- **A playable build** the artist can watch / interact with to see how
  the art ships.

## Engine + workflow

- **Engine:** Unity 6 (6000.0.37f1), DOTS/ECS
- **Render pipeline:** URP (Universal Render Pipeline)
- **3D format:** FBX preferred for models + animations
- **Texture format:** PNG, hand-painted preferred over PBR
- **Texture resolution:** 512×512 for most buildings; 256×256 for units;
  flexible per asset
- **Tools:** Blender (free) is the assumed pipeline; we have a
  `blender/` folder in the project for source files. Maya / 3ds Max are
  also fine.

## Compensation — bootstrap reality

We're a pre-revenue indie studio in pre-announce stage. We're being
upfront about this because we respect artists' time:

**What we can offer (in order of preference for most artists):**

1. **Revenue share** — agreed percentage of net game revenue (and
   merchandise, if it ever exists) for a defined period after launch.
   See PART 2 for the structure we're proposing.
2. **Cash on completion of milestone** — modest per-asset fees, paid on
   delivery and acceptance. Total budget is small but real (we can pay
   for a logo + capsule art set as a starting point).
3. **Credit + portfolio piece + reference** — full credit in
   game / on Steam page / on `shardroot.com`, plus a strong reference
   for your portfolio. This is what every collaborator gets regardless
   of payment structure.
4. **Hybrid** — small cash for Tier 1 assets, rev-share for Tier 2+.

We're flexible and want to find an arrangement that works for both
sides. If you have a structure you've worked with before that you
trust, we want to hear it.

## What we won't ask for

- Speculative work (no "audition" beyond an honest portfolio review)
- Unlimited revisions — we'll define a per-asset revision budget
- Ownership of your portfolio — you keep your work, we license what we
  use
- Exclusivity unless we're paying for it
- Working under NDAs that prevent you from showing your work *after*
  reveal — early secrecy yes, permanent silence no

## How to get in touch

- **Email:** `info@shardroot.com`
- **Subject line:** `Art collaboration — The Waning Border`
- **What to send:**
  1. Portfolio link (ArtStation preferred; personal site fine)
  2. 2-3 specific pieces you think match the brief
  3. Which tier(s) you'd want to work on
  4. Your preferred compensation structure (or "open to discuss")
  5. Realistic timeline / weekly hours you could commit
  6. Any questions

We'll reply to every serious inquiry within 5 business days.

---

# PART 2 — Working terms (for internal discussion with shortlisted candidates)

This section is **not** in the version sent to candidates initially.
Bring it out when conversations get serious.

## Revenue-share structure (proposed default)

**Pool model.** Not per-asset percentages — a single "art collaborator
pool" of net revenue, split among collaborators by an internal point
system based on volume and importance of work delivered.

- **Pool size:** 10-15% of net game revenue (Steam revenue minus
  Valve's 30%, minus refunds) for 36 months from launch.
- **Point allocation:** by deliverable (e.g., a hero key art = 200
  points; a building set of 12 = 600 points; a unit + animations = 80
  points).
- **Vesting:** points are awarded on **acceptance of the deliverable**,
  not on contract signing. Walking away mid-collaboration forfeits
  unvested points.
- **Cap:** no single collaborator above 60% of pool unless they're
  carrying nearly all the art.

**Why pool not per-asset:** if Tier 1 collaborators stop showing up
after capsule art ships and Tier 2 collaborators carry the building
sets, the pool naturally rebalances toward the people who delivered the
work.

## Cash + rev-share hybrid (alternative)

For Tier 1 assets where the artist won't accept pure rev-share:
- **Logo + brand mark:** $200-500 flat, paid on delivery
- **Steam capsule art set:** $400-1000 flat, paid on delivery
- **Key art splash:** $300-800 flat, paid on delivery
- **All else:** rev-share via pool model

These rates are intentionally modest. Adjust based on artist tier and
what shardroot can actually afford at the moment.

## Credits commitment

Every collaborator gets, regardless of payment structure:

1. Name + role in the in-game credits
2. Name + portfolio link on `shardroot.com/credits`
3. Name + portfolio link in the Steam page "About the Developer"
4. A LinkedIn-style endorsement from Luis on request
5. Reuse of their assets in their own portfolio with credit
6. The right to talk publicly about the work after the announce date

## IP terms

- Artist retains copyright on the source work (the .blend file, the
  layered .psd) — they license it to Shardroot for use in the game and
  marketing.
- Shardroot has the right to modify, animate, and re-skin assets for
  game purposes.
- Artist cannot relicense the same exact assets to a competing RTS in
  the same era (12-month non-compete on direct copies only).
- Artist *can* sell similar (non-identical) style work elsewhere.
- If the project is canceled or shelved, asset licenses revert to the
  artist (clean walk-away clause).

## Communication norms

- Weekly check-in (15 min) once active work begins
- Discord DMs for fast questions; email for milestone-level decisions
- Revisions: 2 rounds per asset included; further rounds discussed
- Deadlines: missed deadlines trigger a conversation, not a penalty —
  we're all human, life happens. Repeated misses without communication
  end the collaboration cleanly.

## What "done" looks like (per-asset)

- Source files delivered (.blend / .max / etc.)
- Textures delivered (high-res source PSDs)
- FBX export with correct pivot, scale (1 Unity unit = 1 meter), and
  axis orientation
- Animations on a humanoid rig (or unique skeleton with documentation)
- Test integration in Unity confirmed by Luis before sign-off

## Red lines

- We won't accept AI-generated work passed off as hand-made. AI as a
  sketch tool fine; AI as the deliverable not fine — Steam's
  AI-disclosure rules require honesty, and players notice.
- No stolen / traced reference work. We'll do a reverse-image-search
  spot-check on portfolio pieces.
- Late-game payment renegotiation in bad faith voids the collaboration.

---

## Sources / further reading on commissioning game art

- [How to Commission Game Art for Your Indie Game — Ruxar](https://ruxar.com/game-art-commissions/)
- [ArtStation — Games Artist Channel](https://www.artstation.com/gamesartist)
- [Upwork — Freelance Game Artists for Hire](https://www.upwork.com/hire/game-art-freelancers/)
- [Polycount — freelance discussions](https://polycount.com/discussion/204947/do-studios-hire-freelance-3d-artists)

---

*Last updated: 2026-05-26. Owner: Luis Martins, Shardroot.*
*Hiring contact: `info@shardroot.com`.*
