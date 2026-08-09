// SectPowerVfx.cs
// Presentation-side impact effects for the sect god powers (and the
// Reliquary's targeted abilities). Purely visual — spawned by
// SectActivePowerHelper.Fire after the simulation effect is dispatched,
// so it never influences lockstep state.
//
// Prefabs are MagicArsenal effects copied into
// Resources/Prefabs/Effects/Sect (copying without .meta re-GUIDs the
// prefab while its material/script references stay valid). Each cast
// picks a thematically-matched effect per sect; the instance is scaled
// roughly to the power's radius and force-destroyed after a few seconds
// so looping emitters (AreaDamage/Aura) cannot leak.

using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class SectPowerVfx
    {
        // MagicArsenal effects are authored around a ~6 m footprint;
        // scale casts to their gameplay radius within sane bounds.
        private const float AuthoredRadius = 6f;

        // KoreanTraditionalPattern "Bottom" ground circles are authored around
        // a ~5 m footprint; the flat pattern reads on the terrain and is scaled
        // to the ability's gameplay radius. Copied (re-GUID'd) into
        // Resources/Prefabs/Effects/Korean — swap the prefab there or the paths
        // below to reskin the ground circles.
        private const float KoreanAuthoredRadius = 5f;
        private const string GroundCircleTelegraph = "Prefabs/Effects/Korean/GroundCircleTelegraph";
        private const string GroundCircleSlow      = "Prefabs/Effects/Korean/GroundCircleSlow";
        private const string GroundCircleStop      = "Prefabs/Effects/Korean/GroundCircleStop";
        // Sim-time (s) a "stopped" circle is advanced to before it is frozen, so
        // the pattern is fully drawn rather than mid-build when it locks.
        private const float FreezeSnapshotTime = 1.2f;

        public static void SpawnForSect(string sectId, float3 pos, float radius)
        {
            string path = sectId switch
            {
                TheWaningBorder.Economy.SectConfig.Antiquity   => "Prefabs/Effects/Sect/NovaArcane",
                TheWaningBorder.Economy.SectConfig.Renewal     => "Prefabs/Effects/Sect/AuraCastLife",
                TheWaningBorder.Economy.SectConfig.Fortitude   => "Prefabs/Effects/Sect/NovaEarth",
                TheWaningBorder.Economy.SectConfig.Reclamation => "Prefabs/Effects/Sect/NovaLife",
                TheWaningBorder.Economy.SectConfig.Silence     => "Prefabs/Effects/Sect/NovaStorm",
                TheWaningBorder.Economy.SectConfig.Justice     => "Prefabs/Effects/Sect/LightPillarBlast",
                TheWaningBorder.Economy.SectConfig.Veneration  => "Prefabs/Effects/Sect/AuraCastLight",
                TheWaningBorder.Economy.SectConfig.Witness     => "Prefabs/Effects/Sect/AuraCastArcane",
                TheWaningBorder.Economy.SectConfig.War         => "Prefabs/Effects/Sect/NovaFire",
                TheWaningBorder.Economy.SectConfig.Ash         => "Prefabs/Effects/Sect/AreaDamageFire",
                TheWaningBorder.Economy.SectConfig.Ruin        => "Prefabs/Effects/Sect/ShadowPillarBlast",
                TheWaningBorder.Economy.SectConfig.Wrath       => "Prefabs/Effects/Sect/FirePillarBlast",
                _                                              => null,
            };
            Spawn(path, pos, radius);
        }

        /// <summary>
        /// Danger telegraph for an offensive power's wind-up: a Korean ground
        /// circle marking exactly where the strike will land. Destroyed when the
        /// wind-up elapses — the impact VFX takes over from there.
        /// </summary>
        public static void SpawnTelegraph(float3 pos, float radius, float seconds)
        {
            SpawnGroundCircle(GroundCircleTelegraph, pos, radius, seconds);
        }

        /// <summary>The Guild's Slow field: a Korean ground circle running at a
        /// very slow animation speed, matching the -50% movement debuff it
        /// telegraphs. Self-cleaning after <paramref name="seconds"/> s.</summary>
        public static void SpawnSlowField(float3 pos, float radius, float seconds)
        {
            SpawnGroundCircle(GroundCircleSlow, pos, radius, seconds, simSpeed: 0.15f);
        }

        /// <summary>The Guild's Stop field: a Korean ground circle frozen mid-
        /// animation, matching the -100% (root) debuff it telegraphs.
        /// Self-cleaning after <paramref name="seconds"/> s.</summary>
        public static void SpawnStopField(float3 pos, float radius, float seconds)
        {
            SpawnGroundCircle(GroundCircleStop, pos, radius, seconds, freeze: true);
        }

        /// <summary>Spawn a Korean "Bottom" ground-circle prefab flat on the
        /// terrain, scaled to the ability's gameplay radius and self-cleaning
        /// after <paramref name="seconds"/> s. <paramref name="simSpeed"/> scales
        /// every child ParticleSystem's playback (1 = normal); when
        /// <paramref name="freeze"/> is set the circle is advanced to a fully
        /// drawn snapshot and then held motionless (used for the Stop field).
        /// The authored root rotation is kept — the pattern is already flat.</summary>
        public static void SpawnGroundCircle(string path, float3 pos, float radius,
            float seconds, float simSpeed = 1f, bool freeze = false)
        {
            if (string.IsNullOrEmpty(path)) return;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return;

            var go = Object.Instantiate(prefab,
                (Vector3)pos + Vector3.up * 0.1f, prefab.transform.rotation);
            float s = Mathf.Clamp(radius / KoreanAuthoredRadius, 0.4f, 4f);
            go.transform.localScale *= s;

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            if (freeze)
            {
                // Advance to a representative frame, then Pause so the pattern
                // sits fully drawn and motionless for its whole lifetime.
                var root = go.GetComponent<ParticleSystem>();
                if (root != null)
                {
                    root.Simulate(FreezeSnapshotTime, true, true, false);
                    root.Pause(true);
                }
                else
                {
                    foreach (var ps in systems)
                    {
                        ps.Simulate(FreezeSnapshotTime, false, true, false);
                        ps.Pause();
                    }
                }
            }
            else if (simSpeed != 1f)
            {
                foreach (var ps in systems)
                {
                    var main = ps.main;
                    main.simulationSpeed = Mathf.Max(0.01f, simSpeed);
                }
            }

            Object.Destroy(go, seconds);
        }

        /// <summary>Spawn an effect prefab by Resources path, scaled to the
        /// gameplay radius, self-cleaning after <paramref name="life"/> s.
        /// Also used by the Reliquary abilities (NovaFrost for Lockout).</summary>
        public static void Spawn(string path, float3 pos, float radius, float life = 6f)
        {
            if (string.IsNullOrEmpty(path)) return;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return;

            // Keep the prefab's authored root rotation — MagicArsenal grounds
            // its flat effects via a -90° X rotation on the root, which
            // Instantiate(pos, Quaternion.identity) would stomp (the effects
            // then play standing upright, perpendicular to the ground).
            var go = Object.Instantiate(prefab, (Vector3)pos, prefab.transform.rotation);
            float s = Mathf.Clamp(radius / AuthoredRadius, 0.6f, 3f);
            go.transform.localScale *= s;
            Object.Destroy(go, life);
        }
    }
}
