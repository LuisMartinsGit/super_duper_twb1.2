// File: Assets/Scripts/UI/HUD/SelectionRings.cs
// Selection and hover ring visualization for selected entities

using System.Collections.Generic;
using UnityEngine;
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
    /// Manages selection rings (cylinder decals) for selected and hovered entities.
    /// Ring colors are based on faction colors.
    /// FoW-aware: enemy hover rings only shown when entity is visible.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class SelectionRings : MonoBehaviour
    {
        [Header("Ring Geometry")]
        public float YOffset = 0.04f;
        public float MinRadius = 0.45f;
        public float BuildingRadius = 0.6f;
        public float RingThicknessY = 0.03f;

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

        private Material _ringMat;
        private FogOfWarManager _fow;
        private Faction _humanFaction = GameSettings.LocalPlayerFaction;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated) _em = _world.EntityManager;

            _ringMat = MakeRingMaterial();

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

                if (!_rings.TryGetValue(e, out var go) || go == null)
                {
                    var selCol = GetFactionTint(e, SelectedColor.a);
                    go = NewRing(selCol);
                    _rings[e] = go;
                }

                UpdateRingTransform(go, e, isBuilding: _em.HasComponent<BuildingTag>(e));
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
                    // FoW check for enemies
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
                if (_hoverRing == null || _hoverFor != h)
                {
                    ClearHoverRing();
                    var col = GetFactionTint(h, HoverEnemyColor.a);
                    _hoverRing = NewRing(col);
                    _hoverFor = h;
                }

                UpdateRingTransform(_hoverRing, h, isBuilding: _em.HasComponent<BuildingTag>(h));
                UpdateRingColor(_hoverRing, GetFactionTint(h, HoverEnemyColor.a));
            }
            else
            {
                ClearHoverRing();
            }
        }

        private Material MakeRingMaterial()
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

        private GameObject NewRing(Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SelectionRing";
            go.transform.rotation = Quaternion.identity;

            // Destroy collider entirely — disabled colliders can still interfere
            // with physics queries and crystal collection raycasting
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

        private void UpdateRingColor(GameObject ring, Color color)
        {
            if (ring == null) return;
            var mr = ring.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mat = mr.sharedMaterial;
            if (mat != null) SetMatColor(mat, color);
        }

        private void UpdateRingTransform(GameObject ring, Entity e, bool isBuilding)
        {
            if (!_em.HasComponent<LocalTransform>(e)) return;
            var xf = _em.GetComponentData<LocalTransform>(e);
            var pos = (Vector3)xf.Position;

            // Project ring onto terrain surface (not entity Y which may be stale)
            // This makes the ring appear as a ground decal under the unit
            pos.y = TerrainUtility.GetHeight(pos.x, pos.z) + YOffset;

            float r = MinRadius;
            if (_em.HasComponent<Radius>(e))
                r = Mathf.Max(MinRadius, _em.GetComponentData<Radius>(e).Value);
            if (isBuilding)
                r = Mathf.Max(r, BuildingRadius);

            ring.transform.position = pos;
            ring.transform.localScale = new Vector3(r * 2f, Mathf.Max(0.01f, RingThicknessY), r * 2f);
        }

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