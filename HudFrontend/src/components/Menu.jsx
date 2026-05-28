// Top-left "Menu" button + centered modal overlay with jade-tinted backdrop.
// Modal lists 5 options in heroic Cinzel. Clicks dispatch via Unity bridge.

import React from 'react';
import { sendToUnity, useBridge } from '../bridge.js';
import { hexAlpha } from './themes.js';
import { FiligreeCorner, FiligreeMedallion } from './Filigree.jsx';

// `disabled: true` items render greyed out and don't fire the bridge
// message — the C# side hasn't implemented those flows yet (Settings UI
// is a follow-up; Save / Load tracked separately). Resume / Quit /
// Surrender are the three that actually work end-to-end today.
const MENU_ITEMS = [
  { key: 'resume',    label: 'Resume Game',  hint: 'Return to the field' },
  { key: 'settings',  label: 'Settings',     hint: 'Sound, video, controls (coming soon)', disabled: true },
  { key: 'save',      label: 'Save Game',    hint: 'Inscribe the moment (coming soon)',   disabled: true },
  { key: 'load',      label: 'Load Game',    hint: 'Recall a former hour (coming soon)',  disabled: true },
  { key: 'quit',      label: 'Quit to Menu', hint: 'Return to the main menu' },
  { key: 'surrender', label: 'Surrender',    hint: 'Lay down your banner', danger: true },
];

export function MenuButton({ theme }) {
  const lvl = theme.ornament;
  return (
    <button
      className="hud-menu-btn"
      onClick={() => sendToUnity('menu:open')}
      style={{
        '--mb-base': theme.base,
        '--mb-mid': theme.baseMid,
        '--mb-edge': theme.baseEdge,
        '--mb-inlay': theme.inlay,
        '--mb-inlay-shadow': theme.inlayShadow,
        '--mb-accent': theme.accent,
        '--mb-text': theme.text,
      }}
    >
      <span className="hud-menu-btn-plate" />
      <span className="hud-menu-btn-inlay" />
      {lvl !== 'minimal' && (
        <>
          <span className="hud-menu-btn-corner tl"><FiligreeCorner size={22} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></span>
          <span className="hud-menu-btn-corner tr"><FiligreeCorner size={22} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></span>
          <span className="hud-menu-btn-corner bl"><FiligreeCorner size={22} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></span>
          <span className="hud-menu-btn-corner br"><FiligreeCorner size={22} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></span>
        </>
      )}
      <span className="hud-menu-btn-icon">
        <svg width="16" height="16" viewBox="0 0 16 16">
          <g stroke={theme.accent} strokeWidth="1.6" strokeLinecap="round">
            <line x1="3" y1="4" x2="13" y2="4" />
            <line x1="3" y1="8" x2="13" y2="8" />
            <line x1="3" y1="12" x2="13" y2="12" />
          </g>
          <circle cx="14" cy="4" r="1" fill={theme.accent} />
          <circle cx="14" cy="12" r="1" fill={theme.accent} />
        </svg>
      </span>
      <span className="hud-menu-btn-label" style={{ color: theme.accent }}>Menu</span>
    </button>
  );
}

export function MenuOverlay({ theme }) {
  const open = useBridge('menu', { open: false });
  const lvl = theme.ornament;

  React.useEffect(() => {
    if (!open?.open) return;
    const onKey = (e) => { if (e.key === 'Escape') sendToUnity('menu:close'); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open?.open]);

  if (!open?.open) return null;

  return (
    <div
      className="hud-menu-overlay"
      style={{
        background: `
          radial-gradient(ellipse at 50% 40%, ${hexAlpha(theme.gem, 0.20)}, ${hexAlpha('#000', 0.55)} 80%),
          ${hexAlpha(theme.gem, 0.38)}
        `,
      }}
      onClick={(e) => { if (e.target === e.currentTarget) sendToUnity('menu:close'); }}
    >
      <div
        className="hud-menu-modal"
        style={{
          '--mm-base': theme.base,
          '--mm-mid': theme.baseMid,
          '--mm-edge': theme.baseEdge,
          '--mm-inlay': theme.inlay,
          '--mm-inlay-shadow': theme.inlayShadow,
          '--mm-accent': theme.accent,
          '--mm-text': theme.text,
        }}
      >
        <div className="hud-menu-modal-plate" />
        <div className="hud-menu-modal-inlay" />
        <div className="hud-menu-modal-corner tl"><FiligreeCorner size={56} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="hud-menu-modal-corner tr"><FiligreeCorner size={56} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="hud-menu-modal-corner bl"><FiligreeCorner size={56} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="hud-menu-modal-corner br"><FiligreeCorner size={56} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>

        <div className="hud-menu-modal-crown">
          <div className="hud-menu-crown-medallion">
            <FiligreeMedallion size={62} color={theme.inlay} dim={theme.inlayDim}
                               accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi}
                               level={lvl} />
          </div>
        </div>

        <div className="hud-menu-modal-body">
          <div className="hud-menu-eyebrow" style={{ color: theme.textDim }}>The Field Is Held</div>
          <div className="hud-menu-title" style={{ color: theme.accent, textShadow: `0 0 12px ${theme.accent}66` }}>
            {open.title || 'Paused'}
          </div>
          <div className="hud-menu-sub" style={{ color: theme.textDim, borderColor: hexAlpha(theme.inlay, 0.4) }}>
            {open.subtitle || ''}
          </div>

          <div className="hud-menu-list">
            {MENU_ITEMS.map((item) => (
              <button
                key={item.key}
                className={`hud-menu-item ${item.danger ? 'danger' : ''} ${item.disabled ? 'disabled' : ''}`}
                onClick={() => !item.disabled && sendToUnity('menu:item', { key: item.key })}
                aria-disabled={item.disabled ? 'true' : undefined}
                style={{
                  '--mi-accent': item.danger ? '#e85a3a' : theme.accent,
                  ...(item.disabled ? { opacity: 0.45, cursor: 'not-allowed' } : null),
                }}
              >
                <span className="hud-menu-item-tick" />
                <span className="hud-menu-item-label">{item.label}</span>
                <span className="hud-menu-item-hint">{item.hint}</span>
                <span className="hud-menu-item-chevron">
                  <svg width="10" height="14" viewBox="0 0 10 14">
                    <polyline points="2,2 8,7 2,12" fill="none"
                              stroke="currentColor" strokeWidth="1.6"
                              strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                </span>
              </button>
            ))}
          </div>

          <div className="hud-menu-foot" style={{ color: theme.textDim }}>
            <span>Esc to dismiss</span>
            <span>·</span>
            <span>Autosave at every dawn</span>
          </div>
        </div>
      </div>
    </div>
  );
}
