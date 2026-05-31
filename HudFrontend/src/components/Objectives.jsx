// Objectives panel — vertically centered between the menu button and the
// minimap rail on the left edge. Each row: icon + filled/unfilled pips + count.
//
// Bridge topic `objectives` (array of rows). Default = curse nodes + enemy
// players. Each row: { iconKey:'curse'|'enemy', name, current, total }.

import React from 'react';
import { useBridge } from '../bridge.js';

const DEFAULT_ROWS = [
  { iconKey: 'curse', name: 'Purify curse nodes', current: 0, total: 6 },
  { iconKey: 'enemy', name: 'Defeat enemy players', current: 0, total: 3 },
];

function ObjectiveIcon({ kind, color, dim }) {
  switch (kind) {
    case 'curse':
      return (
        <g transform="translate(11,11)">
          <circle r="7" fill="none" stroke={color} strokeWidth="1.3" />
          <circle r="3.5" fill="none" stroke={dim} strokeWidth="0.9" />
          <circle r="1.4" fill={color} />
          <g stroke={color} strokeWidth="1.3" strokeLinecap="round">
            <line x1="0" y1="-10" x2="0" y2="-8" />
            <line x1="0" y1="10"  x2="0" y2="8" />
            <line x1="-10" y1="0" x2="-8" y2="0" />
            <line x1="10" y1="0"  x2="8" y2="0" />
          </g>
        </g>
      );
    case 'enemy':
      return (
        <g transform="translate(11,11)">
          <path d="M -6 -1 Q -6 -7 0 -7 Q 6 -7 6 -1 L 6 3 Q 6 6 3 6 L -3 6 Q -6 6 -6 3 Z"
                fill={color} stroke="none" />
          <path d="M -6 -7 L -5 -9 L -3 -7 L -1 -10 L 1 -7 L 3 -10 L 5 -7 L 6 -7"
                fill="none" stroke={color} strokeWidth="1.2" strokeLinejoin="round" />
          <circle cx="-2.5" cy="-2" r="1.4" fill={dim} />
          <circle cx="2.5"  cy="-2" r="1.4" fill={dim} />
          <line x1="-3" y1="3.5" x2="3" y2="3.5" stroke={dim} strokeWidth="0.9" />
        </g>
      );
    default: return null;
  }
}

function ObjectiveRow({ theme, iconKey, name, current, total }) {
  return (
    <div className="gv-obj-row" title={name}>
      <div className="gv-obj-icon" style={{ filter: `drop-shadow(0 0 4px ${theme.accent}44)` }}>
        <svg width="18" height="18" viewBox="0 0 22 22">
          <ObjectiveIcon kind={iconKey} color={theme.accent} dim={theme.inlayShadow} />
        </svg>
      </div>
      <div className="gv-obj-pips">
        {Array.from({ length: total }, (_, i) => {
          const on = i < current;
          return (
            <span
              key={i}
              className={`gv-pip ${on ? 'on' : ''}`}
              style={{
                background: on ? theme.accent : 'transparent',
                borderColor: on ? theme.accent : theme.inlay,
                boxShadow: on ? `0 0 5px ${theme.accent}` : 'none',
              }}
            />
          );
        })}
      </div>
      <div className="gv-obj-count" style={{ color: current > 0 ? theme.accent : theme.textDim }}>
        {current}<span style={{ color: theme.textDim }}>/{total}</span>
      </div>
    </div>
  );
}

export function Objectives({ theme }) {
  const rows = useBridge('objectives', DEFAULT_ROWS);
  return (
    <div className="gv-objective">
      <div className="gv-obj-eyebrow" style={{ color: theme.textDim }}>Objectives</div>
      <div className="gv-obj-rule" style={{ background: `linear-gradient(90deg, transparent, ${theme.inlay}66, transparent)` }} />
      {rows.map((r, i) => (
        <ObjectiveRow key={i} theme={theme} iconKey={r.iconKey} name={r.name} current={r.current} total={r.total} />
      ))}
    </div>
  );
}
