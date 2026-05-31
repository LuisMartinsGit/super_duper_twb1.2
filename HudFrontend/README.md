# The Waning Border — Web HUD

A React/CSS port of the original `Game HUD/` design mock, rendered inside
Unity via CEF (Chromium Embedded Framework) using the **UnityWebBrowser**
package. Replaces the IMGUI HUD with a pixel-faithful copy of the design.

```
Source ──► HudFrontend/ (this dir)
              │  esbuild
              ▼
       Assets/StreamingAssets/HUD/
              │  loaded at runtime by
              ▼
       HudWebController (CEF in Unity)
              │  ↔ JS bridge ↔
              ▼
       HudBridge (pushes ECS state)
```

---

## Architecture

| Piece | Where | What it does |
|---|---|---|
| HUD source (JSX + CSS) | `HudFrontend/src/` | All components, bundled by esbuild |
| Esbuild output | `Assets/StreamingAssets/HUD/{hud.js, hud.css, index.html}` | Shipped with the game build |
| `HudWebController.cs` | `Assets/Scripts/UI/Web/` | Hosts UWB (CEF) on a fullscreen Canvas |
| `HudBridge.cs` | `Assets/Scripts/UI/Web/` | Polls game state at 4 Hz and pushes JSON snapshots to JS |
| `bridge.js` | `HudFrontend/src/` | Installs `window.unityHUD = { recv, send, on, peek }`; routes JS→C# via `uwb.ExecuteJsMethod('HudMessage', …)` |
| UWB packages | `Packages/manifest.json` | `dev.voltstro.unitywebbrowser` + CEF engine for Win-x64 |

## First-time setup

1. **Open Unity once** so it can resolve the UWB scoped registry and download the CEF engine binaries (~80 MB, lands in `Library/PackageCache/`).
2. **Install frontend deps:**
   ```
   cd HudFrontend
   npm install
   ```

## Build the HUD bundle

Whenever the JSX/CSS changes:
```
cd HudFrontend
npm run build       # one-shot, minified
npm run watch       # rebuild on every save
```
The bundle lands in `Assets/StreamingAssets/HUD/`. Unity does **not** need to recompile to pick up frontend changes — just reload the running scene (or remount the controller).

## Preview without Unity

Open `Assets/StreamingAssets/HUD/index.html` in any browser to preview the visuals with mock data. Append `?preview=1` to keep the fake game-area vignette/grain instead of going fully transparent.

## Disabling the web HUD

In `GameSettings.cs`:
```csharp
GameSettings.UseWebHud = false;   // falls back to legacy IMGUI / UI Toolkit HUDs
```
This toggles whether `GameBootstrap.CreateManagersObject()` spawns the `WebHud` GameObject and whether the legacy HUDs (`ResourceHUD`, `ReligionHUD`, `MinimapRenderer`, `VictoryProgressHUD`, `GameplayUIController`) are disabled.

## Bindings status

| Panel | C#→JS topic | Wired to | Status |
|---|---|---|---|
| Resources | `resources` | `FactionResourcesHelper`, `PopulationHelper`, `FactionReligionPointsHelper` | ✅ live |
| Objectives | `objectives` | `CrystalMainNodeTag` + `CrystalNodeState` queries | ✅ live |
| Menu | `menu` ⇄ `menu:open`/`menu:close`/`menu:item` | `InGameMenuPanel.Open/Close/IsOpen` | ✅ live (Settings/Save/Load/Surrender items log only — wire to handlers when ready) |
| Selection | `selection` | `SelectionSystem.CurrentSelection` | ⚠️ stub: name + type only. HP/atk/def/spd/upgrade are placeholders. Full extraction via `EntityActionExtractor` is the next step. |
| Minimap | `minimap` ⇄ `minimap:pan`/`minimap:ping` | `UnitTag`/`BuildingTag` + `LocalTransform` queries; camera viewport projection | ✅ live (no fog-of-war check — units always visible) |
| Sidebar (Sects) | `sects` ⇄ `sidebar:action` | — | ⚠️ stub: shows the design's 6 mock sects. Search the C# for `SECTS-BINDING-TODO`. |

### To wire a real binding

1. Add the C# state read inside `HudBridge.Push<Topic>()`.
2. Build the JSON snapshot into the shared `_sb` `StringBuilder`.
3. Call `PushIfChanged(topic, _sb.ToString())` — it dedupes against the last-pushed payload.
4. On the JS side, the component already subscribes via `useBridge('topic', mockFallback)`.

### To handle a new click

1. Component calls `sendToUnity('your:topic', { …payload })`.
2. Add a `case "your:topic":` to `HudBridge.OnHudMessage`. Parse the JSON payload with the existing `QuickField`/`QuickFloat` helpers, or use Newtonsoft if the shape is complex.

## Limitations

- **Windows-only build path.** The `dev.voltstro.unitywebbrowser.engine.cef.win.x64` package ships only Win-x64 CEF. Add the `.linux.x64` / `.macos.x64` engine packages later for cross-platform.
- **~80 MB build size hit** from the CEF runtime, plus ~30–60 MB process memory.
- **First-frame flash.** CEF takes ~1–2 seconds to spin up the renderer process. During that time the HUD area is empty (the 3D game still renders). Acceptable for the first iteration.
- **Google Fonts CDN.** `styles.css` still imports Cinzel + Cormorant Garamond from `fonts.googleapis.com`. For an offline-clean build, self-host these woff2 files in `Assets/StreamingAssets/HUD/fonts/` and rewrite the `@import` to a local `@font-face`.
- **Background transparency.** CEF clears to transparent; the HUD CSS sets `body{background:transparent}`. If you see a coloured fill behind the panels, check `HudWebController.backgroundColor` and the `.in-game` body class in `main.jsx`.

## Files of interest

- Design source (read-only reference): `Game HUD/` at repo root.
- Frontend source: `HudFrontend/src/`.
- C# host: `Assets/Scripts/UI/Web/`.
- Bundled output (committed for build reproducibility): `Assets/StreamingAssets/HUD/`.
