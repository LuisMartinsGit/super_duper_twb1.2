import React, { useState, useEffect } from "react";

/**
 * Tech Tree viewer — GENERATED from the game's SO assets by
 * tools/gen_calculator.py (the DEFAULT_SCHEMA / DEFAULT / DEFAULT_ABILITIES
 * blocks below are rewritten on every run; do not hand-edit them).
 * The editing UI still works but is scratch only: nothing is persisted.
 *
 *  - Buildings, units, research and upgrades; add / edit / delete everything.
 *  - Units carry classification tags: type / race / size.
 *  - Research is toggleable and its EFFECTS mutate the target's stats. A target
 *    can be: the building, a specific unit, or ALL units of a type / race / size
 *    (e.g. "+20% HP to all Melee", "+10 damage to all Machine").
 *  - Effect stats are picked from editable STAT-DEFINITION tables (building stats
 *    and unit stats), shown on the right. Those tables feed the dropdowns.
 *  - Cost scope per upgrade: faction-wide (paid once) or per-building. Costs use
 *    🌾 supplies · ⛏️ iron · 💎 veilstone (crystal) · 🔷 veilsteel.
 *  - Side calculator: effective stats with researched upgrades, plus one-time
 *    and per-building cost rollups. Export / Import JSON; Reset reloads the SO data.
 *
 * Drop <TechTree /> into a React 18 app. Styles are scoped under `.ttc`.
 */

const AGES = ["Age 0", "Age 1", "Age 2", "Age 3"];
const RES = [["s", "🌾", "Supplies"], ["i", "⛏️", "Iron"], ["v", "💎", "Veilstone"], ["vs", "🔷", "Veilsteel"]];
const KINDS = [["lvl", "Base level"], ["eco", "Economy"], ["tech", "Technology / research"], ["mil", "Military unit"]];
const OUTLINES = [["", "None"], ["mil", "Military (red)"], ["eco", "Economy (green)"]];
const MODS = [["+", "+/− flat"], ["+%", "+/− percent"]];
const BUILDING_TYPES = ["Basic", "Alanthor", "Runaii", "Feraldis", "Curse"];
const CULTURES = ["Alanthor", "Runaii", "Feraldis"];
const UNIT_TYPES = ["Economic", "Melee", "Ranged", "Cavalry", "Siege", "Magic", "Curse"];
const UNIT_RACES = ["Human", "Crystal", "Soulless", "Machine", "Automaton"];
const UNIT_SIZES = ["Small", "Medium", "Large", "Colossal"];
const ABILITY_ACTIVATIONS = ["Active", "Passive", "OnDeath"];
const ABILITY_TYPES = ["self", "single target", "area", "aura", "global"];
const emptyCost = () => ({ s: 0, i: 0, v: 0, vs: 0 });
const addCost = (a, b) => ({ s: a.s + b.s, i: a.i + b.i, v: a.v + b.v, vs: a.vs + b.vs });
const scaleCost = (c, n) => ({ s: c.s * n, i: c.i * n, v: c.v * n, vs: c.vs * n });
let _uid = 0;
const uid = () => "n" + Date.now().toString(36) + (_uid++).toString(36);
const fmt = (n) => (Math.round(n * 100) / 100).toString();
function applyEffect(val, op, value) {
  const s = String(val), m = s.match(/-?\d+(\.\d+)?/);
  if (op === "set") return String(value);
  if (!m) return s;
  const num = parseFloat(m[0]); let r = num;
  if (op === "+") r = num + value; else if (op === "-") r = num - value;
  else if (op === "*") r = num * value; else if (op === "+%") r = num * (1 + value / 100);
  else if (op === "-%") r = num * (1 - value / 100);
  return s.replace(m[0], fmt(r));
}

const DEFAULT_SCHEMA = {
  "buildingStats": [
    ["id", "SO id"],
    ["Building Type", "enum: Basic, Alanthor"],
    ["HP", "hp"],
    ["Armor type", "text"],
    ["Melee defense", "number"],
    ["Ranged defense", "number"],
    ["Siege defense", "number"],
    ["Magic defense", "number"],
    ["Line of sight", "units"],
    ["Building radius", "units"],
    ["Footprint (cells)", "2 m cells"],
    ["Build time", "seconds"],
    ["Population", "pop"],
    ["Supplies per tick", "supplies"],
    ["Supplies interval", "seconds"],
    ["Slot income per min (L1/L2/L3)", "per minute"],
    ["Interest per minute", "per minute"],
    ["Interest principal cap", "resource"],
    ["Interest per minute (Coffers / Charters / Bonds)", "per minute"],
    ["Max per faction", "count"],
    ["Min wall level", "level"],
    ["Garrison slots", "units"],
    ["Garrison arrows per occupant", "arrows"],
    ["Max iron", "iron"],
    ["Max veilstone", "veilstone"],
    ["Segment HP", "hp"],
    ["Segment line of sight", "units"],
    ["Min era", "era"],
    ["Can attack", "boolean"],
    ["Attack damage", "number"],
    ["Attack damage type", "text"],
    ["Attack range", "units"],
    ["Attack cooldown", "seconds"],
    ["Auto-fire max targets", "number"],
    ["Train speed bonus", "percent"],
    ["Trains", "text"],
    ["Research", "text"],
    ["Tags", "text"],
    ["PresentationId", "id"]
  ],
  "unitStats": [
    ["Id", "SO id"],
    ["Class", "text"],
    ["HP", "hp"],
    ["Speed", "units/s"],
    ["Training time", "seconds"],
    ["Population cost", "pop"],
    ["Damage", "number"],
    ["Damage type", "text"],
    ["Armor type", "text"],
    ["Melee defense", "number"],
    ["Ranged defense", "number"],
    ["Siege defense", "number"],
    ["Magic defense", "number"],
    ["Attack cooldown", "seconds"],
    ["Min attack range", "units"],
    ["Attack range", "units"],
    ["Line of sight", "units"],
    ["Aim time", "seconds"],
    ["Radius", "units"],
    ["Trajectory", "text"],
    ["Projectile speed", "units/s"],
    ["Min building level", "level"],
    ["Build speed", "/s"],
    ["Gathering speed", "/s"],
    ["Heals per second", "hp/s"],
    ["Heal range", "units"],
    ["Siege range", "units"],
    ["Siege cooldown", "seconds"],
    ["AoE radius", "units"],
    ["Bonus vs tags", "text"],
    ["Tags", "text"],
    ["Abilities", "text"],
    ["PresentationId", "id"]
  ]
};

const DEFAULT = [
  {
    "id": "so_b_Fortress",
    "banner": "Shelter -> Fortress (Alanthor) - Capital / HQ / Research Era / Research Economy / Worker upgrades / resource drop-off / Supply generation / Objective",
    "mil": true,
    "cost": {"s": 1200, "i": 1200, "v": 300, "vs": 0},
    "stats": [["id", "Fortress"], ["Building Type", "Basic"], ["HP", "3600"], ["Armor type", "structure_human"], ["Melee defense", "12"], ["Ranged defense", "18"], ["Siege defense", "2"], ["Magic defense", "8"], ["Line of sight", "30"], ["Building radius", "2"], ["Footprint (cells)", "5 x 5"], ["Build time", "120"], ["Population", "10"], ["Supplies per tick", "50"], ["Supplies interval", "15"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "yes"], ["Attack damage", "20"], ["Attack damage type", "ranged"], ["Attack range", "34"], ["Attack cooldown", "2"], ["Auto-fire max targets", "1"], ["Train speed bonus", "0"], ["Trains", "Worker, Scout, Ledger, King Lexor"], ["Research", "Armed Scouts, Iron Tools, Mason Guild, Scouting Celestarii, Stone Tools, Veilsteel Tools, Veilstone Tools"], ["Tags", "Building"], ["PresentationId", "105"]],
    "nodes": [
      {"id": "so_lvl_Fortress_0", "a": 0, "r": 1, "t": "Shelter - Lv 0", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["Capital / HQ / Research Era / Research Economy / Worker upgrades / resource drop-off / Supply generation / Objective", "The capital - the Shelter in Age 0, the Fortress from age-up. Every player starts the match with one, and more can be raised - one per territory, the most expensive building in the game. Trains Workers and Scouts (and, for Alanthor, the Ledger and King Lexor), hosts the capital's research, and LOCKS its territory so no army can take it while it stands."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Fortress_1", "a": 1, "r": 1, "t": "Fortress - Lv 1", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909", "Auto-fire max targets 1"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Fortress_2", "a": 2, "r": 1, "t": "Fortress - Lv 2", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87", "Auto-fire max targets 2"], "req": ["Fortress - Lv 1"], "stats": [], "cost": {"s": 447, "i": 100, "v": 0, "vs": 0}, "up": true, "researchTime": 65.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Fortress_3", "a": 3, "r": 1, "t": "Fortress - Lv 3", "k": "lvl", "o": "mil", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833", "Auto-fire max targets 3"], "req": ["Fortress - Lv 2"], "stats": [], "cost": {"s": 767, "i": 220, "v": 0, "vs": 0}, "up": true, "researchTime": 90.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Fortress_Worker", "a": 0, "r": 2, "t": "Worker", "k": "eco", "o": "", "arw": false, "type": "Economic", "race": "", "size": "", "d": ["Trained at Shelter"], "req": [], "stats": [["Id", "Worker"], ["Class", "human_support"], ["HP", "70"], ["Speed", "6"], ["Training time", "22.4"], ["Population cost", "1"], ["Damage", "2"], ["Damage type", "melee"], ["Armor type", "infantry_light"], ["Melee defense", "0"], ["Ranged defense", "0"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "0"], ["Min attack range", "0"], ["Attack range", "1"], ["Line of sight", "14"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "0"], ["Build speed", "0"], ["Gathering speed", "1"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "none"], ["Tags", "Infantry, Light"], ["Abilities", "none"], ["PresentationId", "200"]], "cost": {"s": 60, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Fortress_Scout", "a": 0, "r": 3, "t": "Scout", "k": "mil", "o": "", "arw": false, "type": "Melee", "race": "", "size": "", "d": ["Trained at Shelter"], "req": [], "stats": [["Id", "Scout"], ["Class", "human_scout"], ["HP", "60"], ["Speed", "6"], ["Training time", "18.2"], ["Population cost", "1"], ["Damage", "0"], ["Damage type", "melee"], ["Armor type", "infantry_light"], ["Melee defense", "0"], ["Ranged defense", "1"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "1"], ["Min attack range", "0"], ["Attack range", "1"], ["Line of sight", "40"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "0"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "none"], ["Tags", "Infantry, Light"], ["Abilities", "none"], ["PresentationId", "206"]], "cost": {"s": 30, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Fortress_Ledger", "a": 2, "r": 2, "t": "Ledger", "k": "eco", "o": "", "arw": false, "type": "Economic", "race": "", "size": "", "d": ["Trained at Shelter"], "req": ["Fortress - Lv 2"], "stats": [["Id", "Ledger"], ["Class", "support"], ["HP", "140"], ["Speed", "3.5"], ["Training time", "21"], ["Population cost", "1"], ["Damage", "0"], ["Damage type", "melee"], ["Armor type", "structure_light"], ["Melee defense", "0"], ["Ranged defense", "0"], ["Siege defense", "0"], ["Magic defense", "2"], ["Attack cooldown", "0"], ["Min attack range", "0"], ["Attack range", "0"], ["Line of sight", "10"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "2"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "none"], ["Tags", "Infantry, Light"], ["Abilities", "Automate Facility"], ["PresentationId", "250"]], "cost": {"s": 0, "i": 22, "v": 86, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": ["Automate Facility"]},
      {"id": "so_unit_Fortress_King_Lexor", "a": 3, "r": 2, "t": "King Lexor", "k": "mil", "o": "", "arw": false, "type": "Cavalry", "race": "", "size": "", "d": ["Trained at Shelter"], "req": ["Fortress - Lv 3"], "stats": [["Id", "King Lexor"], ["Class", "melee"], ["HP", "650"], ["Speed", "7"], ["Training time", "63"], ["Population cost", "3"], ["Damage", "45"], ["Damage type", "melee"], ["Armor type", "cavalry_heavy"], ["Melee defense", "6"], ["Ranged defense", "5"], ["Siege defense", "0"], ["Magic defense", "3"], ["Attack cooldown", "1.4"], ["Min attack range", "0"], ["Attack range", "1"], ["Line of sight", "26"], ["Aim time", "0"], ["Radius", "0.6"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "3"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "none"], ["Tags", "Cavalry, Heavy"], ["Abilities", "King's Call, Liquid Courage, Honour thy Pledge"], ["PresentationId", "251"]], "cost": {"s": 0, "i": 138, "v": 350, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": ["King's Call", "Liquid Courage", "Honour thy Pledge"]},
      {"id": "so_tech_ArmedScouts", "a": 0, "r": 4, "t": "Armed Scouts", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Arms your Scouts with blades: Scouts gain a melee attack. Until researched Scouts are vision-only."], "req": [], "stats": [], "cost": {"s": 90, "i": 30, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "", "scope": "global", "on": false, "effects": [{"target": "unit:Scout", "stat": "Damage", "op": "set", "value": 2.0}], "abilities": []},
      {"id": "so_tech_IronTools", "a": 2, "r": 3, "t": "Iron Tools", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Tool tier 2 — Alanthor. Workers raise structures 30 percent faster.", "faction: BuildSpeed Pct 30"], "req": ["Fortress - Lv 2"], "stats": [], "cost": {"s": 150, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_MasonGuild", "a": 2, "r": 4, "t": "Mason Guild", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor. +30% HP on all your buildings (behaviour-by-id).", "building:*: BuildingHp Pct 30"], "req": ["Fortress - Lv 2"], "stats": [], "cost": {"s": 180, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_ScoutingCelestarii", "a": 1, "r": 2, "t": "Scouting Celestarii", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the Use Celestar reveal ability on Scouts and restores full Scout vision: settled max LOS and sight-ramp speed return to 100 percent (behaviour-by-id)."], "req": [], "stats": [], "cost": {"s": 120, "i": 30, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_StoneTools", "a": 0, "r": 5, "t": "Stone Tools", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Better tools: workers raise structures 15 percent faster.", "faction: BuildSpeed Pct 15"], "req": [], "stats": [], "cost": {"s": 80, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilsteelTools", "a": 3, "r": 3, "t": "Veilsteel Tools", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Tool tier 4 — Alanthor. Workers raise structures 75 percent faster.", "faction: BuildSpeed Pct 75"], "req": ["Fortress - Lv 3"], "stats": [], "cost": {"s": 387, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilstoneTools", "a": 3, "r": 4, "t": "Veilstone Tools", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Tool tier 3 — Alanthor. Workers raise structures 50 percent faster.", "faction: BuildSpeed Pct 50"], "req": ["Fortress - Lv 3"], "stats": [], "cost": {"s": 67, "i": 120, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Barracks",
    "banner": "Barracks -> Garrison (Alanthor) - Train infantry — basics at L1, advanced at L2",
    "mil": true,
    "cost": {"s": 220, "i": 40, "v": 0, "vs": 0},
    "stats": [["id", "Barracks"], ["Building Type", "Basic"], ["HP", "800"], ["Armor type", "structure_human"], ["Melee defense", "5"], ["Ranged defense", "11"], ["Siege defense", "0"], ["Magic defense", "2"], ["Line of sight", "18"], ["Building radius", "1.6"], ["Footprint (cells)", "4 x 4"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "Spearman, Swordsman, Sentinel, Nobleman"], ["Research", "Charge, Conscription, Elite Infantry, Iron Weapons, Seasoned Infantry, Shard-infused Weapons, Shield Wall, Stone Weapons, Veilstone Weapons, Veteran Infantry, Iron Plate, Shard Plate, Veilstone Plate"], ["Tags", "Building"], ["PresentationId", "510"]],
    "nodes": [
      {"id": "so_lvl_Barracks_0", "a": 0, "r": 1, "t": "Barracks - Lv 0", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["Train infantry — basics at L1, advanced at L2", "Primary melee training building. Produces Spearmen and researches melee upgrades. Becomes the culture's melee structure (Garrison / Route Guard / Longhouse) at age-up."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Barracks_1", "a": 1, "r": 1, "t": "Garrison - Lv 1", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Barracks_2", "a": 2, "r": 1, "t": "Garrison - Lv 2", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87"], "req": ["Garrison - Lv 1"], "stats": [], "cost": {"s": 167, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Barracks_3", "a": 3, "r": 1, "t": "Garrison - Lv 3", "k": "lvl", "o": "mil", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833", "Attack 8 ranged, range 18, every 2.083 s, 1 target(s)"], "req": ["Garrison - Lv 2"], "stats": [], "cost": {"s": 340, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Barracks_Spearman", "a": 0, "r": 2, "t": "Spearman", "k": "mil", "o": "", "arw": false, "type": "Melee", "race": "", "size": "", "d": ["Trained at Barracks"], "req": [], "stats": [["Id", "Spearman"], ["Class", "human_melee"], ["HP", "120"], ["Speed", "5"], ["Training time", "15.4"], ["Population cost", "1"], ["Damage", "10"], ["Damage type", "melee"], ["Armor type", "infantry_heavy"], ["Melee defense", "1"], ["Ranged defense", "1"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "1.5"], ["Min attack range", "0"], ["Attack range", "1.5"], ["Line of sight", "16"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "0"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+22 vs Cavalry"], ["Tags", "Infantry, Heavy"], ["Abilities", "none"], ["PresentationId", "368"]], "cost": {"s": 44, "i": 16, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Barracks_Alanthor_Swordsman", "a": 1, "r": 2, "t": "Swordsman", "k": "mil", "o": "", "arw": false, "type": "Melee", "race": "", "size": "", "d": ["Trained at Barracks"], "req": ["Garrison - Lv 1"], "stats": [["Id", "Alanthor_Swordsman"], ["Class", "human_melee"], ["HP", "120"], ["Speed", "5.5"], ["Training time", "16.8"], ["Population cost", "1"], ["Damage", "14"], ["Damage type", "melee"], ["Armor type", "infantry_heavy"], ["Melee defense", "4"], ["Ranged defense", "2"], ["Siege defense", "0"], ["Magic defense", "1"], ["Attack cooldown", "1.4"], ["Min attack range", "0"], ["Attack range", "1"], ["Line of sight", "16"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "1"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+10 vs Siege"], ["Tags", "Infantry, Heavy"], ["Abilities", "none"], ["PresentationId", "201"]], "cost": {"s": 0, "i": 25, "v": 55, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Barracks_Alanthor_Sentinel", "a": 3, "r": 2, "t": "Sentinel", "k": "mil", "o": "", "arw": false, "type": "Melee", "race": "", "size": "", "d": ["Trained at Barracks"], "req": ["Garrison - Lv 3"], "stats": [["Id", "Alanthor_Sentinel"], ["Class", "human_melee"], ["HP", "210"], ["Speed", "5"], ["Training time", "16.8"], ["Population cost", "2"], ["Damage", "12"], ["Damage type", "melee"], ["Armor type", "infantry_heavy"], ["Melee defense", "7"], ["Ranged defense", "5"], ["Siege defense", "0"], ["Magic defense", "2"], ["Attack cooldown", "1.8"], ["Min attack range", "0"], ["Attack range", "1.7"], ["Line of sight", "18"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "3"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+10 vs Heavy"], ["Tags", "Infantry, Heavy"], ["Abilities", "none"], ["PresentationId", "334"]], "cost": {"s": 0, "i": 0, "v": 89, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Barracks_Alanthor_Nobleman", "a": 2, "r": 2, "t": "Nobleman", "k": "mil", "o": "", "arw": false, "type": "Melee", "race": "", "size": "", "d": ["Trained at Barracks"], "req": ["Garrison - Lv 2"], "stats": [["Id", "Alanthor_Nobleman"], ["Class", "human_melee"], ["HP", "175"], ["Speed", "5.7"], ["Training time", "19.6"], ["Population cost", "1"], ["Damage", "18"], ["Damage type", "melee"], ["Armor type", "infantry_heavy"], ["Melee defense", "5"], ["Ranged defense", "3"], ["Siege defense", "0"], ["Magic defense", "2"], ["Attack cooldown", "1.3"], ["Min attack range", "0"], ["Attack range", "1.2"], ["Line of sight", "17"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "2"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "none"], ["Tags", "Infantry, Heavy"], ["Abilities", "none"], ["PresentationId", "346"]], "cost": {"s": 0, "i": 33, "v": 80, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Charge", "a": 2, "r": 3, "t": "Charge", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Infantry charge: after 5 s out of combat a soldier closes at +50% speed for 2 s and his first blow in that window deals +30%. Miss the window and it resets.", "passive: FirstStrikePct Set 30", "passive: ChargeSpeedPct Set 50", "passive: ChargeWindowSeconds Set 2", "passive: ChargeRearmSeconds Set 5"], "req": ["Garrison - Lv 2"], "stats": [], "cost": {"s": 160, "i": 70, "v": 0, "vs": 0}, "up": true, "researchTime": 33.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Conscription", "a": 0, "r": 3, "t": "Conscription", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Standing levies train 15 percent faster at any Barracks.", "building:Barracks: TrainSpeed Pct 15"], "req": [], "stats": [], "cost": {"s": 100, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_EliteInfantry", "a": 3, "r": 3, "t": "Elite Infantry", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+30 HP, +15% speed, +2 damage, +2 defense to all Garrison line infantry."], "req": ["Veteran Infantry", "Garrison - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 258, "vs": 72}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Spearman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Spearman", "stat": "Speed", "op": "+%", "value": 15.0}, {"target": "unit:Spearman", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Spearman", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "unit:Spearman", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "unit:Spearman", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "unit:Spearman", "stat": "Magic defense", "op": "+", "value": 2.0}, {"target": "unit:Swordsman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Swordsman", "stat": "Speed", "op": "+%", "value": 15.0}, {"target": "unit:Swordsman", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Swordsman", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "unit:Swordsman", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "unit:Swordsman", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "unit:Swordsman", "stat": "Magic defense", "op": "+", "value": 2.0}, {"target": "unit:Nobleman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Nobleman", "stat": "Speed", "op": "+%", "value": 15.0}, {"target": "unit:Nobleman", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Nobleman", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "unit:Nobleman", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "unit:Nobleman", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "unit:Nobleman", "stat": "Magic defense", "op": "+", "value": 2.0}, {"target": "unit:Sentinel", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Sentinel", "stat": "Speed", "op": "+%", "value": 15.0}, {"target": "unit:Sentinel", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Sentinel", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "unit:Sentinel", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "unit:Sentinel", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "unit:Sentinel", "stat": "Magic defense", "op": "+", "value": 2.0}], "abilities": []},
      {"id": "so_tech_IronWeapons", "a": 1, "r": 3, "t": "Iron Weapons", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weapon tier 2: melee units deal +15% damage."], "req": ["Stone Weapons", "Garrison - Lv 1"], "stats": [], "cost": {"s": 0, "i": 200, "v": 225, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Melee", "stat": "Damage", "op": "+%", "value": 15.0}], "abilities": []},
      {"id": "so_tech_SeasonedInfantry", "a": 1, "r": 4, "t": "Seasoned Infantry", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+30 HP, +5% speed, +1 damage, +1 defense to all Garrison line infantry."], "req": ["Garrison - Lv 1"], "stats": [], "cost": {"s": 100, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 28.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Spearman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Spearman", "stat": "Speed", "op": "+%", "value": 5.0}, {"target": "unit:Spearman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Swordsman", "stat": "Speed", "op": "+%", "value": 5.0}, {"target": "unit:Swordsman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Nobleman", "stat": "Speed", "op": "+%", "value": 5.0}, {"target": "unit:Nobleman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Sentinel", "stat": "Speed", "op": "+%", "value": 5.0}, {"target": "unit:Sentinel", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_ShardInfusedWeapons", "a": 3, "r": 4, "t": "Shard-infused Weapons", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weapon tier 4: melee units deal a further +75% damage. Needs Veilsteel."], "req": ["Veilstone Weapons", "Garrison - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 170}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Melee", "stat": "Damage", "op": "+%", "value": 75.0}], "abilities": []},
      {"id": "so_tech_ShieldWall", "a": 3, "r": 5, "t": "Shield Wall", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the Shield Wall passive for Garrison line infantry: +30% defense against the first incoming attack while stationary; recharges after 3 s."], "req": ["Charge", "Garrison - Lv 3"], "stats": [], "cost": {"s": 0, "i": 50, "v": 180, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_StoneWeapons", "a": 0, "r": 4, "t": "Stone Weapons", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Knapped stone heads harden the line: melee units gain +2 damage. (Interim faction-wide bump until per-battalion upgrades ship.)"], "req": [], "stats": [], "cost": {"s": 80, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 25.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilstoneWeapons", "a": 2, "r": 4, "t": "Veilstone Weapons", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weapon tier 3: melee units deal a further +30% damage."], "req": ["Iron Weapons", "Garrison - Lv 2"], "stats": [], "cost": {"s": 0, "i": 0, "v": 300, "vs": 33}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Melee", "stat": "Damage", "op": "+%", "value": 30.0}], "abilities": []},
      {"id": "so_tech_VeteranInfantry", "a": 2, "r": 5, "t": "Veteran Infantry", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+30 HP, +10% speed, +1 damage, +1 defense to all Garrison line infantry."], "req": ["Seasoned Infantry", "Garrison - Lv 2"], "stats": [], "cost": {"s": 0, "i": 80, "v": 270, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Spearman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Spearman", "stat": "Speed", "op": "+%", "value": 10.0}, {"target": "unit:Spearman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Spearman", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Swordsman", "stat": "Speed", "op": "+%", "value": 10.0}, {"target": "unit:Swordsman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Swordsman", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Nobleman", "stat": "Speed", "op": "+%", "value": 10.0}, {"target": "unit:Nobleman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Nobleman", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Sentinel", "stat": "Speed", "op": "+%", "value": 10.0}, {"target": "unit:Sentinel", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Sentinel", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_IronPlate", "a": 1, "r": 5, "t": "Iron Plate", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Infantry armour tier 1: +1 defense to all melee units."], "req": ["Garrison - Lv 1"], "stats": [], "cost": {"s": 120, "i": 150, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Melee", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "type:Melee", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "type:Melee", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "type:Melee", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_ShardPlate", "a": 3, "r": 6, "t": "Shard Plate", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Infantry armour tier 3: +3 defense to all melee units."], "req": ["Veilstone Plate", "Garrison - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 140}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Melee", "stat": "Melee defense", "op": "+", "value": 3.0}, {"target": "type:Melee", "stat": "Ranged defense", "op": "+", "value": 3.0}, {"target": "type:Melee", "stat": "Siege defense", "op": "+", "value": 3.0}, {"target": "type:Melee", "stat": "Magic defense", "op": "+", "value": 3.0}], "abilities": []},
      {"id": "so_tech_VeilstonePlate", "a": 2, "r": 6, "t": "Veilstone Plate", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Infantry armour tier 2: +2 defense to all melee units."], "req": ["Iron Plate", "Garrison - Lv 2"], "stats": [], "cost": {"s": 0, "i": 150, "v": 120, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Melee", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "type:Melee", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "type:Melee", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "type:Melee", "stat": "Magic defense", "op": "+", "value": 2.0}], "abilities": []}
    ]
  },
  {
    "id": "so_b_FiendstoneKeep",
    "banner": "Fiendstone Keep - Fortified training + supply (Age 0 choice)",
    "mil": true,
    "cost": {"s": 600, "i": 300, "v": 200, "vs": 0},
    "stats": [["id", "FiendstoneKeep"], ["Building Type", "Basic"], ["HP", "1000"], ["Armor type", "structure_human"], ["Melee defense", "4"], ["Ranged defense", "10"], ["Siege defense", "0"], ["Magic defense", "3"], ["Line of sight", "18"], ["Building radius", "2.4"], ["Footprint (cells)", "6 x 6"], ["Build time", "90"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "Spearman"], ["Research", "Additional Towers, Ballista Emplacement, Reinforced Walls, Trebuchet Emplacement"], ["Tags", "Building"], ["PresentationId", "540"]],
    "nodes": [
      {"id": "so_lvl_FiendstoneKeep_0", "a": 0, "r": 1, "t": "Fiendstone Keep - Lv 0", "k": "lvl", "o": "mil", "arw": false, "type": "", "race": "", "size": "", "d": ["Fortified training + supply (Age 0 choice)", "Fortified keep and one of the three Age 0 choice buildings. Trains non-religious, non-siege military faster than normal, generates modest Supplies, and fires arrow volleys at attackers (Feraldis +50% HP, Alanthor -50%)."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_FiendstoneKeep_Spearman", "a": 0, "r": 2, "t": "Spearman", "k": "mil", "o": "", "arw": false, "type": "Melee", "race": "", "size": "", "d": ["Trained at Fiendstone Keep"], "req": [], "stats": [["Id", "Spearman"], ["Class", "human_melee"], ["HP", "120"], ["Speed", "5"], ["Training time", "15.4"], ["Population cost", "1"], ["Damage", "10"], ["Damage type", "melee"], ["Armor type", "infantry_heavy"], ["Melee defense", "1"], ["Ranged defense", "1"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "1.5"], ["Min attack range", "0"], ["Attack range", "1.5"], ["Line of sight", "16"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "0"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+22 vs Cavalry"], ["Tags", "Infantry, Heavy"], ["Abilities", "none"], ["PresentationId", "368"]], "cost": {"s": 44, "i": 16, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_AdditionalTowers", "a": 0, "r": 3, "t": "Additional Towers", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["The Keep's auto-fire strikes two additional targets per volley."], "req": [], "stats": [], "cost": {"s": 240, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_BallistaEmplacement", "a": 0, "r": 4, "t": "Ballista Emplacement", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["The Keep's auto-fire gains an extra single-target ballista bolt (18 siege damage) each volley."], "req": [], "stats": [], "cost": {"s": 200, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_ReinforcedWalls", "a": 0, "r": 5, "t": "Reinforced Walls", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["The Keep's hull is reinforced: +20 percent Max HP."], "req": [], "stats": [], "cost": {"s": 180, "i": 60, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_TrebuchetEmplacement", "a": 0, "r": 6, "t": "Trebuchet Emplacement", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["The Keep's auto-fire gains an arcing trebuchet shot (36 siege damage, splash) each volley."], "req": ["Ballista Emplacement"], "stats": [], "cost": {"s": 0, "i": 140, "v": 500, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_GatherersHut",
    "banner": "Gatherer's Hut -> Guild (Alanthor) - Area resource trickle (no stacking)",
    "mil": false,
    "cost": {"s": 120, "i": 10, "v": 0, "vs": 0},
    "stats": [["id", "GatherersHut"], ["Building Type", "Basic"], ["HP", "300"], ["Armor type", "structure_human"], ["Melee defense", "3"], ["Ranged defense", "9"], ["Siege defense", "0"], ["Magic defense", "1"], ["Line of sight", "16"], ["Building radius", "0.5"], ["Footprint (cells)", "2 x 2"], ["Build time", "20"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "110 / 135 / 185"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "Iron reinforcements, Iron Surveying I, Iron Survey II, Iron Survey III, Veilsteel Pylons, Veilsteel Survey, Veilstone Survey I, Veilstone Survey II, Veilstone walls"], ["Tags", "Building"], ["PresentationId", "101"]],
    "nodes": [
      {"id": "so_lvl_GatherersHut_0", "a": 0, "r": 1, "t": "Gatherer's Hut - Lv 0", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["Area resource trickle (no stacking)", "Early Age 0 supply generator. Emits a +Supplies aura over a small radius. At age-up it transforms into the culture's signature economy structure (wall anchor / trade wagon / Hunting or Logging station)."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_GatherersHut_1", "a": 1, "r": 1, "t": "Guild - Lv 1", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909", "Slot income 130 / min"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_GatherersHut_2", "a": 2, "r": 1, "t": "Guild - Lv 2", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87", "Slot income 145 / min"], "req": ["Guild - Lv 1"], "stats": [], "cost": {"s": 270, "i": 50, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_GatherersHut_3", "a": 3, "r": 1, "t": "Guild - Lv 3", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833", "Slot income 195 / min"], "req": ["Guild - Lv 2"], "stats": [], "cost": {"s": 467, "i": 75, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_IronReinforcements", "a": 1, "r": 2, "t": "Iron reinforcements", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Gatherer's Huts auto-repair after 10s without taking damage (5 HP/s out of combat). (behaviour-by-id)"], "req": [], "stats": [], "cost": {"s": 150, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_IronSurveying1", "a": 1, "r": 3, "t": "Iron Surveying I", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Faction-wide: every Guild also produces Iron (+8/min at Guild L1, x1.25 at L2, x1.5 at L3), drawn from no node, so it keeps paying after the nodes run dry. Paid while the Guild's territory is held."], "req": [], "stats": [], "cost": {"s": 120, "i": 60, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_IronSurveying2", "a": 2, "r": 2, "t": "Iron Survey II", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Every Guild's survey Iron rises by another +8/min (scaled by Guild level)."], "req": ["Iron Surveying I", "Guild - Lv 2"], "stats": [], "cost": {"s": 0, "i": 120, "v": 300, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_IronSurveying3", "a": 3, "r": 2, "t": "Iron Survey III", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Every Guild's survey Iron rises by another +8/min (scaled by Guild level)."], "req": ["Iron Survey II", "Guild - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 360, "vs": 80}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilsteelPylons", "a": 3, "r": 3, "t": "Veilsteel Pylons", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Upgrades the low-HP defensive cast from Slow to Stop (-100% speed for 10s, 90s cooldown); further speeds up auto-repair (20 HP/s). (behaviour-by-id)"], "req": ["Veilstone walls", "Guild - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 315, "vs": 170}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilsteelSurvey", "a": 3, "r": 4, "t": "Veilsteel Survey", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Faction-wide: every Guild also produces a slow trickle of Veilsteel (+3/min at Guild L1, scaled by Guild level), drawn from no node."], "req": ["Veilstone Survey II", "Guild - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 325, "vs": 110}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilstoneSurvey1", "a": 2, "r": 3, "t": "Veilstone Survey I", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Faction-wide: every Guild also produces Veilstone (+6/min at Guild L1, scaled by Guild level), drawn from no node, so it keeps paying after the nodes run dry."], "req": ["Iron Surveying I", "Guild - Lv 2"], "stats": [], "cost": {"s": 0, "i": 80, "v": 390, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilstoneSurvey2", "a": 3, "r": 5, "t": "Veilstone Survey II", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Every Guild's survey Veilstone rises by another +6/min (scaled by Guild level)."], "req": ["Veilstone Survey I", "Guild - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 390, "vs": 75}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilstoneWalls", "a": 2, "r": 4, "t": "Veilstone walls", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Alanthor Guild. Below 50% HP a Gatherer's Hut casts a Slow burst on nearby enemies (-50% speed for 7.5s, 90s cooldown); also speeds up auto-repair (10 HP/s). (behaviour-by-id)"], "req": ["Iron reinforcements", "Guild - Lv 2"], "stats": [], "cost": {"s": 0, "i": 100, "v": 458, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Hut",
    "banner": "Hut -> House (Alanthor) - Provides population",
    "mil": false,
    "cost": {"s": 80, "i": 0, "v": 0, "vs": 0},
    "stats": [["id", "Hut"], ["Building Type", "Basic"], ["HP", "650"], ["Armor type", "structure_human"], ["Melee defense", "4"], ["Ranged defense", "10"], ["Siege defense", "0"], ["Magic defense", "2"], ["Line of sight", "6"], ["Building radius", "1.6"], ["Footprint (cells)", "2 x 2"], ["Build time", "15"], ["Population", "6"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "20"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "102"]],
    "nodes": [
      {"id": "so_lvl_Hut_0", "a": 0, "r": 1, "t": "Hut - Lv 0", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["Provides population", "Population housing for Age 0 (the 'House'). Each one raises the population cap. Its post-age-up behavior splits per culture."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Hut_1", "a": 1, "r": 1, "t": "House - Lv 1", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909", "Population 10"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Hut_2", "a": 2, "r": 1, "t": "House - Lv 2", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87", "Population 16"], "req": ["House - Lv 1"], "stats": [], "cost": {"s": 270, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Hut_3", "a": 3, "r": 1, "t": "House - Lv 3", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833", "Population 20"], "req": ["House - Lv 2"], "stats": [], "cost": {"s": 533, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 60.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_VaultOfAlmierra",
    "banner": "Vault of Almiérra - Banking (deposit/interest)",
    "mil": false,
    "cost": {"s": 600, "i": 300, "v": 200, "vs": 0},
    "stats": [["id", "VaultOfAlmierra"], ["Building Type", "Basic"], ["HP", "600"], ["Armor type", "structure_human"], ["Melee defense", "3"], ["Ranged defense", "10"], ["Siege defense", "0"], ["Magic defense", "3"], ["Line of sight", "14"], ["Building radius", "2"], ["Footprint (cells)", "4 x 4"], ["Build time", "90"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0.05"], ["Interest principal cap", "3000"], ["Interest per minute (Coffers / Charters / Bonds)", "0.08 / 0.11 / 0.15"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "Coffers, Iron Subsidies, Merchant Charters, Sovereign Bonds, Veilsteel Bonds, Veilstone Monetization"], ["Tags", "Building"], ["PresentationId", "530"]],
    "nodes": [
      {"id": "so_lvl_VaultOfAlmierra_0", "a": 0, "r": 1, "t": "Vault of Almiérra - Lv 0", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["Banking (deposit/interest)", "Resource bank and one of the three Age 0 choice buildings that unlock the advance to Era II. Deposited resources earn simple interest each minute on the stored principal up to a cap; banking-grade techs raise the rate and the Vault's levels multiply it."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_VaultOfAlmierra_1", "a": 1, "r": 1, "t": "Vault of Almiérra - Lv 1", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909", "Interest x1.5"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_VaultOfAlmierra_2", "a": 2, "r": 1, "t": "Vault of Almiérra - Lv 2", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87", "Interest x2"], "req": ["Vault of Almiérra - Lv 1"], "stats": [], "cost": {"s": 210, "i": 50, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_VaultOfAlmierra_3", "a": 3, "r": 1, "t": "Vault of Almiérra - Lv 3", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833", "Interest x2"], "req": ["Vault of Almiérra - Lv 2"], "stats": [], "cost": {"s": 427, "i": 100, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Coffers", "a": 0, "r": 2, "t": "Coffers", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Safe storage: the Vault's interest rises to banking grade 1."], "req": [], "stats": [], "cost": {"s": 150, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_IronSubsidies", "a": 0, "r": 3, "t": "Iron Subsidies", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks Iron banking: Iron can be deposited in the Vault."], "req": [], "stats": [], "cost": {"s": 180, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_MerchantCharters", "a": 0, "r": 4, "t": "Merchant Charters", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Active credit: the Vault's interest rises to banking grade 2."], "req": [], "stats": [], "cost": {"s": 200, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_SovereignBonds", "a": 0, "r": 5, "t": "Sovereign Bonds", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["High-stakes investment: the Vault's interest rises to banking grade 3."], "req": [], "stats": [], "cost": {"s": 250, "i": 120, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilsteelBonds", "a": 3, "r": 2, "t": "Veilsteel Bonds", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks Veilsteel banking: Veilsteel can be deposited in the Vault."], "req": ["Vault of Almiérra Lv 3"], "stats": [], "cost": {"s": 340, "i": 120, "v": 0, "vs": 0}, "up": true, "researchTime": 50.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilstoneMonetization", "a": 2, "r": 2, "t": "Veilstone Monetization", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks Veilstone banking: Veilstone can be deposited in the Vault."], "req": ["Vault of Almiérra Lv 2"], "stats": [], "cost": {"s": 263, "i": 100, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Palisade",
    "banner": "Palisade - A timber fence every culture can draw in Age 0 — Feraldis keep it for the whole game",
    "mil": false,
    "cost": {"s": 100, "i": 0, "v": 0, "vs": 0},
    "stats": [["id", "Palisade"], ["Building Type", "Basic"], ["HP", "600"], ["Armor type", "structure_human"], ["Melee defense", "8"], ["Ranged defense", "14"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "10"], ["Building radius", "1.6"], ["Footprint (cells)", "2 x 2"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "550"]],
    "nodes": [
      {"id": "so_lvl_Palisade_0", "a": 0, "r": 1, "t": "Palisade - Lv 0", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["A timber fence every culture can draw in Age 0 — Feraldis keep it for the whole game"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_PalisadeSegment",
    "banner": "Palisade Section - One 3 m palisade module. Paid per module when the fence is laid, never placed directly",
    "mil": false,
    "cost": {"s": 12, "i": 0, "v": 0, "vs": 0},
    "stats": [["id", "PalisadeSegment"], ["Building Type", "Basic"], ["HP", "200"], ["Armor type", "structure_human"], ["Melee defense", "8"], ["Ranged defense", "14"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "5"], ["Building radius", "1.5"], ["Footprint (cells)", "4 x 4"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "552"]],
    "nodes": [
      {"id": "so_lvl_PalisadeSegment_0", "a": 0, "r": 1, "t": "Palisade Section - Lv 0", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["One 3 m palisade module. Paid per module when the fence is laid, never placed directly"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_WallGate",
    "banner": "Wall Gate - Gatehouse — one structure three modules wide, converted from a wall module",
    "mil": false,
    "cost": {"s": 80, "i": 30, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_WallGate"], ["Building Type", "Basic"], ["HP", "500"], ["Armor type", "structure_human"], ["Melee defense", "7"], ["Ranged defense", "13"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "8"], ["Building radius", "1.6"], ["Footprint (cells)", "4 x 4"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "554"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_WallGate_0", "a": 0, "r": 1, "t": "Wall Gate - Lv 0", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["Gatehouse — one structure three modules wide, converted from a wall module"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Mine",
    "banner": "Mine - Workerless ore extraction - build next to an iron or veilstone patch",
    "mil": false,
    "cost": {"s": 100, "i": 0, "v": 10, "vs": 0},
    "stats": [["id", "Mine"], ["Building Type", "Basic"], ["HP", "700"], ["Armor type", "structure_human"], ["Melee defense", "1"], ["Ranged defense", "1"], ["Siege defense", "0"], ["Magic defense", "0"], ["Line of sight", "12"], ["Building radius", "1.5"], ["Footprint (cells)", "2 x 2"], ["Build time", "25"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "100 / 150 / 250"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "1"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "Deep Shafts, Rich Seams"], ["Tags", "Building"], ["PresentationId", "364"]],
    "nodes": [
      {"id": "so_lvl_Mine_0", "a": 0, "r": 1, "t": "Mine - Lv 0", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["Workerless ore extraction - build next to an iron or veilstone patch", "Works every iron and veilstone node in range with no workers at all. Slower than mining by hand, but it never depletes the ore."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Mine_1", "a": 1, "r": 1, "t": "Mine - Lv 1", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909", "Slot income 140 / min"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Mine_2", "a": 2, "r": 1, "t": "Mine - Lv 2", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87", "Slot income 170 / min"], "req": ["Mine - Lv 1"], "stats": [], "cost": {"s": 313, "i": 90, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Mine_3", "a": 3, "r": 1, "t": "Mine - Lv 3", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833", "Slot income 270 / min"], "req": ["Mine - Lv 2"], "stats": [], "cost": {"s": 727, "i": 160, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_DeepShafts", "a": 0, "r": 2, "t": "Deep Shafts", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Every iron slot one of your Mines works pays +20%."], "req": [], "stats": [], "cost": {"s": 150, "i": 0, "v": 20, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_RichSeams", "a": 0, "r": 3, "t": "Rich Seams", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Every iron slot one of your Mines works pays +40% (replaces Deep Shafts)."], "req": ["Deep Shafts"], "stats": [], "cost": {"s": 300, "i": 0, "v": 60, "vs": 0}, "up": true, "researchTime": 60.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_TempleOfRidan",
    "banner": "Temple of Ridan - production",
    "mil": false,
    "cost": {"s": 200, "i": 100, "v": 0, "vs": 0},
    "stats": [["id", "TempleOfRidan"], ["Building Type", "Basic"], ["HP", "1500"], ["Armor type", "structure_human"], ["Melee defense", "6"], ["Ranged defense", "12"], ["Siege defense", "0"], ["Magic defense", "6"], ["Line of sight", "18"], ["Building radius", "2.5"], ["Footprint (cells)", "4 x 4"], ["Build time", "40"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "1"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "Litharch"], ["Research", "Fervored Masses, Heightened Masses, Pious Masses, Warrior Priests"], ["Tags", "Building"], ["PresentationId", "521"]],
    "nodes": [
      {"id": "so_lvl_TempleOfRidan_0", "a": 0, "r": 1, "t": "Temple of Ridan - Lv 0", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["production"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_TempleOfRidan_Litharch", "a": 0, "r": 2, "t": "Litharch", "k": "mil", "o": "", "arw": false, "type": "Magic", "race": "", "size": "", "d": ["Trained at Temple of Ridan"], "req": [], "stats": [["Id", "Litharch"], ["Class", "human_support"], ["HP", "120"], ["Speed", "5.5"], ["Training time", "16.8"], ["Population cost", "1"], ["Damage", "0"], ["Damage type", "magic"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "0"], ["Siege defense", "0"], ["Magic defense", "3"], ["Attack cooldown", "1.5"], ["Min attack range", "0"], ["Attack range", "10"], ["Line of sight", "20"], ["Aim time", "0"], ["Radius", "0.5"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "0"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "6"], ["Heal range", "10"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "none"], ["Tags", "Ranged"], ["Abilities", "none"], ["PresentationId", "207"]], "cost": {"s": 55, "i": 14, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_FervoredMasses", "a": 0, "r": 3, "t": "Fervored Masses", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Temple healing aura rises from 6 to 15 percent of Max HP per second."], "req": ["Pious Masses"], "stats": [], "cost": {"s": 0, "i": 0, "v": 678, "vs": 0}, "up": true, "researchTime": 50.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_HeightenedMasses", "a": 0, "r": 4, "t": "Heightened Masses", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Temple healing aura rises from 1 to 3 percent of Max HP per second."], "req": [], "stats": [], "cost": {"s": 177, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_PiousMasses", "a": 0, "r": 5, "t": "Pious Masses", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Temple healing aura rises from 3 to 6 percent of Max HP per second."], "req": ["Heightened Masses"], "stats": [], "cost": {"s": 0, "i": 0, "v": 410, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_WarriorPriests", "a": 0, "r": 6, "t": "Warrior Priests", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Litharchs take up arms: gain a melee attack (6 damage every 1.5 seconds)."], "req": [], "stats": [], "cost": {"s": 193, "i": 50, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "", "scope": "global", "on": false, "effects": [{"target": "unit:Litharch", "stat": "Damage", "op": "set", "value": 6.0}, {"target": "unit:Litharch", "stat": "Attack cooldown", "op": "set", "value": 1.5}], "abilities": []}
    ]
  },
  {
    "id": "so_b_VeilstoneMine",
    "banner": "Veilstone Mine - Veilstone extraction - built on a veilstone outcropping",
    "mil": false,
    "cost": {"s": 100, "i": 0, "v": 10, "vs": 0},
    "stats": [["id", "VeilstoneMine"], ["Building Type", "Basic"], ["HP", "700"], ["Armor type", "structure_human"], ["Melee defense", "1"], ["Ranged defense", "1"], ["Siege defense", "0"], ["Magic defense", "0"], ["Line of sight", "12"], ["Building radius", "1.5"], ["Footprint (cells)", "2 x 2"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "100 / 150 / 250"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "1"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "566"]],
    "nodes": [
      {"id": "so_lvl_VeilstoneMine_0", "a": 0, "r": 1, "t": "Veilstone Mine - Lv 0", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["Veilstone extraction - built on a veilstone outcropping", "Cuts veilstone from the outcropping it stands on, adding to the territory yield. Upgrading draws more per minute - and empties the seam sooner."], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_WallSegment",
    "banner": "Wall Segment - One 3 m stone curtain module. Paid per module when the wall is laid, never placed directly",
    "mil": false,
    "cost": {"s": 20, "i": 10, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_WallSegment"], ["Building Type", "Alanthor"], ["HP", "200"], ["Armor type", "structure_human"], ["Melee defense", "8"], ["Ranged defense", "14"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "5"], ["Building radius", "1.5"], ["Footprint (cells)", "4 x 4"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "552"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_WallSegment_0", "a": 1, "r": 1, "t": "Wall Segment", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["One 3 m stone curtain module. Paid per module when the wall is laid, never placed directly"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_BallistaEmplacement",
    "banner": "Ballista Emplacement - Fixed anti-armour battery",
    "mil": false,
    "cost": {"s": 280, "i": 160, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_BallistaEmplacement"], ["Building Type", "Alanthor"], ["HP", "500"], ["Armor type", "structure_human"], ["Melee defense", "8"], ["Ranged defense", "12"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "22"], ["Building radius", "2"], ["Footprint (cells)", "2 x 2"], ["Build time", "35"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "2"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "1"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "570"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_BallistaEmplacement_0", "a": 1, "r": 1, "t": "Ballista Emplacement", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["Fixed anti-armour battery"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_BallistaEmplacement_Alanthor_EmplacedBallista", "a": 1, "r": 2, "t": "Emplaced Ballista", "k": "mil", "o": "", "arw": false, "type": "Siege", "race": "", "size": "", "d": ["Spawned by Ballista Emplacement (not trained)"], "req": ["Ballista Emplacement Lv 1"], "stats": [["Id", "Alanthor_EmplacedBallista"], ["Class", "machinery_siege"], ["HP", "260"], ["Speed", "0"], ["Training time", "10.5"], ["Population cost", "0"], ["Damage", "46"], ["Damage type", "siege"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "4"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "3.5"], ["Min attack range", "8"], ["Attack range", "26"], ["Line of sight", "30"], ["Aim time", "1"], ["Radius", "1"], ["Trajectory", "flat"], ["Projectile speed", "60"], ["Min building level", "1"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+30 vs Building"], ["Tags", "Ranged, Siege, Heavy"], ["Abilities", "none"], ["PresentationId", "385"]], "cost": {"s": 70, "i": 40, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_TrebuchetEmplacement",
    "banner": "Trebuchet Emplacement - Fixed siege battery",
    "mil": false,
    "cost": {"s": 520, "i": 280, "v": 80, "vs": 0},
    "stats": [["id", "Alanthor_TrebuchetEmplacement"], ["Building Type", "Alanthor"], ["HP", "700"], ["Armor type", "structure_human"], ["Melee defense", "8"], ["Ranged defense", "12"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "26"], ["Building radius", "3"], ["Footprint (cells)", "3 x 3"], ["Build time", "55"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "3"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "1"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "571"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_TrebuchetEmplacement_0", "a": 1, "r": 1, "t": "Trebuchet Emplacement", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["Fixed siege battery"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_TrebuchetEmplacement_Alanthor_EmplacedTrebuchet", "a": 1, "r": 2, "t": "Emplaced Trebuchet", "k": "mil", "o": "", "arw": false, "type": "Siege", "race": "", "size": "", "d": ["Spawned by Trebuchet Emplacement (not trained)"], "req": ["Trebuchet Emplacement Lv 1"], "stats": [["Id", "Alanthor_EmplacedTrebuchet"], ["Class", "machinery_siege"], ["HP", "320"], ["Speed", "0"], ["Training time", "14"], ["Population cost", "0"], ["Damage", "120"], ["Damage type", "siege"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "4"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "8"], ["Min attack range", "14"], ["Attack range", "48"], ["Line of sight", "52"], ["Aim time", "1.5"], ["Radius", "1.4"], ["Trajectory", "high"], ["Projectile speed", "32"], ["Min building level", "1"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "4"], ["Bonus vs tags", "+45 vs Building"], ["Tags", "Ranged, Siege, Heavy"], ["Abilities", "none"], ["PresentationId", "348"]], "cost": {"s": 130, "i": 70, "v": 20, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_ArcheryRange",
    "banner": "Archery Range - Train ranged units — Archer at L1, Crossbowman at L2, Longbowman at L3",
    "mil": true,
    "cost": {"s": 162, "i": 50, "v": 0, "vs": 0},
    "stats": [["id", "ArcheryRange"], ["Building Type", "Alanthor"], ["HP", "600"], ["Armor type", "structure_human"], ["Melee defense", "4"], ["Ranged defense", "10"], ["Siege defense", "0"], ["Magic defense", "2"], ["Line of sight", "18"], ["Building radius", "1.6"], ["Footprint (cells)", "4 x 4"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "2"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "Archer, Crossbowman, Longbowman"], ["Research", "Arrow Shower, Arrow Volley, Choreographed Volleys, Deploy Stakes, Elite Archers, Fletching, Iron-Tipped Arrows, Seasoned Archers, Shard-Tipped Arrows, Stone-Tipped Arrows, Veilstone-Tipped Arrows, Veteran Archers, Iron Brigandine, Shard Brigandine, Veilstone Brigandine"], ["Tags", "Building"], ["PresentationId", "511"]],
    "nodes": [
      {"id": "so_lvl_ArcheryRange_1", "a": 1, "r": 1, "t": "Archery Range - Lv 1", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_ArcheryRange_2", "a": 2, "r": 1, "t": "Archery Range - Lv 2", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87"], "req": ["Archery Range - Lv 1"], "stats": [], "cost": {"s": 167, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_ArcheryRange_3", "a": 3, "r": 1, "t": "Archery Range - Lv 3", "k": "lvl", "o": "mil", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833"], "req": ["Archery Range - Lv 2"], "stats": [], "cost": {"s": 340, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_ArcheryRange_Alanthor_Archer", "a": 1, "r": 2, "t": "Archer", "k": "mil", "o": "", "arw": false, "type": "Ranged", "race": "", "size": "", "d": ["Trained at Archery Range"], "req": [], "stats": [["Id", "Alanthor_Archer"], ["Class", "human_ranged"], ["HP", "60"], ["Speed", "5.2"], ["Training time", "14"], ["Population cost", "1"], ["Damage", "8"], ["Damage type", "ranged"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "1"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "1.5"], ["Min attack range", "0"], ["Attack range", "10"], ["Line of sight", "10"], ["Aim time", "0.5"], ["Radius", "0.5"], ["Trajectory", "low"], ["Projectile speed", "0"], ["Min building level", "0"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+10 vs Infantry"], ["Tags", "Ranged, Light"], ["Abilities", "none"], ["PresentationId", "202"]], "cost": {"s": 28, "i": 14, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_ArcheryRange_Alanthor_Crossbowman", "a": 2, "r": 2, "t": "Crossbowman", "k": "mil", "o": "", "arw": false, "type": "Ranged", "race": "", "size": "", "d": ["Trained at Archery Range"], "req": ["Archery Range - Lv 2"], "stats": [["Id", "Alanthor_Crossbowman"], ["Class", "human_ranged"], ["HP", "70"], ["Speed", "5"], ["Training time", "15.4"], ["Population cost", "1"], ["Damage", "18"], ["Damage type", "ranged"], ["Armor type", "ranged"], ["Melee defense", "1"], ["Ranged defense", "2"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "3"], ["Min attack range", "0"], ["Attack range", "12"], ["Line of sight", "12"], ["Aim time", "0.35"], ["Radius", "0.5"], ["Trajectory", "flat"], ["Projectile speed", "55"], ["Min building level", "2"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+20 vs Cavalry"], ["Tags", "Ranged, Light"], ["Abilities", "none"], ["PresentationId", "335"]], "cost": {"s": 0, "i": 0, "v": 68, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_ArcheryRange_Alanthor_Longbowman", "a": 3, "r": 2, "t": "Longbowman", "k": "mil", "o": "", "arw": false, "type": "Ranged", "race": "", "size": "", "d": ["Trained at Archery Range"], "req": ["Archery Range - Lv 3"], "stats": [["Id", "Alanthor_Longbowman"], ["Class", "human_ranged"], ["HP", "55"], ["Speed", "4"], ["Training time", "15.4"], ["Population cost", "1"], ["Damage", "25"], ["Damage type", "ranged"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "1"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "3.5"], ["Min attack range", "0"], ["Attack range", "20"], ["Line of sight", "20"], ["Aim time", "0.5"], ["Radius", "0.5"], ["Trajectory", "high"], ["Projectile speed", "0"], ["Min building level", "3"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "none"], ["Tags", "Ranged, Light"], ["Abilities", "none"], ["PresentationId", "205"]], "cost": {"s": 0, "i": 22, "v": 26, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_ArrowShower", "a": 2, "r": 3, "t": "Arrow Shower", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Ranged attack cooldown drops to -50% total (stacks multiplicatively with Arrow Volley)."], "req": ["Arrow Volley", "Archery Range - Lv 2"], "stats": [], "cost": {"s": 0, "i": 60, "v": 300, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Attack cooldown", "op": "+%", "value": -28.6}], "abilities": []},
      {"id": "so_tech_ArrowVolley", "a": 1, "r": 3, "t": "Arrow Volley", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["-30% attack cooldown for all ranged units. Permanent, unconditional."], "req": [], "stats": [], "cost": {"s": 120, "i": 30, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Attack cooldown", "op": "+%", "value": -30.0}], "abilities": []},
      {"id": "so_tech_ChoreographedVolleys", "a": 0, "r": 2, "t": "Choreographed Volleys", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Active, carried by every ranged unit: call the cadence and allied ranged units within 15 m fire at double rate for 5 s. 60 s cooldown."], "req": [], "stats": [], "cost": {"s": 120, "i": 30, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_DeployStakes", "a": 3, "r": 3, "t": "Deploy Stakes", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Planted stakes: the first cavalry charge against an archer lands at half damage and pays 50% of what it dealt straight back into the horse. Re-plants after 20 s."], "req": ["Archery Range - Lv 3"], "stats": [], "cost": {"s": 150, "i": 50, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_EliteArchers", "a": 3, "r": 4, "t": "Elite Archers", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+50 HP, +2 damage, +5 line of sight, +3 attack range to all Archery Range archers."], "req": ["Veteran Archers", "Archery Range - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 180, "vs": 55}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Archer", "stat": "HP", "op": "+", "value": 50.0}, {"target": "unit:Archer", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Archer", "stat": "Line of sight", "op": "+", "value": 5.0}, {"target": "unit:Archer", "stat": "Attack range", "op": "+", "value": 3.0}, {"target": "unit:Crossbowman", "stat": "HP", "op": "+", "value": 50.0}, {"target": "unit:Crossbowman", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Crossbowman", "stat": "Line of sight", "op": "+", "value": 5.0}, {"target": "unit:Crossbowman", "stat": "Attack range", "op": "+", "value": 3.0}, {"target": "unit:Longbowman", "stat": "HP", "op": "+", "value": 50.0}, {"target": "unit:Longbowman", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Longbowman", "stat": "Line of sight", "op": "+", "value": 5.0}, {"target": "unit:Longbowman", "stat": "Attack range", "op": "+", "value": 3.0}], "abilities": []},
      {"id": "so_tech_Fletching", "a": 0, "r": 3, "t": "Fletching", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Improved arrow flight: +5 attack range for every ranged unit."], "req": [], "stats": [], "cost": {"s": 80, "i": 30, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Attack range", "op": "+", "value": 5.0}, {"target": "type:Siege", "stat": "Attack range", "op": "+", "value": 5.0}], "abilities": []},
      {"id": "so_tech_IronTippedArrows", "a": 1, "r": 4, "t": "Iron-Tipped Arrows", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Arrow tier 2: ranged units deal +15% damage."], "req": ["Stone-Tipped Arrows", "Archery Range - Lv 1"], "stats": [], "cost": {"s": 0, "i": 200, "v": 225, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Damage", "op": "+%", "value": 15.0}], "abilities": []},
      {"id": "so_tech_SeasonedArchers", "a": 1, "r": 5, "t": "Seasoned Archers", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+20 HP, +3 line of sight, +2 attack range to all Archery Range archers."], "req": ["Archery Range - Lv 1"], "stats": [], "cost": {"s": 80, "i": 20, "v": 0, "vs": 0}, "up": true, "researchTime": 25.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Archer", "stat": "HP", "op": "+", "value": 20.0}, {"target": "unit:Archer", "stat": "Line of sight", "op": "+", "value": 3.0}, {"target": "unit:Archer", "stat": "Attack range", "op": "+", "value": 2.0}, {"target": "unit:Crossbowman", "stat": "HP", "op": "+", "value": 20.0}, {"target": "unit:Crossbowman", "stat": "Line of sight", "op": "+", "value": 3.0}, {"target": "unit:Crossbowman", "stat": "Attack range", "op": "+", "value": 2.0}, {"target": "unit:Longbowman", "stat": "HP", "op": "+", "value": 20.0}, {"target": "unit:Longbowman", "stat": "Line of sight", "op": "+", "value": 3.0}, {"target": "unit:Longbowman", "stat": "Attack range", "op": "+", "value": 2.0}], "abilities": []},
      {"id": "so_tech_ShardTippedArrows", "a": 3, "r": 5, "t": "Shard-Tipped Arrows", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Arrow tier 4: ranged units deal a further +75% damage. Needs Veilsteel."], "req": ["Veilstone-Tipped Arrows", "Archery Range - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 170}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Damage", "op": "+%", "value": 75.0}], "abilities": []},
      {"id": "so_tech_StoneTippedArrows", "a": 0, "r": 4, "t": "Stone-Tipped Arrows", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weighted stone tips: ranged units gain +2 damage. (Interim faction-wide bump until per-battalion upgrades ship.)"], "req": [], "stats": [], "cost": {"s": 80, "i": 20, "v": 0, "vs": 0}, "up": true, "researchTime": 25.0, "culture": "", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeilstoneTippedArrows", "a": 2, "r": 4, "t": "Veilstone-Tipped Arrows", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Arrow tier 3: ranged units deal a further +30% damage."], "req": ["Iron-Tipped Arrows", "Archery Range - Lv 2"], "stats": [], "cost": {"s": 0, "i": 0, "v": 300, "vs": 33}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Damage", "op": "+%", "value": 30.0}], "abilities": []},
      {"id": "so_tech_VeteranArchers", "a": 2, "r": 5, "t": "Veteran Archers", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+20 HP, +1 damage, +3 line of sight, +2 attack range to all Archery Range archers."], "req": ["Seasoned Archers", "Archery Range - Lv 2"], "stats": [], "cost": {"s": 0, "i": 40, "v": 180, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Archer", "stat": "HP", "op": "+", "value": 20.0}, {"target": "unit:Archer", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Archer", "stat": "Line of sight", "op": "+", "value": 3.0}, {"target": "unit:Archer", "stat": "Attack range", "op": "+", "value": 2.0}, {"target": "unit:Crossbowman", "stat": "HP", "op": "+", "value": 20.0}, {"target": "unit:Crossbowman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Crossbowman", "stat": "Line of sight", "op": "+", "value": 3.0}, {"target": "unit:Crossbowman", "stat": "Attack range", "op": "+", "value": 2.0}, {"target": "unit:Longbowman", "stat": "HP", "op": "+", "value": 20.0}, {"target": "unit:Longbowman", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Longbowman", "stat": "Line of sight", "op": "+", "value": 3.0}, {"target": "unit:Longbowman", "stat": "Attack range", "op": "+", "value": 2.0}], "abilities": []},
      {"id": "so_tech_IronBrigandine", "a": 1, "r": 6, "t": "Iron Brigandine", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Ranged armour tier 1: +1 defense to all ranged units."], "req": ["Archery Range - Lv 1"], "stats": [], "cost": {"s": 120, "i": 150, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "type:Ranged", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "type:Ranged", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "type:Ranged", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_ShardBrigandine", "a": 3, "r": 6, "t": "Shard Brigandine", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Ranged armour tier 3: +3 defense to all ranged units."], "req": ["Veilstone Brigandine", "Archery Range - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 140}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Melee defense", "op": "+", "value": 3.0}, {"target": "type:Ranged", "stat": "Ranged defense", "op": "+", "value": 3.0}, {"target": "type:Ranged", "stat": "Siege defense", "op": "+", "value": 3.0}, {"target": "type:Ranged", "stat": "Magic defense", "op": "+", "value": 3.0}], "abilities": []},
      {"id": "so_tech_VeilstoneBrigandine", "a": 2, "r": 6, "t": "Veilstone Brigandine", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Ranged armour tier 2: +2 defense to all ranged units."], "req": ["Iron Brigandine", "Archery Range - Lv 2"], "stats": [], "cost": {"s": 0, "i": 150, "v": 120, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Ranged", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "type:Ranged", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "type:Ranged", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "type:Ranged", "stat": "Magic defense", "op": "+", "value": 2.0}], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_RoyalStable",
    "banner": "Royal Stable - Heavy-cavalry trainer (Cataphract).",
    "mil": true,
    "cost": {"s": 220, "i": 80, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_RoyalStable"], ["Building Type", "Alanthor"], ["HP", "1000"], ["Armor type", "structure_human"], ["Melee defense", "4"], ["Ranged defense", "10"], ["Siege defense", "0"], ["Magic defense", "2"], ["Line of sight", "18"], ["Building radius", "1.6"], ["Footprint (cells)", "4 x 4"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "Outrider, Cataphract"], ["Research", "Stone-Barded Lances, Iron-Barded Lances, Veilstone Lances, Shard-infused Lances, Charge, Elite Cavalry, Full Gallop, Seasoned Cavalry, Veteran Cavalry, War Horn, Iron Barding, Shard Barding, Veilstone Barding"], ["Tags", "Building"], ["PresentationId", "356"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_RoyalStable_1", "a": 1, "r": 1, "t": "Royal Stable - Lv 1", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_RoyalStable_2", "a": 2, "r": 1, "t": "Royal Stable - Lv 2", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87"], "req": ["Royal Stable - Lv 1"], "stats": [], "cost": {"s": 167, "i": 40, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_RoyalStable_3", "a": 3, "r": 1, "t": "Royal Stable - Lv 3", "k": "lvl", "o": "mil", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833"], "req": ["Royal Stable - Lv 2"], "stats": [], "cost": {"s": 340, "i": 80, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_RoyalStable_Alanthor_Outrider", "a": 1, "r": 2, "t": "Outrider", "k": "mil", "o": "", "arw": false, "type": "Cavalry", "race": "", "size": "", "d": ["Trained at Royal Stable"], "req": ["Royal Stable - Lv 1"], "stats": [["Id", "Alanthor_Outrider"], ["Class", "human_cavalry"], ["HP", "95"], ["Speed", "8.2"], ["Training time", "15.4"], ["Population cost", "1"], ["Damage", "12"], ["Damage type", "melee"], ["Armor type", "cavalry"], ["Melee defense", "2"], ["Ranged defense", "2"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "1.4"], ["Min attack range", "0"], ["Attack range", "1.6"], ["Line of sight", "34"], ["Aim time", "0"], ["Radius", "0.55"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "1"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+12 vs Ranged"], ["Tags", "Cavalry, Light"], ["Abilities", "none"], ["PresentationId", "349"]], "cost": {"s": 0, "i": 22, "v": 93, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_RoyalStable_Alanthor_Cataphract", "a": 3, "r": 2, "t": "Cataphract", "k": "mil", "o": "", "arw": false, "type": "Cavalry", "race": "", "size": "", "d": ["Trained at Royal Stable"], "req": ["Royal Stable - Lv 3"], "stats": [["Id", "Alanthor_Cataphract"], ["Class", "human_cavalry"], ["HP", "160"], ["Speed", "6.6"], ["Training time", "28"], ["Population cost", "2"], ["Damage", "18"], ["Damage type", "melee"], ["Armor type", "cavalry"], ["Melee defense", "5"], ["Ranged defense", "4"], ["Siege defense", "0"], ["Magic defense", "1"], ["Attack cooldown", "1.6"], ["Min attack range", "0"], ["Attack range", "1.6"], ["Line of sight", "20"], ["Aim time", "0"], ["Radius", "0.6"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "3"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+20 vs Ranged"], ["Tags", "Cavalry, Heavy"], ["Abilities", "none"], ["PresentationId", "336"]], "cost": {"s": 0, "i": 66, "v": 189, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_StoneBardedLances", "a": 1, "r": 3, "t": "Stone-Barded Lances", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weapon tier 1: cavalry gain +2 damage."], "req": [], "stats": [], "cost": {"s": 80, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 25.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Cavalry", "stat": "Damage", "op": "+", "value": 2.0}], "abilities": []},
      {"id": "so_tech_IronBardedLances", "a": 1, "r": 4, "t": "Iron-Barded Lances", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weapon tier 2: cavalry deal +15% damage."], "req": ["Stone-Barded Lances", "Royal Stable - Lv 1"], "stats": [], "cost": {"s": 0, "i": 200, "v": 225, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Cavalry", "stat": "Damage", "op": "+%", "value": 15.0}], "abilities": []},
      {"id": "so_tech_VeilstoneLances", "a": 2, "r": 2, "t": "Veilstone Lances", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weapon tier 3: cavalry deal a further +30% damage."], "req": ["Iron-Barded Lances", "Royal Stable - Lv 2"], "stats": [], "cost": {"s": 0, "i": 0, "v": 300, "vs": 33}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Cavalry", "stat": "Damage", "op": "+%", "value": 30.0}], "abilities": []},
      {"id": "so_tech_ShardInfusedLances", "a": 3, "r": 3, "t": "Shard-infused Lances", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Weapon tier 4: cavalry deal a further +75% damage. Needs Veilsteel."], "req": ["Veilstone Lances", "Royal Stable - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 170}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Cavalry", "stat": "Damage", "op": "+%", "value": 75.0}], "abilities": []},
      {"id": "so_tech_CavalryCharge", "a": 1, "r": 5, "t": "Charge", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Cavalry charge: after 5 s out of combat a horseman closes at +50% speed for 2 s and his first blow in that window deals +30%. Miss the window and it resets.", "passive: FirstStrikePct Set 30", "passive: ChargeSpeedPct Set 50", "passive: ChargeWindowSeconds Set 2", "passive: ChargeRearmSeconds Set 5"], "req": ["Royal Stable - Lv 1"], "stats": [], "cost": {"s": 120, "i": 60, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_EliteCavalry", "a": 3, "r": 4, "t": "Elite Cavalry", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+30 HP, +15% speed, +2 damage, +2 defense to Outriders and Cataphracts."], "req": ["Veteran Cavalry", "Royal Stable - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 335, "vs": 78}, "up": true, "researchTime": 48.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Outrider", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Outrider", "stat": "Speed", "op": "+%", "value": 15.0}, {"target": "unit:Outrider", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Outrider", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "unit:Outrider", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "unit:Outrider", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "unit:Outrider", "stat": "Magic defense", "op": "+", "value": 2.0}, {"target": "unit:Cataphract", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Cataphract", "stat": "Speed", "op": "+%", "value": 15.0}, {"target": "unit:Cataphract", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Cataphract", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "unit:Cataphract", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "unit:Cataphract", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "unit:Cataphract", "stat": "Magic defense", "op": "+", "value": 2.0}], "abilities": []},
      {"id": "so_tech_FullGallop", "a": 3, "r": 5, "t": "Full Gallop", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the Full Gallop active for Alanthor cavalry: allied cavalry within 15 m gain +40% move speed for 8 s but cannot attack during the burst. 60 s cooldown."], "req": ["War Horn", "Royal Stable - Lv 3"], "stats": [], "cost": {"s": 0, "i": 100, "v": 410, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_SeasonedCavalry", "a": 1, "r": 6, "t": "Seasoned Cavalry", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+30 HP, +5% speed, +1 damage, +1 defense to Outriders and Cataphracts."], "req": ["Royal Stable - Lv 1"], "stats": [], "cost": {"s": 120, "i": 50, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Outrider", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Outrider", "stat": "Speed", "op": "+%", "value": 5.0}, {"target": "unit:Outrider", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Cataphract", "stat": "Speed", "op": "+%", "value": 5.0}, {"target": "unit:Cataphract", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_VeteranCavalry", "a": 2, "r": 3, "t": "Veteran Cavalry", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+30 HP, +10% speed, +1 damage, +1 defense to Outriders and Cataphracts."], "req": ["Seasoned Cavalry", "Royal Stable - Lv 2"], "stats": [], "cost": {"s": 0, "i": 90, "v": 320, "vs": 0}, "up": true, "researchTime": 38.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Outrider", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Outrider", "stat": "Speed", "op": "+%", "value": 10.0}, {"target": "unit:Outrider", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Outrider", "stat": "Magic defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "HP", "op": "+", "value": 30.0}, {"target": "unit:Cataphract", "stat": "Speed", "op": "+%", "value": 10.0}, {"target": "unit:Cataphract", "stat": "Damage", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "unit:Cataphract", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_WarHorn", "a": 2, "r": 4, "t": "War Horn", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the War Horn active for Alanthor cavalry: allied cavalry within 15 m gain +50% damage on their next charge. 20 s window, 60 s cooldown."], "req": ["Royal Stable - Lv 2"], "stats": [], "cost": {"s": 173, "i": 60, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_IronBarding", "a": 1, "r": 7, "t": "Iron Barding", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Cavalry armour tier 1: +1 defense to all cavalry."], "req": ["Royal Stable - Lv 1"], "stats": [], "cost": {"s": 120, "i": 150, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Cavalry", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "type:Cavalry", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "type:Cavalry", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "type:Cavalry", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_ShardBarding", "a": 3, "r": 6, "t": "Shard Barding", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Cavalry armour tier 3: +3 defense to all cavalry."], "req": ["Veilstone Barding", "Royal Stable - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 140}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Cavalry", "stat": "Melee defense", "op": "+", "value": 3.0}, {"target": "type:Cavalry", "stat": "Ranged defense", "op": "+", "value": 3.0}, {"target": "type:Cavalry", "stat": "Siege defense", "op": "+", "value": 3.0}, {"target": "type:Cavalry", "stat": "Magic defense", "op": "+", "value": 3.0}], "abilities": []},
      {"id": "so_tech_VeilstoneBarding", "a": 2, "r": 5, "t": "Veilstone Barding", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Cavalry armour tier 2: +2 defense to all cavalry."], "req": ["Iron Barding", "Royal Stable - Lv 2"], "stats": [], "cost": {"s": 0, "i": 150, "v": 120, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Cavalry", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "type:Cavalry", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "type:Cavalry", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "type:Cavalry", "stat": "Magic defense", "op": "+", "value": 2.0}], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_SiegeYard",
    "banner": "Siege Yard - Train siege engines",
    "mil": true,
    "cost": {"s": 300, "i": 100, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_SiegeYard"], ["Building Type", "Alanthor"], ["HP", "1300"], ["Armor type", "structure_human"], ["Melee defense", "5"], ["Ranged defense", "11"], ["Siege defense", "0"], ["Magic defense", "2"], ["Line of sight", "20"], ["Building radius", "1.6"], ["Footprint (cells)", "4 x 4"], ["Build time", "35"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "Ballista, Battering Ram, Catapult, Trebuchet"], ["Research", "Stone Shot, Iron Shot, Veilstone Shot, Shard-infused Shot, Counterweight Tuning, Elite Crews, Iron-Shod Ram, Ranging Shot, Reinforced Bolts, Seasoned Crews, Siege Screens, Veteran Crews, Iron Plating, Shard Plating, Veilstone Plating"], ["Tags", "Building"], ["PresentationId", "357"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_SiegeYard_1", "a": 1, "r": 1, "t": "Siege Yard - Lv 1", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_SiegeYard_2", "a": 2, "r": 1, "t": "Siege Yard - Lv 2", "k": "lvl", "o": "mil", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87"], "req": ["Siege Yard - Lv 1"], "stats": [], "cost": {"s": 193, "i": 60, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_SiegeYard_3", "a": 3, "r": 1, "t": "Siege Yard - Lv 3", "k": "lvl", "o": "mil", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833"], "req": ["Siege Yard - Lv 2"], "stats": [], "cost": {"s": 380, "i": 120, "v": 0, "vs": 0}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_SiegeYard_Alanthor_Ballista", "a": 1, "r": 2, "t": "Ballista", "k": "mil", "o": "", "arw": false, "type": "Siege", "race": "", "size": "", "d": ["Trained at Siege Yard"], "req": ["Siege Yard - Lv 1"], "stats": [["Id", "Alanthor_Ballista"], ["Class", "machinery_siege"], ["HP", "220"], ["Speed", "3.2"], ["Training time", "26.6"], ["Population cost", "2"], ["Damage", "40"], ["Damage type", "siege"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "6"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "4"], ["Min attack range", "6"], ["Attack range", "22"], ["Line of sight", "26"], ["Aim time", "1"], ["Radius", "0.8"], ["Trajectory", "flat"], ["Projectile speed", "55"], ["Min building level", "1"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+30 vs Building"], ["Tags", "Ranged, Siege, Heavy"], ["Abilities", "none"], ["PresentationId", "385"]], "cost": {"s": 0, "i": 44, "v": 121, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_SiegeYard_Alanthor_BatteringRam", "a": 2, "r": 2, "t": "Battering Ram", "k": "mil", "o": "", "arw": false, "type": "Siege", "race": "", "size": "", "d": ["Trained at Siege Yard"], "req": ["Siege Yard - Lv 2"], "stats": [["Id", "Alanthor_BatteringRam"], ["Class", "machinery_siege"], ["HP", "340"], ["Speed", "3"], ["Training time", "25.2"], ["Population cost", "2"], ["Damage", "36"], ["Damage type", "siege"], ["Armor type", "ranged"], ["Melee defense", "2"], ["Ranged defense", "8"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "3"], ["Min attack range", "0"], ["Attack range", "1"], ["Line of sight", "18"], ["Aim time", "0"], ["Radius", "1"], ["Trajectory", "none"], ["Projectile speed", "0"], ["Min building level", "2"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "0"], ["Bonus vs tags", "+80 vs Building"], ["Tags", "Siege, Heavy"], ["Abilities", "none"], ["PresentationId", "347"]], "cost": {"s": 0, "i": 66, "v": 138, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_SiegeYard_Alanthor_Catapult", "a": 1, "r": 3, "t": "Catapult", "k": "mil", "o": "", "arw": false, "type": "Siege", "race": "", "size": "", "d": ["Trained at Siege Yard"], "req": ["Siege Yard - Lv 1"], "stats": [["Id", "Alanthor_Catapult"], ["Class", "machinery_siege"], ["HP", "220"], ["Speed", "3.2"], ["Training time", "26.6"], ["Population cost", "2"], ["Damage", "40"], ["Damage type", "siege"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "6"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "4"], ["Min attack range", "10"], ["Attack range", "30"], ["Line of sight", "20"], ["Aim time", "1.2"], ["Radius", "0.8"], ["Trajectory", "high"], ["Projectile speed", "26"], ["Min building level", "1"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "3"], ["Bonus vs tags", "+30 vs Building, +20 vs Infantry"], ["Tags", "Ranged, Siege, Heavy"], ["Abilities", "none"], ["PresentationId", "337"]], "cost": {"s": 0, "i": 44, "v": 121, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_unit_Alanthor_SiegeYard_Alanthor_Trebuchet", "a": 3, "r": 2, "t": "Trebuchet", "k": "mil", "o": "", "arw": false, "type": "Siege", "race": "", "size": "", "d": ["Trained at Siege Yard"], "req": ["Siege Yard - Lv 3"], "stats": [["Id", "Alanthor_Trebuchet"], ["Class", "machinery_siege"], ["HP", "200"], ["Speed", "2.4"], ["Training time", "35"], ["Population cost", "3"], ["Damage", "60"], ["Damage type", "siege"], ["Armor type", "ranged"], ["Melee defense", "0"], ["Ranged defense", "5"], ["Siege defense", "0"], ["Magic defense", "0"], ["Attack cooldown", "6"], ["Min attack range", "12"], ["Attack range", "38"], ["Line of sight", "30"], ["Aim time", "1"], ["Radius", "1"], ["Trajectory", "high"], ["Projectile speed", "14"], ["Min building level", "3"], ["Build speed", "0"], ["Gathering speed", "0"], ["Heals per second", "0"], ["Heal range", "0"], ["Siege range", "0"], ["Siege cooldown", "0"], ["AoE radius", "6"], ["Bonus vs tags", "+80 vs Building"], ["Tags", "Ranged, Siege, Heavy"], ["Abilities", "none"], ["PresentationId", "348"]], "cost": {"s": 0, "i": 99, "v": 245, "vs": 0}, "up": false, "researchTime": 0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_StoneShotEngines", "a": 1, "r": 4, "t": "Stone Shot", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Munition tier 1: siege engines gain +2 damage."], "req": [], "stats": [], "cost": {"s": 80, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 25.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Siege", "stat": "Damage", "op": "+", "value": 2.0}], "abilities": []},
      {"id": "so_tech_IronShotEngines", "a": 1, "r": 5, "t": "Iron Shot", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Munition tier 2: siege engines deal +15% damage."], "req": ["Stone Shot", "Siege Yard - Lv 1"], "stats": [], "cost": {"s": 0, "i": 200, "v": 225, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Siege", "stat": "Damage", "op": "+%", "value": 15.0}], "abilities": []},
      {"id": "so_tech_VeilstoneShot", "a": 2, "r": 3, "t": "Veilstone Shot", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Munition tier 3: siege engines deal a further +30% damage."], "req": ["Iron Shot", "Siege Yard - Lv 2"], "stats": [], "cost": {"s": 0, "i": 0, "v": 300, "vs": 33}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Siege", "stat": "Damage", "op": "+%", "value": 30.0}], "abilities": []},
      {"id": "so_tech_ShardInfusedShot", "a": 3, "r": 3, "t": "Shard-infused Shot", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Munition tier 4: siege engines deal a further +75% damage. Needs Veilsteel."], "req": ["Veilstone Shot", "Siege Yard - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 170}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Siege", "stat": "Damage", "op": "+%", "value": 75.0}], "abilities": []},
      {"id": "so_tech_CounterweightTuning", "a": 3, "r": 4, "t": "Counterweight Tuning", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Retuned counterweights: Trebuchet range 38 to 44."], "req": ["Siege Yard - Lv 3"], "stats": [], "cost": {"s": 320, "i": 160, "v": 0, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Trebuchet", "stat": "Attack range", "op": "+", "value": 6.0}], "abilities": []},
      {"id": "so_tech_EliteCrews", "a": 3, "r": 5, "t": "Elite Crews", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+60 HP, +5 damage, +3 attack range to every Alanthor siege engine."], "req": ["Veteran Crews", "Siege Yard - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 390, "vs": 87}, "up": true, "researchTime": 48.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Ballista", "stat": "HP", "op": "+", "value": 60.0}, {"target": "unit:Ballista", "stat": "Damage", "op": "+", "value": 5.0}, {"target": "unit:Ballista", "stat": "Attack range", "op": "+", "value": 3.0}, {"target": "unit:Battering Ram", "stat": "HP", "op": "+", "value": 60.0}, {"target": "unit:Battering Ram", "stat": "Damage", "op": "+", "value": 5.0}, {"target": "unit:Battering Ram", "stat": "Attack range", "op": "+", "value": 3.0}, {"target": "unit:Trebuchet", "stat": "HP", "op": "+", "value": 60.0}, {"target": "unit:Trebuchet", "stat": "Damage", "op": "+", "value": 5.0}, {"target": "unit:Trebuchet", "stat": "Attack range", "op": "+", "value": 3.0}], "abilities": []},
      {"id": "so_tech_IronShodRam", "a": 2, "r": 4, "t": "Iron-Shod Ram", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["The ram is iron-shod: +100 HP so it survives the approach."], "req": ["Siege Yard - Lv 2"], "stats": [], "cost": {"s": 220, "i": 140, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Battering Ram", "stat": "HP", "op": "+", "value": 100.0}], "abilities": []},
      {"id": "so_tech_RangingShot", "a": 2, "r": 5, "t": "Ranging Shot", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the Ranging Shot active for Alanthor siege engines: after standing still for 3 s the next shot deals +100% damage. 60 s cooldown."], "req": ["Siege Yard - Lv 2"], "stats": [], "cost": {"s": 220, "i": 90, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_ReinforcedBolts", "a": 1, "r": 6, "t": "Reinforced Bolts", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Ballista bolts punch harder: +10 damage."], "req": ["Siege Yard - Lv 1"], "stats": [], "cost": {"s": 180, "i": 90, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Ballista", "stat": "Damage", "op": "+", "value": 10.0}], "abilities": []},
      {"id": "so_tech_SeasonedCrews", "a": 1, "r": 7, "t": "Seasoned Crews", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+40 HP, +2 damage, +1 attack range to every Alanthor siege engine."], "req": ["Siege Yard - Lv 1"], "stats": [], "cost": {"s": 140, "i": 60, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Ballista", "stat": "HP", "op": "+", "value": 40.0}, {"target": "unit:Ballista", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Ballista", "stat": "Attack range", "op": "+", "value": 1.0}, {"target": "unit:Battering Ram", "stat": "HP", "op": "+", "value": 40.0}, {"target": "unit:Battering Ram", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Battering Ram", "stat": "Attack range", "op": "+", "value": 1.0}, {"target": "unit:Trebuchet", "stat": "HP", "op": "+", "value": 40.0}, {"target": "unit:Trebuchet", "stat": "Damage", "op": "+", "value": 2.0}, {"target": "unit:Trebuchet", "stat": "Attack range", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_SiegeScreens", "a": 3, "r": 6, "t": "Siege Screens", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the Siege Screens passive for Alanthor siege engines: +50% ranged defense while stationary; lost the moment the engine moves."], "req": ["Ranging Shot", "Siege Yard - Lv 3"], "stats": [], "cost": {"s": 0, "i": 130, "v": 480, "vs": 0}, "up": true, "researchTime": 50.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_VeteranCrews", "a": 2, "r": 6, "t": "Veteran Crews", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["+40 HP, +3 damage, +2 attack range to every Alanthor siege engine."], "req": ["Seasoned Crews", "Siege Yard - Lv 2"], "stats": [], "cost": {"s": 0, "i": 110, "v": 360, "vs": 0}, "up": true, "researchTime": 38.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "unit:Ballista", "stat": "HP", "op": "+", "value": 40.0}, {"target": "unit:Ballista", "stat": "Damage", "op": "+", "value": 3.0}, {"target": "unit:Ballista", "stat": "Attack range", "op": "+", "value": 2.0}, {"target": "unit:Battering Ram", "stat": "HP", "op": "+", "value": 40.0}, {"target": "unit:Battering Ram", "stat": "Damage", "op": "+", "value": 3.0}, {"target": "unit:Battering Ram", "stat": "Attack range", "op": "+", "value": 2.0}, {"target": "unit:Trebuchet", "stat": "HP", "op": "+", "value": 40.0}, {"target": "unit:Trebuchet", "stat": "Damage", "op": "+", "value": 3.0}, {"target": "unit:Trebuchet", "stat": "Attack range", "op": "+", "value": 2.0}], "abilities": []},
      {"id": "so_tech_IronPlating", "a": 1, "r": 8, "t": "Iron Plating", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Siege armour tier 1: +1 defense to all siege engines."], "req": ["Siege Yard - Lv 1"], "stats": [], "cost": {"s": 120, "i": 150, "v": 0, "vs": 0}, "up": true, "researchTime": 35.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Siege", "stat": "Melee defense", "op": "+", "value": 1.0}, {"target": "type:Siege", "stat": "Ranged defense", "op": "+", "value": 1.0}, {"target": "type:Siege", "stat": "Siege defense", "op": "+", "value": 1.0}, {"target": "type:Siege", "stat": "Magic defense", "op": "+", "value": 1.0}], "abilities": []},
      {"id": "so_tech_ShardPlating", "a": 3, "r": 7, "t": "Shard Plating", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Siege armour tier 3: +3 defense to all siege engines."], "req": ["Veilstone Plating", "Siege Yard - Lv 3"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 140}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Siege", "stat": "Melee defense", "op": "+", "value": 3.0}, {"target": "type:Siege", "stat": "Ranged defense", "op": "+", "value": 3.0}, {"target": "type:Siege", "stat": "Siege defense", "op": "+", "value": 3.0}, {"target": "type:Siege", "stat": "Magic defense", "op": "+", "value": 3.0}], "abilities": []},
      {"id": "so_tech_VeilstonePlating", "a": 2, "r": 7, "t": "Veilstone Plating", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Siege armour tier 2: +2 defense to all siege engines."], "req": ["Iron Plating", "Siege Yard - Lv 2"], "stats": [], "cost": {"s": 0, "i": 150, "v": 120, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [{"target": "type:Siege", "stat": "Melee defense", "op": "+", "value": 2.0}, {"target": "type:Siege", "stat": "Ranged defense", "op": "+", "value": 2.0}, {"target": "type:Siege", "stat": "Siege defense", "op": "+", "value": 2.0}, {"target": "type:Siege", "stat": "Magic defense", "op": "+", "value": 2.0}], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_Tower",
    "banner": "Watch Tower - Defensive tower with long range",
    "mil": false,
    "cost": {"s": 280, "i": 140, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_Tower"], ["Building Type", "Alanthor"], ["HP", "700"], ["Armor type", "structure_human"], ["Melee defense", "6"], ["Ranged defense", "13"], ["Siege defense", "0"], ["Magic defense", "3"], ["Line of sight", "28"], ["Building radius", "1.6"], ["Footprint (cells)", "2 x 2"], ["Build time", "25"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "4"], ["Garrison arrows per occupant", "1"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "yes"], ["Attack damage", "10"], ["Attack damage type", "ranged"], ["Attack range", "24"], ["Attack cooldown", "1.8"], ["Auto-fire max targets", "1"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "354"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_Tower_1", "a": 1, "r": 1, "t": "Watch Tower - Lv 1", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.1", "Train time x0.87", "Attack cooldown x0.909", "Line of sight 28", "Attack 10 ranged, range 24, every 1.8 s, 1 target(s)"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_Tower_2", "a": 2, "r": 1, "t": "Watch Tower - Lv 2", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15", "Train time x0.8", "Attack cooldown x0.87", "Line of sight 32", "Attack 10 ranged, range 28, every 1.5 s, 1 target(s)"], "req": ["Watch Tower - Lv 1"], "stats": [], "cost": {"s": 266, "i": 120, "v": 0, "vs": 0}, "up": true, "researchTime": 25.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_Tower_3", "a": 3, "r": 1, "t": "Watch Tower - Lv 3", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.2", "Train time x0.714", "Attack cooldown x0.833", "Line of sight 36", "Attack 10 ranged, range 32, every 1.2 s, 2 target(s), siege shot 20"], "req": ["Watch Tower - Lv 2"], "stats": [], "cost": {"s": 560, "i": 240, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_Wall",
    "banner": "Stone Wall -> Battlemented Wall / Shielded Wall (Alanthor) - Alanthor's walkable stone wall — Battlements and Shielded Ramparts are researched at its hubs",
    "mil": false,
    "cost": {"s": 100, "i": 40, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_Wall"], ["Building Type", "Alanthor"], ["HP", "600"], ["Armor type", "structure_human"], ["Melee defense", "8"], ["Ranged defense", "14"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "10"], ["Building radius", "1.6"], ["Footprint (cells)", "2 x 2"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "Shielded Ramparts, Battlements"], ["Tags", "Building"], ["PresentationId", "550"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_Wall_1", "a": 1, "r": 1, "t": "Stone Wall - Lv 1", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.6"], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 0.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_Wall_2", "a": 2, "r": 1, "t": "Battlemented Wall - Lv 2", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x2.3"], "req": ["Stone Wall - Lv 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 0.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_Wall_3", "a": 3, "r": 1, "t": "Shielded Wall - Lv 3", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x3"], "req": ["Battlemented Wall - Lv 2"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 0.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_ShieldedRamparts", "a": 1, "r": 2, "t": "Shielded Ramparts", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Iron bands at the coping and great shields hung along the outer face: 200 percent HP over the timber you started with, and every curtain module gains two garrison slots for infantry or archers. Every wall you own is re-clad at once."], "req": ["Battlements"], "stats": [], "cost": {"s": 400, "i": 250, "v": 60, "vs": 0}, "up": true, "researchTime": 60.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Battlements", "a": 1, "r": 3, "t": "Battlements", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["A full merlon crown, arrow loops and timber hoardings along the outer face. Every wall you own is rebuilt to it at once: 130 percent tougher than the timber you started with."], "req": [], "stats": [], "cost": {"s": 300, "i": 180, "v": 20, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_WallTower",
    "banner": "Wall Tower - Wall tower — a curtain module converted into a ranged tower",
    "mil": false,
    "cost": {"s": 120, "i": 60, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_WallTower"], ["Building Type", "Alanthor"], ["HP", "900"], ["Armor type", "structure_human"], ["Melee defense", "8"], ["Ranged defense", "14"], ["Siege defense", "0"], ["Magic defense", "4"], ["Line of sight", "16"], ["Building radius", "1.6"], ["Footprint (cells)", "4 x 4"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "0"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "none"], ["Tags", "Building"], ["PresentationId", "553"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_WallTower_0", "a": 1, "r": 1, "t": "Wall Tower", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["Wall tower — a curtain module converted into a ranged tower"], "req": [], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": false, "researchTime": 0, "culture": "", "scope": "per", "on": false, "effects": [], "abilities": []}
    ]
  },
  {
    "id": "so_b_Alanthor_TradingOutpost",
    "banner": "Trading Outpost - Trade post beside a veilstone outcrop - buys veilstone with supplies and iron, or forges veilstone into veilsteel",
    "mil": false,
    "cost": {"s": 160, "i": 80, "v": 0, "vs": 0},
    "stats": [["id", "Alanthor_TradingOutpost"], ["Building Type", "Alanthor"], ["HP", "650"], ["Armor type", "structure_human"], ["Melee defense", "3"], ["Ranged defense", "9"], ["Siege defense", "0"], ["Magic defense", "1"], ["Line of sight", "14"], ["Building radius", "1"], ["Footprint (cells)", "2 x 2"], ["Build time", "30"], ["Population", "0"], ["Supplies per tick", "0"], ["Supplies interval", "0"], ["Slot income per min (L1/L2/L3)", "0"], ["Interest per minute", "0"], ["Interest principal cap", "0"], ["Interest per minute (Coffers / Charters / Bonds)", "0 / 0 / 0"], ["Max per faction", "0"], ["Min wall level", "0"], ["Garrison slots", "0"], ["Garrison arrows per occupant", "0"], ["Max iron", "0"], ["Max veilstone", "0"], ["Segment HP", "0"], ["Segment line of sight", "0"], ["Min era", "2"], ["Can attack", "no"], ["Train speed bonus", "0"], ["Trains", "none"], ["Research", "Veilsteel Forging, Veilsteel Export, Trade Agreements I, Swift Caravans, Trade Agreements II, Trade Agreements III"], ["Tags", "Building"], ["PresentationId", "523"]],
    "nodes": [
      {"id": "so_lvl_Alanthor_TradingOutpost_1", "a": 1, "r": 1, "t": "Trading Outpost - Lv 1", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": [], "req": ["Age 1"], "stats": [], "cost": {"s": 0, "i": 0, "v": 0, "vs": 0}, "up": true, "researchTime": 20.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_TradingOutpost_2", "a": 2, "r": 1, "t": "Trading Outpost - Lv 2", "k": "lvl", "o": "eco", "arw": true, "type": "", "race": "", "size": "", "d": ["HP x1.15"], "req": ["Trading Outpost - Lv 1"], "stats": [], "cost": {"s": 250, "i": 100, "v": 0, "vs": 0}, "up": true, "researchTime": 40.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_lvl_Alanthor_TradingOutpost_3", "a": 3, "r": 1, "t": "Trading Outpost - Lv 3", "k": "lvl", "o": "eco", "arw": false, "type": "", "race": "", "size": "", "d": ["HP x1.3"], "req": ["Trading Outpost - Lv 2"], "stats": [], "cost": {"s": 450, "i": 200, "v": 0, "vs": 0}, "up": true, "researchTime": 60.0, "culture": "Alanthor", "scope": "per", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Alanthor_VeilsteelForging", "a": 1, "r": 2, "t": "Veilsteel Forging", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the Forge Veilsteel trade: 50 veilstone into 10 veilsteel a minute per Outpost."], "req": [], "stats": [], "cost": {"s": 200, "i": 150, "v": 50, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Alanthor_VeilsteelExport", "a": 1, "r": 3, "t": "Veilsteel Export", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Unlocks the Sell Veilsteel trade: 50 veilsteel into 300 iron and 450 supplies a minute per Outpost."], "req": ["Veilsteel Forging"], "stats": [], "cost": {"s": 200, "i": 100, "v": 0, "vs": 40}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Alanthor_TradeAgreements1", "a": 1, "r": 4, "t": "Trade Agreements I", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Trading Outposts spend 20% less on every trade."], "req": [], "stats": [], "cost": {"s": 150, "i": 100, "v": 0, "vs": 0}, "up": true, "researchTime": 30.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Alanthor_SwiftCaravans", "a": 1, "r": 5, "t": "Swift Caravans", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Trading Outposts trade 50% faster: every trade spends and earns half as much again each minute.", "building:Alanthor_TradingOutpost: TradeSpeed Pct 50"], "req": ["Trade Agreements I"], "stats": [], "cost": {"s": 250, "i": 200, "v": 80, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Alanthor_TradeAgreements2", "a": 1, "r": 6, "t": "Trade Agreements II", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Trading Outposts spend 45% less on every trade."], "req": ["Trade Agreements I"], "stats": [], "cost": {"s": 300, "i": 200, "v": 60, "vs": 0}, "up": true, "researchTime": 45.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []},
      {"id": "so_tech_Alanthor_TradeAgreements3", "a": 1, "r": 7, "t": "Trade Agreements III", "k": "tech", "o": "", "arw": false, "type": "", "race": "", "size": "", "d": ["Trading Outposts spend 75% less on every trade."], "req": ["Trade Agreements II"], "stats": [], "cost": {"s": 500, "i": 350, "v": 120, "vs": 0}, "up": true, "researchTime": 60.0, "culture": "Alanthor", "scope": "global", "on": false, "effects": [], "abilities": []}
    ]
  }
];

function defaultUp(n) { return n.up !== undefined ? !!n.up : (n.k === "tech" || (n.k === "eco" && !(n.stats && n.stats.length))); }
function normalize(list) {
  return list.map((b) => ({
    id: b.id || uid(), banner: b.banner || "New building", mil: !!b.mil,
    cost: Object.assign(emptyCost(), b.cost || {}),
    stats: (b.stats || []).map((s) => [s[0], s[1]]),
    nodes: (b.nodes || []).map((n) => ({
      id: n.id || uid(), a: n.a || 0, r: n.r || 1, t: n.t || "New", k: n.k || "tech", o: n.o || "", arw: !!n.arw,
      type: n.type || "", race: n.race || "", size: n.size || "",
      d: n.d ? n.d.slice() : [], req: Array.isArray(n.req) ? n.req.slice() : (n.req ? [n.req] : []),
      stats: (n.stats || []).map((s) => [s[0], s[1]]),
      cost: Object.assign(emptyCost(), n.cost || {}),
      up: defaultUp(n),
      researchTime: Number(n.researchTime) || 0,
      culture: n.culture || "",
      scope: n.scope || (/Upgrade to Lv/i.test(n.t || "") ? "per" : "global"),
      on: !!n.on,
      effects: (n.effects || []).map((e) => ({ target: e.target || "building", stat: e.stat || "", op: e.op || "+%", value: Number(e.value) || 0 })),
      abilities: Array.isArray(n.abilities) ? n.abilities.slice() : [],
    })),
  }));
}
function normSchema(s) {
  s = s || {};
  const norm = (arr, def) => {
    const rows = (arr && arr.length ? arr : def).map((r) => [r[0], r[1]]);
    const have = new Set(rows.map((r) => r[0]));
    def.forEach((r) => { if (!have.has(r[0])) rows.push([r[0], r[1]]); });
    return rows;
  };
  return { buildingStats: norm(s.buildingStats, DEFAULT_SCHEMA.buildingStats), unitStats: norm(s.unitStats, DEFAULT_SCHEMA.unitStats) };
}
function normAbilities(list) {
  return (list || []).map((a) => ({
    id: a.id || uid(),
    name: a.name || "New ability",
    activation: a.activation || "Active",
    type: a.type || "self",
    castingTime: Number(a.castingTime) || 0,
    castingDuration: Number.isFinite(Number(a.castingDuration)) ? Number(a.castingDuration) : -1,
    radius: Number(a.radius) || 0,
    range: Number(a.range) || 0,
    effects: Array.isArray(a.effects) ? a.effects.slice() : [],
    aftermath: Array.isArray(a.aftermath) ? a.aftermath.slice() : (a.aftermath ? [a.aftermath] : []),
  }));
}
const DEFAULT_ABILITIES = [
  {"id": "ab_king_s_call", "name": "King's Call", "activation": "Passive", "type": "aura", "castingTime": 0.0, "castingDuration": -1.0, "radius": 15.0, "range": 0.0, "effects": ["+15% attack", "+15% armor", "+20 charge damage", "affects allied units of the caster's culture"], "aftermath": []},
  {"id": "ab_liquid_courage", "name": "Liquid Courage", "activation": "Active", "type": "self", "castingTime": 0.0, "castingDuration": 10.0, "radius": 0.0, "range": 0.0, "effects": ["-90% damage taken", "+30% attack"], "aftermath": ["Veilshift Withdrawal", "Life Cling", "45 s cooldown"]},
  {"id": "ab_veilshift_withdrawal", "name": "Veilshift Withdrawal", "activation": "Active", "type": "self", "castingTime": 0.0, "castingDuration": 5.0, "radius": 0.0, "range": 0.0, "effects": ["-50% move speed", "self damage 50% of max HP over the duration"], "aftermath": []},
  {"id": "ab_life_cling", "name": "Life Cling", "activation": "Active", "type": "self", "castingTime": 0.0, "castingDuration": 5.0, "radius": 0.0, "range": 0.0, "effects": ["HP never drops below 1"], "aftermath": []},
  {"id": "ab_automate_facility", "name": "Automate Facility", "activation": "Active", "type": "single target", "castingTime": 6.0, "castingDuration": 30.0, "radius": 0.0, "range": 5.0, "effects": ["+30% resource yield", "affects allied economy buildings"], "aftermath": ["Under Automation"]},
  {"id": "ab_under_automation", "name": "Under Automation", "activation": "Active", "type": "self", "castingTime": 0.0, "castingDuration": 60.0, "radius": 0.0, "range": 0.0, "effects": ["cannot be automated"], "aftermath": []},
  {"id": "ab_use_celestar", "name": "Use Celestar", "activation": "Active", "type": "area", "castingTime": 5.0, "castingDuration": 15.0, "radius": 8.0, "range": 0.0, "effects": ["reveals fog (radius 8)"], "aftermath": ["60 s cooldown"]},
  {"id": "ab_scout_sight", "name": "Scout Sight", "activation": "Passive", "type": "self", "castingTime": 0.0, "castingDuration": -1.0, "radius": 0.0, "range": 0.0, "effects": ["line of sight ramps while still"], "aftermath": []},
  {"id": "ab_war_horn", "name": "War Horn", "activation": "Active", "type": "area", "castingTime": 0.0, "castingDuration": 20.0, "radius": 15.0, "range": 0.0, "effects": ["+50% damage on the next charge", "affects allied cavalry"], "aftermath": ["60 s cooldown"]},
  {"id": "ab_full_gallop", "name": "Full Gallop", "activation": "Active", "type": "area", "castingTime": 0.0, "castingDuration": 8.0, "radius": 15.0, "range": 0.0, "effects": ["40% move speed", "cannot attack while buffed", "affects allied cavalry"], "aftermath": ["60 s cooldown"]},
  {"id": "ab_honour_thy_pledge", "name": "Honour thy Pledge", "activation": "Active", "type": "area", "castingTime": 0.0, "castingDuration": 0.0, "radius": 6.0, "range": 0.0, "effects": ["summons the pledge army (level-scaled)", "unlocks at hero level 4"], "aftermath": ["120 s cooldown"]},
  {"id": "ab_choreographed_volleys", "name": "Choreographed Volleys", "activation": "Active", "type": "area", "castingTime": 0.0, "castingDuration": 5.0, "radius": 15.0, "range": 0.0, "effects": ["+100% fire rate", "affects allied ranged units"], "aftermath": ["60 s cooldown"]},
  {"id": "ab_shardbound_fury", "name": "Shardbound Fury", "activation": "Active", "type": "area", "castingTime": 0.0, "castingDuration": 0.0, "radius": 25.0, "range": 0.0, "effects": ["Shardbound Fury (hurls enemies, damages buildings)", "affects enemies", "60 damage"], "aftermath": ["120 s cooldown"]}
];

// GENERATED VIEW: always start from the SO data baked in below. Nothing is
// persisted - edits are scratch and vanish on reload (tools/gen_calculator.py).
function load() {
  return { schema: normSchema(DEFAULT_SCHEMA), buildings: normalize(DEFAULT), abilities: normAbilities(DEFAULT_ABILITIES) };
}

function bName(b) { const id = (b.stats.find((s) => s[0] === "id") || [])[1]; return id || (b.banner || "").split(" — ")[0]; }
function entitiesOf(b) {
  const ents = [{ key: "building", name: bName(b), stats: b.stats, cost: b.cost, isB: true, node: null }];
  b.nodes.forEach((n) => { if (n.stats && n.stats.length) ents.push({ key: "unit:" + n.t, name: n.t, stats: n.stats, cost: n.cost, isB: false, node: n }); });
  return ents;
}
function bTypeOf(b) { return (b.stats.find((s) => s[0] === "Building Type") || [])[1] || ""; }
function statOptions(schema, target) { const k = target.split(":")[0]; const bldg = target === "building" || k === "bname" || k === "btype"; return (bldg ? schema.buildingStats : schema.unitStats).map((s) => s[0]); }
function targetLabel(t) { if (t === "building") return "Building"; const p = t.split(":"), v = p.slice(1).join(":"); if (p[0] === "unit") return v; if (p[0] === "bname") return v + " (building)"; if (p[0] === "btype") return "all " + v + " buildings"; return "all " + v + " (" + p[0] + ")"; }
function effLabel(e) { const pct = e.op.indexOf("%") > -1; const v = e.op[0] === "-" ? -e.value : e.value; return (v >= 0 ? "+" : "") + v + (pct ? "%" : ""); }
function effectApplies(e, effB, ent, entB) {
  if (e.target === "building") return ent.isB && effB.id === entB.id;
  const p = e.target.split(":"), k = p[0], val = p.slice(1).join(":");
  if (k === "bname") return ent.isB && ent.name === val;
  if (k === "btype") return ent.isB && bTypeOf(entB) === val;
  if (k === "unit") return !ent.isB && ent.name === val && effB.id === entB.id;
  if (!ent.isB && ent.node && (k === "type" || k === "race" || k === "size")) { const map = { type: ent.node.type, race: ent.node.race, size: ent.node.size }; return (map[k] || "") === val; }
  return false;
}
function schemaNames(schema, isB) { return (isB ? schema.buildingStats : schema.unitStats).map((s) => s[0]); }
function effectiveStats(all, schema, building, key) {
  const ent = entitiesOf(building).find((e) => e.key === key);
  if (!ent) return [];
  const own = {}; ent.stats.forEach(([k, v]) => { own[k] = v; });
  const names = schemaNames(schema, ent.isB);
  const extra = ent.stats.map((s) => s[0]).filter((n) => names.indexOf(n) < 0);
  const rows = names.concat(extra).map((n) => ({ k: n, base: (n in own) ? own[n] : "—", eff: (n in own) ? own[n] : "—", has: n in own, orig: n in own }));
  all.forEach((bb) => bb.nodes.forEach((n) => { if (n.up && n.on) n.effects.forEach((e) => { if (effectApplies(e, bb, ent, building)) { const row = rows.find((r) => r.k === e.stat); if (row) { if (row.has) row.eff = applyEffect(row.eff, e.op, e.value); else { row.eff = e.op === "set" ? String(e.value) : applyEffect("0", e.op, e.value); row.has = true; } } } }); }));
  rows.forEach((r) => (r.changed = r.orig && String(r.base) !== String(r.eff)));
  return rows.filter((r) => r.has);
}
function improvers(all, entB, ent) { const res = []; all.forEach((bb) => bb.nodes.forEach((n) => { if (n.up && n.on && n.effects.some((e) => effectApplies(e, bb, ent, entB))) res.push(n); })); return res; }
function buildingCostCalc(all, b) {
  const ent = entitiesOf(b)[0];
  let g = emptyCost(), p = emptyCost();
  b.nodes.forEach((n) => { if (n.up && n.on) { if (n.scope === "global") g = addCost(g, n.cost); else p = addCost(p, n.cost); } });
  all.forEach((bb) => { if (bb.id !== b.id) bb.nodes.forEach((n) => { if (n.up && n.on && n.effects.some((e) => effectApplies(e, bb, ent, b))) { if (n.scope === "global") g = addCost(g, n.cost); else p = addCost(p, n.cost); } }); });
  return { global: g, per: addCost(b.cost, p) };
}
function unitCostCalc(all, entB, ent) { let research = emptyCost(); improvers(all, entB, ent).forEach((n) => (research = addCost(research, n.cost))); return { research, production: ent.cost || emptyCost() }; }

function Chips({ cost, cls }) {
  return <div className={cls || "cost"}>{RES.map(([key, emo, label]) => { const val = (cost && cost[key]) || 0; return <span key={key} className={"chip" + (val ? "" : " zero")} title={label}>{emo} {fmt(val)}</span>; })}</div>;
}

function Node({ node, onEdit, onToggle, noAges, dragging, onDragStart, onDragEnd }) {
  const [open, setOpen] = useState(false);
  const cls = "node k-" + node.k + (node.o ? " o-" + node.o : "") + (node.up ? (node.on ? " on" : " off") : "") + (open ? " open" : "") + (dragging ? " dragging" : "");
  const tags = [node.type, node.race, node.size].filter(Boolean);
  return (
    <div className={cls} style={noAges ? { gridRow: node.r } : { gridColumn: node.a + 1, gridRow: node.r }}
      draggable
      onDragStart={(e) => { e.dataTransfer.setData("text/plain", node.id); e.dataTransfer.effectAllowed = "move"; setOpen(false); onDragStart(node.id); }}
      onDragEnd={onDragEnd}>
      {node.up && <input className="chk" type="checkbox" checked={node.on} title="Researched" onChange={() => onToggle(node.id)} />}
      <button className="edit" title="Edit" onClick={() => onEdit(node.id)}>✎</button>
      <div className="ttl" onClick={() => setOpen((o) => !o)} onMouseEnter={() => setOpen(true)} onMouseLeave={() => setOpen(false)}>{node.t}</div>
      <Chips cost={node.cost} />
      {(node.d.length > 0 || (node.stats && node.stats.length) || node.req.length > 0 || node.effects.length > 0 || tags.length > 0 || node.researchTime > 0 || (node.abilities && node.abilities.length > 0)) && (
        <div className="det">
          {node.d.map((line, i) => <div key={i}>{line}</div>)}
          {tags.length > 0 && <div className="cls">{tags.join(" · ")}</div>}
          {node.stats && node.stats.map(([k, v], i) => <div className="st" key={"s" + i}><span className="k">{k}</span><span className="v">{v}</span></div>)}
          {node.effects.map((e, i) => <div className="eff" key={"e" + i}>{targetLabel(e.target)}: {e.stat} {effLabel(e)}</div>)}
          {node.researchTime > 0 && <div className="st"><span className="k">Research time</span><span className="v">{node.researchTime}s</span></div>}
          {node.abilities && node.abilities.length > 0 && <div className="cls">Abilities: {node.abilities.join(", ")}</div>}
          {node.req.length > 0 && <div className="req">Requires: {node.req.join(", ")}</div>}
        </div>
      )}
      {node.arw && <span className="arw">→</span>}
    </div>
  );
}

function Banner({ b, selected, collapsed, onToggleCollapse, onEdit, onSelect }) {
  const [open, setOpen] = useState(false);
  return (
    <div className={"bwrap" + (open ? " open" : "")}>
      <div className={"banner" + (b.mil ? " mil" : "") + (selected ? " sel" : "")}>
        <button className="mini" onClick={onToggleCollapse} title="Collapse / expand section">{collapsed ? "▸" : "▾"}</button>
        <button className="mini" onClick={() => onEdit(b.id)}>✎</button>
        <button className="mini" onClick={() => onSelect(b.id)}>◧ calc</button>
        <span className="btxt" onClick={onToggleCollapse}>{b.banner}</span>
        <button className="mini" onClick={() => setOpen((o) => !o)}>stats ▾</button>
      </div>
      <Chips cost={b.cost} cls="bcost" />
      <div className="stats">{b.stats.map(([k, v], i) => <div className="st" key={i}><span className="k">{k}</span><span className="v">{v}</span></div>)}</div>
    </div>
  );
}

function CostBox({ label, cost, total }) {
  return <div className={"costbox" + (total ? " total" : "")}><div className="lbl">{label}</div><div className="costline">{RES.map(([key, emo, l]) => { const val = (cost && cost[key]) || 0; return <span key={key} className={"chip" + (val ? "" : " zero")} title={l}>{emo} {fmt(val)}</span>; })}</div></div>;
}

function Calc({ data, schema, calc, setCalc, onToggleAll }) {
  const building = data.find((b) => b.id === calc.buildingId) || data[0];
  if (!building) return <div className="card"><h2>Calculator</h2><div className="lbl">Add a building to begin.</div></div>;
  const ents = entitiesOf(building);
  const allEnts = [];
  data.forEach((b) => entitiesOf(b).forEach((e) => allEnts.push({ bId: b.id, key: e.key, isB: e.isB, name: e.name, bname: bName(b) })));
  const previewKey = ents.find((e) => e.key === calc.previewKey) ? calc.previewKey : "building";
  const ent = ents.find((e) => e.key === previewKey) || ents[0];
  const isB = !ent || ent.isB;
  const rows = effectiveStats(data, schema, building, previewKey);
  const qty = Math.max(1, Number(calc.qty) || 1);
  const anyChange = rows.some((r) => r.changed);
  return (
    <div className="card">
      <h2>Calculator</h2>
      <div className="lbl" style={{ marginBottom: "4px" }}>Find any building or unit by name</div>
      <select value={building.id + "|" + previewKey} onChange={(e) => { const idx = e.target.value.indexOf("|"); setCalc({ ...calc, buildingId: e.target.value.slice(0, idx), previewKey: e.target.value.slice(idx + 1) }); }}>
        <optgroup label="Buildings">{allEnts.filter((x) => x.isB).map((x) => <option key={x.bId + x.key} value={x.bId + "|" + x.key}>{x.name}</option>)}</optgroup>
        <optgroup label="Units">{allEnts.filter((x) => !x.isB).map((x) => <option key={x.bId + x.key} value={x.bId + "|" + x.key}>{x.name} — {x.bname}</option>)}</optgroup>
      </select>
      <select value={previewKey} onChange={(e) => setCalc({ ...calc, previewKey: e.target.value })}>{ents.map((e) => <option key={e.key} value={e.key}>{e.isB ? "🏛 " : "› "}{e.name}</option>)}</select>
      <div style={{ display: "flex", gap: "8px", marginBottom: "6px" }}>
        <button className="mini" onClick={() => onToggleAll(building.id, true)}>Research all</button>
        <button className="mini" onClick={() => onToggleAll(building.id, false)}>Clear</button>
      </div>
      <h3>Effective stats {anyChange ? "(with researched)" : ""} · {isB ? "building" : "unit"}</h3>
      {rows.length === 0 && <div className="lbl">No stats defined for this type.</div>}
      {rows.map((r, i) => <div className="crow" key={i}><span className="ck">{r.k}</span><span>{r.changed ? <><s>{r.base}</s> {r.eff}</> : r.eff}</span></div>)}
      <h3>Cost — {isB ? "build a " + bName(building) : "produce " + ent.name}</h3>
      {isB ? (() => {
        const c = buildingCostCalc(data, building); const total = addCost(c.global, scaleCost(c.per, qty));
        return (<>
          <CostBox label="Faction-wide research — paid once" cost={c.global} />
          <CostBox label="Per new building (base + per-building upgrades)" cost={c.per} />
          <div className="qrow"><label>How many buildings?</label><input type="number" min="1" value={qty} onChange={(e) => setCalc({ ...calc, qty: e.target.value })} /></div>
          <CostBox label={"Total for " + qty + " × " + bName(building)} cost={total} total />
        </>);
      })() : (() => {
        const u = unitCostCalc(data, building, ent); const total = addCost(u.research, scaleCost(u.production, qty));
        return (<>
          <CostBox label="Upgrade research affecting this unit — paid once" cost={u.research} />
          <CostBox label="Production cost — per unit" cost={u.production} />
          <div className="qrow"><label>How many units?</label><input type="number" min="1" value={qty} onChange={(e) => setCalc({ ...calc, qty: e.target.value })} /></div>
          <CostBox label={"Total for " + qty + " × " + ent.name + " (upgrades once + production)"} cost={total} total />
        </>);
      })()}
    </div>
  );
}

function SchemaTable({ title, note, rows, onChange }) {
  const set = (i, j, val) => { const r = rows.map((x) => x.slice()); r[i][j] = val; onChange(r); };
  const add = () => onChange([...rows, ["", ""]]);
  const rm = (i) => onChange(rows.filter((_, k) => k !== i));
  return (
    <div className="card">
      <h2>{title}</h2>
      {note && <div className="lbl" style={{ marginBottom: "8px" }}>{note}</div>}
      <div className="schemascroll">
        {rows.map((r, i) => (
          <div className="srow" key={i}>
            <input value={r[0]} placeholder="stat name" onChange={(e) => set(i, 0, e.target.value)} />
            <input className="sty" value={r[1]} placeholder="type" onChange={(e) => set(i, 1, e.target.value)} />
            <button className="mini" onClick={() => rm(i)}>✕</button>
          </div>
        ))}
      </div>
      <button className="mini" onClick={add}>+ stat</button>
    </div>
  );
}

function EnumSel({ label, value, options, onChange }) {
  return <div className="fld"><label>{label}</label><select value={value} onChange={(e) => onChange(e.target.value)}><option value="">—</option>{options.map((o) => <option key={o} value={o}>{o}</option>)}</select></div>;
}

function NodeEditor({ node, building, schema, data, abilities, onSave, onDelete, onClose }) {
  const unitList = building.nodes.filter((n) => n.stats && n.stats.length);
  const techList = Array.from(new Set([].concat(...data.map((b) => b.nodes.filter((n) => n.up).map((n) => n.t))))).filter((t) => t !== node.t).sort();
  const [f, setF] = useState({ ...node, d: node.d.join("\n"), req: (node.req || []).slice(), cost: { ...node.cost }, stats: (node.stats || []).map((s) => s.slice()), effects: node.effects.map((e) => { const pct = e.op.indexOf("%") > -1; const v = e.op[0] === "-" ? -e.value : e.value; return { target: e.target, stat: e.stat, op: pct ? "+%" : "+", value: v }; }) });
  const reqAdd = (val) => setF((prev) => prev.req.indexOf(val) > -1 ? prev : ({ ...prev, req: [...prev.req, val] }));
  const reqRemove = (i) => setF((prev) => ({ ...prev, req: prev.req.filter((_, k) => k !== i) }));
  const abAdd = (val) => setF((prev) => (prev.abilities || []).indexOf(val) > -1 ? prev : ({ ...prev, abilities: [...(prev.abilities || []), val] }));
  const abRemove = (i) => setF((prev) => ({ ...prev, abilities: (prev.abilities || []).filter((_, k) => k !== i) }));
  const set = (p) => setF((prev) => ({ ...prev, ...p }));
  const setCost = (k, val) => setF((prev) => ({ ...prev, cost: { ...prev.cost, [k]: val } }));
  const setStat = (i, j, val) => setF((prev) => { const st = prev.stats.map((r) => r.slice()); st[i][j] = val; return { ...prev, stats: st }; });
  const addStat = () => setF((prev) => ({ ...prev, stats: [...prev.stats, ["", ""]] }));
  const rmStat = (i) => setF((prev) => ({ ...prev, stats: prev.stats.filter((_, k) => k !== i) }));
  const setEff = (i, key, val) => setF((prev) => { const ef = prev.effects.map((e) => ({ ...e })); ef[i][key] = val; return { ...prev, effects: ef }; });
  const setEffTarget = (i, val) => setF((prev) => { const ef = prev.effects.map((e) => ({ ...e })); ef[i].target = val; const opts = statOptions(schema, val); if (opts.indexOf(ef[i].stat) < 0) ef[i].stat = opts[0] || ""; return { ...prev, effects: ef }; });
  const addEff = () => setF((prev) => ({ ...prev, effects: [...prev.effects, { target: "building", stat: statOptions(schema, "building")[0] || "", op: "+", value: 0 }] }));
  const rmEff = (i) => setF((prev) => ({ ...prev, effects: prev.effects.filter((_, k) => k !== i) }));
  const save = () => onSave({
    ...node, t: f.t, k: f.k, o: f.o, a: Number(f.a), r: Number(f.r), arw: !!f.arw, req: f.req,
    type: f.type, race: f.race, size: f.size,
    d: f.d.split("\n").map((s) => s.trim()).filter(Boolean),
    stats: f.stats.filter((r) => r[0] || r[1]),
    up: !!f.up, scope: f.scope, on: !!f.on, researchTime: Number(f.researchTime) || 0, culture: f.culture || "", abilities: (f.abilities || []).slice(),
    effects: f.effects.filter((e) => e.stat).map((e) => ({ target: e.target, stat: e.stat, op: e.op, value: Number(e.value) || 0 })),
    cost: { s: Number(f.cost.s) || 0, i: Number(f.cost.i) || 0, v: Number(f.cost.v) || 0, vs: Number(f.cost.vs) || 0 },
  });
  useEffect(() => { save(); }, [f]);
  return (
    <div className="overlay">
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h2>Edit node</h2>
        <div className="fld"><label>Name</label><input value={f.t} onChange={(e) => set({ t: e.target.value })} /></div>
        <div className="row">
          <div className="fld"><label>Type</label><select value={f.k} onChange={(e) => set({ k: e.target.value })}>{KINDS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
          <div className="fld"><label>Branch outline</label><select value={f.o} onChange={(e) => set({ o: e.target.value })}>{OUTLINES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
        </div>
        <div className="row">
          <div className="fld"><label>Age</label><select value={f.a} onChange={(e) => set({ a: e.target.value })}>{AGES.map((l, i) => <option key={i} value={i}>{l}</option>)}</select></div>
          <div className="fld"><label>Row</label><input type="number" min="1" value={f.r} onChange={(e) => set({ r: e.target.value })} /></div>
          <div className="fld"><label>Arrow →</label><select value={f.arw ? "1" : ""} onChange={(e) => set({ arw: !!e.target.value })}><option value="">No</option><option value="1">Yes</option></select></div>
        </div>
        <div className="row">
          <EnumSel label="Unit type" value={f.type} options={UNIT_TYPES} onChange={(v) => set({ type: v })} />
          <EnumSel label="Unit race" value={f.race} options={UNIT_RACES} onChange={(v) => set({ race: v })} />
          <EnumSel label="Unit size" value={f.size} options={UNIT_SIZES} onChange={(v) => set({ size: v })} />
        </div>
        <div className="row">
          <div className="fld"><label>Toggleable research</label><select value={f.up ? "1" : ""} onChange={(e) => set({ up: !!e.target.value })}><option value="">No</option><option value="1">Yes</option></select></div>
          <div className="fld"><label>Cost scope</label><select value={f.scope} onChange={(e) => set({ scope: e.target.value })}><option value="global">Faction-wide (once)</option><option value="per">Per building</option></select></div>
          <div className="fld"><label>Research time (s)</label><input type="number" min="0" value={f.researchTime || 0} onChange={(e) => set({ researchTime: e.target.value })} /></div>
          <div className="fld"><label>Start researched</label><select value={f.on ? "1" : ""} onChange={(e) => set({ on: !!e.target.value })}><option value="">No</option><option value="1">Yes</option></select></div>
        </div>
        <div className="fld"><label>Culture (blank = cultureless / Age 0 — shows under every culture)</label><select value={f.culture || ""} onChange={(e) => set({ culture: e.target.value })}><option value="">— cultureless / all —</option>{CULTURES.map((c) => <option key={c} value={c}>{c}</option>)}</select></div>
        <div className="fld"><label>Effects (applied when researched)</label>
          {f.effects.map((e, i) => {
            const opts = statOptions(schema, e.target);
            const statList = e.stat && opts.indexOf(e.stat) < 0 ? [e.stat, ...opts] : opts;
            return (
              <div className="effrow" key={i}>
                <select className="e-t" value={e.target} onChange={(ev) => setEffTarget(i, ev.target.value)}>
                  <option value="building">This building</option>
                  <optgroup label="Specific building">{data.map((b) => <option key={b.id} value={"bname:" + bName(b)}>{bName(b)}</option>)}</optgroup>
                  <optgroup label="All buildings — type">{BUILDING_TYPES.map((t) => <option key={t} value={"btype:" + t}>All {t}</option>)}</optgroup>
                  <optgroup label="Specific unit">{unitList.map((n) => <option key={n.id} value={"unit:" + n.t}>{n.t}</option>)}</optgroup>
                  <optgroup label="All units — type">{UNIT_TYPES.map((t) => <option key={t} value={"type:" + t}>All {t}</option>)}</optgroup>
                  <optgroup label="All units — race">{UNIT_RACES.map((t) => <option key={t} value={"race:" + t}>All {t}</option>)}</optgroup>
                  <optgroup label="All units — size">{UNIT_SIZES.map((t) => <option key={t} value={"size:" + t}>All {t}</option>)}</optgroup>
                </select>
                <select className="e-s" value={e.stat} onChange={(ev) => setEff(i, "stat", ev.target.value)}>
                  <option value="">— stat —</option>{statList.map((s) => <option key={s} value={s}>{s}</option>)}
                </select>
                <select className="e-o" value={e.op} onChange={(ev) => setEff(i, "op", ev.target.value)}>{MODS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select>
                <input className="e-v" type="number" value={e.value} onChange={(ev) => setEff(i, "value", ev.target.value)} />
                <button className="mini" onClick={() => rmEff(i)}>✕</button>
              </div>
            );
          })}
          <button className="mini" onClick={addEff}>+ effect</button>
          <div className="lbl" style={{ marginTop: "4px" }}>Stat options come from the stat tables → building stats for “This building”, unit stats for unit/type/race/size targets.</div>
        </div>
        <div className="fld"><label>Details (one line each)</label><textarea value={f.d} onChange={(e) => set({ d: e.target.value })} /></div>
        <div className="fld"><label>Requirements (techs / ages)</label>
          <div style={{ marginBottom: "6px" }}>
            {f.req.map((r, i) => <span className="reqchip" key={i}>{r}<button onClick={() => reqRemove(i)}>✕</button></span>)}
            {f.req.length === 0 && <span className="lbl">None</span>}
          </div>
          <select value="" onChange={(e) => { if (e.target.value) reqAdd(e.target.value); }}>
            <option value="">+ add requirement…</option>
            <optgroup label="Ages">{AGES.map((a) => <option key={a} value={a}>{a}</option>)}</optgroup>
            <optgroup label="Techs">{techList.map((t) => <option key={t} value={t}>{t}</option>)}</optgroup>
          </select>
        </div>
        <div className="fld"><label>Abilities (assigned cards)</label>
          <div style={{ marginBottom: "6px" }}>
            {(f.abilities || []).map((a, i) => <span className="reqchip" key={i}>{a}<button onClick={() => abRemove(i)}>✕</button></span>)}
            {(!f.abilities || f.abilities.length === 0) && <span className="lbl">None</span>}
          </div>
          <select value="" onChange={(e) => { if (e.target.value) abAdd(e.target.value); }}>
            <option value="">+ assign ability…</option>
            {(abilities || []).map((a) => <option key={a.id} value={a.name}>{a.name}</option>)}
          </select>
        </div>
        <div className="fld"><label>Stat block</label>
          {f.stats.map((r, i) => <div className="statrow" key={i}><input value={r[0]} placeholder="label" onChange={(e) => setStat(i, 0, e.target.value)} /><input value={r[1]} placeholder="value" onChange={(e) => setStat(i, 1, e.target.value)} /><button className="mini" onClick={() => rmStat(i)}>✕</button></div>)}
          <button className="mini" onClick={addStat}>+ stat row</button>
        </div>
        <div className="fld"><label>Cost</label><div className="costgrid">{RES.map(([key, emo, label]) => <label key={key}>{emo} {label}<input type="number" min="0" value={f.cost[key]} onChange={(e) => setCost(key, e.target.value)} /></label>)}</div></div>
        <div className="mact"><button className="dng" onClick={() => onDelete(node.id)}>Delete</button><button className="pri" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}

function BuildingEditor({ b, onSave, onDelete, onClose }) {
  const [f, setF] = useState({ banner: b.banner, mil: b.mil, cost: { ...b.cost }, stats: b.stats.map((s) => [s[0], s[1]]) });
  const set = (p) => setF((prev) => ({ ...prev, ...p }));
  const setCost = (k, val) => setF((prev) => ({ ...prev, cost: { ...prev.cost, [k]: val } }));
  const setStat = (i, j, val) => setF((prev) => { const st = prev.stats.map((r) => r.slice()); st[i][j] = val; return { ...prev, stats: st }; });
  const addStat = () => setF((prev) => ({ ...prev, stats: [...prev.stats, ["", ""]] }));
  const rmStat = (i) => setF((prev) => ({ ...prev, stats: prev.stats.filter((_, k) => k !== i) }));
  const save = () => onSave({ ...b, banner: f.banner, mil: !!f.mil, stats: f.stats.filter((r) => r[0] || r[1]), cost: { s: Number(f.cost.s) || 0, i: Number(f.cost.i) || 0, v: Number(f.cost.v) || 0, vs: Number(f.cost.vs) || 0 } });
  useEffect(() => { save(); }, [f]);
  return (
    <div className="overlay">
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h2>Edit building</h2>
        <div className="fld"><label>Banner text</label><input value={f.banner} onChange={(e) => set({ banner: e.target.value })} /></div>
        <div className="fld"><label>Building branch</label><select value={f.mil ? "1" : ""} onChange={(e) => set({ mil: !!e.target.value })}><option value="">Economy (green outline)</option><option value="1">Military (red outline)</option></select></div>
        <div className="fld"><label>Build cost</label><div className="costgrid">{RES.map(([key, emo, label]) => <label key={key}>{emo} {label}<input type="number" min="0" value={f.cost[key]} onChange={(e) => setCost(key, e.target.value)} /></label>)}</div></div>
        <div className="fld"><label>Stats</label>
          {f.stats.map((r, i) => <div className="statrow" key={i}><input value={r[0]} placeholder="label" onChange={(e) => setStat(i, 0, e.target.value)} /><input value={r[1]} placeholder="value" onChange={(e) => setStat(i, 1, e.target.value)} /><button className="mini" onClick={() => rmStat(i)}>✕</button></div>)}
          <button className="mini" onClick={addStat}>+ stat row</button>
        </div>
        <div className="mact"><button className="dng" onClick={() => onDelete(b.id)}>Delete building</button><button className="pri" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}

function AbilityEditor({ ability, all, onSave, onDelete, onClose }) {
  const [f, setF] = useState({ ...ability, effects: (ability.effects || []).slice(), aftermath: (ability.aftermath || []).slice() });
  const set = (p) => setF((prev) => ({ ...prev, ...p }));
  const save = () => onSave({
    ...ability, name: f.name, activation: f.activation, type: f.type,
    castingTime: Number(f.castingTime) || 0,
    castingDuration: Number.isFinite(Number(f.castingDuration)) ? Number(f.castingDuration) : -1,
    radius: Number(f.radius) || 0, range: Number(f.range) || 0,
    effects: f.effects.filter((e) => e.trim()), aftermath: f.aftermath.slice(),
  });
  useEffect(() => { save(); }, [f]);
  const setEff = (i, val) => setF((prev) => { const e = prev.effects.slice(); e[i] = val; return { ...prev, effects: e }; });
  const addEff = () => setF((prev) => ({ ...prev, effects: [...prev.effects, ""] }));
  const rmEff = (i) => setF((prev) => ({ ...prev, effects: prev.effects.filter((_, k) => k !== i) }));
  const aftAdd = (val) => setF((prev) => prev.aftermath.indexOf(val) > -1 ? prev : ({ ...prev, aftermath: [...prev.aftermath, val] }));
  const aftRm = (i) => setF((prev) => ({ ...prev, aftermath: prev.aftermath.filter((_, k) => k !== i) }));
  const others = all.filter((a) => a.id !== ability.id);
  return (
    <div className="overlay">
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h2>Edit ability card</h2>
        <div className="fld"><label>Name</label><input value={f.name} onChange={(e) => set({ name: e.target.value })} /></div>
        <div className="row">
          <div className="fld"><label>Activation</label><select value={f.activation} onChange={(e) => set({ activation: e.target.value })}>{ABILITY_ACTIVATIONS.map((a) => <option key={a} value={a}>{a}</option>)}</select></div>
          <div className="fld"><label>Type</label><select value={f.type} onChange={(e) => set({ type: e.target.value })}>{ABILITY_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}</select></div>
        </div>
        <div className="row">
          <div className="fld"><label>Casting time (s)</label><input type="number" value={f.castingTime} onChange={(e) => set({ castingTime: e.target.value })} /></div>
          <div className="fld"><label>Casting duration (s, -1 = passive/always-on)</label><input type="number" value={f.castingDuration} onChange={(e) => set({ castingDuration: e.target.value })} /></div>
        </div>
        <div className="row">
          <div className="fld"><label>Radius (units)</label><input type="number" value={f.radius} onChange={(e) => set({ radius: e.target.value })} /></div>
          <div className="fld"><label>Range (units)</label><input type="number" value={f.range} onChange={(e) => set({ range: e.target.value })} /></div>
        </div>
        <div className="fld"><label>Effects (one per line)</label>
          {f.effects.map((e, i) => <div className="srow" key={i}><input value={e} placeholder="effect line" onChange={(ev) => setEff(i, ev.target.value)} /><button className="mini" onClick={() => rmEff(i)}>✕</button></div>)}
          <button className="mini" onClick={addEff}>+ effect</button>
        </div>
        <div className="fld"><label>Aftermath (abilities cast afterward)</label>
          <div style={{ marginBottom: "6px" }}>
            {f.aftermath.map((a, i) => <span className="reqchip" key={i}>{a}<button onClick={() => aftRm(i)}>✕</button></span>)}
            {f.aftermath.length === 0 && <span className="lbl">None</span>}
          </div>
          <select value="" onChange={(e) => { if (e.target.value) aftAdd(e.target.value); }}>
            <option value="">+ add aftermath ability…</option>
            {others.map((a) => <option key={a.id} value={a.name}>{a.name}</option>)}
          </select>
        </div>
        <div className="mact"><button className="dng" onClick={() => onDelete(ability.id)}>Delete</button><button className="pri" onClick={onClose}>Close</button></div>
      </div>
    </div>
  );
}
function AbilitiesPanel({ abilities, onEdit, onAdd }) {
  return (
    <div className="card">
      <h2>Ability cards</h2>
      <div className="lbl" style={{ marginBottom: "8px" }}>Create cards, then assign them to units in the node editor (Abilities field).</div>
      {abilities.length === 0 && <div className="lbl">No abilities yet.</div>}
      {abilities.map((a) => (
        <div className="crow" key={a.id}>
          <span className="ck">{a.name}</span>
          <span><span className="lbl">{a.activation} · {a.type} · {a.castingDuration < 0 ? "passive" : a.castingDuration + "s"}</span> <button className="mini" onClick={() => onEdit(a.id)}>✎</button></span>
        </div>
      ))}
      <button className="mini" onClick={onAdd} style={{ marginTop: "8px" }}>+ Ability</button>
    </div>
  );
}
export default function TechTree() {
  const init = load();
  const [data, setData] = useState(init.buildings);
  const [abilities, setAbilities] = useState(init.abilities);
  const [schema, setSchema] = useState(init.schema);
  const [edit, setEdit] = useState(null);
  const [calc, setCalc] = useState({ buildingId: null, previewKey: "building", qty: 1 });
  const [tab, setTab] = useState("Alanthor");
  const [collapsed, setCollapsed] = useState({});
  const [drag, setDrag] = useState(null);

  const findB = (id) => data.find((b) => b.id === id);
  const nodeUnder = (bId, nId) => findB(bId).nodes.find((n) => n.id === nId);
  const saveNode = (bId, nn) => setData((d) => d.map((b) => (b.id !== bId ? b : { ...b, nodes: b.nodes.map((n) => (n.id === nn.id ? nn : n)) })));
  const deleteNode = (bId, nId) => setData((d) => d.map((b) => (b.id !== bId ? b : { ...b, nodes: b.nodes.filter((n) => n.id !== nId) })));
  const toggleNode = (bId, nId) => setData((d) => d.map((b) => (b.id !== bId ? b : { ...b, nodes: b.nodes.map((n) => (n.id === nId ? { ...n, on: !n.on } : n)) })));
  const toggleAll = (bId, on) => setData((d) => d.map((b) => (b.id !== bId ? b : { ...b, nodes: b.nodes.map((n) => (n.up ? { ...n, on } : n)) })));
  const moveNode = (bId, nId, a, r, culture) => setData((d) => d.map((b) => {
    if (b.id !== bId) return b;
    const src = b.nodes.find((n) => n.id === nId);
    if (!src || (src.a === a && src.r === r)) return b;
    const occ = b.nodes.find((n) => n.id !== nId && n.a === a && n.r === r && (!n.culture || n.culture === culture));
    return { ...b, nodes: b.nodes.map((n) => {
      if (n.id === nId) return { ...n, a, r };
      if (occ && n.id === occ.id) return { ...n, a: src.a, r: src.r };
      return n;
    }) };
  }));
  const addNode = (bId) => {
    const b = findB(bId); const r = b.nodes.reduce((m, n) => Math.max(m, n.r), 0) + 1;
    const node = { id: uid(), a: 0, r, t: "New node", k: "tech", o: "", arw: false, type: "", race: "", size: "", d: [], req: [], stats: [], cost: emptyCost(), up: true, researchTime: 0, culture: "", scope: "global", on: false, effects: [], abilities: [] };
    setData((d) => d.map((x) => (x.id !== bId ? x : { ...x, nodes: [...x.nodes, node] })));
    setEdit({ type: "node", bId, nId: node.id });
  };
  const saveBuilding = (nb) => setData((d) => d.map((b) => (b.id === nb.id ? nb : b)));
  const deleteBuilding = (bId) => { setData((d) => d.filter((b) => b.id !== bId)); setEdit(null); };
  const addBuilding = () => {
    const b = { id: uid(), banner: "New building — description", mil: false, cost: emptyCost(), stats: [["id", "NewBuilding"], ["Building Type", tab === "Other" ? "" : tab], ["HP", "100"]], nodes: [{ id: uid(), a: 0, r: 1, t: "Lv 1", k: "lvl", o: "eco", arw: false, type: "", race: "", size: "", d: [], req: [], stats: [], cost: emptyCost(), up: false, scope: "per", on: false, effects: [] }] };
    setData((d) => [...d, b]); setEdit({ type: "building", bId: b.id });
  };
  const saveAbility = (na) => setAbilities((a) => a.map((x) => x.id === na.id ? na : x));
  const deleteAbility = (id) => { setAbilities((a) => a.filter((x) => x.id !== id)); setEdit(null); };
  const addAbility = () => { const na = { id: uid(), name: "New ability", activation: "Active", type: "self", castingTime: 0, castingDuration: -1, radius: 0, range: 0, effects: [], aftermath: [] }; setAbilities((a) => [...a, na]); setEdit({ type: "ability", abId: na.id }); };
  const exportJSON = () => { const blob = new Blob([JSON.stringify({ schema, buildings: data, abilities }, null, 2)], { type: "application/json" }); const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = "techtree.json"; a.click(); URL.revokeObjectURL(a.href); };
  const importJSON = (e) => { const file = e.target.files[0]; if (!file) return; const rd = new FileReader(); rd.onload = () => { try { const o = JSON.parse(rd.result); const arr = Array.isArray(o) ? o : o.buildings; setData(normalize(arr || [])); if (o.schema) setSchema(normSchema(o.schema)); if (o.abilities) setAbilities(normAbilities(o.abilities)); } catch (err) { alert("Invalid JSON"); } }; rd.readAsText(file); e.target.value = ""; };
  const reset = () => { if (window.confirm("Discard your scratch edits and reload the SO data?")) { setData(normalize(DEFAULT)); setSchema(normSchema(DEFAULT_SCHEMA)); setAbilities(normAbilities(DEFAULT_ABILITIES)); } };
  const calcBId = findB(calc.buildingId) ? calc.buildingId : (data[0] && data[0].id);

  const CULTURE_ORDER = CULTURES;
  const CULTURE_LABEL = { Runaii: "Runai" };
  const typeOf = (b) => bTypeOf(b) || "Other";
  const typeCounts = {}; data.forEach((b) => { const t = typeOf(b); typeCounts[t] = (typeCounts[t] || 0) + 1; });
  const cultures = [...CULTURE_ORDER.filter((t) => typeCounts[t]), ...Object.keys(typeCounts).filter((t) => t !== "Basic" && !CULTURE_ORDER.includes(t)).sort()];
  const activeCulture = cultures.includes(tab) ? tab : (cultures[0] || "");
  const shown = data.filter((b) => typeOf(b) === "Basic" || typeOf(b) === activeCulture);
  const noAges = false;
  const allCollapsed = shown.length > 0 && shown.every((b) => collapsed[b.id]);

  return (
    <div className="ttc">
      <style>{css}</style>
      <div className="bar">
        <h1>Tech Tree (generated from SOs - read-only)</h1>
        <button className="pri" onClick={addBuilding}>+ Building</button>
        <button onClick={exportJSON}>Export JSON</button>
        <button onClick={(e) => e.currentTarget.nextSibling.click()}>Import JSON</button>
        <input type="file" accept="application/json" style={{ display: "none" }} onChange={importJSON} />
        <button onClick={() => { const next = {}; shown.forEach((b) => (next[b.id] = !allCollapsed)); setCollapsed((c) => ({ ...c, ...next })); }}>{allCollapsed ? "⊞ Expand all" : "⊟ Collapse all"}</button>
        <button className="dng" onClick={reset}>Reset to SO data</button>
      </div>
        <div className="gennote">Generated from the game's ScriptableObject assets by <code>python tools/gen_calculator.py</code> (Age 0 + Alanthor). Read-only view: research toggles, edits and imports are local scratch only - never saved, gone on reload. To change the game, edit the SO in Unity and re-run the generator.</div>
      <div className="wrap">
        <div className="tree tt">
          <div className="legend">
            <span><i style={{ background: "#4c6a92" }} />Base level</span>
            <span><i style={{ background: "#2f7d34" }} />Economy</span>
            <span><i style={{ background: "#2d5aa0" }} />Technology</span>
            <span><i style={{ background: "#a13c3c" }} />Military unit</span>
            <span><i style={{ background: "#4a3c18", border: "1px solid #6bbf59" }} />Economy building</span>
            <span><i style={{ background: "#4a3c18", border: "1px solid #d15a5a" }} />Military building</span>
            <span>☑ = researched</span>
            <span style={{ marginLeft: "auto" }}>🌾 supplies · ⛏️ iron · 💎 veilstone · 🔷 veilsteel</span>
          </div>
          <div className="tabs"><span style={{ fontSize: "12px", color: "#8f97a3", alignSelf: "center", marginRight: "4px" }}>Base ({typeCounts.Basic || 0}) always shown · Culture:</span>{cultures.map((t) => <button key={t} className={"tab" + (t === activeCulture ? " on" : "")} onClick={() => setTab(t)}>{(CULTURE_LABEL[t] || t)} · {typeCounts[t]}</button>)}</div>
          {!noAges && <div className="hdr">{AGES.map((a) => <div key={a}>{a}</div>)}</div>}
          <div className="body">
            {!noAges && <React.Fragment><span className="vline" style={{ left: "25%" }} /><span className="vline" style={{ left: "50%" }} /><span className="vline" style={{ left: "75%" }} /></React.Fragment>}
            {shown.map((b) => (
              <React.Fragment key={b.id}>
                <Banner b={b} selected={b.id === calcBId} collapsed={!!collapsed[b.id]} onToggleCollapse={() => setCollapsed((c) => ({ ...c, [b.id]: !c[b.id] }))} onEdit={(bId) => setEdit({ type: "building", bId })} onSelect={(bId) => setCalc({ ...calc, buildingId: bId, previewKey: "building" })} />
                {!collapsed[b.id] && <React.Fragment>
                <div className={"grid" + (noAges ? " noages" : "")}>
                {b.nodes.filter((n) => !n.culture || n.culture === activeCulture).map((n) => <Node key={n.id} node={n} noAges={noAges} dragging={!!drag && drag.nId === n.id} onDragStart={(nId) => setDrag({ bId: b.id, nId })} onDragEnd={() => setDrag(null)} onEdit={(nId) => setEdit({ type: "node", bId: b.id, nId })} onToggle={(nId) => toggleNode(b.id, nId)} />)}
                {drag && drag.bId === b.id && (() => {
                  const vis = b.nodes.filter((n) => !n.culture || n.culture === activeCulture);
                  const dragged = b.nodes.find((n) => n.id === drag.nId);
                  const maxR = vis.reduce((m, n) => Math.max(m, n.r), 0) + 1;
                  const cols = noAges ? [null] : [0, 1, 2, 3];
                  const cells = [];
                  for (let r = 1; r <= maxR; r++) cols.forEach((a) => {
                    if (dragged && dragged.r === r && (a === null || dragged.a === a)) return;
                    cells.push(<div key={a + "-" + r} className="dropcell" title={(a === null ? "" : AGES[a] + " · ") + "row " + r}
                      style={a === null ? { gridRow: r } : { gridColumn: a + 1, gridRow: r }}
                      onDragOver={(e) => { e.preventDefault(); e.dataTransfer.dropEffect = "move"; }}
                      onDragEnter={(e) => e.currentTarget.classList.add("over")}
                      onDragLeave={(e) => e.currentTarget.classList.remove("over")}
                      onDrop={(e) => { e.preventDefault(); moveNode(b.id, drag.nId, a === null ? (dragged ? dragged.a : 0) : a, r, activeCulture); setDrag(null); }} />);
                  });
                  return cells;
                })()}
                </div>
                <button className="addnode" onClick={() => addNode(b.id)}>+ Add node to this building</button>
                </React.Fragment>}
              </React.Fragment>
            ))}
          </div>
          <div className="hint">☑ toggles research · research can target one unit or all units of a type/race/size · ✎ edits everything · drag a card onto a dashed cell to move it to another age/row (drop on an occupied spot to swap) · edits are scratch only (not saved; the game reads the SOs)</div>
        </div>
        <div className="side">
          <Calc data={data} schema={schema} calc={{ ...calc, buildingId: calcBId }} setCalc={setCalc} onToggleAll={toggleAll} />
          <SchemaTable title="Building stats" note="Feeds the effect stat dropdown for building targets." rows={schema.buildingStats} onChange={(rows) => setSchema((s) => ({ ...s, buildingStats: rows }))} />
          <SchemaTable title="Unit stats" note="Feeds the effect stat dropdown for unit / type / race / size targets." rows={schema.unitStats} onChange={(rows) => setSchema((s) => ({ ...s, unitStats: rows }))} />
          <AbilitiesPanel abilities={abilities} onEdit={(id) => setEdit({ type: "ability", abId: id })} onAdd={addAbility} />
        </div>
      </div>
      {edit && edit.type === "node" && <NodeEditor node={nodeUnder(edit.bId, edit.nId)} building={findB(edit.bId)} schema={schema} data={data} abilities={abilities} onSave={(nn) => saveNode(edit.bId, nn)} onDelete={(nId) => { deleteNode(edit.bId, nId); setEdit(null); }} onClose={() => setEdit(null)} />}
      {edit && edit.type === "building" && <BuildingEditor b={findB(edit.bId)} onSave={(nb) => saveBuilding(nb)} onDelete={deleteBuilding} onClose={() => setEdit(null)} />}
      {edit && edit.type === "ability" && <AbilityEditor ability={abilities.find((a) => a.id === edit.abId)} all={abilities} onSave={saveAbility} onDelete={deleteAbility} onClose={() => setEdit(null)} />}
    </div>
  );
}

const css = `
.ttc{--panel:#0d0e12;--ink:#e9ecf5;--muted:#8f97a3;--line:#262a31;color:var(--ink);font-family:ui-sans-serif,system-ui,"Segoe UI",Roboto,sans-serif}
.ttc *{box-sizing:border-box}
.ttc .bar{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-bottom:14px}
.ttc .bar h1{font-size:18px;font-weight:500;margin:0 auto 0 0}
.ttc button{font:inherit;color:var(--ink);background:#20232c;border:1px solid #333846;border-radius:7px;padding:6px 11px;cursor:pointer}
.ttc button:hover{background:#2a2e3a}
.ttc button.pri{background:#2d5aa0;border-color:#3f6fb8}
.ttc button.dng{background:#7a2b2b;border-color:#9e3a3a}
.ttc button.mini{padding:2px 7px;font-size:12px;border-radius:6px}
.ttc .wrap{display:flex;gap:16px;align-items:flex-start}
.ttc .tree{flex:1;min-width:0}
.ttc .side{width:322px;flex-shrink:0}
.ttc .tt{background:var(--panel);border-radius:12px;padding:14px}
.ttc .legend{display:flex;flex-wrap:wrap;gap:8px 14px;font-size:11.5px;color:#c7ccd6;margin-bottom:12px;align-items:center}
.ttc .legend i{display:inline-block;width:12px;height:12px;border-radius:3px;margin-right:5px;vertical-align:-1px}
.ttc .tabs{display:flex;flex-wrap:wrap;gap:6px;margin-bottom:12px}
.ttc .tab{font:inherit;font-size:12.5px;padding:5px 12px;border-radius:7px;background:#20232c;border:1px solid #333846;color:#c7ccd6;cursor:pointer}
.ttc .tab:hover{background:#2a2e3a}
.ttc .tab.on{background:#2d5aa0;border-color:#3f6fb8;color:#fff}
.ttc .grid.noages{row-gap:16px}
.ttc .hdr{display:grid;grid-template-columns:repeat(4,1fr);margin-bottom:6px}
.ttc .hdr div{color:#c7ccd6;text-align:center;font-size:13px;padding:8px 0;border:1px solid var(--line);border-left:none}
.ttc .hdr div:first-child{border-left:1px solid var(--line)}
.ttc .body{position:relative}
.ttc .vline{position:absolute;top:0;bottom:0;width:1px;background:#1c1f26;z-index:0}
.ttc .bwrap{position:relative;z-index:1}
.ttc .banner{display:flex;align-items:center;gap:8px;background:#4a3c18;border:2px solid #6bbf59;color:#f0ecd8;font-size:13px;padding:6px 10px;border-radius:5px;margin:14px 0 6px}
.ttc .banner.mil{border-color:#d15a5a}
.ttc .banner.sel{outline:2px solid #e2b53f}
.ttc .banner .btxt{flex:1;text-align:center;cursor:pointer}
.ttc .bcost{display:flex;flex-wrap:wrap;gap:8px;justify-content:center;margin-bottom:6px}
.ttc .stats{max-height:0;opacity:0;overflow:hidden;background:#05060d;border:0 solid #34517d;border-radius:5px;display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:0 18px;transition:max-height .2s,opacity .2s,padding .2s;margin-bottom:6px}
.ttc .bwrap.open .stats{max-height:760px;opacity:1;padding:8px 12px;border-width:1px}
.ttc .st{display:flex;justify-content:space-between;gap:10px;font-size:11.5px;padding:3px 0;border-bottom:1px solid #161822}
.ttc .st .k{color:var(--muted)}
.ttc .st .v{color:var(--ink);text-align:right;word-break:break-word}
.ttc .grid{position:relative;z-index:1;display:grid;grid-template-columns:repeat(4,1fr);column-gap:0;row-gap:14px;align-items:start;padding-bottom:6px}
.ttc .node{position:relative;align-self:start;margin:0 15px}
.ttc .node.dragging{opacity:.3}
.ttc .node .ttl{cursor:grab}
.ttc .dropcell{position:relative;z-index:6;align-self:stretch;min-height:40px;margin:0 15px;border:1px dashed #3a4150;border-radius:6px;opacity:.45}
.ttc .dropcell.over{background:rgba(45,90,160,.4);border-color:#6fa3e8;opacity:1}
.ttc .node.off{opacity:.5}
.ttc .chk{position:absolute;top:-7px;left:-7px;z-index:4;width:15px;height:15px;accent-color:#6bbf59;cursor:pointer}
.ttc .edit{position:absolute;top:-8px;right:-8px;z-index:4;width:20px;height:20px;line-height:1;padding:0;border-radius:50%;font-size:11px;background:#20232c;border:1px solid #3a4150;opacity:0;transition:opacity .12s}
.ttc .node:hover .edit{opacity:1}
.ttc .ttl{border-radius:5px 5px 0 0;text-align:center;color:#fff;font-size:12px;font-weight:500;padding:6px;border:1px solid rgba(255,255,255,.22);border-bottom:none;cursor:pointer}
.ttc .node.on .ttl{box-shadow:0 0 0 2px #6bbf59}
.ttc .k-lvl .ttl{background:#4c6a92}.ttc .k-eco .ttl{background:#2f7d34}.ttc .k-tech .ttl{background:#2d5aa0}.ttc .k-mil .ttl{background:#a13c3c}
.ttc .o-mil .ttl{border:2px solid #d15a5a;border-bottom:none}.ttc .o-eco .ttl{border:2px solid #6bbf59;border-bottom:none}
.ttc .cost{display:flex;flex-wrap:wrap;gap:6px;justify-content:center;background:#0a0c12;border:1px solid rgba(255,255,255,.14);border-top:none;border-radius:0 0 5px 5px;padding:4px 5px;font-size:11px}
.ttc .chip{white-space:nowrap}.ttc .chip.zero{opacity:.3}
.ttc .det{max-height:0;opacity:0;overflow:hidden;background:#05060d;border:0 solid #34517d;border-radius:5px;color:var(--ink);font-size:11.5px;line-height:1.4;text-align:center;transition:max-height .18s,opacity .18s,padding .18s}
.ttc .node:hover .det,.ttc .node.open .det{max-height:660px;opacity:1;padding:7px 8px;border-width:1px;margin-top:5px}
.ttc .det .st .k{color:var(--muted);text-align:left;white-space:normal}
.ttc .det .req{color:#f0c674;margin-top:4px}.ttc .det .eff{color:#7fd08a;margin-top:4px;font-size:11px}.ttc .det .cls{color:#c9a0e0;margin-top:4px;font-size:11px}
.ttc .arw{position:absolute;right:-19px;top:8px;color:#9aa0aa;font-size:18px;z-index:3}
.ttc .addnode{margin:2px 15px 0;font-size:12px;color:var(--muted);background:transparent;border:1px dashed #3a4150}
.ttc .hint{color:var(--muted);font-size:11.5px;text-align:center;margin-top:14px}
.ttc .card{background:var(--panel);border-radius:12px;padding:14px;margin-bottom:12px}
.ttc .card h2{margin:0 0 10px;font-size:15px;font-weight:500}
.ttc .card h3{margin:12px 0 6px;font-size:12px;font-weight:500;color:var(--muted);text-transform:uppercase;letter-spacing:.4px}
.ttc .card>select,.ttc .card>input{width:100%;font:inherit;color:var(--ink);background:#0e1015;border:1px solid #2c313c;border-radius:7px;padding:6px 8px;margin-bottom:8px}
.ttc .crow{display:flex;justify-content:space-between;gap:8px;font-size:12px;padding:3px 0;border-bottom:1px solid #171922}
.ttc .crow .ck{color:var(--muted)}.ttc .crow s{color:#c97b7b}
.ttc .lbl{font-size:11px;color:var(--muted)}
.ttc .costline{display:flex;flex-wrap:wrap;gap:10px;font-size:13px;margin:4px 0}
.ttc .costbox{background:#12141b;border:1px solid #262a31;border-radius:8px;padding:8px 10px;margin-bottom:8px}
.ttc .costbox .lbl{margin-bottom:3px}.ttc .costbox.total{border-color:#3f6fb8}
.ttc .qrow{display:flex;gap:8px;align-items:center;margin-bottom:8px}
.ttc .qrow label{font-size:12px;color:var(--muted);white-space:nowrap}.ttc .qrow input{width:70px;margin:0;font:inherit;color:var(--ink);background:#0e1015;border:1px solid #2c313c;border-radius:7px;padding:6px 8px}
.ttc .schemascroll{max-height:250px;overflow:auto;margin-bottom:6px;padding-right:2px}
.ttc .srow{display:flex;gap:6px;margin-bottom:5px}
.ttc .srow input{flex:1;min-width:0;font:inherit;color:var(--ink);background:#0e1015;border:1px solid #2c313c;border-radius:6px;padding:5px 7px;font-size:12px}
.ttc .srow .sty{flex:none;width:104px}
.ttc .overlay{position:fixed;inset:0;background:rgba(0,0,0,.55);display:flex;align-items:flex-start;justify-content:center;padding:32px 16px;overflow:auto;z-index:50}
.ttc .modal{background:#15171d;border:1px solid #2c313c;border-radius:12px;width:100%;max-width:520px;padding:18px}
.ttc .modal h2{margin:0 0 12px;font-size:16px;font-weight:500}
.ttc .fld{margin-bottom:11px}.ttc .fld label{display:block;font-size:12px;color:var(--muted);margin-bottom:4px}
.ttc .modal input,.ttc .modal select,.ttc .modal textarea{width:100%;font:inherit;color:var(--ink);background:#0e1015;border:1px solid #2c313c;border-radius:7px;padding:7px 9px}
.ttc .modal textarea{min-height:52px;resize:vertical}
.ttc .row{display:flex;gap:8px}.ttc .row>*{flex:1}
.ttc .costgrid{display:grid;grid-template-columns:repeat(2,1fr);gap:8px}.ttc .costgrid label{font-size:12px;color:var(--muted)}
.ttc .reqchip{display:inline-flex;align-items:center;gap:3px;background:#20232c;border:1px solid #333846;border-radius:20px;padding:2px 4px 2px 9px;font-size:12px;margin:0 5px 5px 0}
.ttc .reqchip button{padding:0 4px;font-size:11px;border-radius:50%;border:none;background:transparent}
.ttc .statrow,.ttc .effrow{display:flex;gap:6px;margin-bottom:6px;align-items:center}
.ttc .statrow input{flex:1}
.ttc .effrow select,.ttc .effrow input{margin:0;min-width:0}
.ttc .effrow .e-t{flex:2}.ttc .effrow .e-s{flex:3}.ttc .effrow .e-o{flex:2}.ttc .effrow .e-v{width:56px;flex:none}
.ttc .gennote{font-size:12.5px;color:#e8c872;background:#2a2516;border:1px solid #5a4a20;border-radius:8px;padding:8px 11px;margin-bottom:12px}
.ttc .mact{display:flex;gap:8px;justify-content:space-between;margin-top:16px}
`;
