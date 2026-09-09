// LedgerTestDirector.cs
// Runs the Ledger test loop: spawn, wander, die after a few seconds, let the
// wreckage clear, spawn another.
//
// It drives the prefab the same way the game does — instantiate it and let it
// wire ITSELF through LedgerRigBinder — so a break in the real spawn path shows
// up here rather than being papered over by scene-authored references. The one
// thing it stands in for is PresentationSpawnSystem's death handoff, which does
// not exist outside a match: the director calls LedgerDisassembly.BeginDeath()
// itself, exactly as CleanupDestroyedEntities would.
//
// Test-scene only. Nothing in a real match instantiates units this way.

using System.Collections;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public class LedgerTestDirector : MonoBehaviour
    {
        [Header("Cycle")]
        public GameObject ledgerPrefab;

        [Tooltip("Seconds of walking before the machine is killed.")]
        public float lifeSeconds = 5f;

        [Tooltip("Seconds to stand still before setting off, so the IDLE is " +
                 "visible as its own state rather than being skipped past.")]
        public float idleSeconds = 1.5f;

        [Tooltip("Gap after the wreckage has gone before the next one appears.")]
        public float gapSeconds = 0.6f;

        [Header("Wreckage")]
        [Tooltip("How long the parts lie there before dissolving. Short here so " +
                 "the loop stays watchable; the shipped value lives on " +
                 "LedgerDisassembly.")]
        public float lingerSeconds = 1.5f;

        [Header("Abilities")]
        [Tooltip("Optional. Re-pointed at each new Ledger so the run through " +
                 "every ability continues across deaths instead of restarting.")]
        public LedgerAbilityShowcase showcase;

        [Header("Wander")]
        [Tooltip("Kept tight so a FIXED camera can frame the whole test: the "
                 + "machine, the wreckage where it fell, and the next one.")]
        public float wanderRadius = 1.5f;

        private void Start()
        {
            if (ledgerPrefab == null)
            {
                Debug.LogError("[LedgerTest] no prefab assigned; nothing to run.");
                enabled = false;
                return;
            }
            StartCoroutine(Loop());
        }

        private IEnumerator Loop()
        {
            int round = 0;
            while (true)
            {
                round++;
                var go = Instantiate(ledgerPrefab, transform.position, Quaternion.identity);
                go.name = $"Ledger_{round}";

                // The prefab carries LedgerWalker + LedgerRigBinder; the binder
                // wires the legs in its own Awake, so by this line it can walk.
                var driver = go.GetComponent<LedgerWanderDriver>() ?? go.AddComponent<LedgerWanderDriver>();
                driver.interval = 1f;          // a new heading every second
                driver.speed = 1.2f;
                driver.wanderRadius = wanderRadius;
                driver.faceTravelDirection = false;
                driver.enabled = false;        // hold still first, so idle is visible

                var death = go.GetComponent<LedgerDisassembly>() ?? go.AddComponent<LedgerDisassembly>();
                death.lingerSeconds = lingerSeconds;

                // Point the showcase at the new body. It lives on the director,
                // so its progress through the ability list survives this unit.
                if (showcase != null) showcase.caster = go.transform;

                Debug.Log($"[LedgerTest] round {round}: idle {idleSeconds:0.0}s");
                yield return new WaitForSeconds(idleSeconds);

                Debug.Log($"[LedgerTest] round {round}: walking {lifeSeconds:0.0}s");
                driver.enabled = true;
                yield return new WaitForSeconds(lifeSeconds);

                Debug.Log($"[LedgerTest] round {round}: disassembly");
                death.BeginDeath();

                // The corpse owns its own destruction (fall, linger, dissolve),
                // so wait for the object to actually go rather than guessing a
                // duration that would drift out of step with the dissolver.
                while (go != null) yield return null;

                yield return new WaitForSeconds(gapSeconds);
            }
        }
    }
}
