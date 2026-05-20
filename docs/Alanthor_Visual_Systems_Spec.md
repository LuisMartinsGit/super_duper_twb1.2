# Spec: Alanthor Visual Systems

**Scope:** Building scale standards, faction color tinting on Synty kit assets, and procedural damage visualization, with all conventions and asset wiring scoped to the **Alanthor** culture as the first implementation. Runai and Feraldis follow the same systems but use their own materials/prefabs.

**Touches:** Rendering, Buildings, FX, Economy (faction color lookup). No ECS schema changes.

---

## 1. Goals

1. Establish a single unified "look" pipeline for Alanthor buildings made from Synty kit modules.
2. Tint a *specific* set of decorative elements (cloth awnings, banners, painted stripes) to the owning player's faction color — without tinting walls, wood, or stone.
3. Show damage state automatically from `HealthState` without authoring multiple full-mesh variants per building.
4. Codify scale and silhouette rules so future Alanthor buildings ship consistently.

## 2. Non-Goals

- No PBR upgrades; Alanthor stays flat-shaded Synty.
- No real-time mesh fracture or runtime cutting.
- No changes to combat, projectile, or HP simulation. Visual layer only.
- Runai and Feraldis art/wiring are out of scope for this spec (their materials/SOs are stubbed but unfilled).

---

## 3. Alanthor Aesthetic Direction

Reference: the desert market building in the current Synty Alanthor kit (sand-adobe walls, wood scaffolding, green/orange/red cloth awnings, dark banner over door, rooftop chimney).

| Element | Role | Faction-tinted? | Damage-droppable? |
|---|---|---|---|
| Adobe walls (sand color) | Structural | No | No (replaced via ruin prefab at 0 HP) |
| Wooden frame / scaffolding | Structural | No | Partial — outer scaffolding can drop |
| Cloth awning | Decorative | **Yes** | Yes — drops early |
| Door banner | Decorative | **Yes** | Yes — drops with awning |
| Rooftop chimney / pole | Decorative | No | Yes — drops mid-damage |
| Painted door arch outline | Decorative | **Yes** (low-priority polish) | No |
| Crates, barrels, drying racks | Prop | No | Yes — drops first |

Visual rules:
- **One tinted cloth color per building family**, not three. Today's market prefab has green + orange + red awnings — re-target their UVs in Blender (or pick one and use it) so all awnings sample the same atlas pixel and tint as one.
- **Stone/wood stays sand-colored regardless of owner.** Only cloth signals ownership. This keeps Alanthor reading as Alanthor across all 8 player slots.
- **Banner = the strongest signal.** Place one large faction-colored banner over every primary doorway. This is the player's at-a-glance "whose town is this."

---

## 4. System A — Faction Color Tinting

### 4.1 Approach

Synty kit modules typically pack multiple visible colors (cloth + wood) into a single mesh sharing a single atlas material. Tinting at the renderer level would tint everything. Instead, tint at the **pixel level** using a color-key replacement shader: the shader samples the atlas, and any pixel whose color matches a designated key gets replaced with the faction color. Wood and stone pixels pass through untouched.

### 4.2 Files to add

| Path | Purpose |
|---|---|
| `Assets/Art/Shaders/SyntyFactionTint.shadergraph` | URP Shader Graph implementing color-key replacement |
| `Assets/Art/Materials/Alanthor/M_Alanthor_AtlasTint.mat` | Material instance using the shader + Alanthor atlas |
| `Assets/Scripts/Rendering/FactionColorBinding.cs` | MonoBehaviour applying `_FactionColor` via `MaterialPropertyBlock` |
| `Assets/Scripts/Rendering/IFactionColorSource.cs` | Tiny interface so the visualizer can resolve a color from any owner type |

### 4.3 Shader Graph contract

`SyntyFactionTint.shadergraph` — URP Lit, opaque.

**Properties**
- `_BaseMap` (Texture2D) — Synty atlas, default = Alanthor atlas.
- `_KeyColor` (Color, no HDR) — atlas color to be replaced. Eyedrop from atlas PNG inside the Inspector to match sRGB→linear conversion.
- `_KeyTolerance` (Float, default `0.03`) — match radius in linear RGB.
- `_FactionColor` (Color, default white) — runtime tint, set per-renderer via MPB.
- `_TintSaturation` (Float, default `0.85`) — global de-saturation factor applied to `_FactionColor` to prevent neon cloth.

**Fragment logic**
1. Sample `_BaseMap` at mesh UV → `sample`.
2. `dist = distance(sample.rgb, _KeyColor.rgb)`.
3. `mask = step(dist, _KeyTolerance)`.
4. `tinted = sample.rgb * (1 - mask) + _FactionColor.rgb * _TintSaturation * mask`.
5. Output `tinted` to Base Color; pass `sample.a` to Alpha.
6. Smoothness/Normal/Metallic stay at Synty defaults.

### 4.4 `FactionColorBinding.cs`

```csharp
namespace TheWaningBorder.Rendering
{
    public class FactionColorBinding : MonoBehaviour
    {
        [Tooltip("Renderers using the SyntyFactionTint shader. _FactionColor will be set per-renderer via MPB.")]
        [SerializeField] private Renderer[] tintedRenderers;

        private static readonly int FactionColorId = Shader.PropertyToID("_FactionColor");
        private MaterialPropertyBlock _mpb;

        public void SetFactionColor(Color color)
        {
            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < tintedRenderers.Length; i++)
            {
                var r = tintedRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(FactionColorId, color);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
```

### 4.5 ECS bridge

After a building entity is instantiated and stamped with `OwnerFaction`, the building factory in `Entities/Buildings/` resolves the prefab GameObject and calls:

```csharp
if (go.TryGetComponent<FactionColorBinding>(out var binding))
    binding.SetFactionColor(FactionColors.Get(ownerFaction));
```

`FactionColors.Get(Faction)` already exists in `Core/Settings/FactionColors.cs`. No new lookup needed.

### 4.6 Material wiring (per prefab)

For every Alanthor building prefab:
1. Identify renderers containing cloth/banner geometry.
2. Replace their material reference with `M_Alanthor_AtlasTint`.
3. Add `FactionColorBinding` to the prefab root; drag those renderers into `tintedRenderers`.
4. In `M_Alanthor_AtlasTint`, set `_KeyColor` once (eyedrop the Alanthor cloth pixel from the atlas in the Inspector preview).

---

## 5. System B — Procedural Damage Visualization

### 5.1 Approach

Drive visualization from the existing `HealthState`. Use four stacked layers:

1. **Modular drop-off** — hide kit modules as HP crosses thresholds. Re-uses existing Synty modules; zero new art.
2. **Particle FX** — smoke at 50%, fire at 20%.
3. **Decal hits** — burn/crack decals at projectile impact points.
4. **Ruin swap** — replace the entire prefab with `<Building>_Ruin_Alanthor` at 0 HP.

### 5.2 Files to add

| Path | Purpose |
|---|---|
| `Assets/Scripts/Rendering/BuildingDamageVisualizer.cs` | MonoBehaviour driving all four damage layers |
| `Assets/Scripts/Rendering/BuildingDamageProfile.cs` | `ScriptableObject` defining per-archetype damage curves |
| `Assets/Data/Damage/Alanthor/BDP_Alanthor_Hall.asset` | Damage profile for Alanthor Hall |
| `Assets/Data/Damage/Alanthor/BDP_Alanthor_Barracks.asset` | Damage profile for Alanthor Barracks |
| `Assets/Data/Damage/Alanthor/BDP_Alanthor_GathererHut.asset` | Damage profile for Alanthor GathererHut |
| `Assets/Data/Damage/Alanthor/BDP_Alanthor_Hut.asset` | Damage profile for Alanthor Hut |
| `Assets/Art/FX/FX_AdobeSmoke_Light.prefab` | Light dust/smoke particle |
| `Assets/Art/FX/FX_AdobeSmoke_Dark.prefab` | Black smoke + small flames |
| `Assets/Art/FX/FX_DebrisPuff_Alanthor.prefab` | Burst spawned when a module hides |
| `Assets/Art/Prefabs/Buildings/Alanthor/<Name>_Ruin.prefab` | Pre-built ruin per building (one variant in v1) |

### 5.3 `BuildingDamageProfile.cs`

```csharp
namespace TheWaningBorder.Rendering
{
    [CreateAssetMenu(menuName = "TWB/Rendering/Building Damage Profile")]
    public class BuildingDamageProfile : ScriptableObject
    {
        [Range(0f, 1f)] public float smokeAt = 0.5f;
        [Range(0f, 1f)] public float fireAt  = 0.2f;
        [Range(0f, 1f)] public float ruinAt  = 0.0f;

        public ModuleEntry[] modules;

        [System.Serializable]
        public struct ModuleEntry
        {
            public string slotKey; // matches BuildingDamageVisualizer slot key
            [Range(0f, 1f)] public float hideBelowHealthFraction;
            [Range(0f, 0.2f)] public float randomJitter;
            public bool spawnDebrisOnHide;
        }
    }
}
```

### 5.4 `BuildingDamageVisualizer.cs` (shape)

- Holds a list of `(slotKey, GameObject)` pairs wired in the prefab inspector.
- Reads its `BuildingDamageProfile` SO (assigned per archetype).
- On `Awake`, bakes jittered thresholds per slot once so the same building doesn't flicker.
- Exposes `SetHealthFraction(float)` which:
  - Toggles modules whose threshold was crossed (since last call).
  - On a downward hide, spawns `FX_DebrisPuff_Alanthor` at the module's transform.
  - Toggles light smoke / dark smoke / fire FX based on the profile.
  - At `<= ruinAt`, raises `OnRuined` event for the factory to swap to ruin prefab.

### 5.5 Alanthor damage profiles (v1)

**`BDP_Alanthor_Hall`** (most prominent building)

| slotKey | hideBelow | jitter | debris |
|---|---|---|---|
| `prop_crates` | 0.90 | 0.05 | yes |
| `cloth_awning_a` | 0.75 | 0.10 | yes |
| `cloth_awning_b` | 0.65 | 0.10 | yes |
| `door_banner` | 0.55 | 0.05 | yes |
| `chimney_pole` | 0.40 | 0.05 | yes |
| `roof_box` | 0.20 | 0.00 | yes |

Smoke 0.50, Fire 0.20, Ruin 0.00.

**`BDP_Alanthor_Barracks`** — drop training-dummy/weapon-rack props first (`0.85`), then banners (`0.55`), then roof (`0.20`).

**`BDP_Alanthor_GathererHut`** — drop drying racks (`0.80`), cloth (`0.60`), roof (`0.25`).

**`BDP_Alanthor_Hut`** (small) — fewer modules; cloth at `0.50`, roof at `0.20`.

### 5.6 ECS bridge

Add `Assets/Scripts/Systems/Rendering/BuildingDamageSyncSystem.cs` — a `SystemBase` running on the main thread (managed, no Burst). It:

1. Queries entities with `HealthState` + `BuildingTag` + a managed component `BuildingVisualRef` (added by the building factory at spawn, pointing to the MonoBehaviour).
2. Compares `current/max` against a cached last-seen value on `BuildingVisualRef`.
3. Calls `SetHealthFraction` only when the fraction actually crosses any defined threshold. Skip otherwise — no per-frame churn.

This avoids polling every building every frame for a value that rarely changes.

### 5.7 Ruin handling

When `BuildingDamageVisualizer.OnRuined` fires:
- The owning building factory destroys the live prefab GameObject (visual only — the entity continues its destruction sequence in ECS).
- Spawns `<Name>_Ruin.prefab` at the same transform, parented to a `Ruins` root.
- Ruin prefab has no `FactionColorBinding`, no `BuildingDamageVisualizer`. It's static dressing.
- Ruins despawn after `RuinDecayTime` (config: 60s, in `CultureConfig`).

---

## 6. System C — Alanthor Building Composition Standards

These are authoring rules, not code. They live in a short doc inside the prefab folder.

### 6.1 Scale calibration

- **Reference unit:** Alanthor Miner. Snapshot in `Assets/Art/Reference/Alanthor_ScaleReference.png`.
- **Reference building:** Alanthor Hall.
- A miner's head should reach ~⅓ of the Hall's wall height.
- All other Alanthor buildings scale relative to the Hall, not to footprint tile count.
- Building footprints are set in code to match the visual, not the other way around. Adjust `BuildingConstructionSystem.cs` placement footprints if the visual demands it.

### 6.2 Silhouette priority

For each Alanthor building, ensure:
- One **hero element** readable from the RTS camera (chimney, banner, dome, market awning cluster).
- Roof shape is distinct from neighboring buildings in the kit.
- Height compressed to 60–80% of stock Synty heights.

### 6.3 Tintable element inventory

Every Alanthor building must have **at least one** tintable cloth element. New prefabs without one fail review. Halls and Barracks must have a tintable banner over the primary entrance.

---

## 7. Implementation Plan (phased)

**Phase 1 — Shader & tint foundation**
- Create `SyntyFactionTint.shadergraph`.
- Create `M_Alanthor_AtlasTint` and set `_KeyColor` from the Alanthor cloth pixel.
- Write `FactionColorBinding.cs`.
- Wire one prefab end-to-end (the market building from the screenshot) and verify all 8 factions render correctly.

**Phase 2 — Tint rollout**
- Re-target awning UVs to a single key pixel in Blender for any module with multiple cloth colors.
- Apply `M_Alanthor_AtlasTint` + `FactionColorBinding` to: Alanthor Hall, Barracks, GathererHut, Hut.
- ECS factory call inserted in building spawn path.

**Phase 3 — Damage core**
- Write `BuildingDamageProfile` SO and `BuildingDamageVisualizer` MonoBehaviour.
- Create the four particle FX prefabs and one shared debris puff.
- Author `BDP_Alanthor_Hall` and wire it to the Hall prefab.

**Phase 4 — Damage rollout & ECS sync**
- Author remaining three Alanthor `BDP_*` assets.
- Write `BuildingDamageSyncSystem`.
- Add `BuildingVisualRef` managed component; populate in building factory.

**Phase 5 — Ruins**
- Build `<Name>_Ruin.prefab` for each of the four Alanthor archetypes (one variant each in v1).
- Hook `OnRuined` → factory swap.
- Add `RuinDecayTime` to `CultureConfig`.

**Phase 6 — Decals (optional polish)**
- URP decal projector pool with 4 burn/crack textures.
- Spawn a decal at projectile impact points on building hits.

---

## 8. Acceptance Criteria

1. Spawning an Alanthor Hall as Blue player tints all cloth/banner elements blue; walls and wood remain sand. Same for all 8 factions.
2. Two adjacent Alanthor Halls owned by Blue and Yellow render correctly side by side with no material instance leak (verify in Frame Debugger that `FactionColorBinding` uses MPB, not material clones).
3. Damaging an Alanthor Hall from 100% → 0% sheds modules in the order defined by `BDP_Alanthor_Hall`. Light smoke appears at 50%, fire at 20%, ruin prefab appears at 0%.
4. Two Alanthor Halls damaged to 50% show **slightly different** missing modules due to `randomJitter`.
5. `BuildingDamageSyncSystem` runs zero work per frame on undamaged buildings (verify in Profiler with 50 idle buildings).
6. Ruined building leaves a ruin prop that despawns after `RuinDecayTime`.
7. Healing a damaged building (HP rising) restores hidden modules in reverse order.
8. All Alanthor building prefabs pass the composition checklist in §6.3.

---

## 9. Open Questions

1. **Healing direction.** Are buildings ever healed in TWB? If yes, §8 criterion 7 stands. If no, drop it and let modules stay hidden once dropped (saves a code path).
2. **Multi-cloth tinting.** The current Synty Alanthor market kit uses three cloth atlas pixels (green, orange, red). Do we re-target UVs to one pixel (cleanest), or extend the shader to accept an array of key colors? Re-target is recommended; confirm with art.
3. **Ruin decay.** 60s default — does this conflict with vision/scouting requirements? Should ruins be queryable as gameplay objects (cover, salvage)? If yes, this spec doesn't cover that; spin up a separate spec.
4. **Sect overlay.** The sect system (chapels, Temple, sect-unique buildings) may want its own tintable layer distinct from faction color (e.g. sect emblem on the banner). Out of scope here, but the shader supports a second key/color pair if added later — flagging for forward-compatibility.
5. **Sandbox preview building.** Should the in-build-menu preview (pre-placement ghost) render with the player's tint already applied, or stay neutral? Recommend: tinted, so players see what they're getting.

---

## 10. Risks

- **sRGB/linear color picking on `_KeyColor`** is the most common failure mode. If cloth tints look wrong on first wire-up, this is almost certainly the cause — picked in the wrong color space.
- **Module slot keys drift** between profile SO and prefab. Mitigation: validation pass in `BuildingDamageVisualizer.OnValidate` that warns when a profile references a slot key not present on the prefab.
- **Decal projector cost** at scale. Mitigation: hard cap pool size at 12 per building, recycle oldest; gate behind a quality setting.
