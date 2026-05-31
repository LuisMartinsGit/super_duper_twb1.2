// UnitVisualOverlaySystem.cs
// Procedural overlays for unit feedback (spec refinement #7):
//   - Rank pips above the unit (one per UnitRank.Value, capped at 5).
//   - Glow halo around units whose effective EquipmentTier is Glow
//     (either UnitEquipmentApplied.Value == Glow or a UnitTierOverride
//     claim from a dropped Glow weapon).
//
// Standalone managed MonoBehaviour mirroring RitualBeamSystem's pattern:
// each tick, snapshot ECS state, ensure GameObjects exist + are positioned,
// prune any whose source entity is gone.
//
// Heavy-handed cost-wise for very large unit counts, but bounded — one
// wrapper GameObject per unit, not allocated per frame.
//
// Location: Assets/Scripts/Presentation/

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class UnitVisualOverlaySystem : MonoBehaviour
    {
        private const float PipBaseHeight = 2.4f;
        private const float PipSpacing = 0.18f;
        private const float PipSize = 0.14f;
        private const float HaloRadius = 0.65f;
        private const float HaloHeight = 0.5f;

        private class Overlay
        {
            public GameObject Root;
            public Transform PipParent;
            public GameObject Halo;
            public Transform ShieldBarRoot;   // empty wrapper for the shield bar widget
            public Transform ShieldBarFill;   // inner quad that gets X-scaled by ratio
            public int LastRank = -1;
            public bool LastGlow;
        }

        private readonly Dictionary<Entity, Overlay> _overlays = new();
        private Unity.Entities.World _world;
        private EntityManager _em;
        private EntityQuery _unitQuery;
        private Material _pipMat;
        private Material _haloMat;
        private Material _shieldBgMat;
        private Material _shieldFillMat;

        void Awake()
        {
            _pipMat = BuildMat(new Color(1.00f, 0.85f, 0.30f, 1f), emissive: true);
            _haloMat = BuildMat(new Color(1.00f, 0.80f, 0.20f, 0.45f), emissive: true);
            _shieldBgMat = BuildMat(new Color(0.10f, 0.20f, 0.30f, 0.85f), emissive: false);
            _shieldFillMat = BuildMat(new Color(0.45f, 0.80f, 1.00f, 0.95f), emissive: true);
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
                _unitQuery = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
            }

            using var ents = _unitQuery.ToEntityArray(Allocator.Temp);
            using var transforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var seen = new HashSet<Entity>();
            for (int i = 0; i < ents.Length; i++)
            {
                Entity e = ents[i];
                seen.Add(e);

                int rank = 1;
                if (_em.HasComponent<UnitRank>(e))
                    rank = math.clamp(_em.GetComponentData<UnitRank>(e).Value, 1, 5);

                bool isGlow = false;
                if (_em.HasComponent<UnitTierOverride>(e)
                    && _em.GetComponentData<UnitTierOverride>(e).Value == EquipmentTier.Glow)
                {
                    isGlow = true;
                }
                else if (_em.HasComponent<UnitEquipmentApplied>(e)
                    && _em.GetComponentData<UnitEquipmentApplied>(e).Value == EquipmentTier.Glow)
                {
                    isGlow = true;
                }

                if (!_overlays.TryGetValue(e, out var ov) || ov == null || ov.Root == null)
                {
                    ov = BuildOverlay();
                    _overlays[e] = ov;
                }

                // Position root at the unit; rank pips + halo are children offset above.
                ov.Root.transform.position = transforms[i].Position;

                if (ov.LastRank != rank)
                {
                    RebuildPips(ov, rank);
                    ov.LastRank = rank;
                }

                if (ov.LastGlow != isGlow)
                {
                    if (ov.Halo != null) ov.Halo.SetActive(isGlow);
                    ov.LastGlow = isGlow;
                }

                // Shield bar: visible only when the unit has a ShieldBar component AND Current > 0.
                bool hasShield = _em.HasComponent<ShieldBar>(e);
                int curShield = 0, maxShield = 0;
                if (hasShield)
                {
                    var sb = _em.GetComponentData<ShieldBar>(e);
                    curShield = sb.Current;
                    maxShield = sb.Max;
                }
                bool showBar = hasShield && curShield > 0 && maxShield > 0;
                if (ov.ShieldBarRoot != null)
                {
                    ov.ShieldBarRoot.gameObject.SetActive(showBar);
                    if (showBar && ov.ShieldBarFill != null)
                    {
                        float ratio = math.clamp((float)curShield / maxShield, 0f, 1f);
                        ov.ShieldBarFill.localScale = new Vector3(ratio, 1f, 1f);
                        // Pivot the fill to the left so it shrinks rightward.
                        ov.ShieldBarFill.localPosition = new Vector3(-0.5f * (1f - ratio), 0f, 0f);
                    }
                }
            }

            if (_overlays.Count > seen.Count)
            {
                var toRemove = new List<Entity>();
                foreach (var kv in _overlays)
                    if (!seen.Contains(kv.Key))
                    {
                        if (kv.Value?.Root != null) Destroy(kv.Value.Root);
                        toRemove.Add(kv.Key);
                    }
                foreach (var k in toRemove) _overlays.Remove(k);
            }
        }

        void OnDestroy()
        {
            foreach (var kv in _overlays)
                if (kv.Value?.Root != null) Destroy(kv.Value.Root);
            _overlays.Clear();
        }

        private Overlay BuildOverlay()
        {
            var root = new GameObject("UnitOverlay");

            var pipParent = new GameObject("Pips").transform;
            pipParent.SetParent(root.transform, false);
            pipParent.localPosition = new Vector3(0, PipBaseHeight, 0);

            // Halo: thin ring approximated as a flat scaled sphere centered on the unit's feet.
            var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "GlowHalo";
            halo.transform.SetParent(root.transform, false);
            halo.transform.localScale = new Vector3(HaloRadius * 2f, HaloHeight, HaloRadius * 2f);
            halo.transform.localPosition = new Vector3(0, HaloHeight * 0.5f, 0);
            StripCollider(halo);
            SetMat(halo, _haloMat);
            halo.SetActive(false);

            // Shield bar: a horizontal bar floating slightly below the rank pips.
            // Wrapper holds the background quad + fill quad. Fill is scaled per
            // frame by Current/Max ratio; pivots from the left edge so it
            // shrinks rightward.
            var sbRoot = new GameObject("ShieldBar").transform;
            sbRoot.SetParent(root.transform, false);
            sbRoot.localPosition = new Vector3(0, PipBaseHeight - 0.25f, 0);
            sbRoot.localScale = new Vector3(1.0f, 0.08f, 1f);  // 1u wide, 0.08u tall

            var sbBg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sbBg.name = "Bg";
            sbBg.transform.SetParent(sbRoot, false);
            sbBg.transform.localScale = new Vector3(1f, 1f, 0.1f);
            StripCollider(sbBg);
            SetMat(sbBg, _shieldBgMat);

            var sbFillParent = new GameObject("FillPivot").transform;
            sbFillParent.SetParent(sbRoot, false);
            sbFillParent.localPosition = Vector3.zero;
            var sbFill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sbFill.name = "Fill";
            sbFill.transform.SetParent(sbFillParent, false);
            sbFill.transform.localScale = new Vector3(1f, 1.1f, 0.12f);  // slightly proud of the bg
            StripCollider(sbFill);
            SetMat(sbFill, _shieldFillMat);

            sbRoot.gameObject.SetActive(false);

            return new Overlay
            {
                Root = root,
                PipParent = pipParent,
                Halo = halo,
                ShieldBarRoot = sbRoot,
                ShieldBarFill = sbFillParent,
            };
        }

        private void RebuildPips(Overlay ov, int rank)
        {
            // Clear existing pips.
            for (int i = ov.PipParent.childCount - 1; i >= 0; i--)
                Destroy(ov.PipParent.GetChild(i).gameObject);

            float row = -(rank - 1) * PipSpacing * 0.5f;
            for (int i = 0; i < rank; i++)
            {
                var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pip.name = $"Pip{i + 1}";
                pip.transform.SetParent(ov.PipParent, false);
                pip.transform.localScale = new Vector3(PipSize, PipSize, PipSize);
                pip.transform.localPosition = new Vector3(row + i * PipSpacing, 0, 0);
                StripCollider(pip);
                SetMat(pip, _pipMat);
            }
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private static void SetMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        private static Material BuildMat(Color color, bool emissive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Standard")
                       ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (emissive && mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2.5f);
            return mat;
        }
    }
}
