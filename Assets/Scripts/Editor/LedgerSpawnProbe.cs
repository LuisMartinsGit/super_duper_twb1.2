// LedgerSpawnProbe.cs
// Spawns a Ledger into a RUNNING match and reports everything the health-bar
// path depends on.
//
// FloatingHealthBars draws for the hovered or selected entity only, and both
// routes end in ScreenPick: a raycast that must hit a COLLIDER and then find
// an EntityReference on it or a parent. Whether that chain holds for a Ledger
// is not visible from the prefab — the spawn system adds the collider and the
// link at runtime — so this asks the live object instead of inferring.

using System.Linq;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class LedgerSpawnProbe
    {
        [MenuItem("Waning Border/Debug/Spawn Ledger + Probe Health Bar")]
        public static void SpawnAndProbe()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[LedgerProbe] enter play mode in a MATCH first " +
                                 "(Waning Border > Debug > Launch Quick Skirmish).");
                return;
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[LedgerProbe] no ECS world — this is not a match scene.");
                return;
            }
            var em = world.EntityManager;

            // In front of the camera, so it is on screen to hover.
            var cam = Camera.main;
            float3 pos = cam != null
                ? (float3)(cam.transform.position + cam.transform.forward * 18f)
                : float3.zero;
            pos.y = 0f;

            var faction = GameSettings.LocalPlayerFaction;
            var e = TheWaningBorder.Entities.Ledger.Create(em, pos, faction);
            Debug.Log($"[LedgerProbe] spawned Ledger entity {e.Index}:{e.Version} " +
                      $"for {faction} at ({pos.x:0.0},{pos.y:0.0},{pos.z:0.0})");

            string hpText = em.HasComponent<Health>(e)
                ? em.GetComponentData<Health>(e).Value + "/" + em.GetComponentData<Health>(e).Max
                : "MISSING";
            Debug.Log($"[LedgerProbe] entity: Health={hpText}" +
                      $" LocalTransform={em.HasComponent<LocalTransform>(e)}" +
                      $" UnitTag={em.HasComponent<UnitTag>(e)}" +
                      $" NotControllable={em.HasComponent<NotControllableTag>(e)}");

            // The view is created by PresentationSpawnSystem on a later frame,
            // so the inspection has to wait for it rather than run inline.
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => Probe(em, e);
        }

        private static void Probe(EntityManager em, Entity e)
        {
            if (!em.Exists(e)) { Debug.LogError("[LedgerProbe] entity died before the probe ran."); return; }

            GameObject view = null;
            var mgr = TheWaningBorder.Rendering.EntityViewManager.Instance;
            if (mgr != null && mgr.TryGetView(e, out var go)) view = go;

            if (view == null)
            {
                Debug.LogError("[LedgerProbe] NO VIEW GameObject for the entity — " +
                               "PresentationSpawnSystem never spawned one, so there is nothing " +
                               "to hover and no bar can appear.");
                return;
            }

            Debug.Log($"[LedgerProbe] view '{view.name}' active={view.activeInHierarchy} " +
                      $"layer={LayerMask.LayerToName(view.layer)}({view.layer}) " +
                      $"pos={view.transform.position}");

            var link = view.GetComponent<TheWaningBorder.Core.EntityReference>();
            Debug.Log($"[LedgerProbe] EntityReference on root: {(link != null ? "YES" : "NO")}");

            var cols = view.GetComponentsInChildren<Collider>(true);
            Debug.Log($"[LedgerProbe] colliders: {cols.Length}");
            foreach (var c in cols)
                Debug.Log($"[LedgerProbe]   {c.GetType().Name} on '{c.gameObject.name}' " +
                          $"enabled={c.enabled} bounds={c.bounds.center}±{c.bounds.extents}");

            var rends = view.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends.Take(3))
                Debug.Log($"[LedgerProbe]   renderer '{r.gameObject.name}' enabled={r.enabled} " +
                          $"bounds={r.bounds.center}±{r.bounds.extents}");

            var walker = view.GetComponent<TheWaningBorder.Rendering.LedgerWalker>();
            var binder = view.GetComponent<TheWaningBorder.Rendering.LedgerRigBinder>();
            var death = view.GetComponent<TheWaningBorder.Rendering.LedgerDisassembly>();
            Debug.Log($"[LedgerProbe] walker={(walker ? (walker.enabled ? "enabled" : "DISABLED") : "MISSING")} " +
                      $"binder={(binder ? (binder.enabled ? "enabled" : "DISABLED") : "MISSING")} " +
                      $"disassembly={(death ? "present" : "MISSING")}");

            // The exact test FloatingHealthBars runs.
            bool drawable = em.HasComponent<Health>(e) && em.HasComponent<LocalTransform>(e);
            var hp = em.HasComponent<Health>(e) ? em.GetComponentData<Health>(e) : default;
            Debug.Log($"[LedgerProbe] HasDrawableBar={drawable} Health.Max={hp.Max} " +
                      $"(a bar needs Max > 0, and hover/selection to trigger it)");

            if (cols.Length == 0)
                Debug.LogError("[LedgerProbe] ROOT CAUSE: no collider, so ScreenPick can never " +
                               "hit this unit — it cannot be hovered OR box-selected, and " +
                               "FloatingHealthBars only draws for hovered/selected entities.");
        }
    }
}
