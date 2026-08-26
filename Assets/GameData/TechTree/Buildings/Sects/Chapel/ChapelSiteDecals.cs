// BFME2-style build-plot decals — six ground markers in a hex ring around
// the player's completed Temple of Ridan. Clicking one selects the chapel
// slot; the old sect pickers were removed with the old UI (2026-07-17) and
// the final uGUI will own the sect picker flow.
//
// Each decal is a flat quad lying on the ground with a procedurally
// generated texture: a golden ring with a "+" glyph inside on a semi-
// transparent navy fill. No external art assets required.
//
// Decals hide while their slot is occupied (building or built) and
// reappear if the slot is freed. The whole rig also hides while the
// Temple is under construction or low-HP, mirroring the religion HUD gate.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Manages the six ground decals around the player's Temple of Ridan.
    /// One instance per scene — added by GameBootstrap on the runtime
    /// managers GO. Tracks the local faction's temple; rebuilds decals on
    /// temple change; per-frame, hides any decal whose slot is filled.
    /// </summary>
    public sealed class ChapelSiteDecals : MonoBehaviour
    {
        public const float RingRadiusMeters = TempleChapelRing.SlotRadius; // shared docked ring
        public const float DecalSize        = 2.4f;     // edge length of the square decal (chapel footprint + margin)
        public const float DecalHeight      = 0.4f;     // raise above ground to avoid z-fight + terrain bumps

        EntityWorld _world;
        EntityManager _em;
        Entity _temple;
        Vector3 _templePos;
        readonly GameObject[] _decals = new GameObject[SectConfig.MaxAdoptedSects];
        readonly ChapelSiteTag[] _tags = new ChapelSiteTag[SectConfig.MaxAdoptedSects];

        Material _matIdle;
        Material _matHover;
        Texture2D _decalTex;
        bool _builtAssets;

        Camera _camCache;
        int _hoveredIdx = -1;

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        static readonly ComponentType[] TempleQueryTypes = {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>() };
        TheWaningBorder.Core.CachedEntityQuery _templeQuery;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;
        }

        void OnDestroy()
        {
            ClearDecals();
            if (_matIdle    != null) Destroy(_matIdle);
            if (_matHover   != null) Destroy(_matHover);
            if (_decalTex   != null) Destroy(_decalTex);
        }

        // ─────────────────────────────────────────────────────────────────
        // FRAME LOOP
        // ─────────────────────────────────────────────────────────────────

        void Update()
        {
            if (_em.Equals(default(EntityManager)))
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
            }
            if (!_builtAssets) BuildAssets();

            Entity newTemple = FindLocalTemple();
            if (newTemple != _temple)
            {
                _temple = newTemple;
                if (_temple == Entity.Null)
                {
                    ClearDecals();
                }
                else
                {
                    _templePos = ResolveTemplePosition(_temple);
                    BuildDecals(_templePos);
                }
            }

            if (_temple == Entity.Null) return;

            UpdateSlotVisibility();
            UpdateHover();
            HandleClick();
        }

        // ─────────────────────────────────────────────────────────────────
        // TEMPLE LOOKUP — completed temple only.
        // ─────────────────────────────────────────────────────────────────

        Entity FindLocalTemple()
        {
            var faction = GameSettings.LocalPlayerFaction;
            var q = _templeQuery.Get(_em, TempleQueryTypes);
            using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (_em.GetComponentData<FactionTag>(entities[i]).Value != faction) continue;
                if (_em.HasComponent<UnderConstruction>(entities[i])) continue;
                // CommandRouter.PlaceBuildingDirect creates the entity with
                // Health.Value = 1 BEFORE adding UnderConstruction, so there's
                // a single-frame race where the temple looks "complete". The
                // health check closes it.
                if (_em.HasComponent<Health>(entities[i]))
                {
                    var hp = _em.GetComponentData<Health>(entities[i]);
                    if (hp.Max <= 0 || hp.Value * 5 < hp.Max * 4) continue;
                }
                return entities[i];
            }
            return Entity.Null;
        }

        Vector3 ResolveTemplePosition(Entity temple)
        {
            if (!_em.HasComponent<LocalTransform>(temple)) return Vector3.zero;
            float3 p = _em.GetComponentData<LocalTransform>(temple).Position;
            return new Vector3(p.x, p.y, p.z);
        }

        // ─────────────────────────────────────────────────────────────────
        // BUILD / TEARDOWN
        // ─────────────────────────────────────────────────────────────────

        void BuildDecals(Vector3 templeCentre)
        {
            ClearDecals();
            TWBLog.Log($"[ChapelSiteDecals] Building {_decals.Length} decals around temple at {templeCentre} " +
                      $"(ring radius {RingRadiusMeters}m, decal size {DecalSize}m, elevation +{DecalHeight}m)");
            for (int i = 0; i < _decals.Length; i++)
            {
                // Decal i marks chapel slot i EXACTLY where the chapel will
                // rise: TempleChapelRing puts slot i on face i+1 of the
                // 7-sided temple (face 0 is the door), shared with
                // TempleChapelBuildSystem's spawn offset.
                var offset = TempleChapelRing.WorldOffset(i);
                float wx = templeCentre.x + offset.x;
                float wz = templeCentre.z + offset.z;
                float wy = TerrainUtility.GetHeight(wx, wz) + DecalHeight;

                // Use Unity's built-in Quad primitive. It ships with a correctly
                // wound 1×1 mesh, a MeshCollider, and a default material we
                // immediately overwrite. The hand-rolled mesh I tried first
                // was invisible — most likely a face-culling mismatch — so
                // this is the robust path.
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"ChapelDecal_{i}";

                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _matIdle;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // Default Quad faces -Z (toward the +Z viewer). Rotate +90° on
                // X so the front face points up; viewed from a top-down RTS
                // camera, we see the texture, not the culled back face.
                go.transform.position = new Vector3(wx, wy, wz);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(DecalSize, DecalSize, 1f);

                // Replace the default MeshCollider (which becomes paper-thin on
                // the now-horizontal Quad) with a BoxCollider that has a small
                // vertical extent so the cursor raycast still hits reliably.
                var oldCol = go.GetComponent<Collider>();
                if (oldCol != null) Destroy(oldCol);
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(1f, 1f, 0.4f);

                var tag = go.AddComponent<ChapelSiteTag>();
                tag.SlotIndex = i;
                tag.Temple = _temple;
                tag.Renderer = mr;

                _decals[i] = go;
                _tags[i] = tag;
            }
        }

        void ClearDecals()
        {
            for (int i = 0; i < _decals.Length; i++)
            {
                if (_decals[i] != null) Destroy(_decals[i]);
                _decals[i] = null;
                _tags[i] = null;
            }
            _hoveredIdx = -1;
        }

        // ─────────────────────────────────────────────────────────────────
        // PER-FRAME UPDATES
        // ─────────────────────────────────────────────────────────────────

        void UpdateSlotVisibility()
        {
            if (!_em.HasBuffer<TempleChapelSlot>(_temple)) return;
            var slots = _em.GetBuffer<TempleChapelSlot>(_temple);
            int n = math.min(_decals.Length, slots.Length);
            for (int i = 0; i < n; i++)
            {
                if (_decals[i] == null) continue;
                bool free = slots[i].State == 0;
                if (_decals[i].activeSelf != free) _decals[i].SetActive(free);
            }
        }

        void UpdateHover()
        {
            // Old-UI panel/modal guards replaced by the uGUI EventSystem
            // pointer check (2026-07-17 UI removal).
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                SetHovered(-1);
                return;
            }

            if (_camCache == null) _camCache = Camera.main;
            if (_camCache == null) { SetHovered(-1); return; }

            int idx = -1;
            Ray ray = _camCache.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var tag = hit.collider.GetComponent<ChapelSiteTag>();
                if (tag != null) idx = tag.SlotIndex;
            }
            SetHovered(idx);
        }

        void SetHovered(int idx)
        {
            if (idx == _hoveredIdx) return;
            // Restore previous
            if (_hoveredIdx >= 0 && _hoveredIdx < _tags.Length && _tags[_hoveredIdx] != null
                && _tags[_hoveredIdx].Renderer != null)
            {
                _tags[_hoveredIdx].Renderer.sharedMaterial = _matIdle;
            }
            _hoveredIdx = idx;
            if (_hoveredIdx >= 0 && _hoveredIdx < _tags.Length && _tags[_hoveredIdx] != null
                && _tags[_hoveredIdx].Renderer != null)
            {
                _tags[_hoveredIdx].Renderer.sharedMaterial = _matHover;
            }
        }

        void HandleClick()
        {
            if (!UnityEngine.Input.GetMouseButtonDown(0)) return;
            // Old-UI panel/modal guards replaced by the uGUI EventSystem
            // pointer check (2026-07-17 UI removal).
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            if (_camCache == null) _camCache = Camera.main;
            if (_camCache == null) return;

            Ray ray = _camCache.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;

            var tag = hit.collider.GetComponent<ChapelSiteTag>();
            if (tag == null) return;
            if (tag.Temple == Entity.Null || !_em.Exists(tag.Temple)) return;

            // Old sect pickers (SectPickerModal / SectChoicePopup) removed with
            // the old UI (2026-07-17); the final uGUI will own the sect picker.
            // Until then, log the chapel-site click so the flow stays traceable.
            TWBLog.Log($"[ChapelSiteDecals] Chapel site clicked (temple={tag.Temple.Index}, slot={tag.SlotIndex}) — sect picker pending final UI");
        }

        // ─────────────────────────────────────────────────────────────────
        // ASSET CONSTRUCTION — quad mesh, decal texture, idle + hover materials.
        // Built once per session, shared across all six decals.
        // ─────────────────────────────────────────────────────────────────

        void BuildAssets()
        {
            _decalTex = BuildDecalTexture();

            // Sprites/Default is the safest bet: it's a forward-compatible
            // legacy shader that handles transparent textured quads correctly
            // in both built-in and URP. URP's Unlit Transparent variant needs
            // explicit _Surface/_Blend property setup that often silently
            // fails on Material(shader) constructed at runtime — which is
            // probably why the decals were invisible before.
            Shader shader = Shader.Find("Sprites/Default")
                          ?? Shader.Find("Unlit/Transparent")
                          ?? Shader.Find("Universal Render Pipeline/Unlit");

            _matIdle = BuildDecalMaterial(shader, _decalTex, 0.85f);
            _matHover = BuildDecalMaterial(shader, _decalTex, 1.0f);

            _builtAssets = true;
        }

        // Build a material that reliably shows a transparent textured quad in
        // both Built-In and URP. Sets _MainTex, _BaseMap, and _BaseColor so any
        // of the common shader property names binds the texture.
        static Material BuildDecalMaterial(Shader shader, Texture2D tex, float alpha)
        {
            var mat = new Material(shader)
            {
                renderQueue = 3000,
                mainTexture = tex,
            };
            var tint = new Color(1f, 1f, 1f, alpha);
            mat.color = tint;
            if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", tint);

            // URP unlit transparent setup — only applied when the properties
            // exist on the shader. Sprites/Default already does transparency
            // correctly without these and the property checks no-op.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_Mode"))    mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))   mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }

        // Procedural BFME2-style plot decal:
        //   • outer gold ring
        //   • inner ring (thinner, dimmer)
        //   • semi-transparent navy fill
        //   • golden "+" glyph in the middle
        // 128×128 is plenty — the decal is ~3m on screen and texture pixels
        // beyond ~128 are imperceptible at typical RTS camera distances.
        static Texture2D BuildDecalTexture()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: true, linear: false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color gold       = new Color(0.95f, 0.78f, 0.36f, 1f);
            Color goldDim    = new Color(0.65f, 0.50f, 0.22f, 1f);
            Color navy       = new Color(0.06f, 0.08f, 0.18f, 0.55f);
            Color clear      = new Color(0f, 0f, 0f, 0f);

            float cx = (N - 1) * 0.5f;
            float cy = (N - 1) * 0.5f;
            float outerR = N * 0.46f;
            float ringInner = N * 0.41f;
            float innerR = N * 0.36f;
            float innerRingInner = N * 0.33f;

            float plusHalfThick = N * 0.04f;
            float plusHalfLen   = N * 0.18f;

            var pixels = new Color[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    Color c = clear;
                    if (r <= outerR && r >= ringInner)        c = gold;        // outer gold ring
                    else if (r <= innerR && r >= innerRingInner) c = goldDim;  // inner dim ring
                    else if (r < innerRingInner)               c = navy;       // navy fill

                    // Plus glyph — overrides the navy fill where it lands.
                    bool inHorzBar = Mathf.Abs(dy) <= plusHalfThick && Mathf.Abs(dx) <= plusHalfLen;
                    bool inVertBar = Mathf.Abs(dx) <= plusHalfThick && Mathf.Abs(dy) <= plusHalfLen;
                    if (inHorzBar || inVertBar) c = gold;

                    pixels[y * N + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return tex;
        }
    }

    /// <summary>
    /// Marker on each decal GameObject. Carries the slot index, the temple
    /// entity it represents, and the renderer so the manager can swap its
    /// material on hover without searching children every frame.
    /// </summary>
    public sealed class ChapelSiteTag : MonoBehaviour
    {
        public int SlotIndex;
        public Entity Temple;
        public MeshRenderer Renderer;
    }
}
