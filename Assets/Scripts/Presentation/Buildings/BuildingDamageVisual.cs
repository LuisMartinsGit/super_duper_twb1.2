// BuildingDamageVisual.cs
// Drives the TheWaningBorder/BuildingDamage shader from a building's ECS Health,
// so a building visibly accrues soot / cracks / blown-out chunks as it loses HP.
// Location: Assets/Scripts/Presentation/Buildings/BuildingDamageVisual.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Attached to every building visual by PresentationSpawnSystem. Each frame it
    /// reads the linked entity's <see cref="Health"/> and computes a 0..1 damage
    /// fraction (<c>1 - Value/Max</c>).
    ///
    /// To avoid touching pristine buildings (and to keep their original lit
    /// materials with full normal/detail maps), it stays dormant until the
    /// building first takes damage. On the first hit it swaps every renderer's
    /// materials to instances of <c>Resources/BuildingDamage.mat</c> — carrying
    /// over the original albedo map + colour + metallic/smoothness so the
    /// un-damaged look is preserved — then drives <c>_Damage</c> each frame.
    ///
    /// The death/collapse animation itself is owned by BuildingEffectSystem
    /// (triggered when Health hits 0). At that point _Damage is ~1, so the
    /// building collapses fully scorched and breached — the culmination of the
    /// accumulating damage. Material instances are freed in OnDestroy when
    /// PresentationSpawnSystem removes the GameObject.
    /// </summary>
    public sealed class BuildingDamageVisual : MonoBehaviour
    {
        /// <summary>ECS entity this building visual represents. Set at spawn.</summary>
        public Entity Entity;

        private const string DamageMaterialResource = "BuildingDamage";
        // Below this damage the building is treated as pristine (no swap).
        private const float SwapThreshold = 0.01f;
        // Matches DeathSystem.BuildingCollapseDuration — the window over which a
        // dead building's collapse plays and the fiery 0.5..1 burn sweeps in.
        private const float CollapseDuration = 2.0f;

        private static readonly int DamageID     = Shader.PropertyToID("_Damage");
        private static readonly int BaseMapID    = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorID  = Shader.PropertyToID("_BaseColor");
        private static readonly int UseBaseMapID = Shader.PropertyToID("_UseBaseMap");
        private static readonly int MetallicID   = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessID = Shader.PropertyToID("_Smoothness");
        private static readonly int MainTexID    = Shader.PropertyToID("_MainTex");
        private static readonly int ColorID      = Shader.PropertyToID("_Color");

        private static Material _master;

        private EntityManager _em;
        private bool _valid;
        private bool _swapped;
        private readonly List<Material> _owned = new();

        void Start()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            _em = world.EntityManager;
            _valid = true;
        }

        void LateUpdate()
        {
            if (!_valid) return;

            // Guard against the ECS world being disposed (returned to menu).
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (_em != world.EntityManager) _em = world.EntityManager;

            if (Entity == Entity.Null || !_em.Exists(Entity)) return;
            if (!_em.HasComponent<Health>(Entity)) return;

            // Construction holds HP at 1 while the site builds — that is NOT
            // battle damage. Without this gate every new building swapped to
            // damage materials and ApplyCrookedTilt knocked one RANDOM piece
            // askew the moment the visual spawned ("always one crooked piece
            // after the build animation, never the same one"). Collapse still
            // renders if a site is destroyed mid-construction.
            if (_em.HasComponent<UnderConstruction>(Entity)
                && !_em.HasComponent<BuildingCollapseState>(Entity)) return;

            var h = _em.GetComponentData<Health>(Entity);
            if (h.Max <= 0) return;

            // While the building is alive it only accrues "repairable" damage —
            // cracks + light scorch — so _Damage is held to <= 0.5 no matter how
            // low its HP gets. The fiery upper half (flames, heavy char, blown-out
            // holes) is driven entirely by the death/collapse phase: once the
            // building hits 0 HP, DeathSystem stamps BuildingCollapseState with a
            // ~2 s timer, and we ramp _Damage 0.5 -> 1 across it so the burn sweeps
            // in exactly during the collapse animation.
            float damage;
            if (_em.HasComponent<BuildingCollapseState>(Entity))
            {
                float timer = _em.GetComponentData<BuildingCollapseState>(Entity).Timer;
                float progress = 1f - Mathf.Clamp01(timer / CollapseDuration); // 0 at collapse start -> 1 at end
                damage = Mathf.Lerp(0.5f, 1f, progress);
            }
            else
            {
                damage = (1f - Mathf.Clamp01((float)h.Value / h.Max)) * 0.5f; // alive: 0..0.5
            }

            if (!_swapped)
            {
                if (damage <= SwapThreshold) return; // still pristine — leave originals
                SwapToDamageMaterials();
                ApplyCrookedTilt();
                _swapped = true;
            }

            for (int i = 0; i < _owned.Count; i++)
                if (_owned[i] != null) _owned[i].SetFloat(DamageID, damage);
        }

        // Replace every renderer's materials with BuildingDamage instances,
        // carrying over each slot's albedo map + tint so the building still
        // reads as itself (the faction colour is already baked into the atlas
        // by BuildingFactionColorMarker, so copying _BaseMap preserves it).
        private void SwapToDamageMaterials()
        {
            if (_master == null) _master = Resources.Load<Material>(DamageMaterialResource);
            if (_master == null)
            {
                Debug.LogWarning("[BuildingDamageVisual] Missing Resources/BuildingDamage.mat — " +
                                 "damage shader disabled.");
                return;
            }

            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                var src = renderer.materials; // instances
                var dst = new Material[src.Length];

                for (int s = 0; s < src.Length; s++)
                {
                    var srcMat = src[s];

                    // Effect overlays (the level-up/construction wave band)
                    // are not building surfaces — swapping them to the
                    // damage material both killed the running effect and
                    // spammed "doesn't have a texture property '_MainTex'"
                    // per material per swap (2026-08-04 console flood):
                    // the WaveBand shader has neither _BaseMap nor _MainTex,
                    // and Material.mainTexture LOGS on such shaders.
                    if (srcMat != null && srcMat.shader != null
                        && srcMat.shader.name == "TheWaningBorder/BuildingWaveBand")
                    {
                        dst[s] = srcMat;
                        continue;
                    }

                    var dm = new Material(_master);

                    Texture tex =
                        srcMat.HasProperty(BaseMapID) ? srcMat.GetTexture(BaseMapID) :
                        srcMat.HasProperty(MainTexID) ? srcMat.GetTexture(MainTexID) :
                        null; // never fall through to .mainTexture — it logs on shaders without _MainTex
                    if (tex != null) { dm.SetTexture(BaseMapID, tex); dm.SetFloat(UseBaseMapID, 1f); }
                    else             { dm.SetFloat(UseBaseMapID, 0f); }

                    Color col =
                        srcMat.HasProperty(BaseColorID) ? srcMat.GetColor(BaseColorID) :
                        srcMat.HasProperty(ColorID)     ? srcMat.GetColor(ColorID)     :
                        Color.white;
                    dm.SetColor(BaseColorID, col);

                    if (srcMat.HasProperty(MetallicID))   dm.SetFloat(MetallicID, srcMat.GetFloat(MetallicID));
                    if (srcMat.HasProperty(SmoothnessID)) dm.SetFloat(SmoothnessID, srcMat.GetFloat(SmoothnessID));
                    dm.SetFloat(DamageID, 0f);

                    dst[s] = dm;
                    _owned.Add(dm);
                }

                renderer.materials = dst;
            }
        }

        // Once damaged, knock one random sub-element askew (up to 10°) so the
        // building reads as structurally battered — a beam/wall section leaning.
        // Applied to a child renderer's transform (never the root, so the whole
        // building doesn't tilt and the root collider/selection stay put). The
        // tilt persists, and the collapse animation snapshots it like any other
        // transform, so the crooked piece collapses with the rest.
        private void ApplyCrookedTilt()
        {
            Transform pick = null;
            int seen = 0;
            // Reservoir-pick one child renderer transform uniformly (excludes root).
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                var tr = r.transform;
                if (tr == transform) continue; // never the root itself
                seen++;
                if (UnityEngine.Random.Range(0, seen) == 0) pick = tr;
            }
            if (pick == null) return;

            // Mostly-horizontal axis so the element LEANS (tilts) rather than
            // spins about its up axis; up to 10° in a random direction.
            Vector3 axis = new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-0.15f, 0.15f),
                UnityEngine.Random.Range(-1f, 1f));
            if (axis.sqrMagnitude < 1e-4f) axis = Vector3.right;
            axis.Normalize();

            float angle = UnityEngine.Random.Range(3f, 10f);
            pick.localRotation = Quaternion.AngleAxis(angle, axis) * pick.localRotation;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _owned.Count; i++)
                if (_owned[i] != null) Destroy(_owned[i]);
            _owned.Clear();
        }
    }
}
