// menu.jsx — Top-left "Menu" button + centered modal overlay with
// jade-tinted backdrop. Modal lists 5 options in heroic Cinzel.

const MENU_ITEMS = [
  { key: 'resume',     label: 'Resume Game',  hint: 'Return to the field' },
  { key: 'settings',   label: 'Settings',     hint: 'Sound, video, controls' },
  { key: 'save',       label: 'Save Game',    hint: 'Inscribe the moment' },
  { key: 'load',       label: 'Load Game',    hint: 'Recall a former hour' },
  { key: 'surrender',  label: 'Surrender',    hint: 'Lay down your banner', danger: true },
];

function MenuButton({ theme, onOpen }) {
  const lvl = theme.ornament;
  return (
    <button
      className="hud-menu-btn"
      onClick={onOpen}
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
        {/* three-bars sigil */}
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

function MenuOverlay({ theme, onClose, onItem }) {
  const lvl = theme.ornament;
  // ESC to close
  React.useEffect(() => {
    const onKey = (e) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div
      className="hud-menu-overlay"
      style={{
        // Greenish jade tint over the whole screen
        background: `
          radial-gradient(ellipse at 50% 40%, ${hexAlpha(theme.gem, 0.20)}, ${hexAlpha('#000', 0.55)} 80%),
          ${hexAlpha(theme.gem, 0.38)}
        `,
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
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

        {/* Top cartouche with medallion + title */}
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
            Armistice
          </div>
          <div className="hud-menu-sub" style={{ color: theme.textDim, borderColor: hexAlpha(theme.inlay, 0.4) }}>
            Day&nbsp;14 · Hour&nbsp;03 · Vale of Karrash
          </div>

          <div className="hud-menu-list">
            {MENU_ITEMS.map((item, i) => (
              <button
                key={item.key}
                className={`hud-menu-item ${item.danger ? 'danger' : ''}`}
                onClick={() => onItem(item.key)}
                style={{
                  '--mi-accent': item.danger ? '#e85a3a' : theme.accent,
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

// Small helper — hex (#rrggbb or #rrggbbaa) → rgba(...)
function hexAlpha(hex, a) {
  const h = (hex || '').replace('#', '');
  if (h.length < 6) return `rgba(0,0,0,${a})`;
  const r = parseInt(h.slice(0, 2), 16);
  const g = parseInt(h.slice(2, 4), 16);
  const b = parseInt(h.slice(4, 6), 16);
  return `rgba(${r},${g},${b},${a})`;
}

Object.assign(window, { MenuButton, MenuOverlay });
