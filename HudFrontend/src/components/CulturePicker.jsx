// CulturePicker.jsx — top-center "Choose Culture" button + modal.
//
// Bridge topic `cultureChoice` (pushed by HudBridge):
//   { active, available, canAfford, cost:{supplies,iron,crystal},
//     lacking:{supplies,iron,crystal}, inProgress, progress, remaining,
//     duration, ageUpCulture, currentCulture }
//
// When `inProgress`, the button shows a progress bar (filling left→right)
// and the culture name; when not, it's a clickable button that opens the
// modal. Clicking a culture card sends `culture:choose` { culture } back
// to Unity which primes CultureChoicePopup statics + commits the age-up.

import React from 'react';
import { useBridge, sendToUnity } from '../bridge.js';

const CULTURES = [
  {
    key: 'Runai',
    name: 'Runai',
    blurb: 'Trade-borne sandstone confederacy. Bazaars + caravans, no Huts: population is set by markets.',
    // 16:9 portrait — placeholder gradient until art lands. Replace `image`
    // with a real URL or import to swap in a portrait.
    image: null,
    palette: ['#3fc8d3', '#c2a36e'],
    comingSoon: true,
  },
  {
    key: 'Alanthor',
    name: 'Alanthor',
    blurb: 'Stone-disciplined wall-builders. No Gatherers Huts; income flows from owned wall length.',
    image: null,
    palette: ['#8ea579', '#6e6b66'],
  },
  {
    key: 'Feraldis',
    name: 'Feraldis',
    blurb: 'Hunter-iconoclasts. Hunting lodges + totem towers; Iconoclast halves sect cooldowns.',
    image: null,
    palette: ['#b22e29', '#3f3a36'],
    comingSoon: true,
  },
];

function CostChip({ label, value, lacking, color }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 5,
      fontFamily: "'Cinzel', serif", fontSize: 11,
      letterSpacing: '0.06em', fontVariantNumeric: 'tabular-nums',
    }}>
      <span style={{
        display: 'inline-block', width: 7, height: 7, borderRadius: 999,
        background: lacking ? '#e34a4a' : color,
        boxShadow: `0 0 4px ${lacking ? '#e34a4a' : color}`,
      }} />
      <span style={{ color: lacking ? '#e34a4a' : '#e8e3d1' }}>{value}</span>
      <span style={{ color: '#9a9484' }}>{label}</span>
    </span>
  );
}

export function CulturePicker({ theme }) {
  const state = useBridge('cultureChoice', { active: false });
  const [open, setOpen] = React.useState(false);

  // If state changes such that we're no longer pickable, close any
  // open modal so the player isn't staring at stale options.
  React.useEffect(() => {
    if (!state?.active || state?.inProgress) setOpen(false);
  }, [state?.active, state?.inProgress]);

  if (!state || !state.active) return null;

  const cost  = state.cost  || {};
  const lack  = state.lacking || {};
  const ok    = state.available && state.canAfford;
  const tone  = ok ? theme.accent : theme.inlayDim;

  return (
    <>
      <div className="gv-culture-btn-wrap">
        {state.inProgress ? (
          // Progress display — replaces the clickable button while age-up runs.
          <div className="gv-culture-progress" style={{
            background: `linear-gradient(180deg, ${theme.base}, ${theme.baseEdge})`,
            border: `1px solid ${theme.inlay}`,
            boxShadow: `0 0 0 1px ${theme.inlayShadow}, 0 6px 16px rgba(0,0,0,0.55)`,
          }}>
            <div className="gv-culture-progress-label" style={{ color: theme.accent }}>
              Advancing · {state.ageUpCulture}
            </div>
            <div className="gv-culture-progress-bar" style={{ background: theme.inlayShadow }}>
              <div className="gv-culture-progress-fill" style={{
                width: `${(state.progress || 0) * 100}%`,
                background: `linear-gradient(90deg, ${theme.accent}aa, ${theme.accent})`,
                boxShadow: `0 0 6px ${theme.accent}88`,
              }} />
            </div>
            <div className="gv-culture-progress-sub" style={{ color: theme.textDim }}>
              {state.remaining}s remaining
            </div>
          </div>
        ) : (
          <button
            type="button"
            className={`gv-culture-btn ${ok ? '' : 'disabled'}`}
            onClick={() => ok && setOpen(true)}
            disabled={!ok}
            title={
              !state.available ? 'Requires a completed Shrine / Vault / Keep'
              : !state.canAfford ? 'Not enough resources'
              : 'Pick your Era 2 culture'
            }
            style={{
              background: `linear-gradient(180deg, ${theme.base}, ${theme.baseEdge})`,
              borderColor: tone,
              color: tone,
              boxShadow: ok
                ? `0 0 0 1px ${theme.inlayShadow}, 0 0 18px ${theme.accent}33, 0 6px 16px rgba(0,0,0,0.55)`
                : `0 0 0 1px ${theme.inlayShadow}, 0 6px 16px rgba(0,0,0,0.55)`,
            }}
          >
            <span className="gv-culture-btn-eyebrow" style={{ color: theme.textDim }}>Era 2</span>
            <span className="gv-culture-btn-label">Choose Culture</span>
            <span className="gv-culture-btn-meta">
              <CostChip label="Supplies" value={cost.supplies} lacking={lack.supplies} color={theme.accent} />
              <CostChip label="Iron"     value={cost.iron}     lacking={lack.iron}     color={theme.accent} />
              <CostChip label="Veilstone" value={cost.crystal} lacking={lack.crystal}  color={theme.accent} />
            </span>
          </button>
        )}
      </div>

      {open && (
        <div className="gv-culture-modal-scrim" onClick={() => setOpen(false)}>
          <div
            className="gv-culture-modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              background: `linear-gradient(180deg, ${theme.base}, ${theme.baseEdge})`,
              border: `1px solid ${theme.inlay}`,
              boxShadow: `0 0 0 1px ${theme.inlayShadow}, 0 16px 40px rgba(0,0,0,0.7)`,
            }}
          >
            <div className="gv-culture-modal-title" style={{ color: theme.accent }}>
              Choose Your Culture
            </div>
            <div className="gv-culture-modal-sub" style={{ color: theme.textDim }}>
              This decision locks your Era 2 identity. Spend {cost.supplies} Supplies · {cost.iron} Iron · {cost.crystal} Veilstone.
            </div>
            <div className="gv-culture-cards">
              {CULTURES.map((c) => (
                <button
                  key={c.key}
                  type="button"
                  className={`gv-culture-card ${c.comingSoon ? 'coming-soon' : ''}`}
                  onClick={() => {
                    if (c.comingSoon) return;
                    sendToUnity('culture:choose', { culture: c.key });
                    setOpen(false);
                  }}
                  disabled={c.comingSoon}
                  aria-disabled={c.comingSoon ? 'true' : 'false'}
                  title={c.comingSoon ? `${c.name} — coming soon` : undefined}
                  style={{
                    background: `linear-gradient(180deg, ${theme.baseMid}, ${theme.baseEdge})`,
                    borderColor: theme.inlay,
                    position: 'relative',
                    cursor: c.comingSoon ? 'not-allowed' : 'pointer',
                    opacity: c.comingSoon ? 0.55 : 1,
                  }}
                >
                  <div className="gv-culture-card-img" style={{
                    background: c.image
                      ? `url(${c.image}) center/cover no-repeat`
                      : `linear-gradient(135deg, ${c.palette[0]}cc, ${c.palette[1]}dd)`,
                  }}>
                    {!c.image && (
                      <div className="gv-culture-card-placeholder">
                        {c.name}
                      </div>
                    )}
                  </div>
                  <div className="gv-culture-card-body">
                    <div className="gv-culture-card-name" style={{ color: theme.accent }}>{c.name}</div>
                    <div className="gv-culture-card-blurb" style={{ color: theme.text }}>{c.blurb}</div>
                  </div>
                  {c.comingSoon && (
                    <div
                      style={{
                        position: 'absolute',
                        inset: 0,
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        background: 'rgba(0,0,0,0.55)',
                        pointerEvents: 'none',
                        borderRadius: 'inherit',
                      }}
                    >
                      <div
                        style={{
                          padding: '6px 18px',
                          background: 'rgba(0,0,0,0.82)',
                          border: `1px solid ${theme.accent}`,
                          color: theme.accent,
                          fontFamily: "'Cinzel', serif",
                          fontSize: 13,
                          letterSpacing: '0.18em',
                          textTransform: 'uppercase',
                          boxShadow: `0 0 12px ${theme.accent}55`,
                        }}
                      >
                        Coming Soon
                      </div>
                    </div>
                  )}
                </button>
              ))}
            </div>
            <button
              type="button"
              className="gv-culture-modal-close"
              onClick={() => setOpen(false)}
              style={{ color: theme.textDim }}
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </>
  );
}
