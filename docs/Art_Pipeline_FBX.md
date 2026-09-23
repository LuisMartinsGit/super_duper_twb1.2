# Authoring a building: Blender → FBX → Unity

How procedural material work in Blender becomes a building the game can draw.
Worked against `Assets/GameData/TechTree/Age0/Buildings/Wall/Wall_segment.fbx`;
every other building follows the same three steps.

> **The FBX carries geometry, UVs and material NAMES. It does not carry
> textures.** Textures live in `.mat` assets beside the FBX and are bound by
> material name through the model importer's remap. This is not a
> preference — Blender writes texture links only from one exact node setup,
> and a re-export resets the whole `.fbx.meta` every time, so anything wired
> on the importer side is lost whenever the artist touches the model. Bound by
> name, the materials are the durable asset and the mesh is replaceable.

---

## A. Bake the procedural materials to images (Blender)

Procedural nodes do not survive an export. Each material has to become a flat
image first.

1. **Render engine must be Cycles.** `Render Properties ▸ Render Engine ▸
   Cycles`. EEVEE cannot bake to image textures. Switch back afterwards if you
   like; the bake is already saved.
2. **Give every object a clean UV unwrap.** `Edit Mode ▸ U ▸ Smart UV Project`
   is enough for architecture. Islands must not overlap — overlapping islands
   bake on top of each other and the result looks smeared. Check in the UV
   editor.
3. **Add the bake target.** For each material, in the Shader Editor:
   `Add ▸ Texture ▸ Image Texture`, then `New` on it — 2048 × 2048, **untick
   Alpha**, name it after the material (`Wooden_Stakes_BaseColor`).
   **Leave it unconnected, and leave it selected** (white outline). The bake
   writes into whichever Image Texture node is selected.
4. **Bake the base colour.** `Render Properties ▸ Bake`:
   - Bake Type: **Diffuse**
   - Contributions: untick **Direct** and **Indirect**, leave **Color** ticked
     — you want albedo, not baked lighting. The game lights the wall itself.
   - Selected to Active: **off**
   - Margin: **16 px**
   - Select the object, press **Bake**.
5. **Bake the normal map.** Not optional for a procedural material — see the
   box below. Add a second Image Texture node, `New` with **32-bit Float off**
   and **Color Space: Non-Color**, select it, set Bake Type **Normal**, Space
   **Tangent**, Bake.
6. **Roughness** the same way (Bake Type: Roughness, Non-Color). Metallic has
   no bake type; set Smoothness on the Unity material by hand if you skip it.

> ### If the base-colour bake comes out a flat, solid colour
>
> That is usually **not a failed bake**. A base-colour bake captures base
> colour and nothing else, and in most procedural wood and stone shaders the
> noise drives **Bump** and **Roughness**, not Base Color. The grain you see in
> the viewport is shading relief, so a correct albedo bake of it really is
> nearly uniform.
>
> **The one-minute test.** In the Shader Editor, drag the noise/grain node's
> output straight into `Principled BSDF ▸ Base Color`, bypassing everything
> else, and bake again:
>
> | Result | Meaning |
> |---|---|
> | the noise appears | the grain was never in the colour. Bake **Normal** and **Roughness** (steps 5–6); that is where it lives |
> | still solid | the noise itself is evaluating to a constant — its **Vector** input is wrong. A procedural node reading **UV** coordinates over a degenerate or single-point unwrap samples one spot and returns one value. Re-do the unwrap, or feed it `Texture Coordinate ▸ Object` |
>
> Put the connection back afterwards.
>
> **The other way to get a flat bake: a GEOMETRY node in the colour chain.**
> `Bevel`, `Ambient Occlusion`, `Geometry ▸ Pointiness` and friends do not
> describe a pattern in texture space — they describe the *surface*. Drive a
> Color Ramp from one and the ramp answers "which way does this face point",
> so each UV island bakes to roughly one value with a soft gradient across it.
> Under lighting in the viewport that reads as convincing streaks; flattened
> into an albedo map it reads as nothing.
>
> This is what happened to `Wooden_Stakes` on 2026-09-21. The chain was
> `Noise ▸ Color → Bevel ▸ Normal`, `Bevel ▸ Normal → Color Ramp ▸ Factor` —
> so the noise was being used as a *normal vector* that the Bevel node then
> overwrote with the real geometry, and the ramp never saw the noise at all.
> The fix is to take the Bevel out of the colour chain: **`Noise ▸ Fac →
> Color Ramp ▸ Fac`**, directly. Use the **Fac** output, not Color: Fac is the
> scalar field a ramp wants.
>
> If you want the base-colour bake to reproduce what you see in the
> viewport, everything feeding Base Color has to be a function of texture
> coordinates — noise, gradients, image textures, ramps of those — and
> nothing else.
>
> If you want the grain **in the albedo** — worth it for an RTS, where the
> camera is far enough that normal-map relief barely reads — then mix it into
> the colour in Blender before baking: `Noise ▸ Color Ramp ▸ Mix (Multiply or
> Overlay) → Base Color`. Bake after that and the albedo carries the grain on
> its own.
7. **Save the images to disk.** *This is the step that is usually missed.* A
   baked image lives in memory until it is saved and has no filepath at all.

   **There is no save button in the Shader Editor** — the one that catches
   everybody. `Image ▸ Save As…` lives only in the **Image Editor**, and only
   once that editor is showing the baked image:

   1. Click the **editor-type dropdown** in the top-left corner of any area
      and pick **Image Editor** (or use the **UV Editing** workspace tab).
   2. In its header, click the **image datablock browser** (the picture icon)
      and choose the baked image — `Wooden_Stakes_BaseColor`.
   3. Now the header has an **`Image`** menu: `Image ▸ Save As…`, or **Alt+S**
      with the cursor over that editor. `Image ▸ Save All Images` does the lot
      if your Blender has it.

   This is also **how you check the bake worked** — if the image reads black
   here, the bake is black and saving it will not help.

   Or skip the UI. Paste this into `Scripting ▸ Python Console`; it writes
   every generated image straight to where Unity wants it:

   ```python
   import bpy, os
   out = bpy.path.abspath("//")  # or paste the Unity folder path here
   for img in bpy.data.images:
       if img.source == 'GENERATED' and img.has_data:
           img.filepath_raw = os.path.join(out, img.name + ".png")
           img.file_format = 'PNG'
           img.save()
           print("saved", img.filepath_raw)
   ```

   Either way the PNGs belong in the building's Unity folder — for the wall,
   `Assets/GameData/TechTree/Age0/Buildings/Wall/`.
8. **Rewire the material to the baked image.** Delete or disconnect the
   procedural chain and connect **Image Texture ▸ Color → Principled BSDF ▸
   Base Color**, and for normals **Image Texture (Non-Color) → Normal Map →
   Normal**. The viewport in Material Preview should look the same as before.
   Keep the material's **name** unchanged: the name is the contract with
   Unity.

**Naming is the contract.** Unity binds by material name, so
`Wooden_Stakes`, `Player_cloth` and `GroundMaterial` must keep those names
across re-exports. Rename one in Blender and its Unity material silently stops
being applied.

---

## B. Export the FBX

`File ▸ Export ▸ FBX (.fbx)`, overwriting the existing file so the `.meta`'s
GUID — and every reference to it — survives.

| Panel | Setting | Value |
|---|---|---|
| Include | Limit to **Selected Objects** | on, with only the building's meshes selected |
| Include | Object Types | **Mesh** only — no Camera, no Lamp, no Armature |
| Transform | Scale | **1.0** |
| Transform | Apply Scalings | **FBX All** |
| Transform | Forward | **-Z Forward** |
| Transform | Up | **Y Up** |
| Transform | Apply Unit | on |
| Geometry | Apply Modifiers | on |
| Geometry | Smoothing | **Face** (or Normals Only) — not "Off", or Unity gets flat shading |
| Geometry | Tangent Space | on, if you baked a normal map |
| Path Mode | — | leave as **Auto**. It does not matter: no textures are being exported |

**Do not use Embed Textures.** It bloats the file, and Unity extracts embedded
textures into a folder that a re-export then orphans.

Before exporting: `Object ▸ Apply ▸ All Transforms` on the meshes, so their
rotation and scale are baked in and Unity does not inherit a 100× node scale.

---

## C. Import into Unity

1. **Drop the FBX and the baked PNGs** into the building's folder. For the
   wall that is `Assets/GameData/TechTree/Age0/Buildings/Wall/`.

   The wall has **four pieces, each with its own SO**, and the binder decides
   which is which **from the file name**:

   | Put the FBX in | Binds to | Fitted to |
   |---|---|---|
   | `Hub/` | `Hub/WallHub.asset` (pid 550) | the hub's 4 m diameter |
   | `Segment/` | `Segment/WallSegment.asset` (pid 552) | the 3 m module pitch |
   | `Tower/` | `Tower/WallTower.asset` (pid 553) | the module pitch |
   | `Gate/` | `Gate/WallGate.asset` (pid 554) | the 9 m gate span |

   The folder decides. A model loose in the wall folder falls back to its file
   name — `*_hub`, `*_segment`, `*_tower`, `*_gate`.

   A piece with no FBX keeps its procedural visual, so they can land one at a
   time.
2. **Set the normal maps' type.** Select each normal-map PNG and set
   `Texture Type: Normal map` in the Inspector. Albedo stays `Default` with
   sRGB ticked; roughness/metallic maps are `Default` with **sRGB unticked**.
3. **Assign the textures to the materials.** The `.mat` assets are already in
   the folder and already bound to the FBX's material names:

   | FBX material | Unity material | Put the baked maps here |
   |---|---|---|
   | `Wooden_Stakes` | `Wooden_Stakes.mat` | `_BaseMap`, `_BumpMap` |
   | `Player_cloth` | `Player_cloth.mat` | `_BaseMap` only — see below |
   | `GroundMaterial` | `GroundMaterial.mat` | `_BaseMap`, `_BumpMap` |

   **The ownership cloth's Base Color stays white.** The player's colour is
   applied on top per renderer; a tinted base would colour it twice. Bake it
   as a greyscale/white cloth and let the game colour it.
4. **Run `Waning Border ▸ Walls ▸ Bind Wall Art`.** It re-applies everything a
   Blender re-export just wiped — Read/Write on (the wall bakes its modules
   into one mesh, which reads vertices), cameras and lights excluded, no rig,
   material remap — then rebuilds `Wall.prefab` and writes it into
   each piece's own SO. **Re-run it after every re-export.**
5. **Read the log line it prints.** It reports the module's measured size and
   the factor it will be scaled by to meet the 3 m module pitch. A sane factor
   is near 1; a factor near 0.01 means the model came in as centimetres, which
   still works but tells you Apply Scalings was wrong.

---

## Authoring art that TILES

A curtain module is repeated end to end along a wall. **The repeat is the
width of the wall itself — the timbers — and everything else is expected to
run LONGER and lap into the next copy**, which is what hides the joint. That
is how `Wall_segment.fbx` is built, and the game measures it that way:

| Part | Spans | Role |
|---|---:|---|
| stake row (`WoodenBeams` / `Wooden_Stakes`) | 225 | **sets the repeat** — copies are spaced by this |
| cloth band (`OwnershipCloth` / `Player_cloth`) | 231 | laps ~3 units into each neighbour |
| foundation (`Foundation` / `GroundMaterial`) | 246 | laps ~10 units into each neighbour |

`WallModuleArt` finds the defining part by NAME — the part or its material
saying **stake, beam, plank, timber, palisade, masonry** or **curtain** — and
falls back to the **tallest** part, which on any wall is the wall rather than
its footing. Ownership parts are never the defining part.

So, to author a module that tiles:

1. **Name the wall part** so it is recognisable (`WoodenBeams`, material
   `Wooden_Stakes` — either is enough), or just make sure it is the tallest
   thing in the module.
2. **Give it square ends**, and make its length the spacing you want between
   copies.
3. **Let the footing, capping and trim overhang** past it, by however much
   they need to overlap cleanly. They are never measured.

`AlanthorWall.ModuleOverlap` adds EXTRA interpenetration on top and is **0**.
It is only for art whose parts all end on the same plane, which would
otherwise show a seam at every joint.

## One scale for the whole set

Every wall piece is drawn at the **curtain module's** scale — the hub, the
gatehouse and the tower all take it rather than being fitted to their own
footprints. Author the set at one scale in Blender and it is drawn at one
scale in game.

Fitting each piece to its own footprint was the earlier behaviour and was
wrong: the hub stretched by the ratio between its 4.2 m footprint and the 3 m
module, so hubs came out visibly larger than the wall they anchor.

If a piece has no curtain art to take a scale from, it falls back to fitting
its own footprint.

## If it still looks wrong

| Symptom | Cause |
|---|---|
| Magenta | The material is on the built-in shader, not URP. The `.mat` assets here are already URP/Lit; check the remap took (Inspector ▸ Materials ▸ the three names resolve) |
| Textures smeared or doubled | Overlapping UV islands. Re-unwrap with Smart UV Project |
| Seams / dark edges at island borders | Bake Margin too small — raise it to 16 px and re-bake |
| Lighting baked into the wall (shadows that do not move) | Diffuse bake had Direct/Indirect ticked. Untick both and re-bake |
| Bake is a flat solid colour, grain gone | The noise drives Bump/Roughness, not Base Color — bake those channels, or mix the noise into the colour. See the box in step 5 |
| Bake is flat AND baking the noise straight to Base Color is still flat | The noise reads UV coordinates over a broken unwrap. Re-unwrap, or drive it from `Texture Coordinate ▸ Object` |
| Bake is flat, and a `Bevel` / `AO` / `Pointiness` node feeds the colour | A geometry node describes the surface, not a texture-space pattern. Take it out of the colour chain — see the box in step 5 |
| Grain size changes per island, or breaks at seams | A procedural node on `Texture Coordinate ▸ UV` is sized by each island's UV area. Use `Object` coordinates for consistent grain in metres |
| Texture looks low-res in game although it is 2048² | The UV islands cover only a fraction of the map. Check coverage and scale the islands up to fill it |
| Normal map looks inverted | Blender is OpenGL-handed and Unity agrees, so this is almost always a missing `Texture Type: Normal map` in Unity |
| Wall renders untextured after a re-export | `isReadable` was reset. Re-run Bind Wall Art |
| Ownership part is the wrong colour | Its Base Color is not white, or the part is no longer named `*Ownership*` / `Player_*` |

---

## One thing worth changing later

The wall bakes into one mesh per segment with **one sub-mesh per material**, so
three materials means three draw calls per wall. Two would do: the ownership
cloth has to stay separate (it is tinted per renderer), but `Wooden_Stakes` and
`GroundMaterial` could share one atlas and one material. That needs a single
shared UV layout across both meshes — worth doing when the art settles, not
before.
