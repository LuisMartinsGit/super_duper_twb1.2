import React, { useState, useMemo } from 'react';

/**
 * The Waning Border — Sect Reference
 *
 * Mirrors docs/Design/Sects.md, which is the canonical source. Structure:
 *   3 active powers, each with levels I / II / III
 *   1 passive (live only while the Temple stands)
 *   1 building (limit 5, trains the unit and sells the research)
 *   1 unit (trained at the sect building, capped at 5)
 *   1 research
 *   NO chapel aura — a sect projects nothing passively unless its Passive or
 *   Research says so.
 *
 * A power's level is how many Temple upgrades happened WHILE the sect was
 * already adopted, capped at III — so adopting early is what earns level III.
 *
 * Colours: validated dataviz palette. Clusters use categorical slots 1-3;
 * the radius scale uses a 4-step ordinal blue ramp (all checks pass in both
 * modes — see validate_palette.js).
 */

const CLUSTER = {
  Alanthor: { light: '#2a78d6', dark: '#3987e5' },
  Runai:    { light: '#eb6834', dark: '#d95926' },
  Feraldis: { light: '#1baf7a', dark: '#199e70' },
};

/** The only four radii that exist. Ordinal: reach grows with the index. */
const RADII = ['Single', 'Small', 'Medium', 'Large'];
const RADIUS_META = {
  Single: { label: 'Single Target', metres: null, step: 0 },
  Small:  { label: 'Small',        metres: 8,    step: 1 },
  Medium: { label: 'Medium',       metres: 15,   step: 2 },
  Large:  { label: 'Large',        metres: 25,   step: 3 },
};
const RADIUS_RAMP = {
  light: ['#86b6ef', '#3987e5', '#256abf', '#104281'],
  dark:  ['#6da7ec', '#3987e5', '#256abf', '#184f95'],
};

const LEVELS = ['I', 'II', 'III'];

// ── Data — transcribed from docs/Design/Sects.md ────────────────────────────
// Each power: three levels, each { r: radius key, text: effect }.

const SECTS = [
  {
    id: 'Antiquity', cluster: 'Alanthor', epithet: 'the holy librarians',
    identity: 'Intel & enemy shutdown',
    powers: [
      { name: 'Scour the Registry', levels: [
        { r: 'Medium', text: 'Reveal the area for 15s.' },
        { r: 'Large',  text: 'Reveal the area for 15s.' },
        { r: 'Large',  text: 'Reveal the area for 35s.' },
      ]},
      { name: 'Heavy Bureaucracy', levels: [
        { r: 'Single', text: 'Building stops training, research and resource output for 30s.' },
        { r: 'Small',  text: 'All buildings in the area stop for 30s.' },
        { r: 'Large',  text: 'All buildings in the area stop for 30s.' },
      ]},
      { name: 'Sew Disorder', levels: [
        { r: 'Small',  text: 'Units turn hostile to all other units for 8s.' },
        { r: 'Medium', text: 'Units turn hostile for 20s.' },
        { r: 'Large',  text: 'Units turn hostile until killed.' },
      ]},
    ],
    passive: { name: 'Tally of the Lost', text: 'Units gain +damage per unit-type they have killed this match, tracked per type (12 Archers killed = +12% vs Archers). Caps at +15%. The tally belongs to the unit TYPE, so replacements inherit it.' },
    building: { name: 'Reliquary', cap: 5, text: 'A vaulted archive. Every Reliquary standing shortens your sect-power cooldowns a little, so spreading them out is the Antiquity tempo play.' },
    unit: { name: 'Lore Keeper', cap: 5, text: 'Acts as a Ledger, and empowers military buildings for faster training and more damage.' },
    research: { name: 'Royal Index', text: 'All technologies and building upgrades take 30% less time and 10% fewer resources.' },
  },
  {
    id: 'Renewal', cluster: 'Alanthor', epithet: 'the menders',
    identity: 'Repair & sustain',
    powers: [
      { name: 'Hands of Plenty', levels: [
        { r: 'Small',  text: 'Restore 30% HP to units and buildings.' },
        { r: 'Medium', text: 'Restore 50% HP.' },
        { r: 'Medium', text: 'Restore 80% HP, and healing continues for 10s.' },
      ]},
      { name: 'Raise Anew', note: 'Conjures Watch Towers outright — it never touches construction queues.', levels: [
        { r: 'Single', text: 'Raise one free Lv 1 Watch Tower. Crumbles after 30s.' },
        { r: 'Small',  text: 'Raise Lv 2 Watch Towers across the area. They crumble after 60s.' },
        { r: 'Single', text: 'Raise a Lv 3 Watch Tower. Permanent — stays until destroyed.' },
      ]},
      { name: 'Second Wind', levels: [
        { r: 'Small',  text: 'Units cannot drop below 1 HP for 6s.' },
        { r: 'Small',  text: 'Units cannot drop below 1 HP for 12s.' },
        { r: 'Medium', text: '12s, and survivors heal 25% when it ends.' },
      ]},
    ],
    passive: { name: 'Hands That Mend', text: 'Your buildings auto-repair at 2% max HP/s while out of combat.' },
    building: { name: 'Mending Hall', cap: 5, text: 'An open-sided infirmary. Damaged units that walk inside heal over time.' },
    unit: { name: 'Scar Guard', cap: 5, text: 'Heavy frontline that deals more damage the closer it is to dying — deliberately combos with Second Wind, which pins it at 1 HP.' },
    research: { name: 'Field Hospital', text: 'Your Litharchs unlock Deploy Field Hospital: a temporary infirmary raised at the caster, healing allies around it before destroying itself after 2 minutes. 300s cooldown.' },
  },
  {
    id: 'Fortitude', cluster: 'Alanthor', epithet: 'the wall-keepers',
    identity: 'Static defense',
    powers: [
      { name: 'Stoneveil', note: 'Veiled units move FASTER but are invisible, untargetable and cannot interact with anything. Sect powers still reach them.', levels: [
        { r: 'Small',  text: 'Veil the area for 8s.' },
        { r: 'Small',  text: 'Veil the area for 15s.' },
        { r: 'Medium', text: '15s; on expiry they gain +25% damage for 10s.' },
      ]},
      { name: 'Bulwark', levels: [
        { r: 'Single', text: 'Building gains +100% HP for 30s.' },
        { r: 'Small',  text: 'Buildings gain +100% HP for 30s.' },
        { r: 'Medium', text: '+100% HP for 30s and they reflect 20% of melee damage.' },
      ]},
      { name: 'Immovable', note: 'Replaces the old crowd-control version — there is no pushback system to negate.', levels: [
        { r: 'Small',  text: 'Units gain +5 armor for 10s.' },
        { r: 'Medium', text: 'Units gain +8 armor for 15s.' },
        { r: 'Large',  text: 'Units become invulnerable for 20s.', flag: 'Strongest defensive effect in the game — first number to revisit if Fortitude dominates.' },
      ]},
    ],
    passive: { name: 'Veiled Stone', text: 'Your walls and towers gain +25% HP; towers gain +1 range.' },
    building: { name: 'Stonehold', cap: 5, text: 'A squat windowless blockhouse. Highest HP of any non-Hall structure, and it blocks pathing like a wall.' },
    unit: { name: 'Stone Warden', cap: 5, text: 'Slow heavy infantry projecting a damage-reduction dome. Can NEVER attack — a walking wall, not a fighter.' },
    research: { name: 'Deep Foundations', text: 'Defensive structures cost 20% less and build 30% faster.' },
  },
  {
    id: 'Reclamation', cluster: 'Alanthor', epithet: 'the curse-harvesters',
    identity: 'Curse exploitation',
    powers: [
      { name: 'Harvest the Veil', note: 'Target a resource node; it over-yields on a 5s tick for 30s. Escalation is purely in the yield.', levels: [
        { r: 'Single', text: '50 Supplies per tick — 300 Supplies over 30s.' },
        { r: 'Single', text: '75 Supplies + 20 Iron per tick — 450 S + 120 I.' },
        { r: 'Single', text: '150 Supplies + 60 Iron + 35 Veilstone + 5 Veilsteel per tick — 900 S + 360 I + 210 V + 30 Vs.' },
      ]},
      { name: 'Cleanse', note: 'Pumps aggressive bursts of player influence for the duration — drives the existing influence map, pushing the curse back and claiming ground at once.', levels: [
        { r: 'Small',  text: '20s of heavy influence deposit.' },
        { r: 'Medium', text: '40s of heavy influence deposit.' },
        { r: 'Large',  text: '40s, and allies inside regenerate.' },
      ]},
      { name: 'Veil-Touched', levels: [
        { r: 'Small',  text: 'Units take no curse damage for 15s.' },
        { r: 'Medium', text: 'No curse damage for 30s.' },
        { r: 'Large',  text: '30s, and they move 20% faster on cursed ground.' },
      ]},
    ],
    passive: { name: 'Border-Hardened', text: 'Your units take 50% less damage from Border sources, and your workers can harvest cursed nodes.' },
    building: { name: 'Veilworks', cap: 5, text: 'A smelter for cursed matter. The only building that may be raised ON cursed ground, and it takes no curse damage.' },
    unit: { name: 'Golem Autark', cap: 5, text: 'Curse-immune construct that fights and harvests on cursed ground.' },
    research: { name: "Warden's Ledger", text: 'Veilstone yields +25%, and every cursed node is harvestable regardless of tier.' },
  },
  {
    id: 'Silence', cluster: 'Runai', epithet: 'the hush',
    identity: 'Denial & tempo',
    powers: [
      { name: 'Hush', levels: [
        { r: 'Single', text: 'Building cannot train or research for 20s.' },
        { r: 'Small',  text: 'Buildings silenced for 20s.' },
        { r: 'Medium', text: 'Buildings silenced for 30s.' },
      ]},
      { name: 'Entomb', levels: [
        { r: 'Single', text: 'Unit sealed 5s: untargetable, immobile, deals no damage.' },
        { r: 'Small',  text: 'Units sealed 8s.' },
        { r: 'Small',  text: '8s; on release they are Marked (+25% damage taken) for 15s.' },
      ]},
      { name: 'Whisper-Wind', levels: [
        { r: 'Small',  text: 'Allies move 20% faster for 8s.' },
        { r: 'Medium', text: '20% faster for 12s.' },
        { r: 'Large',  text: '12s, and they ignore terrain slow.' },
      ]},
    ],
    passive: { name: 'Steadfast Vigil', text: 'Your units gain +3 armor while holding position.' },
    building: { name: 'Hush Vault', cap: 5, text: 'A sunken stone cell. Enemy sect powers cast within its footprint cost their caster extra cooldown.' },
    unit: { name: 'Archivist Adept', cap: 5, text: 'Caster that suppresses enemy abilities within a small radius around itself.' },
    research: { name: 'Quiet Roads', text: 'Your units move 15% faster while out of combat.' },
  },
  {
    id: 'Justice', cluster: 'Runai', epithet: 'the tribunal',
    identity: 'Retribution',
    powers: [
      { name: 'Eye of the Law', levels: [
        { r: 'Medium', text: 'Reveal the area for 10s.' },
        { r: 'Large',  text: 'Reveal for 10s, including stealth.' },
        { r: 'Large',  text: 'Reveal for 25s; revealed units are Marked.' },
      ]},
      { name: 'Sentence', levels: [
        { r: 'Single', text: '120 true damage after a 3s telegraph.' },
        { r: 'Small',  text: '120 true damage to everything in the area.' },
        { r: 'Medium', text: '180 true damage; survivors Marked for 30s.' },
      ]},
      { name: 'Writ of Blood', levels: [
        { r: 'Small',  text: 'Enemies that have killed your units take +50% damage for 10s.' },
        { r: 'Medium', text: 'Same, 20s.' },
        { r: 'Large',  text: '20s, and they are slowed 30%.' },
      ]},
    ],
    passive: { name: 'Marked for Sentence', text: 'Anything that kills one of your units takes +25% damage from your army until it dies.' },
    building: { name: 'Tribunal', cap: 5, text: 'A raised court platform. Marked enemies that die anywhere on the map refund part of the Tribunal research cost.' },
    unit: { name: 'Judicator', cap: 5, text: 'Executioner dealing heavy bonus damage to Marked targets.' },
    research: { name: 'Writ of Law', text: 'Marked lasts twice as long and spreads to enemies near the marked target.' },
  },
  {
    id: 'Veneration', cluster: 'Runai', epithet: 'the choir',
    identity: 'Escalating offense',
    powers: [
      { name: 'Litany', levels: [
        { r: 'Small',  text: 'Allies gain +20% damage for 10s.' },
        { r: 'Medium', text: '+20% damage for 15s.' },
        { r: 'Large',  text: '+50% damage for 15s.' },
      ]},
      { name: 'Crystal Communion', levels: [
        { r: 'Small',  text: 'Allies gain +15% damage reduction for 15s.' },
        { r: 'Medium', text: '+25% reduction for 20s.' },
        { r: 'Large',  text: '+25% reduction and +25% move speed for 20s.' },
      ]},
      { name: 'Ascend', levels: [
        { r: 'Single', text: 'Ally gains +1 veterancy rank.' },
        { r: 'Small',  text: 'Allies gain +1 veterancy rank.' },
        { r: 'Medium', text: 'Allies gain +1 veterancy rank.' },
      ]},
    ],
    passive: { name: 'Fervor', text: 'Each of your unit kills grants a stacking +2% damage and attack rate, capping at +20%.' },
    building: { name: 'Choir Hall', cap: 5, text: 'A resonating hall. Friendly units passing through gain a short Fervor bonus.' },
    unit: { name: 'Vault Keeper', cap: 5, text: 'Elite guard that doubles Fervor stacking for nearby allies.' },
    research: { name: 'Rite of Ascension', text: 'Your units gain veterancy 50% faster.' },
  },
  {
    id: 'Witness', cluster: 'Runai', epithet: 'the open eye',
    identity: 'Vision',
    powers: [
      { name: 'Foresight', levels: [
        { r: 'Large', text: 'Reveal the area for 8s.' },
        { r: 'Large', text: 'Reveal for 15s, including stealth.' },
        { r: 'Large', text: 'Reveal for 20s; revealed enemies take +25% damage from your units.' },
      ]},
      { name: "Watcher's Mark", levels: [
        { r: 'Single', text: 'Enemy is revealed until it dies.' },
        { r: 'Small',  text: 'Enemies are revealed until they die.' },
        { r: 'Medium', text: 'Revealed until death, and they lose half their own vision.' },
      ]},
      { name: 'Blinding Glare', levels: [
        { r: 'Small',  text: 'Enemies lose all vision for 8s.' },
        { r: 'Medium', text: 'Lose all vision for 12s.' },
        { r: 'Large',  text: '12s, and they cannot use abilities.' },
      ]},
    ],
    passive: { name: 'All-Seeing', text: 'Your Scouts gain +50% vision; every other unit gains +2m.' },
    building: { name: 'Glass Spire', cap: 5, text: 'A thin mirrored tower. Sees further than any other building; cannot be built inside another Glass Spire sight radius.' },
    unit: { name: 'Glassmark Arcanist', cap: 5, text: 'Caster granting permanent vision over the ground it stands on.' },
    research: { name: 'The Long Watch', text: 'Explored terrain never returns to fog; enemy buildings stay visible once seen.' },
  },
  {
    id: 'War', cluster: 'Feraldis', epithet: 'the muster',
    identity: 'Mass & momentum',
    powers: [
      { name: 'Blood Rain', levels: [
        { r: 'Small',  text: 'Blood falls, leaving a blood pool. For 10s EVERY unit on the map attacks 5% faster and no ability or sect power can be cast anywhere.' },
        { r: 'Medium', text: 'Bigger pool; 10% attack speed map-wide, 20s lockout.' },
        { r: 'Large',  text: 'Largest pool; 15% attack speed map-wide, 30s lockout.' },
      ]},
      { name: 'Call to Arms', levels: [
        { r: 'Single', text: 'Military building trains units 50% cheaper for 15s.' },
        { r: 'Small',  text: 'Military buildings train 50% cheaper for 30s.' },
        { r: 'Medium', text: '30s, and those buildings also train at double speed.' },
      ]},
      { name: 'Bloodfury', levels: [
        { r: 'Small',  text: 'Allies deal +25% attack damage for 8s.' },
        { r: 'Medium', text: '+25% damage for 12s.' },
        { r: 'Large',  text: '12s, +25% damage AND +5 armor.' },
      ]},
    ],
    passive: { name: 'Forged in Battle', text: 'Your military units cost 10% less and train 20% faster.' },
    building: { name: 'Muster Yard', cap: 5, text: "A stockade of training posts and armourers' racks. Every per-battalion upgrade you apply anywhere in the faction costs 50% less. Does not stack." },
    unit: { name: 'Warbreaker', cap: 5, text: 'Shock infantry gaining damage for each nearby ally.' },
    research: { name: 'Endless Muster', text: 'Military buildings train two units at once. Queue depth is unchanged.' },
  },
  {
    id: 'Ash', cluster: 'Feraldis', epithet: 'the burning ground',
    identity: 'Area denial by fire',
    powers: [
      { name: 'Pyre', levels: [
        { r: 'Small',  text: 'Ignite the area for 15s, damaging enemies inside.' },
        { r: 'Medium', text: '30s; the zone is impassable to enemies.' },
        { r: 'Large',  text: '30s, impassable, and any blood in it ignites.' },
      ]},
      { name: 'Cinderfall', levels: [
        { r: 'Single', text: 'Target burns for heavy damage over 10s.' },
        { r: 'Small',  text: 'Area burns for 10s.' },
        { r: 'Medium', text: 'Burns for 15s, spreading to units that flee it.' },
      ]},
      { name: 'Ashen Veil', levels: [
        { r: 'Small',  text: 'Smoke: ranged attacks into the area miss 30% of the time for 10s.' },
        { r: 'Medium', text: '30% miss for 15s.' },
        { r: 'Large',  text: '50% miss for 15s.' },
      ]},
    ],
    passive: { name: "Pyre's Promise", text: 'Your units leave a burning patch where they die.' },
    building: { name: 'Ash Pyre', cap: 5, text: 'A permanently burning pyre. Enemies adjacent to it take burn damage — as much a weapon as a building.' },
    unit: { name: 'Ashblade', cap: 5, text: 'Melee infantry that ignites whatever it strikes.' },
    research: { name: 'Everburning', text: 'All of your fire effects last 50% longer and deal 25% more damage.' },
  },
  {
    id: 'Ruin', cluster: 'Feraldis', epithet: 'the unmakers',
    identity: 'Structure breaking',
    powers: [
      { name: 'Unmake', levels: [
        { r: 'Single', text: 'Enemy building takes 50% of current HP as damage after a 3s telegraph.' },
        { r: 'Single', text: '75% of current HP.' },
        { r: 'Small',  text: '90% of current HP; others in the area take 25% splash.' },
      ]},
      { name: 'Profane Strike', levels: [
        { r: 'Small',  text: 'Burst damage across the area.' },
        { r: 'Medium', text: 'Burst damage across the area.' },
        { r: 'Large',  text: 'Burst damage; buildings hit cannot be repaired for 20s.' },
      ]},
      { name: 'Sunder', levels: [
        { r: 'Single', text: 'Building loses all armor for 20s.' },
        { r: 'Small',  text: 'Buildings lose all armor for 20s.' },
        { r: 'Medium', text: 'Lose all armor and take +50% siege damage.' },
      ]},
    ],
    passive: { name: 'Profane Hands', text: 'Your units deal +25% damage to buildings and refund their own cost when a building falls to them.' },
    building: { name: 'Ruinworks', cap: 5, text: 'A scaffold of breaking-tools. Siege units built while it stands carry extra damage against structures.' },
    unit: { name: 'Nullblade', cap: 5, text: 'Siege-class infantry that ignores building armor.' },
    research: { name: 'Iconoclasm', text: 'Destroying a building grants resources and reveals a large area around the wreck.' },
  },
  {
    id: 'Wrath', cluster: 'Feraldis', epithet: 'the forsaken',
    identity: 'Punishment at low HP',
    powers: [
      { name: 'Final Hour', levels: [
        { r: 'Small',  text: 'Allies cannot drop below 1 HP for 12s; when it ends, low-HP units explode.' },
        { r: 'Medium', text: 'Same, 20s.' },
        { r: 'Large',  text: '20s; explosions leave blood pools and apply Bleeding.' },
      ]},
      { name: 'Spite', levels: [
        { r: 'Single', text: 'Target takes damage equal to 20% of all damage it has dealt this match.' },
        { r: 'Single', text: '35% of damage dealt.' },
        { r: 'Small',  text: '50% of damage dealt, across the area.' },
      ]},
      { name: 'Wrathfire', levels: [
        { r: 'Small',  text: 'A burning pillar scorches the area for 8s.' },
        { r: 'Medium', text: 'Scorches for 12s.' },
        { r: 'Large',  text: 'Scorches for 16s, and enemies inside bleed.' },
      ]},
    ],
    passive: { name: 'Spite of the Forsaken', text: 'Your units deal up to +40% damage as their HP falls.' },
    building: { name: 'Chain Altar', cap: 5, text: 'An altar strung with iron links. Enemies killed near it feed a faction-wide damage stack that decays over time.' },
    unit: { name: 'Chaincaster', cap: 5, text: 'Caster that links enemies so damage dealt to one bleeds through to the others.' },
    research: { name: 'Blood Debt', text: "Your units' deaths grant the faction resources and a brief army-wide damage surge." },
  },
];

const CLUSTERS = ['Alanthor', 'Runai', 'Feraldis'];

// ── Pieces ─────────────────────────────────────────────────────────────────

function RadiusChip({ r, mode }) {
  const m = RADIUS_META[r];
  return (
    <span className="sr-rad" style={{ '--rad': RADIUS_RAMP[mode][m.step] }}>
      <span className="sr-raddot" aria-hidden="true" />
      {m.label}{m.metres ? ` · ${m.metres}m` : ''}
    </span>
  );
}

/** Reach ladder: one cell per level, filled to that level's radius step.
 *  Ordinal encoding — darker/longer means further reach. */
function ReachLadder({ power, mode, onHover, onLeave, sect }) {
  return (
    <div className="sr-ladder" role="img"
      aria-label={`${power.name} reach by level: ${power.levels.map((l, i) => `${LEVELS[i]} ${RADIUS_META[l.r].label}`).join(', ')}`}>
      {power.levels.map((lv, i) => {
        const step = RADIUS_META[lv.r].step;
        return (
          <div key={i} className="sr-lrow" tabIndex={0}
            onMouseEnter={e => onHover({ sect, power: power.name, lvl: LEVELS[i], ...lv }, e)}
            onMouseLeave={onLeave}
            onFocus={e => onHover({ sect, power: power.name, lvl: LEVELS[i], ...lv }, e)}
            onBlur={onLeave}>
            <span className="sr-lvl">{LEVELS[i]}</span>
            <div className="sr-ltrack">
              <div className="sr-lfill"
                style={{ width: `${((step + 1) / 4) * 100}%`, background: RADIUS_RAMP[mode][step] }} />
            </div>
            <span className="sr-lrad">{RADIUS_META[lv.r].label}</span>
          </div>
        );
      })}
    </div>
  );
}

function SectCard({ sect, mode, level, onHover, onLeave }) {
  const hue = CLUSTER[sect.cluster][mode];
  return (
    <article className="sr-card">
      <header className="sr-card-head">
        <span className="sr-swatch" style={{ background: hue }} aria-hidden="true" />
        <div>
          <h3 className="sr-card-title">Sect of {sect.id}</h3>
          <p className="sr-card-sub">{sect.cluster} · <em>{sect.epithet}</em> · {sect.identity}</p>
        </div>
      </header>

      {sect.powers.map(p => {
        const lv = p.levels[level - 1];
        return (
          <section className="sr-block" key={p.name}>
            <h4 className="sr-h4">
              <span className="sr-kind">Active</span> {p.name}
              <span className="sr-lvbadge">{LEVELS[level - 1]}</span>
            </h4>
            {p.note && <p className="sr-mech">{p.note}</p>}
            <p className="sr-body"><RadiusChip r={lv.r} mode={mode} /> {lv.text}</p>
            {lv.flag && <p className="sr-flag"><span aria-hidden="true">⚠</span> Balance: {lv.flag}</p>}
            <ReachLadder power={p} mode={mode} sect={sect.id} onHover={onHover} onLeave={onLeave} />
          </section>
        );
      })}

      <section className="sr-block">
        <h4 className="sr-h4"><span className="sr-kind alt">Passive</span> {sect.passive.name}</h4>
        <p className="sr-body">{sect.passive.text}</p>
        <p className="sr-foot">Live only while the Temple stands.</p>
      </section>

      <section className="sr-block">
        <h4 className="sr-h4"><span className="sr-kind alt">Building</span> {sect.building.name}</h4>
        <p className="sr-body">{sect.building.text}</p>
        <p className="sr-foot">Limit {sect.building.cap} · trains the unit, sells the research</p>
      </section>

      <section className="sr-block">
        <h4 className="sr-h4"><span className="sr-kind alt">Unit</span> {sect.unit.name}</h4>
        <p className="sr-body">{sect.unit.text}</p>
        <p className="sr-foot">Trained at the {sect.building.name} · limit {sect.unit.cap}</p>
      </section>

      <section className="sr-block">
        <h4 className="sr-h4"><span className="sr-kind alt">Research</span> {sect.research.name}</h4>
        <p className="sr-body">{sect.research.text}</p>
      </section>
    </article>
  );
}

// ── Root ───────────────────────────────────────────────────────────────────

export default function SectReference() {
  const [cluster, setCluster] = useState('All');
  const [level, setLevel] = useState(1);
  const [view, setView] = useState('cards');
  const [tip, setTip] = useState(null);

  const mode = typeof document !== 'undefined'
    && (document.documentElement.dataset.theme === 'dark'
      || (document.documentElement.dataset.theme !== 'light'
        && window.matchMedia?.('(prefers-color-scheme: dark)').matches))
    ? 'dark' : 'light';

  const shown = useMemo(
    () => cluster === 'All' ? SECTS : SECTS.filter(s => s.cluster === cluster),
    [cluster]);

  const onHover = (d, e) => setTip({
    x: e.clientX, y: e.clientY,
    text: `${d.sect} · ${d.power} ${d.lvl} — ${RADIUS_META[d.r].label}${RADIUS_META[d.r].metres ? ` (${RADIUS_META[d.r].metres}m)` : ''} — ${d.text}`,
  });

  return (
    <div className="sr-root viz-root">
      <style>{CSS}</style>

      <header className="sr-top">
        <h1 className="sr-title">Sect Reference</h1>
        <p className="sr-lede">
          12 sects. Each grants <strong>3 active powers</strong> (levels I–III), one{' '}
          <strong>passive</strong>, one <strong>unit</strong> and one <strong>research</strong>.
          There are <strong>no chapel auras</strong> — a sect projects nothing passively
          unless its Passive or Research says so. Canon:{' '}
          <code>docs/Design/Sects.md</code>.
        </p>
        <p className="sr-rule">
          <strong>Levels come from adoption timing.</strong> A power's level is how many
          Temple upgrades happened <em>while the sect was already adopted</em>, capped at III.
          Adopt early and reach III; adopt after the Temple is maxed and you stay at I.
        </p>
      </header>

      <div className="sr-filters">
        <div className="sr-group" role="group" aria-label="Filter by culture cluster">
          {['All', ...CLUSTERS].map(c => (
            <button key={c} onClick={() => setCluster(c)}
              className={'sr-btn' + (cluster === c ? ' is-on' : '')}>
              {c !== 'All' && <span className="sr-swatch sm" style={{ background: CLUSTER[c][mode] }} aria-hidden="true" />}
              {c}
            </button>
          ))}
        </div>
        <div className="sr-group" role="group" aria-label="Power level shown">
          <span className="sr-glabel">Level</span>
          {[1, 2, 3].map(l => (
            <button key={l} onClick={() => setLevel(l)}
              className={'sr-btn' + (level === l ? ' is-on' : '')}>{LEVELS[l - 1]}</button>
          ))}
        </div>
        <div className="sr-group" role="group" aria-label="View">
          {['cards', 'table'].map(v => (
            <button key={v} onClick={() => setView(v)}
              className={'sr-btn' + (view === v ? ' is-on' : '')}>
              {v === 'cards' ? 'Cards' : 'Table view'}
            </button>
          ))}
        </div>
      </div>

      <div className="sr-legend">
        {CLUSTERS.map(c => (
          <span key={c} className="sr-leg">
            <span className="sr-swatch sm" style={{ background: CLUSTER[c][mode] }} aria-hidden="true" />{c}
          </span>
        ))}
        <span className="sr-leg-sep" />
        <span className="sr-leg sr-dim">Reach</span>
        {RADII.map(r => (
          <span key={r} className="sr-leg">
            <span className="sr-swatch sm" style={{ background: RADIUS_RAMP[mode][RADIUS_META[r].step] }} aria-hidden="true" />
            {RADIUS_META[r].label}{RADIUS_META[r].metres ? ` ${RADIUS_META[r].metres}m` : ''}
          </span>
        ))}
      </div>

      {view === 'cards' ? (
        <div className="sr-grid">
          {shown.map(s => (
            <SectCard key={s.id} sect={s} mode={mode} level={level}
              onHover={onHover} onLeave={() => setTip(null)} />
          ))}
        </div>
      ) : (
        <div className="sr-tablewrap">
          <table className="sr-table">
            <thead>
              <tr>
                <th>Sect</th><th>Cluster</th><th>Power</th>
                <th>Lv</th><th>Reach</th><th>Effect</th>
              </tr>
            </thead>
            <tbody>
              {shown.flatMap(s => s.powers.flatMap((p, pi) => p.levels.map((lv, li) => (
                <tr key={s.id + p.name + li}>
                  {pi === 0 && li === 0 && <td rowSpan={9} className="sr-strong">{s.id}</td>}
                  {pi === 0 && li === 0 && <td rowSpan={9}>{s.cluster}</td>}
                  {li === 0 && <td rowSpan={3} className="sr-strong">{p.name}
                    {p.note && <span className="sr-mech tbl">{p.note}</span>}</td>}
                  <td className="sr-num">{LEVELS[li]}</td>
                  <td><RadiusChip r={lv.r} mode={mode} /></td>
                  <td>{lv.text}{lv.flag && <span className="sr-flag tbl"><span aria-hidden="true">⚠</span> {lv.flag}</span>}</td>
                </tr>
              ))))}
            </tbody>
          </table>
          <table className="sr-table sr-table2">
            <thead>
              <tr><th>Sect</th><th>Passive</th><th>Building (limit 5)</th><th>Unit (limit)</th><th>Research</th></tr>
            </thead>
            <tbody>
              {shown.map(s => (
                <tr key={s.id}>
                  <td className="sr-strong">{s.id}</td>
                  <td><span className="sr-strong">{s.passive.name}</span> — {s.passive.text}</td>
                  <td><span className="sr-strong">{s.building.name}</span> — {s.building.text}</td>
                  <td><span className="sr-strong">{s.unit.name}</span> ({s.unit.cap}) — {s.unit.text}</td>
                  <td><span className="sr-strong">{s.research.name}</span> — {s.research.text}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {tip && <div className="sr-tip" style={{ left: tip.x + 14, top: tip.y + 14 }} role="status">{tip.text}</div>}
    </div>
  );
}

const CSS = `
.sr-root {
  color-scheme: light;
  --surface-1: #fcfcfb; --plane: #f9f9f7;
  --ink-1: #0b0b0b; --ink-2: #52514e; --ink-muted: #898781;
  --grid: #e1e0d9; --axis: #c3c2b7; --ring: rgba(11,11,11,0.10);
  background: var(--plane); color: var(--ink-1);
  font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
  padding: 28px clamp(16px, 4vw, 48px) 64px; min-height: 100%;
}
@media (prefers-color-scheme: dark) {
  :root:where(:not([data-theme="light"])) .sr-root {
    color-scheme: dark;
    --surface-1: #1a1a19; --plane: #0d0d0d;
    --ink-1: #ffffff; --ink-2: #c3c2b7; --ink-muted: #898781;
    --grid: #2c2c2a; --axis: #383835; --ring: rgba(255,255,255,0.10);
  }
}
:root[data-theme="dark"] .sr-root {
  color-scheme: dark;
  --surface-1: #1a1a19; --plane: #0d0d0d;
  --ink-1: #ffffff; --ink-2: #c3c2b7; --ink-muted: #898781;
  --grid: #2c2c2a; --axis: #383835; --ring: rgba(255,255,255,0.10);
}

.sr-top { max-width: 82ch; margin-bottom: 6px; }
.sr-title { font-size: clamp(24px, 3vw, 34px); font-weight: 650; letter-spacing: -0.01em; }
.sr-lede { color: var(--ink-2); font-size: 14.5px; line-height: 1.55; margin-top: 8px; }
.sr-lede strong, .sr-rule strong { color: var(--ink-1); font-weight: 650; }
.sr-lede code { font-size: 12.5px; background: var(--surface-1); border: 1px solid var(--ring);
  border-radius: 4px; padding: 1px 5px; }
.sr-rule { margin-top: 10px; padding: 10px 12px; background: var(--surface-1);
  border: 1px solid var(--ring); border-left: 3px solid var(--axis); border-radius: 6px;
  font-size: 13.5px; line-height: 1.5; color: var(--ink-2); }

.sr-filters { display: flex; flex-wrap: wrap; gap: 10px 22px; align-items: center; margin: 18px 0 12px; }
.sr-group { display: flex; gap: 6px; flex-wrap: wrap; align-items: center; }
.sr-glabel { font-size: 11.5px; text-transform: uppercase; letter-spacing: 0.06em;
  color: var(--ink-muted); font-weight: 650; margin-right: 2px; }
.sr-btn { display: inline-flex; align-items: center; gap: 6px; font: inherit; font-size: 13px;
  color: var(--ink-2); background: var(--surface-1); border: 1px solid var(--ring);
  border-radius: 999px; padding: 6px 13px; cursor: pointer; }
.sr-btn:hover { border-color: var(--axis); }
.sr-btn.is-on { color: var(--ink-1); border-color: var(--axis); font-weight: 600; }

.sr-legend { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px;
  padding: 10px 0 18px; border-bottom: 1px solid var(--grid); margin-bottom: 22px; }
.sr-leg { display: inline-flex; align-items: center; gap: 6px; font-size: 12.5px; color: var(--ink-2); }
.sr-leg-sep { width: 1px; height: 14px; background: var(--grid); }
.sr-swatch { width: 12px; height: 12px; border-radius: 3px; box-shadow: 0 0 0 1px var(--ring); flex: none; }
.sr-swatch.sm { width: 10px; height: 10px; border-radius: 2px; }
.sr-dim { color: var(--ink-muted); }

.sr-grid { display: grid; gap: 16px; grid-template-columns: repeat(auto-fill, minmax(360px, 1fr)); }
.sr-card { background: var(--surface-1); border: 1px solid var(--ring); border-radius: 12px;
  padding: 16px 16px 12px; }
.sr-card-head { display: flex; gap: 10px; align-items: flex-start; padding-bottom: 12px;
  border-bottom: 1px solid var(--grid); }
.sr-card-title { font-size: 17px; font-weight: 650; }
.sr-card-sub { font-size: 12.5px; color: var(--ink-2); margin-top: 2px; }
.sr-block { padding: 12px 0; border-bottom: 1px solid var(--grid); }
.sr-block:last-child { border-bottom: 0; padding-bottom: 4px; }
.sr-h4 { font-size: 13.5px; color: var(--ink-1); font-weight: 650; margin-bottom: 6px;
  display: flex; align-items: center; gap: 7px; flex-wrap: wrap; }
.sr-kind { font-size: 10px; text-transform: uppercase; letter-spacing: 0.07em; font-weight: 700;
  color: var(--ink-muted); border: 1px solid var(--ring); border-radius: 4px; padding: 1px 5px; }
.sr-kind.alt { background: var(--plane); }
.sr-lvbadge { font-size: 10.5px; font-weight: 700; color: var(--ink-muted);
  border: 1px solid var(--axis); border-radius: 999px; padding: 0 6px; }
.sr-body { font-size: 13.5px; line-height: 1.5; color: var(--ink-2); }
.sr-strong { color: var(--ink-1); font-weight: 600; }
.sr-foot { font-size: 11.5px; color: var(--ink-muted); margin-top: 6px; }

.sr-mech { font-size: 12px; line-height: 1.45; color: var(--ink-muted); font-style: italic;
  margin-bottom: 6px; }
.sr-mech.tbl { display: block; font-style: italic; margin-top: 4px; font-weight: 400; }
.sr-flag { font-size: 11.5px; line-height: 1.45; color: var(--ink-2); margin-top: 6px;
  background: var(--plane); border: 1px solid var(--ring); border-left: 3px solid #fab219;
  border-radius: 5px; padding: 5px 8px; }
.sr-flag.tbl { display: block; margin-top: 5px; }

.sr-rad { display: inline-flex; align-items: center; gap: 5px; font-size: 11px; font-weight: 650;
  color: var(--ink-1); background: var(--plane); border: 1px solid var(--ring);
  border-radius: 999px; padding: 1px 8px 1px 6px; margin-right: 6px; white-space: nowrap; }
.sr-raddot { width: 8px; height: 8px; border-radius: 50%; background: var(--rad); flex: none; }

.sr-ladder { margin-top: 8px; display: grid; gap: 2px; }
.sr-lrow { display: grid; grid-template-columns: 22px 1fr 84px; align-items: center; gap: 8px;
  padding: 2px 0; border-radius: 4px; }
.sr-lrow:hover, .sr-lrow:focus-visible { background: var(--plane); outline: none; }
.sr-lvl { font-size: 10.5px; color: var(--ink-muted); font-weight: 700; }
.sr-ltrack { height: 7px; background: var(--grid); border-radius: 4px; overflow: hidden; }
.sr-lfill { height: 100%; border-radius: 0 4px 4px 0; }
.sr-lrad { font-size: 10.5px; color: var(--ink-2); text-align: right; white-space: nowrap; }

.sr-tablewrap { overflow-x: auto; background: var(--surface-1); border: 1px solid var(--ring);
  border-radius: 12px; padding-bottom: 4px; }
.sr-table { border-collapse: collapse; font-size: 12.5px; min-width: 900px; width: 100%; }
.sr-table2 { margin-top: 8px; border-top: 2px solid var(--axis); min-width: 1100px; }
.sr-table th { position: sticky; top: 0; background: var(--surface-1); text-align: left;
  font-size: 11px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--ink-muted);
  padding: 10px 12px; border-bottom: 1px solid var(--axis); white-space: nowrap; }
.sr-table td { padding: 7px 12px; border-bottom: 1px solid var(--grid); color: var(--ink-2);
  vertical-align: top; }
.sr-num { font-variant-numeric: tabular-nums; }

.sr-tip { position: fixed; z-index: 20; max-width: 340px; pointer-events: none;
  background: var(--surface-1); color: var(--ink-1); border: 1px solid var(--axis);
  border-radius: 8px; padding: 7px 10px; font-size: 12px; line-height: 1.45;
  box-shadow: 0 6px 18px rgba(0,0,0,0.18); }
`;
