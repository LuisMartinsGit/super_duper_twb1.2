# Crystal Curse — Code Sweep + Redesigned Checklist v2 (Alanthor + neutral scope)

This document captures the bug-sweep, structural fixes that shipped in this batch, and the redesigned test checklist. Feraldis-as-a-faction is deferred for testing; the Iconoclast (formerly Feraldis-Longhouse) is now reachable via TempleOfRidan Lv 4 in any faction.

---

## Fixes that shipped (Phases 1–4)

### Phase 1 — `edafb97`: bug-sweep batch
- **Resource HUD**: dropped the Glow row entirely. Glow is a sect-shrine resource on TempleOfRidan.
- **Caravan rendering**: PresentationID 401 → 405. The old ID collided with `Procedural/Rock` and was spawning rocks under every caravan ("large blocks" you saw).
- **Caravan controllability**: spawn now adds `NotControllableTag`. `PatrolThreatDetectionSystem` strips/restores the tag correctly — caravans default to autonomous, become commandable when an enemy is within range.
- **UnitAnimationSync crash on main-menu return**: re-acquires `EntityManager` each frame; bails when the world is disposed. Pre-existing bug, not introduced by Crystal Curse work.
- **VictoryProgressHUD gating**: hidden entirely in Timeless Age. After culture commit, shows only the local player's culture row prominently + a small summary of the other two cultures.
- **CrystalMainNode LineOfSight = 8u**: curse faction can react to nearby attackers without revealing the full map. Player factions still gate node visibility on their own scouts.

### Phase 2 — `03e8bcb`, `5df68fe`: nodes un-targetable + Iconoclast as enabler
- **Replaces** the per-frame HP-refund invulnerability with a `NodeUntargetable` tag. `TargetingSystem.WithNone<NodeUntargetable>` skips tagged nodes — AI doesn't path there, right-click attacks no-op, no infinite-DPS soak.
- `IconoclastAuraSystem` toggles the tag based on `IconoclastTag` proximity (12u radius).
- **Iconoclast is now an ENABLER**: Damage 25 → 0, Class human_melee → human_magic. The aura strips the tag for surrounding units; the Iconoclast itself does not attack.
- Iconoclast moves from `Feraldis_Longhouse` → `TempleOfRidan` with `minBuildingLevel: 4` (Feraldis has no building-level upgrade pipeline).
- `NodeInvulnerabilitySystem.cs` deleted.

### Phase 3 — `4a77a06`: god powers per adopted sect + glow allocation
- **GodPowerHUD (bottom-right) deleted**. The mechanic is now on the existing ReligionHUD's per-sect Fire buttons.
- `TempleChapelSlot` gained `byte GlowAllocated`.
- `SectActivePowerHelper.Fire`: dropped the AP-lever-level-required gate (now any ADOPTED sect can fire) + halves the post-fire cooldown when the slot has Glow allocated.
- New helpers: `AllocateGlow / DeallocateGlow / HasGlowAllocated / IsAdopted / TryGetFactionTemple`. Allocating 1 Glow draws from the Temple's `GlowStored`; deallocating refunds it.
- **ReligionHUD**: each adopted-slot tile now has [Fire / cooldownS] + [◆ Glow ◆ / + Allocate Glow] buttons.

### Phase 4 — `6eec886`: shield bar widget
- `UnitVisualOverlaySystem` now renders a horizontal cyan bar above each unit's rank pips when `ShieldBar.Current > 0`.

---

## Still pending (acknowledged, not in this batch)

- **Sects UI relocation**: the user wants the whole sects UI moved to a vertical strip on the **left edge** of the screen, above the resource HUD, matching the resource HUD's aesthetic. The current ReligionHUD lives top-center. The mechanic (god power per sect + glow allocation) is wired through the existing UI; only the cosmetic relocation remains.
- **Glow trickle source confirmation**: nothing in code currently writes to `FactionResources.Glow` (the bank field). Most likely the trickle the user saw was either `GameSettings.MaxStartingResources` debug-on or the Vault auto-compounding stored Glow. With the Glow row removed from the Resource HUD, this is now invisible regardless.
- **Sect god-power effects beyond Active-Power level 0**: the existing `SectLeverEffects.ActiveOf` table defines per-sect kinds (Smite / Heal / Armor / etc.). Some sects may have `Kind == None` until Phase 2 of the sect-system rewrite fills them in. Untested sects fail silently on Fire.
- **Active per-class tier abilities**: Veilsteel duplicate squad, temporal echo shots/summons. Active-ability UI binding still pending from earlier slices.
- **Feraldis bloodsoaked-ground reward**: requires BloodMap sampling.

---

## Redesigned checklist v2 (Alanthor + neutral)

Mark each row with `X` in either PASS or FAIL. Leave blank if untested.

### A. Setup + boot stability

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | A.1 Unity opens, no compile errors in Console |
| [ ] | [ ] | A.2 Start a Skirmish (Alanthor as local player) → match starts cleanly |
| [ ] | [ ] | A.3 Return to main menu mid-game → no ObjectDisposedException from UnitAnimationSync |
| [ ] | [ ] | A.4 Start a second match after main-menu return → no stale state |

### B. Resource HUD

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | B.1 Resource HUD shows Pop / RP / Supplies / Iron / Crystal / Veilsteel rows |
| [ ] | [ ] | B.2 Resource HUD does **NOT** show a Glow row (verify the row is gone) |
| [ ] | [ ] | B.3 Pop count matches actual unit count |

### C. Crystal nodes — visibility + un-targetability

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | C.1 At match start, crystal nodes are **NOT visible** through fog of war |
| [ ] | [ ] | C.2 Sending a scout near a node reveals it; pulling back hides it again |
| [ ] | [ ] | C.3 Any non-Iconoclast unit attacking a node: node is **NOT targetable** (right-click on node = no attack, AI doesn't path to it) |
| [ ] | [ ] | C.4 Curse units near their own node react to player units approaching (LOS = 8u) |
| [ ] | [ ] | C.5 An Iconoclast within 12u of a node makes the node **targetable** to surrounding units |
| [ ] | [ ] | C.6 Pulling the Iconoclast away → node becomes un-targetable again |
| [ ] | [ ] | C.7 Iconoclast itself does NOT attack nodes (Damage = 0) |
| [ ] | [ ] | C.8 With Iconoclast aura + Swordsmen attacking, node HP drops normally |
| [ ] | [ ] | C.9 Node reaching 0 HP → Destroyed state, regrows after ~9 minutes |

### D. Iconoclast unit (now neutral, trained at Temple)

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | D.1 Iconoclast appears in Temple training options (NOT Longhouse) |
| [ ] | [ ] | D.2 Temple at Lv 1–3: Iconoclast button visible but greyed with "Requires Lv 4 Temple" tooltip |
| [ ] | [ ] | D.3 Temple at Lv 4: Iconoclast button clickable + trains successfully |
| [ ] | [ ] | D.4 Trained Iconoclast spawns + can be selected/moved like any unit |
| [ ] | [ ] | D.5 Iconoclast does NOT auto-attack anything (Damage 0) |

### E. Purification ritual (Alanthor)

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | E.1 Temple Lv 1–3: Scholar button visible but greyed with "Requires Lv 4 Temple" |
| [ ] | [ ] | E.2 Temple Lv 4: Scholar trains successfully |
| [ ] | [ ] | E.3 Right-click an Active node with Scholar → walks to node + channels ~35s |
| [ ] | [ ] | E.4 Cyan-white beam appears at the node during channel |
| [ ] | [ ] | E.5 Defenders spawn at the node and pursue Scholar; spawn rate increases as channel progresses |
| [ ] | [ ] | E.6 Channel completes → node turns Cleansed + Glow pickup spawns |
| [ ] | [ ] | E.7 Scholar dying mid-channel cancels the ritual |

### F. Glow pickup + Temple deposit

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | F.1 Unit walking onto a Glow pickup → golden beam appears above it |
| [ ] | [ ] | F.2 Standing 20s uninterrupted → pickup vanishes; unit becomes GlowCarrier |
| [ ] | [ ] | F.3 Moving the unit out of range mid-attune → progress resets, beam vanishes |
| [ ] | [ ] | F.4 GlowCarrier within ~3u of TempleOfRidan → glow deposits to Temple's GlowStored |
| [ ] | [ ] | F.5 Stored Glow visible somewhere (Temple action panel or Religion HUD) |
| [ ] | [ ] | F.6 Destroying a Temple holding 1+ Glow → AOE explosion damages nearby entities |

### G. Sect god powers (NEW MECHANIC)

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | G.1 No bottom-right God Power HUD visible (deleted) |
| [ ] | [ ] | G.2 Adopting a sect (build a chapel in a Temple slot) → that slot's tile shows the sect name |
| [ ] | [ ] | G.3 Adopted slot shows [Fire] button + [+ Allocate Glow] button stacked |
| [ ] | [ ] | G.4 Fire button enabled when cooldown = 0; greyed with seconds remaining otherwise |
| [ ] | [ ] | G.5 Clicking Fire triggers the sect's effect (e.g., damage/heal AOE depending on sect) and starts cooldown |
| [ ] | [ ] | G.6 With Glow in Temple's GlowStored: clicking [+ Allocate Glow] consumes 1 Glow from Temple → button changes to [◆ Glow ◆] |
| [ ] | [ ] | G.7 Clicking [◆ Glow ◆] deallocates and refunds 1 Glow to the Temple |
| [ ] | [ ] | G.8 With Glow allocated to a shrine: that sect's post-fire cooldown is HALF the normal value |
| [ ] | [ ] | G.9 Cannot allocate a second Glow to the same shrine (button stays [◆ Glow ◆] once filled) |
| [ ] | [ ] | G.10 Glow allocation across all sects is capped by Temple's available GlowStored |

### H. Equipment tier passives (with shield bar widget)

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | H.1 Trigger Crystal-tier equipment upgrade via debug → existing Swordsmen show shield bar widget |
| [ ] | [ ] | H.2 Shield bar = horizontal cyan bar above rank pips |
| [ ] | [ ] | H.3 Damaging the unit drains the cyan fill before HP drops |
| [ ] | [ ] | H.4 Out-of-combat shield regenerates after a 3s no-damage window |
| [ ] | [ ] | H.5 Siege at Crystal+ allies inside aura range get a wider shield bar (bonus stacked into ShieldBar.Max) |
| [ ] | [ ] | H.6 Magic/Support hero at Crystal+ absorbs 50% of one damage hit on a 12s cooldown |

### I. Visual indicators

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | I.1 Every unit shows 1–5 gold pip spheres above head matching UnitRank |
| [ ] | [ ] | I.2 Glow-tier unit (debug-spawn or tier-bump) shows golden emissive halo at feet |
| [ ] | [ ] | I.3 Halo disappears if the unit drops below Glow tier |

### J. Victory progress UI

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | J.1 Timeless Age (pre-culture-commit): Victory HUD is NOT visible |
| [ ] | [ ] | J.2 After choosing Alanthor: Victory HUD appears showing Alanthor's "X/N cleansed" row |
| [ ] | [ ] | J.3 Small summary line shows other cultures' progress (e.g. "Runai 0/N Feraldis 0/N") |
| [ ] | [ ] | J.4 Cleansing nodes increments the count |
| [ ] | [ ] | J.5 All nodes cleansed → row highlights "HOLDING — victory in NNN.Ns" countdown |
| [ ] | [ ] | J.6 Countdown reaches 0 → game-end banner fires |

### K. Caravans

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | K.1 Caravans render as procedural desert-traveler GameObjects (capsule + head + backpack + 2 lances) — no rock blocks |
| [ ] | [ ] | K.2 Caravans travel between trade nodes + deposit on arrival |
| [ ] | [ ] | K.3 In peacetime (no enemies within 12u): caravans are NOT controllable — right-click does nothing |
| [ ] | [ ] | K.4 Enemy within ~12u of caravan: caravan becomes selectable + commandable |
| [ ] | [ ] | K.5 After 8s of peace: caravan returns to autonomous |

### L. Minimap

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | L.1 Active ritual sites show as cyan-white blips |
| [ ] | [ ] | L.2 Free Glow pickups show as gold blips |
| [ ] | [ ] | L.3 Both visible regardless of fog of war |
| [ ] | [ ] | L.4 Crystal nodes show on minimap ONLY when explored (not pre-revealed) |

### M. Regression / stability

| PASS | FAIL | Test |
|:----:|:----:|------|
| [ ] | [ ] | M.1 20-minute match without crashes |
| [ ] | [ ] | M.2 No NullReferenceExceptions in console |
| [ ] | [ ] | M.3 No phantom Glow on the resource HUD |
| [ ] | [ ] | M.4 Caravans no longer have rocks attached |
| [ ] | [ ] | M.5 Nodes don't take damage from non-Iconoclast attacks |

### N. Failure notes

```
Test ID  | Failure description
---------+--------------------------------------------------
         |
         |
         |
         |
```

### O. Known-deferred for this pass

- Sects UI relocation to left edge vertical (mechanic is in; cosmetic move pending)
- Feraldis Violent Extraction full flow (Iconoclast does the gating; the destruction trigger may still need touch-up)
- Runai Conversion + trade lanes
- Active per-class tier abilities (Veilsteel duplicate squad, temporal echo)
- Feraldis bloodsoaked-ground kill reward
