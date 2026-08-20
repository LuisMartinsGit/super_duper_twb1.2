# Synty Character + Archer Animations → Archer Unit (Longbowman)

This guide is grounded in the actual assets in
[`Assets/GameData/TechTree/Units/Alanthor/Longbowman/`](../../Assets/GameData/TechTree/Units/Alanthor/Longbowman/):

| Asset | Role | Status |
|-------|------|--------|
| `Character_Soldier_01.fbx` | Synty humanoid character | Rig **already Humanoid** ✅ |
| `Materials/Characters_Texture_Blue.mat`, `Texture_Alt_04_Dark.mat` | character materials | **Already extracted** ✅ |
| `Animations/standing idle 01.fbx` … (40+ clips) | archer animation set | ⚠️ imported as **Generic** — must be fixed |
| `Longbowman.asset` | unit stats (`UnitDefSO`) | `presentationId 0`, no prefab — must be wired |

> **"Archer" vs "Longbowman":** these assets belong to the **Longbowman** unit
> (an archer-type ranged unit). The Age-0 **Archer** is a *separate* unit
> (`presentationId 202`). Everything below targets the Longbowman; to do the
> Archer instead, see [§6](#6-doing-the-age-0-archer-instead).

---

## TL;DR — the one-click tool

I added an Editor tool that does all four mechanical steps for you. In Unity:

> **Tools → Waning Border → Build Longbowman Unit Visual**

It will:
1. **Remap the character's materials** to the extracted `.mat` files (fixes the
   "all grey" character).
2. Reimport the idle / move / shoot / death clips as **Humanoid** copied from the
   character's avatar (+ Loop on idle & move). *(fixes the animation bug)*
3. Create `Longbowman.controller` with the exact parameters the game drives.
4. Build `Longbowman.prefab` (character + configured Animator).
5. Set the SO's `presentationId = 205` and assign the prefab.

Source: [`Assets/Editor/LongbowmanUnitBuilder.cs`](../../Assets/Editor/LongbowmanUnitBuilder.cs).
The clip choices and target unit are constants at the top of that file — change
them and re-run to use different animations or target the Age-0 Archer.

After it runs, jump to [§5 Test it](#5-test-it). If you'd rather understand and
do each step by hand, read on.

---

## 0. How the pipeline works (why these exact steps)

Units are **ECS entities** (pure data). The MonoBehaviour
[`PresentationSpawnSystem`](../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs)
spawns a **visual GameObject** for each entity, choosing the prefab by the
entity's **`PresentationID`**.

- The Longbowman factory spawns with **`PresentationID = 205`**
  ([`Longbowman.cs:29`](../../Assets/Scripts/Entities/Units/Longbowman.cs#L29)).
- The spawn system resolves a prefab from the **SO's `prefab` field**, looked up
  by `presentationId` in [`TechCatalog`](../../Assets/Scripts/Data/TechTree/TechCatalog.cs#L108).
  Two requirements there: **the SO's `presentationId` must be non-zero** *and*
  match the factory (205), and **`prefab` must be assigned**. (PID 205 has **no**
  Resources-path fallback, so the SO route is the only one for this unit — which
  is why it currently spawns as a placeholder capsule.)

Once spawned, [`UnitAnimationSync`](../../Assets/Scripts/Presentation/UnitAnimationSync.cs)
is **auto-attached** (any unit prefab with an `Animator`) and drives these
parameters from ECS state — **names are matched by hash, so spelling is exact**
([`UnitAnimationSync.cs:37-42`](../../Assets/Scripts/Presentation/UnitAnimationSync.cs#L37-L42)):

| Parameter | Type | Driven by (archer-relevant) |
|-----------|------|------------------------------|
| `IsMoving` | bool | has a movement destination |
| `IsAttacking` | bool | `ArcherState.IsFiring == 1` (combat system sets it) |
| `IsDead` | trigger | health hit 0 |
| `AttackSpeed` | float | attack-rate multiplier |
| `IsWorking`, `IsHealing` | bool | miner/builder/litharch only — ignore |

---

## 1a. Fix the grey character (material remap)

The `.mat` files in `Longbowman/Materials/` are **correct** — `Characters_Texture_Blue.mat`
already has its texture assigned (`_BaseMap` → `Characters_Texture_Blue.png`). The
character is grey because the **FBX's material slots aren't pointing at those
`.mat` files**: its import data has `externalObjects: {}` (empty). "Extract
Materials" created the assets but didn't **remap** the model to them.

Fix it on the **character FBX**:

1. Select `Character_Soldier_01.fbx` → **Materials** tab.
2. **Material Creation Mode** → leave as is; set **Location → Use External Materials**.
3. Under **On Demand Remap**: **Naming → By Base Texture Name** (or *Material
   Name*), **Search → Everywhere**, then click **Search and Remap**.
4. **Apply.** The body picks up `Characters_Texture_Blue.mat` and renders textured.

*(The tool's step 0 does exactly this — `SearchAndRemapMaterials`.)*

## 1b. Fix the animation import (the actual blocker)

Every FBX in `Animations/` is
imported as **Generic** (`animationType: 2`) while the character is **Humanoid**.
Generic clips do not retarget onto a Humanoid avatar, so the unit won't animate.

> **Critical:** the character is a **Synty** rig (bone `Hips`) but these clips are
> **Mixamo** (bone `mixamorig:Hips`) — **different skeletons**. Do **NOT** use
> *Copy From Other Avatar* — it requires identical bone hierarchies and throws
> `Rig Error: Transform 'Hips' for human bone 'Hips' not found`. Use **Create
> From This Model** on every FBX; the Humanoid abstraction retargets across the
> two skeletons automatically.

Fix each clip you intend to use:

1. Select the clip FBX (e.g. `Animations/standing idle 01.fbx`).
2. **Rig** tab:
   - **Animation Type → Humanoid**
   - **Avatar Definition → Create From This Model** *(not Copy From Other Avatar)*
   - **Apply** — Unity auto-maps the Mixamo skeleton and builds an avatar for it.
3. **Animation** tab → select the clip:
   - **Loop Time = ON** for `standing idle 01` and your move clip
     (`standing run forward`). **OFF** for `standing draw arrow` and the death clip.
   - **Root Transform Position (XZ) → Bake Into Pose** so the unit doesn't drift
     (ECS owns position).

Do the same on the **character** FBX (`Create From This Model`) — it already is.
The prefab's Animator uses the character's avatar; the clips retarget onto it.

The tool in the TL;DR does exactly this for the four clips it uses.

---

## 2. Animator Controller (the tool builds this; here's what it builds)

`Longbowman.controller` with:

**Parameters:** `IsMoving` (bool), `IsAttacking` (bool), `IsDead` (trigger),
`AttackSpeed` (float, default 1).

**States** (clip in parentheses):

| State | Clip | Default? |
|-------|------|----------|
| `Idle` | `standing idle 01` | ✅ |
| `Move` | `standing run forward` | |
| `Shoot` | `standing draw arrow` (speed × `AttackSpeed`) | |
| `Death` | `standing death backward 01` | |

**Transitions** (Has Exit Time OFF unless noted):

| From | To | Condition |
|------|----|-----------|
| Idle | Move | `IsMoving == true` |
| Move | Idle | `IsMoving == false` |
| Idle | Shoot | `IsAttacking == true` |
| Move | Shoot | `IsAttacking == true` |
| Shoot | Idle | `IsAttacking == false` *(Exit Time 0.9 — shot finishes first)* |
| **Any State** | Death | `IsDead` (trigger) |

> **Upgrade idea:** for a crisper shot, split `Shoot` into `Draw → Recoil` using
> `standing draw arrow` then `standing aim recoil`, with Recoil → Idle on exit.
> The single-state version is what the tool generates for reliability.

**Doing it by hand** instead: **Create → Animator Controller**, add the four
parameters (exact names!), drag the four clips in as states, set `Idle` as the
default state, and wire the transitions above.

---

## 3. The prefab (detailed — answers "need more detail")

The tool produces `Longbowman.prefab`. To build/inspect it manually:

1. **Drag `Character_Soldier_01.fbx` into an empty scene.** You get a character
   instance with a `SkinnedMeshRenderer` (showing the Synty soldier) and usually
   an `Animator` on the root.
2. **Select the root → `Animator` component:**
   - **Controller → `Longbowman.controller`**
   - **Avatar → `Character_Soldier_01Avatar`**
   - **Apply Root Motion → OFF** (ECS drives movement; root motion would fight it)
   - **Update Mode → Normal**, **Culling Mode → Cull Update Transforms** (cheap
     for an RTS with many units)
3. **Bow / weapon:** if the soldier mesh doesn't already hold a bow, drag a bow
   mesh under the right-hand bone
   (`Root/.../RightHand` — expand the rig in the Hierarchy), then zero its local
   transform and nudge it into the grip. The Synty soldier may already include
   one; skip if so.
4. **Scale:** leave the root at `(1,1,1)`. The spawn system multiplies by the
   ECS `LocalTransform.Scale` (Longbowman spawns at `1`). If the character is the
   wrong size, fix it on the **FBX import → Scale Factor**, not the prefab root,
   so the runtime collider stays correct.
5. **Orientation:** units get **no rotation offset** (only buildings get a 180°
   flip — [`PresentationSpawnSystem.cs:146-151`](../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L146-L151)).
   Synty characters face **+Z**, which is correct. If it faces backward, rotate
   the **child mesh**, never the root.
6. **Do NOT add** `UnitAnimationSync`, a `Collider`, or `EntityReference` — the
   spawn system adds those at runtime
   ([`PresentationSpawnSystem.cs:533-568`](../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L533-L568)).
7. **Drag the configured root into the Project** at
   `Assets/GameData/TechTree/Units/Alanthor/Longbowman/Longbowman.prefab`, then
   delete the scene instance.

### Faction colour — important for an RTS

`ApplyFactionColor` ([`PresentationSpawnSystem.cs:627-660`](../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L627-L660))
tints the unit by ownership:

- If a child renderer's GameObject name contains **`faction`**, only **those**
  renderers are tinted; the rest of the Synty texturing is preserved.
- Otherwise **every material is overwritten** with the flat player colour — the
  whole soldier becomes one solid colour.

The Synty soldier is a **single mesh**, so by default it goes fully player-colour.
If you want to keep the texture, add a **small accent mesh** (cape, sash, shield
trim) as its own renderer named e.g. `Longbowman_FactionAccent`. Only that part
will then recolour per player.

---

## 4. Wire the unit data (the tool does this)

On `Longbowman.asset` ([`UnitDefSO`](../../Assets/Scripts/Data/TechTree/Definitions/UnitDefSO.cs)):

- **`Presentation → Presentation Id` = `205`** (must match
  [`Longbowman.cs:29`](../../Assets/Scripts/Entities/Units/Longbowman.cs#L29); it
  is `0` today, which disables prefab registration).
- **`Presentation → Prefab`** = the `Longbowman.prefab` you just built.

---

## 5. Test it

1. Play a scenario where the Longbowman can be trained (Alanthor Archery Range
   tier — see [`docs/Design/Age_1_Alanthor.md`](../Design/Age_1_Alanthor.md)).
2. Train one and verify:
   - **Spawns as the Synty soldier** (not a white capsule).
   - **Plays run** when moving (`IsMoving`).
   - **Plays draw/shoot** when firing at a target (`IsAttacking` ←
     `ArcherState.IsFiring`).
   - **Plays death** when killed (`IsDead`).

### Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| White capsule | SO `presentationId` ≠ 205, or `prefab` not assigned. |
| Spawns but T-poses | Clips still **Generic** (§1), or controller/avatar not set on the Animator. |
| Moves but legs don't | Move clip not on the `Move` state, or **Loop Time** off. |
| Slides / drifts | Root motion ON, or clip root-XZ not **Bake Into Pose**. |
| Never shoots | `IsAttacking` misspelled, or no Idle/Move → Shoot transition. |
| Whole unit is one colour | Expected — add a `faction`-named accent (§3). |
| Faces wrong way | Rotate the **child mesh**, not the root. |

---

## 6. Doing the Age-0 Archer instead

Same process, different target. In
[`ArcherUnitBuilder.cs`](../../Assets/Editor/ArcherUnitBuilder.cs) change the
CONFIG block:

```
UnitFolder   = "Assets/GameData/TechTree/Units/Age 0/Archer"
CharacterFbx = <that folder>/<your Synty character>.fbx
UnitSO       = <that folder>/Archer.asset
PresentationId = 202   // Archer.cs PresentationID
```

The Archer (`202`) *also* has a Resources-path fallback
(`"Prefabs/Units/Archer"` in [`PresentationSpawnSystem.cs:44`](../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L44)),
so for that unit you can alternatively drop the prefab at
`Assets/Resources/Prefabs/Units/Archer.prefab` and skip the SO wiring.

---

## Checklist

- [ ] Character FBX = Humanoid (✅ already), materials extracted (✅ already)
- [ ] Idle / Move / Shoot / Death clips reimported **Humanoid + Create From This Model**
- [ ] Loop on idle & move; root XZ baked into pose
- [ ] `Longbowman.controller` params `IsMoving`/`IsAttacking`/`IsDead`/`AttackSpeed`
- [ ] `Longbowman.prefab` = character + Animator (controller + avatar, no root motion)
- [ ] SO `presentationId = 205` **and** `prefab` assigned
- [ ] (Optional) `faction`-named accent renderer for team colour
- [ ] Play-test: spawn, move, shoot, die all animate
