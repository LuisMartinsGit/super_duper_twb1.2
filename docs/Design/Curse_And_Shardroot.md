# The Curse & the Shardroot — Canonical Design

**This document COMPLETELY REPLACES the previous curse/Border design and
the Glow economy** (design decisions 2026-07-10/11). Where any other
document — including
[Complete.md § 1.11 The Glow economy](Complete.md#111-the-glow-economy),
the Age 1 culture docs' Glow references, or prior Border behavior
descriptions — disagrees with this file, **this file wins**. The code
(`Systems/Border/`, Glow costs in tech data) is aligned progressively;
see § Implementation phases.

**2026-07-11 revision — the curse is a FORCE, not a faction:** its combat
layer is removed and the crust becomes an **absolute wall**. See
**§2.5**, which supersedes the "spawns/guards/waves", "crust never blocks
pathing", and "dangerous, not lethal" behaviour described below.

## 1. Vision

The Curse is the map's **third player, its veilstone economy, and its
alternative victory condition**. The map starts with **N Wells**. Each
culture has one **verb** it can apply to a well — Feraldis *destroys*,
Runai *pacifies*, Alanthor *purifies* — and **applying your verb to all
N wells simultaneously wins the match**. Buried in one well is the
**Shardroot**, a One-Ring-style artifact of power (BFME2 reference)
discovered through normal verb play.

Three perception rules (hard requirements):

- The curse is **collective pressure**, never a private nuisance — the
  well count is a shared, legible clock every player reads.
- The curse is never **purposeless** (no AoE-wolf): every well is income,
  a victory objective, and a Shardroot candidate at once.
- No match can be played without weighing **curse × culture × map**:
  your culture choice IS your verb.

## 2. Wells

### 2.1 Setup & Age 0

**Default: N = 4, one well per map corner (2026-08-11).** Wells spawn at
the four corners of the playable bounds (12% inset, slid toward the map
centre until the ground fits) — the curse presses in from the edges and
every spawn reads its nearest corner as "its" well. The old
always-centre-of-map well is gone: it sat in everyone's lap and nobody's
territory. Scene `BorderNodeMarker`s are the per-map on/off lever for the
Border faction; by default their positions are unused
(`BorderNodeBootstrap.SpawnCornerNodes`).

**Per-map override: the map may author its own well set (2026-08-12).**
Ticking `BorderNodeMarker.AuthoredPosition` on one marker hands the whole
well list to that map — one well per ticked marker, where it stands, and
**N is whatever the map placed** (`BorderNodeBootstrap.SpawnAuthoredNodes`;
un-ticked markers are then ignored, because a half-authored well set would
be a silent balance change). Corner wells are the right default and a poor
fit whenever the well layout *is* the map:

| Map | Wells | Why not corners |
|---|---|---|
| Hollow Table (1v1) | **N = 1**, dead centre | The map is a duel over one piece of ground; four objectives would be four duels |
| Twin Spans (3v3) | **N = 4**, one per bridgehead | The map is about two river crossings; corner wells would put every objective as far from the river as the map allows |

Nothing downstream needs a special case: `NodeVictorySystem` scores well
domination against the **live node count**, so N = 1 and N = 4 both work —
at N = 1 the victory condition simply reads as king-of-the-hill, and the
"holds all but ONE well" match-point broadcast self-disables below N = 2.
> **REVERTED (2026-08-03, playtest):** mining the Veil directly is
> retired — the reforming crust stranded diggers, output was a trickle,
> and crust spawns killed them. **Veilstone now comes from discrete
> crystal deposits** placed near every base and scattered across the map
> (curse-independent, iron-style gathering, credited straight to the
> bank — `VeilstoneMiningSystem`). The Veil itself is **influence only**:
> a spreading ground texture with no physical crust — it never blocks
> pathing, never harms units, and cannot be dug
> (`VeilCrustConstants.CrustPhysical = false`). The original
> mine-the-sheet design below is kept for reference.
>
> **AMENDED same day (§2.5b):** the influence-only Veil regained teeth —
> walkable **hostile ground** (exposure + travel cost), and veilstone
> deposits are no longer curse-independent: they **precipitate from the
> Veil itself** (blight-pocket residue, recede-residue, frontier
> eruptions). See §2.5b for the current model. Pre-culture players
sneak-mine the crust under harassment threat: early interaction is
guaranteed but survivable. Verbs unlock at age-up with the culture
choice. Wells themselves **look like giant veilstone formations** (the
same gem-cluster mesh the crust uses, at landmark scale) — the well is
an eruption of the very material you mine, and the Shardroot hides
inside one of them.

### 2.2 The well state machine

A well is always in exactly one state:

| State | Applied by | While active | Ends |
|---|---|---|---|
| **Wild** | — (default) | Spreads blight, matures (**no spawns/guards** — §2.5) | When a verb lands |
| **Destroyed** | Feraldis (army kills it) | Well is GONE; its crust **COLLAPSES violently (~25 s, 2026-08-04)** and the death **bursts a veilstone loot ring** around the well (8 nodes) — the loot field made literal, claimable by whoever holds the ground; untouchable by anyone | Respawns Wild after the hold timer — and every regrowth is harder than the last (escalation) |
| **Pacified** | Runai (Acolyte ritual + **Tether** structure) | Veilstone trickle to the owner; no spreading | Reverts Wild on expiry, or when the Tether is razed |
| **Purified** | Alanthor (Scholar ritual → **Sanctified Font**) | Veilstone generation + influence projection (build space) + sect-power discounts nearby | Reverts Wild on expiry, or when the Font is razed |

**Hold timer & tempo rule:** every non-Wild state lasts **10 minutes**
(TBD). Applying your verb to *any* well **refreshes all of that player's
existing holds to full**. Activity is map control; ten minutes of
inactivity and the curse returns everywhere at once.

**Break matrix (confirmed):**

- A **Destroyed** well cannot be interacted with by anyone until it
  respawns. Ritual cultures cannot contest a Feraldis sweep directly —
  only militarily (intercept the army, defend living wells).
- **Feraldis can destroy any living well**, including Pacified and
  Purified ones (razing the Tether/Font is part of the assault).
- **Rituals never overwrite rituals**: Runai/Alanthor can claim only
  Wild wells; breaking a rival ritual means razing its structure
  (reverting the well to Wild), then claiming it.
- Holds are **per-player**, not per-culture (mirrors race each other).

### 2.3 THE VEIL — the curse as a continuous sheet (book canon)

The curse is not the nodes: it is **the world slowly turning to
veilstone**. A map-wide **saturation field** (the Veil) makes it terrain:

- **Wells are eruption points inside the sheet.** Matches start with
  established crust around every well (*the world is already sick*);
  Active wells feed the field and the crust **creeps outward
  cell-by-cell** — a fully neglected map is overrun in ~20–30 minutes.
- **Verbs starve the sheet.** Crust whose nearest well is not Active
  **decays** — the front visibly recedes; a Purified Font additionally
  holds a **sanctified circle** clear. The state of the war against the
  curse is readable terrain: purple ground IS the scoreboard.
- **Mineable — and the curse's visible BODY**: the sheet is **ONE
  continuous crystalline mesh**, not scattered rocks: a faceted veilstone
  crust (with sparse crystal spikes) that procedurally **grows where the
  field advances and recedes where it drains**. It is an **infinite
  veilstone source dug DIRECTLY, Astroneer-style (2D)**: right-click the
  crust and the miner walks to the **closest crusted vertex** of the
  field grid and picks at it — every swing banks veilstone AND drains the
  field under the pick, so **the mesh visibly recedes exactly where that
  villager is digging**; when the vertex breaks through, the digger
  auto-advances to the next crusted vertex nearby, eating the front
  edge-inward. **Hardness scales with proximity to the wells**: the far
  frontier is soft and fast to break, crust near a well is dense and slow
  — digging close pays more in time and danger. There are no veilstone
  deposit entities anywhere; ~~the crust never blocks pathing~~
  (**superseded — the crust is now an absolute impassable wall, §2.5**);
  ground paint is a muted corrupted-soil underlay beneath the crust. A destroyed
  well's crust decays only slowly — an undefended loot window (Feraldis'
  burst income) until the well respawns.
- **Dangerous, not lethal** [**SUPERSEDED by §2.5** — crust is now an
  impassable wall with telegraphed catch-death; the reduced-speed/stat
  debuff no longer applies because units can't stand on crust at all]:
  ~~units on crust suffer reduced speed and combat stats~~.
  Crust is **unbuildable**, and iron deposits under deep veil are
  **swallowed** (unusable until the ground is reclaimed). **Curse nodes
  never attack (2026-08-11)** — no well turret, no turret sub-nodes; the
  curse's teeth are its ground, its creatures, and the Backlash. Border
  *creatures* guarding a well ignore harvesters outside close range
  (guard, don't hunt) so sneak-mining stays survivable.

The Well→Fissure→Maw maturity ladder is superseded by the Veil (the
sheet IS the maturity); the guard-not-hunt rule, budget caps and
telegraphed waves keep the curse dangerous exactly in proportion to the
map's collective neglect.

### 2.4 Victory by domination

**A player wins when all N wells are simultaneously in their verb-state.**
Well states are public; approaching match point is visible to everyone,
and the leader must defend N−1 holds while taking the last well — the
designed set-piece climax (and, in FFA, the natural dogpile trigger).

### 2.5 The curse is a FORCE, not a faction (2026-07-11 revision)

Latest decision. The curse's **combat layer is removed** — it is pure
environmental pressure, not a third army. This **supersedes** the "spawns
/ guards / telegraphed waves" behaviour in §2.2–2.3, the "crust never
blocks pathing" and "dangerous, not lethal / no DoT" lines in §2.3, and
the curse-unit / well-army / ritual-defence content in the code
(`Crystalling` / `Veilstinger` / `Godsplinter`, `BorderSettings` army
tiers, wave + ritual-defence spawners). Wells become **neutral
objectives**; the conflict is **player vs player** on ground the curse
keeps eating. It keeps the challenge while letting players focus on each
other.

- **Absolute wall.** Crusted cells (the crystal field) are **impassable** —
  units cannot enter or path through the curse. It is a spreading wall
  that severs supply lines and isolates expansions. (The crystals are a
  physical barrier; it made no sense for units to weave between them.)
- **Mining is the only way through.** Digging an edge cell clears it
  (**1 veilstone per cell dug through**, §2.3) and *opens* it — so mining
  a corridor is how you tunnel to a cut-off ally, flank a rival, or
  reconnect severed territory. **Economy and mobility are the same act.**
- **Catch-death, always telegraphed.** The **tendril heartbeat** is the
  warning: the front sits dormant for minutes (safe to work beside), then
  bursts. Cells about to be crusted flash a ~1 s tell; a unit still there
  when the crust solidifies takes **rapid lethal damage (~2 s)** — time to
  react, fatal if ignored. **Workers auto-flee** the advancing front.
  The threat is instant but never unwarned.
- **Buildings block the curse** (structures are anchors of order — you can
  wall the curse off with your base). A building the curse *does* engulf
  **corrupts and crumbles over ~15–20 s**, savable by clearing the crust
  around it — never a silent instant loss.

**Faction stances (with the absolute wall):**

- **Alanthor — hold it back.** Purify wells clamp sanctified safe circles;
  walls and buildings stop the crust cold. Carve out order and defend it.
- **Runai — live with it.** Fastest, most efficient diggers; trade lanes
  reroute around new crust automatically, and when the curse **severs** a
  lane they **dig to reconnect** it — their arteries are the pressure
  point. Pacified wells persist as infrastructure.
- **Feraldis — devour it.** They do **not** walk the crust (nobody does):
  they **destroy and loot** it. Fastest, most violent diggers — they punch
  raid-corridors through the wall where enemies expect no threat — and
  destroying wells leaves the slow-decay loot field that is their burst
  income. "Negating the curse" = clearing and profiting from it, never
  traversing it.

**Pathfinding & AI.** Crust stamps **impassable** into the nav cost field;
armies and caravans **path around it for free** (the flow-field stack
handles dynamic obstacles — no AI code). Isolation is a real, legible
consequence: a sealed unit needs a **miner to dig it out** or dies
(telegraphed). AI is staged so the design ships without heroic AI:

- **AI v1** — treat crust as impassable, path around, and **retarget to a
  reachable objective** when the curse cuts one off (graceful, not dumb).
- **AI v2 (later)** — a directional **"dig toward X"** order to reconnect
  a severed lane or reclaim isolated ground.

### 2.5b The curse as HOSTILE GROUND — the exposure model (2026-08-03 rev.2, CURRENT)

**Latest decision — supersedes both extremes tested before it:** the
§2.5 absolute wall (falsified in playtests: the reforming crust stranded
and killed diggers, and miner-spam trivialised the threat) and the
same-morning influence-only revert (no pressure at all — the theme
"players must address the curse to progress" had no mechanical body).
Both poles failed for the same two reasons: the threat was aimed at
**workers in the phase where losing one costs the most**, and the counter
was an **economy check** the richest player wins. The exposure model
redirects the threat at **map control** and the counter at **strategic
actions** (influence, verbs, military), keeping the actor-less
determinism of terrain.

**Retained from §2.5:** no curse faction or army
(`CurseFieldsArmies = false` stays), buildings block spread (rule G),
wells as neutral verb objectives, the influence interaction (§2.6), the
tendril heartbeat, victory by domination.
**Retired:** catch-death, worker ward, miner infection eruptions (may
return as a late-game knob — open item), dig-the-sheet mining, and the
impassable nav stamp.

**Core rules:**

1. **Walkable, hostile.** Crusted cells are passable. The Veil's body is
   the purple-rock ground texture; nothing invisible blocks anything.
2. **Exposure (the DOT).** Units on crust accrue exposure; after a grace
   window it becomes damage-over-time, scaling with saturation depth —
   crossing a thin finger is free, marching an army through deep crust
   is a real toll, camping in it is lethal. Exposure recovers off-crust.
   The grace window + thin-haze scaling means base-ring haze essentially
   cannot kill a worker — early neglect costs tempo, not corpses.
3. **Travel cost.** Crusted cells stamp a **finite, saturation-scaled**
   cost into the nav field (not impassable): pathing automatically
   prefers clean ground and cuts through only when it is worth the
   exposure. Deep crust is a soft wall; the map's topology degrades as
   the curse spreads.
4. **Stat debuff** on crust stays (speed/att/def, depth-scaled).
5. **Buildings.** Spread still stops at structures; a completed building
   standing in DEEP crust (engulfed by later growth) slowly **crumbles**
   — loud, slow, savable by reclaiming the ground. Local backstop for
   total neglect; no global punisher.
6. **Escalation.** The tendril heartbeat's dormant windows shorten over
   match time — the same curse that was a nuisance in Age 0 is a
   dominant terrain force by the late game.
7. **Hearth & Ward (Age 0 securing).** Age 0 projects no influence, so:
   every **Hall radiates a small fixed suppression circle** (the curse
   cannot grow there and decays there — same effect as influence,
   veil-only, no territory claim), and a cheap **Ward** building
   (suppression-only, small radius) lets Age 0 players extend that
   protection deliberately. *(Ward = planned follow-up; hearth ships
   first.)* At age-up, culture influence supersedes both.
8. **Mining corruption (the Age 0 curse — 2026-08-03 rev.3).** *(Replaces
   the pre-placed near-spawn blight pockets of rev.2 — playtest: a pocket
   at the base ring was deleted in seconds by the starting army.)* The
   curse invades **through the economy itself**: mining out a veilstone
   node has a **15 % chance** (`CorruptionChance`) of waking the curse in
   it — the node transforms into a **resistant curse node** (Sporeling,
   deliberately tough — a real military investment to kill, not a
   starting-army snack) that immediately spreads haze over the whole
   patch, **invalidating it** (exposure taxes any further mining there).
   Your choices: kill it (collapse + residue payout reclaims the patch),
   starve it under hearth/ward/influence suppression (slower), or abandon
   the patch and find a safer one. **Nodes on suppressed ground never
   corrupt at all** (2026-08-03): the universal curse-immunity rule
   (§2.6 — "the curse can never expand into your influence") applies to
   corruption too, and the Age 0 hearth counts. Mining from secured
   ground is guaranteed safe; corruption only strikes frontier mining —
   pushing influence/wards over a patch is how you tame it. Every player mines, so everyone
   rolls the same dice: pressure stays symmetric and attributable.
   Authored `BlightPocketMarker`s remain available to map designers; the
   procedural near-spawn placement is gone. **Starting armies lose the
   Catapult** (it trivialised every early curse anchor).

**Blood and the curse (2026-08-03 rev.3):**

- **Blood inside influence fades** (tended ground is cleaned); **blood
  outside influence is ETERNAL** — the stain stays until something uses
  it. Old battlefields in the wilds are permanent marks on the map.
- **Blood + curse = spawner.** Where an eternal pool soaks CURSED
  ground, the curse quickens: the site births crystal creatures — size
  and tier scale with the pool (small → Crystalling, large →
  Veilstinger, massive → Godsplinter) — and the birth **consumes the
  pool**. Waves are a **mix**, never a monotier burst: always mostly
  Crystallings, Veilstingers only from sizeable pools, and **at most ONE
  Godsplinter per wave, only where a large battle occurred** (2026-08-04
  tuning: a first wave of 6 Godsplinters was a table-flip). Single unit
  deaths leave **small local puddles** (tight splat, visible from one
  death) rather than blobbing into one large pool. Fighting battles in the haze has consequences; leaving your
  dead on cursed ground arms it. This is the creatures' return, but as
  an emergent, player-caused, fully attributable source — never an
  aggro-driven faction.
- **Loop damping (2026-08-03 playtest: exposure deaths fed the spawner
  which killed the responders, which fed the spawner…):** (a) units
  killed **by exposure itself shed no blood** — the curse never farms
  its own spawner; only real combat feeds it; (b) blood-curse births
  are **off for the first 5 minutes** — the opening beat is securing,
  not fighting spawners; (c) **workers auto-flee** toward their Hall at
  ~3 s of exposure, before damage starts; (d) **auto-assignment never
  targets hazed nodes** — neither the AI's miner allocator nor the
  depletion auto-find will send a worker onto crusted ground (a manual
  player order still can — explicit greed); (e) the AI's dig-the-sheet
  fallback is retired with the wall model (it marched workers into the
  crust).

**The veilstone economy — the Veil precipitates veilstone:**

Veilstone deposits are ordinary iron-style outcropping nodes, but they
only exist where the curse touched ground:

| Tier | Source | Risk |
|---|---|---|
| **Corruption residue** | Killing a corrupted node's curse growth | Earned — military investment (Age 0 backbone) |
| **Recede residue** | Any crusted cell reclaimed (suppression, verbs) leaves occasional small nodes on clean ground | Safe — behind your own front |
| **Frontier eruptions** | The advancing front occasionally erupts nodes in the haze, richer with depth | Opt-in exposure tax (greed tier) |
| **Well loot fields** | A Destroyed well's slow-decay crust = dense residue | Contested, undefended (Feraldis burst income) |
| **Held wells** | Pacified / Cleansed trickle income | Top of the ladder — verb game |

Throughput is capped by node count and spacing, not worker count —
miner spam buys nothing. Curse-independent free deposits (the morning's
stopgap) are **removed**; a player who never engages the curse has no
veilstone income, by design.

**Influence is the war (2026-08-04):** suppression is now CONTESTED — a
cell is curse-immune only while the player's influence there is at/over
the threshold AND at least as strong as the curse's own influence at
that cell. Curse influence gains a **very small per-minute growth**
(`CurseInfluenceGrowthPerMinute`), so a thin influence rim that was
"just enough" in minute 10 is overrun by minute 30 — weak patches fall,
anchored cores hold. Building influence-heavy structures (watch towers
above all) over your resources is THE way to defeat the curse; the AI
biases its tower placement toward unprotected resource clusters for the
same reason.

**Fairness contract (multiplayer):** every pressure source is anchored
(wells, pockets, sporelings) at authored, symmetric positions; every
punishment lands on the neglecting player's own ground; there is no
target selection anywhere in the curse. "I addressed it" and "I didn't"
are visibly different positions by age-up.

**The sustain tether (2026-08-04 rev.4 — "the curse still does not
recede"):** an Active feeder (well or sporeling) holds crust only within
its **sustain radius** (`SustainRadiusBase` 55 m, growing to
`SustainRadiusEscalated` 85 m across the escalation ramp). Beyond the
tether, crust starves at the normal decay rate even while the feeder
lives, and tendrils refuse to extend. Kill the feeder and everything it
held collapses violently (`DestroyedDecayPerTick`); if no feeder remains
anywhere, ALL crust collapses — the curse is a territory anchored on its
wells and pockets, never one-way map paint. Corollary: destroying a
well now visibly frees the ground it projected onto.

**Leveling claims ground (2026-08-04):** each applied building upgrade
level widens and strengthens the building's influence deposit (+35%
rate, +15% radius per level). A leveled core visibly pushes the curse
where a fresh one merely holds it — upgrading is a territorial verb.

**One influence rule for every curse node (2026-08-04, "the curse
lingers even if no nodes are near"):** only a node still feeding the
curse — Active and awake — projects curse influence. Cleansed,
Converted, Destroyed, and dormant nodes deposit nothing; their old
purple footprint self-decays with the map (before this, every well kept
depositing forever regardless of state — the lingering bleed). Verb
wells cannot be removed, so while Active their influence rightly never
fades; that is the same rule, not a special one.

**The Holy Scholar (2026-08-04 — the player-facing purify flow):**
Alanthor's well ritualist (`Alanthor_Scholar`, display "Holy Scholar")
trains at the **Temple of Ridan at level 3+** (2026-08-11: gate aligned
to the shipped `minBuildingLevel: 3` — requiring max level 4 pushed the
first ritual past most matches' end) — the flow is: level
the Temple → train the Holy Scholar → send it to a well →
it channels Purification while the node births defenders at it → the
escort screens the channel. The Scholar is a **walking font**: a wide
cleanse circle (26 m vs the hero aura's 12 m) that burns crust AND
drains blood pools, so the escort fights on clean ground. The AI levels
its Temple, trains the Scholar, and escorts it heavily (10 bodyguards).
Rubble (Destroyed) wells are purifiable — consecrating a broken well
before it rebuilds is the cheapest hold there is.

**AI pursues the curse (2026-08-04):** when the corridor between an AI
army and its target is buried under deep crust, the wave is rerouted
onto the nearest curse anchor it can actually kill instead of suiciding
through the field — a Sporeling for Age 0 / Alanthor / Runai, the
nearest Active well for Feraldis. Killing the anchor collapses its
crust (tether), and the NEXT wave marches clean. The AI also refuses to
place buildings on crusted ground.

**Wells are FERALDIS-ONLY attack targets (2026-08-04 rev.2, supersedes
the "anyone can break a well" break matrix):** Age 0 and Alanthor/Runai
factions can never attack a verb well — not by order (CommandRouter
rejects it, with a notification), not by auto-acquire (wells are
excluded from target acquisition for everyone; even Feraldis breaks
wells only by deliberate order), and in a match with no Feraldis
faction wells are flatly untargetable node-side (towers and auto-fire
included). Their influence therefore persists while Active — removed
only by the verbs. Only the Feraldis culture breaks wells by force.

### 2.6 Influence — how each culture engages the curse

Post-culture, every player projects an **influence field** from culture
**anchors**. The field is the unifying mechanic: it buffs your army,
shelters you from the curse, and acts on the crust — differently per
culture. Mining stays the **universal baseline / fallback** (early game,
and when your engine is disrupted); each culture's engine is stronger.

**Anchors (the influence source, per culture):**

| Culture | Anchor | Grows influence by |
|---|---|---|
| **Alanthor** | **Forts / buildings** | Building outward (more influence → more guild slots) |
| **Runai** | **Traders** (mobile) | Trade-warriors carry influence along the lanes |
| **Feraldis** | **War Totems** (blood totems) | **Placed on a blood pool ≥ min size** (normal build flow, blood-gated placement — the "planted by a military unit" flavor is relaxed per the 2026-08-05 Feraldis pass); **feedable** (the totem drinks nearby blood into permanent **Fervor** — more blood → stronger influence); **non-decaying** (Fervor is permanent); killable. See [Age_1_Feraldis.md](Age_1_Feraldis.md) § Blood, Frenzy & War Totems |

**The field does three things:**

1. **Combat aura** for your military units inside it:
   - **Alanthor** — +defense / +HP.
   - **Feraldis** — **superseded 2026-08-05:** the Feraldis combat buff is keyed to **blood, not influence** — units frenzy on bloodsoaked ground (+attack damage / +attack speed; [Age_1_Feraldis.md](Age_1_Feraldis.md) § Blood, Frenzy & War Totems). Last-stand became the Berserker's unit mechanic. |
   - **Runai** — stealth + move speed.
2. **Curse immunity (universal):** the curse can **never expand into your
   influence**. This is *the* safe-zone mechanic — the same for everyone;
   the curse flows around influence pockets and owns the neutral middle.
3. **Effect on existing crust the field reaches** (per culture — *design
   target*; see the 2026-07-12 note below for what currently ships):
   - **Alanthor** — crust **decays** (slow recede — reclaim; not an instant
     clear, so mining stays faster for urgent passage).
   - **Runai** — crust **decays** (clears their corridors).
   - **Feraldis** — crust **corrupts** → mineable for **bonus veilstone**
     (they *keep* the wall and get rich off it, rather than clearing it).

> **Implemented 2026-07-12 (supersedes the per-culture split above for now).**
> The crust↔influence loop currently ships **culture-agnostic**:
> - **Any player's** influence ≥ `InfluenceThreshold` (0.5) marks a cell
>   curse-immune — the curse can't grow there **and existing crust decays**
>   (`VeilFieldSystem.SampleInfluence` → `VeilSpreadJob`). Feraldis's distinct
>   *corrupt-and-mine* variant is **deferred** to the Feraldis pass; today
>   Feraldis influence decays crust like everyone else.
> - **Curse influence tracks the crust footprint** (not just discs around
>   wells): `VeilFieldSystem.DepositCurseInfluence` feeds
>   `PlayerInfluenceMap.CurseChannel` from crusted cells each pulse, so the
>   curse's influence area grows and recedes with the actual crystal growth.
> - **Neutral corridors** between curse and player influence are **emergent**:
>   the influence map self-decays, so between a receding crust edge (its curse
>   influence fading) and the player's still-warded 0.5 boundary there is a band
>   that reads as neither — the required buffer, no explicit corridor code.

**Consequences (design intent):**

- **Safe zone = your influence footprint.** You are safe only where you
  hold sway; the curse owns everything else and compresses players
  together (the storm).
- **Raze the anchor → the safe zone collapses → the curse reclaims the
  ground.** Killing a fort / trader / totem removes the immunity and the
  crust floods back. **Hurting an opponent and unleashing the curse on
  them are the same action** — the curse is wired into PvP.
- **Clear vs corrupt splits the factions' relationship to the wall:**
  Alanthor & Runai influence **dissolves** the wall around them (decay),
  so they rarely mine for passage and their economies (guilds / trade)
  don't touch the curse. **Feraldis keep the wall and make it rich** —
  they don't clear it, they corrupt and mine it (or **blast it with
  Veilstone Charges**). Feraldis is the **only** culture that earns
  veilstone *from* influence.

**Feraldis specifics (curse-exploiters; economy runs on violence):**

- **Influence source = the blood map** — combat deaths on walkable ground
  build blood pools; totems anchor and are fed by them. A peaceful
  Feraldis is a broke Feraldis.
- **Veilstone boost = aggression, two loops:** (a) mine **corrupted** crust
  (the blood-fed influence edge) for a bonus; (b) **raid / pillage** enemy
  economy buildings & caravans for veilstone (`PillageSystem`).
- **Strategic quirk = Veilstone Charges:** spend veilstone to **instantly
  blast** a corridor through the wall or demolish an enemy structure — the
  violent, instant counterpart to slow mining.
- **Well-killing stays the HARD end-game win** (§2.2 / §2.4), never the economy.

Nothing here puts a unit *on* the crust: fighting, dying, and influence
sources are all on walkable ground; the field only *reaches* onto the
adjacent wall.

### 2.7 Miner infection — the curse's only creatures (implemented 2026-07-12)

The curse is a **force, not a faction**, so it fields no armies (§2.5, F2).
Its *one* source of hostile creatures is **neglect**: a miner left working
the veil edge too long is taken by it.

- **Exposure, not standing on crust.** The crust is impassable — miners can't
  stand on it. Infection reads the **haze** just outside the wall
  (`InfectionNearThreshold` = 30, below `CrustThreshold` = 80) where miners
  actually dig. Exposure (`InfectionState.Progress`, in seconds) climbs while a
  miner is in haze and **recovers while it's clear** — pull a miner out in time
  and it's saved.
- **Eruption at ~2 minutes.** At `InfectionSeconds` (120 s) of cumulative
  exposure the miner is consumed and a **hostile curse creature erupts in its
  place** (`Faction.Border`).
- **Tier scales with the match clock** — the map left to rot spawns worse
  things: **Crystalling** (< 15 min) → **Veilstinger** (< 30 min) →
  **Godsplinter** (late game, the terror).
- **Behaviour.** Eruptions rely on the standard combat targeting (they lurk and
  strike anything non-`Border` within guard range) — a local menace near the
  wall, not a coordinated army. No curse brain is revived.

This makes working the veil a **risk/reward** you manage, and it is the reason
the Crystalling/Veilstinger/Godsplinter factories survive F2. It is **not**
gated by `CurseFieldsArmies` — it is always on.

**Catch rules (2026-07-12 — supersedes §2.5's "catch-death" for units):**

- **Workers are warded.** The veil never *grows* into cells within
  `WorkerWardRadius` (8 m) of a worker, and enclosure snap-fill never fills a
  pocket a worker stands in — a digger cannot be sealed inside the wall. The
  ward blocks **growth only**: existing haze stays, so infection (above) still
  ticks on a neglected miner. Refreshed from live positions before every CA
  step, including mid-burst substeps.
- **Military units get no ward — the wall takes them.** Any non-worker unit
  standing on a cell that reaches crust (the wall grew over it) is consumed
  **immediately** and erupts as a hostile curse creature — same
  Crystalling → Veilstinger → Godsplinter match-clock ladder as infection.
  Getting caught by a burst doesn't just kill you; it recruits you.

### 2.8 The Waking — well dormancy and the three-phase match (2026-08-07, CURRENT)

**This section governs when the curse exists at all, and supersedes any
earlier reading in which wells spread from match start.**

Wells enter play **dormant**. A dormant well pumps nothing — it is excluded
from the veil field's feeder set outright — so the sheet has no source and
every cell starves at `DecayPerTick`. The map does not creep. A well wakes
**permanently** the instant any ritualist begins a verb channel on it, and
waking is **per-well**, not global.

That produces three phases, each with a different relationship to the curse:

| Phase | Curse present | Who caused it | Player question |
|---|---|---|---|
| **Early** | None. Wells dormant, map still. | Nobody | "Where is the map's value?" |
| **Mid** | Blight pockets only — local, killable, farmable | The players, by mining patches dry (§2.7) | "Is this patch worth the pocket it wakes?" |
| **Late** | Real spread, one well at a time | Whoever reaches for the verb victory | "Which well do I dare touch first?" |

**Why per-well and not a global switch.** Two properties fall out of it that a
global flag cannot give:

- **Order becomes a decision.** Every well you touch arms *that region* for
  the rest of the match. Claiming the well nearest your own base is safe
  progress that pollutes your own ground; claiming the far one first is
  greedier and leaves a woken well you don't control.
- **The curse becomes weaponisable.** Waking the well on a rival's doorstep
  costs them ground whether or not you ever finish the verb there. Starting a
  channel you fully intend to abandon is a legitimate play. This is the first
  mechanic in the design that lets one player point the curse at another.

**Trigger is channel START, not completion.** An attempt that gets interrupted
has still woken the well. There is no safe probe and no take-backs — reaching
for a well is a commitment, and it is legible to everyone the moment it
happens (banner + minimap ping naming the waker).

**Respawns wake nothing.** A well respawned by the extinction system comes back
dormant: nobody has touched it yet.

**Consequence — veilstone supply.** Precipitation only pays out on cells that
just changed state (recede-residue, frontier eruptions), so a still map earns
almost none. In the early phase veilstone comes from the seeded patches alone.
This is deliberate: it is what pushes players to mine patches dry (buying a
farmable pocket) and eventually to wake wells (buying the big supply). The
curse stops being weather and becomes something players switch on for profit.
See §9 for the open balance item this creates.

### 2.7 amendment — corruption is certain, and lands on the last bud (2026-08-07)

The **15 % roll per depletion is retired.** Mining out a veilstone node no
longer rolls dice. Instead:

> The **last live bud of a patch** corrupts with **certainty**. Every other
> node in the patch mines out clean.

Same expected number of pockets per patch — one — with a completely different
feel. The old roll fired on a node the player had no reason to treat as
special, often several nodes before the patch was spent, with no tell. The new
rule makes **the patch itself the telegraph**: you can see it thinning, you
know the last node wakes something, and you choose when to take that last
bite — now while your army is home, later on your terms, or never. A dice roll
becomes a scheduling decision.

- **Detection is by proximity** (`PatchCohesionRadius`, 18 m): no other live
  outcropping nearby means this was the last of its patch. Patches are a
  spawn-time concept with no runtime identity, and proximity matches what the
  player sees — empty ground around the node.
- **Suppressed ground stays immune** (hearth ring / player influence), per the
  universal "the curse cannot expand into your influence" rule. With
  `HallHearthRadius` at 34 m the starter patch (spawned 22–30 m out) sits
  inside the ring, so **a home patch never wakes a pocket**. The opening does
  not punish you; the guaranteed pocket lives out on the **contested** patches
  you have to leave home for.
- **An isolated node is trivially "last"** and always corrupts. Intended —
  mining a lone bud in the wild wakes something — and it makes precipitation
  nodes self-limiting: farm the residue and you re-seed the pocket that made
  it.

### 2.9 The Backlash — the price of a failed rite (2026-08-07)

**A ritual that starts and does not finish wakes the well's fury.** Break a
channel — the ritualist killed, dragged off, or interrupted for any reason —
and the curse answers with **five escalating waves of crystal creatures**
erupting from the well.

| Wave | Units | Crystallings | Veilstingers | Godsplinters |
|---|---|---|---|---|
| 1 | 10 | 10 | – | – |
| 2 | 20 | 15 | 5 | – |
| 3 | 30 | 20 | 9 | 1 |
| 4 | 40 | 24 | 14 | 2 |
| 5 | 50 | 30 | 17 | 3 |
| **Total** | **150** | 99 | 45 | 6 |

This is deliberately **endgame-scale**. The verbs sit behind Temple L3/L4 and
decide the match, so reaching for one is a commitment; the Backlash is what
makes it a commitment rather than a free retry. Fail with an army at your
back and you fight through it. Fail with a lone ritualist and you lose the
ground around the well, probably the well, and possibly the game.

**Godsplinters are rationed to 3 per wave, and never before wave 3.** The
Godsplinter is magic-siege-tank class even after its nerf (420 HP / 34 dmg /
26 range / 5 m AoE); a wave built out of them is a table-flip rather than a
fight. The same restraint `CorruptionDefenseSystem` applies for the same
reason — the difference is that the Backlash is a *punishment*, so it gets
them at all.

**Rules:**

- **Only a broken CHANNEL triggers it.** An approach that was cancelled
  before channelling began — the well already claimed, already cracked,
  already destroyed — costs nothing. You are punished for a rite you began,
  not for walking up and finding the door taken.
- **One Backlash per well at a time.** A well already erupting does not stack
  a second series; a fresh failure re-arms the sequence from wave 1 instead.
- **It outlives the ritualist.** The waves keep coming once started, whether
  or not anyone is still standing at the well. Running away does not stop it.
- **The creatures are ordinary `Faction.Border` hostiles** under normal target
  acquisition, exactly like blight-pocket eruptions (§2.7) and corruption
  defenders. No revived curse brain, no `CurseFieldsArmies` dependency.

**Why it exists.** The well's own turret used to punish a ritual by simply
killing the channeler (20.8 DPS inside 18 m against a 90 HP Scholar), which
is not counterplay, it is a locked door. The turret is now **removed
entirely — curse nodes never attack (2026-08-11)**, and the Backlash is
what restores the risk: the well no longer stops you from *trying*, it
makes *failing* expensive.

## 3. The Shardroot

One well per match — chosen **deterministically from the match seed**,
unknowable to players — hosts the Shardroot. **The first player to apply
their verb to the host well receives it**, and the artifact mechanic
begins. Since every well eventually gets verbed in normal play, the
Shardroot is guaranteed to surface; backstop: if the host well reaches
**Maw** maturity unverbed, the Shardroot becomes visibly embedded in it
(map ping) — still claimed by verbing that Maw.

### 3.1 Lifecycle (power, not victory)

- **Drop & carry**: a physical, **persistent** pickup; any unit carries
  it; the carrier is **visible to every player on the minimap**; carrier
  dies → drops in place; it walks, never teleports.
- **Store — one choice, no backsies** per holding-cycle:
  - **Hall** → awaken the culture's **Shardbound Hero** (three unique
    heroes): **Feraldis — Shardbound Warlord** *(name TBD)*, melee
    juggernaut, shard-burst magic; **Runai — Shardbound Seer** *(name
    TBD)*, mobility/economy caster, curse-sight, tether amplification;
    **Alanthor — Shardbound Hierarch** *(name TBD)*, defensive caster,
    sanctification aura, sect synergy. The hero wields it on their body.
  - **Temple** → **enshrine**: all adopted sect powers amplified while
    enshrined.
- **Volatile**: the holder — hero or Temple — **detonates on death /
  destruction**: a massive veilstone explosion damaging *everyone* in
  range (attacker included); the Shardroot drops intact from the crater.
- **The curse wants it back**: Border aggression **prioritizes the
  holder** — the anti-snowball valve.
- The Shardroot **does not win the game by itself** — it is the power
  that helps you take or defend wells. Victory is § 2.4 (or conquest).

### 3.2 Discovery aids

A fully-bloomed perched scout (expanding-vision design, [Game_AI.md § 7](Game_AI.md))
near the host well senses a **shimmer** — a soft hint rewarding scouting
presence in cursed ground.

## 4. Matchup dynamics (design targets)

| Matchup | Shape | Flashpoints | Key tuning lever |
|---|---|---|---|
| **Fer v Fer** | Demolition race; a well you destroy is dead ground for the rival | Respawn-window camping, army clashes | Revenge waves on respawn |
| **Runai v Runai** | Claim war over exclusive tethers | Acolyte assassination, tether raids | Tether HP; feral leaks unless upgraded |
| **Alan v Alan** | Fortress crawl; fonts = territorial front line; tempo rule forces expansion | Scholar sniping, frontier-font razing | Frontier refresh/cost scaling |
| **Fer v Runai** | Wolf in the pasture: every Feraldis victory step attacks Runai's income AND win | Tether sieges, raider supply lines | **Tether durability** (matchup-defining) |
| **Fer v Alan** | Siege ladder; Feraldis destroys Wild wells first to deny claim targets | Font sieges through tower chokepoints | Font+tower defense budget; Alanthor must militarize |
| **Runai v Alan** | Ritual race decided at expiry windows and unclaimed wells | Escort skirmishes, expiry camping | Map-wide expiry announcements; Wild spread punishes stalemate |

FFA: the shared well count creates implicit truces against the leader
and mercenary hold-breaking — the "collective pressure" requirement,
emergent.

## 5. Victory conditions (summary)

1. **Conquest** — eliminate all rivals (unchanged).
2. **Well domination** — all N wells simultaneously in your verb-state.

## 6. Glow removal — role reassignment

**Glow is removed as a resource.** Its former roles:

| Old Glow role | New owner |
|---|---|
| High-tier victory/power fantasy | **The Shardroot** artifact |
| T4 weapon/arrow upgrade fuel ("Glow-infused / Glow-tipped") | Renamed **Shard-infused / Shard-tipped**, consuming **Veilsteel** |
| Religious units as "Glow-generators" | Religious units are the **verb carriers** (Acolyte pacifies, Scholar purifies; the Iconoclast enables/leads Feraldis well-assaults) |
| "Drop Glow" UI / drop rules | Deleted; the Shardroot's carry/drop rules replace them |

All Glow rows in the Age 1 culture docs and
[Complete.md § 1.11](Complete.md#111-the-glow-economy) are **superseded**.

## 7. Lobby / map knobs

- **Curse intensity**: Sparse / Normal / Infested (N, maturity speed).
- **Hold duration** (default 10 min) and N are the pacing dials: small N
  = short violent races; large N = long positional games.

## 8. Implementation phases (code alignment)

Skeleton: existing `Systems/Border/` (nodes, spread, armies, escalation,
extinction, drops), Purify/Convert rituals, `NodeVictorySystem`.

**§2.5 revision reshapes this roadmap.** The Veil field + crystal renderer
(saturation grid, tendril-heartbeat spread, mining-per-cell-dug-through,
GPU-instanced crystals) are **already built** (`Components/VeilField`,
`Systems/Border/VeilFieldSystem`, `Systems/Border/Jobs/*`,
`Presentation/VeilCrystal*`). Remaining "curse as a force" work, in order:

- **F1 — Absolute wall:** stamp crusted cells impassable into the nav cost
  field (`NavCostField`); units path around; mining an edge cell re-opens
  it. Load-bearing change. **DONE 2026-07-12** — `VeilNavStampSystem` mirrors
  crust (saturation ≥ `CrustThreshold`) → `CostImpassable` using a distinct
  `NavCostField.FlagCrust` bit so cells revert cleanly to the terrain baseline
  as the crust recedes; runs after `CostFieldStampSystem` and self-heals after
  its restamps. **Companion rule G (DONE):** the CA (`VeilSpreadJob`) no longer
  grows into cells that are nav-impassable for a non-crust reason (cliffs /
  deep water / building footprints) — `VeilFieldSystem.SampleBlocked` feeds
  nav-passability into the job each pulse. **Impassable terrain stops growth.**
- **F2 — Retire the curse combat layer AS A FACTION:** no well armies, wave
  spawners, ritual-defence spawns, or `BorderAISystem` brain (all gated off by
  `BorderConstants.CurseFieldsArmies = false`); wells become passive
  spread-drivers + neutral verb objectives. **REVISED 2026-07-12 — curse
  creatures are RETAINED, but only as MINER-INFECTION eruptions** (see §2.7),
  not faction-spawned armies. The Crystalling/Veilstinger/Godsplinter factories
  stay for that one source.
- **F3 — Catch-death + telegraph:** pre-burst tell on cells about to crust;
  rapid lethal damage to engulfed units; worker auto-flee; buildings block
  spread and crumble (~15–20 s) if engulfed.
- **~~F4 — Directional dig order~~ — DROPPED 2026-07-12.** No manual
  "dig toward X" order. Each culture negates crystals its own way (Runai
  caravans reroute, Feraldis blight-diggers, Alanthor defences / decay), so
  units never *have* to dig through the wall to reach anywhere. Hand-mining the
  veil (`GatherVeilCommand`/`VeilMiningSystem`) still exists as the universal
  baseline economy — it is just no longer a *pathing* requirement.
- **AI:** v1 (path-around + retarget-to-reachable) per §2.5. (v2 directional
  digging removed with F4.)

**§2.5b (exposure model) slice list — 2026-08-03:**

- **X1 — Exposure DOT + debuff** (`VeilExposureSystem`, new): grace-gated
  depth-scaled damage + the existing BorderDebuff on crust; building
  crumble in deep crust. Gated `VeilCrustConstants.ExposureEnabled`.
- **X2 — Travel cost**: `VeilNavStampSystem` stamps finite
  saturation-scaled costs (`TravelCostEnabled`) instead of impassable;
  the old wall mode remains behind `CrustPhysical`.
- **X3 — Escalation**: dormant windows scale down with match time.
- **X4 — Hearth suppression**: Halls stamp a veil-only suppression disc
  into the CA's influence array. (Ward building: follow-up.)
- **X5 — Blight pockets**: `BlightPocketMarker` + registry + fallback
  placement near Halls; Sporeling entity feeds its patch through the CA
  well array; kill/starve → collapse → residue payout
  (`BlightPocketSystem`, new).
- **X6 — Precipitation**: crust transition tracking in `VeilFieldSystem`
  spawns recede-residue and frontier-eruption outcroppings on a budget;
  curse-independent fallback deposits near Halls removed.

1. **Veilstone rewiring** — no deposit entities on cursed maps; the Veil
   sheet is the infinite source, dug directly at field vertices
   (GatherVeilCommand), hardness scaling with well proximity.
2. **Well state machine** — the four states, hold timers, tempo-refresh
   rule, break matrix, per-player attribution; Wild maturity + blight
   ring effects; respawn revenge waves.
3. **Verb content** — Feraldis shatter→lingering slow-decay loot crust;
   Runai Tether structure + income; Alanthor Font (extend Purify) +
   influence projection; religious-unit rework.
4. **Domination victory** — win check, public well-state UI, match-point
   broadcasts (extend `NodeVictorySystem`).
5. **Shardroot** — seeded host; first-verb award; carry/beacon; Hall→hero
   (×3) / Temple→amplification, lock-in; detonation + drop;
   curse-targets-holder; scout shimmer.
6. **Glow purge** — remove Glow from tech data + code; T4 tiers →
   Shard-infused / Shard-tipped consuming Veilsteel. Low-risk,
   independent — can run first.
7. **AI** — mine crystal fields, apply the culture verb, defend holds,
   break rival holds, hunt/hold the Shardroot, dogpile a match-point
   leader.

## 9. Open items

- Hero names + kits; blight speeds; budgets; explosion damage; tether/
  font durability (the Fer-vs-ritual balance lever); hold duration; N per
  map — all TBD in balance passes.
- Whether an enshrined Temple can voluntarily eject the Shardroot
  (current canon: no — it leaves only via the vessel's destruction).

- **Early-phase veilstone supply (§2.8, opened 2026-08-07).** Dormant wells
  mean a still map, and a still map earns almost no precipitation, so the early
  economy runs on seeded patches alone. Measured against the procedural
  fallback (1 home patch/player + 6 scattered, 5 nodes × 200 = 1 000 each):

  | Critical path to your verb | Veilstone |
  |---|---|
  | Choice building (Shrine / Vault / Keep) | 70 |
  | Age up to Era 2 | 105 |
  | Temple of Ridan | 70 |
  | Temple L1→L2→L3 | 400 |
  | **Feraldis total (verb at Temple L3)** | **645** |
  | Temple L3→L4 | 400 |
  | **Alanthor total (verb at Temple L4)** | **1 045** |
  | Ritualist unit (Scholar / Iconoclast) | 0 — costs supplies + iron only |

  A home patch is 1 000. So a player who never leaves home can just reach the
  Feraldis verb (645, leaving ~355 for every building and tech that also costs
  veilstone) and **cannot reach the Alanthor verb at all** (1 045 > 1 000).
  Contesting scattered patches is mandatory, which is good — but it is the
  *only* source, and losing those fights is unrecoverable.

  Compounding it: under the §2.7 amendment every scattered patch now
  **guarantees** a Sporeling (1 800 HP, "a real military investment") and pays
  only `PocketResidueNodes × PocketResiduePerNode` = 5 × 40 = **200** veilstone
  on collapse. Spending an army to earn 200 was a marginal trade when pockets
  were a 15 % surprise; as a guaranteed cost on the supply you must contest, it
  is a bad one.

  **Recommendation (not yet applied — needs a call):** keep the scarcity, fix
  the payout. Raise `PocketResiduePerNode` 40 → **120** (600 per pocket) so the
  loop reads "mine the patch dry, clear the pocket it wakes, bank ~1 600" and
  taking a contested patch funds the Alanthor path with room to spare, while a
  stay-at-home player still stalls at 1 000. A dormant-well veilstone trickle
  was the alternative and is worse — it props up the early economy while
  removing the reason to ever wake a well.
