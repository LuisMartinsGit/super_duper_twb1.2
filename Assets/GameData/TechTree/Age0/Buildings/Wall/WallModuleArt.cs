// WallModuleArt.cs
// An authored wall PART — hub, curtain module or gatehouse — measured and
// fitted to the size the sim uses for it. Each piece has its OWN
// BuildingDefSO carrying its own prefab and presentation id (Hub/WallHub,
// Segment/WallSegment, Gate/WallGate, Tower/WallTower); this is what turns
// any of them into something the wall can draw.
//
// The wall is still ONE mesh per segment — the thing the player asked for and
// the thing that keeps a long wall at one draw call per material. What
// changed is where the geometry comes from: instead of a cross-section swept
// by WallCurveMesh, the authored module is BAKED along the same curve, one
// copy per sim cell, and the copies are combined. Seamless, because the pitch
// is the module's own measured length, and art-driven, because the module is
// whatever the FBX says it is.
//
// Preparation happens once per prefab and is cached:
//   * every MeshFilter is collected with its material and its transform
//     relative to the model root;
//   * junk that rides along in a Blender export is dropped — anything with a
//     Camera or a Light, and any node whose footprint is absurd next to the
//     rest (the default 35 m ground plane);
//   * the remainder is measured, and a NORMALISING matrix is worked out that
//     turns the module into the sim's own frame: long horizontal axis along
//     +Z (the wall's run), base at y = 0, length exactly InstanceSpacing.
//     So the art does not have to be authored to the game's numbers — it is
//     fitted to them.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class WallModuleArt
    {
        /// <summary>One sub-mesh of the module, in the normalised frame.</summary>
        public readonly struct Piece
        {
            public readonly Mesh Mesh;
            public readonly int SubMesh;
            /// <summary>Index into <see cref="Materials"/>.</summary>
            public readonly int MaterialSlot;
            public readonly Matrix4x4 Local;
            public Piece(Mesh mesh, int subMesh, int materialSlot, Matrix4x4 local)
            { Mesh = mesh; SubMesh = subMesh; MaterialSlot = materialSlot; Local = local; }
        }

        public readonly List<Piece> Pieces = new List<Piece>();
        /// <summary>Distinct materials, in the order sub-meshes are written.</summary>
        public readonly List<Material> Materials = new List<Material>();
        /// <summary>Which of those slots carry the OWNER's colour — the blue
        /// posts on the authored module. They are tinted per renderer, so one
        /// shared material serves every faction's walls.</summary>
        public readonly HashSet<int> OwnershipSlots = new HashSet<int>();
        /// <summary>The module's length along the wall after normalising — the
        /// tiling pitch. Equals the fit length by construction.</summary>
        public float Length { get; private set; }
        /// <summary>The uniform scale the model was fitted by. Other wall
        /// pieces reuse it so the set stays at ONE world scale instead of each
        /// piece being stretched to its own footprint.</summary>
        public float Scale { get; private set; }
        /// <summary>Its height after normalising, for the construction rise.</summary>
        public float Height { get; private set; }
        public bool IsValid => Pieces.Count > 0;
        /// <summary>The prefab the pieces came from, for the paths that
        /// instantiate a module rather than baking it (a straight segment's
        /// per-cell visual).</summary>
        public GameObject Prefab { get; private set; }
        /// <summary>Model space -> the sim's module frame. Applied to an
        /// instantiated module so it matches the baked ones exactly.</summary>
        public Matrix4x4 Normalise { get; private set; }

        static readonly Dictionary<(GameObject, float, bool), WallModuleArt> _cache
            = new Dictionary<(GameObject, float, bool), WallModuleArt>();

        /// <summary>
        /// The prepared CURTAIN module for a wall level, or null when that
        /// level has no authored art and the procedural cross-section should
        /// be swept instead.
        ///
        /// Every level resolves to the same presentation id today, because
        /// there is one authored module (the wooden wall) and the stone and
        /// reinforced levels are still swept. When their art lands they get
        /// their own ids and this is where the tier picks between them — the
        /// callers do not change.
        /// </summary>
        public static WallModuleArt For(byte tier)
            => ForPart(TheWaningBorder.Entities.AlanthorWall.InstancePresentationID,
                       TheWaningBorder.Entities.AlanthorWall.InstanceSpacing,
                       orientAlongZ: true);

        /// <summary>
        /// Any authored wall part — hub, curtain module, gatehouse — measured
        /// and fitted to the size the SIM uses for it. The prefab comes from
        /// that piece's own SO, resolved by presentation id.
        /// Returns null when that part has no art, which is the signal to
        /// draw it procedurally.
        /// </summary>
        /// <param name="fitLength">What the model's longest horizontal axis
        /// is scaled to: a module's pitch, a hub's diameter, a gate's span.
        /// The art is fitted to the game rather than the other way round, so
        /// a re-export at a different scale keeps working.</param>
        /// <param name="orientAlongZ">True turns the longest horizontal axis
        /// to +Z, which is how the wall runs. False leaves the model's own
        /// facing — a hub is round and has no run to align to.</param>
        /// <param name="explicitScale">Scale the model by THIS instead of
        /// fitting it to <paramref name="fitLength"/>. How the hub, gate and
        /// tower stay the same size as the curtain: they take the curtain's
        /// scale, so a set authored at one scale is drawn at one scale. Fitting
        /// each piece to its own footprint instead blew the hub up by the ratio
        /// between its 4.2 m footprint and the 3 m module.</param>
        public static WallModuleArt ForPart(int presentationId, float fitLength, bool orientAlongZ,
                                            float? explicitScale = null)
        {
            if (!TechCatalog.TryGetPrefab(presentationId, out var prefab) || prefab == null) return null;
            var key = (prefab, explicitScale ?? fitLength, orientAlongZ);
            if (_cache.TryGetValue(key, out var cached))
                return cached != null && cached.IsValid ? cached : null;

            var art = Prepare(prefab, fitLength, orientAlongZ, explicitScale);
            _cache[key] = art;                         // null results cache too
            return art != null && art.IsValid ? art : null;
        }

        /// <summary>
        /// Drop an authored part into the scene under <paramref name="parent"/>,
        /// already in the sim's frame. The path taken by the visuals that draw
        /// ONE of something (a hub, a gatehouse); the curtain bakes its copies
        /// into a single mesh instead — see WallArtMesh.
        /// </summary>
        public static GameObject Instantiate(WallModuleArt art, Transform parent, string name)
        {
            if (art == null || art.Prefab == null) return null;
            var go = Object.Instantiate(art.Prefab, parent);
            go.name = name;
            go.transform.localPosition = art.Normalise.GetColumn(3);
            go.transform.localRotation = art.Normalise.rotation;
            go.transform.localScale = art.Normalise.lossyScale;
            // Authored colliders would fight the sim's own pick box.
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
            return go;
        }

        /// <summary>Drop every cached module — the art changed under us.</summary>
        public static void Invalidate() => _cache.Clear();

        /// <summary>
        /// The part whose length IS the module's length — the wall proper.
        ///
        /// A tiling module is deliberately not a rectangle: the timbers set
        /// the repeat, and the foundation and cloth run LONGER so they lap
        /// into the next copy and hide the joint. Measuring the whole bounding
        /// box therefore measures the overlap as if it were the module, which
        /// spaces copies too far apart and gaps the timbers — 8.7 % of a
        /// module, about 26 cm, on Wall_segment.fbx.
        ///
        /// Found by name (the part or its material saying stake / beam /
        /// plank / timber / palisade / masonry / curtain), falling back to the
        /// TALLEST part, which on any wall is the wall itself rather than its
        /// footing.
        /// </summary>
        static bool IsPitchPart(string nodeName, string materialName)
        {
            if (IsOwnershipPart(nodeName, materialName, Color.white)) return false;
            foreach (string n in new[] { nodeName, materialName })
            {
                if (string.IsNullOrEmpty(n)) continue;
                string t = n.ToLowerInvariant();
                if (t.Contains("stake") || t.Contains("beam") || t.Contains("plank")
                    || t.Contains("timber") || t.Contains("palisade") || t.Contains("masonry")
                    || t.Contains("curtain")) return true;
            }
            return false;
        }

        static WallModuleArt Prepare(GameObject prefab, float fitLength, bool orientAlongZ,
                                     float? explicitScale = null)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            if (filters == null || filters.Length == 0) return null;

            // ── Collect, dropping what a Blender export brings along ──
            var raw = new List<(MeshFilter f, Matrix4x4 local, Bounds world)>();
            var rootInv = prefab.transform.worldToLocalMatrix;
            foreach (var f in filters)
            {
                if (f.sharedMesh == null) continue;
                if (f.GetComponentInParent<Camera>() != null) continue;
                if (f.GetComponentInParent<Light>() != null) continue;
                var local = rootInv * f.transform.localToWorldMatrix;
                var b = f.sharedMesh.bounds;
                raw.Add((f, local, TransformBounds(local, b)));
            }
            if (raw.Count == 0) return null;

            // The default ground plane: a single node whose footprint dwarfs
            // everything else. Measured against the MEDIAN footprint so one
            // oversized node cannot drag the threshold up with it.
            var spans = new List<float>();
            foreach (var r in raw) spans.Add(Mathf.Max(r.world.size.x, r.world.size.z));
            spans.Sort();
            float median = spans[spans.Count / 2];
            float cull = Mathf.Max(median * 6f, 0.01f);

            var kept = new List<(MeshFilter f, Matrix4x4 local, Bounds world)>();
            foreach (var r in raw)
                if (Mathf.Max(r.world.size.x, r.world.size.z) <= cull) kept.Add(r);
            if (kept.Count == 0) kept = raw;

            // ── Measure what is left ──
            Bounds total = kept[0].world;
            for (int i = 1; i < kept.Count; i++) total.Encapsulate(kept[i].world);
            if (total.size.y <= 1e-4f) return null;

            // ── The REPEAT is the defining part's length, not the bounding
            //    box: the footing and the cloth overlap into the next copy on
            //    purpose (see IsPitchPart) ──
            bool havePitch = false;
            Bounds pitchBounds = default;
            foreach (var k in kept)
            {
                var rend = k.f.GetComponent<MeshRenderer>();
                var mat = rend != null ? rend.sharedMaterial : null;
                if (!IsPitchPart(k.f.gameObject.name, mat != null ? mat.name : null)) continue;
                if (!havePitch) { pitchBounds = k.world; havePitch = true; }
                else pitchBounds.Encapsulate(k.world);
            }
            if (!havePitch)
            {
                // Nothing named itself: the tallest part is the wall.
                var tallest = kept[0];
                foreach (var k in kept) if (k.world.size.y > tallest.world.size.y) tallest = k;
                pitchBounds = tallest.world;
            }

            // ── Normalise: base to y = 0, the defining part's long horizontal
            //    axis to the size the sim uses, and (for a run) turned to +Z ──
            bool alongX = pitchBounds.size.x > pitchBounds.size.z;
            float rawLength = math.max(pitchBounds.size.x, pitchBounds.size.z);
            if (rawLength <= 1e-4f) return null;

            float scale = explicitScale ?? (fitLength / rawLength);

            // Centred on the DEFINING part, so copies line up on the timbers
            // and the overlap hangs off both ends evenly.
            var centre = pitchBounds.center;
            var toOrigin = Matrix4x4.Translate(new Vector3(-centre.x, -total.min.y, -centre.z));
            var turn = (orientAlongZ && alongX)
                ? Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f)) : Matrix4x4.identity;
            var fit = Matrix4x4.Scale(Vector3.one * scale);
            var normalise = fit * turn * toOrigin;

            var art = new WallModuleArt
            {
                Length = rawLength * scale,
                Height = total.size.y * scale,
                Scale = scale,
                Prefab = prefab,
                Normalise = normalise,
            };
            foreach (var k in kept)
            {
                var rend = k.f.GetComponent<MeshRenderer>();
                var mats = rend != null ? rend.sharedMaterials : null;
                var mesh = k.f.sharedMesh;
                if (!mesh.isReadable)
                {
                    // Combining reads vertices: an unreadable mesh cannot be
                    // baked, and half a wall is worse than the procedural one.
                    Debug.LogWarning($"[WallModuleArt] '{mesh.name}' is not Read/Write enabled — " +
                                     "wall art ignored, falling back to the procedural wall. " +
                                     "TechTreeModelPostprocessor sets this on import, so seeing " +
                                     "this means the model was imported before it existed: " +
                                     "right-click the FBX and Reimport.");
                    return null;
                }
                var local = normalise * k.local;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    Material mat = mats != null && sm < mats.Length ? mats[sm] : null;
                    if (mat == null) mat = ProceduralMaterialHelper.BaseLit;
                    int slot = art.Materials.IndexOf(mat);
                    if (slot < 0)
                    {
                        slot = art.Materials.Count;
                        art.Materials.Add(mat);
                        if (IsOwnershipPart(k.f.name, mat.name, BaseColorOf(mat)))
                            art.OwnershipSlots.Add(slot);
                    }
                    art.Pieces.Add(new Piece(mesh, sm, slot, local));
                }
            }
            return art;
        }

        /// <summary>
        /// Is this part the module's OWNERSHIP marker — the piece that takes
        /// the player's colour?
        ///
        /// Two ways to say so, and the art only has to use one. By NAME, the
        /// project-wide rule every other building already follows: a part
        /// whose name carries "stripe", "faction", "team", "owner" or
        /// "player" (BuildingFactionColorMarker). Or by COLOUR: a material
        /// authored in the placeholder faction blue, which is how
        /// Wall_segment.fbx marks its posts. Colour is matched in HSV so a
        /// re-export at a different brightness still reads.
        /// </summary>
        public static bool IsOwnershipPart(string nodeName, string materialName, Color color)
        {
            if (NameSaysOwnership(nodeName) || NameSaysOwnership(materialName)) return true;

            // Faction blue, saturated. Not "anything bluish": a grey-blue
            // slate roof must not become the player's colour.
            Color.RGBToHSV(color, out float h, out float sv, out float v);
            var blue = FactionColors.ColorPool[0];
            Color.RGBToHSV(blue, out float bh, out _, out _);
            float dh = Mathf.Abs(Mathf.DeltaAngle(h * 360f, bh * 360f));
            return dh <= 22f && sv >= 0.55f && v >= 0.35f;
        }

        static bool NameSaysOwnership(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            n = n.ToLowerInvariant();
            return n.Contains("stripe") || n.Contains("faction") || n.Contains("team")
                || n.Contains("owner") || n.Contains("player");
        }

        /// <summary>The material's authored base colour, whatever the shader
        /// calls it. "There is a colour node for this in the shader" — this is
        /// where we read it.</summary>
        public static Color BaseColorOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            if (m.HasProperty("_MainColor")) return m.GetColor("_MainColor");
            if (m.HasProperty("_TintColor")) return m.GetColor("_TintColor");
            return Color.white;
        }

        static readonly MaterialPropertyBlock _tintBlock = new MaterialPropertyBlock();

        /// <summary>
        /// Tint every ownership part under <paramref name="root"/> with the
        /// owner's colour — the instantiated and procedural twin of the
        /// per-sub-mesh tint the baked curtain uses.
        ///
        /// Through a MaterialPropertyBlock, NEVER `renderer.materials`. That
        /// property clones a material per renderer, and a finished perimeter
        /// is hundreds of wall pieces: it is the exact batching collapse the
        /// wall's procedural builders were cleaned up to stop causing, which
        /// is why BuildingFactionColorMarker (which does use it) is not the
        /// tool for wall art.
        /// </summary>
        public static void ApplyOwnerColor(GameObject root, Color owner)
        {
            if (root == null) return;
            foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null) continue;
                if (rend is ParticleSystemRenderer || rend is TrailRenderer || rend is LineRenderer) continue;
                var mat = rend.sharedMaterial;
                if (!IsOwnershipPart(rend.gameObject.name, mat != null ? mat.name : null,
                                     BaseColorOf(mat))) continue;

                // Read first: the procedural pieces already carry a block with
                // their colour, metallic and smoothness in it.
                rend.GetPropertyBlock(_tintBlock);
                _tintBlock.SetColor("_BaseColor", owner);
                _tintBlock.SetColor("_Color", owner);
                if (mat != null && mat.HasProperty("_StripeColor"))
                    _tintBlock.SetColor("_StripeColor", owner);
                rend.SetPropertyBlock(_tintBlock);
            }
        }

        static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            var c = m.MultiplyPoint3x4(b.center);
            var e = b.extents;
            var x = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
            var y = m.MultiplyVector(new Vector3(0f, e.y, 0f));
            var z = m.MultiplyVector(new Vector3(0f, 0f, e.z));
            var ext = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(c, ext * 2f);
        }
    }
}
