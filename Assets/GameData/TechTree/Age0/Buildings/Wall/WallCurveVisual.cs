// WallCurveVisual.cs
// Owns a curved segment's swept mesh: builds it from the segment's
// WallCurvePoint buffer, raises it with the cells' construction progress,
// rebuilds it when a cell dies so the breach shows, and re-clads it when the
// faction's wall level changes (docs/Design/Age_1_Alanthor.md § The three
// wall levels). A cell that became a GATE counts as open: the gatehouse is
// one structure three modules wide and draws its own masonry. Polls the sim
// four times a second; nothing per frame.
//
// TWO sources of geometry, one result. When the curtain module's SO has a
// prefab bound, the AUTHORED module is baked along the curve
// by WallArtMesh and the materials come from the art. With no prefab bound,
// WallCurveMesh sweeps the procedural cross-section and the palette below
// colours it. Either way it is one mesh per segment.

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

        // Per-level palette: plinth / body / coping / crown / shields.
        // docs/Design/Age_1_Alanthor.md § The three wall levels.
        static readonly Color TimberDark  = new Color(0.30f, 0.20f, 0.12f);
        static readonly Color Timber      = new Color(0.45f, 0.31f, 0.18f);
        static readonly Color TimberPale  = new Color(0.55f, 0.41f, 0.25f);
        static readonly Color StoneDark   = new Color(0.52f, 0.50f, 0.46f);
        static readonly Color Stone       = new Color(0.78f, 0.76f, 0.72f);
        static readonly Color Marble      = new Color(0.92f, 0.92f, 0.90f);
        static readonly Color Iron        = new Color(0.34f, 0.35f, 0.39f);
        static readonly Color Steel       = new Color(0.62f, 0.65f, 0.70f);

        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        readonly List<float3> _curve = new List<float3>();
        readonly List<Entity> _cells = new List<Entity>();
        readonly List<bool> _alive = new List<bool>();
        readonly List<float> _arcs = new List<float>();   // each cell's arc length along the curve
        int _aliveCount = -1;
        float _nextPoll;
        EntityManager _em;
        bool _valid;
        Matrix4x4 _builtLocalToWorld;   // the root frame the mesh was built for
        float _builtProgress = -1f;
        float _progress = 1f;
        byte _tier = TheWaningBorder.Entities.WallTiers.Palisade;
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
            if (_em.Exists(Segment)) _tier = TheWaningBorder.Entities.WallTiers.Of(_em, Segment);
            ApplyMaterials();
            Rebuild();
        }

        /// <summary>
        /// Point the renderer at the art's materials, or — with no art — at
        /// one shared Lit slot per procedural band, tinted per level.
        /// </summary>
        void ApplyMaterials()
        {
            _art = WallModuleArt.For(_tier);
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
            var mats = new Material[WallCurveMesh.Bands];
            for (int i = 0; i < mats.Length; i++) mats[i] = ProceduralMaterialHelper.BaseLit;
            _renderer.sharedMaterials = mats;
            ApplyPalette();
        }

        /// <summary>Colour the five bands for the current level.</summary>
        void ApplyPalette()
        {
            switch (_tier)
            {
                case TheWaningBorder.Entities.WallTiers.Shielded:
                    Tint(0, StoneDark, 0.30f); Tint(1, Stone, 0.30f);
                    Tint(2, Iron, 0.55f, metallic: 0.8f); Tint(3, Stone, 0.30f);
                    Tint(4, Steel, 0.70f, metallic: 0.9f);   // the great shields
                    break;
                case TheWaningBorder.Entities.WallTiers.Battlemented:
                    Tint(0, StoneDark, 0.30f); Tint(1, Stone, 0.30f);
                    Tint(2, Marble, 0.45f); Tint(3, Stone, 0.30f);
                    Tint(4, Timber, 0.20f);                  // the hoardings
                    break;
                case TheWaningBorder.Entities.WallTiers.Stone:
                    Tint(0, StoneDark, 0.30f); Tint(1, Stone, 0.30f);
                    Tint(2, Marble, 0.45f); Tint(3, Stone, 0.30f); Tint(4, Stone, 0.30f);
                    break;
                default:
                    Tint(0, TimberDark, 0.20f); Tint(1, Timber, 0.20f);
                    Tint(2, TimberDark, 0.20f); Tint(3, TimberPale, 0.20f); Tint(4, Timber, 0.20f);
                    break;
            }
        }

        void Tint(int slot, Color c, float smoothness, float metallic = 0f)
        {
            _mpb.Clear();
            _mpb.SetColor("_BaseColor", c); _mpb.SetColor("_Color", c);
            _mpb.SetFloat("_Metallic", metallic); _mpb.SetFloat("_Smoothness", smoothness);
            _renderer.SetPropertyBlock(_mpb, slot);
        }

        void LateUpdate()
        {
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

            _tier = TheWaningBorder.Entities.WallTiers.Of(_em, Segment);
            if (_tier != _builtTier || (_art != null && OwnerColor() != _tintedFor)) ApplyMaterials();

            // The root is placed by SyncTransforms after we first build; the
            // mesh lives in the root's frame, so a moved root means a rebuild.
            bool moved = transform.localToWorldMatrix != _builtLocalToWorld;
            if (alive != _aliveCount || moved || _tier != _builtTier
                || Mathf.Abs(progress - _builtProgress) > 0.03f) Rebuild();
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
            _cells.Clear(); _alive.Clear(); _arcs.Clear();
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
                    // A GATE cell is not curtain: the gatehouse is its own
                    // structure three modules wide, and its two neighbours
                    // were destroyed to make room. Leaving the span open is
                    // what stops a wall being drawn across the gateway.
                    bool solid = _em.Exists(cell) && !_em.HasComponent<WallGateTag>(cell);
                    _alive.Add(solid);
                    _arcs.Add(cum != null ? TheWaningBorder.Entities.AlanthorWall.ArcLengthAlong(_curve, cum, buf[i].Position) : 0f);
                }
            }
            _aliveCount = 0;
            foreach (var a in _alive) if (a) _aliveCount++;

            _builtLocalToWorld = transform.localToWorldMatrix;
            _builtProgress = _progress;
            _builtTier = _tier;

            // The authored module wins whenever it is bound; the swept
            // cross-section is what a wall looks like until then.
            if (_art == null) _art = WallModuleArt.For(_tier);
            if (_art != null && !_builtFromArt) { ApplyMaterials(); _builtFromArt = true; }

            _mesh = _art != null
                ? WallArtMesh.Build(_art, _curve, TheWaningBorder.Entities.AlanthorWall.HubInsetMetres,
                                    _arcs, i => i < _alive.Count && _alive[i],
                                    _progress, _mesh, transform.worldToLocalMatrix)
                : WallCurveMesh.Build(_curve, TheWaningBorder.Entities.AlanthorWall.HubInsetMetres,
                                      _arcs, i => i < _alive.Count && _alive[i],
                                      _progress, _mesh, transform.worldToLocalMatrix, _tier);
            _filter.sharedMesh = _mesh;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
