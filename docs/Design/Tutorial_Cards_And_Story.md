# Tutorial cards and story: proposal (2026-11-07)

**Status: decided in part (§5).** The cards exist. The tutorial text fixes and the story are
not implemented, and every piece of lore invented here is marked as a proposal. Card
rosters and research are read from the SOs by `tools/tutorial_cards/gen_cards.py`, which
draws every panel, key, icon and bar from the Synty kits and writes
`Assets/Resources/TutorialCards/<lang>/` (tracked in git). T6, T7 and the contact sheets are in
`docs/Tutorial/Cards/`.

## 1. What the tutorial got wrong (fixed 2026-11-07)

Each of these teaches something the game no longer does (`TutorialDirector.cs`).

| Step | It says | Canon | Source |
|---|---|---|---|
| Claim ground with your army (Ch 4) | Comes before the age-up (Ch 5) | Claims open at age-up, so the step can't be done in order | Territory_Claims §10 |
| Claim ground | Two armies "freeze" a territory | The stronger side pushes the meter by the difference | Territory_Claims §2.2 |
| The curse | Claims at "double weight" | Retired | Territory_Claims §6.1 |
| Build on your nodes | Mines work veilstone and veilsteel | Veilsteel is made, never mined; Alanthor stop mining veilstone at age-up | Veilstone_Economy |
| Kill curse creatures | "The only source of religion" | Also monuments, Sanctums, the Temple, sect units and buying RP | Monuments §1, §4 |
| Raise the Temple | Costs 1 RP | Costs no RP | Monuments §4 |
| Adopt a sect | Chapel grants the unit and research | The sect building does that | Religion §3-4 |
| Recruit a sect hero | Recruited at the chapel | Recruited at the sect building, and returns at the same level | Heroes §5 |
| The Shardroot | A curse creature carries it, anyone can take it | Hidden on the map; only heroes can take it; Drop or Temple | Heroes §4 |
| The last one standing | "One way to win" | Also Temple victory and Sanctum dominion, and landless factions are eliminated | Territory_Claims §7 |

The steps no longer state costs in text. Grants are still literal amounts in `TutorialDirector.cs`.

## 2. What it never explained (now covered by steps and cards)

Highest value first. A card already covers each one.

| Topic | Card | Why it matters |
|---|---|---|
| Heroes: stats, levels, auto-cast, level 10 | 23, 24 | The newest and most decisive system |
| Monuments and ruins | 22 | Earliest contested objective, and the route to the Shardroot |
| Sanctums and Sanctum dominion | 13, 26 | A whole win condition |
| Sect building and sect units, 1 RP each | 20 | The tutorial stops at the chapel |
| Building plans and Delete | 05 | White previews look like a bug without this |
| Cursed ground on buildings; Litharch wards | 17 | Players lose bases to this without knowing why |
| Connection rule and cut-off ground | 11 | Buildings collapsing seems random otherwise |
| Veilstone and veilsteel at the Trading Outpost | 03 | Alanthor's whole late economy |
| Nodes running dry; territory types | 04, 13 | Why you must expand |
| Building levels are faction-wide; research; ranks | 08 | The real progression system |
| Walls, gates, garrisons | 10 | Alanthor's identity, and absent entirely |
| Fog, Scouts, Field Intelligence | 09 | Sight also limits where powers can be aimed |
| High ground, flanks, formations, right-drag | 02, 07 | Separates good players from new ones |
| Religion menus (R and T), Tithe, Petriarchy's Army | 19, 20 | RP spending is otherwise hidden |
| Control groups, Game Review, saves and replays | 01, 26 | Quality of life |

## 3. How the cards fit in

Keep the rule that the tutorial never gates the player. A card opens by itself when its step
becomes the suggestion, and one click dismisses it. Add a **Field Guide** to the pause
menu so every card stays available afterwards.

| Tutorial chapter | Cards |
|---|---|
| 1 Controls | 01, 02 |
| 2 Economy | 03, 04, 05 |
| 3 Army | 06, 07, 08, 09, 10 |
| 4 Territory (after 5) | 11, 12, 13 |
| 5 Culture (before 4) | 14, 15, T1 |
| 6 The Curse | 16, 17 |
| 7 Religion | 18, 19, 20, 21, T5 |
| 8 Heroes and Victory | 22, 23, 24, 25, 26 |
| Field Guide only | T2, T3, T4 (T6 and T7 are not in the game yet: Resources has no edition strip) |

## 4. The story

### What the lore already gives us

- **The magic is time.** Glow is condensed time, veilsteel stores it, veilstone is time
  frozen. Alanthor's sects work in the past: the record, rewind, stored charge.
- **The Old Empire (Vyrelin)** left seals, lenses and glow-engines behind.
- **Alanthor** is a defending people under **King Lexor**, with the **House of Vaelith**
  sects, the **Vault of Almi&eacute;rra** and the **Temple of Ridan**.
- **Rule to keep:** player-facing text names what is on the map, with no trade jargon and
  no places beyond the map.

### Proposed story: *The Last Entry*

**On hold (2026-11-07):** the developer has more context to give first. Nothing below goes into the game until then. The narrator below is rejected.

**The premise.** The curse is not a monster. It is **the moment the Old Empire stopped**:
a frozen instant, still spreading, turning living ground to crystal. The Shardroot is where
it began. Whoever holds it holds the power to stop time, or to end the world by trying.

**The narrator.** The **Archivist of Hours** writes the chronicle of your settlement, and
the tutorial is that chronicle being written. It suits the game's rules: an archivist has
no reason to order you around and only records what you do, so it fits a tutorial that
never gates you. Skipped steps simply become gaps the Archivist comments on.

**The player.** The captain of a small band who reach the border with five Spearmen, a
Scout and three Workers. The story uses exactly the real starting army.

| Chapter | Story beat | The Archivist writes |
|---|---|---|
| 1 Controls | A band arrives at the border and finds an empty Shelter | "Entry one. They came with nine souls, and no one sent them." |
| 2 Economy | The ground pays. Nobody knows why it is so generous | "The land gives freely here. Land does not do that." |
| 3 Army | Something is watching from the far ridge | "They learned spears before they learned the name of this place." |
| 5 Culture | The Vault rises, and with it the claim of Alanthor | "A vault is a promise that there will be a tomorrow to spend it in." |
| 4 Territory | The first march outward | "Ground belongs to whoever stands on it. It is the oldest law we have." |
| 6 The Curse | First purple on the horizon. A Scout returns with crystal where a stream had been | "The stream was still flowing. It simply was not moving." |
| 7 Religion | The Temple rises, and the sects bring the old magic | "We fight frozen time with borrowed time. I do not recommend looking closely at the arithmetic." |
| 8 Heroes and Victory | Lexor arrives. Ruins reveal the Shardroot. The choice | "The last entry is yours. Keep it, set it down, or give it to the Temple. I will record whichever you choose." |

**The ending.** The tutorial's final line depends on what the player did, using state the
game already has: the Archivist's closing entry differs for carrying, dropping or
enshrining the Shardroot.

**How it plays.**
- One or two lines of text per chapter, in the coach panel, always skippable.
- No new mechanics, cutscenes or voice in the first version.
- Lines trigger on the same conditions as the steps, so a player who races ahead still
  reads them in order.

**Later, if you want it:** the same narrator could add one line per sect or hero in the
Field Guide, and a short campaign could continue from the last entry.

## 5. Decisions (2026-11-07)

1. **Canon:** the curse is the moment the Old Empire stopped
   ([Curse_And_Shardroot.md](Curse_And_Shardroot.md)).
2. **Narrator:** not the Archivist of Hours. Still open.
3. **Chapter order:** Culture comes before Territory.
4. **Cards show automatically** when their step becomes the suggestion. They stay
   dismissible and never block play, because the tutorial still never gates the player. The
   Field Guide keeps them available afterwards.
5. **Portuguese from the start,** for cards and story alike.
6. **Timing:** 0.0.73 ships with the old tutorial text; all of this goes into the next version.
