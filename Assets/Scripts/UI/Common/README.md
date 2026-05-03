# UI/Common — Canonical IMGUI Theme

`Styles.cs` is the single source of truth for the IMGUI navy + gold theme. New panels read this file.

## Palette

| Constant | RGBA | Purpose |
|---|---|---|
| `Styles.HighlightColor` | `(0.83, 0.66, 0.26, 1)` | gold accent — headers, selected tabs, key text |
| `Styles.PanelBgColor` | `(0.06, 0.08, 0.18, 0.95)` | navy panel background |
| `Styles.InnerRowColor` | `(0.08, 0.10, 0.22, 1)` | inner row / nested box fill |
| `Styles.SuccessColor` | green-tinted | positive feedback, completed actions |
| `Styles.WarningColor` | amber-tinted | non-fatal warnings, low-resource hints |
| `Styles.ErrorColor` | red-tinted | hard errors, blocked actions |
| `Styles.AffordableColor` | green-tinted | cost label when player can afford |
| `Styles.UnaffordableColor` | red-tinted | cost label when player cannot afford |
| `Styles.VictoryColor` | gold/green | post-game victory header |
| `Styles.DefeatColor` | red/grey | post-game defeat header |

## Contributor rule

When adding a new IMGUI panel, call `Styles.Initialize()` as the first line of `OnGUI()` and reference `Styles.<X>` for all theme styling. Do not declare local `GUIStyle` fields or write inline `new Color(...)` literals matching the canonical palette.

## Faction-tinted headers

For faction-tinted headers, use `Styles.GetFactionAccentStyle(Faction)` — see CoreComponents.cs for the `Faction` enum.

## Documented exceptions (not regressions)

These literals are intentionally NOT canonicalized. A reviewer who sees a regression-test hit should check this list before flagging it.

- Alpha-variants `(0.83f, 0.66f, 0.26f, <0.7|0.6|...>)` in `EntityActionPanel.cs`, `EntityInfoPanel.cs`, `ResourceHUD.cs:254`, `SpellPanel.cs:48`, `OptionsMenuUI.cs` — gold with explicit alpha overrides.
- Health-bar reds in `EntityInfoPanel.cs:435`, `EntityInfoPanel.cs:677`, `FloatingHealthBars.cs:135-136`, `ResourceHUD.cs:270` — separate harmonization concern, not a navy/gold violation.
- Faction-Red in `UIHelpers.cs:209` (palette source), `PostGameStatsUI.cs:534` — faction palette, distinct concern.
- `SkirmishLobbyUI.cs:99` light pink-red error tint `(1, 0.5, 0.5, 1)` — softer form-error accent, intentionally distinct from `Styles.ErrorColor`.
- `MainMenuUI.cs:124-126` navy-tint menu overlay `(0, 0, 0.02, 0.35)` — tint, not a dim.

If you add a new alpha-variant, append it here so future reviewers know it's intentional.

## Regression-test recipe (PowerShell)

Run from repo root; each command should return zero lines.
```powershell
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern 'new Color\(0\.83f, 0\.66f, 0\.26f\)' | Where-Object { $_.Path -notmatch 'UI[\\/]Common[\\/]Styles\.cs$' }
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern 'new Color\(0\.06f, 0\.08f, 0\.18f\)' | Where-Object { $_.Path -notmatch 'UI[\\/]Common[\\/]Styles\.cs$' }
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern 'new Color\(0\.08f, 0\.10f, 0\.22f\)' | Where-Object { $_.Path -notmatch 'UI[\\/]Common[\\/]Styles\.cs$' }
```

If you add a new color to `Styles.cs`, add a matching `Select-String` line here.

## Bash variant (WSL/macOS/Linux)

```bash
grep -rn --include='*.cs' --exclude='Styles.cs' -E 'new Color\(0\.83f, 0\.66f, 0\.26f\)|0\.06f, 0\.08f, 0\.18f\)|0\.08f, 0\.10f, 0\.22f\)' Assets/Scripts/
```
