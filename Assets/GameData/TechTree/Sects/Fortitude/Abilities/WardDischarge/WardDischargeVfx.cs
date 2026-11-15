// WardDischargeVfx.cs
// Ward Discharge on screen (docs/Design/Sects.md, Sect of Fortitude,
// 2026-11-15): "These charges must be visible. I imagine them as wisps
// floating around the building."
//
//   * one glowing wisp per remaining charge, spread evenly on a slow orbit
//     round the warded building, bobbing a little, in the owner's colour;
//   * a wisp whose charge detonates swells and is gone (the blast ring on the
//     ground is the simulation's own SectPowerVfx telegraph);
//   * when the ward runs out of time the wisps left simply vanish.
//
// Presentation only: reads WardDischargeCharge, writes nothing the simulation
// reads. Shown to everyone who can see the building: the wisps follow the
// building's view, so fog of war hides them with it, and a hostile building
// out of vision keeps the wisps it had when last seen (LastSeenBuildings).
//
// Wisps are pooled spheres on the shared procedural emissive material
// (ProceduralMaterialHelper, one MaterialPropertyBlock set per acquire), no
// realtime lights, nothing allocated per frame.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Rendering
{
    public sealed class WardDischargeVfx : MonoBehaviour
    {
        // ── Look (presentation only; no gameplay meaning) ───────────────────
        /// <summary>How often the ward components are read.</summary>
        private const float PollSeconds = 0.1f;
        /// <summary>Wisp size, metres across.</summary>
        private const float WispDiameter = 0.6f;
        /// <summary>Orbit radius = the building's half width + this.</summary>
        private const float OrbitMargin = 1.25f;
        private const float MinHalfWidth = 1f, MaxHalfWidth = 12f;
        private const float OrbitDegreesPerSecond = 26f;
        /// <summary>Orbit height as a fraction of the building's height.</summary>
        private const float HeightFraction = 0.55f;
        private const float MinOrbitHeight = 1.6f;
        private const float BobMetres = 0.35f;
        private const float BobRadiansPerSecond = 1.7f;
        /// <summary>The wisp breathes by this fraction of its size.</summary>
        private const float PulseFraction = 0.15f;
        private const float PulseRadiansPerSecond = 3.1f;
        /// <summary>Detonation: the wisp swells to this many times its size
        /// and collapses, over BurstSeconds.</summary>
        private const float BurstScale = 4.5f;
        private const float BurstSeconds = 0.45f;
        private const float BurstSwellShare = 0.3f;
        /// <summary>A ward that ends with more than this on its clock spent
        /// its last charge (a burst); below it, the ward ran out of time.</summary>
        private const float ExpiredBelowSeconds = 0.5f;
        /// <summary>White mixed into the owner's colour for the wisp body.</summary>
        private const float WhiteMix = 0.45f;

        private sealed class Rig
        {
            public Entity Entity;
            public readonly List<Transform> Wisps = new List<Transform>(8);
            public GameObject View;
            public Faction Owner;
            public float HalfWidth, Height;
            /// <summary>The building's own position (its cells' centre). The view's root is
            /// not that: a prefab's pivot offset shifts the root, which put the orbit
            /// several metres off the building.</summary>
            public Vector3 Centre;
            public bool HasCentre;
            public float Lifetime;      // the ward's clock when last read live
            public float Phase;
            public bool Shown;          // the wisps' current active state
            public bool Alive;          // matched by the query this poll
            public bool Stale;          // ended out of sight, kept as last seen
        }

        private struct Burst
        {
            public Transform Wisp;
            public float Age;
        }

        private readonly Dictionary<Entity, Rig> _byEntity = new Dictionary<Entity, Rig>();
        private readonly List<Rig> _rigs = new List<Rig>();
        private readonly List<Burst> _bursts = new List<Burst>();
        private readonly Stack<Transform> _pool = new Stack<Transform>();
        private readonly Stack<Rig> _rigPool = new Stack<Rig>();
        private EntityQuery _wards;
        private EntityManager _queryOwner;
        private bool _hasQuery;
        private float _nextPoll;

        void Update()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + PollSeconds;
                if (!_hasQuery || _queryOwner != em)
                {
                    // A new world: the old rigs point at entities that no
                    // longer mean anything.
                    for (int i = _rigs.Count - 1; i >= 0; i--) DropRig(i);
                    _wards = new EntityQueryBuilder(Allocator.Temp)
                        .WithAll<WardDischargeCharge, BuildingTag, FactionTag>().Build(em);
                    _queryOwner = em;
                    _hasQuery = true;
                }
                Poll(em);
            }

            if (_rigs.Count > 0) Animate();
            if (_bursts.Count > 0) TickBursts();
        }

        /// <summary>Bring every rig's wisp count in line with its ward.</summary>
        private void Poll(EntityManager em)
        {
            for (int i = 0; i < _rigs.Count; i++) _rigs[i].Alive = false;

            // WardDischargeCharge is enableable: only live wards match.
            if (!_wards.IsEmpty)
            {
                var views = EntityViewManager.Instance;
                using var ents = _wards.ToEntityArray(Allocator.Temp);
                using var wards = _wards.ToComponentDataArray<WardDischargeCharge>(Allocator.Temp);
                using var facs = _wards.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    if (!_byEntity.TryGetValue(e, out var rig))
                    {
                        rig = _rigPool.Count > 0 ? _rigPool.Pop() : new Rig();
                        rig.Entity = e;
                        rig.View = null;
                        rig.Shown = true;
                        rig.Lifetime = 0f;
                        rig.Stale = false;
                        // Spread the wards' starting angles so two warded
                        // buildings side by side do not turn in step.
                        rig.Phase = (e.Index * 0.6180339f % 1f) * Mathf.PI * 2f;
                        _byEntity[e] = rig;
                        _rigs.Add(rig);
                    }
                    rig.Alive = true;
                    rig.HasCentre = em.HasComponent<Unity.Transforms.LocalTransform>(e);
                    if (rig.HasCentre)
                    {
                        var at = em.GetComponentData<Unity.Transforms.LocalTransform>(e).Position;
                        rig.Centre = new Vector3(at.x, at.y, at.z);
                    }

                    GameObject view = null;
                    if (views != null) views.TryGetView(e, out view);
                    if (view != rig.View)
                    {
                        // First sight of the view, or a level-up swapped it.
                        rig.View = view;
                        if (view != null && VfxSpawn.MeshBounds(view, out var b))
                        {
                            rig.HalfWidth = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), MinHalfWidth, MaxHalfWidth);
                            rig.Height = Mathf.Max(MinOrbitHeight, b.size.y * HeightFraction);
                        }
                        else
                        {
                            rig.HalfWidth = MinHalfWidth * 2f;
                            rig.Height = MinOrbitHeight;
                        }
                        // The footprint is the truth for the width (the mesh
                        // bounds take in banners, plinth skirts and anything
                        // else hung on the view, and floated the wisps wide
                        // and far above a Watch Tower), and the orbit stays
                        // within a footprint's width of the ground so the
                        // wisps read as belonging to the building.
                        if (em.HasComponent<BuildingSize>(e))
                        {
                            var size = em.GetComponentData<BuildingSize>(e);
                            float half = Mathf.Max(size.Width, size.Height) * 0.5f;
                            if (half > 0f)
                            {
                                rig.HalfWidth = Mathf.Clamp(half, MinHalfWidth, MaxHalfWidth);
                                rig.Height = Mathf.Clamp(rig.Height, MinOrbitHeight, half * 1.5f);
                            }
                        }
                    }

                    // Out of vision a hostile building stays as last seen.
                    if (LastSeenBuildings.IsFrozen(e)) continue;
                    rig.Stale = false;
                    rig.Owner = facs[i].Value;
                    rig.Lifetime = wards[i].Lifetime;
                    SetCount(rig, Mathf.Max(0, wards[i].ChargesLeft), burst: IsOnScreen(rig));
                }
            }

            // Wards that ended since the last poll.
            for (int i = _rigs.Count - 1; i >= 0; i--)
            {
                var rig = _rigs[i];
                if (rig.Alive) continue;
                bool exists = em.Exists(rig.Entity);
                // Ended out of sight: the wisps stay until the building is
                // seen again.
                if (exists && LastSeenBuildings.IsFrozen(rig.Entity)) { rig.Stale = true; continue; }
                // The last charge went off (a burst) or the time ran out /
                // the building is gone (the wisps just vanish). One that
                // ended unseen goes quietly: nobody saw how.
                bool detonated = exists && !rig.Stale && rig.Lifetime > ExpiredBelowSeconds;
                SetCount(rig, 0, burst: detonated && IsOnScreen(rig));
                DropRig(i);
            }
        }

        private static bool IsOnScreen(Rig rig) => rig.View != null && rig.View.activeInHierarchy;

        /// <summary>Add or remove wisps until the rig carries
        /// <paramref name="count"/>. A removed wisp bursts where it floats
        /// when <paramref name="burst"/> is set, else it is simply put away.</summary>
        private void SetCount(Rig rig, int count, bool burst)
        {
            while (rig.Wisps.Count > count)
            {
                int last = rig.Wisps.Count - 1;
                var w = rig.Wisps[last];
                rig.Wisps.RemoveAt(last);
                if (w == null) continue;
                if (burst && w.gameObject.activeSelf) _bursts.Add(new Burst { Wisp = w, Age = 0f });
                else Release(w);
            }
            while (rig.Wisps.Count < count)
            {
                var w = Acquire(rig.Owner);
                w.gameObject.SetActive(rig.Shown);
                rig.Wisps.Add(w);
            }
        }

        private void DropRig(int index)
        {
            var rig = _rigs[index];
            for (int i = 0; i < rig.Wisps.Count; i++) if (rig.Wisps[i] != null) Release(rig.Wisps[i]);
            rig.Wisps.Clear();
            rig.View = null;
            _byEntity.Remove(rig.Entity);
            _rigs.RemoveAt(index);
            _rigPool.Push(rig);
        }

        /// <summary>Every frame: place each rig's wisps on their orbit.</summary>
        private void Animate()
        {
            float t = Time.time;
            float orbit = t * OrbitDegreesPerSecond * Mathf.Deg2Rad;
            for (int r = 0; r < _rigs.Count; r++)
            {
                var rig = _rigs[r];
                int n = rig.Wisps.Count;
                if (n == 0) continue;
                bool show = IsOnScreen(rig);
                if (show != rig.Shown)
                {
                    rig.Shown = show;
                    for (int i = 0; i < n; i++) if (rig.Wisps[i] != null) rig.Wisps[i].gameObject.SetActive(show);
                }
                if (!show) continue;

                Vector3 centre = rig.View.transform.position;
                if (rig.HasCentre) { centre.x = rig.Centre.x; centre.z = rig.Centre.z; }
                float radius = rig.HalfWidth + OrbitMargin;
                float step = Mathf.PI * 2f / n;
                for (int i = 0; i < n; i++)
                {
                    var w = rig.Wisps[i];
                    if (w == null) continue;
                    float a = rig.Phase + orbit + step * i;
                    float own = rig.Phase + i * 1.9f;   // each wisp bobs and breathes out of step
                    w.position = new Vector3(
                        centre.x + Mathf.Cos(a) * radius,
                        centre.y + rig.Height + Mathf.Sin(t * BobRadiansPerSecond + own) * BobMetres,
                        centre.z + Mathf.Sin(a) * radius);
                    float s = WispDiameter * (1f + Mathf.Sin(t * PulseRadiansPerSecond + own) * PulseFraction);
                    w.localScale = new Vector3(s, s, s);
                }
            }
        }

        /// <summary>Detonated wisps: swell fast, collapse, back to the pool.</summary>
        private void TickBursts()
        {
            float dt = Time.deltaTime;
            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                var b = _bursts[i];
                b.Age += dt;
                float k = b.Age / BurstSeconds;
                if (k >= 1f || b.Wisp == null)
                {
                    if (b.Wisp != null) Release(b.Wisp);
                    _bursts.RemoveAt(i);
                    continue;
                }
                float mul = k < BurstSwellShare
                    ? Mathf.Lerp(1f, BurstScale, k / BurstSwellShare)
                    : Mathf.Lerp(BurstScale, 0f, (k - BurstSwellShare) / (1f - BurstSwellShare));
                float s = WispDiameter * mul;
                b.Wisp.localScale = new Vector3(s, s, s);
                _bursts[i] = b;
            }
        }

        // ── The wisp pool ───────────────────────────────────────────────────

        private Transform Acquire(Faction owner)
        {
            Transform w = null;
            while (_pool.Count > 0 && w == null) w = _pool.Pop();
            if (w == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "WardWisp";
                go.layer = 2;   // Ignore Raycast: a wisp is never clicked
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                w = go.transform;
                w.SetParent(transform, false);
            }
            // The owner's colour, lifted toward white, at the lantern rung of
            // the emissive ladder (Art_Direction.md §3.4): a ward is the
            // faction's own light, never cyan (veilstone) or purple (curse).
            Color own = FactionColors.Get(owner);
            Color body = Color.Lerp(own, Color.white, WhiteMix);
            body.a = 1f;
            ProceduralMaterialHelper.SetEmissive(w.GetComponent<Renderer>(), body, body,
                EmissiveLadder.LanternIntensity);
            w.localScale = new Vector3(WispDiameter, WispDiameter, WispDiameter);
            w.gameObject.SetActive(true);
            return w;
        }

        private void Release(Transform w)
        {
            w.gameObject.SetActive(false);
            _pool.Push(w);
        }

        void OnDestroy()
        {
            for (int i = _rigs.Count - 1; i >= 0; i--) DropRig(i);
            for (int i = 0; i < _bursts.Count; i++) if (_bursts[i].Wisp != null) Destroy(_bursts[i].Wisp.gameObject);
            _bursts.Clear();
            while (_pool.Count > 0)
            {
                var w = _pool.Pop();
                if (w != null) Destroy(w.gameObject);
            }
        }
    }
}
