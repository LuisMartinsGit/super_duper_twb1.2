// SpellVfxPlayer.cs
// Runtime spawner that plays a Spell's three VFX slots: the power-up (wind-up),
// the cast (impact/nova) and the ground circle. It scales each to the spell's
// radius, applies the optional per-slot colour tint, drives the circle's
// animation speed (normal / slow / frozen), and force-destroys everything after
// the spell's lifetime so looping emitters can't leak.
//
// The ground circle is sized straight from Spell.radius — its on-ground radius
// equals the ability's Radius.

using UnityEngine;

namespace TheWaningBorder.Abilities.Vfx
{
    public static class SpellVfxPlayer
    {
        // MagicArsenal cast effects are authored around a ~6 m footprint.
        private const float AuthoredEffectRadius = 6f;
        // World radius (m) of the Korean ground-circle prefab at scale 1, so the
        // circle scales to an on-ground radius that EXACTLY equals Spell.radius.
        private const float CirclePrefabRadius = 5f;
        // A "frozen" circle is advanced to this sim time then paused.
        private const float FreezeSnapshotTime = 1.2f;

        /// <summary>Play <paramref name="s"/>'s power-up + cast + circle at
        /// <paramref name="pos"/>. All self-destruct after the spell's lifetime.</summary>
        public static void Cast(Spell s, Vector3 pos)
        {
            if (s == null) return;
            float life = s.DisplayLife;

            // Circle first so the cast draws on top of it.
            if (s.circlePrefab != null)
            {
                var go = SpawnCircleSlot(s, pos);
                ApplySpeed(go, s.circleSpeed);
                Object.Destroy(go, life);
            }
            if (s.castPrefab != null)
            {
                var go = SpawnCastSlot(s.castPrefab, s.radius, s.castTint, s.castColor, pos);
                ApplySpeed(go, s.castSpeed);
                Object.Destroy(go, life);
            }
            // Power-up reads as a wind-up: shorter, tied to cast time.
            if (s.powerUpPrefab != null)
            {
                var go = SpawnCastSlot(s.powerUpPrefab, s.radius, s.powerUpTint, s.powerUpColor, pos);
                ApplySpeed(go, s.powerUpSpeed);
                Object.Destroy(go, Mathf.Max(1.5f, s.castTime));
            }
        }

        /// <summary>Instantiate a cast/power-up effect, scaled to radius and tinted.
        /// No lifetime — the caller owns destruction. Shared by the live cast and
        /// the editor preview.</summary>
        public static GameObject SpawnCastSlot(GameObject prefab, float radius, bool tint,
            Color color, Vector3 pos, Transform parent = null)
        {
            var go = Object.Instantiate(prefab, pos, prefab.transform.rotation, parent);
            go.transform.localScale = prefab.transform.localScale * Mathf.Clamp(
                (radius > 0f ? radius : AuthoredEffectRadius) / AuthoredEffectRadius, 0.4f, 4f);
            if (tint) Tint(go.GetComponentsInChildren<ParticleSystem>(true), color);
            return go;
        }

        /// <summary>Instantiate the ground circle, sized so its on-ground radius
        /// equals Spell.radius and tinted. Playback speed is NOT applied here —
        /// call <see cref="ApplySpeed"/> (live cast) or drive it yourself (editor
        /// preview). No lifetime — the caller owns destruction.</summary>
        public static GameObject SpawnCircleSlot(Spell s, Vector3 pos, Transform parent = null)
        {
            // Korean "Bottom" circles are authored flat (identity root rotation) —
            // keep the prefab's rotation and lift a hair to avoid z-fighting.
            var go = Object.Instantiate(s.circlePrefab, pos + Vector3.up * 0.1f,
                s.circlePrefab.transform.rotation, parent);
            // On-ground radius == Spell.radius. No clamp, so the circle always
            // matches the ability's Radius.
            float r = s.radius > 0f ? s.radius : CirclePrefabRadius;
            go.transform.localScale = s.circlePrefab.transform.localScale * (r / CirclePrefabRadius);
            if (s.circleTint) Tint(go.GetComponentsInChildren<ParticleSystem>(true), s.circleColor);
            return go;
        }

        /// <summary>Apply a playback-speed multiplier to every particle system in
        /// <paramref name="go"/>: 1 = normal, &lt;1 = slow, &gt;1 = fast, 0 =
        /// frozen (advanced to a drawn snapshot then held). Used at cast time; the
        /// editor preview drives speed via incremental Simulate instead.</summary>
        public static void ApplySpeed(GameObject go, float speed)
        {
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            if (speed <= 0.001f)
            {
                var rootPs = go.GetComponent<ParticleSystem>();
                if (rootPs != null) { rootPs.Simulate(FreezeSnapshotTime, true, true, false); rootPs.Pause(true); }
                else foreach (var ps in systems) { ps.Simulate(FreezeSnapshotTime, false, true, false); ps.Pause(); }
                return;
            }
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.simulationSpeed = speed;
            }
        }

        /// <summary>Best-effort recolour: multiply every particle system's start
        /// colour by <paramref name="color"/>.</summary>
        private static void Tint(ParticleSystem[] systems, Color color)
        {
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(color);
            }
        }
    }
}
