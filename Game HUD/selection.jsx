// selection.jsx — Selection panel matched to the resource panel's frame.
// Renders fields based on which selection-tester button is active.
// Layout (same 260×240 box as the resource panel):
//   ┌──────────────────────────────┐
//   │ ICON   Name                  │
//   │        Class · Tier          │
//   │ ─── Health   ────────────    │
//   │ ─── Shield   ────────────    │
//   │ ─── Cooldown ──── (own only) │
//   │ ATK · DEF · SPD (3 columns)  │
//   │              [Upgrade]       │
//   └──────────────────────────────┘
// For "multi" we swap to a 3-row portrait grid summary instead.

// ── Mock data per selection key ──────────────────────────────────────────
const SELECTIONS = {
  military: {
    name: 'Veil-Knight',
    klass: 'Heavy Infantry · Tier II',
    portrait: 'knight',
    portraitTone: 'own',
    hp: 162, hpMax: 180,
    sh: 28,  shMax: 40,
    cd: 0.35, cdLabel: 'Wardstrike',
    atk: { kind: 'Slash', value: 24 },
    def: { kind: 'Plate', value: 18 },
    spd: { kind: 'Stride', value: 3.4 },
    canUpgrade: true,
    upgradeCost: '60 Iron',
  },
  builder: {
    name: 'Mason-Adept',
    klass: 'Builder · Civilian',
    portrait: 'mason',
    portraitTone: 'own',
    hp: 70, hpMax: 80,
    sh: 12, shMax: 20,
    cd: 0.0, cdLabel: 'Quickwright',
    atk: { kind: 'Hammer', value: 6 },
    def: { kind: 'Apron',  value: 4 },
    spd: { kind: 'Stride', value: 2.8 },
    canUpgrade: true,
    upgradeCost: '40 Supplies',
  },
  enemy: {
    name: 'Hollow Behemoth',
    klass: 'Curse Spawn · Elite',
    portrait: 'behemoth',
    portraitTone: 'enemy',
    hp: 410, hpMax: 540,
    sh: 60,  shMax: 120,
    cd: null,
    atk: { kind: 'Crush', value: 46 },
    def: { kind: 'Hide',  value: 22 },
    spd: { kind: 'Lumber', value: 1.9 },
    canUpgrade: false,
  },
  building: {
    name: 'Veil-Forge',
    klass: 'Structure · Workshop',
    portrait: 'forge',
    portraitTone: 'own',
    hp: 1240, hpMax: 1600,
    sh: 0,    shMax: 200,
    cd: 0.62, cdLabel: 'Tempering',
    atk: { kind: 'None', value: 0 },
    def: { kind: 'Stone', value: 64 },
    spd: { kind: 'Fixed', value: 0 },
    canUpgrade: true,
    upgradeCost: '180 Iron',
  },
};

// "multi" — three sample portraits in a compact grid.
const MULTI = [
  { key: 'k1', portrait: 'knight',  tone: 'own', name: 'Veil-Knight',  count: 6, hp: 0.86 },
  { key: 'a1', portrait: 'archer',  tone: 'own', name: 'Glassbow',     count: 4, hp: 0.71 },
  { key: 'm1', portrait: 'mason',   tone: 'own', name: 'Mason-Adept',  count: 2, hp: 1.0  },
];

// ── Portrait drawings (placeholder silhouettes) ──────────────────────────
function PortraitIcon({ kind, fill, stroke }) {
  switch (kind) {
    case 'knight':
      // Helmet silhouette
      return (
        <g>
          <path d="M 6 14 Q 6 4 16 4 Q 26 4 26 14 L 26 22 Q 26 26 22 26 L 10 26 Q 6 26 6 22 Z"
                fill={fill} stroke={stroke} strokeWidth="0.8" />
          <rect x="10" y="11" width="12" height="3" fill={stroke} />
          <line x1="14" y1="14" x2="14" y2="22" stroke={stroke} strokeWidth="0.6" opacity="0.7" />
          <line x1="18" y1="14" x2="18" y2="22" stroke={stroke} strokeWidth="0.6" opacity="0.7" />
          <circle cx="16" cy="4" r="2" fill={fill} stroke={stroke} strokeWidth="0.8" />
        </g>
      );
    case 'archer':
      return (
        <g>
          <circle cx="16" cy="10" r="4" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <path d="M 8 28 Q 8 16 16 16 Q 24 16 24 28 Z" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <path d="M 22 6 Q 28 14 22 22" fill="none" stroke={stroke} strokeWidth="1" />
          <line x1="22" y1="6" x2="22" y2="22" stroke={stroke} strokeWidth="0.6" />
        </g>
      );
    case 'mason':
      // Robed figure with hammer
      return (
        <g>
          <circle cx="13" cy="10" r="4" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <path d="M 6 28 Q 6 16 13 16 Q 20 16 20 28 Z" fill={fill} stroke={stroke} strokeWidth="0.8" />
          {/* hammer */}
          <line x1="22" y1="8" x2="22" y2="20" stroke={stroke} strokeWidth="1.3" strokeLinecap="round" />
          <rect x="19" y="6" width="6" height="4" fill={stroke} />
        </g>
      );
    case 'forge':
      // Anvil silhouette
      return (
        <g>
          <rect x="6" y="18" width="20" height="6" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <rect x="11" y="22" width="10" height="4" fill={stroke} />
          <path d="M 8 18 Q 16 8 24 18 Z" fill={fill} stroke={stroke} strokeWidth="0.8" />
          {/* sparks */}
          <circle cx="22" cy="10" r="0.9" fill={stroke} />
          <circle cx="10" cy="12" r="0.7" fill={stroke} opacity="0.6" />
        </g>
      );
    case 'behemoth':
      // Hulking horned silhouette
      return (
        <g>
          <path d="M 4 28 Q 4 14 10 12 L 10 8 L 14 4 L 14 10 L 18 10 L 18 4 L 22 8 L 22 12 Q 28 14 28 28 Z"
                fill={fill} stroke={stroke} strokeWidth="0.8" strokeLinejoin="round" />
          {/* eyes */}
          <circle cx="13" cy="16" r="1.2" fill={stroke} />
          <circle cx="19" cy="16" r="1.2" fill={stroke} />
        </g>
      );
    default:
      return <rect x="4" y="4" width="24" height="24" fill={fill} stroke={stroke} strokeWidth="0.8" />;
  }
}

// Bar with diamond cap markers.
function StatBar({ label, value, max, color, dim, accent, glow = false, sub }) {
  const pct = Math.max(0, Math.min(1, max > 0 ? value / max : 0));
  return (
    <div className="sel-bar">
      <div className="sel-bar-label">
        <span className="sel-bar-name" style={{ color: dim }}>{label}</span>
        <span className="sel-bar-val" style={{ color: pct > 0.6 ? color : accent }}>
          {sub != null ? sub : `${Math.round(value)} / ${max}`}
        </span>
      </div>
      <div className="sel-bar-track" style={{ background: 'rgba(0,0,0,0.55)', boxShadow: `inset 0 0 0 1px ${dim}55` }}>
        <div className="sel-bar-fill" style={{
          width: `${pct * 100}%`,
          background: `linear-gradient(90deg, ${color}aa, ${color})`,
          boxShadow: glow ? `0 0 6px ${color}` : 'none',
        }} />
        {/* notch ticks every 25% */}
        <div className="sel-bar-ticks" aria-hidden>
          {[0.25, 0.5, 0.75].map((p) => (
            <span key={p} style={{ left: `${p * 100}%`, background: 'rgba(0,0,0,0.6)' }} />
          ))}
        </div>
      </div>
    </div>
  );
}

// Stat cell — used in the ATK / DEF / SPD row.
function StatCell({ glyph, value, kind, theme }) {
  return (
    <div className="sel-stat">
      <div className="sel-stat-glyph" style={{ color: theme.accent, filter: `drop-shadow(0 0 4px ${theme.accent}33)` }}>
        <StatGlyph kind={glyph} color={theme.accent} dim={theme.inlay} />
      </div>
      <div className="sel-stat-text">
        <div className="sel-stat-value" style={{ color: theme.text }}>{value}</div>
        <div className="sel-stat-kind" style={{ color: theme.textDim }}>{kind}</div>
      </div>
    </div>
  );
}

function StatGlyph({ kind, color, dim }) {
  switch (kind) {
    case 'attack':
      return (
        <svg width="18" height="18" viewBox="0 0 18 18">
          <g stroke={color} strokeWidth="1.3" strokeLinecap="round" fill={color}>
            <line x1="9" y1="2" x2="9" y2="13" />
            <line x1="6" y1="13" x2="12" y2="13" strokeWidth="1.6" />
            <line x1="9" y1="13" x2="9" y2="15.5" />
            <polygon points="9,1 10,3 9,4 8,3" stroke="none" />
          </g>
        </svg>
      );
    case 'defense':
      return (
        <svg width="18" height="18" viewBox="0 0 18 18">
          <path d="M 9 2 L 15 4 L 15 9 Q 15 14 9 16 Q 3 14 3 9 L 3 4 Z"
                fill="none" stroke={color} strokeWidth="1.3" strokeLinejoin="round" />
          <line x1="9" y1="3" x2="9" y2="15" stroke={dim} strokeWidth="0.7" />
        </svg>
      );
    case 'speed':
      return (
        <svg width="18" height="18" viewBox="0 0 18 18">
          <g stroke={color} strokeWidth="1.4" strokeLinecap="round" fill="none">
            <polyline points="3,4 9,9 3,14" />
            <polyline points="9,4 15,9 9,14" />
          </g>
        </svg>
      );
    default: return null;
  }
}

// ── Main selection panel ─────────────────────────────────────────────────
function SelectionPanel({ theme, selection, onClose, onUpgrade }) {
  const lvl = theme.ornament;
  const hasSelection = !!selection;
  const sel = hasSelection && selection !== 'multi' ? SELECTIONS[selection] : null;

  return (
    <div className="rc-root rc-v sel-panel" style={{
      '--rc-base': theme.base,
      '--rc-mid': theme.baseMid,
      '--rc-edge': theme.baseEdge,
      '--rc-inlay': theme.inlay,
      '--rc-inlay-shadow': theme.inlayShadow,
      '--rc-accent': theme.accent,
    }}>
      <div className="rc-frame sel-frame">
        <div className="rc-plate" />
        <div className="rc-inlay" />
        <div className="rc-corner rc-corner-tl">
          <FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
        </div>
        <div className="rc-corner rc-corner-tr">
          <FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
        </div>
        <div className="rc-corner rc-corner-bl">
          <FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
        </div>
        <div className="rc-corner rc-corner-br">
          <FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
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

        {!hasSelection && <SelectionEmpty theme={theme} />}
        {hasSelection && (
          <div className="sel-body">
            <SelectionHeader theme={theme} selection={selection} sel={sel} />
            {selection === 'multi' && <SelectionMultiBody theme={theme} />}
            {sel && <SelectionStatusBody theme={theme} sel={sel} selectionKey={selection} onUpgrade={onUpgrade} />}
          </div>
        )}
      </div>
    </div>
  );
}

function SelectionEmpty({ theme }) {
  return (
    <div className="sel-empty">
      <div className="sel-empty-icon" style={{ color: theme.inlayDim, opacity: 0.6 }}>
        <svg width="36" height="36" viewBox="0 0 36 36">
          <polygon points="18,2 34,18 18,34 2,18" fill="none" stroke="currentColor" strokeWidth="1.2" />
          <polygon points="18,8 28,18 18,28 8,18" fill="none" stroke="currentColor" strokeWidth="0.8" opacity="0.6" />
          <circle cx="18" cy="18" r="2" fill="currentColor" opacity="0.7" />
        </svg>
      </div>
      <div className="sel-empty-title" style={{ color: theme.textDim }}>No Selection</div>
      <div className="sel-empty-hint" style={{ color: theme.textDim, opacity: 0.65 }}>
        Choose a target from the Selection Stage
      </div>
    </div>
  );
}

function SelectionHeader({ theme, selection, sel }) {
  if (selection === 'multi') {
    const totalUnits = MULTI.reduce((a, b) => a + b.count, 0);
    return (
      <div className="sel-header sel-multi-header">
        <div className="sel-multi-count" style={{ color: theme.accent }}>
          {totalUnits}
        </div>
        <div className="sel-header-text">
          <div className="sel-name" style={{ color: theme.accent }}>Mixed Detachment</div>
          <div className="sel-class" style={{ color: theme.textDim }}>{MULTI.length} unit types</div>
        </div>
      </div>
    );
  }
  if (!sel) return null;
  const isEnemy = sel.portraitTone === 'enemy';
  const factionColor = isEnemy ? '#e34a4a' : '#4cb5e6';
  return (
    <div className="sel-header">
      <div className="sel-portrait" style={{
        background: `radial-gradient(ellipse at 50% 30%, ${theme.baseMid}, ${theme.baseEdge})`,
        boxShadow: `inset 0 0 0 1px ${theme.inlayShadow}, 0 0 0 1px ${theme.inlay}66, 0 0 12px ${factionColor}33`,
      }}>
        <svg width="32" height="32" viewBox="0 0 32 32">
          <PortraitIcon kind={sel.portrait} fill={isEnemy ? '#1a0606' : '#06121b'} stroke={factionColor} />
        </svg>
        <span className="sel-portrait-faction" style={{ background: factionColor, boxShadow: `0 0 4px ${factionColor}` }} />
      </div>
      <div className="sel-header-text">
        <div className="sel-name" style={{ color: theme.accent }}>{sel.name}</div>
        <div className="sel-class" style={{ color: theme.textDim }}>{sel.klass}</div>
      </div>
    </div>
  );
}

function SelectionStatusBody({ theme, sel, selectionKey, onUpgrade }) {
  const isEnemy = sel.portraitTone === 'enemy';
  const hpColor = isEnemy ? '#e34a4a' : '#6fdb86';
  const shieldColor = '#7cc8e0';
  const cdColor = theme.accent;
  const showCD = sel.cd != null;
  const showUpgrade = !!sel.canUpgrade;
  const isBuilding = selectionKey === 'building';

  return (
    <div className="sel-tab-body">
      {/* Bars */}
      <div className="sel-bars">
        <StatBar label="Health" value={sel.hp} max={sel.hpMax}
                 color={hpColor} dim={theme.inlay} accent={theme.accent} />
        <StatBar label="Shield" value={sel.sh} max={sel.shMax}
                 color={shieldColor} dim={theme.inlay} accent={theme.accent} />
        {showCD && (
          <StatBar label={`Cooldown · ${sel.cdLabel}`} value={sel.cd * 100} max={100}
                   color={cdColor} dim={theme.inlay} accent={theme.accent} glow
                   sub={sel.cd === 0 ? 'Ready' : `${Math.round((1 - sel.cd) * 100)}%`} />
        )}
      </div>

      {/* For buildings: production queue. Otherwise: ATK/DEF/SPD stats row. */}
      {isBuilding ? (
        <BuildingQueue theme={theme} />
      ) : (
        <div className="sel-stats">
          <StatCell glyph="attack"  value={sel.atk.value} kind={sel.atk.kind} theme={theme} />
          <div className="sel-stat-div" style={{ background: `linear-gradient(180deg, transparent, ${theme.inlay}88, transparent)` }} />
          <StatCell glyph="defense" value={sel.def.value} kind={sel.def.kind} theme={theme} />
          <div className="sel-stat-div" style={{ background: `linear-gradient(180deg, transparent, ${theme.inlay}88, transparent)` }} />
          <StatCell glyph="speed"   value={sel.spd.value} kind={sel.spd.kind} theme={theme} />
        </div>
      )}

      {/* Upgrade */}
      {showUpgrade && (
        <button
          type="button"
          className="sel-upgrade"
          onClick={() => onUpgrade && onUpgrade(selectionKey)}
          style={{
            '--ub-base': theme.base,
            '--ub-edge': theme.baseEdge,
            '--ub-mid': theme.baseMid,
            '--ub-inlay': theme.inlay,
            '--ub-inlay-shadow': theme.inlayShadow,
            '--ub-accent': theme.accent,
            '--ub-text': theme.text,
            '--ub-dim': theme.textDim,
          }}
        >
          <span className="sel-upgrade-chev">
            <svg width="10" height="10" viewBox="0 0 10 10">
              <polyline points="2,7 5,3 8,7" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </span>
          <span className="sel-upgrade-label">Ascend</span>
          <span className="sel-upgrade-cost">{sel.upgradeCost}</span>
        </button>
      )}
    </div>
  );
}

// Mock production queue for the Forge — mixes train and research items.
const QUEUE_SAMPLE = [
  { kind: 'train',    key: 'knight',   glyph: 'helm',   progress: 0.42 },
  { kind: 'train',    key: 'spearman', glyph: 'spear' },
  { kind: 'research', key: 'plating',  glyph: 'shield' },
  { kind: 'train',    key: 'archer',   glyph: 'bow' },
  { kind: null },
];

function BuildingQueue({ theme }) {
  // ActionGlyph + toneOf live on window from actions.jsx.
  const Glyph = window.ActionGlyph || (() => null);
  const tones = window.TONES || { train: { color: theme.accent }, research: { color: '#a878e8' } };
  return (
    <div className="sel-queue">
      <div className="sel-queue-head">
        <span className="sel-queue-eyebrow" style={{ color: theme.textDim }}>Production Queue</span>
        <span className="sel-queue-rule" style={{
          background: `linear-gradient(90deg, transparent, ${theme.inlay}66, transparent)`,
        }} />
      </div>
      <div className="sel-queue-row">
        {QUEUE_SAMPLE.map((q, i) => {
          if (!q.kind) {
            return (
              <div key={i} className="sel-queue-slot sel-queue-slot-empty"
                   style={{ borderColor: theme.inlayShadow }}>
                <span className="sel-queue-slot-dot" style={{ background: theme.inlay, opacity: 0.4 }} />
              </div>
            );
          }
          const tone = tones[q.kind] || { color: theme.accent };
          const active = i === 0 && q.progress != null;
          return (
            <div key={i} className={`sel-queue-slot ${active ? 'active' : ''}`}
                 style={{
                   borderColor: active ? tone.color : tone.color + '55',
                   boxShadow: active ? `0 0 0 1px ${tone.color}55, 0 0 8px ${tone.color}33` : 'none',
                   background: `linear-gradient(180deg, ${theme.base}, ${theme.baseEdge})`,
                 }}>
              <svg width="20" height="20" viewBox="-12 -12 24 24">
                <Glyph kind={q.glyph} color={tone.color} dim={theme.inlay} />
              </svg>
              {active && (
                <div className="sel-queue-progress">
                  <div style={{
                    width: `${q.progress * 100}%`,
                    background: tone.color,
                    boxShadow: `0 0 4px ${tone.color}`,
                  }} />
                </div>
              )}
              {!active && (
                <span className="sel-queue-tag" style={{
                  background: tone.color,
                  boxShadow: `0 0 4px ${tone.color}`,
                }} />
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function SelectionMultiBody({ theme }) {
  return (
    <div className="sel-tab-body sel-multi">
      <div className="sel-multi-grid">
        {MULTI.map((u) => (
          <div key={u.key} className="sel-multi-card" style={{
            background: `linear-gradient(180deg, ${theme.base}, ${theme.baseEdge})`,
            boxShadow: `inset 0 0 0 1px ${theme.inlayShadow}, 0 0 0 1px ${theme.inlay}55`,
          }}>
            <div className="sel-multi-portrait" style={{ background: 'rgba(0,0,0,0.4)' }}>
              <svg width="22" height="22" viewBox="0 0 32 32">
                <PortraitIcon kind={u.portrait} fill="#06121b" stroke="#4cb5e6" />
              </svg>
            </div>
            <div className="sel-multi-meta">
              <div className="sel-multi-name" style={{ color: theme.text }}>{u.name}</div>
              <div className="sel-multi-bar" style={{ background: 'rgba(0,0,0,0.5)' }}>
                <div style={{
                  width: `${u.hp * 100}%`,
                  height: '100%',
                  background: `linear-gradient(90deg, #6fdb86aa, #6fdb86)`,
                  boxShadow: `0 0 4px #6fdb86`,
                }} />
              </div>
            </div>
            <div className="sel-multi-tag" style={{ color: theme.accent }}>×{u.count}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

// Legacy wrappers — kept so any external caller is not broken. They render
// the same fully-formed body the new tab system uses.
function SelectionDetail({ theme, sel, selectionKey, onUpgrade }) {
  return (
    <div className="sel-body">
      <SelectionHeader theme={theme} selection={selectionKey} sel={sel} />
      <SelectionStatusBody theme={theme} sel={sel} selectionKey={selectionKey} onUpgrade={onUpgrade} />
    </div>
  );
}

function SelectionMulti({ theme }) {
  return (
    <div className="sel-body sel-multi">
      <SelectionHeader theme={theme} selection="multi" />
      <SelectionMultiBody theme={theme} />
    </div>
  );
}

Object.assign(window, { SelectionPanel });
