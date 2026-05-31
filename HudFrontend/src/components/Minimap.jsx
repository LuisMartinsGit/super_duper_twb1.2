// Minimap CHROME ONLY. The diamond backplate, 4 medallions, and legend are
// rendered by CEF; the actual gameplay minimap (terrain / units / buildings /
// viewport rect / fog) is rendered by Unity's legacy `MinimapRenderer` on a
// canvas layered above CEF, inscribed inside the diamond hole.
//
// This file used to hold an SVG-rendered minimap that consumed the `minimap`
// bridge topic. That topic is no longer pushed.

import React from 'react';
import { FiligreeMedallion } from './Filigree.jsx';

export function Minimap({ theme }) {
  const lvl = theme.ornament;

  return (
    <div className="mm-root" style={{
      '--mm-accent': theme.accent,
      '--mm-inlay': theme.inlay,
      '--mm-text': theme.text,
    }}>
      <div className="mm-frame">
        {/* Diamond backplate — engraved metal */}
        <svg className="mm-back" viewBox="0 0 320 320" width="272" height="272">
          <defs>
            <radialGradient id={`back-${theme.key}`} cx="50%" cy="42%" r="60%">
              <stop offset="0%" stopColor={theme.baseMid} />
              <stop offset="70%" stopColor={theme.base} />
              <stop offset="100%" stopColor={theme.baseEdge} />
            </radialGradient>
            <linearGradient id={`bezel-${theme.key}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={theme.inlay} stopOpacity="0.95" />
              <stop offset="50%" stopColor={theme.inlayDim} stopOpacity="0.9" />
              <stop offset="100%" stopColor={theme.inlay} stopOpacity="0.95" />
            </linearGradient>
          </defs>
          <polygon points="160,4 316,160 160,316 4,160"
                   fill={`url(#back-${theme.key})`}
                   stroke={`url(#bezel-${theme.key})`} strokeWidth="2" />
          <polygon points="160,14 306,160 160,306 14,160" fill="none" stroke={theme.inlayShadow} strokeWidth="1" opacity="0.9" />
          <polygon points="160,18 302,160 160,302 18,160" fill="none" stroke={theme.inlay} strokeWidth="0.7" opacity="0.5" />
          {lvl !== 'minimal' && (
            <g fill={theme.accent} stroke={theme.inlayShadow} strokeWidth="0.5">
              <circle cx="160" cy="4" r="3" />
              <circle cx="316" cy="160" r="3" />
              <circle cx="160" cy="316" r="3" />
              <circle cx="4" cy="160" r="3" />
            </g>
          )}
        </svg>

        {/* Decorative medallions at the 4 diamond tips. Top is larger to
            keep the existing visual hierarchy; no compass label because the
            map is rotated 45° — game-N would sit at the diamond tip-NE, not
            tip-N, so labelling would mislead the player. */}
        <div className="mm-tip mm-tip-top"><FiligreeMedallion size={48} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi} level={lvl} /></div>
        <div className="mm-tip mm-tip-right"><FiligreeMedallion size={30} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi} level={lvl} /></div>
        <div className="mm-tip mm-tip-bottom"><FiligreeMedallion size={30} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi} level={lvl} /></div>
        <div className="mm-tip mm-tip-left"><FiligreeMedallion size={30} color={theme.inlay} dim={theme.inlayDim} accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi} level={lvl} /></div>

        <div className="mm-legend" style={{ color: theme.textDim }}>
          <span><i style={{ background: '#4cb5e6' }} /> Allied</span>
          <span><i style={{ background: '#e34a4a' }} /> Hostile</span>
          <span><i style={{ background: theme.accent }} /> Resource</span>
        </div>
      </div>
    </div>
  );
}
