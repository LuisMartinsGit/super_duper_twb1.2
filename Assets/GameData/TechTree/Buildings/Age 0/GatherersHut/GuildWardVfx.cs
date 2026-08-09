// GuildWardVfx.cs
// Presentation for the Alanthor Guild's Slow ward (Gatherer's Hut, Veilstone
// Walls). Purely visual — spawned by GathererHutReinforcementSystem after the
// slow debuff is dispatched, so it never touches simulation state.
//
//   * Power-up      — AuraCirclingArcane (purple orbiting particles), a brief
//                     wind-up flourish at the hut.
//   * The power     — AuraSimpleArcane, a looping ground aura held for the whole
//                     slow duration. Both are scaled to the hut's gather radius.
//   * Per enemy     — AuraSlowdown, attached to every slowed enemy and made to
//                     follow it (VfxFollower) for the debuff duration.
//
// Prefabs are copied (re-GUID'd) into Resources/Prefabs/Effects/Guild; their
// material references into the source packs stay valid.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class GuildWardVfx
    {
        private const string CirclingPath = "Prefabs/Effects/Guild/AuraCirclingArcane";
        private const string SimplePath   = "Prefabs/Effects/Guild/AuraSimpleArcane";
        private const string SlowdownPath = "Prefabs/Effects/Guild/AuraSlowdown";

        // Approximate on-ground radius (m) of each aura at scale 1, so we can
        // scale them to the gather radius. Tune these two numbers if an aura
        // reads a touch too large/small against the collection circle.
        // Simple aura is dialled in to cover the full gather circle; the circling
        // power-up sits at the original (smaller) size.
        private const float CirclingAuthoredRadius = 3.5f;
        private const float SimpleAuthoredRadius   = 1.3f;
        // How long the circling power-up flourish lingers.
        private const float PowerUpLife = 2.5f;

        /// <summary>Spawn the hut-centred ward visuals: the circling power-up and
        /// the looping ground aura, both scaled to <paramref name="radius"/>
        /// (the gather radius). The aura is held for <paramref name="duration"/>.</summary>
        public static void SpawnGuildSlow(float3 hutPos, float radius, float duration)
        {
            SpawnScaled(CirclingPath, hutPos, radius / CirclingAuthoredRadius, PowerUpLife, loop: false);
            SpawnScaled(SimplePath,   hutPos, radius / SimpleAuthoredRadius,  duration,   loop: true);
        }

        /// <summary>Attach the slowdown aura to <paramref name="enemy"/> and make
        /// it follow the unit for <paramref name="duration"/> seconds.</summary>
        public static void AttachSlowAura(EntityManager em, Entity enemy, float3 pos, float duration)
        {
            var prefab = Resources.Load<GameObject>(SlowdownPath);
            if (prefab == null) return;
            var go = Object.Instantiate(prefab, (Vector3)pos, prefab.transform.rotation);
            SetLooping(go, true); // hold for the whole debuff, not a one-shot
            go.AddComponent<VfxFollower>().Init(em, enemy, duration);
        }

        private static void SpawnScaled(string path, float3 pos, float scale, float life, bool loop)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return;
            var go = Object.Instantiate(prefab, (Vector3)pos, prefab.transform.rotation);
            go.transform.localScale = prefab.transform.localScale * Mathf.Max(0.01f, scale);
            if (loop) SetLooping(go, true);
            Object.Destroy(go, life);
        }

        private static void SetLooping(GameObject go, bool loop)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = loop;
            }
        }
    }

    /// <summary>Sticks a spawned VFX GameObject onto an ECS unit: each frame it
    /// reads the entity's LocalTransform and matches position, self-destructing
    /// after its lifetime or once the target is gone. Main-thread only.</summary>
    public sealed class VfxFollower : MonoBehaviour
    {
        private EntityManager _em;
        private Entity _target;
        private float _life;

        public void Init(EntityManager em, Entity target, float life)
        {
            _em = em;
            _target = target;
            _life = life;
        }

        private void LateUpdate()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) { Destroy(gameObject); return; }

            try
            {
                var w = _em.World;
                if (w == null || !w.IsCreated
                    || !_em.Exists(_target)
                    || !_em.HasComponent<LocalTransform>(_target))
                {
                    Destroy(gameObject);
                    return;
                }
                transform.position = (Vector3)_em.GetComponentData<LocalTransform>(_target).Position;
            }
            catch
            {
                Destroy(gameObject);
            }
        }
    }
}
