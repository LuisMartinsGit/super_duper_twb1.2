// SandboxCastProbe.cs
// End-to-end check that a sandbox cast actually LANDS: spawn two colours of
// unit, cast an offensive sect power as one of them, and report the health of
// both sides before and after.
//
// The VFX playing proves nothing about the effect — the two are separate paths,
// and the whole point of the caster-colour work is that the strike pipeline
// tells friend from foe. This measures that rather than looking at it.
//
// Sect strikes WIND UP before they land, so the readout is taken on a timer
// rather than inline; a same-frame read would always show full health and
// look like a failure.

using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Abilities.Vfx;

namespace TheWaningBorder.EditorTools
{
    public static class SandboxCastProbe
    {
        private const string Foe = "Alanthor_Swordsman";
        private const int PerSide = 3;

        [MenuItem("Waning Border/Debug/Sandbox: Probe Cast Damage")]
        public static void Probe()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[CastProbe] launch the sandbox first (play mode + Launch Sandbox).");
                return;
            }
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { Debug.LogError("[CastProbe] no ECS world."); return; }
            var em = world.EntityManager;

            var cam = Camera.main;
            float3 centre = cam != null
                ? (float3)(cam.transform.position + cam.transform.forward * 25f)
                : float3.zero;
            centre.y = 0f;

            // Two clusters a few metres apart, so one strike can plausibly
            // cover both and the faction test is what separates them.
            var red = Spawn(em, Faction.Red, centre, PerSide);
            var blue = Spawn(em, Faction.Blue, centre + new float3(4f, 0f, 0f), PerSide);
            Debug.Log($"[CastProbe] spawned {red.Count} Red + {blue.Count} Blue {Foe} " +
                      $"around ({centre.x:0},{centre.z:0})");

            // The first OFFENSIVE power in the list — its Detail leads with the
            // kind, which is how an area-damage power identifies itself.
            var entries = AbilityShowcase.Entries;
            int pick = -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].SectId != null && entries[i].Detail.StartsWith("SmiteCircle"))
                { pick = i; break; }

            if (pick < 0)
            {
                Debug.LogError("[CastProbe] no SmiteCircle power in the list to test with.");
                return;
            }

            var e = entries[pick];
            Debug.Log($"[CastProbe] casting as BLUE: {e.Label}  [{e.Detail}]");
            Debug.Log($"[CastProbe] BEFORE  red={Health(em, red)}  blue={Health(em, blue)}");

            bool ok = AbilityShowcase.CastLive(em, Faction.Blue, e, (Vector3)centre, out string note);
            Debug.Log($"[CastProbe] CastLive -> {ok}  ({note})");

            // Sect strikes telegraph then land; read after the windup.
            double due = EditorApplication.timeSinceStartup + 4.0;
            void Tick()
            {
                if (EditorApplication.timeSinceStartup < due) return;
                EditorApplication.update -= Tick;

                if (!EditorApplication.isPlaying) { Debug.LogWarning("[CastProbe] left play mode."); return; }
                int r = Health(em, red), b = Health(em, blue);
                Debug.Log($"[CastProbe] AFTER   red={r}  blue={b}");

                if (r < 0 || b < 0) { Debug.LogWarning("[CastProbe] units were destroyed outright."); return; }
                Debug.Log(r < HealthMax(em, red)
                    ? "[CastProbe] PASS — the hostile side took damage."
                    : "[CastProbe] FAIL — Red is untouched; the strike did not land on enemies.");
                Debug.Log(b >= HealthMax(em, blue)
                    ? "[CastProbe] PASS — the caster's own side was spared."
                    : "[CastProbe] NOTE — Blue also lost health (friendly fire, or an area with no faction test).");
            }
            EditorApplication.update += Tick;
        }

        private static List<Entity> Spawn(EntityManager em, Faction f, float3 at, int n)
        {
            var list = new List<Entity>(n);
            for (int i = 0; i < n; i++)
            {
                float3 p = at + new float3((i % 2) * 1.2f, 0f, (i / 2) * 1.2f);
                var e = TheWaningBorder.Entities.UnitFactory.Create(em, Foe, p, f);
                if (e != Entity.Null) list.Add(e);
            }
            return list;
        }

        private static int Health(EntityManager em, List<Entity> units)
        {
            int total = 0;
            foreach (var e in units)
            {
                if (!em.Exists(e)) return -1;
                if (em.HasComponent<Health>(e)) total += em.GetComponentData<Health>(e).Value;
            }
            return total;
        }

        private static int HealthMax(EntityManager em, List<Entity> units)
            => units.Where(em.Exists)
                    .Where(em.HasComponent<Health>)
                    .Sum(e => em.GetComponentData<Health>(e).Max);
    }
}
