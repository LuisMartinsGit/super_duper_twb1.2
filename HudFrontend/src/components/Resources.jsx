// Ornate fantasy resource counter, bottom-left of the HUD.
// Vertical stack matching the minimap height. Two groups separated by an
// inlaid divider — Vitals (Population, Religion) then Stores (Supplies, Iron,
// Veilstone, Veilsteel, Glow).
//
// Live values come from the Unity bridge topic `resources`. When the bridge
// hasn't pushed anything yet (dev / standalone preview), we fall back to a
// mock animator so the panel still reads visually correct.

import React from 'react';
import { useBridge } from '../bridge.js';
import { IconDisc, FiligreeCorner, FiligreeEdge } from './Filigree.jsx';

// Defines the row order + glyph + which group each resource belongs in.
const RESOURCE_DEFS = [
  { key: 'population', label: 'Population', glyph: 'people',  group: 'vitals', hasCap: true },
  { key: 'religion',   label: 'Religion',   glyph: 'relic',   group: 'vitals' },
  { key: 'supplies',   label: 'Supplies',   glyph: 'sack',    group: 'stores' },
  { key: 'iron',       label: 'Iron',       glyph: 'hex',     group: 'stores' },
  { key: 'veilstone',  label: 'Veilstone',  glyph: 'crystal', group: 'stores' },
  { key: 'veilsteel',  label: 'Veilsteel',  glyph: 'star8',   group: 'stores' },
  { key: 'glow',       label: 'Glow',       glyph: 'star8',   group: 'stores' },
];

// Mock state used only when the Unity bridge has never sent `resources` —
// matches the original mock's starting values + rates so the dev preview
// looks alive.
const MOCK_DEFAULTS = {
  population: { value: 84,   cap: 120,  rate: 0.020 * 60 },
  religion:   { value: 47,   cap: 999,  rate: 0.008 * 60 },
  supplies:   { value: 1248, cap: 5000, rate: 0.62  * 60 },
  iron:       { value: 412,  cap: 2000, rate: 0.28  * 60 },
  veilstone:  { value: 88,   cap: 250,  rate: 0.09  * 60 },
  veilsteel:  { value: 14,   cap: 100,  rate: 0.022 * 60 },
  glow:       { value: 0,    cap: 50,   rate: 0 },
};

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
      return (
        <g>
          <polygon points="11,2 17,9 11,20 5,9" fill={fill} stroke={stroke} strokeWidth="0.6" />
          <line x1="11" y1="2" x2="11" y2="20" stroke={stroke} strokeWidth="0.4" opacity="0.6" />
          <line x1="5" y1="9" x2="17" y2="9" stroke={stroke} strokeWidth="0.4" opacity="0.6" />
        </g>
      );
    case 'star8':
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

function ResourceRow({ def, snap, theme, hovered, onEnter, onLeave }) {
  const value = snap?.value ?? 0;
  const cap = snap?.cap ?? 0;
  const rate = snap?.rate ?? 0;
  const display = Math.floor(value);
  const pct = cap > 0 ? Math.min(1, value / cap) : 0;
  const near = pct > 0.9;
  const showRate = def.group === 'stores' && Math.abs(rate) > 0.01;
  const showCap = !!def.hasCap || (def.group === 'stores' && cap > 0);

  return (
    <div className="rc-row" onMouseEnter={onEnter} onMouseLeave={onLeave}>
      <div className="rc-disc-wrap" style={{ filter: `drop-shadow(0 0 5px ${theme.accent}33)` }}>
        <IconDisc size={26} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={theme.ornament}>
          <g transform="translate(2,2)">
            <Glyph kind={def.glyph} fill={theme.accent} stroke={theme.inlayShadow} />
          </g>
        </IconDisc>
      </div>
      {/* Resource name lives in the tooltip only — see rc-tip below. */}
      <div className="rc-row-value">
        <div className="rc-row-num" style={{
          color: near ? theme.accent : theme.text,
          textShadow: near ? `0 0 6px ${theme.accent}` : 'none',
        }}>
          {display.toLocaleString()}
          {showCap && (
            <span className="rc-row-cap" style={{ color: theme.textDim }}>/{cap}</span>
          )}
        </div>
        {/* Income per minute, computed C#-side as a 1-min rolling sum of
            positive deltas (mining/trickle/tick/plunder/trade/walls). Shown
            for every resource — players want to see religion/population
            growth too, not only stockpiles. */}
        <div className="rc-row-rate" style={{
          color: rate > 0 ? theme.accent : theme.textDim,
          opacity: rate > 0 ? 0.9 : 0.55,
        }}>
          {rate > 0 ? '+' : ''}{rate.toFixed(rate >= 10 || rate <= -10 ? 0 : 1)}<span style={{ color: theme.textDim, opacity: 0.8 }}>/min</span>
        </div>
      </div>
      {hovered && (
        <div className="rc-tip" style={{
          background: theme.base,
          color: theme.text,
          borderColor: theme.inlay,
          boxShadow: `0 0 0 1px ${theme.inlayShadow}, 0 8px 24px rgba(0,0,0,0.6), 0 0 22px ${theme.accent}33`,
        }}>
          <div className="rc-tip-name" style={{ color: theme.accent, fontFamily: "'Cinzel', serif" }}>{def.label}</div>
          {showRate && (
            <div className="rc-tip-row"><span style={{ color: theme.textDim }}>Rate</span><span>{rate >= 0 ? '+' : ''}{rate.toFixed(1)}/min</span></div>
          )}
          {showCap ? (
            <div className="rc-tip-row"><span style={{ color: theme.textDim }}>Stored</span><span>{display.toLocaleString()} of {cap.toLocaleString()}</span></div>
          ) : (
            <div className="rc-tip-row"><span style={{ color: theme.textDim }}>Stockpile</span><span>{display.toLocaleString()}</span></div>
          )}
          {(showCap || showRate) && cap > 0 && (
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

// Light-weight dev animator. Only used until Unity sends real data.
function useResourceSnapshot() {
  const live = useBridge('resources', null);
  const [mock, setMock] = React.useState(MOCK_DEFAULTS);

  React.useEffect(() => {
    if (live) return; // Unity is feeding us real data — no need to animate
    let raf;
    let last = performance.now();
    const tick = (t) => {
      const dt = (t - last) / 1000; last = t;
      setMock((prev) => {
        const next = { ...prev };
        for (const def of RESOURCE_DEFS) {
          const cur = prev[def.key];
          const inc = (cur.rate / 60) * dt * 3;
          next[def.key] = { ...cur, value: Math.min(cur.cap, cur.value + inc) };
        }
        return next;
      });
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [!!live]);

  return live || mock;
}

export function ResourceCounter({ theme }) {
  const snap = useResourceSnapshot();
  const [hovered, setHovered] = React.useState(null);
  const lvl = theme.ornament;

  // HudBridge sets `hidden:true` on individual resource entries that don't
  // apply in the current game state — Religion is hidden in Age 0 because
  // no sects exist yet, so the row would always read 0/0 with no income.
  const isHidden = (key) => !!(snap[key] && snap[key].hidden);

  const grouped = { vitals: [], stores: [] };
  for (const def of RESOURCE_DEFS) {
    if (isHidden(def.key)) continue;
    grouped[def.group].push(def);
  }

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
        <div className="rc-corner rc-corner-tl"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-tr"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-bl"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-br"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        {lvl !== 'minimal' && (
          <>
            {/* Filigree edge — width must match the 138 px rc-v frame width
                set in styles.css (`.rc-v .rc-frame`). The earlier 260 px value
                came from the horizontal-layout footprint and rendered nearly
                twice as wide as the panel itself, hanging the trim off both
                sides. SVG preserveAspectRatio="none" scales the artwork
                cleanly to the new width. */}
            <div className="rc-edge rc-edge-top"><FiligreeEdge width={130} height={18} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
            <div className="rc-edge rc-edge-bot"><FiligreeEdge width={130} height={18} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
          </>
        )}

        <div className="rc-stack">
          {grouped.vitals.map((def) => (
            <ResourceRow key={def.key} def={def} snap={snap[def.key]} theme={theme}
                         hovered={hovered === def.key}
                         onEnter={() => setHovered(def.key)}
                         onLeave={() => setHovered(null)} />
          ))}

          <div className="rc-divider" aria-hidden>
            <div className="rc-divider-line" style={{ background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)` }} />
            <div className="rc-divider-gem" style={{ background: theme.accent, boxShadow: `0 0 6px ${theme.accent}, 0 0 0 1px ${theme.inlayShadow}` }} />
            <div className="rc-divider-line" style={{ background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)` }} />
          </div>

          {grouped.stores.map((def) => (
            <ResourceRow key={def.key} def={def} snap={snap[def.key]} theme={theme}
                         hovered={hovered === def.key}
                         onEnter={() => setHovered(def.key)}
                         onLeave={() => setHovered(null)} />
          ))}
        </div>
      </div>
    </div>
  );
}
