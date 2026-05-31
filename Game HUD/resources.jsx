// resources.jsx — Ornate fantasy resource counter, bottom-left of HUD.
// Vertical stack matching the minimap's height. Two groups separated by
// an inlaid divider:
//   ── Vitals ──
//   Population  · current / cap
//   Religion    · single value
//   ── Stores ──
//   Supplies / Iron / Veilstone / Veilsteel   (with +/min rate)

const RESOURCES = [
  // Top group — vitals (no per-minute rate)
  { key: 'population', label: 'Population', start: 84,   cap: 120,  rate: 0.020, group: 'vitals', glyph: 'people',  hasCap: true },
  { key: 'religion',   label: 'Religion',   start: 47,   cap: 999,  rate: 0.008, group: 'vitals', glyph: 'relic' },
  // Bottom group — stores
  { key: 'supplies',   label: 'Supplies',   start: 1248, cap: 5000, rate: 0.62,  group: 'stores', glyph: 'sack' },
  { key: 'iron',       label: 'Iron',       start: 412,  cap: 2000, rate: 0.28,  group: 'stores', glyph: 'hex' },
  { key: 'veilstone',  label: 'Veilstone',  start: 88,   cap: 250,  rate: 0.09,  group: 'stores', glyph: 'crystal' },
  { key: 'veilsteel',  label: 'Veilsteel',  start: 14,   cap: 100,  rate: 0.022, group: 'stores', glyph: 'star8' },
];

// Resource glyph — only simple primitives (circle, polygon, ellipse).
function Glyph({ kind, fill, stroke }) {
  switch (kind) {
    case 'people':
      return (
        <g>
          <circle cx="7" cy="8" r="2.4" fill={fill} stroke={stroke} strokeWidth="0.6" />
          <circle cx="15" cy="8" r="2.4" fill={fill} stroke={stroke} strokeWidth="0.6" />
          <circle cx="11" cy="14" r="2.8" fill={fill} stroke={stroke} strokeWidth="0.6" />
          <path d="M 3 19 Q 7 13 11 17 Q 15 13 19 19" fill="none" stroke={fill} strokeWidth="1.4" strokeLinecap="round" />
        </g>
      );
    case 'relic':
      // Sacred relic — vertical bar + horizontal bar + small disc, like an ornate cross
      return (
        <g>
          <rect x="9.5" y="3" width="3" height="16" fill={fill} stroke={stroke} strokeWidth="0.5" />
          <rect x="5" y="7" width="12" height="3" fill={fill} stroke={stroke} strokeWidth="0.5" />
          <circle cx="11" cy="5" r="1.3" fill={stroke} />
        </g>
      );
    case 'sack':
      return (
        <g>
          <path d="M 5 10 Q 5 19 11 19 Q 17 19 17 10 Q 16 8 14 8 L 8 8 Q 6 8 5 10 Z"
                fill={fill} stroke={stroke} strokeWidth="0.6" />
          <path d="M 8 8 Q 9 5 11 5 Q 13 5 14 8"
                fill="none" stroke={stroke} strokeWidth="0.8" strokeLinecap="round" />
          <line x1="11" y1="13" x2="11" y2="17" stroke={stroke} strokeWidth="0.5" opacity="0.7" />
        </g>
      );
    case 'hex':
      return (
        <polygon points="11,3 18,7 18,15 11,19 4,15 4,7"
                 fill={fill} stroke={stroke} strokeWidth="0.6" />
      );
    case 'crystal':
      // Tall diamond facet
      return (
        <g>
          <polygon points="11,2 17,9 11,20 5,9" fill={fill} stroke={stroke} strokeWidth="0.6" />
          <line x1="11" y1="2" x2="11" y2="20" stroke={stroke} strokeWidth="0.4" opacity="0.6" />
          <line x1="5" y1="9" x2="17" y2="9" stroke={stroke} strokeWidth="0.4" opacity="0.6" />
        </g>
      );
    case 'star8':
      // Two diamonds overlaid — refined veil-steel
      return (
        <g transform="translate(11,11)">
          <polygon points="0,-8 7,0 0,8 -7,0" fill={fill} stroke={stroke} strokeWidth="0.6" />
          <polygon points="0,-6 6,0 0,6 -6,0" fill="none" stroke={stroke} strokeWidth="0.4" opacity="0.7" transform="rotate(45)" />
          <circle cx="0" cy="0" r="1.6" fill={stroke} />
        </g>
      );
    default:
      return null;
  }
}

function ResourceRow({ res, value, theme, hovered, onEnter, onLeave }) {
  const display = Math.floor(value);
  const pct = Math.min(1, value / res.cap);
  const near = pct > 0.9;
  const showRate = res.group === 'stores';
  const showCap = !!res.hasCap;

  return (
    <div
      className="rc-row"
      onMouseEnter={onEnter}
      onMouseLeave={onLeave}
    >
      <div className="rc-disc-wrap" style={{ filter: `drop-shadow(0 0 5px ${theme.accent}33)` }}>
        <IconDisc size={26} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={theme.ornament}>
          <g transform="translate(2,2)">
            <Glyph kind={res.glyph} fill={theme.accent} stroke={theme.inlayShadow} />
          </g>
        </IconDisc>
      </div>
      <div className="rc-row-value">
        <div className="rc-row-num" style={{
          color: near ? theme.accent : theme.text,
          textShadow: near ? `0 0 6px ${theme.accent}` : 'none',
        }}>
          {display.toLocaleString()}
          {showCap && (
            <span className="rc-row-cap" style={{ color: theme.textDim }}>
              /{res.cap}
            </span>
          )}
        </div>
        {showRate && (
          <div className="rc-row-rate" style={{ color: theme.accent, opacity: 0.85 }}>
            +{(res.rate * 60).toFixed(1)}<span style={{ color: theme.textDim, opacity: 0.8 }}>/min</span>
          </div>
        )}
      </div>
      {hovered && (
        <div className="rc-tip" style={{
          background: theme.base,
          color: theme.text,
          borderColor: theme.inlay,
          boxShadow: `0 0 0 1px ${theme.inlayShadow}, 0 8px 24px rgba(0,0,0,0.6), 0 0 22px ${theme.accent}33`,
        }}>
          <div className="rc-tip-name" style={{ color: theme.accent, fontFamily: "'Cinzel', serif" }}>{res.label}</div>
          {showRate && (
            <div className="rc-tip-row"><span style={{ color: theme.textDim }}>Rate</span><span>+{(res.rate * 60).toFixed(1)}/min</span></div>
          )}
          {showCap ? (
            <div className="rc-tip-row"><span style={{ color: theme.textDim }}>Stored</span><span>{display.toLocaleString()} of {res.cap.toLocaleString()}</span></div>
          ) : showRate ? (
            <div className="rc-tip-row"><span style={{ color: theme.textDim }}>Stored</span><span>{display.toLocaleString()} of {res.cap.toLocaleString()}</span></div>
          ) : (
            <div className="rc-tip-row"><span style={{ color: theme.textDim }}>Devotion</span><span>{display.toLocaleString()}</span></div>
          )}
          {(showCap || showRate) && (
            <div className="rc-tip-bar" style={{ background: theme.inlayShadow }}>
              <div style={{
                width: `${pct * 100}%`,
                height: '100%',
                background: `linear-gradient(90deg, ${theme.accentSoft}, ${theme.accent})`,
                boxShadow: `0 0 6px ${theme.accent}`,
              }} />
            </div>
          )}
          <div className="rc-tip-tail" style={{ background: theme.base, borderColor: theme.inlay }} />
        </div>
      )}
    </div>
  );
}

function ResourceCounter({ theme }) {
  const [values, setValues] = React.useState(() => RESOURCES.map((r) => r.start));
  const [hovered, setHovered] = React.useState(null);

  React.useEffect(() => {
    let raf;
    let last = performance.now();
    const tick = (t) => {
      const dt = (t - last) / 1000; last = t;
      setValues((vs) => vs.map((v, i) => Math.min(RESOURCES[i].cap, v + RESOURCES[i].rate * dt * 3)));
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, []);

  const lvl = theme.ornament;

  // Group into vitals + stores; insert divider between
  const grouped = RESOURCES.reduce((acc, r) => {
    (acc[r.group] = acc[r.group] || []).push(r);
    return acc;
  }, {});

  return (
    <div className="rc-root rc-v" style={{
      '--rc-base': theme.base,
      '--rc-mid': theme.baseMid,
      '--rc-edge': theme.baseEdge,
      '--rc-inlay': theme.inlay,
      '--rc-inlay-shadow': theme.inlayShadow,
      '--rc-accent': theme.accent,
    }}>
      <div className="rc-frame">
        <div className="rc-plate" />
        <div className="rc-inlay" />
        {/* corners */}
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
        {/* top + bottom edge cartouche — narrower for vertical layout */}
        {lvl !== 'minimal' && (
          <>
            <div className="rc-edge rc-edge-top">
              <FiligreeEdge width={100} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
            </div>
            <div className="rc-edge rc-edge-bot">
              <FiligreeEdge width={100} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} />
            </div>
          </>
        )}

        <div className="rc-stack">
          {grouped.vitals.map((r, i) => (
            <ResourceRow
              key={r.key} res={r} value={values[RESOURCES.indexOf(r)]}
              theme={theme}
              hovered={hovered === r.key}
              onEnter={() => setHovered(r.key)}
              onLeave={() => setHovered(null)}
            />
          ))}

          {/* divider */}
          <div className="rc-divider" aria-hidden>
            <div className="rc-divider-line" style={{ background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)` }} />
            <div className="rc-divider-gem" style={{
              background: theme.accent,
              boxShadow: `0 0 6px ${theme.accent}, 0 0 0 1px ${theme.inlayShadow}`,
            }} />
            <div className="rc-divider-line" style={{ background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)` }} />
          </div>

          {grouped.stores.map((r, i) => (
            <ResourceRow
              key={r.key} res={r} value={values[RESOURCES.indexOf(r)]}
              theme={theme}
              hovered={hovered === r.key}
              onEnter={() => setHovered(r.key)}
              onLeave={() => setHovered(null)}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { ResourceCounter });
