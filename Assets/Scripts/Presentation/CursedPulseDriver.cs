// CursedPulseDriver.cs
// Drives the BFS ring-chain emission animation for cursed-ground crystal
// clusters (task-cursed-ground-luminous-crystals-111, Iteration 2 iteration).
//
// Model: chain reaction by BFS ring depth.
//   Each block carries an integer "ring depth" = its Chebyshev grid
//   distance from the owning crystal node's centre cell. Depth 0 = the
//   node cell itself. The driver lights up blocks ring-by-ring:
//     - At cycle-time 0, every depth-0 block fires (briefly bright).
//     - At cycle-time RingStepDuration, every depth-1 block fires.
//     - At cycle-time 2*RingStepDuration, depth-2 fires. Etc.
//     - After MaxRingDepth*RingStepDuration, the last ring fires.
//     - HoldDuration of darkness follows, then the cycle restarts at
//       depth 0.
//   Each ring stays visibly lit for ~RingLitFalloff seconds after firing
//   (brief ramp up + slow decay), so adjacent rings overlap visually for
//   continuity. The wavefront reads as a glow spreading outward by
//   adjacency, not by Euclidean distance — exactly what the user asked
//   for after Iteration 2.
//
// Previous model (Euclidean sine wave from distance) replaced because it
// produced multiple concurrent waves visible as "unrelated rings" once
// the cursed area exceeded ~10 m, even with the formula corrected.
//
// Implementation:
//   - Singleton MonoBehaviour, lazy-spawned the first time a cluster
//     registers itself.
//   - Per-cluster entry: root Transform, MeshRenderers, parallel int[]
//     of per-block depths, optional hero Light + its own depth, base
//     emission, base hero-light intensity.
//   - Update walks the list once per frame. Per-cluster: recomputes per-
//     block pulse01 from depth + cycle time, applies emission via
//     MaterialPropertyBlock (one MPB build per block, since each block
//     can be at a different depth). Hero light intensity tracks the
//     cluster's hero-light-depth pulse.
//   - Destroyed clusters are swap-removed on detection.
//
// Multiplayer determinism:
//   Pulse uses Time.time (wall clock per peer). Peers do not run on the
//   same wall clock so the cosmetic pulse phase drifts between peers.
//   Block placement, depth assignment, and hero-light selection remain
//   fully deterministic — only the pulse animation itself is non-
//   deterministic, and it has zero gameplay impact.
//
// Location: Assets/Scripts/Presentation/CursedPulseDriver.cs

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public sealed class CursedPulseDriver : MonoBehaviour
    {
        // ---- Tunables ----

        /// <summary>Seconds between consecutive ring depths firing. 0.20 s
        /// means depth 1 fires 200 ms after depth 0 — fast enough to read
        /// as a sweeping wave, slow enough to see ring-by-ring spread.</summary>
        private const float RingStepDuration = 0.20f;

        /// <summary>Seconds a ring stays visibly lit after its fire moment.
        /// Set ~3× RingStepDuration so adjacent rings have overlapping lit
        /// windows — the wave reads as continuous, not as blinks.</summary>
        private const float RingLitFalloff = 0.60f;

        /// <summary>How many ring depths the cycle covers before resetting.
        /// At 1.0 m grid step, 25 rings = 25 m radius — comfortably more
        /// than the default CrystalNode.SpreadRadius (15 m). Blocks at
        /// higher depths simply stay dark.</summary>
        private const int MaxRingDepth = 25;

        /// <summary>Dark pause between cycles (s). Keeps the "all dim,
        /// then a fresh wave starts at centre" rhythm punctuated.</summary>
        private const float HoldDuration = 1.0f;

        private const float CycleDuration =
            MaxRingDepth * RingStepDuration + HoldDuration;

        /// <summary>Emission multiplier at peak brightness (per ring's
        /// brief flash). The material's baked emission is intentionally
        /// low (~0.5×) so this multiplier produces a clear bright flash
        /// without being searing.</summary>
        private const float EmissionMultiplierMax = 2.5f;

        /// <summary>Emission multiplier between flashes — essentially
        /// dark. Blocks read as "dark crystal" most of the time.</summary>
        private const float EmissionMultiplierMin = 0.05f;

        /// <summary>Ramp-up duration at the start of a ring's lit window
        /// (s). Short — the flash arrives sharp, then decays slowly.</summary>
        private const float RampUpDuration = 0.05f;

        // ---- Singleton ----

        public static CursedPulseDriver Instance { get; private set; }

        private static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("[CursedPulseDriver]");
            Instance = go.AddComponent<CursedPulseDriver>();
            // Don't DontDestroyOnLoad — pulse driver should die with the
            // scene so a fresh match starts with a clean entry list.
        }

        // ---- Registered clusters ----

        private struct Entry
        {
            public Transform Root;             // for null-check on destroy
            public MeshRenderer[] Blocks;
            public int[] BlockDepths;          // parallel to Blocks
            public Light HeroLight;            // may be null
            public int HeroDepth;              // depth for hero-light pulse phase
            public Color BaseEmission;         // emission * intensity (matches material)
            public float BaseLightIntensity;
        }

        private readonly List<Entry> _entries = new(256);
        private MaterialPropertyBlock _mpb;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // ---- Public API ----

        /// <summary>
        /// Register a cursed-ground crystal cluster with the pulse driver.
        /// The cluster's emission and (optional) hero light will modulate
        /// per frame based on each block's BFS ring depth from the owning
        /// crystal-node centre cell. Cleanup is automatic when
        /// <paramref name="root"/> is destroyed.
        /// </summary>
        public static void Register(
            Transform root,
            MeshRenderer[] blocks,
            int[] blockDepths,
            Light heroLight,
            int heroDepth,
            Color baseEmission,
            float baseLightIntensity)
        {
            if (root == null || blocks == null || blocks.Length == 0) return;
            if (blockDepths == null || blockDepths.Length != blocks.Length) return;
            EnsureInstance();
            Instance._entries.Add(new Entry
            {
                Root = root,
                Blocks = blocks,
                BlockDepths = blockDepths,
                HeroLight = heroLight,
                HeroDepth = heroDepth,
                BaseEmission = baseEmission,
                BaseLightIntensity = baseLightIntensity,
            });
        }

        // ---- Per-frame update ----

        private void Update()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            float now = Time.time;
            float cycleTime = now - Mathf.Floor(now / CycleDuration) * CycleDuration;

            // Iterate backwards so swap-remove of destroyed clusters is cheap.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Root == null)
                {
                    // Cluster destroyed — drop the entry.
                    int last = _entries.Count - 1;
                    if (i != last) _entries[i] = _entries[last];
                    _entries.RemoveAt(last);
                    continue;
                }

                Color e0 = e.BaseEmission;

                // Per-block emission — each block in the cluster has its
                // own depth, so each gets its own pulse01.
                for (int j = 0; j < e.Blocks.Length; j++)
                {
                    var r = e.Blocks[j];
                    if (r == null) continue;
                    float pulse01 = ComputeRingPulse(cycleTime, e.BlockDepths[j]);
                    float mult = Mathf.Lerp(EmissionMultiplierMin, EmissionMultiplierMax, pulse01);
                    Color em = new Color(e0.r * mult, e0.g * mult, e0.b * mult, e0.a);
                    _mpb.Clear();
                    _mpb.SetColor(EmissionColorId, em);
                    r.SetPropertyBlock(_mpb);
                }

                // Hero light — modulate intensity with the cluster's
                // representative depth (cluster centre cell).
                if (e.HeroLight != null)
                {
                    float heroPulse = ComputeRingPulse(cycleTime, e.HeroDepth);
                    float heroMult = Mathf.Lerp(EmissionMultiplierMin, EmissionMultiplierMax, heroPulse);
                    e.HeroLight.intensity = e.BaseLightIntensity * heroMult;
                }
            }
        }

        /// <summary>
        /// Returns the [0, 1] pulse value for a given ring depth at a given
        /// cycle time. Zero outside the depth's lit window; brief sharp
        /// ramp up then quadratic decay across <see cref="RingLitFalloff"/>.
        /// </summary>
        private static float ComputeRingPulse(float cycleTime, int depth)
        {
            if (depth < 0 || depth >= MaxRingDepth) return 0f;
            float ringFireTime = depth * RingStepDuration;
            float dt = cycleTime - ringFireTime;
            if (dt < 0f) return 0f;            // hasn't fired yet
            if (dt > RingLitFalloff) return 0f; // already faded
            if (dt < RampUpDuration)
            {
                // Sharp ramp up — flash arrives crisp.
                return dt / RampUpDuration;
            }
            // Quadratic decay across the remaining window — slow fade.
            float decayX = (dt - RampUpDuration) / (RingLitFalloff - RampUpDuration);
            float invX = 1f - decayX;
            return invX * invX;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
