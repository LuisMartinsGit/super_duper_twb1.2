export const THEMES = {
  jade: {
    key: 'jade',
    name: 'Jade & Silver',
    sub: 'Deep jade crystal · silvered scrollwork',
    bg: '#06100d',
    base: '#0b1f1a',
    baseMid: '#143228',
    baseEdge: '#04100b',
    inlay: '#cfd6d3',
    inlayDim: '#7f8e8a',
    inlayShadow: '#020806',
    accent: '#e8b84a',
    accentSoft: '#8a6a1f',
    text: '#e6efea',
    textDim: 'rgba(207,214,211,0.6)',
    gem: '#1d6a55',
    gemHi: '#3fbf9a',
    terrain: {
      grass: '#1a3d2c', grassDk: '#102a1f',
      forest: '#0d2418', forestDk: '#071810',
      water: '#0c2a3a', waterDk: '#061a26',
      mountain: '#3a3a36', mountainDk: '#23231f',
      sand: '#5a4a30',
    },
  },
  iron: {
    key: 'iron',
    name: 'Forged Iron',
    sub: 'Hammered iron · bronze rivets · ember glow',
    bg: '#0c0907',
    base: '#1a1612', baseMid: '#2a2218', baseEdge: '#0d0a07',
    inlay: '#b08550', inlayDim: '#6b4f2e', inlayShadow: '#0a0604',
    accent: '#e85a3a', accentSoft: '#6e2515',
    text: '#ece2d0', textDim: 'rgba(176,133,80,0.62)',
    gem: '#3a1410', gemHi: '#a83820',
    terrain: {
      grass: '#3a3018', grassDk: '#221c10',
      forest: '#1f2410', forestDk: '#13180a',
      water: '#1a2230', waterDk: '#0d121c',
      mountain: '#3a322a', mountainDk: '#22201a',
      sand: '#6a5028',
    },
  },
  arcane: {
    key: 'arcane',
    name: 'Arcane Crystal',
    sub: 'Sigils of the violet veil · enchanted glass',
    bg: '#070612',
    base: '#140e23', baseMid: '#231740', baseEdge: '#08051a',
    inlay: '#d8d4e8', inlayDim: '#7e7a99', inlayShadow: '#04020a',
    accent: '#a96be8', accentSoft: '#4a2a72',
    text: '#ece8f6', textDim: 'rgba(216,212,232,0.6)',
    gem: '#2a1a55', gemHi: '#7a4ecd',
    terrain: {
      grass: '#22214a', grassDk: '#14132e',
      forest: '#0e1840', forestDk: '#080f2a',
      water: '#0c1638', waterDk: '#060b22',
      mountain: '#3a304a', mountainDk: '#221c2e',
      sand: '#4a3a6a',
    },
  },
  stone: {
    key: 'stone',
    name: 'Ancient Stone',
    sub: 'Carved menhir · weathered gold leaf',
    bg: '#0d0a06',
    base: '#231d14', baseMid: '#3a311f', baseEdge: '#100c07',
    inlay: '#c9a35a', inlayDim: '#7a5e30', inlayShadow: '#0a0704',
    accent: '#e89a3a', accentSoft: '#7a4a14',
    text: '#ede2c8', textDim: 'rgba(201,163,90,0.62)',
    gem: '#3a2a14', gemHi: '#c89858',
    terrain: {
      grass: '#3a3a1c', grassDk: '#23230f',
      forest: '#2a2a10', forestDk: '#1a1a0a',
      water: '#1a2030', waterDk: '#0d1018',
      mountain: '#4a4030', mountainDk: '#2a241b',
      sand: '#6a4f28',
    },
  },
};

export const ACCENT_OPTIONS = {
  theme: null,
  gold: { accent: '#e8b84a', accentSoft: '#7a5a1f' },
  ember: { accent: '#e85a3a', accentSoft: '#6e2515' },
  arcane: { accent: '#a96be8', accentSoft: '#4a2a72' },
  azure: { accent: '#4ab8e8', accentSoft: '#1c4d6e' },
  verdant: { accent: '#6ae89a', accentSoft: '#1f6a3c' },
};

export function resolveTheme(themeKey, accentKey, ornament) {
  const t = { ...THEMES[themeKey] };
  const ovr = ACCENT_OPTIONS[accentKey || 'theme'];
  if (ovr) Object.assign(t, ovr);
  t.ornament = ornament || 'maximal';
  return t;
}

export function hexAlpha(hex, a) {
  const h = (hex || '').replace('#', '');
  if (h.length < 6) return `rgba(0,0,0,${a})`;
  const r = parseInt(h.slice(0, 2), 16);
  const g = parseInt(h.slice(2, 4), 16);
  const b = parseInt(h.slice(4, 6), 16);
  return `rgba(${r},${g},${b},${a})`;
}
