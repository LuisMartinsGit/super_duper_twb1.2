// Actions.jsx — ported from Game HUD/actions.jsx for the build.
// Adds React + Filigree + bridge imports; removes the mockup's
// `Object.assign(window, …)` publishing in favour of a named export.
//
// Driven by the `selection` bridge topic — derives a layout key
// (builder | building | military | multi | enemy | null) from whatever
// the C# side currently has selected. Real action wiring is a follow-up;
// for now `onAction` posts the action key over the bridge as
// `actions:invoke`.

import React from 'react';
import { FiligreeCorner, FiligreeEdge } from './Filigree.jsx';
import { useBridge, sendToUnity } from '../bridge.js';

// actions.jsx — Actions panel system.
//
// Cells are now icon-only (AoE4 style). All copy lives in the hover
// tooltip. Cells are tinted by ACTION TONE:
//   abilities    → blue
//   train units  → gold (matches default theme accent)
//   buildings    → brown
//   research     → purple
//
// Layouts per selection:
//   builder   → 3×3 grid of buildings
//   building  → two stacked zones: Train units, Research
//   military  → 3×2 of abilities
//   multi     → 3×2 of abilities
//   enemy     → empty state

// ── Tone palette — accents share similar chroma/lightness ───────────────
const TONES = {
  ability:  { color: '#4c8fe6', soft: '#7eb0f0' }, // azure
  train:    { color: '#e8b84a', soft: '#f4d77a' }, // gold
  build:    { color: '#b3793b', soft: '#d09a5c' }, // brown / copper
  research: { color: '#a878e8', soft: '#c4a5f0' }, // amethyst
};
function toneOf(kind) { return TONES[kind] || TONES.ability; }

// ── Catalogs ────────────────────────────────────────────────────────────
// Three stages drive the builder action grid. Stage is pushed by C# as the
// `builderState` bridge topic and resolved at render time. `key` matches the
// buildingId expected by BuildCommandPanel.TriggerBuildingPlacement; tone
// 'research' is reused for "special" choice buildings so they stand out.
// Costs/times in the tooltip are descriptive — the real validation happens
// inside BuilderCommandPanel when placement is committed.
//
// Cost resolution rule (Phase 3, task-108):
//   1. Live `costs` bridge topic ALWAYS wins when `costs[key]` is defined.
//   2. Static `cost: { res, n }` below is consulted ONLY when the live
//      `costs[key]` entry is `undefined`. It exists so the first-frame
//      paint matches BuildCosts.cs byte-for-byte (no 60→120 flicker on
//      Gatherer's Hut, etc.).
//   3. Neither defined → the card renders muted with a "Price unavailable"
//      sub-label and a no-op click.
//
// All static fallback numbers below mirror the DOMINANT resource line in
// Assets/Scripts/Data/TechTree/BuildingCosts.cs verbatim. If those costs
// change, this table changes too — see task-108 §Phase 3 verification.
const BUILDINGS_START = [
  // BuildCosts.cs: supplies:120, iron:10  → dominant = 120 Supplies
  { key: 'GatherersHut',    name: 'Gatherers Hut',  hint: 'Houses gatherers; passive supplies trickle.', cost: { res: 'Supplies', n: 120 }, time: 20, glyph: 'sheaf',   hotkey: 'G', tone: 'build' },
  // BuildCosts.cs: supplies:80
  { key: 'Hut',             name: 'House',          hint: 'Raises population cap.',                       cost: { res: 'Supplies', n: 80  }, time: 18, glyph: 'castle',  hotkey: 'H', tone: 'build' },
  // BuildCosts.cs: supplies:220, iron:40 → dominant = 220 Supplies
  { key: 'Barracks',        name: 'Barracks',       hint: 'Trains soldiery and heavy infantry.',          cost: { res: 'Supplies', n: 220 }, time: 30, glyph: 'crossed', hotkey: 'B', tone: 'build' },
  // BuildCosts.cs: supplies:180, iron:50 → dominant = 180 Supplies
  { key: 'ArcheryRange',    name: 'Archery Range',  hint: 'Trains archers and ranged units.',             cost: { res: 'Supplies', n: 180 }, time: 32, glyph: 'bow',     hotkey: 'A', tone: 'build' },
  // BuildCosts.cs: supplies:300, crystal:100 → dominant = 300 Supplies
  { key: 'ShrineOfAhridan', name: 'Shrine of Ridan',hint: 'Religious choice building. Unlocks age-up.',  cost: { res: 'Supplies', n: 300 }, time: 40, glyph: 'sigil',   hotkey: 'S', tone: 'research' },
  // BuildCosts.cs: supplies:300, crystal:100 → dominant = 300 Supplies
  { key: 'VaultOfAlmierra', name: 'Vault of Almiérra', hint: 'Resource choice building. Unlocks age-up.', cost: { res: 'Supplies', n: 300 }, time: 40, glyph: 'anvil',   hotkey: 'V', tone: 'research' },
  // BuildCosts.cs: supplies:300, crystal:100 → dominant = 300 Supplies
  { key: 'FiendstoneKeep',  name: 'Fiendstone Keep',hint: 'Military choice building. Unlocks age-up.',   cost: { res: 'Supplies', n: 300 }, time: 50, glyph: 'spire',   hotkey: 'K', tone: 'research' },
];

// After ANY special is started — choice committed, so the other specials
// hide. Pre-age-up: same basics minus the specials row.
const BUILDINGS_PLACING = [
  { key: 'GatherersHut',    name: 'Gatherers Hut',  hint: 'Houses gatherers; passive supplies trickle.', cost: { res: 'Supplies', n: 120 }, time: 20, glyph: 'sheaf',   hotkey: 'G', tone: 'build' },
  { key: 'Hut',             name: 'House',          hint: 'Raises population cap.',                       cost: { res: 'Supplies', n: 80  }, time: 18, glyph: 'castle',  hotkey: 'H', tone: 'build' },
  { key: 'Barracks',        name: 'Barracks',       hint: 'Trains soldiery and heavy infantry.',          cost: { res: 'Supplies', n: 220 }, time: 30, glyph: 'crossed', hotkey: 'B', tone: 'build' },
  { key: 'ArcheryRange',    name: 'Archery Range',  hint: 'Trains archers and ranged units.',             cost: { res: 'Supplies', n: 180 }, time: 32, glyph: 'bow',     hotkey: 'A', tone: 'build' },
];

// Post-age-up advanced set. Notes:
//   • "Temple of Ridan" here is the post-age-up variant — separate UI key
//     from the Stage-A "Shrine of Ridan". Until C# differentiates them
//     they both currently resolve to the same TempleOfRidan BuildType.
//   • `RoyalStable` / `VeilsteelForge` removed in task-108 §Phase 3 —
//     pure UI mockups with no TechTree entry, no factory, no
//     BuildableBuildings slot. They were marked "drop now (zero-risk)"
//     in the task-064 §A audit row. Will return once a BuildType /
//     factory exists.
//   • Static fallbacks below now mirror the dominant resource in
//     BuildCosts.cs, NOT the previous era-2 placeholder values. Live
//     `costs` always wins per the cost resolution rule above; these
//     numbers only paint the first frame before the bridge delivers.
const BUILDINGS_ERA2 = [
  // BuildCosts.cs: supplies:500, iron:150, crystal:50 → dominant = 500 Supplies.
  // Builder-built Hall: post-age-up expansion, capped at 6 per faction
  // (extractor + SpawnSelectedBuilding both enforce). Hidden until the
  // server-side action list includes "Hall" — extractor filters it out
  // pre-age-up and at the cap so the cell never appears spuriously.
  { key: 'Hall',            name: 'Hall',           hint: 'Expansion HQ. Trains workers, generates supplies, +20 population.', cost: { res: 'Supplies', n: 500 }, time: 50, glyph: 'castle',  hotkey: 'Y', tone: 'build' },
  // BuildCosts.cs: supplies:80 (era-2 reuses the base Hut cost — no separate row)
  { key: 'Hut',             name: 'House',          hint: 'Raises population cap.',                       cost: { res: 'Supplies', n: 80  }, time: 18, glyph: 'castle',  hotkey: 'H', tone: 'build' },
  // BuildCosts.cs: supplies:220, iron:40 → dominant = 220 Supplies
  { key: 'Barracks',        name: 'Barracks',       hint: 'Trains soldiery and heavy infantry.',          cost: { res: 'Supplies', n: 220 }, time: 30, glyph: 'crossed', hotkey: 'B', tone: 'build' },
  // BuildCosts.cs: supplies:180, iron:50 → dominant = 180 Supplies. ArcheryRange
  // IS wired (re-verified task-064 §A line 28) — notWired flag dropped.
  { key: 'ArcheryRange',    name: 'Archery Range',  hint: 'Trains archers and ranged units.',             cost: { res: 'Supplies', n: 180 }, time: 32, glyph: 'bow',     hotkey: 'A', tone: 'build' },
  // BuildCosts.cs: supplies:320, iron:140, crystal:60 → dominant = 320 Supplies
  { key: 'Runai_SiegeWorkshop', name: 'Siege Workshop', hint: 'Builds rams and trebuchets.',              cost: { res: 'Supplies', n: 320 }, time: 45, glyph: 'anvil',   hotkey: 'W', tone: 'build' },
  // BuildCosts.cs: supplies:300, crystal:100 → dominant = 300 Supplies
  { key: 'TempleOfRidan',   name: 'Temple of Ridan',hint: 'Era 2 temple (distinct from the early-game Shrine).', cost: { res: 'Supplies', n: 300 }, time: 50, glyph: 'sigil', hotkey: 'T', tone: 'build' },
  // BuildCosts.cs: supplies:140, iron:70 → dominant = 140 Supplies
  { key: 'Alanthor_Tower',  name: 'Watch Tower',    hint: 'Defensive tower; reveals far ground.',        cost: { res: 'Supplies', n: 140 }, time: 25, glyph: 'eye',     hotkey: 'O', tone: 'build' },
  // BuildCosts.cs: supplies:50, iron:20 → dominant = 50 Supplies
  // task-109: relabel "Wall" → "Wall Hub". Alanthor_Wall IS the hub primitive.
  // Segments form automatically between adjacent hubs (BFME2 hub-and-segment).
  { key: 'Alanthor_Wall',   name: 'Wall Hub',       hint: 'Connect to nearby Wall Hubs to auto-form walls.', cost: { res: 'Supplies', n: 50  }, time: 14, glyph: 'castle',  hotkey: 'L', tone: 'build' },
  // BuildCosts.cs: supplies:220, iron:100 → dominant = 220 Supplies.
  // Alanthor_Smelter — displayed as "Forge". Processes iron + crystal into
  // veilsteel via ForgeStorage / ForgeConversionSystem.
  { key: 'Alanthor_Smelter', name: 'Forge',         hint: 'Converts iron + crystal into veilsteel for advanced units.', cost: { res: 'Supplies', n: 220 }, time: 30, glyph: 'anvil',   hotkey: 'F', tone: 'build' },
  // BuildCosts.cs: supplies:220, iron:80 → dominant = 220 Supplies.
  // Alanthor cavalry trainer (Cataphract, plus future cavalry units).
  { key: 'Alanthor_RoyalStable', name: 'Royal Stable', hint: 'Trains heavy cavalry (Cataphract).',          cost: { res: 'Supplies', n: 220 }, time: 30, glyph: 'hooves',  hotkey: 'R', tone: 'build' },
];

function buildingsForStage(stage) {
  switch (stage) {
    case 'placing': return BUILDINGS_PLACING;
    case 'era2':    return BUILDINGS_ERA2;
    case 'start':
    default:        return BUILDINGS_START;
  }
}

// keys match unit-def IDs in TechTree.json so CommandRouter.IssueTrain
// can queue them directly when the C# bridge handles actions:invoke.
// Worker = unified Builder + Miner (Complete.md \u00a72.2). Same key the
// server expects \u2014 internally still routes through Builder.cs which
// now spawns a unit carrying both CanBuild and MinerTag.
const TRAIN_UNITS = [
  { key: 'Builder', name: 'Worker',  hint: 'Raises structures \u00b7 gathers supplies, iron and crystal.', cost: { res: 'Supplies', n: 50 },  time: 5, glyph: 'mason',  hotkey: 'W', tone: 'train' },
];

const TRAIN_BARRACKS = [
  { key: 'Swordsman', name: 'Swordsman', hint: 'Frontline melee \u00b7 main-line infantry.',  cost: { res: 'Iron',     n: 70 },  time: 16, glyph: 'helm',   hotkey: 'Q', tone: 'train' },
  { key: 'Scout',     name: 'Scout',     hint: 'Fast vision unit \u00b7 cheap to lose.',      cost: { res: 'Supplies', n: 50 },  time: 10, glyph: 'spear',  hotkey: 'W', tone: 'train' },
];

// Trainable from Archery Range. Mirrors TechTree.json ArcheryRange.trains.
// Task-110 adds Crossbowman (lvl 2) and Longbowman (lvl 3) tier units; both
// reuse the `arrow` glyph for v1 per the task brief. Live `actions` topic
// applies the actual minBuildingLevel gate server-side (renderServerActions
// path); these static rows are first-frame safety nets only.
const TRAIN_ARCHERY = [
  { key: 'Archer',      name: 'Archer',      hint: 'Long-range bow infantry \u00b7 strong vs. light units.',                cost: { res: 'Iron',     n: 25 }, time: 15, glyph: 'arrow', hotkey: 'Q', tone: 'train' },
  { key: 'Crossbowman', name: 'Crossbowman', hint: 'Slow heavy-hitter \u00b7 high damage, lower rate of fire (lvl 2).',    cost: { res: 'Supplies', n: 40 }, time: 18, glyph: 'arrow', hotkey: 'W', tone: 'train' },
  { key: 'Longbowman',  name: 'Longbowman',  hint: 'Long-range sniper \u00b7 very high range and damage, slow (lvl 3).',   cost: { res: 'Supplies', n: 50 }, time: 25, glyph: 'arrow', hotkey: 'E', tone: 'train' },
];

const TRAIN_SHRINE = [
  { key: 'Litharch', name: 'Litharch', hint: 'Channels religion \u00b7 heals allies in range.', cost: { res: 'Veilstone', n: 40 }, time: 18, glyph: 'sigil', hotkey: 'L', tone: 'train' },
];

// RESEARCH catalogue removed — the four items (Plating, Fletching, Wardings,
// Logistics) were mock placeholders that didn't exist in TechTree.json and
// none of them wired through to the actual research system in C#. The Hall
// keeps Era 2 advancement via the culture-picker flow, and per-culture
// upgrades land on the Hall's own panel once we migrate research properly.

const MILITARY_CMDS = [
  { key: 'stop',     name: 'Stop',         hint: 'Halt all current orders.',                   glyph: 'stop',     hotkey: 'S', tone: 'ability' },
  { key: 'hold',     name: 'Hold',         hint: 'Hold position \u00b7 ignore aggro.',         glyph: 'hold',     hotkey: 'H', tone: 'ability' },
  { key: 'patrol',   name: 'Patrol',       hint: 'Patrol between two marks.',                  glyph: 'patrol',   hotkey: 'P', tone: 'ability' },
  { key: 'attack',   name: 'A-Move',       hint: 'Attack-move to ground.',                     glyph: 'attack',   hotkey: 'A', tone: 'ability' },
  { key: 'stance',   name: 'Stance',       hint: 'Cycle aggressive / defensive / passive.',    glyph: 'stance',   hotkey: 'X', tone: 'ability' },
  { key: 'special',  name: 'Wardstrike',   hint: 'Channel the unit\u2019s sect ability.',      glyph: 'special',  hotkey: 'Q', tone: 'ability' },
];

const MULTI_CMDS = [
  { key: 'stop',     name: 'Stop',         hint: 'Halt all units in the detachment.',           glyph: 'stop',     hotkey: 'S', tone: 'ability' },
  { key: 'hold',     name: 'Hold',         hint: 'Hold positions \u00b7 form a wall.',          glyph: 'hold',     hotkey: 'H', tone: 'ability' },
  { key: 'patrol',   name: 'Patrol',       hint: 'Patrol the marked line.',                     glyph: 'patrol',   hotkey: 'P', tone: 'ability' },
  { key: 'attack',   name: 'A-Move',       hint: 'Attack-move to ground.',                      glyph: 'attack',   hotkey: 'A', tone: 'ability' },
  { key: 'formation',name: 'Formation',    hint: 'Cycle line / wedge / column.',                glyph: 'stance',   hotkey: 'F', tone: 'ability' },
  { key: 'retreat',  name: 'Retreat',      hint: 'Fall back to the nearest keep.',              glyph: 'special',  hotkey: 'Z', tone: 'ability' },
];

// ── Action glyphs ───────────────────────────────────────────────────────
function ActionGlyph({ kind, color, dim }) {
  const c = color; const d = dim;
  switch (kind) {
    case 'castle':
      return (
        <g stroke={c} strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" fill="none">
          <rect x="-7" y="-1" width="14" height="9" />
          <rect x="-3" y="-5" width="6" height="4" />
          <line x1="-7" y1="-1" x2="-7" y2="-4" />
          <line x1="-4" y1="-1" x2="-4" y2="-4" />
          <line x1="4"  y1="-1" x2="4"  y2="-4" />
          <line x1="7"  y1="-1" x2="7"  y2="-4" />
          <line x1="-7" y1="4"  x2="7"  y2="4" stroke={d} strokeWidth="0.8" />
        </g>
      );
    case 'crossed':
      return (
        <g stroke={c} strokeWidth="1.2" strokeLinecap="round" fill="none">
          <line x1="-6" y1="-6" x2="5" y2="5" />
          <line x1="6"  y1="-6" x2="-5" y2="5" />
          <polygon points="5,-7 7,-5 5,-3 3,-5" fill={c} stroke="none" />
          <polygon points="-5,-7 -3,-5 -5,-3 -7,-5" fill={c} stroke="none" />
          <line x1="-5" y1="5"  x2="-3" y2="7" strokeWidth="1.4" />
          <line x1="5"  y1="5"  x2="3"  y2="7" strokeWidth="1.4" />
        </g>
      );
    case 'anvil':
      return (
        <g stroke={c} strokeWidth="1.2" strokeLinejoin="round" fill="none">
          <path d="M -7 -2 L 7 -2 L 5 1 L 7 1 L 7 2 L -5 2 L -3 1 L -7 1 Z" fill={c} stroke="none" />
          <rect x="-3" y="2" width="6" height="2.5" fill={d} />
          <rect x="-5" y="4.5" width="10" height="2" fill={c} stroke="none" />
        </g>
      );
    case 'sheaf':
      return (
        <g stroke={c} strokeWidth="1.1" strokeLinecap="round" fill="none">
          <line x1="0"  y1="-8" x2="0"  y2="6" />
          <line x1="-5" y1="-6" x2="-2" y2="5" />
          <line x1="5"  y1="-6" x2="2"  y2="5" />
          <path d="M -1 -7 Q -3 -6 -2 -4 Q 0 -5 -1 -7 Z" fill={c} stroke="none" />
          <path d="M  1 -7 Q  3 -6  2 -4 Q 0 -5  1 -7 Z" fill={c} stroke="none" />
          <path d="M -6 -5 Q -7 -3 -5 -2 Q -4 -4 -6 -5 Z" fill={c} stroke="none" />
          <path d="M  6 -5 Q  7 -3  5 -2 Q  4 -4  6 -5 Z" fill={c} stroke="none" />
          <rect x="-3" y="1" width="6" height="1.6" fill={d} stroke="none" />
        </g>
      );
    case 'sigil':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none">
          <circle r="7.5" />
          <polygon points="0,-6 5.2,3 -5.2,3" />
          <circle r="1.4" fill={c} stroke="none" />
        </g>
      );
    case 'eye':
      return (
        <g stroke={c} strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" fill="none">
          <path d="M -8 0 Q 0 -6 8 0 Q 0 6 -8 0 Z" />
          <circle r="2.6" fill={c} />
          <circle cx="-0.8" cy="-0.8" r="0.7" fill={d} stroke="none" />
        </g>
      );
    case 'hooves':
      // Two horseshoe arches side by side
      return (
        <g stroke={c} strokeWidth="1.4" fill="none" strokeLinecap="round">
          <path d="M -6 4 Q -6 -4 -2 -4 Q  2 -4  2 4" />
          <path d="M -1 4 Q -1 -1  3 -1 Q  7 -1  7 4" stroke={d} />
        </g>
      );
    case 'scale':
      // Balance scale — market
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinecap="round">
          <line x1="0" y1="-7" x2="0" y2="6" />
          <line x1="-6" y1="-4" x2="6" y2="-4" />
          <path d="M -6 -4 Q -7 -1 -8 -2 Q -6 1 -4 -2 Q -5 -1 -6 -4 Z" fill={d} />
          <path d="M  6 -4 Q  7 -1  8 -2 Q  6 1  4 -2 Q  5 -1  6 -4 Z" fill={d} />
          <rect x="-3" y="6" width="6" height="1.4" fill={c} stroke="none" />
        </g>
      );
    case 'spire':
      // Tall tower
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinejoin="round">
          <polygon points="0,-8 -3,-4 -3,5 3,5 3,-4" />
          <line x1="-3" y1="-1" x2="3" y2="-1" stroke={d} strokeWidth="0.7" />
          <line x1="-3" y1="2"  x2="3" y2="2"  stroke={d} strokeWidth="0.7" />
          <circle cx="0" cy="-8" r="1.1" fill={c} stroke="none" />
          <line x1="-4" y1="5" x2="4" y2="5" strokeWidth="1.4" />
        </g>
      );
    /* ── Unit silhouettes ── */
    case 'spear':
      return (
        <g stroke={c} strokeWidth="1.3" fill="none" strokeLinecap="round">
          <line x1="-6" y1="6" x2="6" y2="-6" />
          <polygon points="6,-7 7,-3 3,-7" fill={c} stroke="none" />
          <line x1="-7" y1="6" x2="-3" y2="6" stroke={d} strokeWidth="1.5" />
        </g>
      );
    case 'bow':
      return (
        <g stroke={c} strokeWidth="1.3" fill="none" strokeLinecap="round">
          <path d="M -5 -6 Q 4 0 -5 6" />
          <line x1="-5" y1="-6" x2="-5" y2="6" stroke={d} strokeWidth="0.8" />
          <line x1="-4" y1="0" x2="6" y2="0" />
          <polygon points="6,0 4,-1.5 4,1.5" fill={c} stroke="none" />
        </g>
      );
    case 'helm':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinejoin="round">
          <path d="M -6 1 Q -6 -7 0 -7 Q 6 -7 6 1 L 6 5 Q 6 7 4 7 L -4 7 Q -6 7 -6 5 Z" fill={c} stroke="none" />
          <rect x="-4" y="-1" width="8" height="1.6" fill={d} />
          <line x1="-2" y1="1" x2="-2" y2="7" stroke={d} strokeWidth="0.7" />
          <line x1="2"  y1="1" x2="2"  y2="7" stroke={d} strokeWidth="0.7" />
        </g>
      );
    case 'mason':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinecap="round">
          <circle cx="-2" cy="-3" r="2.5" fill={c} stroke="none" />
          <path d="M -7 6 Q -7 -1 -2 -1 Q 3 -1 3 6 Z" fill={c} stroke="none" />
          <line x1="4" y1="-5" x2="4" y2="5" stroke={d} strokeWidth="1.4" />
          <rect x="2" y="-6" width="5" height="3" fill={d} stroke="none" />
        </g>
      );
    /* ── Research glyphs ── */
    case 'shield':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinejoin="round">
          <path d="M 0 -7 L 6 -4 L 6 2 Q 6 6 0 8 Q -6 6 -6 2 L -6 -4 Z" fill={c} stroke="none" />
          <path d="M 0 -5 L 4 -3 L 4 2 Q 4 5 0 6 Q -4 5 -4 2 L -4 -3 Z" fill={d} stroke="none" />
        </g>
      );
    case 'arrow':
      return (
        <g stroke={c} strokeWidth="1.4" fill="none" strokeLinecap="round" strokeLinejoin="round">
          <line x1="-7" y1="7" x2="6" y2="-6" />
          <polygon points="7,-7 7,-2 2,-7" fill={c} stroke="none" />
          <line x1="-7" y1="7" x2="-4" y2="4" stroke={d} strokeWidth="1.4" />
          <line x1="-7" y1="7" x2="-7" y2="4" stroke={d} strokeWidth="1.4" />
        </g>
      );
    case 'ward':
      // Concentric runic rings
      return (
        <g stroke={c} strokeWidth="1.2" fill="none">
          <circle r="7.5" />
          <circle r="4.5" stroke={d} strokeWidth="0.9" />
          <circle r="1.5" fill={c} stroke="none" />
          <line x1="0" y1="-7.5" x2="0" y2="-6" />
          <line x1="0" y1="7.5"  x2="0" y2="6"  />
          <line x1="-7.5" y1="0" x2="-6" y2="0" />
          <line x1="7.5"  y1="0" x2="6"  y2="0" />
        </g>
      );
    case 'gear':
      return (
        <g stroke={c} strokeWidth="1.1" fill={c} strokeLinejoin="round">
          {Array.from({ length: 8 }, (_, i) => {
            const a = (i / 8) * Math.PI * 2;
            const x = Math.cos(a) * 7, y = Math.sin(a) * 7;
            return <rect key={i} x="-1.3" y="-1.3" width="2.6" height="2.6"
                         transform={`translate(${x} ${y}) rotate(${(a * 180) / Math.PI})`}
                         stroke="none" />;
          })}
          <circle r="4.5" fill={c} stroke="none" />
          <circle r="2"   fill={d} stroke="none" />
        </g>
      );
    /* ── Command glyphs ── */
    case 'stop':
      return <g fill={c} stroke="none"><rect x="-5" y="-5" width="10" height="10" /></g>;
    case 'hold':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinejoin="round">
          <path d="M 0 -7 L 6 -4 L 6 2 Q 6 6 0 8 Q -6 6 -6 2 L -6 -4 Z" />
          <line x1="0" y1="-5" x2="0" y2="6" stroke={d} strokeWidth="0.7" />
        </g>
      );
    case 'patrol':
      return (
        <g stroke={c} strokeWidth="1.3" fill="none" strokeLinecap="round" strokeLinejoin="round">
          <path d="M -6 -3 Q 0 -8 6 -3" />
          <polygon points="5,-5 7,-3 4,-2" fill={c} stroke="none" />
          <path d="M 6 3 Q 0 8 -6 3" />
          <polygon points="-5,5 -7,3 -4,2" fill={c} stroke="none" />
        </g>
      );
    case 'attack':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none">
          <circle r="6" />
          <circle r="2.5" fill={c} />
          <line x1="-8" y1="0" x2="-4" y2="0" strokeLinecap="round" />
          <line x1="4"  y1="0" x2="8"  y2="0" strokeLinecap="round" />
          <line x1="0"  y1="-8" x2="0" y2="-4" strokeLinecap="round" />
          <line x1="0"  y1="4"  x2="0" y2="8"  strokeLinecap="round" />
        </g>
      );
    case 'stance':
      return (
        <g stroke={c} strokeWidth="1.4" fill="none" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="-5,-1 0,-6 5,-1" />
          <polyline points="-5,5 0,0 5,5" stroke={d} strokeWidth="1.2" />
        </g>
      );
    case 'special':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinecap="round">
          <polygon points="0,-7 2,-2 7,0 2,2 0,7 -2,2 -7,0 -2,-2" fill={c} stroke="none" />
          <circle r="1.4" fill={d} stroke="none" />
        </g>
      );
    default: return null;
  }
}

const ACT_HEX = '14,0 42,0 56,24.5 42,49 14,49 0,24.5';
const ACT_HEX_INNER = '17,4 39,4 52,24.5 39,45 17,45 4,24.5';

function ActionHex({ theme, glyph, tone, muted, idSuffix }) {
  const id = `act-${theme.key}-${idSuffix}`;
  const t = toneOf(tone);
  const fillColor = muted ? theme.inlayDim : t.color;
  return (
    <svg width="44" height="38" viewBox="0 0 56 49" className="act-hex-svg">
      <defs>
        <radialGradient id={`${id}-fill`} cx="50%" cy="35%" r="70%">
          <stop offset="0%" stopColor={theme.baseMid} />
          <stop offset="70%" stopColor={theme.base} />
          <stop offset="100%" stopColor={theme.baseEdge} />
        </radialGradient>
      </defs>
      <polygon
        points={ACT_HEX}
        fill={`url(#${id}-fill)`}
        stroke={muted ? theme.inlayDim : t.color}
        strokeWidth="1.5"
        strokeLinejoin="round"
        opacity={muted ? 0.7 : 1}
      />
      <polygon
        points={ACT_HEX_INNER}
        fill="none"
        stroke={theme.inlayShadow}
        strokeWidth="0.6"
        strokeLinejoin="round"
        opacity="0.85"
      />
      {!muted && (
        <g fill={t.color}>
          <circle cx="14" cy="0"    r="1" />
          <circle cx="42" cy="0"    r="1" />
          <circle cx="56" cy="24.5" r="1.1" />
          <circle cx="42" cy="49"   r="1" />
          <circle cx="14" cy="49"   r="1" />
          <circle cx="0"  cy="24.5" r="1.1" />
        </g>
      )}
      <polygon
        className="act-hex-glow"
        points={ACT_HEX}
        fill="none"
        stroke={t.color}
        strokeWidth="1.6"
        strokeLinejoin="round"
        opacity="0"
      />
      <g transform="translate(28 24.5)">
        <ActionGlyph kind={glyph} color={fillColor} dim={theme.inlay} />
      </g>
    </svg>
  );
}

function ActionCell({ theme, item, kind, onClick, onPointerEnter, onPointerLeave, size = 'lg' }) {
  const muted = item.muted === true;
  // Price unavailable = neither live `costs[key]` nor static fallback —
  // the card is muted AND shows a "Price unavailable" sub-label in the
  // tooltip kicker slot. See task-091 for the upstream fix that will
  // surface a live cost via PushCosts once the building is wired.
  const unavailable = item.unavailable === true;
  const t = toneOf(item.tone);
  const kickerByTone = {
    build:    'Construction',
    train:    'Train Unit',
    research: 'Research',
    ability:  'Command',
  };
  const kickerLabel = unavailable
    ? 'Price unavailable'
    : (kickerByTone[item.tone] || 'Action');
  const ariaLabel = unavailable
    ? `${item.name} — price unavailable`
    : item.name;
  const titleAttr = unavailable
    ? "This building isn't ready yet — cost data unavailable."
    : undefined;
  return (
    <button
      type="button"
      className={`act-cell act-cell-${size} ${muted ? 'muted' : ''} ${unavailable ? 'act-cell--unavailable' : ''}`}
      onClick={() => !muted && onClick && onClick(item.key)}
      onPointerEnter={onPointerEnter ? () => onPointerEnter(item.key) : undefined}
      onPointerLeave={onPointerLeave ? () => onPointerLeave(item.key) : undefined}
      style={{
        '--ac-base': theme.base,
        '--ac-edge': theme.baseEdge,
        '--ac-mid':  theme.baseMid,
        '--ac-inlay': theme.inlay,
        '--ac-inlay-shadow': theme.inlayShadow,
        '--ac-tone': t.color,
        '--ac-tone-soft': t.soft,
        '--ac-text': theme.text,
        '--ac-dim':  theme.textDim,
      }}
      aria-label={ariaLabel}
      aria-disabled={muted ? 'true' : undefined}
      title={titleAttr}
    >
      <span className="act-cell-hex">
        <ActionHex theme={theme} glyph={item.glyph} tone={item.tone} muted={muted} idSuffix={item.key} />
      </span>
      {item.hotkey && (
        <span className="act-cell-hotkey">{item.hotkey}</span>
      )}

      <span className="act-tooltip" style={{
        background: `linear-gradient(180deg, ${theme.base}, ${theme.baseEdge})`,
        color: theme.text,
        borderColor: theme.inlay,
        boxShadow: `0 0 0 1px ${theme.inlayShadow}, 0 0 0 2px ${t.color}22, 0 10px 24px rgba(0,0,0,0.55)`,
      }}>
        <span className="act-tooltip-head">
          <span className="act-tooltip-name" style={{ color: t.color }}>{item.name}</span>
          {item.hotkey && (
            <span className="act-tooltip-hotkey" style={{ color: theme.textDim, borderColor: theme.inlay }}>
              {item.hotkey}
            </span>
          )}
        </span>
        <span className="act-tooltip-kicker" style={{ color: theme.textDim }}>
          {kickerLabel}
        </span>
        {unavailable && (
          <span className="act-tooltip-meta">
            <span className="act-cell__hint" style={{ color: theme.textDim, opacity: 0.85 }}>
              Price unavailable
            </span>
          </span>
        )}
        {!unavailable && (item.realCost || item.cost) && (
          <span className="act-tooltip-meta">
            {item.realCost ? (
              // Multi-resource breakdown from C# costs map. Each chip turns
              // red when the player can't afford that specific resource.
              RES_KEYS.filter((k) => (item.realCost[k] || 0) > 0).map((k) => {
                // Per-resource red flag — set by withAffordability so the
                // ActionCell doesn't need the live resources prop.
                const lacking = !!(item.lacking && item.lacking[k]);
                return (
                  <span key={k} className="act-tooltip-cost">
                    <span className="act-tooltip-cost-dot" style={{
                      background: lacking ? '#e34a4a' : t.color,
                      boxShadow: `0 0 4px ${lacking ? '#e34a4a' : t.color}`,
                    }} />
                    <span style={{ color: lacking ? '#e34a4a' : theme.text }}>{item.realCost[k]}</span>
                    <span style={{ color: theme.textDim }}> {RES_LABEL[k]}</span>
                  </span>
                );
              })
            ) : (
              // Fallback for items whose id isn't in the C# costs map yet.
              <span className="act-tooltip-cost">
                <span className="act-tooltip-cost-dot" style={{ background: t.color, boxShadow: `0 0 4px ${t.color}` }} />
                <span style={{ color: theme.text }}>{item.cost.n}</span>
                <span style={{ color: theme.textDim }}> {item.cost.res}</span>
              </span>
            )}
          </span>
        )}
        <span className="act-tooltip-rule" style={{
          background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)`,
        }} />
        <span className="act-tooltip-hint" style={{ color: theme.text, opacity: 0.78 }}>
          {item.hint}
        </span>
        <span className="act-tooltip-tail" style={{ background: theme.base, borderColor: theme.inlay }} />
      </span>
    </button>
  );
}

// One labeled section inside a multi-zone Actions panel (used for buildings).
function ActionZone({ theme, label, tone, items, onAction }) {
  const t = toneOf(tone);
  return (
    <div className="act-zone">
      <div className="act-zone-head">
        <span className="act-zone-eyebrow" style={{ color: t.color }}>{label}</span>
        <span className="act-zone-rule" style={{
          background: `linear-gradient(90deg, ${t.color}55, transparent)`,
        }} />
      </div>
      <div className="act-zone-row">
        {items.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind={tone} onClick={onAction} size="sm" />
        ))}
      </div>
    </div>
  );
}

// Real costs come from C# via the `costs` bridge topic, keyed by id:
//   { Hut:{supplies:50}, Barracks:{supplies:150,iron:70}, ... }
// Note: TechTree.json's "Crystal" key maps to the resources topic's
// "veilstone" field — same physical resource, different name in code.
const RES_KEYS = ['supplies', 'iron', 'crystal', 'veilsteel', 'glow'];
const RES_TO_RESOURCE = {
  supplies:  'supplies',
  iron:      'iron',
  crystal:   'veilstone',
  veilsteel: 'veilsteel',
  glow:      'glow',
};
const RES_LABEL = {
  supplies:  'Supplies',
  iron:      'Iron',
  crystal:   'Veilstone',
  veilsteel: 'Veilsteel',
  glow:      'Glow',
};

function canAffordReal(realCost, resources) {
  if (!realCost || !resources) return true;
  for (const key of RES_KEYS) {
    const need = realCost[key] || 0;
    if (need <= 0) continue;
    const have = resources[RES_TO_RESOURCE[key]]?.value ?? 0;
    if (have < need) return false;
  }
  return true;
}

// Re-wrap a catalog: attach real cost from `costs` map, recompute muted.
// item.cost stays for fallback tooltip display; realCost is the gate.
// `lacking` is a per-resource flag map used by the tooltip to redden the
// specific resources the player doesn't have enough of.
//
// Cost resolution rule (task-108 §Phase 3):
//   1. `costs[it.key]` (live bridge topic) ALWAYS wins when defined.
//   2. Static `it.cost` is consulted ONLY when live is `undefined`.
//   3. Neither defined → `unavailable === true`; the cell renders a
//      muted "Price unavailable" card with a no-op click.
function withAffordability(items, costs, resources) {
  return items.map((it) => {
    // Step 1: pick a source. Strict undefined check — we don't want a
    // future deliberate `costs.Foo = null` to fall through to the
    // static fallback; that's an explicit "no" from the C# side.
    const live = costs ? costs[it.key] : undefined;
    const hasLive = live !== undefined && live !== null;
    const hasFallback = !!(it.cost && it.cost.res && typeof it.cost.n === 'number');
    const unavailable = !hasLive && !hasFallback;

    // realCost only fills when the live topic provides a multi-resource
    // breakdown. The tooltip falls back to it.cost when realCost is null.
    const realCost = hasLive ? live : null;

    let lacking = null;
    let unaffordable = false;

    // Preferred path: full per-resource breakdown from the C# costs
    // topic. Lets the tooltip redden specific resource chips.
    if (realCost && resources) {
      lacking = {};
      for (const k of RES_KEYS) {
        const need = realCost[k] || 0;
        if (need <= 0) continue;
        const have = resources[RES_TO_RESOURCE[k]]?.value ?? 0;
        if (have < need) { lacking[k] = true; unaffordable = true; }
      }
    }
    // Fallback: catalog items not yet in the live costs map only carry
    // a single-resource static cost `{ res: 'Iron', n: 90 }`. Still
    // gate the button on it so the player can't click an unaffordable
    // item just because the C# side hasn't emitted that key into the
    // costs map yet.
    else if (!unavailable && resources && hasFallback) {
      const resourceKey = String(it.cost.res).toLowerCase();
      const have = resources[resourceKey]?.value ?? 0;
      if (have < it.cost.n) {
        unaffordable = true;
        lacking = { _static: true };
      }
    }

    return {
      ...it,
      realCost,
      lacking,
      unavailable,
      // `notWired` items (catalog entries the C# side doesn't yet
      // accept) stay muted regardless of affordability so the player
      // can't sink resources into a build command that no-ops.
      // `unavailable` cards (no live, no fallback) are also muted to
      // signal the player they can't click them yet.
      muted: it.muted || unaffordable || unavailable || it.notWired === true,
    };
  });
}

// Convert a single server-supplied action ({key, label, tooltip, enabled,
// canAfford, cost}) into the shape the JSX cell expects ({key, name, hint,
// glyph, hotkey, tone, realCost, lacking, muted}). Realcost uses the
// authoritative per-unit cost from C# rather than the looser `costs` bridge
// map, so post-faction discounts and per-culture overrides land in the
// tooltip exactly the way the server would charge them.
function mapServerAction(a, fallbackGlyph, resources) {
  const cost = a.cost || {};
  const hasAnyCost = (cost.supplies || cost.iron || cost.crystal || cost.veilsteel || cost.glow) > 0;
  const realCost = hasAnyCost
    ? {
        supplies: cost.supplies || 0,
        iron: cost.iron || 0,
        crystal: cost.crystal || 0,
        veilsteel: cost.veilsteel || 0,
        glow: cost.glow || 0,
      }
    : null;

  // Per-resource red flag — recompute locally rather than trusting just
  // canAfford so the tooltip can redden the specific chip the player is
  // short on. (canAfford from C# is also factored into `muted` below.)
  let lacking = null;
  if (realCost && resources) {
    lacking = {};
    for (const k of RES_KEYS) {
      const need = realCost[k] || 0;
      if (need <= 0) continue;
      const have = resources[RES_TO_RESOURCE[k]]?.value ?? 0;
      if (have < need) lacking[k] = true;
    }
  }

  return {
    key: a.key,
    name: a.label || a.key,
    hint: a.tooltip || '',
    glyph: fallbackGlyph || 'helm',
    tone: 'train',
    realCost,
    lacking,
    // muted = grayed out + unclickable. Either the trainer building is
    // under-level (enabled=false) or the player can't afford the cost.
    muted: !a.enabled || !a.canAfford,
  };
}

// Render the server-supplied actions list, falling back to the static
// TRAIN_* constant if the bridge hasn't delivered an `actions` array yet
// (e.g. very first frame after selection change).
function renderServerActions(actions, fallback, costs, resources, onAction, theme, fallbackGlyph) {
  const items = (Array.isArray(actions) && actions.length > 0)
    ? actions.map((a) => mapServerAction(a, fallbackGlyph, resources))
    : withAffordability(fallback, costs, resources);
  return (
    <div className="act-grid act-grid-3x2">
      {items.map((it) => (
        <ActionCell key={it.key} theme={theme} item={it} kind="train" onClick={onAction} size="lg" />
      ))}
    </div>
  );
}

function ActionsGrid({ theme, selectionKey, builderStage, onAction, resources, costs, actions, hutAgeUp, wall, onWallHover }) {
  if (selectionKey === 'builder') {
    const list = buildingsForStage(builderStage);
    return (
      <div className="act-grid act-grid-3x3">
        {withAffordability(list, costs, resources).map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="build" onClick={onAction} size="md" />
        ))}
      </div>
    );
  }
  // Alanthor age-up hut choice (task-109 phase 2). Two large cells:
  // ConvertToWallHub (castle glyph) and ConvertToWatchTower (eye glyph).
  // While mid-conversion the panel collapses to a progress display.
  if (selectionKey === 'hutAgeUp') {
    if (hutAgeUp && hutAgeUp.kind === 'converting') {
      const total = Math.max(0.0001, hutAgeUp.total || 5);
      const remaining = Math.max(0, hutAgeUp.remaining || 0);
      const pct = Math.max(0, Math.min(1, 1 - remaining / total));
      const label = hutAgeUp.target === 'WallHub'
        ? 'Converting to Wall Hub'
        : (hutAgeUp.target === 'WatchTower' ? 'Converting to Watch Tower' : 'Converting');
      return (
        <div className="act-empty">
          <div className="act-empty-icon" style={{ color: theme.inlay }}>
            <svg width="36" height="36" viewBox="-20 -20 40 40">
              <ActionGlyph
                kind={hutAgeUp.target === 'WatchTower' ? 'eye' : 'castle'}
                color={theme.accent}
                dim={theme.inlayDim}
              />
            </svg>
          </div>
          <div className="act-empty-title" style={{ color: theme.text }}>{label}</div>
          <div className="act-empty-hint" style={{ color: theme.textDim, opacity: 0.85 }}>
            {remaining.toFixed(1)}s remaining
          </div>
          <div style={{
            marginTop: 6,
            height: 4,
            width: '70%',
            background: 'rgba(0,0,0,0.5)',
            boxShadow: `inset 0 0 0 1px ${theme.inlay}55`,
          }}>
            <div style={{
              width: `${pct * 100}%`,
              height: '100%',
              background: theme.accent,
            }} />
          </div>
        </div>
      );
    }
    // Default: emit the two converted cells. The server actions ([] when
    // mid-conversion) carry the canonical cost / canAfford / tooltip — we
    // attach the glyph + hotkey on the JSX side.
    const fallbackCells = [
      { key: 'ConvertToWallHub',    name: 'Convert to Wall Hub',    hint: 'Replaces the hut with a Wall Hub. Adjacent hubs auto-link into wall segments.', cost: { res: 'Supplies', n: 40 }, time: 5, glyph: 'castle', hotkey: 'W', tone: 'build' },
      { key: 'ConvertToWatchTower', name: 'Convert to Watch Tower', hint: 'Replaces the hut with a stand-alone Watch Tower (ranged defense).',              cost: { res: 'Supplies', n: 40 }, time: 5, glyph: 'eye',    hotkey: 'T', tone: 'build' },
    ];
    const items = (Array.isArray(actions) && actions.length > 0)
      ? actions.map((a) => {
          const mapped = mapServerAction(a, a.key === 'ConvertToWatchTower' ? 'eye' : 'castle', resources);
          // Override the tone to 'build' (brown / copper) per the Phase 1
          // design call — these cells are construction-shaped choices, not
          // unit-train cells.
          return { ...mapped, tone: 'build' };
        })
      : withAffordability(fallbackCells, costs, resources);
    return (
      <div className="act-grid act-grid-3x2">
        {items.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="build" onClick={onAction} size="lg" />
        ))}
      </div>
    );
  }
  // Wall instance / segment conversion (task-109 phase 6). Two cells:
  //   - Convert to Gate (Nx)   → segment-level conversion (Phase 5
  //                              WallSegmentUpgradeState path).
  //   - Convert to Tower       → per-instance legacy upgrade.
  // While the parent segment is mid-conversion the panel collapses to a
  // centred progress display (the Gate button dropped out server-side).
  if (selectionKey === 'wall') {
    if (wall && wall.kind === 'converting') {
      const total = Math.max(0.0001, wall.total || 8);
      const remaining = Math.max(0, wall.remaining || 0);
      const pct = Math.max(0, Math.min(1, 1 - remaining / total));
      const gateWidth = Math.max(1, Math.min(5, wall.gateWidth || 5));
      return (
        <div className="act-empty">
          <div className="act-empty-icon" style={{ color: theme.inlay }}>
            <svg width="36" height="36" viewBox="-20 -20 40 40">
              <ActionGlyph kind="spire" color={theme.accent} dim={theme.inlayDim} />
            </svg>
          </div>
          <div className="act-empty-title" style={{ color: theme.text }}>
            {`Converting to Gate (${gateWidth}x)`}
          </div>
          <div className="act-empty-hint" style={{ color: theme.textDim, opacity: 0.85 }}>
            {remaining.toFixed(1)}s remaining
          </div>
          <div style={{
            marginTop: 6,
            height: 4,
            width: '70%',
            background: 'rgba(0,0,0,0.5)',
            boxShadow: `inset 0 0 0 1px ${theme.inlay}55`,
          }}>
            <div style={{
              width: `${pct * 100}%`,
              height: '100%',
              background: theme.accent,
            }} />
          </div>
        </div>
      );
    }
    // Default: render the server-supplied actions list. Map ids to glyphs:
    //   WallSegmentToGate    → spire
    //   WallInstanceToTower  → eye
    //   BuildWall            → castle  (per-hub Build Wall action)
    const items = (Array.isArray(actions) && actions.length > 0)
      ? actions.map((a) => {
          const fallbackGlyph =
            a.key === 'WallSegmentToGate' ? 'spire' :
            a.key === 'BuildWall'         ? 'castle' :
            'eye';
          const mapped = mapServerAction(a, fallbackGlyph, resources);
          // Override the tone to 'build' — these are construction-shaped
          // conversion commits, not unit-train cells.
          mapped.tone = 'build';
          // Surface the short-segment warning on the Gate card by piggy-
          // backing on the hint line. The C# extractor already injects the
          // "Battalions wider than N may not fit." subtitle into the
          // tooltip body via BuildTooltip; here we add a small amber chip
          // to the cell's hint for at-a-glance visibility.
          if (a.key === 'WallSegmentToGate' && wall && wall.shortSegment) {
            mapped.warning = `Short segment (${wall.segmentInstanceCount} instances). Battalions wider than ${wall.gateWidth} may not fit.`;
          }
          return mapped;
        })
      : [];
    if (items.length === 0) {
      return (
        <div className="act-empty">
          <div className="act-empty-icon" style={{ color: theme.inlayDim }}>
            <svg width="28" height="28" viewBox="0 0 28 28">
              <rect x="6" y="8" width="16" height="14" fill="none" stroke="currentColor" strokeWidth="1.2" />
            </svg>
          </div>
          <div className="act-empty-title" style={{ color: theme.textDim }}>Wall</div>
          <div className="act-empty-hint" style={{ color: theme.textDim, opacity: 0.65 }}>
            No conversion available
          </div>
        </div>
      );
    }
    // Hover-preview only fires on the Gate card. The wrapper resolves the
    // wall id payload before dispatching the bridge topic. Tower has no
    // preview (per-instance, no 5-cell visual to highlight).
    const handleEnter = (key) => {
      if (key !== 'WallSegmentToGate') return;
      onWallHover && onWallHover(true);
    };
    const handleLeave = (key) => {
      if (key !== 'WallSegmentToGate') return;
      onWallHover && onWallHover(false);
    };
    return (
      <div className="act-grid act-grid-3x2">
        {items.map((it) => (
          <ActionCell
            key={it.key}
            theme={theme}
            item={it}
            kind="build"
            onClick={onAction}
            onPointerEnter={handleEnter}
            onPointerLeave={handleLeave}
            size="lg"
          />
        ))}
      </div>
    );
  }
  // Building train rosters now come from C# (HudBridge.PushSelection embeds
  // the result of EntityActionExtractor.GetTrainingActions in the selection
  // payload). The static TRAIN_* constants below stay only as a safety net
  // for the first frame after selection, and would otherwise be removed.
  if (selectionKey === 'hall') {
    return renderServerActions(actions, TRAIN_UNITS,    costs, resources, onAction, theme, 'mason');
  }
  if (selectionKey === 'barracks') {
    return renderServerActions(actions, TRAIN_BARRACKS, costs, resources, onAction, theme, 'helm');
  }
  if (selectionKey === 'archery') {
    return renderServerActions(actions, TRAIN_ARCHERY,  costs, resources, onAction, theme, 'arrow');
  }
  if (selectionKey === 'shrine') {
    return renderServerActions(actions, TRAIN_SHRINE,   costs, resources, onAction, theme, 'sigil');
  }
  // Royal Stable — heavy-cavalry trainer. Renders the server-supplied
  // actions list (Cataphract today, more cavalry units in future).
  if (selectionKey === 'stable') {
    return renderServerActions(actions, [],            costs, resources, onAction, theme, 'hooves');
  }
  // Vault — no actions wired yet, just the chrome.
  if (selectionKey === 'vault') {
    return (
      <div className="act-empty">
        <div className="act-empty-icon" style={{ color: theme.inlayDim }}>
          <svg width="28" height="28" viewBox="0 0 28 28">
            <rect x="6" y="8" width="16" height="14" fill="none" stroke="currentColor" strokeWidth="1.2" />
            <circle cx="14" cy="15" r="2.5" fill="currentColor" opacity="0.6" />
          </svg>
        </div>
        <div className="act-empty-title" style={{ color: theme.textDim }}>Vault</div>
        <div className="act-empty-hint" style={{ color: theme.textDim, opacity: 0.7 }}>
          Resource overflow protected
        </div>
      </div>
    );
  }
  // Generic building fallback — buildings without a dedicated layout
  // (GathererHut, Hut, FiendstoneKeep, Smelter, etc.) used to dump the
  // Hall's train list + a mock RESEARCH panel here. None of those buttons
  // actually fired anything in C# — the mock RESEARCH items don't exist in
  // TechTree.json, and applying Hall's units to an unrelated building was
  // wrong by construction. Show an empty card until each building gets its
  // own wired panel.
  if (selectionKey === 'building') {
    return (
      <div className="act-empty">
        <div className="act-empty-icon" style={{ color: theme.inlayDim }}>
          <svg width="36" height="36" viewBox="0 0 36 36">
            <rect x="6" y="12" width="24" height="18" fill="none" stroke="currentColor" strokeWidth="1.2" />
            <polygon points="6,12 18,3 30,12" fill="none" stroke="currentColor" strokeWidth="1.2" />
          </svg>
        </div>
        <div className="act-empty-title" style={{ color: theme.textDim }}>Structure</div>
        <div className="act-empty-hint" style={{ color: theme.textDim, opacity: 0.65 }}>
          No commands available
        </div>
      </div>
    );
  }
  if (selectionKey === 'military') {
    // Server-supplied ACTIVE abilities (King Lexor's Liquid Courage, the
    // Scout's Use Celestar, …) render first, then the generic move commands.
    const abilityCells = (Array.isArray(actions) && actions.length > 0)
      ? actions.map((a) => ({ ...mapServerAction(a, 'special', resources), tone: 'ability' }))
      : [];
    return (
      <div className="act-grid act-grid-3x2">
        {abilityCells.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="ability" onClick={onAction} size="lg" />
        ))}
        {MILITARY_CMDS.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="ability" onClick={onAction} size="lg" />
        ))}
      </div>
    );
  }
  if (selectionKey === 'multi') {
    const abilityCells = (Array.isArray(actions) && actions.length > 0)
      ? actions.map((a) => ({ ...mapServerAction(a, 'special', resources), tone: 'ability' }))
      : [];
    return (
      <div className="act-grid act-grid-3x2">
        {abilityCells.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="ability" onClick={onAction} size="lg" />
        ))}
        {MULTI_CMDS.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="ability" onClick={onAction} size="lg" />
        ))}
      </div>
    );
  }
  // enemy / null — no actions
  return (
    <div className="act-empty">
      <div className="act-empty-icon" style={{ color: theme.inlayDim }}>
        <svg width="28" height="28" viewBox="0 0 28 28">
          <circle cx="14" cy="14" r="11" fill="none" stroke="currentColor" strokeWidth="1.1" />
          <line x1="6" y1="22" x2="22" y2="6" stroke="currentColor" strokeWidth="1.1" />
        </svg>
      </div>
      <div className="act-empty-title" style={{ color: theme.textDim }}>No Commands</div>
      <div className="act-empty-hint" style={{ color: theme.textDim, opacity: 0.7 }}>
        You cannot order this target
      </div>
    </div>
  );
}

// ── Actions panel chrome — sits to the right of the Selection panel ─────
function ActionsPanel({ theme, selection, onAction, resources, costs, builderStage, actions, hutAgeUp, wall, onWallHover }) {
  const lvl = theme.ornament;
  const hasSelection = !!selection;
  const label =
    selection === 'builder'   ? 'Construct'    :
    selection === 'building'  ? 'Operate'      :
    selection === 'multi'     ? 'Group Orders' :
    selection === 'military'  ? 'Orders'       :
    selection === 'enemy'     ? 'Target'       :
    selection === 'hutAgeUp'  ? 'Age-Up Choice':
    selection === 'wall'      ? (wall && wall.kind === 'hub' ? 'Wall Hub' : 'Wall Segment') :
    'Actions';

  return (
    <div className="rc-root rc-v act-panel" style={{
      '--rc-base': theme.base,
      '--rc-mid': theme.baseMid,
      '--rc-edge': theme.baseEdge,
      '--rc-inlay': theme.inlay,
      '--rc-inlay-shadow': theme.inlayShadow,
      '--rc-accent': theme.accent,
    }}>
      <div className="rc-frame act-frame">
        <div className="rc-plate" />
        <div className="rc-inlay" />
        <div className="rc-corner rc-corner-tl">
          <FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
        </div>
        <div className="rc-corner rc-corner-tr">
          <FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
        </div>
        <div className="rc-corner rc-corner-bl">
          <FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
        </div>
        <div className="rc-corner rc-corner-br">
          <FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
        </div>
        {lvl !== 'minimal' && (
          <>
            <div className="rc-edge rc-edge-top">
              <FiligreeEdge width={220} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
            </div>
            <div className="rc-edge rc-edge-bot">
              <FiligreeEdge width={220} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
            </div>
          </>
        )}

        {!hasSelection && (
          <div className="act-panel-empty">
            <div className="act-panel-empty-mark" style={{ color: theme.inlayDim }}>
              <svg width="44" height="44" viewBox="0 0 44 44">
                <polygon points="22,3 41,22 22,41 3,22" fill="none" stroke="currentColor" strokeWidth="1.1" />
                <polygon points="22,11 33,22 22,33 11,22" fill="none" stroke="currentColor" strokeWidth="0.8" opacity="0.6" />
                <circle cx="22" cy="22" r="2.4" fill="currentColor" opacity="0.7" />
              </svg>
            </div>
            <div className="act-panel-empty-title" style={{ color: theme.textDim }}>Actions</div>
            <div className="act-panel-empty-hint" style={{ color: theme.textDim, opacity: 0.65 }}>
              Select a unit to issue commands
            </div>
          </div>
        )}

        {hasSelection && (
          <div className="act-panel-body">
            <div className="act-panel-head">
              <span className="act-panel-eyebrow" style={{ color: theme.textDim }}>{label}</span>
              <span className="act-panel-rule" style={{
                background: `linear-gradient(90deg, transparent, ${theme.inlay}66, transparent)`,
              }} />
            </div>
            <ActionsGrid theme={theme} selectionKey={selection} builderStage={builderStage} onAction={onAction} resources={resources} costs={costs} actions={actions} hutAgeUp={hutAgeUp} wall={wall} onWallHover={onWallHover} />
          </div>
        )}
      </div>
    </div>
  );
}

// Bridge-driven wrapper — picks the layout key from the current `selection`
// bridge payload so the panel mirrors whatever Unity says is selected.
// Single Builders show construction; structures show train + research;
// military / multi show command grids; everything else collapses to the
// empty state.
function deriveSelectionKey(sel) {
  if (!sel) return null;
  if (sel.kind === 'multi') return 'multi';
  if (sel.kind === 'single') {
    if (sel.portraitTone === 'enemy') return 'enemy';
    // Hut age-up choice (task-109 phase 2). Tested before the building
    // name match so a Gatherer's Hut tagged with GathererHutAgeUpChoice
    // jumps straight into the dedicated layout instead of falling through
    // to the generic 'building' empty card.
    if (sel.hutAgeUp && (sel.hutAgeUp.kind === 'choice' || sel.hutAgeUp.kind === 'converting')) {
      return 'hutAgeUp';
    }
    // Wall instance / segment conversion (task-109 phase 6) AND the per-hub
    // "Build Wall" action (kind === 'hub'). Tested before the building name
    // match so a selected wall piece jumps to the dedicated Gate / Tower /
    // Build-Wall layout instead of the generic 'building' card.
    if (sel.wall && (sel.wall.kind === 'instance' || sel.wall.kind === 'segment' || sel.wall.kind === 'converting' || sel.wall.kind === 'hub')) {
      return 'wall';
    }
    const name = (sel.name || '').toLowerCase();

    // Specific building → specific layout. Names come straight out of
    // EntityInfoExtractor.GetBuildingTypeName (e.g. "Hall", "Barracks",
    // "Gatherer's Hut") so we match on the bare keyword.
    if (name.includes('hall'))     return 'hall';
    if (name.includes('barracks')) return 'barracks';
    // EntityInfoExtractor sends "Archery Range" for ArcheryRangeTag — match
    // on "archery" so the panel shows the Archer train action instead of
    // falling through to the generic 'building' bucket (which had a stale
    // TRAIN_UNITS fallback showing Builder/Miner).
    if (name.includes('archery')) return 'archery';
    // Royal Stable — heavy-cavalry trainer. EntityInfoExtractor returns
    // "Royal Stable" from RoyalStableTag.
    if (name.includes('stable'))  return 'stable';
    // "Shrine of Ahridan" and "Temple of Ridan" share the train-Litharch
    // layout (both have ShrineTag/TempleOfRidanTag in the TechTree).
    if (name.includes('shrine') || name.includes('temple')) return 'shrine';
    if (name.includes('vault'))    return 'vault';

    // Worker / Builder UNITS (not buildings) get the buildings catalog
    // so they can queue construction. Match 'worker' (the unified
    // Worker name post-Builder+Miner merge), 'builder' (legacy name
    // still surfaced by old saves), and 'mason' (culture-upgraded
    // variant).
    if (name.includes('worker') || name.includes('builder') || name.includes('mason')) return 'builder';

    const klass = (sel.klass || '').toLowerCase();
    if (klass.includes('structure') || klass.includes('building')) return 'building';
    return 'military';
  }
  return null;
}

export function ActionsPanelBridged({ theme }) {
  const sel = useBridge('selection', null);
  const resources = useBridge('resources', null);
  const costs = useBridge('costs', null);
  // builderState pushed by HudBridge: { stage: 'start' | 'placing' | 'era2' }
  // Drives which BUILDINGS catalog renders when a Builder is selected.
  const builderState = useBridge('builderState', null);
  // Hide the entire panel when nothing is selected — matches the Selection
  // panel's behaviour so the player sees a clean HUD with no empty frames.
  if (!sel) return null;
  const key = deriveSelectionKey(sel);
  // Hut age-up clicks fan out to a dedicated topic (actions:convertHut)
  // rather than actions:invoke — payload carries {entityId, target}.
  // Wall conversion clicks fan out to actions:convertWallSegmentToGate
  // (Gate) or stay on the generic actions:invoke path (Tower) — but the
  // Tower path doesn't actually use that — it falls through to the
  // ActionPanelRegion bridge instead since wallToTower is a per-instance
  // legacy path that doesn't need a JSX intermediary. To keep it simple
  // we route the Tower click through the C#-side dispatcher by posting
  // actions:convertWallSegmentToGate with a Tower marker — but the design
  // spec calls for the segment-Gate path only; the Tower path stays the
  // legacy per-instance route. So Tower clicks are dropped here (we route
  // Gate via the dedicated topic and ignore Tower in the bridge — the
  // C# ActionPanelRegion handles the in-game UIToolkit Tower path).
  const onAction = (actionKey) => {
    if (key === 'hutAgeUp') {
      const target = actionKey === 'ConvertToWatchTower' ? 'WatchTower' : 'WallHub';
      sendToUnity('actions:convertHut', { entityId: sel.id, target });
      return;
    }
    if (key === 'wall') {
      if (actionKey === 'WallSegmentToGate') {
        const segmentId = sel?.wall?.segmentId ?? 0;
        const focusInstanceId = sel?.wall?.focusInstanceId ?? sel.id;
        sendToUnity('actions:convertWallSegmentToGate', { segmentId, focusInstanceId });
        return;
      }
      if (actionKey === 'WallInstanceToTower') {
        // No JSX→C# topic for the legacy per-instance Tower path yet —
        // the UIToolkit ActionPanelRegion (in-game native) handles it.
        return;
      }
      // Per-hub Build Wall action and any future hub-anchored extensions
      // ride the generic actions:invoke path. HudBridge.HandleActionInvoke
      // recognises key=="BuildWall" and routes to BuilderCommandPanel's
      // hub-anchored placement mode.
      sendToUnity('actions:invoke', { key: actionKey, selectionKind: key });
      return;
    }
    sendToUnity('actions:invoke', { key: actionKey, selectionKind: key });
  };
  // Hover on the Gate card → toggle a presentation-only highlight tag
  // on the 5 candidate instances. Pure local-client state via wall:previewGate.
  const onWallHover = (on) => {
    if (!sel?.wall) return;
    const segmentId = sel.wall.segmentId ?? 0;
    const focusInstanceId = sel.wall.focusInstanceId ?? sel.id;
    sendToUnity('wall:previewGate', { segmentId, focusInstanceId, on });
  };
  return <ActionsPanel theme={theme}
    selection={key}
    builderStage={builderState?.stage}
    resources={resources}
    costs={costs}
    actions={sel?.actions || null}
    hutAgeUp={sel?.hutAgeUp || null}
    wall={sel?.wall || null}
    onWallHover={onWallHover}
    onAction={onAction} />;
}

export { ActionsPanel, ActionsGrid, ActionGlyph, BUILDINGS_START, BUILDINGS_PLACING, BUILDINGS_ERA2, TRAIN_UNITS };
