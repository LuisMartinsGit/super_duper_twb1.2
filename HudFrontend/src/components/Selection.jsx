// Selection panel — matched to the resource panel's frame footprint.
// Renders an empty state, a single-entity detail, or a multi-selection grid
// depending on the data Unity pushes via the `selection` bridge topic.
//
// Payload shape:
//   null                               → empty state
//   {kind:'single', ...entityFields}   → SelectionDetail
//   {kind:'multi',  units:[{...}]}     → SelectionMulti
//
// Fields on `single`:
//   name, klass, portrait, portraitTone ('own'|'enemy'),
//   hp, hpMax, sh, shMax, cd, cdLabel,
//   atk:{kind,value}|null, def:{kind,value}|null, spd:{kind,value}|null,
//   entityKind: 'unit'|'building'|'resource',
//   yield:{perMinute,label}|null,
//   resource:{remaining,max,label}|null,
//   queue: [ {slotIndex,unitId,label,isInProduction,isActive,progress,refund}|null × 5 ],
//   queueCapacity: 5,
//   canUpgrade, upgradeCost, actions:[{id,label,enabled,cost,canAfford}]

import React, { useCallback, useRef, useState } from 'react';
import { useBridge, sendToUnity } from '../bridge.js';
import { FiligreeCorner, FiligreeEdge } from './Filigree.jsx';
import { ActionGlyph } from './Actions.jsx';

// ── Stat formatting helpers ─────────────────────────────────────────────
// `formatStatValue` returns a long-dash for missing/null component data so
// the player can tell "this entity has no Defense component" (—) apart from
// "this entity has 0 Defense" (0). Phase 2 ensures the bridge emits null
// for the missing-component case; optional chaining handles the rest.
function formatStatValue(v) {
  if (v == null) return '—'; // em-dash
  return v;
}

// ── Queue slot icon mapping ─────────────────────────────────────────────
// Maps the unitId emitted by HudBridge (TechTreeDB unit ids) to one of the
// ActionGlyph kinds defined in Actions.jsx. Mirrors the TRAIN_BARRACKS /
// TRAIN_ARCHERY / TRAIN_SHRINE catalog glyphs so a Swordsman queue slot
// looks identical to the Swordsman training card.
const QUEUE_GLYPH_BY_UNIT_ID = {
  Swordsman: 'helm',
  Scout: 'spear',
  Archer: 'arrow',
  // Task-110: Archery Range tier units (lvl 2 / lvl 3). Both reuse the
  // `arrow` glyph for v1 per the task brief; visual differentiation is
  // deferred to the art pass.
  Crossbowman: 'arrow',
  Longbowman: 'arrow',
  Builder: 'mason',
  Worker: 'mason',
  Miner: 'mason',
  Litharch: 'sigil',
};
function glyphForUnitId(unitId) {
  if (!unitId) return 'helm';
  return QUEUE_GLYPH_BY_UNIT_ID[unitId] || 'helm';
}

// ── Refund popup helpers ────────────────────────────────────────────────
// Compose the floating "+N supplies" string from the refund payload.
// Picks the largest non-zero resource — refunds are single-resource in
// practice (Train cost is dominantly one resource), but if a unit's cost
// spans multiple we display the biggest. Mirrors the IMGUI cancel toast.
function formatRefundText(refund) {
  if (!refund) return '';
  const RES = [
    ['supplies', 'supplies'],
    ['iron', 'iron'],
    ['crystal', 'crystal'],
    ['veilsteel', 'veilsteel'],
    ['glow', 'glow'],
  ];
  let bestKey = null;
  let bestVal = 0;
  for (let i = 0; i < RES.length; i++) {
    const k = RES[i][0];
    const v = refund[k] | 0;
    if (v > bestVal) { bestVal = v; bestKey = RES[i][1]; }
  }
  if (!bestKey) return '';
  return `+${bestVal} ${bestKey}`;
}

function PortraitIcon({ kind, fill, stroke }) {
  switch (kind) {
    case 'knight':
      return (
        <g>
          <path d="M 6 14 Q 6 4 16 4 Q 26 4 26 14 L 26 22 Q 26 26 22 26 L 10 26 Q 6 26 6 22 Z" fill={fill} stroke={stroke} strokeWidth="0.8" />
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
      return (
        <g>
          <circle cx="13" cy="10" r="4" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <path d="M 6 28 Q 6 16 13 16 Q 20 16 20 28 Z" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <line x1="22" y1="8" x2="22" y2="20" stroke={stroke} strokeWidth="1.3" strokeLinecap="round" />
          <rect x="19" y="6" width="6" height="4" fill={stroke} />
        </g>
      );
    case 'forge':
      return (
        <g>
          <rect x="6" y="18" width="20" height="6" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <rect x="11" y="22" width="10" height="4" fill={stroke} />
          <path d="M 8 18 Q 16 8 24 18 Z" fill={fill} stroke={stroke} strokeWidth="0.8" />
          <circle cx="22" cy="10" r="0.9" fill={stroke} />
          <circle cx="10" cy="12" r="0.7" fill={stroke} opacity="0.6" />
        </g>
      );
    case 'behemoth':
      return (
        <g>
          <path d="M 4 28 Q 4 14 10 12 L 10 8 L 14 4 L 14 10 L 18 10 L 18 4 L 22 8 L 22 12 Q 28 14 28 28 Z"
                fill={fill} stroke={stroke} strokeWidth="0.8" strokeLinejoin="round" />
          <circle cx="13" cy="16" r="1.2" fill={stroke} />
          <circle cx="19" cy="16" r="1.2" fill={stroke} />
        </g>
      );
    default:
      return <rect x="4" y="4" width="24" height="24" fill={fill} stroke={stroke} strokeWidth="0.8" />;
  }
}

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
        <div className="sel-bar-ticks" aria-hidden>
          {[0.25, 0.5, 0.75].map((p) => (
            <span key={p} style={{ left: `${p * 100}%`, background: 'rgba(0,0,0,0.6)' }} />
          ))}
        </div>
      </div>
    </div>
  );
}

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
          <path d="M 9 2 L 15 4 L 15 9 Q 15 14 9 16 Q 3 14 3 9 L 3 4 Z" fill="none" stroke={color} strokeWidth="1.3" strokeLinejoin="round" />
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

export function SelectionPanel({ theme }) {
  const sel = useBridge('selection', null);
  const lvl = theme.ornament;

  // Hide the entire panel when nothing is selected — the player asked for a
  // clean HUD with no permanent "no selection" frame eating screen space.
  if (!sel) return null;

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
        <div className="rc-corner rc-corner-tl"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-tr"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-bl"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        <div className="rc-corner rc-corner-br"><FiligreeCorner size={40} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
        {lvl !== 'minimal' && (
          <>
            <div className="rc-edge rc-edge-top"><FiligreeEdge width={220} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
            <div className="rc-edge rc-edge-bot"><FiligreeEdge width={220} height={20} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} level={lvl} /></div>
          </>
        )}

        {sel.kind === 'multi' && <SelectionMulti theme={theme} units={sel.units || []} />}
        {sel.kind === 'single' && <SelectionDetail theme={theme} sel={sel} />}
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
        Click a unit or building to inspect it
      </div>
    </div>
  );
}

function SelectionDetail({ theme, sel }) {
  const isEnemy = sel.portraitTone === 'enemy';
  const factionColor = isEnemy ? '#e34a4a' : '#4cb5e6';
  const hpColor = isEnemy ? '#e34a4a' : '#6fdb86';
  const shieldColor = '#7cc8e0';
  const cdColor = theme.accent;
  const showCD = sel.cd != null;
  const showUpgrade = !!sel.canUpgrade;
  // Battalions and same-type group selections carry a `count` field. Solos
  // omit it; we hide the badge unless the group is > 1.
  const showCount = (sel.count ?? 1) > 1;
  // Action progress (training a unit, upgrading the building). Drawn as
  // a white strip below the HP/Shield bars so the player can see at a
  // glance what the structure is busy with.
  const progress = sel.progress || null;

  return (
    <div className="sel-body">
      <div className="sel-header">
        <div className="sel-portrait" style={{
          background: `radial-gradient(ellipse at 50% 30%, ${theme.baseMid}, ${theme.baseEdge})`,
          boxShadow: `inset 0 0 0 1px ${theme.inlayShadow}, 0 0 0 1px ${theme.inlay}66, 0 0 12px ${factionColor}33`,
        }}>
          <svg width="32" height="32" viewBox="0 0 32 32">
            <PortraitIcon kind={sel.portrait} fill={isEnemy ? '#1a0606' : '#06121b'} stroke={factionColor} />
          </svg>
          <span className="sel-portrait-faction" style={{ background: factionColor, boxShadow: `0 0 4px ${factionColor}` }} />
          {showCount && (
            <span className="sel-portrait-count" style={{
              position: 'absolute', right: 0, bottom: 0,
              background: theme.baseEdge, color: theme.accent,
              padding: '1px 5px', fontSize: 11, lineHeight: '12px',
              borderRadius: 3,
              boxShadow: `0 0 0 1px ${theme.inlay}88`,
            }}>×{sel.count}</span>
          )}
        </div>
        <div className="sel-header-text">
          <div className="sel-name" style={{ color: theme.accent }} title={sel.yield ? (sel.yield.perMinute === 0 ? 'Drop-off depot' : `Yield: ${Math.round(sel.yield.perMinute)} ${sel.yield.label || 'supplies/min'}`) : undefined}>{sel.name}</div>
          <div className="sel-class" style={{ color: theme.textDim }}>
            {sel.klass}{showCount ? ` · group of ${sel.count}` : ''}
          </div>
        </div>
      </div>

      <div className="sel-bars">
        {/* Resource nodes (iron deposit / cadaver) hide the green Health bar entirely —
            the amber depletion bar below takes its place. Phase 5 mirrors this in the
            world-space FloatingHealthBars overlay. */}
        {sel.entityKind !== 'resource' && (
          <StatBar label="Health" value={sel.hp} max={sel.hpMax} color={hpColor} dim={theme.inlay} accent={theme.accent} />
        )}
        {sel.shMax > 0 && sel.entityKind !== 'resource' && (
          <StatBar label="Shield" value={sel.sh} max={sel.shMax} color={shieldColor} dim={theme.inlay} accent={theme.accent} />
        )}
        {showCD && (
          <StatBar label={`Cooldown · ${sel.cdLabel}`} value={sel.cd * 100} max={100}
                   color={cdColor} dim={theme.inlay} accent={theme.accent} glow
                   sub={sel.cd === 0 ? 'Ready' : `${Math.round((1 - sel.cd) * 100)}%`} />
        )}
        {progress && (
          <StatBar
            label={progress.label || 'Working'}
            value={Math.round((progress.ratio || 0) * 100)}
            max={100}
            color={'#ffffff'}
            dim={theme.inlay}
            accent={theme.accent}
            sub={`${Math.round((progress.ratio || 0) * 100)}%`}
          />
        )}

        {/* Yield is surfaced via the entity header's native tooltip
            (sel-name title attribute) — the in-panel row was too small to
            read at HUD scale. */}

        {/* Resource remaining row — iron deposits and other depletable nodes.
            Renders an amber bar that mirrors the world-space depletion overlay
            (Phase 5). When depleted (remaining == 0) the bar collapses to zero
            width and the label switches to "DEPLETED". */}
        {sel.entityKind === 'resource' && sel.resource && (
          <ResourceRemainingBar theme={theme} resource={sel.resource} />
        )}

        {/* Wall segment summary line (task-109 phase 6). Shown when the
            selected entity is a wall instance — surfaces the parent
            segment's instance count and any in-flight conversion timer
            so the player can see at a glance how wide the resulting
            gate will be (5 for normal segments, fewer for short
            segments). The conversion progress is also surfaced in the
            Actions panel; this is the Selection-side mirror. */}
        {sel.wall && (sel.wall.kind === 'instance' || sel.wall.kind === 'converting') && (
          <div className="sel-bar sel-bar-yield" style={{ marginTop: 2 }}>
            <div className="sel-bar-label">
              <span className="sel-bar-name" style={{ color: theme.inlay }}>SEGMENT</span>
              <span className="sel-bar-val" style={{ color: theme.accent }}>
                {sel.wall.segmentInstanceCount > 0
                  ? `${sel.wall.segmentInstanceCount} instances · gate width ${sel.wall.gateWidth}`
                  : 'no parent segment'}
              </span>
            </div>
            {sel.wall.shortSegment && (
              <div className="sel-bar-label" style={{ marginTop: 1 }}>
                <span className="sel-bar-name" style={{ color: '#d97a2e' }}>SHORT</span>
                <span className="sel-bar-val" style={{ color: '#d97a2e', opacity: 0.85 }}>
                  battalions wider than {sel.wall.gateWidth} may not fit
                </span>
              </div>
            )}
          </div>
        )}
      </div>

      <div className="sel-stats" data-entity-kind={sel.entityKind || 'unit'}>
        <StatCell glyph="attack"  value={formatStatValue(sel.atk?.value)} kind={sel.atk?.kind ?? '—'} theme={theme} />
        <div className="sel-stat-div sel-stat-div-1" style={{ background: `linear-gradient(180deg, transparent, ${theme.inlay}88, transparent)` }} />
        <StatCell glyph="defense" value={formatStatValue(sel.def?.value)} kind={sel.def?.kind ?? '—'} theme={theme} />
        <div className="sel-stat-div sel-stat-div-2" style={{ background: `linear-gradient(180deg, transparent, ${theme.inlay}88, transparent)` }} />
        <div className="sel-stat sel-stat-speed">
          <div className="sel-stat-glyph" style={{ color: theme.accent, filter: `drop-shadow(0 0 4px ${theme.accent}33)` }}>
            <StatGlyph kind="speed" color={theme.accent} dim={theme.inlay} />
          </div>
          <div className="sel-stat-text">
            <div className="sel-stat-value" style={{ color: theme.text }}>{formatStatValue(sel.spd?.value)}</div>
            <div className="sel-stat-kind" style={{ color: theme.textDim }}>{sel.spd?.kind ?? '—'}</div>
          </div>
        </div>
      </div>

      {/* Production queue — 5-slot strip for buildings with a TrainingState.
          Empty array (no TrainingState) renders nothing; populated array always
          renders 5 cells with right-click cancel on populated slots. */}
      {Array.isArray(sel.queue) && sel.queue.length > 0 && (
        <QueueStrip theme={theme} queue={sel.queue} selId={sel.id} />
      )}

      {showUpgrade && (
        <button
          type="button"
          className="sel-upgrade"
          onClick={() => sendToUnity('selection:upgrade', { id: sel.id })}
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

function SelectionMulti({ theme, units }) {
  const totalUnits = units.reduce((a, b) => a + (b.count || 1), 0);
  return (
    <div className="sel-body sel-multi">
      <div className="sel-header sel-multi-header">
        <div className="sel-multi-count" style={{ color: theme.accent }}>{totalUnits}</div>
        <div className="sel-header-text">
          <div className="sel-name" style={{ color: theme.accent }}>Mixed Detachment</div>
          <div className="sel-class" style={{ color: theme.textDim }}>{units.length} unit types</div>
        </div>
      </div>
      <div className="sel-multi-grid">
        {units.map((u) => (
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
                  width: `${(u.hp ?? 1) * 100}%`,
                  height: '100%',
                  background: `linear-gradient(90deg, #6fdb86aa, #6fdb86)`,
                  boxShadow: `0 0 4px #6fdb86`,
                }} />
              </div>
            </div>
            <div className="sel-multi-tag" style={{ color: theme.accent }}>×{u.count || 1}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Resource remaining bar ──────────────────────────────────────────────
// Amber bar that mirrors the world-space depletion overlay added in Phase 5.
// Same #d97a2e color token in both places so the player learns the visual
// vocabulary: "amber = depleting resource", distinct from green Health and
// from the white training progress strip.
function ResourceRemainingBar({ theme, resource }) {
  const remaining = resource.remaining | 0;
  const max = Math.max(1, resource.max | 0);
  const pct = Math.max(0, Math.min(1, remaining / max));
  const depleted = remaining <= 0;
  return (
    <div className="sel-bar sel-bar-resource">
      <div className="sel-bar-label">
        <span className="sel-bar-name" style={{ color: theme.inlay }}>REMAINING</span>
        <span className="sel-bar-val" style={{ color: theme.accent }}>
          {depleted ? 'DEPLETED' : `${remaining} / ${resource.max}`}
        </span>
      </div>
      <div className="sel-bar-track" style={{ background: 'rgba(0,0,0,0.55)', boxShadow: `inset 0 0 0 1px ${theme.inlay}55` }}>
        <div className="sel-bar-resource__fill sel-bar-fill" style={{ width: `${pct * 100}%` }} />
      </div>
    </div>
  );
}

// ── Training queue strip ────────────────────────────────────────────────
// 5-slot row rendered below the stats grid for any building with an active
// TrainingState. Slot 0 carries an in-progress white bar driven by
// slot.progress (0..1); slots 1-4 are full-but-greyed glyphs to communicate
// "queued but not started". Empty slots are minimal placeholders.
//
// Right-click on a populated slot fires `actions:cancelTrain` (lockstep-safe
// via CommandRouter.IssueCancelTrain) and triggers two transient effects:
//   - A "+N supplies" popup that rises 12px and fades over 700ms.
//   - A 500ms shimmer on the cancelled slot before the next selection push
//     clears it.
// Left-click is intentionally a no-op (per spec — communicates that the slot
// is non-interactive except via right-click cancel).
function QueueStrip({ theme, queue, selId }) {
  // Transient UI state for the refund popup. Each entry is unique by `id`
  // and self-removes via onAnimationEnd. Bounded to ~5 in practice.
  const [popups, setPopups] = useState([]);
  // Transient shimmer ticks — when a slot is right-clicked, record the slot
  // index + a unique tick so the CSS animation re-runs cleanly across rapid
  // re-clicks of the same slot (key-based remount via tick).
  const [shimmer, setShimmer] = useState({}); // { [slotIndex]: tick }
  const popupSeq = useRef(0);

  const slotZeroLive = queue[0]?.isInProduction === true;
  const eyebrow = slotZeroLive ? 'IN PRODUCTION' : 'QUEUE';

  const handleContextMenu = useCallback((e, slot, slotIndex) => {
    e.preventDefault();
    e.stopPropagation();
    if (!slot) return;

    // 1) Server-authoritative cancel — lockstep-safe.
    sendToUnity('actions:cancelTrain', { buildingId: selId, slotIndex });

    // 2) Client-side feedback only — refund popup at the click site.
    //    The actual resource credit happens in CancelTrainCommandHelper.
    const rect = e.currentTarget.getBoundingClientRect();
    const parentRect = e.currentTarget.parentElement.getBoundingClientRect();
    const text = formatRefundText(slot.refund);
    if (text) {
      const id = ++popupSeq.current;
      const left = rect.left - parentRect.left + rect.width / 2;
      const top = rect.top - parentRect.top;
      setPopups((p) => [...p, { id, text, left, top }]);
    }

    // 3) Shimmer the slot — keyed by tick so a second rapid click restarts the animation.
    setShimmer((s) => ({ ...s, [slotIndex]: (s[slotIndex] || 0) + 1 }));
  }, [selId]);

  const handleLeftClick = useCallback((e) => {
    // No-op. Tooltip is conveyed via aria-label + title attr (browser native).
    e.stopPropagation();
  }, []);

  const removePopup = useCallback((id) => {
    setPopups((p) => p.filter((x) => x.id !== id));
  }, []);

  return (
    <div className="sel-queue" onContextMenu={(e) => e.stopPropagation()}>
      <div className="sel-queue-head">
        <span className="sel-queue-eyebrow" style={{ color: theme.accent }}>{eyebrow}</span>
        <span className="sel-queue-rule" style={{ background: `${theme.inlay}55` }} />
      </div>
      <div className="sel-queue-row" style={{ position: 'relative' }}>
        {queue.map((slot, i) => {
          const populated = !!slot;
          const inProd = populated && slot.isInProduction === true;
          const tick = shimmer[i] || 0;
          // Glyph dims down for queued slots vs. in-production slot to communicate
          // "this one is live, those are waiting" without a separate progress bar.
          const glyphColor = inProd ? theme.accent : theme.inlay;
          const label = populated
            ? `${slot.label || slot.unitId || 'Unit'}${
                inProd ? ` (${Math.round((slot.progress || 0) * 100)}% complete)` : ''
              } — right-click to cancel`
            : null;

          return (
            <div
              key={`${i}-${tick}`}
              className={`sel-queue-slot${populated ? ' active' : ' sel-queue-slot-empty'}${
                inProd ? ' sel-queue-slot--inprod' : ''
              }${populated ? '' : ''}`}
              data-shimmer={tick > 0 ? 'true' : undefined}
              style={{
                borderColor: populated ? theme.accent : theme.inlayShadow,
                cursor: populated ? 'not-allowed' : 'default',
                background: populated ? 'rgba(0,0,0,0.35)' : 'rgba(0,0,0,0.35)',
                opacity: populated && !inProd ? 0.55 : 1, // "full-but-greyed" for queued
              }}
              onClick={handleLeftClick}
              onContextMenu={(e) => handleContextMenu(e, slot, i)}
              aria-label={label}
              aria-hidden={populated ? undefined : true}
              title={label || undefined}
            >
              {populated ? (
                <svg width="22" height="22" viewBox="-12 -12 24 24" aria-hidden>
                  <ActionGlyph kind={glyphForUnitId(slot.unitId)} color={glyphColor} dim={theme.inlayDim} />
                </svg>
              ) : (
                <span className="sel-queue-slot-dot" style={{ background: theme.inlayDim }} />
              )}
              {populated && (
                <div className="sel-queue-progress" aria-hidden>
                  {/* Slot 0 in-production: animated white fill driven by progress.
                      Queued slots (1-4): full-but-greyed bar to communicate "ready
                      to start but waiting" without confusing the player into thinking
                      multiple slots are mid-production. */}
                  <div
                    style={{
                      width: inProd ? `${Math.max(0, Math.min(1, slot.progress || 0)) * 100}%` : '100%',
                      background: inProd ? '#fff' : `${theme.inlayDim}aa`,
                    }}
                  />
                </div>
              )}
            </div>
          );
        })}

        {/* Refund popups — absolute-positioned floats inside the row container.
            One per right-click; self-removes via animationend. */}
        {popups.map((p) => (
          <div
            key={p.id}
            className="sel-queue-refund"
            style={{ left: p.left, top: p.top, color: theme.accent }}
            onAnimationEnd={() => removePopup(p.id)}
          >
            {p.text}
          </div>
        ))}
      </div>
    </div>
  );
}
