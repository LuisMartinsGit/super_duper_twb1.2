import React, { useState } from 'react';

const colors = {
  brain:    { bg: '#1a1a2e', border: '#e94560', text: '#fff' },
  economy:  { bg: '#0f3460', border: '#16c79a', text: '#e8e8e8' },
  building: { bg: '#1a3c40', border: '#2ec4b6', text: '#e8e8e8' },
  military: { bg: '#3d0000', border: '#e94560', text: '#e8e8e8' },
  scout:    { bg: '#2d2d44', border: '#ffd460', text: '#e8e8e8' },
  mission:  { bg: '#3b1f5e', border: '#c77dff', text: '#e8e8e8' },
  tactical: { bg: '#4a0e0e', border: '#ff6b6b', text: '#e8e8e8' },
  crystal:  { bg: '#1a0a2e', border: '#9b59b6', text: '#e8e8e8' },
  defense:  { bg: '#2c3e50', border: '#e67e22', text: '#e8e8e8' },
  hunt:     { bg: '#1b2631', border: '#27ae60', text: '#e8e8e8' },
  decision: { bg: '#2c2c3a', border: '#ffd460', text: '#ffd460' },
  action:   { bg: '#1e3a2f', border: '#2ecc71', text: '#2ecc71' },
  check:    { bg: '#3a2c1e', border: '#e67e22', text: '#e67e22' },
};

const Node = ({ x, y, w, h, color, label, sublabel, onClick, highlight }) => (
  <g onClick={onClick} style={{ cursor: onClick ? 'pointer' : 'default' }}>
    <rect x={x} y={y} width={w} height={h} rx={6}
      fill={color.bg} stroke={highlight ? '#fff' : color.border} strokeWidth={highlight ? 2.5 : 1.5}
      filter={highlight ? 'url(#glow)' : undefined} />
    <text x={x + w/2} y={y + (sublabel ? h/2 - 6 : h/2 + 1)} textAnchor="middle"
      dominantBaseline="middle" fill={color.text} fontSize={11} fontWeight="bold" fontFamily="monospace">
      {label}
    </text>
    {sublabel && (
      <text x={x + w/2} y={y + h/2 + 10} textAnchor="middle"
        dominantBaseline="middle" fill={color.text} fontSize={8.5} fontFamily="monospace" opacity={0.7}>
        {sublabel}
      </text>
    )}
  </g>
);

const Diamond = ({ x, y, size, color, label }) => {
  const half = size / 2;
  const points = `${x},${y-half} ${x+half},${y} ${x},${y+half} ${x-half},${y}`;
  return (
    <g>
      <polygon points={points} fill={color.bg} stroke={color.border} strokeWidth={1.5} />
      <text x={x} y={y+1} textAnchor="middle" dominantBaseline="middle"
        fill={color.text} fontSize={8} fontWeight="bold" fontFamily="monospace">
        {label}
      </text>
    </g>
  );
};

const Arrow = ({ x1, y1, x2, y2, label, dashed }) => (
  <g>
    <line x1={x1} y1={y1} x2={x2} y2={y2}
      stroke="#556" strokeWidth={1.2}
      strokeDasharray={dashed ? '4,3' : undefined}
      markerEnd="url(#arrowhead)" />
    {label && (
      <text x={(x1+x2)/2 + 4} y={(y1+y2)/2 - 4} fill="#889" fontSize={7}
        fontFamily="monospace" textAnchor="middle">{label}</text>
    )}
  </g>
);

const DetailPanel = ({ title, items, color, onClose }) => (
  <div style={{
    position: 'fixed', right: 20, top: 20, width: 340, maxHeight: '90vh',
    background: color.bg, border: `2px solid ${color.border}`,
    borderRadius: 8, padding: 16, overflowY: 'auto', zIndex: 100,
    fontFamily: 'monospace', color: color.text, fontSize: 12
  }}>
    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 12 }}>
      <span style={{ fontWeight: 'bold', fontSize: 14, color: color.border }}>{title}</span>
      <span onClick={onClose} style={{ cursor: 'pointer', color: '#e94560', fontSize: 16 }}>✕</span>
    </div>
    {items.map((item, i) => (
      <div key={i} style={{ marginBottom: 8, padding: '6px 8px',
        background: 'rgba(255,255,255,0.05)', borderRadius: 4, borderLeft: `3px solid ${color.border}` }}>
        <div style={{ fontWeight: 'bold', fontSize: 11, color: color.border }}>{item.label}</div>
        <div style={{ fontSize: 10, opacity: 0.85, marginTop: 2 }}>{item.detail}</div>
        {item.threshold && (
          <div style={{ fontSize: 9, color: '#ffd460', marginTop: 2 }}>⚡ {item.threshold}</div>
        )}
      </div>
    ))}
  </div>
);

const panels = {
  economy: {
    title: '💰 Economy Manager',
    items: [
      { label: 'GathererHuts (P8)', detail: 'Build if activeHuts < target', threshold: 'Target: 5 huts' },
      { label: 'Miners (P7)', detail: 'Train if miners < desired, max 3/cycle', threshold: 'Cap: 10 miners, 2 per mine' },
      { label: 'Barracks Request (P6)', detail: 'Request if barracks < target AND has miners', threshold: 'Target: 4 barracks' },
      { label: 'Choice Building (P4)', detail: 'Aggressive→Keep, Defensive→Shrine, Economic→Vault', threshold: 'Requires crystal ≥ threshold' },
      { label: 'Age-Up (P4)', detail: 'After choice building complete + can afford', threshold: 'Culture by personality' },
      { label: 'Culture Buildings (P5)', detail: 'Sequential build order per culture after Era 2', threshold: 'Runai/Alanthor/Feraldis specific' },
      { label: 'Vault Deposits', detail: 'Deposit surplus resources into vault', threshold: 'Surplus > 500, deposit 200/cycle' },
      { label: 'Smelter Staffing', detail: 'Assign idle miners to smelter', threshold: '2 miners per smelter' },
    ]
  },
  building: {
    title: '🏗️ Building Manager',
    items: [
      { label: 'Builder Count', detail: 'Target 3, +1 if queue>3, max 5', threshold: 'MaxBuilders: 5' },
      { label: 'Process Requests', detail: 'Priority queue → affordability → assign builder → construct', threshold: 'Deduct cost on start' },
      { label: 'Culture Queue', detail: '1 building per tick, duplicate-aware', threshold: 'Era 2+ required' },
    ]
  },
  military: {
    title: '⚔️ Military Manager',
    items: [
      { label: 'Recruitment Cycle', detail: 'Every 5s, request batch of units continuously', threshold: 'No cap — pop + resources throttle' },
      { label: 'Composition', detail: 'Aggressive: 60/30/10, Defensive: 30/50/20, Balanced: 40/40/20', threshold: 'Soldiers / Archers / Siege %' },
      { label: 'Population Housing', detail: 'Request hut when pop ≥ max - 2', threshold: 'MAX_HUTS: 10' },
      { label: 'Army Formation', detail: 'Form army when ≥3 unassigned units', threshold: 'MIN_ARMY_SIZE: 3, MAX: 16' },
      { label: 'Queue Limit', detail: 'Max 5 units per building queue', threshold: '3 of same type per request' },
    ]
  },
  scout: {
    title: '🔭 Scouting',
    items: [
      { label: 'Zone Grid', detail: '5×5 zones, 60u each, centered on Hall', threshold: 'Revisit every 120s' },
      { label: 'Scout Count', detail: 'Train up to 2 scouts', threshold: 'DESIRED_SCOUTS: 2' },
      { label: 'Enemy Detection', detail: 'All units scan within LineOfSight radius', threshold: 'Skip CrystalTag entities' },
      { label: 'Sighting Lifecycle', detail: 'Create → merge within 20u → expire after 30s', threshold: 'Bases get +50 priority' },
    ]
  },
  mission: {
    title: '🎯 Mission Manager',
    items: [
      { label: 'Defense (P10)', detail: 'Always active, one per faction', threshold: 'Strength ≥ 3' },
      { label: 'Attack (P6-8)', detail: 'When sightings exist AND strength ≥ threshold', threshold: 'Aggressive: 5, Others: 10' },
      { label: 'Blind Attack (P7)', detail: 'No sightings after 180s → attack nearest Hall', threshold: '3 min delay, strength ≥ 5' },
      { label: 'Raid (P4)', detail: 'Aggressive/Rush only, weak bases (str ≤ 30)', threshold: 'Max 1 raid per cycle' },
      { label: 'Expansion (P5)', detail: 'When economic strength > 1000', threshold: 'Strength ≥ 15' },
    ]
  },
  tactical: {
    title: '🗡️ Tactical Manager',
    items: [
      { label: 'Army Assignment', detail: 'Sort missions by priority, assign unattached armies', threshold: 'Tick: 1.0s' },
      { label: 'ATTACK Execute', detail: '>30u: march to target | ≤20u: engage enemies', threshold: 'Enter: 20u, Exit: 30u' },
      { label: 'DEFEND Execute', detail: '>15u: return to base | else: hold + engage threats in 30u', threshold: 'Hold: 12u, Return: 15u' },
      { label: 'RAID Execute', detail: 'March → engage → retreat if strength < 50% required', threshold: 'Retreat threshold: 50%' },
    ]
  },
  crystal: {
    title: '💎 Crystal Faction AI',
    items: [
      { label: 'Phases', detail: 'Phase 0: <5min, Phase 1: 5-15min, Phase 2: 15min+', threshold: 'Unlocks units + buildings' },
      { label: 'Training', detail: 'One unit at a time per node, random roll', threshold: 'P0: Crystalling only | P1: +Veilstinger | P2: +Godsplinter' },
      { label: 'Sub-Nodes', detail: 'Resource(2), Turret(2), Restoration(1), Enforcement(1), Suppression(1)', threshold: 'Max 7 per main node' },
      { label: 'Attack Waves', detail: 'Phase 0: 1 target, Phase 1: 2, Phase 2: all', threshold: 'Cost: 25 crystal/unit, interval: 25-120s' },
      { label: 'Expansion', detail: 'New main node at territory edge, logarithmic slowdown', threshold: 'Cost: 9000c, max 16 nodes' },
      { label: 'Territory Defense', detail: 'Send idle units to intercept intruders within spread radius', threshold: 'Automatic, every 5s tick' },
    ]
  },
  defense: {
    title: '🛡️ Defense Behavior',
    items: [
      { label: 'Threat Detection', detail: 'Enemy units within 50u of base', threshold: 'Check every 1.0s' },
      { label: 'EMERGENCY (<25u)', detail: 'Intercept at 30% toward threat, all available fighters', threshold: 'All non-economy, non-army units' },
      { label: 'STANDARD (25-50u)', detail: 'Rally at 25% toward threat, idle units only', threshold: 'Must be >10u from rally point' },
    ]
  },
  hunt: {
    title: '🎯 Crystal Defense (Hunt)',
    items: [
      { label: 'Detection', detail: 'CrystalTag units/buildings near base', threshold: 'Range: 35u from Hall' },
      { label: 'Hunter Assignment', detail: 'Idle melee/ranged, not in army, round-robin', threshold: 'Max N per target' },
      { label: 'Purpose', detail: 'Kill crystal waves near base → cadavers for crystal mining', threshold: 'Defensive farm strategy' },
    ]
  },
};

export default function AIDecisionFlow() {
  const [selected, setSelected] = useState(null);
  const W = 1100, H = 820;

  return (
    <div style={{ background: '#0d0d1a', minHeight: '100vh', padding: 20, fontFamily: 'monospace' }}>
      <h1 style={{ color: '#e94560', textAlign: 'center', margin: '0 0 10px', fontSize: 20 }}>
        The Waning Border — AI Decision Flow
      </h1>
      <p style={{ color: '#556', textAlign: 'center', margin: '0 0 20px', fontSize: 11 }}>
        Click any system node to see decision details, thresholds, and tuning parameters
      </p>

      <div style={{ display: 'flex', justifyContent: 'center' }}>
        <svg width={W} height={H} viewBox={`0 0 ${W} ${H}`}>
          <defs>
            <marker id="arrowhead" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto">
              <polygon points="0 0, 8 3, 0 6" fill="#556" />
            </marker>
            <filter id="glow">
              <feGaussianBlur stdDeviation="3" result="blur" />
              <feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge>
            </filter>
          </defs>

          {/* ── Background grid ── */}
          {Array.from({length: 22}, (_, i) => (
            <line key={`gx${i}`} x1={i*50} y1={0} x2={i*50} y2={H} stroke="#111122" strokeWidth={0.5} />
          ))}
          {Array.from({length: 17}, (_, i) => (
            <line key={`gy${i}`} x1={0} y1={i*50} x2={W} y2={i*50} stroke="#111122" strokeWidth={0.5} />
          ))}

          {/* ══════════════════════════════════════════════ */}
          {/* TIER 0: AI BRAIN (top center) */}
          {/* ══════════════════════════════════════════════ */}
          <Node x={440} y={15} w={220} h={45} color={colors.brain}
            label="AIBrain.Update()" sublabel="Per faction · Personality · Difficulty" />

          {/* Arrows from brain to tier 1 */}
          <Arrow x1={480} y1={60} x2={120} y2={95} />
          <Arrow x1={510} y1={60} x2={340} y2={95} />
          <Arrow x1={550} y1={60} x2={550} y2={95} />
          <Arrow x1={590} y1={60} x2={760} y2={95} />
          <Arrow x1={620} y1={60} x2={980} y2={95} />

          {/* ══════════════════════════════════════════════ */}
          {/* TIER 1: MANAGERS (row 1) */}
          {/* ══════════════════════════════════════════════ */}
          <Node x={30} y={95} w={180} h={40} color={colors.economy}
            label="Economy Manager" sublabel="Huts · Miners · Buildings"
            onClick={() => setSelected('economy')} highlight={selected === 'economy'} />

          <Node x={250} y={95} w={180} h={40} color={colors.building}
            label="Building Manager" sublabel="Builders · Construction"
            onClick={() => setSelected('building')} highlight={selected === 'building'} />

          <Node x={465} y={95} w={180} h={40} color={colors.military}
            label="Military Manager" sublabel="Recruit · Armies · Pop"
            onClick={() => setSelected('military')} highlight={selected === 'military'} />

          <Node x={680} y={95} w={160} h={40} color={colors.scout}
            label="Scouting" sublabel="Zones · Detection"
            onClick={() => setSelected('scout')} highlight={selected === 'scout'} />

          <Node x={880} y={95} w={190} h={40} color={colors.defense}
            label="Defense Behavior" sublabel="Threat · Rally · Emergency"
            onClick={() => setSelected('defense')} highlight={selected === 'defense'} />

          {/* ══════════════════════════════════════════════ */}
          {/* TIER 1.5: ECONOMY DECISIONS */}
          {/* ══════════════════════════════════════════════ */}
          <Arrow x1={120} y1={135} x2={120} y2={160} />

          {/* Economy decision chain */}
          <Diamond x={120} y={178} size={30} color={colors.decision} label="huts?" />
          <Arrow x1={120} y1={193} x2={120} y2={215} label="< target" />
          <Node x={55} y={215} w={130} h={28} color={colors.action}
            label="→ Request Hut" sublabel="Priority 8" />

          <Arrow x1={135} y1={178} x2={200} y2={178} label="✓" dashed />
          <Diamond x={230} y={178} size={30} color={colors.decision} label="miners?" />
          <Arrow x1={230} y1={193} x2={230} y2={215} label="< desired" />
          <Node x={170} y={250} w={120} h={28} color={colors.action}
            label="→ Train Miner" sublabel="Priority 7" />

          <Arrow x1={245} y1={178} x2={310} y2={178} label="✓" dashed />
          <Diamond x={340} y={178} size={30} color={colors.decision} label="choice?" />
          <Arrow x1={340} y1={193} x2={340} y2={250} label="no bldg" />
          <Node x={285} y={285} w={110} h={28} color={colors.action}
            label="→ Build Choice" sublabel="By personality" />

          {/* ══════════════════════════════════════════════ */}
          {/* TIER 2: MILITARY FLOW */}
          {/* ══════════════════════════════════════════════ */}
          <Arrow x1={555} y1={135} x2={555} y2={160} />

          <Diamond x={480} y={178} size={30} color={colors.decision} label="pop?" />
          <Arrow x1={480} y1={193} x2={480} y2={215} label="avail>0" />
          <Node x={420} y={215} w={120} h={28} color={colors.action}
            label="→ Train Units" sublabel="Continuous" />

          <Diamond x={600} y={178} size={30} color={colors.decision} label="army?" />
          <Arrow x1={600} y1={193} x2={600} y2={215} label="≥3 idle" />
          <Node x={545} y={250} w={120} h={28} color={colors.action}
            label="→ Form Army" sublabel="Max 16 units" />

          <Arrow x1={555} y1={160} x2={480} y2={163} />
          <Arrow x1={555} y1={160} x2={600} y2={163} />

          {/* Composition box */}
          <Node x={420} y={290} w={250} h={52} color={colors.military}
            label="Unit Composition (by personality)" sublabel="Aggr: 60/30/10 · Def: 30/50/20 · Bal: 40/40/20" />

          {/* ══════════════════════════════════════════════ */}
          {/* TIER 2: SCOUTING → MISSIONS */}
          {/* ══════════════════════════════════════════════ */}
          <Arrow x1={760} y1={135} x2={760} y2={160} />

          <Diamond x={720} y={178} size={30} color={colors.decision} label="enemy?" />
          <Arrow x1={720} y1={193} x2={720} y2={220} label="spotted" />
          <Node x={670} y={220} w={100} h={28} color={colors.check}
            label="Sighting" sublabel="str + pos" />

          <Diamond x={810} y={178} size={30} color={colors.decision} label="zones?" />
          <Arrow x1={810} y1={193} x2={810} y2={220} />
          <Node x={760} y={220} w={100} h={28} color={colors.action}
            label="→ Send Scout" sublabel="Least explored" />

          <Arrow x1={760} y1={160} x2={720} y2={163} />
          <Arrow x1={760} y1={160} x2={810} y2={163} />

          {/* ══════════════════════════════════════════════ */}
          {/* TIER 3: MISSION MANAGER */}
          {/* ══════════════════════════════════════════════ */}
          <Arrow x1={720} y1={248} x2={550} y2={380} />

          <Node x={380} y={370} w={340} h={45} color={colors.mission}
            label="Mission Manager (4s tick)" sublabel="Sightings + Army Strength → Create Missions"
            onClick={() => setSelected('mission')} highlight={selected === 'mission'} />

          {/* Mission types */}
          <Arrow x1={420} y1={415} x2={100} y2={445} />
          <Arrow x1={470} y1={415} x2={280} y2={445} />
          <Arrow x1={550} y1={415} x2={460} y2={445} />
          <Arrow x1={620} y1={415} x2={640} y2={445} />
          <Arrow x1={680} y1={415} x2={820} y2={445} />

          <Node x={30} y={445} w={140} h={35} color={colors.mission}
            label="DEFEND" sublabel="P10 · str≥3 · always" />
          <Node x={210} y={445} w={140} h={35} color={colors.mission}
            label="ATTACK" sublabel="P6-8 · sightings · str≥5" />
          <Node x={390} y={445} w={140} h={35} color={colors.mission}
            label="BLIND ATK" sublabel="P7 · no sight · 180s" />
          <Node x={570} y={445} w={140} h={35} color={colors.mission}
            label="RAID" sublabel="P4 · aggro only · weak" />
          <Node x={750} y={445} w={140} h={35} color={colors.mission}
            label="EXPAND" sublabel="P5 · econ>1000" />

          {/* ══════════════════════════════════════════════ */}
          {/* TIER 4: TACTICAL EXECUTION */}
          {/* ══════════════════════════════════════════════ */}
          <Arrow x1={280} y1={480} x2={400} y2={520} />
          <Arrow x1={460} y1={480} x2={500} y2={520} />
          <Arrow x1={640} y1={480} x2={600} y2={520} />

          <Node x={350} y={520} w={310} h={45} color={colors.tactical}
            label="Tactical Manager (1s tick)" sublabel="Assign armies → Execute missions → Engage"
            onClick={() => setSelected('tactical')} highlight={selected === 'tactical'} />

          <Arrow x1={505} y1={565} x2={350} y2={595} />
          <Arrow x1={505} y1={565} x2={550} y2={595} />

          <Node x={270} y={595} w={160} h={35} color={colors.tactical}
            label="March to Target" sublabel=">30u from objective" />
          <Node x={470} y={595} w={160} h={35} color={colors.tactical}
            label="ENGAGE!" sublabel="≤20u · find + attack" />

          {/* ══════════════════════════════════════════════ */}
          {/* CRYSTAL FACTION (right column, independent) */}
          {/* ══════════════════════════════════════════════ */}
          <Node x={880} y={180} w={190} h={45} color={colors.crystal}
            label="Crystal AI (5s tick)" sublabel="Independent · Phase-based"
            onClick={() => setSelected('crystal')} highlight={selected === 'crystal'} />

          <Arrow x1={975} y1={225} x2={900} y2={260} />
          <Arrow x1={975} y1={225} x2={975} y2={260} />
          <Arrow x1={975} y1={225} x2={1050} y2={260} />

          <Node x={850} y={260} w={100} h={30} color={colors.crystal}
            label="Train Unit" sublabel="Per node" />
          <Node x={925} y={300} w={100} h={30} color={colors.crystal}
            label="Build Sub" sublabel="15s cycle" />
          <Node x={1000} y={260} w={100} h={30} color={colors.crystal}
            label="Send Wave" sublabel="25-120s" />

          <Arrow x1={975} y1={330} x2={975} y2={350} />

          <Diamond x={975} y={370} size={35} color={colors.decision} label="phase?" />

          <Arrow x1={940} y1={370} x2={870} y2={370} />
          <Node x={820} y={355} w={55} h={30} color={colors.crystal} label="P0" sublabel="<5m" />

          <Arrow x1={975} y1={388} x2={975} y2={415} />
          <Node x={948} y={415} w={55} h={30} color={colors.crystal} label="P1" sublabel="5-15m" />

          <Arrow x1={1010} y1={370} x2={1060} y2={370} />
          <Node x={1035} y={355} w={55} h={30} color={colors.crystal} label="P2" sublabel=">15m" />

          {/* Crystal wave detail */}
          <Arrow x1={1050} y1={290} x2={1050} y2={450} />
          <Node x={920} y={450} w={170} h={35} color={colors.crystal}
            label="Wave Targets" sublabel="P0→1 hall · P1→2 · P2→all" />
          <Arrow x1={1005} y1={485} x2={1005} y2={510} />
          <Node x={920} y={510} w={170} h={35} color={colors.crystal}
            label="Deploy & Charge" sublabel="25c/unit · min 3 units" />

          {/* ══════════════════════════════════════════════ */}
          {/* CRYSTAL HUNT (bottom left) */}
          {/* ══════════════════════════════════════════════ */}
          <Node x={30} y={520} w={200} h={40} color={colors.hunt}
            label="Crystal Hunt (Defense)" sublabel="35u range · idle fighters → cadavers"
            onClick={() => setSelected('hunt')} highlight={selected === 'hunt'} />

          <Arrow x1={130} y1={560} x2={130} y2={590} />
          <Node x={50} y={590} w={160} h={28} color={colors.action}
            label="Kill → Cadaver → Mine" sublabel="Crystal income loop" />

          {/* ══════════════════════════════════════════════ */}
          {/* LEGEND */}
          {/* ══════════════════════════════════════════════ */}
          <rect x={30} y={660} width={340} height={140} rx={6} fill="#0a0a15" stroke="#222" />
          <text x={45} y={680} fill="#889" fontSize={10} fontWeight="bold" fontFamily="monospace">LEGEND</text>

          <rect x={45} y={690} width={12} height={12} fill={colors.decision.bg} stroke={colors.decision.border} />
          <text x={65} y={700} fill="#889" fontSize={9} fontFamily="monospace">Decision point (diamond = condition check)</text>

          <rect x={45} y={710} width={12} height={12} fill={colors.action.bg} stroke={colors.action.border} />
          <text x={65} y={720} fill="#889" fontSize={9} fontFamily="monospace">Action output (command issued)</text>

          <rect x={45} y={730} width={12} height={12} fill={colors.military.bg} stroke={colors.military.border} />
          <text x={65} y={740} fill="#889" fontSize={9} fontFamily="monospace">System node (click for details)</text>

          <rect x={45} y={750} width={12} height={12} fill={colors.crystal.bg} stroke={colors.crystal.border} />
          <text x={65} y={760} fill="#889" fontSize={9} fontFamily="monospace">Crystal faction (independent AI)</text>

          <text x={45} y={785} fill="#556" fontSize={9} fontFamily="monospace">
            All intervals, thresholds, and costs shown. Click nodes for full detail.
          </text>

          {/* ══════════════════════════════════════════════ */}
          {/* UPDATE RATES BOX */}
          {/* ══════════════════════════════════════════════ */}
          <rect x={400} y={660} width={300} height={140} rx={6} fill="#0a0a15" stroke="#222" />
          <text x={415} y={680} fill="#889" fontSize={10} fontWeight="bold" fontFamily="monospace">TICK RATES</text>

          {[
            ['Economy/Building', 'per brain interval'],
            ['Military Recruit', '5.0s'],
            ['Scouting', '2.0s'],
            ['Mission Creation', '4.0s'],
            ['Tactical Execution', '1.0s'],
            ['Defense Check', '1.0s'],
            ['Crystal AI', '5.0s'],
            ['Crystal Waves', '25-120s (dynamic)'],
          ].map(([label, rate], i) => (
            <g key={i}>
              <text x={415} y={698 + i * 15} fill="#667" fontSize={9} fontFamily="monospace">{label}</text>
              <text x={600} y={698 + i * 15} fill="#ffd460" fontSize={9} fontFamily="monospace" textAnchor="end">{rate}</text>
            </g>
          ))}

          {/* ══════════════════════════════════════════════ */}
          {/* GAME FLOW ARROW (left side) */}
          {/* ══════════════════════════════════════════════ */}
          <text x={12} y={400} fill="#333" fontSize={10} fontFamily="monospace"
            transform="rotate(-90, 12, 400)" textAnchor="middle">
            TIME →  Early Game → Mid Game → Late Game
          </text>
        </svg>
      </div>

      {selected && panels[selected] && (
        <DetailPanel
          title={panels[selected].title}
          items={panels[selected].items}
          color={colors[selected]}
          onClose={() => setSelected(null)}
        />
      )}
    </div>
  );
}
