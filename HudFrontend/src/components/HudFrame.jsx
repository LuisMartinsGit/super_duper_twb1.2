// HUD assembly. Anchored to the four corners of the viewport; the centre
// of the screen is left empty so the 3D game (rendered by Unity behind the
// CEF surface) shows through.

import React from 'react';
import { resolveTheme } from './themes.js';
import { useBridge, sendToUnity } from '../bridge.js';
import { MenuButton, MenuOverlay } from './Menu.jsx';
import { Objectives } from './Objectives.jsx';
import { Sidebar } from './Sidebar.jsx';
import { ResourceCounter } from './Resources.jsx';
import { SelectionPanel } from './Selection.jsx';
import { ActionsPanelBridged } from './Actions.jsx';
import { Minimap } from './Minimap.jsx';
import { CulturePicker } from './CulturePicker.jsx';

// Selector for interactive HUD chrome — anything the player can click or
// hover. Game-world input (selection / orders / placement) is gated while
// the pointer is over any element matching this. Deliberately excludes
// .gv-objective (display-only) and .gv-br (the minimap, whose own canvas
// handles its own clicks at sortingOrder 101).
const HUD_INTERACTIVE_SELECTOR = '.gv-bl, .hud-menu-btn, .gv-culture-btn-wrap, .gv-culture-modal-scrim';

export function HudFrame({ themeKey = 'jade', accentKey = 'theme', ornament = 'maximal' }) {
  // Pointer-capture mirror: tells Unity (via `hud:capture`) whether the
  // cursor is over an interactive HTML region so SelectionSystem /
  // RTSInputManager can suppress the same click. CEF runs in a separate
  // process and can't natively stop Unity's input.
  React.useEffect(() => {
    let captured = false;
    // While a mouse button is held down, freeze the capture state. Whichever
    // target the mousedown landed on owns input until mouseup — otherwise a
    // drag-select started on the game world breaks the moment the cursor
    // passes over a HUD panel, and a drag started on the HUD would also
    // bleed through. Re-evaluate on mouseup with the cursor's current
    // position so subsequent hover/click works normally.
    let buttonDown = false;
    const update = (overHud) => {
      if (overHud === captured) return;
      captured = overHud;
      sendToUnity('hud:capture', { capture: captured });
    };
    const overHudFor = (e) => {
      const t = e.target;
      return !!(t && t.closest && t.closest(HUD_INTERACTIVE_SELECTOR));
    };
    const onMove = (e) => {
      if (buttonDown) return;
      update(overHudFor(e));
    };
    const onDown = (e) => { buttonDown = true; update(overHudFor(e)); };
    const onUp   = (e) => { buttonDown = false; update(overHudFor(e)); };

    document.addEventListener('mousemove', onMove);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('mouseup',   onUp);
    window.addEventListener('blur', () => { buttonDown = false; update(false); });
    return () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('mouseup',   onUp);
      update(false);
    };
  }, []);
  // The bridge can override theme at runtime by pushing `theme`:
  //   { key, accent, ornament }
  // — useful for per-culture theming triggered from C#.
  const live = useBridge('theme', null);
  const theme = resolveTheme(
    live?.key ?? themeKey,
    live?.accent ?? accentKey,
    live?.ornament ?? ornament,
  );

  return (
    <div className="artboard-host"
         style={{
           '--gv-bg': theme.bg,
           '--gv-bg-mid': theme.baseMid,
           '--theme-accent': theme.accent,
         }}>
      <div className="gv-root">
        <div className="gv-bg" />

        <MenuButton theme={theme} />
        <Objectives theme={theme} />
        <CulturePicker theme={theme} />

        <div className="gv-bl">
          <Sidebar theme={theme} />
          <div className="gv-bl-row">
            <ResourceCounter theme={theme} />
            <SelectionPanel theme={theme} />
            <ActionsPanelBridged theme={theme} />
          </div>
        </div>
        <div className="gv-br">
          <Minimap theme={theme} />
        </div>

        <MenuOverlay theme={theme} />
      </div>
    </div>
  );
}
