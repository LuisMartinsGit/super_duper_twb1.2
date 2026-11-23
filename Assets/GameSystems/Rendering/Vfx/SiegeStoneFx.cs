// The look of a lobbed siege stone (catapult, trebuchet, Keep emplacement).
//
// The stone the player sees IS the simulation's projectile (decision 118):
// ProjectileVisualSystem moves this visual with the ECS entity and plays the
// burst the frame that entity dies, so the stone, its burst and its damage
// can never disagree about where or when. The Synty FX_CatapultShot effect is
// used for its parts only — rock mesh, smoke trail, impact bursts. Its own
// particle physics (a stone with world collision, launched on a second,
// unsynchronised arc) is not run at all: that is what burst on the engine
// itself and landed away from the damage.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class SiegeStoneFx
    {
        private const string FxResource = "Prefabs/Effects/FX_CatapultShot";
        /// <summary>Rock size when the effect carries none (metres across).</summary>
        private const float FallbackStoneSize = 0.7f;
        /// <summary>Tumble of the rock in flight, degrees per second.</summary>
        public const float TumbleDegreesPerSecond = 300f;

        /// <summary>
        /// Build the two inactive templates: the flying stone (rock mesh with
        /// the smoke trail as a child) and the impact burst. Either may come
        /// back null when the effect prefab is missing.
        /// </summary>
        public static void Build(out GameObject stone, out GameObject impact)
        {
            stone = null;
            impact = null;
            var prefab = Resources.Load<GameObject>(FxResource);
            if (prefab == null) return;

            var source = Object.Instantiate(prefab);
            source.SetActive(false);

            // ── The stone: the root system's rock, drawn as a plain mesh. ──
            stone = new GameObject("SiegeStoneTemplate");
            stone.SetActive(false);
            Object.DontDestroyOnLoad(stone);

            var rootPs = source.GetComponent<ParticleSystem>();
            var rootRenderer = source.GetComponent<ParticleSystemRenderer>();
            Mesh mesh = rootRenderer != null ? rootRenderer.mesh : null;
            Material mat = rootRenderer != null ? rootRenderer.sharedMaterial : null;
            float size = rootPs != null ? rootPs.main.startSize.constantMax : 0f;
            if (size < 0.05f) size = FallbackStoneSize;

            GameObject rock;
            if (mesh != null && mat != null)
            {
                rock = new GameObject("Rock");
                rock.AddComponent<MeshFilter>().sharedMesh = mesh;
                rock.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            else
            {
                rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "Rock";
                var col = rock.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                var r = rock.GetComponent<Renderer>();
                r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.36f, 0.34f, 0.32f) };
            }
            rock.transform.SetParent(stone.transform, false);
            rock.transform.localScale = Vector3.one * size;

            // ── The trail: the effect's Smoke_Trail, emitting on its own. ──
            var trailSrc = source.transform.Find("Smoke_Trail");
            if (trailSrc != null)
            {
                var trail = Object.Instantiate(trailSrc.gameObject, stone.transform, false);
                trail.name = "Trail";
                trail.transform.localPosition = Vector3.zero;
                trail.SetActive(true);
                foreach (var ps in trail.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var m = ps.main;
                    m.loop = true;
                    m.playOnAwake = true;
                    m.simulationSpace = ParticleSystemSimulationSpace.World;
                    var c = ps.collision;
                    c.enabled = false;
                }
            }

            // ── The burst: the effect's Impact systems, played once. ──
            impact = new GameObject("SiegeStoneImpactTemplate");
            impact.SetActive(false);
            Object.DontDestroyOnLoad(impact);
            for (int i = 0; i < source.transform.childCount; i++)
            {
                var child = source.transform.GetChild(i);
                if (!child.name.StartsWith("Impact")) continue;
                var burst = Object.Instantiate(child.gameObject, impact.transform, false);
                burst.transform.localPosition = Vector3.zero;
                burst.SetActive(true);
                foreach (var ps in burst.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var m = ps.main;
                    m.loop = false;
                    m.playOnAwake = true;
                    m.simulationSpace = ParticleSystemSimulationSpace.World;
                    var c = ps.collision;
                    c.enabled = false;
                }
            }
            if (impact.transform.childCount == 0)
            {
                Object.Destroy(impact);
                impact = null;
            }

            Object.Destroy(source);
        }

        /// <summary>A recycled stone leaves the engine: rock shown, trail restarted clean.</summary>
        public static void Launch(GameObject stone)
        {
            var rock = stone.transform.Find("Rock");
            if (rock != null) rock.gameObject.SetActive(true);
            foreach (var ps in stone.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        /// <summary>The stone has landed: the rock goes, the smoke already in the air stays to thin out.</summary>
        public static void Land(GameObject stone)
        {
            var rock = stone.transform.Find("Rock");
            if (rock != null) rock.gameObject.SetActive(false);
            foreach (var ps in stone.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>Tumble the rock (the trail does not turn with it).</summary>
        public static void Tumble(GameObject stone, float dt)
        {
            var rock = stone.transform.Find("Rock");
            if (rock != null) rock.Rotate(TumbleDegreesPerSecond * dt, TumbleDegreesPerSecond * 0.37f * dt, 0f, Space.Self);
        }

        /// <summary>Replay a recycled burst from its start.</summary>
        public static void Burst(GameObject impact)
        {
            foreach (var ps in impact.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }
    }
}
