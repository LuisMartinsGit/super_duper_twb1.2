// minimap.jsx — Diamond-shaped (square rotated 45°) RTS minimap.
//
// World-space coords are 0..1. The whole world group is rotated 45° and
// scaled to fill the diamond viewport, so a unit-square world fills the
// diamond exactly. Click-to-pan, right-click to ping, zoom buttons resize
// the viewport rect, friendly/enemy unit dots wander live.

// ── World data (shared across all themes) ────────────────────────────────
// All coords in world units (0..1). Polygons are arrays of [x, y].
const WORLD = {
  // Big static terrain regions
  terrain: [
    { type: 'water',    poly: [[0,0.55],[0.18,0.45],[0.32,0.6],[0.20,0.78],[0,0.85]] },
    { type: 'water',    poly: [[0.62,0.92],[0.78,0.86],[0.96,0.92],[0.96,1],[0.55,1]] },
    { type: 'forest',   poly: [[0.18,0.05],[0.42,0.04],[0.50,0.18],[0.38,0.30],[0.20,0.22]] },
    { type: 'forest',   poly: [[0.70,0.18],[0.92,0.10],[0.96,0.32],[0.84,0.40],[0.72,0.30]] },
    { type: 'forest',   poly: [[0.05,0.30],[0.16,0.28],[0.20,0.42],[0.10,0.46]] },
    { type: 'mountain', poly: [[0.45,0.40],[0.62,0.36],[0.70,0.48],[0.62,0.58],[0.48,0.56]] },
    { type: 'mountain', poly: [[0.80,0.50],[0.94,0.50],[0.96,0.66],[0.84,0.66]] },
    { type: 'mountain', poly: [[0.04,0.86],[0.18,0.88],[0.20,0.96],[0.06,0.96]] },
    { type: 'sand',     poly: [[0.32,0.62],[0.46,0.62],[0.48,0.74],[0.34,0.74]] },
    { type: 'sand',     poly: [[0.55,0.74],[0.68,0.72],[0.70,0.82],[0.58,0.84]] },
  ],
  resourceNodes: [
    { x: 0.22, y: 0.10, kind: 'wood' },
    { x: 0.30, y: 0.20, kind: 'wood' },
    { x: 0.82, y: 0.22, kind: 'wood' },
    { x: 0.55, y: 0.46, kind: 'stone' },
    { x: 0.60, y: 0.52, kind: 'stone' },
    { x: 0.86, y: 0.58, kind: 'stone' },
    { x: 0.12, y: 0.92, kind: 'stone' },
    { x: 0.40, y: 0.66, kind: 'gold' },
    { x: 0.62, y: 0.78, kind: 'gold' },
    { x: 0.74, y: 0.34, kind: 'gold' },
    { x: 0.28, y: 0.40, kind: 'gold' },
  ],
  // Buildings: faction 'self' (player) or 'enemy' or 'neutral'
  buildings: [
    { x: 0.28, y: 0.86, w: 0.05, h: 0.05, faction: 'self', kind: 'keep' },
    { x: 0.36, y: 0.88, w: 0.025, h: 0.025, faction: 'self' },
    { x: 0.34, y: 0.80, w: 0.025, h: 0.025, faction: 'self' },
    { x: 0.40, y: 0.84, w: 0.025, h: 0.025, faction: 'self' },
    { x: 0.20, y: 0.88, w: 0.025, h: 0.025, faction: 'self' },
    { x: 0.80, y: 0.16, w: 0.045, h: 0.045, faction: 'enemy', kind: 'keep' },
    { x: 0.86, y: 0.08, w: 0.022, h: 0.022, faction: 'enemy' },
    { x: 0.74, y: 0.10, w: 0.022, h: 0.022, faction: 'enemy' },
    { x: 0.88, y: 0.22, w: 0.022, h: 0.022, faction: 'enemy' },
    { x: 0.55, y: 0.05, w: 0.025, h: 0.025, faction: 'neutral' },
  ],
  // Fog of war reveal sources (explored circles)
  reveal: [
    { x: 0.30, y: 0.85, r: 0.22 },     // around player keep
    { x: 0.42, y: 0.62, r: 0.10 },     // outpost / scout
    { x: 0.55, y: 0.50, r: 0.08 },
    { x: 0.78, y: 0.18, r: 0.14 },     // scouted enemy
  ],
};

// Initial unit fleet (positions in world coords)
function makeUnits(seed = 1) {
  const rng = mulberry32(seed);
  const friendly = [];
  for (let i = 0; i < 12; i++) {
    const ang = rng() * Math.PI * 2;
    friendly.push({
      x: 0.30 + (rng() - 0.5) * 0.16,
      y: 0.84 + (rng() - 0.5) * 0.16,
      vx: Math.cos(ang) * 0.012,
      vy: Math.sin(ang) * 0.012,
      group: rng() > 0.6 ? 'patrol' : 'base',
    });
  }
  const enemy = [];
  for (let i = 0; i < 7; i++) {
    const ang = rng() * Math.PI * 2;
    enemy.push({
      x: 0.80 + (rng() - 0.5) * 0.14,
      y: 0.18 + (rng() - 0.5) * 0.14,
      vx: Math.cos(ang) * 0.014,
      vy: Math.sin(ang) * 0.014,
    });
  }
  // a scouting squad heading toward the middle
  for (let i = 0; i < 3; i++) {
    friendly.push({
      x: 0.50 + (rng() - 0.5) * 0.04,
      y: 0.55 + (rng() - 0.5) * 0.04,
      vx: 0.020 * (rng() - 0.5),
      vy: -0.018,
      group: 'scout',
    });
  }
  return { friendly, enemy };
}

function mulberry32(a) {
  return function () {
    let t = (a += 0x6d2b79f5);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// World → screen (in our 100×100 SVG viewBox)
function worldToScreen(wx, wy) {
  // applied transform is translate(50,0) rotate(45) scale(50√2)
  // → s = T·R·S·w, where w=(wx, wy)
  const S = Math.SQRT2 * 50;
  const sx = (wx - wy) * (S / Math.SQRT2) + 50;
  const sy = (wx + wy) * (S / Math.SQRT2);
  return [sx, sy];
}

// Screen (in 100×100 viewBox) → world (0..1)
function screenToWorld(sx, sy) {
  const wx = ((sx - 50) + sy) / 100;
  const wy = (sy - (sx - 50)) / 100;
  return [wx, wy];
}

function inDiamond(sx, sy) {
  return Math.abs(sx - 50) + Math.abs(sy - 50) <= 50.001;
}

// ── Minimap component ────────────────────────────────────────────────────
function Minimap({ theme }) {
  const lvl = theme.ornament;
  const T = theme.terrain;

  // Unit fleet (mutable in ref, displayed via tick state)
  const fleetRef = React.useRef(makeUnits(7));
  const [, force] = React.useReducer((x) => x + 1, 0);

  // Viewport (camera) rect in world coords
  const [vp, setVp] = React.useState({ cx: 0.32, cy: 0.78, w: 0.22, h: 0.18 });
  const vpTargetRef = React.useRef({ cx: 0.32, cy: 0.78 });

  // Map rotation (radians, around screen center).
  const [rot, setRot] = React.useState(0);
  const rotTargetRef = React.useRef(0);

  // Pings — list of {wx, wy, t0}
  const pingsRef = React.useRef([]);

  // SVG ref + click → world conversion using getBoundingClientRect
  const svgRef = React.useRef(null);

  React.useEffect(() => {
    let raf;
    let last = performance.now();
    const tick = (t) => {
      const dt = Math.min(0.05, (t - last) / 1000); last = t;
      const fleet = fleetRef.current;
      const stepUnit = (u, restrictY) => {
        u.x += u.vx * dt;
        u.y += u.vy * dt;
        // Random drift
        if (Math.random() < 0.02) {
          const a = Math.random() * Math.PI * 2;
          const s = Math.hypot(u.vx, u.vy);
          u.vx = Math.cos(a) * s;
          u.vy = Math.sin(a) * s;
        }
        // Soft confinement — friendly stays bottom-left quadrant-ish,
        // enemy stays top-right.
        const cx = restrictY === 'south' ? 0.32 : 0.78;
        const cy = restrictY === 'south' ? 0.84 : 0.18;
        const dx = u.x - cx; const dy = u.y - cy;
        const d = Math.hypot(dx, dy);
        const limit = u.group === 'scout' ? 0.55 : 0.22;
        if (d > limit) {
          u.vx -= dx * dt * 1.5;
          u.vy -= dy * dt * 1.5;
        }
        // Clamp inside world
        u.x = Math.max(0.01, Math.min(0.99, u.x));
        u.y = Math.max(0.01, Math.min(0.99, u.y));
      };
      fleet.friendly.forEach((u) => stepUnit(u, 'south'));
      fleet.enemy.forEach((u) => stepUnit(u, 'north'));

      // Tween viewport center
      const tgt = vpTargetRef.current;
      setVp((p) => {
        const k = 1 - Math.exp(-dt * 8);
        const cx = p.cx + (tgt.cx - p.cx) * k;
        const cy = p.cy + (tgt.cy - p.cy) * k;
        return Math.abs(cx - p.cx) < 1e-5 && Math.abs(cy - p.cy) < 1e-5 ? p : { ...p, cx, cy };
      });

      // Tween rotation
      const rtgt = rotTargetRef.current;
      setRot((r) => {
        const k = 1 - Math.exp(-dt * 6);
        const nr = r + (rtgt - r) * k;
        return Math.abs(nr - r) < 1e-5 ? r : nr;
      });

      // Drop expired pings
      const now = performance.now();
      pingsRef.current = pingsRef.current.filter((p) => now - p.t0 < 1500);

      force();
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, []);

  const pxToView = (clientX, clientY) => {
    const svg = svgRef.current;
    if (!svg) return null;
    const rect = svg.getBoundingClientRect();
    const sx = ((clientX - rect.left) / rect.width) * 100;
    const sy = ((clientY - rect.top) / rect.height) * 100;
    return [sx, sy];
  };

  const screenToWorldR = (sx, sy) => {
    // unrotate around (50, 50) by -rot, then apply the existing inverse
    const dx = sx - 50, dy = sy - 50;
    const c = Math.cos(-rot), s = Math.sin(-rot);
    return screenToWorld(50 + dx * c - dy * s, 50 + dx * s + dy * c);
  };

  const handleClick = (e) => {
    e.preventDefault();
    const p = pxToView(e.clientX, e.clientY);
    if (!p) return;
    if (!inDiamond(p[0], p[1])) return;
    const [wx, wy] = screenToWorldR(p[0], p[1]);
    vpTargetRef.current = { cx: wx, cy: wy };
  };

  const handleContext = (e) => {
    e.preventDefault();
    const p = pxToView(e.clientX, e.clientY);
    if (!p) return;
    if (!inDiamond(p[0], p[1])) return;
    const [wx, wy] = screenToWorldR(p[0], p[1]);
    pingsRef.current.push({ wx, wy, t0: performance.now() });
    force();
  };

  const zoom = (dir) => {
    setVp((p) => {
      const f = dir > 0 ? 0.82 : 1.22;
      const w = Math.max(0.10, Math.min(0.55, p.w * f));
      const h = Math.max(0.08, Math.min(0.45, p.h * f));
      return { ...p, w, h };
    });
  };

  const rotate = (dir) => {
    // 30° steps
    rotTargetRef.current = rotTargetRef.current + dir * (Math.PI / 6);
  };
  const resetRotation = () => { rotTargetRef.current = 0; };

  // Build SVG content
  const friendlyColor = '#4cb5e6';
  const enemyColor = '#e34a4a';
  const allyAccent = theme.accent;

  // Faction colors for buildings
  const factionColor = (f) => f === 'self' ? friendlyColor : f === 'enemy' ? enemyColor : (theme.inlay);

  // Render fog mask using SVG mask: white = visible, black = hidden
  const maskId = `fogmask-${theme.key}`;
  const gradId = `terrain-${theme.key}`;
  const gemId = `gem-${theme.key}`;

  return (
    <div className="mm-root" style={{
      '--mm-accent': theme.accent,
      '--mm-inlay': theme.inlay,
      '--mm-text': theme.text,
    }}>
      {/* Filigree frame layer (behind/around diamond) */}
      <div className="mm-frame">
        {/* Backplate diamond — engraved metal */}
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
          {/* outer plate diamond (slightly larger than map) */}
          <polygon points="160,4 316,160 160,316 4,160"
                   fill={`url(#back-${theme.key})`}
                   stroke={`url(#bezel-${theme.key})`} strokeWidth="2" />
          {/* second engraved ring */}
          <polygon points="160,14 306,160 160,306 14,160"
                   fill="none" stroke={theme.inlayShadow} strokeWidth="1" opacity="0.9" />
          <polygon points="160,18 302,160 160,302 18,160"
                   fill="none" stroke={theme.inlay} strokeWidth="0.7" opacity="0.5" />
          {/* tip studs */}
          {lvl !== 'minimal' && (
            <g fill={theme.accent} stroke={theme.inlayShadow} strokeWidth="0.5">
              <circle cx="160" cy="4" r="3" />
              <circle cx="316" cy="160" r="3" />
              <circle cx="160" cy="316" r="3" />
              <circle cx="4" cy="160" r="3" />
            </g>
          )}
        </svg>

        {/* The diamond world surface */}
        <svg
          ref={svgRef}
          className="mm-world"
          viewBox="0 0 100 100"
          width="238" height="238"
          onMouseDown={(e) => { if (e.button === 0) handleClick(e); }}
          onContextMenu={handleContext}
        >
          <defs>
            <clipPath id={`mm-clip-${theme.key}`}>
              <polygon points="50,0 100,50 50,100 0,50" />
            </clipPath>
            <radialGradient id={gradId} cx="35%" cy="80%" r="80%">
              <stop offset="0%" stopColor={T.grass} />
              <stop offset="100%" stopColor={T.grassDk} />
            </radialGradient>
            <mask id={maskId} maskUnits="userSpaceOnUse" x="0" y="0" width="1" height="1">
              <rect x="0" y="0" width="1" height="1" fill="black" />
              {/* explored reveal circles, drawn in WORLD coords (0..1) so
                  they ride with the rotated world group */}
              {WORLD.reveal.map((r, i) => (
                <circle key={i} cx={r.x} cy={r.y} r={r.r} fill="white">
                  <animate attributeName="r"
                           values={`${r.r};${r.r + 0.006};${r.r}`}
                           dur="3s" repeatCount="indefinite" />
                </circle>
              ))}
            </mask>
          </defs>

          {/* clip everything to the diamond */}
          <g clipPath={`url(#mm-clip-${theme.key})`}>
            {/* world group — rotation pulls world into the diamond,
                plus an outer screen-space rotation for the user-rotated map */}
            <g transform={`rotate(${rot * 180 / Math.PI} 50 50) translate(50 0) rotate(45) scale(70.7107 70.7107)`}>
              {/* base terrain (grass) */}
              <rect x="0" y="0" width="1" height="1" fill={`url(#${gradId})`} />

              {/* terrain patches */}
              {WORLD.terrain.map((tr, i) => (
                <polygon
                  key={i}
                  points={tr.poly.map((p) => p.join(',')).join(' ')}
                  fill={
                    tr.type === 'water' ? T.water :
                    tr.type === 'forest' ? T.forest :
                    tr.type === 'mountain' ? T.mountain :
                    T.sand
                  }
                  stroke={
                    tr.type === 'water' ? T.waterDk :
                    tr.type === 'forest' ? T.forestDk :
                    tr.type === 'mountain' ? T.mountainDk :
                    T.grassDk
                  }
                  strokeWidth="0.004"
                  vectorEffect="non-scaling-stroke"
                />
              ))}

              {/* resource nodes */}
              {WORLD.resourceNodes.map((n, i) => {
                const c = n.kind === 'gold' ? theme.accent
                        : n.kind === 'wood' ? '#7e5a32'
                        : '#a9a6a0';
                return (
                  <circle
                    key={i}
                    cx={n.x} cy={n.y} r="0.010"
                    fill={c}
                    stroke="#000"
                    strokeWidth="0.003"
                    vectorEffect="non-scaling-stroke"
                  />
                );
              })}

              {/* buildings */}
              {WORLD.buildings.map((b, i) => (
                <g key={i}>
                  <rect
                    x={b.x - b.w / 2} y={b.y - b.h / 2}
                    width={b.w} height={b.h}
                    fill={factionColor(b.faction)}
                    stroke="#000"
                    strokeWidth="0.003"
                    vectorEffect="non-scaling-stroke"
                  />
                  {b.kind === 'keep' && (
                    <rect
                      x={b.x - b.w / 4} y={b.y - b.h / 4}
                      width={b.w / 2} height={b.h / 2}
                      fill={b.faction === 'self' ? '#fff' : theme.accent}
                      opacity="0.85"
                      stroke="none"
                      vectorEffect="non-scaling-stroke"
                    />
                  )}
                </g>
              ))}

              {/* friendly units */}
              {fleetRef.current.friendly.map((u, i) => (
                <circle
                  key={`f${i}`} cx={u.x} cy={u.y} r="0.008"
                  fill={friendlyColor}
                  stroke="#06121b"
                  strokeWidth="0.0025"
                  vectorEffect="non-scaling-stroke"
                />
              ))}
              {/* enemy units */}
              {fleetRef.current.enemy.map((u, i) => (
                <circle
                  key={`e${i}`} cx={u.x} cy={u.y} r="0.008"
                  fill={enemyColor}
                  stroke="#1a0606"
                  strokeWidth="0.0025"
                  vectorEffect="non-scaling-stroke"
                />
              ))}

              {/* Viewport rectangle (camera) */}
              <rect
                x={vp.cx - vp.w / 2} y={vp.cy - vp.h / 2}
                width={vp.w} height={vp.h}
                fill="none"
                stroke={theme.accent}
                strokeWidth="0.005"
                vectorEffect="non-scaling-stroke"
                style={{ filter: `drop-shadow(0 0 1px ${theme.accent})` }}
              />
              {/* viewport corner ticks for extra ornament */}
              {[[-1,-1],[1,-1],[-1,1],[1,1]].map(([sx, sy], i) => {
                const x = vp.cx + sx * vp.w / 2;
                const y = vp.cy + sy * vp.h / 2;
                const t = 0.015;
                return (
                  <g key={i} stroke={theme.accent} strokeWidth="0.007" vectorEffect="non-scaling-stroke" strokeLinecap="round">
                    <line x1={x} y1={y} x2={x - sx * t} y2={y} />
                    <line x1={x} y1={y} x2={x} y2={y - sy * t} />
                  </g>
                );
              })}

              {/* Ping markers — expanding rings */}
              {pingsRef.current.map((p, i) => {
                const age = (performance.now() - p.t0) / 1500;
                const r = 0.005 + age * 0.06;
                const opacity = 1 - age;
                return (
                  <g key={i}>
                    <circle cx={p.wx} cy={p.wy} r={r}
                            fill="none" stroke={theme.accent}
                            strokeWidth="0.006"
                            vectorEffect="non-scaling-stroke"
                            opacity={opacity} />
                    <circle cx={p.wx} cy={p.wy} r={r * 0.55}
                            fill="none" stroke={theme.accent}
                            strokeWidth="0.004"
                            vectorEffect="non-scaling-stroke"
                            opacity={opacity * 0.7} />
                    <circle cx={p.wx} cy={p.wy} r="0.005"
                            fill={theme.accent}
                            opacity={1 - age * 0.5} />
                  </g>
                );
              })}
            </g>

            {/* Fog of war overlay — lives inside the diamond clip and rides
                the world group transform so it rotates with the terrain. */}
            <g transform={`rotate(${rot * 180 / Math.PI} 50 50) translate(50 0) rotate(45) scale(70.7107 70.7107)`}>
              <rect x="0" y="0" width="1" height="1"
                    fill="#000" opacity="0.78" mask={`url(#${maskId})`} />
            </g>

            {/* Subtle scanline / parchment grain to lend depth */}
            <rect x="0" y="0" width="100" height="100"
                  fill="url(#scan)" opacity="0.08" />
          </g>

          {/* Diamond edge highlight (drawn over content) */}
          <polygon points="50,0 100,50 50,100 0,50"
                   fill="none"
                   stroke={theme.inlay} strokeWidth="0.6" opacity="0.7" />
          <polygon points="50,1.5 98.5,50 50,98.5 1.5,50"
                   fill="none"
                   stroke={theme.inlayShadow} strokeWidth="0.4" opacity="0.7" />

          {/* Reusable scanline pattern */}
          <defs>
            <pattern id="scan" width="2" height="2" patternUnits="userSpaceOnUse">
              <rect width="2" height="1" fill="#000" />
              <rect y="1" width="2" height="1" fill={theme.inlay} />
            </pattern>
          </defs>
        </svg>

        {/* Filigree corner ornaments around the diamond tips */}
        <div className="mm-tip mm-tip-top">
          <FiligreeMedallion size={48} color={theme.inlay} dim={theme.inlayDim}
                             accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi}
                             level={lvl} label="N" />
        </div>
        <div className="mm-tip mm-tip-right">
          <FiligreeMedallion size={30} color={theme.inlay} dim={theme.inlayDim}
                             accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi}
                             level={lvl} />
        </div>
        <div className="mm-tip mm-tip-bottom">
          <FiligreeMedallion size={30} color={theme.inlay} dim={theme.inlayDim}
                             accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi}
                             level={lvl} />
        </div>
        <div className="mm-tip mm-tip-left">
          <FiligreeMedallion size={30} color={theme.inlay} dim={theme.inlayDim}
                             accent={theme.accent} gem={theme.gem} gemHi={theme.gemHi}
                             level={lvl} />
        </div>

        {/* Legend / footer label */}
        <div className="mm-legend" style={{ color: theme.textDim }}>
          <span><i style={{ background: friendlyColor }} /> Allied</span>
          <span><i style={{ background: enemyColor }} /> Hostile</span>
          <span><i style={{ background: theme.accent }} /> Resource</span>
        </div>
      </div>
    </div>
  );
}

function ZoomGlyph({ kind, color }) {
  return (
    <svg width="18" height="18" viewBox="0 0 18 18">
      <circle cx="9" cy="9" r="7" fill="none" stroke={color} strokeWidth="1.2" />
      <line x1="3" y1="9" x2="15" y2="9" stroke={color} strokeWidth="1.6" strokeLinecap="round" />
      {kind === 'plus' && <line x1="9" y1="3" x2="9" y2="15" stroke={color} strokeWidth="1.6" strokeLinecap="round" />}
    </svg>
  );
}

function RotateGlyph({ kind, color }) {
  // Curved arrow — kind="left" or "right"
  const flip = kind === 'right' ? 'scale(-1 1) translate(-18 0)' : '';
  return (
    <svg width="18" height="18" viewBox="0 0 18 18">
      <g transform={flip}>
        <path d="M 14 5 A 6 6 0 1 0 14 13"
              fill="none" stroke={color} strokeWidth="1.4" strokeLinecap="round" />
        <polygon points="14,2 14,7 11,5" fill={color} stroke={color} strokeWidth="0.8" strokeLinejoin="round" />
      </g>
    </svg>
  );
}

// Compass rose — needle rotates to indicate current map orientation. Click
// to reset.
function CompassGlyph({ color, angle }) {
  const deg = (angle * 180) / Math.PI;
  return (
    <svg width="18" height="18" viewBox="0 0 18 18">
      <circle cx="9" cy="9" r="7" fill="none" stroke={color} strokeWidth="0.9" opacity="0.7" />
      <g transform={`rotate(${deg} 9 9)`}>
        <polygon points="9,2 11,9 9,9" fill={color} />
        <polygon points="9,16 7,9 9,9" fill={color} opacity="0.45" />
      </g>
      <text x="9" y="4.6" textAnchor="middle" fontSize="3.8"
            fontFamily="'Cinzel', serif" fontWeight="700"
            fill={color}>N</text>
    </svg>
  );
}

Object.assign(window, { Minimap });
