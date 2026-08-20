// Sects rail. Six sects stacked inside one jade-and-silver frame. Each row
// is the sect name on the left and three small hex buttons on the right:
// [Level-Up] [Passive] [Active cast]. A handle on the right edge slides
// the whole panel off to the left.
//
// Live state comes from Unity via `sects` topic; falls back to mock SECTS
// for design preview.

import React from 'react';
import { useBridge, sendToUnity } from '../bridge.js';
import { FiligreeCorner, FiligreeEdge } from './Filigree.jsx';

const MOCK_SECTS = [
  { key: 'antiquity', name: 'Antiquity',
    active: { icon: 'castle', label: 'Raise Bastion', hint: 'Found a fortified work' },
    passive: { label: 'Stonebound',  hint: 'Walls endure 25% longer' },
    level: 2, maxLevel: 5, cost: '120 Iron' },
  { key: 'blade', name: 'the Blade',
    active: { icon: 'sword', label: 'Levy', hint: 'Conscript a banner of soldiery' },
    passive: { label: 'Whetted', hint: 'Standing armies deal +10% damage' },
    level: 3, maxLevel: 5, cost: '90 Iron' },
  { key: 'scholarum', name: 'Scholarum',
    active: { icon: 'rune', label: 'Study', hint: 'Decipher a forgotten glyph' },
    passive: { label: 'Inkwell', hint: 'Research completes 15% faster' },
    level: 1, maxLevel: 5, cost: '60 Iron' },
  { key: 'veil', name: 'the Veil',
    active: { icon: 'star', label: 'Cast Sigil', hint: 'Channel a spell through the veil' },
    passive: { label: 'Tideborn', hint: 'Mana regenerates 20% faster' },
    level: 2, maxLevel: 5, cost: '40 Veilstone' },
  { key: 'pact', name: 'the Pact',
    active: { icon: 'banner', label: 'Send Envoy', hint: 'Treaty · Tribute · Pact' },
    passive: { label: 'Goodwill', hint: 'Diplomatic actions cost 30% less' },
    level: 1, maxLevel: 5, cost: '80 Veilsteel' },
  { key: 'lore', name: 'Lore',
    active: { icon: 'scroll', label: 'Consult Codex', hint: 'Open the realm’s tome' },
    passive: { label: 'Cartographer', hint: 'Reveals hidden sites on the map' },
    level: 4, maxLevel: 5, cost: '200 Iron' },
];

const HEX_POINTS = '14,0 42,0 56,24.5 42,49 14,49 0,24.5';
const HEX_INNER_POINTS = '17,4 39,4 52,24.5 39,45 17,45 4,24.5';

function HexIcon({ kind, color, dim }) {
  const c = color; const d = dim;
  switch (kind) {
    case 'castle':
      return (
        <g stroke={c} strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" fill="none">
          <rect x="-7" y="-1" width="14" height="9" />
          <rect x="-3" y="-5" width="6" height="4" />
          <line x1="-7" y1="-1" x2="-7" y2="-4" />
          <line x1="-4" y1="-1" x2="-4" y2="-4" />
          <line x1="4" y1="-1" x2="4" y2="-4" />
          <line x1="7" y1="-1" x2="7" y2="-4" />
          <line x1="-7" y1="4" x2="7" y2="4" stroke={d} strokeWidth="0.8" />
        </g>
      );
    case 'sword':
      return (
        <g stroke={c} strokeWidth="1.3" strokeLinecap="round" fill={c}>
          <line x1="0" y1="-8" x2="0" y2="4" />
          <line x1="-5" y1="4" x2="5" y2="4" strokeWidth="1.6" />
          <line x1="0" y1="4" x2="0" y2="7" />
          <circle cx="0" cy="8" r="1.2" stroke="none" />
          <polygon points="0,-9 1.4,-7 0,-5 -1.4,-7" stroke="none" />
        </g>
      );
    case 'rune':
      return (
        <g stroke={c} strokeWidth="1.2" fill="none" strokeLinecap="round">
          <polygon points="0,-7 6,-3.5 6,3.5 0,7 -6,3.5 -6,-3.5" />
          <line x1="0" y1="-4" x2="0" y2="4" stroke={d} strokeWidth="0.9" />
          <line x1="-3.4" y1="-2" x2="3.4" y2="2" stroke={d} strokeWidth="0.9" />
          <line x1="-3.4" y1="2" x2="3.4" y2="-2" stroke={d} strokeWidth="0.9" />
          <circle cx="0" cy="0" r="1.2" fill={c} stroke="none" />
        </g>
      );
    case 'star':
      return (
        <g fill={c} stroke={c} strokeWidth="0.6" strokeLinejoin="round">
          <polygon points="0,-9 2,-2 9,0 2,2 0,9 -2,2 -9,0 -2,-2" />
          <circle cx="0" cy="0" r="1.4" fill={d} stroke="none" />
        </g>
      );
    case 'banner':
      return (
        <g stroke={c} strokeWidth="1.3" strokeLinecap="round" fill="none">
          <line x1="-3" y1="-8" x2="-3" y2="9" />
          <polygon points="-3,-8 8,-5 -3,-2" fill={c} />
          <circle cx="-3" cy="-9" r="1.2" fill={c} stroke="none" />
          <ellipse cx="-3" cy="9" rx="3" ry="1" fill={d} stroke="none" />
        </g>
      );
    case 'scroll':
      return (
        <g stroke={c} strokeWidth="1.2" fill={c} strokeLinejoin="round">
          <rect x="-7" y="-4" width="14" height="9" fill={d} stroke={c} rx="0.5" />
          <ellipse cx="-7" cy="0.5" rx="1.6" ry="4.5" fill={c} stroke="none" />
          <ellipse cx="7"  cy="0.5" rx="1.6" ry="4.5" fill={c} stroke="none" />
          <line x1="-4" y1="-1.5" x2="4" y2="-1.5" stroke={c} strokeWidth="0.7" />
          <line x1="-4" y1="1"    x2="4" y2="1"    stroke={c} strokeWidth="0.7" />
          <line x1="-4" y1="3.5"  x2="2" y2="3.5"  stroke={c} strokeWidth="0.7" />
        </g>
      );
    case 'eye':
      return (
        <g stroke={c} strokeWidth="1.3" fill="none" strokeLinecap="round" strokeLinejoin="round">
          <path d="M -9 0 Q 0 -7 9 0 Q 0 7 -9 0 Z" />
          <circle cx="0" cy="0" r="3" fill={c} />
          <circle cx="-1" cy="-1" r="0.9" fill={d} stroke="none" />
        </g>
      );
    case 'levelup':
      return (
        <g fill="none" stroke={c} strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="-5,-1 0,-6 5,-1" />
          <polyline points="-5,5 0,0 5,5" stroke={d} strokeWidth="1.2" />
        </g>
      );
    default: return null;
  }
}

function HexButton({ theme, variant, sect, onAction, active, tier }) {
  const lvl = theme.ornament;
  const id = `hex-${theme.key}-${sect.key}-${variant}-${tier || 0}`;
  const showOrn = lvl !== 'minimal';

  // Tiered actives (design 2026-07-05): `active` carries one tier's skill
  // ({icon,label,hint,unlocked}); locked tiers render muted (clicking still
  // dispatches — the C# side answers with an unlock-hint notification).
  const cfg = variant === 'active' ? {
    icon: (active || sect.active).icon,
    title: (active || sect.active).label,
    kicker: tier ? `Active ${['', 'I', 'II', 'III'][tier] || tier}` : 'Active',
    hint: (active || sect.active).hint,
    glow: theme.accent,
    muted: active ? active.unlocked === false : false,
  } : variant === 'passive' ? {
    icon: 'eye', title: sect.passive.label, kicker: 'Passive', hint: sect.passive.hint,
    glow: theme.inlay, muted: true,
  } : {
    icon: 'levelup', title: 'Rank',
    kicker: `Level ${sect.level} / ${sect.maxLevel}`,
    hint: sect.level >= sect.maxLevel ? 'Highest rank' : `${sect.cost}`,
    glow: theme.accent, muted: sect.level >= sect.maxLevel,
  };

  const fillColor = cfg.muted ? theme.inlayDim : theme.accent;
  const accentColor = cfg.muted ? theme.inlayDim : theme.accent;

  return (
    <button
      type="button"
      className={`hex-btn hex-btn-${variant} ${cfg.muted ? 'muted' : ''}`}
      onClick={() => { if (variant !== 'passive' && onAction) onAction(sect.key, variant, tier); }}
      title={cfg.title}
      aria-disabled={variant === 'passive' ? 'true' : undefined}
    >
      <svg width="28" height="25" viewBox="0 0 56 49" className="hex-btn-svg">
        <defs>
          <radialGradient id={`${id}-fill`} cx="50%" cy="35%" r="70%">
            <stop offset="0%" stopColor={theme.baseMid} />
            <stop offset="70%" stopColor={theme.base} />
            <stop offset="100%" stopColor={theme.baseEdge} />
          </radialGradient>
          <linearGradient id={`${id}-bezel`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={cfg.muted ? theme.inlayDim : theme.inlay} stopOpacity="0.95" />
            <stop offset="100%" stopColor={theme.inlayDim} stopOpacity="0.9" />
          </linearGradient>
        </defs>
        <polygon points={HEX_POINTS} fill={`url(#${id}-fill)`}
                 stroke={`url(#${id}-bezel)`} strokeWidth="1.6" strokeLinejoin="round" />
        {showOrn && (
          <polygon points={HEX_INNER_POINTS} fill="none"
                   stroke={theme.inlayShadow} strokeWidth="0.6" opacity="0.9" strokeLinejoin="round" />
        )}
        {showOrn && !cfg.muted && (
          <g fill={accentColor}>
            <circle cx="14" cy="0" r="1" />
            <circle cx="42" cy="0" r="1" />
            <circle cx="56" cy="24.5" r="1.1" />
            <circle cx="42" cy="49" r="1" />
            <circle cx="14" cy="49" r="1" />
            <circle cx="0" cy="24.5" r="1.1" />
          </g>
        )}
        <polygon className="hex-btn-glow" points={HEX_POINTS} fill="none" stroke={cfg.glow} strokeWidth="1.5" strokeLinejoin="round" opacity="0" />
        <g transform="translate(28 24.5)">
          <HexIcon kind={cfg.icon} color={fillColor} dim={theme.inlay} />
        </g>
        {variant === 'level' && (
          <g transform="translate(43 41)">
            <circle r="6" fill={theme.baseEdge} stroke={theme.accent} strokeWidth="0.8" />
            <text x="0" y="2.6" textAnchor="middle" fontSize="8"
                  fontFamily="'Cinzel', serif" fontWeight="700"
                  fill={theme.accent}>{sect.level}</text>
          </g>
        )}
      </svg>
      <span className="hex-btn-label" style={{
        background: theme.base,
        color: theme.text,
        borderColor: theme.inlay,
        boxShadow: `0 0 0 1px ${theme.inlayShadow}, 0 6px 16px rgba(0,0,0,0.5)`,
      }}>
        <span className="hex-btn-label-kicker" style={{ color: theme.textDim }}>{cfg.kicker}</span>
        <span className="hex-btn-label-name" style={{ color: cfg.muted ? theme.text : theme.accent }}>{cfg.title}</span>
        <span className="hex-btn-label-hint" style={{ color: theme.textDim }}>{cfg.hint}</span>
        <span className="hex-btn-label-tail" style={{ background: theme.base, borderColor: theme.inlay }} />
      </span>
    </button>
  );
}

function SectRow({ sect, theme, onAction }) {
  // Empty chapel slot — render a muted placeholder row instead of crashing
  // on missing sect.active / sect.passive. The label invites the player to
  // click the matching ground decal at the Temple of Ridan to fill it.
  if (!sect || sect.state === 'empty') {
    return (
      <div className="sect-row sect-row-empty">
        <div className="sect-name" style={{ color: theme.inlayDim }}>
          <span className="sect-name-prefix" style={{ color: theme.inlayDim }}>Chapel slot</span>
          <span className="sect-name-name" style={{ color: theme.textDim, fontStyle: 'italic' }}>Empty</span>
        </div>
        <div className="sect-buttons" style={{ opacity: 0.35 }}>
          {/* Three placeholder dots so the row keeps the same height as a full sect row. */}
          <div style={{ width: 28, height: 25 }} />
          <div style={{ width: 28, height: 25 }} />
          <div style={{ width: 28, height: 25 }} />
        </div>
      </div>
    );
  }

  // Building — show the sect name with a progress kicker. No live buttons
  // because levers don't exist until the chapel completes.
  if (sect.state === 'building') {
    return (
      <div className="sect-row sect-row-building">
        <div className="sect-name" style={{ color: theme.textDim }}>
          <span className="sect-name-prefix" style={{ color: theme.inlayDim }}>Building</span>
          <span className="sect-name-name" style={{ color: theme.text }}>
            {sect.name} {sect.progress != null ? `· ${sect.progress}%` : ''}
          </span>
        </div>
        <div className="sect-buttons" style={{ opacity: 0.4 }}>
          <div style={{ width: 28, height: 25 }} />
          <div style={{ width: 28, height: 25 }} />
          <div style={{ width: 28, height: 25 }} />
        </div>
      </div>
    );
  }

  // Tiered actives: prefer the `actives` array (one hex per skill tier);
  // fall back to the single legacy `active` object for old payloads/mocks.
  const actives = (sect.actives && sect.actives.length)
    ? sect.actives
    : (sect.active ? [{ tier: 1, unlocked: true, icon: sect.active.icon, label: sect.active.label, hint: sect.active.hint }] : []);

  return (
    <div className="sect-row">
      <div className="sect-name" style={{ color: theme.textDim }}>
        <span className="sect-name-prefix" style={{ color: theme.inlayDim }}>Sect of</span>
        <span className="sect-name-name" style={{ color: theme.text }}>{sect.name}</span>
      </div>
      <div className="sect-buttons">
        <HexButton theme={theme} variant="level"   sect={sect} onAction={onAction} />
        <HexButton theme={theme} variant="passive" sect={sect} onAction={onAction} />
        {actives.map((a) => (
          <HexButton key={`a${a.tier}`} theme={theme} variant="active" sect={sect}
                     onAction={onAction} active={a} tier={a.tier} />
        ))}
      </div>
    </div>
  );
}

export function Sidebar({ theme }) {
  const [collapsed, setCollapsed] = React.useState(false);
  // Falls back to MOCK_SECTS only outside Unity (dev browser preview) so the
  // designer can iterate on the rail's look without launching the game. In
  // Unity, HudBridge.PushSects pushes the real 6-row chapel-slot snapshot
  // immediately after `sectsVisible` flips true — the empty array default
  // keeps the rail from briefly showing mock sects on the first frame.
  const fallback = (typeof window !== 'undefined' && typeof window.uwb === 'undefined') ? MOCK_SECTS : [];
  const sects = useBridge('sects', fallback);
  // Visibility is driven by HudBridge.PushSectsVisibility — the rail stays
  // hidden until the local faction owns a completed Temple of Ridan. We
  // default to `false` so the rail never flashes pre-temple on first load.
  const sectsVisible = useBridge('sectsVisible', { visible: false });
  const handleAction = (sectKey, variant, tier) =>
    sendToUnity('sidebar:action', tier ? { sect: sectKey, variant, tier } : { sect: sectKey, variant });

  if (!sectsVisible || !sectsVisible.visible) return null;

  return (
    <div className={`sect-host ${collapsed ? 'collapsed' : ''}`}>
      {collapsed
        ? <PowersRail theme={theme} sects={sects} onAction={handleAction} />
        : <SectsRail  theme={theme} sects={sects} onAction={handleAction} />}

      <button
        className="sect-handle"
        onClick={() => setCollapsed((c) => !c)}
        title={collapsed ? 'Show sects' : 'Hide sects'}
        aria-label={collapsed ? 'Show sects' : 'Hide sects'}
        style={{
          '--sh-base': theme.base,
          '--sh-edge': theme.baseEdge,
          '--sh-inlay': theme.inlay,
          '--sh-inlay-shadow': theme.inlayShadow,
          '--sh-accent': theme.accent,
        }}
      >
        <svg width="10" height="14" viewBox="0 0 10 14">
          <polyline
            points={collapsed ? '3,2 8,7 3,12' : '7,2 2,7 7,12'}
            fill="none" stroke={theme.accent} strokeWidth="1.6"
            strokeLinecap="round" strokeLinejoin="round"
          />
        </svg>
      </button>
    </div>
  );
}

// Full sects rail — sect name + Level / Passive / Active triplet per row.
function SectsRail({ theme, sects, onAction }) {
  const lvl = theme.ornament;
  return (
    <div className="rc-root rc-v sect-rail" style={{
      '--rc-base': theme.base,
      '--rc-mid': theme.baseMid,
      '--rc-edge': theme.baseEdge,
      '--rc-inlay': theme.inlay,
      '--rc-inlay-shadow': theme.inlayShadow,
      '--rc-accent': theme.accent,
    }}>
      <div className="rc-frame sect-frame">
        <div className="rc-plate" />
        <div className="rc-inlay" />
        <div className="rc-corner rc-corner-tl"><FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-tr"><FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-bl"><FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-br"><FiligreeCorner size={36} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        {lvl !== 'minimal' && (
          <>
            <div className="rc-edge rc-edge-top"><FiligreeEdge width={220} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
            <div className="rc-edge rc-edge-bot"><FiligreeEdge width={220} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
          </>
        )}

        <div className="sect-stack">
          {sects.map((s, i) => (
            <React.Fragment key={s.key}>
              {i > 0 && (
                <div className="rc-divider sect-divider" aria-hidden>
                  <div className="rc-divider-line" style={{ background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)` }} />
                  <div className="rc-divider-gem" style={{ background: theme.accent, boxShadow: `0 0 4px ${theme.accent}, 0 0 0 1px ${theme.inlayShadow}` }} />
                  <div className="rc-divider-line" style={{ background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)` }} />
                </div>
              )}
              <SectRow sect={s} theme={theme} onAction={onAction} />
            </React.Fragment>
          ))}
        </div>
      </div>
    </div>
  );
}

// Compact powers-only rail — just the 6 active hex buttons, stacked. Renders
// when the sects rail is collapsed via the chevron handle.
function PowersRail({ theme, sects, onAction }) {
  const lvl = theme.ornament;
  return (
    <div className="rc-root rc-v powers-rail" style={{
      '--rc-base': theme.base,
      '--rc-mid': theme.baseMid,
      '--rc-edge': theme.baseEdge,
      '--rc-inlay': theme.inlay,
      '--rc-inlay-shadow': theme.inlayShadow,
      '--rc-accent': theme.accent,
    }}>
      <div className="rc-frame powers-frame">
        <div className="rc-plate" />
        <div className="rc-inlay" />
        <div className="rc-corner rc-corner-tl"><FiligreeCorner size={28} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-tr"><FiligreeCorner size={28} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-bl"><FiligreeCorner size={28} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-br"><FiligreeCorner size={28} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="powers-stack">
          {sects
            .filter(s => s && s.state !== 'empty' && s.state !== 'building' && (s.active || (s.actives && s.actives.length)))
            .flatMap(s => {
              const actives = (s.actives && s.actives.length)
                ? s.actives
                : [{ tier: 1, unlocked: true, icon: s.active.icon, label: s.active.label, hint: s.active.hint }];
              return actives.map(a => ({ s, a }));
            })
            .map(({ s, a }, i) => (
              <React.Fragment key={`${s.key}-t${a.tier}`}>
                {i > 0 && (
                  <div className="powers-divider" aria-hidden style={{
                    background: `linear-gradient(90deg, transparent, ${theme.inlay}, transparent)`,
                  }} />
                )}
                <div className="powers-row">
                  <HexButton theme={theme} variant="active" sect={s} onAction={onAction} active={a} tier={a.tier} />
                </div>
              </React.Fragment>
            ))}
        </div>
      </div>
    </div>
  );
}
