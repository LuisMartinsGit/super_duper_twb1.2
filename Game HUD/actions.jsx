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
const BUILDINGS = [
  { key: 'bastion',    name: 'Bastion',     hint: 'Fortified stone work, garrisons archers.',  cost: { res: 'Iron',      n: 120 }, time: 38, glyph: 'castle',    hotkey: 'B', tone: 'build' },
  { key: 'barracks',   name: 'Barracks',    hint: 'Trains soldiery and heavy infantry.',       cost: { res: 'Iron',      n: 90  }, time: 30, glyph: 'crossed',   hotkey: 'R', tone: 'build' },
  { key: 'forge',      name: 'Forge',       hint: 'Smelts arms and armour upgrades.',          cost: { res: 'Iron',      n: 150 }, time: 42, glyph: 'anvil',     hotkey: 'F', tone: 'build' },
  { key: 'granary',    name: 'Granary',     hint: 'Stores supplies; raises population cap.',   cost: { res: 'Supplies',  n: 80  }, time: 24, glyph: 'sheaf',     hotkey: 'G', tone: 'build' },
  { key: 'sanctum',    name: 'Sanctum',     hint: 'Channels veilstone, unlocks ritual magic.', cost: { res: 'Veilstone', n: 60  }, time: 50, glyph: 'sigil',     hotkey: 'V', tone: 'build' },
  { key: 'watchtower', name: 'Watchtower',  hint: 'Reveals far ground; light defence.',        cost: { res: 'Iron',      n: 70  }, time: 22, glyph: 'eye',       hotkey: 'W', tone: 'build' },
  { key: 'stables',    name: 'Stables',     hint: 'Trains horsemen and outriders.',            cost: { res: 'Iron',      n: 110 }, time: 36, glyph: 'hooves',    hotkey: 'S', tone: 'build' },
  { key: 'market',     name: 'Market',      hint: 'Trade routes; converts surplus to coin.',   cost: { res: 'Supplies',  n: 130 }, time: 34, glyph: 'scale',     hotkey: 'M', tone: 'build' },
  { key: 'spire',      name: 'Mage Spire',  hint: 'Advanced veil research and wardings.',      cost: { res: 'Veilstone', n: 120 }, time: 60, glyph: 'spire',     hotkey: 'T', tone: 'build' },
];

const TRAIN_UNITS = [
  { key: 'spearman', name: 'Spearman',   hint: 'Levied pikemen \u00b7 anti-cavalry.',         cost: { res: 'Iron',     n: 50  }, time: 12, glyph: 'spear',    hotkey: 'Q', tone: 'train' },
  { key: 'archer',   name: 'Glassbow',   hint: 'Ranged \u00b7 fragile, fast.',                 cost: { res: 'Iron',     n: 60  }, time: 14, glyph: 'bow',      hotkey: 'W', tone: 'train' },
  { key: 'knight',   name: 'Veil-Knight',hint: 'Heavy infantry \u00b7 plate \u00b7 standard.',cost: { res: 'Iron',     n: 110 }, time: 24, glyph: 'helm',     hotkey: 'E', tone: 'train' },
  { key: 'mason',    name: 'Mason-Adept',hint: 'Builder \u00b7 raises structures.',           cost: { res: 'Supplies', n: 70  }, time: 10, glyph: 'mason',    hotkey: 'A', tone: 'train' },
];

const RESEARCH = [
  { key: 'plating',  name: 'Plating',    hint: '+15% armour for infantry.',                  cost: { res: 'Iron',      n: 140 }, time: 30, glyph: 'shield',   hotkey: 'Z', tone: 'research' },
  { key: 'fletching',name: 'Fletching',  hint: '+1 range, +10% damage for archers.',         cost: { res: 'Iron',      n: 120 }, time: 28, glyph: 'arrow',    hotkey: 'X', tone: 'research' },
  { key: 'wards',    name: 'Wardings',   hint: 'Buildings resist veil-curses.',              cost: { res: 'Veilstone', n: 90  }, time: 40, glyph: 'ward',     hotkey: 'C', tone: 'research' },
  { key: 'logistics',name: 'Logistics',  hint: 'Production queues run 20% faster.',          cost: { res: 'Supplies',  n: 160 }, time: 36, glyph: 'gear',     hotkey: 'V', tone: 'research' },
];

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

function ActionCell({ theme, item, kind, onClick, size = 'lg' }) {
  const muted = item.muted === true;
  const t = toneOf(item.tone);
  const kickerByTone = {
    build:    'Construction',
    train:    'Train Unit',
    research: 'Research',
    ability:  'Command',
  };
  return (
    <button
      type="button"
      className={`act-cell act-cell-${size} ${muted ? 'muted' : ''}`}
      onClick={() => !muted && onClick && onClick(item.key)}
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
      aria-label={item.name}
      aria-disabled={muted ? 'true' : undefined}
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
          {kickerByTone[item.tone] || 'Action'}
        </span>
        {item.cost && (
          <span className="act-tooltip-meta">
            <span className="act-tooltip-cost">
              <span className="act-tooltip-cost-dot" style={{ background: t.color, boxShadow: `0 0 4px ${t.color}` }} />
              <span style={{ color: theme.text }}>{item.cost.n}</span>
              <span style={{ color: theme.textDim }}> {item.cost.res}</span>
            </span>
            <span className="act-tooltip-dot" style={{ background: theme.inlay }} />
            <span className="act-tooltip-time" style={{ color: theme.textDim }}>
              <svg width="9" height="9" viewBox="0 0 9 9" style={{ marginRight: 3, verticalAlign: '-1px' }}>
                <circle cx="4.5" cy="4.5" r="3.6" fill="none" stroke="currentColor" strokeWidth="0.9" />
                <line x1="4.5" y1="4.5" x2="4.5" y2="2.2" stroke="currentColor" strokeWidth="0.9" strokeLinecap="round" />
                <line x1="4.5" y1="4.5" x2="6.3" y2="5.7" stroke="currentColor" strokeWidth="0.9" strokeLinecap="round" />
              </svg>
              {item.time}s
            </span>
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

function ActionsGrid({ theme, selectionKey, onAction }) {
  if (selectionKey === 'builder') {
    return (
      <div className="act-grid act-grid-3x3">
        {BUILDINGS.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="build" onClick={onAction} size="md" />
        ))}
      </div>
    );
  }
  if (selectionKey === 'building') {
    return (
      <div className="act-zones">
        <ActionZone theme={theme} label="Train Units" tone="train"    items={TRAIN_UNITS} onAction={onAction} />
        <ActionZone theme={theme} label="Research"    tone="research" items={RESEARCH}    onAction={onAction} />
      </div>
    );
  }
  if (selectionKey === 'military') {
    return (
      <div className="act-grid act-grid-3x2">
        {MILITARY_CMDS.map((it) => (
          <ActionCell key={it.key} theme={theme} item={it} kind="ability" onClick={onAction} size="lg" />
        ))}
      </div>
    );
  }
  if (selectionKey === 'multi') {
    return (
      <div className="act-grid act-grid-3x2">
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
function ActionsPanel({ theme, selection, onAction }) {
  const lvl = theme.ornament;
  const hasSelection = !!selection;
  const label =
    selection === 'builder'  ? 'Construct' :
    selection === 'building' ? 'Operate'   :
    selection === 'multi'    ? 'Group Orders' :
    selection === 'military' ? 'Orders'   :
    selection === 'enemy'    ? 'Target'   :
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
            <ActionsGrid theme={theme} selectionKey={selection} onAction={onAction} />
          </div>
        )}
      </div>
    </div>
  );
}

Object.assign(window, { ActionsGrid, ActionsPanel, ActionGlyph, BUILDINGS, TRAIN_UNITS, RESEARCH, toneOf, TONES });
