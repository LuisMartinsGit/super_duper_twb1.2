// WallCurveVisual.cs
// Owns a curved segment's swept mesh: builds it from the segment's
// WallCurvePoint buffer, raises it with the cells' construction progress,
// rebuilds it when a cell dies so the breach shows, and re-clads it when the
// faction's wall level changes (docs/Design/Age_1_Alanthor.md § The three
// wall levels). A cell that became a GATE counts as open: the gatehouse is
// one structure three modules wide and draws its own masonry. Polls the sim
// four times a second; nothing per frame.
//
// The AUTHORED curtain module for the level (WallModuleArt.asset, the only
// source of wall art — docs/Design/Art_Direction.md § 13) is baked along the
// curve by WallArtMesh, one mesh per segment, with the art's materials. An
// empty slot draws nothing and is reported as missing art.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class WallCurveVisual : MonoBehaviour
    {
        public Entity Segment;

        const float PollSeconds = 0.25f;
        /// <summary>Construction progress a rebuild waits for. Every 3 % was
        /// ~33 full mesh rebuilds per segment per build; the rise reads as
        /// continuous at 10 %, and completion always rebuilds.</summary>
        const float ProgressStep = 0.10f;
        /// <summary>Segment mesh rebuilds allowed per frame across ALL walls.
        /// A faction-wide level-up re-clads every segment at once; spread
        /// over frames it is a ripple instead of one long hitch.</summary>
        const int MaxRebuildsPerFrame = 12;
        static int _budgetFrame = -1;
        static int _rebuildsThisFrame;

        static bool TakeRebuildBudget()
        {
            if (_budgetFrame != Time.frameCount) { _budgetFrame = Time.frameCount; _rebuildsThisFrame = 0; }
            if (_rebuildsThisFrame >= MaxRebuildsPerFrame) return false;
            _rebuildsThisFrame++;
            return true;
        }
        bool _rebuildPending;

        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        readonly List<float3> _curve = new List<float3>();
        readonly List<Entity> _cells = new List<Entity>();
        /// <summary>Per cell, how much of the curve it leaves open
        /// (WallOpenings): &lt; 0 solid curtain, 0 a breach, &gt; 0 the
        /// half-length of a piece that draws its own masonry.</summary>
        readonly List<float> _openHalf = new List<float>();
        readonly List<float> _arcs = new List<float>();   // each cell's arc length along the curve
        /// <summary>Cells that EXIST, as of the last rebuild — the poll
        /// compares the same count. It used to compare it with the SOLID
        /// count, which a gate never is, so every segment holding a gate,
        /// tower or emplacement rebuilt four times a second forever and ate
        /// the shared rebuild budget other walls were waiting on.</summary>
        int _existCount = -1;
        float _nextPoll;
        EntityManager _em;
        bool _valid;
        Matrix4x4 _builtLocalToWorld;   // the root frame the mesh was built for
        float _builtProgress = -1f;
        float _progress = 1f;
        // The level the ART is picked at (WallTiers.ArtLevelOf — a Feraldis
        // palisade raised to its Wooden Wall level reads 1 here while its sim
        // level stays 0) and the owner culture whose wall set it may draw.
        byte _tier = TheWaningBorder.Entities.WallTiers.Palisade;
        byte _culture = Cultures.None;
        byte _builtTier;
        MaterialPropertyBlock _mpb;
        WallModuleArt _art;
        bool _builtFromArt;
        Color _tintedFor = Color.clear;

        /// <summary>The colour this segment's owner plays in.</summary>
        Color OwnerColor()
        {
            if (Segment == Entity.Null || !_em.Exists(Segment)
                || !_em.HasComponent<FactionTag>(Segment)) return Color.white;
            return FactionColors.Get(_em.GetComponentData<FactionTag>(Segment).Value);
        }

        public void Init(EntityManager em)
        {
            _em = em; _valid = true;
            _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _mpb = new MaterialPropertyBlock();
            if (_em.Exists(Segment)) ReadArtLevel();
            // Start at the cells' real construction progress. _progress
            // defaults to 1, so a freshly laid wall used to be built FULL
            // height on its first frame and only sank to its foundation at
            // the first poll -- a finished-wall flash on every placement.
            _progress = ReadProgress();
            ApplyMaterials();
            // Stagger the poll so a wall placed in one go does not poll every
            // segment on the same frame forever after.
            _nextPoll = Time.time + UnityEngine.Random.value * PollSeconds;
            if (TakeRebuildBudget()) Rebuild();
            else _rebuildPending = true;
        }

        /// <summary>
        /// Point the renderer at the art's materials.
        /// </summary>
        void ReadArtLevel()
        {
            _tier = TheWaningBorder.Entities.WallTiers.ArtLevelOf(_em, Segment);
            _culture = TheWaningBorder.Entities.WallTiers.ArtCultureOf(_em, Segment);
        }

        void ApplyMaterials()
        {
            _art = WallModuleArt.For(_tier, _culture);
            if (_art != null)
            {
                _renderer.sharedMaterials = _art.Materials.ToArray();
                // The art carries its own look, EXCEPT the ownership posts:
                // those take the owner's colour, per renderer, so one shared
                // material serves every faction's walls and the batching the
                // single-mesh wall exists for survives.
                Color owner = OwnerColor();
                for (int i = 0; i < _art.Materials.Count; i++)
                {
                    if (!_art.OwnershipSlots.Contains(i)) { _renderer.SetPropertyBlock(null, i); continue; }
                    _mpb.Clear();
                    _mpb.SetColor("_BaseColor", owner);
                    _mpb.SetColor("_Color", owner);
                    if (_art.Materials[i] != null && _art.Materials[i].HasProperty("_StripeColor"))
                        _mpb.SetColor("_StripeColor", owner);
                    _renderer.SetPropertyBlock(_mpb, i);
                }
                _tintedFor = owner;
                return;
            }
            ReportMissing();
            _renderer.sharedMaterials = System.Array.Empty<Material>();
        }

        static readonly HashSet<(byte, byte)> _reported = new HashSet<(byte, byte)>();
        void ReportMissing()
        {
            if (_reported.Add((_tier, _culture)))
                Debug.LogError($"[Wall] no curtain art at wall level {_tier} (culture {_culture}) - fill its " +
                               "slot in WallModuleArt.asset (Waning Border > Art > Bind Building Art).");
        }

        /// <summary>The segment's construction progress, read straight from
        /// its cell buffer (the poll's rule: the first cell still under
        /// construction speaks for the run; none means finished).</summary>
        float ReadProgress()
        {
            if (Segment == Entity.Null || !_em.Exists(Segment)
                || !_em.HasBuffer<WallInstanceRef>(Segment)) return 1f;
            var buf = _em.GetBuffer<WallInstanceRef>(Segment);
            for (int i = 0; i < buf.Length; i++)
            {
                var c = buf[i].Instance;
                if (!_em.Exists(c) || !_em.HasComponent<UnderConstruction>(c)) continue;
                var uc = _em.GetComponentData<UnderConstruction>(c);
                return uc.Progress / math.max(0.01f, uc.Total);
            }
            return 1f;
        }

        void LateUpdate()
        {
            // A moved root is checked EVERY frame, not at the poll, and
            // rebuilt at once, outside the budget. The mesh is baked in the
            // root's frame, so between a move and its rebuild the whole
            // curtain is drawn displaced (and, through the building +180,
            // mirrored). LateUpdate runs after SyncTransforms moved the root,
            // so rebuilding here means the stale pose never reaches the
            // screen. Roots only move on spawn and when a segment is
            // re-aimed (a split for a branch hub), so this is rare.
            if (_valid && _builtProgress >= 0f
                && transform.localToWorldMatrix != _builtLocalToWorld)
            {
                var mw = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (mw != null && mw.IsCreated)
                {
                    if (_em != mw.EntityManager) _em = mw.EntityManager;
                    if (Segment != Entity.Null && _em.Exists(Segment))
                    {
                        _rebuildPending = false;
                        Rebuild();
                    }
                }
            }

            if (_valid && _rebuildPending && TakeRebuildBudget())
            {
                _rebuildPending = false;
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w != null && w.IsCreated)
                {
                    if (_em != w.EntityManager) _em = w.EntityManager;
                    if (Segment != Entity.Null && _em.Exists(Segment)) Rebuild();
                }
            }
            if (!_valid || Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollSeconds;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (_em != world.EntityManager) _em = world.EntityManager;
            if (Segment == Entity.Null || !_em.Exists(Segment)) return;

            // Construction: the wall rises with its cells (baked into the
            // mesh — SyncTransforms owns the root's scale).
            float progress = 1f; int counted = 0;
            int alive = 0;
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                bool exists = _em.Exists(c);
                if (exists) alive++;
                if (exists && _em.HasComponent<UnderConstruction>(c))
                {
                    var uc = _em.GetComponentData<UnderConstruction>(c);
                    progress = counted == 0 ? uc.Progress / math.max(0.01f, uc.Total) : progress;
                    counted++;
                }
            }
            if (counted == 0) progress = 1f;
            _progress = progress;

            ReadArtLevel();
            // A FINISHED wall changing level re-clads with the level-up wave
            // every other building gets: the current look is snapshotted
            // before the materials change, and the wave runs once the new
            // mesh is built (end of Rebuild — possibly a later frame).
            if (_tier != _builtTier && _builtProgress >= 1f && _waveOld == null)
                _waveOld = SnapshotForWave();
            if (_tier != _builtTier || (_art != null && OwnerColor() != _tintedFor)) ApplyMaterials();

            // The root is placed by SyncTransforms after we first build; the
            // mesh lives in the root's frame, so a moved root means a rebuild.
            bool moved = transform.localToWorldMatrix != _builtLocalToWorld;
            bool finished = progress >= 1f && _builtProgress < 1f;
            if (alive != _existCount || moved || _tier != _builtTier || finished
                || Mathf.Abs(progress - _builtProgress) >= ProgressStep)
            {
                if (TakeRebuildBudget()) { _rebuildPending = false; Rebuild(); }
                else _rebuildPending = true;   // picked up on a later frame
            }
        }

        void Rebuild()
        {
            if (!_em.Exists(Segment)) return;
            _curve.Clear();
            if (_em.HasBuffer<WallCurvePoint>(Segment))
            {
                var buf = _em.GetBuffer<WallCurvePoint>(Segment);
                for (int i = 0; i < buf.Length; i++) _curve.Add(buf[i].Position);
            }
            _cells.Clear(); _openHalf.Clear(); _arcs.Clear();
            int exist = 0;
            if (_em.HasBuffer<WallInstanceRef>(Segment))
            {
                // A cell's place on the curve comes from its STORED position:
                // a dead cell has no transform to read, and a split segment's
                // cells keep their spots while the curve under them changes.
                var cum = _curve.Count >= 2 ? TheWaningBorder.Entities.AlanthorWall.ArcTable(_curve) : null;
                var buf = _em.GetBuffer<WallInstanceRef>(Segment);
                for (int i = 0; i < buf.Length; i++)
                {
                    var cell = buf[i].Instance;
                    _cells.Add(cell);
                    if (_em.Exists(cell)) exist++;
                    _openHalf.Add(OpenHalfOf(cell));
                    _arcs.Add(cum != null ? TheWaningBorder.Entities.AlanthorWall.ArcLengthAlong(_curve, cum, buf[i].Position) : 0f);
                }
            }
            _existCount = exist;

            _builtLocalToWorld = transform.localToWorldMatrix;
            _builtProgress = _progress;
            _builtTier = _tier;

            if (_art == null) _art = WallModuleArt.For(_tier, _culture);
            if (_art != null && !_builtFromArt) { ApplyMaterials(); _builtFromArt = true; }

            if (_art == null) { ReportMissing(); _filter.sharedMesh = null; return; }
            _mesh = WallArtMesh.Build(_art, _curve, TheWaningBorder.Entities.AlanthorWall.HubInsetMetres,
                                      _arcs, i => i < _openHalf.Count ? _openHalf[i] : -1f,
                                      _progress, _mesh, transform.worldToLocalMatrix);
            _filter.sharedMesh = _mesh;

            if (_waveOld != null)
            {
                BuildingDissolveTransition.Begin(_waveOld, gameObject, 1.5f, OwnerColor());
                _waveOld = null;
            }
        }

        /// <summary>The old look waiting for its level-up wave (null when none).</summary>
        GameObject _waveOld;

        /// <summary>
        /// A stand-alone copy of the curtain as it is drawn now — mesh,
        /// materials and the per-slot colours — for the wave to dissolve out.
        /// The copy owns its mesh (<see cref="OwnedMesh"/>), so it leaks
        /// nothing when the wave destroys it.
        /// </summary>
        GameObject SnapshotForWave()
        {
            if (_mesh == null || _mesh.vertexCount == 0 || _renderer == null) return null;
            var go = new GameObject(name + "_LevelUpOld");
            go.transform.SetPositionAndRotation(transform.position, transform.rotation);
            go.transform.localScale = transform.lossyScale;
            var copy = Instantiate(_mesh);
            go.AddComponent<MeshFilter>().sharedMesh = copy;
            go.AddComponent<OwnedMesh>().Mesh = copy;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = _renderer.shadowCastingMode;
            var mats = _renderer.sharedMaterials;
            mr.sharedMaterials = mats;
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < mats.Length; i++)
            {
                _renderer.GetPropertyBlock(block, i);
                mr.SetPropertyBlock(block, i);
            }
            return go;
        }

        /// <summary>Destroys a snapshot's private mesh with it.</summary>
        sealed class OwnedMesh : MonoBehaviour
        {
            public Mesh Mesh;
            void OnDestroy() { if (Mesh != null) Destroy(Mesh); }
        }

        /// <summary>
        /// How much of the curve <paramref name="cell"/> leaves open. A dead
        /// cell opens its own span (the breach). A piece that draws its own
        /// masonry opens EXACTLY what it draws, so the curtain meets it edge
        /// to edge and never runs through it: the gatehouse its
        /// WallGateSpan (as BuildWallGatehouse sizes it - its two flanking
        /// modules were destroyed to make room), a tower or an emplacement
        /// one module (both are fitted to one module's length). A legacy
        /// gate-group flank draws one module too.
        /// </summary>
        float OpenHalfOf(Entity cell)
        {
            const float module = TheWaningBorder.Entities.AlanthorWall.InstanceSpacing;
            if (!_em.Exists(cell)) return 0f;
            if (_em.HasComponent<WallGateTag>(cell))
            {
                bool leader = !_em.HasComponent<WallGateGroup>(cell)
                    || _em.GetComponentData<WallGateGroup>(cell).Leader == cell;
                float span = leader && _em.HasComponent<WallGateSpan>(cell)
                    ? math.max(module, _em.GetComponentData<WallGateSpan>(cell).Metres)
                    : module;
                return span * 0.5f;
            }
            if (_em.HasComponent<WallTowerTag>(cell) || _em.HasComponent<EmplacementTag>(cell))
                return module * 0.5f;
            return -1f;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_waveOld != null) Destroy(_waveOld);
        }
    }
}
