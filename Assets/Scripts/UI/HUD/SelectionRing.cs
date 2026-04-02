// File: Assets/Scripts/UI/HUD/SelectionRings.cs
// Selection and hover ring visualization for selected entities.
// Uses URP DecalProjector for terrain-projected indicators:
// - Buildings: square border decals sized to BuildingSize
// - Units: circle border decals sized to Radius
// Location: Assets/Scripts/UI/HUD/SelectionRing.cs

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Systems.Visibility;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.Input;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Manages selection indicators for selected and hovered entities.
    /// Uses URP DecalProjector so indicators project onto terrain (no hill clipping).
    /// Buildings get square borders; units get circle borders.
    /// FoW-aware: enemy hover rings only shown when entity is visible.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class SelectionRings : MonoBehaviour
    {
        [Header("Ring Geometry")]
        public float MinRadius = 0.45f;
        public float BuildingRadius = 0.6f;
        public float ProjectionDepth = 10f;
        public float DecalPadding = 0.3f;

        [Header("Colors (alpha is taken from these)")]
        [Tooltip("Alpha used for selection rings. RGB comes from FactionColors.")]
        public Color SelectedColor = new Color(0.2f, 0.75f, 1f, 0.75f);
        [Tooltip("Alpha used for hover rings. RGB comes from FactionColors.")]
        public Color HoverEnemyColor = new Color(1f, 0.25f, 0.25f, 0.85f);

        private EntityWorld _world;
        private EntityManager _em;

        private readonly Dictionary<Entity, GameObject> _rings = new();
        private GameObject _hoverRing;
        private Entity _hoverFor = Entity.Null;
        private bool _hoverIsBuilding;

        // Legacy fallback material (used if decal shader not found)
        private Material _ringMat;
        private bool _useDecals = true;
        private FogOfWarManager _fow;
        private Faction _humanFaction = GameSettings.LocalPlayerFaction;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;

            // Check if decal materials are available
            var testMat = DecalHelper.GetCircleSelectionMaterial();
            if (testMat == null)
            {
                Debug.LogWarning("[SelectionRings] Decal shader not found, falling back to legacy cylinders");
                _useDecals = false;
                _ringMat = MakeLegacyRingMaterial();
            }

            _fow = FindObjectOfType<FogOfWarManager>();
            if (_fow != null) _humanFaction = _fow.HumanFaction;
        }

        void LateUpdate()
        {
            if (_em.Equals(default(EntityManager)))
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world != null && _world.IsCreated) _em = _world.EntityManager;
            }
            if (_world == null || !_world.IsCreated) return;

            if (_fow == null) _fow = FindObjectOfType<FogOfWarManager>();
            if (_fow != null) _humanFaction = _fow.HumanFaction;

            UpdateSelectionRings();
            UpdateHoverRing();
        }

        void OnDestroy()
        {
            foreach (var kv in _rings) if (kv.Value) Destroy(kv.Value);
            _rings.Clear();
            ClearHoverRing();
            if (_ringMat != null) Destroy(_ringMat);
        }

        // =====================================================================
        // SELECTION RING UPDATE
        // =====================================================================

        private void UpdateSelectionRings()
        {
            var want = RTSInput.CurrentSelection ?? new List<Entity>();
            var still = new HashSet<Entity>();

            for (int i = 0; i < want.Count; i++)
            {
                var e = want[i];
                if (!_em.Exists(e)) continue;

                // Skip invisible battalion leaders — no visual indicator
                if (_em.HasComponent<BattalionLeader>(e)) continue;

                bool isBuilding = _em.HasComponent<BuildingTag>(e);

                if (!_rings.TryGetValue(e, out var go) || go == null)
                {
                    var selCol = GetFactionTint(e, SelectedColor.a);
                    go = _useDecals ? NewDecalRing(selCol, isBuilding) : NewLegacyRing(selCol);
                    _rings[e] = go;
                }

                UpdateRingTransform(go, e, isBuilding);
                var wantCol = GetFactionTint(e, SelectedColor.a);
                UpdateRingColor(go, wantCol);

                still.Add(e);
            }

            // Cleanup removed
            var toRemove = new List<Entity>();
            foreach (var kv in _rings)
            {
                var e = kv.Key;
                if (!still.Contains(e) || !_em.Exists(e))
                {
                    if (kv.Value != null) Destroy(kv.Value);
                    toRemove.Add(e);
                }
            }
            foreach (var e in toRemove) _rings.Remove(e);
        }

        // =====================================================================
        // HOVER RING UPDATE
        // =====================================================================

        private void UpdateHoverRing()
        {
            var h = RTSInput.HoveredEntity;
            bool showHover = false;

            if (h != Entity.Null && _em.Exists(h) && _em.HasComponent<FactionTag>(h))
            {
                var fac = _em.GetComponentData<FactionTag>(h).Value;
                bool mine = fac == _humanFaction;

                if (mine)
                {
                    showHover = true;
                }
                else
                {
                    if (_em.HasComponent<LocalTransform>(h))
                    {
                        var pos = _em.GetComponentData<LocalTransform>(h).Position;
                        bool visible = FogOfWarSystem.IsVisibleToFaction(_humanFaction, pos);
                        showHover = visible;
                    }
                }
            }

            if (showHover)
            {
                bool isBuilding = _em.HasComponent<BuildingTag>(h);

                if (_hoverRing == null || _hoverFor != h)
                {
                    ClearHoverRing();
                    var col = GetFactionTint(h, HoverEnemyColor.a);
                    _hoverRing = _useDecals ? NewDecalRing(col, isBuilding) : NewLegacyRing(col);
                    _hoverFor = h;
                    _hoverIsBuilding = isBuilding;
                }

                UpdateRingTransform(_hoverRing, h, _hoverIsBuilding);
                UpdateRingColor(_hoverRing, GetFactionTint(h, HoverEnemyColor.a));
            }
            else
            {
                ClearHoverRing();
            }
        }

        // =====================================================================
        // DECAL RING CREATION
        // =====================================================================

        private GameObject NewDecalRing(Color color, bool isBuilding)
        {
            var go = new GameObject(isBuilding ? "SelectionDecalSquare" : "SelectionDecalCircle");

            var decal = go.AddComponent<DecalProjector>();

            // Clone base material so each ring can have its own color
            var baseMat = isBuilding
                ? DecalHelper.GetSquareSelectionMaterial()
                : DecalHelper.GetCircleSelectionMaterial();

            decal.material = new Material(baseMat);
            SetDecalColor(decal, color);
            decal.drawDistance = 500f;
            decal.fadeFactor = 1f;
            decal.size = new Vector3(2f, ProjectionDepth, 2f);

            // Point downward to project onto terrain
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            return go;
        }

        // =====================================================================
        // RING TRANSFORM UPDATE
        // =====================================================================

        private void UpdateRingTransform(GameObject ring, Entity e, bool isBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(e)) return;
            var xf = _em.GetComponentData<LocalTransform>(e);
            var pos = (Vector3)xf.Position;

            var decal = ring.GetComponent<DecalProjector>();
            if (decal != null)
            {
                // Position above terrain, projecting downward
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z) + ProjectionDepth / 2f;

                if (isBuilding && _em.HasComponent<BuildingSize>(e))
                {
                    var size = _em.GetComponentData<BuildingSize>(e);
                    decal.size = new Vector3(
                        size.Width + DecalPadding,
                        ProjectionDepth,
                        size.Height + DecalPadding
                    );
                }
                else
                {
                    float r = MinRadius;
                    if (_em.HasComponent<Radius>(e))
                        r = Mathf.Max(MinRadius, _em.GetComponentData<Radius>(e).Value);
                    if (isBuilding)
                        r = Mathf.Max(r, BuildingRadius);

                    float d = r * 2f + DecalPadding;
                    decal.size = new Vector3(d, ProjectionDepth, d);
                }

                ring.transform.position = pos;
            }
            else
            {
                // Legacy cylinder path
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z) + 0.04f;

                float r = MinRadius;
                if (_em.HasComponent<Radius>(e))
                    r = Mathf.Max(MinRadius, _em.GetComponentData<Radius>(e).Value);
                if (isBuilding)
                    r = Mathf.Max(r, BuildingRadius);

                ring.transform.position = pos;
                ring.transform.localScale = new Vector3(r * 2f, 0.03f, r * 2f);
            }
        }

        // =====================================================================
        // COLOR UPDATE
        // =====================================================================

        private void UpdateRingColor(GameObject ring, Color color)
        {
            if (ring == null) return;

            var decal = ring.GetComponent<DecalProjector>();
            if (decal != null)
            {
                SetDecalColor(decal, color);
                return;
            }

            // Legacy path
            var mr = ring.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mat = mr.sharedMaterial;
            if (mat != null) SetMatColor(mat, color);
        }

        private void SetDecalColor(DecalProjector decal, Color color)
        {
            if (decal.material != null)
            {
                if (decal.material.HasProperty("_BaseColor"))
                    decal.material.SetColor("_BaseColor", color);
                else if (decal.material.HasProperty("Base_Color"))
                    decal.material.SetColor("Base_Color", color);
            }
        }

        // =====================================================================
        // LEGACY FALLBACK
        // =====================================================================

        private Material MakeLegacyRingMaterial()
        {
            Shader sh =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default");

            var m = new Material(sh);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 2);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0);
            m.renderQueue = 3000;
            return m;
        }

        private GameObject NewLegacyRing(Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SelectionRing";
            go.transform.rotation = Quaternion.identity;

            var mc = go.GetComponent<Collider>();
            if (mc) Destroy(mc);

            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(_ringMat);
            SetMatColor(mat, color);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private void SetMatColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private void ClearHoverRing()
        {
            if (_hoverRing != null) Destroy(_hoverRing);
            _hoverRing = null;
            _hoverFor = Entity.Null;
        }

        private Color GetFactionTint(Entity e, float alpha)
        {
            Color baseCol = new Color(1f, 1f, 1f, 1f);
            if (_em.Exists(e) && _em.HasComponent<FactionTag>(e))
            {
                var fac = _em.GetComponentData<FactionTag>(e).Value;
                baseCol = FactionColors.Get(fac);
            }
            baseCol.a = Mathf.Clamp01(alpha);
            return baseCol;
        }
    }
}
