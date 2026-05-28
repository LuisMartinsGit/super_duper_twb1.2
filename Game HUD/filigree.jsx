// filigree.jsx — reusable ornate SVG flourishes for HUD frames.
// All pieces are pure geometry: arcs, lines, small circles arranged
// symmetrically. They scale by ornament level so the panels still
// read clearly in minimal/balanced modes.

// Corner scrollwork — designed to occupy a 60×60 bounding box anchored at
// (0,0) top-left, with the "inside" of the panel toward bottom-right.
// Built from concentric arcs + dot studs.
function FiligreeCorner({ size = 60, color = '#cfd6d3', accent = '#e8b84a', dim = '#7f8e8a', level = 'maximal' }) {
  const W = size;
  // Strokes scale with ornament level
  const sw = level === 'minimal' ? 1.0 : level === 'balanced' ? 1.2 : 1.4;
  const showInner = level !== 'minimal';
  const showFloret = level === 'maximal';
  return (
    <svg width={W} height={W} viewBox="0 0 60 60" style={{ display: 'block', pointerEvents: 'none' }}>
      <g fill="none" stroke={color} strokeWidth={sw} strokeLinecap="round">
        {/* Big outer scroll */}
        <path d="M 2 30 Q 2 2 30 2" />
        {/* Mid arc */}
        {showInner && <path d="M 8 30 Q 8 8 30 8" stroke={dim} strokeWidth={sw * 0.7} />}
        {/* Inner curl that turns back on itself */}
        <path d="M 30 2 Q 18 14 26 22 Q 32 28 22 30" />
        <path d="M 2 30 Q 14 18 22 26 Q 28 32 30 22" />
        {/* Spurs */}
        <line x1="30" y1="2" x2="30" y2="6" />
        <line x1="2" y1="30" x2="6" y2="30" />
        {showFloret && (
          <>
            <circle cx="14" cy="14" r="1.6" fill={accent} stroke="none" />
            <path d="M 11 17 Q 14 14 17 11" stroke={dim} strokeWidth={sw * 0.6} />
            <path d="M 7 23 Q 9 21 11 21" />
            <path d="M 23 7 Q 21 9 21 11" />
          </>
        )}
        {/* Tiny terminal studs */}
        <circle cx="22" cy="30" r="1.1" fill={accent} stroke="none" />
        <circle cx="30" cy="22" r="1.1" fill={accent} stroke="none" />
      </g>
    </svg>
  );
}

// Horizontal edge ornament — symmetric scrollwork that tiles along an edge.
// 120×24 viewBox. Used along the top of the resource panel.
function FiligreeEdge({ width = 240, height = 24, color = '#cfd6d3', accent = '#e8b84a', dim = '#7f8e8a', level = 'maximal' }) {
  const sw = level === 'minimal' ? 0.9 : level === 'balanced' ? 1.1 : 1.3;
  return (
    <svg width={width} height={height} viewBox="0 0 120 24" preserveAspectRatio="none" style={{ display: 'block', pointerEvents: 'none' }}>
      <g fill="none" stroke={color} strokeWidth={sw} strokeLinecap="round">
        {/* central diamond gem */}
        <path d="M 60 4 L 66 12 L 60 20 L 54 12 Z" stroke={accent} strokeWidth={sw} />
        {level === 'maximal' && <path d="M 60 8 L 63 12 L 60 16 L 57 12 Z" stroke={dim} strokeWidth={sw * 0.6} />}
        {/* symmetric scrolls each side */}
        <path d="M 54 12 Q 44 12 42 6" />
        <path d="M 66 12 Q 76 12 78 6" />
        <path d="M 54 12 Q 44 12 42 18" />
        <path d="M 66 12 Q 76 12 78 18" />
        {level !== 'minimal' && (
          <>
            <path d="M 42 6 Q 38 6 36 10" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 78 6 Q 82 6 84 10" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 42 18 Q 38 18 36 14" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 78 18 Q 82 18 84 14" stroke={dim} strokeWidth={sw * 0.7} />
          </>
        )}
        {/* end terminations */}
        <path d="M 18 12 Q 26 12 30 6" />
        <path d="M 102 12 Q 94 12 90 6" />
        <path d="M 18 12 Q 26 12 30 18" />
        <path d="M 102 12 Q 94 12 90 18" />
        <circle cx="30" cy="6" r="1.2" fill={accent} stroke="none" />
        <circle cx="90" cy="6" r="1.2" fill={accent} stroke="none" />
        <circle cx="30" cy="18" r="1.2" fill={accent} stroke="none" />
        <circle cx="90" cy="18" r="1.2" fill={accent} stroke="none" />
        {level === 'maximal' && (
          <>
            <path d="M 6 12 Q 12 6 18 12" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 114 12 Q 108 6 102 12" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 6 12 Q 12 18 18 12" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 114 12 Q 108 18 102 12" stroke={dim} strokeWidth={sw * 0.7} />
          </>
        )}
      </g>
    </svg>
  );
}

// Cartouche / medallion — a small diamond medallion with rays. Lives at the
// crown of the minimap frame.
function FiligreeMedallion({ size = 60, color, accent, dim, gem = '#1d6a55', gemHi = '#3fbf9a', level = 'maximal', label }) {
  const sw = level === 'minimal' ? 1.0 : level === 'balanced' ? 1.2 : 1.4;
  return (
    <svg width={size} height={size} viewBox="0 0 60 60" style={{ display: 'block', pointerEvents: 'none' }}>
      <defs>
        <radialGradient id="medGem" cx="50%" cy="40%" r="60%">
          <stop offset="0%" stopColor={gemHi} stopOpacity="1" />
          <stop offset="60%" stopColor={gem} stopOpacity="1" />
          <stop offset="100%" stopColor="#000" stopOpacity="0.8" />
        </radialGradient>
      </defs>
      <g fill="none" stroke={color} strokeWidth={sw} strokeLinecap="round">
        {/* outer diamond */}
        <path d="M 30 4 L 50 30 L 30 56 L 10 30 Z" />
        {/* inner gem */}
        <path d="M 30 12 L 42 30 L 30 48 L 18 30 Z" fill="url(#medGem)" />
        {/* gem facets */}
        <path d="M 30 12 L 30 48" stroke={gemHi} strokeWidth={sw * 0.5} opacity="0.6" />
        <path d="M 18 30 L 42 30" stroke={gemHi} strokeWidth={sw * 0.5} opacity="0.6" />
        {/* radiating spurs */}
        {level !== 'minimal' && (
          <>
            <line x1="30" y1="0" x2="30" y2="3" stroke={accent} />
            <line x1="30" y1="57" x2="30" y2="60" stroke={accent} />
            <line x1="0" y1="30" x2="3" y2="30" stroke={accent} />
            <line x1="57" y1="30" x2="60" y2="30" stroke={accent} />
          </>
        )}
        {level === 'maximal' && (
          <>
            <path d="M 30 4 Q 22 10 18 14" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 30 4 Q 38 10 42 14" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 30 56 Q 22 50 18 46" stroke={dim} strokeWidth={sw * 0.7} />
            <path d="M 30 56 Q 38 50 42 46" stroke={dim} strokeWidth={sw * 0.7} />
          </>
        )}
      </g>
      {label && (
        <text x="30" y="33" textAnchor="middle" fontSize="8" fontFamily="'Cinzel', serif"
              fontWeight="700" letterSpacing="1" fill={accent}
              style={{ filter: 'drop-shadow(0 0 2px rgba(0,0,0,0.6))' }}>
          {label}
        </text>
      )}
    </svg>
  );
}

// Small icon disc — circular ornate frame holding a resource glyph.
function IconDisc({ size = 44, color, dim, accent, level = 'maximal', children }) {
  const sw = level === 'minimal' ? 1.0 : level === 'balanced' ? 1.2 : 1.4;
  const R = size / 2;
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ display: 'block' }}>
      <defs>
        <radialGradient id={`disc-${size}-${color?.slice(1)}`} cx="50%" cy="40%" r="65%">
          <stop offset="0%" stopColor="#000" stopOpacity="0" />
          <stop offset="80%" stopColor="#000" stopOpacity="0.4" />
          <stop offset="100%" stopColor="#000" stopOpacity="0.8" />
        </radialGradient>
      </defs>
      {/* inset shadow disc */}
      <circle cx={R} cy={R} r={R - 2} fill={`url(#disc-${size}-${color?.slice(1)})`} />
      {/* outer ring */}
      <circle cx={R} cy={R} r={R - 2} fill="none" stroke={color} strokeWidth={sw} />
      {/* inner ring */}
      {level !== 'minimal' && (
        <circle cx={R} cy={R} r={R - 5} fill="none" stroke={dim} strokeWidth={sw * 0.6} />
      )}
      {/* four cardinal studs */}
      {level === 'maximal' && (
        <g fill={accent}>
          <circle cx={R} cy={2} r={1.2} />
          <circle cx={R} cy={size - 2} r={1.2} />
          <circle cx={2} cy={R} r={1.2} />
          <circle cx={size - 2} cy={R} r={1.2} />
        </g>
      )}
      {children}
    </svg>
  );
}

Object.assign(window, { FiligreeCorner, FiligreeEdge, FiligreeMedallion, IconDisc });
